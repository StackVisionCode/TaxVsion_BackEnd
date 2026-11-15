using System.Text.RegularExpressions;
using Fluid;
using TaxVision.Scribe.Application.Templates.Seed;

namespace TaxVision.Scribe.Tests.Templates.Seed;

/// <summary>
/// Plantillas de la demo pública (F6): declaran todo lo que usan (Scribe rechaza publicar si falta una), salen en
/// el idioma del visitante y ocultan las filas vacías. El renderer convierte null en "" y Fluid toma "" por verdadero.
/// </summary>
public sealed class PublicDemoTemplateSeedTests
{
    private static readonly FluidParser Parser = new();

    public static TheoryData<string> Keys =>
        [
            "demo.verification_code.v1",
            "demo.confirmed.v1",
            "demo.new_booking.v1",
            "demo.rescheduled.v1",
            "demo.cancelled.v1",
            "demo.reminder.v1",
            "demo.already_booked.v1",
        ];

    [Theory]
    [MemberData(nameof(Keys))]
    public void Each_template_declares_every_variable_it_uses_and_carries_its_version(string key)
    {
        var seed = Seed(key);

        var declared = seed.Variables.Select(v => v.Name).ToHashSet();
        var used = Regex
            .Matches(seed.Html + seed.Subject, @"(?:\{\{\s*|\{%\s*(?:if|unless|elsif)\s+)([a-z_][a-z0-9_]*)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();
        Assert.Empty(used.Except(declared));
        Assert.Equal(key, seed.TemplateKey);
        // v31: confirmación con "Add to calendar" como acción principal y el aviso "ya tienes una demo" (DEMO-11).
        Assert.Equal(key is "demo.confirmed.v1" or "demo.already_booked.v1" ? 31 : 30, seed.ContentVersion);
        Assert.Equal("system-base", seed.LayoutKey);
        Assert.Contains(NotificationTemplateSeedSource.VariablesWithPreheader(seed), v => v.Name == "preheader");
        Assert.DoesNotContain("TaxVision", seed.Html + seed.Subject);
    }

    [Fact]
    public void Verification_code_shows_the_code_in_the_visitor_language()
    {
        var seed = Seed("demo.verification_code.v1");
        var vars = Visitor("Es");
        vars["code"] = "482913";
        vars["expires_minutes"] = 10;

        var (subject, html) = Render(seed, vars);

        Assert.Equal("Tu código de verificación de TaxOffix: 482913", subject);
        Assert.Contains("482913", html);
        Assert.Contains("Vence en 10 minutos", html);
        Assert.DoesNotContain("expires", html);
    }

    [Theory]
    [InlineData("En", "Your TaxOffix demo is confirmed for Thursday, November 5, 2026", "Reschedule or cancel here")]
    [InlineData(
        "Es",
        "Tu demo de TaxOffix quedó confirmada para el jueves 5 de noviembre de 2026",
        "Cámbiala o cancélala aquí"
    )]
    public void Confirmed_speaks_the_visitor_language_and_links_to_manage(
        string language,
        string subject,
        string manage
    )
    {
        var vars = Visitor(language);
        vars["date_local"] = language == "Es" ? "jueves 5 de noviembre de 2026" : "Thursday, November 5, 2026";

        var (renderedSubject, html) = Render(Seed("demo.confirmed.v1"), vars);

        Assert.Equal(subject, renderedSubject);
        Assert.Contains(manage, html);
        Assert.Contains("href=\"https://taxoffix.io/demo/manage#token=abc\"", html);
        Assert.Contains("href=\"https://meet.google.com/abc-defg-hij\"", html);
        Assert.Contains("href=\"https://calendar.google.com/x\"", html);
        Assert.Contains("(New York time)", html);
    }

    /// <summary>
    /// DEMO-11: el día de la confirmación la acción es agendarla, no entrar. "Add to calendar" es el botón
    /// principal y el enlace de la reunión queda como texto.
    /// </summary>
    [Fact]
    public void Confirmed_makes_adding_to_the_calendar_the_main_action()
    {
        var (_, html) = Render(Seed("demo.confirmed.v1"), Visitor("En"));

        Assert.Matches("bgcolor=\"#1E466B\"[^>]*><a href=\"https://calendar\\.google\\.com/x\"", html);
        Assert.DoesNotMatch("bgcolor=\"#1E466B\"[^>]*><a href=\"https://meet\\.google\\.com", html);
        Assert.Contains("Add to Google Calendar", html);
        Assert.Contains("Join the meeting", html);
    }

    [Theory]
    [InlineData("En", "You already have a TaxOffix demo on Thursday, November 5, 2026", "Reschedule or cancel here")]
    [InlineData("Es", "Ya tienes una demo de TaxOffix el jueves 5 de noviembre de 2026", "Cámbiala o cancélala aquí")]
    public void Already_booked_reminds_the_demo_and_offers_the_new_link(string language, string subject, string manage)
    {
        var vars = Visitor(language);
        vars["date_local"] = language == "Es" ? "jueves 5 de noviembre de 2026" : "Thursday, November 5, 2026";

        var (renderedSubject, html) = Render(Seed("demo.already_booked.v1"), vars);

        Assert.Equal(subject, renderedSubject);
        Assert.Contains("href=\"https://taxoffix.io/demo/manage#token=abc\"", html);
        Assert.Contains(manage, html);
        Assert.Contains("María Pérez", html);
        Assert.Contains("(New York time)", html);
    }

    [Fact]
    public void New_booking_shows_the_prospect_and_hides_empty_rows()
    {
        var vars = Host();
        vars["office_size"] = "";
        vars["phone"] = "";
        vars["heard_about_detail"] = "";
        vars["utm_source"] = "";
        vars["referrer_domain"] = "";

        var (subject, html) = Render(Seed("demo.new_booking.v1"), vars);

        Assert.Equal("New demo booked: Acme Tax · Thursday, November 5, 2026 2:00 PM", subject);
        Assert.Contains("ana@acme.com", html);
        Assert.Contains("Their time: 1:00 PM", html);
        Assert.Contains("Wants to see", html);
        Assert.DoesNotContain("Office size", html);
        Assert.DoesNotContain("Phone", html);
        Assert.DoesNotContain(" — ", html);
        Assert.DoesNotContain("from ", html);
        Assert.Contains("Product emails:</span> <strong style=\"color:#23384B;\">No", html);
    }

    [Fact]
    public void New_booking_shows_where_the_prospect_came_from()
    {
        var vars = Host();
        vars["heard_about_detail"] = "A friend at NATP";
        vars["utm_source"] = "google";
        vars["utm_campaign"] = "fall";
        vars["referrer_domain"] = "bing.com";
        vars["marketing_opt_in"] = true;

        var (_, html) = Render(Seed("demo.new_booking.v1"), vars);

        Assert.Contains("Referral — A friend at NATP", html);
        Assert.Contains("hero · google/fall · from bing.com", html);
        Assert.Contains("Product emails:</span> <strong style=\"color:#23384B;\">Yes", html);
    }

    [Fact]
    public void Rescheduled_tells_the_visitor_who_moved_it_and_why_in_spanish()
    {
        var vars = Visitor("Es");
        vars["previous_date_local"] = "miércoles 4 de noviembre de 2026";
        vars["previous_time_local"] = "10:00";
        vars["changed_by"] = "Staff";
        vars["reason"] = "Se enfermó el anfitrión";

        var (subject, html) = Render(Seed("demo.rescheduled.v1"), vars);

        Assert.StartsWith("Tu demo de TaxOffix cambió al", subject);
        Assert.Contains("<s>miércoles 4 de noviembre de 2026 · 10:00</s>", html);
        Assert.Contains("Lo cambió nuestro equipo: Se enfermó el anfitrión", html);
        Assert.Contains("Cámbiala o cancélala aquí", html);
        Assert.DoesNotContain("Changed by", html);
    }

    [Fact]
    public void Rescheduled_for_the_host_has_no_manage_link()
    {
        var vars = Host();
        vars["previous_date_local"] = "Wednesday, November 4, 2026";
        vars["previous_time_local"] = "10:00 AM";
        vars["changed_by"] = "Visitor";
        vars["reason"] = "";

        var (subject, html) = Render(Seed("demo.rescheduled.v1"), vars);

        Assert.StartsWith("Demo moved: Ana Ruiz ·", subject);
        Assert.Contains("Changed by the visitor.", html);
        Assert.DoesNotContain("Reschedule or cancel", html);
    }

    [Theory]
    [InlineData(false, "Your TaxOffix demo was canceled", "Book another time")]
    [InlineData(true, "Demo canceled: Ana Ruiz · Thursday, November 5, 2026", "Canceled by the visitor.")]
    public void Cancelled_offers_to_book_again_only_to_the_visitor(bool isHost, string subject, string fragment)
    {
        var vars = isHost ? Host() : Visitor("En");
        vars["cancelled_by"] = "Visitor";
        vars["reason"] = "";
        vars["book_again_url"] = isHost ? "" : "https://taxoffix.io/demo";

        var (renderedSubject, html) = Render(Seed("demo.cancelled.v1"), vars);

        Assert.Equal(subject, renderedSubject);
        Assert.Contains(fragment, html);
        if (isHost)
            Assert.DoesNotContain("Book another time", html);
    }

    [Fact]
    public void Reminder_names_the_day_and_time_instead_of_tomorrow()
    {
        var (subject, html) = Render(Seed("demo.reminder.v1"), Visitor("En"));

        Assert.Equal("Reminder: your TaxOffix demo is on Thursday, November 5, 2026 at 2:00 PM", subject);
        Assert.DoesNotContain("tomorrow", subject + html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Join the meeting", html);
    }

    private static Dictionary<string, object?> Common(string language, bool isHost) =>
        new()
        {
            ["language"] = language,
            ["is_host"] = isHost,
            ["product_name"] = "TaxOffix",
            ["page_title"] = "Book a live demo",
            ["host_name"] = "María Pérez",
            ["date_local"] = "Thursday, November 5, 2026",
            ["time_local"] = "2:00 PM",
            ["time_zone_label"] = "New York time",
            ["duration_minutes"] = 30,
            ["meeting_url"] = "https://meet.google.com/abc-defg-hij",
        };

    private static Dictionary<string, object?> Visitor(string language)
    {
        var vars = Common(language, isHost: false);
        vars["first_name"] = "Ana";
        vars["manage_url"] = "https://taxoffix.io/demo/manage#token=abc";
        vars["google_calendar_url"] = "https://calendar.google.com/x";
        vars["outlook_calendar_url"] = "https://outlook.live.com/x";
        return vars;
    }

    private static Dictionary<string, object?> Host()
    {
        var vars = Common("En", isHost: true);
        vars["first_name"] = "María";
        vars["visitor_name"] = "Ana Ruiz";
        vars["visitor_email"] = "ana@acme.com";
        vars["visitor_time_local"] = "1:00 PM";
        vars["company"] = "Acme Tax";
        vars["office_size"] = "2-5";
        vars["phone"] = "+1 555 0100";
        vars["interest"] = "Client portal";
        vars["heard_about"] = "Referral";
        vars["heard_about_detail"] = "";
        vars["entry_source"] = "hero";
        vars["utm_source"] = "";
        vars["utm_campaign"] = "";
        vars["referrer_domain"] = "";
        vars["marketing_opt_in"] = false;
        return vars;
    }

    private static (string Subject, string Html) Render(NotificationTemplateSeed seed, Dictionary<string, object?> vars)
    {
        var context = new TemplateContext();
        foreach (var (key, value) in vars)
            context.SetValue(key, value ?? string.Empty);
        return (Parser.Parse(seed.Subject).Render(context), Parser.Parse(seed.Html).Render(context));
    }

    private static NotificationTemplateSeed Seed(string key) =>
        Assert.Single(NotificationTemplateSeedSource.All, item => item.EventKey == key);
}
