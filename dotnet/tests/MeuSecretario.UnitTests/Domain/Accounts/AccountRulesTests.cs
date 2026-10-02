using System.Numerics;
using MeuSecretario.Domain.Accounts;

namespace MeuSecretario.UnitTests.Domain.Accounts;

internal static class Raw
{
    /// <summary>Quando uma chave se repete, vale a última (permite sobrescrever os campos-base).</summary>
    public static RawBody Body(params (string Key, RawValue Value)[] items)
    {
        var values = new Dictionary<string, RawValue>();
        foreach (var (key, value) in items)
        {
            values[key] = value;
        }
        return new RawBody(values);
    }

    public static RawValue T(string value) => RawValue.Text(value);

    public static RawValue N(int value) => RawValue.Whole(new BigInteger(value));

    public static RawBody Card(params (string Key, RawValue Value)[] extra) =>
        Body([("nome_conta", T("Nubank")), ("tipo_conta", T(AccountTypes.Card)), ("dia_fechamento", N(5)), ("dia_vencimento", N(10)), .. extra]);

    public static RawBody Simple(params (string Key, RawValue Value)[] extra) =>
        Body([("nome_conta", T("Carteira")), ("tipo_conta", T("Dinheiro")), .. extra]);
}

public class CreateRulesTests
{
    private static AccountError? ErrorOf(RawBody? body) =>
        CreateRules.Validate(body) is CreateValidation.Rejected error ? error.Reason : null;

    private static NewAccountData Ok(RawBody body) =>
        CreateRules.Validate(body).ShouldBeOfType<CreateValidation.Ok>().Data;

    [Fact]
    [Trait("AC", "002-12")]
    public void NullOrEmptyBody_IsDataMissing()
    {
        ErrorOf(null).ShouldBe(AccountError.DataMissing);
        ErrorOf(Raw.Body()).ShouldBe(AccountError.DataMissing);
    }

