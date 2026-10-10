using System.Net;
using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure.Database;
using MeuSecretario.Infrastructure.Security;
using MeuSecretario.TestFixtures;
using MeuSecretario.Infrastructure.Whatsapp;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.IntegrationTests.Whatsapp;

/// <summary>012f: <see cref="WhatsAppConfigStore"/> no PostgreSQL real.</summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class WhatsAppConfigStoreTests(PostgresFixture postgres) : IAsyncLifetime
{
    private WhatsAppEnv _env = default!;
    private WhatsAppConfigStore _store = default!;
    private int _user;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _env = new WhatsAppEnv(postgres);
        _user = await _env.SeedUserAsync();
        _store = new WhatsAppConfigStore(new NpgsqlConnectionFactory(postgres.DataSource));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("AC", "012-601")]
    public async Task Notifications_CreateTheRow_AndUpdateOnlyWhatWasSent()
    {
        var created = await _store.GetOrCreateNotificationAsync(_user, CancellationToken.None);
        created.MorningActive.ShouldBeTrue();
        (await _env.ScalarAsync<int>("SELECT count(*)::int FROM notificationconfigs WHERE usuario_id = @_user", new { _user })).ShouldBe(1);

        var morning = await _store.UpdateMorningAsync(_user, false, new TimeOnly(9, 30), CancellationToken.None);
        morning.MorningActive.ShouldBeFalse();
        morning.MorningTime.ShouldBe(new TimeOnly(9, 30));
        morning.CheckinTime.ShouldBe(created.CheckinTime);

        var checkin = await _store.UpdateCheckinAsync(_user, null, new TimeOnly(21, 0), CancellationToken.None);
        checkin.CheckinTime.ShouldBe(new TimeOnly(21, 0));
        checkin.CheckinActive.ShouldBe(created.CheckinActive);
        checkin.MorningActive.ShouldBeFalse();

        var again = await _store.GetOrCreateNotificationAsync(_user, CancellationToken.None);
        again.ShouldBe(checkin);
        (await _env.ScalarAsync<int>("SELECT count(*)::int FROM notificationconfigs WHERE usuario_id = @_user", new { _user })).ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "012-603")]
    public async Task MonthlyReport_CreatesTheDefaults_AndUpdatesPartially()
    {
        var created = await _store.GetOrCreateReportAsync(_user, CancellationToken.None);
        created.ShouldBe(new MeuSecretario.Application.Whatsapp.WhatsAppReportConfig(true, "INICIO_MES", new TimeOnly(8, 0)));

        var updated = await _store.UpdateReportAsync(_user, null, "FIM_MES", new TimeOnly(14, 5, 0), CancellationToken.None);
        updated.ShouldBe(new MeuSecretario.Application.Whatsapp.WhatsAppReportConfig(true, "FIM_MES", new TimeOnly(14, 5)));

        var off = await _store.UpdateReportAsync(_user, false, null, null, CancellationToken.None);
        off.Active.ShouldBeFalse();
        off.Moment.ShouldBe("FIM_MES");

        (await _store.UpdateReportAsync(_user, null, null, null, CancellationToken.None)).ShouldBe(off);
    }

    [Fact]
    [Trait("AC", "012-603")]
    public async Task Location_AndApiKey()
    {
        await _store.UpdateLocationAsync(_user, "Belo Horizonte", "MG", CancellationToken.None);
        await _store.UpdateLocationAsync(_user, "Recife", null, CancellationToken.None);

        (await _env.ScalarAsync<string>("SELECT cidade FROM usuarios WHERE id = @_user", new { _user })).ShouldBe("Recife");
        (await _env.ScalarAsync<string?>("SELECT estado FROM usuarios WHERE id = @_user", new { _user })).ShouldBeNull();

        await _env.QueryAsync<int>("UPDATE usuarios SET api_key_automate = 'chave-ficticia' WHERE id = @_user RETURNING id", new { _user });
        (await _store.GetApiKeyAsync(_user, CancellationToken.None)).ShouldBe(("chave-ficticia", "Ana"));
        (await _store.GetApiKeyAsync(_user + 999, CancellationToken.None)).ShouldBeNull();
    }
}

