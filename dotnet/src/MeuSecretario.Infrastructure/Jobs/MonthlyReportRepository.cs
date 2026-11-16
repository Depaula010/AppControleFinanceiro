using System.Data;
using System.Globalization;
using Dapper;
using MeuSecretario.Application.Jobs;
using MeuSecretario.Domain.Jobs;
using MeuSecretario.Infrastructure.Database;

namespace MeuSecretario.Infrastructure.Jobs;

/// <summary>
/// Dados do relatório mensal, portados de <c>monthly_report_service.py</c> e <c>monthly_report_config_service.py</c>.
/// Desvio 132: as despesas são gravadas negativas e aqui entram em valor absoluto (totais, top, comparação, potes e contas
/// pagas); o Python somava o valor com sinal e o saldo, a variação e os potes saíam errados.
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
            SELECT tipo_transacao AS Type, SUM(CASE WHEN tipo_transacao = 'Despesa' THEN ABS(valor) ELSE valor END) AS Total,
                   COUNT(*) AS Quantity
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
            SELECT sc.nome_sub AS Category, SUM(ABS(t.valor)) AS Total
            FROM transacoes t
            INNER JOIN subcategoria sc ON t.subcategoria_id = sc.id
            WHERE t.usuario_id = @uid AND t.data_transacao BETWEEN @start AND @end
              AND t.tipo_transacao = 'Despesa' AND t.consolidada = TRUE AND t.usuario_id = @uid
            GROUP BY sc.nome_sub
            ORDER BY Total DESC, sc.nome_sub
            LIMIT 5
            """,
            Period(),
            cancellationToken: cancellationToken));

        var comparison = await connection.QuerySingleAsync<ComparisonRow>(new CommandDefinition(
            """
            SELECT SUM(CASE WHEN data_transacao BETWEEN @start AND @end THEN ABS(valor) ELSE 0 END) AS CurrentValue,
                   SUM(CASE WHEN data_transacao BETWEEN @previousStart AND @previousEnd THEN ABS(valor) ELSE 0 END) AS PreviousValue
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
            SELECT p.nome_pote AS Name, p.valor_limite AS LimitValue, COALESCE(SUM(ABS(t.valor)), 0) AS Used
            FROM potesdegastos p
            LEFT JOIN potesubcategorias psc ON p.id = psc.pote_id
            LEFT JOIN transacoes t ON psc.subcategoria_id = t.subcategoria_id
                AND t.data_transacao BETWEEN @start AND @end
                AND t.tipo_transacao = 'Despesa' AND t.consolidada = TRUE AND t.usuario_id = @uid
            WHERE p.usuario_id = @uid AND p.ativo = TRUE
            GROUP BY p.id, p.nome_pote, p.valor_limite
            ORDER BY p.nome_pote
            """,
            Period(),
            cancellationToken: cancellationToken));

        var paid = await connection.QuerySingleAsync<CountRow>(new CommandDefinition(
            """
            SELECT COUNT(*) AS Quantity, SUM(ABS(valor)) AS Total
            FROM transacoes
            WHERE usuario_id = @uid AND data_transacao BETWEEN @start AND @end
              AND tipo_transacao = 'Despesa' AND consolidada = TRUE
            """,
            Period(),
            cancellationToken: cancellationToken));

        // Desvio 285: "pendentes" = o que ainda não foi pago no período (mesma regra do check-in, SchedulePaymentSql). Antes contava todo
        // agendamento ativo, inclusive os já pagos. Contínuo (semanal, quinzenal, diário) entra se o próximo vencimento já chegou até o fim.
        var pending = await connection.QuerySingleAsync<CountRow>(new CommandDefinition(
            $"""
            SELECT COUNT(*) AS Quantity, SUM(a.valor_previsto) AS Total
            FROM agendamentos a
            CROSS JOIN LATERAL {SchedulePaymentSql.DueDates("a", "@start", "@end")} AS d(due)
            WHERE a.usuario_id = @uid AND a.ativo = TRUE
              AND a.tipo_agendamento IN ('FIXO', 'PARCELADO')
              AND (a.tipo_agendamento = 'FIXO' OR (a.total_parcelas IS NOT NULL AND COALESCE(a.parcelas_executadas, 0) < a.total_parcelas))
              AND a.data_inicio <= @end
              AND (({SchedulePaymentSql.IsRolling("a")} AND d.due <= @end) OR d.due BETWEEN @start AND @end)
              AND NOT EXISTS (SELECT 1 FROM transacoes t WHERE {SchedulePaymentSql.Paid("a", "t", "d.due", "a.descricao")})
            """,
            Period(),
            cancellationToken: cancellationToken));

        return new MonthlyReportData(
            month, year, start, end,
            MonthlyReportRules.Totals(expenses, incomes, count),
            MonthlyReportRules.TopCategories([.. topRows.Select(r => (r.Category, (double)r.Total))], expenses),
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
