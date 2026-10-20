using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Fan-out hacia el cliente del portal: usa <see cref="CustomerPortal"/> para resolver el UserId
/// activo de la cuenta de portal del customer (via <c>CustomerPortalAccountProjection</c>).
/// <para>
/// Sin fila de proyeccion = sin cuenta de portal activa → se omite silencioso. Este fan-out
/// es ADITIVO al email: si el cliente no tiene portal el email sigue saliendo igual (lo decide
/// el consumer antes de llamar aca).
/// </para>
/// <para>
/// Validacion: solo mode + exclusion del actor; no se pide capability porque el cliente del
/// portal no tiene el modelo RBAC de empleado. El modulo del portal se gatea por el entitlement
/// del tenant (si el tenant tiene portal habilitado), que ya cubre T4.2.
/// </para>
/// </summary>
internal static class CustomerPortalRecipients
{
    /// <summary>
    /// Devuelve el UserId de la cuenta de portal activa, o null si no existe. Nunca lanza.
    /// </summary>
    public static async Task<Guid?> ResolveActivePortalUserAsync(
        Guid tenantId,
        Guid customerId,
        IRecipientResolver resolver,
        CancellationToken ct
    )
    {
        var result = await resolver.ResolveAsync(
            new CustomerPortal(tenantId, customerId),
            RecipientValidation.None,
            ct
        );
        if (!result.IsSuccess || result.Value.Count == 0)
            return null;
        return result.Value[0];
    }
}
