using System.Reflection;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.Calendar.Api.Controllers;
using TaxVision.Calendar.Api.Requests;
using TaxVision.Calendar.Application.PublicBookings;
using TaxVision.Calendar.Domain.PublicBookings;
using Xunit;

namespace TaxVision.Calendar.Tests.Api;

/// <summary>El contrato público de la demo: anónimo, con cupo nativo y sin tenant, usuario ni anfitrión de afuera.</summary>
public sealed class PublicBookingControllerTests
{
    [Fact]
    public void Every_action_is_anonymous_and_rate_limited()
    {
        var controller = typeof(PublicBookingController);
        Assert.NotNull(controller.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.NotNull(controller.GetCustomAttribute<RateLimitExemptAttribute>());

        var actions = controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToList();
        Assert.Equal(9, actions.Count);
        Assert.All(actions, action => Assert.NotNull(action.GetCustomAttribute<EnableRateLimitingAttribute>()));
    }

    [Fact]
    public void No_public_request_carries_a_tenant_a_user_or_a_host()
    {
        var contracts = typeof(PublicBookingCreateRequest)
            .Assembly.GetTypes()
            .Where(t =>
                t.Namespace == typeof(PublicBookingCreateRequest).Namespace && t.Name.StartsWith("PublicBooking")
            )
            .Concat(
                typeof(RequestPublicBookingCommand)
                    .Assembly.GetTypes()
                    .Where(t =>
                        t.Namespace == typeof(RequestPublicBookingCommand).Namespace
                        && (
                            t.Name.EndsWith("Command")
                            || t.Name.EndsWith("Query")
                            || t.Name == nameof(PublicBookingForm)
                        )
                    )
            )
            .ToList();

        Assert.NotEmpty(contracts);
        foreach (var contract in contracts)
        {
            foreach (var property in contract.GetProperties())
            {
                Assert.False(
                    property.Name.Contains("Tenant")
                        || property.Name.EndsWith("UserId")
                        || property.Name.Contains("Host"),
                    $"{contract.Name}.{property.Name}"
                );
            }
        }
    }

    [Fact]
    public void Errors_map_to_stable_statuses()
    {
        Assert.Equal(StatusCodes.Status404NotFound, PublicBookingController.StatusOf(PublicBookingErrors.NotFound));
        Assert.Equal(StatusCodes.Status409Conflict, PublicBookingController.StatusOf(PublicBookingErrors.SlotTaken));
        Assert.Equal(StatusCodes.Status410Gone, PublicBookingController.StatusOf(PublicBookingErrors.HoldExpired));
        Assert.Equal(StatusCodes.Status410Gone, PublicBookingController.StatusOf(PublicBookingErrors.TooManyAttempts));
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            PublicBookingController.StatusOf(PublicBookingErrors.InvalidCode)
        );
        Assert.Equal(
            StatusCodes.Status429TooManyRequests,
            PublicBookingController.StatusOf(PublicBookingErrors.TooManyCodes)
        );
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            PublicBookingController.StatusOf(PublicBookingErrors.FieldRequired)
        );
    }
}
