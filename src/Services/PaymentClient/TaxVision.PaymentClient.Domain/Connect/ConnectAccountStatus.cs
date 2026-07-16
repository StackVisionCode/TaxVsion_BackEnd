namespace TaxVision.PaymentClient.Domain.Connect;

/// <summary>
/// Pending ──onboarding started──▶ InProgress
/// InProgress ──requirements_currently_due=[]──▶ Enabled
/// Enabled ──charges_enabled=false──▶ Restricted
/// Restricted ──resolved──▶ Enabled
/// Enabled/Restricted ──admin disable──▶ Disabled
/// </summary>
public enum ConnectAccountStatus
{
    Pending = 1,
    InProgress = 2,
    Enabled = 3,
    Restricted = 4,
    Disabled = 5,
}
