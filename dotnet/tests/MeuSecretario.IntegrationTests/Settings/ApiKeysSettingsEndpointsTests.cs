using System.Net;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MeuSecretario.IntegrationTests.Settings;

[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class ApiKeysSettingsEndpointsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Path = "/api/user/api-keys";
    private readonly SettingsEnv _env = new(postgres);
    private int _user;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _user = await _env.InsertUserAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private const string DefaultList =
        """[{"provedor":"gemini","usar_chave_propria":false,"has_key":false},{"provedor":"weather","usar_chave_propria":false,"has_key":false},{"provedor":"openroute","usar_chave_propria":false,"has_key":false}]""";

    [Fact]
    [Trait("AC", "007-20")]
    public async Task Get_WithoutPreferences_ReturnsTheThreeProvidersInFixedOrder()
    {
        var (status, body) = await _env.Get(_user, Path);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(SettingsEnv.Data(DefaultList));
    }

    [Fact]
    [Trait("AC", "007-21")]
    public async Task Get_HasKeyOnlyForActiveKeys_AndNeverLeaksTheKey()
    {
        await _env.ExecuteAsync("INSERT INTO preferenciaschaveapi (usuario_id, provedor, usar_chave_propria) VALUES (@_user,'weather',true),(@_user,'gemini',true)", new { _user });
        await _env.ExecuteAsync(
            "INSERT INTO chavesapiusuario (usuario_id, provedor, chave_api_criptografada, ativo) VALUES (@_user,'weather',@s,true),(@_user,'gemini',@s,false)",
            new { _user, s = SettingsEnv.Sentinel });

        var (_, body) = await _env.Get(_user, Path);

        body.ShouldBe(SettingsEnv.Data(
            """[{"provedor":"gemini","usar_chave_propria":true,"has_key":false},{"provedor":"weather","usar_chave_propria":true,"has_key":true},{"provedor":"openroute","usar_chave_propria":false,"has_key":false}]"""));
        body.ShouldNotContain(SettingsEnv.Sentinel);
    }

    [Fact]
    [Trait("AC", "007-21")]
    public async Task Get_ActiveNullIsInactive()
    {
        await _env.ExecuteAsync("INSERT INTO chavesapiusuario (usuario_id, provedor, chave_api_criptografada, ativo) VALUES (@_user,'gemini','x',NULL)", new { _user });

        (await _env.Get(_user, Path)).Body.ShouldBe(SettingsEnv.Data(DefaultList));
    }

    [Fact]
    [Trait("AC", "007-20")]
    public async Task Get_KeyWithoutPreferenceStillShowsHasKey_Desvio088()
    {
        await _env.ExecuteAsync("INSERT INTO chavesapiusuario (usuario_id, provedor, chave_api_criptografada, ativo) VALUES (@_user,'openroute','x',true)", new { _user });

        (await _env.Get(_user, Path)).Body.ShouldContain("""{"provedor":"openroute","usar_chave_propria":false,"has_key":true}""");
    }

    [Fact]
    [Trait("AC", "007-22")]
    public async Task Put_OwnKey_StoresTheTrimmedKeyEncrypted_AndReplacesItWithoutConflict()
    {
        var (status, body) = await _env.Put(_user, Path, """{"type":"gemini","useOwnKey":true,"key":"  AIza-x  "}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(SettingsEnv.Success("Chave API salva com sucesso"));
        (await _env.ScalarAsync<bool>("SELECT usar_chave_propria FROM preferenciaschaveapi WHERE usuario_id = @_user AND provedor = 'gemini'", new { _user })).ShouldBeTrue();
        var stored = await _env.ScalarAsync<string>("SELECT chave_api_criptografada FROM chavesapiusuario WHERE usuario_id = @_user AND provedor = 'gemini'", new { _user });
        stored.ShouldNotContain("AIza");
        _env.Encryption.Decrypt(stored).ShouldBe("AIza-x");

        (await _env.Put(_user, Path, """{"type":"gemini","useOwnKey":true,"key":"outra"}""")).Status.ShouldBe(HttpStatusCode.OK);

        (await _env.ScalarAsync<int>("SELECT count(*)::int FROM chavesapiusuario WHERE usuario_id = @_user", new { _user })).ShouldBe(1);
        _env.Encryption.Decrypt(await _env.ScalarAsync<string>("SELECT chave_api_criptografada FROM chavesapiusuario WHERE usuario_id = @_user", new { _user })).ShouldBe("outra");
    }

    [Fact]
    [Trait("AC", "007-23")]
    public async Task Put_NotUsingOwnKey_DeactivatesWithoutDeleting_AndReactivationWorks()
    {
        await _env.Put(_user, Path, """{"type":"gemini","useOwnKey":true,"key":"k1"}""");
        var cipher = await _env.ScalarAsync<string>("SELECT chave_api_criptografada FROM chavesapiusuario WHERE usuario_id = @_user", new { _user });

        (await _env.Put(_user, Path, """{"type":"gemini","useOwnKey":false}""")).Status.ShouldBe(HttpStatusCode.OK);

        (await _env.ScalarAsync<bool>("SELECT usar_chave_propria FROM preferenciaschaveapi WHERE usuario_id = @_user", new { _user })).ShouldBeFalse();
        (await _env.ScalarAsync<bool>("SELECT ativo FROM chavesapiusuario WHERE usuario_id = @_user", new { _user })).ShouldBeFalse();
        (await _env.ScalarAsync<string>("SELECT chave_api_criptografada FROM chavesapiusuario WHERE usuario_id = @_user", new { _user })).ShouldBe(cipher);

        await _env.Put(_user, Path, """{"type":"gemini","useOwnKey":true,"key":"k2"}""");
        (await _env.ScalarAsync<bool>("SELECT ativo FROM chavesapiusuario WHERE usuario_id = @_user", new { _user })).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "007-23")]
    public async Task Put_NotUsingOwnKey_WithoutExistingKey_OnlyWritesThePreference()
    {
        (await _env.Put(_user, Path, """{"provedor":"weather","useOwnKey":false}""")).Status.ShouldBe(HttpStatusCode.OK);

        (await _env.ScalarAsync<int>("SELECT count(*)::int FROM preferenciaschaveapi WHERE usuario_id = @_user", new { _user })).ShouldBe(1);
        (await _env.ScalarAsync<int>("SELECT count(*)::int FROM chavesapiusuario", null)).ShouldBe(0);
    }

    [Theory]
    [Trait("AC", "007-24")]
    [InlineData("""{"type":"gemini","useOwnKey":true}""")]
    [InlineData("""{"type":"gemini","useOwnKey":true,"key":"   "}""")]
    [InlineData("""{"type":"gemini","useOwnKey":true,"key":null}""")]
    [InlineData("""{"type":"gemini","useOwnKey":false,"key":5}""")]
    public async Task Put_OwnKeyRequired_Returns400_AndWritesNothing(string json)
    {
        var (status, body) = await _env.Put(_user, Path, json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(SettingsEnv.Error("Chave API obrigatória quando usar_chave_propria=true"));
        (await _env.ScalarAsync<int>("SELECT count(*)::int FROM preferenciaschaveapi", null)).ShouldBe(0);
    }

    [Theory]
    [Trait("AC", "007-25")]
    [InlineData("""{"type":"openai","useOwnKey":false}""")]
    [InlineData("""{"useOwnKey":false}""")]
    [InlineData("""{"type":null}""")]
    [InlineData("""{"type":5}""")]
    [InlineData("""{"type":"GEMINI"}""")]
    [InlineData("""{"type":["gemini"]}""")]
    [InlineData("{}")]
    public async Task Put_InvalidProvider_Returns400(string json)
    {
        var (status, body) = await _env.Put(_user, Path, json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(SettingsEnv.Error("Provedor inválido. Use: gemini, weather, openroute"));
    }

    [Fact]
    [Trait("AC", "007-25")]
    public async Task Put_EmptyTypeFallsBackToProvedor()
    {
        (await _env.Put(_user, Path, """{"type":"","provedor":"openroute","useOwnKey":false}""")).Status.ShouldBe(HttpStatusCode.OK);

        (await _env.ScalarAsync<string>("SELECT provedor FROM preferenciaschaveapi WHERE usuario_id = @_user", new { _user })).ShouldBe("openroute");
    }

    [Theory]
    [Trait("AC", "007-26")]
    [InlineData("""{"type":"gemini","useOwnKey":"false"}""")]
    [InlineData("""{"type":"gemini","useOwnKey":1}""")]
    public async Task Put_UseOwnKeyOfAnotherType_Returns400_Desvio084(string json)
    {
        var (status, body) = await _env.Put(_user, Path, json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(SettingsEnv.Error("Dados inválidos"));
    }

    [Fact]
    [Trait("AC", "007-27")]
    public async Task Put_FailureOnTheKeyInsert_RollsBackThePreferenceToo_AndReturnsFixed500()
    {
        await _env.ExecuteAsync(
            """
            CREATE FUNCTION falha_chave_007() RETURNS trigger AS $$ BEGIN RAISE EXCEPTION 'SENTINELA-segredo-7741'; END $$ LANGUAGE plpgsql;
            CREATE TRIGGER trg_falha_chave_007 BEFORE INSERT ON chavesapiusuario FOR EACH ROW EXECUTE FUNCTION falha_chave_007();
            """);
        try
        {
            var log = new CapturingLogger<MeuSecretario.Api.Settings.SettingsModule>();
            await using var factory = postgres.Api.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<ILogger<MeuSecretario.Api.Settings.SettingsModule>>(log)));

            var (status, body) = await new SettingsEnv(postgres, factory).Put(_user, Path, """{"type":"gemini","useOwnKey":true,"key":"SENTINELA-chave-1"}""");

            status.ShouldBe(HttpStatusCode.InternalServerError);
            body.ShouldBe(SettingsEnv.Error("Erro ao salvar chave API"));
            (await _env.ScalarAsync<int>("SELECT count(*)::int FROM preferenciaschaveapi", null)).ShouldBe(0);
            log.Messages.ShouldHaveSingleItem().ShouldNotContain("SENTINELA");
        }
        finally
        {
            await _env.ExecuteAsync("DROP TRIGGER trg_falha_chave_007 ON chavesapiusuario; DROP FUNCTION falha_chave_007();");
        }
    }

    [Fact]
    [Trait("AC", "007-27")]
    public async Task Put_ForAUserThatNoLongerExists_Returns500()
    {
        var (status, body) = await _env.Put(999, Path, """{"type":"gemini","useOwnKey":false}""");

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(SettingsEnv.Error("Erro ao salvar chave API"));
    }

    // ------------------------------------------------------------------ validate

    private SettingsEnv WithValidator(IProviderKeyValidator validator, out WebApplicationFactory<Program> factory)
    {
        factory = postgres.Api.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton(validator)));
        return new SettingsEnv(postgres, factory);
    }

    [Theory]
    [Trait("AC", "007-30")]
    [InlineData(KeyValidation.Valid, "true")]
    [InlineData(KeyValidation.Invalid, "false")]
    public async Task Validate_ReturnsIsValid_AndNeverStoresTheKey(KeyValidation result, string expected)
    {
        var validator = new FakeValidator((_, key) => key == SettingsEnv.Sentinel ? result : KeyValidation.NotSupported);
        var env = WithValidator(validator, out var factory);
        await using var _ = factory;

        var (status, body) = await env.Post(_user, Path + "/validate", $$"""{"type":"gemini","key":" {{SettingsEnv.Sentinel}} "}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(SettingsEnv.Data($$"""{"is_valid":{{expected}}}"""));
        body.ShouldNotContain(SettingsEnv.Sentinel);
        (await _env.ScalarAsync<int>("SELECT count(*)::int FROM chavesapiusuario", null)).ShouldBe(0);
    }

    [Theory]
    [Trait("AC", "007-31")]
    [InlineData("""{"key":"x"}""")]
    [InlineData("""{"type":"gemini"}""")]
    [InlineData("""{"type":"","key":"x"}""")]
    [InlineData("""{"type":"gemini","key":"   "}""")]
    [InlineData("""{"type":"gemini","key":5}""")]
    [InlineData("{}")]
    public async Task Validate_MissingFields_Returns400(string json)
    {
        var (status, body) = await _env.Post(_user, Path + "/validate", json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(SettingsEnv.Error("Campos 'type' e 'key' são obrigatórios"));
    }

    [Fact]
    [Trait("AC", "007-31")]
    public async Task Validate_UnsupportedProvider_Returns400_WithoutCallingTheValidator()
    {
        var validator = new FakeValidator((_, _) => KeyValidation.Valid);
        var env = WithValidator(validator, out var factory);
        await using var _ = factory;

        var (status, body) = await env.Post(_user, Path + "/validate", """{"type":"openai","key":"x"}""");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(SettingsEnv.Error("Provedor não suportado para validação"));
        validator.Calls.ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "007-32")]
    [Trait("AC", "007-33")]
    public async Task Validate_ProviderUnavailable_Returns503_WithoutExceptionMessageKeyOrLeak()
    {
        var validator = new FakeValidator((_, _) => throw new ProviderUnavailableException("https://x/?key=" + SettingsEnv.Sentinel));
        var log = new CapturingLogger<MeuSecretario.Api.Settings.SettingsModule>();
        await using var factory = postgres.Api.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IProviderKeyValidator>(validator);
            s.AddSingleton<ILogger<MeuSecretario.Api.Settings.SettingsModule>>(log);
        }));

        var (status, body) = await new SettingsEnv(postgres, factory).Post(_user, Path + "/validate", $$"""{"type":"weather","key":"{{SettingsEnv.Sentinel}}"}""");

        status.ShouldBe(HttpStatusCode.ServiceUnavailable);
        body.ShouldBe(SettingsEnv.Error("Não foi possível validar (timeout ou erro de rede)"));
        log.Messages.ShouldHaveSingleItem().ShouldContain("ProviderUnavailableException");
        log.Messages.ShouldAllBe(m => !m.Contains("SENTINELA"));
    }

    private sealed class FakeValidator(Func<string, string, KeyValidation> behavior) : IProviderKeyValidator
    {
        public int Calls { get; private set; }

        public Task<KeyValidation> ValidateAsync(string provider, string key, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(behavior(provider, key));
        }
    }
}
