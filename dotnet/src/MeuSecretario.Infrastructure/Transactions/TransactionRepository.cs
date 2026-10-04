using System.Data;
using Dapper;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.Transactions;
using MeuSecretario.Infrastructure.Database;

namespace MeuSecretario.Infrastructure.Transactions;

/// <summary>Leituras e escritas das 4 rotas de transações com Dapper (spec 004). Toda consulta filtra <c>usuario_id</c>.</summary>
public sealed class TransactionRepository(IDbConnectionFactory connections) : ITransactionRepository
{
    private sealed class ListRow
    {
        public int Id { get; set; }

        public string Descricao { get; set; } = "";

        public decimal Valor { get; set; }

        public string TipoTransacao { get; set; } = "";

        public DateTime DataTransacao { get; set; }

        public string Categoria { get; set; } = "";

        public string Subcategoria { get; set; } = "";

        public string NomeConta { get; set; } = "";

        public bool Consolidada { get; set; }

        public int ContaId { get; set; }

        public int SubcategoriaId { get; set; }

        public string? Observacoes { get; set; }
    }

    private sealed class CurrentRow
    {
        public int Id { get; set; }

        public string TipoTransacao { get; set; } = "";

        public decimal Valor { get; set; }

        public int ContaId { get; set; }

        public DateTime DataTransacao { get; set; }
    }

    private sealed class DeleteRow
    {
        public int Id { get; set; }

        public int? PairId { get; set; }

        public string TipoTransacao { get; set; } = "";

        public int? FaturaId { get; set; }
    }

    public async Task<TransactionPage> ListAsync(int userId, ListFilter filter, CancellationToken cancellationToken)
    {
        var where = new List<string> { "t.usuario_id = @userId" };
        var parameters = new DynamicParameters();
        parameters.Add("userId", userId, DbType.Int32);

        if (filter.Kind is { } kind)
        {
            where.Add("t.tipo_transacao = @tipo");
            parameters.Add("tipo", TransactionTypes.ToDb(kind), DbType.String);
        }
        if (filter.From is { } from)
        {
            where.Add("t.data_transacao >= @from");
            parameters.Add("from", TransactionSql.Day(from), DbType.Date);
        }
        if (filter.To is { } to)
        {
            where.Add("t.data_transacao <= @to");
            parameters.Add("to", TransactionSql.Day(to), DbType.Date);
        }

        var clause = string.Join(" AND ", where);
        parameters.Add("limit", (long)filter.Limit, DbType.Int64);
        parameters.Add("offset", filter.Offset, DbType.Int64);

        await using var connection = await connections.OpenAsync(cancellationToken);

        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(*)::int FROM transacoes t WHERE {clause}",
            parameters,
            cancellationToken: cancellationToken));

        var rows = await connection.QueryAsync<ListRow>(new CommandDefinition(
            $"""
            SELECT t.id AS Id, t.descricao AS Descricao, t.valor AS Valor, t.tipo_transacao AS TipoTransacao,
                   t.data_transacao AS DataTransacao, m.nome_macro AS Categoria, s.nome_sub AS Subcategoria,
                   c.nome_conta AS NomeConta, t.consolidada AS Consolidada,
                   t.conta_id AS ContaId, t.subcategoria_id AS SubcategoriaId, t.observacoes AS Observacoes
            FROM transacoes t
            JOIN subcategoria s ON t.subcategoria_id = s.id
            JOIN macrocategoria m ON s.macro_id = m.id
            JOIN contas c ON t.conta_id = c.id
            WHERE {clause}
            ORDER BY t.data_transacao DESC, t.created_at DESC, t.id DESC
            LIMIT @limit OFFSET @offset
            """,
            parameters,
            cancellationToken: cancellationToken));

        return new TransactionPage(
            total,
            rows.Select(r => new TransactionListItem(
                r.Id, r.Descricao, r.Valor, r.TipoTransacao, DateOnly.FromDateTime(r.DataTransacao), r.Categoria, r.Subcategoria,
                r.NomeConta, r.Consolidada, r.ContaId, r.SubcategoriaId, r.Observacoes)).ToList());
    }

    public async Task<CurrentTransaction?> GetAsync(int userId, int id, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<CurrentRow>(new CommandDefinition(
            """
            SELECT id AS Id, tipo_transacao AS TipoTransacao, valor AS Valor, conta_id AS ContaId, data_transacao AS DataTransacao
            FROM transacoes WHERE id = @id AND usuario_id = @userId
            """,
            new { id, userId },
            cancellationToken: cancellationToken));

        return row is null
            ? null
            : new CurrentTransaction(row.Id, row.TipoTransacao, row.Valor, row.ContaId, DateOnly.FromDateTime(row.DataTransacao));
    }

    public async Task<bool> SubcategoryVisibleAsync(int userId, int subcategoryId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await TransactionSql.SubcategoryVisibleAsync(connection, null, userId, subcategoryId, cancellationToken);
    }

    public async Task<bool> ActiveAccountOwnedAsync(int userId, int accountId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM contas WHERE id = @accountId AND usuario_id = @userId AND ativa = true)",
            new { accountId, userId },
            cancellationToken: cancellationToken));
    }

    public async Task<UpdatedTransaction?> UpdateAsync(int userId, int id, TransactionChanges changes, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await TransactionSql.UpdateAsync(connection, null, userId, id, changes, default, cancellationToken);
    }

    public async Task<bool> DeleteWithPairAsync(int userId, int id, DateOnly today, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<DeleteRow>(new CommandDefinition(
            """
            SELECT id AS Id, transferencia_par_id AS PairId, tipo_transacao AS TipoTransacao, fatura_id AS FaturaId
            FROM transacoes WHERE id = @id AND usuario_id = @userId FOR UPDATE
            """,
            new { id, userId },
            transaction,
            cancellationToken: cancellationToken));
        if (row is null)
        {
            return false;
        }

        // RN-004-15: o par primeiro (só se for do mesmo usuário); a FK do par é ON DELETE SET NULL.
        if (row.PairId is { } pairId)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM transacoes WHERE id = @pairId AND usuario_id = @userId",
                new { pairId, userId },
                transaction,
                cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM transacoes WHERE id = @id AND usuario_id = @userId",
            new { id, userId },
            transaction,
            cancellationToken: cancellationToken));

        // Desvio 070: sem outro pagamento na fatura, ela deixa de estar Paga (Fechada se já fechou, senão Aberta).
        if (row.TipoTransacao == TransactionTypes.InvoicePayment && row.FaturaId is { } invoiceId)
        {
            var parameters = new DynamicParameters();
            parameters.Add("invoiceId", invoiceId, DbType.Int32);
            parameters.Add("userId", userId, DbType.Int32);
            parameters.Add("today", TransactionSql.Day(today), DbType.Date);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE faturas SET status = CASE WHEN data_fechamento < @today THEN 'Fechada' ELSE 'Aberta' END
                WHERE id = @invoiceId AND status = 'Paga'
                  AND conta_id IN (SELECT id FROM contas WHERE usuario_id = @userId)
                  AND NOT EXISTS (SELECT 1 FROM transacoes WHERE fatura_id = @invoiceId AND tipo_transacao = 'Pagamento Fatura')
                """,
                parameters,
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
