using MeuSecretario.Api.Modules;
using MeuSecretario.Application.Whatsapp;

namespace MeuSecretario.Api.Whatsapp;

/// <summary>
/// Feature 012e (calendário): registra os passos do pipeline (confirmar/cancelar evento e "calcular rota"), as quatro intenções de
/// agenda e os serviços que elas usam. Módulo próprio para não mexer no <see cref="WhatsAppModule"/> (as sub-entregas paralelas
/// registram as suas em outros módulos). Não mapeia rotas.
/// </summary>
public sealed class WhatsAppCalendarModule : IFeatureModule
{
    public void AddServices(IServiceCollection services)
    {
        services.AddScoped<UserAddresses>();
        services.AddScoped<AgendaService>();
        services.AddScoped<FreeTimeService>();

        services.AddScoped<IWhatsAppStep, EventReplyStep>();
        services.AddScoped<IWhatsAppStep, RouteRequestStep>();

        services.AddScoped<IWhatsAppIntent, CreateEventIntent>();
        services.AddScoped<IWhatsAppIntent, DeleteEventIntent>();
        services.AddScoped<IWhatsAppIntent, AgendaIntent>();
        services.AddScoped<IWhatsAppIntent, FreeSlotsIntent>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        // Sem rotas: a entrada é o POST /webhook-whatsapp (WhatsAppModule).
    }
}
