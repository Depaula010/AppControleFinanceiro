using Dapper;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Domain.Whatsapp;
using MeuSecretario.Infrastructure.Database;

namespace MeuSecretario.Infrastructure.Whatsapp;

/// <summary>
/// Leitura dos relatórios sob comando (desvio 256): só <c>SELECT</c>, sempre do usuário da mensagem e de contas ativas. "Outros" leva o
/// nome do grupo ("Outros (Lazer e Entretenimento)") porque a subcategoria se repete em vários grupos.
/// </summary>
public sealed class ReportsReader(IDbConnectionFactory connections) : IReportsReader
{
    private const string CategoryName =
        "CASE WHEN s.nome_sub = 'Outros' THEN 'Outros (' || m.nome_macro || ')' ELSE s.nome_sub END";

    private const int TopPurchases = 5;

    public async Task<IReadOnlyList<ReportCard>> ListCardsAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<CardRow>(new CommandDefinition(
            """
            SELECT c.id AS Id, c.nome_conta AS Name
            FROM contas c
            WHERE c.usuario_id = @userId AND c.ativa = TRUE AND c.tipo_conta = 'Cartão de Crédito'
            ORDER BY c.id
            """,
            new { userId },
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => new ReportCard(r.Id, r.Name))];
    }

    public async Task<CardInvoiceReport?> GetInvoiceAsync(
        int userId, int cardId, InvoiceSelection selection, DateOnly today, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var invoice = await FindInvoiceAsync(connection, userId, cardId, selection, today, cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        var totals = await connection.QuerySingleAsync<TotalsRow>(new CommandDefinition(
            """
            SELECT COALESCE(SUM(-t.valor) FILTER (WHERE t.valor < 0), 0) AS Purchases,
                   COALESCE(SUM(t.valor) FILTER (WHERE t.valor > 0), 0) AS Refunds,
                   COUNT(*) AS Count
            FROM transacoes t WHERE t.fatura_id = @invoiceId
            """,
            new { invoiceId = invoice.Id },
            cancellationToken: cancellationToken));

        var categories = await connection.QueryAsync<CategoryRow>(new CommandDefinition(
            $"""
            SELECT {CategoryName} AS Name, SUM(-t.valor) AS Amount, COUNT(*) AS Count
            FROM transacoes t
            JOIN subcategoria s ON s.id = t.subcategoria_id
            JOIN macrocategoria m ON m.id = s.macro_id
            WHERE t.fatura_id = @invoiceId AND t.valor < 0
            GROUP BY 1 ORDER BY 2 DESC, 1
            """,
            new { invoiceId = invoice.Id },
            cancellationToken: cancellationToken));

        var top = await connection.QueryAsync<LineRow>(new CommandDefinition(
            """
            SELECT t.descricao AS Description, t.data_transacao AS Date, -t.valor AS Amount
            FROM transacoes t
            WHERE t.fatura_id = @invoiceId AND t.valor < 0
            ORDER BY t.valor ASC, t.data_transacao DESC, t.id
            LIMIT @limit
            """,
            new { invoiceId = invoice.Id, limit = TopPurchases },
            cancellationToken: cancellationToken));

        // Parcelas a vencer: "(3/6)" no fim da descrição; faltam (6 - 3) parcelas do mesmo valor.
        var installments = await connection.QuerySingleAsync<InstallmentRow>(new CommandDefinition(
            """
            SELECT COUNT(*) AS Purchases, COALESCE(SUM(x.valor * (x.total - x.atual)), 0) AS Remaining
            FROM (
                SELECT -t.valor AS valor,
                       ((regexp_match(t.descricao, '\((\d+)/(\d+)\)\s*$'))[1])::int AS atual,
                       ((regexp_match(t.descricao, '\((\d+)/(\d+)\)\s*$'))[2])::int AS total
                FROM transacoes t
                WHERE t.fatura_id = @invoiceId AND t.valor < 0 AND t.descricao ~ '\(\d+/\d+\)\s*$'
            ) x
            WHERE x.atual < x.total
            """,
            new { invoiceId = invoice.Id },
            cancellationToken: cancellationToken));

        return new CardInvoiceReport(
            invoice.CardName,
            DateOnly.FromDateTime(invoice.DueDate),
            DateOnly.FromDateTime(invoice.ClosingDate),
            invoice.Status,
            totals.Purchases,
            totals.Refunds,
            totals.Count,
            [.. categories.Select(c => new CategoryAmount(c.Name, c.Amount, c.Count))],
            [.. top.Select(l => new ReportInvoiceLine(l.Description, DateOnly.FromDateTime(l.Date), l.Amount))],
            installments.Purchases,
            installments.Remaining);
    }

    public async Task<MonthByCategoryReport> GetMonthByCategoryAsync(
        int userId, DateOnly monthStart, DateOnly today, CancellationToken cancellationToken)
    {
        var monthEnd = monthStart.AddMonths(1);
        var partial = today < monthEnd && today >= monthStart;
        var until = partial ? today : monthEnd.AddDays(-1);

        // Mês atual pela metade: compara com o mesmo trecho do anterior (até o mesmo dia), não com o mês inteiro.
        var previousStart = monthStart.AddMonths(-1);
        var elapsedDays = until.DayNumber - monthStart.DayNumber + 1;
        var previousEndExclusive = partial ? previousStart.AddDays(Math.Min(elapsedDays, monthStart.DayNumber - previousStart.DayNumber)) : monthStart;

        await using var connection = await connections.OpenAsync(cancellationToken);
        var current = await CategoryTotalsListAsync(connection, userId, monthStart, until.AddDays(1), cancellationToken);
        var previous = (await CategoryTotalsListAsync(connection, userId, previousStart, previousEndExclusive, cancellationToken))
            .ToDictionary(c => c.Name, c => (c.Amount, c.Count), StringComparer.Ordinal);

        var rows = current
            .Select(c => new CategoryDelta(c.Name, c.Amount, previous.TryGetValue(c.Name, out var before) ? before.Amount : 0m, c.Count))
            .OrderByDescending(c => c.Amount)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToList();
        return new MonthByCategoryReport(monthStart, until, partial, rows.Sum(r => r.Amount), previous.Values.Sum(p => p.Amount), rows);
    }

    private static async Task<IReadOnlyList<(string Name, decimal Amount, int Count)>> CategoryTotalsListAsync(
        System.Data.Common.DbConnection connection, int userId, DateOnly from, DateOnly toExclusive, CancellationToken cancellationToken) =>
        [.. (await connection.QueryAsync<CategoryRow>(new CommandDefinition(
            $"""
            SELECT {CategoryName} AS Name, SUM(-t.valor) AS Amount, COUNT(*) AS Count
            FROM transacoes t
            JOIN subcategoria s ON s.id = t.subcategoria_id
            JOIN macrocategoria m ON m.id = s.macro_id
            WHERE t.usuario_id = @userId AND t.tipo_transacao = 'Despesa' AND t.valor < 0
              AND t.data_transacao >= @from AND t.data_transacao < @toExclusive
            GROUP BY 1
            """,
            new { userId, from = from.ToDateTime(TimeOnly.MinValue), toExclusive = toExclusive.ToDateTime(TimeOnly.MinValue) },
            cancellationToken: cancellationToken))).Select(r => (r.Name, r.Amount, r.Count))];

    private static async Task<InvoiceRow?> FindInvoiceAsync(
        System.Data.Common.DbConnection connection, int userId, int cardId, InvoiceSelection selection, DateOnly today, CancellationToken cancellationToken)
    {
        const string select = """
            SELECT f.id AS Id, c.nome_conta AS CardName, f.data_vencimento AS DueDate, f.data_fechamento AS ClosingDate, f.status AS Status
            FROM faturas f JOIN contas c ON c.id = f.conta_id
            WHERE c.usuario_id = @userId AND c.id = @cardId AND c.ativa = TRUE
            """;
        var todayValue = today.ToDateTime(TimeOnly.MinValue);

        switch (selection.Kind)
        {
            case InvoiceSelectionKind.DueMonth:
                var monthStart = (selection.DueMonth ?? today).ToDateTime(TimeOnly.MinValue);
                return await connection.QueryFirstOrDefaultAsync<InvoiceRow>(new CommandDefinition(
                    $"{select} AND date_trunc('month', f.data_vencimento) = @monthStart ORDER BY f.data_vencimento DESC LIMIT 1",
                    new { userId, cardId, monthStart },
                    cancellationToken: cancellationToken));
            case InvoiceSelectionKind.Previous:
                return await connection.QueryFirstOrDefaultAsync<InvoiceRow>(new CommandDefinition(
                    $"""
                    {select}
                      AND f.data_vencimento < COALESCE(
                            (SELECT MIN(a.data_vencimento) FROM faturas a WHERE a.conta_id = c.id AND a.status = 'Aberta'), @today)
                    ORDER BY f.data_vencimento DESC LIMIT 1
                    """,
                    new { userId, cardId, today = todayValue },
                    cancellationToken: cancellationToken));
            default:
                var open = await connection.QueryFirstOrDefaultAsync<InvoiceRow>(new CommandDefinition(
                    $"{select} AND f.status = 'Aberta' ORDER BY f.data_vencimento LIMIT 1",
                    new { userId, cardId },
                    cancellationToken: cancellationToken));
                return open ?? await connection.QueryFirstOrDefaultAsync<InvoiceRow>(new CommandDefinition(
                    $"{select} ORDER BY f.data_vencimento DESC LIMIT 1",
                    new { userId, cardId },
                    cancellationToken: cancellationToken));
        }
    }

    private sealed class CardRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";
    }

    private sealed class InvoiceRow
    {
        public int Id { get; set; }

        public string CardName { get; set; } = "";

        public DateTime DueDate { get; set; }

        public DateTime ClosingDate { get; set; }

        public string Status { get; set; } = "";
    }

    private sealed class TotalsRow
    {
        public decimal Purchases { get; set; }

        public decimal Refunds { get; set; }

        public int Count { get; set; }
    }

    private sealed class CategoryRow
    {
        public string Name { get; set; } = "";

        public decimal Amount { get; set; }

        public int Count { get; set; }
    }

    private sealed class LineRow
    {
        public string Description { get; set; } = "";

        public DateTime Date { get; set; }

        public decimal Amount { get; set; }
    }

    private sealed class InstallmentRow
    {
        public int Purchases { get; set; }

        public decimal Remaining { get; set; }
    }
}
