namespace TaxVision.Calendar.Application.Staff.Abstractions;

/// <summary>Nombre y correo de alguien del equipo; Calendar no guarda un directorio de personal.</summary>
public sealed record StaffContact(string Email, string? DisplayName, bool IsActive);

/// <summary>Contacto de un empleado desde Auth (M2M). Null si no se pudo obtener; nunca lanza.</summary>
public interface IStaffContactClient
{
    Task<StaffContact?> FindAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
}
