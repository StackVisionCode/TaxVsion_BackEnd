using BuildingBlocks.Authorization;
using TaxVision.Auth.Domain.Roles;
using Xunit;

namespace TaxVision.Auth.Tests.Domain;

/// <summary>CAT-F0.6. Solo permite los contratos reservados aprobados para Client Forms.</summary>
public sealed class ReservedPermissionsFitnessTests
{
    [Fact]
    public void Only_approved_client_form_contracts_are_reserved()
    {
        string[] approved =
        [
            TasksPermissions.FormRequestsRead,
            TasksPermissions.FormRequestsReview,
            TasksPermissions.FormRequestsExport,
            TasksPermissions.FormRequestsApplyProfile,
            TasksPermissions.FormSubmissionsReveal,
            TasksPermissions.FormsLibraryManage,
            TasksPermissions.FormTemplatesConfigureActions,
            TasksPermissions.FormRequestsSendExternal,
            TasksPermissions.FormIntakeLinksManage,
            TasksPermissions.FormConsentsManage,
        ];
        var reserved = PermissionCatalog.All
            .Where(definition => definition.IsReserved)
            .Select(definition => definition.Code)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(approved.Order(StringComparer.Ordinal), reserved);
    }
}
