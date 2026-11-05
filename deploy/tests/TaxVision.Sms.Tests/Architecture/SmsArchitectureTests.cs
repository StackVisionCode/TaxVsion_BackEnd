using System.Reflection;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.Sms.Api.Controllers;
using TaxVision.Sms.Api.RateLimiting;

namespace TaxVision.Sms.Tests.Architecture;

/// <summary>Reglas de acción de la API de Sms (SMS-07). SMS-08 agrega las de capas.</summary>
public sealed class SmsArchitectureTests
{
    private static readonly Assembly ApiAssembly = typeof(WebhooksController).Assembly;

    [Fact]
    public void Controller_actions_should_declare_RateLimit_or_RateLimitExempt()
    {
        var violations = new List<string>();
        foreach (var controller in Controllers())
        {
            var classRateLimit = controller.GetCustomAttribute<RateLimitAttribute>(inherit: true);
            var classExempt = controller.GetCustomAttribute<RateLimitExemptAttribute>(inherit: true);

            foreach (var action in HttpActions(controller))
            {
                var rateLimit = action.GetCustomAttribute<RateLimitAttribute>() ?? classRateLimit;
                var exempt = action.GetCustomAttribute<RateLimitExemptAttribute>() ?? classExempt;
                if (rateLimit is null && exempt is null)
                    violations.Add($"{controller.FullName}.{action.Name}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Actions missing [RateLimit] or [RateLimitExempt]: " + string.Join(", ", violations)
        );
    }

    // [RateLimit] particiona por tenant/usuario: en una acción anónima queda abierto. La exención
    // solo vale si la cubre el limiter nativo por IP.
    [Fact]
    public void Anonymous_exempt_actions_should_use_the_native_per_ip_limiter()
    {
        var violations = new List<string>();
        foreach (var controller in Controllers())
        {
            var classAnonymous = controller.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null;
            var classLimiter = controller.GetCustomAttribute<EnableRateLimitingAttribute>(inherit: true);

            foreach (var action in HttpActions(controller))
            {
                var anonymous = classAnonymous || action.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
                var exempt =
                    action.GetCustomAttribute<RateLimitExemptAttribute>()
                    ?? controller.GetCustomAttribute<RateLimitExemptAttribute>(inherit: true);
                if (!anonymous || exempt is null)
                    continue;

                var limiter = action.GetCustomAttribute<EnableRateLimitingAttribute>() ?? classLimiter;
                if (limiter?.PolicyName != SmsWebhookRateLimiting.PolicyName)
                    violations.Add($"{controller.FullName}.{action.Name}");
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Anonymous exempt actions without [EnableRateLimiting(\"{SmsWebhookRateLimiting.PolicyName}\")]: "
                + string.Join(", ", violations)
        );
    }

    private static IEnumerable<Type> Controllers() =>
        ApiAssembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsSubclassOf(typeof(ControllerBase)));

    private static IEnumerable<MethodInfo> HttpActions(Type controller) =>
        controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null)
            .Where(m => m.GetCustomAttributes().OfType<HttpMethodAttribute>().Any());
}
