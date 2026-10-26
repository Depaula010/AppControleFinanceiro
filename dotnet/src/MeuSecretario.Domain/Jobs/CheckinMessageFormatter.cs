using System.Globalization;
using System.Text;

namespace MeuSecretario.Domain.Jobs;

/// <summary>
/// <c>format_consolidated_checkin_message</c> (<c>nightly_checkin_service.py</c>, RN-013-45): texto literal, uma
/// mensagem só. <c>checkinId</c> nulo é o modo leitura da intenção do WhatsApp (título <c>RESUMO FINANCEIRO</c>).
/// </summary>
public static class CheckinMessageFormatter
{
    private sealed record PendingItem(string Description, decimal Value, string Account, string Status, int Day, DateOnly? DueDate);

    private sealed record OverdueItem(string Description, decimal Value, DateOnly? DueDate);

    public static string? Format(CheckinSnapshot snapshot, DateOnly today, string? checkinId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var pendingExpenses = new List<PendingItem>();
        var pendingIncomes = new List<PendingItem>();
        var overdueExpenses = new List<OverdueItem>();
        var overdueIncomes = new List<OverdueItem>();
        var cardReminders = new List<(string Description, decimal Value, string Account)>();

        foreach (var bill in snapshot.PendingBills)
        {
            int daysLate;
            string? status;
            if (bill.DueDate is { } due)
            {
                daysLate = today.DayNumber - due.DayNumber;
                status = daysLate switch
                {
                    0 => "Vence hoje",
                    1 => "Venceu ontem",
                    <= 3 => $"Venceu em {due.ToString("dd/MM", CultureInfo.InvariantCulture)}",
                    _ => null,
                };
            }
            else
            {
                (status, daysLate) = CheckinRules.CategorizeByDelay(bill.Day, today);
            }

            if (CheckinRules.IsCardExpense(bill))
            {
                if (daysLate == 0)
                {
                    cardReminders.Add((bill.Description, bill.ExpectedAmount ?? 0m, bill.AccountName));
                }

                continue;
            }

            var item = new PendingItem(
                CheckinRules.DescriptionWithInstallment(bill),
                bill.ExpectedAmount ?? 0m,
                bill.AccountName,
                status ?? $"Atrasado {daysLate.ToString(CultureInfo.InvariantCulture)} dias",
                bill.Day,
                bill.DueDate);
            (bill.GroupName == CheckinRules.IncomeGroup ? pendingIncomes : pendingExpenses).Add(item);
        }

        foreach (var bill in snapshot.OverdueBills)
        {
            if (CheckinRules.IsCardExpense(bill))
            {
                continue;
            }

            var item = new OverdueItem(CheckinRules.DescriptionWithInstallment(bill), bill.ExpectedAmount ?? 0m, bill.DueDate);
            if (bill.GroupName == CheckinRules.IncomeGroup)
            {
                overdueIncomes.Add(item);
            }
            else
            {
                overdueExpenses.Add(item);
            }
        }

        if (pendingExpenses.Count == 0 && pendingIncomes.Count == 0 && overdueExpenses.Count == 0 && overdueIncomes.Count == 0
            && snapshot.OverdueInvoices.Count == 0 && cardReminders.Count == 0 && snapshot.InvoicesDueToday.Count == 0)
        {
            return null;
        }

        var message = new StringBuilder();
        message.Append(checkinId is not null ? "🌙 *CHECK-IN NOTURNO* 🌙\n\n" : "📊 *RESUMO FINANCEIRO* 📊\n\n");

        if (cardReminders.Count > 0)
        {
            message.Append("💳 *DÉBITO CARTÃO DE CRÉDITO*\n");
            foreach (var reminder in cardReminders)
            {
                message.Append(CultureInfo.InvariantCulture, $" * {reminder.Description} - {PtBrMoney.Format(reminder.Value)} - Será debitado hoje\n");
            }

            message.Append('\n');
        }

        var number = 1;

        if (pendingIncomes.Count > 0 || overdueIncomes.Count > 0)
        {
            message.Append("💵 *RECEITAS PENDENTES:*\n");
            message.Append("_Valores previstos que ainda não foram recebidos - confirmáveis via check-in_\n\n");

            var totalIncome = 0m;
            foreach (var item in pendingIncomes)
            {
                totalIncome += item.Value;

                // O vencimento real pode ser do mês anterior (até 7 dias após a virada); "mês de hoje" mostraria uma data futura.
                var (day, month) = item.DueDate is { } pendingDue ? (pendingDue.Day, pendingDue.Month) : (item.Day, today.Month);
                message.Append(CultureInfo.InvariantCulture,
                    $"{number}. {item.Description} - {PtBrMoney.Format(item.Value)} - Previsto em {day:00}/{month:00}\n");
                number++;
            }

            foreach (var item in overdueIncomes)
            {
                totalIncome += item.Value;
                if (item.DueDate is { } due)
                {
                    message.Append(CultureInfo.InvariantCulture,
                        $"{number}. {item.Description} - {PtBrMoney.Format(item.Value)} - Previsto em {InvoiceNotificationFormatter.Date(due)}\n");
                }
                else
                {
                    message.Append(CultureInfo.InvariantCulture, $"{number}. {item.Description} - {PtBrMoney.Format(item.Value)}\n");
                }

                number++;
            }

            message.Append(CultureInfo.InvariantCulture, $"💰 *Total:* {PtBrMoney.Format(totalIncome)}\n\n");
        }

        if (pendingExpenses.Count > 0)
        {
            message.Append("💸 *DESPESAS PENDENTES:*\n");
            foreach (var item in pendingExpenses)
            {
                message.Append(CultureInfo.InvariantCulture,
                    $"{number}. {item.Description} - {PtBrMoney.Format(item.Value)} ({item.Account}) - {item.Status}\n");
                number++;
            }

            message.Append('\n');
        }

        if (overdueExpenses.Count > 0)
        {
            message.Append("🔴 *DESPESAS ATRASADAS (há mais de 7 dias):*\n");
            var totalLate = 0m;
            foreach (var item in overdueExpenses)
            {
                totalLate += item.Value;
                if (item.DueDate is { } due)
                {
                    var daysLate = today.DayNumber - due.DayNumber;
                    if (daysLate > 0)
                    {
                        message.Append(CultureInfo.InvariantCulture,
                            $"{number}. {item.Description} - {PtBrMoney.Format(item.Value)} - Venceu em {InvoiceNotificationFormatter.Date(due)} ({daysLate} dias) ⚠️\n");
                    }
                    else
                    {
                        message.Append(CultureInfo.InvariantCulture,
                            $"{number}. {item.Description} - {PtBrMoney.Format(item.Value)} - Previsto para {InvoiceNotificationFormatter.Date(due)}\n");
                    }
                }
                else
                {
                    message.Append(CultureInfo.InvariantCulture, $"{number}. {item.Description} - {PtBrMoney.Format(item.Value)}\n");
                }

                number++;
            }

            message.Append(CultureInfo.InvariantCulture, $"\n💸 *Total atrasado:* {PtBrMoney.Format(totalLate)}\n");
        }

        if (snapshot.OverdueInvoices.Count > 0)
        {
            message.Append("🔴 *FATURAS VENCIDAS:*\n");
            foreach (var invoice in snapshot.OverdueInvoices)
            {
                // Desvio 130: o Python lia fatura['nome_conta'] (KeyError, a query devolve 'cartao'); aqui sai o nome do cartão.
                // Desvio 264: no check-in a fatura é um item numerado (paga-se pelo "ok" ou pelo número); no modo leitura segue o marcador.
                var bullet = checkinId is null ? "•" : $"{number.ToString(CultureInfo.InvariantCulture)}.";
                message.Append(CultureInfo.InvariantCulture,
                    $"{bullet} {invoice.Card} - {PtBrMoney.Format(invoice.Total)} - Venceu em {InvoiceNotificationFormatter.Date(invoice.DueDate)}\n");
                number++;
            }

            message.Append('\n');
        }

        if (snapshot.InvoicesDueToday.Count > 0)
        {
            message.Append("⏰ *FATURAS QUE VENCEM HOJE:*\n");
            message.Append("_Atenção! Estas faturas devem ser pagas hoje para evitar juros._\n\n");
            foreach (var invoice in snapshot.InvoicesDueToday)
            {
                var prefix = checkinId is null ? string.Empty : $"{number.ToString(CultureInfo.InvariantCulture)}. ";
                message.Append(CultureInfo.InvariantCulture, $"{prefix}💳 {invoice.AccountName} - {PtBrMoney.Format(invoice.Total)} - Vence HOJE\n");
                number++;
            }

            message.Append('\n');
        }

        var anyConfirmable = pendingExpenses.Count > 0 || pendingIncomes.Count > 0 || overdueExpenses.Count > 0 || overdueIncomes.Count > 0
            || (checkinId is not null && (snapshot.OverdueInvoices.Count > 0 || snapshot.InvoicesDueToday.Count > 0));
        if (anyConfirmable)
        {
            if (checkinId is not null)
            {
                message.Append("━━━━━━━━━━━━━━━━━━━━\n");
                message.Append("🔹 *COMO RESPONDER:*\n\n");
                message.Append("✅ Tudo pago:\n");
                message.Append("👍 ou \"Ok\"\n\n");
                message.Append("📝 Pagamento parcial:\n");
                message.Append("\"1\" / \"1 e 3\" / \"1, 2, 4\"\n\n");
                message.Append("⏸️ Não paguei ainda:\n");
                message.Append("\"Depois\" / \"Não\"\n\n");
                message.Append(CultureInfo.InvariantCulture, $"_ID: {checkinId} | Expira em 1h_");
            }
            else
            {
                message.Append("\n━━━━━━━━━━━━━━━━━━━━\n");
                message.Append("_💡 Para confirmar pagamentos, aguarde o check-in noturno automático._");
            }
        }

        return message.ToString();
    }
}
