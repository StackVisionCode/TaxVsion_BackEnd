namespace TaxVision.Signature.Application.Templates.Commands.Instantiate;

/// <summary>
/// Datos reales del firmante que rellena un slot de la plantilla. <see cref="SlotOrder"/>
/// debe corresponder a un slot existente en la plantilla; <see cref="Email"/> y
/// <see cref="FullName"/> se validan como value objects. <see cref="PhoneNumber"/> es opcional,
/// pero obligatorio de facto cuando el slot exige OTP por SMS/WhatsApp (sin él, la emisión del
/// challenge responde <c>NoDeliveryAddress</c>).
/// </summary>
public sealed record SlotBinding(int SlotOrder, string Email, string FullName, string? PhoneNumber = null);
