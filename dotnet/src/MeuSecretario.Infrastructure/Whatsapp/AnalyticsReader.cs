using System.Globalization;
using Dapper;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Domain.Whatsapp.Analytics;
using MeuSecretario.Infrastructure.Database;

namespace MeuSecretario.Infrastructure.Whatsapp;

/// <summary>
/// Consultas das análises e do gráfico do WhatsApp (<c>analytics_service.py</c>, <c>forecast_service.py</c>, <c>chart_service.py</c>),
/// portadas com o mesmo recorte de linhas. As datas do dia vêm por parâmetro (<c>IClock</c>) em vez de <c>CURRENT_DATE</c>
/// (spec 012 §13.6) e os <c>ORDER BY</c> que o Flask deixa abertos ganham desempate por chave.
/// </summary>
public sealed class AnalyticsReader(IDbConnectionFactory connections) : IAnalyticsReader
{
    private sealed class MonthRow
    {
        public string Mes { get; set; } = "";

        public decimal Total { get; set; }
    }

    private sealed class CategoryRow
    {
        public string NomeMacro { get; set; } = "";

        public string NomeSubcategoria { get; set; } = "";

        public decimal Total { get; set; }

        public long Quantidade { get; set; }
    }

    private sealed class WeekdayRow
    {
        public string DiaSemana { get; set; } = "";

        public decimal Total { get; set; }

        public long Quantidade { get; set; }
    }

    private sealed class PotRow
    {
        public string NomePote { get; set; } = "";

        public decimal ValorLimite { get; set; }

        public decimal GastoAtual { get; set; }
    }

    private sealed class ExpenseRow
    {
        public string Descricao { get; set; } = "";

        public decimal Valor { get; set; }

        public DateTime DataTransacao { get; set; }
    }

    private sealed class FixedRow
    {
        public string Descricao { get; set; } = "";

        public decimal ValorPrevisto { get; set; }

        public string Periodicidade { get; set; } = "";
    }

    private sealed class PendingRow
    {
        public string Descricao { get; set; } = "";

        public decimal ValorPrevisto { get; set; }

        public int? DiaExecucao { get; set; }

        public string? TipoConta { get; set; }

        public string? NomeGrupo { get; set; }
    }

    private sealed class MacroRow
    {
        public string Mes { get; set; } = "";

        public string NomeMacro { get; set; } = "";

        public decimal Total { get; set; }
    }

    private sealed class PieDbRow
    {
        public string Categoria { get; set; } = "";

        public decimal Total { get; set; }
    }

    private sealed class BarDbRow
    {
        public DateTime Mes { get; set; }

        public decimal Despesas { get; set; }

        public decimal Rendas { get; set; }
    }

    private sealed class LineDbRow
    {
        public DateTime DataTransacao { get; set; }

        public decimal SaldoAcumulado { get; set; }
    }

