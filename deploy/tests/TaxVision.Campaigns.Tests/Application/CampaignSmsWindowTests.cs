using Microsoft.Extensions.Options;
using TaxVision.Campaigns.Application.Scheduling;
using TaxVision.Campaigns.Application.Settings.SmsWindow;
using TaxVision.Campaigns.Domain.Settings;
using TaxVision.Campaigns.Tests.Fakes;
using Xunit;

namespace TaxVision.Campaigns.Tests.Application;

/// <summary>
/// P32: cada oficina puede acortar su ventana de envío de SMS de campañas, siempre dentro de 08:00–21:00 (TCPA). Sin
/// ajuste propio rige la de la plataforma.
/// </summary>
public sealed class CampaignSmsWindowTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 15, 15, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(7, 30, 18, 0)] // abre antes de las 8:00
    [InlineData(9, 0, 21, 30)] // cierra después de las 21:00
    [InlineData(18, 0, 9, 0)] // abre después de cerrar
    [InlineData(10, 0, 10, 0)] // ventana vacía
    public void Hours_outside_the_legal_window_or_inverted_are_rejected(int openH, int openM, int closeH, int closeM)
    {
        var window = CampaignSmsWindow.Set(
            Tenant,
            new TimeOnly(openH, openM),
            new TimeOnly(closeH, closeM),
            Admin,
            Now
        );

        Assert.True(window.IsFailure);
    }

    [Fact]
    public void A_narrower_window_inside_the_legal_hours_is_accepted()
    {
        var window = CampaignSmsWindow.Set(Tenant, new TimeOnly(9, 0), new TimeOnly(18, 0), Admin, Now);

        Assert.True(window.IsSuccess);
        Assert.Equal((new TimeOnly(9, 0), new TimeOnly(18, 0)), (window.Value.Opens, window.Value.Closes));
    }

    [Fact]
    public async Task Without_an_office_setting_the_platform_window_applies_and_a_reset_returns_to_it()
    {
        var store = new FakeCampaignSmsWindowRepository();
        var unitOfWork = new FakeUnitOfWork();
        var options = Options.Create(new CampaignSchedulerOptions());

        var initial = await GetCampaignSmsWindowHandler.Handle(
            new GetCampaignSmsWindowQuery(Tenant),
            store,
            options,
            CancellationToken.None
        );
        Assert.Equal(("08:00", "21:00", false), (initial.Opens, initial.Closes, initial.IsOfficeSetting));

        var saved = await SetCampaignSmsWindowHandler.Handle(
            new SetCampaignSmsWindowCommand(Tenant, Admin, new TimeOnly(9, 0), new TimeOnly(18, 0), Now),
            store,
            unitOfWork,
            options,
            CancellationToken.None
        );
        Assert.Equal(("09:00", "18:00", true), (saved.Value.Opens, saved.Value.Closes, saved.Value.IsOfficeSetting));

        var reset = await ResetCampaignSmsWindowHandler.Handle(
            new ResetCampaignSmsWindowCommand(Tenant),
            store,
            unitOfWork,
            options,
            CancellationToken.None
        );
        Assert.Equal(("08:00", "21:00", false), (reset.Opens, reset.Closes, reset.IsOfficeSetting));
        Assert.Equal(2, unitOfWork.SaveChangesCallCount);
    }
}
