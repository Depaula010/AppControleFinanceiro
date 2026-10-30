using System.Data;
using System.Globalization;
using Dapper;
using MeuSecretario.Application.Jobs;
using MeuSecretario.Domain.Jobs;
using MeuSecretario.Infrastructure.Database;

namespace MeuSecretario.Infrastructure.Jobs;

/// <summary>Parâmetros tipados: o Dapper não conhece <see cref="DateOnly"/>; as datas viajam como <see cref="DbType.Date"/>.</summary>
internal static class JobSql
{
    public static DateTime Day(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    public static DateOnly ToDate(DateTime value) => DateOnly.FromDateTime(value);

    public static string Time(TimeOnly time) => time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    public static DynamicParameters With(params (string Name, object? Value, DbType Type)[] items)
    {
        var parameters = new DynamicParameters();
        foreach (var (name, value, type) in items)
        {
            parameters.Add(name, value, type);
        }

        return parameters;
    }

    /// <summary>
    /// "Já lançado no mês" (desvios 011 e 012): transação do próprio <c>agendamento_id</c> no mês/ano, ou, sem
    /// <c>agendamento_id</c>, com a mesma descrição do usuário. Espera <c>@mes</c> e <c>@ano</c>; alias do agendamento em <c>a</c>.
    /// </summary>
    public const string LaunchedInMonth = """
        EXISTS (
            SELECT 1 FROM transacoes t
            WHERE t.usuario_id = a.usuario_id
              AND EXTRACT(MONTH FROM t.data_transacao) = @mes
              AND EXTRACT(YEAR FROM t.data_transacao) = @ano
              AND (t.agendamento_id = a.id OR (t.agendamento_id IS NULL AND t.descricao = a.descricao))
        )
        """;
}

/// <summary><c>notificationconfigs</c> para os jobs (spec 013).</summary>
public sealed class JobNotificationConfigRepository(IDbConnectionFactory connections) : IJobNotificationConfigRepository
{
    public async Task<IReadOnlyList<NotifiedUser>> ListMorningUsersAsync(TimeOnly hour, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<UserRow>(new CommandDefinition(
            """
            SELECT DISTINCT u.id AS UserId, u.numero_whatsapp AS WhatsappNumber, u.nome AS Name
            FROM notificationconfigs nc
            JOIN usuarios u ON nc.usuario_id = u.id
            WHERE nc.resumo_matinal_hora = @hour::time
              AND (nc.resumo_matinal_ativo = TRUE OR nc.alertas_financeiros_ativos = TRUE)
            ORDER BY u.id
            """,
            new { hour = JobSql.Time(hour) },
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => new NotifiedUser(r.UserId, r.WhatsappNumber))];
    }

    public async Task<IReadOnlyList<NotifiedUser>> ListCheckinUsersAsync(TimeOnly hour, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<UserRow>(new CommandDefinition(
            """
            SELECT u.id AS UserId, u.numero_whatsapp AS WhatsappNumber, u.nome AS Name
            FROM notificationconfigs nc
            JOIN usuarios u ON nc.usuario_id = u.id
            WHERE nc.checkin_noturno_ativo = TRUE
              AND nc.checkin_noturno_hora = @hour::time
            ORDER BY u.id
            """,
            new { hour = JobSql.Time(hour) },
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => new NotifiedUser(r.UserId, r.WhatsappNumber))];
    }

    public async Task<IReadOnlyList<BillsReminderUser>> ListBillsReminderUsersAsync(TimeOnly min, TimeOnly max, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<UserRow>(new CommandDefinition(
            """
            SELECT u.id AS UserId, u.numero_whatsapp AS WhatsappNumber, u.nome AS Name
            FROM notificationconfigs nc
            JOIN usuarios u ON nc.usuario_id = u.id
            WHERE nc.alertas_financeiros_ativos = TRUE
              AND nc.resumo_matinal_hora BETWEEN @min::time AND @max::time
            ORDER BY u.id
            """,
            new { min = JobSql.Time(min), max = JobSql.Time(max) },
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => new BillsReminderUser(r.UserId, r.WhatsappNumber, r.Name))];
    }

