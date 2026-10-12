using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Bills;
using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.Bills;
using MeuSecretario.UnitTests.Domain.Accounts;
using MeuSecretario.UnitTests.Domain.Bills;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Bills;

internal static class BillSamples
{
    public static BillDetails Details(int id = 41) =>
        new(id, "Internet", 120m, "FIXO", "MENSAL", 10, null, 3, 12, "Internet", 3, "Mercado pago",
            new DateOnly(2026, 10, 4), false, null, 0);

    public static CurrentBill Current(string type = "FIXO") => new(type, "MENSAL", null, 100m, null, 0);

    public static IClock ClockAt(DateOnly today)
    {
        var clock = Substitute.For<IClock>();
        clock.Today.Returns(today);
        return clock;
    }

    public static IBillRepository RepositoryWithOwnedAccountAndSubcategory()
    {
        var repository = Substitute.For<IBillRepository>();
        repository.ActiveAccountOwnedAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(true);
        repository.SubcategoryVisibleAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(true);
        return repository;
    }
}

public class ListBillsTests
{
    [Fact]
    [Trait("AC", "005-01")]
    public async Task ReturnsWhatTheRepositoryListsForTheUser()
    {
        var repository = Substitute.For<IBillRepository>();
        IReadOnlyList<BillListItem> items = [];
        repository.ListActiveAsync(42, Arg.Any<CancellationToken>()).Returns(items);

        (await new ListBills(repository).ExecuteAsync(42, CancellationToken.None)).ShouldBe(items);
    }
}

public class DeleteBillTests
{
    [Theory]
    [Trait("AC", "005-70")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnswersWhatTheSingleStatementSaid(bool deactivated)
    {
        var repository = Substitute.For<IBillRepository>();
        repository.DeactivateAsync(42, 7, Arg.Any<CancellationToken>()).Returns(deactivated);

        (await new DeleteBill(repository).ExecuteAsync(42, 7, CancellationToken.None)).ShouldBe(deactivated);
    }
}

public class CreateBillTests
{
    private static readonly IClock Clock = BillSamples.ClockAt(new DateOnly(2026, 10, 4));

    [Fact]
    [Trait("AC", "005-10")]
    public async Task ValidBody_CreatesForTheTokenUser_WithTodayFromTheClock()
    {
        var repository = BillSamples.RepositoryWithOwnedAccountAndSubcategory();
        repository.CreateAsync(42, Arg.Any<NewBillData>(), Arg.Any<CancellationToken>()).Returns(BillSamples.Details());

        var result = await new CreateBill(repository, Clock).ExecuteAsync(42, BillRaw.Fixed(), CancellationToken.None);

        result.ShouldBeOfType<CreateBillResult.Created>().Bill.ShouldBe(BillSamples.Details());
        await repository.Received(1).CreateAsync(
            42, Arg.Is<NewBillData>(d => d.StartDate == new DateOnly(2026, 10, 4) && d.AccountId == 3 && d.SubcategoryId == 12), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "005-11")]
    public async Task NullBody_IsDataMissing_AndTheRepositoryIsUntouched()
    {
        var repository = Substitute.For<IBillRepository>();

        var result = await new CreateBill(repository, Clock).ExecuteAsync(42, null, CancellationToken.None);

        result.ShouldBeOfType<CreateBillResult.Invalid>().Reason.ShouldBe(BillError.DataMissing);
        repository.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "005-12")]
    public async Task ValidationErrors_AnswerBeforeAnyDatabaseCheck()
    {
        var repository = Substitute.For<IBillRepository>();

        var result = await new CreateBill(repository, Clock).ExecuteAsync(
            42, BillRaw.Fixed(("dia_execucao", Raw.T("abc")), ("conta_id", Raw.N(999))), CancellationToken.None);

        result.ShouldBeOfType<CreateBillResult.Invalid>().Reason.ShouldBe(BillError.DayInvalid);
        repository.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "005-24")]
    public async Task AccountNotOwnedOrInactive_IsAccountNotFound_AndNothingIsWritten()
    {
        var repository = Substitute.For<IBillRepository>();
        repository.ActiveAccountOwnedAsync(42, 3, Arg.Any<CancellationToken>()).Returns(false);

        var result = await new CreateBill(repository, Clock).ExecuteAsync(42, BillRaw.Fixed(), CancellationToken.None);

        result.ShouldBeOfType<CreateBillResult.AccountNotFound>();
        await repository.DidNotReceive().CreateAsync(Arg.Any<int>(), Arg.Any<NewBillData>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "005-26")]
    public async Task SubcategoryNotVisible_IsSubcategoryNotFound_OnlyAfterTheAccountCheck()
    {
        var repository = Substitute.For<IBillRepository>();
        repository.ActiveAccountOwnedAsync(42, 3, Arg.Any<CancellationToken>()).Returns(true);
        repository.SubcategoryVisibleAsync(42, 12, Arg.Any<CancellationToken>()).Returns(false);

        var result = await new CreateBill(repository, Clock).ExecuteAsync(42, BillRaw.Fixed(), CancellationToken.None);

        result.ShouldBeOfType<CreateBillResult.SubcategoryNotFound>();
        await repository.DidNotReceive().CreateAsync(Arg.Any<int>(), Arg.Any<NewBillData>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "005-26")]
    public async Task AccountIsCheckedBeforeTheSubcategory()
    {
        var repository = Substitute.For<IBillRepository>();
        repository.ActiveAccountOwnedAsync(42, 3, Arg.Any<CancellationToken>()).Returns(false);

        await new CreateBill(repository, Clock).ExecuteAsync(42, BillRaw.Fixed(), CancellationToken.None);

        await repository.DidNotReceive().SubcategoryVisibleAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}

