using BuildingBlocks.Domain;

namespace TaxVision.Tasks.ClientForms.Domain.Templates;

// Revisión experta (D-17) de una versión publicada de una plantilla de biblioteca. Hija de FormTemplate.
public sealed class FormLibraryReview : TenantEntity
{
    private FormLibraryReview() { }

    public Guid FormTemplateId { get; private set; }
    public int ReviewedVersionNumber { get; private set; }
    public string ReviewerRole { get; private set; } = string.Empty;
    public Guid ReviewedByUserId { get; private set; }
    public DateTime ReviewedAtUtc { get; private set; }
    public string? Note { get; private set; }

    internal static FormLibraryReview Create(
        Guid tenantId,
        Guid formTemplateId,
        int reviewedVersionNumber,
        string reviewerRole,
        Guid reviewedByUserId,
        string? note,
        DateTime nowUtc
    )
    {
        var review = new FormLibraryReview
        {
            FormTemplateId = formTemplateId,
            ReviewedVersionNumber = reviewedVersionNumber,
            ReviewerRole = reviewerRole,
            ReviewedByUserId = reviewedByUserId,
            ReviewedAtUtc = nowUtc,
            Note = note,
        };
        review.SetTenant(tenantId);
        return review;
    }
}
