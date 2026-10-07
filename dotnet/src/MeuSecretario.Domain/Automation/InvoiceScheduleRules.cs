using System.Globalization;

namespace MeuSecretario.Domain.Automation;

/// <summary>Estatísticas de <c>process_invoice_schedules</c> (spec 011 §7.7).</summary>
public sealed record InvoiceScheduleStats(int Processed, int Fixed, int Installments, int Reminders, IReadOnlyList<InvoiceScheduleDetail> Details)
{
    public static InvoiceScheduleStats Empty { get; } = new(0, 0, 0, 0, []);
}

/// <param name="Amount"><c>0</c> (número) sem valor previsto, ou o texto com duas casas (o Flask serializa o <c>Decimal</c> como texto).</param>
public sealed record InvoiceScheduleDetail(string Type, string Description, object Amount);

/// <summary>Regras puras do populador de faturas (<c>invoice_service.py:590-800</c>).</summary>
public static class InvoiceScheduleRules
{
    public const string Fixed = "FIXO";
    public const string Installment = "PARCELADO";
    public const string Reminder = "LEMBRETE_VARIAVEL";

    /// <summary><c>date(ano, mes, dia_execucao)</c> do mês e ano da referência; dia inexistente cai no último dia.</summary>
    public static DateOnly TransactionDate(DateOnly reference, int day) =>
        new(reference.Year, reference.Month, Math.Min(day, DateTime.DaysInMonth(reference.Year, reference.Month)));

    /// <summary>Descrição lançada: a do agendamento, ou <c>"X (n/total)"</c> na parcela (<c>total</c> nulo escreve <c>None</c>).</summary>
    public static string Description(string description, string scheduleType, int installmentsExecuted, int? total) =>
        scheduleType == Installment
            ? string.Create(CultureInfo.InvariantCulture, $"{description} ({installmentsExecuted + 1}/{(total is { } t ? t.ToString(CultureInfo.InvariantCulture) : "None")})")
            : description;

    /// <summary><c>total_parcelas and nova_parcela &gt;= total_parcelas</c> (total nulo ou zero nunca desativa).</summary>
    public static bool IsLastInstallment(int newInstallment, int? total) => total is { } t && t != 0 && newInstallment >= t;

    /// <summary><c>abs(valor_previsto)</c> como o <c>jsonify</c> do Flask escreve: <c>0</c> sem valor (nulo ou zero), senão o <c>Decimal</c> em texto.</summary>
    public static object DetailAmount(decimal? expected) =>
        expected is null or 0 ? 0 : Math.Abs(expected.Value).ToString(CultureInfo.InvariantCulture);

    /// <summary>Valor gravado: <c>Renda</c> positivo, despesa negativa; sem valor previsto, zero.</summary>
    public static decimal SignedAmount(decimal? expected, bool income)
    {
        var value = expected ?? 0;
        return income ? value : -value;
    }
}
