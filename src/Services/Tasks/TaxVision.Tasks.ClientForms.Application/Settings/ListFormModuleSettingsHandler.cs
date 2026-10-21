using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;

namespace TaxVision.Tasks.ClientForms.Application.Settings;

public sealed record ListFormModuleSettingsQuery;

public static class ListFormModuleSettingsHandler
{
    public static async Task<IReadOnlyList<FormModuleSettingsResponse>> Handle(
        ListFormModuleSettingsQuery _,
        IFormModuleSettingsRepository repository,
        CancellationToken ct
    ) =>
        (await repository.ListAsync(ct)).Select(FormModuleSettingsResponse.From).ToArray();
}
