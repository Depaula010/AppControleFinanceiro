using System.Net;
using System.Text.Json;
using MeuSecretario.IntegrationTests.Automation;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.IntegrationTests.Transactions;

namespace MeuSecretario.IntegrationTests.Whatsapp;

/// <summary>Pagamentos pela rota real (spec 012b): quitação de conta fixa e despesa avulsa gravam pelas portas da 004.</summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class PaymentFlowEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private WhatsAppEnv _env = default!;
    private int _user;
    private int _account;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _env = new WhatsAppEnv(postgres);
        _user = await _env.SeedUserAsync();
        _account = await _env.Data.InsertAccountAsync(_user, "Nubank");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string> SendAsync(string text)
    {
        var (status, body) = await _env.PostJsonAsync(new { texto = text, numero_remetente = WhatsAppEnv.Number });
        status.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(body).RootElement.GetProperty("resposta").GetString()!;
    }

    [Fact]
    [Trait("AC", "012-201")]
    public async Task FixedBill_IsSettledWithTheInformedValue_OnTheScheduleAccount()
    {
        var other = await _env.Data.InsertAccountAsync(_user, "Carteira", "Dinheiro");
        await new AutomationEnv(postgres).InsertScheduleAsync(_user, other, "Internet", amount: 100m);
        _env.Llm.Then("""{"itens": ["internet"], "valor_total": 130.0, "trigger_word": "paguei"}""");

        var reply = await SendAsync("paguei 130 da internet");

        reply.ShouldContain("✅ *CONTA QUITADA* ✅");
        reply.ShouldContain("💡 Valor previsto era R$ 100,00 (+R$ 30,00)");
        var rows = await _env.QueryAsync<(string descricao, decimal valor, int conta_id, string tipo_transacao)>(
            "SELECT descricao, valor, conta_id, tipo_transacao FROM transacoes");
        rows.Count.ShouldBe(1);
        rows[0].ShouldBe(("Internet", -130m, other, "Despesa"));
    }

    [Fact]
    [Trait("AC", "012-203")]
    public async Task LooseExpense_IsRegisteredWithCategoryAndDefaultAccount()
    {
        _env.Llm.Then("""{"itens": ["pizza"], "valor_total": 25.0, "trigger_word": "paguei"}""").Then(TransactionsTestData.ExpenseSubcategory.J());

        var reply = await SendAsync("paguei 25 de pizza");

        reply.ShouldContain("💸 *DESPESA REGISTRADA* 💸");
        reply.ShouldContain("💳 Nubank");
        (await _env.ScalarAsync<decimal>("SELECT valor FROM transacoes WHERE conta_id = @account", new { account = _account })).ShouldBe(-25m);
    }

    [Fact]
    [Trait("AC", "012-205")]
    public async Task NoValue_WritesNothingAndAsksForIt()
    {
        _env.Llm.Then("""{"itens": ["pizza"], "valor_total": null, "trigger_word": "paguei"}""");

        (await SendAsync("paguei pizza")).ShouldBe("🤔 Para processar 'pizza', preciso do valor.\n\nExemplo: *paguei pizza 150*");
        (await _env.ScalarAsync<int>("SELECT COUNT(*) FROM transacoes")).ShouldBe(0);
    }
}
