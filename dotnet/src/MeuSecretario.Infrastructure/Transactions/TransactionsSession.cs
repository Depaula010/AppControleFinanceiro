using System.Data;
using Dapper;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.Transactions;
using MeuSecretario.Infrastructure.Database;
using Npgsql;

namespace MeuSecretario.Infrastructure.Transactions;

/// <summary>Abre uma conexão e uma transação Npgsql por lançamento (spec 004 §7, RN-004-30).</summary>
public sealed class TransactionsUnitOfWork(IDbConnectionFactory connections) : ITransactionsUnitOfWork
{
    public async Task<ITransactionsSession> BeginAsync(CancellationToken cancellationToken)
    {
        var connection = await connections.OpenAsync(cancellationToken);
        try
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken);
            return new TransactionsSession(connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}

/// <summary>Sem <see cref="CommitAsync"/>, o descarte desfaz tudo (o Npgsql faz rollback da transação aberta).</summary>
internal sealed class TransactionsSession(NpgsqlConnection connection, NpgsqlTransaction transaction) : ITransactionsSession
{
    private sealed class StoredRow
    {
        public int Id { get; set; }

        public decimal Valor { get; set; }
    }

    private sealed class ClosedInvoiceRow
    {
        public int Id { get; set; }

        public DateTime DataVencimento { get; set; }
    }

    public async Task<PostingAccount?> FindActiveAccountAsync(int userId, int accountId, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<TransactionSql.AccountRow>(new CommandDefinition(
            $"SELECT {TransactionSql.AccountColumns} FROM contas WHERE id = @accountId AND usuario_id = @userId AND ativa = true",
            new { accountId, userId },
            transaction,
            cancellationToken: cancellationToken));
        return row?.ToModel();
    }

    public Task<bool> SubcategoryVisibleAsync(int userId, int subcategoryId, CancellationToken cancellationToken) =>
        TransactionSql.SubcategoryVisibleAsync(connection, transaction, userId, subcategoryId, cancellationToken);

    public Task<int?> FindInvoiceByDueDateAsync(int cardAccountId, DateOnly due, CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT id FROM faturas WHERE conta_id = @cardAccountId AND data_vencimento = @due",
            DateParameters(cardAccountId, due),
            transaction,
            cancellationToken: cancellationToken));

