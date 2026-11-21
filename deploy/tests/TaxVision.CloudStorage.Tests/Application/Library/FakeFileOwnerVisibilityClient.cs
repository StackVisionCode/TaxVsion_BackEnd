using BuildingBlocks.Contracts.Files;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Library;

namespace TaxVision.CloudStorage.Tests.Application.Library;

/// <summary>Dueño en memoria: confirma los archivos sembrados y anota cada llamada.</summary>
internal sealed class FakeFileOwnerVisibilityClient : IFileOwnerVisibilityClient
{
    private readonly Dictionary<Guid, VisibleFile> _visible = [];
    private readonly HashSet<FileOwnerService> _down = [];

    public List<(FileOwnerService Owner, IReadOnlyList<Guid> FileIds, string Purpose)> Calls { get; } = [];

    /// <summary>Se ejecuta en cada llamada (p. ej. para hacer avanzar el reloj).</summary>
    public Action? OnCall { get; set; }

    public FakeFileOwnerVisibilityClient Confirm(Guid fileId, string title = "Title", Guid? resourceId = null)
    {
        _visible[fileId] = new VisibleFile(fileId, "task", resourceId ?? Guid.NewGuid(), title, null);
        return this;
    }

    public FakeFileOwnerVisibilityClient Confirm(VisibleFile visible)
    {
        _visible[visible.FileId] = visible;
        return this;
    }

    /// <summary>Respuesta del ámbito de Communication; por defecto, disponible y vacío.</summary>
    public OwnerScope Scope { get; set; } = new(true, new CommunicationFileScope([], [], [], []));

    public int ScopeCalls { get; private set; }

    /// <summary>Se ejecuta al pedir el ámbito.</summary>
    public Action? OnScope { get; set; }

    public Task<OwnerScope> GetCommunicationScopeAsync(CancellationToken ct)
    {
        ScopeCalls++;
        OnScope?.Invoke();
        return Task.FromResult(Scope);
    }

    public FakeFileOwnerVisibilityClient Down(FileOwnerService owner)
    {
        _down.Add(owner);
        return this;
    }

    public Task<OwnerVisibility> CheckAsync(
        FileOwnerService owner,
        IReadOnlyCollection<Guid> fileIds,
        string purpose,
        CancellationToken ct
    )
    {
        Calls.Add((owner, fileIds.ToList(), purpose));
        OnCall?.Invoke();
        if (_down.Contains(owner))
            return Task.FromResult(new OwnerVisibility(false, new Dictionary<Guid, VisibleFile>()));
        IReadOnlyDictionary<Guid, VisibleFile> visible = fileIds
            .Where(_visible.ContainsKey)
            .ToDictionary(id => id, id => _visible[id]);
        return Task.FromResult(new OwnerVisibility(true, visible));
    }
}

/// <summary>Reloj que avanza a mano.</summary>
internal sealed class SteppingClock(DateTime start) : ISystemClock
{
    public DateTime UtcNow { get; private set; } = start;

    public void Advance(TimeSpan by) => UtcNow += by;
}
