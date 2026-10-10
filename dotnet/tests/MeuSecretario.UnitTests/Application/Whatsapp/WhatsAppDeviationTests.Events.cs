using MeuSecretario.Application.Automation;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Infrastructure.Whatsapp;
using MeuSecretario.UnitTests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace MeuSecretario.UnitTests.Application.Whatsapp;

// Desvio 155: o evento pendente "mais recente" é o último criado (ponteiro last_pending_event).
public partial class WhatsAppDeviationTests
{
    private sealed class SequenceIds(params string[] ids) : IPendingIdGenerator
    {
        private int _next;

        public string NewId() => ids[_next++];
    }

    private static PendingEvent Event(string title) => new(7, title, "2026-10-12", "07:00", null, null, null);

    [Theory]
    [Trait("AC", "012-502")]
    [InlineData("aaaa0001", "zzzz0001")]
    [InlineData("zzzz0001", "aaaa0001")]
    public async Task Desvio155_LatestIsTheLastCreatedRegardlessOfKeyOrder(string firstId, string secondId)
    {
        var cache = new InMemoryCache(new FakeTimeProvider());
        var store = new PendingEventStore(cache, new SequenceIds(firstId, secondId));
        await store.CreateAsync(Number, Event("Primeiro"), CancellationToken.None);
        await store.CreateAsync(Number, Event("Segundo"), CancellationToken.None);

        var latest = await store.GetLatestAsync(Number, CancellationToken.None);

        latest.Id.ShouldBe(secondId);
        latest.Data!.Title.ShouldBe("Segundo");
        cache.RawValue("last_pending_event:" + Number).ShouldBe($"\"{secondId}\"");
    }

    [Fact]
    [Trait("AC", "012-502")]
    public async Task Desvio155_DeletingTheLatestClearsThePointerAndFallsBackToTheRemainingEvent()
    {
        var cache = new InMemoryCache(new FakeTimeProvider());
        var store = new PendingEventStore(cache, new SequenceIds("aaaa0001", "bbbb0002"));
        await store.CreateAsync(Number, Event("Primeiro"), CancellationToken.None);
        await store.CreateAsync(Number, Event("Segundo"), CancellationToken.None);

        await store.DeleteAsync(Number, "bbbb0002", CancellationToken.None);

        cache.RawValue("last_pending_event:" + Number).ShouldBeNull();
        var latest = await store.GetLatestAsync(Number, CancellationToken.None);
        latest.Id.ShouldBe("aaaa0001");

        await store.DeleteAsync(Number, "aaaa0001", CancellationToken.None);
        (await cache.KeysByPatternAsync("*")).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "012-502")]
    public async Task Desvio155_WithoutPointerTheLastKeyIsUsedLikeTheFlask()
    {
        var cache = new InMemoryCache(new FakeTimeProvider());
        var store = new PendingEventStore(cache, new SequenceIds("unused"));
        await cache.SetJsonAsync("pending_event:" + Number + ":aaaa0001", Event("Antigo"), TimeSpan.FromSeconds(300));

        (await store.GetLatestAsync(Number, CancellationToken.None)).Id.ShouldBe("aaaa0001");
    }
}
