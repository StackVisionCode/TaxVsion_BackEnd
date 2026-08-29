namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    // Compartida por ListCustomerThreads + ListThreadMessages (ThreadsController).
    public static readonly RateLimitPolicyDefinition CorrespondenceThreadRead = Define(
        "correspondence.f.thread_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition CorrespondenceThreadManage = Define(
        "correspondence.g.thread_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Compartida por GetMetadata + GetBody + GetAttachments + GetAttachmentDownloadUrl
    // (MessagesController) — incluye GetBody pese a hacer fetch on-demand a Connectors, mismo
    // criterio que cloudstorage.f.download_url (llama a un servicio externo sin ser búsqueda pesada).
    public static readonly RateLimitPolicyDefinition CorrespondenceMessageRead = Define(
        "correspondence.f.message_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // MessagesController.DownloadAttachment — dispara descarga real desde Connectors + subida a
    // CloudStorage, mismo perfil que cloudstorage.i.upload.
    public static readonly RateLimitPolicyDefinition CorrespondenceAttachmentDownload = Define(
        "correspondence.i.attachment_download",
        RateLimitCategory.I,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 30,
        windowSeconds: 600,
        RateLimitAlgorithm.FixedWindow,
        overlayQuota: 120
    );

    // MessagesController.StartReplyDraft — get-or-create de un Draft desde un mensaje.
    public static readonly RateLimitPolicyDefinition CorrespondenceReplyStart = Define(
        "correspondence.g.reply_start",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Compartida por List + GetById (DraftsController).
    public static readonly RateLimitPolicyDefinition CorrespondenceDraftRead = Define(
        "correspondence.f.draft_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por Create + AutoSave + Discard + AttachFile + RemoveAttachment (DraftsController).
    public static readonly RateLimitPolicyDefinition CorrespondenceDraftManage = Define(
        "correspondence.g.draft_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // DraftsController.Send — llamada SÍNCRONA y bloqueante a Postmaster (no 202 Accepted como
    // notification.g.email_send), la request HTTP no responde hasta tener el resultado real del
    // envío. Mismo perfil de costo/riesgo que payment_app.l.checkout_create, cupo copiado tal cual.
    public static readonly RateLimitPolicyDefinition CorrespondenceDraftSend = Define(
        "correspondence.l.draft_send",
        RateLimitCategory.L,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 10,
        windowSeconds: 60,
        RateLimitAlgorithm.FixedWindow,
        overlayQuota: 60
    );
}