public class UpdateBillTests
{
    private static IBillRepository Repository(CurrentBill? current)
    {
        var repository = BillSamples.RepositoryWithOwnedAccountAndSubcategory();
        repository.GetCurrentAsync(42, 7, Arg.Any<CancellationToken>()).Returns(current);
        repository.UpdateAsync(42, 7, Arg.Any<BillChanges>(), Arg.Any<CancellationToken>()).Returns(BillSamples.Details(7));
        return repository;
    }

    private static Task<UpdateBillResult> Run(IBillRepository repository, RawBody? body) =>
        new UpdateBill(repository).ExecuteAsync(42, 7, body, CancellationToken.None);

    [Fact]
    [Trait("AC", "005-42")]
    public async Task PartialUpdate_WritesOnlyTheChangesAndReturnsTheDetails()
    {
        var repository = Repository(BillSamples.Current());

        var result = await Run(repository, Raw.Body(("descricao", Raw.T("Nova"))));

        result.ShouldBeOfType<UpdateBillResult.Updated>().Bill.ShouldBe(BillSamples.Details(7));
        await repository.Received(1).UpdateAsync(
            42, 7, Arg.Is<BillChanges>(c => c.Description.Value == "Nova" && !c.Day.IsSet && !c.Month.IsSet), Arg.Any<CancellationToken>());
    }

