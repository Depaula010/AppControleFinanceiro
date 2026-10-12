using System.Globalization;
using Dapper;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Infrastructure.Database;

namespace MeuSecretario.Infrastructure.Whatsapp;

/// <summary>
/// Configurações lidas e gravadas pelo WhatsApp (012f): <c>notificationconfigs</c>, <c>monthlyreportconfigs</c>, <c>usuarios.cidade/estado</c>
/// e a chave de automação. Texto do usuário sempre como parâmetro; as horas viajam como texto e o banco as converte.
/// </summary>
public sealed class WhatsAppConfigStore(IDbConnectionFactory connections) : IWhatsAppConfigStore
{
    private sealed class NotificationRow
    {
        public bool Morning { get; set; }

        public string MorningTime { get; set; } = "";

        public bool Checkin { get; set; }

        public string CheckinTime { get; set; } = "";

        public bool Alerts { get; set; }
    }

    private sealed class ReportRow
    {
        public bool Active { get; set; }

        public string Moment { get; set; } = "";

        public string Time { get; set; } = "";
    }

    private sealed class KeyRow
    {
        public string? ApiKey { get; set; }

        public string? Name { get; set; }
    }

    private const string SelectNotification = """
        SELECT resumo_matinal_ativo AS Morning, resumo_matinal_hora::text AS MorningTime,
               checkin_noturno_ativo AS Checkin, checkin_noturno_hora::text AS CheckinTime,
               COALESCE(alertas_financeiros_ativos, FALSE) AS Alerts
        FROM notificationconfigs WHERE usuario_id = @userId
        """;

    private const string SelectReport = """
        SELECT ativo AS Active, momento_envio AS Moment, hora_envio::text AS Time
        FROM monthlyreportconfigs WHERE usuario_id = @userId
        """;

    public async Task<WhatsAppNotificationConfig> GetOrCreateNotificationAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<NotificationRow>(new CommandDefinition(SelectNotification, new { userId }, cancellationToken: cancellationToken));
        if (row is null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO notificationconfigs (usuario_id) VALUES (@userId) ON CONFLICT (usuario_id) DO NOTHING", new { userId }, cancellationToken: cancellationToken));
            row = await connection.QuerySingleAsync<NotificationRow>(new CommandDefinition(SelectNotification, new { userId }, cancellationToken: cancellationToken));
        }

        return ToConfig(row);
    }

    public async Task<WhatsAppNotificationConfig> UpdateMorningAsync(int userId, bool? active, TimeOnly? time, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE notificationconfigs
            SET resumo_matinal_ativo = COALESCE(@active::boolean, resumo_matinal_ativo),
                resumo_matinal_hora = COALESCE(@time::time, resumo_matinal_hora),
                updated_at = now()
            WHERE usuario_id = @userId
            """,
            new { userId, active, time = Text(time) },
            cancellationToken: cancellationToken));
        return ToConfig(await connection.QuerySingleAsync<NotificationRow>(new CommandDefinition(SelectNotification, new { userId }, cancellationToken: cancellationToken)));
    }

    public async Task<WhatsAppNotificationConfig> UpdateCheckinAsync(int userId, bool? active, TimeOnly? time, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE notificationconfigs
            SET checkin_noturno_ativo = COALESCE(@active::boolean, checkin_noturno_ativo),
                checkin_noturno_hora = COALESCE(@time::time, checkin_noturno_hora),
                updated_at = now()
            WHERE usuario_id = @userId
            """,
            new { userId, active, time = Text(time) },
            cancellationToken: cancellationToken));
        return ToConfig(await connection.QuerySingleAsync<NotificationRow>(new CommandDefinition(SelectNotification, new { userId }, cancellationToken: cancellationToken)));
    }

    public async Task<WhatsAppNotificationConfig> UpdateFinancialAlertsAsync(int userId, bool active, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE notificationconfigs SET alertas_financeiros_ativos = @active, updated_at = now() WHERE usuario_id = @userId",
            new { userId, active },
            cancellationToken: cancellationToken));
        return ToConfig(await connection.QuerySingleAsync<NotificationRow>(new CommandDefinition(SelectNotification, new { userId }, cancellationToken: cancellationToken)));
    }

    public async Task<WhatsAppReportConfig> GetOrCreateReportAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<ReportRow>(new CommandDefinition(SelectReport, new { userId }, cancellationToken: cancellationToken));
        if (row is null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO monthlyreportconfigs (usuario_id, ativo, momento_envio, hora_envio)
                VALUES (@userId, TRUE, 'INICIO_MES', '08:00:00') ON CONFLICT (usuario_id) DO NOTHING
                """,
                new { userId },
                cancellationToken: cancellationToken));
            row = await connection.QuerySingleAsync<ReportRow>(new CommandDefinition(SelectReport, new { userId }, cancellationToken: cancellationToken));
        }

        return ToReport(row);
    }

    public async Task<WhatsAppReportConfig> UpdateReportAsync(int userId, bool? active, string? moment, TimeOnly? time, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        if (active is not null || moment is not null || time is not null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE monthlyreportconfigs
                SET ativo = COALESCE(@active::boolean, ativo),
                    momento_envio = COALESCE(@moment::varchar, momento_envio),
                    hora_envio = COALESCE(@time::time, hora_envio),
                    updated_at = now()
                WHERE usuario_id = @userId
                """,
                new { userId, active, moment, time = Text(time) },
                cancellationToken: cancellationToken));
        }

        return ToReport(await connection.QuerySingleAsync<ReportRow>(new CommandDefinition(SelectReport, new { userId }, cancellationToken: cancellationToken)));
    }

    public async Task UpdateLocationAsync(int userId, string city, string? state, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE usuarios SET cidade = @city, estado = @state WHERE id = @userId",
            new { userId, city, state },
            cancellationToken: cancellationToken));
    }

    public async Task<(string? ApiKey, string? Name)?> GetApiKeyAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<KeyRow>(new CommandDefinition(
            "SELECT api_key_automate AS ApiKey, nome AS Name FROM usuarios WHERE id = @userId", new { userId }, cancellationToken: cancellationToken));
        return row is null ? null : (row.ApiKey, row.Name);
    }

    private static string? Text(TimeOnly? time) => time?.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    private static TimeOnly Parse(string text) => TimeOnly.Parse(text, CultureInfo.InvariantCulture);

    private static WhatsAppNotificationConfig ToConfig(NotificationRow row) =>
        new(row.Morning, Parse(row.MorningTime), row.Checkin, Parse(row.CheckinTime), row.Alerts);

    private static WhatsAppReportConfig ToReport(ReportRow row) => new(row.Active, row.Moment, Parse(row.Time));
}
