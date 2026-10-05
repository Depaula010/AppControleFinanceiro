using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.Settings;

namespace MeuSecretario.UnitTests.Domain.Settings;

internal static class SettingsBodies
{
    public static RawBody Of(params (string Key, RawValue Value)[] values) =>
        new(values.ToDictionary(v => v.Key, v => v.Value));

    public static RawBody Empty => Of();

    public static RawValue Obj(params (string Key, RawValue Value)[] values) => new ObjectValue(Of(values));
}

/// <summary>Spec 007 Â§3.2: ordem das validaÃ§Ãµes do perfil e desvios 084 e 085.</summary>
public class ProfileRulesTests
{
    private static ProfileError Rejected(RawBody? body) =>
        ProfileRules.Validate(body).ShouldBeOfType<ProfileValidation.Rejected>().Error;

    private static ProfileChanges Ok(RawBody body) =>
        ProfileRules.Validate(body).ShouldBeOfType<ProfileValidation.Ok>().Changes;

    [Fact]
    [Trait("AC", "007-10")]
    public void NothingToUpdate_WhenNoKnownKey()
    {
        Rejected(null).ShouldBe(ProfileError.NothingToUpdate);
        Rejected(SettingsBodies.Empty).ShouldBe(ProfileError.NothingToUpdate);
        Rejected(SettingsBodies.Of(("id", RawValue.Whole(1)), ("senha", RawValue.Text("x")), ("numero_whatsapp", RawValue.Text("1"))))
            .ShouldBe(ProfileError.NothingToUpdate);
    }

    [Theory]
    [Trait("AC", "007-11")]
    [InlineData("A")]
    [InlineData("")]
    [InlineData("  A ")]
    [InlineData("   ")]
    public void ShortName_AfterTrim_IsRejected(string name)
    {
        Rejected(SettingsBodies.Of(("nome", RawValue.Text(name)))).ShouldBe(ProfileError.NameTooShort);
    }

    [Fact]
    [Trait("AC", "007-11")]
    public void NullName_IsRejectedAsTooShort_AndOtherTypesAsInvalidData()
    {
        Rejected(SettingsBodies.Of(("nome", RawValue.Null))).ShouldBe(ProfileError.NameTooShort);
        Rejected(SettingsBodies.Of(("nome", RawValue.Whole(5)))).ShouldBe(ProfileError.InvalidData);
    }

    [Fact]
    [Trait("AC", "007-11")]
    public void Name_IsStoredTrimmed_Desvio085()
    {
        Ok(SettingsBodies.Of(("nome", RawValue.Text(" Ana ")))).Name.ShouldBe(new FieldChange<string?>("Ana"));
    }

    [Theory]
    [Trait("AC", "007-12")]
    [InlineData("SPX")]
    [InlineData("S")]
    [InlineData(" ")]
    public void State_WithWrongLength_IsRejected(string state)
    {
        Rejected(SettingsBodies.Of(("estado", RawValue.Text(state)))).ShouldBe(ProfileError.StateLength);
    }

    [Fact]
    [Trait("AC", "007-12")]
    public void EmptyState_BecomesNull_AndIsNotUppercased()
    {
        Ok(SettingsBodies.Of(("estado", RawValue.Text("")))).State.ShouldBe(new FieldChange<string?>(null));
        Ok(SettingsBodies.Of(("estado", RawValue.Text("sp")))).State.ShouldBe(new FieldChange<string?>("sp"));
        Ok(SettingsBodies.Of(("estado", RawValue.Null))).State.ShouldBe(new FieldChange<string?>(null));
    }

    [Fact]
    [Trait("AC", "007-12")]
    public void StateOfAnotherType_IsInvalidData()
    {
        Rejected(SettingsBodies.Of(("estado", RawValue.Whole(12)))).ShouldBe(ProfileError.InvalidData);
    }

    [Theory]
    [Trait("AC", "007-13")]
    [InlineData("x")]
    [InlineData("5.5")]
    [InlineData("")]
    public void MonthsNotConvertibleByInt_IsRejected(string text)
    {
        Rejected(SettingsBodies.Of(("meses_reserva_emergencia", RawValue.Text(text)))).ShouldBe(ProfileError.MonthsNotInteger);
    }

    [Fact]
    [Trait("AC", "007-13")]
    public void MonthsAsList_IsNotAnInteger()
    {
        Rejected(SettingsBodies.Of(("meses_reserva_emergencia", new ListValue(0)))).ShouldBe(ProfileError.MonthsNotInteger);
    }

    [Theory]
    [Trait("AC", "007-13")]
    [InlineData(0)]
    [InlineData(37)]
    [InlineData(-3)]
    public void MonthsOutOfRange_IsRejected(int months)
    {
        Rejected(SettingsBodies.Of(("meses_reserva_emergencia", RawValue.Whole(months)))).ShouldBe(ProfileError.MonthsRange);
        Rejected(SettingsBodies.Of(("meses_reserva_emergencia", RawValue.Text("40")))).ShouldBe(ProfileError.MonthsRange);
    }

    public static TheoryData<RawValue, int> ValidMonths => new()
    {
        { RawValue.Text("12"), 12 },
        { RawValue.Text(" 12 "), 12 },
        { RawValue.Fractional(12.9), 12 },
        { RawValue.Bool(true), 1 },
        { RawValue.Whole(1), 1 },
        { RawValue.Whole(36), 36 },
    };

