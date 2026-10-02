using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.ApiKeys;
using MeuSecretario.Domain.ApiKeys;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.ApiKeys;

/// <summary>Casos de uso de chaves, preferências, uso e logs (spec 008 §4.1 a 4.9): ordem das validações e resultados.</summary>
public class KeyUseCasesTests
{
    private readonly IApiKeyRepository _keys = Substitute.For<IApiKeyRepository>();
    private readonly IPreferenceRepository _preferences = Substitute.For<IPreferenceRepository>();
    private readonly IUsageRepository _usage = Substitute.For<IUsageRepository>();
    private readonly IAccessLogRepository _logs = Substitute.For<IAccessLogRepository>();
    private readonly IEncryptionService _encryption = Substitute.For<IEncryptionService>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public KeyUseCasesTests()
    {
        _encryption.Encrypt(Arg.Any<string>()).Returns(call => "ENC(" + call.Arg<string>() + ")");
    }

    // ---------------------------------------------------------------- RegisterApiKey

    [Theory]
    [Trait("AC", "008-11")]
    [InlineData(null, "k")]
    [InlineData("", "k")]
    [InlineData("gemini", null)]
    [InlineData("gemini", "")]
    public async Task Register_EmptyFields_AreMissing(string? provider, string? key)
    {
        var result = await new RegisterApiKey(_keys, _encryption).ExecuteAsync(new RegisterKeyInput(1, provider, key), default);

        result.ShouldBeOfType<RegisterKeyResult.MissingFields>();
        await _keys.DidNotReceiveWithAnyArgs().InsertAsync(default, default!, default!, default);
    }

    [Theory]
    [Trait("AC", "008-12")]
    [InlineData("openai")]
    [InlineData("Gemini")]
    [InlineData(" gemini")]
    public async Task Register_InvalidProvider(string provider)
    {
        var result = await new RegisterApiKey(_keys, _encryption).ExecuteAsync(new RegisterKeyInput(1, provider, "k"), default);

        result.ShouldBeOfType<RegisterKeyResult.InvalidProvider>();
    }

    [Fact]
    [Trait("AC", "008-10")]
    public async Task Register_Success_EncryptsWithoutTrimming()
    {
        _keys.InsertAsync(7, "gemini", "ENC( abc )", default).Returns(new InsertKeyResult.Created(33));

        var result = await new RegisterApiKey(_keys, _encryption).ExecuteAsync(new RegisterKeyInput(7, "gemini", " abc "), default);

        result.ShouldBe(new RegisterKeyResult.Success(33, "gemini", 7));
    }

    [Fact]
    [Trait("AC", "008-13")]
    public async Task Register_MapsRepositoryOutcomes()
    {
        _keys.InsertAsync(1, "gemini", Arg.Any<string>(), default).Returns(new InsertKeyResult.UserNotFound());
        _keys.InsertAsync(2, "gemini", Arg.Any<string>(), default).Returns(new InsertKeyResult.AlreadyExists());
        var useCase = new RegisterApiKey(_keys, _encryption);

        (await useCase.ExecuteAsync(new RegisterKeyInput(1, "gemini", "k"), default)).ShouldBeOfType<RegisterKeyResult.UserNotFound>();
        (await useCase.ExecuteAsync(new RegisterKeyInput(2, "gemini", "k"), default)).ShouldBeOfType<RegisterKeyResult.AlreadyExists>();
    }

    // ---------------------------------------------------------------- UpdateApiKey

