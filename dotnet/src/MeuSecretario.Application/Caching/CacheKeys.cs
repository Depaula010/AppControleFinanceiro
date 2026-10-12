using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MeuSecretario.Application.Caching;

/// <summary>Construtores das chaves do Redis, idênticos às do Python (spec 010 RN-010-07). Sem <c>security:*</c> (feature 009).</summary>
public static class CacheKeys
{
    public static string PendingTx(string whatsapp, string txId) => $"pending_tx:{whatsapp}:{txId}";

    public static string LastPending(string whatsapp) => $"last_pending:{whatsapp}";

    public static string PendingEvent(string whatsapp, string eventId) => $"pending_event:{whatsapp}:{eventId}";

    public static string PendingEventPattern(string whatsapp) => $"pending_event:{whatsapp}:*";

    /// <summary>Ponteiro para o evento pendente mais recente (desvio 155; o Flask não tem esta chave).</summary>
    public static string LastPendingEvent(string whatsapp) => $"last_pending_event:{whatsapp}";

    public static string NightlyCheckin(string whatsapp, string id) => $"nightly_checkin:{whatsapp}:{id}";

    public static string NightlyCheckinActive(string whatsapp) => $"nightly_checkin_active:{whatsapp}";

    public static string InvoiceClosed(int faturaId) => string.Create(CultureInfo.InvariantCulture, $"invoice_closed:{faturaId}");

    public static string InvoiceDueWarning3d(int faturaId) => string.Create(CultureInfo.InvariantCulture, $"invoice_due_warning:{faturaId}:3d");

    public static string CalendarDisconnectedAlert(int userId) => string.Create(CultureInfo.InvariantCulture, $"calendar_disconnected_alert:{userId}");

    public static string CalendarTokenRevokedAlert(int userId) => string.Create(CultureInfo.InvariantCulture, $"calendar_token_revoked_alert:{userId}");

    /// <summary><c>calendar_alert:</c> + MD5 (hex minúsculo, 32 caracteres) de <c>"{uid}:{eventId}:{start}"</c> em UTF-8.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5351", Justification = "MD5 só compõe a chave de dedupe, idêntica à do Python; não é segurança.")]
    public static string CalendarAlert(int userId, string eventId, string start)
    {
        var raw = string.Create(CultureInfo.InvariantCulture, $"{userId}:{eventId}:{start}");
        return "calendar_alert:" + Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    public static string JobLock(string name) => $"job_lock:{name}";

    public static string TravelTimeLimit(int userId, DateOnly date) =>
        string.Create(CultureInfo.InvariantCulture, $"travel_time_limit:{userId}:{date:yyyy-MM-dd}");

    public static string BotSessionId(string sessionName) => $"bot_session_id:{sessionName}";

    /// <summary><c>gemini_cache:{prefix}:{uid ou "global"}:{hash12}</c>.</summary>
    public static string GeminiCache(string prefix, int? userId, string hash12) =>
        string.Create(CultureInfo.InvariantCulture, $"gemini_cache:{prefix}:{(userId is { } uid ? uid.ToString(CultureInfo.InvariantCulture) : "global")}:{hash12}");
}
