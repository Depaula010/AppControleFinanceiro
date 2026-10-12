using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Automation;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Llm;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>
/// <c>Transferência</c> (<c>transaction_intents.py:312-442</c>, spec 012 §6.a). Desvio 151 (implementado): o Flask lê <c>c["id"]</c> de
/// uma <c>Row</c> e quebra para quem tem conta; aqui a transferência funciona: contas ativas por nome, mesma conta, saldo suficiente,
/// pendência no Redis (<c>tipo_transacao</c> <c>Transferência</c>) e, no <c>OK</c>, <see cref="TransferConfirmation"/>.
/// </summary>
public sealed class TransferIntent(
    ILlmClient llm,
    IAutomationLookups lookups,
    IWhatsAppFinanceReader reader,
    IWhatsAppQueryReader queries,
    IPendingTransactionStore pending,
    IClock clock) : IWhatsAppIntent
{
    public string Name => "Transferência";

    public bool Swallows => true;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var userId = context.User.Id;
        var accounts = await lookups.ListActiveAccountsAsync(userId, cancellationToken);

        var raw = await llm.GenerateTextAsync(
            new LlmRequest(WhatsAppPrompts.TransferDetails(context.Message, AccountsJson.Dump(accounts, withId: true)), userId),
            cancellationToken);
        var root = LlmJson.Parse(raw);
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Resposta de extração não é um objeto.");
        }

        var value = Property(root, "valor_decimal");
        var origin = Property(root, "conta_origem");
        var destination = Property(root, "conta_destino");

        if (!PyJsonValue.IsTruthy(value))
        {
            return "❌ Não consegui identificar o valor da transferência.";
        }

        if (value!.Value.ValueKind != JsonValueKind.Number && value.Value.ValueKind != JsonValueKind.True)
        {
            throw new InvalidOperationException("Valor da transferência não é numérico.");
        }

        var amount = PyJsonValue.ToFloat(value.Value) is FloatConversion.Ok ok
            ? ok.Value
            : throw new InvalidOperationException("Valor da transferência não é numérico.");
        if (amount <= 0)
        {
            return "❌ Não consegui identificar o valor da transferência.";
        }

        if (!PyJsonValue.IsTruthy(origin))
        {
            return "❌ Não consegui identificar a conta de origem. Especifique de qual conta transferir.";
        }

        if (!PyJsonValue.IsTruthy(destination))
        {
            return "❌ Não consegui identificar a conta de destino. Especifique para qual conta transferir.";
        }

        var originId = await reader.FindAccountIdByNameAsync(userId, TextOf(origin!.Value), fallback: true, type: null, cancellationToken);
        var destinationId = await reader.FindAccountIdByNameAsync(userId, TextOf(destination!.Value), fallback: true, type: null, cancellationToken);
        if (originId is null || destinationId is null)
        {
            return TransferMessages.NotFound;
        }

        if (originId == destinationId)
        {
            return TransferMessages.SameAccount;
        }

        var balances = await queries.GetBalancesAsync(userId, originId, cancellationToken);
        var balance = balances.Count > 0 ? balances[0].Balance : 0;
        if (balance < amount)
        {
            return TransferMessages.InsufficientBalance(balance, amount);
        }

        if (!MoneyDecimal.TryFromDouble(amount, out _))
        {
            throw new AutomationFailureException("valor fora do numeric(15,2)");
        }

        if (string.IsNullOrEmpty(context.Number))
        {
            return "❌ Erro interno: número WhatsApp não disponível";
        }

        var description = Property(root, "descricao") is { ValueKind: JsonValueKind.String } text && text.GetString() is { Length: > 0 } given
            ? given
            : TransferMessages.DefaultDescription;
        var fromName = accounts.FirstOrDefault(a => a.Id == originId)?.Name ?? "Desconhecida";
        var toName = accounts.FirstOrDefault(a => a.Id == destinationId)?.Name ?? "Desconhecida";

        var data = new Dictionary<string, object?>
        {
            ["usuario_id"] = userId,
            ["tipo_transacao"] = TransferMessages.Type,
            ["conta_id"] = originId.Value,
            ["conta_destino_id"] = destinationId.Value,
            ["conta_nome"] = fromName,
            ["conta_destino_nome"] = toName,
            ["descricao"] = description,
            ["valor_db"] = amount,
            ["valor_original"] = amount,
            ["data_transacao"] = clock.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["origem"] = "whatsapp",
        };
        var transactionId = await pending.CreateAsync(context.Number, data, cancellationToken);
        return transactionId is null
            ? TransactionIntentRules.PendingFailed
            : TransferMessages.Confirmation(description, amount, fromName, toName);
    }

    private static JsonElement? Property(JsonElement root, string name) => root.TryGetProperty(name, out var value) ? value : null;

    private static string TextOf(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString()! : throw new InvalidOperationException("Nome de conta não é texto.");
}

