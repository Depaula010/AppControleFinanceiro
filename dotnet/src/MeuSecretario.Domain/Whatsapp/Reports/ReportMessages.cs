using System.Globalization;
using System.Text;
using MeuSecretario.Domain.Jobs;

namespace MeuSecretario.Domain.Whatsapp;

public sealed record CategoryAmount(string Name, decimal Amount, int Count);

public sealed record ReportInvoiceLine(string Description, DateOnly Date, decimal Amount);

/// <summary>Uma fatura do cartão (desvio 256). <c>Purchases</c> são os valores negativos (como o total da fatura, desvio 131); <c>Refunds</c>, os estornos.</summary>
/// <param name="InstallmentPurchases">Compras parceladas com parcela a vencer ("(3/6)" na descrição) e quanto ainda falta pagar nelas.</param>
public sealed record CardInvoiceReport(
    string CardName,
    DateOnly DueDate,
    DateOnly ClosingDate,
    string Status,
    decimal Purchases,
    decimal Refunds,
    int Count,
    IReadOnlyList<CategoryAmount> Categories,
    IReadOnlyList<ReportInvoiceLine> TopPurchases,
    int InstallmentPurchases,
    decimal InstallmentsRemaining);

public sealed record CategoryDelta(string Name, decimal Amount, decimal Previous, int Count);

/// <param name="UntilDate">Último dia contado; é hoje se o mês ainda não acabou.</param>
/// <param name="PreviousTotal">O mês anterior no mesmo trecho (até o mesmo dia, se o mês atual está pela metade).</param>
public sealed record MonthByCategoryReport(
    DateOnly MonthStart, DateOnly UntilDate, bool Partial, decimal Total, decimal PreviousTotal, IReadOnlyList<CategoryDelta> Categories);

/// <summary>Textos dos relatórios sob comando (desvio 256).</summary>
public static class ReportMessages
{
    private const int InvoiceCategoryLimit = 8;
    private const int MonthCategoryLimit = 10;
    private static readonly string[] Months =
    [
        "janeiro", "fevereiro", "março", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro",
    ];

    public const string NoCard = "💳 Você não tem cartão de crédito cadastrado.";

    public const string FailedGeneric = "❌ Não consegui gerar o relatório agora. Tente de novo em instantes.";

    public const string MonthlyReportRange = "📊 O relatório mensal sob comando cobre o mês atual e o anterior (ex.: \"relatório do mês\" ou \"relatório do mês passado\").";

    public const string MonthlyReportSent = "✅ Relatório enviado!";

    public static string MonthLabel(DateOnly month) =>
        string.Create(CultureInfo.InvariantCulture, $"{Months[month.Month - 1]}/{month.Year}");

    public static string ChooseCard(IEnumerable<string> names) =>
        $"💳 De qual cartão? Tenho: {string.Join(", ", names)}.\nEx.: \"fatura do {names.First()} por categoria\"";

    public static string NoInvoice(string card, string? when) =>
        when is null ? $"💳 Não encontrei fatura do {card}." : $"💳 Não encontrei a fatura do {card} de {when}.";

    public static string NoTransactions(string label) => $"📊 Não encontrei gastos em {label}.";

    public static string NoReportData(string label) => $"📊 Não encontrei transações em {label} para o relatório.";

    public static string Invoice(CardInvoiceReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"💳 *FATURA {report.CardName.ToUpperInvariant()} - VENC. {Date(report.DueDate)}* 💳\n");
        sb.Append(CultureInfo.InvariantCulture, $"{StatusText(report.Status)} • fecha em {report.ClosingDate:dd/MM}\n\n");
        sb.Append(CultureInfo.InvariantCulture, $"💰 *Total de compras:* {PtBrMoney.Format(report.Purchases)}\n");
        if (report.Refunds != 0m)
        {
            sb.Append(CultureInfo.InvariantCulture, $"↩️ Estornos: {PtBrMoney.Format(report.Refunds)}\n");
        }

        sb.Append(CultureInfo.InvariantCulture, $"🧾 {report.Count} lançamento(s)\n");

