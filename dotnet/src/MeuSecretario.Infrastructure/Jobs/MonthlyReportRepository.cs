using System.Data;
using System.Globalization;
using Dapper;
using MeuSecretario.Application.Jobs;
using MeuSecretario.Domain.Jobs;
using MeuSecretario.Infrastructure.Database;

namespace MeuSecretario.Infrastructure.Jobs;

/// <summary>
/// Dados do relatório mensal, portados de <c>monthly_report_service.py:48-320</c> e <c>monthly_report_config_service.py:172-231</c>.
/// Paridade com o Flask, inclusive o sinal das despesas (somas negativas; desvio proposto 132).
/// </summary>
public sealed class MonthlyReportRepository(IDbConnectionFactory connections) : IMonthlyReportRepository
{
    public async Task<IReadOnlyList<ReportRecipient>> ListRecipientsAsync(
        MonthlyMoment moment, TimeOnly min, TimeOnly max, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<RecipientRow>(new CommandDefinition(
            """
            SELECT u.id AS UserId, u.nome AS Name, u.numero_whatsapp AS WhatsappNumber,
                   to_char(mrc.hora_envio, 'HH24:MI:SS') AS SendTime
            FROM usuarios u
            INNER JOIN monthlyreportconfigs mrc ON u.id = mrc.usuario_id
            WHERE mrc.ativo = TRUE
              AND mrc.momento_envio = @moment
              AND mrc.hora_envio BETWEEN @min::time AND @max::time
            ORDER BY u.id
            """,
            new { moment = MonthlyReportRules.MomentName(moment), min = min.ToString("HH:mm:ss.ffffff", CultureInfo.InvariantCulture), max = max.ToString("HH:mm:ss.ffffff", CultureInfo.InvariantCulture) },
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => new ReportRecipient(r.UserId, r.Name, r.WhatsappNumber, ParseTime(r.SendTime)))];
    }

