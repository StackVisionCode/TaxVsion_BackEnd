namespace BuildingBlocks.Authorization;

/// <summary>Permisos del personal sobre el kilometraje de sus clientes (lectura). El módulo lo exige el prefijo mileage.</summary>
public static class MileagePermissions
{
    /// <summary>Ver viajes, trazados, vehículos, resumen y exportación de un cliente visible.</summary>
    public const string View = "mileage.view";
}
