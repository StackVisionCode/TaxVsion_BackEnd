namespace TaxVision.Tenant.Domain.ValueObjects;

/// <summary>Códigos postales de USPS de 2 letras: 50 estados, DC, territorios y destinos militares.</summary>
public static class UsStateCodes
{
    private static readonly HashSet<string> Codes = new(StringComparer.Ordinal)
    {
        // Estados
        "AL",
        "AK",
        "AZ",
        "AR",
        "CA",
        "CO",
        "CT",
        "DE",
        "FL",
        "GA",
        "HI",
        "ID",
        "IL",
        "IN",
        "IA",
        "KS",
        "KY",
        "LA",
        "ME",
        "MD",
        "MA",
        "MI",
        "MN",
        "MS",
        "MO",
        "MT",
        "NE",
        "NV",
        "NH",
        "NJ",
        "NM",
        "NY",
        "NC",
        "ND",
        "OH",
        "OK",
        "OR",
        "PA",
        "RI",
        "SC",
        "SD",
        "TN",
        "TX",
        "UT",
        "VT",
        "VA",
        "WA",
        "WV",
        "WI",
        "WY",
        // Distrito federal y territorios
        "DC",
        "AS",
        "GU",
        "MP",
        "PR",
        "VI",
        "UM",
        // Estados asociados
        "FM",
        "MH",
        "PW",
        // Correo militar
        "AA",
        "AE",
        "AP",
    };

    /// <summary>Compara en mayúsculas: el llamador normaliza antes.</summary>
    public static bool IsValid(string code) => Codes.Contains(code);
}
