namespace TaxVision.DataCollection.Engine.Mapping;

// Puerto: el módulo traduce el catálogo de contratos a esta forma; el engine no conoce a sus dueños.
public interface IDataCollectionTargetCatalog
{
    MappingCatalogSnapshot GetSnapshot();
}
