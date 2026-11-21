using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Sealing;

/// <summary>
/// Rol con que Signature declara sus archivos en CloudStorage (pares de <c>FileSourceCatalog</c>). Misma regla
/// de clientes que <see cref="SealedDocumentOwner"/>: con uno o ninguno el archivo es el sellado/certificado;
/// con varios es el maestro y cada cliente recibe su copia.
/// </summary>
public static class SealedFileRole
{
    public const string Sealed = "sealed";
    public const string Certificate = "certificate";
    public const string SealedMaster = "sealed_master";
    public const string CertificateMaster = "certificate_master";
    public const string PartialCopy = "partial_copy";
    public const string Original = "original";
    public const string OriginalSuperseded = "original_superseded";

    public static (string Role, Guid? RelatedCustomerId) Resolve(
        IReadOnlyCollection<Guid?> mappedCustomerIds,
        string baseRole
    )
    {
        var customers = mappedCustomerIds.OfType<Guid>().Distinct().ToList();
        return customers.Count switch
        {
            0 => (baseRole, null),
            1 => (baseRole, customers[0]),
            // Solo el sellado y el certificado tienen maestro; el resto queda sin cliente.
            _ => baseRole switch
            {
                Sealed => (SealedMaster, null),
                Certificate => (CertificateMaster, null),
                _ => (baseRole, null),
            },
        };
    }

    /// <summary>Origen de un sellado o certificado de la solicitud.</summary>
    public static SignatureFileSource For(SignatureRequest request, string baseRole)
    {
        var (role, customerId) = Resolve(request.Signers.Select(signer => signer.MappedCustomerId).ToList(), baseRole);
        return new SignatureFileSource(request.Id, role, customerId);
    }

    /// <summary>Rol de la copia por cliente: el del archivo copiado (certificado de la solicitud o de un documento, o sellado).</summary>
    public static string OfCopy(SignatureRequest request, Guid sourceFileId) =>
        request.CertificateFileId == sourceFileId
        || request.Documents.Any(document => document.CertificateFileId == sourceFileId)
            ? Certificate
            : Sealed;
}
