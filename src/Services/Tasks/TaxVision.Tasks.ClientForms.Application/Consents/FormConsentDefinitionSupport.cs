using System.Text.Json;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Validation;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Consents;

namespace TaxVision.Tasks.ClientForms.Application.Consents;

internal sealed record FormConsentDefinition(
    string ComponentId,
    string NoticeKey,
    int NoticeVersionNumber,
    string Mode,
    string? Timing,
    string? AcceptanceText,
    bool Blocking
);

internal static class FormConsentDefinitionSupport
{
    internal static IReadOnlyList<FormConsentDefinition> Read(FormDefinition definition) =>
        definition
            .Pages.SelectMany(page => page.Sections)
            .SelectMany(section => section.Components)
            .Where(component => component.Type == "consent")
            .Select(component => new FormConsentDefinition(
                component.Id,
                String(component.Props, "noticeKey") ?? string.Empty,
                Integer(component.Props, "noticeVersionNumber"),
                String(component.Props, "mode") ?? string.Empty,
                String(component.Props, "timing"),
                String(component.Props, "acceptanceText"),
                Boolean(component.Props, "blocking")
            ))
            .ToArray();

    internal static async Task<IReadOnlyList<ValidationIssue>> ValidateForPublishAsync(
        Guid tenantId,
        FormDefinition definition,
        IConsentNoticeRepository notices,
        CancellationToken ct
    )
    {
        var issues = new List<ValidationIssue>();
        var definitions = Read(definition);
        foreach (var consent in definitions)
        {
            var notice = await notices.GetByKeyAsync(tenantId, consent.NoticeKey, ct);
            var version = notice?.Versions.SingleOrDefault(item => item.VersionNumber == consent.NoticeVersionNumber);
            if (notice is null || notice.Status != ConsentNoticeStatus.Active || version is null)
                issues.Add(
                    new ValidationIssue(
                        "consent.notice_missing",
                        $"/components/{consent.ComponentId}/props/noticeVersionNumber",
                        consent.ComponentId,
                        null,
                        "The published consent notice version was not found."
                    )
                );
        }

        issues.AddRange(ConditionallyHiddenGates(definition, definitions));
        if (
            definition.Settings.CollectionBasis == FormCollectionBasis.Consent
            && !IsBeforeCollectionGateFirst(definition, definitions)
        )
            issues.Add(
                new ValidationIssue(
                    "consent.must_precede_personal_fields",
                    "/pages/0/sections/0/components/0",
                    null,
                    null,
                    "A blocking before-collection consent must be the first component."
                )
            );
        return issues;
    }

    // El gate exige la aceptación aunque el consent esté oculto, y oculto no se puede aceptar.
    private static IEnumerable<ValidationIssue> ConditionallyHiddenGates(
        FormDefinition definition,
        IReadOnlyList<FormConsentDefinition> consents
    )
    {
        var gates = consents
            .Where(item => item.Blocking && item.Mode == "acceptance" && item.Timing == "beforeCollection")
            .Select(item => item.ComponentId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var page in definition.Pages)
        foreach (var section in page.Sections)
        foreach (var component in section.Components.Where(item => gates.Contains(item.Id)))
        {
            if (page.VisibleWhen is null && section.VisibleWhen is null && component.VisibleWhen is null)
                continue;
            yield return new ValidationIssue(
                "consent.before_collection_must_be_visible",
                $"/components/{component.Id}/visibleWhen",
                component.Id,
                null,
                "A blocking before-collection consent cannot depend on visibility rules."
            );
        }
    }

    private static bool IsBeforeCollectionGateFirst(
        FormDefinition definition,
        IReadOnlyList<FormConsentDefinition> consents
    )
    {
        var first = definition.Pages.FirstOrDefault()?.Sections.FirstOrDefault()?.Components.FirstOrDefault();
        if (first is null)
            return false;
        return consents.Any(item =>
            item.ComponentId == first.Id
            && item.Mode == "acceptance"
            && item.Timing == "beforeCollection"
            && item.Blocking
        );
    }

    private static string? String(JsonElement props, string name) =>
        props.ValueKind == JsonValueKind.Object
        && props.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int Integer(JsonElement props, string name) =>
        props.ValueKind == JsonValueKind.Object
        && props.TryGetProperty(name, out var value)
        && value.TryGetInt32(out var result)
            ? result
            : 0;

    private static bool Boolean(JsonElement props, string name) =>
        props.ValueKind == JsonValueKind.Object
        && props.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
        && value.GetBoolean();
}
