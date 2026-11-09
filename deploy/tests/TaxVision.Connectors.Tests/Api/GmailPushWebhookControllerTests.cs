using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TaxVision.Connectors.Api.Controllers;
using TaxVision.Connectors.Api.Options;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Tests.OAuth;

namespace TaxVision.Connectors.Tests.Api;

/// <summary>
/// El webhook con JWT firmados de prueba. La llave es local, así que la firma real de Google queda sin ejercitar:
/// solo se prueba qué audiencia exige el controller y qué hace con el resultado.
/// </summary>
public sealed class GmailPushWebhookControllerTests
{
    private const string ExpectedAudience = "https://push.example.test/connectors/webhooks/gmail-push";
    private static readonly RSA SigningKey = RSA.Create(2048);

    [Fact]
    public async Task TokenWithWrongAudience_Returns401()
    {
        var bus = new FakeMessageBus();
        var controller = CreateController(bus, Environments.Production, ExpectedAudience, Token("https://other.test"));

        var result = await controller.HandlePush(Envelope(), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task TokenWithExpectedAudience_IsQueued()
    {
        var bus = new FakeMessageBus();
        var controller = CreateController(bus, Environments.Production, ExpectedAudience, Token(ExpectedAudience));

        var result = await controller.HandlePush(Envelope(), CancellationToken.None);

        Assert.IsType<OkResult>(result);
        Assert.Equal("42", Assert.IsType<GmailPushReceived>(Assert.Single(bus.Published)).HistoryId);
    }

    [Theory]
    [InlineData("Production", "")]
    [InlineData("Production", "   ")]
    [InlineData("Staging", "")]
    public async Task MissingAudience_OutsideDevelopment_Returns401(string environment, string audience)
    {
        // Falla cerrado aunque el arranque no lo haya frenado: solo la firma dejaría pasar cualquier token de Google.
        var bus = new FakeMessageBus();
        var controller = CreateController(bus, environment, audience, Token("https://any-google-audience.test"));

        var result = await controller.HandlePush(Envelope(), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task MissingAudience_InDevelopment_ChecksOnlyTheSignature()
    {
        var bus = new FakeMessageBus();
        var controller = CreateController(bus, Environments.Development, "", Token("https://any-audience.test"));

        var result = await controller.HandlePush(Envelope(), CancellationToken.None);

        Assert.IsType<OkResult>(result);
        Assert.Single(bus.Published);
    }

    [Fact]
    public async Task ForgedSignature_InDevelopment_Returns401()
    {
        var bus = new FakeMessageBus();
        using var otherKey = RSA.Create(2048);
        var controller = CreateController(bus, Environments.Development, "", Token("https://any.test", otherKey));

        var result = await controller.HandlePush(Envelope(), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(bus.Published);
    }

    private static string Token(string audience, RSA? key = null) =>
        new JwtSecurityTokenHandler().WriteToken(
            new JwtSecurityToken(
                issuer: "https://accounts.google.com",
                audience: audience,
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(new RsaSecurityKey(key ?? SigningKey), "RS256")
            )
        );

    private static GmailPushWebhookController.PubSubPushEnvelope Envelope() =>
        new()
        {
            Message = new GmailPushWebhookController.PubSubMessage
            {
                Data = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes("""{ "emailAddress": "mailbox@example.test", "historyId": 42 }""")
                ),
            },
        };

    private static GmailPushWebhookController CreateController(
        FakeMessageBus bus,
        string environment,
        string audience,
        string token
    )
    {
        var correlation = new FakeCorrelationContext();
        correlation.Set("webhook-correlation");
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = $"Bearer {token}";

        return new GmailPushWebhookController(
            bus,
            correlation,
            Options.Create(new GmailPushWebhookOptions { ExpectedAudience = audience }),
            new LocalKeyTokenVerifier(),
            new HostingEnvironment { EnvironmentName = environment },
            NullLogger<GmailPushWebhookController>.Instance
        )
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    /// <summary>Hace lo mismo que la librería de Google, con <see cref="SigningKey"/> en lugar de sus certificados.</summary>
    private sealed class LocalKeyTokenVerifier : IGmailPushTokenVerifier
    {
        public Task VerifyAsync(string token, string? audience)
        {
            try
            {
                new JwtSecurityTokenHandler().ValidateToken(
                    token,
                    new TokenValidationParameters
                    {
                        IssuerSigningKey = new RsaSecurityKey(SigningKey),
                        ValidIssuer = "https://accounts.google.com",
                        ValidateAudience = audience is not null,
                        ValidAudience = audience,
                    },
                    out _
                );
                return Task.CompletedTask;
            }
            catch (SecurityTokenException ex)
            {
                throw new InvalidJwtException(ex.Message);
            }
        }
    }
}
