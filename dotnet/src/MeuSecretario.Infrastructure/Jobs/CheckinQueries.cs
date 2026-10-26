using System.Data;
using Dapper;
using MeuSecretario.Application.Jobs;
using MeuSecretario.Domain.Jobs;
using MeuSecretario.Infrastructure.Database;

namespace MeuSecretario.Infrastructure.Jobs;

/// <summary>
/// Queries do check-in noturno, portadas literalmente de <c>agendamentos_queries.py</c> e
/// <c>faturas_queries.py</c>. Única mudança (desvio 012): o "pagamento" também é achado por descrição quando a transação
/// não tem <c>agendamento_id</c> (as que o motor do Flask gravava). A regra híbrida do ADR 5 (mesmo mês/ano ou até 20 dias
/// depois) segue igual.
/// </summary>
public sealed class CheckinQueries(IDbConnectionFactory connections) : ICheckinQueries
{
    private static readonly string PendingSql = $"""
        WITH ExpectedDates AS (
            SELECT
                a.*,
                c.nome_conta, c.tipo_conta,
                s.nome_sub as categoria,
                m.nome_macro,
                g.nome_grupo,
                CASE
                    WHEN {SchedulePaymentSql.IsRolling("a")} THEN {SchedulePaymentSql.NextDue("a", "@target_date")}
                    WHEN a.dia_execucao <= EXTRACT(DAY FROM (DATE_TRUNC('month', @target_date) + INTERVAL '1 month - 1 day'))
                    THEN (DATE_TRUNC('month', @target_date) + INTERVAL '1 day' * (a.dia_execucao - 1))::date
                    ELSE (DATE_TRUNC('month', @target_date) + INTERVAL '1 month - 1 day')::date
                END as data_esperada_mes_atual,
                CASE
                    WHEN a.periodicidade = 'ANUAL' OR {SchedulePaymentSql.IsRolling("a")} THEN NULL
                    WHEN a.dia_execucao <= EXTRACT(DAY FROM (DATE_TRUNC('month', @target_date - INTERVAL '1 month') + INTERVAL '1 month - 1 day'))
                    THEN (DATE_TRUNC('month', @target_date - INTERVAL '1 month') + INTERVAL '1 day' * (a.dia_execucao - 1))::date
                    ELSE (DATE_TRUNC('month', @target_date - INTERVAL '1 month') + INTERVAL '1 month - 1 day')::date
                END as data_esperada_mes_anterior
            FROM agendamentos a
            JOIN contas c ON a.conta_id = c.id
            JOIN subcategoria s ON a.subcategoria_id = s.id
            JOIN macrocategoria m ON s.macro_id = m.id
            JOIN grupocategoria g ON m.grupo_id = g.id
            WHERE a.usuario_id = @uid
              AND {SchedulePaymentSql.Postable("a")}
        )
        SELECT
            ed.id AS Id, ed.descricao AS Description, ed.valor_previsto AS ExpectedAmount, ed.dia_execucao AS Day,
            ed.conta_id AS AccountId, ed.subcategoria_id AS SubcategoryId, ed.usuario_id AS UserId,
            ed.nome_conta AS AccountName, ed.tipo_conta AS AccountType, ed.categoria AS Category,
            ed.nome_macro AS MacroName, ed.nome_grupo AS GroupName,
            ed.tipo_agendamento AS Kind, ed.parcelas_executadas AS ExecutedInstallments, ed.total_parcelas AS TotalInstallments,
            CASE
                WHEN ed.data_esperada_mes_atual <= @target_date
                     AND ed.data_esperada_mes_atual >= @target_date - INTERVAL '7 days'
                THEN ed.data_esperada_mes_atual
                ELSE ed.data_esperada_mes_anterior
            END as DueDate
        FROM ExpectedDates ed
        WHERE (
            (ed.data_esperada_mes_atual <= @target_date
             AND ed.data_esperada_mes_atual >= @target_date - INTERVAL '7 days'
             AND NOT EXISTS (
                 SELECT 1 FROM transacoes t WHERE {SchedulePaymentSql.Paid("ed", "t", "ed.data_esperada_mes_atual", "ed.descricao")}
             ))
            OR
            (ed.data_esperada_mes_anterior <= @target_date
             AND ed.data_esperada_mes_anterior >= @target_date - INTERVAL '7 days'
             AND NOT EXISTS (
                 SELECT 1 FROM transacoes t WHERE {SchedulePaymentSql.Paid("ed", "t", "ed.data_esperada_mes_anterior", "ed.descricao")}
             ))
        )
        AND (
            ed.periodicidade <> 'ANUAL'
            OR (ed.periodicidade = 'ANUAL' AND ed.mes_execucao = EXTRACT(MONTH FROM @target_date))
        )
        ORDER BY DueDate DESC, ed.nome_grupo, ed.descricao
        """;

