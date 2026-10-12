using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.Bills;
using MeuSecretario.UnitTests.Domain.Accounts;

namespace MeuSecretario.UnitTests.Domain.Bills;

public class UpdateBillRulesTests
{
    private static BillError? HeadError(RawBody body) =>
        UpdateBillRules.ReadHead(body) is BillHeadResult.Rejected rejected ? rejected.Reason : null;

    private static BillHeadChanges Head(RawBody body) =>
        UpdateBillRules.ReadHead(body).ShouldBeOfType<BillHeadResult.Ok>().Head;

    private static BillError? TailError(RawBody body)
    {
        var head = Head(body);
        return UpdateBillRules.ReadTail(body, head, UpdateBillRules.ReadReferences(body)) is BillTailResult.Rejected rejected
            ? rejected.Reason
            : null;
    }

    private static BillChanges Changes(RawBody body)
    {
        var head = Head(body);
        return UpdateBillRules.ReadTail(body, head, UpdateBillRules.ReadReferences(body)).ShouldBeOfType<BillTailResult.Ok>().Changes;
    }

    [Fact]
    [Trait("AC", "005-42")]
    public void Description_IsTrimmed_AndBlankNullOrNonTextIsEmpty()
    {
        Head(Raw.Body(("descricao", Raw.T("  Nova  ")))).Description.ShouldBe(Change.Set("Nova"));
        HeadError(Raw.Body(("descricao", Raw.T("   ")))).ShouldBe(BillError.DescriptionEmpty);
        HeadError(Raw.Body(("descricao", Raw.T("")))).ShouldBe(BillError.DescriptionEmpty);
        HeadError(Raw.Body(("descricao", BillRaw.Null))).ShouldBe(BillError.DescriptionEmpty);   // desvio 072: não grava "None"
        HeadError(Raw.Body(("descricao", Raw.N(5)))).ShouldBe(BillError.DescriptionEmpty);
    }

    [Fact]
    [Trait("AC", "005-42")]
    public void Description_MoreThan255CodePoints_IsRefused()
    {
        Head(Raw.Body(("descricao", Raw.T(new string('a', 255))))).Description.Value.Length.ShouldBe(255);
        HeadError(Raw.Body(("descricao", Raw.T(new string('a', 256))))).ShouldBe(BillError.DescriptionTooLong);
    }

    [Fact]
    [Trait("AC", "005-43")]
    public void TypeAndPeriodicity_AreExactWithoutTrim()
    {
        HeadError(Raw.Body(("tipo_agendamento", Raw.T("X")))).ShouldBe(BillError.TypeInvalidUpdate);
        HeadError(Raw.Body(("tipo_agendamento", Raw.T("fixo")))).ShouldBe(BillError.TypeInvalidUpdate);
        HeadError(Raw.Body(("tipo_agendamento", Raw.T(" FIXO ")))).ShouldBe(BillError.TypeInvalidUpdate);
        HeadError(Raw.Body(("tipo_agendamento", BillRaw.Null))).ShouldBe(BillError.TypeInvalidUpdate);
        HeadError(Raw.Body(("periodicidade", Raw.T("mensal")))).ShouldBe(BillError.PeriodicityInvalidUpdate);
        HeadError(Raw.Body(("periodicidade", Raw.T(" MENSAL ")))).ShouldBe(BillError.PeriodicityInvalidUpdate);
        Head(Raw.Body(("tipo_agendamento", Raw.T("PARCELADO")), ("periodicidade", Raw.T("ANUAL")))).Type.Value.ShouldBe("PARCELADO");
    }

    [Fact]
    [Trait("AC", "005-44")]
    public void Day_IsValidatedWhenPresent()
    {
        foreach (var bad in new[] { Raw.N(0), Raw.N(32), Raw.T("x"), BillRaw.Null })
        {
            HeadError(Raw.Body(("dia_execucao", bad))).ShouldBe(BillError.DayInvalid);
        }

        Head(Raw.Body(("dia_execucao", Raw.T("20")))).Day.ShouldBe(Change.Set(20));
    }

    [Fact]
    [Trait("AC", "005-45")]
    public void Month_NullClears_OtherwiseOneToTwelve()
    {
        Head(Raw.Body(("mes_execucao", BillRaw.Null))).Month.ShouldBe(Change.Set<int?>(null));
        Head(Raw.Body(("mes_execucao", Raw.N(6)))).Month.ShouldBe(Change.Set<int?>(6));
        HeadError(Raw.Body(("mes_execucao", Raw.N(13)))).ShouldBe(BillError.MonthInvalidUpdate);
        HeadError(Raw.Body(("mes_execucao", Raw.T("x")))).ShouldBe(BillError.MonthInvalidUpdate);
    }

