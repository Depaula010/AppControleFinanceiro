using MeuSecretario.Domain.Google;

namespace MeuSecretario.UnitTests.Domain.Google;

public class DriveRulesTests
{
    private static readonly string[] AllMimeTypes =
    [
        "image/jpeg", "image/png", "image/gif", "image/webp", "image/bmp",
        "application/pdf", "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-powerpoint",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "text/plain", "text/csv", "audio/mpeg", "audio/ogg", "audio/opus",
    ];

    [Fact]
    [Trait("AC", "010-94")]
    public void TheSeventeenMimeTypes_Pass()
    {
        AllMimeTypes.Length.ShouldBe(17);
        DriveFilePolicy.AllowedMimeTypes.Count.ShouldBe(17);
        foreach (var mime in AllMimeTypes)
        {
            DriveFilePolicy.Validate(10, mime).ShouldBeNull(mime);
        }
    }

    [Fact]
    [Trait("AC", "010-94")]
    public void EmptyFile_IsRejectedFirst()
    {
        DriveFilePolicy.Validate(0, "application/x-lixo").ShouldBe("Arquivo vazio");
    }

    [Fact]
    [Trait("AC", "010-94")]
    public void TenMiBPlusOneByte_IsTooLarge_AndExactlyTenMiBPasses()
    {
        DriveFilePolicy.Validate((10 * 1024 * 1024) + 1, "application/pdf").ShouldBe("Arquivo muito grande. Máximo permitido: 10MB");
        DriveFilePolicy.Validate(10 * 1024 * 1024, "application/pdf").ShouldBeNull();
    }

    [Theory]
    [InlineData("image/jpeg; charset=x")]
    [InlineData("IMAGE/JPEG")]
    [InlineData("application/zip")]
    [InlineData("")]
    [InlineData(null)]
    [Trait("AC", "010-94")]
    public void OtherMimeTypes_AreRejectedWithTheLiteralMessage(string? mime)
    {
        DriveFilePolicy.Validate(10, mime).ShouldBe(
            "Tipo de arquivo não permitido. Aceitos: imagens (JPG, PNG, GIF), documentos (PDF, DOC, DOCX), planilhas (XLS, XLSX), texto (TXT, CSV), áudio (MP3, OGG)");
    }

    [Fact]
    [Trait("AC", "010-95")]
    public void FolderSearch_BuildsTheExactQuery()
    {
        DriveQuery.FolderSearch("IR", null).ShouldBe("name = 'IR' and mimeType = 'application/vnd.google-apps.folder' and trashed = false");
        DriveQuery.FolderSearch("IR 2026", "abc123").ShouldBe(
            "name = 'IR 2026' and mimeType = 'application/vnd.google-apps.folder' and trashed = false and 'abc123' in parents");
        DriveQuery.FolderSearch("IR", "").ShouldBe("name = 'IR' and mimeType = 'application/vnd.google-apps.folder' and trashed = false");
    }

    [Theory]
    [InlineData("Maria's", "Maria\\'s")]
    [InlineData("a\\b", "a\\\\b")]
    [InlineData("a\\'b", "a\\\\\\'b")] // desvio 061: a barra é escapada ANTES da aspa
    [Trait("AC", "010-95")]
    public void FolderName_EscapesBackslashThenQuote(string name, string escaped)
    {
        DriveQuery.FolderSearch(name, null).ShouldStartWith($"name = '{escaped}' and");
    }

    [Theory]
    [InlineData(403, new[] { "storageQuotaExceeded" }, "Limite de armazenamento do Google Drive atingido.")]
    [InlineData(403, new[] { "insufficientPermissions" }, "Sem permissão para acessar o Google Drive. Por favor, reconecte sua conta Google.")]
    [InlineData(403, new[] { "forbidden" }, "Sem permissão para acessar o Google Drive. Por favor, reconecte sua conta Google.")]
    [InlineData(403, new string[0], "Sem permissão para acessar o Google Drive. Por favor, reconecte sua conta Google.")]
    [InlineData(401, new[] { "authError" }, "Credenciais do Google expiraram. Por favor, reconecte sua conta.")]
    [InlineData(400, new[] { "invalid_grant" }, "Credenciais do Google expiraram. Por favor, reconecte sua conta.")]
    [InlineData(500, new[] { "backendError" }, "Erro ao fazer upload.")]
    [InlineData(null, new string[0], "Erro ao fazer upload.")]
    [InlineData(403, new[] { "userRateLimitExceeded" }, "Erro ao fazer upload.")]
    [InlineData(429, new[] { "quotaExceeded" }, "Limite de armazenamento do Google Drive atingido.")]
    [Trait("AC", "010-96")]
    public void UploadErrors_AreMappedByStatusAndReason(int? status, string[] reasons, string expected)
    {
        DriveUploadErrors.Map(status, reasons).ShouldBe(expected);
    }
}
