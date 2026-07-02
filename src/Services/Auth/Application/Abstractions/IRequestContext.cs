namespace TaxVision.Auth.Application.Abstractions;

/// <summary>
/// Datos de la petición HTTP en curso (IP y user-agent), abstraídos para que
/// Application no dependa de ASP.NET. Implementado en la capa Api.
/// </summary>
public interface IRequestContext
{
    string? IpAddress { get; }
    string? UserAgent { get; }
}
