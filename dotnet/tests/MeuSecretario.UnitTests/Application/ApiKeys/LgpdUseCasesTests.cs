using System.Security.Cryptography;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.ApiKeys;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MeuSecretario.UnitTests.Application.ApiKeys;

/// <summary>Casos de uso LGPD (spec 008 §4.10 a 4.15), AC-008-40 a 49.</summary>
public class LgpdUseCasesTests
{
    private readonly IConsentRepository _consents = Substitute.For<IConsentRepository>();
    private readonly IAccountDataRepository _data = Substitute.For<IAccountDataRepository>();
    private readonly IApiKeyDiagnostics _diagnostics = Substitute.For<IApiKeyDiagnostics>();
    private readonly IEncryptionService _encryption = Substitute.For<IEncryptionService>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IUserCacheCleaner _cache = Substitute.For<IUserCacheCleaner>();

    public LgpdUseCasesTests()
    {
        _clock.UtcNow.Returns(new DateTimeOffset(2026, 10, 2, 15, 0, 0, TimeSpan.Zero));
        _clock.Now.Returns(new DateTime(2026, 10, 2, 12, 0, 0));
        _encryption.LooksEncrypted(Arg.Any<string>()).Returns(call => call.Arg<string>().StartsWith("gAAAAA", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- RegisterConsent

    [Theory]
    [Trait("AC", "008-41")]
    [InlineData(null, "uso_dados_pessoais", true)]
    [InlineData(0, "uso_dados_pessoais", true)]
    [InlineData(1, null, true)]
    [InlineData(1, "", true)]
    [InlineData(1, "uso_dados_pessoais", null)]
    public async Task RegisterConsent_MissingFields(int? userId, string? type, bool? given)
    {
        var result = await new RegisterConsent(_consents).ExecuteAsync(new RegisterConsentInput(userId, type, given, null), default);

        result.ShouldBeOfType<RegisterConsentResult.MissingFields>();
        await _consents.DidNotReceiveWithAnyArgs().UpsertAsync(default, default!, default, default!, default);
    }

    [Fact]
    [Trait("AC", "008-42")]
    public async Task RegisterConsent_UnknownType_ReturnsThePythonMessage()
    {
        var result = await new RegisterConsent(_consents).ExecuteAsync(new RegisterConsentInput(1, "marketing", true, null), default);

        result.ShouldBeOfType<RegisterConsentResult.InvalidType>().Message.ShouldStartWith("Tipo de consentimento inválido: marketing. Válidos: ['uso_dados_pessoais'");
    }

    [Fact]
    [Trait("AC", "008-40")]
    public async Task RegisterConsent_FalseIsValid_AndTheDefaultVersionIsOnePointZero()
    {
        _consents.UpsertAsync(1, "rastreamento_uso", false, "1.0", default).Returns(new UpsertConsentResult.Saved(9));

        var result = await new RegisterConsent(_consents).ExecuteAsync(new RegisterConsentInput(1, "rastreamento_uso", false, ""), default);

        result.ShouldBe(new RegisterConsentResult.Success(9, "rastreamento_uso", false));
    }

    [Fact]
    [Trait("AC", "008-43")]
    public async Task RegisterConsent_UsesTheGivenVersion_AndReportsAnUnknownUser()
    {
        _consents.UpsertAsync(2, "uso_dados_pessoais", true, "2.1", default).Returns(new UpsertConsentResult.UserNotFound());

        var result = await new RegisterConsent(_consents).ExecuteAsync(new RegisterConsentInput(2, "uso_dados_pessoais", true, "2.1"), default);

        result.ShouldBe(new RegisterConsentResult.UserNotFound(2));
    }

    // ---------------------------------------------------------------- ListConsents / CheckConsent

    [Fact]
    [Trait("AC", "008-44")]
    public async Task ListConsents_DatabaseError_ReturnsEmpty_AndIsLogged()
    {
        _consents.ListAsync(1, default).ThrowsAsync(new InvalidOperationException("detalhe sensível"));

        var rows = await new ListConsents(_consents, _diagnostics).ExecuteAsync(1, default);

        rows.ShouldBeEmpty();
        _diagnostics.Received(1).Swallowed("listar_consentimentos", "InvalidOperationException");
    }

    [Fact]
    [Trait("AC", "008-46")]
    public async Task CheckConsent_NoRecordIsFalse_LatestDecidesAndErrorIsFalse()
    {
        _consents.LatestGivenAsync(1, "a", default).Returns((bool?)null);
        _consents.LatestGivenAsync(1, "b", default).Returns(true);
        _consents.LatestGivenAsync(1, "c", default).Returns(false);
        _consents.LatestGivenAsync(1, "d", default).ThrowsAsync(new InvalidOperationException("falha"));
        var useCase = new CheckConsent(_consents, _diagnostics);

        (await useCase.ExecuteAsync(1, "a", default)).ShouldBeFalse();
        (await useCase.ExecuteAsync(1, "b", default)).ShouldBeTrue();
        (await useCase.ExecuteAsync(1, "c", default)).ShouldBeFalse();
        (await useCase.ExecuteAsync(1, "d", default)).ShouldBeFalse();
        _diagnostics.Received(1).Swallowed("verificar_consentimento", "InvalidOperationException");
    }

    // ---------------------------------------------------------------- ExportUserData

    private ExportUserData Export() => new(_data, _consents, _encryption, _clock);

    private static ExportData Data(SubscriptionRow? subscription, params ExportKeyRow[] keys) =>
        new(keys, [], [], [], subscription);

    [Fact]
    [Trait("AC", "008-47")]
    public async Task Export_DecryptsFernetKeys_AndKeepsLegacyPlainText()
    {
        const string token = "gAAAAAB-token-fernet-com-mais-de-quarenta-caracteres";
        _encryption.Decrypt(token).Returns("chave-decifrada");
        _data.ExportAsync(1, default).Returns(Data(null,
            new ExportKeyRow(1, "gemini", token, true, null, null, null),
            new ExportKeyRow(2, "weather", "texto-puro", true, null, null, null)));
        _consents.ListAsync(1, default).Returns([]);

        var result = await Export().ExecuteAsync(1, default);

        result.Keys.Select(k => k.Key).ShouldBe(["chave-decifrada", "texto-puro"]);
        result.Subscription.ShouldBeNull();
        result.ExportedAt.ShouldBe(new DateTime(2026, 10, 2, 12, 0, 0));
    }

    [Fact]
    [Trait("AC", "008-47")]
    public async Task Export_FernetTokenThatDoesNotDecrypt_Throws_WithoutLeakingTheToken()
    {
        const string token = "gAAAAAB-token-corrompido-com-mais-de-quarenta-caracteres";
        _encryption.Decrypt(token).Throws(new CryptographicException("falha"));
        _data.ExportAsync(1, default).Returns(Data(null, new ExportKeyRow(1, "gemini", token, true, null, null, null)));
        _consents.ListAsync(1, default).Returns([]);

        var ex = await Should.ThrowAsync<CryptographicException>(() => Export().ExecuteAsync(1, default));

        ex.Message.ShouldNotContain(token);
    }

    [Theory]
    [Trait("AC", "008-47")]
    [InlineData("ativo", null, true)]
    [InlineData("ativo", "2026-10-03T00:00:00Z", true)]
    [InlineData("ativo", "2026-10-01T00:00:00Z", false)]
    [InlineData("cancelado", null, false)]
    [InlineData("cancelado", "2026-12-01T00:00:00Z", false)]
    public async Task Export_SubscriptionActive_IsDerivedFromStatusAndEndDate(string status, string? end, bool expected)
    {
        DateTime? endDate = end is null ? null : DateTime.Parse(end, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal);
        _data.ExportAsync(1, default).Returns(Data(new SubscriptionRow(5, "Prata", 29.90m, 500, 200, null, null, endDate, status)));
        _consents.ListAsync(1, default).Returns([]);

        var result = await Export().ExecuteAsync(1, default);

        result.Subscription!.Active.ShouldBe(expected);
        result.Subscription.PlanName.ShouldBe("Prata");
    }

    // ---------------------------------------------------------------- DeleteAccount

    private static AccountDeletionCounts Counts() => new(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13);

    [Theory]
    [Trait("AC", "008-48")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("confirmo_delecao")]
    [InlineData("CONFIRMO")]
    public async Task DeleteAccount_WithoutTheExactConfirmation_DeletesNothing(string? confirmation)
    {
        var result = await new DeleteAccount(_data, _cache, _diagnostics).ExecuteAsync(new DeleteAccountInput(1, confirmation), default);

        result.ShouldBeOfType<DeleteAccountResult.NotConfirmed>();
        await _data.DidNotReceiveWithAnyArgs().DeleteAccountCompletelyAsync(default, default);
        await _cache.DidNotReceiveWithAnyArgs().ClearAsync(default, default);
    }

    [Fact]
    [Trait("AC", "008-49")]
    public async Task DeleteAccount_Success_SumsTheCounters_AndClearsTheCacheBeforeDeleting()
    {
        _data.DeleteAccountCompletelyAsync(7, default).Returns(Counts());

        var result = await new DeleteAccount(_data, _cache, _diagnostics).ExecuteAsync(new DeleteAccountInput(7, "CONFIRMO_DELECAO"), default);

        var success = result.ShouldBeOfType<DeleteAccountResult.Success>();
        success.Counts.Total.ShouldBe(91);
        // O cache é limpo ANTES: as chaves do Redis usam o WhatsApp, que some junto com a linha de usuarios.
        Received.InOrder(() =>
        {
            _cache.ClearAsync(7, default);
            _data.DeleteAccountCompletelyAsync(7, default);
        });
    }

    [Fact]
    [Trait("AC", "008-49")]
    public async Task DeleteAccount_UnknownUser_IsNotFound()
    {
        _data.DeleteAccountCompletelyAsync(7, default).Returns((AccountDeletionCounts?)null);

        var result = await new DeleteAccount(_data, _cache, _diagnostics).ExecuteAsync(new DeleteAccountInput(7, "CONFIRMO_DELECAO"), default);

        result.ShouldBe(new DeleteAccountResult.UserNotFound(7));
    }

    [Fact]
    [Trait("AC", "008-49")]
    public async Task DeleteAccount_CacheFailure_DoesNotPreventTheDeletion()
    {
        _data.DeleteAccountCompletelyAsync(7, default).Returns(Counts());
        _cache.ClearAsync(7, default).ThrowsAsync(new InvalidOperationException("redis fora do ar"));

        var result = await new DeleteAccount(_data, _cache, _diagnostics).ExecuteAsync(new DeleteAccountInput(7, "CONFIRMO_DELECAO"), default);

        result.ShouldBeOfType<DeleteAccountResult.Success>();
        _diagnostics.Received(1).Swallowed("limpar_cache_usuario", "InvalidOperationException");
    }
}
