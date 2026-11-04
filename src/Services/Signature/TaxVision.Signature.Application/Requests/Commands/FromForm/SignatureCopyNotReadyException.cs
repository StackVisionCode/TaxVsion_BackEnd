namespace TaxVision.Signature.Application.Requests.Commands.FromForm;

/// <summary>
/// La copia a firmar aún no está registrada o disponible en CloudStorage (subida o scan en curso). Transitoria:
/// el consumer la reintenta con su propia política hasta el último intento, donde publica Failed.
/// </summary>
public sealed class SignatureCopyNotReadyException(string message) : Exception(message);
