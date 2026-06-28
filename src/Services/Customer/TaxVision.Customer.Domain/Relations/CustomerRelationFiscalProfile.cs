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
        Guid updatedByUserId
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

    public void ReplaceTaxIdentifier(byte[] cipher, string blindIndex, string last4, Guid updatedByUserId)
    {
        TaxIdentifierCipher = cipher;
        TaxIdentifierBlindIndex = blindIndex;
        TaxIdentifierLast4 = last4;
        UpdatedAtUtc = DateTime.UtcNow;
        UpdatedByUserId = updatedByUserId;
    }
}
