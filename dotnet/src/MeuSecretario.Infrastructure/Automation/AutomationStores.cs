using System.Data;
using System.Globalization;
using Dapper;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Automation;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Infrastructure.Database;
using Npgsql;

namespace MeuSecretario.Infrastructure.Automation;

/// <summary><c>agendamentos</c> da reserva de emergência: leitura do PATCH, <c>UPDATE</c> do flag e a listagem (<c>reserve_handler.py</c>).</summary>
public sealed class ReserveScheduleStore(IDbConnectionFactory connections) : IReserveScheduleStore
{
    private sealed class ScheduleRow
    {
        public string Descricao { get; set; } = "";

        public decimal? ValorPrevisto { get; set; }

        public string Periodicidade { get; set; } = "";

        public string TipoAgendamento { get; set; } = "";
    }

    private sealed class ListRow
    {
        public int Id { get; set; }

        public string Descricao { get; set; } = "";

        public decimal? ValorPrevisto { get; set; }

        public string Periodicidade { get; set; } = "";

        public string TipoAgendamento { get; set; } = "";

        public int DiaExecucao { get; set; }

        public bool? IncluirNaReserva { get; set; }

        public string Categoria { get; set; } = "";

        public string MacroCategoria { get; set; } = "";

        public string Grupo { get; set; } = "";
    }

    public async Task<ReserveScheduleRow?> FindActiveScheduleAsync(int userId, int scheduleId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync<ScheduleRow>(new CommandDefinition(
            """
            SELECT descricao AS Descricao, valor_previsto AS ValorPrevisto, periodicidade AS Periodicidade, tipo_agendamento AS TipoAgendamento
            FROM agendamentos WHERE id = @scheduleId AND usuario_id = @userId AND ativo = TRUE
            """,
            new { scheduleId, userId },
            cancellationToken: cancellationToken));
        return row is null ? null : new ReserveScheduleRow(row.Descricao, row.ValorPrevisto, row.Periodicidade, row.TipoAgendamento);
    }

    public async Task SetIncludeInReserveAsync(int userId, int scheduleId, bool include, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE agendamentos SET incluir_na_reserva = @include WHERE id = @scheduleId AND usuario_id = @userId",
            new { include, scheduleId, userId },
            cancellationToken: cancellationToken));
    }

    public async Task<ReserveListPage> ListAsync(int userId, ReserveListFilter filter, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);

        var parameters = new DynamicParameters();
        parameters.Add("userId", userId, DbType.Int32);
        var where = new List<string>();
        if (filter.IncludeInReserve is { } include)
        {
            where.Add("a.incluir_na_reserva = @include");
            parameters.Add("include", include, DbType.Boolean);
        }

        if (filter.Periodicity is { } periodicity)
        {
            where.Add("a.periodicidade = @periodicity");
            parameters.Add("periodicity", periodicity, DbType.String);
        }

        if (filter.Category is { } category)
        {
            where.Add("s.nome_sub ILIKE @category");
            parameters.Add("category", "%" + category + "%", DbType.String);
        }

        var extra = string.Concat(where.Select(w => " AND " + w));
        parameters.Add("limit", filter.Limit, DbType.Int64);
        parameters.Add("offset", filter.Offset, DbType.Int64);

        var rows = await connection.QueryAsync<ListRow>(new CommandDefinition(
            $"""
            SELECT a.id AS Id, a.descricao AS Descricao, a.valor_previsto AS ValorPrevisto, a.periodicidade AS Periodicidade,
                   a.tipo_agendamento AS TipoAgendamento, a.dia_execucao AS DiaExecucao, a.incluir_na_reserva AS IncluirNaReserva,
                   s.nome_sub AS Categoria, m.nome_macro AS MacroCategoria, g.nome_grupo AS Grupo
            FROM agendamentos a
            JOIN subcategoria s ON a.subcategoria_id = s.id
            JOIN macrocategoria m ON s.macro_id = m.id
            JOIN grupocategoria g ON m.grupo_id = g.id
            WHERE a.usuario_id = @userId AND a.ativo = TRUE{extra}
            ORDER BY a.valor_previsto DESC, a.descricao ASC
            LIMIT @limit OFFSET @offset
            """,
            parameters,
            cancellationToken: cancellationToken));

        var total = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            $"""
            SELECT COUNT(*) AS total
            FROM agendamentos a
            JOIN subcategoria s ON a.subcategoria_id = s.id
            WHERE a.usuario_id = @userId AND a.ativo = TRUE{extra}
            """,
            parameters,
            cancellationToken: cancellationToken));

        return new ReserveListPage(
            total,
            rows.Select(r => new ReserveListItem(
                r.Id, r.Descricao, r.ValorPrevisto, r.Periodicidade, r.TipoAgendamento, r.DiaExecucao, r.Categoria, r.MacroCategoria,
                r.Grupo, r.IncluirNaReserva)).ToList());
    }
}

