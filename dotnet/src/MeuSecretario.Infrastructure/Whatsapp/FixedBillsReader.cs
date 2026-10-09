using Dapper;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Domain.Whatsapp;
using MeuSecretario.Infrastructure.Database;

namespace MeuSecretario.Infrastructure.Whatsapp;

/// <summary>
/// Contas fixas e vencimentos do WhatsApp (<c>fixed_bills_service.py</c> e <c>finance_service.get_vencimentos_periodo</c>), portados
/// literalmente: datas como parâmetro (<c>IClock</c>), "já pago" por igualdade de <c>descricao</c> (spec 012 §6.d; a checagem dos
/// vencimentos não filtra o usuário, desvio 161 proposto) e <c>ORDER BY</c> completo onde o Flask deixa empate.
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
    }

    private sealed class DueRow
    {
        public string Descricao { get; set; } = "";

        public decimal? ValorPrevisto { get; set; }

        public int DiaExecucao { get; set; }

        public string NomeGrupo { get; set; } = "";
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
        var rows = await connection.QueryAsync<PendingRow>(new CommandDefinition(
            """
            SELECT a.descricao AS Descricao, a.valor_previsto AS ValorPrevisto, a.dia_execucao AS DiaExecucao,
                   s.nome_sub AS Categoria, c.tipo_conta AS TipoConta, g.nome_grupo AS NomeGrupo
            FROM agendamentos a
            JOIN subcategoria s ON a.subcategoria_id = s.id
            JOIN macrocategoria m ON s.macro_id = m.id
            JOIN grupocategoria g ON m.grupo_id = g.id
            JOIN contas c ON a.conta_id = c.id
            WHERE a.usuario_id = @userId
              AND a.ativo = TRUE
              AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
              AND (a.periodicidade != 'ANUAL' OR (a.periodicidade = 'ANUAL' AND a.mes_execucao = @month))
              AND (SELECT COUNT(*) FROM transacoes t
                   WHERE t.descricao = a.descricao
                     AND t.usuario_id = a.usuario_id
                     AND t.data_transacao >= @firstDay
                     AND t.data_transacao <= @lastDay) = 0
            ORDER BY a.dia_execucao ASC, a.id
            """,
            new { userId, month, firstDay, lastDay },
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => new PendingBill(r.Descricao, (double)(r.ValorPrevisto ?? 0m), r.Categoria, r.DiaExecucao, r.TipoConta, r.NomeGrupo))];
    }

    public async Task<IReadOnlyList<CatalogBill>> ListCatalogAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<CatalogRow>(new CommandDefinition(
            """
            SELECT a.descricao AS Descricao, a.valor_previsto AS ValorPrevisto, a.dia_execucao AS DiaExecucao,
                   a.mes_execucao AS MesExecucao, a.periodicidade AS Periodicidade, s.nome_sub AS Categoria,
                   c.tipo_conta AS TipoConta, g.nome_grupo AS NomeGrupo
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
            r.Descricao, (double)(r.ValorPrevisto ?? 0m), r.DiaExecucao, r.MesExecucao, r.Periodicidade, r.Categoria, r.TipoConta, r.NomeGrupo))];
    }

    public async Task<IReadOnlyList<DueBill>> ListDueBillsAsync(int userId, DueSlice slice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slice);
        await using var connection = await connections.OpenAsync(cancellationToken);

        // Fragmentos fixos escolhidos por if; o texto do cliente nunca entra na consulta.
        var days = (slice.DayFrom, slice.DayTo) switch
        {
            ({ } _, { } _) => "AND a.dia_execucao BETWEEN @dayFrom AND @dayTo",
            ({ } _, null) => "AND a.dia_execucao >= @dayFrom",
            _ => "AND a.dia_execucao <= @dayTo",
        };

        var rows = await connection.QueryAsync<DueRow>(new CommandDefinition(
            $"""
            SELECT a.descricao AS Descricao, a.valor_previsto AS ValorPrevisto, a.dia_execucao AS DiaExecucao, g.nome_grupo AS NomeGrupo
            FROM agendamentos a
            JOIN contas c ON a.conta_id = c.id
            JOIN subcategoria s ON a.subcategoria_id = s.id
            JOIN macrocategoria m ON s.macro_id = m.id
            JOIN grupocategoria g ON m.grupo_id = g.id
            WHERE a.usuario_id = @userId
              AND a.ativo = TRUE
              AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
              {days}
              AND (a.periodicidade != 'ANUAL' OR (a.periodicidade = 'ANUAL' AND a.mes_execucao = @month))
              AND NOT EXISTS (
                  SELECT 1 FROM transacoes t
                  WHERE t.descricao = a.descricao
                    AND EXTRACT(MONTH FROM t.data_transacao) = @month
                    AND EXTRACT(YEAR FROM t.data_transacao) = @year
              )
            ORDER BY a.dia_execucao ASC, a.id
            """,
            new { userId, dayFrom = slice.DayFrom, dayTo = slice.DayTo, month = slice.Month, year = slice.Year },
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => new DueBill(r.Descricao, r.ValorPrevisto, r.DiaExecucao, r.NomeGrupo))];
    }

    public async Task<IReadOnlyList<DueInvoice>> ListDueInvoicesAsync(int userId, DateOnly first, DateOnly last, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<InvoiceRow>(new CommandDefinition(
            """
            SELECT c.nome_conta AS NomeConta, f.data_vencimento AS DataVencimento,
                   COALESCE(SUM(CASE WHEN t.valor < 0 THEN ABS(t.valor) ELSE 0 END), 0) AS ValorFatura
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
