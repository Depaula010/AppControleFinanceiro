using System.Globalization;
using System.Text;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Jobs;

namespace MeuSecretario.Application.Admin;

/// <param name="Name">Nome do usuário; vazio se o banco tiver nulo.</param>
/// <param name="WhatsappNumber">Número cadastrado; <c>null</c> ou vazio = sem número.</param>
public sealed record TestUser(string Name, string? WhatsappNumber);

public sealed record TestBill(string Description, decimal? ExpectedValue);

/// <summary>Leituras das rotas de teste (<c>test-notification</c> e <c>test-daily-briefing</c>); só consultas, sem escrita.</summary>
public interface IAdminTestDataRepository
{
    /// <summary><c>null</c> = usuário inexistente.</summary>
    Task<TestUser?> FindUserAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Agendamentos <b>ativos</b> do usuário com <c>dia_execucao</c> = <paramref name="dayOfMonth"/> (só o dia do mês, como o Flask).</summary>
    Task<IReadOnlyList<TestBill>> ListBillsForDayAsync(int userId, int dayOfMonth, int limit, CancellationToken cancellationToken);
}

/// <summary>
/// <c>usuario_id</c> das rotas de teste: ausente ou nulo = 1 (padrão do Flask); inteiro = o valor; outro tipo = inválido.
/// Quem não cabe em <see cref="int"/> ou não é positivo nunca é um usuário existente.
/// </summary>
internal static class TestUserId
{
    public static bool TryResolve(AdminField field, out long id)
    {
        switch (field.Kind)
        {
            case AdminFieldKind.Absent:
            case AdminFieldKind.Null:
                id = 1;
                return true;
            case AdminFieldKind.WholeNumber:
                id = field.Whole;
                return true;
            default:
                id = 0;
                return false;
        }
    }

    public static bool IsPossibleUser(long id) => id is >= 1 and <= int.MaxValue;
}

public abstract record SendTestNotificationResult
{
    private SendTestNotificationResult()
    {
    }

    public sealed record InvalidUserId : SendTestNotificationResult;

    public sealed record UserNotFound : SendTestNotificationResult;

    public sealed record NotConnected : SendTestNotificationResult;

    public sealed record InvalidType : SendTestNotificationResult;

    public sealed record SendFailed : SendTestNotificationResult;

    public sealed record Sent(string UserName, string Type, int UserId, string? WhatsappNumber) : SendTestNotificationResult;
}

/// <summary><c>POST /admin/test-notification</c> (spec 009 §4.15): usuário antes do tipo; só portas já existentes.</summary>
public sealed class SendTestNotification(
    IAdminTestDataRepository repository,
    IGoogleAccountService accounts,
    ICalendarClient calendar,
    IBotClient bot,
    IClock clock)
{
    public const string AgendaType = "agenda";
    public const string BillsType = "contas";
    private const int MaxLines = 5;

    public async Task<SendTestNotificationResult> ExecuteAsync(AdminField type, AdminField userId, CancellationToken cancellationToken)
    {
        if (!TestUserId.TryResolve(userId, out var rawId))
        {
            return new SendTestNotificationResult.InvalidUserId();
        }

        if (!TestUserId.IsPossibleUser(rawId))
        {
            return new SendTestNotificationResult.UserNotFound();
        }

        var id = (int)rawId;
        if (await repository.FindUserAsync(id, cancellationToken) is not { } user)
        {
            return new SendTestNotificationResult.UserNotFound();
        }

        // tipo ausente ou nulo = "agenda"; outro tipo JSON nunca é igual a "agenda" nem "contas" (Flask: "Tipo inválido").
        var kind = type.Kind is AdminFieldKind.Absent or AdminFieldKind.Null
            ? AgendaType
            : type.Kind == AdminFieldKind.Text ? type.Text! : null;
        string message;
        switch (kind)
        {
            case AgendaType:
                if (!await accounts.IsConnectedAsync(id, cancellationToken))
                {
                    return new SendTestNotificationResult.NotConnected();
                }

                message = await AgendaMessageAsync(id, cancellationToken);
                break;
            case BillsType:
                message = await BillsMessageAsync(id, cancellationToken);
                break;
            default:
                return new SendTestNotificationResult.InvalidType();
        }

        // Sem número cadastrado o bot recusa o envio: mesma resposta de qualquer outra falha de envio.
        return await bot.SendTextAsync(user.WhatsappNumber ?? string.Empty, message, cancellationToken)
            ? new SendTestNotificationResult.Sent(user.Name, kind, id, user.WhatsappNumber)
            : new SendTestNotificationResult.SendFailed();
    }

    private async Task<string> AgendaMessageAsync(int userId, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var events = await calendar.GetEventsInRangeAsync(userId, today, today, cancellationToken);

        var text = new StringBuilder("🧪 *TESTE - Agenda de Hoje*\n\n");
        text.Append(CultureInfo.InvariantCulture, $"📅 {events.Count} evento(s) encontrado(s)\n\n");
        for (var i = 0; i < Math.Min(MaxLines, events.Count); i++)
        {
            text.Append(CultureInfo.InvariantCulture, $"{i + 1}. {events[i].Summary}\n");
        }

        if (events.Count > MaxLines)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n... e mais {events.Count - MaxLines}");
        }

        return text.ToString();
    }

    private async Task<string> BillsMessageAsync(int userId, CancellationToken cancellationToken)
    {
        // Só o dia do mês de amanhã: ignora mês, periodicidade e parcelas, como o Flask.
        var tomorrow = clock.Today.AddDays(1);
        var bills = await repository.ListBillsForDayAsync(userId, tomorrow.Day, MaxLines, cancellationToken);

        var text = new StringBuilder("🧪 *TESTE - Contas de Amanhã*\n\n");
        text.Append(CultureInfo.InvariantCulture, $"💰 {bills.Count} conta(s) encontrada(s)\n\n");
        for (var i = 0; i < bills.Count; i++)
        {
            text.Append(CultureInfo.InvariantCulture, $"{i + 1}. {bills[i].Description}: {PtBrMoney.Format((double)(bills[i].ExpectedValue ?? 0m))}\n");
        }

        return text.ToString();
    }
}

