using System.Net;
using MeuSecretario.Api.Admin;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure.Database;
using MeuSecretario.IntegrationTests.Security;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace MeuSecretario.IntegrationTests.Admin;

/// <summary>As 13 rotas de setup (10 a 22) são no-op sem banco (AC-009-31, desvio 002) (T20).</summary>
[Trait("Category", "Docker")]
public sealed class AdminSetupRoutesTests(RedisFixture redis) : IClassFixture<RedisFixture>, IAsyncLifetime
{
    private const string Message =
        "Rota mantida por compatibilidade: o esquema do banco é gerenciado por migrations. Nenhuma alteração foi feita.";

    public static TheoryData<string, string, SetupFormat> Routes
    {
        get
        {
            var data = new TheoryData<string, string, SetupFormat>();
            foreach (var (method, path, format) in SetupRoutes.Table)
            {
                data.Add(method, path, format);
            }

            return data;
        }
    }

    private readonly List<SecurityTestHost> _hosts = [];
    private readonly SpyConnectionFactory _spy = new();

    public async Task InitializeAsync()
    {
        await using var host = new SecurityTestHost(redis.RedisUrl, redis.ConnectionString + ",allowAdmin=true");
        await host.Flush();
    }

    public async Task DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }
    }

    private SecurityTestHost Host()
    {
        var host = new SecurityTestHost(
            redis.RedisUrl,
            redis.ConnectionString + ",allowAdmin=true",
            services: services =>
            {
                services.RemoveAll<IDbConnectionFactory>();
                services.AddSingleton<IDbConnectionFactory>(_spy);
            });
        _hosts.Add(host);
        return host;
    }

    [Fact]
    public void TheTableHasThe13RoutesOfThe009bSpec()
    {
        SetupRoutes.Table.Count.ShouldBe(13);
        SetupRoutes.Table.Select(r => r.Path).Distinct().Count().ShouldBe(13);
        SetupRoutes.Table.Where(r => r.Method == "POST").Select(r => r.Path).ShouldBe(["/admin/setup-api-keys-tables"]);
        SetupRoutes.Table.Count(r => r.Format == SetupFormat.Text).ShouldBe(3);
        SetupRoutes.Table.Count(r => r.Format == SetupFormat.Pre).ShouldBe(7);
        SetupRoutes.Table.Count(r => r.Format == SetupFormat.EnvelopeB).ShouldBe(3);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    [Trait("AC", "009-31")]
    public async Task ValidKey_ReturnsTheFixedMessage_InTheOriginalFormat_WithoutTouchingTheDatabase(string method, string path, SetupFormat format)
    {
        var host = Host();

        var (response, body) = await host.Call(
            new HttpMethod(method), path, method == "POST" ? "{}" : null, new Dictionary<string, string> { ["X-API-KEY"] = AdminHttp.ApiKey });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        switch (format)
        {
            case SetupFormat.Text:
                body.ShouldBe(Message);
                response.Content.Headers.ContentType!.ToString().ShouldBe("text/html; charset=utf-8");
                break;
            case SetupFormat.Pre:
                body.ShouldBe("<pre>" + Message + "</pre>");
                response.Content.Headers.ContentType!.ToString().ShouldBe("text/html; charset=utf-8");
                break;
            default:
                body.ShouldBe(AdminHttp.Success(Message));
                response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
                break;
        }

        foreach (var forbidden in new[] { "user_api_key_para_automate", "tabelas_criadas", "planos_inseridos" })
        {
            body.ShouldNotContain(forbidden);
        }

        _spy.Opened.ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task WithoutKey_Is401_AndNothingRuns(string method, string path, SetupFormat _)
    {
        var host = Host();

        var (status, body) = await host.Admin(new HttpMethod(method), path, method == "POST" ? "{}" : null, key: null);

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(AdminHttp.Error("Não autorizado"));
        _spy.Opened.ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task WrongMethod_Is405(string method, string path, SetupFormat _)
    {
        var host = Host();
        var other = method == "GET" ? HttpMethod.Post : HttpMethod.Get;

        var (status, _) = await host.Admin(other, path, other == HttpMethod.Post ? "{}" : null);

        status.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }

    private sealed class SpyConnectionFactory : IDbConnectionFactory
    {
        private int _opened;

        public int Opened => Volatile.Read(ref _opened);

        public Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _opened);
            throw new InvalidOperationException("As rotas de setup não podem abrir conexão.");
        }
    }
}

