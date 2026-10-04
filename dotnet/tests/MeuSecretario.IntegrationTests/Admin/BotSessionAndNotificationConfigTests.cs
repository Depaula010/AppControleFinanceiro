using System.Net;
using System.Text;
using Dapper;
using MeuSecretario.Application.Admin;
using MeuSecretario.Infrastructure.Admin;
using MeuSecretario.IntegrationTests.Support;
using Npgsql;

namespace MeuSecretario.IntegrationTests.Admin;

/// <summary><c>clear-bot-session</c>, <c>get-notification-config</c> e <c>config-alertas-financeiros</c> com PostgreSQL real (T16 a T18).</summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public sealed class BotSessionAndNotificationConfigTests(PostgresFixture postgres) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        await Execute("DELETE FROM baileys_auth");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task Execute(string sql, object? args = null)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(sql, args);
    }

    private async Task<T> Scalar<T>(string sql, object? args = null)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        return (await connection.ExecuteScalarAsync<T>(sql, args))!;
    }

    private Task<int> SeedUser(string whatsapp = "5511999990001") =>
        Scalar<int>("INSERT INTO usuarios (nome, numero_whatsapp) VALUES ('Usuário', @whatsapp) RETURNING id", new { whatsapp });

    private async Task<(HttpStatusCode Status, string Body, string? ContentType)> Send(
        HttpMethod method, string path, string? json = null, bool withKey = true)
    {
        using var client = postgres.Api.CreateClient();
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (withKey)
        {
            request.Headers.Add("X-API-KEY", AdminHttp.ApiKey);
        }

        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(), response.Content.Headers.ContentType?.MediaType);
    }

    // ---- 7. clear-bot-session ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("AC", "009-27")]
    public async Task ClearBotSession_DeletesOnlyTheBaileysSessionRows()
    {
        await Execute(
            """
            INSERT INTO baileys_auth (session_id, data_key, data_value) VALUES
              ('baileys_session', 'creds', 'a'), ('baileys_session', 'k1', 'b'), ('baileys_session', 'k2', 'c'),
              ('sessao-principal', 'creds', 'd'), ('sessao-principal', 'k1', 'e')
            """);

        var (status, body, _) = await Send(HttpMethod.Post, "/admin/clear-bot-session");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            """{"status":"sucesso","mensagem":"Sessão do bot ('baileys_auth') limpa com sucesso. 3 linhas deletadas.","linhas_deletadas":3}""");
        (await Scalar<int>("SELECT count(*) FROM baileys_auth WHERE session_id = 'baileys_session'")).ShouldBe(0);
        (await Scalar<int>("SELECT count(*) FROM baileys_auth WHERE session_id = 'sessao-principal'")).ShouldBe(2);
    }

    [Fact]
    public async Task ClearBotSession_WithNoRows_Reports0()
    {
        var (status, body, _) = await Send(HttpMethod.Post, "/admin/clear-bot-session");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            """{"status":"sucesso","mensagem":"Sessão do bot ('baileys_auth') limpa com sucesso. 0 linhas deletadas.","linhas_deletadas":0}""");
    }

    [Fact]
    [Trait("AC", "009-21")]
    public async Task ClearBotSession_WithoutKey_Is401_AndDeletesNothing()
    {
        await Execute("INSERT INTO baileys_auth (session_id, data_key, data_value) VALUES ('baileys_session', 'creds', 'a')");

        var (status, body, _) = await Send(HttpMethod.Post, "/admin/clear-bot-session", withKey: false);

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(AdminHttp.Error("Não autorizado"));
        (await Scalar<int>("SELECT count(*) FROM baileys_auth")).ShouldBe(1);
    }

    // ---- 8. get-notification-config ---------------------------------------------------------------------------------

    [Fact]
    [Trait("AC", "009-28")]
    public async Task GetConfig_CreatesTheDefaultRow_WhenMissing()
    {
        var userId = await SeedUser();

        var (status, body, _) = await Send(HttpMethod.Get, $"/admin/get-notification-config/{userId}");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            $$"""{"status":"sucesso","mensagem":"Configurações de notificação obtidas com sucesso","usuario_id":{{userId}},"configuracoes":{"resumo_matinal_ativo":true,"resumo_matinal_hora":"07:00","alertas_financeiros_ativos":true}}""");
        (await Scalar<int>("SELECT count(*) FROM notificationconfigs WHERE usuario_id = @userId", new { userId })).ShouldBe(1);

        // Segunda chamada: lê a mesma linha, não cria outra.
        (await Send(HttpMethod.Get, $"/admin/get-notification-config/{userId}")).Body.ShouldBe(body);
        (await Scalar<int>("SELECT count(*) FROM notificationconfigs WHERE usuario_id = @userId", new { userId })).ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "009-28")]
    public async Task GetConfig_FormatsTheTimeAsHoursAndMinutes_AndKeepsANullAlertFlag()
    {
        var userId = await SeedUser();
        await Execute(
            "INSERT INTO notificationconfigs (usuario_id, resumo_matinal_ativo, resumo_matinal_hora, alertas_financeiros_ativos) VALUES (@userId, false, '08:30:15', NULL)",
            new { userId });

        var (_, body, _) = await Send(HttpMethod.Get, $"/admin/get-notification-config/{userId}");

        body.ShouldBe(
            $$"""{"status":"sucesso","mensagem":"Configurações de notificação obtidas com sucesso","usuario_id":{{userId}},"configuracoes":{"resumo_matinal_ativo":false,"resumo_matinal_hora":"08:30","alertas_financeiros_ativos":null}}""");
    }

    [Fact]
    [Trait("Desvio", "080")]
    public async Task GetConfig_UnknownUser_Is404_AndCreatesNoRow()
    {
        var (status, body, _) = await Send(HttpMethod.Get, "/admin/get-notification-config/987654");

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(AdminHttp.Error("Usuário não encontrado"));
        (await Scalar<int>("SELECT count(*) FROM notificationconfigs")).ShouldBe(0);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("2147483648")]
    [Trait("AC", "009-28")]
    public async Task GetConfig_NonNumericOrOutOfRangeId_Is404Html_BeforeTheAuthentication(string id)
    {
        foreach (var withKey in new[] { true, false })
        {
            var (status, body, contentType) = await Send(HttpMethod.Get, $"/admin/get-notification-config/{id}", withKey: withKey);

            status.ShouldBe(HttpStatusCode.NotFound);
            contentType.ShouldBe("text/html");
            body.ShouldContain("<h1>Not Found</h1>");
        }
    }

    [Fact]
    public async Task GetConfig_WithoutKey_Is401_AndCreatesNoRow()
    {
        var userId = await SeedUser();

        var (status, body, _) = await Send(HttpMethod.Get, $"/admin/get-notification-config/{userId}", withKey: false);

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(AdminHttp.Error("Não autorizado"));
        (await Scalar<int>("SELECT count(*) FROM notificationconfigs")).ShouldBe(0);
    }

    [Fact]
    public async Task GetConfig_OtherMethods_Are405ForNumericIds_And404ForTheRest()
    {
        (await Send(HttpMethod.Post, "/admin/get-notification-config/1", withKey: false)).Status.ShouldBe(HttpStatusCode.MethodNotAllowed);
        (await Send(HttpMethod.Post, "/admin/get-notification-config/abc", withKey: false)).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- 9. config-alertas-financeiros ------------------------------------------------------------------------------

    [Fact]
    [Trait("AC", "009-29")]
    public async Task SetAlerts_False_IsValid_AndPersists_CreatingTheRowIfNeeded()
    {
        var userId = await SeedUser();

        var (status, body, _) = await Send(HttpMethod.Post, "/admin/config-alertas-financeiros", $$"""{"usuario_id":{{userId}},"ativo":false}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            """{"status":"sucesso","mensagem":"Alertas financeiros desativados com sucesso","configuracao":{"alertas_financeiros_ativos":false}}""");
        (await Scalar<bool>("SELECT alertas_financeiros_ativos FROM notificationconfigs WHERE usuario_id = @userId", new { userId })).ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "009-29")]
    public async Task SetAlerts_True_Enables_AndTouchesUpdatedAt()
    {
        var userId = await SeedUser();
        await Execute(
            "INSERT INTO notificationconfigs (usuario_id, alertas_financeiros_ativos, updated_at) VALUES (@userId, NULL, '2020-01-01')",
            new { userId });

        var (status, body, _) = await Send(HttpMethod.Post, "/admin/config-alertas-financeiros", $$"""{"usuario_id":{{userId}},"ativo":true}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            """{"status":"sucesso","mensagem":"Alertas financeiros ativados com sucesso","configuracao":{"alertas_financeiros_ativos":true}}""");
        (await Scalar<bool>("SELECT alertas_financeiros_ativos FROM notificationconfigs WHERE usuario_id = @userId", new { userId })).ShouldBeTrue();
        (await Scalar<bool>("SELECT updated_at > '2025-01-01' FROM notificationconfigs WHERE usuario_id = @userId", new { userId })).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("""{"ativo":true}""")]
    [InlineData("""{"usuario_id":0,"ativo":true}""")]
    [InlineData("""{"usuario_id":"","ativo":true}""")]
    [InlineData("""{"usuario_id":false,"ativo":true}""")]
    [InlineData("""{"usuario_id":null,"ativo":true}""")]
    [InlineData("""{"usuario_id":"1","ativo":true}""")]
    [InlineData("""{"usuario_id":1.5,"ativo":true}""")]
    [InlineData("[1]")]
    [InlineData("quebrado")]
    [Trait("AC", "009-29")]
    public async Task SetAlerts_MissingOrNonIntegerUserId_Is400(string? json)
    {
        var (status, body, _) = await Send(HttpMethod.Post, "/admin/config-alertas-financeiros", json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(AdminHttp.Error("Campos obrigatórios faltando: usuario_id"));
    }

    [Theory]
    [InlineData("""{"usuario_id":1}""")]
    [InlineData("""{"usuario_id":1,"ativo":null}""")]
    [InlineData("""{"usuario_id":1,"ativo":"false"}""")]
    [InlineData("""{"usuario_id":1,"ativo":0}""")]
    [Trait("AC", "009-29")]
    [Trait("Desvio", "079")]
    public async Task SetAlerts_MissingOrNonBooleanActive_Is400_AndTouchesNothing(string json)
    {
        var (status, body, _) = await Send(HttpMethod.Post, "/admin/config-alertas-financeiros", json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(AdminHttp.Error("Campo 'ativo' é obrigatório (true ou false)"));
        (await Scalar<int>("SELECT count(*) FROM notificationconfigs")).ShouldBe(0);
    }

    [Theory]
    [InlineData("""{"usuario_id":987654,"ativo":true}""")]
    [InlineData("""{"usuario_id":-5,"ativo":true}""")]
    [InlineData("""{"usuario_id":99999999999,"ativo":true}""")]
    [Trait("Desvio", "080")]
    public async Task SetAlerts_UnknownUser_Is404_AndCreatesNoRow(string json)
    {
        var (status, body, _) = await Send(HttpMethod.Post, "/admin/config-alertas-financeiros", json);

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(AdminHttp.Error("Usuário não encontrado"));
        (await Scalar<int>("SELECT count(*) FROM notificationconfigs")).ShouldBe(0);
    }

    [Fact]
    public async Task SetAlerts_WithoutKey_Is401_AndChangesNothing()
    {
        var userId = await SeedUser();

        var (status, _, _) = await Send(HttpMethod.Post, "/admin/config-alertas-financeiros", $$"""{"usuario_id":{{userId}},"ativo":false}""", withKey: false);

        status.ShouldBe(HttpStatusCode.Unauthorized);
        (await Scalar<int>("SELECT count(*) FROM notificationconfigs")).ShouldBe(0);
    }

    // ---- Repositório (T17) ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Repository_GetOrCreate_IsIdempotent_AndRacesCreateASingleRow()
    {
        var userId = await SeedUser();
        var repository = new AdminNotificationConfigRepository(postgres.Connections);

        var rows = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => repository.GetOrCreateAsync(userId, CancellationToken.None)));

        rows.ShouldAllBe(r => r != null);
        rows[0].ShouldBe(new NotificationConfigRow(true, "07:00", true));
        (await Scalar<int>("SELECT count(*) FROM notificationconfigs WHERE usuario_id = @userId", new { userId })).ShouldBe(1);
    }

    [Fact]
    public async Task Repository_UnknownUser_ViolatesTheForeignKey_AndReturnsNull()
    {
        var repository = new AdminNotificationConfigRepository(postgres.Connections);

        (await repository.GetOrCreateAsync(424242, CancellationToken.None)).ShouldBeNull();
        (await repository.SetFinancialAlertsAsync(424242, true, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Repository_OtherDatabaseErrors_AreNotSwallowed()
    {
        var userId = await SeedUser();
        await using var broken = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = "nao_existe" }.ConnectionString);
        var repository = new AdminNotificationConfigRepository(new MeuSecretario.Infrastructure.Database.NpgsqlConnectionFactory(broken));

        await Should.ThrowAsync<PostgresException>(() => repository.GetOrCreateAsync(userId, CancellationToken.None));
    }

    [Fact]
    public async Task Repository_BotSession_CountsTheDeletedRows()
    {
        await Execute("INSERT INTO baileys_auth (session_id, data_key, data_value) VALUES ('baileys_session', 'a', NULL), ('x', 'a', 'v')");

        (await new BotSessionRepository(postgres.Connections).ClearAsync(CancellationToken.None)).ShouldBe(1);
        (await Scalar<int>("SELECT count(*) FROM baileys_auth")).ShouldBe(1);
    }
}
