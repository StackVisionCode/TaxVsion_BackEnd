using TaxVision.BuildingBlocks.Testing.Architecture;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Architecture;

/// <summary>
/// BB-16 sobre todos los servicios: nada se publica antes de un guardado que todavía puede fallar. La deuda de
/// hoy queda listada por archivo, con cuántos métodos y por qué sigue ahí. Al corregirla, la entrada se retira.
/// </summary>
public sealed class SaveBeforePublishTests
{
    private const string SourceDirectory = "src/Services";

    private const string CloudStorageFiles = "src/Services/CloudStorage/TaxVision.CloudStorage.Application/";
    private const string MessageOnly = "BB-13: entra solo por mensaje, y por mensaje el intento revertido no sale";

    private static readonly IReadOnlyDictionary<string, (int Methods, string Reason)> PendingAdoption =
        new Dictionary<string, (int, string)>(StringComparer.Ordinal)
        {
        };

    /// <summary>Confirmados por lectura: la regla los marca, pero entran solo por mensaje (BB-13). No son deuda.</summary>
    private static readonly IReadOnlyDictionary<string, (int Methods, string Reason)> FalsePositives =
        new Dictionary<string, (int, string)>(StringComparer.Ordinal)
        {
            [CloudStorageFiles + "Files/Commands/FilesDeletionRequestedConsumer.cs"] = (1, MessageOnly),
            [CloudStorageFiles + "Files/Commands/SaveFileFromSourceHandler.cs"] = (1, MessageOnly),
        };

    private static readonly IReadOnlyDictionary<string, (int Methods, string Reason)> Known = PendingAdoption
        .Concat(FalsePositives)
        .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

    [Fact]
    public void NothingIsPublishedBeforeASaveThatCanStillFail()
    {
        var offenders = SaveBeforePublishRules
            .Scan(SourceDirectory)
            .GroupBy(f => f.Path)
            .Where(g => !Known.TryGetValue(g.Key, out var pending) || g.Count() > pending.Methods)
            .SelectMany(g => g)
            .Select(f => $"{f.Path}:{f.PublishLine} (guarda en :{f.SaveLine})")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Publica antes de guardar ({offenders.Length}). Mover la publicación después del último guardado:\n  "
                + string.Join("\n  ", offenders)
        );
    }

    [Fact]
    public void PendingAdoption_EntriesAreStillNeeded()
    {
        var found = SaveBeforePublishRules.Scan(SourceDirectory).GroupBy(f => f.Path).ToDictionary(g => g.Key, g => g.Count());
        var stale = Known
            .Where(entry => found.GetValueOrDefault(entry.Key) < entry.Value.Methods)
            .Select(entry => $"{entry.Key}: declara {entry.Value.Methods}, quedan {found.GetValueOrDefault(entry.Key)}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(stale.Length == 0, "Entradas de PendingAdoption ya corregidas:\n  " + string.Join("\n  ", stale));
    }
}

/// <summary>BB-16. El clasificador de <see cref="SaveBeforePublishRules"/> con fuentes de ejemplo.</summary>
public sealed class SaveBeforePublishRulesTests
{
    [Fact]
    public void PublishThenSave_IsAViolation() =>
        Assert.Single(
            SaveBeforePublishRules.Classify(
                """
                public static async Task<Result> Handle(Command command)
                {
                    await bus.PublishAsync(new Changed());
                    await unitOfWork.SaveChangesAsync(ct);
                    return Result.Success();
                }
                """
            )
        );

    [Fact]
    public void SaveThenPublish_IsFine() =>
        Assert.Empty(
            SaveBeforePublishRules.Classify(
                """
                public static async Task<Result> Handle(Command command)
                {
                    await unitOfWork.SaveChangesAsync(ct);
                    await bus.PublishAsync(new Changed());
                    return Result.Success();
                }
                """
            )
        );

    [Fact]
    public void PublishInABranchThatReturns_BeforeTheSaveOfTheOtherBranch_IsFine() =>
        Assert.Empty(
            SaveBeforePublishRules.Classify(
                """
                public static async Task<Result> Handle(Command command)
                {
                    if (failed)
                    {
                        await bus.PublishAsync(new Failed());
                        return Result.Failure(error);
                    }
                    await unitOfWork.SaveChangesAsync(ct);
                    return Result.Success();
                }
                """
            )
        );

    [Fact]
    public void GuardReturnBetweenPublishAndSave_DoesNotHideTheViolation() =>
        Assert.Single(
            SaveBeforePublishRules.Classify(
                """
                public static async Task<Result> Handle(Command command)
                {
                    await bus.PublishAsync(new Changed());
                    if (nothingElse)
                        return Result.Success();
                    await unitOfWork.SaveChangesAsync(ct);
                    return Result.Success();
                }
                """
            )
        );

    [Fact]
    public void PublishHelperAndExtensions_Count()
    {
        Assert.Single(
            SaveBeforePublishRules.Classify(
                """
                public static async Task Handle(Command command)
                {
                    await RolePermissionsFanOut.PublishForRoleHoldersAsync(role, bus, ct);
                    await unitOfWork.SaveChangesAsync(ct);
                }
                """
            )
        );
        Assert.Single(
            SaveBeforePublishRules.Classify(
                """
                public static async Task Handle(Command command)
                {
                    await bus.RequestSaveAsync(file, destination);
                    await unitOfWork.SaveChangesAsync(ct);
                }
                """
            )
        );
    }

    [Fact]
    public void InvokeAsync_DoesNotCount() =>
        Assert.Empty(
            SaveBeforePublishRules.Classify(
                """
                public static async Task Handle(Command command)
                {
                    await bus.InvokeAsync(new Inner());
                    await unitOfWork.SaveChangesAsync(ct);
                }
                """
            )
        );

    [Fact]
    public void SaveInTheNextMethod_IsNotTheSamePath() =>
        Assert.Empty(
            SaveBeforePublishRules.Classify(
                """
                private static async Task AnnounceAsync(IMessageBus bus)
                {
                    await bus.PublishAsync(new Changed());
                }

                private static async Task PersistAsync(IUnitOfWork unitOfWork)
                {
                    await unitOfWork.SaveChangesAsync(ct);
                }
                """
            )
        );
}
