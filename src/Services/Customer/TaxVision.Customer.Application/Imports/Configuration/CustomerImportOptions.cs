namespace TaxVision.Customer.Application.Imports.Configuration;

/// <summary>
/// Parametros configurables de la importacion de clientes.
///
/// Se enlaza desde la seccion "CustomerImport" de la configuracion (appsettings hoy),
/// pero esta pensada para resolverse via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>
/// de modo que en el futuro una interfaz grafica de administracion pueda ajustar estos
/// valores en caliente (configuracion global del SaaS o, mas adelante, por tenant) sin
/// necesidad de recompilar ni reiniciar el servicio.
/// </summary>
public sealed class CustomerImportOptions
{
    /// <summary>Nombre de la seccion de configuracion.</summary>
    public const string SectionName = "CustomerImport";

    /// <summary>Valor por defecto del tamano maximo de archivo (10 MB).</summary>
    public const int DefaultMaxFileBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Tamano maximo permitido del archivo a importar, en bytes. Se aplica tanto en el borde
    /// (limite de tamano del request en el controller) como en el handler. Debe ser mayor que 0.
    /// </summary>
    public int MaxFileBytes { get; set; } = DefaultMaxFileBytes;
}
