using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.Tests.ClientForms;

internal static class FormRequestFixtures
{
    internal static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-0000000000a1");
    internal static readonly Guid UserId = Guid.Parse("20000000-0000-0000-0000-0000000000a1");
    internal static readonly Guid CustomerId = Guid.Parse("30000000-0000-0000-0000-0000000000a1");
    internal static readonly DateTime Now = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
    internal const string FileComponent = "fd_file0001";
    internal const string TaxIdComponent = "fd_taxid001";

    internal const string Definition = """
        {"schemaVersion":1,"title":"Documents","purpose":"documentRequest","presentation":"singlePage","settings":{"showProgress":true},"pages":[{"id":"pg_abcdefgh","title":"Documents","description":null,"visibleWhen":null,"style":null,"sections":[{"id":"sc_abcdefgh","title":"Documents","description":null,"visibleWhen":null,"repeat":null,"style":null,"components":[{"id":"fd_file0001","type":"file","key":null,"label":"Return","help":null,"required":false,"requiredWhen":null,"visibleWhen":null,"dataClass":"personal","reportable":false,"prefill":null,"style":null,"props":{"allowedTypes":["application/pdf"],"maxFiles":2,"maxFileBytes":1000000}},{"id":"fd_taxid001","type":"taxId","key":null,"label":"Taxpayer identifier","help":null,"required":false,"requiredWhen":null,"visibleWhen":null,"dataClass":"restricted","reportable":false,"prefill":null,"style":null,"props":{"kinds":["ssn","itin"]}}]}]}],"mappings":[],"actions":[]}
        """;

    internal static FormTemplateVersion Version(Guid? tenantId = null) =>
        FormTemplateVersion.Create(
            tenantId ?? TenantId,
            Guid.NewGuid(),
            1,
            "Documents",
            FormPurpose.DocumentRequest,
            FormPresentation.SinglePage,
            ClientFormsTestDocuments.Definition(Definition),
            ClientFormsTestDocuments.Hash(new string('a', 64)),
            0,
            UserId,
            Now
        );

    internal static FormRequest CustomerRequest(
        FormTemplateVersion? version = null,
        Guid? tenantId = null,
        Guid? requestedBy = null
    ) =>
        FormRequest
            .Create(
                tenantId ?? TenantId,
                FormParticipantRef.Customer(CustomerId),
                version ?? Version(tenantId),
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
                requestedBy ?? UserId,
                Now
            )
            .Value;

    internal static ParticipantContext Portal(FormRequest request) =>
        ParticipantContext.Portal(request.TenantId, CustomerId, UserId);

    // Devuelve la revisión enviada, para poder devolverla o completarla.
    internal static FormSubmissionRevision Submit(FormRequest request) =>
        request
            .Submit(
                new ProtectedAnswers("{\"schemaVersion\":1,\"values\":{}}", 1),
                [],
                [],
                FormActor.PortalUser(UserId),
                Now
            )
            .Value;
}

internal sealed class SingleFormRequestRepository(params FormRequest[] requests) : IFormRequestRepository
{
    public void Add(FormRequest item) { }

    public Task<Result<FormRequest>> GetForCommandAsync(Guid tenantId, Guid requestId, CancellationToken ct = default)
    {
        var found = requests.FirstOrDefault(item => item.TenantId == tenantId && item.Id == requestId);
        return Task.FromResult(
            found is null ? Result.Failure<FormRequest>(FormRequestErrors.NotFound) : Result.Success(found)
        );
    }

    public Task<Result<FormRequest>> GetForReviewAsync(Guid tenantId, Guid requestId, CancellationToken ct = default) =>
        GetForCommandAsync(tenantId, requestId, ct);

    public Task<bool> HasOpenRequestAsync(
        Guid tenantId,
        Guid customerId,
        Guid templateId,
        Guid? exceptRequestId = null,
        CancellationToken ct = default
    ) => Task.FromResult(false);

    public Task<bool> HasOpenExternalRequestAsync(
        Guid tenantId,
        Guid participantId,
        Guid templateId,
        CancellationToken ct = default
    ) => Task.FromResult(false);

    public Task<IReadOnlyList<FormRequest>> ListOpenByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<FormRequest>>([]);

    public Task<IReadOnlyList<FormRequest>> ListByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        bool onlyOpen,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<FormRequest>>([]);

    public Task<IReadOnlyList<FormRequest>> ListByAttachmentFileIdAsync(Guid fileId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<FormRequest>>(
            requests.Where(item => item.Submission?.Attachments.Any(file => file.FileId == fileId) == true).ToArray()
        );
}

internal sealed class FixedRequestVisibility(bool visible) : IFormRequestVisibilityFilter
{
    public Task<bool> CanViewCustomerAsync(
        Guid tenantId,
        Guid actorUserId,
        bool canViewAll,
        Guid customerId,
        CancellationToken ct = default
    ) => Task.FromResult(visible);

    public Task<bool> CanViewAsync(
        Guid tenantId,
        Guid actorUserId,
        bool canViewAll,
        FormRequest request,
        CancellationToken ct = default
    ) => Task.FromResult(visible);
}

internal sealed class PlainFormAnswersProtector : IAnswersProtector
{
    public ProtectedAnswers Protect(string json) => new(json, 1);

    public string Unprotect(ProtectedAnswers value) => value.Cipher;
}

internal sealed class MemoryFormFileScanVerdictRepository(params FormFileScanVerdict[] seed)
    : IFormFileScanVerdictRepository
{
    internal List<FormFileScanVerdict> Items { get; } = [.. seed];

    public Task<FormFileScanVerdict?> GetAsync(Guid fileId, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(item => item.Id == fileId));

    public Task<bool> ExistsAsync(Guid fileId, CancellationToken ct = default) =>
        Task.FromResult(Items.Any(item => item.Id == fileId));

    public void Add(FormFileScanVerdict verdict) => Items.Add(verdict);
}

// Simula el rowversion que SQL Server cambia al guardar la fila raíz.
internal sealed class RowVersionBumpingUnitOfWork(FormRequest request) : IUnitOfWork
{
    internal int Saves { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        Saves++;
        typeof(FormRequest)
            .GetProperty(nameof(FormRequest.RowVersion))!
            .SetValue(request, new byte[] { 0, 0, 0, 0, 0, 0, 0, (byte)Saves });
        return Task.FromResult(Saves);
    }
}

internal sealed class TestCorrelationContext : ICorrelationContext
{
    public string CorrelationId => "tests";

    public void Set(string correlationId) { }

    public IDisposable Push(string correlationId) => new MemoryStream();
}
