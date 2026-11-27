using System.Net.Mail;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Credentials.Administration;

/// <summary>
/// Cambio del correo de login pedido por la oficina (D-11 staff, D-08 clientes). Reutiliza el cambio de correo
/// existente: link al correo nuevo (host estricto de la oficina) y aviso al anterior desde Notification. El login
/// no cambia hasta que el nuevo se confirma (<c>ConfirmEmailChangeHandler</c>). Nunca "cambiar y mandar link".
/// </summary>
public static class OfficeEmailChange
{
    public const string Modality = "change_email";

    /// <summary>Correo nuevo normalizado: válido, distinto del actual y libre en la oficina para ese tipo de cuenta.</summary>
    public static async Task<Result<string>> ValidateAsync(
        User target,
        string? newEmail,
        IUserRepository users,
        CancellationToken ct
    )
    {
        var normalized = newEmail?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!MailAddress.TryCreate(normalized, out _))
            return Result.Failure<string>(new Error("User.Email", "Email is invalid."));
        if (string.Equals(normalized, target.Email, StringComparison.OrdinalIgnoreCase))
            return Result.Failure<string>(new Error("User.Email", "New email must be different."));
        if (await users.EmailExistsAsync(target.TenantId, normalized, target.AccountKind, ct))
            return Result.Failure<string>(
                new Error("User.EmailConflict", "Email is already registered in this tenant.")
            );
        return Result.Success(normalized);
    }

    /// <summary>Crea la verificación (sin guardar) y arma el evento que manda link y aviso (sin publicar).</summary>
    public static async Task<(
        EmailVerificationToken Verification,
        EmailChangeRequestedIntegrationEvent Event
    )> IssueAsync(
        User target,
        string newEmail,
        Guid requestedByUserId,
        TimeSpan validity,
        ICredentialTokenRepository credentials,
        ISecureTokenService tokens,
        IAccountTokenProtector tokenProtector,
        ITenantRegistry tenants,
        string correlationId,
        CancellationToken ct
    )
    {
        var rawToken = tokens.GenerateToken();
        var verification = EmailVerificationToken.Create(
            target.TenantId,
            target.Id,
            newEmail,
            tokens.Hash(rawToken),
            validity,
            requestedByUserId
        );
        await credentials.AddEmailVerificationAsync(verification, ct);
        var tenant = await tenants.GetByIdAsync(target.TenantId, ct);
        var sealedToken = tokenProtector.Seal(
            AuthTokenPayloadSchemas.EmailChange,
            AuthTokenPayloadSchemas.EmailChangeBinding(target.TenantId, target.Id),
            rawToken
        );
        return (
            verification,
            new EmailChangeRequestedIntegrationEvent
            {
                TenantId = target.TenantId,
                UserId = target.Id,
                CurrentEmail = target.Email,
                NewEmail = verification.NewEmail,
                RawToken = sealedToken.RawToken,
                ProtectedToken = sealedToken.ProtectedToken,
                ExpiresAtUtc = verification.ExpiresAtUtc,
                ActorType = target.ActorType.ToString(),
                TenantName = tenant?.Name,
                CorrelationId = correlationId,
            }
        );
    }

    /// <summary>Vida del link al correo nuevo: la del link administrativo (tope 24 h).</summary>
    public static TimeSpan LinkLifetime(CredentialAdministrationOptions options) =>
        TimeSpan.FromHours(OfficeCredentialAdministration.LinkLifetimeHours(options));
}

/// <summary>El Owner corrige el correo laboral de un empleado (D-11). Body: el correo nuevo.</summary>
public sealed record RequestStaffEmailChangeCommand(
    Guid TenantId,
    Guid ActorUserId,
    Guid TargetUserId,
    string NewEmail,
    DateTime? ReauthenticatedAtUtc = null
);

/// <summary>Link enviado al correo nuevo; vence en <see cref="ExpiresAtUtc"/>. El login sigue con el anterior.</summary>
public sealed record StaffEmailChangeRequestedResponse(DateTime ExpiresAtUtc);

public static class RequestStaffEmailChangeHandler
{
    public static async Task<Result<StaffEmailChangeRequestedResponse>> Handle(
        RequestStaffEmailChangeCommand command,
        IUserRepository users,
        ICredentialTokenRepository credentials,
        ISecureTokenService tokens,
        IAccountTokenProtector tokenProtector,
        ITenantRegistry tenants,
        IOptions<CredentialAdministrationOptions> options,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var trail = StaffCredentialAudit.For(
            command.TenantId,
            command.ActorUserId,
            command.ReauthenticatedAtUtc,
            audit,
            request,
            correlation
        );
        var target = await users.GetByIdAsync(command.TargetUserId, ct);
        var authorized = await OfficeCredentialAdministration.AuthorizeAsync(
            command.TenantId,
            command.ActorUserId,
            target,
            CredentialAdministrationAction.ChangeEmail,
            UserAccountKind.Staff,
            options.Value.OwnerOnly,
            users,
            ct
        );
        if (authorized.IsFailure)
            return Result.Failure<StaffEmailChangeRequestedResponse>(
                await OfficeCredentialAdministration.DenyAsync(
                    trail,
                    command.TargetUserId,
                    OfficeEmailChange.Modality,
                    authorized.Error,
                    unitOfWork,
                    ct
                )
            );

        var newEmail = await OfficeEmailChange.ValidateAsync(target!, command.NewEmail, users, ct);
        if (newEmail.IsFailure)
            return Result.Failure<StaffEmailChangeRequestedResponse>(newEmail.Error);

        var (verification, requested) = await OfficeEmailChange.IssueAsync(
            target!,
            newEmail.Value,
            command.ActorUserId,
            OfficeEmailChange.LinkLifetime(options.Value),
            credentials,
            tokens,
            tokenProtector,
            tenants,
            correlation.CorrelationId,
            ct
        );
        // Sin correos en la auditoría (03 §13.2): el nuevo queda solo en la verificación.
        await trail.RecordAsync(
            AuthAuditAction.EmailChangeRequestedByOffice,
            true,
            target!.Id,
            OfficeEmailChange.Modality,
            ct,
            outcome: "Requested"
        );
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(requested);
        return Result.Success(new StaffEmailChangeRequestedResponse(verification.ExpiresAtUtc));
    }
}