    [Theory]
    [Trait("AC", "005-40")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullOrEmptyBody_Is400_EvenWhenTheBillDoesNotExist(bool empty)
    {
        var repository = Repository(null);

        var result = await Run(repository, empty ? Raw.Body() : null);

        result.ShouldBeOfType<UpdateBillResult.Invalid>().Reason.ShouldBe(BillError.DataMissing);
        repository.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "005-41")]
    public async Task MissingInactiveOrForeignBill_IsNotFound_BeforeAnyFieldIsRead()
    {
        var repository = Repository(null);

        var result = await Run(repository, Raw.Body(("dia_execucao", Raw.T("abc"))));

        result.ShouldBeOfType<UpdateBillResult.NotFound>();
        await repository.DidNotReceive().UpdateAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<BillChanges>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "005-55")]
    public async Task WithTwoErrors_TheFieldErrorComesBeforeTheAccountCheck()
    {
        var repository = Repository(BillSamples.Current());

        var result = await Run(repository, Raw.Body(("dia_execucao", Raw.N(0)), ("conta_id", Raw.N(99))));

        result.ShouldBeOfType<UpdateBillResult.Invalid>().Reason.ShouldBe(BillError.DayInvalid);
        await repository.DidNotReceive().ActiveAccountOwnedAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await repository.DidNotReceive().UpdateAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<BillChanges>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "005-47")]
    public async Task AccountNotOwned_IsAccountNotFound()
    {
        var repository = Repository(BillSamples.Current());
        repository.ActiveAccountOwnedAsync(42, 99, Arg.Any<CancellationToken>()).Returns(false);
        repository.ActiveAccountOwnedAsync(42, 0, Arg.Any<CancellationToken>()).Returns(false);   // null vira o id 0, que nunca existe

        (await Run(repository, Raw.Body(("conta_id", Raw.N(99))))).ShouldBeOfType<UpdateBillResult.AccountNotFound>();
        (await Run(repository, Raw.Body(("conta_id", BillRaw.Null)))).ShouldBeOfType<UpdateBillResult.AccountNotFound>();
    }

    [Fact]
    [Trait("AC", "005-48")]
    public async Task SubcategoryNotVisible_IsSubcategoryNotFound_BeforeTheAccount()
    {
        var repository = Repository(BillSamples.Current());
        repository.SubcategoryVisibleAsync(42, 77, Arg.Any<CancellationToken>()).Returns(false);

        var result = await Run(repository, Raw.Body(("subcategoria_id", Raw.N(77)), ("conta_id", Raw.N(99))));

        result.ShouldBeOfType<UpdateBillResult.SubcategoryNotFound>();
        await repository.DidNotReceive().ActiveAccountOwnedAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "005-54")]
    public async Task OnlyUnrecognizedKeys_IsNothingToUpdate()
    {
        var repository = Repository(BillSamples.Current());

        var result = await Run(repository, Raw.Body(("foo", Raw.N(1))));

        result.ShouldBeOfType<UpdateBillResult.Invalid>().Reason.ShouldBe(BillError.NothingToUpdate);
    }

    [Fact]
    [Trait("AC", "005-53")]
    public async Task SwitchingToInstallmentWithoutTotal_IsRequired()
    {
        var repository = Repository(BillSamples.Current());

        var result = await Run(repository, Raw.Body(("tipo_agendamento", Raw.T("PARCELADO"))));

        result.ShouldBeOfType<UpdateBillResult.Invalid>().Reason.ShouldBe(BillError.InstallmentsRequired);
    }

    [Fact]
    [Trait("AC", "005-56")]
    public async Task AnnualEditedToMonthly_WritesTheMonthAsNull()
    {
        var repository = Repository(new CurrentBill("FIXO", "ANUAL", 3, 100m, null, 0));

        var result = await Run(repository, Raw.Body(("periodicidade", Raw.T("MENSAL"))));

        result.ShouldBeOfType<UpdateBillResult.Updated>();
        await repository.Received(1).UpdateAsync(
            42, 7, Arg.Is<BillChanges>(c => c.Month.IsSet && c.Month.Value == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "005-58")]
    public async Task SwitchingToAnnualWithoutMonth_IsRejectedAndNothingIsWritten()
    {
        var repository = Repository(BillSamples.Current());

        var result = await Run(repository, Raw.Body(("periodicidade", Raw.T("ANUAL"))));

        result.ShouldBeOfType<UpdateBillResult.Invalid>().Reason.ShouldBe(BillError.MonthRequiredForAnnual);
        await repository.DidNotReceive().UpdateAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<BillChanges>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "005-41")]
    public async Task BillThatVanishedBetweenCheckAndWrite_IsNotFound()
    {
        var repository = Repository(BillSamples.Current());
        repository.UpdateAsync(42, 7, Arg.Any<BillChanges>(), Arg.Any<CancellationToken>()).Returns((BillDetails?)null);

        (await Run(repository, Raw.Body(("descricao", Raw.T("x"))))).ShouldBeOfType<UpdateBillResult.NotFound>();
    }
}

public class EmergencyReserveServiceTests
{
    [Fact]
    [Trait("AC", "005-100")]
    public async Task GetStatus_UsesTheUserMonthsAndTheFilteredTotals_WithoutRounding()
    {
        var repository = Substitute.For<IBillRepository>();
        repository.GetReserveMonthsAsync(42, Arg.Any<CancellationToken>()).Returns(6);
        IReadOnlyList<PeriodTotal> totals =
        [
            new(Periodicities.Annual, 1200), new(Periodicities.Daily, 2), new(Periodicities.Monthly, 100),
            new(Periodicities.Biweekly, 50), new(Periodicities.Weekly, 10),
        ];
        repository.ListReserveTotalsAsync(42, Arg.Any<CancellationToken>()).Returns(totals);

        var status = await new EmergencyReserveService(repository).GetStatusAsync(42, CancellationToken.None);

        status.Total.ShouldBe(3020.0);
        status.Months.ShouldBe(6);
        status.MonthlyEquivalent.ShouldBe(3020.0 / 6);
    }