    /// <summary><c>float(Decimal)</c> do Python: o double mais próximo do valor decimal exato.</summary>
    private static double ToDouble(decimal value) => double.Parse(value.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private static DateOnly Day(DateTime value) => DateOnly.FromDateTime(value);

    public async Task<SpendingAnalysis> GetSpendingAnalysisAsync(int userId, DateOnly today, CancellationToken cancellationToken)
    {
        var currentMonth = MonthlyComparison.MonthKey(today);
        var previousMonth = MonthlyComparison.PreviousMonthKey(today);
        var ninetyDaysAgo = today.AddDays(-90);

        await using var connection = await connections.OpenAsync(cancellationToken);

        var categories = await connection.QueryAsync<CategoryRow>(new CommandDefinition(
            """
            SELECT mc.nome_macro AS NomeMacro, sc.nome_sub AS NomeSubcategoria, SUM(t.valor) AS Total, COUNT(*) AS Quantidade
            FROM transacoes t
            JOIN subcategoria sc ON t.subcategoria_id = sc.id
            JOIN macrocategoria mc ON sc.macro_id = mc.id
            WHERE t.usuario_id = @userId
              AND t.tipo_transacao = 'Despesa'
              AND t.consolidada = true
              AND TO_CHAR(t.data_transacao, 'YYYY-MM') = @currentMonth
            GROUP BY mc.nome_macro, sc.nome_sub
            ORDER BY Total DESC, mc.nome_macro, sc.nome_sub
            LIMIT 10
            """,
            new { userId, currentMonth },
            cancellationToken: cancellationToken));

        var weekdays = await connection.QueryAsync<WeekdayRow>(new CommandDefinition(
            """
            SELECT TRIM(TO_CHAR(data_transacao, 'Day')) AS DiaSemana, SUM(valor) AS Total, COUNT(*) AS Quantidade
            FROM transacoes
            WHERE usuario_id = @userId
              AND tipo_transacao = 'Despesa'
              AND consolidada = true
              AND data_transacao >= @ninetyDaysAgo
            GROUP BY TRIM(TO_CHAR(data_transacao, 'Day')), EXTRACT(DOW FROM data_transacao)
            ORDER BY EXTRACT(DOW FROM data_transacao)
            """,
            new { userId, ninetyDaysAgo },
            cancellationToken: cancellationToken));

        var comparison = await connection.QueryAsync<MonthRow>(new CommandDefinition(
            """
            SELECT TO_CHAR(data_transacao, 'YYYY-MM') AS Mes, SUM(valor) AS Total
            FROM transacoes
            WHERE usuario_id = @userId
              AND tipo_transacao = 'Despesa'
              AND consolidada = true
              AND TO_CHAR(data_transacao, 'YYYY-MM') IN (@currentMonth, @previousMonth)
            GROUP BY TO_CHAR(data_transacao, 'YYYY-MM')
            ORDER BY Mes DESC
            """,
            new { userId, currentMonth, previousMonth },
            cancellationToken: cancellationToken));

        var pots = await connection.QueryAsync<PotRow>(new CommandDefinition(
            """
            SELECT p.nome_pote AS NomePote, p.valor_limite AS ValorLimite, COALESCE(SUM(t.valor), 0) AS GastoAtual
            FROM potesdegastos p
            LEFT JOIN potesubcategorias psc ON p.id = psc.pote_id
            LEFT JOIN transacoes t ON psc.subcategoria_id = t.subcategoria_id
                AND t.usuario_id = @userId
                AND t.tipo_transacao = 'Despesa'
                AND t.consolidada = true
                AND TO_CHAR(t.data_transacao, 'YYYY-MM') = @currentMonth
            WHERE p.usuario_id = @userId AND p.ativo = true
            GROUP BY p.id, p.nome_pote, p.valor_limite, p.periodicidade
            ORDER BY p.nome_pote, p.id
            """,
            new { userId, currentMonth },
            cancellationToken: cancellationToken));

        var top = await connection.QueryAsync<ExpenseRow>(new CommandDefinition(
            """
            SELECT t.descricao AS Descricao, t.valor AS Valor, t.data_transacao AS DataTransacao
            FROM transacoes t
            JOIN subcategoria sc ON t.subcategoria_id = sc.id
            WHERE t.usuario_id = @userId
              AND t.tipo_transacao = 'Despesa'
              AND t.consolidada = true
              AND TO_CHAR(t.data_transacao, 'YYYY-MM') = @currentMonth
            ORDER BY t.valor DESC, t.id
            LIMIT 5
            """,
            new { userId, currentMonth },
            cancellationToken: cancellationToken));

        var fixedCharges = await FixedChargesAsync(connection, userId, includeInstallments: false, cancellationToken);

        return new SpendingAnalysis(
            currentMonth,
            [.. comparison.Select(r => new MonthTotal(r.Mes, ToDouble(r.Total)))],
            [.. categories.Select(r => new CategorySpend(r.NomeMacro, r.NomeSubcategoria, ToDouble(r.Total), (int)r.Quantidade))],
            [.. weekdays.Select(r => new WeekdaySpend(r.DiaSemana.Trim(), ToDouble(r.Total), (int)r.Quantidade))],
            [.. pots.Select(r => new PotUsage(r.NomePote, ToDouble(r.ValorLimite), ToDouble(r.GastoAtual)))],
            [.. top.Select(r => new TopExpense(r.Descricao, ToDouble(r.Valor), Day(r.DataTransacao)))],
            fixedCharges);
    }

    public async Task<ForecastData> GetForecastDataAsync(int userId, DateOnly today, CancellationToken cancellationToken)
    {
        const int historyMonths = 6;
        var start = new DateOnly(today.Year, today.Month, 1).AddDays(-30 * historyMonths);
        var currentMonth = MonthlyComparison.MonthKey(today);

        await using var connection = await connections.OpenAsync(cancellationToken);

        var monthly = await connection.QueryAsync<MonthRow>(new CommandDefinition(
            """
            SELECT TO_CHAR(data_transacao, 'YYYY-MM') AS Mes, SUM(valor) AS Total
            FROM transacoes
            WHERE usuario_id = @userId
              AND tipo_transacao = 'Despesa'
              AND consolidada = true
              AND data_transacao >= @start
            GROUP BY TO_CHAR(data_transacao, 'YYYY-MM')
            ORDER BY Mes DESC
            """,
            new { userId, start },
            cancellationToken: cancellationToken));

        var fixedCharges = await FixedChargesAsync(connection, userId, includeInstallments: true, cancellationToken);

        var spent = await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(
            """
            SELECT SUM(valor)
            FROM transacoes
            WHERE usuario_id = @userId
              AND tipo_transacao = 'Despesa'
              AND consolidada = true
              AND TO_CHAR(data_transacao, 'YYYY-MM') = @currentMonth
            """,
            new { userId, currentMonth },
            cancellationToken: cancellationToken));

        var pending = await connection.QueryAsync<PendingRow>(new CommandDefinition(
            """
            SELECT a.descricao AS Descricao, a.valor_previsto AS ValorPrevisto, a.dia_execucao AS DiaExecucao, c.tipo_conta AS TipoConta, g.nome_grupo AS NomeGrupo
            FROM agendamentos a
            JOIN contas c ON a.conta_id = c.id
            JOIN subcategoria s ON a.subcategoria_id = s.id
            JOIN macrocategoria m ON s.macro_id = m.id
            JOIN grupocategoria g ON m.grupo_id = g.id
            WHERE a.usuario_id = @userId
              AND a.ativo = true
              AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
              AND a.periodicidade = 'MENSAL'
              AND NOT EXISTS (
                  SELECT 1 FROM transacoes t
                  WHERE t.usuario_id = @userId
                    AND TO_CHAR(t.data_transacao, 'YYYY-MM') = @currentMonth
                    AND t.descricao ILIKE '%' || a.descricao || '%'
                    AND t.consolidada = true
              )
            ORDER BY a.dia_execucao, a.id
            """,
            new { userId, currentMonth },
            cancellationToken: cancellationToken));

        return new ForecastData(
            today,
            DateTime.DaysInMonth(today.Year, today.Month),
            historyMonths,
            [.. monthly.Select(r => new MonthTotal(r.Mes, ToDouble(r.Total)))],
            fixedCharges,
            spent is { } total && total != 0 ? ToDouble(total) : 0.0,
            [.. pending.Select(r => new PendingBill(r.Descricao, ToDouble(r.ValorPrevisto), r.DiaExecucao, r.TipoConta, r.NomeGrupo))]);
    }

