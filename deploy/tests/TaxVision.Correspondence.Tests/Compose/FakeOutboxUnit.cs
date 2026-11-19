using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Tests.Ingest;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Compose;

/// <summary>Guarda con el unit of work falso y deja lo publicado en el bus falso.</summary>
internal sealed class FakeOutboxUnit(FakeUnitOfWork unitOfWork, FakeMessageBus? bus = null) : IOutboxUnit
{
    public FakeMessageBus Bus { get; } = bus ?? new FakeMessageBus();

    public async Task RunAsync(Func<Wolverine.IMessageBus, CancellationToken, Task<bool>> work, CancellationToken ct)
    {
        if (await work(Bus, ct))
            await unitOfWork.SaveChangesAsync(ct);
    }
}
