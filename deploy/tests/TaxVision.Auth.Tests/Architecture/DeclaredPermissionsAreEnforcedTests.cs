using TaxVision.Auth.Domain.Roles;
using Xunit;

namespace TaxVision.Auth.Tests.Architecture;

/// <summary>
/// A7, la dirección inversa de <see cref="EnforcedPermissionsFitnessTests"/>: un permiso declarado en el
/// catálogo que no protege nada es peor que no tenerlo. Se ofrece en el cajón de accesos, un
/// administrador se lo concede a un rol creyendo que restringe algo, y no restringe nada — fue
/// exactamente lo que pasó con <c>portal.folders.view</c>, decorativo durante meses.
///
/// <para>
/// Un permiso se exige de tres formas y solo una es un atributo: también hay chequeos imperativos y los
/// de Node. Por eso "usado" se mide por referencia a la constante, no por el literal, y los archivos que
/// solo declaran o componen (el catálogo, las clases de constantes, el espejo de Node) no cuentan.
/// </para>
///
/// <para>
/// Lo declarado pero todavía no exigido se marca <see cref="Permission.IsReserved"/>: eso lo saca del
/// cajón de accesos, así que nadie cree que ya protege algo. Es la válvula de escape legítima — no
/// agregar excepciones acá.
/// </para>
/// </summary>
public sealed class DeclaredPermissionsAreEnforcedTests
{
    /// <summary>
    /// Excepciones temporales. El 2026-09-26 esta lista nació con 32 y quedó en **cero**: de aquellos,
    /// 2 se aplicaron a su endpoint (<c>signature.request.expire</c>, <c>signature.preparer.manage</c>),
    /// 1 pasó a ser un override real (<c>calendar.manage_all</c>) y el resto se marcó
    /// <see cref="Permission.IsReserved"/> porque no tenía superficie que proteger.
    ///
    /// <para>
    /// Vacía es como debe quedarse. Es un trinquete: agregar una entrada solo se justifica para dejar
    /// pasar temporalmente algo que ya se está arreglando, y el test también falla si una entrada sobra
    /// —o sea, si el permiso ya se exige— para que nadie la deje puesta y afloje la red.
    /// </para>
    /// </summary>
    private static readonly string[] KnownUnenforced = [];

    [Fact]
    public void Every_permission_in_the_catalog_is_enforced_somewhere_or_is_reserved()
    {
        var sourceRoot = Path.Combine(PermissionSourceIndex.RepoRoot(), "src");
        var constants = PermissionSourceIndex.CollectStringConstants(sourceRoot);
        PermissionSourceIndex.AddNodeMirrorConstants(sourceRoot, constants);
        var enforced = PermissionSourceIndex.CollectEnforcedCodes(sourceRoot, constants);
        var referenced = PermissionSourceIndex.CollectReferencedMembers(sourceRoot);

        Assert.NotEmpty(enforced);
        Assert.NotEmpty(referenced);

        // Un mismo código suele tener varias constantes (la del servicio y su alias en el catálogo):
        // alcanza con que UNA se use en algún lado.
        var keysByCode = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, expression) in constants)
        {
            var code = PermissionSourceIndex.Resolve(expression, constants);
            if (code is null)
                continue;
            if (!keysByCode.TryGetValue(code, out var keys))
                keysByCode[code] = keys = [];
            keys.Add(key);
        }

        var orphans = PermissionCatalog
            .All.Where(definition => !definition.IsReserved)
            .Select(definition => definition.Code)
            .Where(code =>
                !enforced.Contains(code)
                && !(keysByCode.TryGetValue(code, out var keys) && keys.Any(referenced.Contains))
            )
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();

        var appeared = orphans.Except(KnownUnenforced, StringComparer.Ordinal).ToArray();
        Assert.True(
            appeared.Length == 0,
            "Estos permisos se ofrecen al conceder un rol pero no los exige ningún endpoint ni chequeo, "
                + "así que un administrador cree que restringen algo y no restringen nada. Aplicalos "
                + "donde corresponda o marcalos IsReserved: "
                + string.Join(", ", appeared)
        );

        var fixedUp = KnownUnenforced.Except(orphans, StringComparer.Ordinal).ToArray();
        Assert.True(
            fixedUp.Length == 0,
            "Estos ya se exigen (o se marcaron reservados): sacalos de KnownUnenforced para que la lista "
                + "siga encogiendo y el trinquete no se afloje. "
                + string.Join(", ", fixedUp)
        );
    }
}
