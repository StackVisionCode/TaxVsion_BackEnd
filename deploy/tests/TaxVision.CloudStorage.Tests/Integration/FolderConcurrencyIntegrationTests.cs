using System.Reflection;
using BuildingBlocks.Results;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Files.RecycleBin;
using TaxVision.CloudStorage.Application.Folders;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;
using TaxVision.CloudStorage.Infrastructure.Time;
using TaxVision.CloudStorage.Tests.Application;

namespace TaxVision.CloudStorage.Tests.Integration;

/// <summary>Movimientos y altas de carpeta a la vez, contra SQL real: el árbol no puede quedar con ciclos ni repetidos.</summary>
[Collection(CloudStorageConcurrencyCollection.Name)]
public sealed class FolderConcurrencyIntegrationTests(CloudStorageConcurrencyFixture fixture)
{
    private static readonly TimeSpan OverlapWait = TimeSpan.FromSeconds(3);
    private static readonly StorageActorScope Scope = new(false, null);
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task CrossMoveInParallel_OnlyOneSucceeds()
    {
        var a = await SeedFolderAsync("A", null);
        var b = await SeedFolderAsync("B", null);
        var overlap = new Rendezvous();

        var moves = await Task.WhenAll(MoveAsync(a.Id, b.Id, overlap), MoveAsync(b.Id, a.Id, overlap));

        Assert.Equal(1, moves.Count(moved => moved));
        Assert.Equal(0, await CountCyclesAsync());
    }

    [Fact]
    public async Task CrossMoveThroughDescendants_OnlyOneSucceeds()
    {
        var a = await SeedFolderAsync("A", null);
        var x = await SeedFolderAsync("X", a);
        var b = await SeedFolderAsync("B", null);
        var y = await SeedFolderAsync("Y", b);
        var overlap = new Rendezvous();

        // Sin filas en común: solo los ancestros del destino revelan el choque.
        var moves = await Task.WhenAll(MoveAsync(a.Id, y.Id, overlap), MoveAsync(b.Id, x.Id, overlap));

        Assert.Equal(1, moves.Count(moved => moved));
        Assert.Equal(0, await CountCyclesAsync());
    }

    [Fact]
    public async Task CreateSameNameSameParentInParallel_ReturnsSameFolder()
    {
        var overlap = new Rendezvous();

        var created = await Task.WhenAll(CreateAsync("Reports", overlap), CreateAsync("Reports", overlap));

        Assert.All(created, result => Assert.True(result.IsSuccess));
        Assert.Equal(created[0].Value.Id, created[1].Value.Id);
        await using var db = fixture.NewContext(_tenantId);
        Assert.Equal(1, await db.Folders.CountAsync(f => f.Name == "Reports"));
    }

    [Fact]
    public async Task The_same_name_is_free_again_after_the_folder_goes_to_the_recycle_bin()
    {
        var first = await SeedFolderAsync("Archive", null);
        await using (var db = fixture.NewContext(_tenantId))
        {
            var folder = await db.Folders.SingleAsync(f => f.Id == first.Id);
            Assert.True(folder.SoftDelete(Guid.NewGuid(), DateTime.UtcNow, TimeSpan.FromDays(30)).IsSuccess);
            await db.SaveChangesAsync();
        }

        var second = await SeedFolderAsync("Archive", null);

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task Restoring_a_folder_whose_name_was_taken_reports_it_and_keeps_it_in_the_recycle_bin()
    {
        var deleted = await SeedFolderAsync("Archive", null);
        await using (var db = fixture.NewContext(_tenantId))
        {
            var folder = await db.Folders.SingleAsync(f => f.Id == deleted.Id);
            Assert.True(folder.SoftDelete(folder.Id, DateTime.UtcNow, TimeSpan.FromDays(30)).IsSuccess);
            await db.SaveChangesAsync();
        }
        await SeedFolderAsync("Archive", null);

        await using var restore = fixture.NewContext(_tenantId);
        var result = await RestoreFolderHandler.Handle(
            new RestoreFolderCommand(_tenantId, Guid.NewGuid(), deleted.Id, new RequestAuditContext(null, null, "it")),
            new FolderRepository(restore),
            new FileObjectRepository(restore),
            new StorageAuditRepository(restore),
            new SystemClock(),
            restore,
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.Equal(FolderErrors.NameAlreadyExists, result.Error);
        await using var check = fixture.NewContext(_tenantId);
        Assert.NotNull((await check.Folders.AsNoTracking().SingleAsync(f => f.Id == deleted.Id)).SoftDeletedAtUtc);
    }

    [Fact]
    public async Task A_system_folder_can_land_next_to_a_user_folder_with_the_same_name()
    {
        await SeedFolderAsync("Documents", null);

        await using var db = fixture.NewContext(_tenantId);
        var provisioner = new SystemFolderProvisioner(new FolderRepository(db));
        var folderId = await provisioner.ResolveFolderIdAsync(
            _tenantId,
            OwnerType.Tenant,
            null,
            FolderType.Documents,
            Guid.NewGuid(),
            DateTime.UtcNow,
            CancellationToken.None
        );
        await db.SaveChangesAsync();

        Assert.NotNull(folderId);
        Assert.Equal(2, await db.Folders.CountAsync(f => f.Name == "Documents"));
    }

    [Fact]
    public async Task A_stale_rename_conflicts_instead_of_overwriting()
    {
        var folder = await SeedFolderAsync("Before", null);
        await using var first = fixture.NewContext(_tenantId);
        await using var second = fixture.NewContext(_tenantId);
        var winner = await first.Folders.SingleAsync(f => f.Id == folder.Id);
        var loser = await second.Folders.SingleAsync(f => f.Id == folder.Id);

        Assert.True(winner.Rename(FolderName.Create("Winner").Value, null).IsSuccess);
        await first.SaveChangesAsync();
        Assert.True(loser.Rename(FolderName.Create("Loser").Value, null).IsSuccess);

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());

        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
    }

    // Una transacción por movimiento, como la abre Wolverine. True si el movimiento quedó guardado.
    private async Task<bool> MoveAsync(Guid folderId, Guid newParentId, Rendezvous overlap)
    {
        await using var db = fixture.NewContext(_tenantId);
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var moved = await MoveFolderHandler.Handle(
                new MoveFolderCommand(_tenantId, Guid.NewGuid(), Scope, folderId, newParentId),
                OverlappingFolders.Wrap(
                    new FolderRepository(db),
                    overlap,
                    nameof(IFolderRepository.NameExistsUnderParentAsync)
                ),
                db,
                CancellationToken.None
            );
            if (moved.IsFailure)
            {
                Assert.Equal(FolderErrors.CircularReference, moved.Error);
                await transaction.RollbackAsync();
                return false;
            }
            await transaction.CommitAsync();
            return true;
        }
        catch (ConflictException)
        {
            return false;
        }
    }

