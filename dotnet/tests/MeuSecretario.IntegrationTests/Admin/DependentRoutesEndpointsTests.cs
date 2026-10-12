using System.Net;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Admin;
using MeuSecretario.Domain.Jobs;
using MeuSecretario.IntegrationTests.Security;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.IntegrationTests.Admin;

/// <summary>As 8 rotas dependentes da 009c com portas falsas (T25 a T30; AC-009-34 a 39).</summary>
[Trait("Category", "Docker")]
public sealed class DependentRoutesEndpointsTests(RedisFixture redis) : IClassFixture<RedisFixture>, IAsyncLifetime, IDisposable
{
    private static readonly string[] AllRoutes =
    [
        "/admin/run-motor-agendamentos",
        "/admin/trigger-agenda-notifications",
        "/admin/trigger-bills-notifications",
        "/admin/trigger-daily-briefing",
        "/admin/trigger-monthly-reports-inicio",
        "/admin/trigger-monthly-reports-fim",
        "/admin/test-notification",
        "/admin/test-monthly-report/5",
        "/admin/test-daily-briefing",
    ];

    public static TheoryData<string> Routes
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var route in AllRoutes)
            {
                data.Add(route);
            }

            return data;
        }
    }

    private readonly List<SecurityTestHost> _hosts = [];
    private readonly DependentFakes _fakes = new();
    private readonly CapturedLogs _logs = new();

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

    public void Dispose() => _logs.Dispose();

    private SecurityTestHost Host(Action<IServiceCollection>? extra = null)
    {
        var host = new SecurityTestHost(
            redis.RedisUrl,
            redis.ConnectionString + ",allowAdmin=true",
            services: services =>
            {
                _fakes.Install(services);
                services.AddCapturedLogs(_logs);
                extra?.Invoke(services);
            });
        _hosts.Add(host);
        return host;
    }

    // ---- 401 sem chamar nada ----------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Routes))]
    [Trait("AC", "009-34")]
    public async Task WithoutKey_Is401_AndNoPortOrRepositoryIsCalled(string path)
    {
        var host = Host();

        foreach (var key in new string?[] { null, string.Empty, "errada" })
        {
            var (status, body) = await host.Admin(HttpMethod.Post, path, "{}", key);

            status.ShouldBe(HttpStatusCode.Unauthorized);
            body.ShouldBe(AdminHttp.Error("Não autorizado"));
        }

        _fakes.JobCalls.ShouldBe(0);
        _fakes.Bot.Sent.ShouldBeEmpty();
    }

    // ---- 25: run-motor-agendamentos ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("{}")]
    [InlineData(null)]
    [InlineData("isto não é json")]
    [Trait("AC", "009-34")]
    public async Task Motor_RunsWithOrWithoutABody_And200(string? json)
    {
        var host = Host();

        var (status, body) = await host.Post("/admin/run-motor-agendamentos", json);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(AdminHttp.Success("Agendamentos processados."));
        _fakes.Motor.Calls.ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "009-34")]
    public async Task Motor_AlreadyRunning_Is200WithTheNewMessage()
    {
        _fakes.Motor.Result = MotorRunResult.AlreadyRunning;
        var host = Host();

        var (status, body) = await host.Post("/admin/run-motor-agendamentos", "{}");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(AdminHttp.Success("Agendamentos já estão em processamento."));
    }

    [Fact]
    [Trait("AC", "009-34")]
    [Trait("AC", "009-30")]
    public async Task Motor_Exception_Is500FixedText_WithoutTheExceptionMessage()
    {
        _fakes.Motor.Failure = new InvalidOperationException(DependentFakes.Sentinel);
        var host = Host();

        var (status, body) = await host.Post("/admin/run-motor-agendamentos", "{}");

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(AdminHttp.Error("Erro ao processar agendamentos. Tente novamente."));
        body.ShouldNotContain(DependentFakes.Sentinel);
        _logs.All.ShouldNotContain(DependentFakes.Sentinel);
        _logs.Entries.ShouldContain(e => e.Message.Contains("Erro na rota admin run-motor-agendamentos") && e.Message.Contains("InvalidOperationException"));
    }

    // ---- 26 a 30: trigger-* -----------------------------------------------------------------------------------------

    [Fact]
    [Trait("AC", "009-35")]
    [Trait("Desvio", "028")]
    public async Task TriggerAgenda_IsTheReplacementNotice_WithoutAnyPort()
    {
        var host = Host();

        var (status, body) = await host.Post("/admin/trigger-agenda-notifications");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            """{"status":"sucesso","mensagem":"Agenda diária foi substituída pelo resumo matinal. Nada a processar.","usuarios_processados":0,"notificacoes_enviadas":0,"erros":0}""");
        _fakes.JobCalls.ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "009-35")]
    public async Task TriggerBills_ReturnsTheResultFieldsAtTheRoot()
    {
        var host = Host();

        var (status, body) = await host.Post("/admin/trigger-bills-notifications");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            """{"status":"sucesso","mensagem":"Processamento de contas a vencer concluído","usuarios_processados":3,"notificacoes_enviadas":2,"erros":1,"hora_processamento":"2026-10-04T09:00:00.123456-03:00"}""");
        _fakes.Bills.Calls.ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "009-35")]
    public async Task TriggerDailyBriefing_WithUsers_ReturnsTheCounters()
    {
        var host = Host();

        var (status, body) = await host.Post("/admin/trigger-daily-briefing");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            """{"status":"sucesso","mensagem":"Processamento de resumos matinais concluído","usuarios_processados":2,"enviados_sucesso":1,"erros":1,"horario":"07:30"}""");
    }

    [Fact]
    [Trait("AC", "009-35")]
    public async Task TriggerDailyBriefing_WithoutUsers_ReturnsTheShortShape()
    {
        _fakes.Briefing.Run = new DailyBriefingRunResult(0, 0, 0, "07:30", true);
        var host = Host();

        var (status, body) = await host.Post("/admin/trigger-daily-briefing");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            """{"status":"sucesso","mensagem":"Nenhum usuario configurado para 07:30","usuarios_processados":0,"enviados_sucesso":0,"horario":"07:30"}""");
    }

    [Theory]
    [InlineData("/admin/trigger-monthly-reports-inicio", "INICIO_MES", MonthlyMoment.InicioMes)]
    [InlineData("/admin/trigger-monthly-reports-fim", "FIM_MES", MonthlyMoment.FimMes)]
    [Trait("AC", "009-35")]
    public async Task TriggerMonthly_ReturnsTheReportRun_WithTheFlaskKeysAndOrder(string path, string moment, MonthlyMoment expected)
    {
        var host = Host();

        var (status, body) = await host.Post(path);

        status.ShouldBe(HttpStatusCode.OK);
        _fakes.Monthly.RunMoment.ShouldBe(expected);
        body.ShouldBe(
            $$"""{"status":"sucesso","mensagem":"Processamento de relatórios mensais ({{moment}}) concluído","momento_envio":"{{moment}}","horario_processamento":"2026-10-01 08:00:00","total_usuarios":3,"enviados_sucesso":1,"enviados_erro":1,"usuarios_processados":[{"usuario_id":1,"nome":"Ana","hora_configurada":"08:00:00","status":"enviado","mensagem":"Relatório enviado com sucesso","grafico_enviado":true},{"usuario_id":2,"nome":"Bia","status":"sem_dados","mensagem":"Nenhuma transação no período"},{"usuario_id":3,"nome":"Caio","status":"erro","mensagem":"Falha ao enviar mensagem de texto"}],"erros":[{"usuario_id":3,"nome":"Caio","erro":"Falha ao enviar mensagem de texto"}],"duracao_segundos":1.5}""");
    }

    [Fact]
    [Trait("AC", "009-35")]
    public async Task TriggerMonthly_GeneralError_UsesTheTipoShape()
    {
        _fakes.Monthly.Run = new MonthlyReportRunResult(
            "FIM_MES", "2026-10-31 23:00:00", 0, 0, 0, [], [new MonthlyReportErrorEntry(null, null, "erro_geral", "Erro geral no processamento")], 0.25);
        var host = Host();

        var (_, body) = await host.Post("/admin/trigger-monthly-reports-fim");

        body.ShouldContain("\"erros\":[{\"tipo\":\"erro_geral\",\"erro\":\"Erro geral no processamento\"}]");
        body.ShouldContain("\"usuarios_processados\":[]");
    }

    [Theory]
    [InlineData("/admin/trigger-bills-notifications", "Erro ao processar notificações de contas a vencer. Tente novamente.")]
    [InlineData("/admin/trigger-daily-briefing", "Erro ao processar os resumos matinais. Tente novamente.")]
    [InlineData("/admin/trigger-monthly-reports-inicio", "Erro ao processar os relatórios mensais. Tente novamente.")]
    [InlineData("/admin/trigger-monthly-reports-fim", "Erro ao processar os relatórios mensais. Tente novamente.")]
    [Trait("AC", "009-30")]
    public async Task Trigger_PortException_Is500FixedText_WithoutTheExceptionMessage(string path, string expected)
    {
        var failure = new InvalidOperationException(DependentFakes.Sentinel);
        _fakes.Bills.Failure = failure;
        _fakes.Briefing.Failure = failure;
        _fakes.Monthly.Failure = failure;
        var host = Host();

        var (status, body) = await host.Post(path);

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(AdminHttp.Error(expected));
        body.ShouldNotContain(DependentFakes.Sentinel);
        _logs.All.ShouldNotContain(DependentFakes.Sentinel);
    }

    // ---- 503 sem implementação --------------------------------------------------------------------------------------

    [Theory]
    [InlineData("/admin/run-motor-agendamentos")]
    [InlineData("/admin/trigger-bills-notifications")]
    [InlineData("/admin/trigger-daily-briefing")]
    [InlineData("/admin/trigger-monthly-reports-inicio")]
    [InlineData("/admin/trigger-monthly-reports-fim")]
    [InlineData("/admin/test-monthly-report/5")]
    [InlineData("/admin/test-daily-briefing")]
    [Trait("AC", "009-39")]
    public async Task WithTheUnavailablePorts_ValidKey_Is503(string path)
    {
        var host = Host(DependentFakes.InstallUnavailable);

        var (status, body) = await host.Post(path, "{}");

        status.ShouldBe(HttpStatusCode.ServiceUnavailable);
        body.ShouldBe(AdminHttp.Error("Funcionalidade ainda não disponível"));
    }

    [Fact]
    [Trait("AC", "009-39")]
    public async Task TheRealPortsOfThe013_PrevailOverTheUnavailableDefaults()
    {
        // Sem fakes: o que o contêiner resolve é o que a 013 registrou (JobsModule), não os Unavailable* da 009c.
        var host = new SecurityTestHost(redis.RedisUrl, redis.ConnectionString + ",allowAdmin=true");
        _hosts.Add(host);
        using var scope = host.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IScheduleMotor>().GetType().Name.ShouldNotStartWith("Unavailable");
        scope.ServiceProvider.GetRequiredService<IBillsNotificationJob>().GetType().Name.ShouldNotStartWith("Unavailable");
        scope.ServiceProvider.GetRequiredService<IDailyBriefingJob>().GetType().Name.ShouldNotStartWith("Unavailable");
        scope.ServiceProvider.GetRequiredService<IMonthlyReportJob>().GetType().Name.ShouldNotStartWith("Unavailable");
    }

    // ---- 31: test-notification --------------------------------------------------------------------------------------

    [Fact]
    [Trait("AC", "009-36")]
    public async Task TestNotification_UnknownUser_Is404_EvenWithAnInvalidType()
    {
        var host = Host();

        var (status, body) = await host.Post("/admin/test-notification", """{"tipo":"x","usuario_id":99}""");

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(AdminHttp.Error("Usuário não encontrado"));
    }

    [Fact]
    [Trait("AC", "009-36")]
    public async Task TestNotification_InvalidType_Is400()
    {
        var host = Host();

        var (status, body) = await host.Post("/admin/test-notification", """{"tipo":"x","usuario_id":7}""");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(AdminHttp.Error("Tipo inválido"));
        _fakes.Bot.Sent.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "009-36")]
    public async Task TestNotification_AgendaWithoutGoogle_Is400()
    {
        _fakes.Google.Connected = false;
        var host = Host();

        var (status, body) = await host.Post("/admin/test-notification", """{"tipo":"agenda","usuario_id":7}""");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(AdminHttp.Error("Usuário não conectou Google Calendar"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData(null)]
    [InlineData("quebrado")]
    [Trait("AC", "009-36")]
    public async Task TestNotification_NoBodyOrInvalidBody_UsesAgendaAndUserOne(string? json)
    {
        _fakes.Calendar.Titles.AddRange(["Reunião", "Dentista"]);
        var host = Host();

        var (status, body) = await host.Post("/admin/test-notification", json);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            """{"status":"sucesso","mensagem":"Notificação de teste enviada para Admin","tipo":"agenda","usuario_id":1,"numero_whatsapp":"5511000000001"}""");
        _fakes.Bot.Sent.ShouldBe([("5511000000001", "🧪 *TESTE - Agenda de Hoje*\n\n📅 2 evento(s) encontrado(s)\n\n1. Reunião\n2. Dentista\n")]);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo")));
        _fakes.Calendar.RequestedDay.ShouldBe(today);
    }

    [Fact]
    [Trait("AC", "009-36")]
    public async Task TestNotification_Bills_ListsAtMostFive_FromTomorrowsDayOfMonth()
    {
        for (var i = 1; i <= 7; i++)
        {
            _fakes.TestData.Bills.Add(new TestBill($"Conta {i}", i * 100.5m));
        }

        var host = Host();

        var (status, body) = await host.Post("/admin/test-notification", """{"tipo":"contas","usuario_id":7}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            """{"status":"sucesso","mensagem":"Notificação de teste enviada para Ana","tipo":"contas","usuario_id":7,"numero_whatsapp":"5511999990007"}""");
        var message = _fakes.Bot.Sent.Single().Message;
        message.ShouldStartWith("🧪 *TESTE - Contas de Amanhã*\n\n💰 5 conta(s) encontrada(s)\n\n1. Conta 1: R$ 100,50\n");
        message.ShouldContain("5. Conta 5: R$ 502,50\n");
        message.ShouldNotContain("Conta 6");
        var tomorrow = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"))).AddDays(1);
        _fakes.TestData.BillsDay.ShouldBe(tomorrow.Day);
    }

    [Fact]
    [Trait("AC", "009-36")]
    public async Task TestNotification_BotFalse_Is500()
    {
        _fakes.Bot.Result = false;
        var host = Host();

        var (status, body) = await host.Post("/admin/test-notification", """{"tipo":"contas","usuario_id":7}""");

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(AdminHttp.Error("Falha ao enviar notificação"));
    }

    [Theory]
    [InlineData("""{"usuario_id":"7"}""")]
    [InlineData("""{"usuario_id":true}""")]
    [InlineData("""{"usuario_id":7.5}""")]
    public async Task TestNotification_UserIdOfAnotherType_Is400(string json)
    {
        var host = Host();

        var (status, body) = await host.Post("/admin/test-notification", json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(AdminHttp.Error("Campo 'usuario_id' deve ser um número inteiro"));
    }

    [Theory]
    [InlineData("""{"usuario_id":0}""")]
    [InlineData("""{"usuario_id":-4}""")]
    [InlineData("""{"usuario_id":99999999999}""")]
    public async Task TestNotification_UserIdThatCannotExist_Is404(string json)
    {
        var host = Host();

        var (status, body) = await host.Post("/admin/test-notification", json);

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(AdminHttp.Error("Usuário não encontrado"));
    }

    [Fact]
    [Trait("AC", "009-30")]
    public async Task TestNotification_Exception_Is500FixedText_WithoutTheExceptionMessage()
    {
        _fakes.TestData.Failure = new InvalidOperationException(DependentFakes.Sentinel);
        var host = Host();

        var (status, body) = await host.Post("/admin/test-notification", """{"usuario_id":7}""");

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(AdminHttp.Error("Erro ao enviar a notificação de teste. Tente novamente."));
        body.ShouldNotContain(DependentFakes.Sentinel);
        _logs.All.ShouldNotContain(DependentFakes.Sentinel);
    }

    // ---- 32: test-monthly-report ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("?momento=XX")]
    [InlineData("?momento=")]
    [InlineData("?momento=inicio_mes")]
    [Trait("AC", "009-37")]
    public async Task TestMonthly_InvalidMoment_Is400_AndThePortIsNotCalled(string query)
    {
        var host = Host();

        var (status, body) = await host.Post("/admin/test-monthly-report/5" + query);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(AdminHttp.Error("Parâmetro 'momento' deve ser 'INICIO_MES' ou 'FIM_MES'"));
        _fakes.Monthly.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData("", MonthlyMoment.InicioMes)]
    [InlineData("?momento=INICIO_MES", MonthlyMoment.InicioMes)]
    [InlineData("?momento=FIM_MES", MonthlyMoment.FimMes)]
    [InlineData("?momento=FIM_MES&momento=XX", MonthlyMoment.FimMes)]
    public async Task TestMonthly_PassesTheUserAndTheMomentToThePort(string query, MonthlyMoment expected)
    {
        var host = Host();

        await host.Post("/admin/test-monthly-report/5" + query);

        _fakes.Monthly.ManualCall.ShouldBe((5, expected));
    }

    [Fact]
    [Trait("AC", "009-37")]
    public async Task TestMonthly_Success_Is200WithTheFlaskKeysAndData()
    {
        _fakes.Monthly.Manual = new ManualReportResult(
            true, null, 5, "Ana", true, false,
            new MonthlyReportData(
                9, 2026, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30),
                new ReportTotals(-300.5, 1000, 1300.5, 4),
                [new ReportCategory("Mercado", -300.5, 100)],
                new ReportComparison(-300.5, -100, -200.5, 0),
                [new ReportPot("Lazer", 200, 50, 150, 25)],
                new ReportAccounts(2, -300.5, 1, 80)));
        var host = Host();

        var (status, body) = await host.Post("/admin/test-monthly-report/5?momento=INICIO_MES");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            """{"status":"sucesso","mensagem":"Relatório mensal enviado com sucesso","sucesso":true,"usuario_id":5,"nome":"Ana","mensagem_enviada":true,"grafico_enviado":false,"dados":{"mes":9,"ano":2026,"periodo":{"inicio":"Tue, 01 Sep 2026 00:00:00 GMT","fim":"Wed, 30 Sep 2026 00:00:00 GMT"},"totais":{"total_despesas":-300.5,"total_rendas":1000,"saldo_periodo":1300.5,"total_transacoes":4},"top_categorias":[{"categoria":"Mercado","valor":-300.5,"percentual":100}],"comparacao":{"mes_atual":-300.5,"mes_anterior":-100,"variacao_valor":-200.5,"variacao_percentual":0},"potes":[{"nome":"Lazer","limite":200,"usado":50,"saldo":150,"percentual":25}],"contas":{"pagas":2,"valor_pago":-300.5,"pendentes":1,"valor_pendente":80}}}""");
    }

    [Theory]
    [InlineData("Usuário não encontrado")]
    [InlineData("Falha ao enviar mensagem de texto")]
    [Trait("AC", "009-37")]
    public async Task TestMonthly_BusinessFailure_Is400_WithTheGenericMessageAndTheReasonInErro(string reason)
    {
        _fakes.Monthly.Manual = new ManualReportResult(false, reason, 5, null, false, false, null);
        var host = Host();

        var (status, body) = await host.Post("/admin/test-monthly-report/5");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe($$"""{"status":"erro","mensagem":"Erro ao enviar relatório","sucesso":false,"erro":"{{reason}}"}""");
    }

    [Fact]
    [Trait("AC", "009-37")]
    public async Task TestMonthly_NoTransactions_Is400_WithTheDataIncluded()
    {
        _fakes.Monthly.Manual = new ManualReportResult(
            false, "Nenhuma transação encontrada no período", 5, "Ana", false, false,
            new MonthlyReportData(
                9, 2026, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30),
                new ReportTotals(0, 0, 0, 0), [], new ReportComparison(0, 0, 0, 0), [], new ReportAccounts(0, 0, 0, 0)));
        var host = Host();

        var (status, body) = await host.Post("/admin/test-monthly-report/5");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldStartWith(
            """{"status":"erro","mensagem":"Erro ao enviar relatório","sucesso":false,"erro":"Nenhuma transação encontrada no período","dados":{"mes":9,"ano":2026,""");
        body.ShouldContain("\"top_categorias\":[]");
    }

    [Fact]
    [Trait("AC", "009-30")]
    public async Task TestMonthly_Exception_Is500FixedText()
    {
        _fakes.Monthly.Failure = new InvalidOperationException(DependentFakes.Sentinel);
        var host = Host();

        var (status, body) = await host.Post("/admin/test-monthly-report/5");

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(AdminHttp.Error("Erro ao enviar o relatório mensal. Tente novamente."));
        body.ShouldNotContain(DependentFakes.Sentinel);
        _logs.All.ShouldNotContain(DependentFakes.Sentinel);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("2147483648")]
    public async Task TestMonthly_NonNumericOrOutOfRangeId_Is404Html_BeforeTheAuthentication(string id)
    {
        var host = Host();

        foreach (var key in new string?[] { AdminHttp.Valid, null })
        {
            var (response, body) = await host.Call(
                HttpMethod.Post,
                $"/admin/test-monthly-report/{id}",
                "{}",
                key is null ? null : new Dictionary<string, string> { ["X-API-KEY"] = AdminHttp.ApiKey });

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
            body.ShouldContain("<h1>Not Found</h1>");
        }

        _fakes.Monthly.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task TestMonthly_OtherMethods_Are405ForNumericIds_And404ForTheRest()
    {
        var host = Host();

        (await host.Admin(HttpMethod.Get, "/admin/test-monthly-report/5", key: null)).Status.ShouldBe(HttpStatusCode.MethodNotAllowed);
        (await host.Admin(HttpMethod.Get, "/admin/test-monthly-report/abc", key: null)).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- 33: test-daily-briefing ------------------------------------------------------------------------------------

    [Fact]
    [Trait("AC", "009-38")]
    public async Task TestBriefing_UnknownUser_Is404_WithTheId()
    {
        var host = Host();

        var (status, body) = await host.Post("/admin/test-daily-briefing", """{"usuario_id":99}""");

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(AdminHttp.Error("Usuário 99 não encontrado"));
        _fakes.Briefing.Calls.ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "009-38")]
    public async Task TestBriefing_UserWithoutWhatsapp_Is400()
    {
        _fakes.TestData.Users[8] = new TestUser("Sem", string.Empty);
        var host = Host();

        var (status, body) = await host.Post("/admin/test-daily-briefing", """{"usuario_id":8}""");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(AdminHttp.Error("Usuário 8 não tem número de WhatsApp cadastrado"));
        _fakes.Briefing.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData(DailyBriefingTestOutcome.PrepareFailed, "Erro ao preparar dados do briefing")]
    [InlineData(DailyBriefingTestOutcome.EmptyMessage, "Falha ao gerar mensagem")]
    [InlineData(DailyBriefingTestOutcome.SendFailed, "Falha ao enviar WhatsApp")]
    [Trait("AC", "009-38")]
    public async Task TestBriefing_PortFailures_Are500(DailyBriefingTestOutcome outcome, string message)
    {
        _fakes.Briefing.Test = new DailyBriefingTestResult(outcome, 0, null);
        var host = Host();

        var (status, body) = await host.Post("/admin/test-daily-briefing", """{"usuario_id":7}""");

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(AdminHttp.Error(message));
    }

    [Fact]
    [Trait("AC", "009-38")]
    public async Task TestBriefing_Success_Is200_WithTheFullShortMessageAsPreview()
    {
        var host = Host();

        var (status, body) = await host.Post("/admin/test-daily-briefing", """{"usuario_id":7}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            """{"status":"sucesso","mensagem":"Resumo matinal enviado com sucesso","usuario_id":7,"numero_whatsapp":"5511999990007","total_eventos":2,"preview_mensagem":"Bom dia!"}""");
        _fakes.Briefing.TestedUser.ShouldBe(7);
    }

    [Fact]
    [Trait("AC", "009-38")]
    public async Task TestBriefing_LongMessage_IsCutAt200_WithEllipsis()
    {
        _fakes.Briefing.Test = new DailyBriefingTestResult(DailyBriefingTestOutcome.Sent, 1, new string('x', 250));
        var host = Host();

        var (_, body) = await host.Post("/admin/test-daily-briefing", "{}");

        body.ShouldContain($"\"preview_mensagem\":\"{new string('x', 200)}...\"");
        _fakes.Briefing.TestedUser.ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "009-30")]
    public async Task TestBriefing_Exception_Is500FixedText()
    {
        _fakes.Briefing.Failure = new InvalidOperationException(DependentFakes.Sentinel);
        var host = Host();

        var (status, body) = await host.Post("/admin/test-daily-briefing", """{"usuario_id":7}""");

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(AdminHttp.Error("Erro ao enviar o resumo matinal de teste. Tente novamente."));
        body.ShouldNotContain(DependentFakes.Sentinel);
        _logs.All.ShouldNotContain(DependentFakes.Sentinel);
    }
}
