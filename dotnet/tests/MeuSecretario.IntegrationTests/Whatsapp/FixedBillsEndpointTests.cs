using System.Net;
using System.Text;
using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Jobs;
using MeuSecretario.Domain.Jobs;
using MeuSecretario.IntegrationTests.Automation;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.IntegrationTests.Transactions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeuSecretario.IntegrationTests.Whatsapp;

/// <summary>
/// 012d pela rota real: contas fixas, vencimentos, contas atrasadas e a resposta ao check-in noturno com o responsável de verdade
/// (<c>CheckinResponder</c> sobre o motor de agendamentos da 013), Gemini e Redis falsos e o banco do contêiner.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class FixedBillsEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Number = "5531999990001";

    private readonly ScriptedGemini _llm = new();
    private readonly PatternCache _cache = new();
    private AutomationEnv _env = default!;
    private WebApplicationFactory<Program> _factory = default!;
    private int _user;
    private int _account;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _env = new AutomationEnv(postgres);
        _user = await _env.SeedUserAsync(Number);
        _account = await _env.Data.InsertAccountAsync(_user, "Nubank");
        _factory = postgres.Api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILlmClient>();
            services.AddSingleton<ILlmClient>(_llm);
            services.RemoveAll<IBotClient>();
            services.AddSingleton<IBotClient>(new RecordingBot());
            services.RemoveAll<ICache>();
            services.AddSingleton<ICache>(_cache);
            services.RemoveAll<IDatabaseHealth>();
            services.AddSingleton<IDatabaseHealth>(new ToggleDatabaseHealth());
        }));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private DateOnly Today => _factory.Services.GetRequiredService<IClock>().Today;

    private async Task<string> SendAsync(string text)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/webhook-whatsapp", UriKind.Relative))
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { texto = text, numero_remetente = Number }))),
        };
        request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
        request.Headers.TryAddWithoutValidation("x-api-key", WhatsAppEnv.ApiKey);
        using var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("resposta").GetString()!;
    }

    private async Task StartCheckinAsync(params CheckinBill[] bills)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<NightlyCheckinService>().CreateSessionAsync(Number, bills, CancellationToken.None);
    }

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

        reply.ShouldStartWith("📋 *CONTAS FIXAS PENDENTES - ");
        reply.ShouldContain("Aluguel");
        reply.ShouldContain("R$ 1.500,00");
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

        reply.ShouldContain("*CONTAS QUE VENCEM HOJE*");
        reply.ShouldContain($"• Internet - R$ 99,90 (dia {Today.Day})");
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
        var existing = await _env.Data.InsertTransactionAsync(_user, _account, valor: -1500m, data: Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), descricao: "Aluguel");
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
}
