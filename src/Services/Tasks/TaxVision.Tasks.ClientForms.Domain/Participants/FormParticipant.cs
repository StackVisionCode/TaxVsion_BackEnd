using System.Text.RegularExpressions;
using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Domain.Participants;

public sealed partial class FormParticipant : AggregateRoot
{
    private FormParticipant() { }

    public string Email { get; private set; } = string.Empty;
    public string? FullName { get; private set; }
    public string? PhoneE164 { get; private set; }
    public string Language { get; private set; } = "En";
    public FormParticipantStatus Status { get; private set; }
    public DateTime? VerifiedAtUtc { get; private set; }
    public Guid? LinkedCustomerId { get; private set; }
    public DateTime? LinkedAtUtc { get; private set; }
    public Guid? LinkedByUserId { get; private set; }
    public FormParticipantSource Source { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? LostAtUtc { get; private set; }
    public Guid? LostByUserId { get; private set; }
    public string? LostReason { get; private set; }

    public const int LostReasonMaxLength = 200;

    public static Result<FormParticipant> Create(
        Guid tenantId,
        string email,
        string? fullName,
        string? phoneE164,
        FormParticipantSource source,
        DateTime nowUtc,
        string language = "En"
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<FormParticipant>(FormParticipantErrors.InvalidContact);
        var normalizedEmail = ParticipantEmail.Create(email);
        if (normalizedEmail.IsFailure)
            return Result.Failure<FormParticipant>(normalizedEmail.Error);
        var contact = NormalizeContact(fullName, phoneE164);
        if (contact.IsFailure)
            return Result.Failure<FormParticipant>(contact.Error);
        var participant = new FormParticipant
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail.Value.Value,
            FullName = contact.Value.FullName,
            PhoneE164 = contact.Value.Phone,
            Language = NormalizeLanguage(language),
            Status = FormParticipantStatus.Unverified,
            Source = source,
            CreatedAtUtc = nowUtc,
        };
        participant.SetTenant(tenantId);
        return Result.Success(participant);
    }

    // Un participante ya vinculado sigue vinculado: verificar otra de sus requests no lo degrada.
    public Result MarkVerified(DateTime nowUtc)
    {
        VerifiedAtUtc ??= nowUtc;
        if (Status != FormParticipantStatus.Linked)
            Status = FormParticipantStatus.Verified;
        return Result.Success();
    }

    public Result LinkToCustomer(Guid customerId, Guid byUserId, DateTime nowUtc)
    {
        if (customerId == Guid.Empty || byUserId == Guid.Empty || Status == FormParticipantStatus.Unverified)
            return Result.Failure(FormParticipantErrors.InvalidTransition);
        if (LinkedCustomerId.HasValue)
            return LinkedCustomerId.Value == customerId
                ? Result.Success()
                : Result.Failure(FormParticipantErrors.AlreadyLinked);
        LinkedCustomerId = customerId;
        LinkedAtUtc = nowUtc;
        LinkedByUserId = byUserId;
        Status = FormParticipantStatus.Linked;
        // Convertirse en cliente reabre un prospecto marcado como perdido.
        ClearLost();
        return Result.Success();
    }

    // Prospecto perdido: nunca uno ya convertido en cliente. Volver a marcarlo actualiza el motivo.
    public Result MarkLost(string? reason, Guid byUserId, DateTime nowUtc)
    {
        if (LinkedCustomerId.HasValue)
            return Result.Failure(FormParticipantErrors.AlreadyConverted);
        var normalized = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (byUserId == Guid.Empty || normalized?.Length > LostReasonMaxLength)
            return Result.Failure(FormParticipantErrors.InvalidLostReason);
        LostAtUtc = nowUtc;
        LostByUserId = byUserId;
        LostReason = normalized;
        return Result.Success();
    }

    // Idempotente: reabrir uno que no está perdido no cambia nada.
    public Result Reopen(Guid byUserId, DateTime nowUtc)
    {
        if (byUserId == Guid.Empty)
            return Result.Failure(FormParticipantErrors.InvalidTransition);
        ClearLost();
        return Result.Success();
    }

    public bool IsLost => LostAtUtc.HasValue;

    private void ClearLost()
    {
        LostAtUtc = null;
        LostByUserId = null;
        LostReason = null;
    }

    public Result UpdateContact(string? fullName, string? phoneE164)
    {
        if (Status == FormParticipantStatus.Linked)
            return Result.Failure(FormParticipantErrors.InvalidTransition);
        var contact = NormalizeContact(fullName, phoneE164);
        if (contact.IsFailure)
            return Result.Failure(contact.Error);
        FullName = contact.Value.FullName;
        PhoneE164 = contact.Value.Phone;
        return Result.Success();
    }

    public void SetLanguage(string? language) => Language = NormalizeLanguage(language);

    // Solo En o Es; cualquier otro valor cae a En.
    public static string NormalizeLanguage(string? language) =>
        string.Equals(language?.Trim(), "Es", StringComparison.OrdinalIgnoreCase) ? "Es" : "En";

    private static Result<(string? FullName, string? Phone)> NormalizeContact(string? fullName, string? phoneE164)
    {
        var name = string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim();
        var phone = string.IsNullOrWhiteSpace(phoneE164) ? null : phoneE164.Trim();
        if (name?.Length > 200 || (phone is not null && !PhoneRegex().IsMatch(phone)))
            return Result.Failure<(string?, string?)>(FormParticipantErrors.InvalidContact);
        return Result.Success((name, phone));
    }

    [GeneratedRegex(@"^\+[1-9]\d{6,14}$", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();
}
