using TaxVision.Auth.Domain.Sessions;

namespace TaxVision.Auth.Application.Abstractions;

/// <summary>Ubicación aproximada de una IP, desde un archivo local. Síncrono, sin red; si no sabe, devuelve nulo y nunca lanza.</summary>
public interface ISessionLocationResolver
{
    SessionLocation? Resolve(string? ipAddress);
}
