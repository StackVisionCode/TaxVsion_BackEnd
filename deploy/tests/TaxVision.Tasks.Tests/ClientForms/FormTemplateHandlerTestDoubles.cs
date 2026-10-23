using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Mapping;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Consents;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;
using TaxVision.Tasks.ClientForms.Domain.Versions;
using TaxVision.Tasks.Infrastructure.ClientForms.Mapping;

namespace TaxVision.Tasks.Tests.ClientForms;

internal static class FormTemplateHandlerTestDoubles
{
    internal static readonly Guid TenantId = Guid.NewGuid();
    internal static readonly Guid UserId = Guid.NewGuid();
    internal const string ValidDefinition = """
        {"schemaVersion":1,"title":"Client intake","purpose":"clientIntake","presentation":"wizard","settings":{"showProgress":true},"pages":[{"id":"pg_abcdefgh","title":"Page","description":null,"visibleWhen":null,"style":null,"sections":[{"id":"sc_abcdefgh","title":"Section","description":null,"visibleWhen":null,"repeat":null,"style":null,"components":[{"id":"fd_abcdefgh","type":"text","key":null,"label":"Name","help":null,"required":false,"requiredWhen":null,"visibleWhen":null,"dataClass":"personal","reportable":false,"prefill":null,"style":null,"props":{}}]}]}],"mappings":[],"actions":[]}
        """;

    internal static readonly PublisherCapabilities Publisher = new(
        new HashSet<string>(["customers.manage"], StringComparer.Ordinal),
        false
    );

    internal static IFormEngine Engine() => new FormEngine(new DataCollectionLimits());

    internal static IDataCollectionTargetCatalog TargetCatalog() => new ContractDataCollectionTargetCatalog();

    internal static ConsentNotice IdentityNotice(Guid tenantId, Guid userId, DateTime nowUtc)
    {
        var notice = ConsentNotice
            .Create(
                tenantId,
                "identity_verification",
                "Identity verification",
                ConsentNoticePurpose.IdentityVerification,
                userId,
                nowUtc
            )
            .Value;
        notice.Publish(
            "en",
            "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"We use your email to verify this request.\"}]}]}",
            "We use your email to verify this request.",
            new string('a', 64),
            userId,
            nowUtc
        );
        return notice;
    }

    internal static FormTemplate Template(Guid? tenantId = null)
    {
        var engine = Engine();
        var canonical = engine.Canonicalize(engine.ParseDefinition(ValidDefinition).Value);
        return FormTemplate
            .Create(
                tenantId ?? TenantId,
                FormTemplateName.Create("Annual intake").Value,
                FormDescription.Create("Collect client details.").Value,
                FormDefinitionDocument.Create(canonical.Json, FormDefinitionDocument.MaxBytes).Value,
                ContentHash.Create(canonical.Sha256).Value,
                FormPurpose.ClientIntake,
                UserId,
                DateTime.UtcNow
            )
            .Value;
    }
}

internal sealed class MemoryFormTemplateRepository(params FormTemplate[] seed) : IFormTemplateRepository
{
    internal List<FormTemplate> Items { get; } = [.. seed];
    internal FormTemplate? Removed { get; private set; }

    public void Add(FormTemplate template) => Items.Add(template);

    public void Remove(FormTemplate template) => Removed = template;

    public Task<Result<FormTemplate>> GetByIdAsync(Guid tenantId, Guid templateId, CancellationToken ct = default)
    {
        var template = Items.SingleOrDefault(item => item.TenantId == tenantId && item.Id == templateId);
        return Task.FromResult(
            template is null ? Result.Failure<FormTemplate>(FormTemplateErrors.NotFound) : Result.Success(template)
        );
    }

    public Task<PagedResult<FormTemplate>> ListAsync(
        Guid tenantId,
        FormTemplateStatus? status,
        FormPurpose? purpose,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        var found = Items
            .Where(item => item.TenantId == tenantId)
            .Where(item => !status.HasValue || item.Status == status)
            .Where(item => !purpose.HasValue || item.Purpose == purpose)
            .Where(item =>
                string.IsNullOrWhiteSpace(search)
                || item.Name.Value.Contains(search, StringComparison.OrdinalIgnoreCase)
            )
            .ToArray();
        return Task.FromResult(new PagedResult<FormTemplate>(found, page, pageSize, found.Length));
    }

