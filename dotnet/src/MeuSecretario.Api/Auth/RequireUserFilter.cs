using MeuSecretario.Api.Contracts;
using MeuSecretario.Application.Abstractions;

namespace MeuSecretario.Api.Auth;

/// <summary>
/// Equivale ao decorator <c>token_required</c> do Flask (api.py:34-90): valida o header
/// <c>Authorization: Bearer &lt;jwt&gt;</c> e disponibiliza o <c>user_id</c> em <see cref="ICurrentUser"/>.
/// Erros em envelope A com as 4 mensagens exatas do Flask.
/// </summary>
public sealed class RequireUserFilter(IJwtTokenService jwt) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        string header = http.Request.Headers.Authorization.ToString();

        // Python: "if not auth_header" — só vazio/ausente; "   " cai no teste de formato.
        if (string.IsNullOrEmpty(header))
        {
            return Unauthorized(AuthMessages.TokenNotProvided);
        }

        // Python: header.split() — separa por qualquer sequência de espaços.
        var parts = header.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !parts[0].Equals("Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return Unauthorized(AuthMessages.InvalidAuthFormat);
        }

        var validation = jwt.Validate(parts[1]);
        if (!validation.IsValid)
        {
            return Unauthorized(AuthMessages.InvalidToken);
        }

        // Python: "if not user_id" — ausente, nulo ou zero.
        if (validation.UserId is null or 0)
        {
            return Unauthorized(AuthMessages.MissingUserId);
        }

        http.RequestServices.GetRequiredService<CurrentUserAccessor>().Set(validation.UserId.Value);
        return await next(context);
    }

    private static IResult Unauthorized(string message) => Envelopes.A.Error(message, StatusCodes.Status401Unauthorized);
}

public static class RequireUserExtensions
{
    /// <summary>Protege o endpoint com JWT (equivale a <c>@token_required</c>).</summary>
    public static TBuilder RequireUser<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter<TBuilder, RequireUserFilter>();
}
