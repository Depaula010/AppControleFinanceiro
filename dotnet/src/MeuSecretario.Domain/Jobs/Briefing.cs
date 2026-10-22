using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MeuSecretario.Domain.Weather;

namespace MeuSecretario.Domain.Jobs;

/// <summary>Evento do dia no formato do dicionário do Python (<c>summary</c>, <c>start</c>, <c>end</c>, ...).</summary>
public sealed record BriefingEvent(string Summary, string Start, string End, string Location, string Description, bool AllDay);

public sealed record BriefingWeather(string FullDescription, int Temperature, int FeelsLike, int? RainChance);

public sealed record BriefingExtraWeather(string City, string State, BriefingWeather Weather);

public sealed record BriefingGap(string Start, string End, int Minutes);

/// <summary><c>prepare_briefing_data</c> (RN-013-22).</summary>
public sealed record BriefingData(
    IReadOnlyList<BriefingEvent> Events,
    BriefingWeather? MainWeather,
    IReadOnlyList<BriefingExtraWeather> ExtraWeather,
    IReadOnlyList<BriefingGap> Gaps,
    int RemoteEvents,
    int InPersonEvents,
    FinancialAlerts Alerts,
    DateOnly Date)
{
    public int TotalEvents => Events.Count;
}

/// <summary>Regras de <c>daily_briefing_service.py</c> (RN-013-22). Funções puras.</summary>
public static class BriefingRules
{
    private static readonly string[] RemoteKeywords =
        ["meet", "zoom", "teams", "online", "remoto", "virtual", "video", "chamada", "call", "reunião online"];

    /// <summary>Sigla de estado: 2 caracteres, com letra maiúscula e nenhuma minúscula (<c>len == 2 and isupper()</c>).</summary>
    private static bool IsStateCode(string part) =>
        part.Length == 2 && part.Any(char.IsUpper) && !part.Any(char.IsLower);

    /// <summary>Pares (cidade, estado) únicos dos locais dos eventos, na ordem do primeiro aparecimento.</summary>
    public static IReadOnlyList<(string City, string State)> ExtractLocations(IEnumerable<BriefingEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var found = new List<(string City, string State)>();
        foreach (var evt in events)
        {
            var location = (evt.Location ?? string.Empty).Trim();
            if (location.Length == 0)
            {
                continue;
            }

            var parts = location.Split(',').Select(p => p.Trim()).ToList();
            string? state = null;
            for (var i = parts.Count - 1; i >= 0; i--)
            {
                if (IsStateCode(parts[i]))
                {
                    state = parts[i];
                    break;
                }
            }

            if (state is not null && parts.Count >= 2)
            {
                var index = parts.IndexOf(state);
                if (index > 0)
                {
                    var pair = (parts[index - 1], state);
                    if (!found.Contains(pair))
                    {
                        found.Add(pair);
                    }
                }
            }
        }

        return found;
    }

    /// <summary>Intervalos livres de 30 min ou mais entre eventos com horário consecutivos.</summary>
    public static IReadOnlyList<BriefingGap> CalculateGaps(IEnumerable<BriefingEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var timed = events.Where(e => !e.AllDay).ToList();
        var gaps = new List<BriefingGap>();
        if (timed.Count < 2)
        {
            return gaps;
        }

        for (var i = 0; i < timed.Count - 1; i++)
        {
            var endText = timed[i].End;
            var startText = timed[i + 1].Start;
            if (string.IsNullOrEmpty(endText) || string.IsNullOrEmpty(startText))
            {
                continue;
            }

            if (!PyIso.TryParse(endText, out var end) || !PyIso.TryParse(startText, out var start) || end.Aware != start.Aware)
            {
                continue;
            }

            var minutes = (start.Value - end.Value).TotalSeconds / 60;
            if (minutes >= 30)
            {
                gaps.Add(new BriefingGap(PyIso.Hhmm(end.Value), PyIso.Hhmm(start.Value), (int)minutes));
            }
        }

        return gaps;
    }

    /// <summary><c>detect_event_type</c>: <c>remoto</c>, <c>presencial</c> ou vazio.</summary>
    public static string DetectType(BriefingEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var all = $"{evt.Summary.ToLowerInvariant()} {evt.Description.ToLowerInvariant()} {evt.Location.ToLowerInvariant()}";
        if (RemoteKeywords.Any(k => all.Contains(k, StringComparison.Ordinal)))
        {
            return "remoto";
        }

        return evt.Location.Length > 0 ? "presencial" : string.Empty;
    }

    /// <summary>Mensagem sem eventos (<c>generate_briefing_message</c>, RN-013-23).</summary>
    public static string NoEventsMessage(BriefingData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var message = "☀️ Bom dia! Você não tem compromissos agendados para hoje! 🎉\n\n";
        if (data.MainWeather is { } weather)
        {
            message += WeatherFormatter.FormatForBriefing(weather.FullDescription, weather.Temperature, weather.FeelsLike, weather.RainChance);
        }

        var alerts = FinancialAlertFormatter.Format(data.Alerts, includeGreeting: false);
        if (!string.IsNullOrEmpty(alerts))
        {
            message += "\n\n" + alerts;
        }

        return message;
    }
}

