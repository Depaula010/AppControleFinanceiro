using MeuSecretario.Application.Jobs;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Jobs;
using MeuSecretario.Domain.Transactions;

namespace MeuSecretario.Application.Automation;

/// <summary>O que a baixa gravou: a transação e o nome com que ela entrou (parcelado ganha <c>(n/N)</c>).</summary>
public sealed record SettledPosting(int TransactionId, string Description);

/// <summary>
/// Baixa de uma conta do cadastro pelo "paguei" do WhatsApp e pelo SMS. Fixa e lembrete variável: despesa avulsa com a
/// descrição do agendamento (<c>settle_fixed_bill</c>). Parcelado (desvio 270): pelo mesmo executor do check-in, que liga a
/// transação ao agendamento, grava a competência e o nome <c>X (n/N)</c> e avança o contador (desativa na última parcela).
/// </summary>
public sealed class BillSettlement(IAutomationLookups lookups, DirectPoster poster, IScheduleExecutor executor)
{
    public async Task<SettledPosting> SettleAsync(int userId, int scheduleId, double amount, DateOnly day, CancellationToken cancellationToken)
    {
        var target = await lookups.FindSettleTargetAsync(userId, scheduleId, cancellationToken)
                     ?? throw new AutomationFailureException("agendamento não encontrado");

        if (target.Kind != ScheduleKinds.Installment)
        {
            var posted = await poster.PostAsync(
                userId, target.AccountId, target.SubcategoryId, TransactionKind.Expense, amount, target.Description, day, cancellationToken);
            return new SettledPosting(posted.TransactionId, target.Description);
        }

        if (!MoneyDecimal.TryFromDouble(amount, out var value))
        {
            throw new AutomationFailureException("valor inválido");
        }

        var lastDay = DateTime.DaysInMonth(day.Year, day.Month);
        var competence = new DateOnly(day.Year, day.Month, Math.Clamp(target.Day, 1, lastDay));
        var outcome = await executor.ExecuteAsync(
            new ScheduleExecutionRequest(scheduleId, day, Amount: value, Competence: competence), cancellationToken);
        return outcome switch
        {
            ScheduleExecutionOutcome.Posted p => new SettledPosting(p.TransactionId, p.Description),
            ScheduleExecutionOutcome.Skipped => throw new AutomationFailureException("parcela já lançada no mês"),
            _ => throw new AutomationFailureException("parcelado sem parcela a pagar"),
        };
    }
}
