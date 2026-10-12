using System.Reflection;
using MeuSecretario.Api.Modules;
using NetArchTest.Rules;

namespace MeuSecretario.UnitTests.Architecture;

/// <summary>Regras de dependência de specs/foundation/architecture.md.</summary>
public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(MeuSecretario.Domain.AssemblyMarker).Assembly;
    private static readonly Assembly Application = typeof(MeuSecretario.Application.AssemblyMarker).Assembly;
    private static readonly Assembly Infrastructure = typeof(MeuSecretario.Infrastructure.AssemblyMarker).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;

    private static void ShouldNotDependOn(Assembly assembly, params string[] forbiddenNamespaces)
    {
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(forbiddenNamespaces).GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"{assembly.GetName().Name} depende de {string.Join(", ", forbiddenNamespaces)}: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Domain_DoesNotDependOnAnyOtherLayerOrFramework()
    {
        ShouldNotDependOn(
            Domain,
            "MeuSecretario.Application", "MeuSecretario.Infrastructure", "MeuSecretario.Api",
            "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore", "Npgsql", "StackExchange.Redis", "Dapper");
    }

    [Fact]
    public void Application_DependsOnlyOnDomain()
    {
        ShouldNotDependOn(
            Application,
            "MeuSecretario.Infrastructure", "MeuSecretario.Api",
            "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore", "Npgsql", "StackExchange.Redis", "Dapper");
    }

    [Fact]
    public void Application_DoesNotDependOnGoogleRedisOrHttp()
    {
        ShouldNotDependOn(Application, "Google", "StackExchange.Redis", "Npgsql", "System.Net.Http");
    }

    [Fact]
    public void DomainAndApplication_DoNotDependOnSerilog()
    {
        ShouldNotDependOn(Domain, "Serilog");
        ShouldNotDependOn(Application, "Serilog");
    }

    [Fact]
    public void Infrastructure_DoesNotDependOnApi()
    {
        ShouldNotDependOn(Infrastructure, "MeuSecretario.Api", "Microsoft.AspNetCore");
    }

    [Fact]
    public void Api_DoesNotUseDataAccessLibrariesDirectly()
    {
        ShouldNotDependOn(Api, "Npgsql", "Dapper", "StackExchange.Redis", "Microsoft.EntityFrameworkCore");
    }

    [Fact]
    public void AdminSetupRoutes_NeverTouchTheDatabase()
    {
        var result = Types.InAssembly(Api)
            .That().ResideInNamespace("MeuSecretario.Api.Admin").And().HaveNameStartingWith("SetupRoutes")
            .ShouldNot().HaveDependencyOnAny("MeuSecretario.Infrastructure", "Npgsql", "Dapper")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
        Types.InAssembly(Api).That().HaveNameStartingWith("SetupRoutes").GetTypes().ShouldNotBeEmpty();
    }

    [Fact]
    public void FeatureModules_AreDiscoveredFromTheApiAssembly()
    {
        FeatureModules.Discover(Api).ShouldNotBeEmpty();
    }
}
