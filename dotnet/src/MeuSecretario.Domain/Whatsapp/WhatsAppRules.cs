using System.Security.Cryptography;
using System.Text;

namespace MeuSecretario.Domain.Whatsapp;

/// <summary>
/// <c>X-Webhook-Signature</c>: HMAC-SHA256 em hexadecimal minúsculo do corpo bruto (<c>verify_hmac_signature</c>,
/// <c>app/shared/security/hmac_validator.py:14-47</c>). Chave ou assinatura vazias nunca valem.
/// </summary>
public static class WebhookSignature
{
    public static string Compute(ReadOnlySpan<byte> payload, string key) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), payload));

    public static bool Verify(ReadOnlySpan<byte> payload, string? signature, string? key)
    {
        if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(key))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(Compute(payload, key));
        var received = Encoding.UTF8.GetBytes(signature);

        // compare_digest do Python: tempo constante; tamanhos diferentes são diferentes.
        return received.Length == expected.Length && CryptographicOperations.FixedTimeEquals(received, expected);
    }
}

/// <summary>
/// Decisões do roteador por texto (<c>whatsapp_handler.py:133-142, 300-307, 431, 446</c>). Tudo recebe o texto já
/// sanitizado; o <c>strip</c>/<c>lower</c>/<c>upper</c> do Python são feitos aqui.
/// </summary>
public static class MessageIntent
{
    private static readonly HashSet<string> EventConfirmWords = new(["sim", "confirmar", "confirma", "ok", "s"], StringComparer.Ordinal);

    private static readonly HashSet<string> EventCancelWords =
        new(["não", "nao", "cancelar", "cancela", "desistir", "n"], StringComparer.Ordinal);

    private static readonly HashSet<string> ExclusiveEventWords = new(["sim", "s", "não", "nao", "n", "desistir"], StringComparer.Ordinal);

    private static readonly string[] TransactionReplyWords = ["CONFIRMAR", "OK", "TROCAR", "CANCELAR"];

    private static readonly string[] PaymentWords = ["paguei", "quitei", "liquidei", "saldei", "zerei"];

    /// <summary>Palavras que interrompem o check-in (<c>NightlyCheckinService.ESCAPE_KEYWORDS</c>).</summary>
    public static readonly IReadOnlyList<string> EscapeKeywords =
    [
        "gastei", "gasto", "comprei", "compra", "paguei", "pagamento", "recebi", "recebimento", "transferi", "transferência", "transferencia",
        "saldo", "extrato", "consulta", "quanto", "valor",
        "add", "criar", "registrar", "lançar", "lancar", "agendar",
        "evento", "compromisso", "agenda", "horário", "horario",
        "config", "configurar", "mudar", "alterar",
    ];

    /// <summary><c>texto.strip().lower()</c>.</summary>
    public static string Lower(string text) => text.Trim().ToLowerInvariant();

    public static bool WantsRoute(string lower) =>
        lower.Contains("calcular", StringComparison.Ordinal) && lower.Contains("rota", StringComparison.Ordinal);

    public static bool IsEventConfirm(string lower) => EventConfirmWords.Contains(lower);

    public static bool IsEventCancel(string lower) => EventCancelWords.Contains(lower);

    public static bool IsEventAnswer(string lower) => IsEventConfirm(lower) || IsEventCancel(lower);

    public static bool IsExclusiveEventWord(string lower) => ExclusiveEventWords.Contains(lower);

