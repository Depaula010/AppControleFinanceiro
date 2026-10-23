using System.Net;
using System.Text.Json;
using MeuSecretario.Api.Settings;
using MeuSecretario.Application.Settings;
using MeuSecretario.Domain.Settings;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MeuSecretario.IntegrationTests.Settings;

[Trait("Category", "Docker")]
public class SettingsAggregateTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private const string Defaults = """
        {"profile":{"id":1,"nome":"Ana Souza","email":null,"numero_whatsapp":"5511900000001","cidade":"Sao Paulo","estado":"SP","fuso_horario":"America/Sao_Paulo","meses_reserva_emergencia":6},"apiKeys":[{"provedor":"gemini","usar_chave_propria":false,"has_key":false},{"provedor":"weather","usar_chave_propria":false,"has_key":false},{"provedor":"openroute","usar_chave_propria":false,"has_key":false}],"notifications":{"morningBriefing":{"enabled":true,"time":"07:00"},"eveningCheckIn":{"enabled":true,"time":"19:00"},"financialAlerts":{"enabled":true,"daysBeforeDue":3}},"addresses":[]}
        """;

    private readonly SettingsEnv _env = new(postgres);
    private int _user;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _user = await _env.InsertUserAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string DataOf(string body) => JsonDocument.Parse(body).RootElement.GetProperty("data").GetRawText();

    private async Task AssertParityAsync()
    {
        var settings = JsonDocument.Parse(DataOf((await _env.Get(_user, "/api/user/settings")).Body)).RootElement;

        settings.GetProperty("profile").GetRawText().ShouldBe(DataOf((await _env.Get(_user, "/api/user/profile")).Body));
        settings.GetProperty("apiKeys").GetRawText().ShouldBe(DataOf((await _env.Get(_user, "/api/user/api-keys")).Body));
        settings.GetProperty("notifications").GetRawText().ShouldBe(DataOf((await _env.Get(_user, "/api/user/notifications")).Body));
        settings.GetProperty("addresses").GetRawText().ShouldBe(DataOf((await _env.Get(_user, "/api/user/addresses")).Body));
    }

    [Fact]
    [Trait("AC", "007-61")]
    public async Task WithoutRows_ReturnsTheDefaults()
    {
        var (status, body) = await _env.Get(_user, "/api/user/settings");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(SettingsEnv.Data(Defaults));        await AssertParityAsync();
    }

    [Fact]
    [Trait("AC", "007-60")]
    [Trait("AC", "007-70")]
    public async Task Populated_EachBlockEqualsTheIndividualRoute()
    {
        await _env.Put(_user, "/api/user/api-keys", """{"type":"weather","useOwnKey":true,"key":"k"}""");
        await _env.Put(_user, "/api/user/notifications", """{"morningBriefing":{"enabled":false,"time":"06:15"},"financialAlerts":{"daysBeforeDue":9}}""");
        await _env.Post(_user, "/api/user/addresses", """{"label":"casa","address":"Rua A","coordinates":{"lat":-23.5,"lng":-46.6}}""");
        await _env.Post(_user, "/api/user/addresses", """{"label":"trabalho","address":"Rua B"}""");
        await _env.Put(_user, "/api/user/profile", """{"cidade":"Campinas","meses_reserva_emergencia":null}""");

        var (_, body) = await _env.Get(_user, "/api/user/settings");

        body.ShouldContain("\"has_key\":true");
        body.ShouldContain("\"daysBeforeDue\":9");
        body.ShouldContain("\"coordinates\":{\"lat\":-23.5,\"lng\":-46.6}");
        body.ShouldContain("\"meses_reserva_emergencia\":null");
        body.ShouldNotContain("\"k\"");
        await AssertParityAsync();
    }
}

