namespace TaxVision.Signature.Application.Requests.Preparer;

/// <summary>Enmascara un PTIN/EFIN dejando visibles el primero y los últimos 4 (p. ej. "P•••••5678").</summary>
public static class PreparerIdentifierMask
{
    public static string Mask(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return string.Empty;
        if (identifier.Length <= 4)
            return new string('•', identifier.Length);
        return identifier[0] + new string('•', identifier.Length - 5) + identifier[^4..];
    }
}
