using MeuSecretario.Api;
using System.Globalization;
using MeuSecretario.Api.Http;
using MeuSecretario.Api.Modules;
using MeuSecretario.Api.Security;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure;
using MeuSecretario.Infrastructure.Database;
using MeuSecretario.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
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
        .Enrich.WithProperty(LogTemplates.CorrelationProperty, string.Empty)
        .WriteTo.Console(outputTemplate: LogTemplates.Output, formatProvider: CultureInfo.InvariantCulture);

    // Em produção os logs vão para ./logs (volume do compose), como o Flask.
    var settings = services.GetService<AppSettings>();
    if (settings is { LogToFile: true })
    {
        logger.WriteTo.File(
            Path.Combine(settings.LogDirectory, "app-.log"),
            outputTemplate: LogTemplates.Output,
            formatProvider: CultureInfo.InvariantCulture,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14);

        // 009: security.log (mesmo formato do Flask) só com a categoria de segurança. Respeita LOG_TO_FILE.
        logger.AddSecurityLog(
            services.GetService<IOptions<SecurityOptions>>()?.Value,
            services.GetService<IClock>()?.TimeZone,
            settings.LogDirectory);
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

// Modo de migração (ADR 0001): `dotnet MeuSecretario.Api.dll --migrate` aplica as migrations pendentes e sai.
// O app nunca executa DDL no boot normal.
if (args.Contains("--migrate"))
{
    var applied = await app.Services.GetRequiredService<MigrationRunner>().RunAsync();
    Console.WriteLine(applied.Count == 0
        ? "Nenhuma migration pendente."
        : "Migrations aplicadas: " + string.Join(", ", applied));
    return;
}

// 009: IP real, rate limit, filtro de segurança, 404 do Flask. A ordem importa. O WebApplication já insere o
// UseRouting no início do pipeline: GetEndpoint() está preenchido a partir do filtro (passo 4).
app.UseForwardedHeaders();                              // 1. IP do cliente (desvio 082)
app.UseMiddleware<SecurityHeadersMiddleware>();         // 2. headers em todas as respostas, inclusive 403/404 do filtro
app.UseRateLimiter();                                   // 3. antes do filtro, como o Limiter do Flask
app.UseMiddleware<SecurityFilterMiddleware>();          // 4. blacklist, bloqueio, suspeito (usa GetEndpoint())
app.UseMiddleware<StrictRoutingMiddleware>();           // 5. barra final => 404
app.UseMiddleware<UnmatchedRouteMiddleware>();          // 6. sem endpoint => 404 HTML do Flask

foreach (var module in modules)
{
    module.MapEndpoints(app);
}

app.Run();

/// <summary>Necessário para o <c>WebApplicationFactory</c> dos testes de integração.</summary>
public partial class Program;
