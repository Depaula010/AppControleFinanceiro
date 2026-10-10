using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Domain.Whatsapp;
using MeuSecretario.UnitTests.Application.Whatsapp.Calendar;
using MeuSecretario.UnitTests.Domain.Whatsapp;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Whatsapp.Drive;

public class DriveUploadIntentTests
{
    [Fact]
    [Trait("AC", "012-801")]
    public void FolderPath_MatchesTheFlask()
    {
        var cases = WhatsAppFixture.Cases("drive_folder_cases");
        cases.Count.ShouldBeGreaterThan(20);

        foreach (var c in cases)
        {
            DriveFolderPath.Extract(c.GetProperty("text").GetString())
                .ShouldBe(c.GetProperty("path").EnumerateArray().Select(p => p.GetString()!), c.GetProperty("text").GetString());
        }
    }

    [Fact]
    [Trait("AC", "012-801")]
    public void FolderPath_KeepsTheUserCase_ButTitlesAllLowercaseNames()
    {
        DriveFolderPath.Extract("pasta IR 2026").ShouldBe(["IR 2026"]);
        DriveFolderPath.Extract("pasta ir 2026").ShouldBe(["Ir 2026"]);
        DriveFolderPath.Extract(null).ShouldBe(["WhatsApp Uploads"]);
    }

    [Fact]
    [Trait("AC", "012-802")]
    public void Base64_MatchesPythonB64Decode()
    {
        foreach (var c in WhatsAppFixture.Cases("base64_cases"))
        {
            var decoded = PyBase64.TryDecode(c.GetProperty("text").GetString()!);
            if (c.GetProperty("bytes").ValueKind == JsonValueKind.Null)
            {
                decoded.ShouldBeNull(c.GetProperty("text").GetString());
            }
            else
            {
                decoded.ShouldBe(c.GetProperty("bytes").EnumerateArray().Select(b => (byte)b.GetInt32()).ToArray(), c.GetProperty("text").GetString());
            }
        }
    }

    private sealed class Env
    {
        public Env(JsonElement c)
        {
            Rig = new CalendarRig("2026-10-08T14:20:00-03:00");
            Account = Substitute.For<IGoogleAccountService>();
            Account.HasDriveScopeAsync(7, Arg.Any<CancellationToken>()).Returns(c.GetProperty("scope").GetBoolean());
            Account.GetCredentialsAsync(7, Arg.Any<CancellationToken>()).Returns(
                c.GetProperty("service_ok").GetBoolean()
                    ? new GoogleCredentialResult(GoogleCredentialStatus.Connected, "token", [])
                    : GoogleCredentialResult.Of(GoogleCredentialStatus.NeedsReconnect));

            Drive = Substitute.For<IDriveClient>();
            var upload = c.GetProperty("upload");
            Drive.UploadFileAsync(Arg.Any<int>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ => ToResult(upload));

            var redirect = c.GetProperty("redirect").GetString()!;
            var links = CalendarRig.Links(redirect, Rig.Time.GetUtcNow());
            Intent = new DriveUploadIntent(Account, Drive, links);
        }

        public CalendarRig Rig { get; }

        public IGoogleAccountService Account { get; }

        public IDriveClient Drive { get; }

        public DriveUploadIntent Intent { get; }

        private static DriveUploadResult ToResult(JsonElement upload)
        {
            string? Text(string name) => upload.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            return upload.GetProperty("success").GetBoolean()
                ? new DriveUploadResult(true, Text("file_id"), Text("filename"), Text("folder_name"), Text("folder_path_display"), Text("folder_id"), Text("web_view_link"), null, null)
                : DriveUploadResult.Failure(Text("error") ?? "x");
        }
    }

    private static MediaPayload? Media(JsonElement c)
    {
        var data = c.GetProperty("data");
        var mime = c.GetProperty("mime");
        var file = c.GetProperty("filename");
        return new MediaPayload(
            data.ValueKind == JsonValueKind.String && data.GetString()!.Length > 0,
            data.ValueKind == JsonValueKind.String ? data.GetString() : null,
            mime.ValueKind == JsonValueKind.String && mime.GetString()!.Length > 0 ? mime.GetString() : null,
            file.ValueKind == JsonValueKind.String ? file.GetString() : null);
    }

