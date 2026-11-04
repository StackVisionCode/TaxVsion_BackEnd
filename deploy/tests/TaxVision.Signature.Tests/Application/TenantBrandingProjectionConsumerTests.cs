using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Projections;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Tests.Application;

/// <summary>
/// Logos por superficie (un logo borrado deja el certificado), nombre de la oficina al día con
/// TenantUpdated y nombre comercial con TenantProfileChanged, ignorando eventos viejos.
/// </summary>
public sealed class TenantBrandingProjectionConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

    private static TenantLogoUpdatedIntegrationEvent LogoUpdated(Guid fileId, DateTime at, string surface) =>
        new()
        {
            TenantId = TenantId,
            Surface = surface,
            CloudStorageFileId = fileId,
            ContentType = "image/png",
            SizeBytes = 100,
            UpdatedAtUtc = at,
        };

    private static TenantLogoRemovedIntegrationEvent LogoRemoved(DateTime at, string surface) =>
        new()
        {
            TenantId = TenantId,
            Surface = surface,
            RemovedAtUtc = at,
        };

    private static TenantUpdatedIntegrationEvent Renamed(string name, DateTime at) =>
        new()
        {
            TenantId = TenantId,
            UpdatedTenantId = TenantId,
            Name = name,
            DefaultTimeZoneId = "America/New_York",
            UpdatedAtUtc = at,
        };

    private static Task HandleAsync(BrandingRepo repo, TenantLogoUpdatedIntegrationEvent evt) =>
        TenantBrandingProjectionConsumer.Handle(evt, repo, new Uow(), new Correlation(), Log, CancellationToken.None);

    private static Task HandleAsync(BrandingRepo repo, TenantLogoRemovedIntegrationEvent evt) =>
        TenantBrandingProjectionConsumer.Handle(evt, repo, new Uow(), new Correlation(), Log, CancellationToken.None);

    private static Task HandleAsync(BrandingRepo repo, TenantUpdatedIntegrationEvent evt) =>
        TenantBrandingProjectionConsumer.Handle(evt, repo, new Uow(), new Correlation(), Log, CancellationToken.None);

    private static TenantProfileChangedIntegrationEvent ProfileChanged(long version, string? displayName) =>
        new()
        {
            TenantId = TenantId,
            ProfileTenantId = TenantId,
            Version = version,
            UpdatedAtUtc = Now,
            DisplayName = displayName,
        };

    private static Task HandleAsync(BrandingRepo repo, TenantProfileChangedIntegrationEvent evt) =>
        TenantBrandingProjectionConsumer.Handle(evt, repo, new Uow(), new Correlation(), Log, CancellationToken.None);

    private static NullLogger<TenantBrandingRef> Log => NullLogger<TenantBrandingRef>.Instance;

    [Fact]
    public async Task Crm_and_portal_logos_are_stored_per_surface()
    {
        var repo = new BrandingRepo();
        var crm = Guid.NewGuid();
        var portal = Guid.NewGuid();

        await HandleAsync(repo, LogoUpdated(crm, Now, TenantBrandSurfaces.Crm));
        await HandleAsync(repo, LogoUpdated(portal, Now, TenantBrandSurfaces.Portal));

        Assert.Equal(crm, (await repo.GetLogoAsync(TenantId, TenantLogoSurface.Crm))!.LogoFileId);
        Assert.Equal(portal, (await repo.GetLogoAsync(TenantId, TenantLogoSurface.Portal))!.LogoFileId);
    }

    [Fact]
    public async Task A_removed_logo_leaves_the_certificate_and_the_other_surface_stays()
    {
        var repo = new BrandingRepo();
        await HandleAsync(repo, LogoUpdated(Guid.NewGuid(), Now, TenantBrandSurfaces.Crm));
        await HandleAsync(repo, LogoUpdated(Guid.NewGuid(), Now, TenantBrandSurfaces.Portal));

        await HandleAsync(repo, LogoRemoved(Now.AddMinutes(1), TenantBrandSurfaces.Portal));

        Assert.False((await repo.GetLogoAsync(TenantId, TenantLogoSurface.Portal))!.HasLogo);
        Assert.True((await repo.GetLogoAsync(TenantId, TenantLogoSurface.Crm))!.HasLogo);
    }

    [Fact]
    public async Task A_removal_without_surface_in_an_old_message_clears_the_crm_logo()
    {
        var repo = new BrandingRepo();
        await HandleAsync(repo, LogoUpdated(Guid.NewGuid(), Now, TenantBrandSurfaces.Crm));

        await HandleAsync(
            repo,
            new TenantLogoRemovedIntegrationEvent { TenantId = TenantId, RemovedAtUtc = Now.AddMinutes(1) }
        );

        Assert.False((await repo.GetLogoAsync(TenantId, TenantLogoSurface.Crm))!.HasLogo);
    }

    [Fact]
    public async Task Older_logo_events_do_not_override_a_newer_logo()
    {
        var repo = new BrandingRepo();
        var current = Guid.NewGuid();
        await HandleAsync(repo, LogoUpdated(current, Now, TenantBrandSurfaces.Crm));

        await HandleAsync(repo, LogoUpdated(Guid.NewGuid(), Now.AddMinutes(-5), TenantBrandSurfaces.Crm));
        await HandleAsync(repo, LogoRemoved(Now.AddMinutes(-1), TenantBrandSurfaces.Crm));

        Assert.Equal(current, (await repo.GetLogoAsync(TenantId, TenantLogoSurface.Crm))!.LogoFileId);
    }

    [Fact]
    public async Task A_replayed_logo_event_leaves_a_single_row()
    {
        var repo = new BrandingRepo();
        var evt = LogoUpdated(Guid.NewGuid(), Now, TenantBrandSurfaces.Portal);

        await HandleAsync(repo, evt);
        await HandleAsync(repo, evt);

        Assert.Single(await repo.ListLogosAsync(TenantId));
    }

    [Fact]
    public async Task A_logo_with_an_unknown_surface_is_ignored()
    {
        var repo = new BrandingRepo();

        await HandleAsync(repo, LogoUpdated(Guid.NewGuid(), Now, "Kiosk"));

        Assert.Empty(await repo.ListLogosAsync(TenantId));
    }

    [Fact]
    public async Task TenantUpdated_keeps_the_office_name_current()
    {
        var repo = new BrandingRepo();
        repo.Seed(TenantBrandingRef.Create(TenantId, Now));

        await HandleAsync(repo, Renamed("Old Name", Now));
        await HandleAsync(repo, Renamed("New Name", Now.AddMinutes(1)));

        Assert.Equal("New Name", (await repo.GetByTenantIdAsync(TenantId))!.OfficeName);
    }

    [Fact]
    public async Task An_older_TenantUpdated_is_ignored()
    {
        var repo = new BrandingRepo();
        repo.Seed(TenantBrandingRef.Create(TenantId, Now));

        await HandleAsync(repo, Renamed("New Name", Now));
        await HandleAsync(repo, Renamed("Old Name", Now.AddMinutes(-1)));

        Assert.Equal("New Name", (await repo.GetByTenantIdAsync(TenantId))!.OfficeName);
    }

    [Fact]
    public async Task TenantUpdated_without_a_row_creates_it_with_the_name()
    {
        var repo = new BrandingRepo();

        await HandleAsync(repo, Renamed("Acme Tax", Now));

        Assert.Equal("Acme Tax", (await repo.GetByTenantIdAsync(TenantId))!.OfficeName);
    }

    [Fact]
    public async Task A_replayed_TenantCreated_does_not_undo_a_later_rename()
    {
        var repo = new BrandingRepo();
        var created = new TenantCreatedIntegrationEvent
        {
            NewTenantId = TenantId,
            Name = "Original",
            SubDomain = "acme",
            AdminEmail = "a@b.com",
            AdminInvitationTokenHash = "h",
            OccurredOn = Now,
        };
        await TenantBrandingProjectionConsumer.Handle(
            created,
            repo,
            new Uow(),
            new Correlation(),
            Log,
            CancellationToken.None
        );
        await HandleAsync(repo, Renamed("Renamed", Now.AddMinutes(5)));

        await TenantBrandingProjectionConsumer.Handle(
            created,
            repo,
            new Uow(),
            new Correlation(),
            Log,
            CancellationToken.None
        );

        Assert.Equal("Renamed", (await repo.GetByTenantIdAsync(TenantId))!.OfficeName);
    }

    [Fact]
    public async Task TenantProfileChanged_makes_the_display_name_the_issuer()
    {
        var repo = new BrandingRepo();
        await HandleAsync(repo, Renamed("Acme Tax Office", Now));

        await HandleAsync(repo, ProfileChanged(1, "Acme Refunds"));

        var branding = (await repo.GetByTenantIdAsync(TenantId))!;
        Assert.Equal("Acme Refunds", branding.IssuerDisplayName);
        Assert.Equal("Acme Tax Office", branding.OfficeName);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public async Task A_same_or_older_TenantProfileChanged_is_ignored(long version)
    {
        var repo = new BrandingRepo();
        await HandleAsync(repo, Renamed("Acme Tax Office", Now));
        await HandleAsync(repo, ProfileChanged(2, "Current"));

        await HandleAsync(repo, ProfileChanged(version, "Stale"));

        var branding = (await repo.GetByTenantIdAsync(TenantId))!;
        Assert.Equal("Current", branding.DisplayName);
        Assert.Equal(2, branding.ProfileVersion);
    }

    [Fact]
    public async Task An_empty_display_name_falls_back_to_the_office_name()
    {
        var repo = new BrandingRepo();
        await HandleAsync(repo, Renamed("Acme Tax Office", Now));
        await HandleAsync(repo, ProfileChanged(1, "Acme Refunds"));

        await HandleAsync(repo, ProfileChanged(2, ""));

        Assert.Equal("Acme Tax Office", (await repo.GetByTenantIdAsync(TenantId))!.IssuerDisplayName);
    }

    [Fact]
    public async Task TenantProfileChanged_without_a_row_creates_it_and_a_later_rename_keeps_the_display_name()
    {
        var repo = new BrandingRepo();

        await HandleAsync(repo, ProfileChanged(1, "Acme Refunds"));
        await HandleAsync(repo, Renamed("Acme Tax Office", Now));

        var branding = (await repo.GetByTenantIdAsync(TenantId))!;
        Assert.Equal(1, branding.ProfileVersion);
        Assert.Equal("Acme Tax Office", branding.OfficeName);
        Assert.Equal("Acme Refunds", branding.IssuerDisplayName);
    }

    private sealed class BrandingRepo : ITenantBrandingRefRepository
    {
        private readonly Dictionary<Guid, TenantBrandingRef> _brandings = new();
        private readonly List<TenantLogoRef> _logos = [];

        public void Seed(TenantBrandingRef branding) => _brandings[branding.TenantId] = branding;

        public Task<TenantBrandingRef?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(_brandings.GetValueOrDefault(tenantId));

        public Task AddAsync(TenantBrandingRef branding, CancellationToken ct = default)
        {
            _brandings[branding.TenantId] = branding;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TenantBrandingRef>> ListAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantBrandingRef>>([.. _brandings.Values]);

        public Task<TenantLogoRef?> GetLogoAsync(
            Guid tenantId,
            TenantLogoSurface surface,
            CancellationToken ct = default
        ) => Task.FromResult(_logos.FirstOrDefault(l => l.TenantId == tenantId && l.Surface == surface));

        public Task<IReadOnlyList<TenantLogoRef>> ListLogosAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantLogoRef>>(_logos.Where(l => l.TenantId == tenantId).ToList());

        public Task AddLogoAsync(TenantLogoRef logo, CancellationToken ct = default)
        {
            _logos.Add(logo);
            return Task.CompletedTask;
        }
    }

    private sealed class Uow : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }

    private sealed class Correlation : ICorrelationContext
    {
        public string CorrelationId => string.Empty;

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new Scope();

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