/// <summary>Prompt e pós-processamento de <c>generate_daily_briefing</c> (<c>gemini_service.py</c>, RN-013-24).</summary>
public static partial class BriefingPrompt
{
    private static readonly string[] WeekDays =
        ["segunda-feira", "terça-feira", "quarta-feira", "quinta-feira", "sexta-feira", "sábado", "domingo"];

    private static readonly string[] PromptRemoteKeywords = ["meet", "zoom", "teams", "online", "remoto"];

    /// <summary>Partes que também alimentam a mensagem básica de fallback.</summary>
    public sealed record Parts(string Events, string Weather, string ExtraWeather, string Gaps, string Alerts);

    public static Parts BuildParts(BriefingData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var lines = new List<string>();
        var number = 1;
        foreach (var evt in data.Events)
        {
            string schedule;
            if (evt.AllDay)
            {
                schedule = "Dia inteiro";
            }
            else
            {
                schedule = TimeText(evt);
            }

            var location = evt.Location;
            var typeText = string.Empty;
            var all = $"{evt.Summary.ToLowerInvariant()} {evt.Description.ToLowerInvariant()} {location.ToLowerInvariant()}";
            if (PromptRemoteKeywords.Any(k => all.Contains(k, StringComparison.Ordinal)))
            {
                typeText = " [remoto]";
            }
            else if (location.Length > 0)
            {
                typeText = $" em {location}";
            }

            lines.Add($"{number.ToString(CultureInfo.InvariantCulture)}. {schedule} - {evt.Summary}{typeText}");
            number++;
        }

        var weatherText = string.Empty;
        if (data.MainWeather is { } main)
        {
            weatherText = $"Clima: {main.FullDescription}";
            var chance = main.RainChance ?? 0;
            if (chance >= 30)
            {
                weatherText += $" - Chance de chuva: {chance.ToString(CultureInfo.InvariantCulture)}%";
            }
        }

        var extra = string.Empty;
        if (data.ExtraWeather.Count > 0)
        {
            extra = "\nClimas em outras cidades (você tem eventos lá):\n" +
                    string.Join("\n", data.ExtraWeather.Select(e => $"{e.City}: {e.Weather.FullDescription}"));
        }

        var gapsText = string.Empty;
        if (data.Gaps.Count > 0)
        {
            var items = data.Gaps.Select(gap =>
            {
                var hours = gap.Minutes / 60;
                var minutes = gap.Minutes % 60;
                var duration = hours > 0
                    ? $"{hours.ToString(CultureInfo.InvariantCulture)}h" + (minutes > 0 ? $"{minutes.ToString(CultureInfo.InvariantCulture)}min" : string.Empty)
                    : $"{minutes.ToString(CultureInfo.InvariantCulture)} min";
                return $"• {gap.Start}-{gap.End} ({duration})";
            });
            gapsText = "\n\nHorários livres entre eventos:\n" + string.Join("\n", items);
        }

        return new Parts(string.Join("\n", lines), weatherText, extra, gapsText, AlertsText(data.Alerts));
    }

    public static string Build(BriefingData data)
    {
        var parts = BuildParts(data);
        var weekDay = WeekDays[((int)data.Date.DayOfWeek + 6) % 7];
        var date = data.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        var prompt = new StringBuilder();
        prompt.Append("Você é um assistente pessoal humanizado. Gere um resumo matinal da agenda do usuário.\n\n");
        prompt.Append(CultureInfo.InvariantCulture, $"DATA: {date} ({weekDay})\n\n");
        prompt.Append("EVENTOS DO DIA:\n");
        prompt.Append(parts.Events).Append("\n\n");
        prompt.Append(parts.Weather).Append('\n');
        prompt.Append(parts.ExtraWeather).Append('\n');
        prompt.Append(parts.Gaps).Append('\n');
        prompt.Append(parts.Alerts).Append("\n\n");
        prompt.Append("INSTRUÇÕES:\n");
        prompt.Append("1. Comece com uma saudação amigável (ex: \"☀️ Bom dia!\", \"🌅 Olá!\")\n");
        prompt.Append(CultureInfo.InvariantCulture, $"2. IMPORTANTE: Use o dia da semana CORRETO informado acima ({weekDay})\n");
        prompt.Append("3. Resuma os compromissos de forma clara e objetiva\n");
        prompt.Append("4. Use emojis apropriados (mas sem exagerar)\n");
        prompt.Append("5. Destaque informações importantes:\n");
        prompt.Append("   - Se há eventos remotos vs presenciais\n");
        prompt.Append("   - Horários livres úteis para trabalho focado ou pausa\n");
        prompt.Append("   - Clima (especialmente se for chover ou temperatura extrema)\n");
        prompt.Append("   - ALERTAS FINANCEIROS: contas e faturas que vencem hoje ou amanhã (use ⚠️ ou 💰)\n");
        prompt.Append("   - Dicas úteis (ex: \"saia cedo\", \"leve guarda-chuva\", \"tempo livre para almoço\")\n");
        prompt.Append("6. Seja conciso mas informativo (máximo 20 linhas)\n");
        prompt.Append("7. Mantenha tom profissional mas amigável\n");
        prompt.Append("8. NÃO invente informações que não estão nos dados fornecidos\n");
        prompt.Append("9. Se houver alertas financeiros, destaque-os claramente no resumo\n\n");
        prompt.Append("FORMATAÇÃO (IMPORTANTE):\n");
        prompt.Append("- Use *texto* para negrito (WhatsApp)\n");
        prompt.Append("- Para listas, use \"•\" (bullet) ou \"-\", NUNCA use \"*\" como marcador\n");
        prompt.Append("- Organize seções com títulos em negrito\n");
        prompt.Append("- EVITE usar dois asteriscos seguidos (** ou * *)\n\n");
        prompt.Append("Gere APENAS o texto da mensagem, sem introduções ou formatação extra.\n");
        return prompt.ToString();
    }

