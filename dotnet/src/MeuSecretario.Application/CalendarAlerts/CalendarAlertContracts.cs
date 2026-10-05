using MeuSecretario.Domain.CalendarAlerts;

namespace MeuSecretario.Application.CalendarAlerts;

/// <summary>Usuário que o job da 013 deve processar (RN-014-10).</summary>
public sealed record ActiveAlertUser(int UserId, string WhatsappNumber, int MinutesBefore);

/// <summary>Tabela <c>calendaralertconfigs</c> (spec 014 §3 e RN-014-10).</summary>
public interface ICalendarAlertConfigRepository
{
    /// <summary>Insere a linha padrão (<c>false</c>/<c>1</c>) se faltar e devolve a linha (desvio 004).</summary>
    Task<CalendarAlertConfig> GetOrCreateAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Atualiza só o que veio e renova <c>updated_at</c>; erro do banco sobe como exceção.</summary>
    Task<CalendarAlertConfig> UpdateAsync(int userId, bool? active, int? minutesBefore, CancellationToken cancellationToken);

    Task<IReadOnlyList<ActiveAlertUser>> ListUsersWithActiveAlertsAsync(CancellationToken cancellationToken);
}

/// <summary>Registro de falhas sem dados pessoais: só id numérico e o tipo da exceção (implementado na Infrastructure).</summary>
public interface ICalendarAlertDiagnostics
{
    void ConfigUpdateFailed(int userId, Exception exception);

    void EventsLookupFailed(int userId, Exception exception);

    void CalendarEventsFailed(int userId, Exception exception);

    void AlertSendFailed(int userId, Exception exception);

    void ProcessFailed(int userId, Exception exception);

    void RedisUnavailable(int userId);
}
