using MeuSecretario.Domain.Whatsapp;
using MeuSecretario.Infrastructure.Whatsapp;
using MeuSecretario.IntegrationTests.Automation;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.IntegrationTests.Transactions;

namespace MeuSecretario.IntegrationTests.Whatsapp;

/// <summary>SQL real das contas fixas e dos vencimentos do WhatsApp (spec 012 §6.d).</summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class FixedBillsReaderTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateOnly October1 = new(2026, 10, 1);
    private static readonly DateOnly October31 = new(2026, 10, 31);

    private AutomationEnv _env = default!;
    private FixedBillsReader _reader = default!;
    private int _user;
    private int _other;
    private int _account;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _env = new AutomationEnv(postgres);
        _reader = new FixedBillsReader(postgres.Connections);
        _user = await _env.SeedUserAsync();
        _other = await _env.SeedUserAsync("5511999990001", "outra-chave");
        _account = await _env.Data.InsertAccountAsync(_user, "Nubank");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<int> IncomeSubcategoryAsync() => _env.Data.ScalarAsync<int>(
        """
        SELECT s.id FROM subcategoria s
        JOIN macrocategoria m ON s.macro_id = m.id
        JOIN grupocategoria g ON m.grupo_id = g.id
        WHERE g.nome_grupo = 'Renda'
        ORDER BY s.id LIMIT 1
        """);

    [Fact]
    [Trait("AC", "012-401")]
    public async Task Pending_ListsActiveFixedAndVariable_NotPaidByDescriptionInTheMonth_OrderedByDay()
    {
        var card = await _env.Data.InsertCardAsync(_user, "Inter", 10, 17);
        await _env.InsertScheduleAsync(_user, _account, "Luz", day: 20);
        await _env.InsertScheduleAsync(_user, _account, "Aluguel", day: 5, amount: 1500m);
        await _env.InsertScheduleAsync(_user, card, "Netflix", day: 12, amount: 55.9m);
        await _env.InsertScheduleAsync(_user, _account, "Salário", day: 5, amount: 5000m, subcategoryId: await IncomeSubcategoryAsync());
        await _env.InsertScheduleAsync(_user, _account, "Água", type: "LEMBRETE_VARIAVEL", day: 8, amount: null);
        await _env.InsertScheduleAsync(_user, _account, "Já pago", day: 3);
        await _env.InsertScheduleAsync(_user, _account, "Do mês passado", day: 4);
        await _env.InsertScheduleAsync(_user, _account, "Parcelado", type: "PARCELADO", total: 3, day: 6);
        await _env.InsertScheduleAsync(_user, _account, "Inativo", day: 7, active: false);
        await _env.InsertScheduleAsync(_other, await _env.Data.InsertAccountAsync(_other, "Dela"), "Alheio", day: 9);
        await _env.Data.InsertTransactionAsync(_user, _account, valor: -10m, data: "2026-10-03", descricao: "Já pago");
        await _env.Data.InsertTransactionAsync(_user, _account, valor: -10m, data: "2026-09-04", descricao: "Do mês passado");
        await _env.Data.InsertTransactionAsync(_other, await _env.Data.InsertAccountAsync(_other, "Outra"), valor: -10m, data: "2026-10-02", descricao: "Luz");

        var bills = await _reader.ListPendingAsync(_user, October1, October31, 10, CancellationToken.None);

        bills.Select(b => (b.Description, b.Day)).ShouldBe(
            [("Do mês passado", 4), ("Aluguel", 5), ("Salário", 5), ("Água", 8), ("Netflix", 12), ("Luz", 20)]);
        bills.Single(b => b.Description == "Netflix").AccountType.ShouldBe("Cartão de Crédito");
        bills.Single(b => b.Description == "Salário").GroupName.ShouldBe("Renda");
        bills.Single(b => b.Description == "Água").Amount.ShouldBe(0d);
    }

    [Fact]
    [Trait("AC", "012-401")]
    public async Task Pending_OnlyDescriptionsOfTheMonthCount_AndAnnualsOnlyInTheirMonth()
    {
        await _env.InsertScheduleAsync(_user, _account, "Do mês passado", day: 4);
        await _env.InsertScheduleAsync(_user, _account, "IPTU", periodicity: "ANUAL", month: 3, day: 10);
        await _env.InsertScheduleAsync(_user, _account, "Seguro", periodicity: "ANUAL", month: 10, day: 11);
        await _env.Data.InsertTransactionAsync(_user, _account, valor: -10m, data: "2026-09-04", descricao: "Do mês passado");
        await _env.Data.InsertTransactionAsync(_user, _account, valor: -10m, data: "2026-11-01", descricao: "Seguro");

        var bills = await _reader.ListPendingAsync(_user, October1, October31, 10, CancellationToken.None);

        bills.Select(b => b.Description).ShouldBe(["Do mês passado", "Seguro"]);
    }

    [Fact]
    [Trait("AC", "012-401")]
    public async Task Catalog_IncludesInstallments_OrderedByPeriodicityThenDay()
    {
        await _env.InsertScheduleAsync(_user, _account, "Mensal 10", day: 10);
        await _env.InsertScheduleAsync(_user, _account, "Anual", periodicity: "ANUAL", month: 3, day: 1);
        await _env.InsertScheduleAsync(_user, _account, "Semanal", periodicity: "SEMANAL", day: 2);
        await _env.InsertScheduleAsync(_user, _account, "Mensal 5", day: 5);
        await _env.InsertScheduleAsync(_user, _account, "Quinzenal", periodicity: "QUINZENAL", day: 15);
        await _env.InsertScheduleAsync(_user, _account, "Parcelado", type: "PARCELADO", total: 3, day: 1);
        await _env.InsertScheduleAsync(_user, _account, "Inativo", day: 1, active: false);
        await _env.InsertScheduleAsync(_other, await _env.Data.InsertAccountAsync(_other, "Dela"), "Alheio");

        var catalog = await _reader.ListCatalogAsync(_user, CancellationToken.None);

        catalog.Select(b => b.Description).ShouldBe(["Semanal", "Quinzenal", "Parcelado", "Mensal 5", "Mensal 10", "Anual"]);
        catalog.Single(b => b.Description == "Anual").Month.ShouldBe(3);
    }

    [Fact]
    [Trait("AC", "012-402")]
    public async Task DueBills_Slices_FilterByDayAndAnnualMonth()
    {
        await _env.InsertScheduleAsync(_user, _account, "Dia 3", day: 3);
        await _env.InsertScheduleAsync(_user, _account, "Dia 7", day: 7);
        await _env.InsertScheduleAsync(_user, _account, "Dia 30", day: 30);
        await _env.InsertScheduleAsync(_user, _account, "Anual outubro", periodicity: "ANUAL", month: 10, day: 7);
        await _env.InsertScheduleAsync(_user, _account, "Anual março", periodicity: "ANUAL", month: 3, day: 7);
        await _env.InsertScheduleAsync(_user, _account, "Parcelado", type: "PARCELADO", total: 3, day: 7);

        var names = async (DueSlice slice) => (await _reader.ListDueBillsAsync(_user, slice, CancellationToken.None)).Select(b => b.Description).ToArray();

        (await names(new DueSlice(7, 7, 10, 2026))).ShouldBe(["Dia 7", "Anual outubro"]);
        (await names(new DueSlice(7, 30, 10, 2026))).ShouldBe(["Dia 7", "Anual outubro", "Dia 30"]);
        (await names(new DueSlice(28, null, 10, 2026))).ShouldBe(["Dia 30"]);
        (await names(new DueSlice(null, 4, 11, 2026))).ShouldBe(["Dia 3"]);
    }

    [Fact]
    [Trait("AC", "012-402")]
    public async Task DueBills_PaidInTheReferenceMonth_ByDescription_OnlyByTheSameUser_Deviation161()
    {
        await _env.InsertScheduleAsync(_user, _account, "Pago por mim", day: 7);
        await _env.InsertScheduleAsync(_user, _account, "Pago por outro", day: 7);
        await _env.InsertScheduleAsync(_user, _account, "Pago em setembro", day: 7);
        await _env.Data.InsertTransactionAsync(_user, _account, valor: -10m, data: "2026-10-02", descricao: "Pago por mim");
        await _env.Data.InsertTransactionAsync(_other, await _env.Data.InsertAccountAsync(_other, "Dela"), valor: -10m, data: "2026-10-02", descricao: "Pago por outro");
        await _env.Data.InsertTransactionAsync(_user, _account, valor: -10m, data: "2026-09-02", descricao: "Pago em setembro");

        var bills = await _reader.ListDueBillsAsync(_user, new DueSlice(7, 7, 10, 2026), CancellationToken.None);

        // Desvio 161: a checagem de "já pago" filtra o usuário; a transação de outro usuário com a mesma descrição não esconde a conta
        // (o Flask a escondia e devolvia só "Pago em setembro").
        bills.Select(b => b.Description).ShouldBe(["Pago por outro", "Pago em setembro"]);
    }

    [Fact]
    [Trait("AC", "012-402")]
    public async Task DueInvoices_AreOpenOnesOfTheUser_WithTheSumOfTheExpenses()
    {
        var card = await _env.Data.InsertCardAsync(_user, "Inter", 10, 17);
        var other = await _env.Data.InsertCardAsync(_other, "Dela", 10, 17);
        var open = await _env.Data.InsertInvoiceAsync(card, "2026-10-10", "2026-10-03");
        await _env.Data.InsertInvoiceAsync(card, "2026-11-10", "2026-11-03");
        await _env.Data.InsertInvoiceAsync(card, "2026-10-11", "2026-10-04", "Paga");
        await _env.Data.InsertInvoiceAsync(other, "2026-10-10", "2026-10-03");
        await _env.Data.InsertTransactionAsync(_user, card, valor: -100m, faturaId: open, descricao: "Mercado");
        await _env.Data.InsertTransactionAsync(_user, card, valor: -50.5m, faturaId: open, descricao: "Cinema");
        await _env.Data.InsertTransactionAsync(_user, card, valor: 20m, faturaId: open, descricao: "Estorno");

        var invoices = await _reader.ListDueInvoicesAsync(_user, new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 14), CancellationToken.None);

        invoices.ShouldBe([new DueInvoice("Inter", new DateOnly(2026, 10, 10), 150.5m)]);
    }
}