    /// <summary>Agendamentos ativos do usuário, do maior valor previsto para o menor (a análise só usa FIXO e LEMBRETE_VARIAVEL).</summary>
    private static async Task<IReadOnlyList<FixedCharge>> FixedChargesAsync(
        System.Data.Common.DbConnection connection, int userId, bool includeInstallments, CancellationToken cancellationToken)
    {
        var types = includeInstallments ? "('FIXO', 'PARCELADO', 'LEMBRETE_VARIAVEL')" : "('FIXO', 'LEMBRETE_VARIAVEL')";
        var rows = await connection.QueryAsync<FixedRow>(new CommandDefinition(
            $"""
            SELECT descricao AS Descricao, valor_previsto AS ValorPrevisto, periodicidade AS Periodicidade
            FROM agendamentos
            WHERE usuario_id = @userId
              AND ativo = true
              AND tipo_agendamento IN {types}
            ORDER BY valor_previsto DESC, id
            """,
            new { userId },
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => new FixedCharge(r.Descricao, ToDouble(r.ValorPrevisto), r.Periodicidade))];
    }

    public async Task<IReadOnlyList<MacroMonthTotal>> GetMonthlyComparisonAsync(
        int userId, string month, string previousMonth, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<MacroRow>(new CommandDefinition(
            """
            SELECT TO_CHAR(t.data_transacao, 'YYYY-MM') AS Mes, mc.nome_macro AS NomeMacro, SUM(t.valor) AS Total
            FROM transacoes t
            JOIN subcategoria sc ON t.subcategoria_id = sc.id
            JOIN macrocategoria mc ON sc.macro_id = mc.id
            WHERE t.usuario_id = @userId
              AND t.tipo_transacao = 'Despesa'
              AND t.consolidada = true
              AND TO_CHAR(t.data_transacao, 'YYYY-MM') IN (@month, @previousMonth)
            GROUP BY TO_CHAR(t.data_transacao, 'YYYY-MM'), mc.nome_macro
            ORDER BY Total DESC, Mes DESC, mc.nome_macro
            """,
            new { userId, month, previousMonth },
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => new MacroMonthTotal(r.Mes, r.NomeMacro, ToDouble(r.Total)))];
    }

