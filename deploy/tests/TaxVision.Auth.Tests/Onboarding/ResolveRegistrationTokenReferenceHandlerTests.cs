using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Onboarding;
using TaxVision.Auth.Application.Onboarding.TokenReferences.Queries;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>PayFlow Fase 9, revisado en la auditoría F15 — el endpoint M2M lee la referencia sin
/// borrarla (<c>PeekAsync</c>, respeta el TTL de Redis de 30s) para que un retry de Notification
/// tras un fallo transient encuentre el mismo raw token en vez de fallar por "ya consumido".</summary>
public sealed class ResolveRegistrationTokenReferenceHandlerTests
{
    private static readonly OnboardingOptions RegistrationOptions = new()
    {
        RegistrationUrlBase = "https://app.example.com",
    };

    [Fact]
    public async Task Resolves_the_same_url_on_repeated_calls_within_the_ttl_window()
    {
        var tokenReferences = new FakeTokenReferenceStore { ToPeek = "raw-token-value" };

        var first = await ResolveRegistrationTokenReferenceHandler.Handle(
            new ResolveRegistrationTokenReferenceQuery(tokenReferences.Reference),
            tokenReferences,
            Options.Create(RegistrationOptions),
            CancellationToken.None
        );

        Assert.True(first.IsSuccess);
        Assert.Contains("token=raw-token-value", first.Value.RegistrationUrl);

        // Simula un retry de Notification (evento redelivered por Wolverine) dentro de la misma
        // ventana de TTL — antes de F15 esto fallaba porque ConsumeAsync (GETDEL) ya había borrado
        // la entrada en el primer intento.
        var second = await ResolveRegistrationTokenReferenceHandler.Handle(
            new ResolveRegistrationTokenReferenceQuery(tokenReferences.Reference),
            tokenReferences,
            Options.Create(RegistrationOptions),
            CancellationToken.None
        );

        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.RegistrationUrl, second.Value.RegistrationUrl);
    }

    [Fact]
    public async Task Fails_when_the_reference_does_not_exist_or_expired()
    {
        var tokenReferences = new FakeTokenReferenceStore { ToPeek = null };

        var result = await ResolveRegistrationTokenReferenceHandler.Handle(
            new ResolveRegistrationTokenReferenceQuery(tokenReferences.Reference),
            tokenReferences,
            Options.Create(RegistrationOptions),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.TokenReferenceNotFound", result.Error.Code);
    }
}
