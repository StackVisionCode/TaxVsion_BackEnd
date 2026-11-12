using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Domain.Definitions;

namespace TaxVision.Codes.Application.Definitions.SendGiftcard;

/// <summary>
/// Pide enviar una giftcard por correo. El backend solo guarda el HASH del código, así que el administrador lo
/// reenvía y aquí se verifica que corresponda a ESA giftcard antes de publicar nada (no se puede mandar un código
/// ajeno ni inventado con el membrete de una tarjeta real). Solo giftcards de plataforma activas.
/// </summary>
public sealed record SendGiftcardCommand(
    Guid CodeDefinitionId,
    string Code,
    string RecipientEmail,
    string? RecipientName,
    string? PersonalMessage,
    Guid ActorUserId
);

/// <summary>Lo necesario para armar el evento de envío; el controlador lo publica por el outbox.</summary>
public sealed record SendGiftcardReceipt(
    string ValueLabel,
    string Code,
    DateTime? ExpiresAtUtc,
    string RecipientEmail,
    string? RecipientName,
    string? PersonalMessage
);

public static class SendGiftcardHandler
{
    private const int MaxMessageLength = 500;

    public static async Task<Result<SendGiftcardReceipt>> Handle(
        SendGiftcardCommand command,
        ICodeDefinitionRepository definitions,
        ICodeTokenHasher hasher,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        if (command.CodeDefinitionId == Guid.Empty || command.ActorUserId == Guid.Empty)
            return Fail("Codes.SendGiftcard.InvalidRequest", "The gift card and the actor are required.");

        var email = command.RecipientEmail?.Trim() ?? string.Empty;
        if (!IsPlausibleEmail(email))
            return Fail("Codes.SendGiftcard.InvalidEmail", "Enter a valid recipient email.");

        var message = string.IsNullOrWhiteSpace(command.PersonalMessage) ? null : command.PersonalMessage.Trim();
        if (message is { Length: > MaxMessageLength })
            return Fail(
                "Codes.SendGiftcard.MessageTooLong",
                $"The message cannot exceed {MaxMessageLength} characters."
            );

        var definition = await definitions.GetOwnedByIdAsync(PlatformTenant.Id, command.CodeDefinitionId, ct);
        if (definition is null || definition.OwnerScope != CodeOwnerScope.Platform)
            return Fail("Codes.SendGiftcard.NotFound", "The gift card does not exist.");

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (definition.Status != CodeDefinitionStatus.Active || definition.ExpiresAtUtc is { } until && until <= nowUtc)
            return Fail("Codes.SendGiftcard.NotActive", "Only an active gift card can be sent.");

        var hash = hasher.Hash(command.Code ?? string.Empty);
        if (hash.IsFailure || hash.Value != definition.CodeHash)
            return Fail("Codes.SendGiftcard.CodeMismatch", "That code does not match this gift card.");

        var rule = definition.RuleVersions.OrderBy(r => r.Version).LastOrDefault();
        var label = ValueLabel(rule);
        if (label is null)
            return Fail("Codes.SendGiftcard.NoBenefit", "The gift card has no discount to send.");

        return Result.Success(
            new SendGiftcardReceipt(
                label,
                command.Code!.Trim().ToUpperInvariant(),
                definition.ExpiresAtUtc,
                email,
                string.IsNullOrWhiteSpace(command.RecipientName) ? null : command.RecipientName.Trim(),
                message
            )
        );
    }

    /// <summary>"20% OFF" o "$25 OFF", como se ve en la tarjeta.</summary>
    internal static string? ValueLabel(CodeRuleVersion? rule)
    {
        if (rule is null)
            return null;

        if (rule.Benefit.Percentage is { } pct)
            return $"{pct.Value / 100m:0.##}% OFF";

        if (rule.Benefit.FixedAmount is { } money)
            return $"${money.AmountCents / 100m:0.##} OFF";

        return null;
    }

    private static bool IsPlausibleEmail(string email) =>
        email.Length is >= 5 and <= 254
        && email.Count(c => c == '@') == 1
        && !email.Contains(' ')
        && email.Split('@') is [{ Length: > 0 }, { Length: > 2 } domain]
        && domain.Contains('.')
        && !domain.StartsWith('.')
        && !domain.EndsWith('.');

    private static Result<SendGiftcardReceipt> Fail(string code, string message) =>
        Result.Failure<SendGiftcardReceipt>(new Error(code, message));
}
