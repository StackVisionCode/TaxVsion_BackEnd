using System.ComponentModel.DataAnnotations;

namespace TaxVision.Tasks.ClientForms.Application.Common;

public sealed class ClientFormsBotProtectionOptions
{
    public string? SiteKey { get; init; }

    public string? SecretKey { get; init; }

    [Required]
    public string SiteVerifyUrl { get; init; } = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    [Required]
    public string ExpectedAction { get; init; } = "client_forms_intake";
}
