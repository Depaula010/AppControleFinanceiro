namespace MeuSecretario.Api.Dashboard;

/// <summary>Logs da feature 006. Só o id do usuário e o tipo da exceção: nunca valores, descrições nem token (RN-006-11).</summary>
internal static partial class DashboardLog
{
    [LoggerMessage(EventId = 6001, Level = LogLevel.Error, Message = "Erro ao carregar resumo do dashboard (usuário {UserId}): {Error}")]
    public static partial void SummaryFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 6002, Level = LogLevel.Error, Message = "Erro ao carregar dados dos gráficos (usuário {UserId}): {Error}")]
    public static partial void ChartsFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 6003, Level = LogLevel.Error, Message = "Erro ao carregar transações recentes (usuário {UserId}): {Error}")]
    public static partial void RecentFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 6004, Level = LogLevel.Error, Message = "Erro ao buscar alertas do dashboard (usuário {UserId}): {Error}")]
    public static partial void AlertsFailed(ILogger logger, int userId, string error);
}
