using System.Security.Cryptography;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.ApiKeys;
using MeuSecretario.Domain.ApiKeys;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MeuSecretario.UnitTests.Application.ApiKeys;

/// <summary>Spec 008 §5, AC-008-60 a 69: ordem das decisões, mensagens literais, fail-open e nenhum segredo nos diagnósticos.</summary>
public class ApiKeyProviderTests
{
    private const string Secret = "chave-secreta-do-usuario";
    private const string Token = "gAAAAAB-token-fernet-fake-com-mais-de-quarenta-caracteres";

    private readonly IApiKeyRepository _keys = Substitute.For<IApiKeyRepository>();
    private readonly IPreferenceRepository _preferences = Substitute.For<IPreferenceRepository>();
    private readonly IUsageRepository _usage = Substitute.For<IUsageRepository>();
    private readonly IPlanRepository _plans = Substitute.For<IPlanRepository>();
    private readonly IAccessLogRepository _log = Substitute.For<IAccessLogRepository>();
    private readonly ISystemApiKeys _system = Substitute.For<ISystemApiKeys>();
    private readonly IEncryptionService _encryption = Substitute.For<IEncryptionService>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IApiKeyDiagnostics _diagnostics = Substitute.For<IApiKeyDiagnostics>();

    public ApiKeyProviderTests()
    {
        // 31/10 23:30 em São Paulo: mês de uso = 2026-10, embora em UTC já seja novembro.
        _clock.Now.Returns(new DateTime(2026, 10, 31, 23, 30, 0));
        _encryption.LooksEncrypted(Arg.Any<string>()).Returns(call => call.Arg<string>().StartsWith("gAAAAA", StringComparison.Ordinal));
        _encryption.Decrypt(Token).Returns(Secret);
        _system.KeyFor("gemini").Returns("chave-do-sistema");
    }

    private ApiKeyProvider Provider() =>
        new(_keys, _preferences, _usage, _plans, _log, _system, _encryption, _clock, _diagnostics);

    private static ApiKeyResolution.Failed AsFailed(ApiKeyResolution r) => r.ShouldBeOfType<ApiKeyResolution.Failed>();

    [Fact]
    [Trait("AC", "008-60")]
    public async Task InvalidProvider_FailsWithTheLiteralMessage_AndTouchesNothing()
    {
        var failed = AsFailed(await Provider().ResolveAsync(1, "openai", default));

        failed.Reason.ShouldBe(ApiKeyFailure.InvalidProvider);
        failed.Message.ShouldBe("Provedor inválido: openai");
        await _preferences.DidNotReceiveWithAnyArgs().FindAsync(default, default!, default);
    }

    [Fact]
    [Trait("AC", "008-61")]
    public async Task NoPreference_Fails_AndIsLogged()
    {
        _preferences.FindAsync(1, "gemini", default).Returns((bool?)null);

        var failed = AsFailed(await Provider().ResolveAsync(1, "gemini", default));

        failed.Reason.ShouldBe(ApiKeyFailure.NoPreference);
        failed.Message.ShouldBe(ApiKeyMessages.NoPreference("gemini"));
        await _log.Received(1).InsertAsync(1, "gemini", "n/a", "obter_chave", false, "NoPreference", default);
    }

    [Fact]
    [Trait("AC", "008-67")]
    public async Task PreferenceReadError_IsTreatedAsNoPreference()
    {
        _preferences.FindAsync(1, "gemini", default).ThrowsAsync(new InvalidOperationException("texto do banco com segredo"));

        var failed = AsFailed(await Provider().ResolveAsync(1, "gemini", default));

        failed.Reason.ShouldBe(ApiKeyFailure.NoPreference);
        _diagnostics.Received(1).Swallowed("buscar_preferencia", "InvalidOperationException");
    }

    [Fact]
    [Trait("AC", "008-62")]
    public async Task OwnPreference_WithoutActiveKey_Fails()
    {
        _preferences.FindAsync(1, "gemini", default).Returns(true);
        _keys.FindActiveEncryptedAsync(1, "gemini", default).Returns((string?)null);

        var failed = AsFailed(await Provider().ResolveAsync(1, "gemini", default));

        failed.Reason.ShouldBe(ApiKeyFailure.OwnKeyMissing);
        failed.Message.ShouldBe(ApiKeyMessages.OwnKeyMissing("gemini"));
        await _usage.DidNotReceiveWithAnyArgs().IncrementAsync(default, default!, default!, default!, default);
    }

