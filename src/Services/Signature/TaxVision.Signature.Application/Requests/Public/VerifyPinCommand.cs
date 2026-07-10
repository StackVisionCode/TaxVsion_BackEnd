namespace TaxVision.Signature.Application.Requests.Public;

public sealed record VerifyPinCommand(string Token, string Pin, string? ClientIp, string? UserAgent);
