using MeuSecretario.Domain.Jobs;

namespace MeuSecretario.Application.Abstractions;

// Portas dos jobs consumidas pelas rotas admin da 009c (spec 013 §5.1; spec 009 §4.13 a 4.17). Implementadas pela 013.
// A 009c usa estes tipos e registra os seus Unavailable* com TryAdd; os reais vêm do JobsModule.

public enum MotorRunResult
{
    Executed,
    AlreadyRunning,
}

/// <summary><c>POST /admin/run-motor-agendamentos</c>: lock <c>job_lock:motor-agendamentos</c> e idempotência (desvio 013).</summary>
public interface IScheduleMotor
{
    Task<MotorRunResult> RunAsync(CancellationToken cancellationToken);
}

/// <param name="ProcessedAt">ISO de São Paulo (<c>agora_br.isoformat()</c>).</param>
public sealed record BillsJobResult(int UsersProcessed, int NotificationsSent, int Errors, string ProcessedAt);

/// <summary><c>POST /admin/trigger-bills-notifications</c> (<c>processar_contas_vencer</c>).</summary>
public interface IBillsNotificationJob
{
    Task<BillsJobResult> RunAsync(CancellationToken cancellationToken);
}

/// <param name="NoUsers">Sem usuário no horário: a rota responde <c>Nenhum usuario configurado para HH:mm</c>.</param>
/// <param name="Time"><c>HH:mm</c>.</param>
public sealed record DailyBriefingRunResult(int UsersProcessed, int Sent, int Errors, string Time, bool NoUsers);

public enum DailyBriefingTestOutcome
{
    Sent,
    PrepareFailed,
    EmptyMessage,
    SendFailed,
}

/// <param name="Message">Texto completo (a rota devolve só os 200 primeiros caracteres).</param>
public sealed record DailyBriefingTestResult(DailyBriefingTestOutcome Outcome, int TotalEvents, string? Message);

public interface IDailyBriefingJob
{
    /// <summary><c>trigger-daily-briefing</c>: ignora as flags de configuração (RN-013-25).</summary>
    Task<DailyBriefingRunResult> RunForCurrentMinuteAsync(CancellationToken cancellationToken);

    /// <summary><c>test-daily-briefing</c>.</summary>
    Task<DailyBriefingTestResult> SendTestAsync(int userId, CancellationToken cancellationToken);
}

/// <param name="Status"><c>enviado</c>, <c>sem_dados</c> ou <c>erro</c>.</param>
public sealed record MonthlyReportUserEntry(
    int UserId, string Name, string Status, string Message, string? ConfiguredHour, bool? ChartSent);

/// <param name="Type"><c>erro_geral</c> no erro geral; senão nulo e com usuário.</param>
public sealed record MonthlyReportErrorEntry(int? UserId, string? Name, string? Type, string Error);

/// <param name="ProcessedAt"><c>yyyy-MM-dd HH:mm:ss</c> de São Paulo.</param>
public sealed record MonthlyReportRunResult(
    string Moment,
    string ProcessedAt,
    int TotalUsers,
    int Sent,
    int Failed,
    IReadOnlyList<MonthlyReportUserEntry> Users,
    IReadOnlyList<MonthlyReportErrorEntry> Errors,
    double DurationSeconds);

/// <param name="Error"><c>Usuário não encontrado</c>, <c>Nenhuma transação encontrada no período</c>, <c>Falha ao enviar mensagem de texto</c> ou o motivo de uma exceção.</param>
public sealed record ManualReportResult(
    bool Success, string? Error, int UserId, string? Name, bool MessageSent, bool ChartSent, MonthlyReportData? Data);

public interface IMonthlyReportJob
{
    /// <summary><c>trigger-monthly-reports-*</c>: não confere o dia (RN-013-71).</summary>
    Task<MonthlyReportRunResult> RunAsync(MonthlyMoment moment, CancellationToken cancellationToken);

    /// <summary><c>test-monthly-report</c>.</summary>
    Task<ManualReportResult> SendManualAsync(int userId, MonthlyMoment moment, CancellationToken cancellationToken);
}

/// <summary>Pizza em PNG (RN-013-75). <c>null</c> sem dados válidos.</summary>
public interface IChartRenderer
{
    byte[]? RenderPie(IReadOnlyList<(string Label, double Value)> data, int days);
}
