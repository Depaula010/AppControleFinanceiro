using System.Globalization;
using MeuSecretario.Domain.Jobs;

namespace MeuSecretario.Domain.Whatsapp.Analytics;

/// <summary>
/// Um número que o Gemini devolveu para <c>periodo_dias</c> ou <c>num_meses</c>, como o Python o enxerga:
/// <see cref="Text"/> é o <c>str()</c> que vai à legenda (<c>30</c>, <c>7.5</c>, <c>True</c>, <c>None</c>);
/// <see cref="Value"/> é o valor que <c>timedelta(days=...)</c> aceita (<c>null</c> onde o Python levanta <c>TypeError</c>: texto, lista, nulo).
/// </summary>
public sealed record ChartParam(string Text, double? Value);

public sealed record PieRow(string? Category, double? Total);

public sealed record BarRow(DateOnly Month, double Expenses, double Income);

public sealed record LineRow(DateOnly Date, double Cumulative);

/// <summary>Saldo inicial de todas as contas e as linhas com o acumulado do período (<c>generate_line_chart</c>).</summary>
public sealed record LineSeries(double InitialBalance, IReadOnlyList<LineRow> Rows);

/// <summary>Regras do <c>Gráfico de Gastos</c> (<c>analytics_intents.GraficoGastosIntent</c> e <c>chart_service.py</c>).</summary>
public static class ChartRules
{
    public const string NoData = "❌ Não há dados suficientes para gerar o gráfico no período solicitado.";

    public const string SendFailed = "❌ Não consegui enviar o gráfico. Tente novamente mais tarde.";

    public const string NoNumber = "❌ Não foi possível identificar seu número para enviar o gráfico.";

    /// <summary>Limite do <c>timedelta</c> do Python em dias.</summary>
    private const double MaxDays = 999_999_999;

    public static string PieCaption(ChartParam days) => $"📊 Gastos por Categoria - Últimos {days.Text} dias";

    public static string BarsCaption(ChartParam months) => $"📊 Evolução Mensal - Últimos {months.Text} meses";

    public static string LineCaption(ChartParam months) => $"📈 Evolução do Saldo - Últimos {months.Text} meses";

    public static string Sent(string caption) => $"✅ {caption}";

    /// <summary>Início do período da pizza: <c>hoje - timedelta(days=periodo)</c>; <c>null</c> onde o Python falha (e devolve sem dados).</summary>
    public static DateOnly? PieStart(DateOnly today, ChartParam days) => Start(today, days.Value);

    /// <summary>Início do período de barras e linha: <c>hoje - timedelta(days=meses * 30)</c>.</summary>
    public static DateOnly? MonthsStart(DateOnly today, ChartParam months) => Start(today, months.Value * 30);

    /// <summary>
    /// A subtração <c>date - timedelta</c> do CPython usa só <c>timedelta.days</c> (o piso dos dias): 7.5 dias volta 7.
    /// Fora do intervalo do <c>timedelta</c> ou do calendário o Python levanta <c>OverflowError</c>.
    /// </summary>
    private static DateOnly? Start(DateOnly today, double? days)
    {
        if (days is not { } value || !double.IsFinite(value) || Math.Abs(value) > MaxDays)
        {
            return null;
        }

        try
        {
            return today.AddDays(-(int)Math.Floor(value));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Os dados da pizza: sem linhas ⇒ <c>null</c>; só entram categoria não vazia e total positivo; nada sobrando ⇒ <c>null</c>.
    /// </summary>
    public static IReadOnlyList<(string Label, double Value)>? PieData(IReadOnlyList<PieRow> rows)
    {
        var data = rows
            .Where(r => !string.IsNullOrEmpty(r.Category) && r.Total is > 0)
            .Select(r => (r.Category!, r.Total!.Value))
            .ToList();
        return data.Count == 0 ? null : data;
    }

    /// <summary><c>row.mes.strftime('%b/%y')</c> com o locale do container do Flask (inglês): <c>Oct/26</c>.</summary>
    public static string MonthLabel(DateOnly month) =>
        month.ToString("MMM", CultureInfo.InvariantCulture) + "/" + (month.Year % 100).ToString("D2", CultureInfo.InvariantCulture);

    /// <summary><c>_format_currency</c>: <c>R$ 1.234,56</c> (o sinal, se houver, fica depois do <c>R$</c>, como no Python).</summary>
    public static string Currency(double value) =>
        "R$ " + PyNumber.Thousands(value).Replace(",", "X", StringComparison.Ordinal).Replace(".", ",", StringComparison.Ordinal).Replace("X", ".", StringComparison.Ordinal);

    /// <summary>O saldo de cada ponto: saldo inicial + acumulado.</summary>
    public static IReadOnlyList<(DateOnly Date, double Balance)> Balances(LineSeries series) =>
        [.. series.Rows.Select(r => (r.Date, series.InitialBalance + r.Cumulative))];
}
