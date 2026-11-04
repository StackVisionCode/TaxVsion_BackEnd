using BuildingBlocks.Domain;

namespace TaxVision.Signature.Domain.Delivery;

/// <summary>Un archivo dentro de una entrega (documento firmado, certificado o copia en curso).</summary>
public sealed class DeliveryItem : BaseEntity
{
    public const int MaxTitleLength = 200;

    private DeliveryItem() { }

    public Guid DeliveryId { get; private set; }
    public Guid FileId { get; private set; }
    public DeliveryItemKind Kind { get; private set; }
    public Guid? DocumentId { get; private set; }
    public string Title { get; private set; } = default!;
    public long SizeBytes { get; private set; }
    public int Order { get; private set; }

    internal static DeliveryItem Create(Guid deliveryId, DeliveryItemSpec spec, int order) =>
        new()
        {
            Id = Guid.NewGuid(),
            DeliveryId = deliveryId,
            FileId = spec.FileId,
            Kind = spec.Kind,
            DocumentId = spec.DocumentId,
            Title = spec.Title.Length > MaxTitleLength ? spec.Title[..MaxTitleLength] : spec.Title,
            SizeBytes = spec.SizeBytes,
            Order = order,
        };
}

/// <summary>Lo que se quiere entregar; <see cref="SizeBytes"/> sirve para decidir si cabe como adjunto.</summary>
public sealed record DeliveryItemSpec(
    Guid FileId,
    DeliveryItemKind Kind,
    Guid? DocumentId,
    string Title,
    long SizeBytes
);
