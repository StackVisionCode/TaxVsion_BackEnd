using BuildingBlocks.Results;

namespace TaxVision.Codes.Domain.ValueObjects;

public sealed record SubjectReference
{
    public SubjectType Type { get; }
    public string SubjectId { get; }

    private SubjectReference(SubjectType type, string subjectId)
    {
        Type = type;
        SubjectId = subjectId;
    }

    public static Result<SubjectReference> Create(SubjectType type, string subjectId)
    {
        if (!Enum.IsDefined(type))
            return Result.Failure<SubjectReference>(
                new Error("Codes.SubjectReference.InvalidType", "Subject type is invalid.")
            );

        if (string.IsNullOrWhiteSpace(subjectId) || subjectId.Trim().Length > 200)
            return Result.Failure<SubjectReference>(
                new Error("Codes.SubjectReference.InvalidId", "SubjectId is required and cannot exceed 200 characters.")
            );

        return Result.Success(new SubjectReference(type, subjectId.Trim()));
    }
}