/// <summary>401 nas 11 rotas, isolamento entre usuários e 500 sem vazamento (AC-007-01, 80, 81).</summary>
[Trait("Category", "Docker")]
public class SettingsAuthAndIsolationTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private readonly SettingsEnv _env = new(postgres);
    private int _a;
    private int _b;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _a = await _env.InsertUserAsync("5511900000001", "Ana", "ana@x.com");
        _b = await _env.InsertUserAsync("5511900000002", "Beto", "beto@x.com");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public static TheoryData<string, string> AllRoutes => new()
    {
        { "GET", "/api/user/profile" },
        { "PUT", "/api/user/profile" },
        { "GET", "/api/user/api-keys" },
        { "PUT", "/api/user/api-keys" },
        { "POST", "/api/user/api-keys/validate" },
        { "GET", "/api/user/notifications" },
        { "PUT", "/api/user/notifications" },
        { "GET", "/api/user/addresses" },
        { "POST", "/api/user/addresses" },
        { "DELETE", "/api/user/addresses/casa" },
        { "GET", "/api/user/settings" },
    };

    [Theory]
    [MemberData(nameof(AllRoutes))]
    [Trait("AC", "007-01")]
    public async Task WithoutTokenOrWithAnInvalidOne_Returns401_BeforeValidatingTheBody(string method, string path)
    {
        var (noToken, noTokenBody) = await _env.SendAsync(new HttpMethod(method), path, method is "GET" or "DELETE" ? null : "{}");
        var (invalid, invalidBody) = await _env.SendAsync(new HttpMethod(method), path, method is "GET" or "DELETE" ? null : "{}", authorization: "Bearer lixo");

        noToken.ShouldBe(HttpStatusCode.Unauthorized);
        invalid.ShouldBe(HttpStatusCode.Unauthorized);
        noTokenBody.ShouldStartWith("""{"status":"error","message":""");
        invalidBody.ShouldStartWith("""{"status":"error","message":""");
    }

    [Fact]
    [Trait("AC", "007-81")]
    public async Task WritesOfOneUser_NeverTouchTheOther()
    {
        await _env.Put(_a, "/api/user/api-keys", """{"type":"gemini","useOwnKey":true,"key":"chave-a"}""");
        await _env.Put(_a, "/api/user/notifications", """{"eveningCheckIn":{"time":"22:00"}}""");
        await _env.Post(_a, "/api/user/addresses", """{"label":"casa","address":"Casa da Ana","coordinates":{"lat":1,"lng":2}}""");
        await _env.Put(_a, "/api/user/profile", """{"cidade":"CidadeA"}""");

        // O usuário B grava as mesmas chaves; o corpo não consegue apontar para outro dono.
        await _env.Put(_b, "/api/user/api-keys", $$"""{"type":"gemini","useOwnKey":false,"usuario_id":{{_a}}}""");
        await _env.Put(_b, "/api/user/notifications", "{}");
        await _env.Post(_b, "/api/user/addresses", $$"""{"label":"casa","address":"Casa do Beto","usuario_id":{{_a}}}""");
        await _env.Put(_b, "/api/user/profile", $$"""{"cidade":"CidadeB","id":{{_a}}}""");
        (await _env.Delete(_b, "/api/user/addresses/trabalho")).Status.ShouldBe(HttpStatusCode.NotFound);

        (await _env.ScalarAsync<bool>("SELECT ativo FROM chavesapiusuario WHERE usuario_id = @_a", new { _a })).ShouldBeTrue();
        (await _env.ScalarAsync<bool>("SELECT usar_chave_propria FROM preferenciaschaveapi WHERE usuario_id = @_a", new { _a })).ShouldBeTrue();
        (await _env.ScalarAsync<string>("SELECT checkin_noturno_hora::text FROM notificationconfigs WHERE usuario_id = @_a", new { _a })).ShouldBe("22:00:00");
        (await _env.ScalarAsync<string>("SELECT endereco_completo FROM enderecosfavoritos WHERE usuario_id = @_a", new { _a })).ShouldBe("Casa da Ana");
        (await _env.ScalarAsync<string>("SELECT cidade FROM usuarios WHERE id = @_a", new { _a })).ShouldBe("CidadeA");
        (await _env.ScalarAsync<string>("SELECT cidade FROM usuarios WHERE id = @_b", new { _b })).ShouldBe("CidadeB");

        (await _env.Delete(_b, "/api/user/addresses/casa")).Status.ShouldBe(HttpStatusCode.OK);
        (await _env.ScalarAsync<string>("SELECT endereco_completo FROM enderecosfavoritos WHERE usuario_id = @_a", new { _a })).ShouldBe("Casa da Ana");
    }
}

