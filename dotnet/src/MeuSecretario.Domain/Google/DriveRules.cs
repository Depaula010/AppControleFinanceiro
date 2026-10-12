namespace MeuSecretario.Domain.Google;

/// <summary>Validação do arquivo antes do upload (RN-010-50), literal de <c>google_drive_service.py:37-100</c>.</summary>
public static class DriveFilePolicy
{
    public const int MaxFileSizeMb = 10;
    public const long MaxFileSize = MaxFileSizeMb * 1024L * 1024L;

    public const string EmptyFile = "Arquivo vazio";
    public const string TooLarge = "Arquivo muito grande. Máximo permitido: 10MB";

    public const string TypeNotAllowed =
        "Tipo de arquivo não permitido. Aceitos: imagens (JPG, PNG, GIF), documentos (PDF, DOC, DOCX), " +
        "planilhas (XLS, XLSX), texto (TXT, CSV), áudio (MP3, OGG)";

    /// <summary>Os 17 tipos aceitos; comparação exata e sensível a maiúsculas (<c>image/jpeg; charset=x</c> não passa).</summary>
    public static readonly IReadOnlySet<string> AllowedMimeTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "image/jpeg",
        "image/png",
        "image/gif",
        "image/webp",
        "image/bmp",
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-powerpoint",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "text/plain",
        "text/csv",
        "audio/mpeg",
        "audio/ogg",
        "audio/opus",
    };

    /// <summary>Ordem do Python: vazio, tamanho, tipo. <c>null</c> = válido.</summary>
    public static string? Validate(long length, string? mimeType)
    {
        if (length <= 0)
        {
            return EmptyFile;
        }

        if (length > MaxFileSize)
        {
            return TooLarge;
        }

        return mimeType is not null && AllowedMimeTypes.Contains(mimeType) ? null : TypeNotAllowed;
    }
}

/// <summary>Consultas de pasta do Drive (RN-010-51, desvio 061).</summary>
public static class DriveQuery
{
    public const string FolderMimeType = "application/vnd.google-apps.folder";

    /// <summary>
    /// <c>name = '&lt;nome&gt;' and mimeType = 'application/vnd.google-apps.folder' and trashed = false</c>, mais
    /// <c> and '&lt;pai&gt;' in parents</c> a partir do 2º segmento. O nome escapa <c>\</c> e depois <c>'</c> (desvio 061;
    /// o Python só escapava a aspa simples).
    /// </summary>
    public static string FolderSearch(string name, string? parentId)
    {
        var query = $"name = '{Escape(name)}' and mimeType = '{FolderMimeType}' and trashed = false";
        return string.IsNullOrEmpty(parentId) ? query : query + $" and '{parentId}' in parents";
    }

    public static string Escape(string name) =>
        name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
}

/// <summary>Mensagens de erro do upload (RN-010-52), por código HTTP e razão da API (o Python casava por substring).</summary>
public static class DriveUploadErrors
{
    public const string PermissionDenied = "Sem permissão para acessar o Google Drive. Por favor, reconecte sua conta Google.";
    public const string QuotaExceeded = "Limite de armazenamento do Google Drive atingido.";
    public const string CredentialsExpired = "Credenciais do Google expiraram. Por favor, reconecte sua conta.";
    public const string Generic = "Erro ao fazer upload.";

    /// <summary>
    /// Prioridade (pergunta 7 da spec): cota antes de permissão, porque o Drive responde 403 também para
    /// <c>storageQuotaExceeded</c>. Depois permissão (403), credenciais (401 ou <c>invalid_grant</c>) e o texto fixo.
    /// </summary>
    public static string Map(int? httpStatus, IEnumerable<string> reasons)
    {
        var list = reasons.Where(reason => !string.IsNullOrEmpty(reason)).ToList();

        if (list.Any(reason => reason.Contains("quota", StringComparison.OrdinalIgnoreCase)))
        {
            return QuotaExceeded;
        }

        if (httpStatus == 403 && !list.Any(reason => reason.Contains("ratelimit", StringComparison.OrdinalIgnoreCase)))
        {
            return PermissionDenied;
        }

        if (httpStatus == 401 || list.Any(reason => reason.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase)))
        {
            return CredentialsExpired;
        }

        return Generic;
    }
}
