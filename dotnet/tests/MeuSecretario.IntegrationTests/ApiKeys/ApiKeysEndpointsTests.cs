using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure.Security;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.TestFixtures;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace MeuSecretario.IntegrationTests.ApiKeys;

/// <summary>
/// Spec 008 (rotas 1 a 9 e 16) ponta a ponta: HTTP real → casos de uso → PostgreSQL 15.
/// Corpos comparados como texto exato (nomes, ordem e acentos são o contrato).
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public partial class ApiKeysEndpointsTests(PostgresFixture postgres) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        await ApiKeysTestSupport.SeedPlansAsync(postgres);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Client(bool withKey = true) => ApiKeysTestSupport.EnabledClient(postgres, withKey);

    internal static async Task<(HttpStatusCode Status, string Body)> Send(HttpClient client, HttpMethod method, string path, string? json = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Troca os timestamps (formato do isoformat do Python, com +00:00) por um marcador, validando o formato.</summary>
    internal static string NormalizeTimestamps(string body) => TimestampRegex().Replace(body, "\"<ts>\"");

    [GeneratedRegex("\"\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}(\\.\\d{6})?\\+00:00\"")]
    private static partial Regex TimestampRegex();

    internal static string Error(string erro, string mensagem) => $$"""{"erro":"{{erro}}","mensagem":"{{mensagem}}"}""";

    private const string Unauthorized = """{"erro":"Não autorizado","mensagem":"API key inválida ou ausente"}""";

    // ============================== autenticação e roteamento ==============================

    [Theory]
    [Trait("AC", "008-01")]
    [InlineData("POST", "/api-keys/usuario/1", """{"provedor":"gemini","chave_api":"k"}""")]
    [InlineData("GET", "/api-keys/usuario/1", null)]
    [InlineData("PUT", "/api-keys/chave/1", """{"ativo":false,"usuario_id":1}""")]
    [InlineData("DELETE", "/api-keys/chave/1?usuario_id=1", null)]
    [InlineData("POST", "/api-keys/preferencias/1", """{"provedor":"gemini","usar_chave_propria":true}""")]
    [InlineData("GET", "/api-keys/preferencias/1", null)]
    [InlineData("DELETE", "/api-keys/preferencias/1/gemini", null)]
    [InlineData("GET", "/api-keys/uso/1", null)]
    [InlineData("GET", "/api-keys/logs/1", null)]
    public async Task MissingOrWrongApiKey_Returns401_AndNothingIsReadOrWritten(string method, string path, string? json)
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);
        await ApiKeysTestSupport.SeedKeyAsync(postgres, userId, "gemini", "original");

        foreach (var header in new string?[] { null, "", "errada" })
        {
            using var client = Client(withKey: false);
            if (header is not null)
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-API-KEY", header);
            }

            var (status, body) = await Send(client, new HttpMethod(method), path, json);

            status.ShouldBe(HttpStatusCode.Unauthorized);
            body.ShouldBe(Unauthorized);
        }

        (await ApiKeysTestSupport.Scalar<int>(postgres, "SELECT count(*) FROM chavesapiusuario")).ShouldBe(1);
        (await ApiKeysTestSupport.Scalar<string>(postgres, "SELECT chave_api_criptografada FROM chavesapiusuario")).ShouldBe("original");
        (await ApiKeysTestSupport.Scalar<bool>(postgres, "SELECT ativo FROM chavesapiusuario")).ShouldBeTrue();
        (await ApiKeysTestSupport.Scalar<int>(postgres, "SELECT count(*) FROM preferenciaschaveapi")).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "008-03")]
    public async Task Health_NeedsNoApiKey()
    {
        var (status, body) = await Send(Client(withKey: false), HttpMethod.Get, "/api-keys/health");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"ok","servico":"API Keys Management","versao":"1.0.0"}""");
    }

    [Theory]
    [Trait("AC", "008-04")]
    [InlineData("/api-keys/usuario/abc")]
    [InlineData("/api-keys/usuario/-1")]
    [InlineData("/api-keys/usuario/1/")]
    [InlineData("/api-keys/preferencias/abc")]
    [InlineData("/api-keys/uso/1.5")]
    [InlineData("/api-keys/usuario/99999999999")] // acima de int.MaxValue: o Flask daria 500 do banco
    public async Task NonNumericOrNegativeIds_Return404(string path)
    {
        var (status, _) = await Send(Client(), HttpMethod.Get, path);

        status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("AC", "008-05")]
    public async Task WithTheFlagOff_EveryRouteIs404_EvenWithTheRightKey()
    {
        // postgres.Api não liga API_KEYS_ROUTES_ENABLED (padrão false).
        using var client = postgres.Api.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-KEY", AuthFixtures.JwtSecret);

        foreach (var (method, path) in new[]
                 {
                     ("GET", "/api-keys/health"),
                     ("POST", "/api-keys/usuario/1"),
                     ("GET", "/api-keys/usuario/1"),
                     ("PUT", "/api-keys/chave/1"),
                     ("DELETE", "/api-keys/chave/1?usuario_id=1"),
                     ("POST", "/api-keys/preferencias/1"),
                     ("GET", "/api-keys/preferencias/1"),
                     ("DELETE", "/api-keys/preferencias/1/gemini"),
                     ("GET", "/api-keys/uso/1"),
                     ("GET", "/api-keys/logs/1"),
                     ("POST", "/api-keys/lgpd/consentimento"),
                     ("GET", "/api-keys/lgpd/consentimentos/1"),
                     ("GET", "/api-keys/lgpd/consentimentos-iniciais/1"),
                     ("GET", "/api-keys/lgpd/exportar/1"),
                     ("DELETE", "/api-keys/lgpd/deletar-conta/1"),
                     ("GET", "/api-keys/lgpd/verificar-consentimento/1/uso_dados_pessoais"),
                 })
        {
            var (status, _) = await Send(client, new HttpMethod(method), path, method is "POST" or "PUT" ? "{}" : null);
            status.ShouldBe(HttpStatusCode.NotFound, $"{method} {path}");
        }
    }

    // ============================== POST /usuario/<id> ==============================

    [Fact]
    [Trait("AC", "008-10")]
    public async Task RegisterKey_Returns201_AndStoresAFernetTokenWithoutTrimming()
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);

        var (status, body) = await Send(Client(), HttpMethod.Post, $"/api-keys/usuario/{userId}", """{"provedor":"gemini","chave_api":" minha-chave "}""");

        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldBe("""{"mensagem":"Chave cadastrada com sucesso","id":1,"provedor":"gemini","usuario_id":1}""");

        var stored = await ApiKeysTestSupport.Scalar<string>(postgres, "SELECT chave_api_criptografada FROM chavesapiusuario WHERE id = 1");
        stored.ShouldStartWith("gAAAAA");
        new FernetEncryptionService(AuthFixtures.FernetKey, TimeProvider.System).Decrypt(stored).ShouldBe(" minha-chave ");
        (await ApiKeysTestSupport.Scalar<bool>(postgres, "SELECT ativo FROM chavesapiusuario WHERE id = 1")).ShouldBeTrue();
    }

    [Theory]
    [Trait("AC", "008-11")]
    [InlineData("""{"chave_api":"k"}""")]
    [InlineData("""{"provedor":"gemini"}""")]
    [InlineData("""{"provedor":"","chave_api":"k"}""")]
    [InlineData("""{"provedor":"gemini","chave_api":""}""")]
    [InlineData("""{"provedor":null,"chave_api":"k"}""")]
    [InlineData("isto não é json")]
    [InlineData("[1,2]")]
    [InlineData("")]
    public async Task RegisterKey_MissingFieldsOrInvalidBody_Returns400(string json)
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);

        var (status, body) = await Send(Client(), HttpMethod.Post, $"/api-keys/usuario/{userId}", json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(Error("Dados incompletos", "Provedor e chave_api são obrigatórios"));
    }

    [Fact]
    [Trait("AC", "008-11")]
    public async Task RegisterKey_WithoutContentType_Returns400()
    {
        var client = Client();
        var response = await client.PostAsync(new Uri("/api-keys/usuario/1", UriKind.Relative), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldBe(Error("Dados incompletos", "Provedor e chave_api são obrigatórios"));
    }

    [Theory]
    [Trait("AC", "008-12")]
    [InlineData("openai")]
    [InlineData("Gemini")]
    [InlineData("GEMINI")]
    public async Task RegisterKey_InvalidProvider_Returns400(string provider)
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);

        var (status, body) = await Send(Client(), HttpMethod.Post, $"/api-keys/usuario/{userId}", $$"""{"provedor":"{{provider}}","chave_api":"k"}""");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(Error("Provedor inválido", "Provedor deve ser um de: gemini, weather, openroute"));
    }

    [Theory]
    [Trait("AC", "008-23")]
    [InlineData("""{"provedor":1,"chave_api":"k"}""")]
    [InlineData("""{"provedor":"gemini","chave_api":123}""")]
    [InlineData("""{"provedor":["gemini"],"chave_api":"k"}""")]
    [InlineData("""{"provedor":"gemini","chave_api":true}""")]
    public async Task RegisterKey_WrongJsonTypes_Return400NotAnInternalError(string json)
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);

        var (status, body) = await Send(Client(), HttpMethod.Post, $"/api-keys/usuario/{userId}", json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(Error("Dados incompletos", "Provedor e chave_api são obrigatórios"));
    }

    [Fact]
    [Trait("AC", "008-13")]
    public async Task RegisterKey_UnknownUser_Returns404()
    {
        var (status, body) = await Send(Client(), HttpMethod.Post, "/api-keys/usuario/777", """{"provedor":"gemini","chave_api":"segredo-777"}""");

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(Error("Usuário não encontrado", "Nenhum usuário com ID 777"));
        body.ShouldNotContain("segredo-777");
    }

    [Fact]
    [Trait("AC", "008-13")]
    public async Task RegisterKey_Duplicate_Returns409_EvenWhenTheExistingKeyIsInactive()
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);
        await ApiKeysTestSupport.SeedKeyAsync(postgres, userId, "gemini", "existente", active: false);

        var (status, body) = await Send(Client(), HttpMethod.Post, $"/api-keys/usuario/{userId}", """{"provedor":"gemini","chave_api":"nova-segredo"}""");

        status.ShouldBe(HttpStatusCode.Conflict);
        body.ShouldBe(Error("Chave já cadastrada", "Já existe uma chave para este provedor. Use PUT /api-keys/chave/<id>."));
        body.ShouldNotContain("chavesapiusuario");
        body.ShouldNotContain("nova-segredo");
    }

    // ============================== GET /usuario/<id> ==============================

    [Fact]
    [Trait("AC", "008-14")]
    public async Task ListKeys_IsOrderedByProvider_NeverReturnsTheKey_AndUsesPythonTimestamps()
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);
        await ApiKeysTestSupport.SeedKeyAsync(postgres, userId, "weather", "segredo-w");
        await ApiKeysTestSupport.SeedKeyAsync(postgres, userId, "gemini", "segredo-g", active: false);
        await ApiKeysTestSupport.Execute(postgres, "UPDATE chavesapiusuario SET ultimo_uso_em = '2026-10-02 13:45:10.123456+00' WHERE provedor = 'weather'");

        var (status, body) = await Send(Client(), HttpMethod.Get, $"/api-keys/usuario/{userId}");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldNotContain("segredo");
        body.ShouldContain("\"ultimo_uso_em\":\"2026-10-02T13:45:10.123456+00:00\"");
        NormalizeTimestamps(body).ShouldBe(
            """{"usuario_id":1,"total":2,"chaves":[""" +
            """{"id":2,"provedor":"gemini","ativo":false,"ultimo_uso_em":null,"criado_em":"<ts>","atualizado_em":"<ts>"},""" +
            """{"id":1,"provedor":"weather","ativo":true,"ultimo_uso_em":"<ts>","criado_em":"<ts>","atualizado_em":"<ts>"}]}""");
    }

    [Fact]
    [Trait("AC", "008-14")]
    public async Task ListKeys_UserWithoutKeysOrUnknown_ReturnsTotalZero()
    {
        var (status, body) = await Send(Client(), HttpMethod.Get, "/api-keys/usuario/42");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"usuario_id":42,"total":0,"chaves":[]}""");
    }

    // ============================== PUT /chave/<id> ==============================

    [Fact]
    [Trait("AC", "008-15")]
    public async Task UpdateKey_WithNewKey_RewritesTheFernetToken_AndTouchesUpdatedAt()
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);
        var keyId = await ApiKeysTestSupport.SeedKeyAsync(postgres, userId, "gemini", "antiga");
        await ApiKeysTestSupport.Execute(postgres, "UPDATE chavesapiusuario SET atualizado_em = '2020-01-01+00'");

        var (status, body) = await Send(Client(), HttpMethod.Put, $"/api-keys/chave/{keyId}", $$"""{"chave_api":"nova","usuario_id":{{userId}}}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"mensagem":"Chave atualizada com sucesso","id":1,"provedor":"gemini","usuario_id":1}""");
        var stored = await ApiKeysTestSupport.Scalar<string>(postgres, "SELECT chave_api_criptografada FROM chavesapiusuario WHERE id = 1");
        new FernetEncryptionService(AuthFixtures.FernetKey, TimeProvider.System).Decrypt(stored).ShouldBe("nova");
        (await ApiKeysTestSupport.Scalar<bool>(postgres, "SELECT atualizado_em > '2020-01-02+00' FROM chavesapiusuario WHERE id = 1")).ShouldBeTrue();
    }

    [Theory]
    [Trait("AC", "008-15")]
    [InlineData("""{"ativo":false,"usuario_id":1}""")]
    [InlineData("""{"chave_api":"","ativo":false,"usuario_id":1}""")]
    public async Task UpdateKey_OnlyActiveFalse_DoesNotTouchTheKey(string json)
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);
        var keyId = await ApiKeysTestSupport.SeedKeyAsync(postgres, userId, "gemini", "intacta");

        var (status, _) = await Send(Client(), HttpMethod.Put, $"/api-keys/chave/{keyId}", json);

        status.ShouldBe(HttpStatusCode.OK);
        (await ApiKeysTestSupport.Scalar<string>(postgres, "SELECT chave_api_criptografada FROM chavesapiusuario WHERE id = 1")).ShouldBe("intacta");
        (await ApiKeysTestSupport.Scalar<bool>(postgres, "SELECT ativo FROM chavesapiusuario WHERE id = 1")).ShouldBeFalse();
    }

    [Theory]
    [Trait("AC", "008-16")]
    [InlineData("""{"usuario_id":1}""")]
    [InlineData("""{"ativo":null,"usuario_id":1}""")]
    [InlineData("""{"chave_api":"","usuario_id":1}""")]
    [InlineData("isto não é json")]
    [InlineData("""{"ativo":"true","usuario_id":1}""")]
    [InlineData("""{"chave_api":5,"usuario_id":1}""")]
    public async Task UpdateKey_NothingToUpdate_Returns400(string json)
    {
        var (status, body) = await Send(Client(), HttpMethod.Put, "/api-keys/chave/1", json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(Error("Dados incompletos", "Forneça pelo menos 'chave_api' ou 'ativo'"));
    }

    [Fact]
    [Trait("AC", "008-16")]
    public async Task UpdateKey_UnknownId_Returns404()
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);

        var (status, body) = await Send(Client(), HttpMethod.Put, "/api-keys/chave/999", $$"""{"ativo":true,"usuario_id":{{userId}}}""");

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(Error("Chave não encontrada", "Nenhuma chave com ID 999"));
    }

    // ============================== DELETE /chave/<id> ==============================

    [Fact]
    [Trait("AC", "008-18")]
    public async Task DeleteKey_SoftDeletes_AndRepeatingStillReturns200()
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);
        var keyId = await ApiKeysTestSupport.SeedKeyAsync(postgres, userId, "weather", "k");

        for (var i = 0; i < 2; i++)
        {
            var (status, body) = await Send(Client(), HttpMethod.Delete, $"/api-keys/chave/{keyId}?usuario_id={userId}");

            status.ShouldBe(HttpStatusCode.OK);
            body.ShouldBe("""{"mensagem":"Chave desativada com sucesso","id":1,"provedor":"weather"}""");
        }

        (await ApiKeysTestSupport.Scalar<bool>(postgres, "SELECT ativo FROM chavesapiusuario WHERE id = 1")).ShouldBeFalse();

        var (missingStatus, missingBody) = await Send(Client(), HttpMethod.Delete, $"/api-keys/chave/55?usuario_id={userId}");
        missingStatus.ShouldBe(HttpStatusCode.NotFound);
        missingBody.ShouldBe(Error("Chave não encontrada", "Nenhuma chave com ID 55"));
    }

    // ============================== preferências ==============================

    [Fact]
    [Trait("AC", "008-20")]
    public async Task SetPreference_Returns201_WithTheLabel_AndRepeatingUpdatesTheSameRow()
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);

        var (s1, b1) = await Send(Client(), HttpMethod.Post, $"/api-keys/preferencias/{userId}", """{"provedor":"gemini","usar_chave_propria":true}""");
        var (s2, b2) = await Send(Client(), HttpMethod.Post, $"/api-keys/preferencias/{userId}", """{"provedor":"gemini","usar_chave_propria":false}""");

        s1.ShouldBe(HttpStatusCode.Created);
        b1.ShouldBe("""{"mensagem":"Preferência configurada com sucesso","id":1,"usuario_id":1,"provedor":"gemini","usar_chave_propria":true,"tipo":"própria (grátis)"}""");
        s2.ShouldBe(HttpStatusCode.Created);
        b2.ShouldBe("""{"mensagem":"Preferência configurada com sucesso","id":1,"usuario_id":1,"provedor":"gemini","usar_chave_propria":false,"tipo":"sistema (cobrado)"}""");
        (await ApiKeysTestSupport.Scalar<int>(postgres, "SELECT count(*) FROM preferenciaschaveapi")).ShouldBe(1);
    }

    [Theory]
    [Trait("AC", "008-21")]
    [InlineData("""{"provedor":"gemini"}""")]
    [InlineData("""{"provedor":"gemini","usar_chave_propria":null}""")]
    [InlineData("""{"usar_chave_propria":true}""")]
    [InlineData("""{"provedor":"gemini","usar_chave_propria":"true"}""")]
    [InlineData("""{"provedor":"gemini","usar_chave_propria":1}""")]
    [InlineData("não é json")]
    public async Task SetPreference_MissingOrWrongType_Returns400(string json)
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);

        var (status, body) = await Send(Client(), HttpMethod.Post, $"/api-keys/preferencias/{userId}", json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(Error("Dados incompletos", "Provedor e usar_chave_propria são obrigatórios"));
    }

    [Fact]
    [Trait("AC", "008-21")]
    public async Task SetPreference_InvalidProvider_400_AndUnknownUser_404()
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);

        var (s1, b1) = await Send(Client(), HttpMethod.Post, $"/api-keys/preferencias/{userId}", """{"provedor":"openai","usar_chave_propria":true}""");
        var (s2, b2) = await Send(Client(), HttpMethod.Post, "/api-keys/preferencias/808", """{"provedor":"gemini","usar_chave_propria":false}""");

        s1.ShouldBe(HttpStatusCode.BadRequest);
        b1.ShouldBe(Error("Provedor inválido", "Provedor deve ser um de: gemini, weather, openroute"));
        s2.ShouldBe(HttpStatusCode.NotFound);
        b2.ShouldBe(Error("Usuário não encontrado", "Nenhum usuário com ID 808"));
    }

    [Fact]
    [Trait("AC", "008-22")]
    public async Task ListAndDeletePreferences()
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);
        await Send(Client(), HttpMethod.Post, $"/api-keys/preferencias/{userId}", """{"provedor":"weather","usar_chave_propria":true}""");
        await Send(Client(), HttpMethod.Post, $"/api-keys/preferencias/{userId}", """{"provedor":"gemini","usar_chave_propria":false}""");

        var (status, body) = await Send(Client(), HttpMethod.Get, $"/api-keys/preferencias/{userId}");
        status.ShouldBe(HttpStatusCode.OK);
        NormalizeTimestamps(body).ShouldBe(
            """{"usuario_id":1,"total":2,"preferencias":[""" +
            """{"id":2,"provedor":"gemini","usar_chave_propria":false,"tipo":"sistema (cobrado)","atualizado_em":"<ts>"},""" +
            """{"id":1,"provedor":"weather","usar_chave_propria":true,"tipo":"própria (grátis)","atualizado_em":"<ts>"}]}""");

        var (deleteStatus, deleteBody) = await Send(Client(), HttpMethod.Delete, $"/api-keys/preferencias/{userId}/gemini");
        deleteStatus.ShouldBe(HttpStatusCode.OK);
        deleteBody.ShouldBe("""{"mensagem":"Preferência removida com sucesso","usuario_id":1,"provedor":"gemini"}""");

        var (againStatus, againBody) = await Send(Client(), HttpMethod.Delete, $"/api-keys/preferencias/{userId}/gemini");
        againStatus.ShouldBe(HttpStatusCode.NotFound);
        againBody.ShouldBe(Error("Preferência não encontrada", "Nenhuma preferência para gemini"));
    }

    // ============================== uso ==============================

    [Fact]
    [Trait("AC", "008-30")]
    public async Task Usage_WithMonthAndProvider_FiltersAndSummarizes()
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);
        await ApiKeysTestSupport.Execute(postgres,
            """
            INSERT INTO rastreamentousoapi (usuario_id, provedor, tipo_chave, quantidade_chamadas, mes_ano) VALUES
              (@userId, 'gemini', 'propria', 3, '2026-09'), (@userId, 'gemini', 'sistema', 4, '2026-09'),
              (@userId, 'weather', 'sistema', 5, '2026-09'), (@userId, 'gemini', 'sistema', 9, '2026-08')
            """, new { userId });

        var (status, body) = await Send(Client(), HttpMethod.Get, $"/api-keys/uso/{userId}?mes_ano=2026-09&provedor=gemini");

        status.ShouldBe(HttpStatusCode.OK);
        NormalizeTimestamps(body).ShouldBe(
            """{"usuario_id":1,"mes_ano":"2026-09","resumo":{"total_chamadas":7,"chamadas_chave_propria":3,"chamadas_chave_sistema":4},"detalhes":[""" +
            """{"provedor":"gemini","tipo_chave":"propria","total_chamadas":3,"mes_ano":"2026-09","ultima_atualizacao":"<ts>"},""" +
            """{"provedor":"gemini","tipo_chave":"sistema","total_chamadas":4,"mes_ano":"2026-09","ultima_atualizacao":"<ts>"}]}""");
    }

    [Fact]
    [Trait("AC", "008-30")]
    public async Task Usage_WithoutData_ReturnsZerosAndEmptyDetails()
    {
        var (status, body) = await Send(Client(), HttpMethod.Get, "/api-keys/uso/5?mes_ano=texto-livre");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"usuario_id":5,"mes_ano":"texto-livre","resumo":{"total_chamadas":0,"chamadas_chave_propria":0,"chamadas_chave_sistema":0},"detalhes":[]}""");
    }

    [Fact]
    [Trait("AC", "008-30")]
    public async Task Usage_DefaultMonth_FollowsSaoPauloAtTheMonthTurn()
    {
        // 02:30 UTC de 01/11 ainda é 23:30 de 31/10 em São Paulo.
        var fake = new FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 2, 30, 0, TimeSpan.Zero));
        using var factory = postgres.Api.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("API_KEYS_ROUTES_ENABLED", "true");
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(fake));
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-KEY", AuthFixtures.JwtSecret);

        var (_, october) = await Send(client, HttpMethod.Get, "/api-keys/uso/1");
        fake.Advance(TimeSpan.FromHours(1)); // 03:30 UTC = 00:30 de 01/11 em São Paulo
        var (_, november) = await Send(client, HttpMethod.Get, "/api-keys/uso/1");

        october.ShouldStartWith("""{"usuario_id":1,"mes_ano":"2026-10",""");
        november.ShouldStartWith("""{"usuario_id":1,"mes_ano":"2026-11",""");
    }

    // ============================== logs ==============================

    private async Task SeedLogsAsync(int userId)
    {
        await ApiKeysTestSupport.Execute(postgres,
            """
            INSERT INTO logacessochaveapi (usuario_id, provedor, tipo_chave, operacao, sucesso, mensagem_erro, criado_em) VALUES
              (@userId, 'gemini', 'propria', 'obter_chave', true,  NULL,           '2026-10-01 10:00:00+00'),
              (@userId, 'weather', 'sistema', 'obter_chave', false, 'LimitReached', '2026-10-02 10:00:00+00'),
              (@userId, 'gemini', 'sistema', 'obter_chave', true,  NULL,           '2026-10-03 10:00:00.5+00')
            """, new { userId });
    }

    [Fact]
    [Trait("AC", "008-31")]
    public async Task Logs_AreOrderedByCreatedAtDesc_WithPythonTimestamps()
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);
        await SeedLogsAsync(userId);

        var (status, body) = await Send(Client(), HttpMethod.Get, $"/api-keys/logs/{userId}");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            """{"usuario_id":1,"total":3,"logs":[""" +
            """{"id":3,"provedor":"gemini","tipo_chave":"sistema","operacao":"obter_chave","sucesso":true,"mensagem_erro":null,"criado_em":"2026-10-03T10:00:00.500000+00:00"},""" +
            """{"id":2,"provedor":"weather","tipo_chave":"sistema","operacao":"obter_chave","sucesso":false,"mensagem_erro":"LimitReached","criado_em":"2026-10-02T10:00:00+00:00"},""" +
            """{"id":1,"provedor":"gemini","tipo_chave":"propria","operacao":"obter_chave","sucesso":true,"mensagem_erro":null,"criado_em":"2026-10-01T10:00:00+00:00"}]}""");
    }

    [Theory]
    [Trait("AC", "008-31")]
    [InlineData("?provedor=weather", 1)]
    [InlineData("?provedor=", 3)]
    [InlineData("?sucesso=true", 2)]
    [InlineData("?sucesso=TRUE", 2)]
    [InlineData("?sucesso=false", 1)]
    [InlineData("?sucesso=", 1)]
    [InlineData("?sucesso=1", 1)]
    [InlineData("?limit=2", 2)]
    [InlineData("?limit=0", 0)]
    [InlineData("?limit=9999", 3)]
    [InlineData("?limit=%202%20", 2)]
    public async Task Logs_Filters(string query, int expectedTotal)
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);
        await SeedLogsAsync(userId);

        var (status, body) = await Send(Client(), HttpMethod.Get, $"/api-keys/logs/{userId}{query}");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain($"\"total\":{expectedTotal},");
    }

    [Theory]
    [Trait("AC", "008-31")]
    [InlineData("?limit=abc")]
    [InlineData("?limit=-1")]
    [InlineData("?limit=")]
    [InlineData("?limit=1.5")]
    public async Task Logs_InvalidLimit_Returns500WithFixedText(string query)
    {
        var (status, body) = await Send(Client(), HttpMethod.Get, $"/api-keys/logs/1{query}");

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(Error("Erro ao consultar logs", "Erro ao consultar logs. Tente novamente."));
    }

    [Fact]
    [Trait("AC", "008-31")]
    public async Task Logs_OtherUsersRowsAreNeverReturned()
    {
        var a = await ApiKeysTestSupport.SeedUserAsync(postgres, "5511999990001");
        var b = await ApiKeysTestSupport.SeedUserAsync(postgres, "5511999990002");
        await SeedLogsAsync(a);

        var (_, body) = await Send(Client(), HttpMethod.Get, $"/api-keys/logs/{b}");

        body.ShouldBe("""{"usuario_id":2,"total":0,"logs":[]}""");
    }

    // ============================== FernetKeys via DI real ==============================

    [Fact]
    [Trait("AC", "008-19")]
    public async Task Provider_ReadsPythonFernetTokens_AndLegacyPlainText_FromTheRealDatabase()
    {
        var a = await ApiKeysTestSupport.SeedUserAsync(postgres, "5511999990001");
        var b = await ApiKeysTestSupport.SeedUserAsync(postgres, "5511999990002");
        var pythonCase = AuthFixtures.FernetCases[2]; // "ação-é-ü-🔑" cifrado pelo Python
        await ApiKeysTestSupport.SeedKeyAsync(postgres, a, "gemini", pythonCase.Token);
        await ApiKeysTestSupport.SeedKeyAsync(postgres, b, "gemini", "texto-puro-legado");
        await ApiKeysTestSupport.Execute(postgres,
            "INSERT INTO preferenciaschaveapi (usuario_id, provedor, usar_chave_propria) VALUES (@a, 'gemini', true), (@b, 'gemini', true)", new { a, b });

        using var scope = ApiKeysTestSupport.Scope(postgres);
        var provider = scope.ServiceProvider.GetRequiredService<IApiKeyProvider>();

        var fromPython = (await provider.ResolveAsync(a, "gemini", default)).ShouldBeOfType<ApiKeyResolution.Resolved>();
        var legacy = (await provider.ResolveAsync(b, "gemini", default)).ShouldBeOfType<ApiKeyResolution.Resolved>();

        fromPython.Key.ShouldBe(pythonCase.Plaintext);
        fromPython.KeyType.ShouldBe("propria");
        legacy.Key.ShouldBe("texto-puro-legado");
    }
}
