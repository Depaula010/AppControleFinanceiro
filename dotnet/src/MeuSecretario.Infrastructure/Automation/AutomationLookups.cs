using System.Data;
using Dapper;
using MeuSecretario.Application.Automation;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Infrastructure.Database;

namespace MeuSecretario.Infrastructure.Automation;

/// <summary><c>SELECT id, numero_whatsapp, api_key_automate FROM usuarios WHERE api_key_automate IS NOT NULL</c> (<c>finance_service.py:264</c>).</summary>
public sealed class UserKeyReader(IDbConnectionFactory connections) : IUserKeyReader
{
    private sealed class Row
    {
        public int Id { get; set; }

        public string NumeroWhatsapp { get; set; } = "";

        public string ApiKeyAutomate { get; set; } = "";
    }

    public async Task<IReadOnlyList<StoredUserKey>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition(
            "SELECT id AS Id, numero_whatsapp AS NumeroWhatsapp, api_key_automate AS ApiKeyAutomate FROM usuarios WHERE api_key_automate IS NOT NULL ORDER BY id",
            cancellationToken: cancellationToken));
        return rows.Select(r => new StoredUserKey(r.Id, r.NumeroWhatsapp, r.ApiKeyAutomate)).ToList();
    }
}

/// <summary>
/// Contas, categorias e contas fixas do Automate, do atalho e do SMS (spec 011 §7.3). Só contas ativas (desvio 039) e
/// <c>ORDER BY id</c> onde o Flask não ordena. O texto do cliente vai sempre parametrizado; o <c>ILIKE '%x%'</c> deixa
/// <c>%</c> e <c>_</c> do cliente valerem como curinga, como hoje.
/// </summary>
public sealed class AutomationLookups(IDbConnectionFactory connections) : IAutomationLookups
{
    private const string DespesaGroups = "'Despesa Essencial', 'Despesa Discricionária', 'Meta Financeira', 'Geral'";

    private sealed class AccountRow
    {
        public int Id { get; set; }

        public string Nome { get; set; } = "";

        public string Tipo { get; set; } = "";

        public AccountRef ToModel() => new(Id, Nome, Tipo);
    }

    private sealed class CategoryRow
    {
        public int Id { get; set; }

        public string NomeSub { get; set; } = "";

        public string NomeMacro { get; set; } = "";
    }

    private sealed class BillRow
    {
        public int Id { get; set; }

        public string Descricao { get; set; } = "";

        public decimal? ValorPrevisto { get; set; }

        public int DiaExecucao { get; set; }

        public string TipoAgendamento { get; set; } = "";

        public int ContaId { get; set; }

        public string Categoria { get; set; } = "";
    }

    private sealed class SettleRow
    {
        public string Descricao { get; set; } = "";

        public int ContaId { get; set; }

        public int SubcategoriaId { get; set; }
    }

