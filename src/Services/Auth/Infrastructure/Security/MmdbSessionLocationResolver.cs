using System.Net;
using System.Net.Sockets;
using MaxMind.Db;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Sessions;

namespace TaxVision.Auth.Infrastructure.Security;

/// <summary>Ruta del archivo DB-IP City Lite (MMDB). Vacía o inexistente: la ubicación queda desactivada.</summary>
public sealed class SessionLocationOptions
{
    public const string SectionName = "Auth:SessionLocation";

    /// <summary>Relativa a la carpeta de la app; admite variables de entorno (%LOCALAPPDATA%).</summary>
    public string? DatabasePath { get; set; }
}

/// <summary>
/// Resuelve la ubicación con el archivo DB-IP abierto una sola vez, mapeado en memoria (el lector es seguro
/// entre hilos). Nunca llama a la red y nunca escribe la IP en el registro.
/// </summary>
public sealed class MmdbSessionLocationResolver(
    IOptions<SessionLocationOptions> options,
    ILogger<MmdbSessionLocationResolver> logger
) : ISessionLocationResolver, IDisposable
{
    private readonly Lazy<Reader?> _reader = new(() => Open(options.Value.DatabasePath, logger));

    public SessionLocation? Resolve(string? ipAddress)
    {
        var address = Lookupable(ipAddress);
        if (address is null)
            return null;

        try
        {
            var reader = _reader.Value;
            var record = reader?.Find<Dictionary<string, object>>(address);
            if (record is null)
                return null;

            var location = SessionLocation.Create(
                Text(record, "country", "iso_code"),
                Name(First(record, "subdivisions")),
                Name(Child(record, "city")),
                Number(Child(record, "location"), "latitude"),
                Number(Child(record, "location"), "longitude"),
                DateTime.UtcNow
            );
            return location.IsSuccess ? location.Value : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Session location lookup failed.");
            return null;
        }
    }

    /// <summary>La IP lista para buscar; nula si no se puede leer o es local, privada o de enlace.</summary>
    public static IPAddress? Lookupable(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress) || !IPAddress.TryParse(ipAddress.Trim(), out var address))
            return null;
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address))
            return null;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            var isPrivate =
                b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254);
            return isPrivate ? null : address;
        }

        var first = address.GetAddressBytes()[0];
        var isLocal = (first & 0xFE) == 0xFC || address.IsIPv6LinkLocal;
        return isLocal ? null : address;
    }

    public void Dispose()
    {
        if (_reader.IsValueCreated)
            _reader.Value?.Dispose();
    }

    private static Reader? Open(string? configuredPath, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            logger.LogWarning("Session location is off: Auth:SessionLocation:DatabasePath is empty.");
            return null;
        }

        var path = Environment.ExpandEnvironmentVariables(configuredPath);
        if (!Path.IsPathRooted(path))
            path = Path.Combine(AppContext.BaseDirectory, path);
        if (!File.Exists(path))
        {
            logger.LogWarning("Session location is off: the location database file was not found.");
            return null;
        }

        try
        {
            return new Reader(path, FileAccessMode.MemoryMapped);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Session location is off: the location database could not be opened.");
            return null;
        }
    }

    private static Dictionary<string, object>? Child(Dictionary<string, object>? record, string key) =>
        record is not null && record.TryGetValue(key, out var value) ? value as Dictionary<string, object> : null;

    private static Dictionary<string, object>? First(Dictionary<string, object> record, string key) =>
        record.TryGetValue(key, out var value) && value is List<object> { Count: > 0 } list
            ? list[0] as Dictionary<string, object>
            : null;

    private static string? Text(Dictionary<string, object> record, string key, string field) =>
        Child(record, key)?.TryGetValue(field, out var value) == true ? value as string : null;

    private static string? Name(Dictionary<string, object>? node) =>
        Child(node, "names")?.TryGetValue("en", out var value) == true ? value as string : null;

    private static double? Number(Dictionary<string, object>? node, string field) =>
        node is not null && node.TryGetValue(field, out var value) ? Convert.ToDouble(value) : null;
}
