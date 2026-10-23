using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Domain.Participants;

public sealed record ParticipantEmail
{
    private ParticipantEmail(string value) => Value = value;

    public string Value { get; }

    public static Result<ParticipantEmail> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Result.Failure<ParticipantEmail>(FormParticipantErrors.InvalidEmail);
        var normalized = raw.Trim().ToLowerInvariant();
        if (normalized.Length > 254 || !normalized.Contains('@') || normalized.StartsWith('@') || normalized.EndsWith('@'))
            return Result.Failure<ParticipantEmail>(FormParticipantErrors.InvalidEmail);
        return Result.Success(new ParticipantEmail(normalized));
    }

    public override string ToString() => Value;
}
