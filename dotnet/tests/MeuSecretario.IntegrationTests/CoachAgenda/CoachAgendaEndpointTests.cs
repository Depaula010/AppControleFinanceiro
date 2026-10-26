using System.Net;
using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.CoachAgenda;
using MeuSecretario.Domain.CoachAgenda;
using MeuSecretario.IntegrationTests.Admin;
using MeuSecretario.IntegrationTests.Security;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.TestFixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeuSecretario.IntegrationTests.CoachAgenda;

/// <summary>Spec 015 (T5 a T7): a rota pela API real: desligada por padrão, chave própria, contrato, erros e log sem conteúdo.</summary>
[Trait("Category", "Docker")]
public sealed class CoachAgendaEndpointTests(RedisFixture redis) : IClassFixture<RedisFixture>, IAsyncLifetime, IDisposable
{
    private const string Chave = "chave-do-coach-so-para-teste-0123456789";
    private const string Rota = "/internal/coach/agenda?de=2026-10-26&ate=2026-11-08";

    private readonly List<SecurityTestHost> _hosts = [];
    private readonly CapturedLogs _logs = new();
    private readonly AgendaFalsa _agenda = new();
    private readonly ChavesFalsas _chaves = new();

    public async Task InitializeAsync()
    {
        await using var host = new SecurityTestHost(redis.RedisUrl, redis.ConnectionString + ",allowAdmin=true");
        await host.Flush();
    }

    public void Dispose() => _logs.Dispose();

