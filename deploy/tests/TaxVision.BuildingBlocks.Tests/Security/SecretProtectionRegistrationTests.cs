using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Security;

/// <summary>
/// BB-01 — el XML-doc de <c>AddSecretProtection</c> prometía idempotencia ("no reemplaza un
/// protector ya registrado") pero usaba <c>AddSingleton</c>, que sí lo pisaba. Estos tests fijan el
/// comportamiento documentado.
/// </summary>
public sealed class SecretProtectionRegistrationTests
{
    [Fact]
    public void Respeta_el_protector_que_el_servicio_ya_habia_registrado()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISecretProtector, StubSecretProtector>();

        services.AddSecretProtection();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(ISecretProtector));
        Assert.Equal(typeof(StubSecretProtector), descriptor.ImplementationType);
    }

    [Fact]
    public void Registra_el_protector_por_defecto_cuando_no_hay_ninguno()
    {
        var services = new ServiceCollection();

        services.AddSecretProtection();

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(ISecretProtector));
        Assert.Equal(typeof(AesGcmSecretProtector), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    private sealed class StubSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;

        public bool TryUnprotect(string? protectedValue, out string plaintext, out SecretUnprotectFailure failure)
        {
            plaintext = protectedValue ?? string.Empty;
            failure = SecretUnprotectFailure.None;
            return true;
        }
    }
}
