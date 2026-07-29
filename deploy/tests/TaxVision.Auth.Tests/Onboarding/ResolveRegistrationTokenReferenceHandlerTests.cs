using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Onboarding;
using TaxVision.Auth.Application.Onboarding.TokenReferences.Queries;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>PayFlow Fase 9 — endpoint M2M one-shot: la referencia se consume (se borra de Redis)
/// en la misma llamada, un segundo intento con la misma referencia siempre falla.</summary>
public sealed class ResolveRegistrationTokenReferenceHandlerTests
{
    private static readonly OnboardingOptions RegistrationOptions = new()
    {
        RegistrationUrlBase = "https://app.example.com",
    };

    [Fact]
    public async Task Consumes_the_reference_exactly_once()
    {
        var tokenReferences = new FakeTokenReferenceStore { ToConsume = "raw-token-value" };

        var first = await ResolveRegistrationTokenReferenceHandler.Handle(
            new ResolveRegistrationTokenReferenceQuery(tokenReferences.Reference),
            tokenReferences,
            Options.Create(RegistrationOptions),
            CancellationToken.None
        );

        Assert.True(first.IsSuccess);
        Assert.Contains("token=raw-token-value", first.Value.RegistrationUrl);

        tokenReferences.ToConsume = null; // segunda llamada: ya se consumió, no queda nada que devolver
        var second = await ResolveRegistrationTokenReferenceHandler.Handle(
            new ResolveRegistrationTokenReferenceQuery(tokenReferences.Reference),
            tokenReferences,
            Options.Create(RegistrationOptions),
            CancellationToken.None
        );

        Assert.True(second.IsFailure);
        Assert.Equal("Onboarding.TokenReferenceNotFound", second.Error.Code);
    }
}
