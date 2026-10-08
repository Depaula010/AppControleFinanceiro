using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MeuSecretario.Domain.Whatsapp;

/// <summary>
/// Prompts do Gemini da feature 012 (spec §8). Cada um é um modelo de texto <b>gerado</b> de <c>gemini_service.py</c>
/// (<c>Prompts/*.txt</c>, recurso embutido) com marcadores no lugar das variáveis; a renderização troca os marcadores
/// de uma só vez (uma mensagem do usuário que contenha um marcador não contamina o resto).
/// </summary>
public static partial class WhatsAppPrompts
{
    private static readonly ConcurrentDictionary<string, string> Templates = new(StringComparer.Ordinal);

    public static string Intent(string message) => Render("intent", message);

    public static string TransferDetails(string message, string accountsJson) => Render("transfer-details", message, accountsJson);

    public static string InvoicePaymentDetails(string message, string accountsJson) => Render("invoice-payment-details", message, accountsJson);

    public static string CategoryQuery(string message) => Render("category-query", message);

    public static string PeriodQuery(string message) => Render("period-query", message);

    public static string ChartType(string message) => Render("chart-type", message);

    public static string PaymentItems(string message) => Render("payment-items", message);

    public static string CalendarQuery(string message) => Render("calendar-query", message);

    public static string EventDeletion(string message) => Render("event-deletion", message);

    public static string TimeFilter(string message) => Render("time-filter", message);

    public static string NotificationConfig(string message) => Render("notification-config", message);

    public static string FreeTimeQuery(string message) => Render("free-time-query", message);

    public static string InvoiceQuery(string message, string accountsJson) => Render("invoice-query", message, accountsJson);

    public static string BalanceQuery(string message, string accountsJson) => Render("balance-query", message, accountsJson);

    public static string MonthlyReportConfig(string message) => Render("monthly-report-config", message);

    public static string LocationConfig(string message) => Render("location-config", message);

    public static string AddressConfig(string message) => Render("address-config", message);

    public static string AddressDeletion(string message) => Render("address-deletion", message);

    /// <summary>
    /// Criação de evento. O modelo do Flask não informa a data de hoje; o desvio 062 (aprovado) acrescenta a linha
    /// <see cref="EventTodayLine"/> logo depois da primeira linha do prompt.
    /// </summary>
    public static string EventCreation(string message, DateOnly today)
    {
        var rendered = Render("event-creation", message);
        var newline = rendered.IndexOf('\n', StringComparison.Ordinal);
        return rendered[..(newline + 1)] + EventTodayLine(today) + rendered[(newline + 1)..];
    }

    /// <summary>A linha que o desvio 062 insere (inclui o recuo de 4 espaços do prompt e a quebra final).</summary>
    public static string EventTodayLine(DateOnly today) =>
        string.Create(CultureInfo.InvariantCulture, $"    Hoje é {today:yyyy-MM-dd} ({WeekdayNames[(int)today.DayOfWeek]}).\n");

    private static readonly string[] WeekdayNames =
        ["domingo", "segunda-feira", "terça-feira", "quarta-feira", "quinta-feira", "sexta-feira", "sábado"];

    /// <summary>Texto longo versionado (menu de ajuda).</summary>
    public static string HelpMenuText() => Template("help-menu");

    private static string Render(string name, string message, string? accountsJson = null) =>
        Token().Replace(Template(name), match => match.Groups[1].Value == "MSG" ? message : accountsJson ?? string.Empty);

    private static string Template(string name) => Templates.GetOrAdd(name, Load);

    private static string Load(string name)
    {
        var assembly = typeof(WhatsAppPrompts).Assembly;
        using var stream = assembly.GetManifestResourceStream("Whatsapp.Prompts." + name + ".txt")
                           ?? throw new InvalidOperationException("Modelo de prompt ausente: " + name);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        // O arquivo pode ter sido normalizado para CRLF no checkout (Windows); o Python escreve sempre LF.
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    [GeneratedRegex("⟦(MSG|CONTAS)⟧")]
    private static partial Regex Token();
}
