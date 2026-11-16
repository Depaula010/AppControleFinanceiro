using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Jobs;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>Leituras das contas fixas e dos vencimentos da 012d (<c>fixed_bills_service.py</c>, <c>finance_service.py</c>); Infrastructure implementa com Dapper.</summary>
public interface IWhatsAppFixedBillsReader
{
    /// <summary>
    /// <c>get_pending_bills_for_month</c>: agendamentos ativos do mês (anuais só no mês certo; contínuos com vencimento até o fim do mês, atrasados
    /// inclusive) cuja ocorrência ainda não foi paga, pela regra do check-in (desvio 280).
    /// </summary>
    Task<IReadOnlyList<PendingBill>> ListPendingAsync(int userId, DateOnly firstDay, DateOnly lastDay, int month, CancellationToken cancellationToken);

    /// <summary><c>get_all_scheduled_bills</c>: catálogo ativo (<c>FIXO</c>, <c>LEMBRETE_VARIAVEL</c>, <c>PARCELADO</c>) por periodicidade e dia.</summary>
    Task<IReadOnlyList<CatalogBill>> ListCatalogAsync(int userId, CancellationToken cancellationToken);

    /// <summary>
    /// <c>get_vencimentos_periodo</c>: contas que vencem de <paramref name="first"/> a <paramref name="last"/> e ainda não foram pagas. O
    /// próximo vencimento e o "já pago" são os do check-in (desvio 280): semanal aparece na semana em que vence e o dia 31 vence no 30 em mês curto.
    /// </summary>
    Task<IReadOnlyList<DueBill>> ListDueBillsAsync(int userId, DateOnly first, DateOnly last, CancellationToken cancellationToken);

    /// <summary>Faturas <c>Aberta</c> do usuário que vencem entre as datas, com o valor das despesas lançadas.</summary>
    Task<IReadOnlyList<DueInvoice>> ListDueInvoicesAsync(int userId, DateOnly first, DateOnly last, CancellationToken cancellationToken);
}

/// <summary><c>Consulta Contas Fixas</c> (template): contas fixas do mês que ainda não foram executadas.</summary>
public sealed class FixedBillsIntent(IWhatsAppFixedBillsReader reader, IClock clock) : IWhatsAppIntent
{
    public string Name => "Consulta Contas Fixas";

    public bool Swallows => true;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var first = new DateOnly(today.Year, today.Month, 1);
        var last = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        var bills = await reader.ListPendingAsync(context.User.Id, first, last, today.Month, cancellationToken);
        return FixedBillsMessages.Pending(bills, today);
    }
}

/// <summary><c>Consulta Todas Contas</c> (template): catálogo completo de agendamentos, sem filtro de mês.</summary>
public sealed class AllBillsIntent(IWhatsAppFixedBillsReader reader) : IWhatsAppIntent
{
    public string Name => "Consulta Todas Contas";

    public bool Swallows => true;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken) =>
        FixedBillsMessages.Catalog(await reader.ListCatalogAsync(context.User.Id, cancellationToken));
}

/// <summary>Base dos três <c>Vencimentos *</c>: período, texto e data de referência mudam; a consulta é a mesma.</summary>
public abstract class DueDatesIntent(IWhatsAppFixedBillsReader reader, IClock clock) : IWhatsAppIntent
{
    public abstract string Name { get; }

    public bool Swallows => true;

    protected abstract string Period { get; }

    /// <summary>Início e fim do período e a data mostrada no título.</summary>
    protected abstract (DateOnly Start, DateOnly End, DateOnly Reference) Range(DateOnly today);

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var (start, end, reference) = Range(clock.Today);
        var bills = await reader.ListDueBillsAsync(context.User.Id, start, end, cancellationToken);
        var invoices = await reader.ListDueInvoicesAsync(context.User.Id, start, end, cancellationToken);
        return DueDatesMessage.Format(bills, invoices, Period, reference);
    }
}

/// <summary><c>Vencimentos Hoje</c> (template).</summary>
public sealed class DueTodayIntent(IWhatsAppFixedBillsReader reader, IClock clock) : DueDatesIntent(reader, clock)
{
    public override string Name => "Vencimentos Hoje";

    protected override string Period => DueDatesMessage.TodayPeriod;

    protected override (DateOnly Start, DateOnly End, DateOnly Reference) Range(DateOnly today) => (today, today, today);
}

/// <summary><c>Vencimentos Amanhã</c> (template).</summary>
public sealed class DueTomorrowIntent(IWhatsAppFixedBillsReader reader, IClock clock) : DueDatesIntent(reader, clock)
{
    public override string Name => "Vencimentos Amanhã";

    protected override string Period => DueDatesMessage.TomorrowPeriod;

    protected override (DateOnly Start, DateOnly End, DateOnly Reference) Range(DateOnly today)
    {
        var tomorrow = today.AddDays(1);
        return (tomorrow, tomorrow, tomorrow);
    }
}

/// <summary><c>Vencimentos Essa Semana</c> (template): de hoje a daqui a sete dias.</summary>
public sealed class DueWeekIntent(IWhatsAppFixedBillsReader reader, IClock clock) : DueDatesIntent(reader, clock)
{
    public override string Name => "Vencimentos Essa Semana";

    protected override string Period => DueDatesMessage.WeekPeriod;

    protected override (DateOnly Start, DateOnly End, DateOnly Reference) Range(DateOnly today) => (today, today.AddDays(7), today);
}

/// <summary>
/// <c>Contas Atrasadas</c> (template): snapshot do <see cref="NightlyCheckinService"/> (013) e o texto do check-in em modo leitura, sem
/// sessão no Redis.
/// </summary>
public sealed class OverdueIntent(NightlyCheckinService checkin, IClock clock) : IWhatsAppIntent
{
    public string Name => "Contas Atrasadas";

    public bool Swallows => true;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var snapshot = await checkin.CollectSnapshotAsync(context.User.Id, today, cancellationToken);
        return CheckinSnapshotMessage.Format(snapshot, today);
    }
}