    public async Task<ReportRecipient?> FindUserAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<RecipientRow>(new CommandDefinition(
            "SELECT id AS UserId, nome AS Name, numero_whatsapp AS WhatsappNumber, NULL AS SendTime FROM usuarios WHERE id = @userId",
            new { userId },
            cancellationToken: cancellationToken));
        return row is null ? null : new ReportRecipient(row.UserId, row.Name, row.WhatsappNumber, default);
    }

    public async Task<MonthlyReportData> GetDataAsync(int userId, MonthlyMoment moment, DateOnly today, CancellationToken cancellationToken)
    {
        var (month, year, start, end) = MonthlyReportRules.Period(moment, today);
        var (previousStart, previousEnd) = MonthlyReportRules.PreviousMonth(start);
        await using var connection = await connections.OpenAsync(cancellationToken);

        DynamicParameters Period() => JobSql.With(
            ("uid", userId, DbType.Int32), ("start", JobSql.Day(start), DbType.Date), ("end", JobSql.Day(end), DbType.Date));

        var totalsRows = (await connection.QueryAsync<TotalRow>(new CommandDefinition(
            """
            SELECT tipo_transacao AS Type, SUM(valor) AS Total, COUNT(*) AS Quantity
            FROM transacoes
            WHERE usuario_id = @uid AND data_transacao BETWEEN @start AND @end
              AND consolidada = TRUE AND tipo_transacao IN ('Despesa', 'Renda')
            GROUP BY tipo_transacao
            """,
            Period(),
            cancellationToken: cancellationToken))).AsList();

        double expenses = 0;
        double incomes = 0;
        var count = 0;
        foreach (var row in totalsRows)
        {
            var value = row.Total is { } total && total != 0 ? (double)total : 0;
            if (row.Type == "Despesa")
            {
                expenses = value;
            }
            else if (row.Type == "Renda")
            {
                incomes = value;
            }

            count += (int)row.Quantity;
        }

        var topRows = await connection.QueryAsync<CategoryRow>(new CommandDefinition(
            """
            SELECT sc.nome_sub AS Category, SUM(t.valor) AS Total
            FROM transacoes t
            INNER JOIN subcategoria sc ON t.subcategoria_id = sc.id
            WHERE t.usuario_id = @uid AND t.data_transacao BETWEEN @start AND @end
              AND t.tipo_transacao = 'Despesa' AND t.consolidada = TRUE
            GROUP BY sc.nome_sub
            ORDER BY Total DESC, sc.nome_sub
            LIMIT 5
            """,
            Period(),
            cancellationToken: cancellationToken));

        var comparison = await connection.QuerySingleAsync<ComparisonRow>(new CommandDefinition(
            """
            SELECT SUM(CASE WHEN data_transacao BETWEEN @start AND @end THEN valor ELSE 0 END) AS CurrentValue,
                   SUM(CASE WHEN data_transacao BETWEEN @previousStart AND @previousEnd THEN valor ELSE 0 END) AS PreviousValue
            FROM transacoes
            WHERE usuario_id = @uid AND tipo_transacao = 'Despesa' AND consolidada = TRUE
              AND data_transacao BETWEEN @previousStart AND @end
            """,
            JobSql.With(
                ("uid", userId, DbType.Int32), ("start", JobSql.Day(start), DbType.Date), ("end", JobSql.Day(end), DbType.Date),
                ("previousStart", JobSql.Day(previousStart), DbType.Date), ("previousEnd", JobSql.Day(previousEnd), DbType.Date)),
            cancellationToken: cancellationToken));

        var pots = await connection.QueryAsync<PotRow>(new CommandDefinition(
            """
            SELECT p.nome_pote AS Name, p.valor_limite AS LimitValue, COALESCE(SUM(t.valor), 0) AS Used
            FROM potesdegastos p
            LEFT JOIN potesubcategorias psc ON p.id = psc.pote_id
            LEFT JOIN transacoes t ON psc.subcategoria_id = t.subcategoria_id
                AND t.data_transacao BETWEEN @start AND @end
                AND t.tipo_transacao = 'Despesa' AND t.consolidada = TRUE
            WHERE p.usuario_id = @uid AND p.ativo = TRUE
            GROUP BY p.id, p.nome_pote, p.valor_limite
            ORDER BY p.nome_pote
            """,
            Period(),
            cancellationToken: cancellationToken));

        var paid = await connection.QuerySingleAsync<CountRow>(new CommandDefinition(
            """
            SELECT COUNT(*) AS Quantity, SUM(valor) AS Total
            FROM transacoes
            WHERE usuario_id = @uid AND data_transacao BETWEEN @start AND @end
              AND tipo_transacao = 'Despesa' AND consolidada = TRUE
            """,
            Period(),
            cancellationToken: cancellationToken));

        var pending = await connection.QuerySingleAsync<CountRow>(new CommandDefinition(
            """
            SELECT COUNT(*) AS Quantity, SUM(valor_previsto) AS Total
            FROM agendamentos
            WHERE usuario_id = @uid AND ativo = TRUE
              AND ((tipo_agendamento = 'FIXO' AND data_inicio <= @end)
                   OR (tipo_agendamento = 'PARCELADO' AND parcelas_executadas < total_parcelas AND data_inicio <= @end))
            """,
            Period(),
            cancellationToken: cancellationToken));

        return new MonthlyReportData(
            month, year, start, end,
            MonthlyReportRules.Totals(expenses, incomes, count),
            MonthlyReportRules.TopCategories([.. topRows.Select(r => (r.Category, (double)r.Total))]),
            MonthlyReportRules.Comparison(comparison.CurrentValue is { } c && c != 0 ? (double)c : 0, comparison.PreviousValue is { } p && p != 0 ? (double)p : 0),
            [.. pots.Select(r => MonthlyReportRules.Pot(r.Name, (double)r.LimitValue, (double)r.Used))],
            new ReportAccounts(
                (int)paid.Quantity, paid.Total is { } paidTotal ? (double)paidTotal : 0,
                (int)pending.Quantity, pending.Total is { } pendingTotal ? (double)pendingTotal : 0));
    }

    private static TimeOnly ParseTime(string? text) =>
        text is not null && TimeOnly.TryParseExact(text, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : default;

    private sealed class RecipientRow
    {
        public int UserId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string WhatsappNumber { get; set; } = string.Empty;

        public string? SendTime { get; set; }
    }

    private sealed class TotalRow
    {
        public string Type { get; set; } = string.Empty;

        public decimal? Total { get; set; }

        public long Quantity { get; set; }
    }

    private sealed class CategoryRow
    {
        public string Category { get; set; } = string.Empty;

        public decimal Total { get; set; }
    }

    private sealed class ComparisonRow
    {
        public decimal? CurrentValue { get; set; }

        public decimal? PreviousValue { get; set; }
    }

    private sealed class PotRow
    {
        public string Name { get; set; } = string.Empty;

        public decimal LimitValue { get; set; }

        public decimal Used { get; set; }
    }

    private sealed class CountRow
    {
        public long Quantity { get; set; }

        public decimal? Total { get; set; }
    }
}
