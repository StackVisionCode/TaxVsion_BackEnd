namespace TaxVision.Documents.Application.Generations;

/// <summary>
/// Nombre de descarga de los recibos de la plataforma: <c>{Marca}_Receipt_{fecha}.pdf</c>. La marca sale
/// de PlatformSettings y se queda solo con letras y números, para que un nombre con espacios o signos
/// no rompa la descarga.
/// </summary>
public static class ReceiptFileNames
{
    public static string For(string platformBrand, DateTime paidAtUtc)
    {
        var brand = new string((platformBrand ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
        return $"{(brand.Length == 0 ? "Platform" : brand)}_Receipt_{paidAtUtc:yyyy-MM-dd}.pdf";
    }
}
