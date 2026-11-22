namespace TaxVision.Notification.Application.Abstractions;

/// <summary>Idioma (<c>en</c>/<c>es</c>) y zona de quien recibe un aviso, en el momento de enviarlo.</summary>
public sealed record RecipientLocale(string Language, string? TimeZoneId, bool LanguageIsFallback)
{
    public static RecipientLocale English { get; } = new("en", null, true);

    /// <summary>Valor de la variable <c>language</c> de las plantillas de Scribe.</summary>
    public string TemplateLanguage => Language == "es" ? "Es" : "En";

    /// <summary>Las dos señales de Scribe: locale de fechas y zona del lector.</summary>
    public ScribeReader Reader => ScribeReader.From(Language, TimeZoneId);
}

/// <summary>
/// Cadena de respaldo (D-N2, D-N38): staff → su idioma → inglés; cuenta del portal → su idioma → el del
/// cliente → inglés; cliente sin cuenta → el del cliente → inglés; externo → el del evento → inglés.
/// </summary>
public interface IRecipientLocale
{
    Task<RecipientLocale> ForUserAsync(Guid tenantId, Guid userId, CancellationToken ct);

    Task<RecipientLocale> ForCustomerAsync(Guid tenantId, Guid customerId, CancellationToken ct);

    /// <summary>
    /// Dueño o administrador del que el productor solo da el correo: el idioma de ese usuario de la oficina; si la
    /// dirección no es de ninguno, inglés.
    /// </summary>
    Task<RecipientLocale> ForOfficeEmailAsync(Guid tenantId, string email, CancellationToken ct);

    /// <summary>Firmante, visitante o invitado: el idioma que trae el evento.</summary>
    RecipientLocale FromEventLanguage(string? language);
}
