namespace TaxVision.DataCollection.Engine.Visibility;

public sealed class VisibilityMap
{
    private readonly HashSet<string> _visible;
    private readonly Dictionary<(string ComponentId, string RowId), bool> _rowVisibility;

    public VisibilityMap(
        IEnumerable<string> visible,
        IDictionary<(string ComponentId, string RowId), bool>? rowVisibility = null
    )
    {
        _visible = new HashSet<string>(visible, StringComparer.Ordinal);
        _rowVisibility = rowVisibility is null
            ? []
            : new Dictionary<(string ComponentId, string RowId), bool>(rowVisibility);
    }

    public IReadOnlyCollection<string> VisibleIds => _visible;
    public bool IsVisible(string id) => _visible.Contains(id);
    public bool IsVisible(string componentId, string rowId) =>
        _rowVisibility.TryGetValue((componentId, rowId), out var visible) && visible;
}
