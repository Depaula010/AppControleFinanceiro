namespace MeuSecretario.Application.Admin;

/// <summary>Linha de <c>notificationconfigs</c> que as rotas admin expõem. <c>ResumoMatinalHora</c> em <c>HH:mm</c>.</summary>
public sealed record NotificationConfigRow(bool ResumoMatinalAtivo, string ResumoMatinalHora, bool? AlertasFinanceirosAtivos);

public interface IAdminNotificationConfigRepository
{
    /// <summary>Garante a linha com os padrões do banco (<c>ON CONFLICT DO NOTHING</c>) e a devolve; <c>null</c> = usuário inexistente (FK).</summary>
    Task<NotificationConfigRow?> GetOrCreateAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Grava <c>alertas_financeiros_ativos</c> (a linha já existe); devolve o valor gravado, <c>null</c> = usuário inexistente.</summary>
    Task<bool?> SetFinancialAlertsAsync(int userId, bool active, CancellationToken cancellationToken);
}

public abstract record GetNotificationConfigResult
{
    private GetNotificationConfigResult()
    {
    }

    public sealed record UserNotFound : GetNotificationConfigResult;

    public sealed record Found(NotificationConfigRow Row) : GetNotificationConfigResult;
}

/// <summary><c>GET /admin/get-notification-config/{usuarioId}</c> (RN-009-17): cria a linha padrão se não existir.</summary>
public sealed class GetNotificationConfig(IAdminNotificationConfigRepository repository)
{
    public async Task<GetNotificationConfigResult> ExecuteAsync(int userId, CancellationToken cancellationToken) =>
        await repository.GetOrCreateAsync(userId, cancellationToken) is { } row
            ? new GetNotificationConfigResult.Found(row)
            : new GetNotificationConfigResult.UserNotFound();
}

public abstract record SetFinancialAlertsResult
{
    private SetFinancialAlertsResult()
    {
    }

    /// <summary><c>usuario_id</c> falso (ausente, nulo, 0, <c>""</c>, <c>false</c>) ou não inteiro.</summary>
    public sealed record MissingUserId : SetFinancialAlertsResult;

    /// <summary><c>ativo</c> ausente, nulo ou não booleano (<c>false</c> é válido).</summary>
    public sealed record MissingActive : SetFinancialAlertsResult;

    public sealed record UserNotFound : SetFinancialAlertsResult;

    public sealed record Updated(bool Active) : SetFinancialAlertsResult;
}

/// <summary><c>POST /admin/config-alertas-financeiros</c> (spec §4.9).</summary>
public sealed class SetFinancialAlerts(IAdminNotificationConfigRepository repository)
{
    public async Task<SetFinancialAlertsResult> ExecuteAsync(AdminField userId, AdminField active, CancellationToken cancellationToken)
    {
        if (!userId.Truthy || userId.Kind != AdminFieldKind.WholeNumber)
        {
            return new SetFinancialAlertsResult.MissingUserId();
        }

        if (active.Kind != AdminFieldKind.Boolean)
        {
            return new SetFinancialAlertsResult.MissingActive();
        }

        // Fora do intervalo de int (ou negativo) nunca é id de usuário: mesmo resultado de um id inexistente, sem ir ao banco.
        if (userId.Whole is < 1 or > int.MaxValue)
        {
            return new SetFinancialAlertsResult.UserNotFound();
        }

        var id = (int)userId.Whole;
        if (await repository.GetOrCreateAsync(id, cancellationToken) is null)
        {
            return new SetFinancialAlertsResult.UserNotFound();
        }

        return await repository.SetFinancialAlertsAsync(id, active.Boolean, cancellationToken) is { } saved
            ? new SetFinancialAlertsResult.Updated(saved)
            : new SetFinancialAlertsResult.UserNotFound();
    }
}
