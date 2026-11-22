using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Infrastructure.Persistence;
using TaxVision.Signature.Infrastructure.Persistence.Queries;
using TaxVision.Signature.Tests.Application.Offboarding;

namespace TaxVision.Signature.Tests.Integration;

/// <summary>
/// Resumen del widget de firmas contra SQL Server real (base temporal BB-10): recorte del listado, períodos y la media
/// con DATEDIFF, que no corre en InMemory. Caso en vivo: 11 enviadas y 9 firmadas, un solo cliente asignado.
/// </summary>
public sealed class SignatureRequestsSummaryIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private readonly Guid _employee = Guid.NewGuid();
    private readonly Guid _author = Guid.NewGuid();
    private readonly Guid _assignedCustomer = Guid.NewGuid();
    private readonly DateTime _now = DateTime.UtcNow;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var tenantId in new[] { _tenantId, _otherTenantId })
        {
            await using var db = NewContext(tenantId);
            await db.SignatureRequests.IgnoreQueryFilters().Where(r => r.TenantId == tenantId).ExecuteDeleteAsync();
            await db.Set<CustomerAssignmentProjection>()
                .IgnoreQueryFilters()
                .Where(a => a.TenantId == tenantId)
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Employee_with_one_assigned_client_sees_only_that_clients_numbers()
    {
        await SeedLiveCaseAsync();

        var employee = await ReadAsync(_employee);
        var office = await ReadAsync(assignedTo: null);

        Assert.Equal((2, 1), (employee.Sent, employee.Completed));
        Assert.Equal((1, 1), (employee.Draft, employee.InProgress));
        Assert.Equal(30d, employee.AvgMinutesToSign);
        Assert.Equal((11, 9), (office.Sent, office.Completed));
        Assert.Equal((2, 2), (office.Draft, office.InProgress));
    }

    [Fact]
    public async Task Period_status_and_expiring_soon_follow_the_row_dates()
    {
        var expiring = Sent(_tenantId, Guid.NewGuid());
        var canceled = Sent(_tenantId, Guid.NewGuid());
        Assert.True(canceled.Cancel(_now).IsSuccess);
        var expired = Sent(_tenantId, Guid.NewGuid());
        Assert.True(expired.MarkExpired(_now).IsSuccess);
        var oldCanceled = Sent(_tenantId, Guid.NewGuid());
        Assert.True(oldCanceled.Cancel(_now.AddDays(-10)).IsSuccess);
        await SeedAsync(_tenantId, expiring, canceled, expired, oldCanceled);
        await using (var db = NewContext(_tenantId))
            await db
                .SignatureRequests.Where(r => r.Id == expiring.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ExpiresAtUtc, _now.AddHours(10)));

        var office = await ReadAsync(assignedTo: null);

        Assert.Equal((1, 1, 1), (office.ExpiringWithin48h, office.Canceled, office.Expired));
        Assert.Null(office.AvgMinutesToSign);
    }

    [Fact]
    public async Task Me_counts_only_what_the_actor_created_and_still_applies_the_cut()
    {
        await SeedLiveCaseAsync();

        var author = await ReadAsync(assignedTo: null, createdBy: _author);
        var employee = await ReadAsync(assignedTo: null, createdBy: _employee);
        var authorSeenByEmployee = await ReadAsync(assignedTo: _employee, createdBy: _author);

        Assert.Equal((11, 9, 1), (author.Sent, author.Completed, author.Draft));
        Assert.Equal((0, 0, 1), (employee.Sent, employee.Completed, employee.Draft));
        // Lo del autor que el empleado ve: solo su cliente asignado, sin el borrador ajeno.
        Assert.Equal(
            (2, 1, 0),
            (authorSeenByEmployee.Sent, authorSeenByEmployee.Completed, authorSeenByEmployee.Draft)
        );
    }

    [Fact]
    public async Task Another_tenant_is_never_counted()
    {
        await SeedAsync(_otherTenantId, Sent(_otherTenantId, _assignedCustomer));

        var office = await ReadAsync(assignedTo: null);

        Assert.Equal(new SignatureRequestsLiveCounts(0, 0, 0, 0, 0, 0, 0, null), office);
    }

    // ----- helpers -----

    // 11 enviadas (2 del cliente asignado) y 9 firmadas (1 del asignado), más un borrador propio y uno ajeno.
    private async Task SeedLiveCaseAsync()
    {
        var mine = new[] { Sent(_tenantId, _assignedCustomer), Sent(_tenantId, _assignedCustomer) };
        var others = Enumerable.Range(0, 9).Select(_ => Sent(_tenantId, Guid.NewGuid())).ToArray();
        await SeedAsync(
            _tenantId,
            [
                .. mine,
                .. others,
                PreparerRequests.Draft(_tenantId, _employee),
                PreparerRequests.Draft(_tenantId, _author),
            ]
        );
        var completed = others.Skip(1).Select(r => r.Id).Append(mine[0].Id).ToArray();
        await using var db = NewContext(_tenantId);
        await db
            .SignatureRequests.Where(r => completed.Contains(r.Id))
            .ExecuteUpdateAsync(s =>
                s.SetProperty(r => r.Status, SignatureRequestStatus.Completed)
                    .SetProperty(r => r.CompletedAtUtc, r => r.SentAtUtc!.Value.AddMinutes(30))
            );
        db.Set<CustomerAssignmentProjection>()
            .Add(CustomerAssignmentProjection.Create(_tenantId, _assignedCustomer, _employee, _now));
        await db.SaveChangesAsync();
    }

    private async Task<SignatureRequestsLiveCounts> ReadAsync(Guid? assignedTo, Guid? createdBy = null)
    {
        await using var db = NewContext(_tenantId);
        return await new SignatureRequestsSummaryReader(db).ReadAsync(
            new SignatureRequestsSummaryFilter(
                _tenantId,
                assignedTo,
                _now.AddDays(-1),
                _now.AddDays(1),
                _now,
                createdBy
            )
        );
    }

    private SignatureRequest Sent(Guid tenantId, Guid customerId)
    {
        var request = PreparerRequests.Draft(tenantId, _author);
        var document = request.Documents.Single();
        var signer = request
            .AddSigner(
                TestSignatureLimits.Default,
                SignerEmail.Create("client@example.test").Value,
                SignerFullName.Create("Client Signer").Value,
                customerId
            )
            .Value;
        Assert.True(
            request
                .PlaceField(
                    TestSignatureLimits.MaxFieldsPerSigner,
                    signer.Id,
                    document.Id,
                    SignatureFieldKind.Signature,
                    FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value,
                    null,
                    true
                )
                .IsSuccess
        );
        request.AttachVerifiedDocument(document.Id, VerifiedDocument.Create(PreparerRequests.Hash('a'), 50).Value);
        Assert.True(request.Send(_now).IsSuccess);
        return request;
    }

    private static async Task SeedAsync(Guid tenantId, params SignatureRequest[] requests)
    {
        await using var db = NewContext(tenantId);
        db.SignatureRequests.AddRange(requests);
        await db.SaveChangesAsync();
    }

    private static SignatureDbContext NewContext(Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<SignatureDbContext>()
                .UseSqlServer(SignatureTestDatabase.ConnectionString)
                .Options,
            new FixedTenant(tenantId)
        );

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
