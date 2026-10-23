using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Infrastructure.Whatsapp;

namespace MeuSecretario.Api.Whatsapp;

/// <summary>
/// Registro da sub-entrega 012d (contas fixas, vencimentos, contas atrasadas e resposta ao check-in noturno). Arquivo próprio para que
/// o <see cref="WhatsAppModule"/> só ganhe uma linha.
/// </summary>
internal static class FixedBillsWhatsAppModule
{
    public static void AddFixedBillsServices(IServiceCollection services)
    {
        services.AddSingleton<ICheckinDiagnostics, CheckinDiagnostics>();
        services.AddScoped<IWhatsAppFixedBillsReader, FixedBillsReader>();
        services.AddScoped<IWhatsAppIntent, FixedBillsIntent>();
        services.AddScoped<IWhatsAppIntent, AllBillsIntent>();
        services.AddScoped<IWhatsAppIntent, DueTodayIntent>();
        services.AddScoped<IWhatsAppIntent, DueTomorrowIntent>();
        services.AddScoped<IWhatsAppIntent, DueWeekIntent>();
        services.AddScoped<IWhatsAppIntent, OverdueIntent>();

        services.AddScoped<CheckinDialogStore>();

        // O módulo base registra o responsável padrão (TryAdd, "não tratada"); esta é a implementação real e vence por vir depois.
        services.AddScoped<INightlyCheckinResponder, CheckinResponder>();
    }
}

/// <summary>Logs da 012d (EventIds 12400 a 12499): só o tipo da exceção, nunca texto do usuário nem valores.</summary>
internal static partial class BillsLog
{
    [LoggerMessage(EventId = 12400, Level = LogLevel.Warning, Message = "Falha ao confirmar contas do check-in noturno: {Error}")]
    public static partial void CheckinReplyFailed(ILogger logger, string error);

    [LoggerMessage(EventId = 12401, Level = LogLevel.Warning, Message = "Item do check-in noturno não confirmado: agendamento {ScheduleId}, motivo {Reason}")]
    public static partial void CheckinItemNotConfirmed(ILogger logger, int scheduleId, string reason);
}

internal sealed class CheckinDiagnostics(ILogger<WhatsAppModule> logger) : ICheckinDiagnostics
{
    public void ReplyFailed(string errorType) => BillsLog.CheckinReplyFailed(logger, errorType);

    public void ItemNotConfirmed(int scheduleId, string reason) => BillsLog.CheckinItemNotConfirmed(logger, scheduleId, reason);
}