    public Task<int?> InsertInvoiceIfMissingAsync(int cardAccountId, DateOnly due, DateOnly closing, CancellationToken cancellationToken)
    {
        var parameters = DateParameters(cardAccountId, due);
        parameters.Add("closing", TransactionSql.Day(closing), DbType.Date);
        return connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            """
            INSERT INTO faturas (conta_id, data_vencimento, data_fechamento, status)
            VALUES (@cardAccountId, @due, @closing, 'Aberta')
            ON CONFLICT (conta_id, data_vencimento) DO NOTHING
            RETURNING id
            """,
            parameters,
            transaction,
            cancellationToken: cancellationToken));
    }

    public async Task<OpenInvoice?> FindOldestClosedInvoiceAsync(int cardAccountId, CancellationToken cancellationToken)
    {
        var row = await connection.QueryFirstOrDefaultAsync<ClosedInvoiceRow>(new CommandDefinition(
            """
            SELECT id AS Id, data_vencimento AS DataVencimento FROM faturas
            WHERE conta_id = @cardAccountId AND status = 'Fechada'
            ORDER BY data_vencimento, id LIMIT 1
            """,
            new { cardAccountId },
            transaction,
            cancellationToken: cancellationToken));
        return row is null ? null : new OpenInvoice(row.Id, DateOnly.FromDateTime(row.DataVencimento));
    }

    public async Task<StoredTransaction> InsertTransactionAsync(NewTransactionRow row, CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        parameters.Add("userId", row.UserId, DbType.Int32);
        parameters.Add("contaId", row.AccountId, DbType.Int32);
        parameters.Add("subcategoriaId", row.SubcategoryId, DbType.Int32);
        parameters.Add("faturaId", row.InvoiceId, DbType.Int32);
        parameters.Add("descricao", row.Description, DbType.String);
        parameters.Add("valor", row.Amount, DbType.Decimal);
        parameters.Add("tipo", row.DbType, DbType.String);
        parameters.Add("data", TransactionSql.Day(row.Date), DbType.Date);
        parameters.Add("observacoes", row.Notes, DbType.String);
        parameters.Add("consolidada", row.Consolidated, DbType.Boolean);
        parameters.Add("origem", row.Origin, DbType.String);
        parameters.Add("agendamentoId", row.ScheduleId, DbType.Int32);

        var stored = await connection.QuerySingleAsync<StoredRow>(new CommandDefinition(
            """
            INSERT INTO transacoes (usuario_id, conta_id, subcategoria_id, fatura_id, descricao, valor, tipo_transacao,
                                    data_transacao, observacoes, consolidada, origem, agendamento_id)
            VALUES (@userId, @contaId, @subcategoriaId, @faturaId, @descricao, @valor, @tipo,
                    @data, @observacoes, @consolidada, @origem, @agendamentoId)
            RETURNING id AS Id, valor AS Valor
            """,
            parameters,
            transaction,
            cancellationToken: cancellationToken));
        return new StoredTransaction(stored.Id, stored.Valor);
    }

    public async Task LinkPairAsync(int userId, int firstId, int secondId, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE transacoes SET transferencia_par_id = @pair WHERE id = @target AND usuario_id = @userId";
        await connection.ExecuteAsync(new CommandDefinition(
            sql, new { pair = secondId, target = firstId, userId }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            sql, new { pair = firstId, target = secondId, userId }, transaction, cancellationToken: cancellationToken));
    }

    public async Task MarkInvoicePaidAsync(int invoiceId, CancellationToken cancellationToken) =>
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE faturas SET status = 'Paga' WHERE id = @invoiceId",
            new { invoiceId },
            transaction,
            cancellationToken: cancellationToken));

    public Task<int> InsertInstallmentScheduleAsync(NewInstallmentSchedule schedule, CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        parameters.Add("userId", schedule.UserId, DbType.Int32);
        parameters.Add("contaId", schedule.AccountId, DbType.Int32);
        parameters.Add("subcategoriaId", schedule.SubcategoryId, DbType.Int32);
        parameters.Add("descricao", schedule.Description, DbType.String);
        parameters.Add("valor", schedule.Amount, DbType.Decimal);
        parameters.Add("dataInicio", TransactionSql.Day(schedule.StartDate), DbType.Date);
        parameters.Add("dia", schedule.DayOfMonth, DbType.Int32);
        parameters.Add("total", schedule.Total, DbType.Int32);

        // incluir_na_reserva e notificar_antes_dias ficam nos defaults do banco; mes_execucao NULL (R6).
        return connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            INSERT INTO agendamentos (usuario_id, conta_id, subcategoria_id, descricao, valor_previsto, tipo_agendamento,
                                      periodicidade, data_inicio, dia_execucao, total_parcelas, parcelas_executadas, ativo)
            VALUES (@userId, @contaId, @subcategoriaId, @descricao, @valor, 'PARCELADO', 'MENSAL',
                    @dataInicio, @dia, @total, 1, TRUE)
            RETURNING id
            """,
            parameters,
            transaction,
            cancellationToken: cancellationToken));
    }

    public Task<UpdatedTransaction?> UpdateTransactionAsync(
        int userId, int id, TransactionChanges changes, Change<int?> invoice, CancellationToken cancellationToken) =>
        TransactionSql.UpdateAsync(connection, transaction, userId, id, changes, invoice, cancellationToken);

    public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

    public async Task<IReadOnlyList<PostingAccount>> ListActiveCardsAsync(int userId, int? cardId, CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        parameters.Add("userId", userId, DbType.Int32);
        parameters.Add("cardId", cardId, DbType.Int32);

        var rows = await connection.QueryAsync<TransactionSql.AccountRow>(new CommandDefinition(
            $"""
            SELECT {TransactionSql.AccountColumns} FROM contas
            WHERE usuario_id = @userId AND tipo_conta = 'Cartão de Crédito' AND ativa = true
              AND (@cardId::int IS NULL OR id = @cardId::int)
            ORDER BY id
            """,
            parameters,
            transaction,
            cancellationToken: cancellationToken));
        return rows.Select(r => r.ToModel()).ToList();
    }

    public Task<bool> OpenInvoiceExistsAsync(int cardAccountId, DateOnly due, CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM faturas WHERE conta_id = @cardAccountId AND data_vencimento = @due AND status = 'Aberta')",
            DateParameters(cardAccountId, due),
            transaction,
            cancellationToken: cancellationToken));

    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync();
        await connection.DisposeAsync();
    }

    private static DynamicParameters DateParameters(int cardAccountId, DateOnly due)
    {
        var parameters = new DynamicParameters();
        parameters.Add("cardAccountId", cardAccountId, DbType.Int32);
        parameters.Add("due", TransactionSql.Day(due), DbType.Date);
        return parameters;
    }
}
