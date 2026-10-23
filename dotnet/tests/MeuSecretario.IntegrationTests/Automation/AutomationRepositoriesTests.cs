using MeuSecretario.Application.Automation;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Infrastructure.Automation;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.IntegrationTests.Transactions;
using Npgsql;

namespace MeuSecretario.IntegrationTests.Automation;

/// <summary>SQL real das leituras de automação (spec 011 §7.3, §7.6): contas ativas, buscas, categorias, contas fixas, potes.</summary>
[Trait("Category", "Docker")]
public class AutomationLookupsTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private AutomationEnv _env = default!;
    private AutomationLookups _lookups = default!;
    private int _user;
    private int _other;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _env = new AutomationEnv(postgres);
        _lookups = new AutomationLookups(postgres.Connections);
        _user = await _env.SeedUserAsync();
        _other = await _env.SeedUserAsync("5511999990001", "outra-chave");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("AC", "011-147")]
    public async Task UserKeyReader_ListsOnlyUsersWithAKey_InIdOrder()
    {
        await _env.SeedUserAsync("5511999990002", key: null);

        var rows = await new UserKeyReader(postgres.Connections).ListAsync(CancellationToken.None);

        rows.Select(r => (r.UserId, r.StoredKey)).ShouldBe([(_user, AutomationEnv.UserKey), (_other, "outra-chave")]);
        rows[0].WhatsApp.ShouldBe(AutomationEnv.Whatsapp);
    }

    [Fact]
    [Trait("AC", "011-014")]
    public async Task ActiveAccounts_ExcludeInactiveAndOtherUsers_InIdOrder()
    {
        var second = await _env.Data.InsertAccountAsync(_user, "Zeta");
        var first = await _env.Data.InsertAccountAsync(_user, "Alfa");
        await _env.Data.InsertAccountAsync(_user, "Antiga", ativa: false);
        await _env.Data.InsertAccountAsync(_other, "Alheia");

        var accounts = await _lookups.ListActiveAccountsAsync(_user, CancellationToken.None);

        accounts.Select(a => a.Id).ShouldBe([second, first]);
        accounts[0].ShouldBe(new AccountRef(second, "Zeta", "Conta Corrente"));
    }

    [Fact]
    [Trait("AC", "011-035")]
    public async Task AccountSearch_PrefersExactThenPartialNameThenType_LowestIdFirst()
    {
        var nubank = await _env.Data.InsertAccountAsync(_user, "Nubank");
        var nuConta = await _env.Data.InsertAccountAsync(_user, "NuConta", "Conta Corrente");
        var card = await _env.Data.InsertCardAsync(_user, "Inter");
        await _env.Data.InsertAccountAsync(_user, "Nu Inativa", ativa: false);
        await _env.Data.InsertAccountAsync(_other, "Nubank");

        (await _lookups.FindAccountByNameOrTypeAsync(_user, "Nubank", CancellationToken.None))!.Id.ShouldBe(nubank);
        (await _lookups.FindAccountByNameOrTypeAsync(_user, "nu", CancellationToken.None))!.Id.ShouldBe(nubank);
        (await _lookups.FindAccountByNameOrTypeAsync(_user, "NUCONTA", CancellationToken.None))!.Id.ShouldBe(nuConta);
        (await _lookups.FindAccountByNameOrTypeAsync(_user, "Crédito", CancellationToken.None))!.Id.ShouldBe(card);
        (await _lookups.FindAccountByNameOrTypeAsync(_user, "corrente", CancellationToken.None))!.Id.ShouldBe(nubank);
        (await _lookups.FindAccountByNameOrTypeAsync(_user, "Inativa", CancellationToken.None)).ShouldBeNull();
        (await _lookups.FindAccountByNameOrTypeAsync(_user, "credito", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "011-035")]
    public async Task AccountSearch_TheClientsWildcardsStillWork()
    {
        var nubank = await _env.Data.InsertAccountAsync(_user, "Nubank");

        (await _lookups.FindAccountByNameOrTypeAsync(_user, "N%k", CancellationToken.None))!.Id.ShouldBe(nubank);
        (await _lookups.FindAccountByNameOrTypeAsync(_user, "Nub_nk", CancellationToken.None))!.Id.ShouldBe(nubank);
    }

    [Fact]
    [Trait("AC", "011-063")]
    public async Task AccountWithFallback_ExactThenPartialThenTheFirstActive()
    {
        var first = await _env.Data.InsertAccountAsync(_user, "Nubank");
        var wallet = await _env.Data.InsertAccountAsync(_user, "Minha Carteira Azul");
        var exact = await _env.Data.InsertAccountAsync(_user, "Carteira");

        (await _lookups.FindAccountIdWithFallbackAsync(_user, "Carteira", CancellationToken.None)).ShouldBe(exact);
        await _env.ScalarAsync<int>("UPDATE contas SET ativa = false WHERE id = @exact RETURNING id", new { exact });
        (await _lookups.FindAccountIdWithFallbackAsync(_user, "Carteira", CancellationToken.None)).ShouldBe(wallet);
        await _env.ScalarAsync<int>("UPDATE contas SET ativa = false WHERE id = @wallet RETURNING id", new { wallet });
        (await _lookups.FindAccountIdWithFallbackAsync(_user, "Carteira", CancellationToken.None)).ShouldBe(first);
        await _env.ScalarAsync<int>("UPDATE contas SET ativa = false WHERE id = @first RETURNING id", new { first });
        (await _lookups.FindAccountIdWithFallbackAsync(_user, "Carteira", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "011-013")]
    public async Task Categories_ExpenseExcludesIncomeGroup_AndIncomeOnlyHasIt_OrderedById()
    {
        var expense = await _lookups.ListCategoriesAsync(_user, income: false, CancellationToken.None);
        var income = await _lookups.ListCategoriesAsync(_user, income: true, CancellationToken.None);

        expense.ShouldContain(c => c.Id == TransactionsTestData.ExpenseSubcategory && c.SubName == "Outros" && c.MacroName == "Despesas Gerais");
        expense.Select(c => c.Id).ShouldBe(expense.Select(c => c.Id).Order());
        income.ShouldNotBeEmpty();
        income.Select(c => c.Id).Intersect(expense.Select(c => c.Id)).ShouldBeEmpty();
        income.ShouldNotContain(c => c.MacroName == "Despesas Gerais");
    }

    [Fact]
    [Trait("AC", "011-013")]
    public async Task Categories_IncludeTheUsersOwnAndNotAnotherUsers()
    {
        await _env.ScalarAsync<int>(
            """
            WITH macro AS (INSERT INTO macrocategoria (grupo_id, usuario_id, nome_macro, ordem_macro)
                           SELECT id, @user, 'Minha macro', 1 FROM grupocategoria WHERE nome_grupo = 'Despesa Essencial' RETURNING id)
            INSERT INTO subcategoria (macro_id, usuario_id, nome_sub) SELECT id, @user, 'Minha sub' FROM macro RETURNING id
            """,
            new { user = _user });

        (await _lookups.ListCategoriesAsync(_user, false, CancellationToken.None)).ShouldContain(c => c.SubName == "Minha sub");
        (await _lookups.ListCategoriesAsync(_other, false, CancellationToken.None)).ShouldNotContain(c => c.SubName == "Minha sub");
    }

    [Fact]
    [Trait("AC", "011-018")]
    public async Task CategoryLabel_IsMacroArrowSub_OrNull()
    {
        (await _lookups.GetCategoryLabelAsync(TransactionsTestData.ExpenseSubcategory, CancellationToken.None)).ShouldBe("Despesas Gerais -> Outros");
        (await _lookups.GetCategoryLabelAsync(999999, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "011-061")]
    public async Task FixedBills_AreActiveFixoOrVariableOfTheUser_OrderedById()
    {
        var account = await _env.Data.InsertAccountAsync(_user, "Nubank");
        var luz = await _env.InsertScheduleAsync(_user, account, "Luz", amount: 120.00m, day: 10);
        var internet = await _env.InsertScheduleAsync(_user, account, "Internet", type: "LEMBRETE_VARIAVEL", amount: null);
        await _env.InsertScheduleAsync(_user, account, "Notebook", type: "PARCELADO", total: 3, executed: 1);
        await _env.InsertScheduleAsync(_user, account, "Inativa", active: false);
        var otherAccount = await _env.Data.InsertAccountAsync(_other, "Conta");
        await _env.InsertScheduleAsync(_other, otherAccount, "Alheia");

        var bills = await _lookups.ListFixedBillsAsync(_user, CancellationToken.None);

        bills.Select(b => (b.ScheduleId, b.Description, b.ExpectedAmount, b.ScheduleType, b.AccountId)).ShouldBe(
        [
            (luz, "Luz", (decimal?)120.00m, "FIXO", account),
            (internet, "Internet", null, "LEMBRETE_VARIAVEL", account),
        ]);
        bills[0].Category.ShouldBe("Outros");
    }

    [Fact]
    [Trait("AC", "011-061")]
    public async Task SettleTarget_IsTheUsersScheduleOnly()
    {
        var account = await _env.Data.InsertAccountAsync(_user, "Nubank");
        var luz = await _env.InsertScheduleAsync(_user, account, "Luz", subcategoryId: 59);

        (await _lookups.FindSettleTargetAsync(_user, luz, CancellationToken.None)).ShouldBe(new SettleTarget("Luz", account, 59));
        (await _lookups.FindSettleTargetAsync(_other, luz, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "011-037")]
    public async Task Pots_SumTheSpendOfThePeriodInclusively_OnlyExpensesOfTheUser()
    {
        var account = await _env.Data.InsertAccountAsync(_user, "Nubank");
        var pot = await _env.InsertPotAsync(_user, "Mercado", 500m, "MENSAL", 59);
        await _env.InsertPotAsync(_user, "Inativo", 1m, "MENSAL", 59);
        await _env.ScalarAsync<int>("UPDATE potesdegastos SET ativo = false WHERE nome_pote = 'Inativo' RETURNING id");
        await _env.InsertPotAsync(_user, "Outra sub", 1m, "MENSAL", 60);
        await _env.InsertPotAsync(_other, "Alheio", 1m, "MENSAL", 59);
        await _env.Data.InsertTransactionAsync(_user, account, "Despesa", -10m, "2026-10-01", subcategoryId: 59);
        await _env.Data.InsertTransactionAsync(_user, account, "Despesa", -20.50m, "2026-10-31", subcategoryId: 59);
        await _env.Data.InsertTransactionAsync(_user, account, "Despesa", -99m, "2026-11-01", subcategoryId: 59);
        await _env.Data.InsertTransactionAsync(_user, account, "Despesa", -99m, "2026-09-30", subcategoryId: 59);
        await _env.Data.InsertTransactionAsync(_user, account, "Renda", 5m, "2026-10-10", subcategoryId: 59);
        await _env.Data.InsertTransactionAsync(_user, account, "Despesa", -77m, "2026-10-10", subcategoryId: 60);
        var reader = new BudgetReader(postgres.Connections);

        var pots = await reader.ListPotsForSubcategoryAsync(_user, 59, CancellationToken.None);
        var spent = await reader.SumPotSpendAsync(_user, pot, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), CancellationToken.None);

        pots.ShouldBe([new PotRow(pot, "Mercado", 500, "MENSAL")]);
        spent.ShouldBe(30.50m);
        (await reader.SumPotSpendAsync(_user, pot, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31), CancellationToken.None)).ShouldBe(0m);
    }
}

/// <summary>SQL real da reserva e do populador de faturas, com rollback (spec 011 §3.4, §3.5, §7.7).</summary>
[Trait("Category", "Docker")]
public class InvoiceScheduleStoreTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private AutomationEnv _env = default!;
    private int _user;
    private int _card;
    private int _invoice;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _env = new AutomationEnv(postgres);
        _user = await _env.SeedUserAsync();
        _card = await _env.Data.InsertCardAsync(_user, "Cartão", 10, 15);
        _invoice = await _env.Data.InsertInvoiceAsync(_card, "2026-04-15", "2026-04-10");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private InvoiceScheduleProcessor Processor() => new(new InvoiceScheduleUnitOfWork(postgres.Connections));

    [Fact]
    [Trait("AC", "011-126")]
    public async Task AFailureInTheMiddle_RollsBackOnlyThatSchedule_AndTheOthersAreKept()
    {
        await _env.InsertScheduleAsync(_user, _card, "A", amount: 1.00m, day: 1);
        await _env.InsertScheduleAsync(_user, _card, "B", amount: 2.00m, day: 2);
        await _env.InsertScheduleAsync(_user, _card, "C", type: "PARCELADO", total: 3, executed: 0, amount: 3.00m, day: 3);
        await using (var connection = await postgres.DataSource.OpenConnectionAsync())
        {
            await using var create = new NpgsqlCommand(
                """
                CREATE OR REPLACE FUNCTION fail_on_b() RETURNS trigger AS $$ BEGIN IF NEW.descricao = 'B' THEN RAISE EXCEPTION 'falha injetada'; END IF; RETURN NEW; END $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_fail_on_b BEFORE INSERT ON transacoes FOR EACH ROW EXECUTE FUNCTION fail_on_b();
                """,
                connection);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            // Desvio 101: um SAVEPOINT por agendamento. O erro de B (que o PostgreSQL abortaria para a transação toda) desfaz só B.
            var stats = await Processor().ProcessAsync(_invoice, _card, new DateOnly(2026, 4, 15), CancellationToken.None);

            stats.Processed.ShouldBe(2);
            stats.Errors.ShouldBe(1);
            stats.Details.Select(d => d.Description).ShouldBe(["A", "C (1/3)"]);
            (await _env.QueryAsync<string>("SELECT descricao FROM transacoes ORDER BY id")).ShouldBe(["A", "C (1/3)"]);
            (await _env.ScalarAsync<int>("SELECT parcelas_executadas FROM agendamentos WHERE descricao = 'C'")).ShouldBe(1);
        }
        finally
        {
            await using var connection = await postgres.DataSource.OpenConnectionAsync();
            await using var drop = new NpgsqlCommand("DROP TRIGGER IF EXISTS trg_fail_on_b ON transacoes; DROP FUNCTION IF EXISTS fail_on_b();", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    [Trait("AC", "011-122")]
    public async Task TheInstallmentThatIsNotTheLastStaysActive()
    {
        var id = await _env.InsertScheduleAsync(_user, _card, "Notebook", type: "PARCELADO", total: 5, executed: 1, amount: 300.00m, day: 5);

        var stats = await Processor().ProcessAsync(_invoice, _card, new DateOnly(2026, 4, 15), CancellationToken.None);

        stats.Installments.ShouldBe(1);
        (await _env.QueryAsync<(int Executed, bool Active)>("SELECT parcelas_executadas, ativo FROM agendamentos WHERE id = @id", new { id })).Single().ShouldBe((2, true));
    }

    [Fact]
    [Trait("AC", "011-121")]
    public async Task FindInvoice_ReturnsTheCardTheDueDateAndTheStatus()
    {
        var target = await new InvoiceScheduleUnitOfWork(postgres.Connections).FindInvoiceAsync(_invoice, CancellationToken.None);

        target.ShouldBe(new ReprocessTarget(_invoice, _card, "Cartão", new DateOnly(2026, 4, 15), "Aberta"));
        (await new InvoiceScheduleUnitOfWork(postgres.Connections).FindInvoiceAsync(999, CancellationToken.None)).ShouldBeNull();
    }
}
