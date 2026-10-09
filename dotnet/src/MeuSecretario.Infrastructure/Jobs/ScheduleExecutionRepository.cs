using System.Data;
using Dapper;
using MeuSecretario.Application.Jobs;
using MeuSecretario.Domain.Jobs;
using MeuSecretario.Domain.Transactions;
using MeuSecretario.Infrastructure.Database;

namespace MeuSecretario.Infrastructure.Jobs;

/// <summary>
/// Motor de agendamentos no banco (spec 013 §4.7). <see cref="ExecuteAsync"/> é a rotina única do desvio 011: uma transação
/// que trava o agendamento, confere a idempotência (<c>agendamento_id</c> no mês, com fallback por descrição, desvio 012),
/// resolve a fatura, grava a transação e avança a parcela.
/// </summary>
public sealed class ScheduleExecutionRepository(IDbConnectionFactory connections) : IScheduleExecutionRepository
{
    private const string Select = """
        SELECT a.id AS Id, a.usuario_id AS UserId, a.conta_id AS AccountId, a.subcategoria_id AS SubcategoryId,
               a.descricao AS Description, a.valor_previsto AS ExpectedAmount, a.tipo_agendamento AS Kind,
               a.periodicidade AS Periodicity, a.dia_execucao AS Day, a.mes_execucao AS Month,
               a.total_parcelas AS TotalInstallments, COALESCE(a.parcelas_executadas, 0) AS ExecutedInstallments,
               COALESCE(a.notificar_antes_dias, 0) AS NotifyBeforeDays, g.nome_grupo AS GroupName,
               c.tipo_conta AS AccountType, c.dia_fechamento AS ClosingDay, c.dia_vencimento AS DueDay,
               u.numero_whatsapp AS WhatsappNumber, a.ativo AS Active
        FROM agendamentos a
        JOIN usuarios u ON a.usuario_id = u.id
        JOIN contas c ON a.conta_id = c.id
        JOIN subcategoria s ON a.subcategoria_id = s.id
        JOIN macrocategoria m ON s.macro_id = m.id
        JOIN grupocategoria g ON m.grupo_id = g.id
        """;

