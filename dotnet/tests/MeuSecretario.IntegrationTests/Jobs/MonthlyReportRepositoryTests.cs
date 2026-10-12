using MeuSecretario.Domain.Jobs;
using MeuSecretario.Infrastructure.Jobs;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Jobs;

[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public sealed class MonthlyReportRepositoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly JobsEnv _env = new(postgres);
    private MonthlyReportRepository _repository = default!;
    private int _user;
    private int _account;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _repository = new MonthlyReportRepository(postgres.Connections);
        _user = await _env.UserAsync();
        _account = await _env.AccountAsync(_user);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("AC", "013-70")]
    public async Task Recipients_AreActiveWithTheMomentAndTheHourInsideTheWindow()
    {
        await _env.MonthlyConfigAsync(_user, "INICIO_MES", "08:03");
        var wrongMoment = await _env.UserAsync("5511900000002", "Bia");
        await _env.MonthlyConfigAsync(wrongMoment, "FIM_MES", "08:00");
        var outside = await _env.UserAsync("5511900000003", "Caio");
        await _env.MonthlyConfigAsync(outside, "INICIO_MES", "08:30");
        var off = await _env.UserAsync("5511900000004", "Duda");
        await _env.MonthlyConfigAsync(off, "INICIO_MES", "08:00", active: false);

        var recipients = await _repository.ListRecipientsAsync(MonthlyMoment.InicioMes, new TimeOnly(7, 55, 30), new TimeOnly(8, 5, 30), CancellationToken.None);

        recipients.Select(r => (r.UserId, r.Name, r.SendTime)).ShouldBe([(_user, "Ana", new TimeOnly(8, 3))]);
        (await _repository.ListRecipientsAsync(MonthlyMoment.FimMes, new TimeOnly(7, 55), new TimeOnly(8, 5), CancellationToken.None))
            .Select(r => r.UserId).ShouldBe([wrongMoment]);
    }

    [Fact]
    [Trait("AC", "013-74")]
    public async Task FindUser_ReturnsNameAndNumber_OrNull()
    {
        (await _repository.FindUserAsync(_user, CancellationToken.None))!.WhatsappNumber.ShouldBe("5511900000001");
        (await _repository.FindUserAsync(999, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "013-71")]
    public async Task Data_ForTheStartMoment_UsesTheLastMonth_WithExpensesAsAbsoluteValues_Deviation132()
    {
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-02-05", "Mercado", -100m);
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-02-10", "Transporte", -50m);
        await _env.TransactionAsync(_user, _account, "Renda", "2026-02-15", "Salário", 1000m);
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-01-20", "Janeiro", -200m);
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-03-02", "Março", -999m);
        await _env.ExecuteAsync("UPDATE transacoes SET consolidada = FALSE WHERE descricao = 'Transporte'");
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Aluguel", 700m, day: 10);
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Curso", 100m, "PARCELADO", total: 3, executed: 1);
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Quitado", 100m, "PARCELADO", total: 3, executed: 3);

        var data = await _repository.GetDataAsync(_user, MonthlyMoment.InicioMes, JobsEnv.Day("2026-03-01"), CancellationToken.None);

        (data.Month, data.Year, data.PeriodStart, data.PeriodEnd).ShouldBe((2, 2026, JobsEnv.Day("2026-02-01"), JobsEnv.Day("2026-02-28")));
        // Desvio 132: a despesa é gravada negativa, mas o relatório a soma em valor absoluto; o saldo é rendas - despesas.
        data.Totals.ShouldBe(new ReportTotals(100, 1000, 900, 2));
        data.TopCategories.Select(c => (c.Category, c.Value, c.Percent)).ShouldBe([("Sub Despesa Essencial", 100d, 100d)]);
        data.Comparison.ShouldBe(new ReportComparison(100, 200, -100, -50));
        data.Accounts.ShouldBe(new ReportAccounts(1, 100, 2, 800));
        data.Pots.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "013-71")]
    public async Task Data_TopCategories_AreOrderedFromTheBiggestSpend_Deviation132()
    {
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-03-05", "Pequena", -10m);
        await _env.TransactionAsync(_user, _account, "Despesa Discricionária", "2026-03-06", "Grande", -300m);
        await _env.TransactionAsync(_user, _account, "Despesa Discricionária", "2026-03-07", "Grande 2", -100m);
        await _env.TransactionAsync(_user, _account, "Renda", "2026-03-08", "Salário", 5000m);

        var data = await _repository.GetDataAsync(_user, MonthlyMoment.FimMes, JobsEnv.Day("2026-03-31"), CancellationToken.None);

        data.TopCategories.Select(c => (c.Category, c.Value)).ShouldBe([("Sub Despesa Discricionária", 400d), ("Sub Despesa Essencial", 10d)]);
        data.TopCategories[0].Percent.ShouldBe(400d / 410d * 100, 0.0001);
        data.Totals.ShouldBe(new ReportTotals(410, 5000, 4590, 4));
    }

    [Fact]
    [Trait("AC", "013-71")]
    public async Task Data_VariationPercent_IsComputedWhenThePreviousMonthHadExpenses_Deviation132()
    {
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-02-10", "Fevereiro", -200m);
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-03-10", "Março", -300m);

        var data = await _repository.GetDataAsync(_user, MonthlyMoment.FimMes, JobsEnv.Day("2026-03-31"), CancellationToken.None);

        data.Comparison.ShouldBe(new ReportComparison(300, 200, 100, 50));
    }

    [Fact]
    [Trait("AC", "013-71")]
    public async Task Data_Pots_UseTheSubcategoryTransactionsOfThePeriod()
    {
        var sub = await _env.SubcategoryAsync("Despesa Essencial");
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-03-10", "Mercado", -120m);
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-02-10", "Fora", -999m);
        var pot = await _env.ScalarAsync<int>(
            "INSERT INTO potesdegastos (usuario_id, nome_pote, valor_limite) VALUES (@u, 'Comida', 500) RETURNING id", new { u = _user });
        await _env.ExecuteAsync("INSERT INTO potesubcategorias (pote_id, subcategoria_id) VALUES (@pot, @sub)", new { pot, sub });
        await _env.ExecuteAsync("INSERT INTO potesdegastos (usuario_id, nome_pote, valor_limite, ativo) VALUES (@u, 'Inativo', 100, false)", new { u = _user });

        var data = await _repository.GetDataAsync(_user, MonthlyMoment.FimMes, JobsEnv.Day("2026-03-31"), CancellationToken.None);

        data.Pots.Select(p => (p.Name, p.Limit, p.Used, p.Balance, p.Percent)).ShouldBe([("Comida", 500d, 120d, 380d, 24d)]);
        data.Totals.TotalTransactions.ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "013-71")]
    public async Task Data_ForAUserWithoutTransactions_IsAllZero()
    {
        var data = await _repository.GetDataAsync(_user, MonthlyMoment.FimMes, JobsEnv.Day("2026-03-31"), CancellationToken.None);

        data.Totals.ShouldBe(new ReportTotals(0, 0, 0, 0));
        data.TopCategories.ShouldBeEmpty();
        data.Comparison.ShouldBe(new ReportComparison(0, 0, 0, 0));
        data.Accounts.ShouldBe(new ReportAccounts(0, 0, 0, 0));
    }
}
