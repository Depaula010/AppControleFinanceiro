using MeuSecretario.Domain.Automation;

namespace MeuSecretario.Domain.Whatsapp;

/// <summary>
/// Textos da transferência por WhatsApp (desvio 151). As mensagens de erro e a de confirmação vêm de
/// <c>transaction_intents.py</c> (código que o Flask nunca alcançava); a de sucesso e a de "trocar" são novas.
/// </summary>
public static class TransferMessages
{
    public const string Type = "Transferência";
    public const string DefaultDescription = "Transferência";
    public const string SameAccount = "❌ As contas de origem e destino devem ser diferentes.";
    public const string NotFound = "❌ Uma ou ambas as contas não foram encontradas.";
    public const string NoCategory = "Transferência não tem categoria. Digite: CONFIRMAR ou CANCELAR.";

    public static string InsufficientBalance(double balance, double requested) =>
        $"❌ Saldo insuficiente na conta de origem.\n\n💰 Saldo disponível: {MoneyText.Brl(balance)}\n💸 Valor solicitado: {MoneyText.Brl(requested)}";

    public static string Confirmation(string description, double amount, string fromName, string toName) =>
        "🔄 *Transferência a confirmar:*\n\n" +
        $"📝 Descrição: {description}\n" +
        $"💵 Valor: {MoneyText.Brl(amount)}\n\n" +
        $"📤 De: {fromName}\n" +
        $"📥 Para: {toName}\n\n" +
        "Responda:\n" +
        "• *confirmar* - para realizar transferência\n" +
        "• *cancelar* - para descartar";

    public static string Done(double amount, string fromName, string toName) =>
        $"✅ *Transferência realizada!*\n\n💵 Valor: {MoneyText.Brl(amount)}\n📤 De: {fromName}\n📥 Para: {toName}";
}