    public async Task<IReadOnlyList<ScheduleInfo>> ListMotorCandidatesAsync(DateOnly today, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"""
            {Select}
            WHERE a.ativo = TRUE
              AND (
                (a.tipo_agendamento IN ('FIXO', 'PARCELADO')
                 AND (a.dia_execucao = @day OR (@lastDay AND a.dia_execucao > @day))
                 AND (a.periodicidade <> 'ANUAL' OR a.mes_execucao = @month))
                OR a.tipo_agendamento = 'LEMBRETE_VARIAVEL')
            ORDER BY a.id
            """,
            JobSql.With(
                ("day", today.Day, DbType.Int32), ("month", today.Month, DbType.Int32), ("lastDay", JobClock.IsLastDayOfMonth(today), DbType.Boolean)),
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => r.ToInfo())];
    }

    public async Task<ScheduleExecutionOutcome> ExecuteAsync(ScheduleExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"{Select} WHERE a.id = @id FOR UPDATE OF a",
            new { id = request.ScheduleId },
            transaction,
            cancellationToken: cancellationToken));

        if (row is null || !row.Active || row.Kind == ScheduleKinds.VariableReminder)
        {
            return new ScheduleExecutionOutcome.NotEligible();
        }

        var schedule = row.ToInfo();
        var isInstallment = schedule.Kind == ScheduleKinds.Installment;
        if (isInstallment && !ScheduleRules.HasInstallmentLeft(schedule.ExecutedInstallments, schedule.TotalInstallments))
        {
            return new ScheduleExecutionOutcome.NotEligible();
        }

        var description = isInstallment
            ? ScheduleRules.InstallmentDescription(schedule.Description, schedule.ExecutedInstallments, schedule.TotalInstallments)
            : schedule.Description;

        var monthStart = new DateOnly(request.Date.Year, request.Date.Month, 1);
        var alreadyPosted = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS (
                SELECT 1 FROM transacoes t
                WHERE t.usuario_id = @userId
                  AND ((t.data_transacao >= @monthStart AND t.data_transacao < @nextMonth
                        AND (t.agendamento_id = @id
                             OR (t.agendamento_id IS NULL AND t.conta_id = @accountId AND t.descricao = @description)))
                       OR (@invoiceId IS NOT NULL AND t.fatura_id = @invoiceId AND t.agendamento_id = @id)))
            """,
            JobSql.With(
                ("userId", schedule.UserId, DbType.Int32), ("monthStart", JobSql.Day(monthStart), DbType.Date),
                ("nextMonth", JobSql.Day(monthStart.AddMonths(1)), DbType.Date), ("id", schedule.Id, DbType.Int32),
                ("accountId", schedule.AccountId, DbType.Int32), ("description", description, DbType.String),
                ("invoiceId", request.InvoiceId, DbType.Int32)),
            transaction,
            cancellationToken: cancellationToken));
        if (alreadyPosted)
        {
            return new ScheduleExecutionOutcome.Skipped("já lançado no mês");
        }

        var invoiceId = request.InvoiceId;
        if (invoiceId is null && schedule.AccountType == ScheduleRules.CardAccountType && row.ClosingDay is > 0 && row.DueDay is > 0)
        {
            invoiceId = await ResolveInvoiceAsync(connection, transaction, schedule.AccountId, row.ClosingDay.Value, row.DueDay.Value, request.Date, cancellationToken);
        }

        var amount = ScheduleRules.SignedAmount(schedule.GroupName, schedule.ExpectedAmount);
        var type = ScheduleRules.TransactionType(schedule.GroupName);
        var transactionId = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            INSERT INTO transacoes (usuario_id, conta_id, subcategoria_id, fatura_id, agendamento_id, descricao, valor,
                                    tipo_transacao, data_transacao, consolidada)
            VALUES (@userId, @accountId, @subcategoryId, @invoiceId, @id, @description, @amount, @type, @date, TRUE)
            RETURNING id
            """,
            JobSql.With(
                ("userId", schedule.UserId, DbType.Int32), ("accountId", schedule.AccountId, DbType.Int32),
                ("subcategoryId", schedule.SubcategoryId, DbType.Int32), ("invoiceId", invoiceId, DbType.Int32),
                ("id", schedule.Id, DbType.Int32), ("description", description, DbType.String), ("amount", amount, DbType.Decimal),
                ("type", type, DbType.String), ("date", JobSql.Day(request.Date), DbType.Date)),
            transaction,
            cancellationToken: cancellationToken));

        if (isInstallment)
        {
            var progress = ScheduleRules.NextProgress(schedule.ExecutedInstallments, schedule.TotalInstallments);
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE agendamentos SET parcelas_executadas = @executed, ativo = @active, updated_at = CURRENT_TIMESTAMP WHERE id = @id",
                new { executed = progress.Executed, active = !progress.Deactivate, id = schedule.Id },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return new ScheduleExecutionOutcome.Posted(transactionId, description, amount, invoiceId);
    }

    private static async Task<int?> ResolveInvoiceAsync(
        Npgsql.NpgsqlConnection connection, Npgsql.NpgsqlTransaction transaction, int accountId, int closingDay, int dueDay,
        DateOnly date, CancellationToken cancellationToken)
    {
        var cycle = InvoiceCycle.ForPurchase(closingDay, dueDay, date);
        var parameters = JobSql.With(
            ("accountId", accountId, DbType.Int32), ("due", JobSql.Day(cycle.Due), DbType.Date), ("closing", JobSql.Day(cycle.Closing), DbType.Date));

        var existing = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT id FROM faturas WHERE conta_id = @accountId AND data_vencimento = @due", parameters, transaction, cancellationToken: cancellationToken));
        if (existing is not null)
        {
            return existing;
        }

        var created = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            """
            INSERT INTO faturas (conta_id, data_vencimento, data_fechamento, status)
            VALUES (@accountId, @due, @closing, 'Aberta')
            ON CONFLICT (conta_id, data_vencimento) DO NOTHING
            RETURNING id
            """,
            parameters,
            transaction,
            cancellationToken: cancellationToken));
        return created ?? await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT id FROM faturas WHERE conta_id = @accountId AND data_vencimento = @due", parameters, transaction, cancellationToken: cancellationToken));
    }

    private sealed class Row
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public int AccountId { get; set; }

        public int SubcategoryId { get; set; }

        public string Description { get; set; } = string.Empty;

        public decimal? ExpectedAmount { get; set; }

        public string Kind { get; set; } = string.Empty;

        public string Periodicity { get; set; } = string.Empty;

        public int Day { get; set; }

        public int? Month { get; set; }

        public int? TotalInstallments { get; set; }

        public int ExecutedInstallments { get; set; }

        public int NotifyBeforeDays { get; set; }

        public string GroupName { get; set; } = string.Empty;

        public string AccountType { get; set; } = string.Empty;

        public int? ClosingDay { get; set; }

        public int? DueDay { get; set; }

        public string WhatsappNumber { get; set; } = string.Empty;

        public bool Active { get; set; }

        public ScheduleInfo ToInfo() => new(
            Id, UserId, AccountId, SubcategoryId, Description, ExpectedAmount, Kind, Periodicity, Day, Month, TotalInstallments,
            ExecutedInstallments, NotifyBeforeDays, GroupName, AccountType, WhatsappNumber);
    }
}
