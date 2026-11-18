using BuildingBlocks.Persistence;
using BuildingBlocks.Platform;
using BuildingBlocks.Results;
using TaxVision.Wallet.Application.Settings.Abstractions;
using TaxVision.Wallet.Domain.Settings;

namespace TaxVision.Wallet.Application.Settings.Commands;

/// <summary><c>PUT /wallet/settings/low-balance</c>: 0 a $1,000 (0 = sin aviso).</summary>
public sealed record ChangeLowBalanceThresholdCommand(Guid TenantId, Guid UserId, long ThresholdMicros);

/// <summary>
/// <c>PUT /wallet/settings/auto-recharge</c>. Todo cambio (monto, umbral o método) exige aceptar de nuevo la versión
/// vigente del texto; el servidor arma el texto final, nunca lo toma del cliente.
/// </summary>
public sealed record EnableAutoRechargeCommand(
    Guid TenantId,
    Guid UserId,
    long AmountCents,
    long ThresholdMicros,
    string? Provider,
    string? MethodReference,
    string? MethodLabel,
    bool ConsentAccepted,
    string? ConsentTextVersion,
    string? IpAddress
);

/// <summary><c>DELETE /wallet/settings/auto-recharge</c>: apaga (queda en la historia).</summary>
public sealed record DisableAutoRechargeCommand(Guid TenantId, Guid UserId);

public static class ChangeLowBalanceThresholdHandler
{
    public static async Task<Result<WalletSettingsView>> Handle(
        ChangeLowBalanceThresholdCommand command,
        IWalletSettingsRepository repository,
        IOfficeTimeZone officeTimeZone,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var (settings, isNew) = await WalletSettingsLoader.GetOrDefaultAsync(repository, command.TenantId, ct);
        var change = settings.ChangeLowBalanceThreshold(command.ThresholdMicros, command.UserId, DateTime.UtcNow);
        if (change.IsFailure)
            return Result.Failure<WalletSettingsView>(change.Error);

        await WalletSettingsLoader.SaveAsync(repository, unitOfWork, settings, isNew, change.Value, ct);
        return Result.Success(await WalletSettingsViews.BuildAsync(settings, officeTimeZone, ct));
    }
}

public static class EnableAutoRechargeHandler
{
    public static async Task<Result<WalletSettingsView>> Handle(
        EnableAutoRechargeCommand command,
        IWalletSettingsRepository repository,
        IOfficeTimeZone officeTimeZone,
        IPlatformSettings platform,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (!command.ConsentAccepted)
            return Result.Failure<WalletSettingsView>(WalletSettingsErrors.ConsentRequired);
        if (command.ConsentTextVersion != AutoRechargeConsentTemplate.Version)
            return Result.Failure<WalletSettingsView>(WalletSettingsErrors.ConsentVersionConflict);
        var method = AutoRechargeMethod.Create(command.Provider, command.MethodReference, command.MethodLabel);
        if (method.IsFailure)
            return Result.Failure<WalletSettingsView>(method.Error);

        var text = AutoRechargeConsentTemplate.Render(
            platform.Current.BrandName,
            command.AmountCents,
            method.Value.Label,
            command.ThresholdMicros
        );
        var consent = new ConsentGrant(
            command.UserId,
            DateTime.UtcNow,
            command.IpAddress,
            command.ConsentTextVersion,
            text
        );

        var (settings, isNew) = await WalletSettingsLoader.GetOrDefaultAsync(repository, command.TenantId, ct);
        var change = settings.EnableAutoRecharge(command.AmountCents, command.ThresholdMicros, method.Value, consent);
        if (change.IsFailure)
            return Result.Failure<WalletSettingsView>(change.Error);

        await WalletSettingsLoader.SaveAsync(repository, unitOfWork, settings, isNew, change.Value, ct);
        return Result.Success(await WalletSettingsViews.BuildAsync(settings, officeTimeZone, ct));
    }
}

public static class DisableAutoRechargeHandler
{
    public static async Task<Result<WalletSettingsView>> Handle(
        DisableAutoRechargeCommand command,
        IWalletSettingsRepository repository,
        IOfficeTimeZone officeTimeZone,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var settings = await repository.GetAsync(command.TenantId, ct);
        if (settings is null)
            return Result.Success(
                await WalletSettingsViews.BuildAsync(WalletSettings.CreateDefault(command.TenantId), officeTimeZone, ct)
            );

        var change = settings.DisableAutoRecharge(command.UserId, DateTime.UtcNow);
        if (change is not null)
            await WalletSettingsLoader.SaveAsync(repository, unitOfWork, settings, false, change, ct);
        return Result.Success(await WalletSettingsViews.BuildAsync(settings, officeTimeZone, ct));
    }
}

/// <summary>Carga los ajustes (o los de fábrica, sin guardarlos) y guarda el ajuste junto con su historia.</summary>
internal static class WalletSettingsLoader
{
    public static async Task<(WalletSettings Settings, bool IsNew)> GetOrDefaultAsync(
        IWalletSettingsRepository repository,
        Guid tenantId,
        CancellationToken ct
    )
    {
        var settings = await repository.GetAsync(tenantId, ct);
        return settings is null ? (WalletSettings.CreateDefault(tenantId), true) : (settings, false);
    }

    public static async Task SaveAsync(
        IWalletSettingsRepository repository,
        IUnitOfWork unitOfWork,
        WalletSettings settings,
        bool isNew,
        WalletSettingsChange change,
        CancellationToken ct
    )
    {
        if (isNew)
            await repository.AddAsync(settings, ct);
        await repository.AddChangeAsync(change, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
