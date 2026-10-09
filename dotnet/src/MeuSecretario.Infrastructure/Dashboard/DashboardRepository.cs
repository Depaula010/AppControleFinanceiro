using System.Data;
using System.Globalization;
using Dapper;
using MeuSecretario.Application.Dashboard;
using MeuSecretario.Domain.Dashboard;
using MeuSecretario.Infrastructure.Database;

namespace MeuSecretario.Infrastructure.Dashboard;

/// <summary>
/// Leituras do dashboard com Dapper (spec 006). SQL fixo, todo valor parametrizado; nada é escrito. A coluna de ativo
/// é <c>ativa</c> em <c>contas</c> e <c>ativo</c> em <c>agendamentos</c>. Mês e dia da semana saem do banco em
/// formato fixo (<c>TO_CHAR(..., 'YYYY-MM')</c>, <c>EXTRACT(DOW)</c>), sem depender de <c>lc_time</c> (desvio 009).
/// </summary>
public sealed class DashboardRepository(IDbConnectionFactory connections) : IDashboardRepository
{
    public async Task<IReadOnlyList<decimal>> ListActiveAccountBalancesAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var balances = await connection.QueryAsync<decimal>(new CommandDefinition(
            """
            SELECT c.saldo_inicial + COALESCE(SUM(t.valor), 0) AS saldo
            FROM contas c
            LEFT JOIN transacoes t ON c.id = t.conta_id AND t.consolidada = true
            WHERE c.usuario_id = @userId AND c.ativa = true
            GROUP BY c.id, c.tipo_conta, c.nome_conta, c.saldo_inicial
            ORDER BY c.tipo_conta, c.nome_conta, c.id
            """,
            new { userId },
            cancellationToken: cancellationToken));

