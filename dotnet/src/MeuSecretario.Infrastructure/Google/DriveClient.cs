using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Google;
using Microsoft.Extensions.Logging;
using DriveFile = Google.Apis.Drive.v3.Data.File;
using GoogleApiException = Google.GoogleApiException;
using GoogleHttp = Google.Apis.Http;

namespace MeuSecretario.Infrastructure.Google;

/// <summary>
/// Upload ao Google Drive (RN-010-46, 50 a 52). Mesma regra do Calendar: o token vem de <see cref="IGoogleAccountService"/>
/// e o SDK recebe só o <c>access_token</c>. Falhas do upload viram <see cref="DriveUploadResult.Success"/> falso com texto fixo
/// (desvio 008): o texto da exceção nunca vai ao usuário.
/// </summary>
public sealed class DriveClient(
    IGoogleAccountService account,
    GoogleHttp.IHttpClientFactory httpFactory,
    ILogger<DriveClient> logger) : IDriveClient
{
    public const string ScopeMissingMessage =
        "Usuário precisa reconectar a conta Google para autorizar o Google Drive. " +
        "Acesse as configurações e clique em 'Conectar Google'.";

    public const string NotConnectedMessage = "Usuário não conectou conta Google";
    public const string UploadFields = "id, name, webViewLink, webContentLink";

    public async Task<DriveUploadResult> UploadFileAsync(
        int userId,
        ReadOnlyMemory<byte> content,
        string fileName,
        IReadOnlyList<string> folderPath,
        string mimeType,
        CancellationToken cancellationToken)
    {
        // Como o Python: o serviço é obtido (e pode lançar "não conectado") antes de validar o arquivo.
        using var service = await OpenAsync(userId, cancellationToken);

        var invalid = DriveFilePolicy.Validate(content.Length, mimeType);
        if (invalid is not null)
        {
            return DriveUploadResult.Failure(invalid);
        }

        if (folderPath.Count == 0)
        {
            return DriveUploadResult.Failure(DriveUploadErrors.Generic);
        }

        try
        {
            string? folderId = null;
            foreach (var folderName in folderPath)
            {
                folderId = await FindFolderAsync(service, userId, folderName, folderId, cancellationToken)
                    ?? await CreateFolderAsync(service, folderName, folderId, cancellationToken);
            }

            using var stream = new MemoryStream(content.ToArray());
            var request = service.Files.Create(new DriveFile { Name = fileName, Parents = [folderId] }, stream, mimeType);
            request.Fields = UploadFields;

            var progress = await request.UploadAsync(cancellationToken);
            if (progress.Status != UploadStatus.Completed)
            {
                throw progress.Exception ?? new InvalidOperationException("upload não concluído");
            }

            var file = request.ResponseBody;
            return new DriveUploadResult(
                Success: true,
                FileId: file.Id,
                FileName: file.Name,
                FolderName: folderPath[^1],
                FolderPathDisplay: string.Join(" > ", folderPath),
                FolderId: folderId,
                WebViewLink: file.WebViewLink,
                WebContentLink: file.WebContentLink,
                Error: null,
                FolderPath: folderPath);
        }
        catch (Exception ex) when (ex is GoogleApiException or HttpRequestException or TaskCanceledException or InvalidOperationException
                                       && !cancellationToken.IsCancellationRequested)
        {
            GoogleApiLog.DriveUploadFailed(logger, userId, GoogleErrors.Describe(ex));
            return DriveUploadResult.Failure(MapError(ex));
        }
    }

    private static string MapError(Exception exception)
    {
        if (exception is not GoogleApiException api)
        {
            return DriveUploadErrors.Generic;
        }

        var reasons = (api.Error?.Errors ?? []).Select(error => error.Reason ?? string.Empty);
        return DriveUploadErrors.Map((int)api.HttpStatusCode, reasons);
    }

    private async Task<DriveService> OpenAsync(int userId, CancellationToken cancellationToken)
    {
        if (!await account.HasDriveScopeAsync(userId, cancellationToken))
        {
            throw new GoogleNotConnectedException(ScopeMissingMessage);
        }

        var credentials = await account.GetCredentialsAsync(userId, cancellationToken);
        if (credentials.Status != GoogleCredentialStatus.Connected || credentials.AccessToken is null)
        {
            throw new GoogleNotConnectedException(NotConnectedMessage);
        }

        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = GoogleCredential.FromAccessToken(credentials.AccessToken),
            HttpClientFactory = httpFactory,
            ApplicationName = "MeuSecretario",
            GZipEnabled = false,
        });
    }

    /// <summary>Falha na busca ⇒ trata como "não achou" (RN-010-51).</summary>
    private async Task<string?> FindFolderAsync(
        DriveService service, int userId, string name, string? parentId, CancellationToken cancellationToken)
    {
        try
        {
            var list = service.Files.List();
            list.Q = DriveQuery.FolderSearch(name, parentId);
            list.Spaces = "drive";
            list.Fields = "files(id, name)";
            list.PageSize = 1;
            var response = await list.ExecuteAsync(cancellationToken);
            return response.Files?.FirstOrDefault()?.Id;
        }
        catch (Exception ex) when (ex is GoogleApiException or HttpRequestException or TaskCanceledException
                                       && !cancellationToken.IsCancellationRequested)
        {
            GoogleApiLog.DriveFolderSearchFailed(logger, userId, GoogleErrors.Describe(ex));
            return null;
        }
    }

    private static async Task<string> CreateFolderAsync(
        DriveService service, string name, string? parentId, CancellationToken cancellationToken)
    {
        var metadata = new DriveFile { Name = name, MimeType = DriveQuery.FolderMimeType };
        if (!string.IsNullOrEmpty(parentId))
        {
            metadata.Parents = [parentId];
        }

        var request = service.Files.Create(metadata);
        request.Fields = "id";
        var created = await request.ExecuteAsync(cancellationToken);
        return created.Id;
    }
}
