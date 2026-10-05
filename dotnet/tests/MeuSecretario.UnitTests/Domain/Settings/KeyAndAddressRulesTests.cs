using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.Settings;

namespace MeuSecretario.UnitTests.Domain.Settings;

public class PyTruthTests
{
    public static TheoryData<RawValue, bool> Cases => new()
    {
        { RawValue.Absent, false },
        { RawValue.Null, false },
        { RawValue.Bool(false), false },
        { RawValue.Bool(true), true },
        { RawValue.Whole(0), false },
        { RawValue.Whole(-1), true },
        { RawValue.Fractional(0.0), false },
        { RawValue.Fractional(0.1), true },
        { RawValue.Text(""), false },
        { RawValue.Text(" "), true },
        { RawValue.Text("false"), true },
        { new ListValue(0), false },
        { new ListValue(2), true },
        { SettingsBodies.Obj(), false },
        { SettingsBodies.Obj(("a", RawValue.Null)), true },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void IsTruthy_FollowsPython(RawValue value, bool expected) => PyTruth.IsTruthy(value).ShouldBe(expected);
}

/// <summary>Spec 007 §3.4 e §3.5: leitura de provider, useOwnKey e key.</summary>
public class ApiKeyRulesTests
{
    private static SaveKeyError SaveRejected(RawBody? body) =>
        ApiKeyRules.ValidateSave(body).ShouldBeOfType<SaveKeyValidation.Rejected>().Error;

    private static SaveKeyValidation.Ok SaveOk(RawBody body) => ApiKeyRules.ValidateSave(body).ShouldBeOfType<SaveKeyValidation.Ok>();

    [Theory]
    [Trait("AC", "007-25")]
    [InlineData("openai")]
    [InlineData("GEMINI")]
    [InlineData(" gemini")]
    [InlineData("")]
    public void SaveWithInvalidProvider_IsRejected(string provider)
    {
        SaveRejected(SettingsBodies.Of(("type", RawValue.Text(provider)))).ShouldBe(SaveKeyError.InvalidProvider);
    }

    [Fact]
    [Trait("AC", "007-25")]
    public void SaveWithProviderOfAnotherTypeOrAbsent_IsInvalidProvider()
    {
        SaveRejected(null).ShouldBe(SaveKeyError.InvalidProvider);
        SaveRejected(SettingsBodies.Empty).ShouldBe(SaveKeyError.InvalidProvider);
        SaveRejected(SettingsBodies.Of(("type", RawValue.Null))).ShouldBe(SaveKeyError.InvalidProvider);
        SaveRejected(SettingsBodies.Of(("type", RawValue.Whole(5)))).ShouldBe(SaveKeyError.InvalidProvider);
        SaveRejected(SettingsBodies.Of(("type", new ListValue(1)))).ShouldBe(SaveKeyError.InvalidProvider);
    }

    [Fact]
    [Trait("AC", "007-25")]
    public void ProviderComesFromType_OrFromProvedor_WhenTypeIsFalsy()
    {
        SaveOk(SettingsBodies.Of(("provedor", RawValue.Text("weather")))).Provider.ShouldBe("weather");
        SaveOk(SettingsBodies.Of(("type", RawValue.Text("")), ("provedor", RawValue.Text("weather")))).Provider.ShouldBe("weather");
        SaveOk(SettingsBodies.Of(("type", RawValue.Null), ("provedor", RawValue.Text("openroute")))).Provider.ShouldBe("openroute");
        SaveOk(SettingsBodies.Of(("type", RawValue.Text("gemini")), ("provedor", RawValue.Text("weather")))).Provider.ShouldBe("gemini");
        SaveRejected(SettingsBodies.Of(("type", RawValue.Text("openai")), ("provedor", RawValue.Text("weather"))))
            .ShouldBe(SaveKeyError.InvalidProvider);
    }

    [Fact]
    [Trait("AC", "007-22")]
    public void Key_IsTrimmed()
    {
        var ok = SaveOk(SettingsBodies.Of(
            ("type", RawValue.Text("gemini")), ("useOwnKey", RawValue.Bool(true)), ("key", RawValue.Text("  AIza-x  "))));

        ok.UseOwnKey.ShouldBeTrue();
        ok.RawKey.ShouldBe("AIza-x");
    }

