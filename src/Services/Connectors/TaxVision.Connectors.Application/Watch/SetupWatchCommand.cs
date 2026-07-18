namespace TaxVision.Connectors.Application.Watch;

/// <summary>
/// Establece (cuenta recién conectada) o restablece (reauth manual tras Status=Error) el watch de
/// una cuenta. Idempotente en su efecto: sobre una cuenta ya Active falla limpio vía la propia
/// invariante de TenantEmailAccount.Activate (Connected → Active únicamente).
/// </summary>
public sealed record SetupWatchCommand(Guid TenantId, Guid AccountId);