    private static readonly string OverdueSql = $"""
        WITH ExpectedDates AS (
            SELECT
                a.*,
                c.nome_conta, c.tipo_conta, c.dia_vencimento, c.dia_fechamento,
                s.nome_sub as categoria,
                m.nome_macro,
                g.nome_grupo,
                CASE
                    WHEN {SchedulePaymentSql.IsRolling("a")} THEN {SchedulePaymentSql.NextDue("a", "@hoje")}
                    WHEN a.dia_execucao <= EXTRACT(DAY FROM (DATE_TRUNC('month', @hoje) + INTERVAL '1 month - 1 day'))
                    THEN (DATE_TRUNC('month', @hoje) + INTERVAL '1 day' * (a.dia_execucao - 1))::date
                    ELSE (DATE_TRUNC('month', @hoje) + INTERVAL '1 month - 1 day')::date
                END as data_esperada_mes_atual,
                CASE
                    WHEN a.periodicidade = 'ANUAL' OR {SchedulePaymentSql.IsRolling("a")} THEN NULL
                    WHEN a.dia_execucao <= EXTRACT(DAY FROM (DATE_TRUNC('month', @hoje - INTERVAL '1 month') + INTERVAL '1 month - 1 day'))
                    THEN (DATE_TRUNC('month', @hoje - INTERVAL '1 month') + INTERVAL '1 day' * (a.dia_execucao - 1))::date
                    ELSE (DATE_TRUNC('month', @hoje - INTERVAL '1 month') + INTERVAL '1 month - 1 day')::date
                END as data_esperada_mes_anterior
            FROM agendamentos a
            JOIN contas c ON a.conta_id = c.id
            JOIN subcategoria s ON a.subcategoria_id = s.id
            JOIN macrocategoria m ON s.macro_id = m.id
            JOIN grupocategoria g ON m.grupo_id = g.id
            WHERE a.usuario_id = @uid
              AND {SchedulePaymentSql.Postable("a")}
              AND NOT (
                  a.tipo_agendamento = 'FIXO'
                  AND g.nome_grupo = 'Despesa'
                  AND c.tipo_conta = 'Cartão de Crédito'
                  AND c.dia_vencimento IS NOT NULL
                  AND c.dia_fechamento IS NOT NULL
              )
        )
        SELECT
            ed.id AS Id, ed.descricao AS Description, ed.valor_previsto AS ExpectedAmount, ed.dia_execucao AS Day,
            ed.conta_id AS AccountId, ed.subcategoria_id AS SubcategoryId, ed.usuario_id AS UserId,
            ed.nome_conta AS AccountName, ed.tipo_conta AS AccountType, ed.categoria AS Category,
            ed.nome_macro AS MacroName, ed.nome_grupo AS GroupName,
            ed.tipo_agendamento AS Kind, ed.parcelas_executadas AS ExecutedInstallments, ed.total_parcelas AS TotalInstallments,
            CASE
                WHEN ed.data_esperada_mes_atual < @hoje
                     AND ed.data_esperada_mes_atual < @hoje - INTERVAL '7 days'
                THEN ed.data_esperada_mes_atual
                ELSE ed.data_esperada_mes_anterior
            END as DueDate
        FROM ExpectedDates ed
        WHERE (
            (ed.data_esperada_mes_atual < @hoje
             AND ed.data_esperada_mes_atual < @hoje - INTERVAL '7 days'
             AND NOT EXISTS (
                 SELECT 1 FROM transacoes t WHERE {SchedulePaymentSql.Paid("ed", "t", "ed.data_esperada_mes_atual", "ed.descricao")}
             ))
            OR
            (ed.data_esperada_mes_anterior < @hoje
             AND ed.data_esperada_mes_anterior >= @data_minima
             AND ed.data_esperada_mes_anterior < @hoje - INTERVAL '7 days'
             AND NOT EXISTS (
                 SELECT 1 FROM transacoes t WHERE {SchedulePaymentSql.Paid("ed", "t", "ed.data_esperada_mes_anterior", "ed.descricao")}
             ))
        )
        AND (
            ed.periodicidade <> 'ANUAL'
            OR (ed.periodicidade = 'ANUAL' AND ed.mes_execucao = EXTRACT(MONTH FROM @hoje))
        )
        ORDER BY DueDate DESC, ed.nome_grupo, ed.descricao
        """;

