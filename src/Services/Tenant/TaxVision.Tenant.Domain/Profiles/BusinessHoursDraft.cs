namespace TaxVision.Tenant.Domain.Profiles;

/// <summary>Un día de atención tal como llega (sin validar): día ISO en inglés y horas <c>HH:mm</c>.</summary>
public sealed record BusinessHoursDraft(string? Day, string? Opens, string? Closes);
