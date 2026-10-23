using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Jobs;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Domain.Jobs;
using MeuSecretario.Infrastructure.Jobs;
using MeuSecretario.Infrastructure.Whatsapp;
using MeuSecretario.IntegrationTests.Automation;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.IntegrationTests.Transactions;
using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.IntegrationTests.Whatsapp;

/// <summary>
/// 012d com o banco de verdade: contas fixas, vencimentos, contas atrasadas e a resposta ao check-in noturno com o responsável real
/// (<c>CheckinResponder</c> sobre o motor de agendamentos da 013). O pipeline é montado com as classes de Infrastructure sobre a conexão
/// do contêiner (sem subir um host por teste: cada host guarda o próprio pool e o contêiner tem um limite de conexões); a fiação de DI e a
/// rota ficam cobertas pelos testes de rota do WhatsApp e pela resolução de DI abaixo.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class FixedBillsEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Number = "5531999990001";

    // Fixa o dia: os cenários dependem de "hoje" (vence hoje, venceu ontem) e do mês corrente.
    private static readonly DateOnly Today = new(2026, 10, 7);

    private readonly ScriptedGemini _llm = new();
    private readonly PatternCache _cache = new();
    private AutomationEnv _env = default!;
    private HandleWhatsAppMessage _useCase = default!;
    private NightlyCheckinService _checkin = default!;
    private int _user;
    private int _account;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _env = new AutomationEnv(postgres);
        _user = await _env.SeedUserAsync(Number);
        _account = await _env.Data.InsertAccountAsync(_user, "Nubank");

        var clock = new FixedClock(Today);
        var reader = new FixedBillsReader(postgres.Connections);
        var session = new CheckinSessionState(_cache);
        _checkin = new NightlyCheckinService(new CheckinQueries(postgres.Connections), _cache, clock);
        var responder = new CheckinResponder(
            new CheckinDialogStore(_cache, session), new ScheduleExecutor(new ScheduleExecutionRepository(postgres.Connections)), clock,
            new NoDiagnostics());

        _useCase = new HandleWhatsAppMessage(
            new WhatsAppUserLookup(postgres.Connections),
            [new CheckinStep(session, responder)],
            new IntentClassifier(_llm),
            new IntentRouter(
                [
                    new FixedBillsIntent(reader, clock), new AllBillsIntent(reader), new DueTodayIntent(reader, clock),
                    new DueTomorrowIntent(reader, clock), new DueWeekIntent(reader, clock), new OverdueIntent(_checkin, clock),
                ],
                new NoDiagnostics()));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<string> SendAsync(string text) =>
        _useCase.ExecuteAsync(new WebhookInput(text, Number, null), CancellationToken.None);

    private Task<string> StartCheckinAsync(params CheckinBill[] bills) => _checkin.CreateSessionAsync(Number, bills, CancellationToken.None);

    private CheckinBill BillOf(int scheduleId, string description, decimal amount, string kind = "FIXO", int executed = 0, int? total = null, DateOnly? due = null) =>
        new(scheduleId, description, amount, Today.Day, _account, TransactionsTestData.ExpenseSubcategory, _user, "Nubank", "Conta Corrente", "Moradia", "Moradia",
            "Despesa", kind, executed, total, due ?? Today);

    private Task<IReadOnlyList<(string descricao, decimal valor, int? agendamento_id, DateTime data_transacao, string tipo_transacao)>> TransactionsAsync() =>
        _env.Data.QueryAsync<(string descricao, decimal valor, int? agendamento_id, DateTime data_transacao, string tipo_transacao)>(
            "SELECT descricao, valor, agendamento_id, data_transacao, tipo_transacao FROM transacoes ORDER BY id");

    [Fact]
    [Trait("AC", "012-401")]
    public async Task FixedBills_AreListedForTheMonth()
    {
        await _env.InsertScheduleAsync(_user, _account, "Aluguel", day: 5, amount: 1500m);
        _llm.Then("""{"intent": "Consulta Contas Fixas"}""");

        var reply = await SendAsync("minhas contas fixas");

        reply.ShouldStartWith("📋 *CONTAS FIXAS PENDENTES - OUTUBRO/2026* 📋");
        reply.ShouldContain("1. Aluguel\n   💰 R$ 1.500,00\n   📊 ");
        reply.ShouldContain("\n   🔴 ATRASADO\n");
    }

    [Fact]
    [Trait("AC", "012-401")]
    public async Task AllBills_ListsTheCatalog()
    {
        await _env.InsertScheduleAsync(_user, _account, "Feira", periodicity: "SEMANAL", day: 6, amount: 120m);
        _llm.Then("""{"intent": "Consulta Todas Contas"}""");

        var reply = await SendAsync("todas as minhas contas");

        reply.ShouldContain("*🔄 SEMANAL*");
        reply.ShouldContain("toda semana (dia 6)");
    }

    [Fact]
    [Trait("AC", "012-402")]
    public async Task DueToday_ListsTheBillsOfTheDay()
    {
        await _env.InsertScheduleAsync(_user, _account, "Internet", day: Today.Day, amount: 99.9m);
        _llm.Then("""{"intent": "Vencimentos Hoje"}""");

        var reply = await SendAsync("o que vence hoje");

        reply.ShouldContain("*CONTAS QUE VENCEM HOJE* (07/10)");
        reply.ShouldContain("• Internet - R$ 99,90 (dia 7)");
    }

    [Fact]
    [Trait("AC", "012-402")]
    public async Task DueThisWeek_AcrossTheMonthTurn_ListsBothSidesAndTheOpenInvoices()
    {
        var clock = new FixedClock(new DateOnly(2026, 10, 28));
        var card = await _env.Data.InsertCardAsync(_user, "Inter", 10, 2);
        await _env.Data.InsertInvoiceAsync(card, "2026-11-02", "2026-10-26");
        await _env.InsertScheduleAsync(_user, _account, "Condomínio", day: 30, amount: 450m);
        await _env.InsertScheduleAsync(_user, _account, "Aluguel", day: 1, amount: 1500m);
        await _env.InsertScheduleAsync(_user, _account, "Distante", day: 15, amount: 10m);
        var intent = new DueWeekIntent(new FixedBillsReader(postgres.Connections), clock);

        var reply = await intent.HandleAsync(new IntentContext(new WhatsAppUser(_user, "Ana"), Number, "semana", null), CancellationToken.None);

        reply.ShouldContain("*CONTAS QUE VENCEM NOS PRÓXIMOS 7 DIAS* (28/10)");
        reply.ShouldContain("• Condomínio - R$ 450,00 (dia 30)");
        reply.ShouldContain("• Aluguel - R$ 1.500,00 (dia 1)");
        reply.ShouldNotContain("Distante");
        reply.ShouldContain("• Inter - R$ 0,00 (vence 02/11)");
    }

    [Fact]
    [Trait("AC", "012-403")]
    public async Task Overdue_ShowsTheCheckinSnapshotInReadOnlyMode()
    {
        await _env.InsertScheduleAsync(_user, _account, "Aluguel", day: Today.Day, amount: 1500m);
        _llm.Then("""{"intent": "Contas Atrasadas"}""");

        var reply = await SendAsync("tenho conta atrasada");

        reply.ShouldStartWith("📊 *RESUMO FINANCEIRO* 📊");
        reply.ShouldContain("Aluguel - R$ 1.500,00 (Nubank) - Vence hoje");
        _cache.Values.Keys.ShouldNotContain(key => key.StartsWith("nightly_checkin", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AC", "012-403")]
    public async Task Overdue_NothingPending_IsTheCheerfulSentence()
    {
        _llm.Then("""{"intent": "Contas Atrasadas"}""");

        (await SendAsync("contas atrasadas")).ShouldBe("✅ Você não tem contas atrasadas! Tudo em dia! 🎉");
    }

    [Fact]
    [Trait("AC", "012-404")]
    public async Task CheckinReply_Ok_PostsEveryBill_LinkedToTheSchedule_AndEndsTheSession()
    {
        var rent = await _env.InsertScheduleAsync(_user, _account, "Aluguel", amount: 1500m);
        var light = await _env.InsertScheduleAsync(_user, _account, "Luz", amount: 230.45m);
        await StartCheckinAsync(BillOf(rent, "Aluguel", 1500m), BillOf(light, "Luz", 230.45m, due: Today.AddDays(-1)));

        var reply = await SendAsync("ok");

        reply.ShouldBe("✅ *Todas as 2 contas confirmadas!*\n\n• Aluguel\n• Luz\n\n💚 Transações registradas com sucesso!");
        var rows = await TransactionsAsync();
        rows.Select(r => (r.descricao, r.valor, r.agendamento_id, r.tipo_transacao)).ShouldBe(
            [("Aluguel", -1500m, rent, "Despesa"), ("Luz", -230.45m, light, "Despesa")]);
        DateOnly.FromDateTime(rows[0].data_transacao).ShouldBe(Today);
        DateOnly.FromDateTime(rows[1].data_transacao).ShouldBe(Today.AddDays(-1));
        _cache.Values.Keys.ShouldNotContain(key => key.StartsWith("nightly_checkin", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AC", "012-404")]
    public async Task CheckinReply_Partial_PostsOnlyTheNumbersSaid()
    {
        var rent = await _env.InsertScheduleAsync(_user, _account, "Aluguel", amount: 1500m);
        var light = await _env.InsertScheduleAsync(_user, _account, "Luz", amount: 230.45m);
        var water = await _env.InsertScheduleAsync(_user, _account, "Água", amount: 80m);
        await StartCheckinAsync(BillOf(rent, "Aluguel", 1500m), BillOf(light, "Luz", 230.45m), BillOf(water, "Água", 80m));

        var reply = await SendAsync("1 e 3");

        reply.ShouldBe("✅ *2 conta(s) confirmada(s):*\n\n• Aluguel\n• Água\n\n💚 Transações registradas com sucesso!");
        (await TransactionsAsync()).Select(r => r.descricao).ShouldBe(["Aluguel", "Água"]);
    }

    [Fact]
    [Trait("AC", "012-404")]
    public async Task CheckinReply_Deferred_WritesNothing_AndEndsTheSession()
    {
        var rent = await _env.InsertScheduleAsync(_user, _account, "Aluguel", amount: 1500m);
        await StartCheckinAsync(BillOf(rent, "Aluguel", 1500m));

        (await SendAsync("depois")).ShouldStartWith("⏸️ Ok, te lembro amanhã");

        (await TransactionsAsync()).ShouldBeEmpty();
        _cache.Values.Keys.ShouldNotContain(key => key.StartsWith("nightly_checkin", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AC", "012-404")]
    public async Task CheckinReply_AlreadyPostedInTheMonth_IsNotDuplicated_ButCountsAsConfirmed()
    {
        var rent = await _env.InsertScheduleAsync(_user, _account, "Aluguel", amount: 1500m);
        var existing = await _env.Data.InsertTransactionAsync(_user, _account, valor: -1500m, data: "2026-10-07", descricao: "Aluguel");
        await _env.Data.ExecuteAsync("UPDATE transacoes SET agendamento_id = @rent WHERE id = @existing", new { rent, existing });
        await StartCheckinAsync(BillOf(rent, "Aluguel", 1500m));

        var reply = await SendAsync("ok");

        reply.ShouldContain("• Aluguel");
        (await TransactionsAsync()).Count.ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "012-404")]
    public async Task CheckinReply_Installment_AdvancesTheCounter()
    {
        var phone = await _env.InsertScheduleAsync(_user, _account, "Celular", type: "PARCELADO", amount: 150m, total: 3, executed: 1);
        await StartCheckinAsync(BillOf(phone, "Celular", 150m, "PARCELADO", executed: 1, total: 3));

        await SendAsync("ok");

        (await TransactionsAsync()).Select(r => r.descricao).ShouldBe(["Celular (2/3)"]);
        (await _env.Data.ScalarAsync<int>("SELECT parcelas_executadas FROM agendamentos WHERE id = @phone", new { phone })).ShouldBe(2);
    }

    [Fact]
    [Trait("AC", "012-404")]
    public async Task CheckinReply_UnknownSession_ExpiresWithoutWriting()
    {
        await _cache.SetJsonAsync($"nightly_checkin_active:{Number}", "chk00001", TimeSpan.FromHours(1));

        (await SendAsync("ok")).ShouldBe("⏱️ Esta sessão expirou. Aguarde o próximo check-in ou registre manualmente.");

        (await TransactionsAsync()).ShouldBeEmpty();
        _cache.Values.Keys.ShouldNotContain($"nightly_checkin_active:{Number}");
    }

    /// <summary>
    /// Fiação de DI da 012d no host que a API sobe de verdade (o do <c>PostgresFixture</c>, sem abrir outro): o responder real vence o
    /// padrão "não tratada" e as seis intenções entram no despacho.
    /// </summary>
    [Fact]
    [Trait("AC", "012-404")]
    public void DependencyInjection_ResolvesTheRealCheckinResponderAndTheBillsIntents()
    {
        using var scope = postgres.Api.Services.CreateScope();
        var services = scope.ServiceProvider;

        services.GetRequiredService<INightlyCheckinResponder>().ShouldBeOfType<CheckinResponder>();
        services.GetRequiredService<IWhatsAppFixedBillsReader>().ShouldBeOfType<FixedBillsReader>();
        var names = services.GetServices<IWhatsAppIntent>().Select(i => i.Name).ToList();
        foreach (var name in new[] { "Consulta Contas Fixas", "Consulta Todas Contas", "Vencimentos Hoje", "Vencimentos Amanhã", "Vencimentos Essa Semana", "Contas Atrasadas" })
        {
            names.ShouldContain(name);
        }
    }

    private sealed class FixedClock(DateOnly today) : IClock
    {
        public DateTimeOffset UtcNow => new(today.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero);

        public DateTime Now => today.ToDateTime(new TimeOnly(12, 0));

        public DateOnly Today => today;

        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
    }

    private sealed class NoDiagnostics : IWhatsAppDiagnostics, ICheckinDiagnostics
    {
        public void IntentFailed(string intent, string errorType)
        {
        }

        public void ReplyFailed(string errorType)
        {
        }
    }
}
