using MeuSecretario.Infrastructure.Configuration;

namespace MeuSecretario.UnitTests.Configuration;

public class AppSettingsTests
{
    private const string SecretKey = "0123456789abcdef0123456789abcdef"; // 32 caracteres

    private static Dictionary<string, string?> Valid() => new()
    {
        ["DATABASE_URL"] = "postgresql://u:p@h:5432/db",
        ["API_SECRET_KEY"] = SecretKey,
        ["ENCRYPTION_KEY"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
        ["GEMINI_API_KEY"] = "gemini",
        ["REDIS_URL"] = "redis://redis:6379",
    };

    private static AppSettings Load(Dictionary<string, string?> values) =>
        AppSettings.Load(key => values.GetValueOrDefault(key));

    [Fact]
    public void Load_WithRequiredVariables_AppliesDefaults()
    {
        var settings = Load(Valid());

        settings.BotSessionName.ShouldBe("sessao-principal");
        settings.TravelTimeDailyLimit.ShouldBe(10);
        settings.OpenRouteBaseUrl.ShouldBe("https://api.openrouteservice.org");
        settings.WebhookSignatureKey.ShouldBe(SecretKey); // padrão = API_SECRET_KEY
        settings.ConnectionString.ShouldContain("Host=h");
    }

    [Fact]
    public void Load_ListsEveryMissingRequiredVariable()
    {
        var ex = Should.Throw<InvalidOperationException>(() => Load([]));

        foreach (var name in new[] { "DATABASE_URL", "API_SECRET_KEY", "ENCRYPTION_KEY", "GEMINI_API_KEY", "REDIS_URL" })
        {
            ex.Message.ShouldContain(name);
        }
    }

    [Fact]
    public void Load_RejectsApiSecretKeyShorterThan32Characters()
    {
        var values = Valid();
        values["API_SECRET_KEY"] = "curta";

        var ex = Should.Throw<InvalidOperationException>(() => Load(values));

        ex.Message.ShouldContain("API_SECRET_KEY");
        ex.Message.ShouldContain("32");
    }

    [Fact]
    public void Load_ErrorMessageNeverContainsSecretValues()
    {
        var values = Valid();
        values["API_SECRET_KEY"] = "curta-demais-mas-secreta";
        values["DATABASE_URL"] = "mysql://u:senhaInterna@h/db";

        var ex = Should.Throw<InvalidOperationException>(() => Load(values));

        ex.Message.ShouldNotContain("curta-demais-mas-secreta");
        ex.Message.ShouldNotContain("senhaInterna");
    }

    [Fact]
    public void Load_RespectsExplicitWebhookSignatureKey()
    {
        var values = Valid();
        values["WEBHOOK_SIGNATURE_KEY"] = "outra-chave";

        Load(values).WebhookSignatureKey.ShouldBe("outra-chave");
    }

    [Fact]
    public void Load_RejectsNonNumericTravelLimit()
    {
        var values = Valid();
        values["TRAVEL_TIME_DAILY_LIMIT"] = "muitos";

        Should.Throw<InvalidOperationException>(() => Load(values)).Message.ShouldContain("TRAVEL_TIME_DAILY_LIMIT");
    }

    [Fact]
    [Trait("AC", "008-05")]
    public void Load_ApiKeysRoutes_AreDisabledByDefault()
    {
        Load(Valid()).ApiKeysRoutesEnabled.ShouldBeFalse();
    }

    [Theory]
    [Trait("AC", "008-05")]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData(" true ", true)]
    [InlineData("false", false)]
    [InlineData("1", false)]
    [InlineData("sim", false)]
    [InlineData("", false)]
    public void Load_ApiKeysRoutes_OnlyTrueEnables(string value, bool expected)
    {
        var values = Valid();
        values["API_KEYS_ROUTES_ENABLED"] = value;

        Load(values).ApiKeysRoutesEnabled.ShouldBe(expected);
    }

    [Fact]
    public void Load_PasswordHashIterations_DefaultsToWerkzeugValue()
    {
        Load(Valid()).PasswordHashIterations.ShouldBe(1_000_000);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("10")]
    [InlineData("999999999")]
    public void Load_RejectsInvalidPasswordHashIterations(string value)
    {
        var values = Valid();
        values["PASSWORD_HASH_ITERATIONS"] = value;

        Should.Throw<InvalidOperationException>(() => Load(values)).Message.ShouldContain("PASSWORD_HASH_ITERATIONS");
    }

    [Theory]
    [InlineData("chave-fernet-invalida")]
    [InlineData("###")]
    [InlineData("AAECAwQ=")] // base64 válido, mas só 5 bytes
    public void Load_RejectsEncryptionKeyThatIsNotAFernetKey_WithoutLeakingIt(string key)
    {
        var values = Valid();
        values["ENCRYPTION_KEY"] = key;

        var ex = Should.Throw<InvalidOperationException>(() => Load(values));

        ex.Message.ShouldContain("ENCRYPTION_KEY");
        ex.Message.ShouldNotContain(key);
    }
}
