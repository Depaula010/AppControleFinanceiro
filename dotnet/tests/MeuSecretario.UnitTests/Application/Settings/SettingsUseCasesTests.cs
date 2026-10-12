using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Settings;
using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.Settings;
using MeuSecretario.UnitTests.Domain.Settings;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Settings;

/// <summary>Casos de uso da 007 com fakes: ordem das validaÃ§Ãµes, resultados tipados e nenhuma gravaÃ§Ã£o em caso de 400.</summary>
public class SettingsUseCasesTests
{
    private const string SentinelKey = "SENTINELA-chave-9981";

    private readonly IProfileRepository _profiles = Substitute.For<IProfileRepository>();
    private readonly IApiKeySettingsRepository _keys = Substitute.For<IApiKeySettingsRepository>();
    private readonly IUserNotificationSettingsRepository _notifications = Substitute.For<IUserNotificationSettingsRepository>();
    private readonly IFavoriteAddressRepository _addresses = Substitute.For<IFavoriteAddressRepository>();
    private readonly IEncryptionService _encryption = Substitute.For<IEncryptionService>();
    private readonly IProviderKeyValidator _validator = Substitute.For<IProviderKeyValidator>();

    private static readonly ProfileRow Row = new(1, "Ana", null, "5511", "Sao Paulo", "SP", "America/Sao_Paulo", 6);

    public SettingsUseCasesTests()
    {
        _encryption.Encrypt(Arg.Any<string>()).Returns(call => "ENC(" + call.Arg<string>() + ")");
    }

    // ---------------------------------------------------------------- perfil

    [Fact]
    [Trait("AC", "007-03")]
    public async Task GetProfile_ReturnsNull_WhenTheRepositoryHasNoRow()
    {
        _profiles.GetAsync(7, default).Returns((ProfileRow?)null);

        (await new GetProfile(_profiles).ExecuteAsync(7, default)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "007-10")]
    public async Task UpdateProfile_Invalid_DoesNotTouchTheRepository()
    {
        var result = await new UpdateProfile(_profiles).ExecuteAsync(1, SettingsBodies.Empty, default);

        result.ShouldBeOfType<UpdateProfileResult.Invalid>().Error.ShouldBe(ProfileError.NothingToUpdate);
        await _profiles.DidNotReceiveWithAnyArgs().UpdateAsync(default, default!, default);
    }

    [Fact]
    [Trait("AC", "007-02")]
    public async Task UpdateProfile_MapsEachOutcome()
    {
        var body = SettingsBodies.Of(("nome", RawValue.Text("Ana")));
        _profiles.UpdateAsync(1, Arg.Any<ProfileChanges>(), default).Returns(new ProfileUpdateOutcome.Updated(Row));
        _profiles.UpdateAsync(2, Arg.Any<ProfileChanges>(), default).Returns(new ProfileUpdateOutcome.NotFound());
        _profiles.UpdateAsync(3, Arg.Any<ProfileChanges>(), default).Returns(new ProfileUpdateOutcome.EmailTaken());
        var useCase = new UpdateProfile(_profiles);

        (await useCase.ExecuteAsync(1, body, default)).ShouldBeOfType<UpdateProfileResult.Updated>().Profile.ShouldBe(Row);
        (await useCase.ExecuteAsync(2, body, default)).ShouldBeOfType<UpdateProfileResult.NotFound>();
        (await useCase.ExecuteAsync(3, body, default)).ShouldBeOfType<UpdateProfileResult.EmailTaken>();
    }

    // ---------------------------------------------------------------- chaves

    [Fact]
    [Trait("AC", "007-22")]
    public async Task SaveOwnApiKey_EncryptsTheTrimmedKey_AndSavesInOneCall()
    {
        var body = SettingsBodies.Of(
            ("type", RawValue.Text("gemini")), ("useOwnKey", RawValue.Bool(true)), ("key", RawValue.Text("  " + SentinelKey + "  ")));

        (await new SaveOwnApiKey(_keys, _encryption).ExecuteAsync(5, body, default)).ShouldBeOfType<SaveOwnApiKeyResult.Saved>();

        _encryption.Received(1).Encrypt(SentinelKey);
        await _keys.Received(1).SaveOwnKeyAsync(5, "gemini", true, "ENC(" + SentinelKey + ")", default);
    }

    [Fact]
    [Trait("AC", "007-23")]
    public async Task SaveOwnApiKey_NotUsingOwnKey_SendsNoKeyEvenIfOneWasGiven()
    {
        var body = SettingsBodies.Of(
            ("type", RawValue.Text("weather")), ("useOwnKey", RawValue.Bool(false)), ("key", RawValue.Text(SentinelKey)));

        await new SaveOwnApiKey(_keys, _encryption).ExecuteAsync(5, body, default);

        _encryption.DidNotReceiveWithAnyArgs().Encrypt(default!);
        await _keys.Received(1).SaveOwnKeyAsync(5, "weather", false, null, default);
    }

