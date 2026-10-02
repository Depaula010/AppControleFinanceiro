using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.UnitTests.TestSupport;

/// <summary>Executa um <see cref="IResult"/> fora do host e devolve status e corpo.</summary>
public static class ResultRunner
{
    public static async Task<(int Status, string Json)> Run(IResult result)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();

        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }
}