    [Theory]
    [MemberData(nameof(ValidMonths))]
    [Trait("AC", "007-13")]
    public void MonthsConvertedLikePythonInt(RawValue raw, int expected)
    {
        Ok(SettingsBodies.Of(("meses_reserva_emergencia", raw))).ReserveMonths.ShouldBe(new FieldChange<int?>(expected));
    }

    [Fact]
    [Trait("AC", "007-13")]
    public void NullMonths_IsAccepted_AndWritesNull()
    {
        Ok(SettingsBodies.Of(("meses_reserva_emergencia", RawValue.Null))).ReserveMonths.ShouldBe(new FieldChange<int?>(null));
    }

    [Fact]
    [Trait("AC", "007-14")]
    public void Order_MonthsConversion_BeforeName_BeforeState_BeforeMonthsRange()
    {
        Rejected(SettingsBodies.Of(("nome", RawValue.Text("A")), ("meses_reserva_emergencia", RawValue.Text("x"))))
            .ShouldBe(ProfileError.MonthsNotInteger);
        Rejected(SettingsBodies.Of(("nome", RawValue.Text("A")), ("estado", RawValue.Text("S"))))
            .ShouldBe(ProfileError.NameTooShort);
        Rejected(SettingsBodies.Of(("estado", RawValue.Text("S")), ("meses_reserva_emergencia", RawValue.Whole(99))))
            .ShouldBe(ProfileError.StateLength);
    }

    [Fact]
    [Trait("AC", "007-15")]
    public void EmptyStrings_BecomeNull_ButABlankIsKept()
    {
        var changes = Ok(SettingsBodies.Of(("email", RawValue.Text("")), ("cidade", RawValue.Text(" "))));

        changes.Email.ShouldBe(new FieldChange<string?>(null));
        changes.City.ShouldBe(new FieldChange<string?>(" "));
    }

    [Fact]
    [Trait("AC", "007-16")]
    public void IgnoredKeys_DoNotEnterTheChanges()
    {
        var changes = Ok(SettingsBodies.Of(
            ("id", RawValue.Whole(9)), ("numero_whatsapp", RawValue.Text("1")), ("nome", RawValue.Text("Ana")),
            ("fuso_horario", RawValue.Text("America/Sao_Paulo"))));

        changes.Name.ShouldNotBeNull();
        changes.TimeZone.ShouldBe(new FieldChange<string?>("America/Sao_Paulo"));
        changes.Email.ShouldBeNull();
        changes.City.ShouldBeNull();
        changes.State.ShouldBeNull();
        changes.ReserveMonths.ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "007-17")]
    public void TooLongValues_AreRejected_Desvio085()
    {
        Rejected(SettingsBodies.Of(("nome", RawValue.Text(new string('a', 256))))).ShouldBe(ProfileError.NameTooLong);
        Rejected(SettingsBodies.Of(("email", RawValue.Text(new string('a', 256))))).ShouldBe(ProfileError.EmailTooLong);
        Rejected(SettingsBodies.Of(("cidade", RawValue.Text(new string('a', 101))))).ShouldBe(ProfileError.CityTooLong);
        Ok(SettingsBodies.Of(("nome", RawValue.Text(new string('a', 255))), ("cidade", RawValue.Text(new string('a', 100)))));
    }

    [Fact]
    [Trait("AC", "007-17")]
    public void Length_CountsCodePoints_NotUtf16Units()
    {
        // 255 emojis = 510 unidades UTF-16, mas 255 pontos de cÃ³digo: cabe no varchar(255).
        Ok(SettingsBodies.Of(("nome", RawValue.Text(string.Concat(Enumerable.Repeat("\U0001F600", 255))))));
        Rejected(SettingsBodies.Of(("nome", RawValue.Text(string.Concat(Enumerable.Repeat("\U0001F600", 256)))))).ShouldBe(ProfileError.NameTooLong);
    }

    [Theory]
    [Trait("AC", "007-17")]
    [InlineData("Marte/Olympus")]
    [InlineData("E. South America Standard Time")]
    [InlineData("")]
    [InlineData("abc")]
    public void UnknownTimeZone_IsRejected(string id)
    {
        Rejected(SettingsBodies.Of(("fuso_horario", RawValue.Text(id)))).ShouldBe(ProfileError.InvalidTimeZone);
    }

    [Fact]
    [Trait("AC", "007-17")]
    public void NullTimeZone_IsRejected_AndANumberIsInvalidData()
    {
        Rejected(SettingsBodies.Of(("fuso_horario", RawValue.Null))).ShouldBe(ProfileError.InvalidTimeZone);
        Rejected(SettingsBodies.Of(("fuso_horario", RawValue.Whole(3)))).ShouldBe(ProfileError.InvalidData);
    }

    [Fact]
    [Trait("AC", "007-17")]
    public void KnownIanaTimeZones_AreAccepted()
    {
        Ok(SettingsBodies.Of(("fuso_horario", RawValue.Text("America/Sao_Paulo")))).TimeZone.ShouldNotBeNull();
        Ok(SettingsBodies.Of(("fuso_horario", RawValue.Text("America/Manaus")))).TimeZone.ShouldNotBeNull();
    }

    [Fact]
    [Trait("AC", "007-17")]
    public void EmailCityAndNameOfAnotherType_AreInvalidData_Desvio084()
    {
        Rejected(SettingsBodies.Of(("email", RawValue.Whole(1)))).ShouldBe(ProfileError.InvalidData);
        Rejected(SettingsBodies.Of(("cidade", RawValue.Bool(true)))).ShouldBe(ProfileError.InvalidData);
    }
}
