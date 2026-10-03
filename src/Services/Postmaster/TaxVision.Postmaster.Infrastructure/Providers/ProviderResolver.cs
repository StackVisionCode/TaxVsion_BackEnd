using BuildingBlocks.Security;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Domain.Providers;

namespace TaxVision.Postmaster.Infrastructure.Providers;

/// <summary>
/// Implementación de <see cref="IProviderResolver"/>, que a estas alturas resuelve una sola cosa: el
/// <see cref="SystemEmailProvider"/>. <see cref="ProviderScope.System"/> lo pide directo y
/// <see cref="ProviderScope.TenantPreferred"/> llega acá solo cuando la oficina NO tiene cuenta
/// conectada en Connectors, y entonces cae al sistema en su nombre — pero únicamente si el caller lo
/// autoriza. Lo que sostiene la política anti-spoofing es que ese permiso exige <c>Reply-To</c>, así
/// que el correo nunca sale con la identidad del sistema sin decir a quién contestarle.
///
/// <para><b>Ya no hay escalón de SMTP propio del tenant.</b> Existía <c>TenantEmailProvider</c>, una
/// tabla con host/puerto/usuario/contraseña por oficina, y resultó redundante desde el día uno:
/// Connectors ya envía por SMTP (<c>OutboundEmailProviderClientFactory</c> mapea
/// <c>ProviderCode.Imap</c> a <c>SmtpManualClient</c>) y publica <c>connected</c> también para esas
/// cuentas, así que una oficina con SMTP manual sale por el primer escalón —el de Connectors— y
/// nunca llegaba al segundo. Encima la tabla no se podía rellenar: a su único endpoint no lo llamaba
/// ninguna pantalla. Dos copias del mismo secreto, una imposible de crear y jamás usada.</para>
/// </summary>
public sealed class ProviderResolver(ISystemEmailProviderRepository systemProviders, ISecretProtector secretProtector)
    : IProviderResolver
{
    public Task<ResolveResult> ResolveAsync(
        Guid tenantId,
        ProviderScope requiredScope,
        ProviderPriorityHint? priorityHint,
        bool systemFallbackAllowed,
        CancellationToken ct
    )
    {
        if (priorityHint == ProviderPriorityHint.ForceSystem)
            return ResolveSystemAsync(ct);

        return requiredScope switch
        {
            ProviderScope.System => ResolveSystemAsync(ct),
            ProviderScope.TenantPreferred => ResolveTenantPreferredAsync(systemFallbackAllowed, ct),

            // TenantMailbox no llega acá: el consumer lo desvía a IConnectedMailboxResolver antes, porque
            // ese canal no envía por SMTP y no produce un ResolvedEmailProvider.
            //
            // Tenant tampoco debería llegar — nadie lo emite desde que se retiró TenantEmailProvider —
            // pero se responde en vez de lanzar: el valor sobrevive en el enum para poder leer
            // SentMessages viejos, y un mensaje reencolado de antes del despliegue no puede tumbar el
            // consumer. ProviderNotConfigured es además lo que ese scope habría devuelto igualmente,
            // porque la tabla que exigía ya no existe.
            ProviderScope.Tenant => Task.FromResult(
                new ResolveResult(
                    ProviderResolutionStatus.ProviderNotConfigured,
                    null,
                    "The per-tenant SMTP provider was retired; connect the office mailbox in Connectors instead."
                )
            ),
            _ => throw new ArgumentOutOfRangeException(
                nameof(requiredScope),
                requiredScope,
                "Unsupported provider scope."
            ),
        };
    }

    /// <summary>
    /// Último escalón de la cadena: el primero (la cuenta conectada en Connectors) lo resuelve el
    /// consumer antes de llegar acá, así que si estamos en esta función es que la oficina no tiene
    /// ninguna. Sin autorización del caller —sin <c>Reply-To</c>, o siendo una campaña— no se cae al
    /// sistema: se deniega, igual que antes.
    /// </summary>
    private Task<ResolveResult> ResolveTenantPreferredAsync(bool systemFallbackAllowed, CancellationToken ct) =>
        systemFallbackAllowed
            ? ResolveSystemAsync(ct)
            : Task.FromResult(
                new ResolveResult(
                    ProviderResolutionStatus.ProviderNotConfigured,
                    null,
                    "No mailbox is connected for this office and the send is not eligible for the system sender."
                )
            );

    private async Task<ResolveResult> ResolveSystemAsync(CancellationToken ct)
    {
        var lookup = await systemProviders.GetEnabledDefaultAsync(ct);
        return lookup.IsFailure
            ? new ResolveResult(ProviderResolutionStatus.SystemProviderMissing, null, lookup.Error.Message)
            : new ResolveResult(
                ProviderResolutionStatus.Resolved,
                ToResolvedSystemProvider(lookup.Value),
                null,
                ProviderScope.System
            );
    }

    private ResolvedEmailProvider ToResolvedSystemProvider(SystemEmailProvider provider) =>
        new(
            provider.ProviderCode,
            provider.Host ?? string.Empty,
            provider.Port ?? 587,
            provider.UseTls,
            provider.Username,
            DecryptOrNull(provider.PasswordCipher),
            provider.FromAddressDefault,
            provider.FromDisplayNameDefault,
            provider.RateLimitPerMinute,
            provider.BulkRateLimitPerMinute
        );

    private string? DecryptOrNull(Domain.ValueObjects.EncryptedSecret? secret) =>
        secret is not null && secretProtector.TryUnprotect(secret.Cipher, out var plaintext, out _) ? plaintext : null;
}