    public async Task<UserNotificationConfig> GetOrCreateAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO notificationconfigs (usuario_id) VALUES (@userId) ON CONFLICT (usuario_id) DO NOTHING",
            new { userId },
            cancellationToken: cancellationToken));
        var row = await connection.QuerySingleAsync<ConfigRow>(new CommandDefinition(
            """
            SELECT resumo_matinal_ativo AS MorningSummary, COALESCE(alertas_financeiros_ativos, FALSE) AS FinancialAlerts
            FROM notificationconfigs WHERE usuario_id = @userId
            """,
            new { userId },
            cancellationToken: cancellationToken));
        return new UserNotificationConfig(row.MorningSummary, row.FinancialAlerts);
    }

    private sealed class UserRow
    {
        public int UserId { get; set; }

        public string WhatsappNumber { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
    }

    private sealed class ConfigRow
    {
        public bool MorningSummary { get; set; }

        public bool FinancialAlerts { get; set; }
    }
}

/// <summary>Cidade, estado e WhatsApp do usuário para o resumo matinal.</summary>
public sealed class BriefingDataRepository(IDbConnectionFactory connections) : IBriefingDataRepository
{
    public async Task<BriefingUser> GetLocationAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            "SELECT cidade AS City, estado AS State, numero_whatsapp AS Whatsapp FROM usuarios WHERE id = @userId",
            new { userId },
            cancellationToken: cancellationToken));
        return row is null ? new BriefingUser(null, null, null) : new BriefingUser(row.City, row.State, row.Whatsapp);
    }

    private sealed class Row
    {
        public string? City { get; set; }

        public string? State { get; set; }

        public string? Whatsapp { get; set; }
    }
}

/// <summary><c>get_upcoming_bills_and_invoices</c> (spec 013 RN-013-30 e 31).</summary>
public sealed class FinancialAlertsRepository(IDbConnectionFactory connections) : IFinancialAlertsRepository
{
    public async Task<FinancialAlerts> GetUpcomingAsync(int userId, DateOnly day, CancellationToken cancellationToken)
    {
        var tomorrow = day.AddDays(1);
        await using var connection = await connections.OpenAsync(cancellationToken);

        var today = await BillsAsync(connection, userId, day, cancellationToken);
        var next = await BillsAsync(connection, userId, tomorrow, cancellationToken);

        var invoices = await connection.QueryAsync<InvoiceRow>(new CommandDefinition(
            """
            SELECT c.nome_conta AS Card, f.data_vencimento AS DueDate,
                   COALESCE(SUM(CASE WHEN t.valor < 0 THEN ABS(t.valor) ELSE 0 END), 0) AS Total
            FROM faturas f
            JOIN contas c ON f.conta_id = c.id
            LEFT JOIN transacoes t ON f.id = t.fatura_id
            WHERE c.usuario_id = @uid
              AND f.status IN ('Aberta', 'Fechada')
              AND (f.data_vencimento = @hoje OR f.data_vencimento = @amanha)
            GROUP BY c.nome_conta, f.data_vencimento, f.status
            ORDER BY f.data_vencimento ASC
            """,
            JobSql.With(
                ("uid", userId, DbType.Int32), ("hoje", JobSql.Day(day), DbType.Date), ("amanha", JobSql.Day(tomorrow), DbType.Date)),
            cancellationToken: cancellationToken));

        var invoicesToday = new List<AlertInvoice>();
        var invoicesTomorrow = new List<AlertInvoice>();
        foreach (var invoice in invoices)
        {
            var due = JobSql.ToDate(invoice.DueDate);
            var item = new AlertInvoice(invoice.Card, (double)invoice.Total, due);
            if (due == day)
            {
                invoicesToday.Add(item);
            }
            else if (due == tomorrow)
            {
                invoicesTomorrow.Add(item);
            }
        }

        return new FinancialAlerts(today, next, invoicesToday, invoicesTomorrow);
    }

