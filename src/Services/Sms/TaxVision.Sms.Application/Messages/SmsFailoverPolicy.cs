using TaxVision.Sms.Application.Providers;

namespace TaxVision.Sms.Application.Messages;

/// <summary>Cuándo se pasa al siguiente proveedor: solo si este dijo que no salió (rechazo, caído, media no soportada).
/// Un resultado desconocido o la baja en el carrier lo cortan: otro proveedor podría mandar un segundo SMS.</summary>
public static class SmsFailoverPolicy
{
    public static bool ShouldTryNext(SmsSendResult result) =>
        !result.Accepted && result.ErrorCode is not (SmsSendFailure.OutcomeUnknown or SmsSendFailure.RecipientOptedOut);
}
