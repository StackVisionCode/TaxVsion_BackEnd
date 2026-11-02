using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Requests.ValueObjects;

/// <summary>Por qué el servidor rechazó el PDF original (código estable para la UI + motivo legible).</summary>
public sealed record DocumentRejection
{
    public const int MaxCodeLength = 100;
    public const int MaxReasonLength = 500;

    public string Code { get; }
    public string Reason { get; }

    private DocumentRejection(string code, string reason)
    {
        Code = code;
        Reason = reason;
    }

    public static Result<DocumentRejection> Create(string? code, string? reason)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > MaxCodeLength)
            return Result.Failure<DocumentRejection>(
                new Error("Signature.Document.RejectionCode", $"Rejection code is required (max {MaxCodeLength}).")
            );
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure<DocumentRejection>(
                new Error("Signature.Document.RejectionReason", "Rejection reason is required.")
            );

        var trimmed = reason.Trim();
        return Result.Success(
            new DocumentRejection(code.Trim(), trimmed.Length > MaxReasonLength ? trimmed[..MaxReasonLength] : trimmed)
        );
    }
}
