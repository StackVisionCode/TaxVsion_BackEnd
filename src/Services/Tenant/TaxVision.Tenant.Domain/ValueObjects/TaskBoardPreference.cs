using System.Text.Json;
using BuildingBlocks.Results;
using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Domain.ValueObjects;

/// <summary>
/// Vista del tablero de tareas del CRM: agrupación (estado o etapa; null = aún sin elegir), columnas ocultas y plegadas
/// por código de estado, si se esconden las vacías y el orden. Lo desconocido se descarta.
/// </summary>
public sealed class TaskBoardPreference : IWorkspacePreferenceContent
{
    public const int SupportedSchemaVersion = 1;
    public const int MaxCodes = 100;
    public const int MaxCodeLength = 40;

    private static readonly HashSet<string> Groupings = new(StringComparer.Ordinal) { "status", "stage" };
    private static readonly HashSet<string> Sorts = new(StringComparer.Ordinal) { "Due", "Priority" };

    private TaskBoardPreference(
        string? groupBy,
        IReadOnlyList<string> hidden,
        IReadOnlyList<string> collapsed,
        bool hideEmpty,
        string sort
    )
    {
        GroupBy = groupBy;
        HiddenStatusCodes = hidden;
        CollapsedStatusCodes = collapsed;
        HideEmpty = hideEmpty;
        Sort = sort;
    }

    public int SchemaVersion => SupportedSchemaVersion;
    public string? GroupBy { get; }
    public IReadOnlyList<string> HiddenStatusCodes { get; }
    public IReadOnlyList<string> CollapsedStatusCodes { get; }
    public bool HideEmpty { get; }
    public string Sort { get; }

    public static Result<TaskBoardPreference> Parse(int schemaVersion, string rawJson)
    {
        if (schemaVersion != SupportedSchemaVersion)
            return Result.Failure<TaskBoardPreference>(WorkspacePreferenceErrors.UnsupportedSchema);
        try
        {
            using var document = JsonDocument.Parse(rawJson);
            return Read(document.RootElement);
        }
        catch (JsonException)
        {
            return Result.Failure<TaskBoardPreference>(WorkspacePreferenceErrors.InvalidBoardPreferences);
        }
    }

    public string ToCanonicalJson() =>
        JsonSerializer.Serialize(
            new
            {
                groupBy = GroupBy,
                hiddenStatusCodes = HiddenStatusCodes,
                collapsedStatusCodes = CollapsedStatusCodes,
                hideEmpty = HideEmpty,
                sort = Sort,
            }
        );

    private static Result<TaskBoardPreference> Read(JsonElement root)
    {
        var invalid = Result.Failure<TaskBoardPreference>(WorkspacePreferenceErrors.InvalidBoardPreferences);
        if (root.ValueKind != JsonValueKind.Object)
            return invalid;

        var groupBy = OptionalString(root, "groupBy");
        var sort = OptionalString(root, "sort") ?? "Due";
        var hidden = Codes(root, "hiddenStatusCodes");
        var collapsed = Codes(root, "collapsedStatusCodes");
        var hideEmpty = root.TryGetProperty("hideEmpty", out var flag) && flag.ValueKind == JsonValueKind.True;

        if (groupBy is not null && !Groupings.Contains(groupBy))
            return invalid;
        if (!Sorts.Contains(sort) || hidden is null || collapsed is null)
            return invalid;
        return Result.Success(new TaskBoardPreference(groupBy, hidden, collapsed, hideEmpty, sort));
    }

    private static string? OptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>Sin repetidos y en el orden recibido; null si la lista no es válida.</summary>
    private static List<string>? Codes(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var list) || list.ValueKind == JsonValueKind.Null)
            return [];
        if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() > MaxCodes)
            return null;
        var codes = new List<string>();
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !IsCode(item.GetString()))
                return null;
            if (!codes.Contains(item.GetString()!))
                codes.Add(item.GetString()!);
        }
        return codes;
    }

    // Mismo formato que los códigos de estado de Tasks: ^[a-z0-9_]{1,40}$.
    private static bool IsCode(string? code) =>
        code is { Length: > 0 and <= MaxCodeLength }
        && code.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_');
}