    [Fact]
    [Trait("AC", "005-46")]
    public void Amount_NullClears_NoSignValidation_UnreadableIsInvalid()
    {
        Head(Raw.Body(("valor_previsto", BillRaw.Null))).Amount.ShouldBe(Change.Set<decimal?>(null));
        Head(Raw.Body(("valor_previsto", Raw.N(0)))).Amount.ShouldBe(Change.Set<decimal?>(0m));
        Head(Raw.Body(("valor_previsto", Raw.N(-10)))).Amount.ShouldBe(Change.Set<decimal?>(-10m));
        Head(Raw.Body(("valor_previsto", BillRaw.D(250.75)))).Amount.ShouldBe(Change.Set<decimal?>(250.75m));
        HeadError(Raw.Body(("valor_previsto", Raw.T("abc")))).ShouldBe(BillError.AmountInvalidUpdate);
        HeadError(Raw.Body(("valor_previsto", RawValue.Other))).ShouldBe(BillError.AmountInvalidUpdate);
        HeadError(Raw.Body(("valor_previsto", BillRaw.D(double.NaN)))).ShouldBe(BillError.AmountInvalidUpdate);   // desvio 072
        HeadError(Raw.Body(("valor_previsto", BillRaw.Big("99999999999999999999")))).ShouldBe(BillError.AmountInvalidUpdate);
    }

    [Fact]
    [Trait("AC", "005-57")]
    public void HeadFollowsTheSpecOrder()
    {
        HeadError(Raw.Body(("tipo_agendamento", Raw.T("X")), ("descricao", Raw.T("")))).ShouldBe(BillError.DescriptionEmpty);
        HeadError(Raw.Body(("dia_execucao", Raw.N(0)), ("valor_previsto", Raw.T("x")))).ShouldBe(BillError.DayInvalid);
    }

    [Fact]
    [Trait("AC", "005-47")]
    public void References_AreRawIds_ZeroWhenNotAnInteger()
    {
        UpdateBillRules.ReadReferences(Raw.Body(("descricao", Raw.T("x")))).ShouldBe(new BillReferences(null, null));
        UpdateBillRules.ReadReferences(Raw.Body(("conta_id", Raw.N(3)), ("subcategoria_id", Raw.T("12"))))
            .ShouldBe(new BillReferences(12, 3));
        UpdateBillRules.ReadReferences(Raw.Body(("conta_id", BillRaw.Null), ("subcategoria_id", BillRaw.D(1.5))))
            .ShouldBe(new BillReferences(0, 0));
    }

