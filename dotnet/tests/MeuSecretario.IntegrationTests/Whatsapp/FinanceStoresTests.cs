using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Domain.Whatsapp;
using MeuSecretario.Infrastructure.Whatsapp;
using MeuSecretario.IntegrationTests.Automation;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.IntegrationTests.Transactions;

namespace MeuSecretario.IntegrationTests.Whatsapp;

/// <summary>SQL real das leituras financeiras do WhatsApp (spec 012 §7.6, §7.7, §7.8).</summary>
[Trait("Category", "Docker")]
public class FinanceStoresTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private AutomationEnv _env = default!;
    private WhatsAppFinanceReader _reader = default!;
    private FeedbackReader _feedback = default!;
    private int _user;
    private int _other;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _env = new AutomationEnv(postgres);
        _reader = new WhatsAppFinanceReader(postgres.Connections);
        _feedback = new FeedbackReader(postgres.Connections);
        _user = await _env.SeedUserAsync();
        _other = await _env.SeedUserAsync("5511999990001", "outra-chave");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("AC", "012-107")]
    public async Task ActiveAccountById_IgnoresInactiveAndForeignAccounts()
    {
        var active = await _env.Data.InsertAccountAsync(_user, "Nubank");
        var inactive = await _env.Data.InsertAccountAsync(_user, "Velha", ativa: false);
        var foreign = await _env.Data.InsertAccountAsync(_other, "Alheia");

        (await _reader.FindActiveAccountAsync(_user, active, CancellationToken.None))!.Name.ShouldBe("Nubank");
        (await _reader.FindActiveAccountAsync(_user, inactive, CancellationToken.None)).ShouldBeNull();
        (await _reader.FindActiveAccountAsync(_user, foreign, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "012-107")]
    public async Task DefaultAccounts_AreReadFromTheUser()
    {
        var income = await _env.Data.InsertAccountAsync(_user, "Renda");
        var expense = await _env.Data.InsertAccountAsync(_user, "Despesa");
        await _env.Data.ExecuteAsync("UPDATE usuarios SET conta_padrao_renda_id = @income, conta_padrao_despesa_id = @expense WHERE id = @user", new { income, expense, user = _user });

        (await _reader.GetDefaultAccountsAsync(_user, CancellationToken.None)).ShouldBe(new DefaultAccounts(income, expense));
        (await _reader.GetDefaultAccountsAsync(_other, CancellationToken.None)).ShouldBe(new DefaultAccounts(null, null));
    }

    [Fact]
    [Trait("AC", "012-104")]
    public async Task RecurringSchedule_PrefersExactThenVariableReminderThenDescription()
    {
        var account = await _env.Data.InsertAccountAsync(_user, "Nubank");
        await _env.InsertScheduleAsync(_user, account, "Conta de Luz Extra", amount: 10m);
        await _env.InsertScheduleAsync(_user, account, "Luz", type: "LEMBRETE_VARIAVEL", amount: 20m);
        await _env.InsertScheduleAsync(_user, account, "Luz", amount: 30m);
        await _env.InsertScheduleAsync(_user, account, "Luz Inativa", active: false, amount: 40m);
        await _env.InsertScheduleAsync(_user, account, "Luz Parcelada", type: "PARCELADO", amount: 50m, total: 3);
        await _env.InsertScheduleAsync(_other, await _env.Data.InsertAccountAsync(_other, "X"), "Luz", amount: 60m);

        var found = await _reader.FindRecurringByDescriptionAsync(_user, "luz", CancellationToken.None);
        found!.ExpectedAmount.ShouldBe(20m);
        found.AccountId.ShouldBe(account);

        (await _reader.FindRecurringByDescriptionAsync(_user, "extra", CancellationToken.None))!.ExpectedAmount.ShouldBe(10m);
        (await _reader.FindRecurringByDescriptionAsync(_user, "parcelada", CancellationToken.None)).ShouldBeNull();
        (await _reader.FindRecurringByDescriptionAsync(_user, "inexistente", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "012-130")]
    public async Task AccountByName_UsesExactThenPartialThenOptionalFallback_OnlyActive()
    {
        var nubank = await _env.Data.InsertAccountAsync(_user, "Nubank");
        var wallet = await _env.Data.InsertAccountAsync(_user, "Carteira", "Dinheiro");
        await _env.Data.InsertAccountAsync(_user, "Nu Velha", ativa: false);

        (await _reader.FindAccountIdByNameAsync(_user, "Nubank", false, null, CancellationToken.None)).ShouldBe(nubank);
        (await _reader.FindAccountIdByNameAsync(_user, "carte", false, null, CancellationToken.None)).ShouldBe(wallet);
        (await _reader.FindAccountIdByNameAsync(_user, "xyz", false, null, CancellationToken.None)).ShouldBeNull();
        (await _reader.FindAccountIdByNameAsync(_user, "xyz", true, null, CancellationToken.None)).ShouldBe(nubank);
        (await _reader.FindAccountIdByNameAsync(_user, "xyz", true, "Dinheiro", CancellationToken.None)).ShouldBe(wallet);
        (await _reader.FindAccountIdByNameAsync(_user, "Nubank", false, "Dinheiro", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "012-115")]
    public async Task Feedback_PotSumsAllTheSubcategoriesOfThePot_Deviation170()
    {
        var account = await _env.Data.InsertAccountAsync(_user, "Nubank");
        var otherSubcategory = await _env.ScalarAsync<int>(
            "SELECT id FROM subcategoria WHERE usuario_id IS NULL AND id <> @expense AND id <> @income ORDER BY id LIMIT 1",
            new { expense = TransactionsTestData.ExpenseSubcategory, income = TransactionsTestData.IncomeSubcategory });
        await _env.Data.InsertTransactionAsync(_user, account, valor: -30m, data: "2026-10-06");
        await _env.Data.InsertTransactionAsync(_user, account, valor: -50m, data: "2026-10-05", subcategoryId: otherSubcategory);
        await _env.InsertPotAsync(_user, "Comida", 200m, "MENSAL", TransactionsTestData.ExpenseSubcategory, otherSubcategory);

        var pot = await _feedback.FindPotAsync(_user, TransactionsTestData.ExpenseSubcategory, new DateOnly(2026, 10, 7), CancellationToken.None);

        // Gasto do pote = 30 + 50 (o Flask mostraria só os 30 da subcategoria da transação).
        pot!.Remaining.ShouldBe(120d);
        pot.PercentUsed.ShouldBe(40d);
    }

    [Fact]
    [Trait("AC", "012-115")]
    public async Task Feedback_ReadsTransactionPotAlertAndBalance()
    {
        var account = await _env.Data.InsertAccountAsync(_user, "Nubank");
        var tx = await _env.Data.InsertTransactionAsync(_user, account, valor: -30m, data: "2026-10-06", descricao: "Padaria");
        await _env.Data.InsertTransactionAsync(_user, account, valor: -70m, data: "2026-10-05");
        await _env.Data.InsertTransactionAsync(_user, account, valor: -999m, data: "2026-09-20");
        await _env.InsertPotAsync(_user, "Comida", 200m, "MENSAL", TransactionsTestData.ExpenseSubcategory);

        var transaction = await _feedback.FindTransactionAsync(tx, CancellationToken.None);
        transaction!.Description.ShouldBe("Padaria");
        transaction.Amount.ShouldBe(-30d);
        transaction.AccountName.ShouldBe("Nubank");

        var pot = await _feedback.FindPotAsync(_user, TransactionsTestData.ExpenseSubcategory, new DateOnly(2026, 10, 7), CancellationToken.None);
        pot!.Name.ShouldBe("Comida");
        pot.Remaining.ShouldBe(100d);
        pot.PercentUsed.ShouldBe(50d);
        pot.Periodicity.ShouldBe("Mensal");
        (await _feedback.FindPotAsync(_user, TransactionsTestData.IncomeSubcategory, new DateOnly(2026, 10, 7), CancellationToken.None)).ShouldBeNull();

        (await _feedback.GetPotAlertConfigAsync(_user, CancellationToken.None)).ShouldBeNull();
        await _env.Data.ExecuteAsync(
            "INSERT INTO notificationconfigs (usuario_id, alerta_potes_ativo, alerta_potes_threshold) VALUES (@user, true, 70)", new { user = _user });
        (await _feedback.GetPotAlertConfigAsync(_user, CancellationToken.None)).ShouldBe(new PotAlertConfig(true, 70));

        var balance = await _feedback.GetBalanceFooterAsync(_user, account, CancellationToken.None);
        balance!.AccountName.ShouldBe("Nubank");
        balance.Available.ShouldBe(-30d - 70d - 999d);
    }

    [Fact]
    [Trait("AC", "012-115")]
    public async Task Feedback_InvoiceFooter_SumsOpenFutureInvoicesOfTheCard()
    {
        var card = await _env.Data.InsertCardAsync(_user, "Inter", 10, 17);
        var open = await _env.Data.InsertInvoiceAsync(card, "2026-10-17", "2026-10-10");
        var paid = await _env.Data.InsertInvoiceAsync(card, "2026-09-17", "2026-09-10", "Paga");
        await _env.Data.InsertTransactionAsync(_user, card, valor: -100m, faturaId: open);
        await _env.Data.InsertTransactionAsync(_user, card, valor: -25.5m, faturaId: open);
        await _env.Data.InsertTransactionAsync(_user, card, valor: -500m, faturaId: paid);

        var footer = await _feedback.GetInvoiceFooterAsync(_user, card, new DateOnly(2026, 10, 7), CancellationToken.None);
        footer!.Total.ShouldBe(125.5d);
        footer.ClosingDay.ShouldBe(10);

        var checking = await _env.Data.InsertAccountAsync(_user, "Nubank");
        (await _feedback.GetInvoiceFooterAsync(_user, checking, new DateOnly(2026, 10, 7), CancellationToken.None)).ShouldBeNull();
    }
}
