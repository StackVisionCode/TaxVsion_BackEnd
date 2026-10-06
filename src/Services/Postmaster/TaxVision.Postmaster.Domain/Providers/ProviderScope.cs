namespace TaxVision.Postmaster.Domain.Providers;

/// <summary>
/// Por cuál transporte sale un envío.
///
/// <para><b>Se guarda como texto</b> en <c>SentMessages.RequiredProviderScope</c>: renombrar un valor
/// rompe la lectura del historial si no va con un UPDATE en la misma migración. Lo fija
/// <c>PersistedProviderScopeNamesTests</c>.</para>
/// </summary>
public enum ProviderScope
{
    /// <summary>Credenciales de la plataforma.</summary>
    System,

    /// <summary>
    /// Histórico. Exigía un SMTP propio por oficina (<c>TenantEmailProvider</c>), retirado el
    /// 2026-10-02 por redundante con el buzón de Connectors. Solo sobrevive para leer envíos viejos;
    /// nadie lo emite y el resolver lo rechaza.
    /// </summary>
    Tenant,

    /// <summary>Buzón que la oficina conectó en Connectors: Gmail, Graph o SMTP manual.</summary>
    TenantMailbox,

    /// <summary>
    /// Correo de negocio de la oficina, sin decir por cuál transporte. Resuelve en cadena: buzón
    /// conectado → sistema en nombre de la oficina.
    ///
    /// <para>Existe porque quien pide el envío sabe de QUIÉN es el correo, pero no qué tiene
    /// conectado esa oficina. Eso solo lo sabe Postmaster.</para>
    ///
    /// <para>El escalón de sistema exige <c>ReplyTo</c> y <c>Stream.Transactional</c>. Sin
    /// <c>ReplyTo</c> sería un correo al que nadie puede contestar; en <c>Bulk</c> sería el volumen de
    /// una campaña quemando la reputación de envío de todas las oficinas.</para>
    /// </summary>
    TenantPreferred,
}
