using System.Reflection;
using System.Text.RegularExpressions;
using MeuSecretario.Api.Modules;
using MeuSecretario.Infrastructure.Jobs;
using NetArchTest.Rules;

namespace MeuSecretario.UnitTests.Architecture;

/// <summary>Regras da feature 013 (AC-013-80): sem relógio do sistema, sem Quartz fora da Infrastructure, módulo antes do da 004.</summary>
public partial class JobsArchitectureTests
{
    private static readonly Assembly Domain = typeof(MeuSecretario.Domain.AssemblyMarker).Assembly;
    private static readonly string[] Projects = ["MeuSecretario.Domain", "MeuSecretario.Application", "MeuSecretario.Infrastructure"];
    private static readonly Assembly Application = typeof(MeuSecretario.Application.AssemblyMarker).Assembly;

    [Fact]
    [Trait("AC", "013-80")]
    public void DomainAndApplication_DoNotDependOnQuartz()
    {
        foreach (var assembly in new[] { Domain, Application })
        {
            var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOn("Quartz").GetResult();

            result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
        }
    }

    [Fact]
    [Trait("AC", "013-80")]
    public void JobsCode_NeverReadsTheSystemClock()
    {
        var src = FindSourceRoot();
        var files = Projects
            .Select(project => Path.Combine(src, project, "Jobs"))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            .ToList();

        files.ShouldNotBeEmpty();
        foreach (var file in files)
        {
            ForbiddenClock().IsMatch(File.ReadAllText(file)).ShouldBeFalse($"{Path.GetFileName(file)} usa o relógio do sistema; use IClock.");
        }
    }

    [Fact]
    [Trait("AC", "013-80")]
    public void JobsModule_IsDiscovered_BeforeTheTransactionsModule()
    {
        var names = FeatureModules.Discover(typeof(Program).Assembly).Select(m => m.GetType().FullName!).ToList();

        names.ShouldContain("MeuSecretario.Api.Jobs.JobsModule");
        names.IndexOf("MeuSecretario.Api.Jobs.JobsModule").ShouldBeLessThan(names.IndexOf("MeuSecretario.Api.Transactions.TransactionsModule"));
    }

    [Fact]
    [Trait("AC", "013-80")]
    public void TheAdapter_IsTheOnlyQuartzJob()
    {
        typeof(ScheduledJobAdapter).GetInterfaces().ShouldContain(typeof(Quartz.IJob));
    }

    [GeneratedRegex(@"DateTime(Offset)?\.(Now|UtcNow)\b|TimeProvider\.System|DateTime\.Today")]
    private static partial Regex ForbiddenClock();

    private static string FindSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "MeuSecretario.Domain")))
        {
            dir = dir.Parent;
        }

        return dir is null ? throw new InvalidOperationException("Pasta src não encontrada.") : Path.Combine(dir.FullName, "src");
    }
}

public class ChartRendererTests
{
    [Fact]
    [Trait("AC", "013-75")]
    public void RenderPie_ProducesAPng_ForValidData()
    {
        var png = new ScottPlotChartRenderer().RenderPie([("Mercado", 120.5), ("Transporte", 30)], 28);

        png.ShouldNotBeNull();
        png.Take(4).ToArray().ShouldBe([0x89, 0x50, 0x4E, 0x47]);
        png.Length.ShouldBeGreaterThan(1000);
    }

    [Fact]
    [Trait("AC", "013-75")]
    public void RenderPie_ReturnsNull_WithoutDataOrWithNothingPositive()
    {
        var renderer = new ScottPlotChartRenderer();

        renderer.RenderPie([], 30).ShouldBeNull();
        renderer.RenderPie([("A", 0), ("B", -3)], 30).ShouldBeNull();
    }
}
