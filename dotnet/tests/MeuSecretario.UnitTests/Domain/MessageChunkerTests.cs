using MeuSecretario.Domain.Common;

namespace MeuSecretario.UnitTests.Domain;

public class MessageChunkerTests
{
    [Fact]
    public void Split_ShortText_IsASingleUntouchedPart()
    {
        MessageChunker.Split("ola").ShouldBe(["ola"]);
        MessageChunker.Split(new string('a', 4096)).Count.ShouldBe(1);
    }

    [Fact]
    public void Split_LongText_CutsAtTheLastNewlineInsideTheWindow()
    {
        var first = new string('a', 3000);
        var second = new string('b', 3000);
        var text = first + "\n" + second;

        MessageChunker.Split(text).ShouldBe([first, second]);
    }

    [Fact]
    public void Split_NoNewline_CutsAtTheLimit_AndLosesNothing()
    {
        var text = new string('x', 4096 * 2 + 10);

        var parts = MessageChunker.Split(text);

        parts.Select(p => p.Length).ShouldBe([4096, 4096, 10]);
        string.Concat(parts).ShouldBe(text);
    }

    [Fact]
    public void Split_NeverBreaksASurrogatePair()
    {
        var text = new string('a', 4095) + "😀" + "tail";

        var parts = MessageChunker.Split(text);

        parts.ShouldAllBe(p => p.Length <= 4096);
        parts[0].Length.ShouldBe(4095);
        string.Concat(parts).ShouldBe(text);
    }

    [Fact]
    public void Split_ManyLines_EveryPartFitsAndOrderIsPreserved()
    {
        var lines = Enumerable.Range(0, 2000).Select(i => $"linha {i} com algum texto de relatório").ToArray();
        var text = string.Join('\n', lines);

        var parts = MessageChunker.Split(text);

        parts.Count.ShouldBeGreaterThan(1);
        parts.ShouldAllBe(p => p.Length <= MessageChunker.MaxLength);
        string.Join('\n', parts).ShouldBe(text);
    }
}
