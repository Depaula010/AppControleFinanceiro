using MeuSecretario.Domain.Security;

namespace MeuSecretario.Api.Security;

/// <summary>
/// Logs do filtro de segurança (spec 009 §5 e §9), com os textos e prefixos do Flask. Categoria fixa
/// (<see cref="Category"/>): o sub-logger do Serilog filtra por ela para gravar o <c>security.log</c>. Tudo que veio do
/// cliente (ip, path, razão) passa por <see cref="SecurityLogText.Sanitize"/>. Nunca recebe corpo, chave nem User-Agent.
/// </summary>
internal static partial class SecurityLog
{
    public const string Category = "MeuSecretario.Security";

    public static void Blacklisted(ILogger logger, string ip, string path) =>
        BlacklistedCore(logger, Clean(ip), Clean(path));

    public static void Blocked(ILogger logger, string ip, string path) =>
        BlockedCore(logger, Clean(ip), Clean(path));

    public static void Attempt(ILogger logger, string ip, int count, string reason) =>
        AttemptCore(logger, Clean(ip), count, Clean(reason));

    public static void AttemptWithoutRedis(ILogger logger, string ip, string reason) =>
        AttemptWithoutRedisCore(logger, Clean(ip), Clean(reason));

    public static void Block(ILogger logger, string ip, int count, DateTime until) =>
        BlockCore(logger, Clean(ip), count, until.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));

    public static void AutoBlock(ILogger logger, string ip, int attempts) =>
        AutoBlockCore(logger, Clean(ip), attempts);

    public static void BlockNotPersisted(ILogger logger, string ip) =>
        BlockNotPersistedCore(logger, Clean(ip));

    public static void FilterFailed(ILogger logger, string error) =>
        FilterFailedCore(logger, Clean(error));

    // Rotas admin (009a, spec §4.1 a §4.4).
    public static void Unblocked(ILogger logger, string ip) => UnblockedCore(logger, Clean(ip));

    public static void BlacklistRemoved(ILogger logger, string ip) => BlacklistRemovedCore(logger, Clean(ip));

    public static void BlacklistAdded(ILogger logger, string ip, string? reason) =>
        BlacklistAddedCore(logger, Clean(ip), Clean(reason));

    public static void BlacklistAddWithoutRedis(ILogger logger, string ip) => BlacklistAddWithoutRedisCore(logger, Clean(ip));

    private static string Clean(string? text) => SecurityLogText.Sanitize(text);

    [LoggerMessage(EventId = 9009, Level = LogLevel.Warning, Message = "[SECURITY-UNBLOCK] Bloqueio temporário removido manualmente: {Ip}")]
    private static partial void UnblockedCore(ILogger logger, string ip);

    [LoggerMessage(EventId = 9010, Level = LogLevel.Warning, Message = "[SECURITY-BLACKLIST] IP removido da blacklist: {Ip}")]
    private static partial void BlacklistRemovedCore(ILogger logger, string ip);

    [LoggerMessage(EventId = 9011, Level = LogLevel.Warning, Message = "[SECURITY-BLACKLIST] IP adicionado à blacklist permanente: {Ip} | Razão: {Reason}")]
    private static partial void BlacklistAddedCore(ILogger logger, string ip, string reason);

    [LoggerMessage(EventId = 9012, Level = LogLevel.Error, Message = "[SECURITY-BLACKLIST] Redis indisponível, não foi possível adicionar IP à blacklist: {Ip}")]
    private static partial void BlacklistAddWithoutRedisCore(ILogger logger, string ip);

    [LoggerMessage(EventId = 9001, Level = LogLevel.Warning, Message = "[SECURITY-BLACKLISTED] IP na blacklist tentou acessar | IP: {Ip} | Path: {Path}")]
    private static partial void BlacklistedCore(ILogger logger, string ip, string path);

    [LoggerMessage(EventId = 9002, Level = LogLevel.Warning, Message = "[SECURITY-BLOCKED] IP bloqueado tentou acessar | IP: {Ip} | Path: {Path}")]
    private static partial void BlockedCore(ILogger logger, string ip, string path);

    [LoggerMessage(EventId = 9003, Level = LogLevel.Warning, Message = "[SECURITY-ATTEMPT] IP: {Ip} | Tentativa: {Count}/5 | Razão: {Reason}")]
    private static partial void AttemptCore(ILogger logger, string ip, int count, string reason);

    [LoggerMessage(EventId = 9004, Level = LogLevel.Warning, Message = "[SECURITY-ATTEMPT] IP: {Ip} | Razão: {Reason} (Redis indisponível)")]
    private static partial void AttemptWithoutRedisCore(ILogger logger, string ip, string reason);

    [LoggerMessage(EventId = 9005, Level = LogLevel.Warning, Message = "[SECURITY-BLOCK] IP bloqueado: {Ip} | Tentativas: {Count} | Bloqueado até: {Until}")]
    private static partial void BlockCore(ILogger logger, string ip, int count, string until);

    [LoggerMessage(EventId = 9006, Level = LogLevel.Warning, Message = "[SECURITY-AUTO-BLOCK] IP {Ip} bloqueado automaticamente | Razão: {Attempts} tentativas suspeitas")]
    private static partial void AutoBlockCore(ILogger logger, string ip, int attempts);

    [LoggerMessage(EventId = 9007, Level = LogLevel.Warning, Message = "[SECURITY-BLOCK] Redis indisponível, bloqueio não persistido: {Ip}")]
    private static partial void BlockNotPersistedCore(ILogger logger, string ip);

    [LoggerMessage(EventId = 9008, Level = LogLevel.Error, Message = "[SECURITY-FILTER] Falha no filtro de segurança, requisição liberada: {Error}")]
    private static partial void FilterFailedCore(ILogger logger, string error);
}
