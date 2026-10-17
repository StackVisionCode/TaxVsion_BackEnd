using Microsoft.Extensions.DependencyInjection;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Infrastructure.Onboarding.Concurrency;
using TaxVision.Auth.Infrastructure.Persistence;

namespace TaxVision.Auth.Tests.Infrastructure;

// Smoke del wiring de G3 (sp_getapplock). El comportamiento del lock lo garantiza SQL Server; lo
// que un bug típico rompe es el registro DI — alguien lo baja a Singleton (incompatible con el
// DbContext scoped) o lo reemplaza por el InMemory fallback. Un test de integración real
// requeriría SQL Server en CI, que este repo no tiene — por eso cubrimos solo el wiring.
public sealed class SqlOnboardingReconcileSerializerWiringTests
{
    [Fact]
    public void SqlOnboardingReconcileSerializer_implements_the_abstraction()
    {
        Assert.True(typeof(IOnboardingReconcileSerializer).IsAssignableFrom(typeof(SqlOnboardingReconcileSerializer)));
    }

    [Fact]
    public void SqlOnboardingReconcileSerializer_takes_AuthDbContext_so_lifetime_matches_scoped()
    {
        var ctor = typeof(SqlOnboardingReconcileSerializer)
            .GetConstructors()
            .Single(c => c.GetParameters().Length == 1);

        Assert.Equal(typeof(AuthDbContext), ctor.GetParameters()[0].ParameterType);
    }

    [Fact]
    public void SqlOnboardingReconcileSerializer_source_uses_session_scoped_sp_getapplock()
    {
        // Guardarraíl del comportamiento mínimo: multi-pod exige session-scoped (adherido a la
        // conexión, no a la transacción) para que el lock sobreviva al Dispose del scope del handler.
        // Un refactor que cambie a @LockOwner='Transaction' rompe el contrato — pero EF InMemory no
        // puede validarlo en runtime, así que lo cerramos leyendo el source por reflexión.
        var sourcePath = Path.Combine(
            FindRepoRoot(),
            "src",
            "Services",
            "Auth",
            "Infrastructure",
            "Onboarding",
            "Concurrency",
            "SqlOnboardingReconcileSerializer.cs"
        );
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("sp_getapplock", source, StringComparison.Ordinal);
        Assert.Contains("\"Session\"", source, StringComparison.Ordinal);
        Assert.Contains("sp_releaseapplock", source, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Services")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Could not find repo root from the test binary location.");
    }
}
