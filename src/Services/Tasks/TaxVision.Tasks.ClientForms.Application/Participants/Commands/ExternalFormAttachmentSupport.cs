using System.Text.Json;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

internal static class ExternalFormAttachmentSupport
{
    internal static bool AllowsType(JsonElement props, string name, string contentType)
    {
        if (!props.TryGetProperty("allowedTypes", out var allowed) || allowed.ValueKind != JsonValueKind.Array)
            return true;
        var extension = Path.GetExtension(name).ToLowerInvariant();
        return allowed
            .EnumerateArray()
            .Any(item =>
                string.Equals(item.GetString(), contentType, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.GetString(), extension, StringComparison.OrdinalIgnoreCase)
            );
    }

    internal static int ReadMaxFiles(JsonElement props, int fallback) =>
        props.TryGetProperty("maxFiles", out var node) && node.TryGetInt32(out var value) ? value : fallback;

    internal static long ReadMaxBytes(JsonElement props, long fallback) =>
        props.TryGetProperty("maxFileBytes", out var node) && node.TryGetInt64(out var value) ? value : fallback;

    internal static bool HasValidRow(
        FormRequest request,
        string componentId,
        string? rowId,
        FormDefinition definition,
        string? answersJson,
        TaxVision.DataCollection.Engine.IFormEngine engine
    )
    {
        if (string.IsNullOrWhiteSpace(rowId) || string.IsNullOrWhiteSpace(answersJson))
            return false;
        var answers = engine.ParseAnswers(answersJson);
        return answers.IsSuccess && FormRequestApplicationSupport.HasRow(answers.Value, definition, componentId, rowId);
    }
}
