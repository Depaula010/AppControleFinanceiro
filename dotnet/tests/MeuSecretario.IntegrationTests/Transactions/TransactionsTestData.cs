using System.Globalization;
using Dapper;
using MeuSecretario.IntegrationTests.Accounts;
using Npgsql;

namespace MeuSecretario.IntegrationTests.Transactions;

/// <summary>Linhas de apoio dos testes da feature 004 (usa o seed de categorias da migration 0020 e os helpers da 002 sem editá-los).</summary>
internal sealed class TransactionsTestData(NpgsqlDataSource dataSource)
{
    /// <summary>Subcategorias globais do seed: 59 = Outros (Despesas Gerais), 60 = Outros (Receitas Gerais).</summary>
    public const int ExpenseSubcategory = 59;

    public const int IncomeSubcategory = 60;

    public AccountsTestData Accounts { get; } = new(dataSource);

    public Task<int> InsertUserAsync(string whatsapp = "5511999999999", string nome = "Ana Souza") => Accounts.InsertUserAsync(whatsapp, nome);

    public Task<int> InsertAccountAsync(int userId, string nome, string tipo = "Conta Corrente", bool ativa = true) =>
        Accounts.InsertAccountAsync(userId, nome, tipo, ativa: ativa);

    public Task<int> InsertCardAsync(int userId, string nome = "Nubank", int fechamento = 10, int vencimento = 15, bool ativa = true) =>
        Accounts.InsertAccountAsync(userId, nome, "Cartão de Crédito", ativa: ativa, fechamento: fechamento, vencimento: vencimento);

    public static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public async Task<int> InsertInvoiceAsync(int cardId, string due, string closing, string status = "Aberta")
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(
            "INSERT INTO faturas (conta_id, data_vencimento, data_fechamento, status) VALUES (@cardId, @due, @closing, @status) RETURNING id",
            new { cardId, due = Day(due).ToDateTime(TimeOnly.MinValue), closing = Day(closing).ToDateTime(TimeOnly.MinValue), status });
    }

    public async Task<int> InsertTransactionAsync(
        int userId,
        int accountId,
        string tipo = "Despesa",
        decimal valor = -10m,
        string data = "2026-03-10",
        string descricao = "teste",
        int? faturaId = null,
        int subcategoryId = ExpenseSubcategory,
        bool consolidada = true,
        int? parId = null,
        string? createdAt = null)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(
            """
            INSERT INTO transacoes (usuario_id, conta_id, subcategoria_id, fatura_id, descricao, valor, tipo_transacao,
                                    data_transacao, consolidada, transferencia_par_id, created_at)
            VALUES (@userId, @accountId, @subcategoryId, @faturaId, @descricao, @valor, @tipo, @data, @consolidada, @parId,
                    COALESCE(@createdAt::timestamptz, CURRENT_TIMESTAMP))
            RETURNING id
            """,
            new
            {
                userId, accountId, subcategoryId, faturaId, descricao, valor, tipo, consolidada, parId, createdAt,
                data = Day(data).ToDateTime(TimeOnly.MinValue),
            });
    }

    public async Task LinkPairAsync(int first, int second)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("UPDATE transacoes SET transferencia_par_id = @second WHERE id = @first", new { first, second });
        await connection.ExecuteAsync("UPDATE transacoes SET transferencia_par_id = @first WHERE id = @second", new { first, second });
    }

    public Task<T> ScalarAsync<T>(string sql, object? parameters = null) => Accounts.ScalarAsync<T>(sql, parameters);

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null) => Accounts.QueryAsync<T>(sql, parameters);

    public async Task ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(sql, parameters);
    }
}

internal static class IntJson
{
    /// <summary>Inteiro em texto com cultura invariante (para montar JSON de teste).</summary>
    public static string J(this int value) => value.ToString(CultureInfo.InvariantCulture);
}
