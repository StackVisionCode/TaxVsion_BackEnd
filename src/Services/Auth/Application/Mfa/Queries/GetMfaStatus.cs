using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Mfa;

namespace TaxVision.Auth.Application.Mfa.Queries;

public sealed record MfaMethodResponse(
    Guid Id,
    string Type,
    bool IsConfirmed,
    bool IsPreferred,
    string? MaskedDestination,
    DateTime? LastUsedAtUtc);

public sealed record TrustedDeviceResponse(
    Guid Id,
    string? UserAgent,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc);

public sealed record MfaStatusResponse(
    bool MfaEnabled,
    IReadOnlyList<MfaMethodResponse> Methods,
    int RecoveryCodesRemaining,
    IReadOnlyList<TrustedDeviceResponse> TrustedDevices);

public sealed record GetMyMfaStatusQuery(Guid UserId);

public static class GetMyMfaStatusHandler
{
    public static async Task<Result<MfaStatusResponse>> Handle(
        GetMyMfaStatusQuery query,
        IUserRepository users,
        IMfaRepository mfa,
        CancellationToken ct)
    {
        var user = await users.GetByIdAsync(query.UserId, ct);
        if (user is null)
            return Result.Failure<MfaStatusResponse>(new Error("User.NotFound", "User does not exist."));

        var methods = await mfa.GetMethodsAsync(query.UserId, ct);
        var recoveryCodes = await mfa.GetRecoveryCodesAsync(query.UserId, ct);
        var devices = await mfa.GetTrustedDevicesAsync(query.UserId, ct);

        return Result.Success(new MfaStatusResponse(
            user.MfaEnabled,
            methods.Select(method => new MfaMethodResponse(
                method.Id,
                method.Type.ToString(),
                method.IsConfirmed,
                method.IsPreferred,
                Mask(method.Destination),
                method.LastUsedAtUtc)).ToList(),
            recoveryCodes.Count(code => code.IsUsable),
            devices.Where(device => device.IsActive)
                .Select(device => new TrustedDeviceResponse(
                    device.Id, device.UserAgent, device.CreatedAtUtc, device.ExpiresAtUtc))
                .ToList()));
    }

    private static string? Mask(string? destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
            return null;

        if (destination.Contains('@'))
        {
            var parts = destination.Split('@');
            var local = parts[0];
            var visible = local.Length <= 2 ? local[..1] : local[..2];
            return $"{visible}***@{parts[1]}";
        }

        return destination.Length <= 4
            ? "***"
            : $"***{destination[^4..]}";
    }
}

public sealed record GetTenantMfaPolicyQuery(Guid TenantId);

public sealed record TenantMfaPolicyResponse(
    bool RequireForAdmins,
    bool RequireForEmployees,
    bool RequireForCustomerPortal,
    int TrustedDeviceDays);

public static class GetTenantMfaPolicyHandler
{
    public static async Task<Result<TenantMfaPolicyResponse>> Handle(
        GetTenantMfaPolicyQuery query,
        IMfaRepository mfa,
        CancellationToken ct)
    {
        var policy = await mfa.GetPolicyAsync(query.TenantId, ct)
            ?? TenantMfaPolicy.CreateDefault(query.TenantId);

        return Result.Success(new TenantMfaPolicyResponse(
            policy.RequireForAdmins,
            policy.RequireForEmployees,
            policy.RequireForCustomerPortal,
            policy.TrustedDeviceDays));
    }
}
