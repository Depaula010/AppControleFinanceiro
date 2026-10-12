using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.IntegrationTests.Transactions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeuSecretario.IntegrationTests.Whatsapp;

/// <summary>Bot que guarda o PNG e a legenda recebidos.</summary>
internal sealed class ImageCapturingBot : IBotClient
{
    public List<(string Number, byte[] Image, string? Caption)> Images { get; } = [];

    public Task<bool> SendTextAsync(string number, string message, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<bool> SendImageAsync(string number, ReadOnlyMemory<byte> image, string? caption, CancellationToken cancellationToken)
    {
        Images.Add((number, image.ToArray(), caption));
        return Task.FromResult(true);
    }
}

/// <summary>Fiação de <c>Comparação Mensal</c> e <c>Gráfico de Gastos</c> pela rota real (DI, banco, renderizador ScottPlot e bot).</summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class WhatsAppAnalyticsRouteTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private WhatsAppEnv _env = default!;
    private ImageCapturingBot _bot = default!;
    private WebApplicationFactory<Program> _factory = default!;
    private int _user;
    private int _account;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _env = new WhatsAppEnv(postgres);
        _bot = new ImageCapturingBot();
        _factory = _env.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IBotClient>();
            services.AddSingleton<IBotClient>(_bot);
        }));
        _user = await _env.SeedUserAsync();
        _account = await _env.Data.InsertAccountAsync(_user, "Nubank");
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task<string> SendAsync(string text, params string[] geminiReplies)
    {
        _env.Llm.Reset();
        foreach (var reply in geminiReplies)
        {
            _env.Llm.Then(reply);
        }

        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/webhook-whatsapp", UriKind.Relative));
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { texto = text, numero_remetente = WhatsAppEnv.Number + "@s.whatsapp.net" }), Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("x-api-key", WhatsAppEnv.ApiKey);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("resposta").GetString()!;
    }

    private string Day(int daysAgo) => _env.Today.AddDays(-daysAgo).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    [Trait("AC", "012-702")]
    public async Task MonthlyComparison_ReadsTheRealTables()
    {
        var today = _env.Today;
        await _env.Data.InsertTransactionAsync(_user, _account, valor: -100m, data: today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var reply = await SendAsync("comparar com o mês passado", """{"intent": "Comparação Mensal"}""");

        reply.ShouldStartWith("📊 *Comparação: ");
        reply.ShouldContain("R$ 100,00"); // desvio 232: valor absoluto em pt-BR
        reply.ShouldContain("*Maiores mudanças por categoria:*");
    }

    [Fact]
    [Trait("AC", "012-703")]
    public async Task Chart_Pie_IsSentAsAPngWithTheCaption()
    {
        await _env.Data.InsertTransactionAsync(_user, _account, valor: -100m, data: Day(2));
        await _env.Data.InsertTransactionAsync(_user, _account, valor: -50m, data: Day(3));

        var reply = await SendAsync("gráfico dos meus gastos", """{"intent": "Gráfico de Gastos"}""", """{"tipo_grafico": "pizza", "periodo_dias": 30}""");

        reply.ShouldBe("✅ 📊 Gastos por Categoria - Últimos 30 dias");
        var image = _bot.Images.ShouldHaveSingleItem();
        image.Number.ShouldBe(WhatsAppEnv.Number);
        image.Caption.ShouldBe("📊 Gastos por Categoria - Últimos 30 dias");
        image.Image.Take(8).ShouldBe(PngSignature);
    }

    [Fact]
    [Trait("AC", "012-703")]
    public async Task Chart_BarsAndLine_AreSent()
    {
        await _env.Data.InsertTransactionAsync(_user, _account, valor: -100m, data: Day(2));
        await _env.Data.InsertTransactionAsync(_user, _account, valor: 800m, tipo: "Renda", data: Day(40), subcategoryId: TransactionsTestData.IncomeSubcategory);

        (await SendAsync("barras", """{"intent": "Gráfico de Gastos"}""", """{"tipo_grafico": "barras", "num_meses": 3}"""))
            .ShouldBe("✅ 📊 Evolução Mensal - Últimos 3 meses");
        (await SendAsync("saldo", """{"intent": "Gráfico de Gastos"}""", """{"tipo_grafico": "linha", "num_meses": 6}"""))
            .ShouldBe("✅ 📈 Evolução do Saldo - Últimos 6 meses");

        _bot.Images.Count.ShouldBe(2);
        _bot.Images.ShouldAllBe(i => i.Image.Take(8).SequenceEqual(PngSignature));
    }

    [Fact]
    [Trait("AC", "012-703")]
    public async Task Chart_WithoutData_AnswersWithoutSendingAnImage()
    {
        var reply = await SendAsync("gráfico", """{"intent": "Gráfico de Gastos"}""", """{"tipo_grafico": "pizza", "periodo_dias": 30}""");

        reply.ShouldBe("❌ Não há dados suficientes para gerar o gráfico no período solicitado.");
        _bot.Images.ShouldBeEmpty();
    }
}
