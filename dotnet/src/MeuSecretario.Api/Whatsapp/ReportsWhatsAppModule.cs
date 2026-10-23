using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Infrastructure.Whatsapp;

namespace MeuSecretario.Api.Whatsapp;

/// <summary>Registro dos relatórios sob comando (desvio 256): fatura por categoria, gastos do mês por categoria e relatório do mês.</summary>
internal static class ReportsWhatsAppModule
{
    public static void AddReportsServices(IServiceCollection services)
    {
        services.AddScoped<IReportsReader, ReportsReader>();
        services.AddScoped<IWhatsAppStep, ReportCommandStep>();
    }
}
