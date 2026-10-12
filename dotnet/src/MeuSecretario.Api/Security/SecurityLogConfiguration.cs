using System.Globalization;
using Serilog;
using Serilog.Filters;

namespace MeuSecretario.Api.Security;

/// <summary>
/// Sub-logger do Serilog que grava o <c>security.log</c> (spec 009 §9, RN-009-14): só a categoria
/// <see cref="SecurityLog.Category"/>, nível mínimo <c>SECURITY_LOG_LEVEL</c>, formato do Flask
/// <c>[yyyy-MM-dd HH:mm:ss] LEVEL: mensagem</c> com a hora de São Paulo. Fica fora do <c>Program.cs</c> para ser testável
/// sem o estado estático do <c>Log.Logger</c> compartilhado entre hosts.
/// </summary>
public static class SecurityLogConfiguration
{
    public const string FileName = "security.log";

    public static readonly string OutputTemplate =
        "[{" + SaoPauloTimestampEnricher.PropertyName + "}] {Level:u}: {Message:lj}{NewLine}";

    public static LoggerConfiguration AddSecurityLog(
        this LoggerConfiguration logger,
        SecurityOptions? security,
        TimeZoneInfo? zone,
        string logDirectory)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (security is null)
        {
            return logger;
        }

        return logger.WriteTo.Logger(sub => sub
            .Enrich.With(new SaoPauloTimestampEnricher(zone ?? TimeZoneInfo.Utc))
            .MinimumLevel.Is(security.LogLevel)
            .Filter.ByIncludingOnly(Matching.FromSource(SecurityLog.Category))
            .WriteTo.File(
                security.LogFile ?? Path.Combine(logDirectory, FileName),
                outputTemplate: OutputTemplate,
                formatProvider: CultureInfo.InvariantCulture));
    }
}
