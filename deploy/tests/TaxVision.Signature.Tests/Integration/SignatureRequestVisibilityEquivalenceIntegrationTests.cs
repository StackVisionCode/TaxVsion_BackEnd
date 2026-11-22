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
/// El recorte por asignación escrito como semi-join desde la asignación devuelve las mismas solicitudes que el EXISTS
/// anterior (copiado aquí), contra SQL Server: borradores propios y ajenos, firmantes de clientes asignados y sin
/// asignar, varios firmantes por solicitud y otra oficina con la misma asignación.
/// </summary>
public sealed class SignatureRequestVisibilityEquivalenceIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private readonly Guid _employee = Guid.NewGuid();
    private readonly Guid _colleague = Guid.NewGuid();
    private readonly Guid _author = Guid.NewGuid();
    private readonly Guid _assignedA = Guid.NewGuid();
    private readonly Guid _assignedB = Guid.NewGuid();
    private readonly Guid _unassigned = Guid.NewGuid();
    private readonly DateTime _now = DateTime.UtcNow;
    private readonly Dictionary<string, Guid> _ids = [];

    public async Task InitializeAsync()
    {
        var requests = new Dictionary<string, SignatureRequest>
        {
            ["own draft"] = PreparerRequests.Draft(_tenantId, _employee),
            ["foreign draft"] = PreparerRequests.Draft(_tenantId, _author),
            ["own draft, unassigned signer"] = DraftWith(_employee, _unassigned),
            ["foreign draft, assigned signer"] = DraftWith(_author, _assignedA),
            ["sent, assigned"] = Sent(_tenantId, _author, _assignedA),
            ["sent, unassigned"] = Sent(_tenantId, _author, _unassigned),
            ["sent by me, unassigned"] = Sent(_tenantId, _employee, _unassigned),
            ["sent, two assigned"] = Sent(_tenantId, _author, _assignedA, _assignedB),
            ["sent, mixed"] = Sent(_tenantId, _author, _unassigned, _assignedB),
            ["sent, unmapped"] = Sent(_tenantId, _author, [null]),
        };
        foreach (var (name, request) in requests)
            _ids[name] = request.Id;

        await using (var db = NewContext(_tenantId))
        {
            db.SignatureRequests.AddRange(requests.Values);
            db.Set<CustomerAssignmentProjection>()
                .AddRange(
                    CustomerAssignmentProjection.Create(_tenantId, _assignedA, _employee, _now),
                    CustomerAssignmentProjection.Create(_tenantId, _assignedB, _employee, _now),
                    CustomerAssignmentProjection.Create(_tenantId, _unassigned, _colleague, _now)
                );
            await db.SaveChangesAsync();
        }

        // Otra oficina: el mismo cliente asignado al empleado no abre nada en la oficina medida.
        await using var other = NewContext(_otherTenantId);
        other.SignatureRequests.Add(Sent(_otherTenantId, _author, _unassigned));
        other
            .Set<CustomerAssignmentProjection>()
            .Add(CustomerAssignmentProjection.Create(_otherTenantId, _unassigned, _employee, _now));
        await other.SaveChangesAsync();
    }

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
    public async Task The_employee_sees_the_expected_requests()
    {
        var visible = await VisibleAsync(_employee);

        Assert.Equal(
            new[]
            {
                "foreign draft, assigned signer",
                "own draft",
                "own draft, unassigned signer",
                "sent, assigned",
                "sent, mixed",
                "sent, two assigned",
            },
            visible.Select(id => _ids.Single(x => x.Value == id).Key).Order()
        );
    }

    [Fact]
    public async Task Every_viewer_gets_the_same_rows_as_the_previous_exists()
    {
        foreach (var viewer in new[] { _employee, _colleague, _author, Guid.NewGuid() })
            Assert.Equal(await LegacyVisibleAsync(viewer), await VisibleAsync(viewer));
    }

    [Fact]
    public async Task The_summary_counts_match_the_previous_exists()
    {
        await using var db = NewContext(_tenantId);
        var counts = await new SignatureRequestsSummaryReader(db).ReadAsync(
            new SignatureRequestsSummaryFilter(_tenantId, _employee, _now.AddDays(-1), _now.AddDays(1), _now)
        );
        var legacy = await Legacy(db, _employee).Select(r => r.Status).ToListAsync();

        Assert.Equal(legacy.Count(s => s == SignatureRequestStatus.Draft), counts.Draft);
        Assert.Equal(legacy.Count(s => s == SignatureRequestStatus.InProgress), counts.InProgress);
        Assert.Equal(legacy.Count(s => s != SignatureRequestStatus.Draft), counts.Sent);
    }

    // Sin Distinct: una solicitud repetida por tener dos firmantes asignados también es una diferencia.
    private async Task<List<Guid>> VisibleAsync(Guid viewer)
    {
        await using var db = NewContext(_tenantId);
        return await Scope(db).VisibleTo(db, _tenantId, viewer).Select(r => r.Id).OrderBy(id => id).ToListAsync();
    }

    private async Task<List<Guid>> LegacyVisibleAsync(Guid viewer)
    {
        await using var db = NewContext(_tenantId);
        return await Legacy(db, viewer).Select(r => r.Id).OrderBy(id => id).ToListAsync();
    }

    private IQueryable<SignatureRequest> Scope(SignatureDbContext db) =>
        db.SignatureRequests.AsNoTracking().IgnoreQueryFilters().Where(r => r.TenantId == _tenantId);

    // La regla anterior, tal cual: EXISTS (asignación ... EXISTS (firmante ...)) evaluado por solicitud.
    private IQueryable<SignatureRequest> Legacy(SignatureDbContext db, Guid assignee)
    {
        var tenantId = _tenantId;
        return Scope(db)
            .Where(r =>
                (r.Status == SignatureRequestStatus.Draft && r.CreatedByUserId == assignee)
                || db.Set<CustomerAssignmentProjection>()
                    .IgnoreQueryFilters()
                    .Any(a =>
                        a.TenantId == tenantId
                        && a.UserId == assignee
                        && r.Signers.Any(s => s.MappedCustomerId == a.CustomerId)
                    )
            );
    }

    private SignatureRequest DraftWith(Guid author, Guid customerId)
    {
        var request = PreparerRequests.Draft(_tenantId, author);
        AddSigner(request, 0, customerId);
        return request;
    }

    private SignatureRequest Sent(Guid tenantId, Guid author, params Guid?[] customers)
    {
        var request = PreparerRequests.Draft(tenantId, author);
        var document = request.Documents.Single();
        for (var i = 0; i < customers.Length; i++)
        {
            var signer = AddSigner(request, i, customers[i]);
            Assert.True(
                request
                    .PlaceField(
                        TestSignatureLimits.MaxFieldsPerSigner,
                        signer.Id,
                        document.Id,
                        SignatureFieldKind.Signature,
                        FieldPosition.Create(1, 0.1, 0.1 + i * 0.1, 0.2, 0.05).Value,
                        null,
                        true
                    )
                    .IsSuccess
            );
        }
        request.AttachVerifiedDocument(document.Id, VerifiedDocument.Create(PreparerRequests.Hash('a'), 50).Value);
        Assert.True(request.Send(_now).IsSuccess);
        return request;
    }

    private static Signer AddSigner(SignatureRequest request, int index, Guid? customerId) =>
        request
            .AddSigner(
                TestSignatureLimits.Default,
                SignerEmail.Create($"client{index}@example.test").Value,
                SignerFullName.Create($"Client Signer {index}").Value,
                customerId
            )
            .Value;

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