    [Theory]
    [Trait("AC", "002-13")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingBlankOrNonTextName_IsNameRequired_EvenWithBadType(string? name)
    {
        var body = name is null
            ? Raw.Body(("tipo_conta", Raw.T("invalido")))
            : Raw.Body(("nome_conta", Raw.T(name)), ("tipo_conta", Raw.T("invalido")));

        ErrorOf(body).ShouldBe(AccountError.NameRequired);
    }

    [Fact]
    [Trait("AC", "002-13")]
    public void NonTextName_IsNameRequired()
    {
        ErrorOf(Raw.Body(("nome_conta", RawValue.Null), ("tipo_conta", Raw.T("Dinheiro")))).ShouldBe(AccountError.NameRequired);
        ErrorOf(Raw.Body(("nome_conta", Raw.N(123)), ("tipo_conta", Raw.T("Dinheiro")))).ShouldBe(AccountError.NameRequired);
        ErrorOf(Raw.Body(("nome_conta", RawValue.Other), ("tipo_conta", Raw.T("Dinheiro")))).ShouldBe(AccountError.NameRequired);
    }

    [Fact]
    [Trait("AC", "002-14")]
    public void MissingBlankOrNonTextType_IsTypeRequired()
    {
        ErrorOf(Raw.Body(("nome_conta", Raw.T("A")))).ShouldBe(AccountError.TypeRequired);
        ErrorOf(Raw.Body(("nome_conta", Raw.T("A")), ("tipo_conta", Raw.T("  ")))).ShouldBe(AccountError.TypeRequired);
        ErrorOf(Raw.Body(("nome_conta", Raw.T("A")), ("tipo_conta", RawValue.Null))).ShouldBe(AccountError.TypeRequired);
        ErrorOf(Raw.Body(("nome_conta", Raw.T("A")), ("tipo_conta", Raw.N(1)))).ShouldBe(AccountError.TypeRequired);
    }

    [Theory]
    [Trait("AC", "002-15")]
    [InlineData("corrente")]
    [InlineData("conta corrente")]
    [InlineData("Conta  Corrente")]
    public void InvalidType_IsTypeInvalid(string type)
    {
        ErrorOf(Raw.Body(("nome_conta", Raw.T("A")), ("tipo_conta", Raw.T(type)))).ShouldBe(AccountError.TypeInvalid);
    }

    [Fact]
    [Trait("AC", "002-15")]
    public void Type_IsTrimmed()
    {
        Ok(Raw.Body(("nome_conta", Raw.T("A")), ("tipo_conta", Raw.T(" Dinheiro ")))).TipoConta.ShouldBe("Dinheiro");
    }

    [Fact]
    [Trait("AC", "002-10")]
    public void MinimalBody_AppliesTheDefaults()
    {
        var data = Ok(Raw.Body(("nome_conta", Raw.T("Nubank")), ("tipo_conta", Raw.T("Conta Corrente"))));

        data.ShouldBe(new NewAccountData("Nubank", "Conta Corrente", 0m, "#3B82F6", "wallet", null, null, null));
    }

    [Fact]
    [Trait("AC", "002-24")]
    public void ExplicitNullColorAndIcon_StayNull_AndDefaultsOnlyWhenAbsent()
    {
        var data = Ok(Raw.Simple(("cor_hex", RawValue.Null), ("icone", RawValue.Null)));

        data.CorHex.ShouldBeNull();
        data.Icone.ShouldBeNull();
    }

    [Theory]
    [Trait("AC", "002-16")]
    [InlineData("  Nubank  ", "Nubank")]
    public void Name_IsTrimmed(string raw, string expected)
    {
        Ok(Raw.Body(("nome_conta", Raw.T(raw)), ("tipo_conta", Raw.T("Dinheiro")))).NomeConta.ShouldBe(expected);
    }

    [Fact]
    [Trait("AC", "002-16")]
    public void Name_IsCutAt100CodePointsAfterValidation()
    {
        var data = Ok(Raw.Body(("nome_conta", Raw.T(new string('x', 120))), ("tipo_conta", Raw.T("Dinheiro"))));

        data.NomeConta.Length.ShouldBe(100);
    }

    [Fact]
    [Trait("AC", "002-17")]
    public void Balance_AcceptsNumbersTextAndBool_AndNullMeansZero()
    {
        Ok(Raw.Simple(("saldo_inicial", Raw.T("12.5")))).SaldoInicial.ShouldBe(12.5m);
        Ok(Raw.Simple(("saldo_inicial", Raw.N(12)))).SaldoInicial.ShouldBe(12m);
        Ok(Raw.Simple(("saldo_inicial", RawValue.Fractional(-3.5)))).SaldoInicial.ShouldBe(-3.5m);
        Ok(Raw.Simple(("saldo_inicial", RawValue.Bool(true)))).SaldoInicial.ShouldBe(1m);
        Ok(Raw.Simple(("saldo_inicial", Raw.T(" 7 ")))).SaldoInicial.ShouldBe(7m);
        Ok(Raw.Simple(("saldo_inicial", RawValue.Null))).SaldoInicial.ShouldBe(0m);
    }

    [Theory]
    [Trait("AC", "002-17")]
    [Trait("AC", "002-28")]
    [InlineData("abc")]
    [InlineData("1,5")]
    [InlineData("inf")]
    [InlineData("nan")]
    [InlineData("1e30")]
    public void InvalidBalance_IsBalanceInvalid(string text)
    {
        ErrorOf(Raw.Simple(("saldo_inicial", Raw.T(text)))).ShouldBe(AccountError.BalanceInvalid);
    }

    [Fact]
    [Trait("AC", "002-17")]
    public void NonScalarBalance_IsBalanceInvalid()
    {
        ErrorOf(Raw.Simple(("saldo_inicial", RawValue.Other))).ShouldBe(AccountError.BalanceInvalid);
    }

    [Theory]
    [Trait("AC", "002-24")]
    [InlineData("azul")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("3B82F6")]
    [InlineData("#GGGGGG")]
    [InlineData("#123456\n")]
    public void InvalidColor_IsColorInvalid(string color)
    {
        ErrorOf(Raw.Simple(("cor_hex", Raw.T(color)))).ShouldBe(AccountError.ColorInvalid);
    }

    [Fact]
    [Trait("AC", "002-24")]
    public void Color_AcceptsAnyHexCase_AndRejectsNumbers()
    {
        Ok(Raw.Simple(("cor_hex", Raw.T("#aBc123")))).CorHex.ShouldBe("#aBc123");
        ErrorOf(Raw.Simple(("cor_hex", Raw.N(123)))).ShouldBe(AccountError.ColorInvalid);
    }

    [Fact]
    [Trait("AC", "002-24")]
    public void Icon_Allows50CodePoints_Not51_AndRejectsNumbers()
    {
        Ok(Raw.Simple(("icone", Raw.T(new string('i', 50))))).Icone!.Length.ShouldBe(50);
        Ok(Raw.Simple(("icone", Raw.T(string.Concat(Enumerable.Repeat("😀", 50)))))).Icone.ShouldNotBeNull();
        ErrorOf(Raw.Simple(("icone", Raw.T(new string('i', 51))))).ShouldBe(AccountError.IconInvalid);
        ErrorOf(Raw.Simple(("icone", Raw.N(1)))).ShouldBe(AccountError.IconInvalid);
    }

    [Fact]
    [Trait("AC", "002-18")]
    public void Limit_IsValidatedInAnyType()
    {
        ErrorOf(Raw.Simple(("limite_credito", Raw.T("abc")))).ShouldBe(AccountError.LimitInvalid);
        ErrorOf(Raw.Simple(("limite_credito", Raw.T("1e30")))).ShouldBe(AccountError.LimitInvalid);
        Ok(Raw.Simple(("limite_credito", Raw.N(500)))).LimiteCredito.ShouldBe(500m);
        Ok(Raw.Simple(("limite_credito", RawValue.Null))).LimiteCredito.ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "002-19")]
    public void DaysOutsideCard_AreRefused_WhateverTheValue()
    {
        ErrorOf(Raw.Simple(("dia_fechamento", Raw.N(5)))).ShouldBe(AccountError.DaysOnlyForCard);
        ErrorOf(Raw.Simple(("dia_vencimento", Raw.T("abc")))).ShouldBe(AccountError.DaysOnlyForCard);
        ErrorOf(Raw.Body(("nome_conta", Raw.T("A")), ("tipo_conta", Raw.T("Conta Corrente")), ("dia_fechamento", Raw.N(5)))).ShouldBe(AccountError.DaysOnlyForCard);
    }

