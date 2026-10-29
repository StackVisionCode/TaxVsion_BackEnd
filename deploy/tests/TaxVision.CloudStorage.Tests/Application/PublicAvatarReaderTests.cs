using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Caching.Memory;
using TaxVision.CloudStorage.Application.Files.Avatars;
using TaxVision.CloudStorage.Infrastructure.Storage;
using TaxVision.CloudStorage.Tests.Api;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>Fotos de perfil F0 — PublicAvatarReader: allowlist de tipos, caché de aciertos y desalojo por evento.</summary>
public sealed class PublicAvatarReaderTests
{
    private static PublicAvatarDescriptor Descriptor(string contentType = "image/webp") =>
        new(Guid.NewGuid(), "tenants/t/user/u/avatars/x.webp", contentType, AvatarTestFiles.Sha256, 10);

    private static MemoryPublicAvatarCache NewCache() => new(new MemoryCache(new MemoryCacheOptions()));

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("text/html")]
    [InlineData("image/gif")]
    public async Task Content_types_outside_the_image_allowlist_are_never_served(string contentType)
    {
        var lookup = new FakePublicAvatarLookup();
        var avatar = Descriptor(contentType);
        lookup.Add(avatar);
        var cache = NewCache();

        var resolved = await new PublicAvatarReader(lookup, cache).ResolveAsync(avatar.FileId, CancellationToken.None);

        Assert.Null(resolved);
        Assert.False(cache.TryGet(avatar.FileId, out _));
    }

    [Fact]
    public async Task Misses_are_not_cached_so_an_avatar_that_finishes_scanning_is_served_next_time()
    {
        var lookup = new FakePublicAvatarLookup();
        var cache = NewCache();
        var reader = new PublicAvatarReader(lookup, cache);
        var avatar = Descriptor();

        Assert.Null(await reader.ResolveAsync(avatar.FileId, CancellationToken.None));
        lookup.Add(avatar);

        Assert.Equal(avatar, await reader.ResolveAsync(avatar.FileId, CancellationToken.None));
    }

    [Fact]
    public async Task Revalidate_drops_the_cached_entry_and_asks_the_database_again()
    {
        var lookup = new FakePublicAvatarLookup();
        var cache = NewCache();
        var reader = new PublicAvatarReader(lookup, cache);
        var avatar = Descriptor();
        lookup.Add(avatar);
        await reader.ResolveAsync(avatar.FileId, CancellationToken.None);
        lookup.Remove(avatar.FileId);

        Assert.Null(await reader.RevalidateAsync(avatar.FileId, CancellationToken.None));
        Assert.False(cache.TryGet(avatar.FileId, out _));
    }

    [Fact]
    public void FileDeleted_and_dmca_takedown_evict_the_cached_avatar()
    {
        var cache = NewCache();
        var deleted = Descriptor();
        var takenDown = Descriptor();
        cache.Set(deleted);
        cache.Set(takenDown);

        PublicAvatarCacheEvictionConsumer.Handle(
            new FileDeletedIntegrationEvent
            {
                TenantId = Guid.NewGuid(),
                FileId = deleted.FileId,
                CreatedBy = Guid.NewGuid(),
            },
            cache
        );
        PublicAvatarCacheEvictionConsumer.Handle(
            new FileBlockedByDmcaTakedownIntegrationEvent
            {
                TenantId = Guid.NewGuid(),
                FileId = takenDown.FileId,
                DmcaNoticeId = Guid.NewGuid(),
                CreatedBy = Guid.NewGuid(),
            },
            cache
        );

        Assert.False(cache.TryGet(deleted.FileId, out _));
        Assert.False(cache.TryGet(takenDown.FileId, out _));
    }

    [Fact]
    public void Cache_entries_expire_within_ten_minutes_at_worst()
    {
        Assert.True(MemoryPublicAvatarCache.TimeToLive <= TimeSpan.FromMinutes(10));
    }
}
