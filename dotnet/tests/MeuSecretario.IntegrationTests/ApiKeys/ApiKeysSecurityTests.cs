using System.Net;
using MeuSecretario.Api.ApiKeys;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.ApiKeys;
using MeuSecretario.Domain.ApiKeys;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.TestFixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MeuSecretario.IntegrationTests.ApiKeys;

/// <summary>IDOR (desvios 006 e 045) e 500 sem vazamento (desvio 008, AC-008-17 e 50).</summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class ApiKeysSecurityTests(PostgresFixture postgres) : IAsyncLifetime
{
    public async Task InitializeAsync() => await postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static Task<(HttpStatusCode Status, string Body)> Send(HttpClient client, HttpMethod method, string path, string? json = null) =>
        ApiKeysEndpointsTests.Send(client, method, path, json);

    // ============================== IDOR ==============================

    [Fact]
    [Trait("AC", "008-17")]
    public async Task Put_WithAnotherUsersId_Returns404_AndTheKeyIsUntouched()
    {
        var a = await ApiKeysTestSupport.SeedUserAsync(postgres, "5511999990001");
        var b = await ApiKeysTestSupport.SeedUserAsync(postgres, "5511999990002");
        var keyId = await ApiKeysTestSupport.SeedKeyAsync(postgres, a, "gemini", "chave-de-A");
        using var client = ApiKeysTestSupport.EnabledClient(postgres);

        var (status, body) = await Send(client, HttpMethod.Put, $"/api-keys/chave/{keyId}", $$"""{"chave_api":"invasor","ativo":false,"usuario_id":{{b}}}""");

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(ApiKeysEndpointsTests.Error("Chave não encontrada", "Nenhuma chave com ID 1"));
        (await ApiKeysTestSupport.Scalar<string>(postgres, "SELECT chave_api_criptografada FROM chavesapiusuario WHERE id = 1")).ShouldBe("chave-de-A");
        (await ApiKeysTestSupport.Scalar<bool>(postgres, "SELECT ativo FROM chavesapiusuario WHERE id = 1")).ShouldBeTrue();

        // A resposta para "chave de outro usuário" é igual à de "chave inexistente" (não revela existência).
        var (_, missingBody) = await Send(client, HttpMethod.Put, "/api-keys/chave/1000", $$"""{"ativo":false,"usuario_id":{{b}}}""");
        missingBody.ShouldBe(ApiKeysEndpointsTests.Error("Chave não encontrada", "Nenhuma chave com ID 1000"));
    }

    [Fact]
    [Trait("AC", "008-17")]
    public async Task Delete_WithAnotherUsersId_Returns404_AndTheKeyStaysActive()
    {
        var a = await ApiKeysTestSupport.SeedUserAsync(postgres, "5511999990001");
        var b = await ApiKeysTestSupport.SeedUserAsync(postgres, "5511999990002");
        var keyId = await ApiKeysTestSupport.SeedKeyAsync(postgres, a, "weather", "chave-de-A");
        using var client = ApiKeysTestSupport.EnabledClient(postgres);

        var (status, _) = await Send(client, HttpMethod.Delete, $"/api-keys/chave/{keyId}?usuario_id={b}");
        var (nonInteger, _) = await Send(client, HttpMethod.Delete, $"/api-keys/chave/{keyId}?usuario_id=abc");

        status.ShouldBe(HttpStatusCode.NotFound);
        nonInteger.ShouldBe(HttpStatusCode.NotFound);
        (await ApiKeysTestSupport.Scalar<bool>(postgres, "SELECT ativo FROM chavesapiusuario WHERE id = 1")).ShouldBeTrue();

        var (ownerStatus, _) = await Send(client, HttpMethod.Delete, $"/api-keys/chave/{keyId}?usuario_id={a}");
        ownerStatus.ShouldBe(HttpStatusCode.OK);
        (await ApiKeysTestSupport.Scalar<bool>(postgres, "SELECT ativo FROM chavesapiusuario WHERE id = 1")).ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "008-17")]
    public async Task Put_WithTheOwnersId_Succeeds()
    {
        var a = await ApiKeysTestSupport.SeedUserAsync(postgres);
        var keyId = await ApiKeysTestSupport.SeedKeyAsync(postgres, a, "gemini", "antiga");
        using var client = ApiKeysTestSupport.EnabledClient(postgres);

        var (status, _) = await Send(client, HttpMethod.Put, $"/api-keys/chave/{keyId}", $$"""{"ativo":false,"usuario_id":{{a}}}""");

        status.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [Trait("AC", "008-17")]
    [InlineData("""{"chave_api":"nova"}""")]
    [InlineData("""{"chave_api":"nova","usuario_id":null}""")]
    [InlineData("""{"chave_api":"nova","usuario_id":0}""")]
    [InlineData("""{"chave_api":"nova","usuario_id":"1"}""")]
    [InlineData("""{"chave_api":"nova","usuario_id":1.5}""")]
    public async Task Put_WithoutAValidOwner_Returns400_AndNothingChanges(string json)
    {
        var a = await ApiKeysTestSupport.SeedUserAsync(postgres);
        var keyId = await ApiKeysTestSupport.SeedKeyAsync(postgres, a, "gemini", "antiga");
        using var client = ApiKeysTestSupport.EnabledClient(postgres);

        var (status, body) = await Send(client, HttpMethod.Put, $"/api-keys/chave/{keyId}", json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(ApiKeysEndpointsTests.Error("Dados incompletos", "usuario_id é obrigatório"));
        (await ApiKeysTestSupport.Scalar<string>(postgres, "SELECT chave_api_criptografada FROM chavesapiusuario WHERE id = 1")).ShouldBe("antiga");
    }

    [Theory]
    [Trait("AC", "008-17")]
    [InlineData("")]
    [InlineData("?usuario_id=")]
    public async Task Delete_WithoutOwner_Returns400_AndTheKeyStaysActive(string query)
    {
        var a = await ApiKeysTestSupport.SeedUserAsync(postgres);
        var keyId = await ApiKeysTestSupport.SeedKeyAsync(postgres, a, "gemini", "k");
        using var client = ApiKeysTestSupport.EnabledClient(postgres);

        var (status, body) = await Send(client, HttpMethod.Delete, $"/api-keys/chave/{keyId}{query}");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(ApiKeysEndpointsTests.Error("Dados incompletos", "usuario_id é obrigatório"));
        (await ApiKeysTestSupport.Scalar<bool>(postgres, "SELECT ativo FROM chavesapiusuario WHERE id = 1")).ShouldBeTrue();
    }

    // ============================== 500 sem vazamento ==============================

    /// <summary>Captura as mensagens (e exceções) logadas pelo módulo; o Serilog do host não repassa providers extras.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<string> _lines = [];

        public IReadOnlyList<string> Lines
        {
            get
            {
                lock (_lines)
                {
                    return [.. _lines];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_lines)
            {
                _lines.Add(formatter(state, exception) + (exception is null ? string.Empty : " | " + exception));
            }
        }
    }

    /// <summary>Repositório que sempre falha com uma mensagem cheia de segredos.</summary>
    private sealed class ThrowingRepository : IApiKeyRepository, IPreferenceRepository, IUsageRepository, IAccessLogRepository
    {
        public const string Leak = "detalhe-sensivel segredo-vazado Npgsql 23505 chavesapiusuario_usuario_id_provedor_key";

        private static InvalidOperationException Boom() => new(Leak);

        Task<InsertKeyResult> IApiKeyRepository.InsertAsync(int userId, string provider, string encryptedKey, CancellationToken cancellationToken) => throw Boom();

        Task<IReadOnlyList<ApiKeyRow>> IApiKeyRepository.ListByUserAsync(int userId, CancellationToken cancellationToken) => throw Boom();

        Task<KeyRef?> IApiKeyRepository.UpdateAsync(int keyId, int ownerUserId, string? encryptedKey, bool? active, CancellationToken cancellationToken) => throw Boom();

        Task<KeyRef?> IApiKeyRepository.DeactivateAsync(int keyId, int ownerUserId, CancellationToken cancellationToken) => throw Boom();

        Task<string?> IApiKeyRepository.FindActiveEncryptedAsync(int userId, string provider, CancellationToken cancellationToken) => throw Boom();

        Task IApiKeyRepository.TouchLastUseAsync(int userId, string provider, CancellationToken cancellationToken) => throw Boom();

        Task<UpsertPreferenceResult> IPreferenceRepository.UpsertAsync(int userId, string provider, bool useOwnKey, CancellationToken cancellationToken) => throw Boom();

        Task<IReadOnlyList<PreferenceRow>> IPreferenceRepository.ListAsync(int userId, CancellationToken cancellationToken) => throw Boom();

        Task<bool> IPreferenceRepository.DeleteAsync(int userId, string provider, CancellationToken cancellationToken) => throw Boom();

        Task<bool?> IPreferenceRepository.FindAsync(int userId, string provider, CancellationToken cancellationToken) => throw Boom();

        Task<IReadOnlyList<UsageRow>> IUsageRepository.QueryMonthAsync(int userId, string month, string? provider, CancellationToken cancellationToken) => throw Boom();

        Task IUsageRepository.IncrementAsync(int userId, string provider, string keyType, string month, CancellationToken cancellationToken) => throw Boom();

        Task<long> IUsageRepository.SumSystemMonthAsync(int userId, string provider, string month, CancellationToken cancellationToken) => throw Boom();

        Task<IReadOnlyList<AccessLogRow>> IAccessLogRepository.ListAsync(int userId, string? provider, bool? success, int limit, CancellationToken cancellationToken) => throw Boom();

        Task IAccessLogRepository.InsertAsync(int userId, string provider, string keyType, string operation, bool success, string? errorMessage, CancellationToken cancellationToken) => throw Boom();
    }

    [Theory]
    [Trait("AC", "008-50")]
    [InlineData("POST", "/api-keys/usuario/1", """{"provedor":"gemini","chave_api":"chave-super-secreta"}""", "Erro ao cadastrar chave")]
    [InlineData("GET", "/api-keys/usuario/1", null, "Erro ao listar chaves")]
    [InlineData("PUT", "/api-keys/chave/1", """{"chave_api":"chave-super-secreta","usuario_id":1}""", "Erro ao atualizar chave")]
    [InlineData("DELETE", "/api-keys/chave/1?usuario_id=1", null, "Erro ao deletar chave")]
    [InlineData("POST", "/api-keys/preferencias/1", """{"provedor":"gemini","usar_chave_propria":true}""", "Erro ao configurar preferência")]
    [InlineData("GET", "/api-keys/preferencias/1", null, "Erro ao listar preferências")]
    [InlineData("DELETE", "/api-keys/preferencias/1/gemini", null, "Erro ao deletar preferência")]
    [InlineData("GET", "/api-keys/uso/1", null, "Erro ao consultar uso")]
    [InlineData("GET", "/api-keys/logs/1", null, "Erro ao consultar logs")]
    public async Task InternalFailures_ReturnOnlyTheFixedText_AndLogsHoldNoSecret(string method, string path, string? json, string title)
    {
        var logs = new CapturingLogger<ApiKeysModule>();
        using var factory = postgres.Api.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("API_KEYS_ROUTES_ENABLED", "true");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ILogger<ApiKeysModule>>(logs);
                var throwing = new ThrowingRepository();
                services.AddSingleton<IApiKeyRepository>(throwing);
                services.AddSingleton<IPreferenceRepository>(throwing);
                services.AddSingleton<IUsageRepository>(throwing);
                services.AddSingleton<IAccessLogRepository>(throwing);
            });
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-KEY", AuthFixtures.JwtSecret);

        var (status, body) = await Send(client, new HttpMethod(method), path, json);

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(ApiKeysEndpointsTests.Error(title, title + ". Tente novamente."));

        var logged = string.Join('\n', logs.Lines);
        logged.ShouldContain("InvalidOperationException");
        foreach (var secret in new[] { "segredo-vazado", "detalhe-sensivel", "chave-super-secreta", AuthFixtures.JwtSecret, "chavesapiusuario_usuario_id_provedor_key" })
        {
            logged.ShouldNotContain(secret);
            body.ShouldNotContain(secret);
        }
    }

    // ============================== porta com banco real ==============================

    private async Task<(IApiKeyProvider Provider, string Month)> ProviderAsync(int userId, string provider, bool useOwnKey)
    {
        await ApiKeysTestSupport.Execute(postgres,
            "INSERT INTO preferenciaschaveapi (usuario_id, provedor, usar_chave_propria) VALUES (@userId, @provider, @useOwnKey)",
            new { userId, provider, useOwnKey });
        var scope = ApiKeysTestSupport.Scope(postgres);
        var clock = scope.ServiceProvider.GetRequiredService<MeuSecretario.Application.Abstractions.IClock>();
        return (scope.ServiceProvider.GetRequiredService<IApiKeyProvider>(), UsageMonth.Of(clock.Now));
    }

    [Fact]
    [Trait("AC", "008-63")]
    public async Task Provider_OwnKey_UpdatesLastUse_CountsUsage_AndWritesTheAccessLog()
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);
        await ApiKeysTestSupport.SeedKeyAsync(postgres, userId, "gemini", "texto-puro-legado");
        var (provider, month) = await ProviderAsync(userId, "gemini", useOwnKey: true);

        var resolved = (await provider.ResolveAsync(userId, "gemini", default)).ShouldBeOfType<ApiKeyResolution.Resolved>();

        resolved.Key.ShouldBe("texto-puro-legado");
        (await ApiKeysTestSupport.Scalar<bool>(postgres, "SELECT ultimo_uso_em IS NOT NULL FROM chavesapiusuario WHERE usuario_id = @userId", new { userId })).ShouldBeTrue();
        (await ApiKeysTestSupport.Scalar<int>(postgres,
            "SELECT quantidade_chamadas FROM rastreamentousoapi WHERE usuario_id = @userId AND tipo_chave = 'propria' AND mes_ano = @month", new { userId, month })).ShouldBe(1);
        (await ApiKeysTestSupport.Scalar<int>(postgres,
            "SELECT count(*) FROM logacessochaveapi WHERE usuario_id = @userId AND tipo_chave = 'propria' AND operacao = 'obter_chave' AND sucesso AND mensagem_erro IS NULL", new { userId })).ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "008-65")]
    public async Task Provider_SystemKey_CreatesThenIncrementsTheUsageRow()
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);
        var (provider, month) = await ProviderAsync(userId, "gemini", useOwnKey: false);

        for (var i = 0; i < 2; i++)
        {
            var resolved = (await provider.ResolveAsync(userId, "gemini", default)).ShouldBeOfType<ApiKeyResolution.Resolved>();
            resolved.Key.ShouldBe(ApiKeysTestSupport.SystemGeminiKey);
            resolved.KeyType.ShouldBe("sistema");
        }

        (await ApiKeysTestSupport.Scalar<int>(postgres,
            "SELECT quantidade_chamadas FROM rastreamentousoapi WHERE usuario_id = @userId AND tipo_chave = 'sistema' AND mes_ano = @month", new { userId, month })).ShouldBe(2);
    }

    [Fact]
    [Trait("AC", "008-66")]
    public async Task Provider_PlanLimit_BlocksAtTheLimit_AndOwnKeyUsageDoesNotCount()
    {
        await ApiKeysTestSupport.SeedPlansAsync(postgres);
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);
        var (provider, month) = await ProviderAsync(userId, "gemini", useOwnKey: false);
        await ApiKeysTestSupport.Execute(postgres,
            "INSERT INTO assinaturasusuario (usuario_id, plano_id, status) SELECT @userId, id, 'ativo' FROM planos WHERE nome_plano = 'Bronze'", new { userId });
        await ApiKeysTestSupport.Execute(postgres,
            """
            INSERT INTO rastreamentousoapi (usuario_id, provedor, tipo_chave, quantidade_chamadas, mes_ano) VALUES
              (@userId, 'gemini', 'propria', 500, @month), (@userId, 'gemini', 'sistema', 99, @month)
            """, new { userId, month });

        (await provider.ResolveAsync(userId, "gemini", default)).ShouldBeOfType<ApiKeyResolution.Resolved>(); // 99 -> passa, vira 100

        var failed = (await provider.ResolveAsync(userId, "gemini", default)).ShouldBeOfType<ApiKeyResolution.Failed>();
        failed.Reason.ShouldBe(ApiKeyFailure.LimitReached);
        failed.Message.ShouldBe(MeuSecretario.Application.Abstractions.ApiKeyMessages.LimitReached("gemini", "Bronze", 100, 100));
        (await ApiKeysTestSupport.Scalar<int>(postgres,
            "SELECT count(*) FROM logacessochaveapi WHERE usuario_id = @userId AND NOT sucesso AND mensagem_erro = 'LimitReached'", new { userId })).ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "008-64")]
    public async Task Provider_SystemKeyUnavailable_WhenTheSettingIsEmpty()
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);
        var (provider, _) = await ProviderAsync(userId, "openroute", useOwnKey: false); // OPENROUTE_API_KEY não configurada nos testes

        var failed = (await provider.ResolveAsync(userId, "openroute", default)).ShouldBeOfType<ApiKeyResolution.Failed>();

        failed.Reason.ShouldBe(ApiKeyFailure.SystemKeyUnavailable);
        (await ApiKeysTestSupport.Scalar<int>(postgres, "SELECT count(*) FROM rastreamentousoapi")).ShouldBe(0);
    }

    [Fact]
    public async Task Provider_CorruptedFernetToken_Throws_AndNeverReturnsTheCipherText()
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);
        await ApiKeysTestSupport.SeedKeyAsync(postgres, userId, "gemini", AuthFixtures.FernetTamperedToken);
        var (provider, _) = await ProviderAsync(userId, "gemini", useOwnKey: true);

        await Should.ThrowAsync<System.Security.Cryptography.CryptographicException>(() => provider.ResolveAsync(userId, "gemini", default));
    }
}
