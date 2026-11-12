namespace TaxVision.Subscription.Domain.Catalog;

/// <summary>
/// Una revisión del catálogo ya aplicada al desplegar (D3). Su existencia impide que vuelva a correr. Sus avisos
/// (recálculo y <c>CatalogChanged</c>) salen cuando Wolverine ya arrancó; hasta entonces queda pendiente.
/// </summary>
public sealed class AppliedCatalogRevision
{
    public const int MaxSummaryLength = 4000;

    private AppliedCatalogRevision() { }

    public string RevisionId { get; private set; } = default!;
    public string Summary { get; private set; } = default!;
    public DateTime AppliedAtUtc { get; private set; }
    public DateTime? FollowUpPublishedAtUtc { get; private set; }

    public static AppliedCatalogRevision Create(string revisionId, string summary, DateTime appliedAtUtc) =>
        new()
        {
            RevisionId = revisionId,
            Summary = summary.Length > MaxSummaryLength ? summary[..MaxSummaryLength] : summary,
            AppliedAtUtc = appliedAtUtc,
        };

    /// <summary>Idempotente: la primera marca queda.</summary>
    public void MarkFollowUpPublished(DateTime nowUtc) => FollowUpPublishedAtUtc ??= nowUtc;
}
