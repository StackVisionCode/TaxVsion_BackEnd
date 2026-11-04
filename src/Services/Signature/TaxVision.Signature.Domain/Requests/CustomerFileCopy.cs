using BuildingBlocks.Domain;

namespace TaxVision.Signature.Domain.Requests;

/// <summary>
/// Copia de un archivo final (documento sellado o certificado) en la carpeta "Signed Documents" del portal
/// de un cliente firmante. Solo existe cuando firman VARIOS clientes: con uno solo, el original ya es suyo.
/// El original (dueño Signature) sigue siendo la evidencia; la copia es lo que ve cada cliente.
/// </summary>
public sealed class CustomerFileCopy : BaseEntity
{
    private CustomerFileCopy() { }

    public Guid SignatureRequestId { get; private set; }
    public Guid SourceFileId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid CopyFileId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    internal static CustomerFileCopy Create(
        Guid requestId,
        Guid sourceFileId,
        Guid customerId,
        Guid copyFileId,
        DateTime nowUtc
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            SignatureRequestId = requestId,
            SourceFileId = sourceFileId,
            CustomerId = customerId,
            CopyFileId = copyFileId,
            CreatedAtUtc = nowUtc,
        };
}

/// <summary>Un archivo final que todavía tiene que quedar en el portal de un cliente.</summary>
public sealed record PendingCustomerCopy(Guid SourceFileId, Guid CustomerId);
