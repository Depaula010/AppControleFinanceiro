using MeuSecretario.Application.Jobs;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Jobs;
using MeuSecretario.Domain.Transactions;

namespace MeuSecretario.Application.Automation;

/// <summary>O que a baixa gravou: a transação e o nome com que ela entrou (parcelado ganha <c>(n/N)</c>).</summary>
public sealed record SettledPosting(int TransactionId, string Description);

/// <summary>
/// Baixa de uma conta do cadastro pelo "paguei" do WhatsApp e pelo SMS. Toda conta (fixa, lembrete variável e parcelada) passa pelo
/// mesmo executor do check-in (desvios 270 e 275): a transação fica ligada ao agendamento, com a competência do vencimento, e o
/// parcelado ganha <c>X (n/N)</c> e avança o contador (desativa na última parcela). Sem isso, conta semanal paga por "paguei" nunca
/// saía do check-in e podia ser lançada duas vezes.
/// </summary>
public sealed class BillSettlement(IAutomationLookups lookups, IScheduleExecutor executor)
{
    public async Task<SettledPosting> SettleAsync(int userId, int scheduleId, double amount, DateOnly day, CancellationToken cancellationToken)
    {
        var target = await lookups.FindSettleTargetAsync(userId, scheduleId, cancellationToken)
                     ?? throw new AutomationFailureException("agendamento não encontrado");

        if (!MoneyDecimal.TryFromDouble(amount, out var value))
        {
            throw new AutomationFailureException("valor inválido");
        }

        // Mensal/anual: o vencimento do mês do pagamento. Cadência contínua (semanal, quinzenal, diária): o próprio dia.
        var competence = ScheduleCadence.IsRolling(target.Periodicity)
            ? day
            : new DateOnly(day.Year, day.Month, Math.Clamp(target.Day, 1, DateTime.DaysInMonth(day.Year, day.Month)));
        var outcome = await executor.ExecuteAsync(
            new ScheduleExecutionRequest(scheduleId, day, AllowVariableReminder: true, Amount: value, Competence: competence), cancellationToken);
        return outcome switch
        {
            ScheduleExecutionOutcome.Posted p => new SettledPosting(p.TransactionId, p.Description),
            ScheduleExecutionOutcome.Skipped => throw new AutomationFailureException(
                target.Kind == ScheduleKinds.Installment ? "parcela já lançada no mês" : "conta já lançada neste vencimento"),
            _ => throw new AutomationFailureException(
                target.Kind == ScheduleKinds.Installment ? "parcelado sem parcela a pagar" : "conta não pode ser lançada"),
        };
    }
}
