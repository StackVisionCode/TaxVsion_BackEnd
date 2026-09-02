namespace TaxVision.Auth.Application.Common;

/// <summary>
/// Configuracion de la exigencia de MFA. <see cref="Enforced"/> es true por defecto (produccion):
/// admins y usuarios con MFA activo pasan por segundo factor / enrolamiento obligatorio. Se apaga
/// SOLO en desarrollo local (appsettings.Development.json) para poder entrar sin setup de MFA.
/// NUNCA debe ir en false en produccion.
/// </summary>
public sealed class MfaOptions
{
    public const string SectionName = "Mfa";

    public bool Enforced { get; set; } = true;
}
