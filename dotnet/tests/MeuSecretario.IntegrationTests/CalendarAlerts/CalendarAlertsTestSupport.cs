using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.CalendarAlerts;
using MeuSecretario.Domain.CalendarAlerts;
using MeuSecretario.Infrastructure.CalendarAlerts;
using MeuSecretario.Infrastructure.Database;
using MeuSecretario.IntegrationTests.Settings;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeuSecretario.IntegrationTests.CalendarAlerts;

/// <summary>Google falso: conectado ou não, ou lançando; conta as consultas.</summary>
internal sealed class FakeGoogleAccount : IGoogleAccountService
{
    private int _calls;

    public bool Connected { get; set; } = true;

    public bool Throws { get; set; }

    public int Calls => Volatile.Read(ref _calls);

    public Task<GoogleCredentialResult> GetCredentialsAsync(int userId, CancellationToken cancellationToken) =>
        Task.FromResult(GoogleCredentialResult.Of(GoogleCredentialStatus.Connected));

    public Task<bool> IsConnectedAsync(int userId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return Throws ? throw new InvalidOperationException("SEGREDO-do-google") : Task.FromResult(Connected);
    }

    public Task<bool> HasDriveScopeAsync(int userId, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<bool> RevokeAsync(int userId, CancellationToken cancellationToken) => Task.FromResult(true);
}

/// <summary>Repositório real com falhas injetáveis (a exceção carrega um texto que nunca pode chegar ao corpo).</summary>
internal sealed class FlakyRepository(ICalendarAlertConfigRepository inner) : ICalendarAlertConfigRepository
{
    public const string Secret = "SEGREDO-do-banco-5521";

    public bool FailGetOrCreate { get; set; }

    public bool FailUpdate { get; set; }

    public Task<CalendarAlertConfig> GetOrCreateAsync(int userId, CancellationToken cancellationToken) =>
        FailGetOrCreate ? throw new InvalidOperationException(Secret) : inner.GetOrCreateAsync(userId, cancellationToken);

    public Task<CalendarAlertConfig> UpdateAsync(int userId, bool? active, int? minutesBefore, CancellationToken cancellationToken) =>
        FailUpdate ? throw new InvalidOperationException(Secret) : inner.UpdateAsync(userId, active, minutesBefore, cancellationToken);

    public Task<IReadOnlyList<ActiveAlertUser>> ListUsersWithActiveAlertsAsync(CancellationToken cancellationToken) =>
        inner.ListUsersWithActiveAlertsAsync(cancellationToken);
}

/// <summary>API real em memória, PostgreSQL de teste, Google e repositório com falhas injetáveis.</summary>
internal sealed class CalendarAlertsEnv
{
    private readonly WebApplicationFactory<Program> _factory;

    public CalendarAlertsEnv(PostgresFixture postgres)
    {
        _factory = postgres.Api.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IGoogleAccountService>();
            services.AddSingleton<IGoogleAccountService>(Google);
            services.RemoveAll<ICalendarAlertConfigRepository>();
            services.AddSingleton<ICalendarAlertConfigRepository>(sp =>
            {
                Repository = new FlakyRepository(new CalendarAlertConfigRepository(sp.GetRequiredService<IDbConnectionFactory>()));
                return Repository;
            });
        }));
        Http = new SettingsEnv(postgres, _factory);
        _ = _factory.Services.GetRequiredService<ICalendarAlertConfigRepository>();
    }

    public FakeGoogleAccount Google { get; } = new();

    public FlakyRepository Repository { get; private set; } = default!;

    public SettingsEnv Http { get; }

    public async Task CloseAsync() => await _factory.DisposeAsync();
}
