using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Profiles;

/// <summary>Una sola firma default por (tenant, dueño): el índice único filtrado la respalda en la base.</summary>
public static class DefaultSignatureProfilePolicy
{
    /// <summary>Quita la marca de las demás firmas del ámbito y la pone en <paramref name="target"/>.</summary>
    public static Result SetDefault(IEnumerable<SignatureProfile> current, SignatureProfile target)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(target);

        var marked = target.MarkDefault();
        if (marked.IsFailure)
            return marked;

        foreach (var other in current.Where(profile => profile.Id != target.Id))
            other.UnsetDefault();
        return Result.Success();
    }
}
