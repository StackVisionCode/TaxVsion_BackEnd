namespace TaxVision.Tenant.Application.Profiles;

/// <summary>Día ISO en inglés ("Monday") y horas <c>HH:mm</c>.</summary>
public sealed record BusinessHoursResponse(string Day, string Opens, string Closes);
