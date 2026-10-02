using System.Globalization;
using MeuSecretario.Api.Http;
using MeuSecretario.Api.Modules;
using MeuSecretario.Infrastructure;
using MeuSecretario.Infrastructure.Configuration;
using Serilog;

// Cultura fixa: mensagens de WhatsApp e formatos em pt-BR, independentemente do container.
var culture = new CultureInfo("pt-BR");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

var builder = WebApplication.CreateBuilder(args);

string? Read(string key) => builder.Configuration[key];

builder.Host.UseSerilog((_, services, logger) =>
{
    logger.MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
        .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture);

    // Em produção os logs vão para ./logs (volume do compose), como o Flask.
    var settings = services.GetService<AppSettings>();
    if (settings is { LogToFile: true })
    {
        logger.WriteTo.File(
            Path.Combine(settings.LogDirectory, "app-.log"),
            formatProvider: CultureInfo.InvariantCulture,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14);
    }
});

builder.Services.AddInfrastructure(Read);
builder.Services.ConfigureHttpJsonOptions(options => JsonDefaults.Apply(options.SerializerOptions));

var modules = FeatureModules.Discover(typeof(Program).Assembly);
foreach (var module in modules)
{
    module.AddServices(builder.Services);
}

var app = builder.Build();

// Falha rápido: configuração inválida derruba o processo no boot, não na primeira requisição.
_ = app.Services.GetRequiredService<AppSettings>();

app.UseMiddleware<StrictRoutingMiddleware>();

foreach (var module in modules)
{
    module.MapEndpoints(app);
}

app.Run();

/// <summary>Necessário para o <c>WebApplicationFactory</c> dos testes de integração.</summary>
public partial class Program;
