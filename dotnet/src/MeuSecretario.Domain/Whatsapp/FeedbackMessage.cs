using System.Globalization;
using System.Text;

namespace MeuSecretario.Domain.Whatsapp;

/// <summary>Transação recém-gravada lida para o feedback (<c>buscar_dados_transacao</c>).</summary>
public sealed record FeedbackTransaction(
    int Id, int UserId, int AccountId, int SubcategoryId, string? Description, double Amount, string AccountName, string? AccountType);

/// <summary>Pote da subcategoria (<c>calcular_status_pote</c>); <paramref name="Periodicity"/> já capitalizada ("Semanal").</summary>
public sealed record PotFeedback(string Name, double Remaining, double PercentUsed, string Periodicity);

/// <summary>Rodapé: fatura do cartão (<c>calcular_fatura_atual</c>) ou saldo (<c>calcular_saldo_conta</c>).</summary>
public abstract record FeedbackFooter
{
    public sealed record Invoice(string AccountName, double Total, int? ClosingDay) : FeedbackFooter;

    public sealed record Balance(string AccountName, double Available) : FeedbackFooter;
}

/// <summary>
/// Mensagem de feedback da transação salva (<c>transaction_feedback_service.py</c>, spec 012 §7.8): cabeçalho, bloco do
/// pote (só se o alerta dispara) e rodapé de fatura ou saldo.
/// </summary>
public static class FeedbackMessage
{
    public const string Fallback = "✅ Transação Salva com Sucesso!";

    /// <summary><c>'Crédito' in tipo_conta</c> (com maiúscula; é a verificação da 012, não a de <c>transaction_intents</c>).</summary>
    public static bool IsCreditKind(string? accountType) => accountType is not null && accountType.Contains("Crédito", StringComparison.Ordinal);

    /// <summary><c>get_emoji_status</c>: semáforo do percentual usado.</summary>
    public static string Light(double percentUsed) => percentUsed < 70 ? "🟢" : percentUsed < 90 ? "🟡" : "🔴";

    /// <summary><c>deve_exibir_alerta</c>: sem configuração mostra; desativado não; limiar 0 sempre; senão percentual ≥ limiar.</summary>
    public static bool ShouldShowPot(bool hasConfig, bool active, int threshold, double percentUsed) =>
        !hasConfig || (active && (threshold == 0 || percentUsed >= threshold));

    public static string Format(FeedbackTransaction transaction, PotFeedback? pot, FeedbackFooter? footer, bool creditKind, bool showPot)
    {
        var sb = new StringBuilder("✅ Transação Salva!\n");
        sb.Append("📝 ").Append(transaction.Description ?? "None").Append('\n');
        sb.Append("💵 R$ ").Append(Fixed2(Math.Abs(transaction.Amount))).Append(" (").Append(transaction.AccountName).Append(")\n");

        if (pot is not null && showPot)
        {
            sb.Append("\n🎯 Pote ").Append(pot.Name).Append(" (").Append(pot.Periodicity).Append("):\n");
            sb.Append("Restam: R$ ").Append(Fixed2(pot.Remaining)).Append(' ').Append(Light(pot.PercentUsed)).Append('\n');
        }

        switch (footer)
        {
            case FeedbackFooter.Invoice invoice when creditKind:
                sb.Append("\n💳 Fatura ").Append(invoice.AccountName).Append(":\n");
                sb.Append("R$ ").Append(Fixed2(invoice.Total)).Append(" (Fecha dia ")
                    .Append(invoice.ClosingDay is { } day ? day.ToString(CultureInfo.InvariantCulture) : "None").Append(")\n");
                break;
            case FeedbackFooter.Balance balance when !creditKind:
                sb.Append("\n🏦 Saldo ").Append(balance.AccountName).Append(":\n");
                sb.Append("R$ ").Append(Fixed2(balance.Available)).Append(" (Disponível)\n");
                break;
        }

        return sb.ToString();
    }

    /// <summary><c>f"{v:.2f}".replace('.', ',')</c>: meio-par sobre o valor binário exato, sem milhar.</summary>
    internal static string Fixed2(double value)
    {
        if (!double.IsFinite(value))
        {
            return double.IsNaN(value) ? "nan" : value > 0 ? "inf" : "-inf";
        }

        if (Math.Abs(value) >= 1e27)
        {
            return value.ToString("F2", CultureInfo.InvariantCulture).Replace('.', ',');
        }

        var exact = decimal.Parse(value.ToString("F30", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
        var rounded = decimal.Round(exact, 2, MidpointRounding.ToEven);
        var text = rounded.ToString("F2", CultureInfo.InvariantCulture).Replace('.', ',');
        return double.IsNegative(value) && rounded == 0 ? "-" + text : text;
    }
}
