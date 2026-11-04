using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Settings;

/// <summary>
/// Cómo entrega la oficina los documentos firmados: cuántos días vale el enlace, si quien lo recibió puede
/// pedir uno nuevo cuando venció y si los PDFs también van adjuntos (solo si caben bajo el tope).
/// </summary>
public sealed record DeliveryPolicy
{
    public const int DefaultLinkLifetimeDays = 30;
    public const int MinLinkLifetimeDays = 1;
    public const int MaxLinkLifetimeDays = 365;
    public const long DefaultMaxAttachmentBytes = 10L * 1024 * 1024;
    public const long MinAttachmentBytes = 1L * 1024 * 1024;

    /// <summary>Techo de lo adjuntable: la mayoría de proveedores de correo corta cerca de 25 MB codificados.</summary>
    public const long MaxAttachmentBytesCeiling = 20L * 1024 * 1024;

    public int LinkLifetimeDays { get; private init; }
    public bool AllowPublicReissue { get; private init; }
    public bool AttachDocuments { get; private init; }
    public long MaxAttachmentBytes { get; private init; }

    private DeliveryPolicy() { }

    public static DeliveryPolicy Default() =>
        new()
        {
            LinkLifetimeDays = DefaultLinkLifetimeDays,
            AllowPublicReissue = true,
            AttachDocuments = false,
            MaxAttachmentBytes = DefaultMaxAttachmentBytes,
        };

    public static Result<DeliveryPolicy> Create(
        int linkLifetimeDays,
        bool allowPublicReissue,
        bool attachDocuments,
        long maxAttachmentBytes
    )
    {
        if (linkLifetimeDays is < MinLinkLifetimeDays or > MaxLinkLifetimeDays)
            return Result.Failure<DeliveryPolicy>(
                new Error(
                    "Signature.DeliveryPolicy.LinkLifetime",
                    $"Delivery links must last between {MinLinkLifetimeDays} and {MaxLinkLifetimeDays} days."
                )
            );
        if (maxAttachmentBytes is < MinAttachmentBytes or > MaxAttachmentBytesCeiling)
            return Result.Failure<DeliveryPolicy>(
                new Error("Signature.DeliveryPolicy.Attachment", "The attachment limit must be between 1 MB and 20 MB.")
            );

        return Result.Success(
            new DeliveryPolicy
            {
                LinkLifetimeDays = linkLifetimeDays,
                AllowPublicReissue = allowPublicReissue,
                AttachDocuments = attachDocuments,
                MaxAttachmentBytes = maxAttachmentBytes,
            }
        );
    }

    public TimeSpan LinkLifetime => TimeSpan.FromDays(LinkLifetimeDays);
}
