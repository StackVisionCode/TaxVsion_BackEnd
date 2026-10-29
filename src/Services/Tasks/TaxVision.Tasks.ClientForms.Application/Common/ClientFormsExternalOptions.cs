using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace TaxVision.Tasks.ClientForms.Application.Common;

public sealed class ClientFormsExternalOptions
{
    [Range(1, 100_000)]
    public int MaxIntakeSubmissionsPerTenantPerDay { get; init; } = 200;

    [Range(1, 100_000)]
    public int MaxOtpIssuesPerTenantPerHour { get; init; } = 300;

    [Required]
    [ValidateObjectMembers]
    public ClientFormsBotProtectionOptions BotProtection { get; init; } = new();

    // Solo desarrollo: hosts locales (localhost) que pasan la validación de host de cualquier oficina,
    // para probar externos e intake en el navegador. Vacío por defecto; fuera de Development se ignora.
    public string[] DevelopmentHosts { get; init; } = [];
}
