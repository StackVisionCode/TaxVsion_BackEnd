namespace TaxVision.Tenant.Domain.ValueObjects;

/// <summary>
/// EFIN cifrado con AES-GCM (partes crudas + versión de la clave maestra, para rotar sin perder datos) y
/// sus últimos 2 dígitos en claro para mostrarlo enmascarado sin descifrar. El dominio nunca conoce la
/// clave: lo produce y lo abre un protector de infraestructura.
/// </summary>
public sealed class EncryptedEfin
{
    public const string MaskPrefix = "••••";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    // EF Core materializa el valor guardado.
    private EncryptedEfin() { }

    public byte[] Ciphertext { get; private set; } = [];
    public byte[] Nonce { get; private set; } = [];
    public byte[] Tag { get; private set; } = [];
    public short KeyVersion { get; private set; }
    public string LastDigits { get; private set; } = string.Empty;

    public string Masked => MaskPrefix + LastDigits;

    public static EncryptedEfin Create(byte[] ciphertext, byte[] nonce, byte[] tag, short keyVersion, string lastDigits)
    {
        if (ciphertext.Length == 0 || nonce.Length != NonceSize || tag.Length != TagSize)
            throw new ArgumentException("The encrypted EFIN parts do not match AES-GCM sizes.");
        if (lastDigits.Length != Efin.VisibleDigits)
            throw new ArgumentException("The visible EFIN digits must be the last two.", nameof(lastDigits));

        return new EncryptedEfin
        {
            Ciphertext = ciphertext,
            Nonce = nonce,
            Tag = tag,
            KeyVersion = keyVersion,
            LastDigits = lastDigits,
        };
    }
}
