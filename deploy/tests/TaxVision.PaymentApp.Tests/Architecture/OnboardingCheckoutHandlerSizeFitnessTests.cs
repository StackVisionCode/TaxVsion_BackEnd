namespace TaxVision.PaymentApp.Tests.Architecture;

/// <summary>
/// PayFlow (auditoría F20) — fitness function heurística basada en texto (no hay referencia a
/// Microsoft.CodeAnalysis.CSharp/Roslyn en el repo, y agregarla solo para esto sería
/// sobre-ingeniería) que evita que <c>CreateOnboardingCheckoutHandler.Handle</c> vuelva a crecer en
/// un god-method que mezcle replay idempotente, pricing, creación de sesión en Stripe y
/// persistencia/auditoría. Cuenta líneas físicas del cuerpo del método (entre su <c>{</c> y
/// <c>}</c>) — un proxy tosco pero suficiente para esta alarma; no reemplaza a NetArchTest para
/// reglas de dependencias.
/// </summary>
public sealed class OnboardingCheckoutHandlerSizeFitnessTests
{
    private const int MaxHandleBodyLines = 40;

    [Fact]
    public void CreateOnboardingCheckoutHandler_Handle_StaysSmall()
    {
        var path = HandlerSourceLocator.FindRepoFile(
            "src/Services/PaymentApp/TaxVision.PaymentApp.Application/OnboardingCheckouts/Commands/CreateOnboardingCheckoutHandler.cs"
        );
        var lineCount = HandlerSourceLocator.CountHandleMethodBodyLines(path);

        Assert.True(
            lineCount <= MaxHandleBodyLines,
            $"CreateOnboardingCheckoutHandler.Handle has {lineCount} body lines (max {MaxHandleBodyLines}). "
                + "Extract another private helper instead of growing this method."
        );
    }
}
