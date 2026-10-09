using Dapper;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Whatsapp;
using MeuSecretario.Infrastructure.Database;

namespace MeuSecretario.Infrastructure.Whatsapp;

/// <summary>
/// Consultas financeiras do WhatsApp (<c>finance_service.py</c>, <c>period_query_service.py</c>), portadas literalmente: datas como
/// parâmetro (<c>IClock</c>), só contas ativas (desvio 039) e <c>ORDER BY</c> completo onde o Flask não fecha a ordem.
/// </summary>
public sealed class WhatsAppQueryReader(IDbConnectionFactory connections) : IWhatsAppQueryReader
{
    private sealed class BalanceRow
    {
        public string NomeConta { get; set; } = "";

        public string TipoConta { get; set; } = "";

        public decimal Saldo { get; set; }
    }

    private sealed class PotRow
    {
        public int Id { get; set; }

        public string NomePote { get; set; } = "";

        public decimal ValorLimite { get; set; }

        public string Periodicidade { get; set; } = "";
    }

    private sealed class InvoiceRow
    {
        public string NomeConta { get; set; } = "";

        public int FaturaId { get; set; }

        public DateTime DataVencimento { get; set; }

        public string Status { get; set; } = "";

        public decimal ValorFatura { get; set; }
    }

    private sealed class LineRow
    {
        public string Descricao { get; set; } = "";

        public decimal Valor { get; set; }

        public DateTime DataTransacao { get; set; }

        public string? TipoAgendamento { get; set; }

        public int? ParcelasExecutadas { get; set; }

        public int? TotalParcelas { get; set; }
    }

    private sealed class ExpenseRow
    {
        public string Descricao { get; set; } = "";

        public decimal Valor { get; set; }

        public string Categoria { get; set; } = "";

        public DateTime DataTransacao { get; set; }

        public string NomeMacro { get; set; } = "";
    }

    private sealed class CategoryRow
    {
        public string Descricao { get; set; } = "";

        public decimal Valor { get; set; }

        public DateTime DataTransacao { get; set; }
    }

    public async Task<IReadOnlyList<AccountBalance>> GetBalancesAsync(int userId, int? accountId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var filter = accountId is null ? string.Empty : "AND c.id = @accountId";
        var rows = await connection.QueryAsync<BalanceRow>(new CommandDefinition(
            $"""
            SELECT c.nome_conta AS NomeConta, c.tipo_conta AS TipoConta, c.saldo_inicial + COALESCE(SUM(t.valor), 0) AS Saldo
            FROM contas c
            LEFT JOIN transacoes t ON c.id = t.conta_id AND t.consolidada = true
            WHERE c.usuario_id = @userId {filter} AND c.ativa = true
            GROUP BY c.id, c.nome_conta, c.tipo_conta, c.saldo_inicial
            ORDER BY c.tipo_conta, c.nome_conta, c.id
            """,
            new { userId, accountId },
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => new AccountBalance(r.NomeConta, r.TipoConta, (double)r.Saldo))];
    }