/// <summary><c>oauth-config-check</c> e <c>debug-calendar</c> (AC-009-32, AC-009-33) (T21, T22).</summary>
[Trait("Category", "Docker")]
public sealed class AdminDiagnosticsEndpointsTests(RedisFixture redis) : IClassFixture<RedisFixture>, IAsyncLifetime
{
    private const string ClientId = "CLIENT-ID-SENTINELA-0123456789.apps.googleusercontent.com";
    private const string ClientSecret = "CLIENT-SECRET-SENTINELA-9z";
    private const string Token = "TOKEN-SENTINELA-qwertyuiopasdfghjkl";
    private const string Redirect = "https://api.exemplo.com/connect-calendar/callback";

    private readonly List<SecurityTestHost> _hosts = [];

    public async Task InitializeAsync()
    {
        await using var host = new SecurityTestHost(redis.RedisUrl, redis.ConnectionString + ",allowAdmin=true");
        await host.Flush();
    }

    public async Task DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }
    }

    private SecurityTestHost Host(IReadOnlyDictionary<string, string>? settings = null, Action<IServiceCollection>? services = null)
    {
        var host = new SecurityTestHost(redis.RedisUrl, redis.ConnectionString + ",allowAdmin=true", settings, services);
        _hosts.Add(host);
        return host;
    }

    [Fact]
    [Trait("AC", "009-32")]
    public async Task OAuthConfigCheck_Configured_ReportsBooleansAndRedirect_WithoutAnySecretFragment()
    {
        var host = Host(new Dictionary<string, string>
        {
            ["GOOGLE_CLIENT_ID"] = ClientId,
            ["GOOGLE_CLIENT_SECRET"] = ClientSecret,
            ["GOOGLE_REDIRECT_URI"] = Redirect,
        });

        var (status, body) = await host.Get("/admin/oauth-config-check");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            """{"status":"sucesso","mensagem":"Configuracao OAuth verificada","client_id_configured":true,"client_secret_configured":true,"redirect_uri":"https://api.exemplo.com/connect-calendar/callback"}""");
        body.ShouldNotContain("client_id_prefix");
        body.ShouldNotContain("SENTINELA");
        body.ShouldNotContain("CLIENT-ID");
    }

    [Fact]
    [Trait("AC", "009-32")]
    public async Task OAuthConfigCheck_NotConfigured_ReportsFalse()
    {
        var host = Host(new Dictionary<string, string>
        {
            ["GOOGLE_CLIENT_ID"] = string.Empty,
            ["GOOGLE_CLIENT_SECRET"] = string.Empty,
            ["GOOGLE_REDIRECT_URI"] = string.Empty,
        });

        var (status, body) = await host.Get("/admin/oauth-config-check");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            """{"status":"sucesso","mensagem":"Configuracao OAuth verificada","client_id_configured":false,"client_secret_configured":false,"redirect_uri":""}""");
    }

    [Fact]
    [Trait("AC", "009-33")]
    public async Task DebugCalendar_Connected_Returns200Pre_WithTokenHidden_AndTodayInSaoPaulo()
    {
        var calendar = new FakeCalendarClient();
        var host = Host(services: services => Fakes(services, new FakeGoogleAccounts(connected: true), calendar));

        var (response, body) = await host.Admin(HttpMethod.Get, "/admin/debug-calendar");

        response.ShouldBe(HttpStatusCode.OK);
        body.ShouldStartWith("<pre>");
        body.ShouldEndWith("</pre>");
        body.ShouldContain("🧪 TESTE DE DEBUG - GOOGLE CALENDAR");
        body.ShouldContain("   - Token: [oculto]");
        body.ShouldContain("✅ 2 calendários encontrados");
        body.ShouldContain("✅ API retornou 1 eventos");
        body.ShouldContain("   1. Evento de teste");
        body.ShouldContain("✅ TODOS OS TESTES PASSARAM!");
        body.ShouldNotContain("SENTINELA");
        calendar.RequestedUser.ShouldBe(1);
        body.ShouldContain($"   Data: {calendar.RequestedDay:yyyy-MM-dd}");
        calendar.RequestedDay.ShouldBe(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"))));
    }

    [Fact]
    [Trait("AC", "009-33")]
    public async Task DebugCalendar_Connected_ContentTypeIsHtmlUtf8()
    {
        var host = Host(services: services => Fakes(services, new FakeGoogleAccounts(connected: true), new FakeCalendarClient()));

        var (response, _) = await host.Call(
            HttpMethod.Get, "/admin/debug-calendar", headers: new Dictionary<string, string> { ["X-API-KEY"] = AdminHttp.ApiKey });

        response.Content.Headers.ContentType!.ToString().ShouldBe("text/html; charset=utf-8");
    }

    [Fact]
    [Trait("AC", "009-33")]
    public async Task DebugCalendar_NotConnected_ReturnsBrLinesWithoutPre()
    {
        var host = Host(services: services => Fakes(services, new FakeGoogleAccounts(connected: false), new FakeCalendarClient()));

        var (status, body) = await host.Get("/admin/debug-calendar");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldNotContain("<pre>");
        body.ShouldContain("<br>✅ Usuário conectado? False<br>❌ Usuário não conectado!");
    }

    [Fact]
    [Trait("AC", "009-30")]
    public async Task DebugCalendar_Failure_Is500FixedText_WithoutTheExceptionMessage()
    {
        var logs = new CapturedLogs();
        var host = Host(services: services =>
        {
            Fakes(services, new FakeGoogleAccounts(connected: true), new FakeCalendarClient { FailWith = "Resposta do Google: " + Token });
            services.AddCapturedLogs(logs);
        });

        var (status, body) = await host.Get("/admin/debug-calendar");

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(AdminHttp.Error("Erro ao executar o diagnóstico do Google Calendar. Tente novamente."));
        body.ShouldNotContain("SENTINELA");
        logs.All.ShouldNotContain("SENTINELA");
        logs.Entries.ShouldContain(e => e.Message.Contains("Erro na rota admin debug-calendar"));
    }

    [Theory]
    [InlineData("/admin/oauth-config-check")]
    [InlineData("/admin/debug-calendar")]
    public async Task WithoutKey_Is401_AndTheCalendarIsNeverCalled(string path)
    {
        var accounts = new FakeGoogleAccounts(connected: true);
        var host = Host(services: services => Fakes(services, accounts, new FakeCalendarClient()));

        var (status, body) = await host.Get(path, key: null);

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(AdminHttp.Error("Não autorizado"));
        accounts.Calls.ShouldBe(0);
    }

    private static void Fakes(IServiceCollection services, FakeGoogleAccounts accounts, FakeCalendarClient calendar)
    {
        services.RemoveAll<IGoogleAccountService>();
        services.AddSingleton<IGoogleAccountService>(accounts);
        services.RemoveAll<ICalendarClient>();
        services.AddSingleton<ICalendarClient>(calendar);
    }

    private sealed class FakeGoogleAccounts(bool connected) : IGoogleAccountService
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<GoogleCredentialResult> GetCredentialsAsync(int userId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new GoogleCredentialResult(GoogleCredentialStatus.Connected, Token, ["scope"]));
        }

        public Task<bool> IsConnectedAsync(int userId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(connected);
        }

        public Task<bool> HasDriveScopeAsync(int userId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> RevokeAsync(int userId, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class FakeCalendarClient : ICalendarClient
    {
        public string? FailWith { get; init; }

        public int? RequestedUser { get; private set; }

        public DateOnly? RequestedDay { get; private set; }

        public Task<IReadOnlyList<CalendarInfo>> ListSelectedCalendarsAsync(int userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CalendarInfo>>([new CalendarInfo("primary", "Principal", true), new CalendarInfo("c2", "Outro", false)]);

        public Task<IReadOnlyList<CalendarEvent>> GetEventsInRangeAsync(int userId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken)
        {
            if (FailWith is not null)
            {
                throw new InvalidOperationException(FailWith);
            }

            RequestedUser = userId;
            RequestedDay = startDate;
            return Task.FromResult<IReadOnlyList<CalendarEvent>>(
                [new CalendarEvent("evento-id-sentinela", "Evento de teste", "2026-10-04T10:00:00-03:00", "2026-10-04T11:00:00-03:00", string.Empty, string.Empty, false, null)]);
        }

        public Task<IReadOnlyList<CalendarEvent>> ListEventsAsync(int userId, ListEventsQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CalendarEvent?> GetEventAsync(int userId, string eventId, string calendarId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string> InsertEventAsync(int userId, NewCalendarEvent newEvent, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> UpdateEventAsync(int userId, string eventId, string calendarId, EventUpdate update, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CalendarDeleteResult> DeleteEventAsync(int userId, string eventId, string calendarId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<CalendarEvent>> FindEventsByTitleAsync(int userId, string title, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
