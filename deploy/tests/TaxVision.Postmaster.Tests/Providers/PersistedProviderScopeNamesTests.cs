using TaxVision.Postmaster.Domain.Providers;

namespace TaxVision.Postmaster.Tests.Providers;

/// <summary>
/// <see cref="ProviderScope"/> se guarda como TEXTO en <c>SentMessages.RequiredProviderScope</c>
/// (<c>HasConversion&lt;string&gt;()</c>). Renombrar un valor sin un UPDATE en la misma migración
/// deja filas con un nombre que el enum ya no tiene, y leerlas revienta al materializar.
///
/// <para>No es hipotético: el 2026-10-02 se renombró <c>TenantOAuth</c> a <c>TenantMailbox</c> con
/// 5 filas en dev. Este test hace que el siguiente rename sea una decisión y no un descuido.</para>
/// </summary>
public sealed class PersistedProviderScopeNamesTests
{
    [Fact]
    public void The_persisted_names_are_exactly_these()
    {
        // Si este test falla, el enum cambió. Antes de actualizarlo:
        //   1. UPDATE SentMessages SET RequiredProviderScope = '<nuevo>' WHERE ... = '<viejo>'
        //   2. en la MISMA migración que el rename, no en otra.
        Assert.Equal(
            ["System", "Tenant", "TenantMailbox", "TenantPreferred"],
            Enum.GetNames<ProviderScope>().Order(StringComparer.Ordinal)
        );
    }

    [Fact]
    public void Every_name_fits_the_column()
    {
        // nvarchar(20) en SentMessageConfiguration. Un valor más largo se trunca al guardar y deja de
        // parsear al leer — un fallo que solo aparece después del despliegue.
        foreach (var name in Enum.GetNames<ProviderScope>())
            Assert.True(name.Length <= 20, $"'{name}' no cabe en RequiredProviderScope (nvarchar(20)).");
    }
}