    public async Task<IReadOnlyList<PotLine>> GetPotsAsync(int userId, DateOnly today, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var pots = (await connection.QueryAsync<PotRow>(new CommandDefinition(
            """
            SELECT id AS Id, nome_pote AS NomePote, valor_limite AS ValorLimite, periodicidade AS Periodicidade
            FROM potesdegastos
            WHERE usuario_id = @userId AND ativo = TRUE
            ORDER BY nome_pote, id
            """,
            new { userId },
            cancellationToken: cancellationToken))).ToList();

        var lines = new List<PotLine>();
        foreach (var pot in pots)
        {
            var (start, end) = BudgetPeriods.For(pot.Periodicidade, today);
            var spent = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
                """
                SELECT COALESCE(SUM(t.valor), 0)
                FROM transacoes t
                WHERE t.tipo_transacao = 'Despesa'
                  AND t.usuario_id = @userId
                  AND t.subcategoria_id IN (SELECT subcategoria_id FROM potesubcategorias WHERE pote_id = @potId)
                  AND t.data_transacao >= @start AND t.data_transacao <= @end
                """,
                new { userId, potId = pot.Id, start, end },
                cancellationToken: cancellationToken));
            lines.Add(new PotLine(pot.NomePote, (double)pot.ValorLimite, (double)spent));
        }

        return lines;
    }

    public async Task<IReadOnlyList<InvoiceSummary>> GetOpenInvoicesAsync(int userId, int? cardId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var cardFilter = cardId is null ? string.Empty : "AND c.id = @cardId";
        var limit = cardId is null ? string.Empty : "LIMIT 1";
        var rows = await connection.QueryAsync<InvoiceRow>(new CommandDefinition(
            $"""
            SELECT c.nome_conta AS NomeConta, COALESCE(SUM(CASE WHEN t.valor < 0 THEN ABS(t.valor) ELSE 0 END), 0) AS ValorFatura,
                   f.data_vencimento AS DataVencimento, f.status AS Status
            FROM faturas f
            JOIN contas c ON f.conta_id = c.id
            LEFT JOIN transacoes t ON f.id = t.fatura_id
            WHERE c.usuario_id = @userId {cardFilter} AND c.ativa = true AND f.status = 'Aberta'
            GROUP BY c.id, c.nome_conta, f.data_vencimento, f.status
            ORDER BY f.data_vencimento ASC, c.id
            {limit}
            """,
            new { userId, cardId },
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => new InvoiceSummary(r.NomeConta, (double)r.ValorFatura, DateOnly.FromDateTime(r.DataVencimento), r.Status))];
    }

    public async Task<InvoiceDetail?> GetInvoiceDetailAsync(int userId, int cardId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var invoice = await connection.QueryFirstOrDefaultAsync<InvoiceRow>(new CommandDefinition(
            """
            SELECT c.nome_conta AS NomeConta, f.id AS FaturaId, f.data_vencimento AS DataVencimento, f.status AS Status,
                   COALESCE(SUM(CASE WHEN t.valor < 0 THEN ABS(t.valor) ELSE 0 END), 0) AS ValorFatura
            FROM faturas f
            JOIN contas c ON f.conta_id = c.id
            LEFT JOIN transacoes t ON f.id = t.fatura_id
            WHERE c.usuario_id = @userId AND c.id = @cardId AND c.ativa = true AND f.status = 'Aberta'
            GROUP BY c.nome_conta, f.id, f.data_vencimento, f.status
            ORDER BY f.data_vencimento ASC, f.id
            LIMIT 1
            """,
            new { userId, cardId },
            cancellationToken: cancellationToken));
        if (invoice is null)
        {
            return null;
        }

        var lines = await connection.QueryAsync<LineRow>(new CommandDefinition(
            """
            SELECT t.descricao AS Descricao, t.valor AS Valor, t.data_transacao AS DataTransacao, a.tipo_agendamento AS TipoAgendamento,
                   a.parcelas_executadas AS ParcelasExecutadas, a.total_parcelas AS TotalParcelas
            FROM transacoes t
            LEFT JOIN agendamentos a ON t.agendamento_id = a.id
            WHERE t.fatura_id = @invoiceId AND t.valor < 0
            ORDER BY t.data_transacao DESC, t.descricao, t.id
            """,
            new { invoiceId = invoice.FaturaId },
            cancellationToken: cancellationToken));

        return new InvoiceDetail(
            invoice.NomeConta,
            (double)invoice.ValorFatura,
            DateOnly.FromDateTime(invoice.DataVencimento),
            invoice.Status,
            [.. lines.Select(l => new InvoiceLine(
                l.Descricao,
                Math.Abs((double)l.Valor),
                DateOnly.FromDateTime(l.DataTransacao),
                l.TipoAgendamento,
                l.TipoAgendamento == "PARCELADO" && l.TotalParcelas is { } and not 0 ? $"{l.ParcelasExecutadas}/{l.TotalParcelas}" : null))]);
    }

    public async Task<bool> UpdateInitialBalanceAsync(int userId, int accountId, decimal value, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var changed = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE contas SET saldo_inicial = @value WHERE id = @accountId AND usuario_id = @userId",
            new { value, accountId, userId },
            cancellationToken: cancellationToken));
        return changed > 0;
    }

    public async Task<double> GetCategorySpendingAsync(int userId, string categoryName, DateOnly today, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var total = await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(
            """
            WITH categoriaalvo AS (
                SELECT id FROM subcategoria WHERE (usuario_id = @userId OR usuario_id IS NULL) AND nome_sub ILIKE @like
                UNION
                SELECT s.id FROM subcategoria s
                JOIN macrocategoria m ON s.macro_id = m.id
                WHERE (m.usuario_id = @userId OR m.usuario_id IS NULL) AND m.nome_macro ILIKE @like
            )
            SELECT COALESCE(SUM(t.valor), 0)
            FROM transacoes t
            WHERE t.usuario_id = @userId
              AND t.tipo_transacao = 'Despesa'
              AND EXTRACT(MONTH FROM t.data_transacao) = @month
              AND EXTRACT(YEAR FROM t.data_transacao) = @year
              AND t.subcategoria_id IN (SELECT id FROM categoriaalvo)
            """,
            new { userId, like = "%" + categoryName + "%", month = today.Month, year = today.Year },
            cancellationToken: cancellationToken));
        return (double)(total ?? 0m);
    }

    public async Task<IReadOnlyList<PeriodExpense>> ListExpensesAsync(int userId, DateOnly start, DateOnly last, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<ExpenseRow>(new CommandDefinition(
            """
            SELECT t.descricao AS Descricao, t.valor AS Valor, s.nome_sub AS Categoria, t.data_transacao AS DataTransacao, m.nome_macro AS NomeMacro
            FROM transacoes t
            JOIN subcategoria s ON t.subcategoria_id = s.id
            JOIN macrocategoria m ON s.macro_id = m.id
            WHERE t.usuario_id = @userId AND t.tipo_transacao = 'Despesa'
              AND t.data_transacao >= @start AND t.data_transacao <= @end
            ORDER BY t.data_transacao DESC, t.created_at DESC, t.id DESC
            """,
            new { userId, start, end = last },
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => new PeriodExpense(r.Descricao, (double)r.Valor, r.Categoria, DateOnly.FromDateTime(r.DataTransacao), r.NomeMacro))];
    }

    public async Task<IReadOnlyList<CategoryExpense>> ListCategoryExpensesAsync(
        int userId, string category, DateOnly start, DateOnly last, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<CategoryRow>(new CommandDefinition(
            """
            SELECT t.descricao AS Descricao, t.valor AS Valor, t.data_transacao AS DataTransacao
            FROM transacoes t
            JOIN subcategoria s ON t.subcategoria_id = s.id
            JOIN macrocategoria m ON s.macro_id = m.id
            WHERE t.usuario_id = @userId AND t.tipo_transacao = 'Despesa'
              AND t.data_transacao >= @start AND t.data_transacao <= @end
              AND (s.nome_sub ILIKE @like OR m.nome_macro ILIKE @like)
            ORDER BY t.data_transacao DESC, t.id DESC
            """,
            new { userId, like = "%" + category + "%", start, end = last },
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => new CategoryExpense(r.Descricao, (double)r.Valor, DateOnly.FromDateTime(r.DataTransacao)))];
    }
}
