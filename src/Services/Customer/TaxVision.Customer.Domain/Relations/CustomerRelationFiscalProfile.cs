using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Customer.Domain.Relations;

public sealed class CustomerRelationFiscalProfile : TenantEntity
{
    private CustomerRelationFiscalProfile() { }

    public Guid CustomerRelationId { get; private set; }
    public TaxRelationshipRole Role { get; private set; }
    public byte[] TaxIdentifierCipher { get; private set; } = default!;
    public string TaxIdentifierBlindIndex { get; private set; } = default!;
    public string TaxIdentifierLast4 { get; private set; } = default!;

    // B-9/X-9: versión de la clave con la que está cifrado el tax identifier (ver CustomerFiscalProfile).
    public short TaxIdentifierKeyVersion { get; private set; } = 1;
    public int TaxYear { get; private set; }
    public bool QualifiesAsDependent { get; private set; }
    public bool LivedWithTaxpayer { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid UpdatedByUserId { get; private set; }

    internal static Result<CustomerRelationFiscalProfile> Create(
        Guid tenantId,
        Guid customerRelationId,
        TaxRelationshipRole role,
        byte[] taxIdentifierCipher,
        string taxIdentifierBlindIndex,
        string taxIdentifierLast4,
        int taxYear,
        Guid updatedByUserId,
        short taxIdentifierKeyVersion = 1
    )
    {
        if (customerRelationId == Guid.Empty)
            return Result.Failure<CustomerRelationFiscalProfile>(
                new Error("RelationFiscalProfile.Relation", "CustomerRelationId is required.")
            );
        if (taxIdentifierCipher is null || taxIdentifierCipher.Length == 0)
            return Result.Failure<CustomerRelationFiscalProfile>(
                new Error("RelationFiscalProfile.TaxId", "Tax identifier cipher is required.")
            );
        if (taxYear < 2000 || taxYear > 2100)
            return Result.Failure<CustomerRelationFiscalProfile>(
                new Error("RelationFiscalProfile.TaxYear", "Tax year out of range.")
            );

        var entity = new CustomerRelationFiscalProfile
        {
            Id = Guid.Empty,
            CustomerRelationId = customerRelationId,
            Role = role,
            TaxIdentifierCipher = taxIdentifierCipher,
            TaxIdentifierBlindIndex = taxIdentifierBlindIndex,
            TaxIdentifierLast4 = taxIdentifierLast4,
            TaxIdentifierKeyVersion = taxIdentifierKeyVersion,
            TaxYear = taxYear,
            UpdatedAtUtc = DateTime.UtcNow,
            UpdatedByUserId = updatedByUserId,
        };
        entity.SetTenant(tenantId);
        return Result.Success(entity);
    }

    public void UpdateEligibility(bool qualifiesAsDependent, bool livedWithTaxpayer, Guid updatedByUserId)
    {
        QualifiesAsDependent = qualifiesAsDependent;
        LivedWithTaxpayer = livedWithTaxpayer;
        UpdatedAtUtc = DateTime.UtcNow;
        UpdatedByUserId = updatedByUserId;
    }

    public void ReplaceTaxIdentifier(
        byte[] cipher,
        string blindIndex,
        string last4,
        Guid updatedByUserId,
        short keyVersion = 1
    )
    {
        TaxIdentifierCipher = cipher;
        TaxIdentifierBlindIndex = blindIndex;
        TaxIdentifierLast4 = last4;
        TaxIdentifierKeyVersion = keyVersion;
        UpdatedAtUtc = DateTime.UtcNow;
        UpdatedByUserId = updatedByUserId;
    }

    // B-9/X-9: re-cifrado del sweep de rotación (ver CustomerFiscalProfile).
    public void ReEncryptTaxIdentifier(byte[] cipher, string blindIndex, short keyVersion)
    {
        TaxIdentifierCipher = cipher;
        TaxIdentifierBlindIndex = blindIndex;
        TaxIdentifierKeyVersion = keyVersion;
    }
}
