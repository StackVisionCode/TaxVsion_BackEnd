namespace TaxVision.Signature.Application.Abstractions.Sealing;

/// <summary>
/// Cómo se estampan los campos de un tenant: marco de la firma y formato de fecha (oficina) y rango de la
/// letra automática (plataforma). El editor y la vista del firmante leen los mismos valores, así lo que se
/// ve al preparar es lo que queda en el PDF.
/// </summary>
public sealed record StampStyle(bool ShowSignatureFrame, string DatePattern, int AutoFontMinPt, int AutoFontMaxPt);

public interface IStampStyleResolver
{
    Task<StampStyle> ResolveAsync(Guid tenantId, CancellationToken ct = default);
}
