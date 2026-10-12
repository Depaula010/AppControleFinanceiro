using Dapper;
using Npgsql;

namespace MeuSecretario.IntegrationTests.Accounts;

/// <summary>Cria linhas diretamente no banco para os testes da feature 002 (usa o seed de categorias da migration 0020).</summary>
internal sealed class AccountsTestData(NpgsqlDataSource dataSource)
{
    public async Task<int> InsertUserAsync(string whatsapp = "5511999999999", string nome = "Ana Souza")
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(
            "INSERT INTO usuarios (nome, numero_whatsapp) VALUES (@nome, @whatsapp) RETURNING id",
            new { nome, whatsapp });
    }

    /// <summary>Usuário com id fixo (para os tokens das fixtures do PyJWT, que levam <c>user_id</c> 42).</summary>
    public async Task<int> InsertUserWithIdAsync(int id)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(
            "INSERT INTO usuarios (id, nome, numero_whatsapp) VALUES (@id, 'Fixture', @whatsapp) RETURNING id",
            new { id, whatsapp = $"55119{id:D8}" });
    }

    public async Task<int> InsertAccountAsync(
        int userId,
        string nome,
        string tipo = "Dinheiro",
        decimal saldoInicial = 0m,
        bool ativa = true,
        int? fechamento = null,
        int? vencimento = null,
        string? cor = "#3B82F6",
        string? icone = "wallet",
        decimal? limite = null)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(
            """
            INSERT INTO contas (usuario_id, nome_conta, tipo_conta, saldo_inicial, ativa, dia_fechamento, dia_vencimento,
                                cor_hex, icone, limite_credito)
            VALUES (@userId, @nome, @tipo, @saldoInicial, @ativa, @fechamento, @vencimento, @cor, @icone, @limite)
            RETURNING id
            """,
            new { userId, nome, tipo, saldoInicial, ativa, fechamento, vencimento, cor, icone, limite });
    }

    public async Task<int> InsertTransactionAsync(int userId, int accountId, decimal valor, bool consolidada = true)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await InsertTransactionAsync(connection, null, userId, accountId, valor, consolidada);
    }

    public static async Task<int> InsertTransactionAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, int userId, int accountId, decimal valor, bool consolidada = true)
    {
        return await connection.ExecuteScalarAsync<int>(
            """
            INSERT INTO transacoes (usuario_id, conta_id, subcategoria_id, descricao, valor, tipo_transacao, consolidada)
            VALUES (@userId, @accountId, (SELECT id FROM subcategoria ORDER BY id LIMIT 1), 'teste', @valor,
                    CASE WHEN @valor < 0 THEN 'Despesa' ELSE 'Renda' END, @consolidada)
            RETURNING id
            """,
            new { userId, accountId, valor, consolidada },
            transaction);
    }

    public async Task<int> InsertScheduleAsync(int userId, int accountId, bool ativo = true)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(
            """
            INSERT INTO agendamentos (usuario_id, conta_id, subcategoria_id, descricao, tipo_agendamento, periodicidade,
                                      data_inicio, dia_execucao, ativo)
            VALUES (@userId, @accountId, (SELECT id FROM subcategoria ORDER BY id LIMIT 1), 'agendamento', 'FIXO', 'MENSAL',
                    CURRENT_DATE, 5, @ativo)
            RETURNING id
            """,
            new { userId, accountId, ativo });
    }

    public async Task SetDefaultAccountsAsync(int userId, int? padrao, int? renda, int? despesa)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "UPDATE usuarios SET conta_padrao_id = @padrao, conta_padrao_renda_id = @renda, conta_padrao_despesa_id = @despesa WHERE id = @userId",
            new { userId, padrao, renda, despesa });
    }

    public async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return (await connection.ExecuteScalarAsync<T>(sql, parameters))!;
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return (await connection.QueryAsync<T>(sql, parameters)).AsList();
    }
}