    [Theory]
    [Trait("AC", "008-16")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public async Task Update_WithNeitherKeyNorActive_IsNothingToUpdate(string? key, bool? active)
    {
        var result = await new UpdateApiKey(_keys, _encryption).ExecuteAsync(new UpdateKeyInput(1, 5, key, active), default);

        result.ShouldBeOfType<UpdateKeyResult.NothingToUpdate>();
    }

    [Theory]
    [Trait("AC", "008-17")]
    [InlineData(null)]
    [InlineData(0)]
    public async Task Update_WithoutOwner_IsRejected_BeforeTouchingTheDatabase(int? owner)
    {
        var result = await new UpdateApiKey(_keys, _encryption).ExecuteAsync(new UpdateKeyInput(1, owner, "k", null), default);

        result.ShouldBeOfType<UpdateKeyResult.OwnerRequired>();
        await _keys.DidNotReceiveWithAnyArgs().UpdateAsync(default, default, default, default, default);
    }

    [Fact]
    [Trait("AC", "008-15")]
    public async Task Update_OnlyActiveFalse_DoesNotTouchTheKey()
    {
        _keys.UpdateAsync(1, 5, null, false, default).Returns(new KeyRef("gemini", 5));

        var result = await new UpdateApiKey(_keys, _encryption).ExecuteAsync(new UpdateKeyInput(1, 5, "", false), default);

        result.ShouldBe(new UpdateKeyResult.Success(1, "gemini", 5));
        _encryption.DidNotReceive().Encrypt(Arg.Any<string>());
    }

    [Fact]
    [Trait("AC", "008-15")]
    public async Task Update_WithKey_EncryptsIt()
    {
        _keys.UpdateAsync(1, 5, "ENC(nova)", null, default).Returns(new KeyRef("weather", 5));

        var result = await new UpdateApiKey(_keys, _encryption).ExecuteAsync(new UpdateKeyInput(1, 5, "nova", null), default);

        result.ShouldBe(new UpdateKeyResult.Success(1, "weather", 5));
    }

    [Fact]
    [Trait("AC", "008-17")]
    public async Task Update_OfAnotherUsersKey_IsNotFound()
    {
        _keys.UpdateAsync(1, 9, Arg.Any<string?>(), Arg.Any<bool?>(), default).Returns((KeyRef?)null);

        var result = await new UpdateApiKey(_keys, _encryption).ExecuteAsync(new UpdateKeyInput(1, 9, "k", true), default);

        result.ShouldBeOfType<UpdateKeyResult.NotFound>();
    }

    // ---------------------------------------------------------------- DeactivateApiKey

    [Fact]
    [Trait("AC", "008-17")]
    public async Task Deactivate_WithoutOwner_IsRejected()
    {
        (await new DeactivateApiKey(_keys).ExecuteAsync(1, null, default)).ShouldBeOfType<DeactivateKeyResult.OwnerRequired>();
        await _keys.DidNotReceiveWithAnyArgs().DeactivateAsync(default, default, default);
    }

    [Fact]
    [Trait("AC", "008-18")]
    public async Task Deactivate_MapsRepositoryOutcome()
    {
        _keys.DeactivateAsync(1, 5, default).Returns(new KeyRef("gemini", 5));
        _keys.DeactivateAsync(2, 5, default).Returns((KeyRef?)null);
        var useCase = new DeactivateApiKey(_keys);

        (await useCase.ExecuteAsync(1, 5, default)).ShouldBe(new DeactivateKeyResult.Success(1, "gemini"));
        (await useCase.ExecuteAsync(2, 5, default)).ShouldBeOfType<DeactivateKeyResult.NotFound>();
    }

    // ---------------------------------------------------------------- SetPreference

    [Theory]
    [Trait("AC", "008-21")]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("gemini", null)]
    public async Task SetPreference_MissingFields(string? provider, bool? own)
    {
        var result = await new SetPreference(_preferences).ExecuteAsync(new SetPreferenceInput(1, provider, own), default);

        result.ShouldBeOfType<SetPreferenceResult.MissingFields>();
    }

    [Fact]
    [Trait("AC", "008-21")]
    public async Task SetPreference_FalseIsValid_AndInvalidProviderIsRejected()
    {
        _preferences.UpsertAsync(1, "gemini", false, default).Returns(new UpsertPreferenceResult.Saved(4));
        var useCase = new SetPreference(_preferences);

        (await useCase.ExecuteAsync(new SetPreferenceInput(1, "gemini", false), default)).ShouldBe(new SetPreferenceResult.Success(4, "gemini", false));
        (await useCase.ExecuteAsync(new SetPreferenceInput(1, "openai", true), default)).ShouldBeOfType<SetPreferenceResult.InvalidProvider>();
    }

