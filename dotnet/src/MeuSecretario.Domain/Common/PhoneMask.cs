namespace MeuSecretario.Domain.Common;

/// <summary>Máscara do número de WhatsApp para logs: só os 4 últimos dígitos (spec 010 §3).</summary>
public static class PhoneMask
{
    private const string Ellipsis = "…";

    /// <summary><c>5511999991072</c> vira <c>…1072</c>; nulo, vazio ou com menos de 4 caracteres vira só <c>…</c> (não vaza o número).</summary>
    public static string Last4(string? number)
    {
        if (string.IsNullOrEmpty(number) || number.Length < 4)
        {
            return Ellipsis;
        }

        return Ellipsis + number[^4..];
    }
}
