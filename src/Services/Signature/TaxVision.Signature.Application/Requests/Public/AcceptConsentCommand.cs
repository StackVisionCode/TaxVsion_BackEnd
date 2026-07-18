namespace TaxVision.Signature.Application.Requests.Public;

public sealed record AcceptConsentCommand(string Token, string? ClientIp, string? UserAgent);
