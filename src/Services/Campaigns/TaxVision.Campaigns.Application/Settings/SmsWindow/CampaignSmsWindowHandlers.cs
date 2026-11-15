using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Campaigns.Application.Scheduling;
using TaxVision.Campaigns.Domain.Settings;

namespace TaxVision.Campaigns.Application.Settings.SmsWindow;

public interface ICampaignSmsWindowRepository
{
    Task<CampaignSmsWindow?> GetAsync(Guid tenantId, CancellationToken ct = default);
    void Add(CampaignSmsWindow window);
    void Remove(CampaignSmsWindow window);
}

/// <summary>La ventana que rige para la oficina, con la de la plataforma y los límites legales para la pantalla.</summary>
public sealed record CampaignSmsWindowResponse(
    string Opens,
    string Closes,
    bool IsOfficeSetting,
    string DefaultOpens,
    string DefaultCloses,
    string LegalOpens,
    string LegalCloses
)
{
    internal static CampaignSmsWindowResponse From(CampaignSmsWindow? office, CampaignSchedulerOptions platform)
    {
        var defaultOpens = TimeOnly.FromTimeSpan(platform.SmsWindowOpens);
        var defaultCloses = TimeOnly.FromTimeSpan(platform.SmsWindowCloses);
        return new(
            Format(office?.Opens ?? defaultOpens),
            Format(office?.Closes ?? defaultCloses),
            office is not null,
            Format(defaultOpens),
            Format(defaultCloses),
            Format(CampaignSmsWindow.LegalOpens),
            Format(CampaignSmsWindow.LegalCloses)
        );
    }

    private static string Format(TimeOnly time) => time.ToString("HH:mm");
}

public sealed record GetCampaignSmsWindowQuery(Guid TenantId);

public sealed record SetCampaignSmsWindowCommand(
    Guid TenantId,
    Guid ActorUserId,
    TimeOnly Opens,
    TimeOnly Closes,
    DateTime NowUtc
);

public sealed record ResetCampaignSmsWindowCommand(Guid TenantId);

public static class GetCampaignSmsWindowHandler
{
    public static async Task<CampaignSmsWindowResponse> Handle(
        GetCampaignSmsWindowQuery query,
        ICampaignSmsWindowRepository windows,
        IOptions<CampaignSchedulerOptions> options,
        CancellationToken ct
    ) => CampaignSmsWindowResponse.From(await windows.GetAsync(query.TenantId, ct), options.Value);
}

/// <summary>P32: guarda la ventana de la oficina; fuera de 8:00–21:00 o invertida se rechaza.</summary>
public static class SetCampaignSmsWindowHandler
{
    public static async Task<Result<CampaignSmsWindowResponse>> Handle(
        SetCampaignSmsWindowCommand command,
        ICampaignSmsWindowRepository windows,
        IUnitOfWork unitOfWork,
        IOptions<CampaignSchedulerOptions> options,
        CancellationToken ct
    )
    {
        var window = await windows.GetAsync(command.TenantId, ct);
        if (window is null)
        {
            var created = CampaignSmsWindow.Set(
                command.TenantId,
                command.Opens,
                command.Closes,
                command.ActorUserId,
                command.NowUtc
            );
            if (created.IsFailure)
                return Result.Failure<CampaignSmsWindowResponse>(created.Error);
            window = created.Value;
            windows.Add(window);
        }
        else
        {
            var changed = window.Change(command.Opens, command.Closes, command.ActorUserId, command.NowUtc);
            if (changed.IsFailure)
                return Result.Failure<CampaignSmsWindowResponse>(changed.Error);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(CampaignSmsWindowResponse.From(window, options.Value));
    }
}

/// <summary>Vuelve a la ventana de la plataforma.</summary>
public static class ResetCampaignSmsWindowHandler
{
    public static async Task<CampaignSmsWindowResponse> Handle(
        ResetCampaignSmsWindowCommand command,
        ICampaignSmsWindowRepository windows,
        IUnitOfWork unitOfWork,
        IOptions<CampaignSchedulerOptions> options,
        CancellationToken ct
    )
    {
        if (await windows.GetAsync(command.TenantId, ct) is { } window)
            windows.Remove(window);
        await unitOfWork.SaveChangesAsync(ct);
        return CampaignSmsWindowResponse.From(null, options.Value);
    }
}
