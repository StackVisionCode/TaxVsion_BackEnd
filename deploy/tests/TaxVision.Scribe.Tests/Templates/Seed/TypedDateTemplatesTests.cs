using TaxVision.Scribe.Application.Templates.Seed;
using TaxVision.Scribe.Domain.Templates;

namespace TaxVision.Scribe.Tests.Templates.Seed;

/// <summary>
/// Fase 5: las plantillas con fechas reciben instantes tipados y Scribe los imprime en la zona de quien lee. Ya no
/// escriben "UTC" a mano, declaran la variable como fecha y suben su versión para que el seeder las republique.
/// </summary>
public sealed class TypedDateTemplatesTests
{
    private const int TypedDatesVersion = 32;

    [Theory]
    [InlineData("auth.invitation", "expires_at")]
    [InlineData("auth.password_reset", "expires_at")]
    [InlineData("auth.email_change", "expires_at")]
    [InlineData("onboarding.registration_ready", "paid_at")]
    [InlineData("sig.invitation.v1", "expires_at")]
    [InlineData("sig.reminder.v1", "expires_at")]
    [InlineData("sig.completed.v1", "completed_at")]
    [InlineData("sig.completed.v1", "expires_at")]
    [InlineData("sig.partial_copy.v1", "signed_at")]
    [InlineData("sig.partial_copy.v1", "expires_at")]
    [InlineData("sig.certificate.v1", "completed_at")]
    [InlineData("sig.expired.v1", "expired_at")]
    [InlineData("sig.verification-challenge.v1", "expires_at")]
    [InlineData("storage.share_invited.v1", "expires_at")]
    [InlineData("subscription.access_ending", "access_ends_date")]
    [InlineData("subscription.cancellation_scheduled", "access_ends_date")]
    [InlineData("subscription.version_change_scheduled", "change_date")]
    [InlineData("subscription.billing_cycle_changed", "date_charged")]
    [InlineData("subscription.billing_cycle_changed", "next_charge_date")]
    [InlineData("subscription.renewal_upcoming", "renewal_date")]
    [InlineData("subscription.recurring_charge", "next_charge_date")]
    [InlineData("subscription.recurring_charge_failed", "next_retry_date")]
    [InlineData("communication.meeting.invitation", "expires_at")]
    [InlineData("communication.meeting.invitation", "start_local")]
    [InlineData("communication.meeting.cancelled.v1", "start_local")]
    [InlineData("communication.meeting.rescheduled.v1", "previous_local")]
    [InlineData("communication.meeting.rescheduled.v1", "new_local")]
    [InlineData("communication.meeting.reminder.v1", "start_local")]
    [InlineData("calendar.appointment_scheduled.v1", "start_local")]
    [InlineData("calendar.appointment_attendee_added.v1", "start_local")]
    [InlineData("calendar.appointment_rescheduled.v1", "previous_local")]
    [InlineData("calendar.appointment_rescheduled.v1", "new_local")]
    [InlineData("calendar.appointment_updated.v1", "start_local")]
    [InlineData("calendar.booking_declined.v1", "start_local")]
    [InlineData("calendar.appointment_cancelled.v1", "start_local")]
    [InlineData("task.waiting_on_client.v1", "client_due_at_utc")]
    [InlineData("task.client_request_created.v1", "due_at_utc")]
    [InlineData("task.form_request_assigned.v1", "due_date")]
    [InlineData("task.form_request_reminder.v1", "due_date")]
    [InlineData("task.form_access_link.v1", "expires_at")]
    [InlineData("task.form_access_code.v1", "expires_at")]
    [InlineData("billing.invoice_sent", "due_date")]
    public void A_template_with_a_typed_date_does_not_write_utc_and_is_republished(string templateKey, string variable)
    {
        var seed = NotificationTemplateSeedSource.All.Single(s => s.TemplateKey == templateKey);

        Assert.DoesNotContain("}} UTC", seed.Html);
        Assert.Contains("{{ " + variable, seed.Html);
        Assert.Equal(VariableType.Date, seed.Variables.Single(v => v.Name == variable).Type);
        Assert.True(
            seed.ContentVersion >= TypedDatesVersion,
            $"{templateKey}: ContentVersion {seed.ContentVersion} < {TypedDatesVersion}"
        );
    }

    /// <summary>La hora tipada ya trae su zona rotulada ("New York time, GMT-5"): el id IANA al lado sobra.</summary>
    [Theory]
    [InlineData("communication.meeting.invitation")]
    [InlineData("communication.meeting.cancelled.v1")]
    [InlineData("communication.meeting.rescheduled.v1")]
    [InlineData("communication.meeting.reminder.v1")]
    [InlineData("calendar.appointment_scheduled.v1")]
    [InlineData("calendar.appointment_attendee_added.v1")]
    [InlineData("calendar.appointment_rescheduled.v1")]
    [InlineData("calendar.appointment_updated.v1")]
    [InlineData("calendar.booking_declined.v1")]
    public void A_typed_start_does_not_repeat_the_zone_id_beside_it(string templateKey)
    {
        var seed = NotificationTemplateSeedSource.All.Single(s => s.TemplateKey == templateKey);
        Assert.DoesNotContain("{{ time_zone }}", seed.Html);
    }

    /// <summary>Un vencimiento civil incluye ese día (P4): "before Nov 15" decía lo contrario.</summary>
    [Theory]
    [InlineData("task.waiting_on_client.v1", "client_due_at_utc")]
    [InlineData("task.client_request_created.v1", "due_at_utc")]
    public void A_civil_due_date_is_inclusive_so_the_copy_says_by(string templateKey, string variable)
    {
        var seed = NotificationTemplateSeedSource.All.Single(s => s.TemplateKey == templateKey);
        Assert.Contains("by <strong style=\"color:#23384B;\">{{ " + variable + " }}</strong>", seed.Html);
        Assert.DoesNotContain("before <strong", seed.Html);
    }
}
