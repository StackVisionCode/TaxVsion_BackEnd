using BuildingBlocks.Persistence;
using BuildingBlocks.Messaging;
using BuildingBlocks.Results;
using BuildingBlocks.Common;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Caching;
using TaxVision.Tenant.Application.Tenants;
using Microsoft.Extensions.Logging;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using Wolverine;
using BuildingBlocks.Tenancy;

namespace TaxVision.Tenant.Application.Tenants.Commands;

public sealed record CreateTenantCommand(
    string Name,
    string Subdomain,
    string AdminEmail,
    string DefaultTimeZoneId);

public sealed record CreateTenantResponse(
    Guid Id,
    string Name,
    string Subdomain,
    string DefaultTimeZoneId,
    string AdminActivationToken,
    DateTime AdminInvitationExpiresAtUtc);

public sealed record TenantResponse(
    Guid Id,
    string Name,
    string Subdomain,
    string DefaultTimeZoneId);

public static class CreateTenantHandler
{
    public static async Task<Result<CreateTenantResponse>> Handle(
        CreateTenantCommand cmd,
        ITenantRepository repo,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ICacheService cache,
        ILogger<CreateTenantCommand> logger,
        CancellationToken ct)
    {
        var adminEmail = cmd.AdminEmail.Trim().ToLowerInvariant();
        if (!MailAddress.TryCreate(adminEmail, out var parsedEmail) ||
            !string.Equals(parsedEmail.Address, adminEmail, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<CreateTenantResponse>(
                new Error("Tenant.AdminEmail", "Admin email is invalid."));
        }

        if (await repo.SubDomainExistsAsync(cmd.Subdomain, ct))
        {
            return Result.Failure<CreateTenantResponse>(
                new Error("Tenant.SubdomainConflict", "Subdomain already exists."));
        }

        var result = Domain.Tenant.Create(
            cmd.Name,
            cmd.Subdomain,
            cmd.DefaultTimeZoneId);
        if (result.IsFailure)
        {
            return Result.Failure<CreateTenantResponse>(result.Error);
        }

        var tenant = result.Value;
        var activationToken = ToBase64Url(RandomNumberGenerator.GetBytes(32));
        var activationTokenHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(activationToken)));
        var invitationExpiresAtUtc = DateTime.UtcNow.AddDays(7);

        await repo.AddAsync(tenant, ct);
        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(new TenantCreatedIntegrationEvent
        {
            NewTenantId = tenant.Id,
            TenantId = tenant.Id,
            Name = tenant.Name,
            SubDomain = tenant.SubDomain,
            Kind = TenantKind.Customer.ToString(),
            DefaultTimeZoneId = tenant.DefaultTimeZoneId,
            AdminEmail = adminEmail,
            AdminInvitationTokenHash = activationTokenHash,
            AdminInvitationExpiresAtUtc = invitationExpiresAtUtc,
            CorrelationId = correlation.CorrelationId
        });
        try
        {
            await TenantListCache.InvalidateAsync(cache, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Tenant list cache invalidation failed for tenant {TenantId}.",
                tenant.Id);
        }

        return Result.Success(
            new CreateTenantResponse(
                tenant.Id,
                tenant.Name,
                tenant.SubDomain,
                tenant.DefaultTimeZoneId,
                activationToken,
                invitationExpiresAtUtc));
    }

    private static string ToBase64Url(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
