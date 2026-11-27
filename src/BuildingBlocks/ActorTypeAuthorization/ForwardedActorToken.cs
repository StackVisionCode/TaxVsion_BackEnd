namespace BuildingBlocks.ActorTypeAuthorization;

/// <summary>
/// Cabecera con el access token del usuario que originó una llamada M2M, aparte del token de servicio. Quien la
/// recibe valida la firma y lee el actor y su step-up del token, no del cuerpo. Pieza aditiva.
/// </summary>
public static class ForwardedActorToken
{
    public const string HeaderName = "X-Actor-Authorization";
}