    [Theory]
    [Trait("AC", "007-24")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UseOwnKeyWithoutKey_IsKeyRequired(string? key)
    {
        var values = new List<(string, RawValue)> { ("type", RawValue.Text("gemini")), ("useOwnKey", RawValue.Bool(true)) };
        if (key is not null)
        {
            values.Add(("key", RawValue.Text(key)));
        }

        SaveRejected(SettingsBodies.Of([.. values])).ShouldBe(SaveKeyError.KeyRequired);
    }

    [Fact]
    [Trait("AC", "007-26")]
    public void UseOwnKey_AbsentOrNullIsFalse_OtherTypesAreInvalidData_Desvio084()
    {
        SaveOk(SettingsBodies.Of(("type", RawValue.Text("gemini")))).UseOwnKey.ShouldBeFalse();
        SaveOk(SettingsBodies.Of(("type", RawValue.Text("gemini")), ("useOwnKey", RawValue.Null))).UseOwnKey.ShouldBeFalse();
        SaveRejected(SettingsBodies.Of(("type", RawValue.Text("gemini")), ("useOwnKey", RawValue.Text("false"))))
            .ShouldBe(SaveKeyError.InvalidData);
        SaveRejected(SettingsBodies.Of(("type", RawValue.Text("gemini")), ("useOwnKey", RawValue.Whole(1))))
            .ShouldBe(SaveKeyError.InvalidData);
    }

    [Fact]
    [Trait("AC", "007-28")]
    public void KeyThatIsNotText_IsKeyRequired_Desvio084()
    {
        SaveRejected(SettingsBodies.Of(("type", RawValue.Text("gemini")), ("key", RawValue.Null))).ShouldBe(SaveKeyError.KeyRequired);
        SaveRejected(SettingsBodies.Of(("type", RawValue.Text("gemini")), ("key", RawValue.Whole(5)))).ShouldBe(SaveKeyError.KeyRequired);
        SaveRejected(SettingsBodies.Of(("type", RawValue.Text("gemini")), ("key", new ListValue(1)))).ShouldBe(SaveKeyError.KeyRequired);
    }

    [Fact]
    [Trait("AC", "007-25")]
    public void ProviderIsValidatedBeforeTheKey()
    {
        SaveRejected(SettingsBodies.Of(("type", RawValue.Text("openai")), ("useOwnKey", RawValue.Bool(true))))
            .ShouldBe(SaveKeyError.InvalidProvider);
    }

    [Fact]
    [Trait("AC", "007-23")]
    public void NotUsingOwnKey_WithoutKey_IsOkWithEmptyKey()
    {
        var ok = SaveOk(SettingsBodies.Of(("type", RawValue.Text("gemini")), ("useOwnKey", RawValue.Bool(false))));

        ok.UseOwnKey.ShouldBeFalse();
        ok.RawKey.ShouldBeEmpty();
    }

    private static ValidateKeyError CheckRejected(RawBody? body) =>
        ApiKeyRules.ValidateCheck(body).ShouldBeOfType<ValidateKeyValidation.Rejected>().Error;

    [Theory]
    [Trait("AC", "007-31")]
    [InlineData(null, "k")]
    [InlineData("", "k")]
    [InlineData("gemini", null)]
    [InlineData("gemini", "")]
    [InlineData("gemini", "   ")]
    public void Validate_WithMissingFields_IsRejected(string? type, string? key)
    {
        var values = new List<(string, RawValue)>();
        if (type is not null)
        {
            values.Add(("type", RawValue.Text(type)));
        }
        if (key is not null)
        {
            values.Add(("key", RawValue.Text(key)));
        }

        CheckRejected(SettingsBodies.Of([.. values])).ShouldBe(ValidateKeyError.MissingFields);
    }

    [Fact]
    [Trait("AC", "007-31")]
    public void Validate_WithKeyThatIsNotText_IsMissingFields_Desvio084()
    {
        CheckRejected(SettingsBodies.Of(("type", RawValue.Text("gemini")), ("key", RawValue.Whole(5)))).ShouldBe(ValidateKeyError.MissingFields);
        CheckRejected(SettingsBodies.Of(("type", RawValue.Text("gemini")), ("key", RawValue.Null))).ShouldBe(ValidateKeyError.MissingFields);
        CheckRejected(null).ShouldBe(ValidateKeyError.MissingFields);
    }

    [Fact]
    [Trait("AC", "007-31")]
    public void Validate_WithUnknownProvider_IsUnsupported()
    {
        CheckRejected(SettingsBodies.Of(("type", RawValue.Text("openai")), ("key", RawValue.Text("x")))).ShouldBe(ValidateKeyError.UnsupportedProvider);
        CheckRejected(SettingsBodies.Of(("type", RawValue.Whole(5)), ("key", RawValue.Text("x")))).ShouldBe(ValidateKeyError.UnsupportedProvider);
    }

    [Fact]
    [Trait("AC", "007-30")]
    public void Validate_TrimsTheKey_AndAcceptsProvedor()
    {
        var ok = ApiKeyRules.ValidateCheck(SettingsBodies.Of(("provedor", RawValue.Text("weather")), ("key", RawValue.Text(" k "))))
            .ShouldBeOfType<ValidateKeyValidation.Ok>();

        ok.Provider.ShouldBe("weather");
        ok.RawKey.ShouldBe("k");
    }
}

public class AddressRulesTests
{
    private static SaveAddressError Rejected(RawBody? body) =>
        AddressRules.Validate(body).ShouldBeOfType<SaveAddressValidation.Rejected>().Error;

