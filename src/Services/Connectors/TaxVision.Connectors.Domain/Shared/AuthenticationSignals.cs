namespace TaxVision.Connectors.Domain.Shared;

public enum AuthenticationResult
{
    Pass,
    Fail,
    None,
    Unknown,
}

/// <summary>
/// SPF/DKIM/DMARC del header <c>Authentication-Results</c> del proveedor, parseado por Connectors
/// (Gmail: <c>format=metadata&amp;metadataHeaders=Authentication-Results</c>; Graph:
/// <c>internetMessageHeaders</c>). Ver Connectors_Service_Design_And_Implementation_Plan.md §34.5
/// D2 — cierra el hueco de spoofing: Correspondence no puede confiar solo en el string-match de
/// <c>From</c> contra un customer conocido, porque nunca ve estas señales si Connectors no las propaga.
/// </summary>
public sealed record AuthenticationSignals(
    AuthenticationResult SpfResult,
    AuthenticationResult DkimResult,
    AuthenticationResult DmarcResult
)
{
    public static readonly AuthenticationSignals Unknown = new(
        AuthenticationResult.Unknown,
        AuthenticationResult.Unknown,
        AuthenticationResult.Unknown
    );
}
