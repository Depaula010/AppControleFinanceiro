using Serilog.Events;

namespace MeuSecretario.Api.Security;

/// <summary>
/// Configuração da 009 (spec §4 do plan), lida de <see cref="IConfiguration"/> com os nomes do Flask. Não toca
/// <c>AppSettings</c>. <c>AUTO_BLOCK_ENABLED</c>, <c>BLOCK_DURATION_MINUTES</c> e <c>MAX_SUSPICIOUS_ATTEMPTS</c> são
/// letra morta no Flask e não são lidas.
/// </summary>
public sealed class SecurityOptions
{
    public const int DefaultPerHour = 300;
    public const int DefaultAdminPerHour = 60;

    /// <summary><c>BOT_PROTECTION_ENABLED</c> (padrão ligado): liga o filtro. Como o Flask, só <c>true</c> liga.</summary>
    public bool BotProtectionEnabled { get; set; } = true;

    /// <summary><c>SECURITY_HEADERS_ENABLED</c> (padrão ligado).</summary>
    public bool SecurityHeadersEnabled { get; set; } = true;

    /// <summary><c>SECURITY_LOG_FILE</c>; nulo = <c>&lt;LOG_DIR&gt;/security.log</c>.</summary>
    public string? LogFile { get; set; }

    /// <summary><c>SECURITY_LOG_LEVEL</c> (padrão <c>WARNING</c>).</summary>
    public LogEventLevel LogLevel { get; set; } = LogEventLevel.Warning;

    /// <summary><c>RATE_LIMIT_ENABLED</c>: desligado por padrão até a confirmação dos limites (desvio 081).</summary>
    public bool RateLimitEnabled { get; set; }

    public int RateLimitPerHour { get; set; } = DefaultPerHour;

    public int RateLimitAdminPerHour { get; set; } = DefaultAdminPerHour;

    public static SecurityOptions Load(Func<string, string?> get)
    {
        ArgumentNullException.ThrowIfNull(get);
        return new SecurityOptions
        {
            BotProtectionEnabled = Flag(get("BOT_PROTECTION_ENABLED"), true),
            SecurityHeadersEnabled = Flag(get("SECURITY_HEADERS_ENABLED"), true),
            LogFile = string.IsNullOrWhiteSpace(get("SECURITY_LOG_FILE")) ? null : get("SECURITY_LOG_FILE")!.Trim(),
            LogLevel = ParseLevel(get("SECURITY_LOG_LEVEL")),
            RateLimitEnabled = Flag(get("RATE_LIMIT_ENABLED"), false),
            RateLimitPerHour = PositiveInt(get("RATE_LIMIT_PER_HOUR"), DefaultPerHour),
            RateLimitAdminPerHour = PositiveInt(get("RATE_LIMIT_ADMIN_PER_HOUR"), DefaultAdminPerHour),
        };
    }

    private static bool Flag(string? value, bool fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    private static int PositiveInt(string? value, int fallback) =>
        int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) && number > 0
            ? number
            : fallback;

    /// <summary>Nomes do <c>logging</c> do Python; desconhecido cai em <c>WARNING</c>.</summary>
    private static LogEventLevel ParseLevel(string? value) =>
        value?.Trim().ToUpperInvariant() switch
        {
            "DEBUG" => LogEventLevel.Debug,
            "INFO" => LogEventLevel.Information,
            "ERROR" => LogEventLevel.Error,
            "CRITICAL" => LogEventLevel.Fatal,
            _ => LogEventLevel.Warning,
        };
}
