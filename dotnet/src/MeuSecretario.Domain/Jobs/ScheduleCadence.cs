using MeuSecretario.Domain.Bills;

namespace MeuSecretario.Domain.Jobs;

/// <summary>
/// Agendamentos de cadência contínua (desvio 255): <c>SEMANAL</c> (7 dias), <c>QUINZENAL</c> (14) e <c>DIARIA</c> (1). Não têm "dia do mês":
/// o próximo vencimento é o do último lançamento mais o intervalo, ou <c>data_inicio</c> se nunca foi lançado. As outras periodicidades
/// (<c>MENSAL</c>, <c>ANUAL</c>) seguem o <c>dia_execucao</c>. O SQL (<c>SchedulePaymentSql.NextDue</c>) tem a mesma definição.
/// </summary>
public static class ScheduleCadence
{
    public static int? IntervalDays(string periodicity) => periodicity switch
    {
        Periodicities.Daily => 1,
        Periodicities.Weekly => 7,
        Periodicities.Biweekly => 14,
        _ => null,
    };

    public static bool IsRolling(string periodicity) => IntervalDays(periodicity) is not null;

    /// <summary>
    /// Próximo vencimento de um agendamento contínuo: <paramref name="lastDone"/> (a competência do último lançamento, ou a data dele se
    /// antigo) + intervalo; sem lançamento, <paramref name="startDate"/>. <c>null</c> se a periodicidade não é contínua.
    /// </summary>
    public static DateOnly? NextDue(string periodicity, DateOnly? lastDone, DateOnly? startDate) =>
        IntervalDays(periodicity) is not { } days ? null : lastDone is { } last ? last.AddDays(days) : startDate;
}