    [Fact]
    [Trait("AC", "008-63")]
    public async Task OwnPreference_WithKey_DecryptsTouchesLastUse_AndCountsOwnUsageInTheSaoPauloMonth()
    {
        _preferences.FindAsync(1, "gemini", default).Returns(true);
        _keys.FindActiveEncryptedAsync(1, "gemini", default).Returns(Token);

        var resolved = (await Provider().ResolveAsync(1, "gemini", default)).ShouldBeOfType<ApiKeyResolution.Resolved>();

        resolved.Key.ShouldBe(Secret);
        resolved.KeyType.ShouldBe("propria");
        await _keys.Received(1).TouchLastUseAsync(1, "gemini", default);
        await _usage.Received(1).IncrementAsync(1, "gemini", "propria", "2026-10", default);
        await _log.Received(1).InsertAsync(1, "gemini", "propria", "obter_chave", true, null, default);
    }

    [Fact]
    [Trait("AC", "008-63")]
    public async Task OwnPreference_LegacyPlainText_IsReturnedAsIs()
    {
        _preferences.FindAsync(1, "gemini", default).Returns(true);
        _keys.FindActiveEncryptedAsync(1, "gemini", default).Returns("texto-puro-legado");

        var resolved = (await Provider().ResolveAsync(1, "gemini", default)).ShouldBeOfType<ApiKeyResolution.Resolved>();

        resolved.Key.ShouldBe("texto-puro-legado");
        _encryption.DidNotReceive().Decrypt(Arg.Any<string>());
    }

    [Fact]
    public async Task OwnPreference_FernetTokenThatDoesNotDecrypt_Throws_NeverReturnsTheCipherText()
    {
        var corrupted = "gAAAAAB-token-corrompido-com-mais-de-quarenta-caracteres";
        _preferences.FindAsync(1, "gemini", default).Returns(true);
        _keys.FindActiveEncryptedAsync(1, "gemini", default).Returns(corrupted);
        _encryption.Decrypt(corrupted).Throws(new CryptographicException("Token Fernet inválido"));

        await Should.ThrowAsync<CryptographicException>(() => Provider().ResolveAsync(1, "gemini", default));

        await _usage.DidNotReceiveWithAnyArgs().IncrementAsync(default, default!, default!, default!, default);
        await _log.Received(1).InsertAsync(1, "gemini", "propria", "obter_chave", false, "KeyUnreadable", default);
    }

    [Fact]
    [Trait("AC", "008-64")]
    public async Task SystemPreference_WithEmptySystemKey_Fails_AndRecordsNoUsage()
    {
        _preferences.FindAsync(1, "weather", default).Returns(false);
        _system.KeyFor("weather").Returns(string.Empty);

        var failed = AsFailed(await Provider().ResolveAsync(1, "weather", default));

        failed.Reason.ShouldBe(ApiKeyFailure.SystemKeyUnavailable);
        failed.Message.ShouldBe(ApiKeyMessages.SystemKeyUnavailable("weather"));
        await _usage.DidNotReceiveWithAnyArgs().IncrementAsync(default, default!, default!, default!, default);
    }

    [Fact]
    [Trait("AC", "008-65")]
    public async Task SystemPreference_WithoutPlan_ReturnsTheSystemKey_AndCountsSystemUsage()
    {
        _preferences.FindAsync(1, "gemini", default).Returns(false);
        _plans.FindActivePlanAsync(1, default).Returns((ActivePlan?)null);

        var resolved = (await Provider().ResolveAsync(1, "gemini", default)).ShouldBeOfType<ApiKeyResolution.Resolved>();

        resolved.Key.ShouldBe("chave-do-sistema");
        resolved.KeyType.ShouldBe("sistema");
        await _usage.Received(1).IncrementAsync(1, "gemini", "sistema", "2026-10", default);
        await _log.Received(1).InsertAsync(1, "gemini", "sistema", "obter_chave", true, null, default);
    }

