using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MeuSecretario.Domain.Whatsapp;

public enum ReportKind
{
    /// <summary>"fatura do inter por categoria": a fatura de um cartão, com gasto por categoria, maiores compras e parcelas a vencer.</summary>
    CardInvoice,

    /// <summary>"gastos do mês por categoria": ranking de categorias do mês, comparado ao anterior.</summary>
    MonthByCategory,

    /// <summary>"me manda o relatório do mês": o relatório mensal (texto e gráfico) na hora.</summary>
    MonthlyReport,
}

public enum ReportPeriod
{
    /// <summary>Nada dito: a fatura aberta, o mês atual.</summary>
    Default,

    /// <summary>"passada", "anterior", "mês passado".</summary>
    Previous,

    /// <summary>Um mês citado pelo nome ("setembro"); <see cref="ReportRequest.Year"/> e <see cref="ReportRequest.Month"/> valem.</summary>
    Specific,
}

/// <param name="Text">A mensagem sem acento e em minúsculas (para achar o nome de um cartão).</param>
public sealed record ReportRequest(ReportKind Kind, ReportPeriod Period, int Year, int Month, string Text)
{
    /// <summary>Primeiro dia do mês pedido (para fatura, o mês do vencimento).</summary>
    public DateOnly MonthStart(DateOnly today) => Period switch
    {
        ReportPeriod.Previous => new DateOnly(today.Year, today.Month, 1).AddMonths(-1),
        ReportPeriod.Specific => new DateOnly(Year, Month, 1),
        _ => new DateOnly(today.Year, today.Month, 1),
    };
}

/// <summary>
/// Pedidos de relatório (desvio 256): comandos reconhecidos por palavras-chave antes do Gemini, sem chamada ao modelo. O prompt de intenção
/// do Flask não muda (paridade); frases como "qual valor da fatura?", "configurar relatório mensal" e "paguei 500 da fatura" ficam fora.
/// </summary>
public static partial class ReportCommandParser
{
    private static readonly string[] MonthNames =
    [
        "janeiro", "fevereiro", "marco", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro",
    ];

    /// <summary>Quem pede para fazer algo (configurar, pagar, registrar) não está pedindo um relatório.</summary>
    [GeneratedRegex(@"\b(configur\w*|agendar|ativar|desativar|cancel\w*|paguei|pagar|pagamento|transfer\w*|comprei|recebi|registr\w*)\b")]
    private static partial Regex Action();

    [GeneratedRegex(@"\bfatura\b")]
    private static partial Regex Invoice();

    [GeneratedRegex(@"\b(categorias?|detalhad\w*|detalhes?|resumo)\b")]
    private static partial Regex InvoiceDetail();

    [GeneratedRegex(@"\bcategorias?\b")]
    private static partial Regex Category();

    [GeneratedRegex(@"\b(gast\w*|despes\w*|quanto)\b|onde .*gastei")]
    private static partial Regex Spending();

    [GeneratedRegex(@"\brelatorios?\b")]
    private static partial Regex Report();

    /// <summary>
    /// Relatório com cara de configuração, consulta ou análise ("como está meu relatório?", "receber relatório todo dia 1", "relatório
    /// financeiro"): é do Gemini (Configurar Relatório Mensal, Análise Inteligente), não do envio na hora.
    /// </summary>
    [GeneratedRegex(@"\b(todo|toda|todos|receber|financeiro|horario|status|configurad\w*|consult\w*|como esta)\b|\bdia \d|\d ?h\b")]
    private static partial Regex ReportSettingsHint();

    /// <summary>"relatório" sozinho, "relatório do mês", ou um verbo de envio ("me manda o relatório", "gera o relatório").</summary>
    [GeneratedRegex(@"^relatorio$|\brelatorio (do |de |da )?mes\b|\b(manda\w*|mande|envi\w+|gera\w*|gere|mostr\w+)\b.*\brelatorio\b")]
    private static partial Regex ReportNow();

    [GeneratedRegex(@"\b(passad[oa]|anterior|ultim[oa])\b")]
    private static partial Regex Previous();

    public static ReportRequest? Parse(string message, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(message);
        var text = Normalize(message);
        if (text.Length == 0 || Action().IsMatch(text))
        {
            return null;
        }

        if (Invoice().IsMatch(text) && (InvoiceDetail().IsMatch(text) || Report().IsMatch(text)))
        {
            return Build(ReportKind.CardInvoice, text, today, futureMonths: 2);
        }

        if (Category().IsMatch(text) && Spending().IsMatch(text) && !Invoice().IsMatch(text))
        {
            return Build(ReportKind.MonthByCategory, text, today, futureMonths: 0);
        }

        // "onde gastei mais" sem a palavra categoria também é o ranking.
        if (Spending().IsMatch(text) && text.Contains("onde", StringComparison.Ordinal) && text.Contains("mais", StringComparison.Ordinal))
        {
            return Build(ReportKind.MonthByCategory, text, today, futureMonths: 0);
        }

        if (Report().IsMatch(text) && !ReportSettingsHint().IsMatch(text) && (ReportNow().IsMatch(text) || NamesAMonth(text)))
        {
            return Build(ReportKind.MonthlyReport, text, today, futureMonths: 0);
        }

        return null;
    }

    public static string MonthName(int month) => MonthNames[month - 1];

    /// <summary>"relatório de agosto", "relatório do mês de agosto".</summary>
    private static bool NamesAMonth(string text) =>
        Regex.IsMatch(text, $@"\brelatorio (do |de |da )?(mes de )?({string.Join('|', MonthNames)})\b");

    /// <summary>Texto sem acento, em minúsculas, espaços únicos.</summary>
    public static string Normalize(string message)
    {
        var sb = new StringBuilder(message.Length);
        foreach (var c in message.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return Regex.Replace(sb.ToString(), @"\s+", " ");
    }

    private static ReportRequest Build(ReportKind kind, string text, DateOnly today, int futureMonths)
    {
        var named = Array.FindIndex(MonthNames, name => Regex.IsMatch(text, $@"\b{name}\b"));
        if (named >= 0)
        {
            var month = named + 1;
            return new ReportRequest(kind, ReportPeriod.Specific, NearestYear(month, today, futureMonths), month, text);
        }

        return new ReportRequest(kind, Previous().IsMatch(text) ? ReportPeriod.Previous : ReportPeriod.Default, 0, 0, text);
    }

    /// <summary>
    /// O ano do mês citado: o mais recente que não passa de <paramref name="futureMonths"/> meses à frente de hoje (gasto não existe no
    /// futuro; a fatura que vence no mês que vem existe). "dezembro" em outubro é dezembro do ano passado.
    /// </summary>
    private static int NearestYear(int month, DateOnly today, int futureMonths)
    {
        var limit = new DateOnly(today.Year, today.Month, 1).AddMonths(futureMonths);
        var candidate = new DateOnly(today.Year, month, 1);
        return candidate <= limit ? today.Year : today.Year - 1;
    }
}
