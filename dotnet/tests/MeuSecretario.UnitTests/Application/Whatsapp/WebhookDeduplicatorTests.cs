using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.UnitTests.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Whatsapp;

/// <summary>Idempotência do webhook (desvio 259): a mesma mensagem reenviada pelo bot é ignorada; sem id, nada é descartado.</summary>
public class WebhookDeduplicatorTests
{
    private const string Number = "5531999990001@s.whatsapp.net";

    private readonly FakeTimeProvider _time = new();
    private readonly InMemoryCache _cache;
    private readonly CacheWebhookDeduplicator _deduplicator;

    public WebhookDeduplicatorTests()
    {
        _cache = new InMemoryCache(_time);
        _deduplicator = new CacheWebhookDeduplicator(_cache);
    }

    [Fact]
    public async Task TheSameIdTwice_IsADuplicate_TheFirstIsNot()
    {
        (await _deduplicator.IsDuplicateAsync(Number, "ABC123", CancellationToken.None)).ShouldBeFalse();
        (await _deduplicator.IsDuplicateAsync(Number, "ABC123", CancellationToken.None)).ShouldBeTrue();
    }

    [Fact]
    public async Task AnotherIdOrAnotherNumber_IsNotADuplicate()
    {
        await _deduplicator.IsDuplicateAsync(Number, "ABC123", CancellationToken.None);

        (await _deduplicator.IsDuplicateAsync(Number, "OUTRO", CancellationToken.None)).ShouldBeFalse();
        (await _deduplicator.IsDuplicateAsync("5531888880002@s.whatsapp.net", "ABC123", CancellationToken.None)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WithoutAnId_NothingIsEverDiscarded(string? id)
    {
        (await _deduplicator.IsDuplicateAsync(Number, id, CancellationToken.None)).ShouldBeFalse();
        (await _deduplicator.IsDuplicateAsync(Number, id, CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task Release_LetsTheRetryThrough()
    {
        await _deduplicator.IsDuplicateAsync(Number, "ABC123", CancellationToken.None);

        await _deduplicator.ReleaseAsync(Number, "ABC123", CancellationToken.None);

        (await _deduplicator.IsDuplicateAsync(Number, "ABC123", CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task TheMarkExpires()
    {
        await _deduplicator.IsDuplicateAsync(Number, "ABC123", CancellationToken.None);

        _time.Advance(CacheWebhookDeduplicator.Window + TimeSpan.FromSeconds(1));

        (await _deduplicator.IsDuplicateAsync(Number, "ABC123", CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task WhenTheCacheIsDown_TheMessageIsProcessed()
    {
        var broken = Substitute.For<ICache>();
        broken.SetIfNotExistsAsync(default!, default!, default, default).ReturnsForAnyArgs<Task<bool>>(_ => throw new InvalidOperationException("redis fora"));
        broken.DeleteAsync(default!, default).ReturnsForAnyArgs<Task<bool>>(_ => throw new InvalidOperationException("redis fora"));
        var deduplicator = new CacheWebhookDeduplicator(broken);

        (await deduplicator.IsDuplicateAsync(Number, "ABC123", CancellationToken.None)).ShouldBeFalse();
        await deduplicator.ReleaseAsync(Number, "ABC123", CancellationToken.None);
    }

    [Theory]
    [InlineData("ok", true)]
    [InlineData("3EB0C767D26A1B2C4F5E", true)]
    [InlineData("com\nquebra", false)]
    public void Key_RejectsUnusableIds(string id, bool usable) =>
        (CacheWebhookDeduplicator.Key(Number, id) is not null).ShouldBe(usable);

    [Fact]
    public void Key_RejectsATooLongId() =>
        CacheWebhookDeduplicator.Key(Number, new string('a', 129)).ShouldBeNull();
}