    /// <summary>Mensagem básica quando a IA falha.</summary>
    public static string Fallback(BriefingData data)
    {
        var parts = BuildParts(data);
        var message = "☀️ Bom dia! Sua agenda de hoje:\n\n" + parts.Events;
        if (parts.Weather.Length > 0)
        {
            message += $"\n\n{parts.Weather}";
        }

        if (parts.Gaps.Length > 0)
        {
            message += parts.Gaps;
        }

        return message;
    }

    /// <summary>Limpeza da resposta da IA (asteriscos de lista e tags HTML).</summary>
    public static string CleanResponse(string response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var message = response.Trim();
        message = ListMarker1().Replace(message, "   • ");
        message = ListMarker2().Replace(message, "   • ");
        message = Blockquote().Replace(message, string.Empty);
        message = AnyTag().Replace(message, string.Empty);
        return message.Trim();
    }

    [GeneratedRegex(@"^\*\s+\*", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ListMarker1();

    [GeneratedRegex(@"^\s*\*\s*\*", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ListMarker2();

    [GeneratedRegex(@"</?blockquote>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Blockquote();

    [GeneratedRegex(@"</?[a-z]+>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnyTag();

    private static string TimeText(BriefingEvent evt)
    {
        if (!PyIso.TryParse(evt.Start, out var start))
        {
            return "Horário não especificado";
        }

        var schedule = PyIso.Hhmm(start.Value);
        if (!string.IsNullOrEmpty(evt.End))
        {
            if (!PyIso.TryParse(evt.End, out var end) || end.Aware != start.Aware)
            {
                return "Horário não especificado";
            }

            var minutes = (int)((end.Value - start.Value).TotalSeconds / 60);
            schedule += $" ({minutes.ToString(CultureInfo.InvariantCulture)} min)";
        }

        return schedule;
    }

    private static string AlertsText(FinancialAlerts alerts)
    {
        if (!alerts.Any)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        var expensesToday = alerts.BillsToday.Where(b => b.Type == AlertBill.Expense).ToList();
        var incomesToday = alerts.BillsToday.Where(b => b.Type == AlertBill.Income).ToList();
        var expensesTomorrow = alerts.BillsTomorrow.Where(b => b.Type == AlertBill.Expense).ToList();
        var incomesTomorrow = alerts.BillsTomorrow.Where(b => b.Type == AlertBill.Income).ToList();

        var todayExpenses = expensesToday.Count > 0 || alerts.InvoicesToday.Count > 0;
        if (todayExpenses)
        {
            parts.Add("VENCE HOJE (Despesas):");
            AddLines(parts, expensesToday, alerts.InvoicesToday);
        }

        if (incomesToday.Count > 0)
        {
            if (todayExpenses)
            {
                parts.Add(string.Empty);
            }

            parts.Add("VENCE HOJE (Receitas):");
            AddLines(parts, incomesToday, []);
        }

        var tomorrowExpenses = expensesTomorrow.Count > 0 || alerts.InvoicesTomorrow.Count > 0;
        if (tomorrowExpenses)
        {
            if (todayExpenses || incomesToday.Count > 0)
            {
                parts.Add(string.Empty);
            }

            parts.Add("VENCE AMANHÃ (Despesas):");
            AddLines(parts, expensesTomorrow, alerts.InvoicesTomorrow);
        }

        if (incomesTomorrow.Count > 0)
        {
            if (todayExpenses || incomesToday.Count > 0 || tomorrowExpenses)
            {
                parts.Add(string.Empty);
            }

            parts.Add("VENCE AMANHÃ (Receitas):");
            AddLines(parts, incomesTomorrow, []);
        }

        return "\n\nALERTAS FINANCEIROS:\n" + string.Join("\n", parts);
    }

    private static void AddLines(List<string> parts, IEnumerable<AlertBill> bills, IEnumerable<AlertInvoice> invoices)
    {
        foreach (var bill in bills)
        {
            parts.Add($"• {bill.Description} - R$ {PyNumber.FixedComma(bill.Value)}");
        }

        foreach (var invoice in invoices)
        {
            parts.Add($"• Fatura {invoice.Card} - R$ {PyNumber.FixedComma(invoice.Value)}");
        }
    }
}
