using MeuSecretario.Api.Modules;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Infrastructure.Whatsapp;

namespace MeuSecretario.Api.Whatsapp;

/// <summary>Feature 012g (análises e gráfico): <c>Análise Inteligente</c>, <c>Comparação Mensal</c>, <c>Previsão de Gastos</c> e <c>Gráfico de Gastos</c>. Sem rotas.</summary>
public sealed class WhatsAppAnalyticsModule : IFeatureModule
{
    public void AddServices(IServiceCollection services)
    {
        services.AddScoped<IAnalyticsReader, AnalyticsReader>();

        services.AddScoped<IWhatsAppIntent, AnalysisIntent>();
        services.AddScoped<IWhatsAppIntent, MonthlyComparisonIntent>();
        services.AddScoped<IWhatsAppIntent, ForecastIntent>();
        services.AddScoped<IWhatsAppIntent, ChartIntent>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        // Sem rotas: a entrada é o POST /webhook-whatsapp (WhatsAppModule).
    }
}