    [Fact]
    [Trait("AC", "002-19")]
    public void NullDaysOutsideCard_AreAccepted()
    {
        var data = Ok(Raw.Simple(("dia_fechamento", RawValue.Null), ("dia_vencimento", RawValue.Null)));

        data.DiaFechamento.ShouldBeNull();
        data.DiaVencimento.ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "002-11")]
    public void ValidCard_KeepsLimitAndDays()
    {
        var data = Ok(Raw.Card(("limite_credito", Raw.N(5000))));

        data.ShouldBe(new NewAccountData("Nubank", AccountTypes.Card, 0m, "#3B82F6", "wallet", 5000m, 5, 10));
    }

    [Theory]
    [Trait("AC", "002-18")]
    [InlineData(0)]
    [InlineData(32)]
    public void ClosingDay_OutOfRange_IsClosingDayInvalid(int day)
    {
        ErrorOf(Raw.Card(("dia_fechamento", Raw.N(day)))).ShouldBe(AccountError.ClosingDayInvalid);
    }

    [Fact]
    [Trait("AC", "002-18")]
    public void DueDay_Invalid_IsDueDayInvalid()
    {
        ErrorOf(Raw.Card(("dia_vencimento", Raw.N(0)))).ShouldBe(AccountError.DueDayInvalid);
        ErrorOf(Raw.Card(("dia_vencimento", Raw.T("x")))).ShouldBe(AccountError.DueDayInvalid);
        ErrorOf(Raw.Card(("dia_vencimento", RawValue.Other))).ShouldBe(AccountError.DueDayInvalid);
    }

    [Fact]
    [Trait("AC", "002-18")]
    public void CardErrors_FollowTheOrderLimitClosingDue()
    {
        var all = Raw.Card(("limite_credito", Raw.T("abc")), ("dia_fechamento", Raw.N(0)), ("dia_vencimento", Raw.N(0)));
        ErrorOf(all).ShouldBe(AccountError.LimitInvalid);

        var noLimit = Raw.Card(("dia_fechamento", Raw.N(0)), ("dia_vencimento", Raw.N(0)));
        ErrorOf(noLimit).ShouldBe(AccountError.ClosingDayInvalid);
    }

    [Fact]
    [Trait("AC", "002-18")]
    public void Days_AreConvertedLikePythonInt()
    {
        var data = Ok(Raw.Card(("dia_fechamento", RawValue.Fractional(10.9)), ("dia_vencimento", Raw.T("12"))));

        data.DiaFechamento.ShouldBe(10);
        data.DiaVencimento.ShouldBe(12);
        Ok(Raw.Card(("dia_fechamento", RawValue.Bool(true)))).DiaFechamento.ShouldBe(1);
    }

    [Theory]
    [Trait("AC", "002-29")]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void CardWithoutBothDays_IsCardNeedsDays(bool nullClosing, bool nullDue)
    {
        var closing = nullClosing ? RawValue.Null : Raw.N(5);
        var due = nullDue ? RawValue.Null : Raw.N(10);

        ErrorOf(Raw.Body(("nome_conta", Raw.T("N")), ("tipo_conta", Raw.T(AccountTypes.Card)), ("dia_fechamento", closing), ("dia_vencimento", due)))
            .ShouldBe(AccountError.CardNeedsDays);
    }

    [Fact]
    [Trait("AC", "002-29")]
    public void CardWithAbsentDays_IsCardNeedsDays()
    {
        ErrorOf(Raw.Body(("nome_conta", Raw.T("N")), ("tipo_conta", Raw.T(AccountTypes.Card)))).ShouldBe(AccountError.CardNeedsDays);
        ErrorOf(Raw.Body(("nome_conta", Raw.T("N")), ("tipo_conta", Raw.T(AccountTypes.Card)), ("dia_fechamento", Raw.N(5)))).ShouldBe(AccountError.CardNeedsDays);
    }

    [Fact]
    [Trait("AC", "002-18")]
    public void InvalidDayWinsOverMissingDay()
    {
        ErrorOf(Raw.Body(("nome_conta", Raw.T("N")), ("tipo_conta", Raw.T(AccountTypes.Card)), ("dia_fechamento", Raw.N(40)))).ShouldBe(AccountError.ClosingDayInvalid);
    }

    [Fact]
    [Trait("AC", "002-25")]
    public void UnknownKeys_AreIgnored()
    {
        var data = Ok(Raw.Simple(("usuario_id", Raw.N(99)), ("ativa", RawValue.Bool(false)), ("banco", Raw.T("x"))));

        data.NomeConta.ShouldBe("Carteira");
    }
}

