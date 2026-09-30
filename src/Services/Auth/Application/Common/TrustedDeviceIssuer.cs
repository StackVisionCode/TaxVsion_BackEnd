using BuildingBlocks.Common;
using BuildingBlocks.Security;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Mfa;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Common;

/// <summary>
/// Punto único "el usuario pidió no volver a que le pidan el código → dispositivo de confianza".
///
/// Vive acá porque lo minta TRES caminos —la verificación de MFA del login clásico, el canje del vale
/// del login central y la confirmación del takeover— y el token tiene que nacer igual en los tres: con
/// los días que decida la política del tenant y con su registro en la auditoría.
/// </summary>
public static class TrustedDeviceIssuer
{
    /// <summary>
    /// Devuelve el token en claro para que el navegador lo guarde, o <c>null</c> si no se pidió. Lo que
    /// se persiste es solo el hash: un volcado de la tabla no sirve para saltarse el segundo factor.
    /// </summary>
    public static async Task<string?> IssueIfRequestedAsync(
        bool remember,
        User user,
        IMfaRepository mfa,
        ISecureTokenService tokens,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (!remember)
            return null;

        var policy = await mfa.GetPolicyAsync(user.TenantId, ct);
        var trustedDays = policy?.TrustedDeviceDays ?? TrustedDeviceDefaults.Days;

        var deviceToken = tokens.GenerateToken();
        var device = TrustedDevice.Create(
            user.TenantId,
            user.Id,
            tokens.Hash(deviceToken),
            request.UserAgent,
            TimeSpan.FromDays(trustedDays)
        );
        await mfa.AddTrustedDeviceAsync(device, ct);

        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId,
                user.Id,
                AuthAuditAction.TrustedDeviceAdded,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "TrustedDevice",
                targetId: device.Id
            ),
            ct
        );

        return deviceToken;
    }
}