    public Task<IReadOnlyList<AssignableFormTemplateRecord>> ListAssignableAsync(
        Guid tenantId,
        string? search,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<AssignableFormTemplateRecord>>(
            Items
                .Where(item =>
                    item.TenantId == tenantId
                    && item.Status == FormTemplateStatus.Active
                    && item.LatestVersionNumber > 0
                )
                .Select(item => new AssignableFormTemplateRecord(item, item.Name.Value))
                .ToArray()
        );
}

internal sealed class MemoryFormTemplateVersionRepository(params FormTemplateVersion[] seed)
    : IFormTemplateVersionRepository
{
    internal List<FormTemplateVersion> Items { get; } = [.. seed];

    public void Add(FormTemplateVersion version) => Items.Add(version);

    public Task<Result<FormTemplateVersion>> GetAsync(
        Guid tenantId,
        Guid templateId,
        int versionNumber,
        CancellationToken ct = default
    )
    {
        var version = Items.SingleOrDefault(item =>
            item.TenantId == tenantId && item.FormTemplateId == templateId && item.VersionNumber == versionNumber
        );
        return Task.FromResult(
            version is null
                ? Result.Failure<FormTemplateVersion>(FormTemplateVersionErrors.NotFound)
                : Result.Success(version)
        );
    }

    public Task<Result<FormTemplateVersion>> GetByIdAsync(Guid tenantId, Guid versionId, CancellationToken ct = default)
    {
        var version = Items.SingleOrDefault(item => item.TenantId == tenantId && item.Id == versionId);
        return Task.FromResult(
            version is null
                ? Result.Failure<FormTemplateVersion>(FormTemplateVersionErrors.NotFound)
                : Result.Success(version)
        );
    }

    public Task<IReadOnlyList<FormTemplateVersion>> ListAsync(
        Guid tenantId,
        Guid templateId,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<FormTemplateVersion>>(
            Items.Where(item => item.TenantId == tenantId && item.FormTemplateId == templateId).ToArray()
        );
}

internal sealed class RecordingFormAuditLog : IFormAuditLog
{
    internal List<string> Actions { get; } = [];
    internal List<(string Action, string Outcome, string? Details)> StaffOutcomes { get; } = [];
    internal List<FormAuditRequestContext?> RequestContexts { get; } = [];

    public void Record(
        Guid tenantId,
        Guid? templateId,
        Guid? requestId,
        string action,
        Guid? actorUserId,
        string? detailsJson = null
    ) => Actions.Add(action);

    public void RecordStaffOutcome(
        Guid tenantId,
        Guid? templateId,
        Guid? requestId,
        string action,
        string outcome,
        Guid actorUserId,
        string? detailsJson = null,
        FormAuditRequestContext? requestContext = null
    )
    {
        Actions.Add($"{action}:{outcome}");
        StaffOutcomes.Add((action, outcome, detailsJson));
        RequestContexts.Add(requestContext);
    }

    public void RecordParticipant(
        Guid tenantId,
        Guid requestId,
        string action,
        FormActor actor,
        string? detailsJson = null
    ) => Actions.Add(action);

    public Task RecordAnswersViewedAsync(
        Guid tenantId,
        Guid requestId,
        int revisionNumber,
        Guid actorUserId,
        DateTime nowUtc,
        CancellationToken ct = default
    )
    {
        Actions.Add($"answers.viewed:{revisionNumber}");
        return Task.CompletedTask;
    }
}

internal sealed class RecordingUnitOfWork : IUnitOfWork
{
    internal int Saves { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(++Saves);
}

// Simula que el guardado número N pierde la carrera (RowVersion o índice único).
internal sealed class ConflictingUnitOfWork(int conflictOnSave, string code = "Persistence.ConcurrencyConflict")
    : IUnitOfWork
{
    internal int Saves { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        if (++Saves == conflictOnSave)
            throw new ConflictException(code, "The resource was modified by someone else.");
        return Task.FromResult(Saves);
    }
}

internal sealed class MemoryConsentNoticeRepository(params ConsentNotice[] seed) : IConsentNoticeRepository
{
    internal List<ConsentNotice> Items { get; } = [.. seed];

    public Task<ConsentNotice?> GetAsync(Guid tenantId, Guid noticeId, CancellationToken ct = default) =>
        Task.FromResult(Items.SingleOrDefault(item => item.TenantId == tenantId && item.Id == noticeId));

    public Task<ConsentNotice?> GetByKeyAsync(Guid tenantId, string key, CancellationToken ct = default) =>
        Task.FromResult(
            Items.SingleOrDefault(item => item.TenantId == tenantId && item.Key == key.Trim().ToLowerInvariant())
        );

    public Task<ConsentNoticeVersion?> GetVersionAsync(
        Guid tenantId,
        Guid noticeId,
        int versionNumber,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Items
                .SingleOrDefault(item => item.TenantId == tenantId && item.Id == noticeId)
                ?.Versions.SingleOrDefault(item => item.VersionNumber == versionNumber)
        );

    public Task<IReadOnlyList<ConsentNotice>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ConsentNotice>>(Items.Where(item => item.TenantId == tenantId).ToArray());

    public void Add(ConsentNotice notice) => Items.Add(notice);
}

internal sealed class MemoryFormConsentRecordRepository(params FormConsentRecord[] seed) : IFormConsentRecordRepository
{
    internal List<FormConsentRecord> Items { get; } = [.. seed];

    public Task<FormConsentRecord?> GetAsync(FormConsentRecordKey key, CancellationToken ct = default) =>
        Task.FromResult(
            Items.SingleOrDefault(item =>
                item.TenantId == key.TenantId
                && item.FormRequestId == key.RequestId
                && item.ComponentId == key.ComponentId
                && item.ParticipantKind == key.ParticipantKind
                && item.CustomerId == key.CustomerId
                && item.ParticipantId == key.ParticipantId
                && item.ConsentNoticeId == key.NoticeId
                && item.NoticeVersionNumber == key.NoticeVersionNumber
                && item.Outcome == key.Outcome
            )
        );

    public Task<IReadOnlyList<FormConsentRecord>> ListForRequestAsync(
        Guid tenantId,
        Guid requestId,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<FormConsentRecord>>(
            Items.Where(item => item.TenantId == tenantId && item.FormRequestId == requestId).ToArray()
        );

    public void Add(FormConsentRecord record) => Items.Add(record);

    public void Discard(FormConsentRecord record) => Items.Remove(record);
}
