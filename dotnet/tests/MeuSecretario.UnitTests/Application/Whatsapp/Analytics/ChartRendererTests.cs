using MeuSecretario.Infrastructure.Jobs;

namespace MeuSecretario.UnitTests.Application.Whatsapp.Analytics;

/// <summary>Barras e linha do <see cref="ScottPlotChartRenderer"/> (AC-012-703): PNG válido; sem dados, nada.</summary>
public class ChartRendererTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly (string, double, double)[] Months =
    [
        ("May/26", 1200.5, 5000), ("Jun/26", 1800, 5000), ("Jul/26", 2100.25, 0), ("Aug/26", 0, 5200.5), ("Sep/26", 1999.99, 5000), ("Oct/26", 420.5, 0),
    ];

    private static readonly (DateOnly, double)[] Balance =
    [
        (new DateOnly(2026, 8, 2), 1450.25), (new DateOnly(2026, 8, 15), 6450.25), (new DateOnly(2026, 9, 1), 4500.75), (new DateOnly(2026, 10, 5), -79.75),
    ];

    [Fact]
    [Trait("AC", "012-703")]
    public void RenderBars_ProducesAPng()
    {
        var png = new ScottPlotChartRenderer().RenderBars(Months);

        png.ShouldNotBeNull();
        png.Take(8).ShouldBe(PngSignature);
        Dump("barras", png);
    }

    [Fact]
    [Trait("AC", "012-703")]
    public void RenderLine_ProducesAPng_AlsoWithASinglePoint()
    {
        var renderer = new ScottPlotChartRenderer();

        var png = renderer.RenderLine(Balance);
        var single = renderer.RenderLine(Balance[..1]);

        png.ShouldNotBeNull();
        png.Take(8).ShouldBe(PngSignature);
        single.ShouldNotBeNull();
        single.Take(8).ShouldBe(PngSignature);
        Dump("linha", png);
    }

    [Fact]
    [Trait("AC", "012-703")]
    public void BarsAndLine_WithoutData_ReturnNull()
    {
        var renderer = new ScottPlotChartRenderer();

        renderer.RenderBars([]).ShouldBeNull();
        renderer.RenderLine([]).ShouldBeNull();
    }

    /// <summary>Para olhar o desenho: <c>CHART_DUMP_DIR=pasta dotnet test --filter ChartRendererTests</c>.</summary>
    private static void Dump(string name, byte[] png)
    {
        var directory = Environment.GetEnvironmentVariable("CHART_DUMP_DIR");
        if (!string.IsNullOrEmpty(directory))
        {
            File.WriteAllBytes(Path.Combine(directory, name + ".png"), png);
        }
    }
}
