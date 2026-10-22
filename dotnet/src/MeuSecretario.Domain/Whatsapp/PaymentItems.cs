using System.Globalization;
using System.Text;
using System.Text.Json;
using MeuSecretario.Domain.Automation;

namespace MeuSecretario.Domain.Whatsapp;

/// <summary>Resposta do Gemini para <c>extract_payment_items</c>: itens, valor total e a palavra que disparou o fluxo.</summary>
public sealed record PaymentExtraction(IReadOnlyList<string> Items, double? TotalValue, string TriggerWord)
{
    /// <summary><c>valor_total</c> verdadeiro (<c>if valor_total</c>).</summary>
    public bool HasTotal => TotalValue is { } total && total != 0;

    /// <summary>
    /// <c>payment_data.get('itens', [])</c>, <c>get('valor_total')</c> e <c>get('trigger_word', 'paguei')</c>. Formatos que quebram o
    /// Flask (não objeto, item que não é texto, total verdadeiro que não é número) lançam.
    /// </summary>
    public static PaymentExtraction Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Resposta de extração não é um objeto.");
        }

        var items = new List<string>();
        if (root.TryGetProperty("itens", out var list) && PyJsonValue.IsTruthy(list))
        {
            if (list.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Itens não são uma lista.");
            }

            foreach (var item in list.EnumerateArray())
            {
                items.Add(item.ValueKind == JsonValueKind.String ? item.GetString()! : throw new InvalidOperationException("Item não é texto."));
            }
        }

        double? total = null;
        if (root.TryGetProperty("valor_total", out var totalElement) && totalElement.ValueKind != JsonValueKind.Null)
        {
            total = totalElement.ValueKind switch
            {
                JsonValueKind.Number => double.Parse(totalElement.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture),
                JsonValueKind.True => 1,
                JsonValueKind.False => 0,
                _ when !PyJsonValue.IsTruthy(totalElement) => 0,
                _ => throw new InvalidOperationException("Valor total não é numérico."),
            };
        }

        var trigger = root.TryGetProperty("trigger_word", out var triggerElement) ? PyJsonValue.Str(triggerElement) : "paguei";
        return new PaymentExtraction(items, total, trigger);
    }
}

/// <summary>Conta fixa quitada (<c>contas_fixas_quitadas</c>).</summary>
public sealed record SettledBill(string Description, double Value, double Expected, string ScheduleType, string Category);

/// <summary>Despesa avulsa registrada (<c>despesas_criadas</c>).</summary>
public sealed record CreatedExpense(string Description, double Value, string Category, string AccountName);

/// <summary>
/// Textos do fluxo de pagamento (<c>whatsapp_handler.py</c>, spec 012 §6.b): nada reconhecido, pedido de valor e a
/// resposta unificada de quitações e despesas.
/// </summary>
public static class PaymentReply
{
    public const string NothingRecognized = "🤔 Não consegui identificar o que você pagou. Pode reformular?";

    private const string Line = "━━━━━━━━━━━━━━━━━━━━";

    public static string Build(
        IReadOnlyList<SettledBill> settled, IReadOnlyList<CreatedExpense> expenses, IReadOnlyList<string> missing, string triggerWord)
    {
        if (settled.Count == 0 && expenses.Count == 0 && missing.Count > 0)
        {
            return missing.Count == 1
                ? $"🤔 Para processar '{missing[0]}', preciso do valor.\n\nExemplo: *{triggerWord} {missing[0]} 150*"
                : $"🤔 Para processar '{string.Join("', '", missing)}', preciso dos valores.\n\nTente informar um item por vez com o valor.";
        }

        var sb = new StringBuilder();
        if (settled.Count == 1)
        {
            AppendSingleSettled(sb, settled[0]);
        }
        else if (settled.Count > 1)
        {
            AppendManySettled(sb, settled);
        }

        if (expenses.Count > 0)
        {
            if (settled.Count > 0)
            {
                sb.Append('\n').Append(Line).Append("\n\n");
            }

            if (expenses.Count == 1)
            {
                var d = expenses[0];
                sb.Append("💸 *DESPESA REGISTRADA* 💸\n\n")
                    .Append("📝 ").Append(d.Description).Append('\n')
                    .Append("💰 ").Append(MoneyText.Brl(d.Value)).Append('\n')
                    .Append("📊 ").Append(d.Category).Append('\n')
                    .Append("💳 ").Append(d.AccountName).Append('\n');
            }
            else
            {
                sb.Append(CultureInfo.InvariantCulture, $"💸 *{expenses.Count} DESPESAS REGISTRADAS* 💸\n\n");
                double total = 0;
                var index = 1;
                foreach (var d in expenses)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"{index++}. *{d.Description}*\n")
                        .Append("   💰 ").Append(MoneyText.Brl(d.Value)).Append('\n')
                        .Append("   📊 ").Append(d.Category).Append('\n')
                        .Append("   💳 ").Append(d.AccountName).Append("\n\n");
                    total += d.Value;
                }

                sb.Append(Line).Append('\n').Append("💵 *Total: ").Append(MoneyText.Brl(total)).Append("*\n");
            }
        }

        if (missing.Count > 0 && (settled.Count > 0 || expenses.Count > 0))
        {
            sb.Append("\n\n⚠️ *Itens não processados* (faltou valor):\n");
            foreach (var name in missing)
            {
                sb.Append("• ").Append(name).Append('\n');
            }
        }

        return sb.ToString();
    }

    private static void AppendSingleSettled(StringBuilder sb, SettledBill c)
    {
        sb.Append("✅ *CONTA QUITADA* ✅\n\n")
            .Append("📝 ").Append(c.Description).Append('\n')
            .Append("💰 ").Append(MoneyText.Brl(c.Value)).Append('\n')
            .Append("📊 ").Append(c.Category).Append("\n\n")
            .Append("_Esta conta não será cobrada automaticamente este mês._\n");

        var difference = Math.Abs(c.Expected - c.Value);
        if (difference <= 1)
        {
            return;
        }

        if (c.ScheduleType == "LEMBRETE_VARIAVEL")
        {
            sb.Append("\n📊 *Lembrete Variável*\n")
                .Append("Valor previsto: ").Append(MoneyText.Brl(c.Expected)).Append('\n')
                .Append("Valor pago: ").Append(MoneyText.Brl(c.Value));
        }
        else
        {
            var sign = c.Value > c.Expected ? "+" : "-";
            sb.Append("\n💡 Valor previsto era ").Append(MoneyText.Brl(c.Expected))
                .Append(" (").Append(sign).Append(MoneyText.Brl(difference)).Append(')');
        }
    }

    private static void AppendManySettled(StringBuilder sb, IReadOnlyList<SettledBill> settled)
    {
        sb.Append(CultureInfo.InvariantCulture, $"✅ *{settled.Count} CONTAS QUITADAS* ✅\n\n");
        double total = 0;
        var index = 1;
        foreach (var c in settled)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{index++}. *{c.Description}*\n")
                .Append("   💰 ").Append(MoneyText.Brl(c.Value)).Append('\n')
                .Append("   📊 ").Append(c.Category).Append('\n');
            if (c.ScheduleType == "LEMBRETE_VARIAVEL")
            {
                sb.Append("   ⚠️ Variável (previsto: ").Append(MoneyText.Brl(c.Expected)).Append(")\n");
            }

            sb.Append('\n');
            total += c.Value;
        }

        sb.Append(Line).Append('\n').Append("💵 *Total: ").Append(MoneyText.Brl(total)).Append("*\n\n");
    }
}
