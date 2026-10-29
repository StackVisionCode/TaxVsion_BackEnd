using System.ComponentModel.DataAnnotations;

namespace TaxVision.Tasks.ClientForms.Application.Common;

public sealed class ClientFormsAttachmentOptions
{
    [Range(1, 1440)]
    public int PendingExpiryMinutes { get; init; } = 60;

    [Range(1, 1440)]
    public int ReconciliationIntervalMinutes { get; init; } = 5;

    [Range(1, 1440)]
    public int ReconcileAfterMinutes { get; init; } = 10;
}
