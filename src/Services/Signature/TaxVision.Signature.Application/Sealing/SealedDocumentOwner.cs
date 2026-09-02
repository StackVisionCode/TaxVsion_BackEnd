namespace TaxVision.Signature.Application.Sealing;

/// <summary>
/// Politica de propiedad de un documento firmado en CloudStorage. Si hay EXACTAMENTE un
/// cliente firmante mapeado (regla P-14, <c>Signer.MappedCustomerId</c>), el sellado y su
/// certificado pertenecen a ese cliente ("Customer") y aparecen bajo su carpeta
/// "Signed Documents" en Documents. Con 0 o varios cae al dueno de firma ("Signature"),
/// sin regresion. No afecta el flujo de firma: Signature siempre recupera por FileId.
/// </summary>
public static class SealedDocumentOwner
{
    public static (string OwnerType, Guid OwnerId) Resolve(
        IReadOnlyCollection<Guid?> mappedCustomerIds,
        Guid fallbackSignatureOwnerId
    )
    {
        var distinctCustomers = mappedCustomerIds
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        return distinctCustomers.Count == 1
            ? ("Customer", distinctCustomers[0])
            : ("Signature", fallbackSignatureOwnerId);
    }
}
