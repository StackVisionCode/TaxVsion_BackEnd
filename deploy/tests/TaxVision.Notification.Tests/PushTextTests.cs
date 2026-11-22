using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Tests;

public sealed class PushTextTests
{
    [Fact]
    public void Spanish_recipient_gets_spanish_text()
    {
        var (title, body) = PushText.For(NotificationKinds.TasksTaskOverdue, new RecipientLocale("es", null, false));

        Assert.Equal("Tarea vencida", title);
        Assert.Equal("Una tarea asignada a ti pasó su fecha de vencimiento.", body);
    }

    [Fact]
    public void Unknown_language_falls_back_to_english()
    {
        var (title, _) = PushText.For(NotificationKinds.TasksTaskOverdue, new RecipientLocale("fr", null, false));

        Assert.Equal("Task overdue", title);
    }

    [Fact]
    public void Sms_fills_the_parameters_in_the_recipient_language()
    {
        var text = SmsText.For(
            SmsText.MfaCode,
            new RecipientLocale("es", null, false),
            new Dictionary<string, string> { ["brand"] = "Acme", ["code"] = "123456" }
        );

        Assert.Equal("Acme: tu codigo es 123456. Vence en pocos minutos. No lo compartas.", text);
    }
}
