using TaxVision.Customer.Domain.ContactPoints;

namespace TaxVision.Customer.Api.Requests;

public sealed record UpdateContactPointRequest(ContactPointType Type, string Value, string? Label, bool IsPrimary);