    private static SaveAddressValidation.Ok Ok(RawBody body) => AddressRules.Validate(body).ShouldBeOfType<SaveAddressValidation.Ok>();

    private static RawBody Address(params (string Key, RawValue Value)[] extra) =>
        SettingsBodies.Of([("label", RawValue.Text("casa")), ("address", RawValue.Text("Rua A, 10")), .. extra]);

    [Theory]
    [Trait("AC", "007-51")]
    [InlineData(" CASA ", "casa")]
    [InlineData("Trabalho", "trabalho")]
    [InlineData("outro", "outro")]
    public void Label_IsTrimmedAndLowercased(string raw, string expected)
    {
        AddressLabels.TryNormalize(raw, out var label).ShouldBeTrue();
        label.ShouldBe(expected);
    }

    [Theory]
    [Trait("AC", "007-52")]
    [InlineData("escritorio")]
    [InlineData("")]
    [InlineData(null)]
    public void InvalidLabel_IsNotNormalized(string? raw) => AddressLabels.TryNormalize(raw, out _).ShouldBeFalse();

    [Fact]
    [Trait("AC", "007-55")]
    public void PathLabel_IsLowercasedButNotTrimmed()
    {
        AddressLabels.TryNormalizePath("Casa", out var label).ShouldBeTrue();
        label.ShouldBe("casa");
        AddressLabels.TryNormalizePath(" casa", out _).ShouldBeFalse();
        AddressLabels.TryNormalizePath("xyz", out _).ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "007-52")]
    public void LabelOfAnyNonTextType_IsInvalidLabel()
    {
        Rejected(null).ShouldBe(SaveAddressError.InvalidLabel);
        Rejected(SettingsBodies.Of(("address", RawValue.Text("x")))).ShouldBe(SaveAddressError.InvalidLabel);
        Rejected(SettingsBodies.Of(("label", RawValue.Null), ("address", RawValue.Text("x")))).ShouldBe(SaveAddressError.InvalidLabel);
        Rejected(SettingsBodies.Of(("label", RawValue.Whole(1)), ("address", RawValue.Text("x")))).ShouldBe(SaveAddressError.InvalidLabel);
    }

