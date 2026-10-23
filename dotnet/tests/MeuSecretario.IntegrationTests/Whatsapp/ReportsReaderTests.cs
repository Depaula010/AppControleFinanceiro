using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Infrastructure.Whatsapp;
using MeuSecretario.IntegrationTests.Jobs;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Whatsapp;

/// <summary>Leitura dos relatórios sob comando (desvio 256) contra o PostgreSQL real.</summary>
[Trait("Category", "Docker")]
public sealed class ReportsReaderTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private static readonly DateOnly Today = new(2026, 10, 22);

    private readonly JobsEnv _env = new(postgres);
    private ReportsReader _reader = default!;
    private int _user;
    private int _card;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _reader = new ReportsReader(postgres.Connections);
        _user = await _env.UserAsync();
        _card = await _env.CardAsync(_user, "Cartão Inter", 13, 20);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<int> SubAsync(string name) => _env.ScalarAsync<int>("SELECT id FROM subcategoria WHERE nome_sub = @name ORDER BY id LIMIT 1", new { name });

    private Task<int> OthersOfLeisureAsync() => _env.ScalarAsync<int>(
        "SELECT s.id FROM subcategoria s JOIN macrocategoria m ON m.id = s.macro_id WHERE s.nome_sub = 'Outros' AND m.nome_macro LIKE 'Lazer%'");

    private async Task AddAsync(int account, int sub, string date, string description, decimal value, int? invoice = null, string type = "Despesa") =>
        await _env.ExecuteAsync(
            """
            INSERT INTO transacoes (usuario_id, conta_id, subcategoria_id, fatura_id, descricao, valor, data_transacao, tipo_transacao)
            VALUES (@user, @account, @sub, @invoice, @description, @value, @date, @type)
            """,
            new { user = _user, account, sub, invoice, description, value, date = JobsEnv.D(date), type });

    [Fact]
    [Trait("AC", "012-505")]
    public async Task Invoice_TotalsCategoriesTopPurchasesAndFutureInstallments()
    {
        var invoice = await _env.InvoiceAsync(_card, "2026-09-20", "2026-09-13", "Paga");
        var market = await SubAsync("Supermercado / Mercearia");
        var food = await SubAsync("Restaurantes, Bares e Delivery");
        var others = await OthersOfLeisureAsync();
        await AddAsync(_card, market, "2026-09-01", "ABC ATACADO", -100m, invoice);
        await AddAsync(_card, market, "2026-09-02", "BH", -50m, invoice);
        await AddAsync(_card, food, "2026-09-03", "BURGER", -200m, invoice);
        await AddAsync(_card, others, "2026-09-04", "ZP ROGGI (2/6)", -511m, invoice);
        await AddAsync(_card, others, "2026-09-05", "PASSEIO", -40m, invoice);
        await AddAsync(_card, market, "2026-09-06", "ESTORNO", 30m, invoice);

        var report = await _reader.GetInvoiceAsync(_user, _card, new InvoiceSelection(InvoiceSelectionKind.DueMonth, new DateOnly(2026, 9, 1)), Today, CancellationToken.None);

        report.ShouldNotBeNull();
        (report.CardName, report.Status, report.Purchases, report.Refunds, report.Count).ShouldBe(("Cartão Inter", "Paga", 901m, 30m, 6));
        report.DueDate.ShouldBe(new DateOnly(2026, 9, 20));
        report.ClosingDate.ShouldBe(new DateOnly(2026, 9, 13));
        report.Categories.Select(c => (c.Name, c.Amount, c.Count)).ShouldBe(
            [("Outros (Lazer e Entretenimento)", 551m, 2), ("Restaurantes, Bares e Delivery", 200m, 1), ("Supermercado / Mercearia", 150m, 2)]);
        report.TopPurchases.Select(l => (l.Description, l.Amount)).ShouldBe([("ZP ROGGI (2/6)", 511m), ("BURGER", 200m), ("ABC ATACADO", 100m), ("BH", 50m), ("PASSEIO", 40m)]);
        (report.InstallmentPurchases, report.InstallmentsRemaining).ShouldBe((1, 2044m));
    }

    [Fact]
    [Trait("AC", "012-505")]
    public async Task InvoiceSelection_OpenPreviousAndByMonth_OnlyOfTheUsersActiveCard()
    {
        await _env.InvoiceAsync(_card, "2026-09-20", "2026-09-13", "Paga");
        await _env.InvoiceAsync(_card, "2026-10-20", "2026-10-13", "Fechada");
        await _env.InvoiceAsync(_card, "2026-11-20", "2026-11-13", "Aberta");

        async Task<DateOnly?> DueAsync(InvoiceSelection selection) =>
            (await _reader.GetInvoiceAsync(_user, _card, selection, Today, CancellationToken.None))?.DueDate;

        (await DueAsync(new InvoiceSelection(InvoiceSelectionKind.Open))).ShouldBe(new DateOnly(2026, 11, 20));
        (await DueAsync(new InvoiceSelection(InvoiceSelectionKind.Previous))).ShouldBe(new DateOnly(2026, 10, 20));
        (await DueAsync(new InvoiceSelection(InvoiceSelectionKind.DueMonth, new DateOnly(2026, 9, 1)))).ShouldBe(new DateOnly(2026, 9, 20));
        (await DueAsync(new InvoiceSelection(InvoiceSelectionKind.DueMonth, new DateOnly(2026, 7, 1)))).ShouldBeNull();

        var stranger = await _env.UserAsync("5511900000002", "Outra");
        var strangerCard = await _env.CardAsync(stranger, "Cartão Alheio", 10, 15);
        await _env.InvoiceAsync(strangerCard, "2026-11-15", "2026-11-10", "Aberta");
        (await _reader.GetInvoiceAsync(_user, strangerCard, new InvoiceSelection(InvoiceSelectionKind.Open), Today, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "012-505")]
    public async Task InvoiceSelection_WithoutAnOpenInvoice_UsesTheMostRecent()
    {
        await _env.InvoiceAsync(_card, "2026-08-20", "2026-08-13", "Paga");
        await _env.InvoiceAsync(_card, "2026-09-20", "2026-09-13", "Paga");

        var report = await _reader.GetInvoiceAsync(_user, _card, new InvoiceSelection(InvoiceSelectionKind.Open), Today, CancellationToken.None);

        report!.DueDate.ShouldBe(new DateOnly(2026, 9, 20));
    }

    [Fact]
    [Trait("AC", "012-505")]
    public async Task Cards_OnlyActiveCreditCards()
    {
        await _env.AccountAsync(_user, "Mercado Pago");
        var inactive = await _env.CardAsync(_user, "Cartão Velho", 5, 10);
        await _env.ExecuteAsync("UPDATE contas SET ativa = FALSE WHERE id = @inactive", new { inactive });

        var cards = await _reader.ListCardsAsync(_user, CancellationToken.None);

        cards.Select(c => c.Name).ShouldBe(["Cartão Inter"]);
    }

    [Fact]
    [Trait("AC", "012-505")]
    public async Task MonthByCategory_CurrentMonthUntilToday_AgainstTheSameStretchOfTheLast()
    {
        var checking = await _env.AccountAsync(_user, "Mercado Pago");
        var market = await SubAsync("Supermercado / Mercearia");
        var food = await SubAsync("Restaurantes, Bares e Delivery");
        var salary = await SubAsync("Salário Fixo / Pró-labore");

        // outubro, até o dia 22
        await AddAsync(_card, market, "2026-10-02", "MERCADO", -300m);
        await AddAsync(checking, food, "2026-10-10", "JANTAR", -100m);
        await AddAsync(checking, food, "2026-10-25", "FUTURO (assinatura adiantada)", -999m);
        await AddAsync(checking, salary, "2026-10-05", "SALARIO", 5000m, type: "Renda");
        await AddAsync(checking, market, "2026-10-06", "TRANSFERENCIA", -777m, type: "Transferência");

        // setembro: o trecho 1 a 22 conta; o dia 25 não
        await AddAsync(_card, market, "2026-09-03", "MERCADO", -200m);
        await AddAsync(checking, food, "2026-09-12", "JANTAR", -100m);
        await AddAsync(checking, food, "2026-09-25", "FORA DO TRECHO", -500m);

        var report = await _reader.GetMonthByCategoryAsync(_user, new DateOnly(2026, 10, 1), Today, CancellationToken.None);

        (report.Partial, report.UntilDate, report.Total, report.PreviousTotal).ShouldBe((true, Today, 400m, 300m));
        report.Categories.Select(c => (c.Name, c.Amount, c.Previous, c.Count)).ShouldBe(
            [("Supermercado / Mercearia", 300m, 200m, 1), ("Restaurantes, Bares e Delivery", 100m, 100m, 1)]);
    }

    [Fact]
    [Trait("AC", "012-505")]
    public async Task MonthByCategory_ClosedMonth_IsWholeAndComparesWithTheWholeLastMonth()
    {
        var market = await SubAsync("Supermercado / Mercearia");
        await AddAsync(_card, market, "2026-09-30", "MERCADO", -120m);
        await AddAsync(_card, market, "2026-08-31", "MERCADO", -100m);

        var report = await _reader.GetMonthByCategoryAsync(_user, new DateOnly(2026, 9, 1), Today, CancellationToken.None);

        (report.Partial, report.UntilDate, report.Total, report.PreviousTotal).ShouldBe((false, new DateOnly(2026, 9, 30), 120m, 100m));
    }

    [Fact]
    [Trait("AC", "012-505")]
    public async Task MonthByCategory_NeverReadsAnotherUsersTransactions()
    {
        var stranger = await _env.UserAsync("5511900000002", "Outra");
        var strangerAccount = await _env.AccountAsync(stranger, "Conta Alheia");
        var market = await SubAsync("Supermercado / Mercearia");
        await _env.ExecuteAsync(
            "INSERT INTO transacoes (usuario_id, conta_id, subcategoria_id, descricao, valor, data_transacao, tipo_transacao) VALUES (@stranger, @strangerAccount, @market, 'ALHEIO', -500, @date, 'Despesa')",
            new { stranger, strangerAccount, market, date = JobsEnv.D("2026-10-02") });

        var report = await _reader.GetMonthByCategoryAsync(_user, new DateOnly(2026, 10, 1), Today, CancellationToken.None);

        report.Categories.ShouldBeEmpty();
    }
}
