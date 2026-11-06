// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Authorization;

namespace TaxVision.Notification.Tests.Architecture;

// BB-6: reglas de capas comunes (LayeringRulesBase).
public sealed class NotificationLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(AuthzRolePermissionsProjection).Assembly;

    protected override Assembly ApplicationAssembly => typeof(InAppRoutingOptions).Assembly;
}
