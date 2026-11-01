using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Mfa;

/// <summary>
/// Quita el segundo factor de un usuario: métodos, códigos de recuperación y dispositivos de confianza.
/// Lo comparten "apagarlo uno mismo" y "la oficina se lo resetea a un cliente". No guarda: lo hace el caller.
/// </summary>
internal static class MfaFactors
{
    public static async Task ClearAsync(User user, IMfaRepository mfa, CancellationToken ct)
    {
        user.DisableMfa();

        foreach (var method in await mfa.GetMethodsAsync(user.Id, ct))
            mfa.RemoveMethod(method);

        mfa.RemoveRecoveryCodes(await mfa.GetRecoveryCodesAsync(user.Id, ct));

        foreach (var device in await mfa.GetTrustedDevicesAsync(user.Id, ct))
            device.Revoke();
    }
}