        return balances.ToList();
    }

    public async Task<IReadOnlyList<TypeTotal>> SumByTypeAsync(int userId, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<TypeTotalRow>(new CommandDefinition(
            """
            SELECT tipo_transacao AS Type, SUM(valor) AS Total
            FROM transacoes
            WHERE usuario_id = @userId
              AND data_transacao >= @from AND data_transacao <= @to
              AND consolidada = true
            GROUP BY tipo_transacao
            """,
            Params(userId, ("from", firstDay), ("to", lastDay)),
            cancellationToken: cancellationToken));

        return rows.Select(r => new TypeTotal(r.Type, r.Total ?? 0m)).ToList();
    }

    public async Task<IReadOnlyList<MonthlyExpense>> MonthlyExpensesAsync(int userId, DateOnly from, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<MonthlyRow>(new CommandDefinition(
            """
            SELECT TO_CHAR(data_transacao, 'YYYY-MM') AS Month, SUM(valor) AS Total
            FROM transacoes
            WHERE usuario_id = @userId
              AND tipo_transacao = 'Despesa'
              AND consolidada = true
              AND data_transacao >= @from
            GROUP BY TO_CHAR(data_transacao, 'YYYY-MM')
            ORDER BY TO_CHAR(data_transacao, 'YYYY-MM')
            """,
            Params(userId, ("from", from)),
            cancellationToken: cancellationToken));

        return rows.Select(r => new MonthlyExpense(r.Month, r.Total)).ToList();
    }

    public async Task<IReadOnlyList<CategoryExpense>> CategoryExpensesAsync(
        int userId, DateOnly from, DateOnly toExclusive, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);

        // Desvio 092: a soma é negativa (despesa), então ASC traz o MAIOR gasto primeiro (o Flask usava DESC e levava as 10
        // de menor gasto). O desempate por nome só fixa a ordem onde o Flask era indefinido.
        var rows = await connection.QueryAsync<CategoryRow>(new CommandDefinition(
            """
            SELECT mc.nome_macro AS Macro, sc.nome_sub AS Subcategory, SUM(t.valor) AS Total, COUNT(*)::int AS Count
            FROM transacoes t
            JOIN subcategoria sc ON t.subcategoria_id = sc.id
            JOIN macrocategoria mc ON sc.macro_id = mc.id
            WHERE t.usuario_id = @userId
              AND t.tipo_transacao = 'Despesa'
              AND t.consolidada = true
              AND t.data_transacao >= @from AND t.data_transacao < @toExclusive
            GROUP BY mc.nome_macro, sc.nome_sub
            ORDER BY SUM(t.valor) ASC, mc.nome_macro, sc.nome_sub
            LIMIT 10
            """,
            Params(userId, ("from", from), ("toExclusive", toExclusive)),
            cancellationToken: cancellationToken));

        return rows.Select(r => new CategoryExpense(r.Macro, r.Subcategory, r.Total, r.Count)).ToList();
    }

    public async Task<IReadOnlyList<WeekdayExpense>> WeekdayExpensesAsync(int userId, DateOnly from, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<WeekdayRow>(new CommandDefinition(
            """
            SELECT EXTRACT(DOW FROM data_transacao)::int AS DayOfWeek, SUM(valor) AS Total, COUNT(*)::int AS Count
            FROM transacoes
            WHERE usuario_id = @userId
              AND tipo_transacao = 'Despesa'
              AND consolidada = true
              AND data_transacao >= @from
            GROUP BY EXTRACT(DOW FROM data_transacao)
            ORDER BY EXTRACT(DOW FROM data_transacao)
            """,
            Params(userId, ("from", from)),
            cancellationToken: cancellationToken));

        return rows.Select(r => new WeekdayExpense(r.DayOfWeek, r.Total, r.Count)).ToList();
    }

    public async Task<IReadOnlyList<RecentTransaction>> ListRecentAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<RecentRow>(new CommandDefinition(
            """
            SELECT t.id AS Id, t.descricao AS Description, t.valor AS Amount, t.tipo_transacao AS DbType,
                   t.data_transacao AS Date, m.nome_macro AS Category, c.nome_conta AS AccountName
            FROM transacoes t
            JOIN subcategoria s ON t.subcategoria_id = s.id
            JOIN macrocategoria m ON s.macro_id = m.id
            JOIN contas c ON t.conta_id = c.id
            WHERE t.usuario_id = @userId
            ORDER BY t.data_transacao DESC, t.created_at DESC, t.id DESC
            LIMIT 10
            """,
            new { userId },
            cancellationToken: cancellationToken));

        return rows.Select(r => new RecentTransaction(
            r.Id, r.Description, r.Amount, r.DbType, DateOnly.FromDateTime(r.Date), r.Category, r.AccountName)).ToList();
    }

    public async Task<IReadOnlyList<ScheduleRow>> ListActiveSchedulesAsync(
        int userId, DateOnly paidFrom, DateOnly paidToExclusive, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);

        // Desvio 095: fora os agendamentos de receita (grupo Renda ou macro Receitas Gerais; o mesmo predicado da 005).
        // Desvio 094: PaidDates = datas das transações do próprio agendamento (agendamento_id) ou, sem vínculo, de mesma
        // descrição, na janela dos alertas (desvio 012).
        var rows = await connection.QueryAsync<ScheduleDbRow>(new CommandDefinition(
            """
            SELECT a.id AS Id, a.descricao AS Description, a.valor_previsto AS Amount, a.tipo_agendamento AS Type,
                   a.periodicidade AS Periodicity, a.dia_execucao AS Day, a.mes_execucao AS Month,
                   a.data_inicio AS StartDate, sc.nome_sub AS SubcategoryName,
                   ARRAY(
                       SELECT DISTINCT t.data_transacao::text
                       FROM transacoes t
                       WHERE t.usuario_id = a.usuario_id
                         AND t.data_transacao >= @paidFrom AND t.data_transacao < @paidTo
                         AND (t.agendamento_id = a.id OR (t.agendamento_id IS NULL AND t.descricao = a.descricao))
                   ) AS PaidDates
            FROM agendamentos a
            LEFT JOIN subcategoria sc ON a.subcategoria_id = sc.id
            LEFT JOIN macrocategoria mc ON sc.macro_id = mc.id
            LEFT JOIN grupocategoria g ON mc.grupo_id = g.id
            WHERE a.usuario_id = @userId AND a.ativo = true
              AND NOT (COALESCE(g.nome_grupo, '') = 'Renda'
                   OR (COALESCE(g.nome_grupo, '') = 'Geral' AND COALESCE(mc.nome_macro, '') = 'Receitas Gerais'))
            ORDER BY a.dia_execucao, a.id
            """,
            Params(userId, ("paidFrom", paidFrom), ("paidTo", paidToExclusive)),
            cancellationToken: cancellationToken));

        return rows.Select(r => new ScheduleRow(
            r.Id, r.Description, r.Amount, r.Type, r.Periodicity, r.Day, r.Month,
            r.StartDate is { } start ? DateOnly.FromDateTime(start) : null, r.SubcategoryName,
            (r.PaidDates ?? []).Select(d => DateOnly.ParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture)).ToList())).ToList();
    }

    /// <summary>O Dapper não conhece <see cref="DateOnly"/>: a data viaja como <see cref="DateTime"/> à meia-noite.</summary>
    private static DynamicParameters Params(int userId, params (string Name, DateOnly Value)[] dates)
    {
        var parameters = new DynamicParameters();
        parameters.Add("userId", userId, DbType.Int32);
        foreach (var (name, value) in dates)
        {
            parameters.Add(name, value.ToDateTime(TimeOnly.MinValue), DbType.Date);
        }

        return parameters;
    }

    private sealed class TypeTotalRow
    {
        public string Type { get; set; } = "";

        public decimal? Total { get; set; }
    }

    private sealed class MonthlyRow
    {
        public string Month { get; set; } = "";

        public decimal Total { get; set; }
    }

    private sealed class CategoryRow
    {
        public string Macro { get; set; } = "";

        public string Subcategory { get; set; } = "";

        public decimal Total { get; set; }

        public int Count { get; set; }
    }

    private sealed class WeekdayRow
    {
        public int DayOfWeek { get; set; }

        public decimal Total { get; set; }

        public int Count { get; set; }
    }

    private sealed class RecentRow
    {
        public int Id { get; set; }

        public string Description { get; set; } = "";

        public decimal Amount { get; set; }

        public string DbType { get; set; } = "";

        public DateTime Date { get; set; }

        public string Category { get; set; } = "";

        public string AccountName { get; set; } = "";
    }

    private sealed class ScheduleDbRow
    {
        public int Id { get; set; }

        public string Description { get; set; } = "";

        public decimal? Amount { get; set; }

        public string Type { get; set; } = "";

        public string? Periodicity { get; set; }

        public int? Day { get; set; }

        public int? Month { get; set; }

        public DateTime? StartDate { get; set; }

        public string? SubcategoryName { get; set; }

        public string[]? PaidDates { get; set; }
    }
}
