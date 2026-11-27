namespace TaxVision.Customer.Application.Abstractions;

/// <summary>
/// Access token del usuario de la petición en curso (ya validado por el JwtBearer de Customer). Se reenvía a Auth en
/// los M2M de credenciales para que lea el actor y su step-up de la firma (SR-03). Null fuera de una petición HTTP.
/// Nunca se loguea.
/// </summary>
public interface IActorAccessTokenReader
{
    string? Current { get; }
}

/// <summary>Sin petición HTTP (jobs, consumidores): no hay token que reenviar.</summary>
public sealed class NoActorAccessToken : IActorAccessTokenReader
{
    public string? Current => null;
}
