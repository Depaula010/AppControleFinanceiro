using System.Buffers.Text;
using System.Security.Cryptography;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Auth;

namespace MeuSecretario.Application.Auth;

/// <param name="Nome">Texto do corpo; <c>null</c> se ausente ou não for texto.</param>
/// <param name="DiaVencimento">Valor bruto do JSON (número, texto, booleano...); <c>null</c> se ausente ou nulo.</param>
public sealed record RegisterInput(string? Nome, string? Whatsapp, string? Password, object? DiaVencimento, object? DiaFechamento);

public abstract record RegisterResult
{
    public sealed record Success(int UserId) : RegisterResult;

    public sealed record MissingFields : RegisterResult;

    public sealed record PasswordTooShort : RegisterResult;

    public sealed record MissingDays : RegisterResult;

    public sealed record DayOutOfRange(string Field) : RegisterResult;

    public sealed record DaysNotInteger : RegisterResult;

    public sealed record WhatsappTaken : RegisterResult;
}

/// <summary>
/// Cadastro (<c>auth.py:61-203</c>). Validações na ordem do Flask; a primeira que falhar responde.
/// A <c>api_key_automate</c> é cifrada com Fernet e, se a cifra falhar, o cadastro falha (desvio 017);
/// o Flask gravava em texto puro.
/// </summary>
public sealed class RegisterUser(
    IUserRepository users,
    IPasswordHasher hasher,
    IEncryptionService encryption)
{
    public async Task<RegisterResult> ExecuteAsync(RegisterInput input, CancellationToken cancellationToken)
    {
        var nome = input.Nome?.Trim() ?? string.Empty;
        var whatsapp = input.Whatsapp?.Trim() ?? string.Empty;
        var password = input.Password?.Trim() ?? string.Empty;

        if (nome.Length == 0 || whatsapp.Length == 0 || password.Length == 0)
        {
            return new RegisterResult.MissingFields();
        }

        if (!PasswordRules.IsValid(password))
        {
            return new RegisterResult.PasswordTooShort();
        }

        if (input.DiaVencimento is null || input.DiaFechamento is null)
        {
            return new RegisterResult.MissingDays();
        }

        var days = CardDays.Parse(input.DiaVencimento, input.DiaFechamento);
        switch (days.Outcome)
        {
            case CardDaysOutcome.NotInteger:
                return new RegisterResult.DaysNotInteger();
            case CardDaysOutcome.OutOfRange:
                return new RegisterResult.DayOutOfRange(days.Field!);
        }

        var digits = PhoneNumber.Digits(whatsapp);
        if (digits.Length == 0)
        {
            // Desvio 031: o Flask gravaria um usuário com número vazio.
            return new RegisterResult.MissingFields();
        }

        if (await users.FindByWhatsappAsync(digits, cancellationToken) is not null)
        {
            return new RegisterResult.WhatsappTaken();
        }

        // Equivale a secrets.token_urlsafe(32): 32 bytes aleatórios em base64 url-safe, sem padding (43 caracteres).
        var apiKey = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        var created = await users.CreateWithDefaultAccountsAsync(
            new NewUser(nome, digits, hasher.Hash(password), encryption.Encrypt(apiKey)),
            DefaultAccounts.For(days.Vencimento, days.Fechamento),
            cancellationToken);

        return created.UserId is { } id
            ? new RegisterResult.Success(id)
            : new RegisterResult.WhatsappTaken();
    }
}
