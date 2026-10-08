using System.Globalization;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Jobs;
using ScottPlot;

namespace MeuSecretario.Infrastructure.Jobs;

/// <summary>
/// Pizza de gastos por categoria (<c>chart_service.generate_pie_chart</c>, modo dicionário). Dados vazios ou todos
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
}