    [Fact]
    [Trait("AC", "008-21")]
    public async Task SetPreference_UnknownUser_IsReported()
    {
        _preferences.UpsertAsync(9, "gemini", true, default).Returns(new UpsertPreferenceResult.UserNotFound());

        var result = await new SetPreference(_preferences).ExecuteAsync(new SetPreferenceInput(9, "gemini", true), default);

        result.ShouldBeOfType<SetPreferenceResult.UserNotFound>();
    }

    // ---------------------------------------------------------------- GetUsage

    [Fact]
    [Trait("AC", "008-30")]
    public async Task Usage_DefaultMonth_IsTheSaoPauloMonth_AndEmptyCountsAsAbsent()
    {
        _clock.Now.Returns(new DateTime(2026, 10, 31, 23, 30, 0));
        _usage.QueryMonthAsync(1, "2026-10", null, default).Returns([]);

        var result = await new GetUsage(_usage, _clock).ExecuteAsync(1, "", "", default);

        result.Month.ShouldBe("2026-10");
        result.Summary.ShouldBe(new UsageSummary(0, 0, 0));
        result.Details.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "008-30")]
    public async Task Usage_WithMonthAndProvider_FiltersAndSummarizes()
    {
        UsageRow[] rows = [new("gemini", "propria", 2, "2026-09", null), new("gemini", "sistema", 3, "2026-09", null)];
        _usage.QueryMonthAsync(1, "texto-livre", "gemini", default).Returns(rows);

        var result = await new GetUsage(_usage, _clock).ExecuteAsync(1, "texto-livre", "gemini", default);

        result.Month.ShouldBe("texto-livre");
        result.Summary.ShouldBe(new UsageSummary(5, 2, 3));
    }

    // ---------------------------------------------------------------- GetAccessLogs

    [Theory]
    [Trait("AC", "008-31")]
    [InlineData(null, 50)]
    [InlineData("10", 10)]
    [InlineData(" 7 ", 7)]
    [InlineData("+5", 5)]
    [InlineData("0", 0)]
    [InlineData("9999", 500)]
    [InlineData("99999999999999999999999", 500)]
    public async Task Logs_LimitFollowsPythonInt_WithDefault50AndCeiling500(string? limit, int expected)
    {
        _logs.ListAsync(1, null, null, expected, default).Returns([]);

        var result = await new GetAccessLogs(_logs).ExecuteAsync(new AccessLogQuery(1, null, null, limit), default);

        result.ShouldBeOfType<AccessLogsResult.Success>();
        await _logs.Received(1).ListAsync(1, null, null, expected, default);
    }

    [Theory]
    [Trait("AC", "008-31")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("1_0")]
    public async Task Logs_InvalidOrNegativeLimit_IsAnError(string limit)
    {
        var result = await new GetAccessLogs(_logs).ExecuteAsync(new AccessLogQuery(1, null, null, limit), default);

        result.ShouldBeOfType<AccessLogsResult.InvalidLimit>();
        await _logs.DidNotReceiveWithAnyArgs().ListAsync(default, default, default, default, default);
    }

    [Theory]
    [Trait("AC", "008-31")]
    [InlineData(null, null)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("", false)]
    [InlineData("1", false)]
    [InlineData("sim", false)]
    public async Task Logs_SuccessFilter(string? text, bool? expected)
    {
        _logs.ListAsync(1, "gemini", expected, 50, default).Returns([]);

        await new GetAccessLogs(_logs).ExecuteAsync(new AccessLogQuery(1, "gemini", text, null), default);

        await _logs.Received(1).ListAsync(1, "gemini", expected, 50, default);
    }
}