    [Theory]
    [Trait("AC", "007-24")]
    [InlineData("openai", true)]
    [InlineData("gemini", true)]
    public async Task SaveOwnApiKey_Invalid_NeverWritesAnything(string provider, bool useOwn)
    {
        var body = SettingsBodies.Of(("type", RawValue.Text(provider)), ("useOwnKey", RawValue.Bool(useOwn)));

        (await new SaveOwnApiKey(_keys, _encryption).ExecuteAsync(5, body, default)).ShouldBeOfType<SaveOwnApiKeyResult.Invalid>();

        await _keys.DidNotReceiveWithAnyArgs().SaveOwnKeyAsync(default, default!, default, default, default);
        _encryption.DidNotReceiveWithAnyArgs().Encrypt(default!);
    }

    [Fact]
    [Trait("AC", "007-27")]
    public async Task SaveOwnApiKey_RepositoryFailure_PropagatesWithoutTheKeyInTheMessage()
    {
        _keys.SaveOwnKeyAsync(default, default!, default, default, default).ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("falha")));
        var body = SettingsBodies.Of(("type", RawValue.Text("gemini")), ("useOwnKey", RawValue.Bool(true)), ("key", RawValue.Text(SentinelKey)));

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => new SaveOwnApiKey(_keys, _encryption).ExecuteAsync(5, body, default));

        ex.ToString().ShouldNotContain(SentinelKey);
    }

    [Theory]
    [Trait("AC", "007-30")]
    [InlineData(KeyValidation.Valid, true)]
    [InlineData(KeyValidation.Invalid, false)]
    public async Task ValidateProviderKey_MapsTheValidatorResult(KeyValidation validation, bool expected)
    {
        _validator.ValidateAsync("gemini", SentinelKey, default).Returns(validation);
        var body = SettingsBodies.Of(("type", RawValue.Text("gemini")), ("key", RawValue.Text(" " + SentinelKey + " ")));

        var result = await new ValidateProviderKey(_validator).ExecuteAsync(body, default);

        result.ShouldBeOfType<ValidateProviderKeyResult.Checked>().IsValid.ShouldBe(expected);
    }

    [Fact]
    [Trait("AC", "007-31")]
    public async Task ValidateProviderKey_NotSupported_IsTheUnsupportedProvider()
    {
        _validator.ValidateAsync(default!, default!, default).ReturnsForAnyArgs(KeyValidation.NotSupported);
        var body = SettingsBodies.Of(("type", RawValue.Text("gemini")), ("key", RawValue.Text("k")));

        var result = await new ValidateProviderKey(_validator).ExecuteAsync(body, default);

        result.ShouldBeOfType<ValidateProviderKeyResult.Invalid>().Error.ShouldBe(ValidateKeyError.UnsupportedProvider);
    }

    [Theory]
    [Trait("AC", "007-31")]
    [InlineData("openai")]
    [InlineData("")]
    public async Task ValidateProviderKey_InvalidInput_NeverCallsTheNetworkValidator(string provider)
    {
        var body = SettingsBodies.Of(("type", RawValue.Text(provider)), ("key", RawValue.Text("k")));

        (await new ValidateProviderKey(_validator).ExecuteAsync(body, default)).ShouldBeOfType<ValidateProviderKeyResult.Invalid>();

        await _validator.DidNotReceiveWithAnyArgs().ValidateAsync(default!, default!, default);
    }

    [Fact]
    [Trait("AC", "007-32")]
    public async Task ValidateProviderKey_Unavailable_CarriesTheException_WithoutTheKey()
    {
        _validator.ValidateAsync(default!, default!, default).ReturnsForAnyArgs<Task<KeyValidation>>(_ => throw new ProviderUnavailableException("timeout"));
        var body = SettingsBodies.Of(("type", RawValue.Text("weather")), ("key", RawValue.Text(SentinelKey)));

        var result = await new ValidateProviderKey(_validator).ExecuteAsync(body, default);

        var unavailable = result.ShouldBeOfType<ValidateProviderKeyResult.Unavailable>();
        unavailable.ToString().ShouldNotContain(SentinelKey);
    }

    // ---------------------------------------------------------------- notificaÃ§Ãµes

    [Fact]
    [Trait("AC", "007-40")]
    public async Task GetNotifications_WithoutRow_ReturnsTheDefaults()
    {
        _notifications.GetAsync(1, default).Returns((NotificationRow?)null);

        var view = await new GetNotificationSettings(_notifications).ExecuteAsync(1, default);

        view.ShouldBe(new NotificationView(true, "07:00", true, "19:00", true, 3));
    }

    [Fact]
    [Trait("AC", "007-41")]
    public async Task GetNotifications_WithRow_UsesTheColumns()
    {
        _notifications.GetAsync(1, default).Returns(new NotificationRow(false, "08:30:00", true, "21:15:00", null, 0));
        _notifications.GetAsync(2, default).Returns(new NotificationRow(true, "07:00:00", false, "19:30:00.5", true, 7));
        var useCase = new GetNotificationSettings(_notifications);

        (await useCase.ExecuteAsync(1, default)).ShouldBe(new NotificationView(false, "08:30", true, "21:15", false, 3));
        (await useCase.ExecuteAsync(2, default)).ShouldBe(new NotificationView(true, "07:00", false, "19:30", true, 7));
    }

    [Fact]
    [Trait("AC", "007-43")]
    public async Task SaveNotifications_Invalid_DoesNotWrite()
    {
        var body = SettingsBodies.Of(("eveningCheckIn", SettingsBodies.Obj(("time", RawValue.Text("17:00")))));

        var result = await new SaveNotificationSettings(_notifications).ExecuteAsync(1, body, default);

        result.ShouldBeOfType<SaveNotificationsResult.Invalid>().Error.ShouldBe(NotificationError.EveningHourRange);
        await _notifications.DidNotReceiveWithAnyArgs().UpsertAsync(default, default!, default);
    }

    [Fact]
    [Trait("AC", "007-44")]
    public async Task SaveNotifications_EmptyBody_WritesTheDefaults()
    {
        (await new SaveNotificationSettings(_notifications).ExecuteAsync(1, SettingsBodies.Empty, default))
            .ShouldBeOfType<SaveNotificationsResult.Saved>();

        await _notifications.Received(1).UpsertAsync(1, new NotificationInput(true, "07:00", true, "19:00", true, 3), default);
    }

    // ---------------------------------------------------------------- endereÃ§os

    [Fact]
    [Trait("AC", "007-52")]
    public async Task SaveAddress_LabelIsCheckedBeforeTheAddress_AndNothingIsWritten()
    {
        var body = SettingsBodies.Of(("label", RawValue.Text("escritorio")), ("address", RawValue.Text("")));

        var result = await new SaveAddress(_addresses).ExecuteAsync(1, body, default);

        result.ShouldBeOfType<SaveAddressResult.Invalid>().Error.ShouldBe(SaveAddressError.InvalidLabel);
        await _addresses.DidNotReceiveWithAnyArgs().UpsertAsync(default, default!, default!, default, default);
    }

    [Fact]
    [Trait("AC", "007-51")]
    public async Task SaveAddress_WritesTheNormalizedValues_AndNullCoordinatesToPreserve()
    {
        var body = SettingsBodies.Of(("label", RawValue.Text(" CASA ")), ("address", RawValue.Text(" Rua A, 10 ")));

        (await new SaveAddress(_addresses).ExecuteAsync(1, body, default)).ShouldBeOfType<SaveAddressResult.Saved>();

        await _addresses.Received(1).UpsertAsync(1, "casa", "Rua A, 10", null, default);
    }

    [Fact]
    [Trait("AC", "007-55")]
    public async Task DeleteAddress_MapsTheOutcomes()
    {
        _addresses.DeleteAsync(1, "casa", default).Returns(true);
        _addresses.DeleteAsync(2, "casa", default).Returns(false);
        var useCase = new DeleteAddress(_addresses);

        (await useCase.ExecuteAsync(1, "Casa", default)).ShouldBe(DeleteAddressResult.Deleted);
        (await useCase.ExecuteAsync(2, "casa", default)).ShouldBe(DeleteAddressResult.NotFound);
        (await useCase.ExecuteAsync(1, "xyz", default)).ShouldBe(DeleteAddressResult.InvalidLabel);
        await _addresses.DidNotReceive().DeleteAsync(1, "xyz", default);
    }

    // ---------------------------------------------------------------- settings

    [Fact]
    [Trait("AC", "007-03")]
    public async Task GetUserSettings_WithoutProfile_DoesNotRunTheOtherReaders()
    {
        _profiles.GetAsync(9, default).Returns((ProfileRow?)null);

        var result = await BuildSettings().ExecuteAsync(9, default);

        result.ShouldBeNull();
        await _keys.DidNotReceiveWithAnyArgs().ListAsync(default, default);
        await _notifications.DidNotReceiveWithAnyArgs().GetAsync(default, default);
        await _addresses.DidNotReceiveWithAnyArgs().ListAsync(default, default);
    }

    [Fact]
    [Trait("AC", "007-61")]
    public async Task GetUserSettings_ComposesTheFourReaders()
    {
        _profiles.GetAsync(1, default).Returns(Row);
        _keys.ListAsync(1, default).Returns([new ApiKeyPreferenceRow("gemini", true, true)]);
        _notifications.GetAsync(1, default).Returns((NotificationRow?)null);
        _addresses.ListAsync(1, default).Returns([new AddressRow(3, "casa", "Rua A", null, null)]);

        var settings = await BuildSettings().ExecuteAsync(1, default);

        settings.ShouldNotBeNull();
        settings.Profile.ShouldBe(Row);
        settings.ApiKeys.Count.ShouldBe(1);
        settings.Notifications.ShouldBe(new NotificationView(true, "07:00", true, "19:00", true, 3));
        settings.Addresses.Single().Label.ShouldBe("casa");
    }

    private GetUserSettings BuildSettings() => new(
        new GetProfile(_profiles), new GetApiKeySettings(_keys), new GetNotificationSettings(_notifications), new ListAddresses(_addresses));
}
