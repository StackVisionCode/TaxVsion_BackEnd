using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using TaxVision.DataCollection.Engine;

namespace TaxVision.Tasks.ClientForms.Application.Common;

public sealed class ClientFormsOptions
{
    public const string SectionName = "ClientForms";

    [Required]
    [ValidateObjectMembers]
    public DataCollectionLimits Limits { get; init; } = new();

    [Required]
    [ValidateObjectMembers]
    public ClientFormsAttachmentOptions Attachments { get; init; } = new();

    [Required]
    [ValidateObjectMembers]
    public ClientFormsReminderOptions Reminders { get; init; } = new();

    [Required]
    [ValidateObjectMembers]
    public ClientFormsExternalOptions External { get; init; } = new();

    [Required]
    [ValidateObjectMembers]
    public ClientFormsRetentionOptions Retention { get; init; } = new();
}
