using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Jobs;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.Application.Whatsapp;

public sealed record ReportCard(int Id, string Name);

public enum InvoiceSelectionKind
{
    /// <summary>A fatura aberta (ou, sem nenhuma aberta, a mais recente).</summary>
    Open,

    /// <summary>A fatura logo antes da aberta.</summary>
    Previous,

    /// <summary>A que vence no mês de <see cref="InvoiceSelection.DueMonth"/>.</summary>
    DueMonth,
}

public sealed record InvoiceSelection(InvoiceSelectionKind Kind, DateOnly? DueMonth = null);

/// <summary>Leitura dos relatórios sob comando (desvio 256).</summary>
public interface IReportsReader
{
    Task<IReadOnlyList<ReportCard>> ListCardsAsync(int userId, CancellationToken cancellationToken);

    Task<CardInvoiceReport?> GetInvoiceAsync(
        int userId, int cardId, InvoiceSelection selection, DateOnly today, CancellationToken cancellationToken);

    Task<MonthByCategoryReport> GetMonthByCategoryAsync(int userId, DateOnly monthStart, DateOnly today, CancellationToken cancellationToken);
}

/// <summary>
/// Relatórios pedidos pelo WhatsApp (desvio 256): "fatura do inter por categoria", "gastos do mês por categoria" e "relatório do mês". Roda
/// antes do Gemini e só responde quando a mensagem é um desses pedidos (<see cref="ReportCommandParser"/>); o resto segue o pipeline.
/// </summary>
public sealed class ReportCommandStep(IReportsReader reader, IMonthlyReportJob monthlyReport, IClock clock) : IWhatsAppStep
{
    public int Order => StepOrder.Reports;

    public async Task<string?> HandleAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var request = ReportCommandParser.Parse(context.Message, clock.Today);
        if (request is null)
        {
            return null;
        }

        try
        {
            return request.Kind switch
            {
                ReportKind.CardInvoice => await InvoiceAsync(context, request, cancellationToken),
                ReportKind.MonthByCategory => await MonthByCategoryAsync(context, request, cancellationToken),
                _ => await MonthlyReportAsync(context, request, cancellationToken),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ReportMessages.FailedGeneric;
        }
    }

    private async Task<string> InvoiceAsync(PipelineContext context, ReportRequest request, CancellationToken cancellationToken)
    {
        var cards = await reader.ListCardsAsync(context.User.Id, cancellationToken);
        if (cards.Count == 0)
        {
            return ReportMessages.NoCard;
        }

        var card = PickCard(cards, request.Text);
        if (card is null)
        {
            return ReportMessages.ChooseCard(cards.Select(c => c.Name));
        }

        var today = clock.Today;
        var selection = request.Period switch
        {
            ReportPeriod.Previous => new InvoiceSelection(InvoiceSelectionKind.Previous),
            ReportPeriod.Specific => new InvoiceSelection(InvoiceSelectionKind.DueMonth, request.MonthStart(today)),
            _ => new InvoiceSelection(InvoiceSelectionKind.Open),
        };

        var report = await reader.GetInvoiceAsync(context.User.Id, card.Id, selection, today, cancellationToken);
        if (report is null)
        {
            var when = request.Period == ReportPeriod.Specific ? ReportMessages.MonthLabel(request.MonthStart(today)) : null;
            return ReportMessages.NoInvoice(card.Name, when);
        }

        return ReportMessages.Invoice(report);
    }

    private async Task<string> MonthByCategoryAsync(PipelineContext context, ReportRequest request, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var month = request.MonthStart(today);
        var report = await reader.GetMonthByCategoryAsync(context.User.Id, month, today, cancellationToken);
        return report.Categories.Count == 0 ? ReportMessages.NoTransactions(ReportMessages.MonthLabel(month)) : ReportMessages.MonthByCategory(report);
    }

    private async Task<string> MonthlyReportAsync(PipelineContext context, ReportRequest request, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var month = request.MonthStart(today);
        var current = new DateOnly(today.Year, today.Month, 1);
        MonthlyMoment? moment = month == current ? MonthlyMoment.FimMes : month == current.AddMonths(-1) ? MonthlyMoment.InicioMes : null;
        if (moment is null)
        {
            return ReportMessages.MonthlyReportRange;
        }

        // O próprio relatório (texto e gráfico) chega pelo bot; esta resposta só confirma, na sequência.
        var result = await monthlyReport.SendManualAsync(context.User.Id, moment.Value, cancellationToken);
        if (result.Success)
        {
            return ReportMessages.MonthlyReportSent;
        }

        return result.Data is not null && result.Data.Totals.TotalTransactions == 0
            ? ReportMessages.NoReportData(ReportMessages.MonthLabel(month))
            : ReportMessages.FailedGeneric;
    }

    /// <summary>Com um cartão só, é ele; com vários, o que a mensagem cita (ex.: "inter"); sem pista, nenhum.</summary>
    private static ReportCard? PickCard(IReadOnlyList<ReportCard> cards, string text)
    {
        if (cards.Count == 1)
        {
            return cards[0];
        }

        var words = text.Split([' ', ',', '.', '?', '!'], StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        string[] noise = ["cartao", "de", "do", "da", "credito", "card"];
        return cards.FirstOrDefault(card => ReportCommandParser.Normalize(card.Name)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(token => token.Length >= 3 && !noise.Contains(token) && words.Contains(token)));
    }
}
