namespace TaxVision.Signature.Application.Requests.Preparer;

/// <summary>
/// Códigos de permiso que necesita el cambio de preparador fuera de la capa Api (el catálogo
/// <c>SignaturePermissions</c> vive allí): los inyecta Program.cs para no repetir cadenas.
/// </summary>
/// <param name="SignOwn">Permite estampar la firma personal (si no, la de oficina).</param>
/// <param name="AdminAudience">Audiencia de los avisos sin destinatario concreto (admins de firma).</param>
public sealed record PreparerPermissionCodes(string SignOwn, string AdminAudience);
