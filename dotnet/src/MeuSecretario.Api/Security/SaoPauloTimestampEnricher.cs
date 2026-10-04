using Serilog.Core;
using Serilog.Events;

namespace MeuSecretario.Api.Security;

/// <summary>
/// Acrescenta <c>SaoPauloTime</c> (<c>yyyy-MM-dd HH:mm:ss</c> em America/Sao_Paulo) para o <c>security.log</c>: o
/// <c>{Timestamp}</c> do Serilog usaria o fuso do container (constituição, regra 8).
/// </summary>
public sealed class SaoPauloTimestampEnricher(TimeZoneInfo zone) : ILogEventEnricher
{
    public const string PropertyName = "SaoPauloTime";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);
        var local = TimeZoneInfo.ConvertTime(logEvent.Timestamp, zone);
        var text = local.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(PropertyName, text));
    }
}
