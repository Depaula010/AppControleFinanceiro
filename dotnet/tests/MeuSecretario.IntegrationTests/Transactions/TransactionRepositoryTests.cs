using MeuSecretario.Application.Transactions;
using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.Transactions;
using MeuSecretario.Infrastructure.Transactions;
using MeuSecretario.IntegrationTests.Support;
using Npgsql;
using static MeuSecretario.IntegrationTests.Transactions.TransactionsTestData;

namespace MeuSecretario.IntegrationTests.Transactions;

/// <summary>SQL real do repositório de transações (spec 004 §3 e plan §3).</summary>
[Trait("Category", "Docker")]
public class TransactionRepositoryTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private readonly TransactionsTestData _data = new(postgres.DataSource);
    private readonly TransactionRepository _repository = new(postgres.Connections);
    private int _user;
    private int _other;
    private int _account;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _user = await _data.InsertUserAsync("5511900000001", "Ana");
        _other = await _data.InsertUserAsync("5511900000002", "Beto");
        _account = await _data.InsertAccountAsync(_user, "Corrente");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static ListFilter Filter(int limit = 20, long offset = 0, TransactionKind? kind = null, string? from = null, string? to = null) =>
        new(limit, offset, kind, from is null ? null : Day(from), to is null ? null : Day(to));

    private static TransactionChanges Changes(
        Change<string> description = default, Change<decimal> amount = default, Change<string> type = default, Change<DateOnly> date = default,
        Change<int> subcategory = default, Change<int> account = default, Change<string?> notes = default, Change<bool> consolidated = default) =>
        new(description, amount, type, date, subcategory, account, notes, consolidated);

    // ---------- listar ----------

    [Fact]
    [Trait("AC", "004-01")]
    public async Task List_ReturnsTheRowsWithJoinedNames_AndSignedCents()
    {
        await _data.InsertTransactionAsync(_user, _account, valor: -70.55m, data: "2025-12-11", descricao: "Supermercado");

        var page = await _repository.ListAsync(_user, Filter(), CancellationToken.None);

        page.Total.ShouldBe(1);
        var item = page.Items.Single();
        item.Description.ShouldBe("Supermercado");
        item.Amount.ShouldBe(-70.55m);
        item.DbType.ShouldBe("Despesa");
        item.Date.ShouldBe(Day("2025-12-11"));
        item.Category.ShouldBe("Despesas Gerais");
        item.Subcategory.ShouldBe("Outros");
        item.AccountName.ShouldBe("Corrente");
        item.Consolidated.ShouldBeTrue();
        item.AccountId.ShouldBe(_account);
        item.SubcategoryId.ShouldBe(ExpenseSubcategory);
        item.Notes.ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "004-09")]
    public async Task List_WithoutTransactions_IsEmpty()
    {
        var page = await _repository.ListAsync(_user, Filter(), CancellationToken.None);

        page.Total.ShouldBe(0);
        page.Items.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "004-04")]
    public async Task List_ReceitaFilterFindsRenda_AndNoFilterListsAllFourTypes()
    {
        await _data.InsertTransactionAsync(_user, _account, "Renda", 3000m, descricao: "salário");
        await _data.InsertTransactionAsync(_user, _account, "Despesa", -10m);
        await _data.InsertTransactionAsync(_user, _account, "Transferência", -5m);
        await _data.InsertTransactionAsync(_user, _account, "Pagamento Fatura", -7m);

        var income = await _repository.ListAsync(_user, Filter(kind: TransactionKind.Income), CancellationToken.None);
        var expense = await _repository.ListAsync(_user, Filter(kind: TransactionKind.Expense), CancellationToken.None);
        var all = await _repository.ListAsync(_user, Filter(), CancellationToken.None);

        income.Items.Single().Description.ShouldBe("salário");
        income.Total.ShouldBe(1);
        expense.Total.ShouldBe(1);
        all.Items.Select(i => i.DbType).Order().ShouldBe(["Despesa", "Pagamento Fatura", "Renda", "Transferência"]);
    }

    [Fact]
    [Trait("AC", "004-05")]
    public async Task List_DateFilters_AreInclusive_AndInvertedRangeIsEmpty()
    {
        foreach (var day in new[] { "2025-11-30", "2025-12-01", "2025-12-15", "2025-12-31", "2026-01-01" })
        {
            await _data.InsertTransactionAsync(_user, _account, data: day);
        }

        var december = await _repository.ListAsync(_user, Filter(from: "2025-12-01", to: "2025-12-31"), CancellationToken.None);
        var onlyFrom = await _repository.ListAsync(_user, Filter(from: "2025-12-31"), CancellationToken.None);
        var onlyTo = await _repository.ListAsync(_user, Filter(to: "2025-12-01"), CancellationToken.None);
        var inverted = await _repository.ListAsync(_user, Filter(from: "2025-12-31", to: "2025-12-01"), CancellationToken.None);

        december.Total.ShouldBe(3);
        onlyFrom.Total.ShouldBe(2);
        onlyTo.Total.ShouldBe(2);
        inverted.Total.ShouldBe(0);
        inverted.Items.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "004-07")]
    public async Task List_OrdersByDateThenCreatedAtThenIdDescending_AndPaginates()
    {
        var oldest = await _data.InsertTransactionAsync(_user, _account, data: "2026-03-01", descricao: "c-antiga");
        var sameDayEarly = await _data.InsertTransactionAsync(_user, _account, data: "2026-03-05", descricao: "b-cedo", createdAt: "2026-03-05T08:00:00Z");
        var sameDayLate = await _data.InsertTransactionAsync(_user, _account, data: "2026-03-05", descricao: "a-tarde", createdAt: "2026-03-05T20:00:00Z");
        var tieA = await _data.InsertTransactionAsync(_user, _account, data: "2026-03-09", descricao: "empate-1", createdAt: "2026-03-09T10:00:00Z");
        var tieB = await _data.InsertTransactionAsync(_user, _account, data: "2026-03-09", descricao: "empate-2", createdAt: "2026-03-09T10:00:00Z");

        var all = await _repository.ListAsync(_user, Filter(), CancellationToken.None);
        all.Items.Select(i => i.Id).ShouldBe([tieB, tieA, sameDayLate, sameDayEarly, oldest]);

        var page = await _repository.ListAsync(_user, Filter(limit: 2, offset: 2), CancellationToken.None);
        page.Items.Select(i => i.Id).ShouldBe([sameDayLate, sameDayEarly]);
        page.Total.ShouldBe(5);

        var beyond = await _repository.ListAsync(_user, Filter(limit: 2, offset: 50), CancellationToken.None);
        beyond.Items.ShouldBeEmpty();
        beyond.Total.ShouldBe(5);

        var huge = await _repository.ListAsync(_user, Filter(offset: long.MaxValue), CancellationToken.None);
        huge.Items.ShouldBeEmpty();
        huge.Total.ShouldBe(5);
    }

    [Fact]
    [Trait("AC", "004-08")]
    public async Task List_NeverShowsAnotherUsersRows_NorCountsThem()
    {
        var otherAccount = await _data.InsertAccountAsync(_other, "Dele");
        await _data.InsertTransactionAsync(_other, otherAccount, descricao: "do outro");
        await _data.InsertTransactionAsync(_user, _account, descricao: "meu");

        var page = await _repository.ListAsync(_user, Filter(), CancellationToken.None);

        page.Total.ShouldBe(1);
        page.Items.Single().Description.ShouldBe("meu");
    }

    [Fact]
    [Trait("AC", "004-10a")]
    public async Task List_ShowsTransactionsOfInactiveAccounts()
    {
        var inactive = await _data.InsertAccountAsync(_user, "Velha", ativa: false);
        await _data.InsertTransactionAsync(_user, inactive);

        (await _repository.ListAsync(_user, Filter(), CancellationToken.None)).Items.Single().AccountName.ShouldBe("Velha");
    }

    // ---------- leitura e checagens ----------

    [Fact]
    [Trait("AC", "004-41")]
    public async Task Get_OnlyReturnsTheUsersOwnRow()
    {
        var id = await _data.InsertTransactionAsync(_user, _account, "Renda", 50m, "2026-03-10");

        var mine = await _repository.GetAsync(_user, id, CancellationToken.None);
        mine.ShouldBe(new CurrentTransaction(id, "Renda", 50m, _account, Day("2026-03-10")));
        (await _repository.GetAsync(_other, id, CancellationToken.None)).ShouldBeNull();
        (await _repository.GetAsync(_user, 99999, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "004-48")]
    public async Task SubcategoryVisible_GlobalOrOwn_NeverAnotherUsers()
    {
        var own = await _data.ScalarAsync<int>(
            "INSERT INTO subcategoria (macro_id, nome_sub, usuario_id) VALUES (17, 'Minha', @_user) RETURNING id", new { _user });
        var others = await _data.ScalarAsync<int>(
            "INSERT INTO subcategoria (macro_id, nome_sub, usuario_id) VALUES (17, 'Dele', @_other) RETURNING id", new { _other });

        (await _repository.SubcategoryVisibleAsync(_user, ExpenseSubcategory, CancellationToken.None)).ShouldBeTrue();
        (await _repository.SubcategoryVisibleAsync(_user, own, CancellationToken.None)).ShouldBeTrue();
        (await _repository.SubcategoryVisibleAsync(_user, others, CancellationToken.None)).ShouldBeFalse();
        (await _repository.SubcategoryVisibleAsync(_user, 99999, CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "004-49")]
    public async Task ActiveAccountOwned_RejectsInactiveForeignAndUnknown()
    {
        var inactive = await _data.InsertAccountAsync(_user, "Velha", ativa: false);
        var foreign = await _data.InsertAccountAsync(_other, "Alheia");

        (await _repository.ActiveAccountOwnedAsync(_user, _account, CancellationToken.None)).ShouldBeTrue();
        (await _repository.ActiveAccountOwnedAsync(_user, inactive, CancellationToken.None)).ShouldBeFalse();
        (await _repository.ActiveAccountOwnedAsync(_user, foreign, CancellationToken.None)).ShouldBeFalse();
        (await _repository.ActiveAccountOwnedAsync(_user, 99999, CancellationToken.None)).ShouldBeFalse();
    }

    // ---------- atualizar ----------

    [Fact]
    [Trait("AC", "004-42")]
    public async Task Update_WritesOnlyTheSetFields_AndAdvancesUpdatedAt()
    {
        var id = await _data.InsertTransactionAsync(_user, _account, "Despesa", -50m, "2026-03-10", "Antes");
        await _data.ExecuteAsync("UPDATE transacoes SET updated_at = '2020-01-01' WHERE id = @id", new { id });

        var updated = await _repository.UpdateAsync(_user, id, Changes(description: Change.Set("Depois")), CancellationToken.None);

        updated.ShouldBe(new UpdatedTransaction(id, "Depois", -50m, "Despesa", Day("2026-03-10")));
        var row = (await _data.QueryAsync<(decimal Valor, bool Consolidada, DateTime Updated)>(
            "SELECT valor, consolidada, updated_at FROM transacoes WHERE id = @id", new { id })).Single();
        row.Valor.ShouldBe(-50m);
        row.Consolidada.ShouldBeTrue();
        row.Updated.Year.ShouldBeGreaterThan(2020);
    }

    [Fact]
    [Trait("AC", "004-44")]
    public async Task Update_AllFields_AndNotesCanBeCleared()
    {
        var id = await _data.InsertTransactionAsync(_user, _account, "Despesa", -50m);
        await _data.ExecuteAsync("UPDATE transacoes SET observacoes = 'antiga' WHERE id = @id", new { id });
        var other = await _data.InsertAccountAsync(_user, "Outra");

        var updated = await _repository.UpdateAsync(
            _user,
            id,
            Changes(
                Change.Set("Novo"), Change.Set(10.005m), Change.Set("Renda"), Change.Set(Day("2026-04-01")),
                Change.Set(IncomeSubcategory), Change.Set(other), Change.Set<string?>(null), Change.Set(false)),
            CancellationToken.None);

        updated.ShouldBe(new UpdatedTransaction(id, "Novo", 10.01m, "Renda", Day("2026-04-01")));
        var row = (await _data.QueryAsync<(int Conta, int Sub, string? Notes, bool Consolidada)>(
            "SELECT conta_id, subcategoria_id, observacoes, consolidada FROM transacoes WHERE id = @id", new { id })).Single();
        row.ShouldBe((other, IncomeSubcategory, null, false));
    }

    [Fact]
    [Trait("AC", "004-55")]
    public async Task Update_OfAnotherUsersRow_ReturnsNull_AndChangesNothing()
    {
        var id = await _data.InsertTransactionAsync(_user, _account, descricao: "minha");

        (await _repository.UpdateAsync(_other, id, Changes(description: Change.Set("roubada")), CancellationToken.None)).ShouldBeNull();

        (await _data.ScalarAsync<string>("SELECT descricao FROM transacoes WHERE id = @id", new { id })).ShouldBe("minha");
    }

    // ---------- apagar ----------

    [Fact]
    [Trait("AC", "004-70")]
    public async Task Delete_RemovesTheRow_AndASecondDeleteIsNotFound()
    {
        var id = await _data.InsertTransactionAsync(_user, _account);

        (await _repository.DeleteWithPairAsync(_user, id, Day("2026-10-04"), CancellationToken.None)).ShouldBeTrue();
        (await _repository.DeleteWithPairAsync(_user, id, Day("2026-10-04"), CancellationToken.None)).ShouldBeFalse();
        (await _data.ScalarAsync<int>("SELECT count(*)::int FROM transacoes")).ShouldBe(0);
    }

    [Theory]
    [Trait("AC", "004-73")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Delete_OfEitherLegOfAPair_RemovesBoth(bool deleteTheFirst)
    {
        var second = await _data.InsertAccountAsync(_user, "Poupança");
        var first = await _data.InsertTransactionAsync(_user, _account, "Transferência", -100m);
        var other = await _data.InsertTransactionAsync(_user, second, "Transferência", 100m);
        await _data.LinkPairAsync(first, other);

        (await _repository.DeleteWithPairAsync(_user, deleteTheFirst ? first : other, Day("2026-10-04"), CancellationToken.None)).ShouldBeTrue();

        (await _data.ScalarAsync<int>("SELECT count(*)::int FROM transacoes")).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "004-74")]
    public async Task Delete_WithAPairBelongingToAnotherUser_OnlyRemovesTheOwnRow()
    {
        var otherAccount = await _data.InsertAccountAsync(_other, "Dele");
        var mine = await _data.InsertTransactionAsync(_user, _account, "Transferência", -100m);
        var foreign = await _data.InsertTransactionAsync(_other, otherAccount, "Transferência", 100m);
        await _data.LinkPairAsync(mine, foreign);

        (await _repository.DeleteWithPairAsync(_user, mine, Day("2026-10-04"), CancellationToken.None)).ShouldBeTrue();

        (await _data.ScalarAsync<int>("SELECT count(*)::int FROM transacoes WHERE id = @foreign", new { foreign })).ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "004-76")]
    public async Task Delete_OfAnotherUsersRow_IsNotFound_AndItStays()
    {
        var id = await _data.InsertTransactionAsync(_user, _account);

        (await _repository.DeleteWithPairAsync(_other, id, Day("2026-10-04"), CancellationToken.None)).ShouldBeFalse();

        (await _data.ScalarAsync<int>("SELECT count(*)::int FROM transacoes")).ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "004-75")]
    public async Task Delete_WhenTheSecondDeleteFails_RollsTheFirstBack()
    {
        var second = await _data.InsertAccountAsync(_user, "Poupança");
        var first = await _data.InsertTransactionAsync(_user, _account, "Transferência", -100m, descricao: "perna-1");
        var other = await _data.InsertTransactionAsync(_user, second, "Transferência", 100m, descricao: "perna-2");
        await _data.LinkPairAsync(first, other);
        // Falha injetada: a linha principal (perna-1) não pode ser apagada.
        await _data.ExecuteAsync(
            """
            CREATE FUNCTION bloqueia_perna() RETURNS trigger AS $$ BEGIN
                IF OLD.descricao = 'perna-1' THEN RAISE EXCEPTION 'falha injetada'; END IF; RETURN OLD; END $$ LANGUAGE plpgsql;
            CREATE TRIGGER trg_bloqueia_perna BEFORE DELETE ON transacoes FOR EACH ROW EXECUTE FUNCTION bloqueia_perna();
            """);

        try
        {
            await Should.ThrowAsync<PostgresException>(
                () => _repository.DeleteWithPairAsync(_user, first, Day("2026-10-04"), CancellationToken.None));

            (await _data.ScalarAsync<int>("SELECT count(*)::int FROM transacoes")).ShouldBe(2);
        }
        finally
        {
            await _data.ExecuteAsync("DROP TRIGGER trg_bloqueia_perna ON transacoes; DROP FUNCTION bloqueia_perna();");
        }
    }

    [Fact]
    [Trait("AC", "004-77")]
    public async Task Delete_OfAnExpense_LeavesTheInvoiceAsItWas()
    {
        var card = await _data.InsertCardAsync(_user);
        var invoice = await _data.InsertInvoiceAsync(card, "2026-04-15", "2026-04-10", "Paga");
        var id = await _data.InsertTransactionAsync(_user, card, faturaId: invoice);

        (await _repository.DeleteWithPairAsync(_user, id, Day("2026-10-04"), CancellationToken.None)).ShouldBeTrue();

        (await _data.ScalarAsync<string>("SELECT status FROM faturas WHERE id = @invoice", new { invoice })).ShouldBe("Paga");
    }

    // ---------- desvio 070 ----------

    private async Task<(int Invoice, int Out, int In)> PaidInvoiceWithPayment(string due, string closing)
    {
        var card = await _data.InsertCardAsync(_user);
        var invoice = await _data.InsertInvoiceAsync(card, due, closing, "Paga");
        var outgoing = await _data.InsertTransactionAsync(_user, _account, "Pagamento Fatura", -500m, faturaId: invoice);
        var incoming = await _data.InsertTransactionAsync(_user, card, "Pagamento Fatura", 500m, faturaId: invoice);
        await _data.LinkPairAsync(outgoing, incoming);
        return (invoice, outgoing, incoming);
    }

    [Fact]
    [Trait("AC", "004-70")]
    public async Task Delete_OfTheOnlyInvoicePayment_ReopensTheClosedInvoice()
    {
        var past = await PaidInvoiceWithPayment("2026-09-15", "2026-09-10");

        (await _repository.DeleteWithPairAsync(_user, past.Out, Day("2026-10-04"), CancellationToken.None)).ShouldBeTrue();

        (await _data.ScalarAsync<string>("SELECT status FROM faturas WHERE id = @Invoice", new { past.Invoice })).ShouldBe("Fechada");
    }

    [Fact]
    [Trait("AC", "004-70")]
    public async Task Delete_OfThePayment_OfAnInvoiceThatHasNotClosed_ReopensItAsOpen()
    {
        var future = await PaidInvoiceWithPayment("2026-11-15", "2026-11-10");

        await _repository.DeleteWithPairAsync(_user, future.In, Day("2026-10-04"), CancellationToken.None);

        (await _data.ScalarAsync<string>("SELECT status FROM faturas WHERE id = @Invoice", new { future.Invoice })).ShouldBe("Aberta");
    }

    [Fact]
    [Trait("AC", "004-70")]
    public async Task Delete_OfOneOfTwoPayments_KeepsTheInvoicePaid()
    {
        var first = await PaidInvoiceWithPayment("2026-09-15", "2026-09-10");
        var secondOut = await _data.InsertTransactionAsync(_user, _account, "Pagamento Fatura", -100m, faturaId: first.Invoice);
        var card = await _data.ScalarAsync<int>("SELECT conta_id FROM faturas WHERE id = @Invoice", new { first.Invoice });
        var secondIn = await _data.InsertTransactionAsync(_user, card, "Pagamento Fatura", 100m, faturaId: first.Invoice);
        await _data.LinkPairAsync(secondOut, secondIn);

        await _repository.DeleteWithPairAsync(_user, first.Out, Day("2026-10-04"), CancellationToken.None);

        (await _data.ScalarAsync<string>("SELECT status FROM faturas WHERE id = @Invoice", new { first.Invoice })).ShouldBe("Paga");
    }

    [Fact]
    [Trait("AC", "004-70")]
    public async Task Delete_OfAPaymentWithoutInvoice_DoesNotTouchAnyInvoice()
    {
        var card = await _data.InsertCardAsync(_user);
        var invoice = await _data.InsertInvoiceAsync(card, "2026-09-15", "2026-09-10", "Paga");
        var id = await _data.InsertTransactionAsync(_user, _account, "Pagamento Fatura", -500m);

        (await _repository.DeleteWithPairAsync(_user, id, Day("2026-10-04"), CancellationToken.None)).ShouldBeTrue();

        (await _data.ScalarAsync<string>("SELECT status FROM faturas WHERE id = @invoice", new { invoice })).ShouldBe("Paga");
    }
}
