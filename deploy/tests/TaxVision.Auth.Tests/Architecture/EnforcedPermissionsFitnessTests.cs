using TaxVision.Auth.Domain.Roles;

namespace TaxVision.Auth.Tests.Architecture;

/// <summary>
/// A4.4 — fitness function: todo código que algún endpoint exige con <c>[HasPermission]</c> o
/// <c>[HasPermissionForActor]</c> tiene que existir en <see cref="PermissionCatalog"/>.
///
/// <para>
/// El motivo es un bug real y repetido: <c>signature.constraints.manage</c> y los dos de
/// <c>connectors.accounts.*</c> se exigían desde que se construyeron sus controllers pero nunca se
/// habían sembrado. Sin fila en el catálogo ningún rol puede tenerlos, así que el endpoint dependía
/// por completo del bypass por nombre de rol — y cuando ese bypass se retiró (A0), el endpoint quedó
/// inalcanzable para todos. Un chequeo de catálogo en CI lo habría atrapado el día que se escribió.
/// </para>
///
/// <para>
/// Es un escaneo de texto sobre <c>src/</c>, no un análisis de C#: resuelve las constantes
/// <c>public const string</c> de todo el repo para poder seguir <c>[HasPermission(XPermissions.Y)]</c>
/// hasta su literal. Los 26 microservicios declaran sus permisos en su propia clase, así que este
/// proyecto de test no puede referenciarlos por assembly sin crear dependencias cruzadas entre
/// servicios.
/// </para>
///
/// <para>
/// La dirección inversa (todo código del catálogo se exige en algún lado o es
/// <see cref="Permission.IsReserved"/>) vive en <see cref="DeclaredPermissionsAreEnforcedTests"/>.
/// </para>
/// </summary>
public sealed class EnforcedPermissionsFitnessTests
{
    [Fact]
    public void Every_permission_code_required_by_an_endpoint_exists_in_the_catalog()
    {
        var sourceRoot = Path.Combine(PermissionSourceIndex.RepoRoot(), "src");
        var constants = PermissionSourceIndex.CollectStringConstants(sourceRoot);
        var enforced = PermissionSourceIndex.CollectEnforcedCodes(sourceRoot, constants);

        Assert.NotEmpty(enforced);

        var catalog = PermissionCatalog
            .All.Select(definition => definition.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = enforced.Where(code => !catalog.Contains(code)).OrderBy(code => code).ToArray();

        Assert.True(
            missing.Length == 0,
            "Estos permisos los exige un endpoint pero no existen en PermissionCatalog, así que ningún "
                + "rol puede tenerlos y el endpoint es inalcanzable: "
                + string.Join(", ", missing)
        );
    }
}
