using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Customer.Domain.Customers.ValueObjects;

namespace TaxVision.Customer.Domain.Relations;

public sealed class CustomerRelation : TenantEntity
{
    private CustomerRelation() { }

    public Guid CustomerId { get; private set; }
    public RelationshipKind RelationshipKind { get; private set; }
    public RelationPurpose Purposes { get; private set; }
    public PersonalName Name { get; private set; } = default!;
    public EmailAddress? PrimaryEmail { get; private set; }
    public PhoneNumber? PrimaryPhone { get; private set; }
    public DateOnly? DateOfBirth { get; private set; }
    public AddressValue? Address { get; private set; }
    public bool IsActive { get; private set; }
    public CustomerRelationFiscalProfile? FiscalProfile { get; private set; }

    internal static Result<CustomerRelation> Create(
        Guid tenantId,
        Guid customerId,
        RelationshipKind relationshipKind,
        RelationPurpose purposes,
        PersonalName name,
        EmailAddress? email = null,
        PhoneNumber? phone = null,
        DateOnly? dateOfBirth = null,
        AddressValue? address = null
    )
    {
        if (customerId == Guid.Empty)
            return Result.Failure<CustomerRelation>(new Error("Relation.Customer", "CustomerId is required."));
        if (purposes == RelationPurpose.None)
            return Result.Failure<CustomerRelation>(
                new Error("Relation.Purposes", "At least one purpose is required.")
            );

        var entity = new CustomerRelation
        {
            Id = Guid.Empty,
            CustomerId = customerId,
            RelationshipKind = relationshipKind,
            Purposes = purposes,
            Name = name,
            PrimaryEmail = email,
            PrimaryPhone = phone,
            DateOfBirth = dateOfBirth,
            IsActive = true,
            Address = address,
        };
        entity.SetTenant(tenantId);
        return Result.Success(entity);
    }

    public Result SetFiscalProfile(
        TaxRelationshipRole role,
        byte[] taxIdentifierCipher,
        string taxIdentifierBlindIndex,
        string taxIdentifierLast4,
        int taxYear,
        bool qualifiesAsDependent,
        bool livedWithTaxpayer,
        Guid byUserId
    )
    {
        var taxRelevant =
            Purposes.HasFlag(RelationPurpose.Dependent)
            || Purposes.HasFlag(RelationPurpose.TaxHouseholdMember)
            || RelationshipKind == RelationshipKind.Spouse;
        if (!taxRelevant)
            return Result.Failure(
                new Error(
                    "Relation.NotFiscal",
                    "Only spouse, dependent or tax-household relations can have a fiscal profile."
                )
            );

        if (FiscalProfile is null)
        {
            var createResult = CustomerRelationFiscalProfile.Create(
                tenantId: TenantId,
                customerRelationId: Id,
                role: role,
                taxIdentifierCipher: taxIdentifierCipher,
                taxIdentifierBlindIndex: taxIdentifierBlindIndex,
                taxIdentifierLast4: taxIdentifierLast4,
                taxYear: taxYear,
                updatedByUserId: byUserId
            );
            if (createResult.IsFailure)
                return createResult;

            createResult.Value.UpdateEligibility(qualifiesAsDependent, livedWithTaxpayer, byUserId);
            FiscalProfile = createResult.Value;
        }
        else
        {
            FiscalProfile.ReplaceTaxIdentifier(
                taxIdentifierCipher,
                taxIdentifierBlindIndex,
                taxIdentifierLast4,
                byUserId
            );
            FiscalProfile.UpdateEligibility(qualifiesAsDependent, livedWithTaxpayer, byUserId);
        }

        return Result.Success();
    }

    internal void Update(
        RelationshipKind kind,
        RelationPurpose purposes,
        PersonalName name,
        EmailAddress? email,
        PhoneNumber? phone,
        DateOnly? dateOfBirth,
        AddressValue? address
    )
    {
        RelationshipKind = kind;
        Purposes = purposes;
        Name = name;
        PrimaryEmail = email;
        PrimaryPhone = phone;
        DateOfBirth = dateOfBirth;
        Address = address;
    }

    internal void Deactivate() => IsActive = false;

    internal void Reactivate() => IsActive = true;
}
