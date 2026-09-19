using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Application.OptOut.Queries;
using TaxVision.Sms.Domain.OptOut;
using TaxVision.Sms.Domain.ValueObjects;

namespace TaxVision.Sms.Application.OptOut.Commands;

/// <summary>Acción de consentimiento manual desde el CRM (admin, <c>sms.manage</c>).</summary>
public enum SmsConsentAction
{
    OptOut = 0,
    OptIn,
}

/// <summary>
/// Gestión manual del consentimiento de un cliente (sin esperar al STOP/START entrante). Misma lógica
/// de dominio que el webhook inbound (find-or-create + OptOut/OptIn), pero disparada por un admin del
/// tenant con <c>(customerId, phone)</c> conocidos. El teléfono lo valida el VO. Idempotente.
/// </summary>
public sealed record SetSmsConsentCommand(Guid TenantId, Guid CustomerId, string Phone, SmsConsentAction Action);

public static class SetSmsConsentHandler
{
    private const string ManualKeyword = "manual";

    public static async Task<Result<SmsOptOutSummaryResponse>> Handle(
        SetSmsConsentCommand command,
        ISmsOptOutRepository optOuts,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var phoneResult = PhoneE164.Create(command.Phone);
        if (phoneResult.IsFailure)
            return Result.Failure<SmsOptOutSummaryResponse>(phoneResult.Error);
        var phone = phoneResult.Value;

        var nowUtc = DateTime.UtcNow;
        var optOut = await optOuts.GetAsync(command.TenantId, command.CustomerId, phone.Value, ct);
        if (optOut is null)
        {
            optOut = SmsOptOut.CreateSubscribed(command.TenantId, command.CustomerId, phone, nowUtc);
            await optOuts.AddAsync(optOut, ct);
        }

        var applied =
            command.Action == SmsConsentAction.OptOut
                ? optOut.OptOut(ManualKeyword, nowUtc)
                : optOut.OptIn(ManualKeyword, nowUtc);
        if (applied.IsFailure)
            return Result.Failure<SmsOptOutSummaryResponse>(applied.Error);

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(
            new SmsOptOutSummaryResponse(
                optOut.CustomerId,
                optOut.PhoneE164,
                optOut.Status,
                optOut.LastKeyword,
                optOut.OptedOutAtUtc,
                optOut.OptedInAtUtc,
                optOut.UpdatedAtUtc
            )
        );
    }
}
