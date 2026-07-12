namespace TaxVision.Signature.Application.Templates.Commands.RemoveSlot;

public sealed record RemoveTemplateSlotCommand(Guid TenantId, Guid TemplateId, int SlotOrder);
