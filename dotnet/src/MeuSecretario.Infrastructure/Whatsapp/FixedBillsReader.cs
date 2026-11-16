using Dapper;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Domain.Whatsapp;
using MeuSecretario.Infrastructure.Database;
using MeuSecretario.Infrastructure.Jobs;

namespace MeuSecretario.Infrastructure.Whatsapp;

/// <summary>
/// Contas fixas e vencimentos do WhatsApp (<c>fixed_bills_service.py</c> e <c>finance_service.get_vencimentos_periodo</c>) com datas como
/// parâmetro (<c>IClock</c>) e <c>ORDER BY</c> completo onde o Flask deixa empate. Desvio 280: o vencimento e o "já pago" das contas
/// pendentes e dos vencimentos vêm de <see cref="SchedulePaymentSql"/>, a mesma definição do check-in (o Flask comparava <c>descricao</c> e
/// mês, e o desvio 161 passou a filtrar o usuário).
/// </summary>
public sealed class FixedBillsReader(IDbConnectionFactory connections) : IWhatsAppFixedBillsReader
{
    private sealed class PendingRow
    {
        public string Descricao { get; set; } = "";

        public decimal? ValorPrevisto { get; set; }

        public int DiaExecucao { get; set; }

        public string Categoria { get; set; } = "";

        public string TipoConta { get; set; } = "";

        public string NomeGrupo { get; set; } = "";

        public int? TotalParcelas { get; set; }

        public int? ParcelasExecutadas { get; set; }
    }

    private sealed class CatalogRow
    {
        public string Descricao { get; set; } = "";

        public decimal? ValorPrevisto { get; set; }

        public int DiaExecucao { get; set; }

        public int? MesExecucao { get; set; }

        public string? Periodicidade { get; set; }

        public string Categoria { get; set; } = "";

        public string TipoConta { get; set; } = "";

        public string NomeGrupo { get; set; } = "";

        public int? TotalParcelas { get; set; }

        public int? ParcelasExecutadas { get; set; }
    }

    private sealed class DueRow
    {
        public string Descricao { get; set; } = "";

        public decimal? ValorPrevisto { get; set; }

        public int DiaExecucao { get; set; }

        public string NomeGrupo { get; set; } = "";

        public int? TotalParcelas { get; set; }

        public int? ParcelasExecutadas { get; set; }
    }

    private sealed class InvoiceRow
    {
        public string NomeConta { get; set; } = "";

        public DateTime DataVencimento { get; set; }

        public decimal ValorFatura { get; set; }
    }

    public async Task<IReadOnlyList<PendingBill>> ListPendingAsync(
        int userId, DateOnly firstDay, DateOnly lastDay, int month, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);

