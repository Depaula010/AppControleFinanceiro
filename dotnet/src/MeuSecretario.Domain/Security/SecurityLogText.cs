namespace MeuSecretario.Domain.Security;

/// <summary>Sanitização de texto controlado pelo cliente antes de ir para o <c>security.log</c> (spec 009 §9).</summary>
public static class SecurityLogText
{
    /// <summary>CR, LF e demais caracteres de controle viram <c>?</c>; <c>null</c> vira vazio.</summary>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Any(char.IsControl)
            ? string.Create(text.Length, text, static (span, source) =>
            {
                for (var i = 0; i < source.Length; i++)
                {
                    span[i] = char.IsControl(source[i]) ? '?' : source[i];
                }
            })
            : text;
    }
}
