namespace TaxVision.Signature.Api.Requests;

/// <summary>
/// Crea una firma reutilizable. <c>Scope</c>: "office" = firma de oficina (requiere admin); cualquier
/// otro valor = firma personal del actor. <c>ImageBase64</c> es el PNG sin el prefijo data-url.
/// </summary>
public sealed record CreateSignatureProfileBody(string Label, string? Scope, string ImageBase64);

public sealed record RenameSignatureProfileBody(string Label);
