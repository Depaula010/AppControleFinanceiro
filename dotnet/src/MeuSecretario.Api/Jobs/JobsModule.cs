using MeuSecretario.Api.Modules;
using MeuSecretario.Application.Jobs;
using MeuSecretario.Infrastructure.Jobs;

namespace MeuSecretario.Api.Jobs;

/// <summary>
/// Feature 013: jobs Quartz (sem rota). Registra os jobs, as portas das rotas admin da 009c e o agendador, que só liga com
/// <c>JOBS_ENABLED=true</c> (padrão desligado; nada dispara em dev nem em teste).
/// </summary>
public sealed class JobsModule : IFeatureModule
{
    public void AddServices(IServiceCollection services)
    {
        services.AddOptions<JobsOptions>().Configure<IConfiguration>((options, configuration) =>
            options.Enabled = string.Equals(configuration["JOBS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase));
        services.AddJobs();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        // Sem rotas: as de /admin/run-motor-agendamentos e /admin/trigger-* são da 009c e usam as portas registradas aqui.
    }
}
