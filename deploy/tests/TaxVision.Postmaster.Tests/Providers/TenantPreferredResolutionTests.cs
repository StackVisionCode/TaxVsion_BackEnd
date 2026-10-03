using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Domain.Providers;
using TaxVision.Postmaster.Infrastructure.Providers;

namespace TaxVision.Postmaster.Tests.Providers;

/// <summary>
/// El último escalón de <see cref="ProviderScope.TenantPreferred"/>: salir por el remitente del
/// sistema EN NOMBRE de la oficina. El primero —la cuenta conectada en Connectors— lo resuelve el
/// consumer antes de llegar acá, porque ese canal no envía por SMTP
/// (<see cref="Consumers.TenantPreferredLaneTests"/>).
///
/// <para>Lo que se fija es que este escalón sea una concesión y no la regla: solo corre cuando el
/// caller lo autoriza, y esa autorización exige <c>Reply-To</c> y que no sea una campaña. Si se
/// relajara, el correo de una oficina saldría con la identidad de la plataforma y sin forma de
/// contestarle — el spoofing silencioso que toda esta cadena evita.</para>
///
/// <para>Hasta 2026-10-02 había un escalón intermedio, <c>TenantEmailProvider</c> (SMTP propio por
/// oficina). Se retiró: Connectors ya envía por SMTP, así que una oficina con SMTP manual salía por
/// el PRIMER escalón y nunca llegaba al segundo — que además era imposible de rellenar porque a su
/// endpoint no lo llamaba ninguna pantalla.</para>
/// </summary>
public sealed class TenantPreferredResolutionTests
{
    private static SystemEmailProvider CreateSystemProvider() =>
        SystemEmailProvider
            .Create(
                providerCode: "smtp-default",
                displayName: "Default SMTP",
                providerType: EmailProviderType.Smtp,
                fromAddressDefault: "no-reply@taxvision.local",
                fromDisplayNameDefault: "TaxVision",
                host: "localhost",
                port: 1025,
                useTls: false,
                username: null,
                passwordCipher: "system-secret",
                rateLimitPerMinute: 60,
                createdAtUtc: DateTime.UtcNow
            )
            .Value;

    [Fact]
    public async Task With_permission_it_falls_back_to_the_system_sender()
    {
        // El caso de una oficina sin buzón conectado mandando una factura: el correo sale igual, por
        // el remitente del sistema, con Reply-To a quien lo manda.
        var result = await Resolve(systemFallbackAllowed: true, withSystemProvider: true);

        Assert.Equal(ProviderResolutionStatus.Resolved, result.Status);
        Assert.Equal("smtp-default", result.Provider!.ProviderCode);
        // Lo que de verdad importa del EffectiveScope: es lo que hace que el consumer ponga el
        // Reply-To y anuncie a la oficina en el From.
        Assert.Equal(ProviderScope.System, result.EffectiveScope);
    }

    [Fact]
    public async Task Without_permission_it_does_NOT_fall_back()
    {
        // El permiso se lo niega el consumer a las campañas y a los envíos sin Reply-To. Acá se fija
        // que negarlo sirva de algo: sin esto, el flag sería decorativo.
        var result = await Resolve(systemFallbackAllowed: false, withSystemProvider: true);

        Assert.Equal(ProviderResolutionStatus.ProviderNotConfigured, result.Status);
        Assert.Null(result.Provider);
    }

    [Fact]
    public async Task Without_permission_it_never_even_reads_the_system_provider()
    {
        // Mismo resultado que el test de arriba pero por el camino contrario: aunque NO exista
        // proveedor de sistema, la respuesta sigue siendo ProviderNotConfigured y no
        // SystemProviderMissing. Prueba que sin permiso ni se mira — si se mirara, el motivo del
        // fallo cambiaría según el estado de una tabla que no tiene nada que ver con la decisión.
        var result = await Resolve(systemFallbackAllowed: false, withSystemProvider: false);

        Assert.Equal(ProviderResolutionStatus.ProviderNotConfigured, result.Status);
    }

    [Fact]
    public async Task With_permission_but_no_system_provider_it_reports_the_real_reason()
    {
        var result = await Resolve(systemFallbackAllowed: true, withSystemProvider: false);

        Assert.Equal(ProviderResolutionStatus.SystemProviderMissing, result.Status);
    }

    private static async Task<ResolveResult> Resolve(bool systemFallbackAllowed, bool withSystemProvider)
    {
        var systemRepo = new FakeSystemEmailProviderRepository();
        if (withSystemProvider)
            await systemRepo.AddAsync(CreateSystemProvider());

        return await new ProviderResolver(systemRepo, new FakeSecretProtector()).ResolveAsync(
            Guid.NewGuid(),
            ProviderScope.TenantPreferred,
            null,
            systemFallbackAllowed,
            CancellationToken.None
        );
    }
}
