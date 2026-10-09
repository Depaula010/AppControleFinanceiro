using System.Globalization;
using MeuSecretario.Domain.Common;
using MeuSecretario.Domain.Jobs;

namespace MeuSecretario.Domain.Whatsapp.Analytics;

/// <summary>Conta fixa pendente no mês (<c>forecast_service.get_forecast_data</c>, consulta 5).</summary>
public sealed record PendingBill(string Description, double Value, int? Day, string? AccountType, string? GroupName)
{
    public bool IsIncome => GroupName == "Renda";

    public bool IsCard => AccountType == "Cartão de Crédito";

    public bool IsNormalExpense => !IsIncome && !IsCard;
}

/// <summary>O dicionário de <c>get_forecast_data</c> que entra no prompt (as chaves sem uso no Flask não são lidas).</summary>
public sealed record ForecastData(
    DateOnly Today,
    int DaysInMonth,
    int HistoryMonths,
    IReadOnlyList<MonthTotal> MonthlyTotals,
    IReadOnlyList<FixedCharge> FixedCharges,
    double SpentToDate,
    IReadOnlyList<PendingBill> Pending)
{
    public int Day => Today.Day;
}

/// <summary><c>calculate_simple_forecast</c> (média móvel, taxa diária e pendentes), com os arredondamentos de 2 casas do Python.</summary>
public sealed record ForecastProjection(
    double FinalProjection,
    double HistoricalAverage,
    double PendingTotal,
    double NormalPending,
    double CardPending,
    double IncomePending,
    double SpentToDate,
    double DailyRate)
{
    /// <summary>
    /// <c>null</c> quando não há gasto no histórico: o Flask devolve um dicionário sem as chaves <c>gasto_ate_hoje</c>,
    /// <c>taxa_diaria_atual</c>... e o prompt quebra com <c>KeyError</c> (desvio 158, proposto: reproduzido).
    /// </summary>
    public static ForecastProjection? Calculate(ForecastData data)
    {
        if (data.MonthlyTotals.Count == 0)
        {
            return null;
        }

        var average = SumInOrder(data.MonthlyTotals.Select(m => m.Total)) / data.MonthlyTotals.Count;
        var spent = data.SpentToDate;
        var day = data.Day;

        var dailyRate = spent / day;
        var linear = dailyRate * data.DaysInMonth;

        var normal = SumInOrder(data.Pending.Where(c => c.IsNormalExpense).Select(c => c.Value));
        var card = SumInOrder(data.Pending.Where(c => c.IsCard).Select(c => c.Value));
        var income = SumInOrder(data.Pending.Where(c => c.IsIncome).Select(c => c.Value));
        var pendingTotal = normal + card;

        var final = Math.Max(Math.Max(linear, spent + pendingTotal), average * 0.85);

        return new ForecastProjection(
            PyRound.Round2(final),
            PyRound.Round2(average),
            PyRound.Round2(pendingTotal),
            PyRound.Round2(normal),
            PyRound.Round2(card),
            PyRound.Round2(income),
            PyRound.Round2(spent),
            PyRound.Round2(spent / day));
    }

    /// <summary><c>sum()</c> do Python: soma da esquerda para a direita (a ordem muda os últimos bits do double).</summary>
    private static double SumInOrder(IEnumerable<double> values)
    {
        var total = 0.0;
        foreach (var value in values)
        {
            total += value;
        }

        return total;
    }
}

/// <summary>Texto de <c>Previsão de Gastos</c> (<c>forecast_service.generate_forecast_insights</c>).</summary>
public static class ForecastReport
{
    public const string Header = "📈 *Previsão de Gastos*\n\n";

    public static string Footer(ForecastData data) =>
        string.Create(CultureInfo.InvariantCulture, $"\n\n💬 _Baseado em: histórico de {data.HistoryMonths} meses + contas fixas_");

    private static string Money(double value) => AnalyticsReport.Money(value);

    private static string Lines(IEnumerable<PendingBill> bills) =>
        string.Join('\n', bills.Select(c => $"- {c.Description}: R$ {Money(c.Value)} (dia {(c.Day is { } d ? d.ToString(CultureInfo.InvariantCulture) : "None")})"));

