namespace TaxVision.Connectors.Application.Abstractions;

public interface IDeliveryReportParser
{
    /// <summary>Sin destinatarios si el correo no trae un informe de entrega legible.</summary>
    DeliveryReport Parse(byte[] rawMime);
}
