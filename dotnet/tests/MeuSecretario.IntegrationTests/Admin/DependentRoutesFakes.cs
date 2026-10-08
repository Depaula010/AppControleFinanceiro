using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Admin;
using MeuSecretario.Domain.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeuSecretario.IntegrationTests.Admin;

/// <summary>Portas dos jobs e leituras de teste falsas para as rotas da 009c: contam chamadas e podem falhar com sentinela.</summary>
internal sealed class DependentFakes
{
    public const string Sentinel = "SEGREDO-SENTINELA-009c";

    public FakeMotor Motor { get; } = new();

    public FakeBills Bills { get; } = new();

    public FakeBriefing Briefing { get; } = new();

    public FakeMonthly Monthly { get; } = new();

    public FakeTestData TestData { get; } = new();

    public FakeBot Bot { get; } = new();

    public FakeGoogle Google { get; } = new();

    public FakeCalendar Calendar { get; } = new();

    public int JobCalls => Motor.Calls + Bills.Calls + Briefing.Calls + Monthly.Calls;

    public void Install(IServiceCollection services)
    {
        services.RemoveAll<IScheduleMotor>();
        services.AddSingleton<IScheduleMotor>(Motor);
        services.RemoveAll<IBillsNotificationJob>();
        services.AddSingleton<IBillsNotificationJob>(Bills);
        services.RemoveAll<IDailyBriefingJob>();
        services.AddSingleton<IDailyBriefingJob>(Briefing);
        services.RemoveAll<IMonthlyReportJob>();
        services.AddSingleton<IMonthlyReportJob>(Monthly);
        services.RemoveAll<IAdminTestDataRepository>();
        services.AddSingleton<IAdminTestDataRepository>(TestData);
        services.RemoveAll<IBotClient>();
        services.AddSingleton<IBotClient>(Bot);
        services.RemoveAll<IGoogleAccountService>();
        services.AddSingleton<IGoogleAccountService>(Google);
        services.RemoveAll<ICalendarClient>();
        services.AddSingleton<ICalendarClient>(Calendar);
    }

    /// <summary>As portas ainda sem implementação (013 ausente): os <c>Unavailable*</c> da 009c.</summary>
    public static void InstallUnavailable(IServiceCollection services)
    {
        services.RemoveAll<IScheduleMotor>();
        services.AddScoped<IScheduleMotor, UnavailableScheduleMotor>();
        services.RemoveAll<IBillsNotificationJob>();
        services.AddScoped<IBillsNotificationJob, UnavailableBillsNotificationJob>();
        services.RemoveAll<IDailyBriefingJob>();
        services.AddScoped<IDailyBriefingJob, UnavailableDailyBriefingJob>();
        services.RemoveAll<IMonthlyReportJob>();
        services.AddScoped<IMonthlyReportJob, UnavailableMonthlyReportJob>();
    }

    internal sealed class FakeMotor : IScheduleMotor
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public MotorRunResult Result { get; set; } = MotorRunResult.Executed;

        public Exception? Failure { get; set; }

