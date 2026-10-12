using Dapper;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Whatsapp;
using MeuSecretario.Infrastructure.Database;

namespace MeuSecretario.Infrastructure.Whatsapp;

/// <summary>
/// Leituras de contas e agendamentos das intenções financeiras (spec 012 §7.6 e §7.7). Só contas <b>ativas</b> (desvio 039) e
/// <c>ORDER BY id</c> onde o Flask não ordena. O texto do cliente vai sempre como parâmetro.
/// </summary>
public sealed class WhatsAppFinanceReader(IDbConnectionFactory connections) : IWhatsAppFinanceReader
{
    private sealed class AccountRow
    {
        public int Id { get; set; }

        public string Nome { get; set; } = "";

        public string Tipo { get; set; } = "";
    }

    private sealed class DefaultsRow
    {
        public int? Renda { get; set; }

        public int? Despesa { get; set; }
    }

    private sealed class ScheduleRow
    {
        public string Descricao { get; set; } = "";

        public decimal? ValorPrevisto { get; set; }

        public int ContaId { get; set; }

        public int SubcategoriaId { get; set; }
    }

    public async Task<AccountRef?> FindActiveAccountAsync(int userId, int accountId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync<AccountRow>(new CommandDefinition(
            "SELECT id AS Id, nome_conta AS Nome, tipo_conta AS Tipo FROM contas WHERE id = @accountId AND usuario_id = @userId AND ativa = true",
            new { accountId, userId },
            cancellationToken: cancellationToken));
        return row is null ? null : new AccountRef(row.Id, row.Nome, row.Tipo);
    }

    public async Task<DefaultAccounts> GetDefaultAccountsAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync<DefaultsRow>(new CommandDefinition(
            "SELECT conta_padrao_renda_id AS Renda, conta_padrao_despesa_id AS Despesa FROM usuarios WHERE id = @userId",
            new { userId },
            cancellationToken: cancellationToken));
        return new DefaultAccounts(row?.Renda, row?.Despesa);
    }

    public async Task<RecurringSchedule?> FindRecurringByDescriptionAsync(int userId, string description, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync<ScheduleRow>(new CommandDefinition(
            """
            SELECT a.descricao AS Descricao, a.valor_previsto AS ValorPrevisto, a.conta_id AS ContaId, a.subcategoria_id AS SubcategoriaId
            FROM agendamentos a
            JOIN contas c ON a.conta_id = c.id
            JOIN subcategoria s ON a.subcategoria_id = s.id
            JOIN macrocategoria m ON s.macro_id = m.id
            JOIN grupocategoria g ON m.grupo_id = g.id
            WHERE a.usuario_id = @userId
              AND a.ativo = TRUE
              AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
              AND a.descricao ILIKE @like
            ORDER BY
              CASE WHEN LOWER(a.descricao) = LOWER(@exact) THEN 0 ELSE 1 END,
              CASE WHEN a.tipo_agendamento = 'LEMBRETE_VARIAVEL' THEN 0 ELSE 1 END,
              a.descricao,
              a.id
            LIMIT 1
            """,
            new { userId, like = "%" + description + "%", exact = description },
            cancellationToken: cancellationToken));
        return row is null ? null : new RecurringSchedule(row.Descricao, row.ValorPrevisto, row.ContaId, row.SubcategoriaId);
    }

    public async Task<int?> FindAccountIdByNameAsync(int userId, string name, bool fallback, string? type, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var typeFilter = type is null ? string.Empty : "AND tipo_conta = @type";
        var like = "%" + name + "%";

        foreach (var predicate in new[] { "nome_conta = @name", "nome_conta ILIKE @like" })
        {
            var id = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                $"SELECT id FROM contas WHERE usuario_id = @userId AND ativa = true AND {predicate} {typeFilter} ORDER BY id LIMIT 1",
                new { userId, name, like, type },
                cancellationToken: cancellationToken));
            if (id is not null)
            {
                return id;
            }
        }

        if (!fallback)
        {
            return null;
        }

        return await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            $"SELECT id FROM contas WHERE usuario_id = @userId AND ativa = true {typeFilter} ORDER BY id LIMIT 1",
            new { userId, type },
            cancellationToken: cancellationToken));
    }
}

/// <summary>Leituras do feedback da transação salva (<c>transaction_feedback_service.py:15-250</c>), com o período do pote do desvio 015.</summary>
public sealed class FeedbackReader(IDbConnectionFactory connections) : IFeedbackReader
{
    private sealed class TransactionRow
    {
        public int Id { get; set; }

        public int UsuarioId { get; set; }

        public int ContaId { get; set; }

        public int SubcategoriaId { get; set; }

        public string? Descricao { get; set; }

        public decimal Valor { get; set; }

        public string NomeConta { get; set; } = "";

        public string? TipoConta { get; set; }
    }

    private sealed class PotRow
    {
        public int Id { get; set; }

        public string NomePote { get; set; } = "";

        public decimal ValorLimite { get; set; }

        public string Periodicidade { get; set; } = "";
    }

    private sealed class AlertRow
    {
        public bool? AlertaPotesAtivo { get; set; }

        public int? AlertaPotesThreshold { get; set; }
    }

    private sealed class InvoiceRow
    {
        public string NomeConta { get; set; } = "";

        public int? DiaFechamento { get; set; }

        public decimal ValorTotal { get; set; }
    }