    public static string Prompt(ForecastData data, ForecastProjection projection)
    {
        var monthName = MonthlyReportRules.MonthNames[data.Today.Month];

        var history = string.Join('\n', data.MonthlyTotals.Take(6).Select(g => $"- {g.Month}: R$ {Money(g.Total)}"));

        var normal = data.Pending.Where(c => c.IsNormalExpense).ToList();
        var card = data.Pending.Where(c => c.IsCard).ToList();
        var income = data.Pending.Where(c => c.IsIncome).ToList();

        var pending = string.Empty;
        if (normal.Count > 0)
        {
            pending += "💸 Despesas:\n" + Lines(normal) + "\n";
        }

        if (card.Count > 0)
        {
            pending += "💳 Cartão (na fatura):\n" + Lines(card) + "\n";
        }

        if (income.Count > 0)
        {
            pending += "💰 Receitas:\n" + Lines(income);
        }

        if (pending.Length == 0)
        {
            pending = "- Nenhuma conta pendente";
        }

        var fixedCharges = data.FixedCharges.Count > 0
            ? string.Join('\n', data.FixedCharges.Take(5).Select(c => $"- {c.Description}: R$ {Money(c.Value)} ({c.Periodicity})"))
            : "- Nenhuma conta fixa cadastrada";

        var date = data.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var day = data.Day.ToString(CultureInfo.InvariantCulture);
        var daysInMonth = data.DaysInMonth.ToString(CultureInfo.InvariantCulture);
        var months = data.HistoryMonths.ToString(CultureInfo.InvariantCulture);

        var text = $"""

            Você é um assistente financeiro. Analise os dados e gere uma PROJEÇÃO DE GASTOS FUTUROS.

            *DADOS DO USUÁRIO:*

            📅 *Contexto Temporal:*
            - Data atual: {date} (dia {day} de {daysInMonth})
            - Mês atual: {monthName}

            💰 *Situação Atual:*
            - Gastos até hoje: R$ {Money(projection.SpentToDate)}
            - Taxa média diária: R$ {Money(projection.DailyRate)}
            - Despesas pendentes: R$ {Money(projection.NormalPending)}
            - Cartão pendente: R$ {Money(projection.CardPending)}
            - Receitas pendentes: R$ {Money(projection.IncomePending)}

            📊 *Histórico (últimos {months} meses):*
            {history}
            - Média mensal: R$ {Money(projection.HistoricalAverage)}

            📋 *Contas Pendentes Este Mês:*
            {pending}

            🔧 *Contas Fixas Mensais:*
            {fixedCharges}

            📈 *Projeção Calculada:*
            - Projeção final {monthName}: R$ {Money(projection.FinalProjection)}

            ---

            *INSTRUÇÕES:*

            Gere um relatório de PROJEÇÃO DE GASTOS com as seguintes seções:

            1. 📈 Projeção {monthName} (3-4 linhas):
               - Gastos até agora (dia {day})
               - Projeção final do mês (separar despesas normais e cartão se relevante)
               - Liste contas pendentes principais (IMPORTANTE: diferenciar despesas normais de cartão)
               - Se houver receitas pendentes, calcular o saldo líquido previsto
               - Base de cálculo (média + padrão)

               NOTA IMPORTANTE: Despesas de cartão já estão debitadas na fatura, são lembretes informativos.

            2. 🔍 Análise de Tendências (2-3 pontos):
               - Compare com média histórica
               - Identifique se está acima/abaixo do esperado
               - Destaque mudanças de padrão

            3. ⚠️ Alertas (se aplicável):
               - Se projeção está muito acima da média
               - Contas grandes pendentes
               - Padrão de gastos preocupante

            4. 💡 Recomendações (2-3 sugestões):
               - Baseado na projeção
               - Categorias para controlar
               - Meta de gastos até fim do mês

            *FORMATO:*
            - Use emojis e formatação clara
            - Seja objetivo (máximo 12 linhas)
            - Use valores reais dos dados
            - Foque em insights acionáveis
            - Use asteriscos simples * para negrito (ex: *texto*), NÃO use asteriscos duplos **

            """;
        return text.Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
