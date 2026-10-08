using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Automation;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Llm;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>
/// <c>Transferência</c> (<c>transaction_intents.py:312-442</c>, spec 012 §6.a). O Flask lê <c>c["id"]</c> de uma <c>Row</c> do
/// SQLAlchemy 2 e quebra na primeira conta (<c>TypeError</c>): para todo usuário com ao menos uma conta a resposta é
/// <see cref="WhatsAppTexts.IntentFailed"/> e o Gemini nem é chamado. Só quem não tem conta segue (desvio 151 proposto:
/// a transferência de verdade e as mensagens de saldo não são portadas).
/// </summary>
public sealed class TransferIntent(ILlmClient llm, IAutomationLookups lookups, IWhatsAppFinanceReader reader) : IWhatsAppIntent
{
    public string Name => "Transferência";

    public bool Swallows => true;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var userId = context.User.Id;
        var accounts = await lookups.ListActiveAccountsAsync(userId, cancellationToken);
        if (accounts.Count > 0)
        {
            throw new InvalidOperationException("'Row' não é subscrevível por texto (TypeError no Flask).");
        }

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

        if (PyJsonValue.ToFloat(value.Value) is FloatConversion.Ok { Value: <= 0 })
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
            return "❌ Uma ou ambas as contas não foram encontradas.";
        }

        // Sem contas ativas não há como achar nenhuma; o ramo do Flask depois daqui nunca roda (a transferência quebra antes).
        throw new InvalidOperationException("Transferência por WhatsApp não é alcançável (desvio 151).");
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
