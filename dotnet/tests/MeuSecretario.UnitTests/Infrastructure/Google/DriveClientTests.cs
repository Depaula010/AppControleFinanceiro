using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure.Google;
using MeuSecretario.UnitTests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MeuSecretario.UnitTests.Infrastructure.Google;

/// <summary>Drive com o fake do Google: busca e criação de pastas, upload retomável e mapeamento de erros (spec 010d, T36).</summary>
public sealed class DriveClientTests : IDisposable
{
    private const string Files = "/drive/v3/files";
    private const string Upload = "/upload/drive/v3/files";
    private const string FolderMime = "application/vnd.google-apps.folder";
    private const string FileJson = """{"id":"arq-1","name":"nf.pdf","webViewLink":"https://drive.exemplo/view","webContentLink":"https://drive.exemplo/dl"}""";

    private readonly FakeHttpHandler _handler = new();
    private readonly IGoogleAccountService _account = Substitute.For<IGoogleAccountService>();
    private readonly DriveClient _client;
    private readonly byte[] _content = Encoding.UTF8.GetBytes("%PDF-conteudo");

    public DriveClientTests()
    {
        _account.HasDriveScopeAsync(7, Arg.Any<CancellationToken>()).Returns(true);
        _account.GetCredentialsAsync(7, Arg.Any<CancellationToken>()).Returns(
            new GoogleCredentialResult(GoogleCredentialStatus.Connected, "tok-1", ["https://www.googleapis.com/auth/drive.file"]));
        _client = new DriveClient(_account, new FakeGoogleHttpClientFactory(_handler), NullLogger<DriveClient>.Instance);
    }

    public void Dispose() => _handler.Dispose();

    private static string Path(RecordedRequest request) => Uri.UnescapeDataString(request.Uri.AbsolutePath);

    private static System.Collections.Specialized.NameValueCollection Query(RecordedRequest request) => HttpUtility.ParseQueryString(request.Uri.Query);

    private static void ShouldMatchJson(string? actual, string expected)
    {
        actual.ShouldNotBeNull();
        JsonNode.DeepEquals(JsonNode.Parse(actual), JsonNode.Parse(expected)).ShouldBeTrue($"esperado {expected}, veio {actual}");
    }