    public async Task<IReadOnlyList<AccountRef>> ListActiveAccountsAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<AccountRow>(new CommandDefinition(
            "SELECT id AS Id, nome_conta AS Nome, tipo_conta AS Tipo FROM contas WHERE usuario_id = @userId AND ativa = true ORDER BY id",
            new { userId },
            cancellationToken: cancellationToken));
        return rows.Select(r => r.ToModel()).ToList();
    }

    public async Task<AccountRef?> FindAccountByNameOrTypeAsync(int userId, string name, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var like = "%" + name + "%";

        foreach (var predicate in new[] { "nome_conta = @name", "nome_conta ILIKE @like", "tipo_conta ILIKE @like" })
        {
            var row = await connection.QueryFirstOrDefaultAsync<AccountRow>(new CommandDefinition(
                $"SELECT id AS Id, nome_conta AS Nome, tipo_conta AS Tipo FROM contas WHERE usuario_id = @userId AND ativa = true AND {predicate} ORDER BY id LIMIT 1",
                new { userId, name, like },
                cancellationToken: cancellationToken));
            if (row is not null)
            {
                return row.ToModel();
            }
        }

        return null;
    }

    public async Task<int?> FindAccountIdWithFallbackAsync(int userId, string name, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var like = "%" + name + "%";

        foreach (var predicate in new[] { "nome_conta = @name", "nome_conta ILIKE @like", "TRUE" })
        {
            var id = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                $"SELECT id FROM contas WHERE usuario_id = @userId AND ativa = true AND {predicate} ORDER BY id LIMIT 1",
                new { userId, name, like },
                cancellationToken: cancellationToken));
            if (id is not null)
            {
                return id;
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<CategoryOption>> ListCategoriesAsync(int userId, bool income, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var groups = income ? "g.nome_grupo = 'Renda'" : $"g.nome_grupo IN ({DespesaGroups})";
        var rows = await connection.QueryAsync<CategoryRow>(new CommandDefinition(
            $"""
            SELECT s.id AS Id, s.nome_sub AS NomeSub, m.nome_macro AS NomeMacro
            FROM subcategoria s
            JOIN macrocategoria m ON s.macro_id = m.id
            JOIN grupocategoria g ON m.grupo_id = g.id
            WHERE (s.usuario_id IS NULL OR s.usuario_id = @userId) AND ({groups})
            ORDER BY s.id
            """,
            new { userId },
            cancellationToken: cancellationToken));
        return rows.Select(r => new CategoryOption(r.Id, r.NomeSub, r.NomeMacro)).ToList();
    }

    public async Task<string?> GetCategoryLabelAsync(int subcategoryId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync<CategoryRow>(new CommandDefinition(
            """
            SELECT s.id AS Id, s.nome_sub AS NomeSub, m.nome_macro AS NomeMacro
            FROM subcategoria s JOIN macrocategoria m ON s.macro_id = m.id
            WHERE s.id = @subcategoryId
            """,
            new { subcategoryId },
            cancellationToken: cancellationToken));
        return row is null ? null : $"{row.NomeMacro} -> {row.NomeSub}";
    }

    public async Task<IReadOnlyList<FixedBill>> ListFixedBillsAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<BillRow>(new CommandDefinition(
            """
            SELECT a.id AS Id, a.descricao AS Descricao, a.valor_previsto AS ValorPrevisto, a.dia_execucao AS DiaExecucao,
                   a.tipo_agendamento AS TipoAgendamento, a.conta_id AS ContaId, s.nome_sub AS Categoria
            FROM agendamentos a
            JOIN subcategoria s ON a.subcategoria_id = s.id
            WHERE a.usuario_id = @userId AND a.ativo = TRUE AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
            ORDER BY a.id
            """,
            new { userId },
            cancellationToken: cancellationToken));
        return rows.Select(r => new FixedBill(r.Id, r.Descricao, r.ValorPrevisto, r.DiaExecucao, r.TipoAgendamento, r.ContaId, r.Categoria)).ToList();
    }

    public async Task<SettleTarget?> FindSettleTargetAsync(int userId, int scheduleId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync<SettleRow>(new CommandDefinition(
            """
            SELECT a.descricao AS Descricao, a.conta_id AS ContaId, a.subcategoria_id AS SubcategoriaId
            FROM agendamentos a JOIN contas c ON a.conta_id = c.id
            WHERE a.id = @scheduleId AND a.usuario_id = @userId
            """,
            new { scheduleId, userId },
            cancellationToken: cancellationToken));
        return row is null ? null : new SettleTarget(row.Descricao, row.ContaId, row.SubcategoriaId);
    }
}

/// <summary>Potes ativos e gasto do período (<c>budget_validation_service.py:50-150</c>); só leitura.</summary>
public sealed class BudgetReader(IDbConnectionFactory connections) : IBudgetReader
{
    private sealed class PotDbRow
    {
        public int Id { get; set; }

        public string NomePote { get; set; } = "";

        public decimal ValorLimite { get; set; }

        public string Periodicidade { get; set; } = "";
    }

    public async Task<IReadOnlyList<PotRow>> ListPotsForSubcategoryAsync(int userId, int subcategoryId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<PotDbRow>(new CommandDefinition(
            """
            SELECT p.id AS Id, p.nome_pote AS NomePote, p.valor_limite AS ValorLimite, p.periodicidade AS Periodicidade
            FROM potesdegastos p
            INNER JOIN potesubcategorias psc ON p.id = psc.pote_id
            WHERE p.usuario_id = @userId AND psc.subcategoria_id = @subcategoryId AND p.ativo = TRUE
            ORDER BY p.id
            """,
            new { userId, subcategoryId },
            cancellationToken: cancellationToken));
        return rows.Select(r => new PotRow(r.Id, r.NomePote, (double)r.ValorLimite, r.Periodicidade)).ToList();
    }

    public async Task<decimal> SumPotSpendAsync(int userId, int potId, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var parameters = new DynamicParameters();
        parameters.Add("userId", userId, DbType.Int32);
        parameters.Add("potId", potId, DbType.Int32);
        parameters.Add("periodStart", periodStart.ToDateTime(TimeOnly.MinValue), DbType.Date);
        parameters.Add("periodEnd", periodEnd.ToDateTime(TimeOnly.MinValue), DbType.Date);

        var sum = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            """
            SELECT COALESCE(SUM(t.valor), 0) AS valor_gasto
            FROM transacoes t
            INNER JOIN potesubcategorias psc ON t.subcategoria_id = psc.subcategoria_id
            WHERE t.usuario_id = @userId AND psc.pote_id = @potId AND t.tipo_transacao = 'Despesa'
              AND t.data_transacao >= @periodStart AND t.data_transacao <= @periodEnd
            """,
            parameters,
            cancellationToken: cancellationToken));
        return Math.Abs(sum);
    }
}
