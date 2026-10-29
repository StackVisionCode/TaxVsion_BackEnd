namespace TaxVision.Documents.Application.Generations.ClientFormSummary;

/// <summary>
/// Caja de firma reservada en la última página del Submission Summary (P-18), en coordenadas
/// normalizadas 0..1 desde la esquina superior izquierda. Se deriva de las medidas físicas que usa la
/// plantilla client_form.submission_summary.v1 (.sign-box en mm) sobre A4 con los márgenes del
/// conversor. La página la decide el conteo real del PDF (ancla dinámica).
/// </summary>
public static class ClientFormSignatureAnchor
{
    // A4 y márgenes de PlaywrightHtmlToPdfConverter.
    public const double PageWidthMm = 210;
    public const double PageHeightMm = 297;
    public const double MarginTopMm = 12;
    public const double MarginLeftMm = 10;

    // padding-left del body (40px a 96 dpi).
    public const double BodyPaddingLeftMm = 40 * 25.4 / 96;

    // .sign-box dentro de .sign-page, que empieza arriba de la última página.
    public const double BoxTopMm = 50;
    public const double BoxWidthMm = 80;
    public const double BoxHeightMm = 24;

    public const double X = (MarginLeftMm + BodyPaddingLeftMm) / PageWidthMm;
    public const double Y = (MarginTopMm + BoxTopMm) / PageHeightMm;
    public const double W = BoxWidthMm / PageWidthMm;
    public const double H = BoxHeightMm / PageHeightMm;

    public const string PurposeClientForms = "ClientForms";
    public const string PurposeSignatures = "Signatures";
}
