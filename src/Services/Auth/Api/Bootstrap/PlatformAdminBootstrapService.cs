using System.Net.Mail;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Invitations;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Persistence;

namespace TaxVision.Auth.Api.Bootstrap;

public sealed class PlatformBootstrapOptions
{
    public const string SectionName = "PlatformBootstrap";

    public bool Enabled { get; init; }
    public string? Email { get; init; }
    public string? InvitationToken { get; init; }
    public int InvitationValidityHours { get; init; } = 24;
}

public sealed class PlatformAdminBootstrapService(
    IServiceScopeFactory scopeFactory,
    IOptions<PlatformBootstrapOptions> options,
    ILogger<PlatformAdminBootstrapService> logger) : IHostedService
{
    private readonly PlatformBootstrapOptions _options = options.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return;

        var email = _options.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!MailAddress.TryCreate(email, out var parsedEmail) ||
            !string.Equals(parsedEmail.Address, email, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "PlatformBootstrap:Email must contain a valid email.");
        }

        if (string.IsNullOrWhiteSpace(_options.InvitationToken) ||
            _options.InvitationToken.Length < 32)
        {
            throw new InvalidOperationException(
                "PlatformBootstrap:InvitationToken must contain at least 32 characters.");
        }

        if (_options.InvitationValidityHours is < 1 or > 168)
        {
            throw new InvalidOperationException(
                "PlatformBootstrap:InvitationValidityHours must be between 1 and 168.");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var tokens = scope.ServiceProvider.GetRequiredService<IInvitationTokenService>();
        var invitations = scope.ServiceProvider.GetRequiredService<IInvitationRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var platformTenantExists = await db.Tenants.AnyAsync(
            tenant =>
                tenant.Id == PlatformTenant.Id &&
                tenant.Kind == TenantKind.Platform &&
                tenant.IsActive,
            cancellationToken);
        if (!platformTenantExists)
        {
            throw new InvalidOperationException(
                "The reserved platform tenant is missing. Apply Auth migrations before enabling bootstrap.");
        }

        var platformAdminExists = await db.Users.AnyAsync(
            user =>
                user.TenantId == PlatformTenant.Id &&
                user.ActorType == UserActorType.PlatformAdmin,
            cancellationToken);
        if (platformAdminExists)
        {
            logger.LogInformation(
                "Platform bootstrap skipped because a PlatformAdmin already exists.");
            return;
        }

        var tokenHash = tokens.Hash(_options.InvitationToken);
        var existingInvitation = await invitations.GetByTokenHashAsync(
            tokenHash,
            cancellationToken);
        if (existingInvitation is not null)
        {
            logger.LogInformation(
                "Platform bootstrap invitation already exists with status {Status}.",
                existingInvitation.Status);
            return;
        }

        if (await invitations.HasPendingAsync(
                PlatformTenant.Id,
                email,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "A different pending platform invitation already exists for the configured email.");
        }

        var invitationResult = Invitation.Create(
            PlatformTenant.Id,
            email,
            UserActorType.PlatformAdmin,
            customerId: null,
            invitedByUserId: null,
            tokenHash: tokenHash,
            expiresAtUtc: DateTime.UtcNow.AddHours(_options.InvitationValidityHours));
        if (invitationResult.IsFailure)
            throw new InvalidOperationException(invitationResult.Error.Message);

        await invitations.AddAsync(invitationResult.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Platform bootstrap invitation created for {Email}. Disable PlatformBootstrap after acceptance.",
            email);
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
