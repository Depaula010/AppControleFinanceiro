using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Auth;

namespace MeuSecretario.Application.Auth;

public sealed record LoginInput(string? Whatsapp, string? Password);

public sealed record LoggedUser(int Id, string Nome, string Whatsapp);

public abstract record LoginResult
{
    public sealed record Success(string Token, LoggedUser User) : LoginResult;

    public sealed record MissingFields : LoginResult;

    public sealed record NotRegistered : LoginResult;

    public sealed record NoPassword : LoginResult;

    public sealed record WrongPassword : LoginResult;
}

/// <summary>
/// Login (<c>auth.py:206-300</c>). Distingue "não cadastrado" (404) de "senha incorreta" (401), como o Flask.
/// Não verifica <c>usuarios.ativo</c> nem grava <c>ultimo_acesso</c> (o Flask também não).
/// </summary>
public sealed class LoginUser(IUserRepository users, IPasswordHasher hasher, IJwtTokenService jwt)
{
    public async Task<LoginResult> ExecuteAsync(LoginInput input, CancellationToken cancellationToken)
    {
        var whatsapp = input.Whatsapp?.Trim() ?? string.Empty;
        var password = input.Password?.Trim() ?? string.Empty;

        if (whatsapp.Length == 0 || password.Length == 0)
        {
            return new LoginResult.MissingFields();
        }

        var user = await users.FindByWhatsappAsync(PhoneNumber.Digits(whatsapp), cancellationToken);
        if (user is null)
        {
            return new LoginResult.NotRegistered();
        }

        if (string.IsNullOrEmpty(user.SenhaHash))
        {
            return new LoginResult.NoPassword();
        }

        if (!hasher.Verify(user.SenhaHash, password))
        {
            return new LoginResult.WrongPassword();
        }

        return new LoginResult.Success(jwt.Issue(user.Id), new LoggedUser(user.Id, user.Nome, user.NumeroWhatsapp));
    }
}

public abstract record VerifyResult
{
    public sealed record Valid(int UserId) : VerifyResult;

    public sealed record MissingToken : VerifyResult;

    public sealed record Invalid : VerifyResult;

    /// <summary>Token válido sem o claim <c>user_id</c>: o Flask lança <c>KeyError</c> e responde 500.</summary>
    public sealed record NoUserId : VerifyResult;
}

/// <summary><c>POST /auth/verify</c> (<c>auth.py:303-352</c>). Não consulta o banco.</summary>
public sealed class VerifyToken(IJwtTokenService jwt)
{
    public VerifyResult Execute(string? token)
    {
        var trimmed = token?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return new VerifyResult.MissingToken();
        }

        var validation = jwt.Validate(trimmed);
        if (!validation.IsValid)
        {
            return new VerifyResult.Invalid();
        }

        return validation.UserId is { } id
            ? new VerifyResult.Valid(id)
            : new VerifyResult.NoUserId();
    }
}