    [Fact]
    [Trait("AC", "005-100")]
    public async Task GetStatus_NothingMarked_IsZeroWithTheMonths()
    {
        var repository = Substitute.For<IBillRepository>();
        repository.GetReserveMonthsAsync(42, Arg.Any<CancellationToken>()).Returns(12);
        repository.ListReserveTotalsAsync(42, Arg.Any<CancellationToken>()).Returns([]);

        var status = await new EmergencyReserveService(repository).GetStatusAsync(42, CancellationToken.None);

        status.ShouldBe(new EmergencyReserveStatus(0, 0, 12));
    }

    [Fact]
    [Trait("AC", "005-101")]
    public async Task GetItems_AreOrderedByDescendingImpactThenId_WithTheSameFactor()
    {
        var repository = Substitute.For<IBillRepository>();
        repository.GetReserveMonthsAsync(42, Arg.Any<CancellationToken>()).Returns(50);
        IReadOnlyList<ReserveItemRow> rows =
        [
            new(5, "Aluguel", 100m, Periodicities.Monthly, "Aluguel"),    // 5000
            new(2, "Feira", 10m, Periodicities.Weekly, "Mercado"),         // 10 × round(216,5) = 2160
            new(9, "Streaming", 100m, Periodicities.Monthly, null),        // 5000 (empata com o 5: id decide)
            new(3, "IPTU", 1200m, Periodicities.Annual, "Impostos"),       // 1200
        ];
        repository.ListReserveItemsAsync(42, Arg.Any<CancellationToken>()).Returns(rows);

        var items = await new EmergencyReserveService(repository).GetItemsAsync(42, CancellationToken.None);

        items.Select(i => i.ScheduleId).ShouldBe([5, 9, 2, 3]);
        items.Select(i => i.Impact).ShouldBe([5000.0, 5000.0, 2160.0, 1200.0]);
        items[2].ShouldBe(new EmergencyReserveItem(2, "Feira", 10, "SEMANAL", "Mercado", 2160));
        items[1].Category.ShouldBeNull();
    }
}

public class GetBillsSummaryTests
{
    [Fact]
    [Trait("AC", "005-90")]
    public async Task UsesTheReserveServiceTotalAndMonths_NotItsOwnSql()
    {
        var repository = Substitute.For<IBillRepository>();
        IReadOnlyList<SummaryRow> rows = [new(Periodicities.Monthly, false, 100), new(Periodicities.Monthly, true, 3000)];
        repository.ListSummaryRowsAsync(42, Arg.Any<CancellationToken>()).Returns(rows);
        var reserve = Substitute.For<IEmergencyReserveService>();
        reserve.GetStatusAsync(42, Arg.Any<CancellationToken>()).Returns(new EmergencyReserveStatus(503.33, 3020, 6));

        var summary = await new GetBillsSummary(repository, reserve).ExecuteAsync(42, CancellationToken.None);

        summary.ShouldBe(new BillsSummary(100, 0, 3000, 3020, 503.33, 6));
        await repository.DidNotReceive().ListReserveTotalsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await repository.DidNotReceive().GetReserveMonthsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "005-95")]
    public async Task NothingMarked_FallsBackToMonthlyExpensesTimesMonths()
    {
        var repository = Substitute.For<IBillRepository>();
        IReadOnlyList<SummaryRow> rows = [new(Periodicities.Monthly, false, 1000)];
        repository.ListSummaryRowsAsync(42, Arg.Any<CancellationToken>()).Returns(rows);
        var reserve = Substitute.For<IEmergencyReserveService>();
        reserve.GetStatusAsync(42, Arg.Any<CancellationToken>()).Returns(new EmergencyReserveStatus(0, 0, 6));

        var summary = await new GetBillsSummary(repository, reserve).ExecuteAsync(42, CancellationToken.None);

        summary.ReserveGoal.ShouldBe(6000.0);
        summary.ReserveMonthlyEquivalent.ShouldBe(1000.0);
    }
}
