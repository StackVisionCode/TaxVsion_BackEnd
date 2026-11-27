namespace TaxVision.Auth.Application.Common;

/// <summary>
/// Administración de credenciales por la oficina (03 §6, §8.2). <see cref="OwnerOnly"/> en true (D-01): solo el Owner
/// actúa, aunque todo TenantAdmin reciba el permiso. Vidas de links con tope de 24 h (D-03). Sin secretos.
/// </summary>
public sealed class CredentialAdministrationOptions
{
    public const string SectionName = "CredentialAdministration";

    public bool OwnerOnly { get; set; } = true;

    public TimeSpan AdministrativeLinkLifetime { get; set; } = TimeSpan.FromHours(24);

    public TimeSpan IncidentLinkLifetime { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Tope de links administrativos o de incidente por objetivo en 24 h (03 §6.6).</summary>
    public int TargetDailyCap { get; set; } = 5;

    /// <summary>Pausa del reset administrativo de un cliente tras un cambio de correo pedido por la oficina (D-08).</summary>
    public TimeSpan EmailChangeCooldown { get; set; } = TimeSpan.FromHours(72);
}
