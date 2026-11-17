namespace TaxVision.Codes.Domain.ValueObjects;

public enum SubjectType
{
    Tenant = 1,
    Taxpayer = 2,
    User = 3,
    Anonymous = 4,

    /// <summary>Una persona compradora (HMAC de su email normalizado): cuenta los límites por sujeto del alta.</summary>
    Buyer = 5,
}