[Trait("Category", "Docker")]
public class SettingsFailureTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private const string Secret = "SENTINELA-interna 5511900000001 ana@x.com";

    public async Task InitializeAsync() => await postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    public static TheoryData<string, string, string?, string> Operations => new()
    {
        { "GET", "/api/user/profile", null, "Erro ao buscar perfil" },
        { "PUT", "/api/user/profile", """{"nome":"Ana"}""", "Erro ao atualizar perfil" },
        { "GET", "/api/user/api-keys", null, "Erro ao buscar chaves API" },
        { "PUT", "/api/user/api-keys", """{"type":"gemini","useOwnKey":false}""", "Erro ao salvar chave API" },
        { "GET", "/api/user/notifications", null, "Erro ao buscar notificações" },
        { "PUT", "/api/user/notifications", "{}", "Erro ao salvar notificações" },
        { "GET", "/api/user/addresses", null, "Erro ao buscar endereços" },
        { "POST", "/api/user/addresses", """{"label":"casa","address":"x"}""", "Erro ao salvar endereço" },
        { "DELETE", "/api/user/addresses/casa", null, "Erro ao remover endereço" },
        { "GET", "/api/user/settings", null, "Erro ao buscar configurações" },
    };

    [Theory]
    [MemberData(nameof(Operations))]
    [Trait("AC", "007-80")]
    public async Task RepositoryFailure_Returns500WithTheFixedMessage_AndLogsOnlyTheErrorType(string method, string path, string? json, string message)
    {
        var log = new CapturingLogger<SettingsModule>();
        await using var factory = postgres.Api.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IProfileRepository>(new Throwing());
            s.AddSingleton<IApiKeySettingsRepository>(new Throwing());
            s.AddSingleton<IUserNotificationSettingsRepository>(new Throwing());
            s.AddSingleton<IFavoriteAddressRepository>(new Throwing());
            s.AddSingleton<ILogger<SettingsModule>>(log);
        }));

        var (status, body) = await new SettingsEnv(postgres, factory).SendAsync(new HttpMethod(method), path, json, userId: 42);

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(SettingsEnv.Error(message));
        log.Messages.Count.ShouldBe(1);
        log.Messages[0].ShouldContain("PostgresException (SqlState 23505)");
        log.Messages[0].ShouldContain("usuário 42");
        log.Messages[0].ShouldNotContain("SENTINELA");
        log.Messages[0].ShouldNotContain("5511900000001");
        log.Messages[0].ShouldNotContain("ana@x.com");
    }

    private sealed class Throwing : IProfileRepository, IApiKeySettingsRepository, IUserNotificationSettingsRepository, IFavoriteAddressRepository
    {
        private static PostgresException Failure() => new(Secret, "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation);

        Task<ProfileRow?> IProfileRepository.GetAsync(int userId, CancellationToken cancellationToken) => throw Failure();

        Task<ProfileUpdateOutcome> IProfileRepository.UpdateAsync(int userId, ProfileChanges changes, CancellationToken cancellationToken) => throw Failure();

        Task<IReadOnlyList<ApiKeyPreferenceRow>> IApiKeySettingsRepository.ListAsync(int userId, CancellationToken cancellationToken) => throw Failure();

        Task IApiKeySettingsRepository.SaveOwnKeyAsync(int userId, string provider, bool useOwnKey, string? encryptedKey, CancellationToken cancellationToken) => throw Failure();

        Task<NotificationRow?> IUserNotificationSettingsRepository.GetAsync(int userId, CancellationToken cancellationToken) => throw Failure();

        Task IUserNotificationSettingsRepository.UpsertAsync(int userId, NotificationInput input, CancellationToken cancellationToken) => throw Failure();

        Task<IReadOnlyList<AddressRow>> IFavoriteAddressRepository.ListAsync(int userId, CancellationToken cancellationToken) => throw Failure();

        Task IFavoriteAddressRepository.UpsertAsync(int userId, string label, string address, AddressCoordinates? coordinates, CancellationToken cancellationToken) => throw Failure();

        Task<bool> IFavoriteAddressRepository.DeleteAsync(int userId, string label, CancellationToken cancellationToken) => throw Failure();
    }
}
