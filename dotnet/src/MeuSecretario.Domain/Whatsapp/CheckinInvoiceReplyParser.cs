using System.Globalization;

namespace MeuSecretario.Domain.Whatsapp;

public enum CheckinInvoiceReplyKind
{
    /// <summary>Número de uma das contas listadas (ou "ok" quando só há uma).</summary>
    Account,

    /// <summary>"Pular", "depois", "não": a fatura fica como está.</summary>
    Skip,

    /// <summary>Nada reconhecido: repete a pergunta.</summary>
    Invalid,
}

/// <summary>Resposta à pergunta "pagar a fatura com qual conta?" (desvio 264). <paramref name="Index"/> é 1..N na lista mostrada.</summary>
public sealed record CheckinInvoiceReply(CheckinInvoiceReplyKind Kind, int Index);

public static class CheckinInvoiceReplyParser
{
    private static readonly string[] SkipWords = ["pular", "pula", "depois", "nao", "não", "n", "cancelar", "cancela"];

    private static readonly string[] OkWords = ["ok", "sim", "s", "confirmar", "confirmo"];

    public static CheckinInvoiceReply Parse(string message, int accountCount)
    {
        ArgumentNullException.ThrowIfNull(message);
        var text = message.Trim().TrimEnd('.', '!').ToLowerInvariant();

        if (SkipWords.Contains(text, StringComparer.Ordinal))
        {
            return new CheckinInvoiceReply(CheckinInvoiceReplyKind.Skip, 0);
        }

        if (accountCount == 1 && (OkWords.Contains(text, StringComparer.Ordinal) || text.Contains("👍", StringComparison.Ordinal)))
        {
            return new CheckinInvoiceReply(CheckinInvoiceReplyKind.Account, 1);
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index >= 1 && index <= accountCount
            ? new CheckinInvoiceReply(CheckinInvoiceReplyKind.Account, index)
            : new CheckinInvoiceReply(CheckinInvoiceReplyKind.Invalid, 0);
    }
}
