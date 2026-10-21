namespace TaxVision.Tasks.ClientForms.Application.Common;

public sealed record ClientFormsAvailability(bool Enabled, string? Reason)
{
    public static readonly ClientFormsAvailability Available = new(true, null);
}

public static class ClientFormsAvailabilityReasons
{
    public const string NotAvailable = "ClientForms.NotAvailable";
    public const string NotEnabled = "ClientForms.NotEnabled";
    public const string ModuleUnavailable = "ClientForms.ModuleUnavailable";
}