        if (report.Categories.Count > 0)
        {
            sb.Append("\n📊 *POR CATEGORIA*\n");
            AppendRanking(sb, report.Categories.Select(c => (c.Name, c.Amount, (decimal?)null)).ToList(), report.Purchases, InvoiceCategoryLimit);
        }

        if (report.TopPurchases.Count > 0)
        {
            sb.Append("\n🔝 *MAIORES COMPRAS*\n");
            foreach (var line in report.TopPurchases)
            {
                sb.Append(CultureInfo.InvariantCulture, $"• {line.Date:dd/MM} {line.Description} - {PtBrMoney.Format(line.Amount)}\n");
            }
        }

        if (report.InstallmentPurchases > 0)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"\n🧮 *Parcelas a vencer:* {report.InstallmentPurchases} compra(s) parcelada(s); faltam {PtBrMoney.Format(report.InstallmentsRemaining)} nas próximas faturas\n");
        }

        return sb.ToString().TrimEnd('\n');
    }

    public static string MonthByCategory(MonthByCategoryReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var sb = new StringBuilder();
        var month = report.MonthStart;
        sb.Append(CultureInfo.InvariantCulture, $"📊 *GASTOS POR CATEGORIA - {Months[month.Month - 1].ToUpperInvariant()}/{month.Year}* 📊\n");
        if (report.Partial)
        {
            sb.Append(CultureInfo.InvariantCulture, $"_até {report.UntilDate:dd/MM}, contra o mesmo trecho do mês anterior_\n");
        }

        sb.Append(CultureInfo.InvariantCulture, $"\n💸 *Total:* {PtBrMoney.Format(report.Total)}");
        if (report.PreviousTotal > 0m)
        {
            sb.Append(CultureInfo.InvariantCulture, $" ({Variation(report.Total, report.PreviousTotal)} vs {PtBrMoney.Format(report.PreviousTotal)})");
        }

        sb.Append("\n\n");
        AppendRanking(
            sb, report.Categories.Select(c => (c.Name, c.Amount, (decimal?)c.Previous)).ToList(), report.Total, MonthCategoryLimit);
        return sb.ToString().TrimEnd('\n');
    }

    private static void AppendRanking(StringBuilder sb, List<(string Name, decimal Amount, decimal? Previous)> rows, decimal total, int limit)
    {
        var position = 1;
        foreach (var row in rows.Take(limit))
        {
            sb.Append(CultureInfo.InvariantCulture, $"{position}. {row.Name} - {PtBrMoney.Format(row.Amount)} ({Percent(row.Amount, total)})");
            if (row.Previous is { } previous)
            {
                sb.Append(CultureInfo.InvariantCulture, $" {Variation(row.Amount, previous)}");
            }

            sb.Append('\n');
            position++;
        }

        if (rows.Count > limit)
        {
            var rest = rows.Skip(limit).ToList();
            var restTotal = rest.Sum(r => r.Amount);
            sb.Append(CultureInfo.InvariantCulture, $"… outras {rest.Count} categoria(s) - {PtBrMoney.Format(restTotal)} ({Percent(restTotal, total)})\n");
        }
    }

    private static string Percent(decimal part, decimal total) =>
        total <= 0m ? "0%" : string.Create(CultureInfo.InvariantCulture, $"{Math.Round(part / total * 100m, MidpointRounding.AwayFromZero):0}%");

    /// <summary>"▲ 12%", "▼ 8%", "➖" ou "🆕" (não havia gasto antes).</summary>
    private static string Variation(decimal current, decimal previous)
    {
        if (previous <= 0m)
        {
            return "🆕";
        }

        var percent = Math.Round((current - previous) / previous * 100m, MidpointRounding.AwayFromZero);
        return percent switch
        {
            >= 1m => string.Create(CultureInfo.InvariantCulture, $"▲ {percent:0}%"),
            <= -1m => string.Create(CultureInfo.InvariantCulture, $"▼ {Math.Abs(percent):0}%"),
            _ => "➖",
        };
    }

    private static string StatusText(string status) => status switch
    {
        "Aberta" => "🟢 Aberta",
        "Fechada" => "🔒 Fechada",
        "Paga" => "✅ Paga",
        _ => status,
    };

    private static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
