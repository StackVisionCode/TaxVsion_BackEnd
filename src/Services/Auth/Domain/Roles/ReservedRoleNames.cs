using System.Text;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Domain.Roles;

/// <summary>
/// Nombres que un rol de tenant no puede tomar: los pseudo-roles derivados del actor type
/// (<see cref="UserActorRoles"/>) y los nombres de los roles de sistema. Es defensa en
/// profundidad, no la barrera: la autorización de plataforma se decide por el claim
/// <c>actor_type</c>, nunca por el texto del nombre.
/// </summary>
public static class ReservedRoleNames
{
    /// <summary>
    /// Homoglifos cirílicos y griegos que se ven como una letra latina. NFKC no los unifica (son
    /// letras distintas, no formas de compatibilidad), así que sin este mapa un nombre con una
    /// <c>а</c> cirílica pasaría la validación viéndose idéntico al reservado.
    /// </summary>
    private static readonly Dictionary<char, char> Confusables = new()
    {
        ['а'] = 'a',
        ['А'] = 'a',
        ['е'] = 'e',
        ['Е'] = 'e',
        ['о'] = 'o',
        ['О'] = 'o',
        ['р'] = 'p',
        ['Р'] = 'p',
        ['с'] = 'c',
        ['С'] = 'c',
        ['у'] = 'y',
        ['У'] = 'y',
        ['х'] = 'x',
        ['Х'] = 'x',
        ['т'] = 't',
        ['Т'] = 't',
        ['ѕ'] = 's',
        ['Ѕ'] = 's',
        ['і'] = 'i',
        ['І'] = 'i',
        ['ј'] = 'j',
        ['Ј'] = 'j',
        ['М'] = 'm',
        ['Н'] = 'h',
        ['К'] = 'k',
        ['В'] = 'b',
        ['һ'] = 'h',
        ['α'] = 'a',
        ['Α'] = 'a',
        ['ε'] = 'e',
        ['Ε'] = 'e',
        ['ο'] = 'o',
        ['Ο'] = 'o',
        ['ρ'] = 'p',
        ['Ρ'] = 'p',
        ['τ'] = 't',
        ['Τ'] = 't',
        ['ι'] = 'i',
        ['Ι'] = 'i',
        ['Ν'] = 'n',
        ['Μ'] = 'm',
        ['Η'] = 'h',
        ['Β'] = 'b',
        ['Κ'] = 'k',
        ['Χ'] = 'x',
        ['Ζ'] = 'z',
        ['Υ'] = 'y',
        ['Σ'] = 's',
    };

    private static readonly HashSet<string> Reserved = BuildReservedKeys();

    /// <summary>
    /// Clave de comparación de un nombre de rol: NFKC, homoglifos plegados a su letra latina,
    /// minúsculas invariantes y todo lo que no sea alfanumérico descartado. Así
    /// <c>PlatformAdmin</c>, <c>platform admin</c>, <c>Platform-Admin</c>, un guion suave en medio
    /// y las variantes con letras cirílicas caen en la misma clave.
    /// </summary>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var composed = name.Normalize(NormalizationForm.FormKC);
        var key = new StringBuilder(composed.Length);
        foreach (var character in composed)
        {
            var folded = Confusables.TryGetValue(character, out var latin) ? latin : character;
            if (char.IsLetterOrDigit(folded))
                key.Append(char.ToLowerInvariant(folded));
        }
        return key.ToString();
    }

    /// <summary>true si el nombre colisiona con un pseudo-rol de actor type o con un rol de sistema.</summary>
    public static bool IsReserved(string? name) => Reserved.Contains(Normalize(name));

    /// <summary>Los nombres reservados tal como se escriben, para mensajes y para los tests.</summary>
    public static IReadOnlyList<string> DisplayNames { get; } =
    [
        UserActorRoles.For(UserActorType.TenantEmployee),
        UserActorRoles.For(UserActorType.TenantAdmin),
        UserActorRoles.For(UserActorType.CustomerPortal),
        UserActorRoles.For(UserActorType.PlatformAdmin),
        Role.SystemTenantAdmin,
        Role.SystemEmployee,
        Role.SystemCustomerPortal,
    ];

    private static HashSet<string> BuildReservedKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var actorType in Enum.GetValues<UserActorType>())
            keys.Add(Normalize(UserActorRoles.For(actorType)));

        keys.Add(Normalize(Role.SystemTenantAdmin));
        keys.Add(Normalize(Role.SystemEmployee));
        keys.Add(Normalize(Role.SystemCustomerPortal));
        return keys;
    }
}
