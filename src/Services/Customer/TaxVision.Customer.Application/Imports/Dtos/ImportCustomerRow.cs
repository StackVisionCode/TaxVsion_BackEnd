namespace TaxVision.Customer.Application.Imports.Dtos;

/// <summary>
/// Fila cruda parseada desde CSV o XLSX. Todos los campos son string para no validar prematuramente:
/// la validacion vive en el handler de cada fila (factory + value objects).
///
/// Layout alineado al template descargable. Si cambia, tambien cambia el template y el reader.
/// </summary>
public sealed class ImportCustomerRow
{
    public int RowNumber { get; init; }

    // ----- Identidad base -----
    public string? Kind { get; init; } // "Individual" | "Business"
    public string? FirstName { get; init; }
    public string? MiddleName { get; init; }
    public string? LastName { get; init; }
    public string? Prefix { get; init; }
    public string? Suffix { get; init; }
    public string? LegalName { get; init; } // solo Business
    public string? Dba { get; init; } // solo Business
    public string? BusinessStructure { get; init; } // solo Business: Sole, LLC, Partnership, etc.
    public string? FormationDate { get; init; } // solo Business (yyyy-MM-dd)
    public string? PrincipalBusinessActivityCode { get; init; } // solo Business
    public string? DateOfBirth { get; init; } // yyyy-MM-dd
    public string? OccupationName { get; init; } // se resuelve a OccupationId via catalogo

    // ----- Contacto primario -----
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? Language { get; init; } // "Es" | "En"
    public string? PreferredChannel { get; init; } // "Email" | "Sms" | "Call"

    // ----- Direccion primaria (Home por defecto) -----
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? Region { get; init; }
    public string? PostalCode { get; init; }
    public string? CountryCode { get; init; } // ISO-3166 alpha-2, default US

    // ----- Perfil fiscal del customer -----
    public string? TaxIdentifier { get; init; } // SSN/ITIN/EIN segun Kind
    public string? FilingStatus { get; init; }
    public string? PriorYearAgi { get; init; }
    public string? IsReturningCustomer { get; init; } // "true"|"false"|null

    // ----- Spouse (opcional, solo aplica a Individual) -----
    public string? SpouseFirstName { get; init; }
    public string? SpouseMiddleName { get; init; }
    public string? SpouseLastName { get; init; }
    public string? SpouseDateOfBirth { get; init; }
    public string? SpouseEmail { get; init; }
    public string? SpousePhone { get; init; }
    public string? SpouseTaxIdentifier { get; init; }
}
