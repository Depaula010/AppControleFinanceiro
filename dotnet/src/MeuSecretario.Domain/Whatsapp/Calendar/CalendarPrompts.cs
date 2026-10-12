using System.Text;
using System.Text.RegularExpressions;
using MeuSecretario.Domain.Automation;

namespace MeuSecretario.Domain.Whatsapp;

/// <summary>
/// Prompt da sugestão do melhor horário (<c>FreeTimeFinderService.suggest_best_slot_with_ai</c>). O modelo de texto é gerado do Flask
/// por <c>tests/Fixtures/generate_whatsapp_fixtures.py</c> (seção 012e) com marcadores no lugar das variáveis; a lista dos cinco
/// primeiros horários entra como o <c>repr</c> do Python (<c>[{'data': ...}]</c>). Os marcadores são trocados em uma só passada
/// (um marcador digitado pelo usuário não contamina o resto).
/// </summary>
public static partial class CalendarPrompts
{
    private static readonly Lazy<string> Template = new(Load);

    public static string Suggestion(IReadOnlyList<FreeSlot> slots, string context, string preferences)
    {
        var slotsText = SlotsRepr(slots);
        return Token().Replace(Template.Value, match => match.Groups[1].Value switch
        {
            "SLOTS" => slotsText,
            "CONTEXTO" => context,
            _ => preferences,
        });
    }

    /// <summary><c>str(list_of_dicts)</c> dos cinco primeiros horários.</summary>
    public static string SlotsRepr(IReadOnlyList<FreeSlot> slots) =>
        "[" + string.Join(
            ", ",
            slots.Take(5).Select(slot =>
                "{'data': " + PyJsonValue.Repr(slot.Date)
                + ", 'horario': " + PyJsonValue.Repr(slot.StartTime + "-" + slot.EndTime)
                + ", 'qualidade': " + PyJsonValue.Repr(slot.Quality)
                + ", 'motivo': " + PyJsonValue.Repr(slot.Reason) + "}")) + "]";

    private static string Load()
    {
        var assembly = typeof(CalendarPrompts).Assembly;
        using var stream = assembly.GetManifestResourceStream("Whatsapp.Prompts.free-time-suggestion.txt")
                           ?? throw new InvalidOperationException("Modelo de prompt ausente: free-time-suggestion");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    [GeneratedRegex("⟦(SLOTS|CONTEXTO|PREFS)⟧")]
    private static partial Regex Token();
}
