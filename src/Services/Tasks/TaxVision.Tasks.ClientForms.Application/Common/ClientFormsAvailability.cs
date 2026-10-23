namespace TaxVision.Tasks.ClientForms.Application.Common;

public sealed record ClientFormsAvailability(bool Enabled, string? Reason)
{
    public static readonly ClientFormsAvailability Available = new(true, null);
}
