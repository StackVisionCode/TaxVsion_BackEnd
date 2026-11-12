namespace TaxVision.Referrals.Domain.Shares;

/// <summary>
/// Canal por el que se copió o envió el código de referido. Se registra en cada envío
/// (<see cref="ReferralCodeShare"/>) y viaja en el enlace (<c>?ch=</c>) para que la atribución
/// del referido quede con el canal real por el que llegó.
/// </summary>
public enum ReferralShareChannel
{
    Copy,
    Email,
    Sms,
    WhatsApp,
    Social,
    Other,
}