        public Task<MotorRunResult> RunAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Failure is null ? Task.FromResult(Result) : throw Failure;
        }
    }

    internal sealed class FakeBills : IBillsNotificationJob
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Exception? Failure { get; set; }

        public Task<BillsJobResult> RunAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Failure is null
                ? Task.FromResult(new BillsJobResult(3, 2, 1, "2026-10-04T09:00:00.123456-03:00"))
                : throw Failure;
        }
    }

    internal sealed class FakeBriefing : IDailyBriefingJob
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Exception? Failure { get; set; }

        public DailyBriefingRunResult Run { get; set; } = new(2, 1, 1, "07:30", false);

        public DailyBriefingTestResult Test { get; set; } = new(DailyBriefingTestOutcome.Sent, 2, "Bom dia!");

        public int? TestedUser { get; private set; }

        public Task<DailyBriefingRunResult> RunForCurrentMinuteAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Failure is null ? Task.FromResult(Run) : throw Failure;
        }

        public Task<DailyBriefingTestResult> SendTestAsync(int userId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            TestedUser = userId;
            return Failure is null ? Task.FromResult(Test) : throw Failure;
        }
    }

    internal sealed class FakeMonthly : IMonthlyReportJob
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Exception? Failure { get; set; }

        public MonthlyReportRunResult Run { get; set; } = new(
            "INICIO_MES", "2026-10-01 08:00:00", 3, 1, 1,
            [
                new MonthlyReportUserEntry(1, "Ana", "enviado", "Relatório enviado com sucesso", "08:00:00", true),
                new MonthlyReportUserEntry(2, "Bia", "sem_dados", "Nenhuma transação no período", null, null),
                new MonthlyReportUserEntry(3, "Caio", "erro", "Falha ao enviar mensagem de texto", null, null),
            ],
            [new MonthlyReportErrorEntry(3, "Caio", null, "Falha ao enviar mensagem de texto")],
            1.5);

        public ManualReportResult Manual { get; set; } = new(false, "Usuário não encontrado", 5, null, false, false, null);

        public MonthlyMoment? RunMoment { get; private set; }

        public (int UserId, MonthlyMoment Moment)? ManualCall { get; private set; }

        public Task<MonthlyReportRunResult> RunAsync(MonthlyMoment moment, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            RunMoment = moment;
            return Failure is null ? Task.FromResult(Run with { Moment = MonthlyReportRules.MomentName(moment) }) : throw Failure;
        }

        public Task<ManualReportResult> SendManualAsync(int userId, MonthlyMoment moment, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            ManualCall = (userId, moment);
            return Failure is null ? Task.FromResult(Manual) : throw Failure;
        }
    }

    internal sealed class FakeTestData : IAdminTestDataRepository
    {
        public Dictionary<int, TestUser> Users { get; } = new() { [1] = new TestUser("Admin", "5511000000001"), [7] = new TestUser("Ana", "5511999990007") };

        public List<TestBill> Bills { get; } = [];

        public int? BillsDay { get; private set; }

        public Exception? Failure { get; set; }

        public Task<TestUser?> FindUserAsync(int userId, CancellationToken cancellationToken) =>
            Failure is null ? Task.FromResult(Users.GetValueOrDefault(userId)) : throw Failure;

        public Task<IReadOnlyList<TestBill>> ListBillsForDayAsync(int userId, int dayOfMonth, int limit, CancellationToken cancellationToken)
        {
            BillsDay = dayOfMonth;
            return Task.FromResult<IReadOnlyList<TestBill>>([.. Bills.Take(limit)]);
        }
    }

    internal sealed class FakeBot : IBotClient
    {
        public bool Result { get; set; } = true;

        public List<(string Number, string Message)> Sent { get; } = [];

        public Task<bool> SendTextAsync(string number, string message, CancellationToken cancellationToken)
        {
            Sent.Add((number, message));
            return Task.FromResult(Result);
        }

        public Task<bool> SendImageAsync(string number, ReadOnlyMemory<byte> image, string? caption, CancellationToken cancellationToken) =>
            Task.FromResult(Result);
    }

    internal sealed class FakeGoogle : IGoogleAccountService
    {
        public bool Connected { get; set; } = true;

        public Task<GoogleCredentialResult> GetCredentialsAsync(int userId, CancellationToken cancellationToken) =>
            Task.FromResult(GoogleCredentialResult.Of(GoogleCredentialStatus.NotConnected));

        public Task<bool> IsConnectedAsync(int userId, CancellationToken cancellationToken) => Task.FromResult(Connected);

        public Task<bool> HasDriveScopeAsync(int userId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> RevokeAsync(int userId, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    internal sealed class FakeCalendar : ICalendarClient
    {
        public List<string> Titles { get; } = [];

        public DateOnly? RequestedDay { get; private set; }

        public Task<IReadOnlyList<CalendarEvent>> GetEventsInRangeAsync(int userId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken)
        {
            RequestedDay = startDate;
            return Task.FromResult<IReadOnlyList<CalendarEvent>>([.. Titles.Select(t =>
                new CalendarEvent("id", t, "2026-10-04T10:00:00-03:00", "2026-10-04T11:00:00-03:00", string.Empty, string.Empty, false, null))]);
        }

        public Task<IReadOnlyList<CalendarInfo>> ListSelectedCalendarsAsync(int userId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<CalendarEvent>> ListEventsAsync(int userId, ListEventsQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CalendarEvent?> GetEventAsync(int userId, string eventId, string calendarId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string> InsertEventAsync(int userId, NewCalendarEvent newEvent, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> UpdateEventAsync(int userId, string eventId, string calendarId, EventUpdate update, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CalendarDeleteResult> DeleteEventAsync(int userId, string eventId, string calendarId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<CalendarEvent>> FindEventsByTitleAsync(int userId, string title, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