    [Theory]
    [Trait("AC", "007-52")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void EmptyAddress_IsRequired_AfterTheLabel(string? address)
    {
        var values = new List<(string, RawValue)> { ("label", RawValue.Text("casa")) };
        if (address is not null)
        {
            values.Add(("address", RawValue.Text(address)));
        }

        Rejected(SettingsBodies.Of([.. values])).ShouldBe(SaveAddressError.AddressRequired);
        Rejected(SettingsBodies.Of(("label", RawValue.Text("zzz")), ("address", RawValue.Text("")))).ShouldBe(SaveAddressError.InvalidLabel);
    }

    [Fact]
    [Trait("AC", "007-52")]
    public void AddressThatIsNotText_IsRequired_Desvio084()
    {
        Rejected(SettingsBodies.Of(("label", RawValue.Text("casa")), ("address", RawValue.Whole(5)))).ShouldBe(SaveAddressError.AddressRequired);
    }

    [Fact]
    [Trait("AC", "007-51")]
    public void Address_IsTrimmed() => Ok(Address()).Address.ShouldBe("Rua A, 10");

    [Fact]
    [Trait("AC", "007-51")]
    public void Address_TrimmedInTheBody()
    {
        Ok(SettingsBodies.Of(("label", RawValue.Text(" CASA ")), ("address", RawValue.Text(" Rua A, 10 ")))).ShouldSatisfyAllConditions(
            ok => ok.Label.ShouldBe("casa"),
            ok => ok.Address.ShouldBe("Rua A, 10"));
    }

    [Fact]
    [Trait("AC", "007-53")]
    public void WithoutCoordinates_TheyAreNull_ToPreserve_Desvio086()
    {
        Ok(Address()).Coordinates.ShouldBeNull();
        Ok(Address(("coordinates", RawValue.Null))).Coordinates.ShouldBeNull();
        Ok(Address(("coordinates", SettingsBodies.Obj()))).Coordinates.ShouldBeNull();
        Ok(Address(("coordinates", new ListValue(0)))).Coordinates.ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "007-53")]
    public void Coordinates_AreReadAsNumbersOrNumericText()
    {
        Ok(Address(("coordinates", SettingsBodies.Obj(("lat", RawValue.Fractional(-23.5)), ("lng", RawValue.Fractional(-46.6))))))
            .Coordinates.ShouldBe(new AddressCoordinates(-23.5m, -46.6m));
        Ok(Address(("coordinates", SettingsBodies.Obj(("lat", RawValue.Text("-23.5")), ("lng", RawValue.Whole(0))))))
            .Coordinates.ShouldBe(new AddressCoordinates(-23.5m, 0m));
    }

    [Fact]
    [Trait("AC", "007-53")]
    public void OnlyLatitude_KeepsLongitudeNull()
    {
        Ok(Address(("coordinates", SettingsBodies.Obj(("lat", RawValue.Fractional(10.5))))))
            .Coordinates.ShouldBe(new AddressCoordinates(10.5m, null));
    }

    [Theory]
    [Trait("AC", "007-54")]
    [InlineData(91.0, 0.0)]
    [InlineData(-90.1, 0.0)]
    [InlineData(0.0, 180.1)]
    [InlineData(0.0, -181.0)]
    public void CoordinatesOutOfRange_AreInvalid_Desvio087(double lat, double lng)
    {
        Rejected(Address(("coordinates", SettingsBodies.Obj(("lat", RawValue.Fractional(lat)), ("lng", RawValue.Fractional(lng))))))
            .ShouldBe(SaveAddressError.InvalidCoordinates);
    }

    [Fact]
    [Trait("AC", "007-54")]
    public void CoordinateLimits_AreInclusive()
    {
        Ok(Address(("coordinates", SettingsBodies.Obj(("lat", RawValue.Whole(90)), ("lng", RawValue.Whole(-180))))))
            .Coordinates.ShouldBe(new AddressCoordinates(90m, -180m));
    }

    [Fact]
    [Trait("AC", "007-54")]
    public void NonNumericOrHugeCoordinates_AreInvalid()
    {
        Rejected(Address(("coordinates", SettingsBodies.Obj(("lat", RawValue.Text("abc")))))).ShouldBe(SaveAddressError.InvalidCoordinates);
        Rejected(Address(("coordinates", SettingsBodies.Obj(("lat", RawValue.Text("nan")))))).ShouldBe(SaveAddressError.InvalidCoordinates);
        Rejected(Address(("coordinates", SettingsBodies.Obj(("lat", RawValue.Whole(123456)), ("lng", RawValue.Whole(0))))))
            .ShouldBe(SaveAddressError.InvalidCoordinates);
        Rejected(Address(("coordinates", SettingsBodies.Obj(("lat", RawValue.Bool(true)))))).ShouldBe(SaveAddressError.InvalidCoordinates);
        Rejected(Address(("coordinates", SettingsBodies.Obj(("lat", new ListValue(1)))))).ShouldBe(SaveAddressError.InvalidCoordinates);
    }

    [Fact]
    [Trait("AC", "007-54")]
    public void CoordinatesThatAreNotAnObject_AreInvalidData_Desvio084()
    {
        Rejected(Address(("coordinates", RawValue.Text("x")))).ShouldBe(SaveAddressError.InvalidData);
        Rejected(Address(("coordinates", new ListValue(2)))).ShouldBe(SaveAddressError.InvalidData);
        Rejected(Address(("coordinates", RawValue.Whole(1)))).ShouldBe(SaveAddressError.InvalidData);
    }
}
