namespace BuildingBlocks.Security;

/// <summary>
/// Por qué falló <see cref="ISecretProtector.TryUnprotect"/> (BB-21). Antes todo devolvía
/// <c>null</c>, así que un ataque activo y una rotación de claves mal hecha se veían exactamente
/// igual que un campo vacío: en silencio.
/// </summary>
public enum SecretUnprotectFailure
{
    /// <summary>Descifrado correcto.</summary>
    None = 0,

    /// <summary>No había nada guardado (null o en blanco). Caso normal, no es un incidente.</summary>
    NoValueStored,

    /// <summary>
    /// No es base64, o es más corto que <c>nonce + tag</c>: nunca fue un envelope de este sistema.
    /// Apunta a corrupción de datos o a un campo que se llenó por otra vía.
    /// </summary>
    MalformedInput,

    /// <summary>
    /// El envelope tiene la forma correcta pero el tag de autenticación no valida. **Evento de
    /// seguridad o incidente operativo**: o el dato fue manipulado, o la clave configurada no es la
    /// que lo cifró (rotación mal hecha). AES-GCM no permite distinguir ambos casos — el tag falla
    /// igual — así que el llamante debe tratarlo como los dos a la vez: alertar y revisar la config.
    /// </summary>
    AuthenticationFailed,
}
