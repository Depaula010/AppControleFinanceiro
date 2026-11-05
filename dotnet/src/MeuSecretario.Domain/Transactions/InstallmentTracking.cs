using MeuSecretario.Domain.Bills;

namespace MeuSecretario.Domain.Transactions;

/// <summary>Parcelado ativo que corresponde a uma compra: mesmo usuário, conta, descrição-base e total de parcelas.</summary>
public sealed record TrackedInstallmentSchedule(int Id, int ExecutedInstallments);

/// <summary>O que fazer com o agendamento quando entra uma despesa com <c>(n/N)</c> na descrição (desvio 271).</summary>
public abstract record InstallmentTrackingAction
{
    /// <summary>Não há parcelado para a compra: cria um com <paramref name="Executed"/> parcelas já lançadas e liga a transação.</summary>
    public sealed record Create(int Executed) : InstallmentTrackingAction;

    /// <summary>O parcelado existe e ficou para trás: o contador vai para <paramref name="Executed"/> e a transação é ligada.</summary>
    public sealed record Advance(int ScheduleId, int Executed, bool Deactivate) : InstallmentTrackingAction;

    /// <summary>Nada a fazer: última parcela sem cadastro, ou parcela que o agendamento já contou (provável repetição).</summary>
    public sealed record None : InstallmentTrackingAction;
}

/// <summary>
/// Desvio 271: compra parcelada que chega como despesa simples (SMS, atalho, automate, extrato do banco) com
/// <c>"LOJA (n/N)"</c> na descrição vira conta parcelada, para aparecer em Contas Mensais até a última parcela.
/// </summary>
public static class InstallmentTracking
{
    public static InstallmentTrackingAction Decide(InstallmentSuffix suffix, TrackedInstallmentSchedule? existing)
    {
        ArgumentNullException.ThrowIfNull(suffix);
        if (existing is null)
        {
            return suffix.Number < suffix.Total
                ? new InstallmentTrackingAction.Create(suffix.Number)
                : new InstallmentTrackingAction.None();
        }

        return suffix.Number > existing.ExecutedInstallments
            ? new InstallmentTrackingAction.Advance(existing.Id, suffix.Number, suffix.Number >= suffix.Total)
            : new InstallmentTrackingAction.None();
    }

    /// <summary>Agendamento das parcelas seguintes: começa um mês depois da parcela lançada, no mesmo dia do mês.</summary>
    public static InstallmentSchedule PlanFor(DateOnly postedOn, decimal amount, InstallmentSuffix suffix)
    {
        ArgumentNullException.ThrowIfNull(suffix);
        return new InstallmentSchedule(MonthDays.AddMonths(postedOn, 1), postedOn.Day, suffix.Number, suffix.Total, amount);
    }
}
