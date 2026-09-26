using BuildingBlocks.Results;

namespace TaxVision.Signature.Application.Profiles;

/// <summary>Errores compartidos por los handlers de firmas reutilizables.</summary>
public static class SignatureProfileErrors
{
    public static readonly Error NotFound = new(
        "Signature.Profile.NotFound",
        "The signature does not exist for this tenant."
    );

    /// <summary>El tenant apagó las firmas propias de empleado: solo se permite la firma de oficina.</summary>
    public static readonly Error OwnSignatureDisabled = new(
        "Signature.Profile.OwnSignatureDisabled",
        "Your office requires the office signature. Personal signatures are disabled."
    );
}
