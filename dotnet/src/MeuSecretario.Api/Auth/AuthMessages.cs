namespace MeuSecretario.Api.Auth;

/// <summary>Textos exatos do Flask (spec 001 §4). Mudar uma palavra aqui é mudar o contrato com o Angular.</summary>
public static class AuthMessages
{
    // Filtro RequireUser (token_required, api.py:34-90)
    public const string TokenNotProvided = "Token de autenticação não fornecido";
    public const string InvalidAuthFormat = "Formato de autenticação inválido. Use: Bearer <token>";
    public const string InvalidToken = "Token inválido ou expirado";
    public const string MissingUserId = "Token não contém user_id";

    // POST /auth/register (auth.py:61-203)
    public const string RegisterMissingFields = "Campos obrigatórios: nome, whatsapp, password";
    public const string PasswordTooShort = "Senha deve ter no mínimo 6 caracteres";
    public const string RegisterMissingDays = "Campos obrigatórios: dia_vencimento, dia_fechamento";
    public const string DaysNotInteger = "Dias inválidos: dia_vencimento e dia_fechamento devem ser números inteiros";
    public const string WhatsappTaken = "WhatsApp já cadastrado";
    public const string Registered = "Usuário cadastrado com sucesso";
    public const string RegisterFailed = "Erro ao cadastrar usuário. Tente novamente.";

    public static string DayOutOfRange(string field) => $"Dias inválidos: {field} deve estar entre 1 e 31";

    // POST /auth/login (auth.py:206-300)
    public const string LoginMissingFields = "Campos obrigatórios: whatsapp, password";
    public const string NotRegistered = "WhatsApp não cadastrado";
    public const string NoPassword = "Usuário sem senha cadastrada. Cadastre-se pelo site.";
    public const string WrongPassword = "Senha incorreta";
    public const string LoginFailed = "Erro ao fazer login. Tente novamente.";

    // POST /auth/verify (auth.py:303-352)
    public const string MissingToken = "Token não fornecido";
    public const string VerifyFailed = "Erro ao verificar token";
}
