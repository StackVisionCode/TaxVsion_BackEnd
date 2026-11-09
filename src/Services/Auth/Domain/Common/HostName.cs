using BuildingBlocks.Results;

namespace TaxVision.Auth.Domain.Common;

/// <summary>Host completo en minúsculas, por ejemplo "portal.oficina.com".</summary>
public sealed record HostName
{
    private HostName(string value) => Value = value;

    public string Value { get; }

    public static Result<HostName> Create(string? raw)
    {
        var value = raw?.Trim().ToLowerInvariant() ?? string.Empty;
        if (value.Length is 0 or > 253 || !value.Contains('.'))
            return Result.Failure<HostName>(new Error("TenantDomain.HostInvalid", "Host is invalid."));

        return Result.Success(new HostName(value));
    }

    public override string ToString() => Value;
}
