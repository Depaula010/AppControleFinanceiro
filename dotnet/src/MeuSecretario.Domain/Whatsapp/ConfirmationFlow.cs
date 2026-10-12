using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using MeuSecretario.Domain.Automation;

namespace MeuSecretario.Domain.Whatsapp;

public enum ConfirmationStatus
{
    Saved,
    AwaitingCategory,
    Cancelled,
    Error,
}

/// <summary>O que o chamador precisa fazer no Redis depois de decidir (o fluxo em si não faz I/O).</summary>
public enum ConfirmationEffect
{
    None,

    /// <summary><c>delete_pending_transaction</c>: apaga <c>pending_tx</c> e <c>last_pending</c>.</summary>
    DeleteBoth,

    /// <summary>Regrava <c>pending_tx</c> com TTL de 300 s.</summary>
    SavePending,
}

public sealed record ConfirmationOutcome(ConfirmationStatus Status, string? Message, JsonObject? Data, ConfirmationEffect Effect);

/// <summary>
/// Resposta do usuário a uma transação pendente (<c>TransactionConfirmationService.process_confirmation_response</c>,
/// <c>transaction_confirmation_service.py:224-296</c>, spec 012 §7.2). Pura: recebe a pendência lida do Redis e devolve
/// o estado e o efeito esperado.
/// </summary>
public static class ConfirmationFlow
{
    public const string NoPending = "Não encontrei nenhuma transação pendente para você. Ela pode ter expirado (5 min).";
    public const string Expired = "⏱️ Esta transação expirou ou não existe mais. Registre novamente.";
    public const string InvalidNumber = "Número inválido. Escolha um número da lista ou digite CANCELAR.";
    public const string NotANumber = "Digite um número válido da lista ou CANCELAR.";
    public const string Cancelled = "❌ Transação cancelada.";
    public const string UnknownCommand = "Comando não reconhecido. Digite: CONFIRMAR, TROCAR ou CANCELAR.";

    public const string AwaitingKey = "awaiting_category_selection";
    public const string CategoryMapKey = "categoria_map";

    public static ConfirmationOutcome Process(string userMessage, string? transactionId, JsonObject? pending)
    {
        var clean = userMessage.Trim().ToUpperInvariant();

        if (string.IsNullOrEmpty(transactionId))
        {
            return Error(NoPending);
        }

        if (pending is null)
        {
            return Error(Expired);
        }

        if (IsTrue(pending[AwaitingKey]))
        {
            return ProcessCategoryChoice(clean, pending);
        }

        if (clean is "CONFIRMAR" or "OK" or "SIM" or "CONFIRMA")
        {
            return new ConfirmationOutcome(ConfirmationStatus.Saved, null, pending, ConfirmationEffect.DeleteBoth);
        }

        if (clean is "TROCAR" or "MUDAR" or "ALTERAR")
        {
            pending[AwaitingKey] = true;
            return new ConfirmationOutcome(ConfirmationStatus.AwaitingCategory, null, pending, ConfirmationEffect.SavePending);
        }

        if (clean is "CANCELAR" or "NÃO" or "NAO" or "CANCELA")
        {
            return CancelledOutcome();
        }

        return Error(UnknownCommand);
    }

    private static ConfirmationOutcome ProcessCategoryChoice(string clean, JsonObject pending)
    {
        if (!PyInt.TryParse(clean, 0, out var choice) || clean.Length == 0)
        {
            return clean == "CANCELAR" ? CancelledOutcome() : Error(NotANumber);
        }

        var key = choice.ToString(CultureInfo.InvariantCulture);
        if (pending[CategoryMapKey] is JsonObject map && map[key] is { } categoryId)
        {
            pending["categoria_id"] = categoryId.DeepClone();
            pending[AwaitingKey] = false;
            return new ConfirmationOutcome(ConfirmationStatus.Saved, null, pending, ConfirmationEffect.DeleteBoth);
        }

        return Error(InvalidNumber);
    }

    private static ConfirmationOutcome CancelledOutcome() =>
        new(ConfirmationStatus.Cancelled, Cancelled, null, ConfirmationEffect.DeleteBoth);

    private static ConfirmationOutcome Error(string message) =>
        new(ConfirmationStatus.Error, message, null, ConfirmationEffect.None);

    private static bool IsTrue(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
}

/// <summary><c>format_category_selection_message</c> (<c>transaction_confirmation_service.py:167-197</c>).</summary>
public static class CategorySelection
{
    public static (string Text, IReadOnlyDictionary<string, int> Map) Format(IReadOnlyList<CategoryOption> categories)
    {
        var groups = new List<(string Macro, List<CategoryOption> Subs)>();
        foreach (var category in categories)
        {
            var existing = groups.FindIndex(g => g.Macro == category.MacroName);
            if (existing < 0)
            {
                groups.Add((category.MacroName, [category]));
            }
            else
            {
                groups[existing].Subs.Add(category);
            }
        }

        var sb = new StringBuilder("📂 *ESCOLHA A CATEGORIA CORRETA:*\n\n");
        var map = new Dictionary<string, int>();
        var index = 1;
        foreach (var (macro, subs) in groups)
        {
            sb.Append(CultureInfo.InvariantCulture, $"*{macro}:*\n");
            foreach (var sub in subs)
            {
                sb.Append(CultureInfo.InvariantCulture, $"  {index}. {sub.SubName}\n");
                map[index.ToString(CultureInfo.InvariantCulture)] = sub.Id;
                index++;
            }

            sb.Append('\n');
        }

        sb.Append("━━━━━━━━━━━━━━━━━━━━\n");
        sb.Append("Digite o *número* da categoria desejada\n");
        sb.Append("ou *CANCELAR* para descartar.");
        return (sb.ToString(), map);
    }
}
