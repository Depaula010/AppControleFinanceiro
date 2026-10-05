using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.CalendarAlerts;

namespace MeuSecretario.Application.CalendarAlerts;

public enum AlertRejection
{
    NoParameters,
    NotConnected,
    InvalidMinutes,
    UpdateFailed,
}

public abstract record AlertConfigResult
{
    private AlertConfigResult()
    {
    }

    public sealed record Ok(string Message, CalendarAlertConfig Config) : AlertConfigResult;

    public sealed record Rejected(AlertRejection Reason) : AlertConfigResult;
}

/// <summary>Gravação comum às rotas 2, 3 e 4: erro do banco no <c>UPDATE</c> vira <see cref="AlertRejection.UpdateFailed"/> (400, como o Flask).</summary>
internal static class AlertConfigWriter
{
    public static async Task<AlertConfigResult> ApplyAsync(
        ICalendarAlertConfigRepository repository,
        ICalendarAlertDiagnostics diagnostics,
        int userId,
        bool? active,
        int? minutes,
        CancellationToken cancellationToken)
    {
        try
        {
            var config = await repository.UpdateAsync(userId, active, minutes, cancellationToken);
            return new AlertConfigResult.Ok(CalendarAlertRules.SuccessMessage(active, minutes), config);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.ConfigUpdateFailed(userId, ex);
            return new AlertConfigResult.Rejected(AlertRejection.UpdateFailed);
        }
    }
}

/// <summary><c>GET /calendar-alerts/config/&lt;id&gt;</c> (spec 014 §3.1).</summary>
public sealed class GetCalendarAlertConfig(ICalendarAlertConfigRepository repository)
{
    public Task<CalendarAlertConfig> ExecuteAsync(int userId, CancellationToken cancellationToken) =>
        repository.GetOrCreateAsync(userId, cancellationToken);
}

/// <summary><c>POST /calendar-alerts/config/&lt;id&gt;</c> (spec 014 §3.2).</summary>
public sealed class UpdateCalendarAlertConfig(
    ICalendarAlertConfigRepository repository,
    IGoogleAccountService google,
    ICalendarAlertDiagnostics diagnostics)
{
    public async Task<AlertConfigResult> ExecuteAsync(int userId, RawBody? body, CancellationToken cancellationToken)
    {
        var activeRaw = body?.Get("ativo") ?? RawValue.Absent;
        var minutesRaw = body?.Get("minutos_antes") ?? RawValue.Absent;
        var active = CalendarAlertRules.ParseActive(activeRaw);
        var minutes = CalendarAlertRules.ValidateMinutes(minutesRaw);

        if (IsNotInformed(activeRaw) && IsNotInformed(minutesRaw))
        {
            return new AlertConfigResult.Rejected(AlertRejection.NoParameters);
        }

        if (CalendarAlertRules.IsActivationRequested(activeRaw) && !await google.IsConnectedAsync(userId, cancellationToken))
        {
            return new AlertConfigResult.Rejected(AlertRejection.NotConnected);
        }

        await repository.GetOrCreateAsync(userId, cancellationToken);

        if (minutes.State == FieldState.Invalid)
        {
            return new AlertConfigResult.Rejected(AlertRejection.InvalidMinutes);
        }

        if (active.State == FieldState.Invalid)
        {
            return new AlertConfigResult.Rejected(AlertRejection.UpdateFailed);
        }

        return await AlertConfigWriter.ApplyAsync(
            repository,
            diagnostics,
            userId,
            active.State == FieldState.Valid ? active.Value : null,
            minutes.State == FieldState.Valid ? minutes.Value : null,
            cancellationToken);
    }

    private static bool IsNotInformed(RawValue value) => value is RawValue.AbsentValue or RawValue.NullValue;
}

/// <summary><c>POST /calendar-alerts/ativar/&lt;id&gt;</c> (spec 014 §3.3).</summary>
public sealed class ActivateCalendarAlerts(
    ICalendarAlertConfigRepository repository,
    IGoogleAccountService google,
    ICalendarAlertDiagnostics diagnostics)
{
    public async Task<AlertConfigResult> ExecuteAsync(int userId, RawBody? body, CancellationToken cancellationToken)
    {
        if (!await google.IsConnectedAsync(userId, cancellationToken))
        {
            return new AlertConfigResult.Rejected(AlertRejection.NotConnected);
        }

        var minutes = CalendarAlertRules.ValidateMinutes(body?.Get("minutos_antes") ?? RawValue.Absent);

        await repository.GetOrCreateAsync(userId, cancellationToken);

        if (minutes.State == FieldState.Invalid)
        {
            return new AlertConfigResult.Rejected(AlertRejection.InvalidMinutes);
        }

        return await AlertConfigWriter.ApplyAsync(
            repository, diagnostics, userId, true, minutes.State == FieldState.Valid ? minutes.Value : null, cancellationToken);
    }
}

/// <summary><c>POST /calendar-alerts/desativar/&lt;id&gt;</c> (spec 014 §3.4): sem consulta ao Google.</summary>
public sealed class DeactivateCalendarAlerts(ICalendarAlertConfigRepository repository, ICalendarAlertDiagnostics diagnostics)
{
    public async Task<AlertConfigResult> ExecuteAsync(int userId, CancellationToken cancellationToken)
    {
        await repository.GetOrCreateAsync(userId, cancellationToken);
        return await AlertConfigWriter.ApplyAsync(repository, diagnostics, userId, false, null, cancellationToken);
    }
}
