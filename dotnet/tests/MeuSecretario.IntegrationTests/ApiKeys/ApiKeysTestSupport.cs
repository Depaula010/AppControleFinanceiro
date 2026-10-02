using System.Runtime.CompilerServices;
using Dapper;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.TestFixtures;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.IntegrationTests.ApiKeys;

/// <summary>Semeadura e hosts de teste da feature 008.</summary>
internal static class ApiKeysTestSupport
{
    public const string SystemGeminiKey = "gemini-de-teste";
    public const string SystemWeatherKey = "weather-do-sistema";

    private static readonly ConditionalWeakTable<PostgresFixture, WebApplicationFactory<Program>> EnabledHosts = [];

    /// <summary>API com <c>API_KEYS_ROUTES_ENABLED=true</c> (desvio 052) e chaves do sistema de teste.</summary>
    public static WebApplicationFactory<Program> EnabledApi(PostgresFixture postgres) =>
        EnabledHosts.GetValue(postgres, p => p.Api.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("API_KEYS_ROUTES_ENABLED", "true");
            builder.UseSetting("WEATHER_API_KEY", SystemWeatherKey);
        }));

    public static HttpClient EnabledClient(PostgresFixture postgres, bool withKey = true)
    {
        var client = EnabledApi(postgres).CreateClient();
        if (withKey)
        {
            client.DefaultRequestHeaders.Add("X-API-KEY", AuthFixtures.JwtSecret);
        }
        return client;
    }

    public static async Task<int> SeedUserAsync(PostgresFixture postgres, string whatsapp = "5511999990001", string name = "Usuário de Teste")
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(
            "INSERT INTO usuarios (nome, numero_whatsapp) VALUES (@name, @whatsapp) RETURNING id", new { name, whatsapp });
    }

    /// <summary>Bronze 100/50/10, Prata 500/200/50 e Ouro ilimitado (como a produção); idempotente.</summary>
    public static async Task SeedPlansAsync(PostgresFixture postgres)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO planos (nome_plano, preco_mensal, limite_gemini, limite_weather, limite_openroute) VALUES
              ('Bronze', 9.90, 100, 50, 10),
              ('Prata', 29.90, 500, 200, 50),
              ('Ouro', 0, NULL, NULL, NULL)
            ON CONFLICT (nome_plano) DO NOTHING
            """);
    }

    public static async Task<T> Scalar<T>(PostgresFixture postgres, string sql, object? args = null)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        return (await connection.ExecuteScalarAsync<T>(sql, args))!;
    }

    public static async Task Execute(PostgresFixture postgres, string sql, object? args = null)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(sql, args);
    }

    /// <summary>Insere a chave já cifrada (ou texto puro legado) direto no banco.</summary>
    public static Task<int> SeedKeyAsync(PostgresFixture postgres, int userId, string provider, string storedValue, bool active = true) =>
        Scalar<int>(
            postgres,
            """
            INSERT INTO chavesapiusuario (usuario_id, provedor, chave_api_criptografada, ativo)
            VALUES (@userId, @provider, @storedValue, @active) RETURNING id
            """,
            new { userId, provider, storedValue, active });

    public static IServiceScope Scope(PostgresFixture postgres) => EnabledApi(postgres).Services.CreateScope();
}