/// <summary>Uma transação do banco para <c>process_invoice_schedules</c> e a leitura da fatura do <c>reprocessar</c>.</summary>
public sealed class InvoiceScheduleUnitOfWork(IDbConnectionFactory connections) : IInvoiceScheduleUnitOfWork
{
    private sealed class InvoiceRow
    {
        public int Id { get; set; }

        public int ContaId { get; set; }

        public DateTime DataVencimento { get; set; }

        public string Status { get; set; } = "";

        public string NomeConta { get; set; } = "";
    }

    public async Task<IInvoiceScheduleSession> BeginAsync(CancellationToken cancellationToken)
    {
        var connection = await connections.OpenAsync(cancellationToken);
        try
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken);
            return new InvoiceScheduleSession(connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<ReprocessTarget?> FindInvoiceAsync(int invoiceId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync<InvoiceRow>(new CommandDefinition(
            """
            SELECT f.id AS Id, f.conta_id AS ContaId, f.data_vencimento AS DataVencimento, f.status AS Status, c.nome_conta AS NomeConta
            FROM faturas f JOIN contas c ON f.conta_id = c.id
            WHERE f.id = @invoiceId
            """,
            new { invoiceId },
            cancellationToken: cancellationToken));
        return row is null
            ? null
            : new ReprocessTarget(row.Id, row.ContaId, row.NomeConta, DateOnly.FromDateTime(row.DataVencimento), row.Status);
    }
}

internal sealed class InvoiceScheduleSession(NpgsqlConnection connection, NpgsqlTransaction transaction) : IInvoiceScheduleSession
{
    private sealed class ScheduleDbRow
    {
        public int AgendamentoId { get; set; }

        public string TipoAgendamento { get; set; } = "";

        public string Descricao { get; set; } = "";

        public decimal? ValorPrevisto { get; set; }

        public int DiaExecucao { get; set; }

        public int? TotalParcelas { get; set; }

        public int? ParcelasExecutadas { get; set; }

        public int SubcategoriaId { get; set; }

        public int UsuarioId { get; set; }

        public string NomeGrupo { get; set; } = "";
    }

    public async Task<IReadOnlyList<ScheduleToPost>> ListSchedulesAsync(int cardAccountId, int referenceMonth, CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<ScheduleDbRow>(new CommandDefinition(
            """
            SELECT a.id AS AgendamentoId, a.tipo_agendamento AS TipoAgendamento, a.descricao AS Descricao,
                   a.valor_previsto AS ValorPrevisto, a.dia_execucao AS DiaExecucao, a.total_parcelas AS TotalParcelas,
                   a.parcelas_executadas AS ParcelasExecutadas, a.subcategoria_id AS SubcategoriaId, a.usuario_id AS UsuarioId,
                   g.nome_grupo AS NomeGrupo
            FROM agendamentos a
            JOIN subcategoria s ON a.subcategoria_id = s.id
            JOIN macrocategoria m ON s.macro_id = m.id
            JOIN grupocategoria g ON m.grupo_id = g.id
            WHERE a.conta_id = @cardAccountId
              AND a.ativo = TRUE
              AND a.tipo_agendamento IN ('FIXO', 'PARCELADO', 'LEMBRETE_VARIAVEL')
              AND (a.periodicidade != 'ANUAL' OR (a.periodicidade = 'ANUAL' AND a.mes_execucao = @referenceMonth))
            ORDER BY a.dia_execucao, a.descricao, a.id
            """,
            new { cardAccountId, referenceMonth },
            transaction,
            cancellationToken: cancellationToken));

        return rows.Select(r => new ScheduleToPost(
            r.AgendamentoId, r.UsuarioId, r.TipoAgendamento, r.Descricao, r.ValorPrevisto, r.DiaExecucao, r.SubcategoriaId,
            r.TotalParcelas, r.ParcelasExecutadas, r.NomeGrupo)).ToList();
    }

