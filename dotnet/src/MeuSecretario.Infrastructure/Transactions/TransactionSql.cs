using System.Data;
using Dapper;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.Transactions;
using Npgsql;

namespace MeuSecretario.Infrastructure.Transactions;

/// <summary>
/// Linhas e comandos compartilhados pelo repositório e pela sessão de lançamento. O SQL só se monta com fragmentos
/// fixos; todo valor vai parametrizado. As datas viajam como <see cref="DateTime"/> com <see cref="DbType.Date"/>
/// (o Dapper não conhece <see cref="DateOnly"/>).
/// </summary>
internal static class TransactionSql
{
    public sealed class UpdatedRow
    {
        public int Id { get; set; }

        public string Descricao { get; set; } = "";

        public decimal Valor { get; set; }

        public string TipoTransacao { get; set; } = "";

        public DateTime DataTransacao { get; set; }

        public UpdatedTransaction ToModel() => new(Id, Descricao, Valor, TipoTransacao, DateOnly.FromDateTime(DataTransacao));
    }

    public sealed class AccountRow
    {
        public int Id { get; set; }

        public string NomeConta { get; set; } = "";

        public string TipoConta { get; set; } = "";

        public int? DiaFechamento { get; set; }

        public int? DiaVencimento { get; set; }

        public PostingAccount ToModel() => new(Id, NomeConta, TipoConta, DiaFechamento, DiaVencimento);
    }

    public const string AccountColumns =
        "id AS Id, nome_conta AS NomeConta, tipo_conta AS TipoConta, dia_fechamento AS DiaFechamento, dia_vencimento AS DiaVencimento";

    public static DateTime Day(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    /// <summary><c>UPDATE</c> só dos campos presentes, sempre com <c>updated_at</c>; <c>null</c> quando a linha sumiu.</summary>
    public static async Task<UpdatedTransaction?> UpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        int userId,
        int id,
        TransactionChanges changes,
        Change<int?> invoice,
        CancellationToken cancellationToken)
    {
        var sets = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("id", id, DbType.Int32);
        parameters.Add("userId", userId, DbType.Int32);

        if (changes.Description.IsSet)
        {
            sets.Add("descricao = @descricao");
            parameters.Add("descricao", changes.Description.Value, DbType.String);
        }
        if (changes.Amount.IsSet)
        {
            sets.Add("valor = @valor");
            parameters.Add("valor", changes.Amount.Value, DbType.Decimal);
        }
        if (changes.Type.IsSet)
        {
            sets.Add("tipo_transacao = @tipo");
            parameters.Add("tipo", changes.Type.Value, DbType.String);
        }
        if (changes.Date.IsSet)
        {
            sets.Add("data_transacao = @data");
            parameters.Add("data", Day(changes.Date.Value), DbType.Date);
        }
        if (changes.SubcategoryId.IsSet)
        {
            sets.Add("subcategoria_id = @subcategoriaId");
            parameters.Add("subcategoriaId", changes.SubcategoryId.Value, DbType.Int32);
        }
        if (changes.AccountId.IsSet)
        {
            sets.Add("conta_id = @contaId");
            parameters.Add("contaId", changes.AccountId.Value, DbType.Int32);
        }
        if (changes.Notes.IsSet)
        {
            sets.Add("observacoes = @observacoes");
            parameters.Add("observacoes", changes.Notes.Value, DbType.String);
        }
        if (changes.Consolidated.IsSet)
        {
            sets.Add("consolidada = @consolidada");
            parameters.Add("consolidada", changes.Consolidated.Value, DbType.Boolean);
        }
        if (invoice.IsSet)
        {
            sets.Add("fatura_id = @faturaId");
            parameters.Add("faturaId", invoice.Value, DbType.Int32);
        }

        if (sets.Count == 0)
        {
            throw new ArgumentException("Nenhum campo para atualizar.", nameof(changes));
        }

        var row = await connection.QuerySingleOrDefaultAsync<UpdatedRow>(new CommandDefinition(
            $"""
            UPDATE transacoes SET {string.Join(", ", sets)}, updated_at = CURRENT_TIMESTAMP
            WHERE id = @id AND usuario_id = @userId
            RETURNING id AS Id, descricao AS Descricao, valor AS Valor, tipo_transacao AS TipoTransacao, data_transacao AS DataTransacao
            """,
            parameters,
            transaction,
            cancellationToken: cancellationToken));

        return row?.ToModel();
    }

    public static Task<bool> SubcategoryVisibleAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, int userId, int subcategoryId, CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM subcategoria WHERE id = @subcategoryId AND (usuario_id IS NULL OR usuario_id = @userId))",
            new { subcategoryId, userId },
            transaction,
            cancellationToken: cancellationToken));
}
