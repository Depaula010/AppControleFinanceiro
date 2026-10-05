using Dapper;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.CalendarAlerts;
using MeuSecretario.Domain.CalendarAlerts;
using MeuSecretario.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MeuSecretario.Infrastructure.CalendarAlerts;

/// <summary>
/// <c>calendaralertconfigs</c> (spec 014 §3 e RN-014-10). Todo SQL é fixo; valores sempre em parâmetro. A tabela não
/// tem trigger de <c>updated_at</c>: o <c>UPDATE</c> grava.
/// </summary>
public sealed class CalendarAlertConfigRepository(IDbConnectionFactory connections) : ICalendarAlertConfigRepository
{
    private const string Columns = "alertas_tarefas_ativo AS Active, minutos_antes AS MinutesBefore";

    public async Task<CalendarAlertConfig> GetOrCreateAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO calendaralertconfigs (usuario_id) VALUES (@userId) ON CONFLICT (usuario_id) DO NOTHING",
            new { userId },
            cancellationToken: cancellationToken));
        var row = await connection.QuerySingleAsync<ConfigRow>(new CommandDefinition(
            $"SELECT {Columns} FROM calendaralertconfigs WHERE usuario_id = @userId",
            new { userId },
            cancellationToken: cancellationToken));
        return new CalendarAlertConfig(row.Active, row.MinutesBefore);
    }

    public async Task<CalendarAlertConfig> UpdateAsync(int userId, bool? active, int? minutesBefore, CancellationToken cancellationToken)
    {
        var assignments = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("userId", userId);
        if (active is { } flag)
        {
            assignments.Add("alertas_tarefas_ativo = @active");
            parameters.Add("active", flag, System.Data.DbType.Boolean);
        }

        if (minutesBefore is { } minutes)
        {
            assignments.Add("minutos_antes = @minutes");
            parameters.Add("minutes", minutes, System.Data.DbType.Int32);
        }

        assignments.Add("updated_at = CURRENT_TIMESTAMP");

        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleAsync<ConfigRow>(new CommandDefinition(
            $"UPDATE calendaralertconfigs SET {string.Join(", ", assignments)} WHERE usuario_id = @userId RETURNING {Columns}",
            parameters,
            cancellationToken: cancellationToken));
        return new CalendarAlertConfig(row.Active, row.MinutesBefore);
    }

    public async Task<IReadOnlyList<ActiveAlertUser>> ListUsersWithActiveAlertsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<ActiveUserRow>(new CommandDefinition(
            """
            SELECT u.id AS UserId, u.numero_whatsapp AS WhatsappNumber, cac.minutos_antes AS MinutesBefore
            FROM calendaralertconfigs cac
            JOIN usuarios u ON cac.usuario_id = u.id
            JOIN googlecalendartokens gct ON u.id = gct.usuario_id
            WHERE cac.alertas_tarefas_ativo = TRUE
              AND (gct.needs_reconnect = FALSE OR gct.needs_reconnect IS NULL)
            ORDER BY u.id
            """,
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => new ActiveAlertUser(r.UserId, r.WhatsappNumber, r.MinutesBefore))];
    }

    private sealed class ConfigRow
    {
        public bool Active { get; set; }

        public int MinutesBefore { get; set; }
    }

    private sealed class ActiveUserRow
    {
        public int UserId { get; set; }

        public string WhatsappNumber { get; set; } = string.Empty;

        public int MinutesBefore { get; set; }
    }
}

/// <summary>Logs da 014 (EventIds 14001 a 14006): só id numérico e tipo da exceção; nunca WhatsApp, título nem local de evento.</summary>
internal static partial class CalendarAlertLog
{
    [LoggerMessage(EventId = 14001, Level = LogLevel.Error, Message = "Erro ao atualizar a configuração de alertas do usuário {UserId}: {Error}")]
    public static partial void ConfigUpdateFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 14002, Level = LogLevel.Warning, Message = "Não foi possível buscar eventos para alerta (usuário {UserId}): {Error}")]
    public static partial void EventsLookupFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 14003, Level = LogLevel.Warning, Message = "Erro ao buscar eventos de um calendário para alerta (usuário {UserId}): {Error}")]
    public static partial void CalendarEventsFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 14004, Level = LogLevel.Error, Message = "Erro ao enviar alerta de evento (usuário {UserId}): {Error}")]
    public static partial void AlertSendFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 14005, Level = LogLevel.Error, Message = "Erro inesperado ao processar alertas do usuário {UserId}: {Error}")]
    public static partial void ProcessFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 14006, Level = LogLevel.Warning, Message = "Redis indisponível: alerta do usuário {UserId} segue sem lock (fail-open)")]
    public static partial void RedisUnavailable(ILogger logger, int userId);
}

public sealed class CalendarAlertDiagnostics(ILogger<CalendarAlertDiagnostics> logger) : ICalendarAlertDiagnostics
{
    public void ConfigUpdateFailed(int userId, Exception exception) => CalendarAlertLog.ConfigUpdateFailed(logger, userId, Describe(exception));

    public void EventsLookupFailed(int userId, Exception exception) => CalendarAlertLog.EventsLookupFailed(logger, userId, Describe(exception));

    public void CalendarEventsFailed(int userId, Exception exception) => CalendarAlertLog.CalendarEventsFailed(logger, userId, Describe(exception));

    public void AlertSendFailed(int userId, Exception exception) => CalendarAlertLog.AlertSendFailed(logger, userId, Describe(exception));

    public void ProcessFailed(int userId, Exception exception) => CalendarAlertLog.ProcessFailed(logger, userId, Describe(exception));

    public void RedisUnavailable(int userId) => CalendarAlertLog.RedisUnavailable(logger, userId);

    /// <summary>Só o tipo (e o código SQL ou HTTP): a mensagem pode ter dado pessoal.</summary>
    private static string Describe(Exception exception) => exception switch
    {
        PostgresException pg => $"{nameof(PostgresException)} (SqlState {pg.SqlState})",
        CalendarApiException { StatusCode: { } status } => $"{nameof(CalendarApiException)} (HTTP {(int)status})",
        _ => exception.GetType().Name,
    };
}

public static class CalendarAlertsServiceCollectionExtensions
{
    public static IServiceCollection AddCalendarAlertsInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton<ICalendarAlertConfigRepository, CalendarAlertConfigRepository>();
        services.TryAddSingleton<ICalendarAlertDiagnostics, CalendarAlertDiagnostics>();
        return services;
    }
}
