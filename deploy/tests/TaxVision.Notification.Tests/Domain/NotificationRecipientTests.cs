using TaxVision.Notification.Domain.Emailing.Sending;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Tests.Domain;

/// <summary>NOT-16. Los formatos son los que ya hay en la columna: cambiarlos rompe la deduplicación por destinatario.</summary>
public sealed class NotificationRecipientTests
{
    private static readonly Guid User = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Device = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public void ForEmail_keeps_the_address_as_it_came_without_outer_spaces() =>
        Assert.Equal("Ana@Mail.COM", NotificationRecipient.ForEmail("  Ana@Mail.COM ").Value.Value);

    [Fact]
    public void ForPhone_keeps_the_number_as_it_came_without_outer_spaces() =>
        Assert.Equal("+1 (829) 592-4420", NotificationRecipient.ForPhone(" +1 (829) 592-4420 ").Value.Value);

    [Fact]
    public void ForUser_uses_the_stored_format() =>
        Assert.Equal("user:11111111222233334444555555555555", NotificationRecipient.ForUser(User).Value);

    [Fact]
    public void ForDevice_uses_the_stored_format() =>
        Assert.Equal(
            "11111111222233334444555555555555:aaaaaaaabbbbccccddddeeeeeeeeeeee",
            NotificationRecipient.ForDevice(User, Device).Value
        );

    [Fact]
    public void ForPushTarget_uses_the_stored_format() =>
        Assert.Equal("11111111-2222-3333-4444-555555555555", NotificationRecipient.ForPushTarget(User).Value);

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("@mail.com")]
    [InlineData("ana@")]
    public void ForEmail_rejects_an_invalid_address(string? address)
    {
        var result = NotificationRecipient.ForEmail(address);

        Assert.True(result.IsFailure);
        Assert.Equal("Notification.Recipient", result.Error.Code);
    }

    [Fact]
    public void ForEmail_rejects_an_address_longer_than_the_column() =>
        Assert.True(NotificationRecipient.ForEmail(new string('a', 320) + "@mail.com").IsFailure);

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void ForPhone_requires_a_value(string? phone) =>
        Assert.True(NotificationRecipient.ForPhone(phone).IsFailure);

    [Fact]
    public void ForGuest_falls_back_to_unknown() => Assert.Equal("unknown", NotificationRecipient.ForGuest(null).Value);

    [Theory]
    [InlineData("@mail.com")]
    [InlineData("ana@")]
    public void Outbound_message_rejects_an_address_that_only_has_an_at_sign(string address)
    {
        var result = OutboundEmailMessage.Create(
            Guid.NewGuid(),
            "Subject",
            "<p>Body</p>",
            "Body",
            EmailPriority.Normal,
            [(address, EmailRecipientKind.To, null)],
            "[]",
            templateId: null,
            templateVersionId: null,
            campaignId: null,
            correlationId: null
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Email.Recipients", result.Error.Code);
    }
}
