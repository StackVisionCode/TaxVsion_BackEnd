using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Application.PublicBookings;

/// <summary>
/// La página pública en el idioma del visitante. Sin anti-bot listo, sin tipo usable o sin anfitriones
/// responde <c>BookingAvailable = false</c> y el landing ofrece escribirnos.
/// </summary>
public static class GetPublicBookingPageHandler
{
    public static async Task<Result<PublicBookingPageResponse>> Handle(
        GetPublicBookingPageQuery query,
        IBookingPageRepository pages,
        PublicBookingScheduler scheduler,
        IPublicBookingBotCheck bot,
        IOptions<PublicBookingOptions> options,
        CancellationToken ct
    )
    {
        var page = await pages.FindActiveBySlugAsync(options.Value.EffectiveTenantIds, query.Slug, ct);
        if (page is null)
            return Result.Failure<PublicBookingPageResponse>(PublicBookingErrors.NotFound);

        var language = string.Equals(query.Language, "es", StringComparison.OrdinalIgnoreCase)
            ? BookingLanguage.Es
            : BookingLanguage.En;
        var type = await scheduler.TypeOfAsync(page, ct);
        var available = bot.IsReady && type is not null && page.ActiveHosts.Count > 0;

        var answers = new List<PublicHeardAboutOption>();
        foreach (var option in page.ActiveHeardAboutOptions)
            answers.Add(new PublicHeardAboutOption(option.Code, option.LabelFor(language), option.AllowsDetail));

        var hosts = new List<PublicBookingHostSummary>();
        foreach (var host in page.ActiveHosts)
            hosts.Add(new PublicBookingHostSummary(host.DisplayName));

        return Result.Success(
            new PublicBookingPageResponse(
                page.Slug.Value,
                page.Title.For(language),
                page.Description.For(language),
                type is null ? 0 : (int)type.DefaultDuration.TotalMinutes,
                page.MaxDaysAhead,
                page.RequireEmailVerification,
                available,
                available ? bot.SiteKey : null,
                page.PhoneField.ToString(),
                page.OfficeSizeField.ToString(),
                page.InterestField.ToString(),
                answers,
                page.PrivacyNotice?.For(language) ?? string.Empty,
                page.MarketingConsent?.For(language) ?? string.Empty,
                page.ConsentVersion,
                hosts
            )
        );
    }
}