/// <summary>
/// <c>Pagamento Fatura</c> (<c>whatsapp_handler.py:1071-1112</c>, spec 012 §6.a). Inline: exceções sobem (cota do Gemini ⇒ 429).
/// <c>NotACreditCard</c>/<c>SameAccount</c> (desvios 071/004) respondem o mesmo "Não encontrei as contas mencionadas".
/// </summary>
public sealed class InvoicePaymentIntent(
    ILlmClient llm,
    IAutomationLookups lookups,
    IWhatsAppFinanceReader reader,
    ITransactionPosting posting,
    IClock clock) : IWhatsAppIntent
{
    private const string Incomplete =
        "🤔 Não consegui identificar todos os dados do pagamento.\n\n" +
        "Por favor, informe:\n" +
        "• Valor pago\n" +
        "• Conta de origem\n" +
        "• Cartão que foi pago\n\n" +
        "Exemplo: *Paguei 500 reais da fatura do Nubank com o Inter*";

    public string Name => "Pagamento Fatura";

    public bool Swallows => false;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var userId = context.User.Id;
        var accounts = await lookups.ListActiveAccountsAsync(userId, cancellationToken);

        var raw = await llm.GenerateTextAsync(
            new LlmRequest(WhatsAppPrompts.InvoicePaymentDetails(context.Message, AccountsJson.Dump(accounts, withId: false)), userId),
            cancellationToken);
        var root = LlmJson.Parse(raw);
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Resposta de extração não é um objeto.");
        }

        var value = Property(root, "valor_decimal");
        var origin = Property(root, "conta_origem");
        var card = Property(root, "conta_cartao");
        if (!PyJsonValue.IsTruthy(value) || !PyJsonValue.IsTruthy(origin) || !PyJsonValue.IsTruthy(card))
        {
            return Incomplete;
        }

        var amount = PyJsonValue.ToFloat(value!.Value) is FloatConversion.Ok ok
            ? ok.Value
            : throw new InvalidOperationException("Valor do pagamento não é numérico.");

        var originName = PyJsonValue.Str(origin!.Value);
        var cardName = PyJsonValue.Str(card!.Value);
        var originId = await FindAsync(userId, origin.Value, cancellationToken);
        var cardId = await FindAsync(userId, card.Value, cancellationToken);
        var notFound = $"❌ Não encontrei as contas mencionadas ({originName} → {cardName}).";
        if (originId is null || cardId is null)
        {
            return notFound;
        }

        if (!MoneyDecimal.TryFromDouble(amount, out var decimalAmount))
        {
            throw new AutomationFailureException("valor fora do numeric(15,2)");
        }

        var result = await posting.PostInvoicePaymentAsync(
            new PostInvoicePayment(userId, originId.Value, cardId.Value, decimalAmount, clock.Today), cancellationToken);
        switch (result)
        {
            case PostingResult<PostedInvoicePayment>.Ok posted:
                return $"✅ Pagamento da fatura '{posted.Value.CardName}' ({MoneyText.Brl(amount)}) registrado com sucesso!";
            case PostingResult<PostedInvoicePayment>.Failed
                { Error: PostingError.NotACreditCard or PostingError.SameAccount or PostingError.AccountNotFound or PostingError.DestinationAccountNotFound }:
                return notFound;
            case PostingResult<PostedInvoicePayment>.Failed failed:
                throw new AutomationFailureException("pagamento recusado: " + failed.Error);
            default:
                throw new InvalidOperationException("Resultado do pagamento desconhecido.");
        }
    }

    private async Task<int?> FindAsync(int userId, JsonElement name, CancellationToken cancellationToken) =>
        name.ValueKind == JsonValueKind.String
            ? await reader.FindAccountIdByNameAsync(userId, name.GetString()!, fallback: false, type: null, cancellationToken)
            : throw new InvalidOperationException("Nome de conta não é texto.");

    private static JsonElement? Property(JsonElement root, string name) => root.TryGetProperty(name, out var value) ? value : null;
}

/// <summary>
/// <c>OK</c> de uma pendência do tipo <c>Transferência</c> (desvio 151): grava o par pela porta da 004 e responde o resumo.
/// </summary>
public sealed class TransferConfirmation(ITransactionPosting posting)
{
    public static bool IsTransfer(System.Text.Json.Nodes.JsonObject? data) =>
        data?["tipo_transacao"] is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<string>(out var type) && type == TransferMessages.Type;

    public async Task<string> SaveAsync(System.Text.Json.Nodes.JsonObject data, CancellationToken cancellationToken)
    {
        var userId = (int)Number(data, "usuario_id");
        var from = (int)Number(data, "conta_id");
        var to = (int)Number(data, "conta_destino_id");
        var amount = Number(data, "valor_db");
        var date = PyIsoDate.TryParse(JsonSerializer.SerializeToElement(data["data_transacao"]?.GetValue<string>()), out var parsed)
            ? parsed
            : throw new FormatException("Data inválida.");
        if (!MoneyDecimal.TryFromDouble(amount, out var decimalAmount))
        {
            throw new AutomationFailureException("valor fora do numeric(15,2)");
        }

        var result = await posting.PostTransferAsync(new PostTransfer(userId, from, to, decimalAmount, date, "whatsapp"), cancellationToken);
        return result switch
        {
            PostingResult<PostedTransfer>.Ok ok => TransferMessages.Done(amount, ok.Value.FromAccountName, ok.Value.ToAccountName),
            PostingResult<PostedTransfer>.Failed { Error: PostingError.SameAccount } => TransferMessages.SameAccount,
            PostingResult<PostedTransfer>.Failed { Error: PostingError.AccountNotFound or PostingError.DestinationAccountNotFound } => TransferMessages.NotFound,
            PostingResult<PostedTransfer>.Failed failed => throw new AutomationFailureException("transferência recusada: " + failed.Error),
            _ => throw new InvalidOperationException("Resultado da transferência desconhecido."),
        };
    }

    private static double Number(System.Text.Json.Nodes.JsonObject data, string key) =>
        data[key] is { } node && node.GetValueKind() == JsonValueKind.Number
            ? double.Parse(node.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture)
            : throw new KeyNotFoundException(key);
}
