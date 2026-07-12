namespace TaxVision.Signature.Application.Requests.Public;

public sealed record RejectSignatureCommand(string Token, string? Reason, string? ClientIp, string? UserAgent);
