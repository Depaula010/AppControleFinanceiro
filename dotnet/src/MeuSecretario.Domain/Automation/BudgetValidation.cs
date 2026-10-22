using System.Globalization;
using System.Text;

namespace MeuSecretario.Domain.Automation;

/// <summary>Pote de gastos que contém a subcategoria da despesa (<c>get_potes_for_subcategoria</c>).</summary>
public sealed record PotRow(int Id, string Name, double Limit, string Periodicity);

/// <summary>Período do pote (desvio 015: a definição de <c>budget_validation_service</c>, RN-011-11).</summary>
public static class BudgetPeriods
{
    public static (DateOnly Start, DateOnly End) For(string periodicity, DateOnly reference)
    {
        switch (periodicity)
        {
            case "SEMANAL":
                var sinceMonday = ((int)reference.DayOfWeek + 6) % 7;
                var start = reference.AddDays(-sinceMonday);
                return (start, start.AddDays(6));
            case "QUINZENAL":
                return reference.Day <= 15
                    ? (new DateOnly(reference.Year, reference.Month, 1), new DateOnly(reference.Year, reference.Month, 15))
                    : (new DateOnly(reference.Year, reference.Month, 16), LastDay(reference));
            case "ANUAL":
                return (new DateOnly(reference.Year, 1, 1), new DateOnly(reference.Year, 12, 31));
            default:
                return (new DateOnly(reference.Year, reference.Month, 1), LastDay(reference));
        }
    }

    private static DateOnly LastDay(DateOnly date) =>
        new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
}

/// <summary>Resultado de um pote (<c>BudgetValidationResult</c>).</summary>
public sealed record PotCheck(int PotId, string Name, double Limit, double Spent, double SpentAfter, double Percent, bool Exceeds);

/// <summary>Resultado da validação (<c>ValidateBudgetOutput</c>): só avisa, nunca bloqueia.</summary>
public sealed record BudgetCheck(bool RequiresConfirmation, string Message, IReadOnlyList<PotCheck> Checks)
{
    public static BudgetCheck None { get; } = new(false, string.Empty, []);
}

/// <summary><c>validate_budget</c> sem I/O (<c>budget_validation_service.py</c>, spec 011 §7.6).</summary>
public static class BudgetValidation
{
    /// <param name="pots">Cada pote com o gasto do período atual (positivo).</param>
    /// <param name="amount">Valor da transação (qualquer sinal; vale o módulo).</param>
    public static BudgetCheck Evaluate(IReadOnlyList<(PotRow Pot, double Spent)> pots, double amount)
    {
        if (pots.Count == 0)
        {
            return BudgetCheck.None;
        }

        var absolute = Math.Abs(amount);
        var checks = new List<PotCheck>();
        foreach (var (pot, spent) in pots)
        {
            var after = spent + absolute;
            var percent = pot.Limit > 0 ? after / pot.Limit * 100 : 0;
            checks.Add(new PotCheck(pot.Id, pot.Name, pot.Limit, spent, after, PyFloat.Round(percent, 1), after > pot.Limit));
        }

        var exceeded = checks.Where(c => c.Exceeds).ToList();
        return exceeded.Count == 0
            ? new BudgetCheck(false, string.Empty, checks)
            : new BudgetCheck(true, WarningMessage(exceeded), checks);
    }

    /// <summary>Sem limite o Python usa o inteiro <c>0</c> (escreve <c>0</c>, não <c>0.0</c>).</summary>
    private static string PercentText(PotCheck pot) => pot.Limit > 0 ? PyFloat.Repr(pot.Percent) : "0";

    private static string WarningMessage(List<PotCheck> exceeded)
    {
        if (exceeded.Count == 1)
        {
            var pot = exceeded[0];
            return "Atenção: Esta despesa ultrapassará seu limite!\n\n"
                + string.Create(CultureInfo.InvariantCulture, $"Pote: {pot.Name}\n")
                + $"Limite: R$ {MoneyText.Grouped2(pot.Limit)}\n"
                + $"Gasto atual: R$ {MoneyText.Grouped2(pot.Spent)}\n"
                + $"Após transação: R$ {MoneyText.Grouped2(pot.SpentAfter)} ({PercentText(pot)}% do limite)\n"
                + $"Excesso: R$ {MoneyText.Grouped2(pot.SpentAfter - pot.Limit)}\n\n"
                + "Deseja prosseguir mesmo assim?";
        }

        var sb = new StringBuilder("Atenção: Esta despesa ultrapassará limites em múltiplos potes!\n");
        foreach (var pot in exceeded)
        {
            sb.Append(CultureInfo.InvariantCulture, $"\n• {pot.Name}: R$ {MoneyText.Grouped2(pot.SpentAfter)} / R$ {MoneyText.Grouped2(pot.Limit)} ")
                .Append(CultureInfo.InvariantCulture, $"({PercentText(pot)}%) - Excesso: R$ {MoneyText.Grouped2(pot.SpentAfter - pot.Limit)}");
        }

        return sb.Append("\n\nDeseja prosseguir mesmo assim?").ToString();
    }
}
