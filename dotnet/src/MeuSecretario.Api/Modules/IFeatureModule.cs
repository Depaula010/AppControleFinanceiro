using System.Reflection;

namespace MeuSecretario.Api.Modules;

/// <summary>
/// Cada feature (001, 002, ...) implementa um módulo em sua própria pasta. Assim, features
/// desenvolvidas em paralelo nunca editam o mesmo arquivo (roadmap.md, "Execução em paralelo").
/// </summary>
public interface IFeatureModule
{
    void AddServices(IServiceCollection services);

    void MapEndpoints(IEndpointRouteBuilder app);
}

public static class FeatureModules
{
    public static IReadOnlyList<IFeatureModule> Discover(Assembly assembly) =>
        assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IFeatureModule).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => (IFeatureModule)Activator.CreateInstance(type)!)
            .ToList();
}