    public async Task<bool> TransactionExistsAsync(int invoiceId, int scheduleId, CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM transacoes WHERE fatura_id = @invoiceId AND agendamento_id = @scheduleId)",
            new { invoiceId, scheduleId },
            transaction,
            cancellationToken: cancellationToken));

    public async Task InsertTransactionAsync(NewScheduledTransaction row, CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        parameters.Add("userId", row.UserId, DbType.Int32);
        parameters.Add("accountId", row.AccountId, DbType.Int32);
        parameters.Add("subcategoryId", row.SubcategoryId, DbType.Int32);
        parameters.Add("invoiceId", row.InvoiceId, DbType.Int32);
        parameters.Add("scheduleId", row.ScheduleId, DbType.Int32);
        parameters.Add("description", row.Description, DbType.String);
        parameters.Add("amount", row.Amount, DbType.Decimal);
        parameters.Add("dbType", row.DbType, DbType.String);
        parameters.Add("date", row.Date.ToDateTime(TimeOnly.MinValue), DbType.Date);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO transacoes (usuario_id, conta_id, subcategoria_id, fatura_id, agendamento_id, descricao, valor,
                                    tipo_transacao, data_transacao, consolidada)
            VALUES (@userId, @accountId, @subcategoryId, @invoiceId, @scheduleId, @description, @amount, @dbType, @date, TRUE)
            """,
            parameters,
            transaction,
            cancellationToken: cancellationToken));
    }

    public async Task AdvanceInstallmentAsync(int scheduleId, int newInstallmentsExecuted, bool active, CancellationToken cancellationToken) =>
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE agendamentos SET parcelas_executadas = @newInstallmentsExecuted, ativo = @active WHERE id = @scheduleId",
            new { newInstallmentsExecuted, active, scheduleId },
            transaction,
            cancellationToken: cancellationToken));

    private const string ScheduleSavepoint = "invoice_schedule";

    public Task BeginScheduleAsync(CancellationToken cancellationToken) => transaction.SaveAsync(ScheduleSavepoint, cancellationToken);

    public Task ReleaseScheduleAsync(CancellationToken cancellationToken) => transaction.ReleaseAsync(ScheduleSavepoint, cancellationToken);

    public Task RollbackScheduleAsync(CancellationToken cancellationToken) => transaction.RollbackAsync(ScheduleSavepoint, cancellationToken);

    public async Task CommitAsync(CancellationToken cancellationToken) => await transaction.CommitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync();
        await connection.DisposeAsync();
    }
}

/// <summary>Pendência no Redis pelas chaves do Flask (<c>pending_tx:{whatsapp}:{id}</c>, <c>last_pending:{whatsapp}</c>; 300 s).</summary>
public sealed class PendingTransactionStore(ICache cache, IPendingIdGenerator ids) : IPendingTransactionStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(300);

    public async Task<string?> CreateAsync(string whatsapp, IReadOnlyDictionary<string, object?> data, CancellationToken cancellationToken)
    {
        var id = ids.NewId();
        if (!await cache.SetJsonAsync($"pending_tx:{whatsapp}:{id}", data, Ttl, cancellationToken))
        {
            return null;
        }

        await SetLastPendingAsync(whatsapp, id, cancellationToken);
        return id;
    }

    public async Task SetLastPendingAsync(string whatsapp, string transactionId, CancellationToken cancellationToken) =>
        await cache.SetJsonAsync($"last_pending:{whatsapp}", transactionId, Ttl, cancellationToken);
}

/// <summary><c>str(uuid.uuid4())[:8]</c>: 8 caracteres hexadecimais minúsculos.</summary>
public sealed class UuidPendingIdGenerator : IPendingIdGenerator
{
    public string NewId() => Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture)[..8];
}