        // Desvio 280: o vencimento do mês e o "já pago" são os do check-in (SchedulePaymentSql). Contínuo atrasado também pende.
        var rows = await connection.QueryAsync<PendingRow>(new CommandDefinition(
            $"""
            SELECT a.descricao AS Descricao, a.valor_previsto AS ValorPrevisto, EXTRACT(DAY FROM d.due)::int AS DiaExecucao,
                   s.nome_sub AS Categoria, c.tipo_conta AS TipoConta, g.nome_grupo AS NomeGrupo,
                   a.total_parcelas AS TotalParcelas, a.parcelas_executadas AS ParcelasExecutadas
            FROM agendamentos a
            JOIN subcategoria s ON a.subcategoria_id = s.id
            JOIN macrocategoria m ON s.macro_id = m.id
            JOIN grupocategoria g ON m.grupo_id = g.id
            JOIN contas c ON a.conta_id = c.id
            CROSS JOIN LATERAL {SchedulePaymentSql.DueDates("a", "@firstDay", "@lastDay")} AS d(due)
            WHERE a.usuario_id = @userId
              AND a.ativo = TRUE
              AND {SchedulePaymentSql.ListedKind("a")}
              AND (({SchedulePaymentSql.IsRolling("a")} AND d.due <= @lastDay)
                   OR d.due BETWEEN @firstDay AND @lastDay)
              AND NOT EXISTS (SELECT 1 FROM transacoes t WHERE {SchedulePaymentSql.Paid("a", "t", "d.due", "a.descricao")})
            ORDER BY d.due ASC, a.id
            """,
            new { userId, firstDay = firstDay.ToDateTime(TimeOnly.MinValue), lastDay = lastDay.ToDateTime(TimeOnly.MinValue) },
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => new PendingBill(
            r.Descricao, (double)(r.ValorPrevisto ?? 0m), r.Categoria, r.DiaExecucao, r.TipoConta, r.NomeGrupo,
            r.TotalParcelas, r.ParcelasExecutadas))];
    }

    public async Task<IReadOnlyList<CatalogBill>> ListCatalogAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<CatalogRow>(new CommandDefinition(
            """
            SELECT a.descricao AS Descricao, a.valor_previsto AS ValorPrevisto, a.dia_execucao AS DiaExecucao,
                   a.mes_execucao AS MesExecucao, a.periodicidade AS Periodicidade, s.nome_sub AS Categoria,
                   c.tipo_conta AS TipoConta, g.nome_grupo AS NomeGrupo,
                   a.total_parcelas AS TotalParcelas, a.parcelas_executadas AS ParcelasExecutadas
            FROM agendamentos a
            JOIN subcategoria s ON a.subcategoria_id = s.id
            JOIN macrocategoria m ON s.macro_id = m.id
            JOIN grupocategoria g ON m.grupo_id = g.id
            JOIN contas c ON a.conta_id = c.id
            WHERE a.usuario_id = @userId
              AND a.ativo = TRUE
              AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL', 'PARCELADO')
            ORDER BY
                CASE a.periodicidade
                    WHEN 'SEMANAL'    THEN 1
                    WHEN 'QUINZENAL'  THEN 2
                    WHEN 'MENSAL'     THEN 3
                    WHEN 'ANUAL'      THEN 4
                    ELSE 5
                END,
                a.dia_execucao ASC, a.id
            """,
            new { userId },
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => new CatalogBill(
            r.Descricao, (double)(r.ValorPrevisto ?? 0m), r.DiaExecucao, r.MesExecucao, r.Periodicidade, r.Categoria, r.TipoConta, r.NomeGrupo,
            r.TotalParcelas, r.ParcelasExecutadas))];
    }

    public async Task<IReadOnlyList<DueBill>> ListDueBillsAsync(int userId, DateOnly first, DateOnly last, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);

        // Desvio 280: próximo vencimento (cadência, mês curto) e "já pago" (competência) pelo mesmo SQL do check-in.
        var rows = await connection.QueryAsync<DueRow>(new CommandDefinition(
            $"""
            SELECT a.descricao AS Descricao, a.valor_previsto AS ValorPrevisto, EXTRACT(DAY FROM d.due)::int AS DiaExecucao,
                   g.nome_grupo AS NomeGrupo, a.total_parcelas AS TotalParcelas, a.parcelas_executadas AS ParcelasExecutadas
            FROM agendamentos a
            JOIN contas c ON a.conta_id = c.id
            JOIN subcategoria s ON a.subcategoria_id = s.id
            JOIN macrocategoria m ON s.macro_id = m.id
            JOIN grupocategoria g ON m.grupo_id = g.id
            CROSS JOIN LATERAL {SchedulePaymentSql.DueDates("a", "@start", "@end")} AS d(due)
            WHERE a.usuario_id = @userId
              AND a.ativo = TRUE
              AND {SchedulePaymentSql.ListedKind("a")}
              AND d.due BETWEEN @start AND @end
              AND (a.data_inicio IS NULL OR d.due >= a.data_inicio)
              AND NOT EXISTS (SELECT 1 FROM transacoes t WHERE {SchedulePaymentSql.Paid("a", "t", "d.due", "a.descricao")})
            ORDER BY d.due ASC, a.id
            """,
            new { userId, start = first.ToDateTime(TimeOnly.MinValue), end = last.ToDateTime(TimeOnly.MinValue) },
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => new DueBill(r.Descricao, r.ValorPrevisto, r.DiaExecucao, r.NomeGrupo, r.TotalParcelas, r.ParcelasExecutadas))];
    }

    public async Task<IReadOnlyList<DueInvoice>> ListDueInvoicesAsync(int userId, DateOnly first, DateOnly last, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<InvoiceRow>(new CommandDefinition(
            """
            SELECT c.nome_conta AS NomeConta, f.data_vencimento AS DataVencimento,
                   GREATEST(COALESCE(-SUM(CASE WHEN t.tipo_transacao IN ('Despesa', 'Renda') THEN t.valor ELSE 0 END), 0), 0) AS ValorFatura
            FROM faturas f
            JOIN contas c ON f.conta_id = c.id
            LEFT JOIN transacoes t ON f.id = t.fatura_id
            WHERE c.usuario_id = @userId
              AND f.status = 'Aberta'
              AND f.data_vencimento BETWEEN @first AND @last
            GROUP BY c.nome_conta, f.data_vencimento, f.status
            ORDER BY f.data_vencimento ASC, c.nome_conta
            """,
            new { userId, first, last },
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => new DueInvoice(r.NomeConta, DateOnly.FromDateTime(r.DataVencimento), r.ValorFatura))];
    }
}
