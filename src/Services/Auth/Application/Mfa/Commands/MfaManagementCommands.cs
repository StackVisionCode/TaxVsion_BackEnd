using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Mfa;
using Wolverine;

namespace TaxVision.Auth.Application.Mfa.Commands;

// ---------------------------------------------------------------------------
// Desactivar MFA (requiere contraseña; bloqueado si la política lo exige)
// ---------------------------------------------------------------------------

public sealed record DisableMfaCommand(Guid UserId, string Password);

public static class DisableMfaHandler
{
    public static async Task<Result> Handle(
        DisableMfaCommand command,
        IUserRepository users,
        IMfaRepository mfa,
        IPasswordHasher hasher,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct)
    {
        var user = await users.GetByIdAsync(command.UserId, ct);
        if (user is null || !user.IsActive)
            return Result.Failure(new Error("User.NotFound", "User does not exist."));

        if (!hasher.Verify(command.Password, user.PasswordHash))
            return Result.Failure(new Error("Auth.Invalid", "Invalid credentials."));

        var policy = await mfa.GetPolicyAsync(user.TenantId, ct);
        var requiredByPolicy = policy?.RequiresFor(user.ActorType)
            ?? user.ActorType is Domain.Users.UserActorType.TenantAdmin
                or Domain.Users.UserActorType.PlatformAdmin;
        if (requiredByPolicy)
        {
            return Result.Failure(
                new Error("Mfa.RequiredByPolicy", "MFA is required by your tenant policy."));
        }

        user.DisableMfa();

        var methods = await mfa.GetMethodsAsync(user.Id, ct);
        foreach (var method in methods)
            mfa.RemoveMethod(method);

        var recoveryCodes = await mfa.GetRecoveryCodesAsync(user.Id, ct);
        mfa.RemoveRecoveryCodes(recoveryCodes);

        var devices = await mfa.GetTrustedDevicesAsync(user.Id, ct);
        foreach (var device in devices)
            device.Revoke();

        await bus.PublishAsync(new SecurityAlertIntegrationEvent
        {
            TenantId = user.TenantId,
            UserId = user.Id,
            AlertType = SecurityAlertType.MfaDisabled,
            IpAddress = request.IpAddress,
            UserAgent = request.UserAgent,
            CorrelationId = correlation.CorrelationId
        });

        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId, user.Id, AuthAuditAction.MfaDisabled, true,
                request.IpAddress, request.UserAgent, correlation.CorrelationId),
            ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ---------------------------------------------------------------------------
// Regenerar recovery codes
// ---------------------------------------------------------------------------

public sealed record RegenerateRecoveryCodesCommand(Guid UserId, string Password);

public sealed record RegenerateRecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);

public static class RegenerateRecoveryCodesHandler
{
    public static async Task<Result<RegenerateRecoveryCodesResponse>> Handle(
        RegenerateRecoveryCodesCommand command,
        IUserRepository users,
        IMfaRepository mfa,
        IPasswordHasher hasher,
        ISecureTokenService tokens,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var user = await users.GetByIdAsync(command.UserId, ct);
        if (user is null || !user.IsActive || !user.MfaEnabled)
        {
            return Result.Failure<RegenerateRecoveryCodesResponse>(
                new Error("Mfa.NotEnabled", "MFA is not enabled."));
        }

        if (!hasher.Verify(command.Password, user.PasswordHash))
        {
            return Result.Failure<RegenerateRecoveryCodesResponse>(
                new Error("Auth.Invalid", "Invalid credentials."));
        }

        var existing = await mfa.GetRecoveryCodesAsync(user.Id, ct);
        mfa.RemoveRecoveryCodes(existing);

        var rawCodes = new List<string>(ConfirmTotpHandler.RecoveryCodeCount);
        var entities = new List<RecoveryCode>(ConfirmTotpHandler.RecoveryCodeCount);
        for (var i = 0; i < ConfirmTotpHandler.RecoveryCodeCount; i++)
        {
            var raw = tokens.GenerateToken(6);
            rawCodes.Add(raw);
            entities.Add(RecoveryCode.Create(user.TenantId, user.Id, tokens.Hash(raw)));
        }
        await mfa.AddRecoveryCodesAsync(entities, ct);

        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId, user.Id, AuthAuditAction.RecoveryCodesRegenerated, true,
                request.IpAddress, request.UserAgent, correlation.CorrelationId),
            ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new RegenerateRecoveryCodesResponse(rawCodes));
    }
}

// ---------------------------------------------------------------------------
// Revocar dispositivo confiable
// ---------------------------------------------------------------------------

public sealed record RevokeTrustedDeviceCommand(Guid UserId, Guid DeviceId);

public static class RevokeTrustedDeviceHandler
{
    public static async Task<Result> Handle(
        RevokeTrustedDeviceCommand command,
        IMfaRepository mfa,
        IUserRepository users,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var user = await users.GetByIdAsync(command.UserId, ct);
        if (user is null)
            return Result.Failure(new Error("User.NotFound", "User does not exist."));

        var devices = await mfa.GetTrustedDevicesAsync(command.UserId, ct);
        var device = devices.FirstOrDefault(value => value.Id == command.DeviceId);
        if (device is null)
            return Result.Failure(new Error("Mfa.DeviceNotFound", "Trusted device does not exist."));

        device.Revoke();
        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId, user.Id, AuthAuditAction.TrustedDeviceRevoked, true,
                request.IpAddress, request.UserAgent, correlation.CorrelationId,
                targetType: "TrustedDevice", targetId: device.Id),
            ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ---------------------------------------------------------------------------
// Política MFA del tenant
// ---------------------------------------------------------------------------

public sealed record UpdateTenantMfaPolicyCommand(
    Guid TenantId,
    Guid UpdatedByUserId,
    bool RequireForEmployees,
    bool RequireForCustomerPortal,
    int TrustedDeviceDays);

public static class UpdateTenantMfaPolicyHandler
{
    public static async Task<Result> Handle(
        UpdateTenantMfaPolicyCommand command,
        IMfaRepository mfa,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var policy = await mfa.GetPolicyAsync(command.TenantId, ct);
        if (policy is null)
        {
            policy = TenantMfaPolicy.CreateDefault(command.TenantId);
            await mfa.AddPolicyAsync(policy, ct);
        }

        var result = policy.Update(
            command.RequireForEmployees,
            command.RequireForCustomerPortal,
            command.TrustedDeviceDays);
        if (result.IsFailure)
            return result;

        await audit.AddAsync(
            AuthAuditLog.Record(
                command.TenantId, command.UpdatedByUserId, AuthAuditAction.MfaPolicyUpdated, true,
                request.IpAddress, request.UserAgent, correlation.CorrelationId),
            ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
