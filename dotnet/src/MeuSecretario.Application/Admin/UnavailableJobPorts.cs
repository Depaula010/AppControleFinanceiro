using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Jobs;

namespace MeuSecretario.Application.Admin;

// Implementações padrão das portas dos jobs, registradas com TryAdd pela 009c (spec 009 plan §2.2; spec 013 §5.1).
// A 013 registra as reais (JobsServiceCollectionExtensions.AddJobs) e elas prevalecem; estas só existem para as rotas
// dependentes não ficarem sem porta se a feature que as implementa estiver ausente.

public sealed class UnavailableScheduleMotor : IScheduleMotor
{
    public Task<MotorRunResult> RunAsync(CancellationToken cancellationToken) => throw new FeatureNotAvailableException();
}

public sealed class UnavailableBillsNotificationJob : IBillsNotificationJob
{
    public Task<BillsJobResult> RunAsync(CancellationToken cancellationToken) => throw new FeatureNotAvailableException();
}

public sealed class UnavailableDailyBriefingJob : IDailyBriefingJob
{
    public Task<DailyBriefingRunResult> RunForCurrentMinuteAsync(CancellationToken cancellationToken) =>
        throw new FeatureNotAvailableException();

    public Task<DailyBriefingTestResult> SendTestAsync(int userId, CancellationToken cancellationToken) =>
        throw new FeatureNotAvailableException();
}

public sealed class UnavailableMonthlyReportJob : IMonthlyReportJob
{
    public Task<MonthlyReportRunResult> RunAsync(MonthlyMoment moment, CancellationToken cancellationToken) =>
        throw new FeatureNotAvailableException();

    public Task<ManualReportResult> SendManualAsync(int userId, MonthlyMoment moment, CancellationToken cancellationToken) =>
        throw new FeatureNotAvailableException();
}
