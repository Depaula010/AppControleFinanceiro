using System.Globalization;
using System.Text.RegularExpressions;

namespace MeuSecretario.Domain.Transactions;

/// <summary>
/// Receita ou despesa (as duas formas que entram por API e pelo serviço de lançamento). Fica no Domain porque as
/// regras de entrada o produzem; os contratos de lançamento (Application) o reutilizam.
/// </summary>
public enum TransactionKind
{
    Income,
    Expense,
}

/// <summary>Tipos gravados em <c>transacoes.tipo_transacao</c> (CHECK do banco) e o contrato HTTP (desvio 001).</summary>
public static class TransactionTypes
{
    public const string Income = "Renda";
    public const string Expense = "Despesa";
    public const string Transfer = "Transferência";
    public const string InvoicePayment = "Pagamento Fatura";

    /// <summary>Tipo HTTP para o do banco: só <c>Renda</c> muda (vira <c>Receita</c>).</summary>
    public static string ToApi(string dbType) => dbType == Income ? "Receita" : dbType;

    /// <summary>Texto HTTP exato (<c>Receita</c>/<c>Despesa</c>, com maiúscula) para o tipo; qualquer outro é <c>null</c>.</summary>
    public static TransactionKind? FromApi(string? apiType) => apiType switch
    {
        "Receita" => TransactionKind.Income,
        "Despesa" => TransactionKind.Expense,
        _ => null,
    };

    public static string ToDb(TransactionKind kind) => kind == TransactionKind.Income ? Income : Expense;

    /// <summary>Só receita e despesa podem ser editadas por PUT (desvio 068).</summary>
    public static bool IsEditable(string dbType) => dbType is Income or Expense;
}

/// <summary>Sinal do valor (R1): receita positiva, despesa negativa.</summary>
public static class SignedAmount
{
    public static decimal Apply(TransactionKind kind, decimal amount) =>
        kind == TransactionKind.Income ? amount : -amount;

    /// <summary>
    /// Valor gravado pelo PUT (RN-004-13). O tipo considerado é o do corpo, se vier, senão o atual da linha; só
    /// <c>Receita</c>/<c>Renda</c> é positivo. <paramref name="magnitude"/> é o módulo (valor do corpo ou o módulo do valor atual).
    /// </summary>
    public static decimal ForUpdate(string currentDbType, TransactionKind? bodyKind, decimal magnitude)
    {
        var income = bodyKind is { } kind ? kind == TransactionKind.Income : currentDbType == TransactionTypes.Income;
        return income ? magnitude : -magnitude;
    }
}

/// <summary>Datas de calendário como o Python (<c>relativedelta</c>, <c>monthrange</c>).</summary>
public static class MonthDays
{
    /// <summary>Dia inexistente no mês cai no último dia (<c>31</c> em fevereiro ⇒ 28 ou 29).</summary>
    public static DateOnly Clamp(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));

    /// <summary>Soma meses com o corte de <c>relativedelta</c> (31/01 + 1 mês = 28/02).</summary>
    public static DateOnly AddMonths(DateOnly date, int months) => date.AddMonths(months);
}

/// <summary><c>datetime.strptime(x, '%Y-%m-%d')</c> (RN-004-06).</summary>
public static partial class FlaskDate
{
    [GeneratedRegex("^([0-9]{4})-([0-9]{1,2})-([0-9]{1,2})\\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool TryParse(string? text, out DateOnly date)
    {
        date = default;
        if (text is null)
        {
            return false;
        }

        var match = Pattern().Match(text);
        if (!match.Success)
        {
            return false;
        }

        var year = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var month = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var day = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        date = new DateOnly(year, month, day);
        return true;
    }
}

/// <summary>Descrições literais dos pares (spec 004 §7.3).</summary>
public static class PostingDescriptions
{
    public static string TransferOut(string destinationName) => $"Transferência para {destinationName}";

    public static string TransferIn(string originName) => $"Transferência de {originName}";

    public static string PaymentOut(string cardName) => $"Pagamento Fatura {cardName}";

    public static string PaymentIn(string originName) => $"Pagamento Recebido (de {originName})";
}

/// <summary>Agendamento das parcelas seguintes de uma compra parcelada (R6, spec 004 §7.4).</summary>
public readonly record struct InstallmentSchedule(
    DateOnly StartDate, int DayOfMonth, int InstallmentsExecuted, int Total, decimal Amount)
{
    public static InstallmentSchedule For(DateOnly purchase, decimal amount, int total) =>
        new(MonthDays.AddMonths(purchase, 1), purchase.Day, 1, total, amount);
}