    private static async Task<IReadOnlyList<AlertBill>> BillsAsync(
        Npgsql.NpgsqlConnection connection, int userId, DateOnly day, CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<BillRow>(new CommandDefinition(
            $"""
            SELECT a.id AS Id, a.descricao AS Description, a.valor_previsto AS Amount, s.nome_sub AS Category,
                   c.nome_conta AS Account, g.nome_grupo AS GroupName, c.tipo_conta AS AccountType
            FROM agendamentos a
            JOIN subcategoria s ON a.subcategoria_id = s.id
            JOIN macrocategoria m ON s.macro_id = m.id
            JOIN grupocategoria g ON m.grupo_id = g.id
            JOIN contas c ON a.conta_id = c.id
            WHERE a.usuario_id = @uid
              AND a.ativo = TRUE
              AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
              AND a.dia_execucao = @dia
              AND (a.periodicidade <> 'ANUAL' OR EXTRACT(MONTH FROM a.data_inicio) = @mes)
              AND (c.tipo_conta = 'Cartão de Crédito' OR NOT {JobSql.LaunchedInMonth})
            ORDER BY a.dia_execucao ASC, a.id
            """,
            JobSql.With(
                ("uid", userId, DbType.Int32), ("dia", day.Day, DbType.Int32), ("mes", day.Month, DbType.Int32), ("ano", day.Year, DbType.Int32)),
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => new AlertBill(r.Id, r.Description, (double)(r.Amount ?? 0m), r.Category, r.Account, AlertBill.TypeFor(r.GroupName), r.AccountType == ScheduleRules.CardAccountType))];
    }

    private sealed class BillRow
    {
        public int Id { get; set; }

        public string Description { get; set; } = string.Empty;

        public decimal? Amount { get; set; }

        public string Category { get; set; } = string.Empty;

        public string Account { get; set; } = string.Empty;

        public string GroupName { get; set; } = string.Empty;

        public string AccountType { get; set; } = string.Empty;
    }

    private sealed class InvoiceRow
    {
        public string Card { get; set; } = string.Empty;

        public DateTime DueDate { get; set; }

        public decimal Total { get; set; }
    }
}

/// <summary>Contas do dia seguinte para <c>processar_contas_vencer</c> (RN-013-33).</summary>
public sealed class BillsReminderRepository(IDbConnectionFactory connections) : IBillsReminderRepository
{
    public async Task<IReadOnlyList<ReminderBill>> ListAsync(int userId, DateOnly target, DateOnly today, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            $"""
            SELECT a.descricao AS Description, a.valor_previsto AS Amount, s.nome_sub AS Category,
                   c.tipo_conta AS AccountType, g.nome_grupo AS GroupName
            FROM agendamentos a
            JOIN subcategoria s ON a.subcategoria_id = s.id
            JOIN macrocategoria m ON s.macro_id = m.id
            JOIN grupocategoria g ON m.grupo_id = g.id
            JOIN contas c ON a.conta_id = c.id
            WHERE a.usuario_id = @uid
              AND a.ativo = TRUE
              AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
              AND a.dia_execucao = @dia
              AND (a.periodicidade <> 'ANUAL' OR a.mes_execucao = @mesAlvo)
              AND NOT {JobSql.LaunchedInMonth}
            ORDER BY a.dia_execucao, a.id
            """,
            JobSql.With(
                ("uid", userId, DbType.Int32), ("dia", target.Day, DbType.Int32), ("mesAlvo", target.Month, DbType.Int32),
                ("mes", today.Month, DbType.Int32), ("ano", today.Year, DbType.Int32)),
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => new ReminderBill(r.Description, (double)(r.Amount ?? 0m), r.Category, r.AccountType, r.GroupName))];
    }

    private sealed class Row
    {
        public string Description { get; set; } = string.Empty;

        public decimal? Amount { get; set; }

        public string Category { get; set; } = string.Empty;

        public string AccountType { get; set; } = string.Empty;

        public string GroupName { get; set; } = string.Empty;
    }
}

