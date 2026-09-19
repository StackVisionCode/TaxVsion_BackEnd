namespace TaxVision.Campaigns.Domain.Contacts;

/// <summary>Origen de un <see cref="Contact"/> — la libreta de contactos de campañas (Domain_Design.md §6).</summary>
public enum ContactSource
{
    /// <summary>Alta manual por staff.</summary>
    Manual = 0,

    /// <summary>Importado (CSV).</summary>
    Import = 1,

    /// <summary>Derivado del directorio de Customer (además es cliente). El vínculo es <c>CustomerRef</c>.</summary>
    FromCustomer = 2,
}