    public async Task<IReadOnlyList<PieRow>> GetPieRowsAsync(int userId, DateOnly start, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<PieDbRow>(new CommandDefinition(
            """
            SELECT COALESCE(mc.nome_macro, 'Outros') AS Categoria, SUM(ABS(t.valor)) AS Total
            FROM transacoes t
            LEFT JOIN subcategoria sc ON t.subcategoria_id = sc.id
            LEFT JOIN macrocategoria mc ON sc.macro_id = mc.id
            WHERE t.usuario_id = @userId
              AND t.tipo_transacao = 'Despesa'
              AND t.data_transacao >= @start
            GROUP BY mc.nome_macro
            ORDER BY Total DESC, Categoria
            """,
            new { userId, start },
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => new PieRow(r.Categoria, ToDouble(r.Total)))];
    }

    public async Task<IReadOnlyList<BarRow>> GetBarRowsAsync(int userId, DateOnly start, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<BarDbRow>(new CommandDefinition(
            """
            SELECT DATE_TRUNC('month', data_transacao)::date AS Mes,
                   SUM(CASE WHEN tipo_transacao = 'Despesa' THEN ABS(valor) ELSE 0 END) AS Despesas,
                   SUM(CASE WHEN tipo_transacao = 'Renda' THEN valor ELSE 0 END) AS Rendas
            FROM transacoes
            WHERE usuario_id = @userId
              AND data_transacao >= @start
            GROUP BY DATE_TRUNC('month', data_transacao)
            ORDER BY Mes
            """,
            new { userId, start },
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => new BarRow(Day(r.Mes), ToDouble(r.Despesas), ToDouble(r.Rendas)))];
    }

    public async Task<LineSeries> GetLineSeriesAsync(int userId, DateOnly start, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var initial = await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(
            "SELECT COALESCE(SUM(saldo_inicial), 0) FROM contas WHERE usuario_id = @userId",
            new { userId },
            cancellationToken: cancellationToken));

        // O acumulado usa a janela padrão (RANGE até a linha corrente): transações do mesmo dia dividem o mesmo total.
        var rows = await connection.QueryAsync<LineDbRow>(new CommandDefinition(
            """
            SELECT data_transacao AS DataTransacao, SUM(valor) OVER (ORDER BY data_transacao) AS SaldoAcumulado
            FROM transacoes
            WHERE usuario_id = @userId
              AND data_transacao >= @start
            ORDER BY data_transacao
            """,
            new { userId, start },
            cancellationToken: cancellationToken));

        return new LineSeries(initial is { } value ? ToDouble(value) : 0.0, [.. rows.Select(r => new LineRow(Day(r.DataTransacao), ToDouble(r.SaldoAcumulado)))]);
    }
}
