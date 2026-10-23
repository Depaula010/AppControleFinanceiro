using MeuSecretario.Domain.Whatsapp;
using MeuSecretario.Infrastructure.Whatsapp;
using MeuSecretario.IntegrationTests.Automation;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.IntegrationTests.Transactions;

namespace MeuSecretario.IntegrationTests.Whatsapp;

/// <summary>SQL real das consultas financeiras do WhatsApp (spec 012 §6.c).</summary>
[Trait("Category", "Docker")]
public class QueryReaderTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    private AutomationEnv _env = default!;
    private WhatsAppQueryReader _reader = default!;
    private int _user;
    private int _other;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _env = new AutomationEnv(postgres);
        _reader = new WhatsAppQueryReader(postgres.Connections);
        _user = await _env.SeedUserAsync();
        _other = await _env.SeedUserAsync("5511999990001", "outra-chave");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("AC", "012-301")]
    public async Task Balances_AreConsolidatedActiveOnly_OrderedByTypeThenName()
    {
        var checking = await _env.Data.InsertAccountAsync(_user, "Zeta");
        var wallet = await _env.Data.InsertAccountAsync(_user, "Carteira", "Dinheiro");
        var inactive = await _env.Data.InsertAccountAsync(_user, "Velha", ativa: false);
        await _env.Data.InsertTransactionAsync(_user, checking, valor: -40m);
        await _env.Data.InsertTransactionAsync(_user, checking, valor: 100m, tipo: "Renda");
        await _env.Data.InsertTransactionAsync(_user, checking, valor: -999m, consolidada: false);
        await _env.Data.InsertTransactionAsync(_user, inactive, valor: -1m);
        await _env.Data.ExecuteAsync("UPDATE contas SET saldo_inicial = 10 WHERE id = @checking", new { checking });

        var all = await _reader.GetBalancesAsync(_user, null, CancellationToken.None);
        all.Select(a => (a.Name, a.Type, a.Balance)).ShouldBe([("Zeta", "Conta Corrente", 70d), ("Carteira", "Dinheiro", 0d)]);

        (await _reader.GetBalancesAsync(_user, wallet, CancellationToken.None)).Count.ShouldBe(1);
        (await _reader.GetBalancesAsync(_user, inactive, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "012-305")]
    public async Task UpdateInitialBalance_ReportsWhetherARowChanged()
    {
        var account = await _env.Data.InsertAccountAsync(_user, "Nubank");
        var foreign = await _env.Data.InsertAccountAsync(_other, "Alheia");

        (await _reader.UpdateInitialBalanceAsync(_user, account, 1500.5m, CancellationToken.None)).ShouldBeTrue();
        (await _reader.UpdateInitialBalanceAsync(_user, foreign, 1m, CancellationToken.None)).ShouldBeFalse();
        (await _env.Data.ScalarAsync<decimal>("SELECT saldo_inicial FROM contas WHERE id = @account", new { account })).ShouldBe(1500.5m);
    }

    [Fact]
    [Trait("AC", "012-303")]
    public async Task Pots_UseEachPotsOwnPeriod_AndKeepTheRawNegativeSum()
    {
        var account = await _env.Data.InsertAccountAsync(_user, "Nubank");
        await _env.Data.InsertTransactionAsync(_user, account, valor: -30m, data: "2026-10-06");
        await _env.Data.InsertTransactionAsync(_user, account, valor: -50m, data: "2026-10-05");
        await _env.Data.InsertTransactionAsync(_user, account, valor: -70m, data: "2026-09-30");
        await _env.InsertPotAsync(_user, "Mensal", 500m, "MENSAL", TransactionsTestData.ExpenseSubcategory);
        await _env.InsertPotAsync(_user, "Semanal", 100m, "SEMANAL", TransactionsTestData.ExpenseSubcategory);
        await _env.InsertPotAsync(_user, "Vazio", 10m, "MENSAL", TransactionsTestData.IncomeSubcategory);
        await _env.InsertPotAsync(_other, "Alheio", 10m, "MENSAL", TransactionsTestData.ExpenseSubcategory);

        var pots = await _reader.GetPotsAsync(_user, Today, CancellationToken.None);

        // 7/10/2026 é quarta: a semana vai de 5/10 a 11/10 (desvio 015).
        pots.Select(p => (p.Name, p.Limit, p.RawSpent)).ShouldBe([("Mensal", 500d, -80d), ("Semanal", 100d, -80d), ("Vazio", 10d, 0d)]);
    }

    [Fact]
    [Trait("AC", "012-306")]
    public async Task Invoices_SummaryAndDetail_OfOpenInvoices()
    {
        var card = await _env.Data.InsertCardAsync(_user, "Inter", 10, 17);
        var open = await _env.Data.InsertInvoiceAsync(card, "2026-10-17", "2026-10-10");
        var later = await _env.Data.InsertInvoiceAsync(card, "2026-11-17", "2026-11-10");
        await _env.Data.InsertInvoiceAsync(card, "2026-09-17", "2026-09-10", "Paga");
        var schedule = await _env.InsertScheduleAsync(_user, card, "Tênis", type: "PARCELADO", total: 3, executed: 2);
        await _env.Data.InsertTransactionAsync(_user, card, valor: -100m, faturaId: open, data: "2026-10-05", descricao: "Mercado");
        await _env.Data.InsertTransactionAsync(_user, card, valor: -50m, faturaId: open, data: "2026-10-05", descricao: "Cinema");
        await _env.Data.InsertTransactionAsync(_user, card, valor: 20m, faturaId: open, data: "2026-10-06", descricao: "Estorno");
        await _env.Data.ExecuteAsync("UPDATE transacoes SET agendamento_id = @schedule WHERE descricao = 'Cinema'", new { schedule });
        _ = later;

        var summary = await _reader.GetOpenInvoicesAsync(_user, null, CancellationToken.None);
        summary.Select(i => (i.CardName, i.Value, i.DueDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), i.Status)).ShouldBe(
            [("Inter", 150d, "2026-10-17", "Aberta"), ("Inter", 0d, "2026-11-17", "Aberta")]);
        (await _reader.GetOpenInvoicesAsync(_user, card, CancellationToken.None)).Count.ShouldBe(1);

        var detail = await _reader.GetInvoiceDetailAsync(_user, card, CancellationToken.None);
        detail!.Total.ShouldBe(150d);
        detail.Lines.Select(l => (l.Description, l.Value, l.ScheduleType, l.InstallmentInfo)).ShouldBe(
            [("Cinema", 50d, "PARCELADO", "2/3"), ("Mercado", 100d, null, null)]);
        (await _reader.GetInvoiceDetailAsync(_user, await _env.Data.InsertAccountAsync(_user, "Outra"), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "012-307")]
    public async Task CategorySpending_AndPeriodQueries()
    {
        var account = await _env.Data.InsertAccountAsync(_user, "Nubank");
        await _env.Data.InsertTransactionAsync(_user, account, valor: -30m, data: "2026-10-06", descricao: "A");
        await _env.Data.InsertTransactionAsync(_user, account, valor: -20m, data: "2026-10-07", descricao: "B");
        await _env.Data.InsertTransactionAsync(_user, account, valor: -99m, data: "2026-09-30", descricao: "Antiga");
        await _env.Data.InsertTransactionAsync(_user, account, valor: 500m, tipo: "Renda", data: "2026-10-06", descricao: "Salário", subcategoryId: TransactionsTestData.IncomeSubcategory);

        var name = await _env.Data.ScalarAsync<string>("SELECT nome_sub FROM subcategoria WHERE id = @id", new { id = TransactionsTestData.ExpenseSubcategory });

        (await _reader.GetCategorySpendingAsync(_user, name, Today, CancellationToken.None)).ShouldBe(-50d);
        (await _reader.GetCategorySpendingAsync(_user, "inexistente-xyz", Today, CancellationToken.None)).ShouldBe(0d);

        var expenses = await _reader.ListExpensesAsync(_user, new DateOnly(2026, 10, 1), Today, CancellationToken.None);
        expenses.Select(e => e.Description).ShouldBe(["B", "A"]);

        var byCategory = await _reader.ListCategoryExpensesAsync(_user, name, new DateOnly(2026, 9, 1), Today, CancellationToken.None);
        byCategory.Select(e => e.Description).ShouldBe(["B", "A", "Antiga"]);
    }
}
