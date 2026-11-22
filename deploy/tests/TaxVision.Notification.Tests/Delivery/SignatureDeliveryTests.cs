using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Consumers.Signature;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests.Delivery;

internal static class SignatureCase
{
    public const string SignerEmail = "signer@example.com";
    public const string SignerPhone = "+15550001111";

    public static SignerInvitedIntegrationEvent Invited(
        string language = "En",
        string channel = "Email",
        Guid? customerId = null
    ) =>
        new()
        {
            EventId = DeliveryCase.EventId,
            TenantId = DeliveryCase.TenantId,
            OccurredOn = DeliveryCase.OccurredOn,
            CorrelationId = "corr",
            SignatureRequestId = DeliveryCase.ResourceId,
            SignerId = DeliveryCase.OtherUserId,
            Email = SignerEmail,
            FullName = "Ada Lovelace",
            Order = 1,
            Language = language,
            PhoneE164 = SignerPhone,
            PreferredChannel = channel,
            PublicToken = "public-token",
            ExpiresAtUtc = new DateTime(2026, 11, 27, 15, 0, 0, DateTimeKind.Utc),
            RevocationEpoch = 0,
            RequiresConsent = true,
            RequiresSequentialSigning = false,
            MappedCustomerId = customerId,
        };

    public static Task InvitedAsync(DeliveryRecorder recorder, SignerInvitedIntegrationEvent evt) =>
        SignerInvitedConsumer.Handle(
            evt,
            recorder.Scribe,
            recorder.Dispatcher,
            new DeliveryCase.PortalResolver(),
            recorder.Locales,
            DeliveryCase.Portal,
            TestPlatform.Settings,
            DeliveryCase.Host,
            recorder.Correlation,
            NullLogger<SignerInvitedIntegrationEvent>.Instance,
            CancellationToken.None
        );
}

public sealed class SignerInvitedConsumerDeliveryTests
{
    [Fact]
    public async Task Delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder();

        await SignatureCase.InvitedAsync(recorder, SignatureCase.Invited());

        DeliveryCase.Same(
            [
                "email to=signer@example.com tpl=sig.invitation.v1 event=evt scope=System confidential=False idem= attachments=0",
                "event NotificationsEmailSendRequestedIntegrationEvent To=signer@example.com TemplateKey=sig.invitation.v1",
                "log Email kind=sig.invitation.v1 tpl=sig.invitation.v1 to=signer@example.com status=Pending event=evt subject=subject",
                "render sig.signer_invited.v1 audience=Client vars=expires_at={\"$instant\":\"2026-11-27T15:00:00.0000000Z\"};full_name=Ada Lovelace;invite_link=https://office.example.com/signature/public/public-token;language=En;requires_consent=True",
            ],
            recorder.Lines()
        );
        Assert.Equal("En", recorder.Scribe.LastReader?.Locale);
    }

    [Fact]
    public async Task Sms_delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder("es");

        await SignatureCase.InvitedAsync(recorder, SignatureCase.Invited("Es", "Sms"));

        DeliveryCase.Same(
            [
                "log Sms kind=sig.signer_invited.sms tpl=sig.signer_invited.sms to=+15550001111 status=Sent event=evt subject=sig.signer_invited.sms",
                "sms to=+15550001111 tpl=sig.signer_invited.sms idem=evt text=TaxOffix: Ada Lovelace, tienes un documento para firmar. Ábrelo aquí: https://office.example.com/signature/public/public-token",
            ],
            recorder.Lines()
        );
    }

    [Fact]
    public async Task Portal_signer_delivery_is_unchanged_after_migration()
    {
        var recorder = DeliveryCase.Recorder();

        await SignatureCase.InvitedAsync(recorder, SignatureCase.Invited(customerId: DeliveryCase.CustomerId));

        DeliveryCase.Same(
            [
                "email to=signer@example.com tpl=sig.invitation.v1 event=evt scope=System confidential=False idem= attachments=0",
                "event NotificationsEmailSendRequestedIntegrationEvent To=signer@example.com TemplateKey=sig.invitation.v1",
                "inapp kind=signature.signer.invited to=portal cat=Portal priority=High title=Signature requested body=A document is waiting for your signature. rt=signature_request rid=resource ctx=customerId=customer params=fullName=Ada Lovelace action=sign src=evt srcType=signature.signer.invited.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log Email kind=sig.invitation.v1 tpl=sig.invitation.v1 to=signer@example.com status=Pending event=evt subject=subject",
                "log InApp kind=signature.signer.invited tpl=signature.signer.invited to=user:portal status=Sent event=evt subject=Signature requested",
                "render sig.signer_invited.v1 audience=Client vars=expires_at={\"$instant\":\"2026-11-27T15:00:00.0000000Z\"};full_name=Ada Lovelace;invite_link=https://office.example.com/signature/public/public-token;language=En;requires_consent=True",
            ],
            recorder.Lines()
        );
    }

    // Obligatorio por catálogo y el firmante externo no tiene usuario: apagar la preferencia no cambia nada.
    [Fact]
    public async Task Preferences_off_change_nothing()
    {
        var recorder = DeliveryCase.Recorder();
        recorder.Preferences.Off.Add((NotificationCategory.DocumentsAndSignatures, NotificationChannel.InApp));
        recorder.Preferences.Off.Add((NotificationCategory.DocumentsAndSignatures, NotificationChannel.Email));

        await SignatureCase.InvitedAsync(recorder, SignatureCase.Invited(customerId: DeliveryCase.CustomerId));

        DeliveryCase.Same(
            [
                "email to=signer@example.com tpl=sig.invitation.v1 event=evt scope=System confidential=False idem= attachments=0",
                "event NotificationsEmailSendRequestedIntegrationEvent To=signer@example.com TemplateKey=sig.invitation.v1",
                "inapp kind=signature.signer.invited to=portal cat=Portal priority=High title=Signature requested body=A document is waiting for your signature. rt=signature_request rid=resource ctx=customerId=customer params=fullName=Ada Lovelace action=sign src=evt srcType=signature.signer.invited.v1 group= actor=/ at=2026-11-20T15:00:00.0000000Z",
                "log Email kind=sig.invitation.v1 tpl=sig.invitation.v1 to=signer@example.com status=Pending event=evt subject=subject",
                "log InApp kind=signature.signer.invited tpl=signature.signer.invited to=user:portal status=Sent event=evt subject=Signature requested",
                "render sig.signer_invited.v1 audience=Client vars=expires_at={\"$instant\":\"2026-11-27T15:00:00.0000000Z\"};full_name=Ada Lovelace;invite_link=https://office.example.com/signature/public/public-token;language=En;requires_consent=True",
            ],
            recorder.Lines()
        );
    }
}
