namespace TaxVision.Signature.Application.Abstractions;

/// <summary>Snapshot renderizado del consent + sus metadatos (version + hash).</summary>
public sealed record ConsentTextSnapshot(string Version, string Language, string Text, string TextSha256);

/// <summary>
/// Resuelve el texto del consent activo por categoría (nombre) e idioma. Es un contrato: la
/// implementación por defecto puede leer templates locales; a futuro puede consultar una tabla
/// versionada por tenant (P-16 avanzado). Una categoría custom del tenant cae en el texto genérico.
/// </summary>
public interface IConsentTextProvider
{
    ConsentTextSnapshot Resolve(string category, string language);
}
