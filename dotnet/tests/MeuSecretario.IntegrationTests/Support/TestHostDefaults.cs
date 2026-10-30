using System.Runtime.CompilerServices;

namespace MeuSecretario.IntegrationTests.Support;

/// <summary>
/// Cada <c>WebApplicationFactory</c> sobe um host que, por padrão, observa o <c>appsettings.json</c> (um <c>inotify</c> por host no Linux) e esse
/// observador só é liberado quando o processo termina. Com ~2.250 testes e centenas de hosts, o runner do CI (limite de 128 instâncias)
/// estourava nos últimos testes: "The configured user limit (128) on the number of inotify instances has been reached". Nenhum teste
/// precisa de recarga de configuração, então ela fica desligada para todos (vale para qualquer fábrica, em qualquer classe).
/// </summary>
internal static class TestHostDefaults
{
    [ModuleInitializer]
    internal static void DisableConfigReloadOnChange() =>
        Environment.SetEnvironmentVariable("DOTNET_hostBuilder__reloadConfigOnChange", "false");
}
