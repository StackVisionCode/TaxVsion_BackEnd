// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using NetArchTest.Rules;
using TaxVision.Auth.Application.AccountSessions.Commands;
using TaxVision.Auth.Domain.Audit;

namespace TaxVision.Auth.Tests.Architecture;

// BB-6: reglas de capas comunes (LayeringRulesBase).
public sealed class AuthLayeringTests : LayeringRulesBase
{
    protected override Assembly DomainAssembly => typeof(AuthAuditLog).Assembly;

    protected override Assembly ApplicationAssembly => typeof(EndAccountSessionHandler).Assembly;

    // El lector del archivo de ubicación es un detalle de Infrastructure.
    [Fact]
    public void Domain_and_application_do_not_know_the_location_file_reader()
    {
        Assert.True(
            Types.InAssembly(DomainAssembly).ShouldNot().HaveDependencyOn("MaxMind.Db").GetResult().IsSuccessful
        );
        Assert.True(
            Types.InAssembly(ApplicationAssembly).ShouldNot().HaveDependencyOn("MaxMind.Db").GetResult().IsSuccessful
        );
    }
}
