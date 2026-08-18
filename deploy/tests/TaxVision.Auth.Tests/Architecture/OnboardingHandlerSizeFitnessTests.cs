namespace TaxVision.Auth.Tests.Architecture;

/// <summary>
/// PayFlow (auditoría F20) — fitness function heurística basada en texto (no hay referencia a
/// Microsoft.CodeAnalysis.CSharp/Roslyn en el repo, y agregarla solo para esto sería
/// sobre-ingeniería) que evita que <c>CompleteOnboardingRegistrationHandler.Handle</c> vuelva a
/// crecer en un god-method que mezcle validación, carga de contexto, transición del aggregate y
/// side-effects. Cuenta líneas físicas del cuerpo del método (entre su <c>{</c> y <c>}</c>) — un
/// proxy tosco pero suficiente para esta alarma; no reemplaza a NetArchTest para reglas de
/// dependencias.
/// </summary>
public sealed class OnboardingHandlerSizeFitnessTests
{
    private const int MaxHandleBodyLines = 40;

    [Fact]
    public void CompleteOnboardingRegistrationHandler_Handle_StaysSmall()
    {
        var path = HandlerSourceLocator.FindRepoFile(
            "src/Services/Auth/Application/Onboarding/Registration/Commands/CompleteOnboardingRegistration.cs"
        );
        var lineCount = HandlerSourceLocator.CountHandleMethodBodyLines(path);

        Assert.True(
            lineCount <= MaxHandleBodyLines,
            $"CompleteOnboardingRegistrationHandler.Handle has {lineCount} body lines (max {MaxHandleBodyLines}). "
                + "Extract another private helper instead of growing this method."
        );
    }
}
