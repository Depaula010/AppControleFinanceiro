using System.Net;
using System.Text.Json;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.IntegrationTests.Transactions;

namespace MeuSecretario.IntegrationTests.Whatsapp;

/// <summary>Fluxo completo pela rota real (spec 012a): "gastei 50" gera a pendência; "OK" grava e responde o feedback.</summary>
[Trait("Category", "Docker")]
public class TransactionFlowEndpointTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private WhatsAppEnv _env = default!;
    private int _user;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _env = new WhatsAppEnv(postgres);
        _user = await _env.SeedUserAsync();
    }

    // Solta o pool de conexões do host do teste: sem isso a suíte inteira passa do limite de conexões do contêiner.
    public async Task DisposeAsync() => await _env.Factory.DisposeAsync();

    private async Task<string> SendAsync(string text)
    {
        var (status, body) = await _env.PostJsonAsync(new { texto = text, numero_remetente = WhatsAppEnv.Number + "@s.whatsapp.net" });
        status.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(body).RootElement.GetProperty("resposta").GetString()!;
    }

    [Fact]
    [Trait("AC", "012-113")]
    public async Task Expense_IsPendingThenSavedWithFeedback()
    {
        var account = await _env.Data.InsertAccountAsync(_user, "Nubank");
        _env.Llm.Then("""{"intent": "Despesa"}""")
            .Then("""{"valor_decimal": 50.0, "descricao_bruta": "padaria"}""")
            .Then(TransactionsTestData.ExpenseSubcategory.J());

        var confirmation = await SendAsync("gastei 50 na padaria");
        confirmation.ShouldContain("CONFIRME SUA TRANSAÇÃO");
        confirmation.ShouldContain("Nubank (Débito)");
        _env.Cache.Values.Keys.ShouldContain("last_pending:" + WhatsAppEnv.Number);
        (await _env.ScalarAsync<int>("SELECT COUNT(*) FROM transacoes")).ShouldBe(0);

        // pendência sem descricao_final (sem agendamento): o OK grava com a descrição bruta da mensagem (desvio 150).
        var saved = await SendAsync("OK");

        saved.ShouldContain("✅ Transação Salva!");
        saved.ShouldContain("📝 padaria");
        (await _env.ScalarAsync<int>("SELECT COUNT(*) FROM transacoes")).ShouldBe(1);
        (await _env.ScalarAsync<string>("SELECT descricao FROM transacoes")).ShouldBe("padaria");
        (await _env.ScalarAsync<decimal>("SELECT valor FROM transacoes")).ShouldBe(-50m);
        _env.Cache.Values.ShouldBeEmpty();
        _ = account;
    }

    [Fact]
    [Trait("AC", "012-110")]
    public async Task ScheduledExpense_IsSavedOnOk_WithTheScheduleDescription()
    {
        var account = await _env.Data.InsertAccountAsync(_user, "Nubank");
        await new MeuSecretario.IntegrationTests.Automation.AutomationEnv(postgres).InsertScheduleAsync(_user, account, "Conta de Luz", amount: 120.5m);
        _env.Llm.Then("""{"intent": "Despesa"}""").Then("""{"valor_decimal": null, "descricao_bruta": "luz"}""");

        var confirmation = await SendAsync("conta de luz");
        confirmation.ShouldContain("R$ 120,50");

        var saved = await SendAsync("ok");

        saved.ShouldContain("✅ Transação Salva!");
        saved.ShouldContain("📝 Conta de Luz");
        saved.ShouldContain("💵 R$ 120,50 (Nubank)");
        saved.ShouldContain("🏦 Saldo Nubank:");
        (await _env.ScalarAsync<decimal>("SELECT valor FROM transacoes")).ShouldBe(-120.5m);
        _env.Cache.Values.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "012-112")]
    public async Task Cancel_DropsThePendingAndWritesNothing()
    {
        await _env.Data.InsertAccountAsync(_user, "Nubank");
        _env.Llm.Then("""{"intent": "Despesa"}""")
            .Then("""{"valor_decimal": 50.0, "descricao_bruta": "padaria"}""")
            .Then(TransactionsTestData.ExpenseSubcategory.J());
        await SendAsync("gastei 50 na padaria");

        (await SendAsync("CANCELAR")).ShouldBe("❌ Transação cancelada.");

        _env.Cache.Values.ShouldBeEmpty();
        (await _env.ScalarAsync<int>("SELECT COUNT(*) FROM transacoes")).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "012-120")]
    public async Task Transfer_AsksConfirmationThenPostsTheTransferPair()
    {
        var nubank = await _env.Data.Accounts.InsertAccountAsync(_user, "Nubank", "Conta Corrente", saldoInicial: 500m);
        var wallet = await _env.Data.Accounts.InsertAccountAsync(_user, "Carteira", "Dinheiro", saldoInicial: 10m);
        _env.Llm.Then("""{"intent": "Transferência"}""")
            .Then("""{"valor_decimal": 100.0, "conta_origem": "Nubank", "conta_destino": "Carteira"}""");

        var confirmation = await SendAsync("transferir 100 do nubank para a carteira");
        confirmation.ShouldContain("Transferência a confirmar");
        confirmation.ShouldContain("📤 De: Nubank");
        (await _env.ScalarAsync<int>("SELECT COUNT(*) FROM transacoes")).ShouldBe(0);

        var done = await SendAsync("confirmar");

        done.ShouldBe("✅ *Transferência realizada!*\n\n💵 Valor: R$ 100,00\n📤 De: Nubank\n📥 Para: Carteira");
        (await _env.ScalarAsync<int>("SELECT COUNT(*) FROM transacoes")).ShouldBe(2);
        (await _env.ScalarAsync<int>($"SELECT COUNT(*) FROM transacoes WHERE conta_id = {nubank} AND valor = -100")).ShouldBe(1);
        (await _env.ScalarAsync<int>($"SELECT COUNT(*) FROM transacoes WHERE conta_id = {wallet} AND valor = 100")).ShouldBe(1);
        _env.Cache.Values.ShouldBeEmpty();
    }
}
