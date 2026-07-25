using System.Net.Mail;
using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Persistence;

namespace TaxVision.Auth.Api.Bootstrap;

public sealed class PlatformEmergencyAccessOptions
{
    public const string SectionName = "PlatformEmergencyAccess";

    public bool Enabled { get; init; }
    public string? Email { get; init; }
    public string? Name { get; init; }
    public string? LastName { get; init; }
    public string? PasswordHash { get; init; }
}

/// <summary>
/// Recovery path: crea un PlatformAdmin directo (sin pasar por invitación) cuando no hay
/// ningún PlatformAdmin operativo para invitar a otro. Idempotente por email — nunca
/// sobreescribe un usuario ya existente, así que dejar Enabled=true no resetea nada en
/// reinicios sucesivos. El hash viaja solo por config (user-secrets/env), nunca hardcodeado.
/// </summary>
public sealed class PlatformAdminEmergencyAccessService(
    IServiceScopeFactory scopeFactory,
    IOptions<PlatformEmergencyAccessOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<PlatformAdminEmergencyAccessService> logger
) : DeferredStartupHostedService(lifetime, logger)
{
    private readonly PlatformEmergencyAccessOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return;

        var email = _options.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (
            !MailAddress.TryCreate(email, out var parsedEmail)
            || !string.Equals(parsedEmail.Address, email, StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new InvalidOperationException("PlatformEmergencyAccess:Email must contain a valid email.");
        }

        if (string.IsNullOrWhiteSpace(_options.Name) || string.IsNullOrWhiteSpace(_options.LastName))
        {
            throw new InvalidOperationException("PlatformEmergencyAccess:Name and LastName are required.");
        }

        if (!IsWellFormedPbkdf2Hash(_options.PasswordHash))
        {
            throw new InvalidOperationException(
                "PlatformEmergencyAccess:PasswordHash must be a PBKDF2 hash in \"base64(salt).base64(hash)\" format "
                    + "(16-byte salt, 32-byte key), matching Pbkdf2PasswordHasher's output."
            );
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // RBAC Fase 5 — este job opera enteramente dentro del tenant de plataforma (crea un
        // PlatformAdmin ahí, nunca en otro tenant); setearlo acá evita que el filtro fail-closed
        // (sin tenant en un job de background) devuelva 0 filas en la consulta de "ya existe".
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(PlatformTenant.Id);

        var platformTenantExists = await db.Tenants.AnyAsync(
            tenant => tenant.Id == PlatformTenant.Id && tenant.Kind == TenantKind.Platform && tenant.IsActive,
            cancellationToken
        );
        if (!platformTenantExists)
        {
            throw new InvalidOperationException(
                "The reserved platform tenant is missing. Apply Auth migrations before enabling emergency access."
            );
        }

        var alreadyExists = await db.Users.AnyAsync(
            user =>
                user.TenantId == PlatformTenant.Id
                && user.Email == email
                && user.ActorType == UserActorType.PlatformAdmin,
            cancellationToken
        );
        if (alreadyExists)
        {
            logger.LogInformation("Platform emergency access skipped: {Email} already exists as PlatformAdmin.", email);
            return;
        }

        var userResult = User.Register(
            PlatformTenant.Id,
            _options.Name!,
            _options.LastName!,
            email,
            _options.PasswordHash!,
            UserActorType.PlatformAdmin
        );
        if (userResult.IsFailure)
        {
            logger.LogError("Failed to build emergency PlatformAdmin user: {Error}", userResult.Error.Message);
            return;
        }

        db.Users.Add(userResult.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Emergency PlatformAdmin user created for {Email}. Set PlatformEmergencyAccess:Enabled=false now "
                + "and rotate the password via the normal change-password flow after first login.",
            email
        );
    }

    private static bool IsWellFormedPbkdf2Hash(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return false;

        var parts = hash.Split('.');
        if (parts.Length != 2)
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[0]);
            var key = Convert.FromBase64String(parts[1]);
            return salt.Length == 16 && key.Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
