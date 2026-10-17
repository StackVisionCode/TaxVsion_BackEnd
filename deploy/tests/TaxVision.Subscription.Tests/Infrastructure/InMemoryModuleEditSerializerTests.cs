using TaxVision.Subscription.Infrastructure.Catalog.Concurrency;
using Xunit;

namespace TaxVision.Subscription.Tests.Infrastructure;

public sealed class InMemoryModuleEditSerializerTests
{
    [Fact]
    public async Task Serializes_the_same_module_code_case_insensitively()
    {
        var serializer = new InMemoryModuleEditSerializer();
        var first = await serializer.AcquireAsync("wallet", CancellationToken.None);

        var secondTask = serializer.AcquireAsync("WALLET", CancellationToken.None);
        await Task.Delay(25);
        Assert.False(secondTask.IsCompleted);

        await first.DisposeAsync();
        await using var second = await secondTask;
    }
}
