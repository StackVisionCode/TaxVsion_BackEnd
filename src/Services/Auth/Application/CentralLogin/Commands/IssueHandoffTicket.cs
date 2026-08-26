using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Mfa;

namespace TaxVision.Auth.Application.CentralLogin.Commands;

public sealed record IssueHandoffTicketCommand(Guid DiscoverySessionRef, Guid ChosenTenantId, string? MfaCode = null);

/// <summary>Subdominio destino + vale, para que el frontend arme la URL de <c>continue</c>.</summary>
public sealed record HandoffTicketView(string Subdomain, Guid Ticket);

/// <summary>
/// Paso 2 del login central (solo cuando hubo selector o MFA): valida que la oficina elegida está en
/// el set ya autenticado, resuelve el MFA si la oficina lo exige, y emite el vale de handoff. El
/// password NO se re-verifica: ya quedó probado en <c>discover-login</c> y guardado en la sesión.
/// MFA soportado: TOTP y recovery codes (stateless). Email/SMS OTP requiere enviar el código en
/// discover — pendiente.
/// </summary>
public static class IssueHandoffTicketHandler
{
    public static async Task<Result<HandoffTicketView>> Handle(
        IssueHandoffTicketCommand command,
        IDiscoverySessionStore sessions,
        IHandoffTicketStore tickets,
        ITenantRegistry tenants,
        IMfaRepository mfa,
        ITotpService totp,
        ISecretProtector protector,
        ISecureTokenService tokens,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var invalid = new Error("Auth.HandoffInvalid", "The sign-in session is invalid or has expired.");

        // Peek, no consume: si el MFA falla, el usuario reintenta dentro de la ventana.
        var session = await sessions.PeekAsync(command.DiscoverySessionRef, ct);
        if (session is null)
            return Result.Failure<HandoffTicketView>(invalid);

        // La oficina elegida tiene que ser una de las que el password ya validó.
        var office = session.Offices.FirstOrDefault(o => o.TenantId == command.ChosenTenantId);
        if (office is null)
            return Result.Failure<HandoffTicketView>(invalid);

        if (
            office.ChallengeRequired
            && !await VerifyMfaAsync(office.UserId, command.MfaCode, mfa, totp, protector, tokens, ct)
        )
        {
            await audit.AddAsync(
                AuthAuditLog.Record(
                    office.TenantId,
                    office.UserId,
                    AuthAuditAction.MfaFailed,
                    false,
                    request.IpAddress,
                    request.UserAgent,
                    correlation.CorrelationId
                ),
                ct
            );
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Failure<HandoffTicketView>(new Error("Auth.MfaInvalid", "MFA code is invalid or expired."));
        }

        var tenant = await tenants.GetByIdAsync(office.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
            return Result.Failure<HandoffTicketView>(invalid);

        // Si retó y pasó, ya tiene método → no debe enrolar. Si no retaba, arrastra el flag de setup.
        var ticket = await tickets.IssueAsync(
            new HandoffTicketPayload(office.TenantId, office.UserId, office.MustEnroll),
            ct
        );
        await sessions.ConsumeAsync(command.DiscoverySessionRef, ct);

        await audit.AddAsync(
            AuthAuditLog.Record(
                office.TenantId,
                office.UserId,
                AuthAuditAction.LoginSucceeded,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                detailsJson: """{"stage":"handoff_issued"}"""
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new HandoffTicketView(tenant.SubDomain, ticket));
    }

    /// <summary>
    /// Verifica el segundo factor sin desafío: TOTP contra el secreto del método confirmado (stateless),
    /// o un recovery code. Reusa las primitivas del verify de MFA existente; no cubre OTP por Email/SMS
    /// (esos sí necesitan un desafío enviado en discover).
    /// </summary>
    private static async Task<bool> VerifyMfaAsync(
        Guid userId,
        string? code,
        IMfaRepository mfa,
        ITotpService totp,
        ISecretProtector protector,
        ISecureTokenService tokens,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        var trimmed = code.Trim();
        var now = DateTime.UtcNow;

        var totpMethod = (await mfa.GetMethodsAsync(userId, ct)).FirstOrDefault(method =>
            method.IsConfirmed && method.Type == MfaMethodType.Totp
        );
        if (
            totpMethod?.SecretCiphertext is not null
            && protector.TryUnprotect(totpMethod.SecretCiphertext, out var secret, out _)
            && totp.ValidateCode(secret, trimmed, now)
        )
        {
            totpMethod.MarkUsed();
            return true;
        }

        var codeHash = tokens.Hash(trimmed);
        var recovery = (await mfa.GetRecoveryCodesAsync(userId, ct)).FirstOrDefault(recoveryCode =>
            recoveryCode.IsUsable && string.Equals(recoveryCode.CodeHash, codeHash, StringComparison.Ordinal)
        );
        if (recovery is not null)
        {
            recovery.MarkUsed();
            return true;
        }

        return false;
    }
}
