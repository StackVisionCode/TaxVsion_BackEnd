using BuildingBlocks.Infrastructure.Hosting;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Domain.Invitations;
using TaxVision.Auth.Infrastructure.Persistence;

namespace TaxVision.Auth.Api.Jobs;

/// <summary>
/// Mantenimiento periódico (cada hora): expira invitaciones vencidas y purga
/// tokens/desafíos caducados hace más de 30 días. Idempotente y seguro con
/// múltiples réplicas (las operaciones son tolerantes a carreras).
/// Migrable a Quartz cuando se estandarice el scheduling en la plataforma.
/// </summary>
public sealed class AuthMaintenanceService(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<AuthMaintenanceService> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan PurgeAge = TimeSpan.FromDays(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // El primer tick corre de inmediato (sin esperar el intervalo) — sin esto, un reinicio
        // con RabbitMQ/Wolverine lento podría ganarle la carrera y SaveChangesAsync (que
        // auto-publica domain events por Wolverine) revienta con WolverineHasNotStartedException.
        await lifetime.WaitForApplicationStartedAsync(stoppingToken);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Auth maintenance run failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var now = DateTime.UtcNow;

        // 1. Invitaciones pendientes vencidas → Expired.
        var expiredInvitations = await db
            .Invitations.Where(invitation =>
                invitation.Status == InvitationStatus.Pending && invitation.ExpiresAtUtc <= now
            )
            .ToListAsync(ct);
        foreach (var invitation in expiredInvitations)
            invitation.MarkExpired(now);

        // 2. Purga de artefactos caducados hace más de PurgeAge.
        var cutoff = now.Subtract(PurgeAge);

        var oldRefreshTokens = await db.RefreshTokens.Where(token => token.ExpiresAtUtc < cutoff).ToListAsync(ct);
        db.RefreshTokens.RemoveRange(oldRefreshTokens);

        var oldChallenges = await db.MfaChallenges.Where(challenge => challenge.ExpiresAtUtc < cutoff).ToListAsync(ct);
        db.MfaChallenges.RemoveRange(oldChallenges);

        var oldResetTokens = await db.PasswordResetTokens.Where(token => token.ExpiresAtUtc < cutoff).ToListAsync(ct);
        db.PasswordResetTokens.RemoveRange(oldResetTokens);

        var oldEmailTokens = await db
            .EmailVerificationTokens.Where(token => token.ExpiresAtUtc < cutoff)
            .ToListAsync(ct);
        db.EmailVerificationTokens.RemoveRange(oldEmailTokens);

        var oldPhoneTokens = await db
            .PhoneVerificationTokens.Where(token => token.ExpiresAtUtc < cutoff)
            .ToListAsync(ct);
        db.PhoneVerificationTokens.RemoveRange(oldPhoneTokens);

        var oldDevices = await db.TrustedDevices.Where(device => device.ExpiresAtUtc < cutoff).ToListAsync(ct);
        db.TrustedDevices.RemoveRange(oldDevices);

        var changes = await db.SaveChangesAsync(ct);
        if (changes > 0)
        {
            logger.LogInformation(
                "Auth maintenance: {ExpiredInvitations} invitations expired, {Changes} rows affected.",
                expiredInvitations.Count,
                changes
            );
        }
    }
}
