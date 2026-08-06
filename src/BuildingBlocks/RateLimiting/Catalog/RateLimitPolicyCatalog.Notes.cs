namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    public static readonly RateLimitPolicyDefinition NotesGet = Define(
        "notes.f.get",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por ListByReference + ListMine (NotesController) — mismo perfil de lectura paginada.
    public static readonly RateLimitPolicyDefinition NotesList = Define(
        "notes.f.list",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // CustomerPortal leyendo sus propias notas ClientVisible.
    public static readonly RateLimitPolicyDefinition NotesPortalRead = Define(
        "notes.f.portal_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition NotesCreate = Define(
        "notes.g.create",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Compartida por UpdateContent/ChangeVisibility/Pin/Unpin/SetColor/Archive/Restore/Delete/
    // AttachFile/DetachFile — todas escrituras simples sobre una nota existente.
    public static readonly RateLimitPolicyDefinition NotesWrite = Define(
        "notes.g.write",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Búsqueda simple sobre ContentPreview (sin full-text en v1) — más costosa que un GET normal.
    public static readonly RateLimitPolicyDefinition NotesSearch = Define(
        "notes.h.search",
        RateLimitCategory.H,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 20,
        windowSeconds: 60,
        RateLimitAlgorithm.SlidingWindow,
        overlayQuota: 100
    );
}
