using System.Globalization;
using System.Text;

namespace MeuSecretario.Domain.Whatsapp;

/// <summary>Textos e datas da resposta ao check-in (<c>process_response</c> e <c>mark_bills_as_paid</c>).</summary>
public static class CheckinReplyTexts
{
    public const string Expired = "⏱️ Esta sessão expirou. Aguarde o próximo check-in ou registre manualmente.";

    public const string NoRecentItems = "Não há itens recentes para confirmar. Use 'Pendencias' para ver contas antigas.";

    public const string Deferred =
        "⏸️ Ok, te lembro amanhã no próximo check-in!\n\nVocê pode registrar os pagamentos manualmente a qualquer momento.";

    public const string NoValidItems = "Nenhum item válido para confirmar.";

    /// <summary>Sem o motivo da exceção (desvio 008): o Flask escreve <c>str(e)</c> depois dos dois-pontos.</summary>
    public const string SaveFailed = "❌ Erro ao salvar transações.";

    public const string NotEligibleReason = "agendamento inativo ou sem parcelas restantes";

    /// <summary>Itens pedidos que não viraram lançamento: o usuário vê o que ficou de fora e por quê, em vez de achar que pagou tudo.</summary>
    public static string NotConfirmed(IReadOnlyList<(string Description, string Reason)> items, bool includeHeader)
    {
        ArgumentNullException.ThrowIfNull(items);
        var sb = new StringBuilder(includeHeader
            ? "⚠️ *Não foi possível confirmar:*\n\n"
            : "⚠️ *Nenhuma conta foi confirmada:*\n\n");
        foreach (var (description, reason) in items)
        {
            sb.Append("• ").Append(description).Append(" (").Append(reason).Append(")\n");
        }

        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>Pergunta do valor de um lembrete variável (<paramref name="expected"/> é o valor previsto, se houver).</summary>
    public static string ValueQuestion(string description, decimal? expected, int position, int total)
    {
        var counter = total > 1 ? string.Create(CultureInfo.InvariantCulture, $" ({position}/{total})") : string.Empty;
        var hint = expected is > 0
            ? $"Responda o valor (ex: 150,50), \"ok\" para usar o previsto ({MeuSecretario.Domain.Jobs.PtBrMoney.Format(expected)}) ou \"pular\"."
            : "Responda o valor (ex: 150,50) ou \"pular\".";
        return $"💬 *Qual o valor de {description}?*{counter}\n{hint}";
    }

    public const string ValueInvalid =
        "❓ Não entendi o valor. Responda só o número (ex: 150,50), \"ok\" para o valor previsto ou \"pular\".";

    public const string ValueNeeded = "❓ Esta conta não tem valor previsto. Responda o valor (ex: 150,50) ou \"pular\".";

    public static string ValuePosted(string description, decimal amount) =>
        $"✅ {description} lançada: {MeuSecretario.Domain.Jobs.PtBrMoney.Format(amount)}";

    /// <summary>Pergunta da conta de origem do pagamento da fatura (desvio 264). <paramref name="accounts"/> são os nomes, na ordem numerada.</summary>
    public static string InvoiceQuestion(string card, decimal amount, IReadOnlyList<string> accounts, int position, int total)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        var counter = total > 1 ? string.Create(CultureInfo.InvariantCulture, $" ({position}/{total})") : string.Empty;
        var sb = new StringBuilder($"💳 *Pagar a fatura {card} ({MeuSecretario.Domain.Jobs.PtBrMoney.Format(amount)})?*{counter}{NL}");
        if (accounts.Count == 0)
        {
            return sb.Append("Não encontrei uma conta ativa (que não seja cartão) para pagar. Responda \"pular\".").ToString();
        }

        if (accounts.Count == 1)
        {
            return sb.Append(CultureInfo.InvariantCulture, $"Responda \"ok\" para pagar com {accounts[0]} ou \"pular\".").ToString();
        }

        sb.Append("Com qual conta você pagou?" + NL);
        for (var i = 0; i < accounts.Count; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{i + 1}. {accounts[i]}{NL}");
        }

        return sb.Append("Responda o número ou \"pular\".").ToString();
    }

    private const string NL = "\n";

    public const string InvoiceInvalid = "❓ Não entendi. Responda o número da conta que pagou a fatura ou \"pular\".";

    public static string InvoicePaid(string card, decimal amount, string account) =>
        $"✅ Fatura {card} paga: {MeuSecretario.Domain.Jobs.PtBrMoney.Format(amount)} ({account}).";

    public static string InvoiceSkipped(string card) => $"⏭️ A fatura {card} continua em aberto.";

    public static string InvoiceFailed(string card) => $"❌ Não consegui registrar o pagamento da fatura {card}. Registre manualmente: \"paguei a fatura do {card} com [conta]\".";

    public static string ValueSkipped(string description) => $"⏭️ {description} não foi lançada.";

    public static string ValueAlreadyPosted(string description) => $"✅ {description} já estava lançada neste mês.";

    public static string Confirmed(IReadOnlyList<string> descriptions, int totalItems)
    {
        ArgumentNullException.ThrowIfNull(descriptions);
        var sb = new StringBuilder(descriptions.Count == totalItems
            ? string.Create(CultureInfo.InvariantCulture, $"✅ *Todas as {totalItems} contas confirmadas!*\n\n")
            : string.Create(CultureInfo.InvariantCulture, $"✅ *{descriptions.Count} conta(s) confirmada(s):*\n\n"));
        foreach (var description in descriptions)
        {
            sb.Append("• ").Append(description).Append('\n');
        }

        return sb.Append("\n💚 Transações registradas com sucesso!").ToString();
    }

    /// <summary>
    /// Data do lançamento (<c>mark_bills_as_paid</c>): com <c>data_vencimento_real</c> (texto ISO) vale o vencimento se o atraso é de
    /// até um dia (ou futuro), senão hoje; sem ela, o <c>dia_execucao</c> deste mês quando venceu hoje ou ontem, senão hoje.
    /// Texto que não é data ISO lança <see cref="FormatException"/>, como o <c>ValueError</c> do Python.
    /// </summary>
    public static DateOnly PaymentDate(string? realDueDate, int scheduleDay, DateOnly today)
    {
        if (!string.IsNullOrEmpty(realDueDate))
        {
            var due = DateOnly.ParseExact(realDueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return today.DayNumber - due.DayNumber <= 1 ? due : today;
        }

        var late = today.Day - scheduleDay;
        return late is >= 0 and <= 1 ? new DateOnly(today.Year, today.Month, scheduleDay) : today;
    }
}
