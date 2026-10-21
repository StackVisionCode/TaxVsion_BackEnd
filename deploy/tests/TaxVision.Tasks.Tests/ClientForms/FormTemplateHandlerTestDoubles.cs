using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;
using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.Tests.ClientForms;

internal static class FormTemplateHandlerTestDoubles
{
    internal static readonly Guid TenantId = Guid.NewGuid();
    internal static readonly Guid UserId = Guid.NewGuid();
    internal const string ValidDefinition = """
        {"schemaVersion":1,"title":"Client intake","purpose":"clientIntake","presentation":"wizard","settings":{"showProgress":true},"pages":[{"id":"pg_abcdefgh","title":"Page","description":null,"visibleWhen":null,"style":null,"sections":[{"id":"sc_abcdefgh","title":"Section","description":null,"visibleWhen":null,"repeat":null,"style":null,"components":[{"id":"fd_abcdefgh","type":"text","key":null,"label":"Name","help":null,"required":false,"requiredWhen":null,"visibleWhen":null,"dataClass":"personal","reportable":false,"prefill":null,"style":null,"props":{}}]}]}],"mappings":[],"actions":[]}
        """;

    internal static IFormEngine Engine() => new FormEngine(new DataCollectionLimits());

    internal static FormTemplate Template(Guid? tenantId = null)
    {
        var engine = Engine();
        var canonical = engine.Canonicalize(engine.ParseDefinition(ValidDefinition).Value);
        return FormTemplate.Create(
            tenantId ?? TenantId,
            FormTemplateName.Create("Annual intake").Value,
            FormDescription.Create("Collect client details.").Value,
            new FormDefinitionDocument(canonical.Json),
            ContentHash.Create(canonical.Sha256).Value,
            FormPurpose.ClientIntake,
            UserId,
            DateTime.UtcNow
        ).Value;
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
            .Where(item => string.IsNullOrWhiteSpace(search) || item.Name.Value.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return Task.FromResult(new PagedResult<FormTemplate>(found, page, pageSize, found.Length));
    }

    public Task<IReadOnlyList<AssignableFormTemplateRecord>> ListAssignableAsync(
        Guid tenantId,
        string? search,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<AssignableFormTemplateRecord>>(
        Items
            .Where(item => item.TenantId == tenantId && item.Status == FormTemplateStatus.Active && item.LatestVersionNumber > 0)
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

    public Task<IReadOnlyList<FormTemplateVersion>> ListAsync(
        Guid tenantId,
        Guid templateId,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlyList<FormTemplateVersion>>(Items.Where(item => item.TenantId == tenantId && item.FormTemplateId == templateId).ToArray());
}

internal sealed class RecordingFormAuditLog : IFormAuditLog
{
    internal List<string> Actions { get; } = [];
    public void Record(Guid tenantId, Guid? templateId, Guid? requestId, string action, Guid? actorUserId, string? detailsJson = null) => Actions.Add(action);
}

internal sealed class RecordingUnitOfWork : IUnitOfWork
{
    internal int Saves { get; private set; }
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(++Saves);
}