    [Fact]
    [Trait("AC", "012-802")]
    public async Task Upload_MatchesTheFlask_ForEveryScenario()
    {
        var cases = WhatsAppFixture.Cases("drive_cases");
        cases.Count.ShouldBeGreaterThan(20);

        foreach (var c in cases)
        {
            var name = c.GetProperty("name").GetString()!;
            var env = new Env(c);
            var context = new IntentContext(env.Rig.User, CalendarRig.Number, c.GetProperty("text").GetString()!, Media(c));

            var reply = await env.Intent.HandleAsync(context, CancellationToken.None);

            // O link de conexão é assinado (desvio 053); o Flask não assinava.
            // Desvio 159 (2026-10-10): o Flask respondia sempre o genérico; o cenário antigo fica no JSON como evidência.
            var expected = name == "falha_no_upload"
                ? "❌ " + c.GetProperty("upload").GetProperty("error").GetString()
                : c.GetProperty("message").GetString();
            CalendarRig.WithoutToken(reply).ShouldBe(expected, name);

            var uploaded = c.GetProperty("uploaded").EnumerateArray().ToList();
            var calls = env.Drive.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(IDriveClient.UploadFileAsync)).ToList();
            calls.Count.ShouldBe(uploaded.Count, name);
            if (uploaded.Count == 1)
            {
                var args = calls[0].GetArguments();
                ((string)args[2]!).ShouldBe(uploaded[0].GetProperty("filename").GetString(), name);
                ((IReadOnlyList<string>)args[3]!).ShouldBe(uploaded[0].GetProperty("folder_path").EnumerateArray().Select(x => x.GetString()!), name);
                ((string)args[4]!).ShouldBe(uploaded[0].GetProperty("mime").GetString(), name);
                ((ReadOnlyMemory<byte>)args[1]!).Length.ShouldBe(uploaded[0].GetProperty("size").GetInt32(), name);
            }
        }
    }

    [Fact]
    [Trait("AC", "012-802")]
    public async Task Upload_SignedConnectLinkCarriesTheToken()
    {
        var c = WhatsAppFixture.Cases("drive_cases").Single(x => x.GetProperty("name").GetString() == "sem_escopo");
        var env = new Env(c);

        var reply = await env.Intent.HandleAsync(new IntentContext(env.Rig.User, CalendarRig.Number, "salvar", Media(c)), CancellationToken.None);

        reply.ShouldContain("https://proxy.example/oauth/connect-calendar/7?t=");
    }

    [Fact]
    [Trait("AC", "012-802")]
    public async Task Upload_WhenTheDriveClientSaysNotConnected_AnswersTheAuthError()
    {
        var c = WhatsAppFixture.Cases("drive_cases").Single(x => x.GetProperty("name").GetString() == "sucesso");
        var env = new Env(c);
        env.Drive.UploadFileAsync(default, default, default!, default!, default!, default)
            .ReturnsForAnyArgs<DriveUploadResult>(_ => throw new GoogleNotConnectedException("segredo"));

        var reply = await env.Intent.HandleAsync(new IntentContext(env.Rig.User, CalendarRig.Number, "salvar", Media(c)), CancellationToken.None);

        reply.ShouldStartWith("⚠️ *Erro de autenticação*");
        reply.ShouldNotContain("segredo");
    }

    [Fact]
    [Trait("AC", "012-802")]
    public async Task Upload_WhenTheClientBreaks_AnswersTheGenericFailure()
    {
        var c = WhatsAppFixture.Cases("drive_cases").Single(x => x.GetProperty("name").GetString() == "sucesso");
        var env = new Env(c);
        env.Drive.UploadFileAsync(default, default, default!, default!, default!, default)
            .ReturnsForAnyArgs<DriveUploadResult>(_ => throw new InvalidOperationException("detalhe interno"));

        var reply = await env.Intent.HandleAsync(new IntentContext(env.Rig.User, CalendarRig.Number, "salvar", Media(c)), CancellationToken.None);

        reply.ShouldBe("❌ Erro ao enviar arquivo para o Drive.");
    }

    [Theory]
    [Trait("AC", "012-159")]
    [InlineData("Limite de armazenamento do Google Drive atingido.")]
    [InlineData("Sem permissão para acessar o Google Drive. Por favor, reconecte sua conta Google.")]
    [InlineData("Credenciais do Google expiraram. Por favor, reconecte sua conta.")]
    public async Task Upload_WhenTheUploadFailsWithAReason_ShowsTheReason(string reason)
    {
        var c = WhatsAppFixture.Cases("drive_cases").Single(x => x.GetProperty("name").GetString() == "sucesso");
        var env = new Env(c);
        env.Drive.UploadFileAsync(default, default, default!, default!, default!, default)
            .ReturnsForAnyArgs(DriveUploadResult.Failure(reason));

        var reply = await env.Intent.HandleAsync(new IntentContext(env.Rig.User, CalendarRig.Number, "salvar", Media(c)), CancellationToken.None);

        reply.ShouldBe("❌ " + reason);
    }

    [Fact]
    [Trait("AC", "012-159")]
    public async Task Upload_WhenTheUploadFailsWithoutAReason_AnswersTheGenericFailure()
    {
        var c = WhatsAppFixture.Cases("drive_cases").Single(x => x.GetProperty("name").GetString() == "sucesso");
        var env = new Env(c);
        env.Drive.UploadFileAsync(default, default, default!, default!, default!, default)
            .ReturnsForAnyArgs(DriveUploadResult.Failure(""));

        var reply = await env.Intent.HandleAsync(new IntentContext(env.Rig.User, CalendarRig.Number, "salvar", Media(c)), CancellationToken.None);

        reply.ShouldBe("❌ Erro ao enviar arquivo para o Drive.");
    }

    [Fact]
    [Trait("AC", "012-802")]
    public async Task Upload_WithoutMedia_AsksForTheFile()
    {
        var c = WhatsAppFixture.Cases("drive_cases").Single(x => x.GetProperty("name").GetString() == "sucesso");
        var env = new Env(c);

        var reply = await env.Intent.HandleAsync(new IntentContext(env.Rig.User, CalendarRig.Number, "salvar", null), CancellationToken.None);

        reply.ShouldStartWith("📎 Nenhum arquivo recebido.");
    }
}
