// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using TaxVision.Auth.Application.AccountSessions.Commands;
using TaxVision.Auth.Domain.Audit;

namespace TaxVision.Auth.Tests.Architecture;

// BB-6: reglas de capas comunes (LayeringRulesBase).
public sealed class AuthLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(AuthAuditLog).Assembly;

    protected override Assembly ApplicationAssembly => typeof(EndAccountSessionHandler).Assembly;
}