    private const string OverdueInvoicesSql = """
        SELECT f.id AS Id, c.nome_conta AS Name, f.data_vencimento AS DueDate, f.status AS Status, f.conta_id AS AccountId,
               COALESCE(SUM(CASE WHEN t.valor < 0 THEN ABS(t.valor) ELSE 0 END), 0) AS Total
        FROM faturas f
        JOIN contas c ON f.conta_id = c.id
        LEFT JOIN transacoes t ON f.id = t.fatura_id
        WHERE c.usuario_id = @uid
          AND f.status IN ('Aberta', 'Fechada')
          AND f.data_vencimento < @hoje
          AND f.data_vencimento >= @limite_inferior
        GROUP BY f.id, c.nome_conta, f.data_vencimento, f.status, f.conta_id
        ORDER BY f.data_vencimento DESC
        """;

    private const string DueTodaySql = """
        SELECT f.id AS Id, c.nome_conta AS Name, f.data_vencimento AS DueDate, f.status AS Status, f.conta_id AS AccountId,
               COALESCE(SUM(CASE WHEN t.valor < 0 THEN ABS(t.valor) ELSE 0 END), 0) AS Total
        FROM faturas f
        JOIN contas c ON f.conta_id = c.id
        LEFT JOIN transacoes t ON f.id = t.fatura_id
        WHERE c.usuario_id = @uid
          AND f.status IN ('Aberta', 'Fechada')
          AND f.data_vencimento = @hoje
        GROUP BY f.id, c.nome_conta, f.data_vencimento, f.status, f.conta_id
        ORDER BY c.nome_conta
        """;

    public async Task<CheckinSnapshot> GetSnapshotAsync(int userId, DateOnly today, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);

        var pending = await connection.QueryAsync<BillRow>(new CommandDefinition(
            PendingSql,
            JobSql.With(("uid", userId, DbType.Int32), ("target_date", JobSql.Day(today), DbType.Date)),
            cancellationToken: cancellationToken));

        var overdue = await connection.QueryAsync<BillRow>(new CommandDefinition(
            OverdueSql,
            JobSql.With(
                ("uid", userId, DbType.Int32), ("hoje", JobSql.Day(today), DbType.Date),
                ("data_minima", JobSql.Day(today.AddDays(-30)), DbType.Date)),
            cancellationToken: cancellationToken));

        var invoiceParameters = JobSql.With(
            ("uid", userId, DbType.Int32), ("hoje", JobSql.Day(today), DbType.Date),
            ("limite_inferior", JobSql.Day(today.AddDays(-30)), DbType.Date));
        var overdueInvoices = await connection.QueryAsync<InvoiceRow>(new CommandDefinition(
            OverdueInvoicesSql, invoiceParameters, cancellationToken: cancellationToken));
        var dueToday = await connection.QueryAsync<InvoiceRow>(new CommandDefinition(
            DueTodaySql, invoiceParameters, cancellationToken: cancellationToken));

        var pendingBills = pending.Select(ToBill).ToList();
        var overdueBills = CheckinRules.Deduplicate(pendingBills, overdue.Select(ToBill));
        return new CheckinSnapshot(
            pendingBills,
            overdueBills,
            [.. overdueInvoices.Select(i => new OverdueInvoice(i.Id, i.Name, JobSql.ToDate(i.DueDate), i.Status, i.Total, i.AccountId))],
            [.. dueToday.Select(i => new DueTodayInvoice(i.Id, i.Name, JobSql.ToDate(i.DueDate), i.Status, i.Total, i.AccountId))]);
    }

    private static CheckinBill ToBill(BillRow row) => new(
        row.Id, row.Description, row.ExpectedAmount, row.Day, row.AccountId, row.SubcategoryId, row.UserId, row.AccountName,
        row.AccountType, row.Category, row.MacroName, row.GroupName, row.Kind, row.ExecutedInstallments, row.TotalInstallments,
        row.DueDate is { } due ? JobSql.ToDate(due) : null);

    private sealed class BillRow
    {
        public int Id { get; set; }

        public string Description { get; set; } = string.Empty;

        public decimal? ExpectedAmount { get; set; }

        public int Day { get; set; }

        public int AccountId { get; set; }

        public int SubcategoryId { get; set; }

        public int UserId { get; set; }

        public string AccountName { get; set; } = string.Empty;

        public string AccountType { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public string MacroName { get; set; } = string.Empty;

        public string GroupName { get; set; } = string.Empty;

        public string Kind { get; set; } = string.Empty;

        public int? ExecutedInstallments { get; set; }

        public int? TotalInstallments { get; set; }

        public DateTime? DueDate { get; set; }
    }

    private sealed class InvoiceRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public DateTime DueDate { get; set; }

        public string Status { get; set; } = string.Empty;

        public decimal Total { get; set; }

        public int AccountId { get; set; }
    }
}
