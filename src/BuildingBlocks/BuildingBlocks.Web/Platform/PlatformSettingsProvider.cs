using BuildingBlocks.Platform;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Web.Platform;

/// <summary>
/// Guarda la versión vigente de <see cref="PlatformSettingsSnapshot"/> en memoria. La alimentan el
/// evento de Tenant y el refresco periódico; arranca con los valores locales (versión 0).
/// </summary>
public sealed class PlatformSettingsProvider(IOptions<PlatformSettingsOptions> options) : IPlatformSettings
{
    private readonly Lock _gate = new();
    private PlatformSettingsSnapshot _current = options.Value.ToBootstrapSnapshot();

    public PlatformSettingsSnapshot Current => Volatile.Read(ref _current);

    /// <summary>
    /// Aplica la foto solo si es más nueva que la vigente: el evento y el refresco pueden llegar en
    /// cualquier orden, y una foto vieja nunca pisa a una nueva. Devuelve si la aplicó.
    /// </summary>
    public bool TryApply(PlatformSettingsSnapshot snapshot)
    {
        lock (_gate)
        {
            if (snapshot.Version <= _current.Version)
                return false;

            Volatile.Write(ref _current, snapshot);
            return true;
        }
    }
}