/// <summary>Faturas para o processador (RN-013-50 a 52).</summary>
public sealed class InvoiceJobRepository(IDbConnectionFactory connections) : IInvoiceJobRepository
{
    public async Task<IReadOnlyList<ClosedInvoice>> CloseExpiredAsync(DateOnly today, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<ClosedRow>(new CommandDefinition(
            """
            SELECT f.id AS Id, f.conta_id AS AccountId, f.data_fechamento AS ClosingDate, f.data_vencimento AS DueDate,
                   c.nome_conta AS AccountName, c.usuario_id AS UserId, u.numero_whatsapp AS WhatsappNumber,
                   COALESCE(SUM(CASE WHEN t.valor < 0 THEN ABS(t.valor) ELSE 0 END), 0) AS Total
            FROM faturas f
            JOIN contas c ON f.conta_id = c.id
            JOIN usuarios u ON c.usuario_id = u.id
            LEFT JOIN transacoes t ON f.id = t.fatura_id
            WHERE f.status = 'Aberta' AND f.data_fechamento < @today
            GROUP BY f.id, f.conta_id, f.data_fechamento, f.data_vencimento, c.nome_conta, c.usuario_id, u.numero_whatsapp
            ORDER BY f.data_fechamento ASC, f.id
            """,
            JobSql.With(("today", JobSql.Day(today), DbType.Date)),
            transaction,
            cancellationToken: cancellationToken))).AsList();

        if (rows.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE faturas SET status = 'Fechada', updated_at = CURRENT_TIMESTAMP WHERE id = ANY(@ids)",
                new { ids = rows.Select(r => r.Id).ToArray() },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return [.. rows.Select(r => new ClosedInvoice(
            r.Id, r.AccountId, r.AccountName, JobSql.ToDate(r.ClosingDate), JobSql.ToDate(r.DueDate), (double)r.Total, r.UserId, r.WhatsappNumber))];
    }

    public async Task<IReadOnlyList<OpenInvoiceRef>> ListOpenAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<OpenRow>(new CommandDefinition(
            """
            SELECT f.id AS Id, f.conta_id AS AccountId, f.data_vencimento AS DueDate, c.nome_conta AS AccountName
            FROM faturas f JOIN contas c ON f.conta_id = c.id
            WHERE f.status = 'Aberta'
            ORDER BY f.data_vencimento, f.id
            """,
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => new OpenInvoiceRef(r.Id, r.AccountId, JobSql.ToDate(r.DueDate), r.AccountName))];
    }

    public async Task<IReadOnlyList<DueSoonInvoice>> ListDueSoonAsync(DateOnly dueDate, int daysUntilDue, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<ClosedRow>(new CommandDefinition(
            """
            SELECT f.id AS Id, f.conta_id AS AccountId, f.data_vencimento AS DueDate, c.nome_conta AS AccountName,
                   c.usuario_id AS UserId, u.numero_whatsapp AS WhatsappNumber,
                   COALESCE(SUM(CASE WHEN t.valor < 0 THEN ABS(t.valor) ELSE 0 END), 0) AS Total
            FROM faturas f
            JOIN contas c ON f.conta_id = c.id
            JOIN usuarios u ON c.usuario_id = u.id
            LEFT JOIN transacoes t ON f.id = t.fatura_id
            WHERE f.status = 'Fechada' AND f.data_vencimento = @due
            GROUP BY f.id, f.conta_id, f.data_vencimento, c.nome_conta, c.usuario_id, u.numero_whatsapp
            ORDER BY f.id
            """,
            JobSql.With(("due", JobSql.Day(dueDate), DbType.Date)),
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => new DueSoonInvoice(
            r.Id, r.AccountId, r.AccountName, JobSql.ToDate(r.DueDate), (double)r.Total, r.UserId, r.WhatsappNumber, daysUntilDue))];
    }

    private sealed class ClosedRow
    {
        public int Id { get; set; }

        public int AccountId { get; set; }

        public DateTime ClosingDate { get; set; }

        public DateTime DueDate { get; set; }

        public string AccountName { get; set; } = string.Empty;

        public int UserId { get; set; }

        public string WhatsappNumber { get; set; } = string.Empty;

        public decimal Total { get; set; }
    }

    private sealed class OpenRow
    {
        public int Id { get; set; }

        public int AccountId { get; set; }

        public DateTime DueDate { get; set; }

        public string AccountName { get; set; } = string.Empty;
    }
}