    private sealed class BalanceRow
    {
        public string NomeConta { get; set; } = "";

        public decimal? Saldo { get; set; }
    }

    public async Task<FeedbackTransaction?> FindTransactionAsync(int transactionId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync<TransactionRow>(new CommandDefinition(
            """
            SELECT t.id AS Id, t.usuario_id AS UsuarioId, t.conta_id AS ContaId, t.subcategoria_id AS SubcategoriaId,
                   t.descricao AS Descricao, t.valor AS Valor, c.nome_conta AS NomeConta, c.tipo_conta AS TipoConta
            FROM transacoes t
            JOIN contas c ON t.conta_id = c.id
            JOIN subcategoria s ON t.subcategoria_id = s.id
            WHERE t.id = @transactionId
            """,
            new { transactionId },
            cancellationToken: cancellationToken));
        return row is null
            ? null
            : new FeedbackTransaction(row.Id, row.UsuarioId, row.ContaId, row.SubcategoriaId, row.Descricao, (double)row.Valor, row.NomeConta, row.TipoConta);
    }

    public async Task<PotFeedback?> FindPotAsync(int userId, int subcategoryId, DateOnly today, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var pot = await connection.QueryFirstOrDefaultAsync<PotRow>(new CommandDefinition(
            """
            SELECT p.id AS Id, p.nome_pote AS NomePote, p.valor_limite AS ValorLimite, p.periodicidade AS Periodicidade
            FROM potesdegastos p
            JOIN potesubcategorias psc ON p.id = psc.pote_id
            WHERE p.usuario_id = @userId AND psc.subcategoria_id = @subcategoryId AND p.ativo = TRUE
            ORDER BY p.id
            LIMIT 1
            """,
            new { userId, subcategoryId },
            cancellationToken: cancellationToken));
        if (pot is null)
        {
            return null;
        }

        var (start, end) = BudgetPeriods.For(pot.Periodicidade, today);

        // Desvio 170: o gasto é o de TODAS as subcategorias do pote (como o aviso de orçamento da 011); o Flask somava só a da transação.
        var spent = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            """
            SELECT COALESCE(SUM(ABS(t.valor)), 0)
            FROM transacoes t
            WHERE t.subcategoria_id IN (SELECT subcategoria_id FROM potesubcategorias WHERE pote_id = @potId)
              AND t.usuario_id = @userId
              AND t.tipo_transacao = 'Despesa'
              AND t.data_transacao >= @start AND t.data_transacao <= @end
            """,
            new { userId, potId = pot.Id, start, end },
            cancellationToken: cancellationToken));

        var limit = (double)pot.ValorLimite;
        var spentValue = (double)spent;
        var percent = limit > 0 ? spentValue / limit * 100 : 0;
        return new PotFeedback(pot.NomePote, limit - spentValue, percent, PyStrings.Capitalize(pot.Periodicidade));
    }

    public async Task<PotAlertConfig?> GetPotAlertConfigAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync<AlertRow>(new CommandDefinition(
            "SELECT alerta_potes_ativo AS AlertaPotesAtivo, alerta_potes_threshold AS AlertaPotesThreshold FROM notificationconfigs WHERE usuario_id = @userId LIMIT 1",
            new { userId },
            cancellationToken: cancellationToken));
        return row is null ? null : new PotAlertConfig(row.AlertaPotesAtivo == true, row.AlertaPotesThreshold);
    }

    public async Task<FeedbackFooter.Invoice?> GetInvoiceFooterAsync(int userId, int accountId, DateOnly today, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync<InvoiceRow>(new CommandDefinition(
            """
            SELECT c.nome_conta AS NomeConta, c.dia_fechamento AS DiaFechamento, COALESCE(SUM(ABS(t.valor)), 0) AS ValorTotal
            FROM contas c
            LEFT JOIN faturas f ON f.conta_id = c.id AND f.data_vencimento > @today AND f.status = 'Aberta'
            LEFT JOIN transacoes t ON t.fatura_id = f.id AND t.tipo_transacao = 'Despesa'
            WHERE c.id = @accountId AND c.usuario_id = @userId AND c.tipo_conta = 'Cartão de Crédito'
            GROUP BY c.id, c.nome_conta, c.dia_fechamento
            """,
            new { userId, accountId, today },
            cancellationToken: cancellationToken));
        return row is null ? null : new FeedbackFooter.Invoice(row.NomeConta, (double)row.ValorTotal, row.DiaFechamento);
    }

    public async Task<FeedbackFooter.Balance?> GetBalanceFooterAsync(int userId, int accountId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync<BalanceRow>(new CommandDefinition(
            """
            SELECT c.nome_conta AS NomeConta, c.saldo_inicial + COALESCE(SUM(t.valor), 0) AS Saldo
            FROM contas c
            LEFT JOIN transacoes t ON t.conta_id = c.id AND t.usuario_id = @userId
            WHERE c.id = @accountId AND c.usuario_id = @userId
            GROUP BY c.id, c.nome_conta, c.saldo_inicial
            """,
            new { userId, accountId },
            cancellationToken: cancellationToken));
        if (row is null)
        {
            return null;
        }

        return new FeedbackFooter.Balance(
            row.NomeConta,
            row.Saldo is { } balance ? (double)balance : throw new InvalidOperationException("Saldo inicial nulo."));
    }
}
