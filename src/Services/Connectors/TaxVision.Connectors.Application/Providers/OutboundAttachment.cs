namespace TaxVision.Connectors.Application.Providers;

/// <summary>
/// Adjunto normalizado que cruza el M2M de envío (D3 Compose §9/§11.2) — bytes ya resueltos, nunca una
/// referencia a CloudStorage: Connectors no habla con CloudStorage (esa es responsabilidad del caller,
/// Postmaster, D3 Compose §12). Cada <see cref="IOutboundEmailProviderClient"/> lo embebe en la forma
/// nativa del proveedor.
/// </summary>
public sealed record OutboundAttachment(string Filename, string ContentType, byte[] Content);