public abstract record SendTestMonthlyReportResult
{
    private SendTestMonthlyReportResult()
    {
    }

    public sealed record InvalidMoment : SendTestMonthlyReportResult;

    public sealed record Done(ManualReportResult Report) : SendTestMonthlyReportResult;
}

/// <summary><c>POST /admin/test-monthly-report/{usuarioId}</c> (spec 009 §4.16): valida <c>momento</c> e delega à porta da 013.</summary>
public sealed class SendTestMonthlyReport(IMonthlyReportJob job)
{
    /// <summary>Query <c>momento</c> ausente = <c>INICIO_MES</c>; só <c>INICIO_MES</c> e <c>FIM_MES</c> (exatos) valem.</summary>
    public async Task<SendTestMonthlyReportResult> ExecuteAsync(int userId, string? moment, CancellationToken cancellationToken)
    {
        MonthlyMoment parsed;
        switch (moment ?? "INICIO_MES")
        {
            case "INICIO_MES":
                parsed = MonthlyMoment.InicioMes;
                break;
            case "FIM_MES":
                parsed = MonthlyMoment.FimMes;
                break;
            default:
                return new SendTestMonthlyReportResult.InvalidMoment();
        }

        return new SendTestMonthlyReportResult.Done(await job.SendManualAsync(userId, parsed, cancellationToken));
    }
}

public abstract record SendTestDailyBriefingResult
{
    private SendTestDailyBriefingResult()
    {
    }

    public sealed record InvalidUserId : SendTestDailyBriefingResult;

    public sealed record UserNotFound(long UserId) : SendTestDailyBriefingResult;

    public sealed record NoWhatsapp(int UserId) : SendTestDailyBriefingResult;

    public sealed record PrepareFailed : SendTestDailyBriefingResult;

    public sealed record EmptyMessage : SendTestDailyBriefingResult;

    public sealed record SendFailed : SendTestDailyBriefingResult;

    public sealed record Sent(int UserId, string WhatsappNumber, int TotalEvents, string Preview) : SendTestDailyBriefingResult;
}

/// <summary><c>POST /admin/test-daily-briefing</c> (spec 009 §4.17).</summary>
public sealed class SendTestDailyBriefing(IAdminTestDataRepository repository, IDailyBriefingJob job)
{
    private const int PreviewLength = 200;

    public async Task<SendTestDailyBriefingResult> ExecuteAsync(AdminField userId, CancellationToken cancellationToken)
    {
        if (!TestUserId.TryResolve(userId, out var rawId))
        {
            return new SendTestDailyBriefingResult.InvalidUserId();
        }

        if (!TestUserId.IsPossibleUser(rawId))
        {
            return new SendTestDailyBriefingResult.UserNotFound(rawId);
        }

        var id = (int)rawId;
        if (await repository.FindUserAsync(id, cancellationToken) is not { } user)
        {
            return new SendTestDailyBriefingResult.UserNotFound(id);
        }

        if (string.IsNullOrEmpty(user.WhatsappNumber))
        {
            return new SendTestDailyBriefingResult.NoWhatsapp(id);
        }

        var result = await job.SendTestAsync(id, cancellationToken);
        return result.Outcome switch
        {
            DailyBriefingTestOutcome.PrepareFailed => new SendTestDailyBriefingResult.PrepareFailed(),
            DailyBriefingTestOutcome.EmptyMessage => new SendTestDailyBriefingResult.EmptyMessage(),
            DailyBriefingTestOutcome.SendFailed => new SendTestDailyBriefingResult.SendFailed(),
            _ => new SendTestDailyBriefingResult.Sent(id, user.WhatsappNumber, result.TotalEvents, Preview(result.Message ?? string.Empty)),
        };
    }

    /// <summary><c>mensagem[:200] + "..."</c> se passar de 200 caracteres; o Python conta pontos de código, não unidades UTF-16.</summary>
    public static string Preview(string message)
    {
        var runes = message.EnumerateRunes().ToList();
        if (runes.Count <= PreviewLength)
        {
            return message;
        }

        var text = new StringBuilder();
        foreach (var rune in runes.Take(PreviewLength))
        {
            text.Append(rune.ToString());
        }

        return text.Append("...").ToString();
    }
}
