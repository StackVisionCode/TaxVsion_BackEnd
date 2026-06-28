namespace TaxVision.Customer.Domain.Relations;

[Flags]
public enum RelationPurpose
{
    None = 0,
    Dependent = 1,
    TaxHouseholdMember = 2,
    EmergencyContact = 4,
    AuthorizedRepresentative = 8,
    BusinessContact = 16,
    BeneficialOwner = 32,
}
