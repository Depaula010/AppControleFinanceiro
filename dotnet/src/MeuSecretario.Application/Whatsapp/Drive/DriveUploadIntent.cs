using System.Globalization;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Google;
using MeuSecretario.Domain.Google;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>
/// <c>Upload Drive</c> (<c>drive_intents.py:UploadDriveIntent</c>): sobe o arquivo anexo à mensagem para a pasta dita no texto. Ordem do
/// Flask: arquivo e tipo presentes, escopo do Drive, credenciais, decodificação do base64, validação, upload. A falha do upload
/// responde sempre o texto genérico (desvio 159, proposto: o Flask lê <c>message</c> em vez de <c>error</c>).
/// </summary>
public sealed class DriveUploadIntent(IGoogleAccountService account, IDriveClient drive, GoogleLinks links) : IWhatsAppIntent
{
    public string Name => "Upload Drive";

    public bool Swallows => true;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var folderPath = DriveFolderPath.Extract(context.Message);
        var media = context.Media;

        if (media is not { HasData: true })
        {
            return DriveMessages.NoFile;
        }

        if (string.IsNullOrEmpty(media.Type))
        {
            return DriveMessages.NoType;
        }

        var userId = context.User.Id;
        if (!await account.HasDriveScopeAsync(userId, cancellationToken))
        {
            // Sem GOOGLE_REDIRECT_URI o Flask monta "/connect-calendar/{id}" (rsplit de texto vazio).
            return DriveMessages.NotConnected(links.ConnectUrl(userId) ?? $"/connect-calendar/{userId.ToString(CultureInfo.InvariantCulture)}");
        }

        var credentials = await account.GetCredentialsAsync(userId, cancellationToken);
        if (credentials.Status != GoogleCredentialStatus.Connected)
        {
            return DriveMessages.AuthError(links.ConnectUrl(userId));
        }

        var content = Decode(media.Data);
        if (content is not { Length: > 0 })
        {
            return DriveMessages.InvalidData;
        }

        var invalid = DriveFilePolicy.Validate(content.Length, media.Type);
        if (invalid is not null)
        {
            return DriveMessages.ValidationError(invalid);
        }

        DriveUploadResult result;
        try
        {
            var fileName = string.IsNullOrEmpty(media.FileName) ? $"arquivo_{userId.ToString(CultureInfo.InvariantCulture)}" : media.FileName;
            result = await drive.UploadFileAsync(userId, content, fileName, folderPath, media.Type, cancellationToken);
        }
        catch (GoogleNotConnectedException)
        {
            return DriveMessages.AuthError(links.ConnectUrl(userId));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return DriveMessages.UploadFailed;
        }

        return result.Success
            ? DriveMessages.Success(result.FolderPathDisplay ?? result.FolderName ?? DriveFolderPath.DefaultFolder, result.FileName ?? "arquivo", result.WebViewLink)
            : DriveMessages.UploadFailed;
    }

    /// <summary>Remove o prefixo <c>data:...;base64,</c> (tudo até a primeira vírgula) e decodifica; <c>null</c> se não for texto ou não decodificar.</summary>
    private static byte[]? Decode(string? data)
    {
        if (data is null)
        {
            return null;
        }

        var comma = data.IndexOf(',', StringComparison.Ordinal);
        return PyBase64.TryDecode(comma >= 0 ? data[(comma + 1)..] : data);
    }
}
