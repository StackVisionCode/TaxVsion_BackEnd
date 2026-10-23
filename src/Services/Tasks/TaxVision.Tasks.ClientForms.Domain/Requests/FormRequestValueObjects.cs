using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Domain.Requests;

public sealed record FormParticipantRef
{
    private FormParticipantRef() { }

    public FormParticipantKind Kind { get; private init; }
    public Guid? CustomerId { get; private init; }
    public Guid? ParticipantId { get; private init; }

    public static FormParticipantRef Customer(Guid customerId) =>
        new() { Kind = FormParticipantKind.Customer, CustomerId = customerId };

    public static FormParticipantRef External(Guid participantId) =>
        new() { Kind = FormParticipantKind.External, ParticipantId = participantId };

    public bool IsValid =>
        Kind switch
        {
            FormParticipantKind.Customer => CustomerId is not null && CustomerId != Guid.Empty && ParticipantId is null,
            FormParticipantKind.External => ParticipantId is not null
                && ParticipantId != Guid.Empty
                && CustomerId is null,
            _ => false,
        };
}

public sealed record CustomerLinkState
{
    private CustomerLinkState() { }

    public CustomerLinkStatus Status { get; private init; }
    public Guid? CustomerId { get; private init; }
    public string? Reason { get; private init; }
    public Guid? ConflictingRequestId { get; private init; }

    public static CustomerLinkState None() => new() { Status = CustomerLinkStatus.None };

    public static CustomerLinkState Linked(Guid customerId) =>
        new() { Status = CustomerLinkStatus.Linked, CustomerId = customerId };

    public static CustomerLinkState Blocked(Guid customerId, string reason, Guid? conflictingRequestId) =>
        new()
        {
            Status = CustomerLinkStatus.Blocked,
            CustomerId = customerId,
            Reason = reason,
            ConflictingRequestId = conflictingRequestId,
        };
}

public sealed class FormActor
{
    private FormActor() { }

    public FormActorKind Kind { get; private set; }
    public Guid? PortalUserId { get; private set; }
    public Guid? ParticipantId { get; private set; }
    public Guid? SessionId { get; private set; }

    public static FormActor PortalUser(Guid portalUserId) =>
        new() { Kind = FormActorKind.PortalUser, PortalUserId = portalUserId };

    public static FormActor ExternalSession(Guid participantId, Guid sessionId) =>
        new()
        {
            Kind = FormActorKind.ExternalSession,
            ParticipantId = participantId,
            SessionId = sessionId,
        };

    public FormActor Copy() =>
        Kind == FormActorKind.PortalUser
            ? PortalUser(PortalUserId ?? Guid.Empty)
            : ExternalSession(ParticipantId ?? Guid.Empty, SessionId ?? Guid.Empty);

    public bool IsValid =>
        Kind switch
        {
            FormActorKind.PortalUser => PortalUserId is not null
                && PortalUserId != Guid.Empty
                && ParticipantId is null
                && SessionId is null,
            FormActorKind.ExternalSession => PortalUserId is null
                && ParticipantId is not null
                && ParticipantId != Guid.Empty
                && SessionId is not null
                && SessionId != Guid.Empty,
            _ => false,
        };
}

public sealed record FormAnswerAddress(string ComponentId, string? RowId)
{
    public static Result<FormAnswerAddress> Create(string? componentId, string? rowId)
    {
        var component = componentId?.Trim();
        var row = string.IsNullOrWhiteSpace(rowId) ? null : rowId.Trim();
        return string.IsNullOrWhiteSpace(component) || component.Length > 32 || row?.Length > 32
            ? Result.Failure<FormAnswerAddress>(FormRequestErrors.InvalidComponent)
            : Result.Success(new FormAnswerAddress(component, row));
    }
}

public sealed class ProtectedAnswers
{
    private ProtectedAnswers() { }

    public ProtectedAnswers(string cipher, short keyVersion)
    {
        Cipher = cipher;
        KeyVersion = keyVersion;
    }

    public string Cipher { get; private set; } = string.Empty;
    public short KeyVersion { get; private set; }
}

public sealed class ProtectedTaxIdentifier
{
    private ProtectedTaxIdentifier() { }

    public ProtectedTaxIdentifier(string cipher, short keyVersion, string last4)
    {
        Cipher = cipher;
        KeyVersion = keyVersion;
        Last4 = last4;
    }

    public string Cipher { get; private set; } = string.Empty;
    public short KeyVersion { get; private set; }
    public string Last4 { get; private set; } = string.Empty;

    public static Result<ProtectedTaxIdentifier> Create(string cipher, short keyVersion, string last4) =>
        string.IsNullOrWhiteSpace(cipher) || keyVersion <= 0 || !IsLast4(last4)
            ? Result.Failure<ProtectedTaxIdentifier>(FormRequestErrors.InvalidTaxIdentifier)
            : Result.Success(new ProtectedTaxIdentifier(cipher, keyVersion, last4));

    private static bool IsLast4(string value)
    {
        if (value.Length != 4)
            return false;
        foreach (var character in value)
        {
            if (!char.IsAsciiDigit(character))
                return false;
        }
        return true;
    }
}

public sealed record FormFileVerdict(
    FormFileVerdictStatus Status,
    string? OwnerType,
    Guid? OwnerId,
    string? FolderType,
    DateTime OccurredAtUtc
);