public class UpdateRulesTests
{
    [Fact]
    [Trait("AC", "002-42")]
    public void ReadName_AbsentIsNotPresent_AndTextIsNormalized()
    {
        UpdateRules.ReadName(Raw.Body(("saldo_inicial", Raw.N(1)))).ShouldBeOfType<NameStep.NotPresent>();

        var step = UpdateRules.ReadName(Raw.Body(("nome_conta", Raw.T("  Novo  "))));
        step.ShouldBeOfType<NameStep.Value>().Name.ShouldBe("Novo");
    }

    [Theory]
    [Trait("AC", "002-43")]
    [InlineData("")]
    [InlineData("   ")]
    public void ReadName_BlankIsNameEmpty(string text)
    {
        UpdateRules.ReadName(Raw.Body(("nome_conta", Raw.T(text)))).ShouldBeOfType<NameStep.Failed>().Reason.ShouldBe(AccountError.NameEmpty);
    }

    [Fact]
    [Trait("AC", "002-56")]
    public void ReadName_NonTextIsNameEmpty()
    {
        UpdateRules.ReadName(Raw.Body(("nome_conta", RawValue.Null))).ShouldBeOfType<NameStep.Failed>().Reason.ShouldBe(AccountError.NameEmpty);
        UpdateRules.ReadName(Raw.Body(("nome_conta", Raw.N(5)))).ShouldBeOfType<NameStep.Failed>().Reason.ShouldBe(AccountError.NameEmpty);
    }

    [Fact]
    [Trait("AC", "002-43")]
    public void ReadName_CutsAt100CodePoints()
    {
        UpdateRules.ReadName(Raw.Body(("nome_conta", Raw.T(" " + new string('x', 150)))))
            .ShouldBeOfType<NameStep.Value>().Name.Length.ShouldBe(100);
    }

    private static AccountError? FieldError(RawBody body) =>
        UpdateRules.ReadFields(body) is FieldsResult.Rejected e ? e.Reason : null;

    private static AccountChanges Changes(RawBody body) =>
        UpdateRules.ReadFields(body).ShouldBeOfType<FieldsResult.Ok>().Changes;