    private static HttpResponseMessage ResumableStart(RecordedRequest _)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) };
        response.Headers.Location = new Uri("https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&upload_id=sessao-1");
        return response;
    }

    private Task<DriveUploadResult> UploadAsync(string[] path, string mime = "application/pdf", byte[]? content = null, string name = "nf.pdf") =>
        _client.UploadFileAsync(7, content ?? _content, name, path, mime, CancellationToken.None);

    // ============================== sucesso (AC-010-95) ==============================

    [Fact]
    [Trait("AC", "010-95")]
    public async Task Upload_IntoAnExistingFolder_SearchesThenUploadsWithMetadataAndFields()
    {
        _handler
            .Enqueue(HttpStatusCode.OK, """{"files":[{"id":"pasta-1","name":"Notas Fiscais"}]}""")
            .Enqueue(ResumableStart)
            .Enqueue(HttpStatusCode.OK, FileJson);

        var result = await UploadAsync(["Notas Fiscais"]);

        result.ShouldBe(
            new DriveUploadResult(
                true, "arq-1", "nf.pdf", "Notas Fiscais", "Notas Fiscais", "pasta-1", "https://drive.exemplo/view", "https://drive.exemplo/dl", null, result.FolderPath),
            "campos");
        result.FolderPath.ShouldBe(["Notas Fiscais"]);

        var requests = _handler.Requests;
        requests.Count.ShouldBe(3);

        requests[0].Method.ShouldBe(HttpMethod.Get);
        Path(requests[0]).ShouldBe(Files);
        var search = Query(requests[0]);
        search["q"].ShouldBe("name = 'Notas Fiscais' and mimeType = 'application/vnd.google-apps.folder' and trashed = false");
        (search["spaces"] ?? "drive").ShouldBe("drive"); // "drive" é o padrão da API e o SDK o omite
        search["fields"].ShouldBe("files(id, name)");
        search["pageSize"].ShouldBe("1");
        requests[0].Headers["Authorization"].ShouldBe("Bearer tok-1");

        requests[1].Method.ShouldBe(HttpMethod.Post);
        Path(requests[1]).ShouldBe(Upload);
        Query(requests[1])["uploadType"].ShouldBe("resumable");
        Query(requests[1])["fields"].ShouldBe("id, name, webViewLink, webContentLink");
        ShouldMatchJson(requests[1].BodyText, """{"name":"nf.pdf","parents":["pasta-1"]}""");

        requests[2].Method.ShouldBe(HttpMethod.Put);
        requests[2].BodyBytes.ShouldBe(_content);
    }

    [Fact]
    [Trait("AC", "010-95")]
    public async Task Upload_NestedPath_FindsOrCreatesEachSegmentInsideThePreviousOne()
    {
        _handler
            .Enqueue(HttpStatusCode.OK, """{"files":[]}""")
            .Enqueue(HttpStatusCode.OK, """{"id":"pasta-ir"}""")
            .Enqueue(HttpStatusCode.OK, """{"files":[]}""")
            .Enqueue(HttpStatusCode.OK, """{"id":"pasta-ir-2026"}""")
            .Enqueue(ResumableStart)
            .Enqueue(HttpStatusCode.OK, FileJson);

        var result = await UploadAsync(["IR", "IR 2026"]);

        result.Success.ShouldBeTrue();
        result.FolderName.ShouldBe("IR 2026");
        result.FolderPathDisplay.ShouldBe("IR > IR 2026");
        result.FolderId.ShouldBe("pasta-ir-2026");
        result.FolderPath.ShouldBe(["IR", "IR 2026"]);

        var requests = _handler.Requests;
        requests.Count.ShouldBe(6);

        Query(requests[0])["q"].ShouldBe("name = 'IR' and mimeType = 'application/vnd.google-apps.folder' and trashed = false");

        requests[1].Method.ShouldBe(HttpMethod.Post);
        Path(requests[1]).ShouldBe(Files);
        Query(requests[1])["fields"].ShouldBe("id");
        ShouldMatchJson(requests[1].BodyText, $$"""{"name":"IR","mimeType":"{{FolderMime}}"}""");

        Query(requests[2])["q"].ShouldBe(
            "name = 'IR 2026' and mimeType = 'application/vnd.google-apps.folder' and trashed = false and 'pasta-ir' in parents");

        ShouldMatchJson(requests[3].BodyText, $$"""{"name":"IR 2026","mimeType":"{{FolderMime}}","parents":["pasta-ir"]}""");
        ShouldMatchJson(requests[4].BodyText, """{"name":"nf.pdf","parents":["pasta-ir-2026"]}""");
    }

    [Fact]
    [Trait("AC", "010-95")]
    public async Task Upload_FolderNameWithQuoteAndBackslash_IsEscapedInTheSearch()
    {
        _handler
            .Enqueue(HttpStatusCode.OK, """{"files":[{"id":"p"}]}""")
            .Enqueue(ResumableStart)
            .Enqueue(HttpStatusCode.OK, FileJson);

        await UploadAsync(["Maria's \\ notas"]);

        Query(_handler.Requests[0])["q"].ShouldStartWith("name = 'Maria\\'s \\\\ notas' and");
    }

    [Fact]
    [Trait("AC", "010-95")]
    public async Task Upload_WhenTheFolderSearchFails_TreatsItAsNotFound_AndCreatesTheFolder()
    {
        _handler
            .Enqueue(HttpStatusCode.InternalServerError, "{}")
            .Enqueue(HttpStatusCode.OK, """{"id":"pasta-nova"}""")
            .Enqueue(ResumableStart)
            .Enqueue(HttpStatusCode.OK, FileJson);

        var result = await UploadAsync(["Notas"]);

        result.Success.ShouldBeTrue();
        result.FolderId.ShouldBe("pasta-nova");
    }

    [Fact]
    public async Task Upload_FileWithoutWebContentLink_KeepsItNull()
    {
        _handler
            .Enqueue(HttpStatusCode.OK, """{"files":[{"id":"p"}]}""")
            .Enqueue(ResumableStart)
            .Enqueue(HttpStatusCode.OK, """{"id":"a","name":"x.txt","webViewLink":"https://v"}""");

        var result = await UploadAsync(["P"], "text/plain", name: "x.txt");

        result.WebContentLink.ShouldBeNull();
        result.WebViewLink.ShouldBe("https://v");
    }

    // ============================== validação e conexão (AC-010-94, 98) ==============================

    [Fact]
    [Trait("AC", "010-94")]
    public async Task Upload_InvalidFile_ReturnsTheValidationMessage_WithoutCallingGoogle()
    {
        (await UploadAsync(["P"], content: [])).Error.ShouldBe("Arquivo vazio");
        (await UploadAsync(["P"], content: new byte[(10 * 1024 * 1024) + 1])).Error.ShouldBe("Arquivo muito grande. Máximo permitido: 10MB");
        (await UploadAsync(["P"], "application/zip")).Error.ShouldStartWith("Tipo de arquivo não permitido. Aceitos:");

        _handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "010-98")]
    public async Task Upload_WithoutDriveScope_ThrowsTheReconnectMessage_WithoutCallingGoogle()
    {
        _account.HasDriveScopeAsync(7, Arg.Any<CancellationToken>()).Returns(false);

        var ex = await Should.ThrowAsync<GoogleNotConnectedException>(() => UploadAsync(["P"]));

        ex.Message.ShouldBe(
            "Usuário precisa reconectar a conta Google para autorizar o Google Drive. Acesse as configurações e clique em 'Conectar Google'.");
        _handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "010-98")]
    public async Task Upload_WithScopeButNoCredentials_ThrowsNotConnected()
    {
        _account.GetCredentialsAsync(7, Arg.Any<CancellationToken>()).Returns(GoogleCredentialResult.Of(GoogleCredentialStatus.NeedsReconnect));

        var ex = await Should.ThrowAsync<GoogleNotConnectedException>(() => UploadAsync(["P"]));

        ex.Message.ShouldBe("Usuário não conectou conta Google");
    }

    [Fact]
    public async Task Upload_WithEmptyFolderPath_FailsWithTheGenericText()
    {
        (await UploadAsync([])).Error.ShouldBe("Erro ao fazer upload.");
        _handler.Requests.ShouldBeEmpty();
    }

    // ============================== erros (AC-010-96) ==============================

    [Theory]
    [InlineData(403, "insufficientPermissions", "Sem permissão para acessar o Google Drive. Por favor, reconecte sua conta Google.")]
    [InlineData(403, "forbidden", "Sem permissão para acessar o Google Drive. Por favor, reconecte sua conta Google.")]
    [InlineData(403, "storageQuotaExceeded", "Limite de armazenamento do Google Drive atingido.")]
    [InlineData(401, "authError", "Credenciais do Google expiraram. Por favor, reconecte sua conta.")]
    [InlineData(500, "backendError", "Erro ao fazer upload.")]
    [InlineData(404, "notFound", "Erro ao fazer upload.")]
    [Trait("AC", "010-96")]
    public async Task Upload_ApiErrors_AreMappedToTheFixedMessages_WithoutTheExceptionText(int status, string reason, string expected)
    {
        var body = $$$"""{"error":{"code":{{{status}}},"message":"detalhe-secreto tok-1","errors":[{"reason":"{{{reason}}}","message":"detalhe-secreto"}]}}""";
        _handler
            .Enqueue(HttpStatusCode.OK, """{"files":[{"id":"p"}]}""")
            .Enqueue((HttpStatusCode)status, body);

        var result = await UploadAsync(["P"]);

        result.Success.ShouldBeFalse();
        result.Error.ShouldBe(expected);
        result.Error!.ShouldNotContain("detalhe-secreto");
        result.Error!.ShouldNotContain("tok-1");
    }

    [Fact]
    [Trait("AC", "010-96")]
    public async Task Upload_FolderCreationFailure_IsMappedToo()
    {
        _handler
            .Enqueue(HttpStatusCode.OK, """{"files":[]}""")
            .Enqueue(HttpStatusCode.Forbidden, """{"error":{"code":403,"message":"x","errors":[{"reason":"storageQuotaExceeded"}]}}""");

        (await UploadAsync(["P"])).Error.ShouldBe("Limite de armazenamento do Google Drive atingido.");
    }

    [Fact]
    [Trait("AC", "010-96")]
    public async Task Upload_NetworkFailure_IsTheGenericText()
    {
        _handler
            .Enqueue(HttpStatusCode.OK, """{"files":[{"id":"p"}]}""")
            .EnqueueException(new HttpRequestException("dns tok-1"));

        var result = await UploadAsync(["P"]);

        result.Error.ShouldBe("Erro ao fazer upload.");
    }
}