    private async Task<Result<FolderResponse>> CreateAsync(string name, Rendezvous overlap)
    {
        await using var db = fixture.NewContext(_tenantId);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var created = await CreateFolderHandler.Handle(
            new CreateFolderCommand(_tenantId, Guid.NewGuid(), Scope, null, name, OwnerType.Tenant, null),
            OverlappingFolders.Wrap(new FolderRepository(db), overlap, nameof(IFolderRepository.Add)),
            new SystemClock(),
            db,
            NullLogger<Folder>.Instance,
            CancellationToken.None
        );
        await transaction.CommitAsync();
        return created;
    }

    private async Task<Folder> SeedFolderAsync(string name, Folder? parent)
    {
        var folder = Folder
            .Create(
                Guid.NewGuid(),
                _tenantId,
                OwnerType.Tenant,
                null,
                parent?.Id,
                FolderName.Create(name).Value,
                parent?.RelativePath,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        await using var db = fixture.NewContext(_tenantId);
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        return folder;
    }

    // Q-CST-6b sobre el tenant de la prueba.
    private async Task<int> CountCyclesAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH chain AS (
              SELECT Id AS StartId, Id, ParentFolderId, 0 AS Depth,
                     CAST(CONCAT('/', CONVERT(nvarchar(36), Id), '/') AS nvarchar(max)) AS Path
              FROM Folders WHERE SoftDeletedAtUtc IS NULL AND TenantId = @tenant
              UNION ALL
              SELECT c.StartId, p.Id, p.ParentFolderId, c.Depth + 1,
                     CONCAT(c.Path, CONVERT(nvarchar(36), p.Id), '/')
              FROM chain c JOIN Folders p ON p.Id = c.ParentFolderId
              WHERE c.Depth < 100 AND c.Path NOT LIKE CONCAT('%/', CONVERT(nvarchar(36), p.Id), '/%')
            )
            SELECT COUNT(DISTINCT c.StartId)
            FROM chain c JOIN Folders p ON p.Id = c.ParentFolderId
            WHERE c.Path LIKE CONCAT('%/', CONVERT(nvarchar(36), p.Id), '/%')
            OPTION (MAXRECURSION 200);
            """;
        command.Parameters.AddWithValue("@tenant", _tenantId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    /// <summary>Dos llamadas se esperan aquí para que pasen la verificación a la vez; con candado, una llega sola.</summary>
    public sealed class Rendezvous
    {
        private readonly Barrier _barrier = new(2);

        public void Meet() => _barrier.SignalAndWait(OverlapWait);
    }

    /// <summary>Repositorio real que frena en un método para forzar el solape.</summary>
    public class OverlappingFolders : DispatchProxy
    {
        private IFolderRepository _inner = default!;
        private Rendezvous _overlap = default!;
        private string _method = default!;

        internal static IFolderRepository Wrap(IFolderRepository inner, Rendezvous overlap, string method)
        {
            var proxy = Create<IFolderRepository, OverlappingFolders>();
            var self = (OverlappingFolders)(object)proxy;
            self._inner = inner;
            self._overlap = overlap;
            self._method = method;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == _method)
                _overlap.Meet();
            return targetMethod.Invoke(_inner, args);
        }
    }
}
