using MeuSecretario.Api.Google;
using MeuSecretario.UnitTests.Application.Google;

namespace MeuSecretario.UnitTests.Api.Google;

/// <summary>Páginas B.1 a B.4 comparadas byte a byte com o que o Flask devolve (fixture gerada pelo Python).</summary>
public class GooglePagesTests
{
    private static string Page(string name) => GoogleFixtures.Root.GetProperty("pages").GetProperty(name).GetString()!;

    [Fact]
    [Trait("AC", "010-71")]
    public void AlreadyConnected_IsIdenticalToThePython()
    {
        GooglePages.AlreadyConnected.ShouldBe(Page("already_connected"));
        GooglePages.AlreadyConnected.ShouldContain("<title>Ja Conectado</title>");
    }

    [Fact]
    [Trait("AC", "010-76")]
    public void Denied_IsIdenticalToThePython()
    {
        GooglePages.Denied.ShouldBe(Page("denied"));
        GooglePages.Denied.ShouldContain("Autorizacao Negada");
    }

    [Fact]
    [Trait("AC", "010-77")]
    public void Connected_IsIdenticalToThePythonWithTheNameFilledIn()
    {
        GooglePageRenderer.Connected("Maria").ShouldBe(Page("connected").Replace("{nome}", "Maria", StringComparison.Ordinal));
        GooglePageRenderer.Connected("Maria").ShouldContain("<p>Ola <strong>Maria</strong>!</p>");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Connected_WithoutAName_UsesUsuario(string? name)
    {
        GooglePageRenderer.Connected(name).ShouldContain("<strong>Usuario</strong>");
    }

    [Fact]
    [Trait("AC", "010-77")]
    public void Connected_EscapesHtmlInTheName_Deviation060()
    {
        var html = GooglePageRenderer.Connected("""<script>alert("x")</script> & João""");

        html.ShouldContain("<strong>&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt; &amp; João</strong>");
        html.ShouldNotContain("<script>");
    }

    [Theory]
    [InlineData(GoogleMessages.InvalidStateText)]
    [InlineData(GoogleMessages.InternalErrorText)]
    [Trait("AC", "010-80")]
    public void Error_IsIdenticalToThePythonWithTheFixedText(string text)
    {
        GooglePageRenderer.Error(text).ShouldBe(Page("error").Replace("{texto}", text, StringComparison.Ordinal));
        GooglePageRenderer.Error(text).ShouldContain($"<p><code>{text}</code></p>");
    }

    [Fact]
    [Trait("AC", "010-53")]
    public void Forbidden_IsSimpleHtmlWithoutPersonalData()
    {
        var html = GooglePageRenderer.Forbidden();

        html.ShouldContain("<html>");
        html.ShouldContain("Link invalido");
    }
}
