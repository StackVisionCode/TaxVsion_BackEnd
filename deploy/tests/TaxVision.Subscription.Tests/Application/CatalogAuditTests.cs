using TaxVision.Subscription.Application.Catalog.Audit;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Application;

// Auditoría de plataforma del catálogo: quién, qué, antes y después, motivo y correlación. Solo se agrega.
public sealed class CatalogAuditTests
{
    [Fact]
    public void The_actor_says_who_changed_the_catalog()
    {
        var admin = Guid.NewGuid();

        Assert.Equal(CatalogAuditActorKind.PlatformAdmin, CatalogAuditActor.ForUser(admin).Kind);
        Assert.Equal(admin, CatalogAuditActor.ForUser(admin).UserId);
        Assert.Equal(CatalogAuditActorKind.System, CatalogAuditActor.ForUser(Guid.Empty).Kind);
        Assert.Equal("deploy:2026-11-11-r2", CatalogAuditActor.Deploy("2026-11-11-r2").Label);
        Assert.Equal("system:reconciler", CatalogAuditActor.System("reconciler").Label);
    }

    [Fact]
    public void An_entry_needs_an_action_and_an_entity()
    {
        var actor = CatalogAuditActor.ForUser(Guid.NewGuid());

        var missingAction = CatalogAuditEntry.Record(
            actor,
            " ",
            new CatalogAuditTarget("Plan", null, "pro"),
            null,
            null,
            null,
            null,
            DateTime.UtcNow
        );
        var missingEntity = CatalogAuditEntry.Record(
            actor,
            "plan.rank",
            new CatalogAuditTarget("", null, "pro"),
            null,
            null,
            null,
            null,
            DateTime.UtcNow
        );

        Assert.Equal("CatalogAudit.ActionRequired", missingAction.Error.Code);
        Assert.Equal("CatalogAudit.EntityRequired", missingEntity.Error.Code);
    }

    [Fact]
    public async Task Recording_serializes_before_and_after()
    {
        var log = new InMemoryCatalogAuditLog();

        await CatalogAudit.RecordAsync(
            log,
            CatalogAuditActor.ForUser(Guid.NewGuid()),
            "addon.prices",
            new CatalogAuditTarget("AddOn", Guid.NewGuid(), "addon.storage.small"),
            before: new { Monthly = 3m },
            after: new { Monthly = 4m },
            reason: null,
            correlationId: "corr-9",
            DateTime.UtcNow,
            CancellationToken.None
        );

        var entry = Assert.Single(log.Entries);
        Assert.Equal("{\"Monthly\":3}", entry.BeforeJson);
        Assert.Equal("{\"Monthly\":4}", entry.AfterJson);
        Assert.Equal("corr-9", entry.CorrelationId);
    }

    [Fact]
    public async Task The_audit_query_returns_the_newest_first_filtered_and_capped()
    {
        var log = new InMemoryCatalogAuditLog();
        var planId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        for (var minutes = 3; minutes >= 1; minutes--)
            await CatalogAudit.RecordAsync(
                log,
                CatalogAuditActor.ForUser(Guid.NewGuid()),
                $"plan.change{minutes}",
                new CatalogAuditTarget("Plan", planId, "pro"),
                null,
                null,
                null,
                "c",
                now.AddMinutes(-minutes),
                CancellationToken.None
            );
        await CatalogAudit.RecordAsync(
            log,
            CatalogAuditActor.ForUser(Guid.NewGuid()),
            "module.updated",
            new CatalogAuditTarget("Module", Guid.NewGuid(), "email"),
            null,
            null,
            null,
            "c",
            now,
            CancellationToken.None
        );

        var result = await GetCatalogAuditHandler.Handle(
            new GetCatalogAuditQuery(EntityType: "Plan", EntityId: planId, BeforeUtc: null, Take: 2),
            log,
            CancellationToken.None
        );

        Assert.Equal(["plan.change1", "plan.change2"], result.Value.Select(entry => entry.Action));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(500, 200)]
    public async Task The_page_size_stays_between_1_and_200(int requested, int expected)
    {
        var log = new InMemoryCatalogAuditLog();
        for (var i = 0; i < 210; i++)
            await CatalogAudit.RecordAsync(
                log,
                CatalogAuditActor.System("test"),
                "x",
                new CatalogAuditTarget("Plan", null, "pro"),
                null,
                null,
                null,
                "c",
                DateTime.UtcNow.AddSeconds(-i),
                CancellationToken.None
            );

        var result = await GetCatalogAuditHandler.Handle(
            new GetCatalogAuditQuery(null, null, null, requested),
            log,
            CancellationToken.None
        );

        Assert.Equal(expected, result.Value.Count);
    }
}
