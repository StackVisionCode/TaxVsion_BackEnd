using TaxVision.Customer.Domain.ContactPoints;

namespace TaxVision.Customer.Api.Requests;

public sealed record AddContactPointRequest(ContactPointType Type, string Value, string? Label, bool IsPrimary);
