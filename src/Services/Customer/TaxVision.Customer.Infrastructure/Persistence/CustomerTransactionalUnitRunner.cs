using System.Runtime.ExceptionServices;
using BuildingBlocks.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Infrastructure.Persistence;

/// <summary><see cref="ITransactionalUnitRunner"/> sobre <see cref="TransactionalJobUnit"/> con el CustomerDbContext.</summary>
internal sealed class CustomerTransactionalUnitRunner(
    IServiceScopeFactory scopeFactory,
    ILogger<CustomerTransactionalUnitRunner> logger
) : ITransactionalUnitRunner
{
    public async Task RunAsync(Func<TransactionalUnit, CancellationToken, Task<bool>> work, CancellationToken ct)
    {
        var result = await TransactionalJobUnit.RunAsync<CustomerDbContext>(
            scopeFactory,
            (unit, token) => work(new TransactionalUnit(unit.Services, message => unit.PublishAsync(message)), token),
            logger,
            ct
        );

        // No quedó nada: quien llama decide qué hacer (la importación lo deja fallido y visible).
        if (result.Exception is { } failure)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
