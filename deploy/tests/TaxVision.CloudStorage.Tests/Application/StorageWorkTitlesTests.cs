using System.Reflection;
using System.Security.Claims;
using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Api.Common;
using TaxVision.CloudStorage.Api.Controllers;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Files.Queries;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;
using Wolverine;

namespace TaxVision.CloudStorage.Tests.Application;

// Títulos en vivo del historial: nombre de los archivos que quien mira puede abrir (ABAC de asignación incluido).
public sealed class StorageWorkTitlesTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private sealed class TenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid tenantId) { }
    }

    private static FileObject File(Guid tenantId, OwnerType ownerType, Guid? ownerId, string name = "return.pdf") =>
        FileObject
            .Register(
                Guid.NewGuid(),
                tenantId,
                ownerType,
                ownerId,
                FolderType.Documents,
                2026,
                ObjectKey.Create($"tenants/{tenantId:N}/{ownerType}/documents/2026/{Guid.NewGuid():N}.pdf").Value,
                name,
                "application/pdf",
                1024,
                Guid.NewGuid(),
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(1)
            )
            .Value;

    private static async Task<CloudStorageDbContext> SeedAsync(params FileObject[] files)
    {
        var db = new CloudStorageDbContext(
            new DbContextOptionsBuilder<CloudStorageDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new TenantContext(Tenant)
        );
        db.Files.AddRange(files);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private static Task<WorkTitlesResponse> Resolve(
        CloudStorageDbContext db,
        StorageActorScope scope,
        params WorkTitleRef[] refs
    ) =>
        GetWorkTitlesHandler.Handle(
            new GetWorkTitlesQuery(Tenant, scope, refs),
            new FileObjectRepository(db),
            CancellationToken.None
        );

    private static WorkTitleRef Document(Guid id) => new(WorkResourceTypes.Document, id);

    private static StorageActorScope Staff => new(false, null) { TenantId = Tenant };

    [Fact]
    public async Task A_visible_file_resolves_to_its_name()
    {
        var file = File(Tenant, OwnerType.Customer, Guid.NewGuid(), "W-2 2026.pdf");
        await using var db = await SeedAsync(file);

        var result = await Resolve(db, Staff, Document(file.Id));

        Assert.Equal([new WorkTitle(WorkResourceTypes.Document, file.Id, "W-2 2026.pdf")], result.Items);
    }

    [Fact]
    public async Task Files_of_customers_not_assigned_to_the_viewer_are_omitted()
    {
        var assigned = Guid.NewGuid();
        var mine = File(Tenant, OwnerType.Customer, assigned);
        var other = File(Tenant, OwnerType.Customer, Guid.NewGuid());
        var office = File(Tenant, OwnerType.Tenant, null);
        await using var db = await SeedAsync(mine, other, office);
        var scope = Staff with { BypassAssignmentFilter = false, AssignedCustomerIds = [assigned] };

        var result = await Resolve(db, scope, Document(mine.Id), Document(other.Id), Document(office.Id));

        Assert.Equal(new HashSet<Guid> { mine.Id, office.Id }, result.Items.Select(t => t.Id).ToHashSet());
    }

    [Fact]
    public async Task Trashed_missing_and_foreign_tenant_files_are_omitted()
    {
        var trashed = File(Tenant, OwnerType.Tenant, null);
        trashed.SoftDelete(DateTime.UtcNow, TimeSpan.FromDays(30));
        var foreign = File(Guid.NewGuid(), OwnerType.Tenant, null);
        await using var db = await SeedAsync(trashed, foreign);

        var result = await Resolve(db, Staff, Document(trashed.Id), Document(foreign.Id), Document(Guid.NewGuid()));

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Foreign_resource_types_are_ignored()
    {
        var file = File(Tenant, OwnerType.Tenant, null);
        await using var db = await SeedAsync(file);

        var result = await Resolve(db, Staff, new WorkTitleRef(WorkResourceTypes.Task, file.Id), Document(file.Id));

        Assert.Equal(WorkResourceTypes.Document, Assert.Single(result.Items).ResourceType);
    }

    [Fact]
    public async Task Long_names_are_clipped()
    {
        var file = File(Tenant, OwnerType.Tenant, null, new string('a', 180) + ".pdf");
        await using var db = await SeedAsync(file);

        var title = Assert.Single((await Resolve(db, Staff, Document(file.Id))).Items).Title;

        Assert.Equal(WorkLimits.TitleMaxLength, title.Length);
        Assert.EndsWith("…", title);
    }

    // ---------- Controlador ----------

    public class CapturingBus : DispatchProxy
    {
        public object? Last { get; private set; }

        protected override object Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Last = args?[0];
            return Task.FromResult(new WorkTitlesResponse([]));
        }
    }

    private sealed class FixedScopeResolver(StorageActorScope scope) : IStorageActorScopeResolver
    {
        public Task<StorageActorScope> ResolveAsync(
            ClaimsPrincipal user,
            Guid tenantId,
            Guid actorId,
            StorageActorScope baseScope,
            CancellationToken ct
        ) => Task.FromResult(scope);
    }

    private static (StorageWorkController Controller, CapturingBus Bus) Controller(StorageActorScope resolved)
    {
        var bus = DispatchProxy.Create<IMessageBus, CapturingBus>();
        var controller = new StorageWorkController(bus, new FixedScopeResolver(resolved))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity(
                            [new Claim("tenant_id", Tenant.ToString()), new Claim("sub", Guid.NewGuid().ToString())],
                            "test"
                        )
                    ),
                },
            },
        };
        return (controller, (CapturingBus)(object)bus);
    }

    [Fact]
    public async Task More_than_the_batch_limit_is_rejected()
    {
        var (controller, bus) = Controller(Staff);
        var refs = Enumerable.Range(0, WorkLimits.TitleBatch + 1).Select(_ => Document(Guid.NewGuid())).ToList();

        var result = await controller.Titles(new WorkTitlesRequest(refs), CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("CloudStorage.Work.Titles", Assert.IsType<Error>(bad.Value).Code);
        Assert.Null(bus.Last);
    }

    [Fact]
    public async Task The_query_carries_the_scope_with_the_assignment_filter()
    {
        var resolved = Staff with { BypassAssignmentFilter = false, AssignedCustomerIds = [Guid.NewGuid()] };
        var (controller, bus) = Controller(resolved);

        var result = await controller.Titles(new WorkTitlesRequest([Document(Guid.NewGuid())]), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var query = Assert.IsType<GetWorkTitlesQuery>(bus.Last);
        Assert.Equal(Tenant, query.TenantId);
        Assert.Same(resolved, query.Scope);
    }
}
