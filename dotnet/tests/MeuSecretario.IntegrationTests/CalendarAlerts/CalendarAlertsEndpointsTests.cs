using System.Net;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.CalendarAlerts;

[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public sealed class CalendarAlertsEndpointsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string NotConnected = "Você precisa conectar o Google Calendar primeiro para ativar alertas de tarefas";
    private const string NoParameters = "Nenhum parâmetro fornecido. Use 'ativo' e/ou 'minutos_antes'";
    private const string BadMinutes = "minutos_antes deve ser um número entre 1 e 60";
    private const string Defaults = """{"alertas_tarefas_ativo":false,"minutos_antes":1}""";

    private readonly CalendarAlertsEnv _env = new(postgres);
    private int _user;
    private int _other;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _user = await _env.Http.InsertUserAsync("5511900000001");
        _other = await _env.Http.InsertUserAsync("5511900000002", "Bia");
    }

    public Task DisposeAsync() => _env.CloseAsync();

    private static string Err(string message) => $$"""{"status":"erro","mensagem":"{{message}}"}""";

    private static string Ok(string message, bool active, int minutes) =>
        "{\"status\":\"sucesso\",\"mensagem\":\"" + message + "\",\"config\":{\"alertas_tarefas_ativo\":" +
        (active ? "true" : "false") + ",\"minutos_antes\":" + minutes + "}}";

    private string Path(string route, int? id = null) => $"/calendar-alerts/{route}/{id ?? _user}";

    private Task<(HttpStatusCode Status, string Body)> Config(string? json, int? asUser = null) =>
        _env.Http.Post(asUser ?? _user, Path("config"), json);

    private Task<int> Rows(int? id = null) =>
        _env.Http.ScalarAsync<int>("SELECT count(*)::int FROM calendaralertconfigs WHERE usuario_id = @id", new { id = id ?? _user });

    private async Task SeedAsync(int userId, bool active, int minutes) =>
        await _env.Http.ExecuteAsync(
            "INSERT INTO calendaralertconfigs (usuario_id, alertas_tarefas_ativo, minutos_antes) VALUES (@userId, @active, @minutes)",
            new { userId, active, minutes });

    // ---------------------------------------------------------------- auth, id e roteamento

    [Theory]
    [Trait("AC", "014-01")]
    [InlineData("GET", "config")]
    [InlineData("POST", "config")]
    [InlineData("POST", "ativar")]
    [InlineData("POST", "desativar")]
    public async Task WithoutToken_Is401EnvelopeA_AndNothingIsWritten(string method, string route)
    {
        var (status, body) = await _env.Http.SendAsync(new HttpMethod(method), Path(route), """{"ativo":false}""");

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe("""{"status":"error","message":"Token de autenticação não fornecido"}""");
        (await Rows()).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "014-01")]
    public async Task BadAuthorizationFormat_AndInvalidToken_Are401WithTheSpecMessages()
    {
        var format = await _env.Http.SendAsync(HttpMethod.Get, Path("config"), authorization: "Token abc");
        var invalid = await _env.Http.SendAsync(HttpMethod.Get, Path("config"), authorization: "Bearer xyz");

        format.Status.ShouldBe(HttpStatusCode.Unauthorized);
        format.Body.ShouldBe("""{"status":"error","message":"Formato de autenticação inválido. Use: Bearer <token>"}""");
        invalid.Status.ShouldBe(HttpStatusCode.Unauthorized);
        invalid.Body.ShouldBe("""{"status":"error","message":"Token inválido ou expirado"}""");
    }

    [Theory]
    [Trait("AC", "014-02")]
    [InlineData("GET", "config")]
    [InlineData("POST", "config")]
    [InlineData("POST", "ativar")]
    [InlineData("POST", "desativar")]
    public async Task PathIdOfAnotherUser_Is403_AndTheOtherConfigIsUntouched(string method, string route)
    {
        await SeedAsync(_other, active: true, minutes: 20);

        var (status, body) = await _env.Http.SendAsync(
            new HttpMethod(method), Path(route, _other), """{"ativo":false,"minutos_antes":2}""", userId: _user);

        status.ShouldBe(HttpStatusCode.Forbidden);
        body.ShouldBe(Err("Acesso negado"));
        (await _env.Http.ScalarAsync<bool>("SELECT alertas_tarefas_ativo FROM calendaralertconfigs WHERE usuario_id = @_other", new { _other })).ShouldBeTrue();
        (await _env.Http.ScalarAsync<int>("SELECT minutos_antes FROM calendaralertconfigs WHERE usuario_id = @_other", new { _other })).ShouldBe(20);
        (await Rows()).ShouldBe(0);
    }

    [Theory]
    [Trait("AC", "014-03")]
    [InlineData("/calendar-alerts/config/abc")]
    [InlineData("/calendar-alerts/config/-1")]
    [InlineData("/calendar-alerts/config/99999999999")]
    [InlineData("/calendar-alerts/config/1/")]
    [InlineData("/calendar-alerts/config")]
    public async Task NonNumericOrHugeIds_Are404(string path)
    {
        var (status, _) = await _env.Http.Get(_user, path);

        status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("AC", "014-03")]
    public async Task WrongMethods_Are405()
    {
        (await _env.Http.Get(_user, Path("ativar"))).Status.ShouldBe(HttpStatusCode.MethodNotAllowed);
        (await _env.Http.Get(_user, Path("desativar"))).Status.ShouldBe(HttpStatusCode.MethodNotAllowed);
        (await _env.Http.Delete(_user, Path("config"))).Status.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }

    // ---------------------------------------------------------------- GET

    [Fact]
    [Trait("AC", "014-10")]
    public async Task Get_WithoutRow_CreatesTheDefaultRow()
    {
        var (status, body) = await _env.Http.Get(_user, Path("config"));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe($$"""{"status":"sucesso","config":{{Defaults}}}""");
        (await Rows()).ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "014-11")]
    public async Task Get_WithRow_ReturnsItsValues_AndCreatesNothingElse()
    {
        await SeedAsync(_user, active: true, minutes: 15);

        var (status, body) = await _env.Http.Get(_user, Path("config"));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"sucesso","config":{"alertas_tarefas_ativo":true,"minutos_antes":15}}""");
        (await Rows()).ShouldBe(1);
        (await Rows(_other)).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "014-12")]
    public async Task Get_WhenTheRepositoryFails_Is500WithFixedText()
    {
        _env.Repository.FailGetOrCreate = true;

        var (status, body) = await _env.Http.Get(_user, Path("config"));

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(Err("Erro ao obter configuração de alertas"));
        body.ShouldNotContain(FlakyRepository.Secret, Case.Sensitive);
    }

    // ---------------------------------------------------------------- POST config

    [Theory]
    [Trait("AC", "014-20")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("não é json")]
    [InlineData("""{"ativo":null,"minutos_antes":null}""")]
    [InlineData("""{"outro":1}""")]
    public async Task Config_WithoutParameters_Is400(string? json)
    {
        var (status, body) = await Config(json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(Err(NoParameters));
        (await Rows()).ShouldBe(0);
        _env.Google.Calls.ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "014-21")]
    public async Task Config_ActivatingWithoutGoogle_Is400_AndWritesNothing()
    {
        _env.Google.Connected = false;

        var (status, body) = await Config("""{"ativo":true}""");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(Err(NotConnected));
        (await Rows()).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "014-22")]
    public async Task Config_Activating_WithGoogle_UpdatesTheRow_AndRenewsUpdatedAt()
    {
        await SeedAsync(_user, active: false, minutes: 9);
        await _env.Http.ExecuteAsync("UPDATE calendaralertconfigs SET updated_at = '2020-01-01' WHERE usuario_id = @_user", new { _user });

        var (status, body) = await Config("""{"ativo":true}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(Ok("Alertas de tarefas ativados", true, 9));
        (await _env.Http.ScalarAsync<bool>("SELECT updated_at > now() - interval '1 minute' FROM calendaralertconfigs WHERE usuario_id = @_user", new { _user })).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "014-23")]
    public async Task Config_Deactivating_NeedsNoGoogle()
    {
        _env.Google.Connected = false;
        await SeedAsync(_user, active: true, minutes: 4);

        var (status, body) = await Config("""{"ativo":false}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(Ok("Alertas de tarefas desativados", false, 4));
        _env.Google.Calls.ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "014-24")]
    public async Task Config_OnlyMinutes_KeepsActive_AndNeedsNoGoogle()
    {
        _env.Google.Connected = false;
        await SeedAsync(_user, active: true, minutes: 4);

        var (status, body) = await Config("""{"minutos_antes":5}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(Ok("alerta configurado para 5 minuto(s) antes", true, 5));
        _env.Google.Calls.ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "014-25")]
    public async Task Config_ActiveAndMinutes_JoinsTheMessage()
    {
        var (status, body) = await Config("""{"ativo":true,"minutos_antes":10}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(Ok("Alertas de tarefas ativados e alerta configurado para 10 minuto(s) antes", true, 10));
    }

    [Theory]
    [Trait("AC", "014-26")]
    [InlineData("0")]
    [InlineData("61")]
    [InlineData("-1")]
    [InlineData("5.5")]
    [InlineData("5.0")]
    [InlineData("\"5\"")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("99999999999999999999")]
    public async Task Config_InvalidMinutes_Is400_CreatesTheDefaultRow_AndChangesNothing(string minutes)
    {
        var (status, body) = await Config($$"""{"minutos_antes":{{minutes}}}""");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(Err(BadMinutes));
        (await Rows()).ShouldBe(1);
        (await _env.Http.ScalarAsync<int>("SELECT minutos_antes FROM calendaralertconfigs WHERE usuario_id = @_user", new { _user })).ShouldBe(1);
    }

    [Theory]
    [Trait("AC", "014-26")]
    [InlineData(1)]
    [InlineData(60)]
    public async Task Config_BoundaryMinutes_AreAccepted(int minutes)
    {
        var (status, body) = await Config($$"""{"minutos_antes":{{minutes}}}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(Ok($"alerta configurado para {minutes} minuto(s) antes", false, minutes));
    }

    [Theory]
    [Trait("AC", "014-27")]
    [InlineData("\"abc\"")]
    [InlineData("\"true\"")]
    [InlineData("\"false\"")]
    [InlineData("1")]
    [InlineData("[]")]
    public async Task Config_NonBooleanActive_Is400WithItsOwnText_AndNothingChanges(string ativo)
    {
        // Desvio 120.
        var (status, body) = await Config($$"""{"ativo":{{ativo}}}""");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(Err("ativo deve ser true ou false"));
        _env.Google.Calls.ShouldBe(0);
        (await _env.Http.ScalarAsync<int>("SELECT count(*)::int FROM calendaralertconfigs WHERE alertas_tarefas_ativo")).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "014-28")]
    public async Task Config_FalsyNonBooleanActive_SkipsGoogle()
    {
        _env.Google.Connected = false;

        var (status, _) = await Config("""{"ativo":0,"minutos_antes":3}""");

        status.ShouldBe(HttpStatusCode.BadRequest);
        _env.Google.Calls.ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "014-29")]
    public async Task Config_UpdateFailing_Is400FixedText_GoogleFailing_Is500FixedText()
    {
        _env.Repository.FailUpdate = true;
        var update = await Config("""{"ativo":false}""");
        _env.Repository.FailUpdate = false;
        _env.Google.Throws = true;
        var google = await Config("""{"ativo":true}""");

        update.Status.ShouldBe(HttpStatusCode.BadRequest);
        update.Body.ShouldBe(Err("Erro ao atualizar configuração"));
        google.Status.ShouldBe(HttpStatusCode.InternalServerError);
        google.Body.ShouldBe(Err("Erro ao atualizar configuração de alertas"));
        (update.Body + google.Body).ShouldNotContain("SEGREDO", Case.Sensitive);
    }

    // ---------------------------------------------------------------- ativar

    [Fact]
    [Trait("AC", "014-30")]
    public async Task Activate_WithoutGoogle_Is400_WithConnected_KeepsMinutes()
    {
        await SeedAsync(_user, active: false, minutes: 12);
        _env.Google.Connected = false;
        var refused = await _env.Http.Post(_user, Path("ativar"), null);
        _env.Google.Connected = true;
        var accepted = await _env.Http.Post(_user, Path("ativar"), null);

        refused.Status.ShouldBe(HttpStatusCode.BadRequest);
        refused.Body.ShouldBe(Err(NotConnected));
        accepted.Status.ShouldBe(HttpStatusCode.OK);
        accepted.Body.ShouldBe(Ok("Alertas de tarefas ativados", true, 12));
    }

    [Fact]
    [Trait("AC", "014-31")]
    public async Task Activate_WithMinutes_UpdatesBoth_AndZeroIsRejected()
    {
        var ok = await _env.Http.Post(_user, Path("ativar"), """{"minutos_antes":7}""");
        var bad = await _env.Http.Post(_user, Path("ativar"), """{"minutos_antes":0}""");

        ok.Body.ShouldBe(Ok("Alertas de tarefas ativados e alerta configurado para 7 minuto(s) antes", true, 7));
        bad.Status.ShouldBe(HttpStatusCode.BadRequest);
        bad.Body.ShouldBe(Err(BadMinutes));
    }

    [Fact]
    [Trait("AC", "014-33")]
    public async Task Activate_Unexpected_Is500FixedText()
    {
        _env.Repository.FailGetOrCreate = true;

        var (status, body) = await _env.Http.Post(_user, Path("ativar"), null);

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(Err("Erro ao ativar alertas de tarefas"));
        body.ShouldNotContain(FlakyRepository.Secret, Case.Sensitive);
    }

    // ---------------------------------------------------------------- desativar

    [Fact]
    [Trait("AC", "014-32")]
    public async Task Deactivate_PreservesMinutes_NeedsNoGoogle_AndCreatesTheRowWhenMissing()
    {
        _env.Google.Connected = false;
        var created = await _env.Http.Post(_user, Path("desativar"), null);
        await _env.Http.ExecuteAsync("UPDATE calendaralertconfigs SET alertas_tarefas_ativo = true, minutos_antes = 25 WHERE usuario_id = @_user", new { _user });
        var again = await _env.Http.Post(_user, Path("desativar"), null);

        created.Status.ShouldBe(HttpStatusCode.OK);
        created.Body.ShouldBe(Ok("Alertas de tarefas desativados", false, 1));
        again.Body.ShouldBe(Ok("Alertas de tarefas desativados", false, 25));
        _env.Google.Calls.ShouldBe(0);
        (await Rows()).ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "014-33")]
    public async Task Deactivate_Unexpected_Is500FixedText_AndUpdateFailureIs400()
    {
        _env.Repository.FailUpdate = true;
        var update = await _env.Http.Post(_user, Path("desativar"), null);
        _env.Repository.FailUpdate = false;
        _env.Repository.FailGetOrCreate = true;
        var other = await _env.Http.Post(_user, Path("desativar"), null);

        update.Status.ShouldBe(HttpStatusCode.BadRequest);
        update.Body.ShouldBe(Err("Erro ao atualizar configuração"));
        other.Status.ShouldBe(HttpStatusCode.InternalServerError);
        other.Body.ShouldBe(Err("Erro ao desativar alertas de tarefas"));
    }

    // ---------------------------------------------------------------- isolamento

    [Fact]
    [Trait("AC", "014-50")]
    public async Task EachUser_ChangesOnlyTheOwnConfig()
    {
        await Config("""{"ativo":true,"minutos_antes":30}""");
        await _env.Http.Post(_other, Path("ativar", _other), """{"minutos_antes":2}""");
        await _env.Http.Post(_user, Path("desativar"), null);

        var mine = await _env.Http.Get(_user, Path("config"));
        var theirs = await _env.Http.Get(_other, Path("config", _other));

        mine.Body.ShouldBe("""{"status":"sucesso","config":{"alertas_tarefas_ativo":false,"minutos_antes":30}}""");
        theirs.Body.ShouldBe("""{"status":"sucesso","config":{"alertas_tarefas_ativo":true,"minutos_antes":2}}""");
    }
}
