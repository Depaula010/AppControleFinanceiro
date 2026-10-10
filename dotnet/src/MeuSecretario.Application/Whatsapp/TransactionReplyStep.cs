using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeuSecretario.Application.Automation;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Transactions;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>
/// Passo 6 do pipeline (<c>whatsapp_handler.py:291-428</c>, spec 012 §4 e §7.2): resposta do usuário a uma transação pendente
/// (<c>OK</c>, <c>TROCAR</c>, número, <c>CANCELAR</c>). Palavras exclusivas de evento (<c>sim</c>, <c>n</c>...) nunca chegam aqui.
/// </summary>
public sealed class TransactionReplyStep(
    IPendingTransactionState state,
    IAutomationLookups lookups,
    TransactionConfirmationSaver saver,
    IFeedbackService feedback,
    TransferConfirmation transfers) : IWhatsAppStep
{
    public int Order => StepOrder.TransactionReply;

    public async Task<string?> HandleAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        var upper = context.Message.Trim().ToUpperInvariant();
        if (MessageIntent.IsExclusiveEventWord(context.Lower) || !MessageIntent.LooksLikeTransactionReply(upper))
        {
            return null;
        }

        var lastId = await state.GetLastPendingIdAsync(context.Number, cancellationToken);
        if (string.IsNullOrEmpty(lastId))
        {
            // Palavra exata de confirmação ou cancelamento sem nada pendente: aviso claro (evita virar outra intenção).
            if (upper is "OK" or "CONFIRMAR" or "SIM" or "CONFIRMA")
            {
                return WhatsAppTexts.NothingPendingToConfirm;
            }

            return upper is "CANCELAR" or "CANCELA" or "NÃO" or "NAO" ? WhatsAppTexts.NothingPendingToCancel : null;
        }

        var pending = await state.GetAsync(context.Number, lastId, cancellationToken);
        if (TransferConfirmation.IsTransfer(pending) && upper is "TROCAR" or "MUDAR" or "ALTERAR")
        {
            // Desvio 151: a transferência não tem categoria para trocar.
            return TransferMessages.NoCategory;
        }

        var outcome = ConfirmationFlow.Process(context.Message, lastId, pending);
        switch (outcome.Effect)
        {
            case ConfirmationEffect.DeleteBoth:
                await state.DeleteBothAsync(context.Number, lastId, cancellationToken);
                break;
            case ConfirmationEffect.SavePending:
                await state.SaveAsync(context.Number, lastId, outcome.Data!, cancellationToken);
                break;
        }

        switch (outcome.Status)
        {
            case ConfirmationStatus.Saved:
            {
                if (TransferConfirmation.IsTransfer(outcome.Data))
                {
                    var done = await transfers.SaveAsync(outcome.Data!, cancellationToken);
                    await state.DeleteLastPendingAsync(context.Number, cancellationToken);
                    return done;
                }

                var transactionId = await saver.SaveAsync(outcome.Data!, cancellationToken);
                var reply = await feedback.GenerateAsync(transactionId, cancellationToken);
                await state.DeleteLastPendingAsync(context.Number, cancellationToken);
                return reply;
            }

            case ConfirmationStatus.AwaitingCategory:
            {
                var data = outcome.Data!;
                var type = Text(data, "tipo_transacao");
                var categories = await lookups.ListCategoriesAsync(context.User.Id, income: type != TransactionIntentRules.Expense, cancellationToken);
                var (text, map) = CategorySelection.Format(categories);
                var categoryMap = new JsonObject();
                foreach (var (key, id) in map)
                {
                    categoryMap[key] = id;
                }

                data[ConfirmationFlow.CategoryMapKey] = categoryMap;
                await state.SaveAsync(context.Number, lastId, data, cancellationToken);
                return text;
            }

            case ConfirmationStatus.Cancelled:
                await state.DeleteLastPendingAsync(context.Number, cancellationToken);
                return outcome.Message;

            default:
                return outcome.Message;
        }
    }

    private static string Text(JsonObject data, string key) =>
        data[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : throw new KeyNotFoundException(key);
}

/// <summary>
/// Grava a transação confirmada (<c>whatsapp_handler.py:324-386</c>) pelas portas da 004: <c>Renda</c> vira receita e qualquer outro
/// tipo, despesa; valor sem sinal; descrição <c>descricao_final</c> se preenchida, senão <c>descricao</c> (desvio 150: o Flask
/// gravava <c>NULL</c> quando a chave existia vazia e o INSERT falhava); <c>num_parcelas &gt; 1</c> usa a compra parcelada.
/// </summary>
public sealed class TransactionConfirmationSaver(DirectPoster poster, ITransactionPosting posting)
{
    public async Task<int> SaveAsync(JsonObject data, CancellationToken cancellationToken)
    {
        var userId = Int(data, "usuario_id");
        var accountId = Int(data, "conta_id");
        var categoryId = Int(data, "categoria_id");
        var valueDb = Number(data, "valor_db");
        var kind = Text(data, "tipo_transacao") == TransactionIntentRules.Income ? TransactionKind.Income : TransactionKind.Expense;
        var date = ParseDate(Text(data, "data_transacao"));

        var description = ConfirmationDescription.Pick(NullableText(data, "descricao_final"), NullableText(data, "descricao"));

        var installments = data["num_parcelas"] is { } installmentNode && installmentNode.GetValueKind() == JsonValueKind.Number
                           && Number(data, "num_parcelas") is var count && count > 1
            ? (int)count
            : (int?)null;
        if (installments is null)
        {
            var posted = await poster.PostAsync(userId, accountId, categoryId, kind, Math.Abs(valueDb), description, date, cancellationToken);
            return posted.TransactionId;
        }

        if (description is null)
        {
            throw new AutomationFailureException("descrição ausente");
        }

        if (!MoneyDecimal.TryFromDouble(Math.Abs(valueDb), out var amount))
        {
            throw new AutomationFailureException("valor fora do numeric(15,2)");
        }

        var result = await posting.PostInstallmentPurchaseAsync(
            new PostInstallmentPurchase(userId, accountId, categoryId, amount, installments.Value, description, date), cancellationToken);
        return result is PostingResult<PostedInstallmentPurchase>.Ok ok
            ? ok.Value.TransactionId
            : throw new AutomationFailureException("lançamento parcelado recusado: " + ((PostingResult<PostedInstallmentPurchase>.Failed)result).Error);
    }

    private static int Int(JsonObject data, string key) => (int)Number(data, key);

    private static double Number(JsonObject data, string key) =>
        data[key] is { } node && node.GetValueKind() == JsonValueKind.Number
            ? double.Parse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture)
            : throw new KeyNotFoundException(key);

    private static string Text(JsonObject data, string key) =>
        data[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : throw new KeyNotFoundException(key);

    private static string? NullableText(JsonObject data, string key) =>
        data[key] switch
        {
            null => null,
            JsonValue value when value.TryGetValue<string>(out var text) => text,
            _ => throw new InvalidOperationException("Descrição não é texto."),
        };

    private static DateOnly ParseDate(string text) =>
        PyIsoDate.TryParse(JsonSerializer.SerializeToElement(text), out var date)
            ? date
            : throw new FormatException("Data inválida.");
}

/// <summary>Desvio 150: <c>descricao_final or descricao</c> (descrição vazia ou nula cai na bruta da mensagem).</summary>
public static class ConfirmationDescription
{
    public static string? Pick(string? final, string? raw) =>
        !string.IsNullOrEmpty(final) ? final : (string.IsNullOrEmpty(raw) ? null : raw);
}
