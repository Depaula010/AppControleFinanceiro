using System.Data;
using Dapper;
using MeuSecretario.Application.Accounts;
using MeuSecretario.Domain.Accounts;
using MeuSecretario.Infrastructure.Database;
using Npgsql;

namespace MeuSecretario.Infrastructure.Accounts;

/// <summary>
/// Acesso a <c>contas</c> com Dapper (spec 002). O SQL monta-se só com fragmentos fixos; todo valor vai parametrizado.
/// </summary>
public sealed class AccountRepository(IDbConnectionFactory connections) : IAccountRepository
{
    private const string DetailColumns =
        """
        id AS Id, nome_conta AS NomeConta, tipo_conta AS TipoConta, saldo_inicial AS SaldoInicial,
        cor_hex AS CorHex, icone AS Icone, limite_credito AS LimiteCredito,
        dia_fechamento AS DiaFechamento, dia_vencimento AS DiaVencimento
        """;

    public async Task<IReadOnlyList<AccountListItem>> ListActiveAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);

        // Desvio 007: só transações consolidadas entram no saldo. Sem COLLATE: a ordem é a do banco, como no Flask.
        var rows = await connection.QueryAsync<AccountListItem>(new CommandDefinition(
            """
            SELECT c.id AS Id, c.nome_conta AS NomeConta, c.tipo_conta AS TipoConta, c.saldo_inicial AS SaldoInicial,
                   c.cor_hex AS CorHex, c.icone AS Icone, c.limite_credito AS LimiteCredito,
                   c.dia_fechamento AS DiaFechamento, c.dia_vencimento AS DiaVencimento,
                   c.saldo_inicial + COALESCE(SUM(t.valor), 0) AS Saldo
            FROM contas c
            LEFT JOIN transacoes t ON t.conta_id = c.id AND t.consolidada = true
            WHERE c.usuario_id = @userId AND c.ativa = true
            GROUP BY c.id
            ORDER BY c.tipo_conta, c.nome_conta
            """,
            new { userId },
            cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<AccountDetails?> GetActiveAsync(int userId, int accountId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<AccountDetails>(new CommandDefinition(
            $"SELECT {DetailColumns} FROM contas WHERE id = @accountId AND usuario_id = @userId AND ativa = true",
            new { accountId, userId },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> NameTakenAsync(int userId, string name, int exceptAccountId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);

        // Desvio 016: sem filtro de ativa, porque a UNIQUE vale para todas as contas.
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM contas WHERE usuario_id = @userId AND nome_conta = @name AND id <> @exceptAccountId)",
            new { userId, name, exceptAccountId },
            cancellationToken: cancellationToken));
    }

    public async Task<CreateAccountResult> CreateAsync(int userId, NewAccountData data, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);

        // Um comando só: cria, reativa a conta inativa de mesmo nome (desvio 032) ou não devolve linha quando a ativa
        // já existe. O ON CONFLICT serializa duas criações simultâneas do mesmo nome.
        var created = await connection.QuerySingleOrDefaultAsync<AccountDetails>(new CommandDefinition(
            $"""
            INSERT INTO contas (usuario_id, nome_conta, tipo_conta, saldo_inicial, cor_hex, icone, limite_credito,
                                dia_fechamento, dia_vencimento, ativa, inclui_saldo_total, created_at)
            VALUES (@userId, @NomeConta, @TipoConta, @SaldoInicial, @CorHex, @Icone, @LimiteCredito,
                    @DiaFechamento, @DiaVencimento, true, true, CURRENT_TIMESTAMP)
            ON CONFLICT (usuario_id, nome_conta) DO UPDATE SET
                tipo_conta = EXCLUDED.tipo_conta, saldo_inicial = EXCLUDED.saldo_inicial, cor_hex = EXCLUDED.cor_hex,
                icone = EXCLUDED.icone, limite_credito = EXCLUDED.limite_credito,
                dia_fechamento = EXCLUDED.dia_fechamento, dia_vencimento = EXCLUDED.dia_vencimento,
                ativa = true, inclui_saldo_total = true
            WHERE contas.ativa = false
            RETURNING {DetailColumns}
            """,
            new
            {
                userId,
                data.NomeConta,
                data.TipoConta,
                data.SaldoInicial,
                data.CorHex,
                data.Icone,
                data.LimiteCredito,
                data.DiaFechamento,
                data.DiaVencimento,
            },
            cancellationToken: cancellationToken));

        return created is null ? new CreateAccountResult.NameTaken() : new CreateAccountResult.Created(created);
    }

    public async Task<UpdateAccountResult> UpdateAsync(int userId, int accountId, AccountChanges changes, CancellationToken cancellationToken)
    {
        var sets = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("accountId", accountId, DbType.Int32);
        parameters.Add("userId", userId, DbType.Int32);

        if (changes.Nome.IsSet)
        {
            sets.Add("nome_conta = @Nome");
            parameters.Add("Nome", changes.Nome.Value, DbType.String);
        }
        if (changes.Tipo.IsSet)
        {
            sets.Add("tipo_conta = @Tipo");
            parameters.Add("Tipo", changes.Tipo.Value, DbType.String);
        }
        if (changes.Saldo.IsSet)
        {
            sets.Add("saldo_inicial = @Saldo");
            parameters.Add("Saldo", changes.Saldo.Value, DbType.Decimal);
        }
        if (changes.Cor.IsSet)
        {
            sets.Add("cor_hex = @Cor");
            parameters.Add("Cor", changes.Cor.Value, DbType.String);
        }
        if (changes.Icone.IsSet)
        {
            sets.Add("icone = @Icone");
            parameters.Add("Icone", changes.Icone.Value, DbType.String);
        }
        if (changes.Limite.IsSet)
        {
            sets.Add("limite_credito = @Limite");
            parameters.Add("Limite", changes.Limite.Value, DbType.Decimal);
        }
        if (changes.Fechamento.IsSet)
        {
            sets.Add("dia_fechamento = @Fechamento");
            parameters.Add("Fechamento", changes.Fechamento.Value, DbType.Int32);
        }
        if (changes.Vencimento.IsSet)
        {
            sets.Add("dia_vencimento = @Vencimento");
            parameters.Add("Vencimento", changes.Vencimento.Value, DbType.Int32);
        }

        if (sets.Count == 0)
        {
            throw new ArgumentException("Nenhum campo para atualizar.", nameof(changes));
        }

        await using var connection = await connections.OpenAsync(cancellationToken);
        try
        {
            var updated = await connection.QuerySingleOrDefaultAsync<AccountDetails>(new CommandDefinition(
                $"""
                UPDATE contas SET {string.Join(", ", sets)}
                WHERE id = @accountId AND usuario_id = @userId AND ativa = true
                RETURNING {DetailColumns}
                """,
                parameters,
                cancellationToken: cancellationToken));

            return updated is null ? new UpdateAccountResult.NotFound() : new UpdateAccountResult.Updated(updated);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Renomeação simultânea para o mesmo nome (a pré-checagem passou nas duas).
            return new UpdateAccountResult.NameTaken();
        }
    }

    public async Task<DeleteAccountResult> SoftDeleteAsync(int userId, int accountId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // RN-002-15: a trava de linha faz uma inserção de transação ainda não confirmada na conta (FOR KEY SHARE
        // pela FK) ser esperada, e a contagem a seguir já a enxerga.
        var locked = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT id FROM contas WHERE id = @accountId AND usuario_id = @userId AND ativa = true FOR UPDATE",
            new { accountId, userId },
            transaction,
            cancellationToken: cancellationToken));
        if (locked is null)
        {
            return new DeleteAccountResult.NotFound();
        }

        var transactions = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*)::int FROM transacoes WHERE conta_id = @accountId",
            new { accountId },
            transaction,
            cancellationToken: cancellationToken));
        if (transactions > 0)
        {
            return new DeleteAccountResult.HasTransactions(transactions);
        }

        var schedules = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*)::int FROM agendamentos WHERE conta_id = @accountId AND ativo = true",
            new { accountId },
            transaction,
            cancellationToken: cancellationToken));
        if (schedules > 0)
        {
            return new DeleteAccountResult.HasActiveSchedules(schedules);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE contas SET ativa = false WHERE id = @accountId AND usuario_id = @userId",
            new { accountId, userId },
            transaction,
            cancellationToken: cancellationToken));

        // Desvio 037: a remoção é lógica, então o ON DELETE SET NULL das FKs nunca dispara; zera à mão.
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE usuarios SET
                conta_padrao_id = NULLIF(conta_padrao_id, @accountId),
                conta_padrao_renda_id = NULLIF(conta_padrao_renda_id, @accountId),
                conta_padrao_despesa_id = NULLIF(conta_padrao_despesa_id, @accountId)
            WHERE id = @userId
              AND (conta_padrao_id = @accountId OR conta_padrao_renda_id = @accountId OR conta_padrao_despesa_id = @accountId)
            """,
            new { accountId, userId },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return new DeleteAccountResult.Deleted();
    }
}