    [Theory]
    [Trait("AC", "008-66")]
    [InlineData(99, true)]
    [InlineData(100, false)]
    [InlineData(101, false)]
    public async Task PlanLimit_BlocksWhenUsedReachesTheLimit(long used, bool passes)
    {
        _preferences.FindAsync(1, "gemini", default).Returns(false);
        _plans.FindActivePlanAsync(1, default).Returns(new ActivePlan("Bronze", 100, 50, 10));
        _usage.SumSystemMonthAsync(1, "gemini", "2026-10", default).Returns(used);

        var result = await Provider().ResolveAsync(1, "gemini", default);

        if (passes)
        {
            result.ShouldBeOfType<ApiKeyResolution.Resolved>();
            return;
        }

        var failed = AsFailed(result);
        failed.Reason.ShouldBe(ApiKeyFailure.LimitReached);
        failed.Message.ShouldBe(ApiKeyMessages.LimitReached("gemini", "Bronze", 100, used));
        await _usage.DidNotReceiveWithAnyArgs().IncrementAsync(default, default!, default!, default!, default);
    }

    [Fact]
    [Trait("AC", "008-66")]
    public async Task PlanLimit_Null_IsUnlimited()
    {
        _preferences.FindAsync(1, "gemini", default).Returns(false);
        _plans.FindActivePlanAsync(1, default).Returns(new ActivePlan("Ouro", null, null, null));
        _usage.SumSystemMonthAsync(1, "gemini", "2026-10", default).Returns(1_000_000);

        (await Provider().ResolveAsync(1, "gemini", default)).ShouldBeOfType<ApiKeyResolution.Resolved>();
    }

    [Fact]
    [Trait("AC", "008-67")]
    public async Task DatabaseErrorsInBestEffortSteps_DoNotPreventReturningTheKey_AndNeverLeakSecrets()
    {
        _preferences.FindAsync(1, "gemini", default).Returns(true);
        _keys.FindActiveEncryptedAsync(1, "gemini", default).Returns(Token);
        var boom = new InvalidOperationException($"detalhe sensível {Secret}");
        _keys.TouchLastUseAsync(1, "gemini", default).ThrowsAsync(boom);
        _usage.IncrementAsync(1, "gemini", "propria", "2026-10", default).ThrowsAsync(boom);
        _log.InsertAsync(default, default!, default!, default!, default, default, default).ThrowsForAnyArgs(boom);

        var resolved = (await Provider().ResolveAsync(1, "gemini", default)).ShouldBeOfType<ApiKeyResolution.Resolved>();

        resolved.Key.ShouldBe(Secret);
        _diagnostics.Received(1).Swallowed("atualizar_ultimo_uso", "InvalidOperationException");
        _diagnostics.Received(1).Swallowed("registrar_uso", "InvalidOperationException");
        _diagnostics.Received(1).Swallowed("registrar_log_acesso", "InvalidOperationException");
        foreach (var call in _diagnostics.ReceivedCalls())
        {
            string.Join('|', call.GetArguments().Select(a => a?.ToString())).ShouldNotContain(Secret);
        }
    }

    [Fact]
    [Trait("AC", "008-67")]
    public async Task LimitCheckError_FailsOpen()
    {
        _preferences.FindAsync(1, "gemini", default).Returns(false);
        _plans.FindActivePlanAsync(1, default).ThrowsAsync(new InvalidOperationException("falha"));

        (await Provider().ResolveAsync(1, "gemini", default)).ShouldBeOfType<ApiKeyResolution.Resolved>();

        _diagnostics.Received(1).Swallowed("verificar_limite", "InvalidOperationException");
    }

    [Fact]
    [Trait("AC", "008-69")]
    public async Task FailureIsLoggedWithTheReasonName_NeverWithTheMessageText()
    {
        _preferences.FindAsync(1, "gemini", default).Returns(true);
        _keys.FindActiveEncryptedAsync(1, "gemini", default).Returns((string?)null);

        await Provider().ResolveAsync(1, "gemini", default);

        await _log.Received(1).InsertAsync(1, "gemini", "propria", "obter_chave", false, "OwnKeyMissing", default);
    }

    [Fact]
    public async Task Cancellation_IsNotSwallowed()
    {
        _preferences.FindAsync(1, "gemini", Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() => Provider().ResolveAsync(1, "gemini", default));
    }
}
