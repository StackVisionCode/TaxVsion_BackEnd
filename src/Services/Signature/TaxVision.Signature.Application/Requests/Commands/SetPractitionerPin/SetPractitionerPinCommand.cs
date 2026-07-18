namespace TaxVision.Signature.Application.Requests.Commands.SetPractitionerPin;

public sealed record SetPractitionerPinCommand(Guid TenantId, Guid SignatureRequestId, Guid SetByUserId, string Pin);
