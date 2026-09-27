namespace TaxVision.Auth.Application.Abstractions;

/// <summary>
/// Un deny por usuario tal como llega desde la API: el permiso, y opcionalmente por qué y hasta
/// cuándo. Sin expiración el deny es indefinido, que es el comportamiento que ya existía.
/// </summary>
public sealed record PermissionDenyInput(Guid PermissionId, string? Reason = null, DateTime? ExpiresAtUtc = null);
