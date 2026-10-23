using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Caching;
using MeuSecretario.Application.Jobs;
using MeuSecretario.Domain.Jobs;
using MeuSecretario.Infrastructure.Caching;
using MeuSecretario.Infrastructure.Time;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace MeuSecretario.IntegrationTests.Jobs;

/// <summary>Lock <c>job_lock:*</c>, motor e sessão do check-in contra o Redis real (AC-013-03, 44 e 66).</summary>
[Trait("Category", "Docker")]
public sealed class JobsRedisTests(RedisFixture redis) : IClassFixture<RedisFixture>, IDisposable
{
    private sealed class QuietLog : IJobLog
    {
        public void Info(string job, string message)
        {
        }

        public void Warning(string job, string message)
        {
        }

        public void Failure(string job, string message)
        {
        }
    }

    private sealed class RecordingBot : IBotClient
    {
        public int Texts { get; private set; }

        public Task<bool> SendTextAsync(string number, string message, CancellationToken cancellationToken)
        {
            Texts++;
            return Task.FromResult(true);
        }

        public Task<bool> SendImageAsync(string number, ReadOnlyMemory<byte> image, string? caption, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class BlockingJob(string lockName, TaskCompletionSource started, TaskCompletionSource release) : IScheduledJob
    {
        public string Name => lockName;

        public string LockName => lockName;

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            started.SetResult();
            await release.Task;
        }
    }

    private sealed class BlockingMotorRepository(TaskCompletionSource started, TaskCompletionSource release) : MeuSecretario.Application.Jobs.IScheduleExecutionRepository
    {
        public async Task<IReadOnlyList<ScheduleInfo>> ListMotorCandidatesAsync(DateOnly today, CancellationToken cancellationToken)
        {
            started.TrySetResult();
            await release.Task;
            return [];
        }

