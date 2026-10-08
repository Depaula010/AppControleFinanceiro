using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Jobs;
using MeuSecretario.Infrastructure.Time;
using MeuSecretario.UnitTests.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Jobs;

/// <summary>Registra as linhas de log dos jobs (sem I/O).</summary>
internal sealed class RecordingJobLog : IJobLog
{
    public List<string> Lines { get; } = [];

    public void Info(string job, string message) => Lines.Add($"INFO {job}: {message}");

    public void Warning(string job, string message) => Lines.Add($"WARNING {job}: {message}");

    public void Failure(string job, string message) => Lines.Add($"ERROR {job}: {message}");
}

/// <summary><see cref="TimeProvider"/> que pode andar para trás (o FakeTimeProvider não deixa).</summary>
internal sealed class MutableTimeProvider(DateTimeOffset initial) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = initial;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Relógio de São Paulo fixo (quarta, 18/03/2026 18:00), cache em memória, bot e log falsos.</summary>
internal sealed class JobsHarness
{
    /// <summary>Só para o TTL do cache em memória; o relógio dos jobs é <see cref="Moment"/>.</summary>
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 3, 18, 21, 0, 0, TimeSpan.Zero));

    public MutableTimeProvider Moment { get; } = new(new DateTimeOffset(2026, 3, 18, 21, 0, 0, TimeSpan.Zero));

    public IClock Clock { get; }

    public InMemoryCache Cache { get; }

    public IBotClient Bot { get; } = Substitute.For<IBotClient>();

    public RecordingJobLog Log { get; } = new();

    public JobRunner Runner { get; }

    public JobsHarness()
    {
        Clock = new BrazilClock(Moment);
        Cache = new InMemoryCache(Time);
        Runner = new JobRunner(Cache, Clock, Log);
        Bot.SendTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        Bot.SendImageAsync(Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    public void At(int hour, int minute = 0, int second = 0) =>
        Moment.Now = new DateTimeOffset(2026, 3, 18, 3, minute, second, TimeSpan.Zero).AddHours(hour);

    public void SetToday(int year, int month, int day, int hour = 12) =>
        Moment.Now = new DateTimeOffset(year, month, day, 3, 0, 0, TimeSpan.Zero).AddHours(hour);
}
