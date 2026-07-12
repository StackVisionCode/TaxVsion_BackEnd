namespace TaxVision.Signature.Application.Abstractions;

/// <summary>
/// Hashing/verification del Practitioner PIN. La implementación por defecto usa PBKDF2-HMAC-SHA256
/// con salt aleatorio y comparación en tiempo constante (evita side-channel de timing).
/// El dominio nunca ve el PIN en claro; sólo el hash resultante que se pasa a
/// <c>SignatureRequest.SetPractitionerPin</c>.
/// </summary>
public interface IPinHasher
{
    /// <summary>Produce un hash serializable (formato: <c>iters$saltB64$hashB64</c>).</summary>
    string Hash(string plaintext);

    /// <summary>Comparación en tiempo constante entre el hash almacenado y el candidato en claro.</summary>
    bool Verify(string plaintext, string hash);
}
