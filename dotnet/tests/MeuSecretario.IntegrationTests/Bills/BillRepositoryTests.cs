using MeuSecretario.Application.Bills;
using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.Bills;
using MeuSecretario.Infrastructure.Bills;
using MeuSecretario.IntegrationTests.Support;
using Npgsql;

namespace MeuSecretario.IntegrationTests.Bills;

/// <summary>SQL real da spec 005 (§3, §6): <c>ativo</c>, ordem, <c>LEFT JOIN</c>, releitura, <c>CHECK</c>, tenant, soma e reserva.</summary>
[Trait("Category", "Docker")]
public class BillRepositoryTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private readonly BillRepository _repository = new(postgres.Connections);
    private readonly BillsTestData _data = new(postgres.DataSource);
    private int _user;
    private int _account;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _user = await _data.InsertUserAsync();
        _account = await _data.InsertAccountAsync(_user);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly DateOnly Today = new(2026, 10, 4);

    private NewBillData New(
        string description = "Internet", decimal? amount = 120m, string type = "FIXO", string periodicity = "MENSAL", int day = 10,
        int? month = null, int notify = 3, bool include = false, int? installments = null, int? subcategory = null, int? account = null) =>
        new(description, amount, type, periodicity, day, month, notify, Today, include, installments,
            subcategory ?? BillsTestData.ExpenseSubcategory, account ?? _account);

    private static BillChanges Changes(
        Change<string> description = default, Change<string> type = default, Change<string> periodicity = default,
        Change<int> day = default, Change<int?> month = default, Change<decimal?> amount = default,
        Change<int> subcategory = default, Change<int> account = default, Change<int> notify = default,
        Change<DateOnly> start = default, Change<bool> include = default, Change<int?> installments = default,
        Change<int> executed = default) =>
        new(description, type, periodicity, day, month, amount, subcategory, account, notify, start, include, installments, executed);

    // ============================== listar ==============================

    [Fact]
    [Trait("AC", "005-01")]
    public async Task List_ReturnsTheNineteenFields_WithJoinedNamesAndDerivedGroup()
    {
        var rendaSub = await _data.GlobalSubcategoryOfGroupAsync("Renda");
        var id = await _data.InsertScheduleAsync(_user, _account, "Salário", valor: 3000.5m, subcategoryId: rendaSub, incluirNaReserva: true, dia: 5);

        var item = (await _repository.ListActiveAsync(_user, CancellationToken.None)).ShouldHaveSingleItem();

        item.Id.ShouldBe(id);
        item.Description.ShouldBe("Salário");
        item.Amount.ShouldBe(3000.5m);
        item.Type.ShouldBe("FIXO");
        item.Periodicity.ShouldBe("MENSAL");
        item.Day.ShouldBe(5);
        item.Month.ShouldBeNull();
        item.NotifyDaysBefore.ShouldBe(3);
        item.SubcategoryId.ShouldBe(rendaSub);
        item.SubcategoryName.ShouldNotBeNullOrEmpty();
        item.MacroName.ShouldNotBeNullOrEmpty();
        item.Group.ShouldBe("Renda");
        item.AccountId.ShouldBe(_account);
        item.AccountName.ShouldBe("Mercado pago");
        item.StartDate.ShouldBe(new DateOnly(2026, 1, 5));
        item.IncludeInReserve.ShouldBe(true);
        item.TotalInstallments.ShouldBeNull();
        item.ExecutedInstallments.ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "005-02")]
    public async Task List_IsOrderedByPeriodicityDayDescriptionThenId_ByBytes()
    {
        await _data.InsertScheduleAsync(_user, _account, "b", periodicidade: "SEMANAL", dia: 1);
        await _data.InsertScheduleAsync(_user, _account, "a", periodicidade: "MENSAL", dia: 20);
        await _data.InsertScheduleAsync(_user, _account, "c", periodicidade: "MENSAL", dia: 5);
        await _data.InsertScheduleAsync(_user, _account, "b", periodicidade: "MENSAL", dia: 5);
        await _data.InsertScheduleAsync(_user, _account, "b", periodicidade: "MENSAL", dia: 5);
        await _data.InsertScheduleAsync(_user, _account, "d", periodicidade: "DIARIA", dia: 9);
        await _data.InsertScheduleAsync(_user, _account, "y", periodicidade: "ANUAL", mes: 3, dia: 9);

        var items = await _repository.ListActiveAsync(_user, CancellationToken.None);

        items.Select(i => (i.Periodicity, i.Day, i.Description, i.Id)).ShouldBe(
        [
            ("ANUAL", 9, "y", 7), ("DIARIA", 9, "d", 6), ("MENSAL", 5, "b", 4), ("MENSAL", 5, "b", 5),
            ("MENSAL", 5, "c", 3), ("MENSAL", 20, "a", 2), ("SEMANAL", 1, "b", 1),
        ]);
    }

    [Fact]
    [Trait("AC", "005-03")]
    public async Task List_SkipsInactiveAndOtherUsers_ButKeepsBillsOfInactiveAccounts()
    {
        var other = await _data.InsertUserAsync("5511900000002", "Beto");
        var otherAccount = await _data.InsertAccountAsync(other, "Dele");
        var inactiveAccount = await _data.InsertAccountAsync(_user, "Velha", ativa: false);
        await _data.InsertScheduleAsync(_user, _account, "ativo");
        await _data.InsertScheduleAsync(_user, _account, "removido", ativo: false);
        await _data.InsertScheduleAsync(other, otherAccount, "alheio");
        await _data.InsertScheduleAsync(_user, inactiveAccount, "de conta inativa");

        var items = await _repository.ListActiveAsync(_user, CancellationToken.None);

        items.Select(i => i.Description).ShouldBe(["ativo", "de conta inativa"]);
    }

    [Fact]
    [Trait("AC", "005-05")]
    public async Task List_WithoutBills_IsEmpty()
    {
        (await _repository.ListActiveAsync(_user, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "005-06")]
    public async Task List_NullableColumnsComeBackNull_AndAmountKeepsCents()
    {
        await _data.InsertScheduleAsync(_user, _account, tipo: "LEMBRETE_VARIAVEL", valor: null);
        await _data.ExecuteAsync("UPDATE agendamentos SET incluir_na_reserva = NULL, notificar_antes_dias = NULL");
        var cents = await _data.InsertScheduleAsync(_user, _account, "centavos", valor: 1500.50m, dia: 6);

        var items = await _repository.ListActiveAsync(_user, CancellationToken.None);

        items[0].Amount.ShouldBeNull();
        items[0].IncludeInReserve.ShouldBeNull();
        items[0].NotifyDaysBefore.ShouldBeNull();
        items.Single(i => i.Id == cents).Amount.ShouldBe(1500.5m);
    }

    // ============================== criar ==============================

    [Fact]
    [Trait("AC", "005-10")]
    public async Task Create_InsertsActiveWithZeroExecuted_AndRereadsTheSixteenFields()
    {
        var created = await _repository.CreateAsync(_user, New(), CancellationToken.None);

        created.Id.ShouldBe(1);
        created.Description.ShouldBe("Internet");
        created.Amount.ShouldBe(120m);
        created.NotifyDaysBefore.ShouldBe(3);
        created.SubcategoryId.ShouldBe(BillsTestData.ExpenseSubcategory);
        created.SubcategoryName.ShouldNotBeNullOrEmpty();
        created.AccountName.ShouldBe("Mercado pago");
        created.StartDate.ShouldBe(Today);
        created.IncludeInReserve.ShouldBe(false);
        created.TotalInstallments.ShouldBeNull();
        created.ExecutedInstallments.ShouldBe(0);
        var row = (await _data.QueryAsync<(int User, bool Ativo)>("SELECT usuario_id, ativo FROM agendamentos WHERE id = 1")).Single();
        row.ShouldBe((_user, true));
    }

    [Fact]
    [Trait("AC", "005-19")]
    public async Task Create_Installment_StoresTotalWithZeroExecuted()
    {
        var created = await _repository.CreateAsync(_user, New(type: "PARCELADO", installments: 12), CancellationToken.None);

        created.TotalInstallments.ShouldBe(12);
        created.ExecutedInstallments.ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "005-20")]
    public async Task Create_AnnualWithoutMonth_ViolatesTheCheck()
    {
        var failure = await Should.ThrowAsync<PostgresException>(() =>
            _repository.CreateAsync(_user, New(periodicity: "ANUAL", month: null), CancellationToken.None));

        failure.ConstraintName.ShouldBe("chk_anual_mes");
        (await _data.ScalarAsync<int>("SELECT count(*)::int FROM agendamentos")).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "005-26")]
    public async Task Create_WithAMissingSubcategory_ViolatesTheForeignKey()
    {
        var failure = await Should.ThrowAsync<PostgresException>(() =>
            _repository.CreateAsync(_user, New(subcategory: 999999), CancellationToken.None));

        failure.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    [Trait("AC", "005-18")]
    public async Task Create_VariableReminderWithoutAmount_StoresNull_AndNegativeIsKept()
    {
        var none = await _repository.CreateAsync(_user, New(type: "LEMBRETE_VARIAVEL", amount: null), CancellationToken.None);
        var negative = await _repository.CreateAsync(_user, New(type: "LEMBRETE_VARIAVEL", amount: -5m), CancellationToken.None);

        none.Amount.ShouldBeNull();
        negative.Amount.ShouldBe(-5m);
    }

    // ============================== atualizar ==============================

    [Fact]
    [Trait("AC", "005-42")]
    public async Task Update_WritesOnlyTheSetFields_AndUpdatedAtAdvances()
    {
        var id = await _data.InsertScheduleAsync(_user, _account, "Antiga", valor: 100m, dia: 5);
        await _data.ExecuteAsync("UPDATE agendamentos SET updated_at = '2020-01-01' WHERE id = @id", new { id });

        var updated = await _repository.UpdateAsync(_user, id, Changes(description: Change.Set("Nova")), CancellationToken.None);

        updated.ShouldNotBeNull().Description.ShouldBe("Nova");
        updated.Amount.ShouldBe(100m);
        updated.Day.ShouldBe(5);
        (await _data.ScalarAsync<bool>("SELECT updated_at > '2020-01-01' FROM agendamentos WHERE id = @id", new { id })).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "005-46")]
    public async Task Update_ExplicitNull_WritesNull_AndZeroAndNegativeAreKept()
    {
        var id = await _data.InsertScheduleAsync(_user, _account, tipo: "LEMBRETE_VARIAVEL", valor: 100m);

        (await _repository.UpdateAsync(_user, id, Changes(amount: Change.Set<decimal?>(null)), CancellationToken.None))!.Amount.ShouldBeNull();
        (await _repository.UpdateAsync(_user, id, Changes(amount: Change.Set<decimal?>(-10m)), CancellationToken.None))!.Amount.ShouldBe(-10m);
        (await _repository.UpdateAsync(_user, id, Changes(amount: Change.Set<decimal?>(0m)), CancellationToken.None))!.Amount.ShouldBe(0m);
    }

    [Fact]
    [Trait("AC", "005-47")]
    public async Task Update_AccountAndSubcategory_ShowTheNewNamesInTheReread()
    {
        var id = await _data.InsertScheduleAsync(_user, _account);
        var other = await _data.InsertAccountAsync(_user, "Nubank");
        var subcategory = await _data.GlobalSubcategoryOfGroupAsync("Renda");

        var updated = await _repository.UpdateAsync(
            _user, id, Changes(account: Change.Set(other), subcategory: Change.Set(subcategory)), CancellationToken.None);

        updated!.AccountId.ShouldBe(other);
        updated.AccountName.ShouldBe("Nubank");
        updated.SubcategoryId.ShouldBe(subcategory);
    }

    [Fact]
    [Trait("AC", "005-56")]
    public async Task Update_AnnualToMonthly_WithoutClearingTheMonth_ViolatesTheCheck_ButWithTheAdjustmentPasses()
    {
        var id = await _data.InsertScheduleAsync(_user, _account, periodicidade: "ANUAL", mes: 3);

        var failure = await Should.ThrowAsync<PostgresException>(() =>
            _repository.UpdateAsync(_user, id, Changes(periodicity: Change.Set("MENSAL")), CancellationToken.None));
        failure.ConstraintName.ShouldBe("chk_anual_mes");

        var updated = await _repository.UpdateAsync(
            _user, id, Changes(periodicity: Change.Set("MENSAL"), month: Change.Set<int?>(null)), CancellationToken.None);
        updated!.Periodicity.ShouldBe("MENSAL");
        updated.Month.ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "005-56")]
    public async Task Update_LeavingInstallmentWithExecuted_ViolatesTheCheck_ButWithTheAdjustmentPasses()
    {
        var id = await _data.InsertScheduleAsync(_user, _account, tipo: "PARCELADO", totalParcelas: 6, parcelasExecutadas: 2);

        var failure = await Should.ThrowAsync<PostgresException>(() =>
            _repository.UpdateAsync(_user, id, Changes(type: Change.Set("FIXO"), installments: Change.Set<int?>(null)), CancellationToken.None));
        failure.ConstraintName.ShouldBe("chk_parcelado");

        var updated = await _repository.UpdateAsync(
            _user, id, Changes(type: Change.Set("FIXO"), installments: Change.Set<int?>(null), executed: Change.Set(0)), CancellationToken.None);
        updated!.Type.ShouldBe("FIXO");
        updated.TotalInstallments.ShouldBeNull();
        updated.ExecutedInstallments.ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "005-41")]
    public async Task Update_OfInactiveOrForeignBill_ReturnsNull_AndChangesNothing()
    {
        var other = await _data.InsertUserAsync("5511900000002", "Beto");
        var otherAccount = await _data.InsertAccountAsync(other, "Dele");
        var inactive = await _data.InsertScheduleAsync(_user, _account, "x", ativo: false);
        var foreign = await _data.InsertScheduleAsync(other, otherAccount, "alheio");

        (await _repository.UpdateAsync(_user, inactive, Changes(description: Change.Set("Y")), CancellationToken.None)).ShouldBeNull();
        (await _repository.UpdateAsync(_user, foreign, Changes(description: Change.Set("Y")), CancellationToken.None)).ShouldBeNull();
        (await _repository.UpdateAsync(_user, 999, Changes(description: Change.Set("Y")), CancellationToken.None)).ShouldBeNull();

        (await _data.ScalarAsync<string>("SELECT descricao FROM agendamentos WHERE id = @foreign", new { foreign })).ShouldBe("alheio");
        (await _data.ScalarAsync<string>("SELECT descricao FROM agendamentos WHERE id = @inactive", new { inactive })).ShouldBe("x");
    }

    [Fact]
    public async Task Update_WithNoSetField_ThrowsInsteadOfWritingNothing()
    {
        await Should.ThrowAsync<ArgumentException>(() => _repository.UpdateAsync(_user, 1, Changes(), CancellationToken.None));
    }

    [Fact]
    [Trait("AC", "005-52")]
    public async Task GetCurrent_ReadsTheStoredState_OnlyOfActiveBillsOfTheUser()
    {
        var id = await _data.InsertScheduleAsync(_user, _account, tipo: "PARCELADO", periodicidade: "ANUAL", mes: 3, valor: 50m, totalParcelas: 6, parcelasExecutadas: 2);
        var inactive = await _data.InsertScheduleAsync(_user, _account, ativo: false);
        var other = await _data.InsertUserAsync("5511900000002", "Beto");

        var current = await _repository.GetCurrentAsync(_user, id, CancellationToken.None);

        current.ShouldBe(new CurrentBill("PARCELADO", "ANUAL", 3, 50m, 6, 2));
        (await _repository.GetCurrentAsync(_user, inactive, CancellationToken.None)).ShouldBeNull();
        (await _repository.GetCurrentAsync(other, id, CancellationToken.None)).ShouldBeNull();
    }

    // ============================== remover ==============================

    [Fact]
    [Trait("AC", "005-70")]
    public async Task Deactivate_KeepsTheRow_AndAnswersFalseTheSecondTime()
    {
        var id = await _data.InsertScheduleAsync(_user, _account);

        (await _repository.DeactivateAsync(_user, id, CancellationToken.None)).ShouldBeTrue();
        (await _repository.DeactivateAsync(_user, id, CancellationToken.None)).ShouldBeFalse();

        (await _data.ScalarAsync<bool>("SELECT ativo FROM agendamentos WHERE id = @id", new { id })).ShouldBeFalse();
        (await _repository.ListActiveAsync(_user, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "005-71")]
    public async Task Deactivate_OfAnotherUsersBill_IsFalse_AndItStaysActive()
    {
        var other = await _data.InsertUserAsync("5511900000002", "Beto");
        var otherAccount = await _data.InsertAccountAsync(other, "Dele");
        var foreign = await _data.InsertScheduleAsync(other, otherAccount);

        (await _repository.DeactivateAsync(_user, foreign, CancellationToken.None)).ShouldBeFalse();

        (await _data.ScalarAsync<bool>("SELECT ativo FROM agendamentos WHERE id = @foreign", new { foreign })).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "005-72")]
    public async Task Deactivate_BillWithTransactions_KeepsTheirLink()
    {
        var id = await _data.InsertScheduleAsync(_user, _account);
        var transaction = await _data.Accounts.InsertTransactionAsync(_user, _account, -10m);
        await _data.ExecuteAsync("UPDATE transacoes SET agendamento_id = @id WHERE id = @transaction", new { id, transaction });

        (await _repository.DeactivateAsync(_user, id, CancellationToken.None)).ShouldBeTrue();

        (await _data.ScalarAsync<int>("SELECT agendamento_id FROM transacoes WHERE id = @transaction", new { transaction })).ShouldBe(id);
    }

    // ============================== existência ==============================

    [Fact]
    [Trait("AC", "005-24")]
    public async Task ActiveAccountOwned_IsFalseForInactiveForeignAndMissing()
    {
        var other = await _data.InsertUserAsync("5511900000002", "Beto");
        var foreign = await _data.InsertAccountAsync(other, "Dele");
        var inactive = await _data.InsertAccountAsync(_user, "Velha", ativa: false);

        (await _repository.ActiveAccountOwnedAsync(_user, _account, CancellationToken.None)).ShouldBeTrue();
        (await _repository.ActiveAccountOwnedAsync(_user, foreign, CancellationToken.None)).ShouldBeFalse();
        (await _repository.ActiveAccountOwnedAsync(_user, inactive, CancellationToken.None)).ShouldBeFalse();
        (await _repository.ActiveAccountOwnedAsync(_user, 999, CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "005-26")]
    public async Task SubcategoryVisible_IsTrueForGlobalAndOwn_FalseForForeignAndMissing()
    {
        var other = await _data.InsertUserAsync("5511900000002", "Beto");
        var own = await _data.InsertUserSubcategoryAsync(_user, "Minha");
        var foreign = await _data.InsertUserSubcategoryAsync(other, "Dele");

        (await _repository.SubcategoryVisibleAsync(_user, BillsTestData.ExpenseSubcategory, CancellationToken.None)).ShouldBeTrue();
        (await _repository.SubcategoryVisibleAsync(_user, own, CancellationToken.None)).ShouldBeTrue();
        (await _repository.SubcategoryVisibleAsync(_user, foreign, CancellationToken.None)).ShouldBeFalse();
        (await _repository.SubcategoryVisibleAsync(_user, 999999, CancellationToken.None)).ShouldBeFalse();
    }

    // ============================== resumo e reserva ==============================

    private async Task SeedSpecSummaryAsync(bool include)
    {
        var renda = await _data.GlobalSubcategoryOfGroupAsync("Renda");
        await _data.InsertScheduleAsync(_user, _account, "m", periodicidade: "MENSAL", valor: 100m, incluirNaReserva: include);
        await _data.InsertScheduleAsync(_user, _account, "s", periodicidade: "SEMANAL", valor: 10m, incluirNaReserva: include, tipo: "LEMBRETE_VARIAVEL");
        await _data.InsertScheduleAsync(_user, _account, "q", periodicidade: "QUINZENAL", valor: 50m, incluirNaReserva: include);
        await _data.InsertScheduleAsync(_user, _account, "d", periodicidade: "DIARIA", valor: 2m, incluirNaReserva: include);
        await _data.InsertScheduleAsync(_user, _account, "a", periodicidade: "ANUAL", mes: 3, valor: 1200m, incluirNaReserva: include);
        await _data.InsertScheduleAsync(_user, _account, "r", periodicidade: "MENSAL", valor: 3000m, subcategoryId: renda, incluirNaReserva: false);
    }

    [Fact]
    [Trait("AC", "005-90")]
    public async Task SummaryRows_SumByPeriodicityAndSide_InAFixedOrder()
    {
        await SeedSpecSummaryAsync(include: false);

        var rows = await _repository.ListSummaryRowsAsync(_user, CancellationToken.None);

        rows.ShouldBe(
        [
            new SummaryRow("ANUAL", false, 1200), new SummaryRow("DIARIA", false, 2), new SummaryRow("MENSAL", false, 100),
            new SummaryRow("MENSAL", true, 3000), new SummaryRow("QUINZENAL", false, 50), new SummaryRow("SEMANAL", false, 10),
        ]);
    }

    [Fact]
    [Trait("AC", "005-91")]
    public async Task SummaryRows_LeaveOutNullZeroNegativeInactiveAndOtherUsers()
    {
        var other = await _data.InsertUserAsync("5511900000002", "Beto");
        var otherAccount = await _data.InsertAccountAsync(other, "Dele");
        await _data.InsertScheduleAsync(_user, _account, "nulo", tipo: "LEMBRETE_VARIAVEL", valor: null);
        await _data.InsertScheduleAsync(_user, _account, "zero", tipo: "LEMBRETE_VARIAVEL", valor: 0m);
        await _data.InsertScheduleAsync(_user, _account, "negativo", tipo: "LEMBRETE_VARIAVEL", valor: -5m);
        await _data.InsertScheduleAsync(_user, _account, "inativo", valor: 77m, ativo: false);
        await _data.InsertScheduleAsync(other, otherAccount, "alheio", valor: 88m);
        await _data.InsertScheduleAsync(_user, _account, "ok", valor: 10m);

        var rows = await _repository.ListSummaryRowsAsync(_user, CancellationToken.None);

        rows.ShouldBe([new SummaryRow("MENSAL", false, 10)]);
    }

    [Fact]
    [Trait("AC", "005-91")]
    public async Task SummaryRows_ReceitasGerais_CountsAsIncome_ButOtherGeneralDoesNot()
    {
        await _data.InsertScheduleAsync(_user, _account, "geral receita", valor: 40m, subcategoryId: BillsTestData.GeneralIncomeSubcategory);
        await _data.InsertScheduleAsync(_user, _account, "geral despesa", valor: 5m, subcategoryId: BillsTestData.ExpenseSubcategory);

        var rows = await _repository.ListSummaryRowsAsync(_user, CancellationToken.None);

        rows.ShouldBe([new SummaryRow("MENSAL", false, 5), new SummaryRow("MENSAL", true, 40)]);
    }

    [Fact]
    [Trait("AC", "005-93")]
    public async Task ReserveTotals_OnlyMarkedFixedAndVariable_ByPeriodicity()
    {
        await SeedSpecSummaryAsync(include: true);

        var totals = await _repository.ListReserveTotalsAsync(_user, CancellationToken.None);

        totals.ShouldBe(
        [
            new PeriodTotal("ANUAL", 1200), new PeriodTotal("DIARIA", 2), new PeriodTotal("MENSAL", 100),
            new PeriodTotal("QUINZENAL", 50), new PeriodTotal("SEMANAL", 10),
        ]);
    }

    [Fact]
    [Trait("AC", "005-94")]
    public async Task ReserveTotals_LeaveOutInstallmentsUnmarkedNullInactiveForeignAndIncome()
    {
        var other = await _data.InsertUserAsync("5511900000002", "Beto");
        var otherAccount = await _data.InsertAccountAsync(other, "Dele");
        var renda = await _data.GlobalSubcategoryOfGroupAsync("Renda");
        await _data.InsertScheduleAsync(_user, _account, "parcelado", tipo: "PARCELADO", totalParcelas: 6, valor: 500m, incluirNaReserva: true);
        await _data.InsertScheduleAsync(_user, _account, "desmarcado", valor: 11m, incluirNaReserva: false);
        await _data.InsertScheduleAsync(_user, _account, "nulo", valor: 12m, incluirNaReserva: null);
        await _data.InsertScheduleAsync(_user, _account, "inativo", valor: 13m, incluirNaReserva: true, ativo: false);
        await _data.InsertScheduleAsync(other, otherAccount, "alheio", valor: 14m, incluirNaReserva: true);
        await _data.InsertScheduleAsync(_user, _account, "receita", valor: 15m, incluirNaReserva: true, subcategoryId: renda);
        await _data.InsertScheduleAsync(_user, _account, "receita geral", valor: 16m, incluirNaReserva: true, subcategoryId: BillsTestData.GeneralIncomeSubcategory);
        await _data.InsertScheduleAsync(_user, _account, "vale", valor: 7m, incluirNaReserva: true);

        var totals = await _repository.ListReserveTotalsAsync(_user, CancellationToken.None);

        totals.ShouldBe([new PeriodTotal("MENSAL", 7)]);
    }

    [Fact]
    [Trait("AC", "005-102")]
    public async Task ReserveTotals_NegativeIsSummed_NullIsNot()
    {
        await _data.InsertScheduleAsync(_user, _account, "neg", tipo: "LEMBRETE_VARIAVEL", valor: -10m, incluirNaReserva: true);
        await _data.InsertScheduleAsync(_user, _account, "nulo", tipo: "LEMBRETE_VARIAVEL", valor: null, incluirNaReserva: true);

        (await _repository.ListReserveTotalsAsync(_user, CancellationToken.None)).ShouldBe([new PeriodTotal("MENSAL", -10)]);
    }

    [Fact]
    [Trait("AC", "005-101")]
    public async Task ReserveItems_AreTheMarkedActiveFixedAndVariable_WithTheSubcategoryName()
    {
        var included = await _data.InsertScheduleAsync(_user, _account, "Aluguel", valor: 100m, incluirNaReserva: true);
        await _data.InsertScheduleAsync(_user, _account, "Parcela", tipo: "PARCELADO", totalParcelas: 3, incluirNaReserva: true);
        await _data.InsertScheduleAsync(_user, _account, "Fora", incluirNaReserva: false);
        await _data.InsertScheduleAsync(_user, _account, "Sem valor", tipo: "LEMBRETE_VARIAVEL", valor: null, incluirNaReserva: true);

        var items = await _repository.ListReserveItemsAsync(_user, CancellationToken.None);

        var item = items.ShouldHaveSingleItem();
        item.ShouldBe(new ReserveItemRow(included, "Aluguel", 100m, "MENSAL", item.Category));
        item.Category.ShouldNotBeNullOrEmpty();
    }

    [Theory]
    [Trait("AC", "005-92")]
    [InlineData(null, 6)]
    [InlineData(12, 12)]
    [InlineData(0, 0)]
    public async Task ReserveMonths_DefaultsToSixWhenNull(int? configured, int expected)
    {
        var user = await _data.InsertUserAsync("5511900000003", "Carla", mesesReserva: configured, nullMonths: configured is null);

        (await _repository.GetReserveMonthsAsync(user, CancellationToken.None)).ShouldBe(expected);
    }

    [Fact]
    [Trait("AC", "005-92")]
    public async Task ReserveMonths_UserWithoutRow_IsSix()
    {
        (await _repository.GetReserveMonthsAsync(999999, CancellationToken.None)).ShouldBe(6);
    }

    [Fact]
    [Trait("AC", "005-01")]
    public async Task NumericToDouble_EqualsPythonFloatOfDecimalForTwoPlaces()
    {
        // (double)decimal == float(Decimal) para valores de 2 casas (plan §3).
        foreach (var text in new[] { "0.10", "0.30", "1500.50", "19.99", "2143.30", "12345678.07" })
        {
            var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            ((double)value).ShouldBe(double.Parse(text, System.Globalization.CultureInfo.InvariantCulture));
        }

        await _data.InsertScheduleAsync(_user, _account, valor: 0.30m);
        var row = (await _repository.ListSummaryRowsAsync(_user, CancellationToken.None)).Single();
        row.Total.ShouldBe(0.3);
    }
}
