using System.Net;
using System.Text;
using Dapper;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure.Admin;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeuSecretario.IntegrationTests.Admin;

/// <summary><c>AdminTestDataRepository</c> e <c>test-notification tipo=contas</c> contra PostgreSQL real (T27, AC-009-36).</summary>
[Trait("Category", "Docker")]
public sealed class TestDataRepositoryTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    public Task InitializeAsync() => postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<T> Scalar<T>(string sql, object? args = null)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        return (await connection.ExecuteScalarAsync<T>(sql, args))!;
    }

    private async Task Execute(string sql, object? args = null)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(sql, args);
    }

    private async Task<(int UserId, int AccountId, int SubcategoryId)> Seed(string? whatsapp = "5511999990001")
    {
        var userId = await Scalar<int>("INSERT INTO usuarios (nome, numero_whatsapp) VALUES ('Ana', @whatsapp) RETURNING id", new { whatsapp });
        var accountId = await Scalar<int>(
            "INSERT INTO contas (usuario_id, nome_conta, tipo_conta) VALUES (@userId, 'Conta', 'Conta Corrente') RETURNING id", new { userId });
        var subcategoryId = await Scalar<int>("SELECT min(id) FROM subcategoria");
        return (userId, accountId, subcategoryId);
    }

    private Task Schedule(int userId, int accountId, int subcategoryId, string description, decimal? value, int day, bool active = true) =>
        Execute(
            """
            INSERT INTO agendamentos (usuario_id, conta_id, subcategoria_id, descricao, valor_previsto, tipo_agendamento,
                                      periodicidade, data_inicio, dia_execucao, ativo)
            VALUES (@userId, @accountId, @subcategoryId, @description, @value, 'FIXO', 'MENSAL', DATE '2026-01-01', @day, @active)
            """,
            new { userId, accountId, subcategoryId, description, value, day, active });

    [Fact]
    public async Task FindUser_ReturnsNameAndNumber_OrNullWhenMissing()
    {
        var (userId, _, _) = await Seed();
        var repository = new AdminTestDataRepository(postgres.Connections);

        var user = await repository.FindUserAsync(userId, default);

        user.ShouldNotBeNull();
        user.Name.ShouldBe("Ana");
        user.WhatsappNumber.ShouldBe("5511999990001");
        (await repository.FindUserAsync(userId + 1000, default)).ShouldBeNull();
    }

    [Fact]
    public async Task FindUser_WithAnEmptyNumber_ReturnsItEmpty()
    {
        // A coluna é NOT NULL: "sem número" no banco real é a string vazia.
        var (userId, _, _) = await Seed(whatsapp: string.Empty);

        var user = await new AdminTestDataRepository(postgres.Connections).FindUserAsync(userId, default);

        user.ShouldNotBeNull();
        user.WhatsappNumber.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task ListBills_FiltersActiveAndDay_OfTheUser_AndAppliesTheLimit()
    {
        var (userId, accountId, subcategoryId) = await Seed();
        var (otherUser, otherAccount, _) = await Seed("5511999990002");
        for (var i = 1; i <= 7; i++)
        {
            await Schedule(userId, accountId, subcategoryId, $"Conta {i}", i * 10m, 15);
        }

        await Schedule(userId, accountId, subcategoryId, "Inativa", 99m, 15, active: false);
        await Schedule(userId, accountId, subcategoryId, "Outro dia", 99m, 16);
        await Schedule(otherUser, otherAccount, subcategoryId, "De outro usuário", 99m, 15);
        var repository = new AdminTestDataRepository(postgres.Connections);

        var bills = await repository.ListBillsForDayAsync(userId, 15, 5, default);

        bills.Count.ShouldBe(5);
        bills.ShouldAllBe(b => b.Description.StartsWith("Conta ", StringComparison.Ordinal));
        (await repository.ListBillsForDayAsync(userId, 15, 50, default)).Count.ShouldBe(7);
        (await repository.ListBillsForDayAsync(userId, 20, 5, default)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ListBills_NullValue_IsKeptAsNull()
    {
        var (userId, accountId, subcategoryId) = await Seed();
        await Schedule(userId, accountId, subcategoryId, "Variável", null, 3);

        var bills = await new AdminTestDataRepository(postgres.Connections).ListBillsForDayAsync(userId, 3, 5, default);

        bills.Single().ExpectedValue.ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "009-36")]
    public async Task TestNotification_Bills_SevenSchedulesTomorrow_FiveLines_OnlyTheDayOfTheMonth()
    {
        var (userId, accountId, subcategoryId) = await Seed();
        var tomorrow = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"))).AddDays(1);
        for (var i = 1; i <= 7; i++)
        {
            await Schedule(userId, accountId, subcategoryId, $"Conta {i}", i * 1000.5m, tomorrow.Day);
        }

        var bot = new DependentFakes.FakeBot();
        using var factory = postgres.Api.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IBotClient>();
            services.AddSingleton<IBotClient>(bot);
        }));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/admin/test-notification")
        {
            Content = new StringContent($$"""{"tipo":"contas","usuario_id":{{userId}}}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-API-KEY", AdminHttp.ApiKey);

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe(
            $$"""{"status":"sucesso","mensagem":"Notificação de teste enviada para Ana","tipo":"contas","usuario_id":{{userId}},"numero_whatsapp":"5511999990001"}""");
        var (number, message) = bot.Sent.Single();
        number.ShouldBe("5511999990001");
        message.ShouldStartWith("🧪 *TESTE - Contas de Amanhã*\n\n💰 5 conta(s) encontrada(s)\n\n");
        message.Split('\n').Count(l => l.Contains(": R$ ", StringComparison.Ordinal)).ShouldBe(5);
        message.ShouldContain(": R$ 1.000,50\n");
    }
}
