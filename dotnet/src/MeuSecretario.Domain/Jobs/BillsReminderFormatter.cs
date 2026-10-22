using System.Globalization;
using System.Text;

namespace MeuSecretario.Domain.Jobs;

/// <summary>Conta que vence no dia alvo (<c>processar_contas_vencer</c>, spec 013 RN-013-33).</summary>
public sealed record ReminderBill(string Description, double Value, string Category, string AccountType, string GroupName);

/// <summary>Mensagem de <c>notification_processor_service.py</c>: texto literal.</summary>
public static class BillsReminderFormatter
{
    /// <summary><c>null</c> se não há contas.</summary>
    public static string? Format(string userName, IReadOnlyList<ReminderBill> bills, DateOnly targetDate, int daysBefore = 1)
    {
        ArgumentNullException.ThrowIfNull(bills);
        if (bills.Count == 0)
        {
            return null;
        }

        var expenses = new List<ReminderBill>();
        var cardExpenses = new List<ReminderBill>();
        var incomes = new List<ReminderBill>();
        foreach (var bill in bills)
        {
            if (bill.GroupName == CheckinRules.IncomeGroup)
            {
                incomes.Add(bill);
            }
            else if (bill.AccountType == ScheduleRules.CardAccountType)
            {
                cardExpenses.Add(bill);
            }
            else
            {
                expenses.Add(bill);
            }
        }

        var dayMonth = targetDate.ToString("dd/MM", CultureInfo.InvariantCulture);
        var period = daysBefore switch
        {
            1 => $"*amanhã* ({dayMonth})",
            0 => $"*hoje* ({dayMonth})",
            _ => $"em *{daysBefore.ToString(CultureInfo.InvariantCulture)} dias* ({dayMonth})",
        };

        var message = new StringBuilder();
        message.Append(CultureInfo.InvariantCulture, $"🔔 *Lembrete de Contas, {userName}!*\n\n");

        var totalBills = expenses.Count + incomes.Count;
        if (totalBills > 0)
        {
            message.Append(CultureInfo.InvariantCulture, $"📅 Você tem *{totalBills} conta(s)* que vencem {period}:\n\n");
        }

        var totalExpenses = 0d;
        if (expenses.Count > 0)
        {
            message.Append("💸 *DESPESAS:*\n");
            var index = 1;
            foreach (var bill in expenses)
            {
                totalExpenses += bill.Value;
                message.Append(CultureInfo.InvariantCulture, $"{index}. *{bill.Description}*\n");
                message.Append(CultureInfo.InvariantCulture, $"   💰 {PtBrMoney.Format(bill.Value)}\n");
                message.Append(CultureInfo.InvariantCulture, $"   📊 {bill.Category}\n\n");
                index++;
            }
        }

        var totalIncomes = 0d;
        if (incomes.Count > 0)
        {
            message.Append("💰 *RECEITAS:*\n");
            var index = 1;
            foreach (var bill in incomes)
            {
                totalIncomes += bill.Value;
                message.Append(CultureInfo.InvariantCulture, $"{index}. *{bill.Description}*\n");
                message.Append(CultureInfo.InvariantCulture, $"   💵 {PtBrMoney.Format(bill.Value)}\n");
                message.Append(CultureInfo.InvariantCulture, $"   📊 {bill.Category}\n\n");
                index++;
            }
        }

        var totalCard = 0d;
        if (cardExpenses.Count > 0)
        {
            message.Append("💳 *CARTÃO (na fatura):*\n");
            foreach (var bill in cardExpenses)
            {
                totalCard += bill.Value;
                message.Append(CultureInfo.InvariantCulture, $"ℹ️ {bill.Description}\n");
                message.Append(CultureInfo.InvariantCulture, $"   💰 {PtBrMoney.Format(bill.Value)}\n");
                message.Append(CultureInfo.InvariantCulture, $"   📊 {bill.Category}\n\n");
            }
        }

        message.Append("━━━━━━━━━━━━━━━━\n");
        if (totalExpenses > 0)
        {
            message.Append(CultureInfo.InvariantCulture, $"💸 Total Despesas: {PtBrMoney.Format(totalExpenses)}\n");
        }

        if (totalIncomes > 0)
        {
            message.Append(CultureInfo.InvariantCulture, $"💰 Total Receitas: {PtBrMoney.Format(totalIncomes)}\n");
        }

        if (totalCard > 0)
        {
            message.Append(CultureInfo.InvariantCulture, $"💳 Total Cartão: {PtBrMoney.Format(totalCard)}\n");
        }

        message.Append(cardExpenses.Count > 0
            ? "\n_💳 Despesas do cartão são apenas lembretes (já debitadas na fatura)_"
            : "\n_Não esqueça de pagar! 😊_");
        return message.ToString();
    }
}
