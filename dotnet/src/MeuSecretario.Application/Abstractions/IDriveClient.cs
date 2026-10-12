namespace MeuSecretario.Application.Abstractions;

/// <summary>Upload ao Google Drive (spec 010 RN-010-50 a 52). Falhas do upload viram <see cref="DriveUploadResult.Success"/> falso.</summary>
public interface IDriveClient
{
    Task<DriveUploadResult> UploadFileAsync(
        int userId,
        ReadOnlyMemory<byte> content,
        string fileName,
        IReadOnlyList<string> folderPath,
        string mimeType,
        CancellationToken cancellationToken);
}

public sealed record DriveUploadResult(
    bool Success,
    string? FileId,
    string? FileName,
    string? FolderName,
    string? FolderPathDisplay,
    string? FolderId,
    string? WebViewLink,
    string? WebContentLink,
    string? Error,
    IReadOnlyList<string>? FolderPath = null)
{
    public static DriveUploadResult Failure(string error) => new(false, null, null, null, null, null, null, null, error);
}
