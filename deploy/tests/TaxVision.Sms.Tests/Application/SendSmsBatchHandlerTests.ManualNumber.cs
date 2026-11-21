using TaxVision.Sms.Application.Messages.Commands;
using TaxVision.Sms.Domain;
using TaxVision.Sms.Domain.ValueObjects;

namespace TaxVision.Sms.Tests.Application;

/// <summary>Un número escrito a mano exige <c>sms.send_any_number</c> cuando quien envía es una persona.</summary>
public sealed partial class SendSmsBatchHandlerTests
{
    private static readonly string ManualNumberNotAllowedCode = SmsErrors.ManualNumberNotAllowed.Code;

    private static SmsSendItemDto TypedNumber(string key = "typed") =>
        new(Guid.Empty, OtherPhone, "hi", null, key, "docs");

    private static SendSmsBatchCommand ChargedFrom(Guid sender, params SmsSendItemDto[] items) =>
        new(
            Tenant,
            "corr-1",
            items,
            ActorUserId: sender,
            CanViewAll: true,
            ChargeWallet: true,
            CallerBearerToken: "bearer-token",
            BatchKey: BatchKey
        );

    [Fact]
    public async Task Typed_number_without_the_permission_fails_alone()
    {
        var h = new Harness();

        var result = await h.Run(BatchFrom(Guid.NewGuid(), canViewAll: true, TypedNumber(), Item()));

        var typed = result.Value.Results[0];
        Assert.Equal("Failed", typed.Status);
        Assert.Equal(ManualNumberNotAllowedCode, typed.ErrorCode);
        Assert.Null(typed.MessageId);
        Assert.Equal("Accepted", result.Value.Results[1].Status);
        Assert.Equal(1, h.Provider.SendAsyncCallCount);
        Assert.Single(h.Messages.Added);
    }

    [Fact]
    public async Task Typed_number_without_the_permission_is_not_reserved_in_the_wallet()
    {
        var h = new Harness();

        await h.Run(ChargedFrom(Guid.NewGuid(), TypedNumber(), Item()));

        Assert.Equal(1, Assert.Single(h.Wallet.Reservations).Units.Sms);
        Assert.Equal(1, Assert.Single(h.Wallet.Settlements).ConsumedUnits);
    }

    [Fact]
    public async Task Batch_of_only_typed_numbers_without_the_permission_never_touches_the_wallet()
    {
        var h = new Harness();

        var result = await h.Run(ChargedFrom(Guid.NewGuid(), TypedNumber()));

        Assert.Equal(ManualNumberNotAllowedCode, Assert.Single(result.Value.Results).ErrorCode);
        Assert.Empty(h.Wallet.Reservations);
        Assert.Empty(h.Wallet.Settlements);
        Assert.Equal(0, h.Provider.SendAsyncCallCount);
    }

    // Responder en la conversación de un número escrito a mano manda el id derivado del teléfono.
    [Fact]
    public async Task Reply_to_a_typed_number_conversation_without_the_permission_fails()
    {
        var h = new Harness();
        var conversationId = SmsRecipientId.ForPhone(PhoneE164.Create(OtherPhone).Value);

        var result = await h.Run(
            BatchFrom(
                Guid.NewGuid(),
                canViewAll: true,
                new SmsSendItemDto(conversationId, OtherPhone, "hi", null, "reply", "docs")
            )
        );

        var reply = Assert.Single(result.Value.Results);
        Assert.Equal(ManualNumberNotAllowedCode, reply.ErrorCode);
        Assert.Equal(0, h.Provider.SendAsyncCallCount);
    }

    [Fact]
    public async Task Typed_number_with_the_permission_is_sent_and_charged()
    {
        var h = new Harness();
        var command = ChargedFrom(Guid.NewGuid(), TypedNumber(), Item()) with { CanSendToAnyNumber = true };

        var result = await h.Run(command);

        Assert.All(result.Value.Results, item => Assert.Equal("Accepted", item.Status));
        Assert.Equal(2, Assert.Single(h.Wallet.Reservations).Units.Sms);
        Assert.Equal(2, Assert.Single(h.Wallet.Settlements).ConsumedUnits);
    }

    [Fact]
    public void The_error_code_is_stable_for_the_crm()
    {
        Assert.Equal("sms.manualNumberNotAllowed", SmsErrors.ManualNumberNotAllowed.Code);
    }

    [Fact]
    public async Task Client_is_not_affected_by_the_missing_permission()
    {
        var h = new Harness();

        var result = await h.Run(ChargedFrom(Guid.NewGuid(), Item()));

        Assert.Equal("Accepted", Assert.Single(result.Value.Results).Status);
        Assert.Equal(1, Assert.Single(h.Wallet.Reservations).Units.Sms);
    }

    // Un servicio (campaña, aviso transaccional) no tiene usuario al que medirle el permiso.
    [Fact]
    public async Task Service_sender_still_reaches_a_typed_number()
    {
        var h = new Harness();

        var result = await h.Run(Batch(TypedNumber()));

        Assert.Equal("Accepted", Assert.Single(result.Value.Results).Status);
    }
}
