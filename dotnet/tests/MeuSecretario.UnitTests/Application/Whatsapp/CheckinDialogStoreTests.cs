using MeuSecretario.Application.Caching;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Domain.Jobs;
using MeuSecretario.Infrastructure.Whatsapp;
using MeuSecretario.UnitTests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace MeuSecretario.UnitTests.Application.Whatsapp;

/// <summary>O diálogo do check-in (desvio 254): um estado tipado por cima das chaves do Redis, com as transições num lugar só.</summary>
public class CheckinDialogStoreTests
{
    private const string Number = "5531999990001";
    private const string Id = "chk00001";
    private static readonly DateOnly Today = new(2026, 10, 20);

    private readonly FakeTimeProvider _time = new();
    private readonly InMemoryCache _cache;
    private readonly CheckinDialogStore _store;

    public CheckinDialogStoreTests()
    {
        _cache = new InMemoryCache(_time);
        _store = new CheckinDialogStore(_cache, new CheckinSessionState(_cache));
    }

    private static CheckinBill Bill(int id, string kind = "FIXO") =>
        new(id, $"Conta {id}", 100m, 7, 1, 10, 7, "Nubank", "Conta Corrente", "Moradia", "Moradia", "Despesa", kind, null, null, Today);

    private async Task SeedSessionAsync(params CheckinBill[] bills)
    {
        await _cache.SetJsonAsync(CacheKeys.NightlyCheckin(Number, Id), CheckinSession.Build(bills, Today), TimeSpan.FromHours(1));
        await _cache.SetJsonAsync(CacheKeys.NightlyCheckinActive(Number), Id, TimeSpan.FromHours(1));
    }

    private Task<bool> ExistsAsync(string key) => _cache.ExistsAsync(key);

    [Fact]
    public async Task Load_WithoutSession_IsExpired() =>
        (await _store.LoadAsync(Number, Id, CancellationToken.None)).ShouldBeOfType<CheckinDialogState.Expired>();

    [Fact]
    public async Task Load_WithTheJobSession_AwaitsConfirmation()
    {
        await SeedSessionAsync(Bill(1), Bill(2));

        var state = await _store.LoadAsync(Number, Id, CancellationToken.None);

        state.ShouldBeOfType<CheckinDialogState.AwaitingConfirmation>().Session.TotalItems.ShouldBe(2);
    }

    [Fact]
    public async Task StartValues_DropsTheSession_KeepsTheActiveFlag_AndAwaitsValues()
    {
        await SeedSessionAsync(Bill(1, "LEMBRETE_VARIAVEL"));
        var item = CheckinSession.Build([Bill(1, "LEMBRETE_VARIAVEL")], Today).Items["1"];

        await _store.StartValuesAsync(Number, Id, new CheckinPendingValues([item], 1), CancellationToken.None);

        (await ExistsAsync(CacheKeys.NightlyCheckin(Number, Id))).ShouldBeFalse();
        (await ExistsAsync(CacheKeys.NightlyCheckinActive(Number))).ShouldBeTrue();
        var state = await _store.LoadAsync(Number, Id, CancellationToken.None);
        state.ShouldBeOfType<CheckinDialogState.AwaitingValues>().Pending.Items.Single().Id.ShouldBe(1);
    }

    [Fact]
    public async Task SaveValues_RenewsTheActiveFlag()
    {
        var item = CheckinSession.Build([Bill(1, "LEMBRETE_VARIAVEL")], Today).Items["1"];
        await _store.StartValuesAsync(Number, Id, new CheckinPendingValues([item], 1), CancellationToken.None);

        _time.Advance(TimeSpan.FromMinutes(50));
        await _store.SaveValuesAsync(Number, Id, new CheckinPendingValues([item], 1), CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(20));

        // 70 min depois do início, mas só 20 depois da renovação: a flag e a fila continuam valendo.
        (await ExistsAsync(CacheKeys.NightlyCheckinActive(Number))).ShouldBeTrue();
        (await _store.LoadAsync(Number, Id, CancellationToken.None)).ShouldBeOfType<CheckinDialogState.AwaitingValues>();
    }

    [Fact]
    public async Task End_RemovesTheSession_TheValuesAndTheActiveFlag()
    {
        await SeedSessionAsync(Bill(1, "LEMBRETE_VARIAVEL"));
        var item = CheckinSession.Build([Bill(1, "LEMBRETE_VARIAVEL")], Today).Items["1"];
        await _store.SaveValuesAsync(Number, Id, new CheckinPendingValues([item], 1), CancellationToken.None);

        await _store.EndAsync(Number, Id, CancellationToken.None);

        (await ExistsAsync(CacheKeys.NightlyCheckin(Number, Id))).ShouldBeFalse();
        (await ExistsAsync(CacheKeys.NightlyCheckinValues(Number, Id))).ShouldBeFalse();
        (await ExistsAsync(CacheKeys.NightlyCheckinActive(Number))).ShouldBeFalse();
        (await _store.LoadAsync(Number, Id, CancellationToken.None)).ShouldBeOfType<CheckinDialogState.Expired>();
    }

    [Fact]
    public async Task Expire_ClearsOnlyTheActiveFlag()
    {
        await _cache.SetJsonAsync(CacheKeys.NightlyCheckinActive(Number), Id, TimeSpan.FromHours(1));

        await _store.ExpireAsync(Number, CancellationToken.None);

        (await ExistsAsync(CacheKeys.NightlyCheckinActive(Number))).ShouldBeFalse();
    }
}
