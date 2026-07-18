namespace TaxVision.Correspondence.Domain.Projections;

/// <summary>
/// Origen del dato de email dentro de <see cref="CustomerEmailAddress"/>. En esta fase
/// (Correspondence Fase 2) solo <see cref="CustomerPrimary"/> se produce en la práctica,
/// porque los únicos eventos de Customer que llevan email (Created/Updated) publican el
/// <c>PrimaryEmail</c>. Los otros dos valores quedan reservados para cuando exista una
/// fuente de datos real de emails secundarios/de contacto (ver §36 del plan).
/// </summary>
public enum CustomerEmailSource
{
    CustomerPrimary = 0,
    CustomerSecondary = 1,
    CustomerContact = 2,
}
