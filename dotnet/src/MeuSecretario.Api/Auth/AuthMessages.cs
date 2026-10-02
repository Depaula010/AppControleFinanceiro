namespace MeuSecretario.Api.Auth;

/// <summary>Textos exatos do Flask (spec 001 §4). Mudar uma palavra aqui é mudar o contrato com o Angular.</summary>
public static class AuthMessages
{
    // Filtro RequireUser (token_required, api.py:34-90)
    public const string TokenNotProvided = "Token de autenticação não fornecido";
    public const string InvalidAuthFormat = "Formato de autenticação inválido. Use: Bearer <token>";
    public const string InvalidToken = "Token inválido ou expirado";
    public const string MissingUserId = "Token não contém user_id";
}