    [Fact]
    [Trait("AC", "002-51")]
    public void ReadFields_NoRecognizedKey_YieldsEmptyChanges()
    {
        Changes(Raw.Body(("foo", Raw.N(1)))).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "002-45")]
    public void Type_MustBeValid_Text()
    {
        Changes(Raw.Body(("tipo_conta", Raw.T(" Dinheiro ")))).Tipo.ShouldBe(Change.Set<string>("Dinheiro"));
        FieldError(Raw.Body(("tipo_conta", Raw.T("")))).ShouldBe(AccountError.TypeInvalid);
        FieldError(Raw.Body(("tipo_conta", Raw.T("x")))).ShouldBe(AccountError.TypeInvalid);
        FieldError(Raw.Body(("tipo_conta", RawValue.Null))).ShouldBe(AccountError.TypeInvalid);
        FieldError(Raw.Body(("tipo_conta", Raw.N(3)))).ShouldBe(AccountError.TypeInvalid);
    }

    [Fact]
    [Trait("AC", "002-46")]
    public void Balance_NullIsInvalid_AndNumbersAreSet()
    {
        FieldError(Raw.Body(("saldo_inicial", RawValue.Null))).ShouldBe(AccountError.BalanceInvalid);
        FieldError(Raw.Body(("saldo_inicial", Raw.T("abc")))).ShouldBe(AccountError.BalanceInvalid);
        FieldError(Raw.Body(("saldo_inicial", Raw.T("nan")))).ShouldBe(AccountError.BalanceInvalid);
        FieldError(Raw.Body(("saldo_inicial", RawValue.Fractional(1e30)))).ShouldBe(AccountError.BalanceInvalid);
        Changes(Raw.Body(("saldo_inicial", RawValue.Fractional(250.75)))).Saldo.ShouldBe(Change.Set<decimal>(250.75m));
    }

    [Fact]
    [Trait("AC", "002-47")]
    public void ColorAndIcon_NullClears_TextValidated()
    {
        var changes = Changes(Raw.Body(("cor_hex", RawValue.Null), ("icone", RawValue.Null)));
        changes.Cor.IsSet.ShouldBeTrue();
        changes.Cor.Value.ShouldBeNull();
        changes.Icone.IsSet.ShouldBeTrue();
        changes.Icone.Value.ShouldBeNull();

        Changes(Raw.Body(("cor_hex", Raw.T("#FFFFFF")), ("icone", Raw.T("star")))).Cor.Value.ShouldBe("#FFFFFF");
        FieldError(Raw.Body(("cor_hex", Raw.T("branco")))).ShouldBe(AccountError.ColorInvalid);
        FieldError(Raw.Body(("icone", Raw.T(new string('i', 51))))).ShouldBe(AccountError.IconInvalid);
    }

    [Fact]
    [Trait("AC", "002-48")]
    public void Limit_NullClears_ZeroIsKept_InvalidIsRejected()
    {
        var cleared = Changes(Raw.Body(("limite_credito", RawValue.Null))).Limite;
        cleared.IsSet.ShouldBeTrue();
        cleared.Value.ShouldBeNull();
        Changes(Raw.Body(("limite_credito", Raw.N(0)))).Limite.Value.ShouldBe(0m);
        FieldError(Raw.Body(("limite_credito", Raw.T("abc")))).ShouldBe(AccountError.LimitInvalid);
    }

    [Fact]
    [Trait("AC", "002-49")]
    public void Days_NullClears_RangeIsChecked_OrderIsLimitClosingDue()
    {
        var changes = Changes(Raw.Body(("dia_fechamento", RawValue.Null), ("dia_vencimento", Raw.N(7))));
        changes.Fechamento.IsSet.ShouldBeTrue();
        changes.Fechamento.Value.ShouldBeNull();
        changes.Vencimento.Value.ShouldBe(7);

        FieldError(Raw.Body(("dia_fechamento", Raw.N(0)))).ShouldBe(AccountError.ClosingDayInvalid);
        FieldError(Raw.Body(("dia_vencimento", Raw.T("x")))).ShouldBe(AccountError.DueDayInvalid);
        FieldError(Raw.Body(("dia_fechamento", Raw.N(0)), ("dia_vencimento", Raw.N(0)), ("limite_credito", Raw.T("x")))).ShouldBe(AccountError.LimitInvalid);
        FieldError(Raw.Body(("dia_fechamento", Raw.N(0)), ("dia_vencimento", Raw.N(0)))).ShouldBe(AccountError.ClosingDayInvalid);
    }

    [Fact]
    [Trait("AC", "002-50")]
    public void FieldErrors_FollowTheSpecOrder()
    {
        // tipo antes de saldo, saldo antes de cor, cor antes de ícone, ícone antes de limite.
        FieldError(Raw.Body(("tipo_conta", Raw.T("x")), ("saldo_inicial", Raw.T("x")))).ShouldBe(AccountError.TypeInvalid);
        FieldError(Raw.Body(("saldo_inicial", Raw.T("x")), ("cor_hex", Raw.T("x")))).ShouldBe(AccountError.BalanceInvalid);
        FieldError(Raw.Body(("cor_hex", Raw.T("x")), ("icone", Raw.N(1)))).ShouldBe(AccountError.ColorInvalid);
        FieldError(Raw.Body(("icone", Raw.N(1)), ("limite_credito", Raw.T("x")))).ShouldBe(AccountError.IconInvalid);
    }

    [Fact]
    [Trait("AC", "002-51")]
    public void WithName_MakesTheChangesNonEmpty()
    {
        var empty = Changes(Raw.Body(("foo", Raw.N(1))));

        empty.WithName("Novo").IsEmpty.ShouldBeFalse();
        empty.WithName("Novo").Nome.ShouldBe(Change.Set<string>("Novo"));
    }

    [Fact]
    [Trait("AC", "002-52")]
    public void Consistency_DayOnNonCardAccount_IsDaysOnlyForCard()
    {
        var changes = Changes(Raw.Body(("dia_fechamento", Raw.N(5))));

        UpdateRules.CheckConsistency("Dinheiro", null, null, changes).ShouldBe(AccountError.DaysOnlyForCard);
    }

    [Fact]
    [Trait("AC", "002-52")]
    public void Consistency_TypeChangeAwayFromCardWithDays_IsRefused_UnlessDaysAreClearedTogether()
    {
        var only = Changes(Raw.Body(("tipo_conta", Raw.T("Dinheiro"))));
        UpdateRules.CheckConsistency(AccountTypes.Card, 5, 10, only).ShouldBe(AccountError.DaysOnlyForCard);

        var together = Changes(Raw.Body(("tipo_conta", Raw.T("Dinheiro")), ("dia_fechamento", RawValue.Null), ("dia_vencimento", RawValue.Null)));
        UpdateRules.CheckConsistency(AccountTypes.Card, 5, 10, together).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "002-54")]
    public void Consistency_NullDaysOnCard_IsCardNeedsDays_ButFineOnNonCard()
    {
        var angular = Changes(Raw.Body(("dia_vencimento", RawValue.Null), ("dia_fechamento", RawValue.Null)));

        UpdateRules.CheckConsistency(AccountTypes.Card, 5, 10, angular).ShouldBe(AccountError.CardNeedsDays);
        UpdateRules.CheckConsistency("Dinheiro", null, null, angular).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "002-55")]
    public void Consistency_ChangeToCard_NeedsBothDays()
    {
        UpdateRules.CheckConsistency("Dinheiro", null, null, Changes(Raw.Body(("tipo_conta", Raw.T(AccountTypes.Card)))))
            .ShouldBe(AccountError.CardNeedsDays);
        UpdateRules.CheckConsistency("Dinheiro", null, null, Changes(Raw.Body(("tipo_conta", Raw.T(AccountTypes.Card)), ("dia_fechamento", Raw.N(5)))))
            .ShouldBe(AccountError.CardNeedsDays);
        UpdateRules.CheckConsistency("Dinheiro", null, null, Changes(Raw.Body(("tipo_conta", Raw.T(AccountTypes.Card)), ("dia_fechamento", Raw.N(5)), ("dia_vencimento", Raw.N(9)))))
            .ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "002-55")]
    public void Consistency_ChangesThatDoNotTouchTypeOrDays_AreNotChecked()
    {
        var nameOnly = Changes(Raw.Body(("saldo_inicial", Raw.N(1))));

        UpdateRules.CheckConsistency(AccountTypes.Card, null, null, nameOnly).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "002-52")]
    public void Consistency_UsesCurrentValuesForWhatTheBodyDoesNotTouch()
    {
        var onlyClosing = Changes(Raw.Body(("dia_fechamento", Raw.N(8))));

        UpdateRules.CheckConsistency(AccountTypes.Card, 5, 10, onlyClosing).ShouldBeNull();
        UpdateRules.CheckConsistency(AccountTypes.Card, 5, null, onlyClosing).ShouldBe(AccountError.CardNeedsDays);
    }
}
