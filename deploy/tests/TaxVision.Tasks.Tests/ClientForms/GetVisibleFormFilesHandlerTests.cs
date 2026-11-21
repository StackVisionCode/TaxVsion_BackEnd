using BuildingBlocks.Contracts.Files;
using BuildingBlocks.Contracts.Work;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Files;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.Tests.ClientForms;

/// <summary>
/// Archivos visibles de formularios: al descargar queda "answers viewed" una vez por request, como al abrir el
/// detalle; al listar no se escribe nada.
/// </summary>
public sealed class GetVisibleFormFilesHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid RequestA = Guid.NewGuid();
    private static readonly Guid RequestB = Guid.NewGuid();

    [Fact]
    public async Task Download_records_answers_viewed_once_per_request_and_saves()
    {
        var reader = new FixedReader(
            new VisibleFormFile(Guid.NewGuid(), RequestA, "Organizer", null, 1),
            new VisibleFormFile(Guid.NewGuid(), RequestA, "Organizer", null, 2),
            new VisibleFormFile(Guid.NewGuid(), RequestB, "Intake", Guid.NewGuid(), 1)
        );
        var audit = new AnswersViewedLog();
        var unitOfWork = new RecordingUnitOfWork();

        var files = await HandleAsync(reader, audit, unitOfWork, FileVisibilityPurposes.Download);

        Assert.Equal(3, files.Count);
        Assert.Equal([(RequestA, 2), (RequestB, 1)], audit.Viewed.OrderBy(v => v.RequestId == RequestB).ToArray());
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task List_writes_nothing()
    {
        var reader = new FixedReader(new VisibleFormFile(Guid.NewGuid(), RequestA, "Organizer", null, 1));
        var audit = new AnswersViewedLog();
        var unitOfWork = new RecordingUnitOfWork();

        await HandleAsync(reader, audit, unitOfWork, FileVisibilityPurposes.List);

        Assert.Empty(audit.Viewed);
        Assert.Equal(0, unitOfWork.Saves);
    }

    [Fact]
    public async Task Files_point_to_their_request()
    {
        var fileId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var reader = new FixedReader(new VisibleFormFile(fileId, RequestA, new string('a', 200), customerId, 1));

        var file = Assert.Single(
            await HandleAsync(reader, new AnswersViewedLog(), new RecordingUnitOfWork(), FileVisibilityPurposes.List)
        );

        Assert.Equal(
            (fileId, WorkResourceTypes.FormRequest, RequestA, (Guid?)customerId),
            (file.FileId, file.ResourceType, file.ResourceId, file.CustomerId)
        );
        Assert.Equal(WorkLimits.TitleMaxLength, file.Title!.Length);
    }

    [Fact]
    public async Task Nothing_asked_reads_nothing()
    {
        var reader = new FixedReader();

        var files = await GetVisibleFormFilesHandler.Handle(
            new GetVisibleFormFilesQuery(TenantId, Actor, false, [], FileVisibilityPurposes.Download),
            reader,
            new AnswersViewedLog(),
            new RecordingUnitOfWork(),
            CancellationToken.None
        );

        Assert.Empty(files);
        Assert.False(reader.WasCalled);
    }

    private static Task<IReadOnlyList<VisibleFile>> HandleAsync(
        FixedReader reader,
        AnswersViewedLog audit,
        RecordingUnitOfWork unitOfWork,
        string purpose
    ) =>
        GetVisibleFormFilesHandler.Handle(
            new GetVisibleFormFilesQuery(TenantId, Actor, false, [Guid.NewGuid()], purpose),
            reader,
            audit,
            unitOfWork,
            CancellationToken.None
        );

    private sealed class FixedReader(params VisibleFormFile[] files) : IFormFileVisibilityReader
    {
        public bool WasCalled { get; private set; }

        public Task<IReadOnlyList<VisibleFormFile>> VisibleFormFilesAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> fileIds,
            Guid actorUserId,
            bool canViewAll,
            CancellationToken ct
        )
        {
            WasCalled = true;
            return Task.FromResult<IReadOnlyList<VisibleFormFile>>(files);
        }
    }

    private sealed class AnswersViewedLog : IFormAuditLog
    {
        public List<(Guid RequestId, int Revision)> Viewed { get; } = [];

        public void Record(
            Guid tenantId,
            Guid? templateId,
            Guid? requestId,
            string action,
            Guid? actorUserId,
            string? detailsJson = null
        ) => throw new NotSupportedException();

        public void RecordStaffOutcome(
            Guid tenantId,
            Guid? templateId,
            Guid? requestId,
            string action,
            string outcome,
            Guid actorUserId,
            string? detailsJson = null,
            FormAuditRequestContext? requestContext = null
        ) => throw new NotSupportedException();

        public void RecordSystemOutcome(
            Guid tenantId,
            Guid? templateId,
            Guid? requestId,
            string action,
            string outcome,
            string? detailsJson = null
        ) => throw new NotSupportedException();

        public void RecordParticipant(
            Guid tenantId,
            Guid requestId,
            string action,
            FormActor actor,
            string? detailsJson = null
        ) => throw new NotSupportedException();

        public Task RecordAnswersViewedAsync(
            Guid tenantId,
            Guid requestId,
            int revisionNumber,
            Guid actorUserId,
            DateTime nowUtc,
            CancellationToken ct = default
        )
        {
            Assert.Equal((TenantId, Actor), (tenantId, actorUserId));
            Viewed.Add((requestId, revisionNumber));
            return Task.CompletedTask;
        }
    }
}
