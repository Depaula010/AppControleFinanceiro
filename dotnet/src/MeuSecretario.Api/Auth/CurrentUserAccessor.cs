using MeuSecretario.Application.Abstractions;

namespace MeuSecretario.Api.Auth;

/// <summary>Guarda o <c>user_id</c> da requisição (escopo por requisição); preenchido pelo <see cref="RequireUserFilter"/>.</summary>
public sealed class CurrentUserAccessor : ICurrentUser
{
    private int? _userId;

    public int UserId => _userId
        ?? throw new InvalidOperationException("Nenhum usuário autenticado: o endpoint precisa de RequireUser().");

    internal void Set(int userId) => _userId = userId;
}
