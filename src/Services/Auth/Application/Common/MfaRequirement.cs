using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Common;

/// <summary>
/// ¿Este usuario necesita segundo factor? Preferencia del usuario (<c>MfaEnabled</c>) O la política
/// del tenant para su actor type; si no hay política, los admins lo requieren por defecto. Único
/// punto compartido por el login directo y el descubrimiento del login central.
/// </summary>
public static class MfaRequirement
{
    public static async Task<bool> EvaluateAsync(
        User user,
        IMfaRepository mfa,
        CancellationToken ct,
        bool enforced = true
    )
    {
        // Interruptor de desarrollo local: cuando MFA no se exige, nadie pasa por segundo
        // factor ni enrolamiento. NUNCA false en produccion (ver MfaOptions).
        if (!enforced)
            return false;

        if (user.MfaEnabled)
            return true;

        var policy = await mfa.GetPolicyAsync(user.TenantId, ct);
        return policy?.RequiresFor(user.ActorType)
            ?? user.ActorType is UserActorType.TenantAdmin or UserActorType.PlatformAdmin;
    }

    /// <summary>
    /// Disposición de MFA para el login central. Separa "hay que pedir un código" (lo exige la
    /// política Y ya hay un método confirmado) de "hay que enrolar" (lo exige la política pero no
    /// hay método): el login directo trata ese segundo caso dejando entrar con el flag de setup, no
    /// bloqueando — si no, un admin sin MFA no podría entrar jamás para poder enrolarlo.
    /// </summary>
    public static async Task<MfaDisposition> DisposeAsync(
        User user,
        IMfaRepository mfa,
        CancellationToken ct,
        bool enforced = true
    )
    {
        if (!await EvaluateAsync(user, mfa, ct, enforced))
            return new MfaDisposition(ChallengeRequired: false, MustEnroll: false);

        var hasConfirmedMethod = (await mfa.GetMethodsAsync(user.Id, ct)).Any(method => method.IsConfirmed);
        return new MfaDisposition(ChallengeRequired: hasConfirmedMethod, MustEnroll: !hasConfirmedMethod);
    }
}

/// <summary>
/// <see cref="ChallengeRequired"/>: pedir un código en el handoff. <see cref="MustEnroll"/>: dejar
/// entrar pero marcar que debe enrolar MFA. Nunca son ambos true a la vez.
/// </summary>
public sealed record MfaDisposition(bool ChallengeRequired, bool MustEnroll);
