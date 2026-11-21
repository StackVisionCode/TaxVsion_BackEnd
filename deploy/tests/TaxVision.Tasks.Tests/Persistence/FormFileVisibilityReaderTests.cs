using BuildingBlocks.CustomerVisibility;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Application.Requests.Files;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories;
using TaxVision.Tasks.Tests.ClientForms;
using TaxVision.Tasks.Tests.ClientForms.Integration;
using static TaxVision.Tasks.Tests.ClientForms.FormRequestFixtures;

namespace TaxVision.Tasks.Tests.Persistence;

/// <summary>
/// Un archivo de formulario es visible si es un adjunto disponible o el PDF de una revisión de un request que quien
/// mira ve en la bandeja (<c>FormRequestInboxVisibility</c>), traducido por SQL Server.
/// </summary>
public sealed class FormFileVisibilityReaderTests : IAsyncLifetime
{
    private readonly TasksTemporaryDatabase _database = new();
    private readonly Guid _me = Guid.NewGuid();

    private readonly Guid _assignedFile = Guid.NewGuid();
    private readonly Guid _assignedPdf = Guid.NewGuid();
    private readonly Guid _unassignedFile = Guid.NewGuid();
    private readonly Guid _externalFile = Guid.NewGuid();
    private readonly Guid _rejectedFile = Guid.NewGuid();
    private readonly Guid _pendingFile = Guid.NewGuid();
    private readonly Guid _foreignFile = Guid.NewGuid();

    private FormRequest _assigned = null!;
    private FormRequest _unassigned = null!;
    private FormRequest _external = null!;
    private int _pdfRevision;

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        _assigned = CustomerRequest();
        Available(_assigned, _assignedFile);
        FormFileSourcesTests.Register(_assigned, _pendingFile);
        Rejected(_assigned, _rejectedFile);
        var revision = Submit(_assigned);
        var generationId = Guid.NewGuid();
        Assert.True(_assigned.Summary.MarkPdfRequested(revision.RevisionNumber, generationId).IsSuccess);
        Assert.True(_assigned.Summary.LinkPdf(generationId, _assignedPdf).IsSuccess);
        _pdfRevision = revision.RevisionNumber;

        _unassigned = FormRequest
            .Create(
                TenantId,
                FormParticipantRef.Customer(Guid.NewGuid()),
                Version(),
                null,
                null,
                2026,
                null,
                Now.AddDays(7),
                null,
                null,
                FormRequestOrigin.Directed,
                null,
                null,
                UserId,
                Now
            )
            .Value;
        Available(_unassigned, _unassignedFile);
        _external = FormFileSourcesTests.ExternalRequest(responsible: _me);
        Available(_external, _externalFile);
        var foreign = CustomerRequest(tenantId: Guid.NewGuid());
        Available(foreign, _foreignFile);

        await using var db = _database.NewContext(Guid.Empty);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE FormRequests NOCHECK CONSTRAINT ALL;");
        db.FormRequests.AddRange(_assigned, _unassigned, _external, foreign);
        db.Set<CustomerAssignmentProjection>()
            .Add(CustomerAssignmentProjection.Create(TenantId, CustomerId, _me, DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task A_restricted_viewer_gets_the_files_of_assigned_clients_and_his_external_requests()
    {
        var files = await ReadAsync(false);

        Assert.Equal(new[] { _assignedFile, _assignedPdf, _externalFile }.Order(), files.Select(f => f.FileId).Order());
        var attachment = files.Single(f => f.FileId == _assignedFile);
        Assert.Equal(
            (_assigned.Id, (Guid?)CustomerId, _assigned.CurrentRevisionNumber),
            (attachment.RequestId, attachment.CustomerId, attachment.RevisionNumber)
        );
        var pdf = files.Single(f => f.FileId == _assignedPdf);
        Assert.Equal((_assigned.Id, _pdfRevision), (pdf.RequestId, pdf.RevisionNumber));
        Assert.Null(files.Single(f => f.FileId == _externalFile).CustomerId);
    }

    [Fact]
    public async Task Viewing_all_sees_every_request_of_the_office_but_not_rejected_or_pending_files()
    {
        var files = await ReadAsync(true);

        Assert.Equal(
            new[] { _assignedFile, _assignedPdf, _unassignedFile, _externalFile }.Order(),
            files.Select(f => f.FileId).Order()
        );
    }

    private async Task<IReadOnlyList<VisibleFormFile>> ReadAsync(bool canViewAll)
    {
        await using var db = _database.NewContext(Guid.Empty);
        return await new FormFileVisibilityReader(db).VisibleFormFilesAsync(
            TenantId,
            [
                _assignedFile,
                _assignedPdf,
                _unassignedFile,
                _externalFile,
                _rejectedFile,
                _pendingFile,
                _foreignFile,
                Guid.NewGuid(),
            ],
            _me,
            canViewAll,
            default
        );
    }

    private static void Available(FormRequest request, Guid fileId)
    {
        FormFileSourcesTests.Register(request, fileId);
        Verdict(request, fileId, FormFileVerdictStatus.Available);
    }

    private static void Rejected(FormRequest request, Guid fileId)
    {
        FormFileSourcesTests.Register(request, fileId);
        Verdict(request, fileId, FormFileVerdictStatus.Infected);
    }

    private static void Verdict(FormRequest request, Guid fileId, FormFileVerdictStatus status)
    {
        var (ownerType, ownerId) = request.ExpectedFileOwners().First();
        request.Files.ApplyVerdict(fileId, new FormFileVerdict(status, ownerType, ownerId, "ClientForms", Now), Now);
    }
}
