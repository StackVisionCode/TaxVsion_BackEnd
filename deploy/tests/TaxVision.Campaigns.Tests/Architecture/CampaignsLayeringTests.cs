// Alias: el namespace TaxVision.BuildingBlocks taparía los BuildingBlocks.* calificados del proyecto.
extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Architecture;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NetArchTest.Rules;
using TaxVision.Campaigns.Application;
using TaxVision.Campaigns.Domain.Campaigns;

namespace TaxVision.Campaigns.Tests.Architecture;

// BB-6: reglas de capas comunes (LayeringRulesBase) + reglas propias de la Api (CMP-09).
public sealed class CampaignsLayeringTests : LayeringRulesBase
{
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    protected override Assembly DomainAssembly => typeof(Campaign).Assembly;

    protected override Assembly ApplicationAssembly => typeof(AssemblyMarker).Assembly;

    [Fact]
    public void Controllers_should_not_depend_on_Infrastructure()
    {
        var result = Types
            .InAssembly(ApiAssembly)
            .That()
            .Inherit(typeof(ControllerBase))
            .ShouldNot()
            .HaveDependencyOn("TaxVision.Campaigns.Infrastructure")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            "Controllers depending on Infrastructure: " + string.Join(", ", result.FailingTypeNames ?? [])
        );
    }

    [Fact]
    public void Controller_actions_should_declare_AllowActorTypes()
    {
        var violations = new List<string>();
        foreach (var controller in Controllers())
        {
            var classAnonymous = controller.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null;
            var classActorTypes = controller.GetCustomAttribute<AllowActorTypesAttribute>(inherit: true);

            foreach (var action in Actions(controller))
            {
                if (classAnonymous || action.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
                    continue;

                if ((action.GetCustomAttribute<AllowActorTypesAttribute>() ?? classActorTypes) is null)
                    violations.Add($"{controller.FullName}.{action.Name}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Actions missing [AllowActorTypes] (method or controller level): " + string.Join(", ", violations)
        );
    }

    [Fact]
    public void Controller_actions_should_declare_RateLimit_or_RateLimitExempt()
    {
        var violations = new List<string>();
        foreach (var controller in Controllers())
        {
            var classRateLimit = controller.GetCustomAttribute<RateLimitAttribute>(inherit: true);
            var classExempt = controller.GetCustomAttribute<RateLimitExemptAttribute>(inherit: true);

            foreach (var action in Actions(controller))
            {
                if (!action.GetCustomAttributes().OfType<HttpMethodAttribute>().Any())
                    continue;

                var rateLimit = action.GetCustomAttribute<RateLimitAttribute>() ?? classRateLimit;
                var exempt = action.GetCustomAttribute<RateLimitExemptAttribute>() ?? classExempt;
                if (rateLimit is null && exempt is null)
                    violations.Add($"{controller.FullName}.{action.Name}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Actions missing [RateLimit] or [RateLimitExempt] (method or controller level): "
                + string.Join(", ", violations)
        );
    }

    private static IEnumerable<Type> Controllers() =>
        ApiAssembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsSubclassOf(typeof(ControllerBase)));

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null);
}
