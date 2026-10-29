using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace TaxVision.Tasks.ClientForms.Application.Common;

/// <summary>
/// X-5 — retención y eliminación. Todos los plazos y los interruptores se cambian sin tocar código.
/// Default seguro: el job apagado y en DryRun; nada borra hasta que el operador lo encienda por regla
/// tras una semana de validación. R-3 queda SIEMPRE en DryRun en el código (espera validación legal).
/// </summary>
public sealed class ClientFormsRetentionOptions
{
    // Interruptor maestro: apagado por defecto. Con él apagado el job no hace nada.
    public bool Enabled { get; init; }

    // DryRun global: cuenta y audita, no borra. Encendido por defecto.
    public bool DryRun { get; init; } = true;

    [Range(1, 168)]
    public int JobIntervalHours { get; init; } = 24;

    [Range(1, 5000)]
    public int BatchSize { get; init; } = 200;

    // R-3 — mínimo de plataforma en años. El override por tenant solo sube desde aquí.
    [Range(1, 50)]
    public int CompletedRetentionYears { get; init; } = 7;

    // R-1 — SSN/ITIN/EIN reemplazados: a los 30 días y solo si nada los referencia.
    [Required]
    [ValidateObjectMembers]
    public ClientFormsRetentionRule SupersededSensitive { get; init; } = new() { Days = 30 };

    // R-2 — requests canceladas sin ningún envío: 180 días.
    [Required]
    [ValidateObjectMembers]
    public ClientFormsRetentionRule CancelledWithoutSubmission { get; init; } = new() { Days = 180 };

    // R-9 — participantes externos nunca vinculados: 365 días desde el último envío/cierre.
    [Required]
    [ValidateObjectMembers]
    public ClientFormsRetentionRule ExternalUnlinked { get; init; } = new() { Days = 365 };

    // R-10 — tokens/OTP/sesiones: 30 días tras expirar o consumirse.
    [Required]
    [ValidateObjectMembers]
    public ClientFormsRetentionRule AccessTokens { get; init; } = new() { Days = 30 };
}

/// <summary>Plazo y modo de una regla de retención. DryRun por defecto para no borrar sin validar.</summary>
public sealed class ClientFormsRetentionRule
{
    public bool DryRun { get; init; } = true;

    [Range(1, 36500)]
    public int Days { get; init; } = 30;
}