    public async Task DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }
    }

    private SecurityTestHost Host(bool ligada = true, NivelDeDetalhe nivel = NivelDeDetalhe.Completo, bool mascarar = true, bool conectado = true)
    {
        var settings = ligada
            ? new Dictionary<string, string> { ["COACH_AGENDA_API_KEY"] = Chave, ["COACH_AGENDA_USER_ID"] = "7" }
            : new Dictionary<string, string>();
        _agenda.Conectado = conectado;
        var host = new SecurityTestHost(
            redis.RedisUrl,
            redis.ConnectionString + ",allowAdmin=true",
            settings,
            services =>
            {
                services.RemoveAll<ICalendarClient>();
                services.AddSingleton<ICalendarClient>(_agenda);
                services.RemoveAll<IGoogleAccountService>();
                services.AddSingleton<IGoogleAccountService>(_agenda);
                services.RemoveAll<IChavesDaAgenda>();
                services.AddSingleton<IChavesDaAgenda>(_chaves);
                services.RemoveAll<ICoachAgendaConfig>();
                services.AddSingleton<ICoachAgendaConfig>(new ConfigFixa(nivel, mascarar));
                services.AddCapturedLogs(_logs);
            });
        _hosts.Add(host);
        return host;
    }

    private static Task<(HttpResponseMessage Response, string Body)> Chamar(SecurityTestHost host, string url, string? chave = Chave) =>
        host.Call(HttpMethod.Get, url, null, chave is null ? null : new Dictionary<string, string> { ["X-API-KEY"] = chave });

    [Fact]
    public async Task AC_016_06_Sem_nenhuma_chave_a_rota_responde_401_e_o_resto_segue_igual()
    {
        var host = Host(ligada: false);

        (await Chamar(host, Rota)).Response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await host.Send("/")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);                          // as rotas existentes não mudam
        _agenda.Chamadas.ShouldBe(0);
    }

    [Fact]
    public async Task AC_016_07_Com_as_variaveis_antigas_a_rota_segue_funcionando_para_aquele_usuario()
    {
        var host = Host();

        (await Chamar(host, Rota)).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _agenda.UltimoUsuario.ShouldBe(7);
    }

    [Fact]
    public async Task AC_016_04_Duas_pessoas_com_chaves_diferentes_recebem_cada_uma_a_sua_agenda()
    {
        var host = Host(ligada: false);
        _chaves.Donos["chave-da-ana"] = 11;
        _chaves.Donos["chave-do-beto"] = 22;

        (await Chamar(host, Rota, "chave-da-ana")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _agenda.UltimoUsuario.ShouldBe(11);
        (await Chamar(host, Rota, "chave-do-beto")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _agenda.UltimoUsuario.ShouldBe(22);
    }

    [Fact]
    public async Task AC_016_05_Falha_do_banco_nas_chaves_nega_com_401_e_nao_chama_o_Google()
    {
        var host = Host(ligada: false);
        _chaves.Falha = true;

        (await Chamar(host, Rota, "qualquer")).Response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        _agenda.Chamadas.ShouldBe(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("chave-errada")]
    [InlineData("")]
    public async Task AC_015_02_Sem_a_chave_certa_responde_401_e_nao_chama_o_Google(string? chave)
    {
        var host = Host();

        var (resposta, corpo) = await Chamar(host, Rota, chave);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        corpo.ShouldBe("""{"erro":"Chave inválida"}""");
        _agenda.Chamadas.ShouldBe(0);
    }

    [Fact]
    public async Task AC_015_02_A_API_SECRET_KEY_nao_vale_nesta_rota()
    {
        var host = Host();

        (await Chamar(host, Rota, AuthFixtures.JwtSecret)).Response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        _agenda.Chamadas.ShouldBe(0);
    }

    [Theory]
    [InlineData("/internal/coach/agenda")]
    [InlineData("/internal/coach/agenda?de=2026-10-26")]
    [InlineData("/internal/coach/agenda?de=26/10/2026&ate=2026-11-08")]
    [InlineData("/internal/coach/agenda?de=2026-11-08&ate=2026-10-26")]
    [InlineData("/internal/coach/agenda?de=2026-10-26&ate=2026-12-20")]
    public async Task AC_015_03_Entrada_invalida_responde_400_e_nao_chama_o_Google(string url)
    {
        var host = Host();

        var (resposta, corpo) = await Chamar(host, url);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        corpo.ShouldBe("""{"erro":"Informe de e ate (AAAA-MM-DD), com até 42 dias"}""");
        _agenda.Chamadas.ShouldBe(0);
    }

    [Fact]
    public async Task AC_015_04_e_09_Completo_devolve_os_quatro_campos_e_o_dia_inteiro_so_com_data()
    {
        var host = Host(nivel: NivelDeDetalhe.Completo);

        var (resposta, corpo) = await Chamar(host, Rota);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = JsonDocument.Parse(corpo).RootElement;
        (json.GetProperty("de").GetString(), json.GetProperty("ate").GetString()).ShouldBe(("2026-10-26", "2026-11-08"));
        var eventos = json.GetProperty("eventos").EnumerateArray().ToList();
        eventos.Count.ShouldBe(2);
        var reuniao = eventos[0];
        reuniao.GetProperty("inicio").GetString().ShouldBe("2026-10-27T09:00:00-03:00");
        reuniao.GetProperty("nivel").GetString().ShouldBe("completo");
        (reuniao.GetProperty("titulo").GetString(), reuniao.GetProperty("descricao").GetString(), reuniao.GetProperty("local").GetString(), reuniao.GetProperty("calendario").GetString())
            .ShouldBe(("Reunião de planejamento", "Link [link] e telefone [telefone]. Pauta confidencial. [e-mail]", "Sala 3", "Trabalho"));
        var folga = eventos[1];
        (folga.GetProperty("diaInteiro").GetBoolean(), folga.GetProperty("inicio").GetString(), folga.GetProperty("fim").GetString()).ShouldBe((true, "2026-10-30", "2026-10-31"));
        _agenda.UltimoUsuario.ShouldBe(7);
    }

    [Fact]
    public async Task AC_015_04_Ocupado_nao_traz_titulo_descricao_local_nem_calendario_e_nunca_traz_id()
    {
        var host = Host(nivel: NivelDeDetalhe.Ocupado);

        var (_, corpo) = await Chamar(host, Rota);

        var campos = JsonDocument.Parse(corpo).RootElement.GetProperty("eventos").EnumerateArray().First().EnumerateObject().Select(p => p.Name).ToList();
        campos.ShouldBe(["inicio", "fim", "diaInteiro", "nivel"]);
        corpo.ShouldNotContain("Reunião");
        corpo.ShouldNotContain("evento-secreto-123");                                                    // AC-015-12: nunca o id do evento
        corpo.ShouldNotContain("participante@empresa.com");
    }

    [Fact]
    public async Task AC_015_07_Sem_mascarar_o_texto_sai_igual()
    {
        var host = Host(mascarar: false);

        var (_, corpo) = await Chamar(host, Rota);

        corpo.ShouldContain("https://meet.google.com/abc-defg-hij");
    }

    [Fact]
    public async Task AC_015_10_Google_nao_conectado_responde_503_com_texto_fixo()
    {
        var host = Host(conectado: false);

        var (resposta, corpo) = await Chamar(host, Rota);

        resposta.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        corpo.ShouldBe("""{"erro":"Google Calendar não conectado"}""");
    }

    [Fact]
    public async Task AC_015_10_Falha_do_Google_responde_503_sem_vazar_a_causa()
    {
        var host = Host();
        _agenda.Falha = new CalendarApiException("SEGREDO do corpo da resposta do Google", HttpStatusCode.InternalServerError);

        var (resposta, corpo) = await Chamar(host, Rota);

        resposta.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        corpo.ShouldBe("""{"erro":"Agenda indisponível agora"}""");
        corpo.ShouldNotContain("SEGREDO");
    }

    [Fact]
    public async Task AC_015_11_Nenhum_log_leva_titulo_descricao_local_ou_calendario()
    {
        var host = Host();
        await Chamar(host, Rota);
        _agenda.Falha = new CalendarApiException("Reunião de planejamento SEGREDO", HttpStatusCode.InternalServerError);
        await Chamar(host, Rota);
        await Chamar(host, Rota, "chave-errada");

        var tudo = _logs.All;
        foreach (var proibido in new[] { "Reunião de planejamento", "Pauta confidencial", "Sala 3", "Trabalho", "meet.google.com", "SEGREDO", Chave, "evento-secreto-123" })
        {
            tudo.ShouldNotContain(proibido);
        }

        tudo.ShouldContain("Agenda do Coach atendida: 2 eventos de 2026-10-26 a 2026-11-08");
    }

    private sealed class ChavesFalsas : IChavesDaAgenda
    {
        public Dictionary<string, int> Donos { get; } = [];

        public bool Falha { get; set; }

        public Task<int?> ResolverAsync(string chave, CancellationToken cancellationToken) =>
            Falha ? throw new InvalidOperationException("banco fora") : Task.FromResult(Donos.TryGetValue(chave, out var id) ? id : (int?)null);

        public Task<ChaveGerada> GerarAsync(int userId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ChaveExistente?> ConsultarAsync(int userId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> RevogarAsync(int userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ConfigFixa(NivelDeDetalhe nivel, bool mascarar) : ICoachAgendaConfig
    {
        public Task<NivelDeDetalhe> NivelDeAsync(string? calendarId, CancellationToken cancellationToken) => Task.FromResult(nivel);

        public Task<bool> MascararAsync(CancellationToken cancellationToken) => Task.FromResult(mascarar);
    }

    /// <summary>Google e agenda falsos: conta as chamadas e devolve dois eventos (um com hora e um de dia inteiro).</summary>
    private sealed class AgendaFalsa : ICalendarClient, IGoogleAccountService
    {
        public bool Conectado { get; set; } = true;

        public Exception? Falha { get; set; }

        public int Chamadas { get; private set; }

        public int? UltimoUsuario { get; private set; }

        public Task<IReadOnlyList<CalendarEvent>> GetEventsInRangeAsync(int userId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken)
        {
            Chamadas++;
            UltimoUsuario = userId;
            if (Falha is not null)
            {
                throw Falha;
            }

            return Task.FromResult<IReadOnlyList<CalendarEvent>>(
            [
                new CalendarEvent("evento-secreto-123", "Reunião de planejamento", "2026-10-27T09:00:00-03:00", "2026-10-27T10:00:00-03:00", "Sala 3",
                    "Link https://meet.google.com/abc-defg-hij e telefone (11) 98765-4321. Pauta confidencial. participante@empresa.com", false, "Trabalho", "trabalho@empresa.com"),
                new CalendarEvent("evento-secreto-456", "Folga", "2026-10-30", "2026-10-31", string.Empty, string.Empty, true, "Pessoal", "pessoal@gmail.com"),
            ]);
        }

        public Task<bool> IsConnectedAsync(int userId, CancellationToken cancellationToken) => Task.FromResult(Conectado);

        public Task<IReadOnlyList<CalendarInfo>> ListSelectedCalendarsAsync(int userId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<CalendarEvent>> ListEventsAsync(int userId, ListEventsQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CalendarEvent?> GetEventAsync(int userId, string eventId, string calendarId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string> InsertEventAsync(int userId, NewCalendarEvent newEvent, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> UpdateEventAsync(int userId, string eventId, string calendarId, EventUpdate update, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CalendarDeleteResult> DeleteEventAsync(int userId, string eventId, string calendarId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<CalendarEvent>> FindEventsByTitleAsync(int userId, string title, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GoogleCredentialResult> GetCredentialsAsync(int userId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> HasDriveScopeAsync(int userId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> RevokeAsync(int userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