/// <summary>Fiação das intenções e dos passos da 012e/012f/012h pela rota real (DI, banco e pipeline).</summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class WhatsAppCalendarConfigDriveRouteTests(PostgresFixture postgres) : IAsyncLifetime
{
    private WhatsAppEnv _env = default!;
    private int _user;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _env = new WhatsAppEnv(postgres);
        _user = await _env.SeedUserAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string> SendAsync(string text, params string[] geminiReplies)
    {
        _env.Llm.Reset();
        foreach (var reply in geminiReplies)
        {
            _env.Llm.Then(reply);
        }

        var (status, body) = await _env.PostJsonAsync(new { texto = text, numero_remetente = WhatsAppEnv.Number + "@s.whatsapp.net" });
        status.ShouldBe(HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("status").GetString().ShouldBe("sucesso");
        return document.RootElement.GetProperty("resposta").GetString()!;
    }

    [Fact]
    [Trait("AC", "012-603")]
    public async Task ConfigureLocation_WritesTheUserRow()
    {
        var reply = await SendAsync("estou em Belo Horizonte, mg", """{"intent": "Configurar Localização"}""", """{"cidade": "Belo Horizonte", "estado": "mg"}""");

        reply.ShouldStartWith("✅ 📍 Localização configurada: Belo Horizonte, MG");
        (await _env.ScalarAsync<string>("SELECT cidade FROM usuarios WHERE id = @_user", new { _user })).ShouldBe("Belo Horizonte");
        (await _env.ScalarAsync<string>("SELECT estado FROM usuarios WHERE id = @_user", new { _user })).ShouldBe("MG");
    }

    [Fact]
    [Trait("AC", "012-601")]
    public async Task MorningSummary_IsConfiguredWithoutAskingGemini_ForTheShortcut()
    {
        var reply = await SendAsync("ativar resumo matinal às 8h", """{"intent": "Configurar Notificações"}""");

        reply.ShouldStartWith("✅ Resumo matinal ativado e horário configurado para 08:00");
        (await _env.ScalarAsync<string>("SELECT resumo_matinal_hora::text FROM notificationconfigs WHERE usuario_id = @_user", new { _user })).ShouldBe("08:00:00");
    }

    [Fact]
    [Trait("AC", "012-152")]
    public async Task GeminiBranch_AgendaDiaria_WritesAndAnswersTheStatus()
    {
        var reply = await SendAsync(
            "configurar notificação da agenda", """{"intent": "Configurar Notificações"}""", """{"tipo": "agenda_diaria", "acao": "desativar"}""");

        reply.ShouldBe("✅ Resumo matinal desativado\n\n📱 *Agenda Diária - Status atual:*\n• Ativa: Não\n• Horário: 07:00\n");
        (await _env.ScalarAsync<bool>("SELECT resumo_matinal_ativo FROM notificationconfigs WHERE usuario_id = @_user", new { _user })).ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "012-191")]
    public async Task GeminiBranch_ContasVencer_WritesTheFinancialAlertsColumn()
    {
        var off = await SendAsync(
            "configurar notificação de contas", """{"intent": "Configurar Notificações"}""", """{"tipo": "contas_vencer", "acao": "desativar", "dias_antes": 2}""");

        off.ShouldBe(
            "✅ Alertas financeiros desativados com sucesso\n\n📱 *Contas a Vencer - Status atual:*\n• Ativa: Não\n• Horário: junto com o resumo matinal (07:00)\n");
        (await _env.ScalarAsync<bool>("SELECT alertas_financeiros_ativos FROM notificationconfigs WHERE usuario_id = @_user", new { _user })).ShouldBeFalse();

        var on = await SendAsync(
            "configurar notificação de contas", """{"intent": "Configurar Notificações"}""", """{"tipo": "contas_vencer", "acao": "ativar"}""");

        on.ShouldContain("• Ativa: Sim");
        (await _env.ScalarAsync<bool>("SELECT alertas_financeiros_ativos FROM notificationconfigs WHERE usuario_id = @_user", new { _user })).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "012-190")]
    public async Task MorningSummary_Desativar_TurnsItOff()
    {
        await SendAsync("ativar resumo matinal", """{"intent": "Configurar Notificações"}""");

        var reply = await SendAsync("desativar resumo matinal", """{"intent": "Configurar Notificações"}""");

        reply.ShouldStartWith("✅ Resumo matinal desativado");
        (await _env.ScalarAsync<bool>("SELECT resumo_matinal_ativo FROM notificationconfigs WHERE usuario_id = @_user", new { _user })).ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "012-153")]
    public async Task ApiKey_StoredWithFernet_IsAnsweredDecrypted()
    {
        var clear = "a-chave-em-claro-0123456789012345678901234567890";
        var token = new FernetEncryptionService(AuthFixtures.FernetKey, TimeProvider.System).Encrypt(clear);
        await _env.QueryAsync<int>("UPDATE usuarios SET api_key_automate = @token WHERE id = @_user RETURNING id", new { token, _user });

        var reply = await SendAsync("minha api key", """{"intent": "Solicitar API Key"}""");

        reply.ShouldContain($"`{clear}`");
        reply.ShouldNotContain(token);
    }

    [Fact]
    [Trait("AC", "012-603")]
    public async Task ListAddresses_AndApiKey()
    {
        (await SendAsync("meus endereços", """{"intent": "Listar Endereços"}""")).ShouldStartWith("📍 *Seus Endereços Favoritos*\n\nVocê ainda não tem");

        await _env.QueryAsync<int>("UPDATE usuarios SET api_key_automate = 'chave-ficticia' WHERE id = @_user RETURNING id", new { _user });
        var reply = await SendAsync("minha api key", """{"intent": "Solicitar API Key"}""");
        reply.ShouldContain("`chave-ficticia`");
        reply.ShouldContain("Olá Ana!");
    }

    [Fact]
    [Trait("AC", "012-603")]
    public async Task MonthlyReport_Consult()
    {
        var reply = await SendAsync("como está meu relatório?", """{"intent": "Configurar Relatório Mensal"}""", """{"acao": "consultar"}""");

        reply.ShouldStartWith("📊 *CONFIGURAÇÃO DO RELATÓRIO MENSAL*");
        reply.ShouldContain("Início do mês (dia 1)");
    }

    [Fact]
    [Trait("AC", "012-501")]
    public async Task CreateEvent_ThenConfirm_WithoutGoogle_AnswersTheConnectionReason()
    {
        var created = await SendAsync("criar reunião amanhã 14h", """{"intent": "Criar Evento"}""",
            """{"titulo": "Reunião", "data": "amanha", "hora_inicio": "14:00", "hora_fim": null, "descricao": null, "localizacao": null}""");
        created.ShouldStartWith("📅 *Confirmar Evento?*");
        _env.Cache.Values.Keys.ShouldContain(k => k.StartsWith("pending_event:", StringComparison.Ordinal));

        var confirmed = await SendAsync("sim");

        confirmed.ShouldBe("❌ Erro ao criar evento: Usuário não conectou Google Calendar");
        _env.Cache.Values.Keys.ShouldContain(k => k.StartsWith("pending_event:", StringComparison.Ordinal));

        (await SendAsync("não")).ShouldBe("✅ Evento cancelado com sucesso!");
        _env.Cache.Values.Keys.ShouldNotContain(k => k.StartsWith("pending_event:", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AC", "012-505")]
    public async Task Agenda_WithoutGoogle_TellsToConnect_AndFreeSlotsStillWork()
    {
        var agenda = await SendAsync("minha agenda de hoje", """{"intent": "Consultar Agenda"}""", """{"period_type": "hoje"}""", """{"time_filter": null}""");
        agenda.ShouldMatch("^(📅 \\*Google Calendar não conectado\\*|⚠️ \\*Google Calendar não configurado\\*)[\\s\\S]*");

        var free = await SendAsync("quando estou livre amanhã?", """{"intent": "Horários Livres"}""",
            """{"period_type": "amanha", "duracao_minutos": 60, "contexto": null}""");
        free.ShouldStartWith("🗓️ *Horários Livres*");
    }

    [Fact]
    [Trait("AC", "012-802")]
    public async Task UploadDrive_WithoutMedia_AsksForTheFile_AndWithMediaWithoutDriveScopeAsksToConnect()
    {
        (await SendAsync("salvar no drive pasta Notas", """{"intent": "Upload Drive"}""")).ShouldStartWith("📎 Nenhum arquivo recebido.");

        _env.Llm.Reset();
        _env.Llm.Then("""{"intent": "Upload Drive"}""");
        var (status, body) = await _env.PostJsonAsync(new
        {
            texto = "salvar no drive pasta Notas",
            numero_remetente = WhatsAppEnv.Number,
            media_data = Convert.ToBase64String("arquivo"u8.ToArray()),
            media_type = "image/png",
            media_filename = "nota.png",
        });
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("Google Drive não conectado");
    }

    [Fact]
    public void AllTheIntentsAndStepsOfTheSubdeliveries_AreRegistered()
    {
        using var scope = _env.Factory.Services.CreateScope();
        var router = scope.ServiceProvider.GetRequiredService<MeuSecretario.Application.Whatsapp.IntentRouter>();

        foreach (var name in new[]
                 {
                     "Criar Evento", "Deletar Evento", "Consultar Agenda", "Horários Livres", "Configurar Notificações", "Configurar Localização",
                     "Configurar Relatório Mensal", "Configurar Endereço", "Listar Endereços", "Deletar Endereço", "Solicitar API Key", "Upload Drive",
                 })
        {
            router.Names.ShouldContain(name);
        }
    }
}
