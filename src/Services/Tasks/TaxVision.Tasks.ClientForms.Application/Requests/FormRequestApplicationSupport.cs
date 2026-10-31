using System.Text.Json;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Validation;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests;

internal static class FormRequestApplicationSupport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static bool Matches(ParticipantContext context, FormRequest request)
    {
        if (context.TenantId != request.TenantId)
            return false;
        return context.Kind switch
        {
            FormParticipantKind.Customer => context.CustomerId.HasValue
                && request.EffectiveCustomerId == context.CustomerId,
            FormParticipantKind.External => context.ParticipantId.HasValue
                && request.ParticipantKind == FormParticipantKind.External
                && request.ParticipantId == context.ParticipantId
                && context.BoundRequestId == request.Id,
            _ => false,
        };
    }

    internal static bool RowVersionMatches(FormRequest request, byte[] expected) =>
        request.RowVersion.AsSpan().SequenceEqual(expected);

    internal static string Serialize(FormAnswers answers) => JsonSerializer.Serialize(answers, JsonOptions);

    internal static IReadOnlyList<ValidationIssue> ValidateReferences(
        FormDefinition definition,
        FormAnswers answers,
        FormRequest request,
        bool requireReady
    )
    {
        var issues = new List<ValidationIssue>();
        foreach (var value in Values(definition, answers))
        {
            if (value.Component.Type == "file" && value.Value.ValueKind == JsonValueKind.Array)
                ValidateFiles(value, request, requireReady, issues);
            else if (value.Component.Type == "taxId" && value.Value.ValueKind == JsonValueKind.Object)
                ValidateSensitive(value, request, requireReady, issues);
        }
        if (requireReady)
            ValidateHeldAttachments(request, issues);
        return issues;
    }

    /// <summary>
    /// Un archivo subido que sigue escaneándose o en revisión de contenido frena el envío aunque la respuesta no lo
    /// nombre (el portal solo lo escribe al quedar disponible). Para mandar sin él, el cliente lo quita (Detached).
    /// </summary>
    private static void ValidateHeldAttachments(FormRequest request, ICollection<ValidationIssue> issues)
    {
        foreach (var attachment in request.Submission?.Attachments ?? [])
        {
            if (issues.Any(issue => issue.ComponentId == attachment.ComponentId && issue.RowId == attachment.RowId))
                continue;
            var path = $"/values/{attachment.ComponentId}";
            if (attachment.Status == FormAttachmentStatus.Pending)
                issues.Add(new(
                    "answer.attachment_pending",
                    path,
                    attachment.ComponentId,
                    attachment.RowId,
                    "The attachment is still being processed."
                ));
            else if (attachment.Status == FormAttachmentStatus.UnderReview)
                issues.Add(new(
                    "answer.attachment_under_review",
                    path,
                    attachment.ComponentId,
                    attachment.RowId,
                    "The attachment is under review against our content policy."
                ));
        }
    }

    internal static (IReadOnlyCollection<Guid> FileIds, IReadOnlyCollection<Guid> SensitiveIds) References(
        FormDefinition definition,
        FormAnswers answers
    )
    {
        var files = new HashSet<Guid>();
        var sensitive = new HashSet<Guid>();
        foreach (var value in Values(definition, answers))
        {
            if (value.Component.Type == "file" && value.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.Value.EnumerateArray())
                    if (item.TryGetProperty("fileId", out var node) && node.TryGetGuid(out var id))
                        files.Add(id);
            }
            else if (
                value.Component.Type == "taxId"
                && value.Value.ValueKind == JsonValueKind.Object
                && value.Value.TryGetProperty("sensitiveRef", out var node)
                && node.TryGetGuid(out var id)
            )
                sensitive.Add(id);
        }
        return (files, sensitive);
    }

    internal static (FormComponent Component, bool Repeated)? FindComponent(
        FormDefinition definition,
        string componentId
    )
    {
        foreach (var section in definition.Pages.SelectMany(page => page.Sections))
        {
            var component = section.Components.FirstOrDefault(item => item.Id == componentId);
            if (component is not null)
                return (component, section.Repeat is not null);
        }
        return null;
    }

    internal static bool HasRow(FormAnswers answers, FormDefinition definition, string componentId, string rowId)
    {
        var section = definition
            .Pages.SelectMany(page => page.Sections)
            .FirstOrDefault(item =>
                item.Repeat is not null && item.Components.Any(component => component.Id == componentId)
            );
        if (
            section is null
            || !answers.Values.TryGetValue(section.Id, out var raw)
            || raw.ValueKind != JsonValueKind.Object
            || !raw.TryGetProperty("rows", out var rows)
            || rows.ValueKind != JsonValueKind.Array
        )
            return false;
        return rows.EnumerateArray().Any(row => row.TryGetProperty("rowId", out var node) && node.GetString() == rowId);
    }

    private static IEnumerable<AddressedValue> Values(FormDefinition definition, FormAnswers answers)
    {
        foreach (var section in definition.Pages.SelectMany(page => page.Sections))
        {
            if (section.Repeat is null)
            {
                foreach (var component in section.Components)
                    if (answers.Values.TryGetValue(component.Id, out var value))
                        yield return new AddressedValue(component, null, value);
                continue;
            }

            if (
                !answers.Values.TryGetValue(section.Id, out var raw)
                || raw.ValueKind != JsonValueKind.Object
                || !raw.TryGetProperty("rows", out var rows)
                || rows.ValueKind != JsonValueKind.Array
            )
                continue;
            foreach (var row in rows.EnumerateArray())
            {
                if (
                    !row.TryGetProperty("rowId", out var rowIdNode)
                    || !row.TryGetProperty("values", out var values)
                    || values.ValueKind != JsonValueKind.Object
                )
                    continue;
                var rowId = rowIdNode.GetString();
                foreach (var component in section.Components)
                    if (values.TryGetProperty(component.Id, out var value))
                        yield return new AddressedValue(component, rowId, value);
            }
        }
    }

    private static void ValidateFiles(
        AddressedValue value,
        FormRequest request,
        bool requireReady,
        ICollection<ValidationIssue> issues
    )
    {
        foreach (var item in value.Value.EnumerateArray())
        {
            if (!item.TryGetProperty("fileId", out var node) || !node.TryGetGuid(out var fileId))
                continue;
            var attachment = request.Submission?.Attachments.FirstOrDefault(candidate =>
                candidate.FileId == fileId
                && candidate.ComponentId == value.Component.Id
                && candidate.RowId == value.RowId
                && candidate.Status != FormAttachmentStatus.Detached
            );
            if (attachment is null)
                issues.Add(
                    Issue("answer.attachment_not_registered", value, "The attachment is not registered for this field.")
                );
            else if (requireReady && attachment.Status == FormAttachmentStatus.Pending)
                issues.Add(Issue("answer.attachment_pending", value, "The attachment is still being processed."));
            else if (requireReady && attachment.Status == FormAttachmentStatus.UnderReview)
                issues.Add(
                    Issue(
                        "answer.attachment_under_review",
                        value,
                        "The attachment is under review against our content policy."
                    )
                );
            else if (requireReady && attachment.Status == FormAttachmentStatus.Rejected)
                issues.Add(Issue("answer.attachment_rejected", value, "The attachment was rejected."));
        }
    }

    private static void ValidateSensitive(
        AddressedValue value,
        FormRequest request,
        bool requireReady,
        ICollection<ValidationIssue> issues
    )
    {
        if (!value.Value.TryGetProperty("sensitiveRef", out var node) || !node.TryGetGuid(out var answerId))
            return;
        var answer = request.Submission?.SensitiveAnswers.FirstOrDefault(candidate =>
            candidate.Id == answerId
            && candidate.ComponentId == value.Component.Id
            && candidate.RowId == value.RowId
            && candidate.SupersededAtUtc is null
        );
        if (answer is null && requireReady)
            issues.Add(Issue("answer.sensitive_missing", value, "The sensitive answer is not available."));
        else if (answer is null)
            issues.Add(
                Issue(
                    "answer.sensitive_not_registered",
                    value,
                    "The sensitive answer is not registered for this field."
                )
            );
    }

    private static ValidationIssue Issue(string code, AddressedValue value, string message) =>
        new(code, $"/values/{value.Component.Id}", value.Component.Id, value.RowId, message);

    private sealed record AddressedValue(FormComponent Component, string? RowId, JsonElement Value);
}
