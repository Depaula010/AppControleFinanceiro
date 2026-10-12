using System.Net;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure;
using MeuSecretario.Infrastructure.Google;
using MeuSecretario.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace MeuSecretario.UnitTests.Infrastructure.Google;

public sealed class GoogleApiClientsRegistrationTests
{
    private static readonly Dictionary<string, string?> Env = new()
    {
        ["DATABASE_URL"] = "postgresql://u:p@h:5432/db",
        ["API_SECRET_KEY"] = "0123456789abcdef0123456789abcdef",
        ["ENCRYPTION_KEY"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
        ["GEMINI_API_KEY"] = "gemini-do-sistema",
        ["REDIS_URL"] = "redis://redis:6379",
    };

    [Fact]
    public async Task Container_ResolvesTheCalendarAndDriveClients()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(key => Env.GetValueOrDefault(key));
        services.AddLogging();
        services.AddSingleton(Substitute.For<ICache>());
        services.AddSingleton(Substitute.For<IBotClient>());
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ICalendarClient>().ShouldBeOfType<CalendarClient>();
        provider.GetRequiredService<IDriveClient>().ShouldBeOfType<DriveClient>();
        provider.GetRequiredService<IGoogleAccountService>().ShouldNotBeNull();
    }

    private sealed class Sink<T> : ILogger<T>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception) + " " + exception);
    }

    [Fact]
    [Trait("AC", "010-89")]
    public async Task ClientLogs_NeverCarryTokenUrlOrApiBody()
    {
        using var handler = new FakeHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, """{"files":[{"id":"p"}]}""")
            .Enqueue(HttpStatusCode.Forbidden, """{"error":{"code":403,"message":"corpo-secreto tok-segredo","errors":[{"reason":"forbidden"}]}}""");
        var account = Substitute.For<IGoogleAccountService>();
        account.HasDriveScopeAsync(7, Arg.Any<CancellationToken>()).Returns(true);
        account.GetCredentialsAsync(7, Arg.Any<CancellationToken>()).Returns(
            new GoogleCredentialResult(GoogleCredentialStatus.Connected, "tok-segredo", []));
        var sink = new Sink<DriveClient>();
        var client = new DriveClient(account, new FakeGoogleHttpClientFactory(handler), sink);

        var result = await client.UploadFileAsync(7, new byte[] { 1, 2, 3 }, "a.pdf", ["P"], "application/pdf", CancellationToken.None);

        result.Success.ShouldBeFalse();
        sink.Lines.ShouldNotBeEmpty();
        var all = string.Join('\n', sink.Lines);
        all.ShouldNotContain("tok-segredo");
        all.ShouldNotContain("corpo-secreto");
        all.ShouldNotContain("googleapis.com");
    }
}
