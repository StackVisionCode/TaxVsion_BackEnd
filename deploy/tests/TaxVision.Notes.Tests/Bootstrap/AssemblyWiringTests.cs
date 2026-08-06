namespace TaxVision.Notes.Tests.Bootstrap;

/// <summary>
/// Fase 0 — test trivial de arranque: confirma que las 4 capas referencian correctamente entre sí
/// y que <c>AssemblyMarker</c> (ancla para Wolverine <c>Discovery.IncludeAssembly</c>) existe donde
/// Program.cs lo espera. Fase 1 en adelante agrega la cobertura real de dominio.
/// </summary>
public class AssemblyWiringTests
{
    [Fact]
    public void ApplicationAssemblyMarker_Exists()
    {
        Assert.NotNull(typeof(TaxVision.Notes.Application.AssemblyMarker));
    }
}
