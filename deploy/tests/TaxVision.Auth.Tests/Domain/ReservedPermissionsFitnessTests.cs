using BuildingBlocks.Authorization;
using TaxVision.Auth.Domain.Roles;
using Xunit;

namespace TaxVision.Auth.Tests.Domain;

/// <summary>CAT-F0.6. Un permiso reservado se vende en la pantalla de roles sin gatear nada: solo se
/// toleran los de Client Forms cuyo endpoint todavía no existe. Cuando el endpoint exista, se libera.</summary>
public sealed class ReservedPermissionsFitnessTests
{
    [Fact]
    public void Only_client_form_permissions_without_an_endpoint_are_reserved()
    {
        string[] approved =
        [
            TasksPermissions.FormRequestsExport,
            TasksPermissions.FormRequestsApplyProfile,
            TasksPermissions.FormsLibraryManage,
        ];
        var reserved = PermissionCatalog
            .All.Where(definition => definition.IsReserved)
            .Select(definition => definition.Code)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(approved.Order(StringComparer.Ordinal), reserved);
    }
}
