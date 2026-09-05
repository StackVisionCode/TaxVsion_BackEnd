using System.Text.Json;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Api.Controllers;
using TaxVision.Auth.Application.Onboarding;
using TaxVision.Auth.Application.Onboarding.EmailVerification.Commands;
using TaxVision.Auth.Application.Onboarding.PaymentOptions;
using TaxVision.Auth.Application.Onboarding.Sessions;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Commands;
using TaxVision.Auth.Infrastructure.Security;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

public sealed class OnboardingSessionControllerTests
{
    [Fact]
    public async Task Verify_sets_http_only_onboarding_session_cookie()
    {
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(30);
        var bus = new FakeMessageBus
        {
            InvokeHandler = _ =>
                Result.Success(new VerifyEmailChallengeResponse("raw-session-token", expiresAtUtc, "Bearer")),
        };
        var http = new DefaultHttpContext();
        var controller = new OnboardingChallengesController(bus)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };

        var result = await controller.Verify(
            Guid.NewGuid(),
            new OnboardingChallengesController.VerifyChallengeRequest("123456"),
            CancellationToken.None
        );

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        var cookie = http.Response.Headers.SetCookie.ToString();
        Assert.Contains(OnboardingSessionHttp.CookieName, cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        var body = JsonSerializer.Serialize(ok.Value);
        Assert.DoesNotContain("SessionToken", body, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionToken", body, StringComparison.Ordinal);
        Assert.DoesNotContain("raw-session-token", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_rejects_missing_onboarding_session_before_invoking_bus()
    {
        var bus = new FakeMessageBus
        {
            InvokeHandler = _ =>
                Result.Success(new CreateOnboardingResponse(Guid.NewGuid(), "owner@castillotax.com", Guid.NewGuid())),
        };
        var controller = new OnboardingCheckoutController(bus, SessionService(new FakeOnboardingSessionStore()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.Create(
            new OnboardingCheckoutController.CreateOnboardingRequest(
                "owner@castillotax.com",
                "Carlos",
                "Castillo",
                null,
                Guid.NewGuid(),
                Guid.NewGuid()
            ),
            CancellationToken.None
        );

        var rejected = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, rejected.StatusCode);
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task Create_accepts_valid_session_and_binds_it_to_created_onboarding()
    {
        var store = new FakeOnboardingSessionStore();
        var sessions = SessionService(store);
        var challengeId = Guid.NewGuid();
        var issued = await sessions.IssueAsync(
            challengeId,
            "owner@castillotax.com",
            DateTime.UtcNow,
            CancellationToken.None
        );
        var onboardingId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var bus = new FakeMessageBus
        {
            InvokeHandler = _ =>
                Result.Success(new CreateOnboardingResponse(onboardingId, "owner@castillotax.com", planId)),
        };
        var controller = new OnboardingCheckoutController(bus, sessions)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = HttpWithOnboardingCookie(issued.Value.SessionToken),
            },
        };

        var result = await controller.Create(
            new OnboardingCheckoutController.CreateOnboardingRequest(
                "owner@castillotax.com",
                "Carlos",
                "Castillo",
                null,
                planId,
                challengeId,
                "Monthly"
            ),
            CancellationToken.None
        );

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Single(bus.Invoked);
        var session = (
            await sessions.ValidateAsync(issued.Value.SessionToken, DateTime.UtcNow, CancellationToken.None)
        ).Value;
        Assert.Equal(onboardingId, session.OnboardingId);
    }

    [Fact]
    public async Task Payment_options_rejects_missing_onboarding_session_before_invoking_bus()
    {
        var bus = new FakeMessageBus { InvokeHandler = _ => Result.Success(new OnboardingPaymentOptionsResponse([])) };
        var controller = new OnboardingCheckoutController(bus, SessionService(new FakeOnboardingSessionStore()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.PaymentOptions(Guid.NewGuid(), "Monthly", "USD", CancellationToken.None);

        var rejected = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, rejected.StatusCode);
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task Payment_options_accepts_valid_post_otp_session()
    {
        var store = new FakeOnboardingSessionStore();
        var sessions = SessionService(store);
        var issued = await sessions.IssueAsync(
            Guid.NewGuid(),
            "owner@castillotax.com",
            DateTime.UtcNow,
            CancellationToken.None
        );
        var planId = Guid.NewGuid();
        var bus = new FakeMessageBus
        {
            InvokeHandler = message =>
            {
                var query = Assert.IsType<GetOnboardingPaymentOptionsQuery>(message);
                Assert.Equal(planId, query.PlanId);
                Assert.Equal("Yearly", query.BillingCycle);
                return Result.Success(
                    new OnboardingPaymentOptionsResponse([
                        new OnboardingPaymentOption("Stripe", "Card", "Card", true, 10, null),
                    ])
                );
            },
        };
        var controller = new OnboardingCheckoutController(bus, sessions)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = HttpWithOnboardingCookie(issued.Value.SessionToken),
            },
        };

        var result = await controller.PaymentOptions(planId, "Yearly", "USD", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Single(bus.Invoked);
    }

    [Fact]
    public async Task Checkout_passes_selected_provider_and_method_after_session_validation()
    {
        var store = new FakeOnboardingSessionStore();
        var sessions = SessionService(store);
        var issued = await sessions.IssueAsync(
            Guid.NewGuid(),
            "owner@castillotax.com",
            DateTime.UtcNow,
            CancellationToken.None
        );
        var onboardingId = Guid.NewGuid();
        await sessions.BindOnboardingAsync(
            issued.Value.SessionToken,
            (await sessions.ValidateAsync(issued.Value.SessionToken, DateTime.UtcNow, CancellationToken.None)).Value,
            onboardingId,
            DateTime.UtcNow,
            CancellationToken.None
        );

        var bus = new FakeMessageBus
        {
            InvokeHandler = message =>
            {
                var command = Assert.IsType<StartOnboardingCheckoutCommand>(message);
                Assert.Equal("PayPal", command.Provider);
                Assert.Equal("Wallet", command.Method);
                return Result.Success(
                    new StartOnboardingCheckoutResponse(
                        Guid.NewGuid(),
                        "https://paypal.example.com/checkout",
                        DateTime.UtcNow.AddHours(1)
                    )
                );
            },
        };
        var controller = new OnboardingCheckoutController(bus, sessions)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = HttpWithOnboardingCookie(issued.Value.SessionToken),
            },
        };

        var result = await controller.Checkout(
            new OnboardingCheckoutController.StartCheckoutRequest(
                onboardingId,
                "owner@castillotax.com",
                "https://app.example.com/success",
                "https://app.example.com/cancel",
                Provider: "PayPal",
                Method: "Wallet"
            ),
            CancellationToken.None
        );

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Single(bus.Invoked);
    }

    [Fact]
    public async Task Reconcile_payment_rejects_missing_onboarding_session_before_invoking_bus()
    {
        var bus = new FakeMessageBus
        {
            InvokeHandler = _ =>
                Result.Success(
                    new ReconcileOnboardingPaymentResponse(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        "PaymentProcessing",
                        RegistrationUrl: null,
                        FailureCode: null,
                        FailureMessage: null
                    )
                ),
        };
        var controller = new OnboardingCheckoutController(bus, SessionService(new FakeOnboardingSessionStore()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.ReconcilePayment(null, CancellationToken.None);

        var rejected = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, rejected.StatusCode);
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public async Task Reconcile_payment_uses_the_bound_onboarding_id_from_session()
    {
        var store = new FakeOnboardingSessionStore();
        var sessions = SessionService(store);
        var issued = await sessions.IssueAsync(
            Guid.NewGuid(),
            "owner@castillotax.com",
            DateTime.UtcNow,
            CancellationToken.None
        );
        var onboardingId = Guid.NewGuid();
        await sessions.BindOnboardingAsync(
            issued.Value.SessionToken,
            (await sessions.ValidateAsync(issued.Value.SessionToken, DateTime.UtcNow, CancellationToken.None)).Value,
            onboardingId,
            DateTime.UtcNow,
            CancellationToken.None
        );
        var bus = new FakeMessageBus
        {
            InvokeHandler = message =>
            {
                var command = Assert.IsType<ReconcileOnboardingPaymentCommand>(message);
                Assert.Equal(onboardingId, command.OnboardingId);
                return Result.Success(
                    new ReconcileOnboardingPaymentResponse(
                        onboardingId,
                        Guid.NewGuid(),
                        "RegistrationPending",
                        "https://app.example.com/register?token=abc",
                        FailureCode: null,
                        FailureMessage: null
                    )
                );
            },
        };
        var controller = new OnboardingCheckoutController(bus, sessions)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = HttpWithOnboardingCookie(issued.Value.SessionToken),
            },
        };

        var result = await controller.ReconcilePayment(
            new OnboardingCheckoutController.ReconcilePaymentRequest(),
            CancellationToken.None
        );

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Single(bus.Invoked);
    }

    private static OnboardingSessionService SessionService(FakeOnboardingSessionStore store) =>
        new(new SecureTokenService(), store, Options.Create(new OnboardingOptions()));

    private static DefaultHttpContext HttpWithOnboardingCookie(string token)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = $"{OnboardingSessionHttp.CookieName}={token}";
        return http;
    }
}
