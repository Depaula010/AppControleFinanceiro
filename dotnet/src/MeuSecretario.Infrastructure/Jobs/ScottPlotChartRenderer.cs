using System.Globalization;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Jobs;
using MeuSecretario.Domain.Whatsapp.Analytics;
using ScottPlot;
using ScottPlot.TickGenerators;

namespace MeuSecretario.Infrastructure.Jobs;

/// <summary>
/// Gráficos em PNG: pizza de gastos por categoria (<c>chart_service.generate_pie_chart</c>, modo dicionário). Dados vazios ou todos
/// não positivos devolvem <c>null</c>. Equivalente visual ao matplotlib (paleta <c>Set3</c>), não idêntico (Q-013-5).
/// </summary>
public sealed class ScottPlotChartRenderer : IChartRenderer
{
    private static readonly string[] Palette =
    [
        "#8DD3C7", "#FFFFB3", "#BEBADA", "#FB8072", "#80B1D3", "#FDB462",
        "#B3DE69", "#FCCDE5", "#D9D9D9", "#BC80BD", "#CCEBC5", "#FFED6F",
    ];

    public byte[]? RenderPie(IReadOnlyList<(string Label, double Value)> data, int days)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Count == 0 || data.All(item => item.Value <= 0))
        {
            return null;
        }

        try
        {
            var total = data.Sum(item => item.Value);
            var slices = new List<PieSlice>();
            for (var i = 0; i < data.Count; i++)
            {
                var (label, value) = data[i];
                slices.Add(new PieSlice
                {
                    Value = value,
                    FillColor = Color.FromHex(Palette[i % Palette.Length]),
                    Label = string.Create(CultureInfo.InvariantCulture, $"{value / total * 100:F1}%"),
                    LabelFontSize = 18,
                    LegendText = $"{label}: {PtBrMoney.Format(value)}",
                });
            }

            using var plot = new Plot();
            var pie = plot.Add.Pie(slices);
            pie.DonutFraction = 0;
            pie.SliceLabelDistance = 0.6;
            plot.Title(string.Create(CultureInfo.InvariantCulture, $"Gastos por Categoria - Últimos {days} dias"), 22);
            plot.Axes.Frameless();
            plot.HideGrid();
            plot.ShowLegend(Alignment.MiddleRight);
            return plot.GetImageBytes(1200, 800, ImageFormat.Png);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // O Python devolve None em qualquer falha do gráfico (chart_service.py).
            return null;
        }
    }

    /// <summary>
    /// Barras de despesas e rendas por mês (<c>generate_bar_chart</c>): vermelho <c>#ff6b6b</c> e verde <c>#51cf66</c>, valor em reais
    /// sobre cada barra, grade horizontal e legenda. Equivalente visual ao matplotlib, não idêntico (spec 012 §13.1).
    /// </summary>
    public byte[]? RenderBars(IReadOnlyList<(string Label, double Expenses, double Income)> data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Count == 0)
        {
            return null;
        }

        try
        {
            const double width = 0.35;
            var expenses = new List<Bar>();
            var incomes = new List<Bar>();
            for (var i = 0; i < data.Count; i++)
            {
                expenses.Add(new Bar { Position = i - (width / 2), Value = data[i].Expenses, Size = width, FillColor = Color.FromHex("#ff6b6b"), Label = ChartRules.Currency(data[i].Expenses) });
                incomes.Add(new Bar { Position = i + (width / 2), Value = data[i].Income, Size = width, FillColor = Color.FromHex("#51cf66"), Label = ChartRules.Currency(data[i].Income) });
            }

            using var plot = new Plot();
            var expensePlot = plot.Add.Bars(expenses);
            expensePlot.LegendText = "Despesas";
            var incomePlot = plot.Add.Bars(incomes);
            incomePlot.LegendText = "Rendas";
            foreach (var barPlot in new[] { expensePlot, incomePlot })
            {
                barPlot.ValueLabelStyle.FontSize = 12;
                barPlot.ValueLabelStyle.ForeColor = Colors.Black;
            }

            plot.Axes.Bottom.SetTicks([.. Enumerable.Range(0, data.Count).Select(i => (double)i)], [.. data.Select(d => d.Label)]);
            plot.Axes.Margins(bottom: 0, top: 0.15);
            plot.Title("Evolução Mensal - Despesas vs Rendas", 22);
            plot.XLabel("Mês", 18);
            plot.YLabel("Valor (R$)", 18);
            plot.ShowLegend(Alignment.UpperRight);
            return plot.GetImageBytes(1800, 1050, ImageFormat.Png);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // O Python devolve None em qualquer falha do gráfico (chart_service.py).
            return null;
        }
    }

    /// <summary>
    /// Linha do saldo ao longo do tempo (<c>generate_line_chart</c>): azul <c>#339af0</c> com a área preenchida e a linha do zero
    /// tracejada em vermelho. Equivalente visual ao matplotlib, não idêntico (spec 012 §13.1).
    /// </summary>
    public byte[]? RenderLine(IReadOnlyList<(DateOnly Date, double Balance)> data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Count == 0)
        {
            return null;
        }

        try
        {
            var blue = Color.FromHex("#339af0");
            using var plot = new Plot();
            var xs = data.Select(d => d.Date.ToDateTime(TimeOnly.MinValue).ToOADate()).ToArray();
            var ys = data.Select(d => d.Balance).ToArray();
            var line = plot.Add.Scatter(xs, ys);
            line.Color = blue;
            line.LineWidth = 3;
            line.MarkerShape = MarkerShape.OpenCircle;
            line.MarkerSize = 7;
            line.FillY = true;
            line.FillYColor = blue.WithAlpha(0.3);

            var zero = plot.Add.HorizontalLine(0);
            zero.Color = Colors.Red.WithAlpha(0.5);
            zero.LineWidth = 1;
            zero.LinePattern = LinePattern.Dashed;

            plot.Axes.DateTimeTicksBottom();
            plot.Axes.Left.TickGenerator = new NumericAutomatic { LabelFormatter = ChartRules.Currency };
            plot.Title("Evolução do Saldo ao Longo do Tempo", 22);
            plot.XLabel("Data", 18);
            plot.YLabel("Saldo (R$)", 18);
            return plot.GetImageBytes(1800, 1050, ImageFormat.Png);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}
