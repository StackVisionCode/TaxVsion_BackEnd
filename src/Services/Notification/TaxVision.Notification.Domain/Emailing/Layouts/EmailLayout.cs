using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Notification.Domain.Emailing.Layouts;

/// <summary>
/// Layout de correo que envuelve el cuerpo de una plantilla. Global del SaaS (System) o por tenant.
/// Solo un layout default por scope/tenant; si el tenant no define uno, se usa el default global.
/// El HTML/design/preview viven en CloudStorage; la BD guarda claves y FileIds.
/// El HTML del layout debe contener el marcador <c>{{ body }}</c> donde se inserta el cuerpo renderizado.
/// </summary>
public sealed class EmailLayout : BaseEntity
{
    private EmailLayout() { }

    public Guid? TenantId { get; private set; }
    public EmailScope Scope { get; private set; }
    public string LayoutName { get; private set; } = default!;
    public string? HtmlStorageKey { get; private set; }
    public Guid? HtmlFileId { get; private set; }
    public string? DesignStorageKey { get; private set; }
    public Guid? DesignFileId { get; private set; }
    public string? PreviewStorageKey { get; private set; }
    public Guid? PreviewFileId { get; private set; }
    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public static Result<EmailLayout> Create(
        EmailScope scope,
        Guid? tenantId,
        string layoutName,
        string? htmlStorageKey,
        Guid? htmlFileId,
        string? designStorageKey,
        Guid? designFileId,
        string? previewStorageKey,
        Guid? previewFileId,
        bool isDefault,
        Guid? createdByUserId
    )
    {
        if (scope == EmailScope.Tenant && (tenantId is null || tenantId == Guid.Empty))
            return Result.Failure<EmailLayout>(new Error("EmailLayout.Tenant", "Tenant layouts require a tenant id."));

        if (scope == EmailScope.System && tenantId is not null)
            return Result.Failure<EmailLayout>(new Error("EmailLayout.Scope", "System layouts must not carry a tenant id."));

        if (string.IsNullOrWhiteSpace(layoutName))
            return Result.Failure<EmailLayout>(new Error("EmailLayout.Name", "Layout name is required."));

        var layout = new EmailLayout
        {
            Id = Guid.NewGuid(),
            Scope = scope,
            TenantId = scope == EmailScope.Tenant ? tenantId : null,
            LayoutName = layoutName.Trim(),
            HtmlStorageKey = htmlStorageKey,
            HtmlFileId = htmlFileId,
            DesignStorageKey = designStorageKey,
            DesignFileId = designFileId,
            PreviewStorageKey = previewStorageKey,
            PreviewFileId = previewFileId,
            IsDefault = isDefault,
            IsActive = true,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        return Result.Success(layout);
    }

    public void SetAsDefault()
    {
        IsDefault = true;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void UnsetDefault()
    {
        IsDefault = false;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        IsDefault = false;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
