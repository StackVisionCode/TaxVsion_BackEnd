using System.Buffers;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Results;
using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Domain.ValueObjects;

/// <summary>
/// Layout del dashboard del CRM: arreglo ordenado de widgets. Valida la forma, no que el widget exista (el servidor no
/// conoce el catálogo). Se guarda en JSON canónico: id, size y hidden; el esquema 2 añade <c>scope</c> opcional.
/// </summary>
public sealed class DashboardLayout : IWorkspacePreferenceContent
{
    /// <summary>Esquema que se informa cuando no hay nada guardado: un CRM anterior lo sigue leyendo.</summary>
    public const int SupportedSchemaVersion = 1;

    /// <summary>Esquema 2: cada widget puede llevar su alcance (Me / Office).</summary>
    public const int ScopedSchemaVersion = 2;

    public const int MaxItems = 50;
    public const int MaxWidgetIdLength = 40;

    private static readonly HashSet<string> Sizes = new(StringComparer.Ordinal) { "s", "m", "l" };
    private static readonly HashSet<string> Scopes = new(StringComparer.Ordinal) { "me", "office" };

    private readonly string _canonicalJson;

    private DashboardLayout(int schemaVersion, IReadOnlyList<DashboardWidgetPlacement> items)
    {
        SchemaVersion = schemaVersion;
        Items = items;
        _canonicalJson = Serialize(items);
    }

    public int SchemaVersion { get; }
    public IReadOnlyList<DashboardWidgetPlacement> Items { get; }

    /// <summary>Reglas en orden fijo: esquema, arreglo, tope, id, repetidos, tamaño, hidden y alcance.</summary>
    public static Result<DashboardLayout> Parse(int schemaVersion, string rawJson)
    {
        if (schemaVersion is not (SupportedSchemaVersion or ScopedSchemaVersion))
            return Result.Failure<DashboardLayout>(WorkspacePreferenceErrors.UnsupportedSchema);

        var read = Read(rawJson, schemaVersion == ScopedSchemaVersion);
        if (read.IsFailure)
            return Result.Failure<DashboardLayout>(read.Error);

        var invalid = Validate(read.Value);
        if (invalid is not null)
            return Result.Failure<DashboardLayout>(invalid);

        var items = read
            .Value.Select(w => new DashboardWidgetPlacement(w.Id!, w.Size!, w.Hidden!.Value, w.Scope))
            .ToList();
        return Result.Success(new DashboardLayout(schemaVersion, items));
    }

    public string ToCanonicalJson() => _canonicalJson;

    // Solo extrae los campos conocidos (scope, solo en el esquema 2); las propiedades desconocidas se descartan aquí.
    private static Result<List<RawWidget>> Read(string rawJson, bool scoped)
    {
        try
        {
            using var document = JsonDocument.Parse(rawJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
                return Result.Failure<List<RawWidget>>(WorkspacePreferenceErrors.InvalidPayload);
            if (root.GetArrayLength() > MaxItems)
                return Result.Failure<List<RawWidget>>(WorkspacePreferenceErrors.TooManyItems);

            var widgets = new List<RawWidget>();
            foreach (var element in root.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                    return Result.Failure<List<RawWidget>>(WorkspacePreferenceErrors.InvalidPayload);
                widgets.Add(
                    new RawWidget(
                        StringOf(element, "id"),
                        StringOf(element, "size"),
                        BoolOf(element),
                        scoped ? ScopeOf(element) : null
                    )
                );
            }
            return Result.Success(widgets);
        }
        catch (JsonException)
        {
            return Result.Failure<List<RawWidget>>(WorkspacePreferenceErrors.InvalidPayload);
        }
    }

    private static Error? Validate(List<RawWidget> widgets)
    {
        if (!widgets.TrueForAll(w => IsWidgetId(w.Id)))
            return WorkspacePreferenceErrors.InvalidWidgetId;
        if (widgets.Select(w => w.Id).Distinct(StringComparer.Ordinal).Count() != widgets.Count)
            return WorkspacePreferenceErrors.DuplicateWidgetId;
        if (!widgets.TrueForAll(w => w.Size is not null && Sizes.Contains(w.Size)))
            return WorkspacePreferenceErrors.InvalidWidgetSize;
        if (!widgets.TrueForAll(w => w.Hidden is not null))
            return WorkspacePreferenceErrors.InvalidPayload;
        return widgets.TrueForAll(w => w.Scope is null || Scopes.Contains(w.Scope))
            ? null
            : WorkspacePreferenceErrors.InvalidWidgetScope;
    }

    // ^[a-z0-9-]{1,40}$ sin regex.
    private static bool IsWidgetId(string? id) =>
        id is { Length: > 0 and <= MaxWidgetIdLength }
        && id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    private static string? StringOf(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? BoolOf(JsonElement element) =>
        element.TryGetProperty("hidden", out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;

    private static string Serialize(IReadOnlyList<DashboardWidgetPlacement> items)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var item in items)
            {
                writer.WriteStartObject();
                writer.WriteString("id", item.Id);
                writer.WriteString("size", item.Size);
                writer.WriteBoolean("hidden", item.Hidden);
                if (item.Scope is not null)
                    writer.WriteString("scope", item.Scope);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // Sin scope (o null) = el de siempre; un valor que no es texto queda como "" y se rechaza al validar.
    private static string? ScopeOf(JsonElement element) =>
        !element.TryGetProperty("scope", out var value) || value.ValueKind == JsonValueKind.Null ? null
        : value.ValueKind == JsonValueKind.String ? value.GetString()
        : "";

    private sealed record RawWidget(string? Id, string? Size, bool? Hidden, string? Scope);
}
