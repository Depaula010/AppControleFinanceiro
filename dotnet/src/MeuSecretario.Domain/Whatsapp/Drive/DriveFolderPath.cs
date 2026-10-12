using System.Text;
using System.Text.RegularExpressions;

namespace MeuSecretario.Domain.Whatsapp;

/// <summary>Caminho da pasta do Drive dito na mensagem (<c>drive_intents.py:UploadDriveIntent._extract_folder_path</c>).</summary>
public static partial class DriveFolderPath
{
    public const string DefaultFolder = "WhatsApp Uploads";

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>Casa o texto em minúsculas e recorta o texto original (preserva a caixa: <c>IR</c> não vira <c>Ir</c>).</summary>
    public static IReadOnlyList<string> Extract(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [DefaultFolder];
        }

        var lower = text.ToLowerInvariant().Trim();
        var original = text.Trim();

        foreach (var pattern in new[] { Explicit(), AfterFolderWord(), AtEnd() })
        {
            var match = pattern.Match(lower);
            if (!match.Success)
            {
                continue;
            }

            var group = match.Groups[1];
            var raw = original.Substring(group.Index, group.Length).Trim();
            var parts = Separator().Split(raw).Select(SanitizeName).Where(part => part.Length > 0).ToList();
            if (parts.Count > 0)
            {
                return parts;
            }
        }

        return [DefaultFolder];
    }

    /// <summary>
    /// <c>re.sub(r'[^\w\s\-_.]', '', nome)</c>, espaços normalizados, até 100 caracteres e, se o usuário digitou tudo em minúsculas,
    /// <c>str.title()</c> (preserva <c>IR 2026</c>, <c>CNPJ</c>).
    /// </summary>
    public static string SanitizeName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var cleaned = Invalid().Replace(name, string.Empty);
        cleaned = string.Join(' ', cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        var runes = cleaned.EnumerateRunes().Take(100);
        var limited = new StringBuilder();
        foreach (var rune in runes)
        {
            limited.Append(rune.ToString());
        }

        cleaned = limited.ToString();
        return cleaned.Any(char.IsUpper) ? cleaned : PyStrings.Title(cleaned);
    }

    [GeneratedRegex(
        @"(?:salvar|guardar|enviar|subir|upload)\s+(?:no|para|pro|pra|em)\s+(?:google\s+)?drive\s+(?:na\s+)?pasta\s+[""']?([^""']+)[""']?",
        Options)]
    private static partial Regex Explicit();

    [GeneratedRegex(@"pasta\s+[""']?([^""']+)[""']?", Options)]
    private static partial Regex AfterFolderWord();

    [GeneratedRegex(@"(?:em|na|para)\s+[""']?([^""']+)[""']?$", Options)]
    private static partial Regex AtEnd();

    [GeneratedRegex(@"\s*[/\\>]\s*")]
    private static partial Regex Separator();

    [GeneratedRegex(@"[^\w\s\-_.]")]
    private static partial Regex Invalid();
}

/// <summary><c>base64.b64decode</c> do Python (modo não estrito): ignora o que não é do alfabeto e exige o preenchimento correto.</summary>
public static class PyBase64
{
    /// <summary>
    /// <c>null</c> quando o Python levanta (texto não ASCII ou preenchimento incorreto). O decodificador para no <c>=</c> que completa
    /// o grupo de quatro.
    /// </summary>
    public static byte[]? TryDecode(string text)
    {
        // str.encode('ascii') acontece antes de decodificar: qualquer caractere fora do ASCII, em qualquer posição, recusa.
        if (text.Any(ch => ch > 0x7f))
        {
            return null;
        }

        var result = new List<byte>();
        var buffer = 0;
        var quad = 0;
        var pads = 0;

        foreach (var ch in text)
        {
            if (ch == '=')
            {
                if (quad >= 2 && quad + ++pads >= 4)
                {
                    quad = 0;
                    break;
                }

                continue;
            }

            var value = Value(ch);
            if (value < 0)
            {
                continue;
            }

            pads = 0;
            buffer = (buffer << 6) | value;
            quad++;
            switch (quad)
            {
                case 2:
                    result.Add((byte)((buffer >> 4) & 0xFF));
                    break;
                case 3:
                    result.Add((byte)((buffer >> 2) & 0xFF));
                    break;
                case 4:
                    result.Add((byte)(buffer & 0xFF));
                    quad = 0;
                    buffer = 0;
                    break;
                default:
                    break;
            }
        }

        return quad == 0 ? [.. result] : null;
    }

    private static int Value(char ch) => ch switch
    {
        >= 'A' and <= 'Z' => ch - 'A',
        >= 'a' and <= 'z' => ch - 'a' + 26,
        >= '0' and <= '9' => ch - '0' + 52,
        '+' => 62,
        '/' => 63,
        _ => -1,
    };
}

/// <summary>Textos da intenção <c>Upload Drive</c> (<c>drive_intents.py</c>).</summary>
public static class DriveMessages
{
    public const string NoFile =
        "📎 Nenhum arquivo recebido.\n\n" +
        "Para salvar no Google Drive, envie uma *imagem* ou *documento* junto com a mensagem.\n\n" +
        "Exemplo:\n" +
        "[Envie uma foto]\n" +
        "_salvar no drive pasta Notas Fiscais_";

    public const string NoType = "Não foi possível identificar o tipo do arquivo.";

    public const string InvalidData = "Não foi possível processar o arquivo enviado.";

    /// <summary>Sem motivo conhecido (ou exceção inesperada): o texto genérico do Flask.</summary>
    public const string UploadFailed = "❌ Erro ao enviar arquivo para o Drive.";

    /// <summary>
    /// Desvio 159: mostra o motivo fixo do upload (cota, permissão, credenciais) em vez do genérico. O motivo vem de
    /// <c>DriveUploadErrors</c> (textos fixos, nunca <c>str(e)</c>).
    /// </summary>
    public static string UploadFailure(string? reason) => string.IsNullOrWhiteSpace(reason) ? UploadFailed : "❌ " + reason;

    public static string NotConnected(string connectUrl) =>
        "🔗 *Google Drive não conectado*\n\n" +
        "Para usar esta funcionalidade, conecte sua conta Google:\n" +
        connectUrl + "\n\n" +
        "Após conectar, envie o arquivo novamente.";

    public static string AuthError(string? connectUrl) =>
        "⚠️ *Erro de autenticação*\n\n" +
        "Não foi possível acessar o Google Drive. Sua conexão pode ter expirado.\n\n" +
        (connectUrl is not null
            ? $"👉 *Reconecte agora:*\n{connectUrl}\n\nApós reconectar, envie o arquivo novamente."
            : "Reconecte sua conta Google nas configurações do app.");

    public static string ValidationError(string message) => "❌ " + message;

    public static string Success(string folder, string fileName, string? link)
    {
        var message = "✅ *Arquivo enviado para o Google Drive!*\n\n" + $"📁 *Pasta:* {folder}\n" + $"📄 *Arquivo:* {fileName}\n";
        return string.IsNullOrEmpty(link) ? message : message + $"\n🔗 *Abrir no Drive:*\n{link}";
    }
}