    [Fact]
    [Trait("AC", "005-49")]
    public void NotifyDays_UnreadableIsIgnored_Readable_IsKept()
    {
        Changes(Raw.Body(("notificar_antes_dias", Raw.T("7")))).NotifyDaysBefore.ShouldBe(Change.Set(7));
        Changes(Raw.Body(("notificar_antes_dias", Raw.N(0)))).NotifyDaysBefore.ShouldBe(Change.Set(0));
        // desvio 072: "abc" e null deixam de dar 500; o campo é ignorado
        Changes(Raw.Body(("notificar_antes_dias", Raw.T("abc")), ("descricao", Raw.T("x")))).NotifyDaysBefore.IsSet.ShouldBeFalse();
        Changes(Raw.Body(("notificar_antes_dias", BillRaw.Null), ("descricao", Raw.T("x")))).NotifyDaysBefore.IsSet.ShouldBeFalse();
        Changes(Raw.Body(("notificar_antes_dias", Raw.T("abc")))).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "005-51")]
    public void StartDate_OnlyIsoDate_NullIsRefusedToo()
    {
        Changes(Raw.Body(("data_inicio", Raw.T("2026-03-01")))).StartDate.ShouldBe(Change.Set(new DateOnly(2026, 3, 1)));
        TailError(Raw.Body(("data_inicio", Raw.T("amanhã")))).ShouldBe(BillError.StartDateInvalid);
        TailError(Raw.Body(("data_inicio", BillRaw.Null))).ShouldBe(BillError.StartDateInvalid);
    }

    [Fact]
    [Trait("AC", "005-50")]
    public void IncludeInReserve_OnlyBooleans()
    {
        Changes(Raw.Body(("incluir_na_reserva", BillRaw.B(true)))).IncludeInReserve.ShouldBe(Change.Set(true));
        Changes(Raw.Body(("incluir_na_reserva", BillRaw.B(false)))).IncludeInReserve.ShouldBe(Change.Set(false));
        TailError(Raw.Body(("incluir_na_reserva", Raw.T("false")))).ShouldBe(BillError.ReserveFlagInvalid);
        TailError(Raw.Body(("incluir_na_reserva", BillRaw.Null))).ShouldBe(BillError.ReserveFlagInvalid);
    }

    [Fact]
    [Trait("AC", "005-52")]
    public void Installments_NullClears_AtLeastTwo()
    {
        Changes(Raw.Body(("total_parcelas", BillRaw.Null))).TotalInstallments.ShouldBe(Change.Set<int?>(null));
        Changes(Raw.Body(("total_parcelas", Raw.N(3)))).TotalInstallments.ShouldBe(Change.Set<int?>(3));
        TailError(Raw.Body(("total_parcelas", Raw.N(1)))).ShouldBe(BillError.InstallmentsInvalid);
        TailError(Raw.Body(("total_parcelas", Raw.T("x")))).ShouldBe(BillError.InstallmentsInvalid);
    }

    [Fact]
    [Trait("AC", "005-53")]
    public void SwitchingToInstallment_NeedsTotalInTheSameBody()
    {
        TailError(Raw.Body(("tipo_agendamento", Raw.T("PARCELADO")))).ShouldBe(BillError.InstallmentsRequired);
        TailError(Raw.Body(("tipo_agendamento", Raw.T("PARCELADO")), ("total_parcelas", Raw.N(6)))).ShouldBeNull();
        TailError(Raw.Body(("tipo_agendamento", Raw.T("FIXO")))).ShouldBeNull();
        TailError(Raw.Body(("descricao", Raw.T("x")))).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "005-54")]
    public void OnlyUnrecognizedKeys_IsEmpty()
    {
        Changes(Raw.Body(("foo", Raw.N(1)))).IsEmpty.ShouldBeTrue();
        Changes(Raw.Body(("parcelas_executadas", Raw.N(3)), ("ativo", BillRaw.B(false)), ("usuario_id", Raw.N(9)))).IsEmpty.ShouldBeTrue();
        Changes(Raw.Body(("descricao", Raw.T("x")))).IsEmpty.ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "005-56")]
    public void AngularEditBody_IsReadWithoutErrors()
    {
        var body = Raw.Body(
            ("descricao", Raw.T("Aluguel")), ("tipo_agendamento", Raw.T("FIXO")), ("periodicidade", Raw.T("MENSAL")),
            ("dia_execucao", Raw.N(5)), ("data_inicio", Raw.T("2026-01-05")), ("subcategoria_id", Raw.N(12)),
            ("conta_id", Raw.N(3)), ("notificar_antes_dias", Raw.N(3)), ("incluir_na_reserva", BillRaw.B(true)),
            ("total_parcelas", BillRaw.Null), ("valor_previsto", BillRaw.D(1500)));

        var changes = Changes(body);

        changes.IsEmpty.ShouldBeFalse();
        changes.TotalInstallments.ShouldBe(Change.Set<int?>(null));
        changes.Month.IsSet.ShouldBeFalse();
    }
}

public class BillConsistencyTests
{
    private static readonly BillChanges None = new(
        default, default, default, default, default, default, default, default, default, default, default, default, default);

    private static CurrentBill Monthly(string type = "FIXO", int? total = null, int executed = 0, decimal? amount = 100m) =>
        new(type, "MENSAL", null, amount, total, executed);

    private static CurrentBill Annual(string type = "FIXO") => new(type, "ANUAL", 3, 100m, null, 0);

    private static BillChanges With(
        Change<string> type = default,
        Change<string> periodicity = default,
        Change<int?> month = default,
        Change<decimal?> amount = default,
        Change<int?> total = default) =>
        None with { Type = type, Periodicity = periodicity, Month = month, Amount = amount, TotalInstallments = total };

    private static BillError? ErrorOf(CurrentBill current, BillChanges changes) =>
        BillConsistency.Check(current, changes) is ConsistencyResult.Rejected rejected ? rejected.Reason : null;

    private static BillChanges Adjusted(CurrentBill current, BillChanges changes) =>
        BillConsistency.Check(current, changes).ShouldBeOfType<ConsistencyResult.Ok>().Changes;

    [Fact]
    [Trait("AC", "005-56")]
    public void AnnualEditedToMonthly_ClearsTheOldMonth()
    {
        var adjusted = Adjusted(Annual(), With(periodicity: Change.Set("MENSAL")));

        adjusted.Month.ShouldBe(Change.Set<int?>(null));
    }

