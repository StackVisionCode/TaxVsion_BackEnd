using TaxVision.DataCollection.Engine.Answers;

namespace TaxVision.DataCollection.Engine.Visibility;

public sealed class VisibilityMap
{
    private readonly HashSet<string> _visible;
    private readonly Dictionary<(string ComponentId, string RowId), bool> _rowVisibility;
    private readonly Dictionary<string, int> _rowCounts;

    public VisibilityMap(
        IEnumerable<string> visible,
        IDictionary<(string ComponentId, string RowId), bool>? rowVisibility = null
    )
        : this(
            new HashSet<string>(visible, StringComparer.Ordinal),
            rowVisibility is null ? [] : new Dictionary<(string ComponentId, string RowId), bool>(rowVisibility),
            new Dictionary<string, int>(StringComparer.Ordinal)
        ) { }

    // Comparte las colecciones sin copiarlas: el evaluador las llena en una sola pasada.
    private VisibilityMap(
        HashSet<string> visible,
        Dictionary<(string ComponentId, string RowId), bool> rowVisibility,
        Dictionary<string, int> rowCounts
    )
    {
        _visible = visible;
        _rowVisibility = rowVisibility;
        _rowCounts = rowCounts;
    }

    public IReadOnlyCollection<string> VisibleIds => _visible;

    public bool IsVisible(string id) => _visible.Contains(id);

    public bool IsVisible(string componentId, string rowId) =>
        _rowVisibility.TryGetValue((componentId, rowId), out var visible) && visible;

    internal static VisibilityMap CreateMutable() =>
        new(new HashSet<string>(StringComparer.Ordinal), [], new Dictionary<string, int>(StringComparer.Ordinal));

    internal void MarkVisible(string id) => _visible.Add(id);

    internal void SetRowVisibility(string componentId, string rowId, bool visible) =>
        _rowVisibility[(componentId, rowId)] = visible;

    internal void SetRowCount(string sectionId, int count) => _rowCounts[sectionId] = count;

    // Filas de un repeater visible; un repeater oculto cuenta 0.
    internal int RowCount(string sectionId, FormAnswers answers)
    {
        if (!IsVisible(sectionId))
            return 0;
        return _rowCounts.TryGetValue(sectionId, out var count)
            ? count
            : AnswersReader.ReadRows(answers, sectionId).Count;
    }
}
