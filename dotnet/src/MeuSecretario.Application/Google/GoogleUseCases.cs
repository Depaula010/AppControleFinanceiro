using MeuSecretario.Application.Abstractions;

namespace MeuSecretario.Application.Google;

public abstract record ConnectResult
{
    /// <summary>Link sem token válido (desvio 053).</summary>
    public sealed record Forbidden : ConnectResult;

    public sealed record DatabaseUnavailable : ConnectResult;

    public sealed record UserNotFound : ConnectResult;

    public sealed record AlreadyConnected : ConnectResult;

    public sealed record Redirect(string Url) : ConnectResult
    {
        /// <summary>A URL leva o <c>state</c> assinado: nunca no <c>ToString</c> (logs).</summary>
        public override string ToString() => "Redirect { Url = *** }";
    }

    /// <summary>Qualquer exceção (config OAuth ausente, banco): o chamador loga só o tipo e responde o texto fixo.</summary>
    public sealed record Failed(Exception Error) : ConnectResult;
}

public abstract record CallbackResult
{
    public sealed record Denied : CallbackResult;

    public sealed record MissingParams : CallbackResult;

    public sealed record Connected(string UserName) : CallbackResult;

    public sealed record InvalidState : CallbackResult;

    public sealed record Failed(Exception Error) : CallbackResult;
}

public abstract record DisconnectResult
{
    public sealed record Disconnected : DisconnectResult;

    public sealed record Failed(Exception Error) : DisconnectResult;
}

/// <summary><c>GET /connect-calendar/{id}</c> (spec 010 §7.4, RN-010-42, desvio 053). Sem referência a HTTP.</summary>
public sealed class ConnectCalendar(
    IDatabaseHealth database,
    IGoogleUserLookup users,
    GoogleAccountService account,
    ConnectLinkTokenCodec linkTokens,
    IIntegrationSettings settings,
    TimeProvider time)
{
    public async Task<ConnectResult> ExecuteAsync(int userId, string? linkToken, bool force, CancellationToken cancellationToken)
    {
        // Desvio 053: o token assinado vem antes de qualquer leitura (nem o banco responde a um link falso).
        if (!LinkIsAccepted(userId, linkToken))
        {
            return new ConnectResult.Forbidden();
        }

        var status = await database.CheckAsync(cancellationToken);
        if (!status.Connected)
        {
            return new ConnectResult.DatabaseUnavailable();
        }

        try
        {
            if (await users.FindAsync(userId, cancellationToken) is null)
            {
                return new ConnectResult.UserNotFound();
            }

            // Ordem do Flask: is_connected (pode renovar o token) antes de has_drive_scope.
            var connected = await account.IsConnectedAsync(userId, cancellationToken);
            var hasDrive = await account.HasDriveScopeAsync(userId, cancellationToken);
            if (connected && hasDrive && !force)
            {
                return new ConnectResult.AlreadyConnected();
            }

            return new ConnectResult.Redirect(account.BuildAuthorizationUrl(userId));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectResult.Failed(ex);
        }
    }

    private bool LinkIsAccepted(int userId, string? linkToken)
    {
        if (string.IsNullOrEmpty(linkToken))
        {
            // Válvula opcional da virada (desligada por padrão): links antigos, sem `t`, até a data configurada.
            return settings.GoogleConnectLegacyUntil is { } until && time.GetUtcNow() < until;
        }

        return linkTokens.Verify(linkToken) == userId;
    }
}

/// <summary><c>GET /oauth2callback</c> (spec 010 §7.5, RN-010-43): sempre vira HTML na rota.</summary>
public sealed class HandleOAuthCallback(GoogleAccountService account, IGoogleUserLookup users, IBotClient bot)
{
    public const string FallbackName = "Usuario";

    public async Task<CallbackResult> ExecuteAsync(string? code, string? state, string? error, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(error))
        {
            return new CallbackResult.Denied();
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            return new CallbackResult.MissingParams();
        }

        try
        {
            if (await account.ExchangeAndSaveAsync(code, state, cancellationToken) is not { } userId)
            {
                return new CallbackResult.InvalidState();
            }

            var user = await users.FindAsync(userId, cancellationToken);
            if (!string.IsNullOrEmpty(user?.WhatsappNumber))
            {
                // O WhatsApp só sai depois de os tokens estarem gravados; falha do envio é ignorada.
                try
                {
                    _ = await bot.SendTextAsync(user.WhatsappNumber, GoogleNotificationMessages.Connected(user.Name), cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // O IBotClient não deve lançar; se lançar, a conexão já está gravada e a página continua 200.
                }
            }

            return new CallbackResult.Connected(user?.Name ?? FallbackName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new CallbackResult.Failed(ex);
        }
    }
}

/// <summary><c>POST /disconnect-calendar/{id}</c> (spec 010 §7.6, RN-010-44). A autenticação é do filtro da rota.</summary>
public sealed class DisconnectCalendar(GoogleAccountService account)
{
    public async Task<DisconnectResult> ExecuteAsync(int userId, CancellationToken cancellationToken)
    {
        try
        {
            await account.RevokeAsync(userId, cancellationToken);
            return new DisconnectResult.Disconnected();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DisconnectResult.Failed(ex);
        }
    }
}