    [Fact]
    [Trait("AC", "005-45")]
    public void MonthOnNonAnnual_IsClearedAutomatically()
    {
        Adjusted(Monthly(), With(month: Change.Set<int?>(6))).Month.ShouldBe(Change.Set<int?>(null));
    }

    [Fact]
    [Trait("AC", "005-58")]
    public void SwitchingToAnnualWithoutMonth_IsRejected()
    {
        ErrorOf(Monthly(), With(periodicity: Change.Set("ANUAL"))).ShouldBe(BillError.MonthRequiredForAnnual);
        ErrorOf(Annual(), With(month: Change.Set<int?>(null))).ShouldBe(BillError.MonthRequiredForAnnual);
    }

    [Fact]
    [Trait("AC", "005-58")]
    public void SwitchingToAnnualWithMonth_IsAccepted()
    {
        var adjusted = Adjusted(Monthly(), With(periodicity: Change.Set("ANUAL"), month: Change.Set<int?>(3)));

        adjusted.Month.ShouldBe(Change.Set<int?>(3));
    }

    [Fact]
    [Trait("AC", "005-56")]
    public void LeavingInstallment_ClearsTotalAndExecuted()
    {
        var current = Monthly("PARCELADO", total: 6, executed: 2);

        var adjusted = Adjusted(current, With(type: Change.Set("FIXO"), total: Change.Set<int?>(null)));

        adjusted.TotalInstallments.ShouldBe(Change.Set<int?>(null));
        adjusted.ExecutedInstallments.ShouldBe(Change.Set(0));
    }

    [Fact]
    public void LeavingInstallmentWithoutSendingTotal_StillClearsTheStoredTotal()
    {
        var current = Monthly("PARCELADO", total: 6, executed: 0);

        var adjusted = Adjusted(current, With(type: Change.Set("FIXO")));

        adjusted.TotalInstallments.ShouldBe(Change.Set<int?>(null));
        adjusted.ExecutedInstallments.IsSet.ShouldBeFalse();
    }

    [Fact]
    public void TotalOnANonInstallment_IsClearedAutomatically()
    {
        Adjusted(Monthly(), With(total: Change.Set<int?>(3))).TotalInstallments.ShouldBe(Change.Set<int?>(null));
    }

    [Fact]
    [Trait("AC", "005-52")]
    public void InstallmentTotalBelowExecuted_IsRejected()
    {
        var current = Monthly("PARCELADO", total: 6, executed: 4);

        ErrorOf(current, With(total: Change.Set<int?>(3))).ShouldBe(BillError.InstallmentsBelowExecuted);
        ErrorOf(current, With(total: Change.Set<int?>(4))).ShouldBeNull();
    }

    [Fact]
    public void SwitchingToInstallmentWithTotal_IsAccepted()
    {
        var adjusted = Adjusted(Monthly(), With(type: Change.Set("PARCELADO"), total: Change.Set<int?>(6)));

        adjusted.TotalInstallments.ShouldBe(Change.Set<int?>(6));
        adjusted.ExecutedInstallments.IsSet.ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "005-46")]
    public void FixedOrInstallmentResultingWithoutPositiveAmount_IsRejected_WhenBodyTouchesTypeOrAmount()
    {
        ErrorOf(Monthly("LEMBRETE_VARIAVEL", amount: null), With(type: Change.Set("FIXO"))).ShouldBe(BillError.AmountRequired);
        ErrorOf(Monthly(), With(amount: Change.Set<decimal?>(null))).ShouldBe(BillError.AmountRequired);
        ErrorOf(Monthly(), With(amount: Change.Set<decimal?>(0m))).ShouldBe(BillError.AmountNotPositive);
        ErrorOf(Monthly(), With(amount: Change.Set<decimal?>(-10m))).ShouldBe(BillError.AmountNotPositive);
        ErrorOf(Monthly(), With(amount: Change.Set<decimal?>(250.75m))).ShouldBeNull();
    }

    [Fact]
    public void VariableReminder_AcceptsAnyAmount_AndUntouchedLegacyAmountIsNotChecked()
    {
        ErrorOf(Monthly("LEMBRETE_VARIAVEL"), With(amount: Change.Set<decimal?>(null))).ShouldBeNull();
        ErrorOf(Monthly("LEMBRETE_VARIAVEL"), With(amount: Change.Set<decimal?>(-5m))).ShouldBeNull();
        ErrorOf(Monthly(amount: null), With(periodicity: Change.Set("SEMANAL"))).ShouldBeNull();
    }

    [Fact]
    public void ConsistentChange_IsReturnedUntouched()
    {
        var changes = With(periodicity: Change.Set("SEMANAL"));

        Adjusted(Monthly(), changes).ShouldBe(changes);
    }
}
