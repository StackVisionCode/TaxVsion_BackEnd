using TaxVision.Auth.Domain.Roles;
using Xunit;

namespace TaxVision.Auth.Tests.Domain;

/// <summary>CAT-F0.6 — guard que impide reintroducir el patrón "declarar un permiso IsReserved para
/// 'tenerlo listo'". Un permiso reservado se cobraba como accesos en pantallas de roles sin gatear
/// nada; se vendía por nada hasta que lo barrimos en Fase 0 del rediseño de catálogo.
/// Si vuelve a hacer falta un permiso que todavía no tiene endpoint real, se agrega cuando el endpoint
/// exista — no antes.</summary>
public sealed class ReservedPermissionsFitnessTests
{
    [Fact]
    public void No_permission_in_catalog_is_marked_IsReserved()
    {
        var reserved = PermissionCatalog.All.Where(definition => definition.IsReserved).ToArray();

        Assert.Empty(reserved);
    }
}
