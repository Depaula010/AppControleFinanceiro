using System.Numerics;
using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.Bills;
using MeuSecretario.UnitTests.Domain.Accounts;

namespace MeuSecretario.UnitTests.Domain.Bills;

internal static class BillRaw
{
    public static readonly DateOnly Today = new(2026, 10, 4);

    public static RawValue D(double value) => RawValue.Fractional(value);

    public static RawValue B(bool value) => RawValue.Bool(value);

    public static RawValue Null => RawValue.Null;

    public static RawValue Big(string digits) => RawValue.Whole(BigInteger.Parse(digits, System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Corpo mínimo válido de um FIXO mensal; os pares extras sobrescrevem as chaves-base.</summary>
    public static RawBody Fixed(params (string Key, RawValue Value)[] extra) =>
        Raw.Body([
            ("descricao", Raw.T("Internet")), ("tipo_agendamento", Raw.T("FIXO")), ("periodicidade", Raw.T("MENSAL")),
            ("subcategoria_id", Raw.N(12)), ("conta_id", Raw.N(3)), ("dia_execucao", Raw.N(10)), ("valor_previsto", Raw.N(120)),
            .. extra]);

    /// <summary>Remove chaves do corpo-base antes de sobrescrever.</summary>
    public static RawBody Without(string key, params (string Key, RawValue Value)[] extra)
    {
        var baseItems = new (string Key, RawValue Value)[]
        {
            ("descricao", Raw.T("Internet")), ("tipo_agendamento", Raw.T("FIXO")), ("periodicidade", Raw.T("MENSAL")),
            ("subcategoria_id", Raw.N(12)), ("conta_id", Raw.N(3)), ("dia_execucao", Raw.N(10)), ("valor_previsto", Raw.N(120)),
        };
        return Raw.Body([.. baseItems.Where(i => i.Key != key), .. extra]);
    }
}

public class CreateBillRulesTests
{
    private static BillError? ErrorOf(RawBody? body) =>
        CreateBillRules.Validate(body, BillRaw.Today) is CreateBillValidation.Rejected error ? error.Reason : null;

    private static NewBillData Ok(RawBody body) =>
        CreateBillRules.Validate(body, BillRaw.Today).ShouldBeOfType<CreateBillValidation.Ok>().Data;

    [Fact]
    [Trait("AC", "005-10")]
    public void MinimalFixedBody_UsesTheDocumentedDefaults()
    {
        var data = Ok(BillRaw.Fixed());

        data.ShouldBe(new NewBillData("Internet", 120m, "FIXO", "MENSAL", 10, null, 3, BillRaw.Today, false, null, 12, 3));
    }

    [Fact]
    [Trait("AC", "005-11")]
    public void NullOrEmptyBody_IsDataMissing()
    {
        ErrorOf(null).ShouldBe(BillError.DataMissing);
        ErrorOf(Raw.Body()).ShouldBe(BillError.DataMissing);
    }

    [Theory]
    [Trait("AC", "005-13")]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankDescription_IsRequired(string description)
    {
        ErrorOf(BillRaw.Fixed(("descricao", Raw.T(description)))).ShouldBe(BillError.DescriptionRequired);
    }

    [Fact]
    [Trait("AC", "005-13")]
    public void MissingNullOrNonTextDescription_IsRequired()
    {
        ErrorOf(BillRaw.Without("descricao")).ShouldBe(BillError.DescriptionRequired);
        ErrorOf(BillRaw.Fixed(("descricao", BillRaw.Null))).ShouldBe(BillError.DescriptionRequired);   // desvio 072
        ErrorOf(BillRaw.Fixed(("descricao", Raw.N(5)))).ShouldBe(BillError.DescriptionRequired);
    }

    [Fact]
    [Trait("AC", "005-13")]
    public void Description_IsTrimmed_AndMoreThan255CodePointsIsRefused()
    {
        Ok(BillRaw.Fixed(("descricao", Raw.T("  Luz  ")))).Description.ShouldBe("Luz");
        Ok(BillRaw.Fixed(("descricao", Raw.T(new string('a', 255))))).Description.Length.ShouldBe(255);
        ErrorOf(BillRaw.Fixed(("descricao", Raw.T(new string('a', 256))))).ShouldBe(BillError.DescriptionTooLong);
        ErrorOf(BillRaw.Fixed(("descricao", Raw.T(string.Concat(Enumerable.Repeat("😀", 256)))))).ShouldBe(BillError.DescriptionTooLong);
        Ok(BillRaw.Fixed(("descricao", Raw.T(string.Concat(Enumerable.Repeat("é😀", 127)))))).Description.EnumerateRunes().Count().ShouldBe(254);
    }

    [Theory]
    [Trait("AC", "005-14")]
    [InlineData("fixo")]
    [InlineData("")]
    [InlineData("MENSAL")]
    public void InvalidType_IsRejected(string type)
    {
        ErrorOf(BillRaw.Fixed(("tipo_agendamento", Raw.T(type)))).ShouldBe(BillError.TypeInvalidCreate);
    }

    [Fact]
    [Trait("AC", "005-14")]
    public void MissingOrNullType_IsRejected_AndTrimIsAccepted()
    {
        ErrorOf(BillRaw.Without("tipo_agendamento")).ShouldBe(BillError.TypeInvalidCreate);
        ErrorOf(BillRaw.Fixed(("tipo_agendamento", BillRaw.Null))).ShouldBe(BillError.TypeInvalidCreate);   // desvio 072
        Ok(BillRaw.Fixed(("tipo_agendamento", Raw.T(" FIXO ")))).Type.ShouldBe("FIXO");
    }

    [Fact]
    [Trait("AC", "005-15")]
    public void InvalidPeriodicity_IsRejected_AndTrimIsAccepted()
    {
        ErrorOf(BillRaw.Fixed(("periodicidade", Raw.T("mensal")))).ShouldBe(BillError.PeriodicityInvalidCreate);
        ErrorOf(BillRaw.Fixed(("periodicidade", Raw.T("")))).ShouldBe(BillError.PeriodicityInvalidCreate);
        ErrorOf(BillRaw.Without("periodicidade")).ShouldBe(BillError.PeriodicityInvalidCreate);
        ErrorOf(BillRaw.Fixed(("periodicidade", Raw.T("BIMESTRAL")))).ShouldBe(BillError.PeriodicityInvalidCreate);
        Ok(BillRaw.Fixed(("periodicidade", Raw.T(" MENSAL ")))).Periodicity.ShouldBe("MENSAL");
    }

    [Fact]
    [Trait("AC", "005-16")]
    public void FalsySubcategoryOrAccount_IsRequired()
    {
        foreach (var falsy in new[] { RawValue.Absent, BillRaw.Null, Raw.N(0), Raw.T(""), BillRaw.B(false), BillRaw.D(0) })
        {
            ErrorOf(BillRaw.Fixed(("subcategoria_id", falsy))).ShouldBe(BillError.SubcategoryRequired);
            ErrorOf(BillRaw.Fixed(("conta_id", falsy))).ShouldBe(BillError.AccountRequired);
        }
    }

    [Fact]
    [Trait("AC", "005-16")]
    public void IdsThatAreNotIntegers_BecomeZero_SoTheyAreNotFound()
    {
        Ok(BillRaw.Fixed(("subcategoria_id", Raw.T("12")))).SubcategoryId.ShouldBe(12);
        Ok(BillRaw.Fixed(("conta_id", BillRaw.D(3.5)))).AccountId.ShouldBe(0);
        Ok(BillRaw.Fixed(("conta_id", BillRaw.Big("99999999999999999999")))).AccountId.ShouldBe(0);
    }

    [Theory]
    [Trait("AC", "005-17")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("32")]
    [InlineData("-1")]
    public void InvalidDay_IsRejected(string text)
    {
        ErrorOf(BillRaw.Fixed(("dia_execucao", Raw.T(text)))).ShouldBe(BillError.DayInvalid);
    }

    [Fact]
    [Trait("AC", "005-17")]
    public void DayAcceptsWhatPythonIntAccepts()
    {
        ErrorOf(BillRaw.Without("dia_execucao")).ShouldBe(BillError.DayInvalid);
        ErrorOf(BillRaw.Fixed(("dia_execucao", BillRaw.Null))).ShouldBe(BillError.DayInvalid);
        ErrorOf(BillRaw.Fixed(("dia_execucao", RawValue.Other))).ShouldBe(BillError.DayInvalid);
        Ok(BillRaw.Fixed(("dia_execucao", Raw.T("15")))).Day.ShouldBe(15);
        Ok(BillRaw.Fixed(("dia_execucao", BillRaw.D(15.9)))).Day.ShouldBe(15);
        Ok(BillRaw.Fixed(("dia_execucao", BillRaw.B(true)))).Day.ShouldBe(1);
        Ok(BillRaw.Fixed(("dia_execucao", Raw.N(31)))).Day.ShouldBe(31);
    }

    [Theory]
    [Trait("AC", "005-18")]
    [InlineData("FIXO")]
    [InlineData("PARCELADO")]
    public void FixedAndInstallment_RequireAmount(string type)
    {
        var withInstallments = ("total_parcelas", Raw.N(3));
        ErrorOf(BillRaw.Without("valor_previsto", ("tipo_agendamento", Raw.T(type)), withInstallments)).ShouldBe(BillError.AmountRequired);
        ErrorOf(BillRaw.Fixed(("tipo_agendamento", Raw.T(type)), withInstallments, ("valor_previsto", BillRaw.Null))).ShouldBe(BillError.AmountRequired);
    }

    [Fact]
    [Trait("AC", "005-18")]
    public void FixedAmount_MustBeAPositiveNumber()
    {
        foreach (var bad in new[] { Raw.N(0), Raw.N(-5), Raw.T("abc"), RawValue.Other, BillRaw.D(double.PositiveInfinity), BillRaw.Big("99999999999999999999") })
        {
            ErrorOf(BillRaw.Fixed(("valor_previsto", bad))).ShouldBe(BillError.AmountNotPositive);
        }

        Ok(BillRaw.Fixed(("valor_previsto", Raw.T("120.5")))).Amount.ShouldBe(120.5m);
        Ok(BillRaw.Fixed(("valor_previsto", BillRaw.B(true)))).Amount.ShouldBe(1m);
    }

    [Fact]
    [Trait("AC", "005-18")]
    public void VariableReminder_AmountIsOptional_UnreadableBecomesNull_ZeroAndNegativeAreKept()
    {
        var variable = ("tipo_agendamento", Raw.T("LEMBRETE_VARIAVEL"));

        Ok(BillRaw.Without("valor_previsto", variable)).Amount.ShouldBeNull();
        Ok(BillRaw.Fixed(variable, ("valor_previsto", BillRaw.Null))).Amount.ShouldBeNull();
        Ok(BillRaw.Fixed(variable, ("valor_previsto", Raw.T("abc")))).Amount.ShouldBeNull();
        Ok(BillRaw.Fixed(variable, ("valor_previsto", Raw.N(0)))).Amount.ShouldBe(0m);
        Ok(BillRaw.Fixed(variable, ("valor_previsto", Raw.N(-5)))).Amount.ShouldBe(-5m);
    }

    [Theory]
    [Trait("AC", "005-19")]
    [InlineData(null)]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("x")]
    public void Installment_NeedsAtLeastTwoInstallments(string? text)
    {
        var body = text is null
            ? BillRaw.Fixed(("tipo_agendamento", Raw.T("PARCELADO")))
            : BillRaw.Fixed(("tipo_agendamento", Raw.T("PARCELADO")), ("total_parcelas", Raw.T(text)));

        ErrorOf(body).ShouldBe(BillError.InstallmentsInvalid);
        ErrorOf(BillRaw.Fixed(("tipo_agendamento", Raw.T("PARCELADO")), ("total_parcelas", BillRaw.Null))).ShouldBe(BillError.InstallmentsInvalid);
    }

    [Fact]
    [Trait("AC", "005-19")]
    public void Installments_AreKeptOnlyForInstallmentType()
    {
        Ok(BillRaw.Fixed(("tipo_agendamento", Raw.T("PARCELADO")), ("total_parcelas", Raw.T("12")))).TotalInstallments.ShouldBe(12);
        Ok(BillRaw.Fixed(("tipo_agendamento", Raw.T("PARCELADO")), ("total_parcelas", Raw.N(2)))).TotalInstallments.ShouldBe(2);
        Ok(BillRaw.Fixed(("total_parcelas", Raw.N(5)))).TotalInstallments.ShouldBeNull();
    }

    [Theory]
    [Trait("AC", "005-20")]
    [InlineData("13")]
    [InlineData("0")]
    [InlineData("x")]
    public void AnnualWithInvalidMonth_IsRejected(string month)
    {
        ErrorOf(BillRaw.Fixed(("periodicidade", Raw.T("ANUAL")), ("mes_execucao", Raw.T(month)))).ShouldBe(BillError.MonthInvalidCreate);
    }

    [Fact]
    [Trait("AC", "005-20")]
    public void Annual_NeedsAMonth_OtherPeriodicitiesIgnoreIt()
    {
        Ok(BillRaw.Fixed(("periodicidade", Raw.T("ANUAL")), ("mes_execucao", Raw.N(3)))).Month.ShouldBe(3);
        Ok(BillRaw.Fixed(("mes_execucao", Raw.N(5)))).Month.ShouldBeNull();
        Ok(BillRaw.Fixed(("mes_execucao", Raw.T("x")))).Month.ShouldBeNull();
        // desvio 074: ANUAL sem mês deixa de ser 500 do CHECK
        ErrorOf(BillRaw.Fixed(("periodicidade", Raw.T("ANUAL")))).ShouldBe(BillError.MonthRequiredForAnnual);
        ErrorOf(BillRaw.Fixed(("periodicidade", Raw.T("ANUAL")), ("mes_execucao", BillRaw.Null))).ShouldBe(BillError.MonthRequiredForAnnual);
    }

    [Fact]
    [Trait("AC", "005-21")]
    public void NotifyDays_DefaultsToThree_WhenAbsentOrUnreadable()
    {
        Ok(BillRaw.Fixed()).NotifyDaysBefore.ShouldBe(3);
        Ok(BillRaw.Fixed(("notificar_antes_dias", Raw.T("abc")))).NotifyDaysBefore.ShouldBe(3);
        Ok(BillRaw.Fixed(("notificar_antes_dias", BillRaw.Null))).NotifyDaysBefore.ShouldBe(3);
        Ok(BillRaw.Fixed(("notificar_antes_dias", Raw.N(0)))).NotifyDaysBefore.ShouldBe(0);
        Ok(BillRaw.Fixed(("notificar_antes_dias", Raw.N(-2)))).NotifyDaysBefore.ShouldBe(-2);
        Ok(BillRaw.Fixed(("notificar_antes_dias", Raw.T("7")))).NotifyDaysBefore.ShouldBe(7);
    }

    [Fact]
    [Trait("AC", "005-22")]
    public void StartDate_DefaultsToTodayWhenAbsentNullOrFalsy_AndTakesAnIsoDate()
    {
        Ok(BillRaw.Fixed(("data_inicio", BillRaw.Null))).StartDate.ShouldBe(BillRaw.Today);
        Ok(BillRaw.Fixed(("data_inicio", Raw.T("")))).StartDate.ShouldBe(BillRaw.Today);
        Ok(BillRaw.Fixed(("data_inicio", Raw.T("2026-02-01")))).StartDate.ShouldBe(new DateOnly(2026, 2, 1));
    }

    [Fact]
    [Trait("AC", "005-22")]
    public void StartDate_NotInYyyyMmDd_IsRejected()
    {
        foreach (var bad in new[] { Raw.T("amanhã"), Raw.T("2026-02-30"), Raw.T("01/02/2026"), Raw.T(" "), Raw.N(5), BillRaw.B(true) })
        {
            ErrorOf(BillRaw.Fixed(("data_inicio", bad))).ShouldBe(BillError.StartDateInvalid);
        }
    }

    [Fact]
    [Trait("AC", "005-23")]
    public void IncludeInReserve_DefaultsToFalse_AcceptsOnlyBooleans()
    {
        Ok(BillRaw.Fixed()).IncludeInReserve.ShouldBeFalse();
        Ok(BillRaw.Fixed(("incluir_na_reserva", BillRaw.B(true)))).IncludeInReserve.ShouldBeTrue();
        Ok(BillRaw.Fixed(("incluir_na_reserva", BillRaw.Null))).IncludeInReserve.ShouldBeFalse();   // desvio 072
        ErrorOf(BillRaw.Fixed(("incluir_na_reserva", Raw.T("true")))).ShouldBe(BillError.ReserveFlagInvalid);
        ErrorOf(BillRaw.Fixed(("incluir_na_reserva", Raw.N(1)))).ShouldBe(BillError.ReserveFlagInvalid);
    }

    [Fact]
    [Trait("AC", "005-25")]
    public void UnknownKeys_AreIgnored()
    {
        var data = Ok(BillRaw.Fixed(("usuario_id", Raw.N(99)), ("ativo", BillRaw.B(false)), ("parcelas_executadas", Raw.N(9)), ("id", Raw.N(1))));

        data.ShouldBe(Ok(BillRaw.Fixed()));
    }

    [Fact]
    [Trait("AC", "005-12")]
    public void WithTwoErrors_TheFirstOfTheSpecOrderAnswers()
    {
        ErrorOf(BillRaw.Without("descricao", ("tipo_agendamento", Raw.T("X")))).ShouldBe(BillError.DescriptionRequired);
        ErrorOf(BillRaw.Fixed(("tipo_agendamento", Raw.T("X")), ("periodicidade", Raw.T("X")))).ShouldBe(BillError.TypeInvalidCreate);
        ErrorOf(BillRaw.Without("subcategoria_id", ("conta_id", RawValue.Absent))).ShouldBe(BillError.SubcategoryRequired);
        ErrorOf(BillRaw.Without("valor_previsto", ("dia_execucao", Raw.T("abc")))).ShouldBe(BillError.DayInvalid);
        ErrorOf(BillRaw.Fixed(("valor_previsto", Raw.N(0)), ("periodicidade", Raw.T("ANUAL")))).ShouldBe(BillError.AmountNotPositive);
        ErrorOf(BillRaw.Fixed(("periodicidade", Raw.T("ANUAL")), ("data_inicio", Raw.T("x")))).ShouldBe(BillError.MonthRequiredForAnnual);
        ErrorOf(BillRaw.Fixed(("data_inicio", Raw.T("x")), ("incluir_na_reserva", Raw.T("x")))).ShouldBe(BillError.StartDateInvalid);
    }

    [Fact]
    [Trait("AC", "005-27")]
    public void AngularCreateBody_IsAccepted()
    {
        var body = Raw.Body(
            ("descricao", Raw.T("Aluguel")), ("tipo_agendamento", Raw.T("FIXO")), ("periodicidade", Raw.T("MENSAL")),
            ("dia_execucao", Raw.N(5)), ("data_inicio", Raw.T("2026-10-04")), ("subcategoria_id", Raw.N(12)),
            ("conta_id", Raw.N(3)), ("notificar_antes_dias", Raw.N(3)), ("incluir_na_reserva", BillRaw.B(true)),
            ("total_parcelas", BillRaw.Null), ("valor_previsto", BillRaw.D(1500.5)));

        var data = Ok(body);

        data.Amount.ShouldBe(1500.5m);
        data.IncludeInReserve.ShouldBeTrue();
        data.TotalInstallments.ShouldBeNull();
    }
}
