namespace MeuSecretario.Application.Abstractions;

/// <summary>
/// Usuário autenticado da requisição atual (o <c>user_id</c> que o <c>token_required</c> do Flask injeta).
/// Só é preenchido em endpoints protegidos por <c>RequireUser()</c>.
/// </summary>
public interface ICurrentUser
{
    int UserId { get; }
}