    /// <summary>Contém <c>CONFIRMAR</c>, <c>OK</c>, <c>TROCAR</c> ou <c>CANCELAR</c> (em qualquer parte), ou só dígitos.</summary>
    public static bool LooksLikeTransactionReply(string trimmedUpper)
    {
        foreach (var word in TransactionReplyWords)
        {
            if (trimmedUpper.Contains(word, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return trimmedUpper.Length > 0 && trimmedUpper.All(char.IsDigit);
    }

    public static bool HasPaymentWord(string message)
    {
        var lower = message.ToLowerInvariant();
        return PaymentWords.Any(word => lower.Contains(word, StringComparison.Ordinal));
    }

    /// <summary>Uma só palavra, <c>cancelar</c> ou <c>cancela</c> (<c>whatsapp_handler.py:431</c>).</summary>
    public static bool IsBareCancel(string lower, string message) =>
        (lower is "cancelar" or "cancela") && message.Trim().Split(default(char[]), StringSplitOptions.RemoveEmptyEntries).Length == 1;

    /// <summary>
    /// Desvio 210: palavra inteira, aceitando o plural simples (<c>gastos</c>, <c>eventos</c>, <c>valores</c>); o Flask procurava trecho
    /// ("valor" casava com "valorizar").
    /// </summary>
    public static bool HasEscapeKeyword(string message)
    {
        var lower = message.ToLowerInvariant();
        return EscapeKeywords.Any(word =>
            CheckinReplyParser.ContainsWord(lower, word)
            || CheckinReplyParser.ContainsWord(lower, word + "s")
            || CheckinReplyParser.ContainsWord(lower, word + "es"));
    }
}

/// <summary>Textos fixos de resposta ao usuário (spec 012 §7.1), literais do Flask.</summary>
public static class WhatsAppTexts
{
    public const string NotRegistered =
        "Olá! 👋 Parece que você ainda não tem cadastro no Meu Secretário.\n\n" +
        "Para começar a usar, crie sua conta em nosso site:\n" +
        "https://app.meusecretario.com/register\n\n" +
        "Após criar sua conta, volte aqui e eu estarei pronto para ajudar! 😊";

    public const string NothingPendingToConfirm =
        "✅ Não encontrei nada pendente para confirmar.\n\nSe quer registrar algo, diga:\nExemplo: 'gastei 50 em comida'";

    public const string NothingPendingToCancel =
        "❌ Não encontrei nada pendente para cancelar.\n\nSe quer deletar um evento específico, diga qual:\nExemplo: 'Deletar academia de hoje'";

    /// <summary><c>BaseIntent._get_error_message</c>.</summary>
    public const string IntentFailed = "❌ Ops! Algo deu errado. Por favor, tente novamente.";

    public const string NotUnderstood = "🤔 Não entendi. Tente 'gastei 50' ou 'o que você pode fazer?'.";

    public const string AiLimit =
        "⚠️ *Limite de IA Excedido*\n\n" +
        "O sistema atingiu o limite diário de processamento de IA.\n\n" +
        "🔧 *Soluções:*\n" +
        "• Aguarde alguns minutos e tente novamente\n" +
        "• Use comandos diretos (ex: 'gastei 50 em comida')\n" +
        "• Configure sua própria chave de API do Gemini\n\n" +
        "⏰ O limite é renovado automaticamente.";

    /// <summary>Mensagem de erro do Flask que aciona o 429 (<c>whatsapp_handler.py:1561</c>).</summary>
    public static bool IsQuotaMessage(string? message) =>
        message is not null
        && (message.Contains("429", StringComparison.Ordinal)
            || message.Contains("quota", StringComparison.OrdinalIgnoreCase)
            || message.Contains("rate limit", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Equivalentes dos métodos de <c>str</c> do Python que mudam o texto das respostas.</summary>
public static class PyStrings
{
    /// <summary><c>str.capitalize()</c>: primeiro caractere em maiúscula, o resto em minúscula.</summary>
    public static string Capitalize(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        var first = char.IsSurrogate(text[0]) && text.Length > 1 ? text[..2] : text[..1];
        return first.ToUpperInvariant() + text[first.Length..].ToLowerInvariant();
    }

    /// <summary><c>str.title()</c>: cada sequência de letras começa em maiúscula e segue em minúscula.</summary>
    public static string Title(string text)
    {
        var sb = new StringBuilder(text.Length);
        var previousIsLetter = false;
        foreach (var ch in text)
        {
            sb.Append(char.IsLetter(ch) ? (previousIsLetter ? char.ToLowerInvariant(ch) : char.ToUpperInvariant(ch)) : ch);
            previousIsLetter = char.IsLetter(ch);
        }

        return sb.ToString();
    }
}
