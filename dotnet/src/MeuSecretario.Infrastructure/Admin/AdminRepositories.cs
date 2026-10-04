using Dapper;
using MeuSecretario.Application.Admin;
using MeuSecretario.Infrastructure.Database;
using Npgsql;

namespace MeuSecretario.Infrastructure.Admin;

/// <summary>
/// <c>baileys_auth</c> é compartilhada com o bot Node. O <c>session_id</c> é o literal do Flask (<c>setup_service.py:22</c>;
/// Q-009-3 e P-009-8 seguem abertas: manter o literal).
/// </summary>
public sealed class BotSessionRepository(IDbConnectionFactory connections) : IBotSessionRepository
{
    public async Task<int> ClearAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM baileys_auth WHERE session_id = 'baileys_session'",
            cancellationToken: cancellationToken));
    }
}

/// <summary>
/// <c>notificationconfigs</c> para as rotas admin (spec 009 §4.8 e §4.9). O <c>INSERT ... ON CONFLICT DO NOTHING</c> evita a
/// corrida do get-or-create do Flask; <c>23503</c> (FK de <c>usuario_id</c>) significa usuário inexistente (desvio 080).
/// </summary>
public sealed class AdminNotificationConfigRepository(IDbConnectionFactory connections) : IAdminNotificationConfigRepository
{
    private const string ForeignKeyViolation = "23503";

    public async Task<NotificationConfigRow?> GetOrCreateAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO notificationconfigs (usuario_id) VALUES (@userId) ON CONFLICT (usuario_id) DO NOTHING",
                new { userId },
                cancellationToken: cancellationToken));
        }
        catch (PostgresException ex) when (ex.SqlState == ForeignKeyViolation)
        {
            return null;
        }

        var row = await connection.QuerySingleOrDefaultAsync<ConfigRow>(new CommandDefinition(
            """
            SELECT resumo_matinal_ativo AS ResumoMatinalAtivo,
                   resumo_matinal_hora::text AS ResumoMatinalHora,
                   alertas_financeiros_ativos AS AlertasFinanceirosAtivos
            FROM notificationconfigs WHERE usuario_id = @userId
            """,
            new { userId },
            cancellationToken: cancellationToken));

        // "07:00:00" => "07:00", como o strftime('%H:%M').
        return row is null
            ? null
            : new NotificationConfigRow(row.ResumoMatinalAtivo, row.ResumoMatinalHora[..5], row.AlertasFinanceirosAtivos);
    }

    public async Task<bool?> SetFinancialAlertsAsync(int userId, bool active, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition(
            """
            UPDATE notificationconfigs SET alertas_financeiros_ativos = @active, updated_at = now()
            WHERE usuario_id = @userId RETURNING alertas_financeiros_ativos
            """,
            new { active, userId },
            cancellationToken: cancellationToken));
    }

    private sealed class ConfigRow
    {
        public bool ResumoMatinalAtivo { get; set; }

        public string ResumoMatinalHora { get; set; } = string.Empty;

        public bool? AlertasFinanceirosAtivos { get; set; }
    }
}
