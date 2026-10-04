using System.Globalization;
using MeuSecretario.Api.Contracts;
using MeuSecretario.Application.Bills;
using MeuSecretario.Domain.Bills;

namespace MeuSecretario.Api.Bills;

/// <summary>Corpos de resposta de contas mensais (envelope A). As chaves saem na ordem da spec 005 §3.</summary>
public static class BillsResponses
{
    public static IResult List(IReadOnlyList<BillListItem> bills) =>
        Envelopes.A.Success(data: bills.Select(b => new Dictionary<string, object?>
        {
            ["id"] = b.Id,
            ["descricao"] = b.Description,
            ["valor_previsto"] = Money(b.Amount),
            ["tipo_agendamento"] = b.Type,
            ["tipo_transacao"] = BillKinds.IsIncome(b.Group, b.MacroName) ? "Receita" : "Despesa",
            ["grupo"] = b.Group,
            ["periodicidade"] = b.Periodicity,
            ["dia_execucao"] = b.Day,
            ["mes_execucao"] = b.Month,
            ["notificar_antes_dias"] = b.NotifyDaysBefore,
            ["subcategoria_id"] = b.SubcategoryId,
            ["subcategoria_nome"] = b.SubcategoryName,
            ["macro_categoria_nome"] = b.MacroName,
            ["conta_id"] = b.AccountId,
            ["conta_nome"] = b.AccountName,
            ["data_inicio"] = Date(b.StartDate),
            ["incluir_na_reserva"] = b.IncludeInReserve,
            ["total_parcelas"] = b.TotalInstallments,
            ["parcelas_executadas"] = b.ExecutedInstallments,
        }).ToList());

    /// <summary>201, sem <c>message</c> (spec §3.2).</summary>
    public static IResult Created(BillDetails bill) =>
        Envelopes.A.Success(Details(bill), statusCode: StatusCodes.Status201Created);

    /// <summary>200, sem <c>message</c> (spec §3.3).</summary>
    public static IResult Updated(BillDetails bill) => Envelopes.A.Success(Details(bill));

    public static IResult Deleted() => Envelopes.A.Success(message: BillsMessages.Deleted);

    public static IResult Summary(BillsSummary summary) =>
        Envelopes.A.Success(new Dictionary<string, object?>
        {
            ["total_despesas_mensais"] = summary.MonthlyExpenses,
            ["total_despesas_anuais"] = summary.AnnualExpenses,
            ["total_receitas_mensais"] = summary.MonthlyIncome,
            ["reserva_emergencia"] = new Dictionary<string, object?>
            {
                ["meta"] = summary.ReserveGoal,
                ["gasto_mensal_equivalente"] = summary.ReserveMonthlyEquivalent,
                ["meses_configurados"] = summary.ReserveMonths,
            },
        });

    public static IResult Error(string message, int statusCode) => Envelopes.A.Error(message, statusCode);

    private static Dictionary<string, object?> Details(BillDetails b) => new()
    {
        ["id"] = b.Id,
        ["descricao"] = b.Description,
        ["valor_previsto"] = Money(b.Amount),
        ["tipo_agendamento"] = b.Type,
        ["periodicidade"] = b.Periodicity,
        ["dia_execucao"] = b.Day,
        ["mes_execucao"] = b.Month,
        ["notificar_antes_dias"] = b.NotifyDaysBefore,
        ["subcategoria_id"] = b.SubcategoryId,
        ["subcategoria_nome"] = b.SubcategoryName,
        ["conta_id"] = b.AccountId,
        ["conta_nome"] = b.AccountName,
        ["data_inicio"] = Date(b.StartDate),
        ["incluir_na_reserva"] = b.IncludeInReserve,
        ["total_parcelas"] = b.TotalInstallments,
        ["parcelas_executadas"] = b.ExecutedInstallments,
    };

    private static double? Money(decimal? value) => value is null ? null : (double)value.Value;

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
