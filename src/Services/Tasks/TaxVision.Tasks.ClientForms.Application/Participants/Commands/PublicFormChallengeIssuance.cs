using Microsoft.Extensions.Options;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

// Cuerpo constante del 202 del challenge: idéntico se haya enviado o no el código, así no es un oráculo.
// Solo informa el cupo para que la página lo muestre de antemano.
public sealed record PublicFormChallengeAcceptedResponse(int CodeLimitPerHour, int? RetryAfterSeconds)
{
    public const int CodeLimit = 5;

    public static readonly PublicFormChallengeAcceptedResponse Instance = new(CodeLimit, null);
}

internal static class PublicFormChallengeIssuance
{
    internal static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(10);
    private const int MaxIssuesPerEmailPerHour = PublicFormChallengeAcceptedResponse.CodeLimit;

    // Sin bloqueo vigente, fuera de la espera entre reenvíos y dentro de los cupos por email y tenant.
    internal static async Task<bool> CanIssueAsync(
        Guid tenantId,
        Guid? grantId,
        Guid? intakeLinkId,
        string email,
        DateTime nowUtc,
        IFormAccessChallengeRepository challenges,
        IFormExternalAccessSecurity security,
        IOptions<ClientFormsOptions> options,
        CancellationToken ct
    )
    {
        var lookups = security.EmailLookupHashes(tenantId, email);
        if (await challenges.HasActiveBlockAsync(tenantId, grantId, intakeLinkId, lookups, nowUtc, ct))
            return false;
        var latest = await challenges.GetLatestAsync(tenantId, grantId, intakeLinkId, lookups, ct);
        if (latest is not null && !latest.AllowsReissueAt(nowUtc))
            return false;
        var since = nowUtc.AddHours(-1);
        return await challenges.CountIssuedAsync(tenantId, lookups, since, ct) < MaxIssuesPerEmailPerHour
            && await challenges.CountTenantIssuedAsync(tenantId, since, ct)
                < options.Value.External.MaxOtpIssuesPerTenantPerHour;
    }
}
