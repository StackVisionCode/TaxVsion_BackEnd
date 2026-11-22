using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Tests.Domain;

public sealed class NotificationCopyCatalogTests
{
    [Fact]
    public void Render_fills_the_parameters_in_each_language()
    {
        var parameters = new Dictionary<string, string> { ["providerCode"] = "Google" };

        var english = NotificationCopyCatalog.Render("connectors.oauth.refresh_failed", "en", parameters);
        var spanish = NotificationCopyCatalog.Render("connectors.oauth.refresh_failed", "es", parameters);

        Assert.Equal(
            new NotificationCopyRendering("Reconnect Google", "Access to the Google mailbox could not be renewed."),
            english
        );
        Assert.Equal("No se pudo renovar el acceso al buzón de Google.", spanish!.Body);
    }

    [Fact]
    public void Render_uses_the_plural_text_when_count_is_above_one()
    {
        var one = NotificationCopyCatalog.Render(
            "communication.call.missed",
            "en",
            new Dictionary<string, string> { ["count"] = "1" }
        );
        var many = NotificationCopyCatalog.Render(
            "communication.call.missed",
            "es",
            new Dictionary<string, string> { ["count"] = "3" }
        );

        Assert.Equal("Missed call", one!.Title);
        Assert.Equal("3 llamadas perdidas", many!.Title);
    }

    [Fact]
    public void Unknown_language_falls_back_to_english_and_regional_codes_are_accepted()
    {
        Assert.Equal("Task overdue", NotificationCopyCatalog.Render("tasks.task_overdue", "fr", null)!.Title);
        Assert.Equal("Tarea vencida", NotificationCopyCatalog.Render("tasks.task_overdue", "es-MX", null)!.Title);
    }

    [Fact]
    public void Server_text_kinds_keep_the_server_wording_in_english()
    {
        Assert.True(NotificationCopyCatalog.IsServerText("wallet.low_balance"));
        Assert.Null(NotificationCopyCatalog.Render("wallet.low_balance", "en", null));
        Assert.NotNull(NotificationCopyCatalog.Render("wallet.low_balance", "es", null));
    }

    [Fact]
    public void An_aliased_kind_resolves_through_its_alias()
    {
        Assert.True(NotificationCopyCatalog.TryGet("support.ticket.opened", "es", out var text));
        Assert.Equal("Nuevo ticket de soporte: {subject}", text.Title);
    }

    [Fact]
    public void Date_parameters_render_the_raw_value_and_missing_parameters_render_empty()
    {
        var wisp = NotificationCopyCatalog.Render(
            "tenant.wisp_review_due",
            "en",
            new Dictionary<string, string> { ["dueOn"] = "2026-03-01" }
        );
        var meeting = NotificationCopyCatalog.Render("communication.meeting.cancelled", "en", null);

        Assert.Equal("Security plan review due 2026-03-01", wisp!.Title);
        Assert.Equal(" was canceled", meeting!.Title);
    }

    [Theory]
    [InlineData("en", "1 days")]
    [InlineData("es", "1 días")]
    public void Discount_ending_reads_well_with_one_day_left(string language, string wrong)
    {
        var text = NotificationCopyCatalog.Render(
            "subscription.discount_ending",
            language,
            new Dictionary<string, string> { ["discountLabel"] = "20%", ["daysLeft"] = "1" }
        );

        Assert.DoesNotContain(wrong, text!.Body);
        Assert.Contains("1", text.Body);
    }
}