        public Task<IReadOnlyList<ScheduleInfo>> ListCatchUpCandidatesAsync(DateOnly day, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScheduleInfo>>([]);

        public Task<ScheduleExecutionOutcome> ExecuteAsync(ScheduleExecutionRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedQueries(CheckinSnapshot snapshot) : ICheckinQueries
    {
        public Task<CheckinSnapshot> GetSnapshotAsync(int userId, DateOnly today, CancellationToken cancellationToken) => Task.FromResult(snapshot);
    }

    private sealed class FixedConfig(NotifiedUser user) : IJobNotificationConfigRepository
    {
        public Task<IReadOnlyList<NotifiedUser>> ListMorningUsersAsync(TimeOnly hour, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<NotifiedUser>>([]);

        public Task<IReadOnlyList<NotifiedUser>> ListCheckinUsersAsync(TimeOnly hour, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<NotifiedUser>>([user]);

        public Task<IReadOnlyList<BillsReminderUser>> ListBillsReminderUsersAsync(TimeOnly min, TimeOnly max, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BillsReminderUser>>([]);

        public Task<UserNotificationConfig> GetOrCreateAsync(int userId, CancellationToken cancellationToken) =>
            Task.FromResult(new UserNotificationConfig(true, true));
    }

    private readonly ConnectionMultiplexer _mux = ConnectionMultiplexer.Connect(redis.ConnectionString);
    private readonly RedisCache _cache = new(RedisConnectionOptions.Parse(redis.RedisUrl), TimeProvider.System, NullLogger<RedisCache>.Instance);
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

    public void Dispose() => _mux.Dispose();

    private JobRunner Runner() => new(_cache, new BrazilClock(TimeProvider.System), new QuietLog());

    [Fact]
    [Trait("AC", "013-03")]
    public async Task Lock_IsTheRawRunningValueWithFiveMinutes_SecondRunnerSkips_AndItIsReleased()
    {
        var name = "TESTE-" + _suffix;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Runner().RunAsync(new BlockingJob(name, started, release), CancellationToken.None);
        await started.Task;

        var db = _mux.GetDatabase();
        ((string?)await db.StringGetAsync(CacheKeys.JobLock(name))).ShouldBe("running");
        (await db.KeyTimeToLiveAsync(CacheKeys.JobLock(name)))!.Value.TotalSeconds.ShouldBeInRange(290, 300);
        var second = await Runner().RunAsync(new BlockingJob(name, new TaskCompletionSource(), new TaskCompletionSource()), CancellationToken.None);
        release.SetResult();
        var firstOutcome = await first;

        second.ShouldBe(JobRunOutcome.SkippedLocked);
        firstOutcome.ShouldBe(JobRunOutcome.Completed);
        (await db.KeyExistsAsync(CacheKeys.JobLock(name))).ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "013-66")]
    public async Task Motor_SecondCallWhileRunning_IsAlreadyRunning_AndTheFirstCompletes()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var motor = new ScheduleMotor(
            new BlockingMotorRepository(started, release), new ScheduleExecutor(new BlockingMotorRepository(started, release)),
            new RecordingBot(), _cache, new BrazilClock(TimeProvider.System), Runner(), new QuietLog(), new MeuSecretario.Infrastructure.Ops.OpsAlertChannel(false));
        var lockKey = CacheKeys.JobLock("motor-agendamentos");
        await _mux.GetDatabase().KeyDeleteAsync(lockKey);

        var first = motor.RunAsync(CancellationToken.None);
        await started.Task;
        var second = await motor.RunAsync(CancellationToken.None);
        release.SetResult();

        second.ShouldBe(MotorRunResult.AlreadyRunning);
        (await first).ShouldBe(MotorRunResult.Executed);
        (await _mux.GetDatabase().KeyExistsAsync(lockKey)).ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "013-44")]
    public async Task CheckinSession_IsWrittenInThePythonFormatWithOneHourTtl()
    {
        var whatsapp = "5511" + _suffix;
        var bill = new CheckinBill(7, "Salário", 5000m, 18, 1, 2, 3, "Conta", "Conta Corrente", "Renda", "Macro", "Renda", "FIXO", 0, null, new DateOnly(2026, 3, 18));
        var snapshot = new CheckinSnapshot([bill], [], [], []);
        var service = new NightlyCheckinService(new FixedQueries(snapshot), _cache, new BrazilClock(TimeProvider.System));

        var id = await service.CreateSessionAsync(whatsapp, [bill], CancellationToken.None);

        var db = _mux.GetDatabase();
        id.Length.ShouldBe(8);
        ((string?)await db.StringGetAsync(CacheKeys.NightlyCheckinActive(whatsapp))).ShouldBe($"\"{id}\"");
        (await db.KeyTimeToLiveAsync(CacheKeys.NightlyCheckinActive(whatsapp)))!.Value.TotalSeconds.ShouldBeInRange(3590, 3600);
        using var session = JsonDocument.Parse((string)(await db.StringGetAsync(CacheKeys.NightlyCheckin(whatsapp, id)))!);
        var item = session.RootElement.GetProperty("items").GetProperty("1");
        item.GetProperty("descricao").GetString().ShouldBe("Salário");
        item.GetProperty("valor_previsto").GetDouble().ShouldBe(5000);
        item.GetProperty("data_vencimento_real").GetString().ShouldBe("2026-03-18");
        item.GetProperty("status_text").GetString().ShouldNotBeNullOrEmpty();
        session.RootElement.GetProperty("total_items").GetInt32().ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "013-51")]
    public async Task InvoiceDedupeKeys_AreStoredAsTheJsonTrueWithSevenDays()
    {
        var key = CacheKeys.InvoiceClosed(Random.Shared.Next(100000, 999999));

        await _cache.SetJsonAsync(key, true, TimeSpan.FromDays(7));

        var db = _mux.GetDatabase();
        ((string?)await db.StringGetAsync(key)).ShouldBe("true");
        (await db.KeyTimeToLiveAsync(key))!.Value.TotalDays.ShouldBeInRange(6.9, 7);
        (await _cache.ExistsAsync(key)).ShouldBeTrue();
    }
}
