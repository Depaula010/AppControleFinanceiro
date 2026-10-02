using MeuSecretario.Api.Contracts;
using MeuSecretario.Api.Http;
using MeuSecretario.Api.Modules;
using MeuSecretario.Application.Abstractions;

namespace MeuSecretario.Api.Health;

/// <summary><c>GET /</c> (app/__init__.py:178) e <c>GET /api/health</c> (api.py:908).</summary>
public sealed class HealthModule : IFeatureModule
{
    public void AddServices(IServiceCollection services)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        // Sempre 200, mesmo com o banco fora (o healthcheck do Docker e o deploy dependem disso).
        app.MapGet("/", async (IDatabaseHealth database, IClock clock, CancellationToken cancellationToken) =>
        {
            var status = await database.CheckAsync(cancellationToken);
            return Results.Json(
                new Dictionary<string, object?>
                {
                    ["status"] = "online",
                    ["service"] = "Meu Secretário API",
                    ["database"] = status.Connected ? "✅ Conectado" : $"❌ Erro: {status.Error}",
                    // Python: datetime.now().isoformat() — hora local, sem fuso.
                    ["timestamp"] = clock.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff", System.Globalization.CultureInfo.InvariantCulture),
                },
                JsonDefaults.Options);
        });

        app.MapGet("/api/health", () => Envelopes.Json(
            new Dictionary<string, object?>
            {
                ["status"] = "success",
                ["message"] = "API está funcionando",
                ["version"] = "1.0.0",
            },
            StatusCodes.Status200OK));
    }
}
