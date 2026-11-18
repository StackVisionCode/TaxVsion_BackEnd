using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Domain.Customers.ValueObjects;

namespace TaxVision.Customer.Application.Abstractions;

/// <summary>
/// Los datos de un cliente que se va a crear o editar, para preguntar si ya existe.
/// Sin identificador fiscal: el alta no lo pide, se pone despues. <c>DisplayName</c>: persona = nombre y apellido
/// (el segundo nombre va en <c>MiddleName</c>); empresa = razón social.
/// </summary>
public sealed record CustomerDuplicateCandidate(
    string? Email,
    string? PhoneE164,
    string? DisplayName,
    DateOnly? DateOfBirth,
    string? MiddleName = null
)
{
    /// <summary>Persona (con <paramref name="person"/>) o empresa (con <paramref name="legalName"/>).</summary>
    public static CustomerDuplicateCandidate For(
        string? email,
        string? phoneE164,
        PersonalName? person,
        string? legalName,
        DateOnly? dateOfBirth
    )
    {
        var (name, middle) = CustomerDuplicateRules.NameOf(person, legalName);
        return new(email, phoneE164, name, dateOfBirth, middle);
    }
}

/// <summary>
/// Detecta duplicados en batch para un chunk del archivo de import.
/// Usa blind index (HMAC-SHA256 por tenant) para SSN/EIN, sin descifrar nada.
/// Tambien matchea por email normalizado, telefono E164 y (nombre normalizado + DOB).
/// </summary>
public interface ICustomerDuplicateDetector
{
    /// <summary>
    /// Para cada fila del chunk que coincida con un customer existente, devuelve un DuplicateMatch.
    /// Las filas sin match no aparecen en el resultado.
    /// UNA sola query por chunk, no por fila.
    /// </summary>
    Task<IReadOnlyList<DuplicateMatch>> FindDuplicatesAsync(
        Guid tenantId,
        IReadOnlyList<ImportCustomerRow> chunk,
        CancellationToken ct
    );

    /// <summary>
    /// El mismo criterio, para un solo cliente que entra por la API.
    ///
    /// <para>
    /// Existe para que crear a mano y crear por archivo decidan lo mismo: dos puertas al mismo dato con
    /// reglas distintas terminan en la puerta laxa usandose para meter lo que la otra rechaza.
    /// </para>
    /// </summary>
    /// <param name="excludeCustomerId">El propio cliente, al editarlo: nadie es duplicado de si mismo.</param>
    Task<DuplicateMatch?> FindDuplicateAsync(
        Guid tenantId,
        CustomerDuplicateCandidate candidate,
        Guid? excludeCustomerId,
        CancellationToken ct
    );
}
