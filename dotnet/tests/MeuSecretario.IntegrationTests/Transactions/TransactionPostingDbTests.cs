using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Domain.Transactions;
using MeuSecretario.Infrastructure.Categories;
using MeuSecretario.Infrastructure.Transactions;
using MeuSecretario.IntegrationTests.Support;
using Npgsql;
using static MeuSecretario.IntegrationTests.Transactions.TransactionsTestData;

namespace MeuSecretario.IntegrationTests.Transactions;

/// <summary>Os 4 serviços de lançamento e o <c>EnsureCurrent</c> contra o banco real (spec 004 §7).</summary>
[Trait("Category", "Docker")]
public class TransactionPostingDbTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private readonly TransactionsTestData _data = new(postgres.DataSource);
    private readonly TransactionsUnitOfWork _unitOfWork = new(postgres.Connections);
    private TransactionPosting _posting = default!;
    private int _user;
    private int _other;
    private int _checking;
    private int _wallet;
    private int _card;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _posting = new TransactionPosting(_unitOfWork, new CategoryRepository(postgres.Connections));
        _user = await _data.InsertUserAsync("5511900000001", "Ana");
        _other = await _data.InsertUserAsync("5511900000002", "Beto");
        _checking = await _data.InsertAccountAsync(_user, "Conta Corrente");
        _wallet = await _data.InsertAccountAsync(_user, "Carteira", "Dinheiro");
        _card = await _data.InsertCardAsync(_user, "Nubank", 10, 15);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private PostSimpleTransaction Simple(
        TransactionKind kind = TransactionKind.Expense, decimal amount = 50m, int? account = null, string date = "2026-03-20", int subcategory = ExpenseSubcategory) =>
        new(_user, account ?? _checking, subcategory, kind, amount, "Mercado", Day(date), Origin: "bot");

    private static T Ok<T>(PostingResult<T> result) => result.ShouldBeOfType<PostingResult<T>.Ok>().Value;

    private static PostingError Failure<T>(PostingResult<T> result) => result.ShouldBeOfType<PostingResult<T>.Failed>().Error;

    private Task<int> Count(string table) => _data.ScalarAsync<int>($"SELECT count(*)::int FROM {table}");

    // ---------- simples ----------

    [Fact]
    [Trait("AC", "004-110")]
    public async Task Simple_Expense_IsStoredNegativeWithOriginAndNoInvoice()
    {
        var posted = Ok(await _posting.PostSimpleAsync(Simple(), CancellationToken.None));

        posted.StoredAmount.ShouldBe(-50m);
        var row = (await _data.QueryAsync<(decimal Valor, string Tipo, int? Fatura, string? Origem, bool Consolidada, string? Obs)>(
            "SELECT valor, tipo_transacao, fatura_id, origem, consolidada, observacoes FROM transacoes WHERE id = @id",
            new { id = posted.TransactionId })).Single();
        row.ShouldBe((-50m, "Despesa", null, "bot", true, null));
    }

    [Fact]
    [Trait("AC", "004-111")]
    public async Task Simple_Income_IsStoredPositiveAsRenda()
    {
        var posted = Ok(await _posting.PostSimpleAsync(Simple(TransactionKind.Income, 3000m, subcategory: IncomeSubcategory), CancellationToken.None));

        (await _data.ScalarAsync<string>("SELECT tipo_transacao FROM transacoes WHERE id = @id", new { id = posted.TransactionId })).ShouldBe("Renda");
        posted.StoredAmount.ShouldBe(3000m);
    }

    [Fact]
    [Trait("AC", "004-16")]
    public async Task Simple_StoredAmountIsRoundedByTheDatabase()
    {
        Ok(await _posting.PostSimpleAsync(Simple(amount: 10.005m), CancellationToken.None)).StoredAmount.ShouldBe(-10.01m);
    }

    [Fact]
    [Trait("AC", "004-23")]
    public async Task Simple_ExpenseOnACard_CreatesTheInvoiceOnce_AndReusesIt()
    {
        var first = Ok(await _posting.PostSimpleAsync(Simple(account: _card), CancellationToken.None));
        var second = Ok(await _posting.PostSimpleAsync(Simple(account: _card, date: "2026-03-25"), CancellationToken.None));

        second.InvoiceId.ShouldBe(first.InvoiceId);
        first.InvoiceDueDate.ShouldBe(Day("2026-04-15"));
        var invoice = (await _data.QueryAsync<(DateTime Due, DateTime Closing, string Status)>(
            "SELECT data_vencimento, data_fechamento, status FROM faturas")).Single();
        (DateOnly.FromDateTime(invoice.Due), DateOnly.FromDateTime(invoice.Closing), invoice.Status)
            .ShouldBe((Day("2026-04-15"), Day("2026-04-10"), "Aberta"));
    }

    [Fact]
    [Trait("AC", "004-24")]
    public async Task Simple_IncomeOnACard_DoesNotLink()
    {
        Ok(await _posting.PostSimpleAsync(Simple(TransactionKind.Income, account: _card, subcategory: IncomeSubcategory), CancellationToken.None))
            .InvoiceId.ShouldBeNull();

        (await Count("faturas")).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "004-113")]
    public async Task Simple_WithAnInvalidSubcategory_DoesNotLeaveTheInvoice()
    {
        Failure(await _posting.PostSimpleAsync(Simple(account: _card, subcategory: 99999), CancellationToken.None))
            .ShouldBe(PostingError.SubcategoryNotFound);

        (await Count("faturas")).ShouldBe(0);
        (await Count("transacoes")).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "004-113")]
    public async Task Simple_Tenant_InactiveAndForeignAccountsAreNotFound()
    {
        var inactive = await _data.InsertAccountAsync(_user, "Velha", ativa: false);
        var foreign = await _data.InsertAccountAsync(_other, "Alheia");

        Failure(await _posting.PostSimpleAsync(Simple(account: inactive), CancellationToken.None)).ShouldBe(PostingError.AccountNotFound);
        Failure(await _posting.PostSimpleAsync(Simple(account: foreign), CancellationToken.None)).ShouldBe(PostingError.AccountNotFound);
        (await Count("transacoes")).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "004-114")]
    public async Task Simple_NotesConsolidatedAndSchedule_AreStored()
    {
        var schedule = await _data.ScalarAsync<int>(
            """
            INSERT INTO agendamentos (usuario_id, conta_id, subcategoria_id, descricao, tipo_agendamento, periodicidade, data_inicio, dia_execucao)
            VALUES (@_user, @_checking, 59, 'x', 'FIXO', 'MENSAL', CURRENT_DATE, 5) RETURNING id
            """,
            new { _user, _checking });

        var posted = Ok(await _posting.PostSimpleAsync(
            Simple() with { Notes = "obs", Consolidated = false, ScheduleId = schedule }, CancellationToken.None));

        var row = (await _data.QueryAsync<(string? Obs, bool Consolidada, int? Agendamento)>(
            "SELECT observacoes, consolidada, agendamento_id FROM transacoes WHERE id = @id", new { id = posted.TransactionId })).Single();
        row.ShouldBe(("obs", false, schedule));
    }

    [Fact]
    [Trait("AC", "004-26")]
    public async Task Simple_TwoSimultaneousPurchasesOnTheSameNewCycle_ShareOneInvoice()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            _posting.PostSimpleAsync(Simple(account: _card), CancellationToken.None)));

        results.Select(r => Ok(r).InvoiceId).Distinct().Count().ShouldBe(1);
        (await Count("faturas")).ShouldBe(1);
        (await Count("transacoes")).ShouldBe(8);
    }

    [Fact]
    [Trait("AC", "004-25")]
    public async Task Simple_WhenTheInsertFailsAfterTheInvoice_TheInvoiceIsNotLeftBehind()
    {
        // O CHECK de tipo não pode ser quebrado por valores válidos; um trigger de teste faz o INSERT falhar.
        await _data.ExecuteAsync(
            """
            CREATE FUNCTION falha_insert() RETURNS trigger AS $$ BEGIN RAISE EXCEPTION 'falha injetada'; END $$ LANGUAGE plpgsql;
            CREATE TRIGGER trg_falha_insert BEFORE INSERT ON transacoes FOR EACH ROW EXECUTE FUNCTION falha_insert();
            """);
        try
        {
            await Should.ThrowAsync<PostgresException>(() => _posting.PostSimpleAsync(Simple(account: _card), CancellationToken.None));

            (await Count("faturas")).ShouldBe(0);
        }
        finally
        {
            await _data.ExecuteAsync("DROP TRIGGER trg_falha_insert ON transacoes; DROP FUNCTION falha_insert();");
        }
    }

    // ---------- transferência ----------

    [Fact]
    [Trait("AC", "004-115")]
    public async Task Transfer_WritesTwoLinkedLegs()
    {
        var posted = Ok(await _posting.PostTransferAsync(new PostTransfer(_user, _wallet, _checking, 100m, Day("2026-03-20")), CancellationToken.None));

        posted.FromAccountName.ShouldBe("Carteira");
        posted.ToAccountName.ShouldBe("Conta Corrente");
        var rows = await _data.QueryAsync<(int Id, int Conta, decimal Valor, string Desc, string Tipo, int Sub, int? Par, int? Fatura, bool Cons, string? Origem)>(
            "SELECT id, conta_id, valor, descricao, tipo_transacao, subcategoria_id, transferencia_par_id, fatura_id, consolidada, origem FROM transacoes ORDER BY id");
        rows.ShouldBe(
        [
            (posted.OutgoingTransactionId, _wallet, -100m, "Transferência para Conta Corrente", "Transferência", 28, posted.IncomingTransactionId, null, true, null),
            (posted.IncomingTransactionId, _checking, 100m, "Transferência de Carteira", "Transferência", 28, posted.OutgoingTransactionId, null, true, null),
        ]);
    }

    [Fact]
    [Trait("AC", "004-116")]
    public async Task Transfer_Errors_AndTheSmallestIdWinsWithDuplicatedNames()
    {
        Failure(await _posting.PostTransferAsync(new PostTransfer(_user, _wallet, _wallet, 1m, Day("2026-03-20")), CancellationToken.None))
            .ShouldBe(PostingError.SameAccount);
        var foreign = await _data.InsertAccountAsync(_other, "Alheia");
        Failure(await _posting.PostTransferAsync(new PostTransfer(_user, foreign, _wallet, 1m, Day("2026-03-20")), CancellationToken.None))
            .ShouldBe(PostingError.AccountNotFound);
        Failure(await _posting.PostTransferAsync(new PostTransfer(_user, _wallet, foreign, 1m, Day("2026-03-20")), CancellationToken.None))
            .ShouldBe(PostingError.DestinationAccountNotFound);

        await _data.ExecuteAsync("INSERT INTO subcategoria (macro_id, nome_sub) SELECT macro_id, nome_sub FROM subcategoria WHERE id = 28");
        Ok(await _posting.PostTransferAsync(new PostTransfer(_user, _wallet, _checking, 1m, Day("2026-03-20")), CancellationToken.None));
        (await _data.ScalarAsync<int>("SELECT DISTINCT subcategoria_id FROM transacoes")).ShouldBe(28);
    }

    [Fact]
    [Trait("AC", "004-116")]
    public async Task Transfer_WithoutTheSeededCategory_FailsAndWritesNothing()
    {
        await _data.ExecuteAsync("DELETE FROM subcategoria WHERE id = 28");

        Failure(await _posting.PostTransferAsync(new PostTransfer(_user, _wallet, _checking, 1m, Day("2026-03-20")), CancellationToken.None))
            .ShouldBe(PostingError.RequiredCategoryMissing);

        (await Count("transacoes")).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "004-117")]
    public async Task Transfer_WhenTheSecondLegFails_NoLegStays()
    {
        await _data.ExecuteAsync(
            """
            CREATE FUNCTION falha_entrada() RETURNS trigger AS $$ BEGIN
                IF NEW.valor > 0 THEN RAISE EXCEPTION 'falha injetada'; END IF; RETURN NEW; END $$ LANGUAGE plpgsql;
            CREATE TRIGGER trg_falha_entrada BEFORE INSERT ON transacoes FOR EACH ROW EXECUTE FUNCTION falha_entrada();
            """);
        try
        {
            await Should.ThrowAsync<PostgresException>(() =>
                _posting.PostTransferAsync(new PostTransfer(_user, _wallet, _checking, 100m, Day("2026-03-20")), CancellationToken.None));

            (await Count("transacoes")).ShouldBe(0);
        }
        finally
        {
            await _data.ExecuteAsync("DROP TRIGGER trg_falha_entrada ON transacoes; DROP FUNCTION falha_entrada();");
        }
    }

    // ---------- pagamento de fatura ----------

    [Fact]
    [Trait("AC", "004-118")]
    public async Task Payment_PaysTheOldestClosedInvoice_AndLeavesTheOpenOne()
    {
        var feb = await _data.InsertInvoiceAsync(_card, "2026-02-10", "2026-02-05", "Fechada");
        await _data.InsertInvoiceAsync(_card, "2026-03-10", "2026-03-05", "Fechada");
        var open = await _data.InsertInvoiceAsync(_card, "2026-04-10", "2026-04-05");

        var posted = Ok(await _posting.PostInvoicePaymentAsync(
            new PostInvoicePayment(_user, _checking, _card, 800m, Day("2026-03-20")), CancellationToken.None));

        posted.InvoiceId.ShouldBe(feb);
        posted.InvoiceMarkedPaid.ShouldBeTrue();
        (await _data.ScalarAsync<string>("SELECT status FROM faturas WHERE id = @feb", new { feb })).ShouldBe("Paga");
        (await _data.ScalarAsync<string>("SELECT status FROM faturas WHERE id = @open", new { open })).ShouldBe("Aberta");
        var legs = await _data.QueryAsync<(int Conta, decimal Valor, string Desc, string Tipo, int Sub, int? Fatura, int? Par)>(
            "SELECT conta_id, valor, descricao, tipo_transacao, subcategoria_id, fatura_id, transferencia_par_id FROM transacoes ORDER BY id");
        legs.ShouldBe(
        [
            (_checking, -800m, "Pagamento Fatura Nubank", "Pagamento Fatura", 29, feb, posted.IncomingTransactionId),
            (_card, 800m, "Pagamento Recebido (de Conta Corrente)", "Pagamento Fatura", 29, feb, posted.OutgoingTransactionId),
        ]);
    }

    [Fact]
    [Trait("AC", "004-119")]
    public async Task Payment_WithoutAClosedInvoice_PaysTheCycleInvoice_CreatedIfMissing()
    {
        var posted = Ok(await _posting.PostInvoicePaymentAsync(
            new PostInvoicePayment(_user, _checking, _card, 1m, Day("2026-03-20")), CancellationToken.None));

        posted.InvoiceDueDate.ShouldBe(Day("2026-04-15"));
        (await _data.ScalarAsync<string>("SELECT status FROM faturas WHERE id = @id", new { id = posted.InvoiceId })).ShouldBe("Paga");
    }

    [Fact]
    [Trait("AC", "004-122")]
    public async Task Payment_ToANonCard_IsRejected_AndNothingIsWritten()
    {
        Failure(await _posting.PostInvoicePaymentAsync(
            new PostInvoicePayment(_user, _checking, _wallet, 1m, Day("2026-03-20")), CancellationToken.None)).ShouldBe(PostingError.NotACreditCard);

        (await Count("transacoes")).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "004-124")]
    public async Task Payment_NeverPaysAnotherCardsOrAnotherUsersInvoice()
    {
        var secondCard = await _data.InsertCardAsync(_user, "Itaú", 5, 20);
        var siblingClosed = await _data.InsertInvoiceAsync(secondCard, "2026-01-20", "2026-01-05", "Fechada");
        var foreignCard = await _data.InsertCardAsync(_other, "Alheio", 10, 15);
        var foreignClosed = await _data.InsertInvoiceAsync(foreignCard, "2026-01-15", "2026-01-10", "Fechada");

        var posted = Ok(await _posting.PostInvoicePaymentAsync(
            new PostInvoicePayment(_user, _checking, _card, 5m, Day("2026-03-20")), CancellationToken.None));

        posted.InvoiceId.ShouldNotBe(siblingClosed);
        (await _data.ScalarAsync<string>("SELECT status FROM faturas WHERE id = @siblingClosed", new { siblingClosed })).ShouldBe("Fechada");
        (await _data.ScalarAsync<string>("SELECT status FROM faturas WHERE id = @foreignClosed", new { foreignClosed })).ShouldBe("Fechada");
        Failure(await _posting.PostInvoicePaymentAsync(
            new PostInvoicePayment(_user, _checking, foreignCard, 5m, Day("2026-03-20")), CancellationToken.None))
            .ShouldBe(PostingError.DestinationAccountNotFound);
    }

    // ---------- parcelada ----------

    [Fact]
    [Trait("AC", "004-130")]
    public async Task Installments_PostTheFirstOnTheInvoice_AndScheduleTheRest()
    {
        var posted = Ok(await _posting.PostInstallmentPurchaseAsync(
            new PostInstallmentPurchase(_user, _card, ExpenseSubcategory, 33.33m, 3, "Geladeira", Day("2026-01-31")), CancellationToken.None));

        var tx = (await _data.QueryAsync<(decimal Valor, DateTime Data, int? Fatura, int? Agendamento, string Desc)>(
            "SELECT valor, data_transacao, fatura_id, agendamento_id, descricao FROM transacoes WHERE id = @id", new { id = posted.TransactionId })).Single();
        tx.Valor.ShouldBe(-33.33m);
        DateOnly.FromDateTime(tx.Data).ShouldBe(Day("2026-01-31"));
        tx.Fatura.ShouldBe(posted.InvoiceId);
        tx.Agendamento.ShouldBeNull();
        tx.Desc.ShouldBe("Geladeira");

        var schedule = (await _data.QueryAsync<(string Tipo, string Per, decimal? Valor, DateTime Inicio, int Dia, int Total, int Exec, bool Ativo, int Conta, int Sub, bool Reserva, int Notificar, int? Mes)>(
            """
            SELECT tipo_agendamento, periodicidade, valor_previsto, data_inicio, dia_execucao, total_parcelas, parcelas_executadas, ativo,
                   conta_id, subcategoria_id, incluir_na_reserva, notificar_antes_dias, mes_execucao
            FROM agendamentos WHERE id = @id
            """,
            new { id = posted.ScheduleId })).Single();
        (schedule.Tipo, schedule.Per, schedule.Valor, DateOnly.FromDateTime(schedule.Inicio), schedule.Dia, schedule.Total, schedule.Exec, schedule.Ativo)
            .ShouldBe(("PARCELADO", "MENSAL", 33.33m, Day("2026-02-28"), 31, 3, 1, true));
        (schedule.Conta, schedule.Sub, schedule.Reserva, schedule.Notificar, schedule.Mes).ShouldBe((_card, ExpenseSubcategory, true, 3, null));
    }

    [Fact]
    [Trait("AC", "004-132")]
    public async Task Installments_WhenTheScheduleFails_TheTransactionIsNotLeftBehind()
    {
        await _data.ExecuteAsync(
            """
            CREATE FUNCTION falha_agendamento() RETURNS trigger AS $$ BEGIN RAISE EXCEPTION 'falha injetada'; END $$ LANGUAGE plpgsql;
            CREATE TRIGGER trg_falha_agendamento BEFORE INSERT ON agendamentos FOR EACH ROW EXECUTE FUNCTION falha_agendamento();
            """);
        try
        {
            await Should.ThrowAsync<PostgresException>(() => _posting.PostInstallmentPurchaseAsync(
                new PostInstallmentPurchase(_user, _card, ExpenseSubcategory, 10m, 3, "x", Day("2026-03-20")), CancellationToken.None));

            (await Count("transacoes")).ShouldBe(0);
            (await Count("faturas")).ShouldBe(0);
        }
        finally
        {
            await _data.ExecuteAsync("DROP TRIGGER trg_falha_agendamento ON agendamentos; DROP FUNCTION falha_agendamento();");
        }
    }

    [Fact]
    [Trait("AC", "004-133")]
    public async Task Installments_OnANonCard_HaveNoInvoice()
    {
        var posted = Ok(await _posting.PostInstallmentPurchaseAsync(
            new PostInstallmentPurchase(_user, _wallet, ExpenseSubcategory, 10m, 2, "x", Day("2026-03-20")), CancellationToken.None));

        posted.InvoiceId.ShouldBeNull();
        (await Count("agendamentos")).ShouldBe(1);
    }

    // ---------- sessão ----------

    [Fact]
    [Trait("AC", "004-26")]
    public async Task Session_InsertInvoiceIfMissing_ReturnsNullWhenItAlreadyExists()
    {
        await using var first = await _unitOfWork.BeginAsync(CancellationToken.None);
        (await first.InsertInvoiceIfMissingAsync(_card, Day("2026-04-15"), Day("2026-04-10"), CancellationToken.None)).ShouldNotBeNull();
        await first.CommitAsync(CancellationToken.None);

        await using var second = await _unitOfWork.BeginAsync(CancellationToken.None);
        (await second.InsertInvoiceIfMissingAsync(_card, Day("2026-04-15"), Day("2026-04-10"), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "004-118")]
    public async Task Session_FindOldestClosedInvoice_OrdersByDueDateThenId()
    {
        await _data.InsertInvoiceAsync(_card, "2026-05-10", "2026-05-05", "Fechada");
        var oldest = await _data.InsertInvoiceAsync(_card, "2026-02-10", "2026-02-05", "Fechada");
        await _data.InsertInvoiceAsync(_card, "2026-01-10", "2026-01-05", "Paga");

        await using var session = await _unitOfWork.BeginAsync(CancellationToken.None);

        (await session.FindOldestClosedInvoiceAsync(_card, CancellationToken.None))!.Id.ShouldBe(oldest);
    }

    [Fact]
    public async Task Session_WithoutCommit_RollsBack()
    {
        await using (var session = await _unitOfWork.BeginAsync(CancellationToken.None))
        {
            await session.InsertInvoiceIfMissingAsync(_card, Day("2026-04-15"), Day("2026-04-10"), CancellationToken.None);
        }

        (await Count("faturas")).ShouldBe(0);
    }

    // ---------- EnsureCurrent ----------

    [Fact]
    [Trait("AC", "004-95")]
    public async Task EnsureCurrent_CreatesTheInvoiceOfTodaysCycle_ForActiveCardsOfTheUserOnly()
    {
        var populator = new RecordingPopulator();
        var service = new InvoiceService(_unitOfWork, populator, new FixedClock(Day("2026-03-20")), new SilentDiagnostics());
        await _data.InsertCardAsync(_user, "Inativo", 10, 15, ativa: false);
        await _data.InsertCardAsync(_other, "Alheio", 10, 15);

        await service.EnsureCurrentAsync(_user, null, CancellationToken.None);
        await service.EnsureCurrentAsync(_user, null, CancellationToken.None);   // segunda vez: nada novo

        var invoices = await _data.QueryAsync<(int Id, int Conta, DateTime Due, string Status)>("SELECT id, conta_id, data_vencimento, status FROM faturas");
        var single = invoices.ShouldHaveSingleItem();
        single.Conta.ShouldBe(_card);
        DateOnly.FromDateTime(single.Due).ShouldBe(Day("2026-04-15"));
        single.Status.ShouldBe("Aberta");
        populator.Calls.ShouldHaveSingleItem().ShouldBe((single.Id, _card, Day("2026-04-15")));
    }

    private sealed class FixedClock(DateOnly today) : IClock
    {
        public DateTimeOffset UtcNow => new(today.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero);

        public DateTime Now => today.ToDateTime(new TimeOnly(12, 0));

        public DateOnly Today => today;

        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
    }

    private sealed class RecordingPopulator : IInvoiceSchedulePopulator
    {
        public List<(int InvoiceId, int CardId, DateOnly Due)> Calls { get; } = [];

        public Task PopulateAsync(int invoiceId, int cardAccountId, DateOnly dueDate, CancellationToken cancellationToken)
        {
            Calls.Add((invoiceId, cardAccountId, dueDate));
            return Task.CompletedTask;
        }
    }

    private sealed class SilentDiagnostics : ITransactionsDiagnostics
    {
        public void PopulateFailed(int invoiceId, string errorType)
        {
        }
    }
}
