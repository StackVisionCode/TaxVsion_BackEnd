namespace TaxVision.Connectors.Application.Abstractions;

public interface IDeliveryReportParser
{
    /// <summary>Lista vacía si el correo no trae un informe de entrega legible.</summary>
    IReadOnlyList<DeliveryReportEntry> Parse(byte[] rawMime);
}
