using System.Reflection;
using TaxVision.CloudStorage.Application.Folders;
using TaxVision.CloudStorage.Application.Sharing;

namespace TaxVision.CloudStorage.Tests.Architecture;

/// <summary>
/// Caracterización de la partición de FolderCommands y ShareLinkCommands (fila F): cada comando conserva su handler,
/// su namespace y la firma de su <c>Handle</c>, que es lo que Wolverine descubre y genera. El comportamiento lo
/// cubren FolderHandlerTests, FolderShareHandlerTests y ShareLinkHandlerTests.
/// </summary>
public sealed class FolderAndShareHandlerSurfaceTests
{
    [Theory]
    [InlineData(typeof(CreateFolderHandler), typeof(CreateFolderCommand))]
    [InlineData(typeof(RenameFolderHandler), typeof(RenameFolderCommand))]
    [InlineData(typeof(MoveFolderHandler), typeof(MoveFolderCommand))]
    [InlineData(typeof(MoveFileToFolderHandler), typeof(MoveFileToFolderCommand))]
    [InlineData(typeof(DeleteFolderHandler), typeof(DeleteFolderCommand))]
    [InlineData(typeof(CreateShareLinkHandler), typeof(CreateShareLinkCommand))]
    [InlineData(typeof(CreateFolderShareLinkHandler), typeof(CreateFolderShareLinkCommand))]
    [InlineData(typeof(RevokeShareLinkHandler), typeof(RevokeShareLinkCommand))]
    [InlineData(typeof(UpdateShareExpirationHandler), typeof(UpdateShareExpirationCommand))]
    [InlineData(typeof(ChangeSharePermissionHandler), typeof(ChangeSharePermissionCommand))]
    public void Each_command_keeps_one_static_Handle_in_its_namespace(Type handler, Type command)
    {
        var handle = Assert.Single(
            handler.GetMethods(BindingFlags.Public | BindingFlags.Static),
            method => method.Name == "Handle"
        );

        Assert.Equal(command, handle.GetParameters()[0].ParameterType);
        Assert.Equal(command.Namespace, handler.Namespace);
        Assert.True(handler.IsAbstract && handler.IsSealed);
    }

    [Fact]
    public void Handle_signatures_do_not_change()
    {
        var actual = typeof(CreateFolderHandler)
            .Assembly.GetTypes()
            .Where(type =>
                type.Namespace is "TaxVision.CloudStorage.Application.Folders" or "TaxVision.CloudStorage.Application.Sharing"
            )
            .Where(type => Surface.Contains(type.Name))
            .Select(Signature)
            .Order()
            .ToArray();

        Assert.Equal(Surface.Count, actual.Length);
        Assert.Equal(SignatureSnapshot, actual);
    }

    private static readonly HashSet<string> Surface =
    [
        "CreateFolderHandler",
        "RenameFolderHandler",
        "MoveFolderHandler",
        "MoveFileToFolderHandler",
        "DeleteFolderHandler",
        "CreateShareLinkHandler",
        "CreateFolderShareLinkHandler",
        "RevokeShareLinkHandler",
        "UpdateShareExpirationHandler",
        "ChangeSharePermissionHandler",
    ];

    private static string Signature(Type handler)
    {
        var handle = handler.GetMethod("Handle", BindingFlags.Public | BindingFlags.Static)!;
        return $"{handler.Name}({string.Join(",", handle.GetParameters().Select(p => p.ParameterType.Name))})";
    }

    // Tomada del código antes de partir los archivos.
    private static readonly string[] SignatureSnapshot =
    [
        "ChangeSharePermissionHandler(ChangeSharePermissionCommand,IShareLinkRepository,IStorageLimitRepository,IStorageAuditRepository,ISystemClock,IMessageBus,IUnitOfWork,CancellationToken)",
        "CreateFolderHandler(CreateFolderCommand,IFolderRepository,ISystemClock,IUnitOfWork,ILogger`1,CancellationToken)",
        "CreateFolderShareLinkHandler(CreateFolderShareLinkCommand,IFolderRepository,IShareLinkRepository,IStorageLimitRepository,IShareLinkPasswordHasher,IStorageAuditRepository,ISystemClock,IMessageBus,IUnitOfWork,CancellationToken)",
        "CreateShareLinkHandler(CreateShareLinkCommand,IFileObjectRepository,IShareLinkRepository,IStorageLimitRepository,IShareLinkPasswordHasher,IStorageAuditRepository,ISystemClock,IMessageBus,IUnitOfWork,CancellationToken)",
        "DeleteFolderHandler(DeleteFolderCommand,IFolderRepository,IFileObjectRepository,IStorageAuditRepository,IStoragePlatformSettingsProvider,ISystemClock,IUnitOfWork,IMessageBus,CancellationToken)",
        "MoveFileToFolderHandler(MoveFileToFolderCommand,IFileObjectRepository,IFolderRepository,IShareLinkRepository,IStorageAuditRepository,ISystemClock,IMessageBus,IUnitOfWork,CancellationToken)",
        "MoveFolderHandler(MoveFolderCommand,IFolderRepository,IUnitOfWork,CancellationToken)",
        "RenameFolderHandler(RenameFolderCommand,IFolderRepository,IUnitOfWork,CancellationToken)",
        "RevokeShareLinkHandler(RevokeShareLinkCommand,IShareLinkRepository,IStorageAuditRepository,ISystemClock,IMessageBus,IUnitOfWork,CancellationToken)",
        "UpdateShareExpirationHandler(UpdateShareExpirationCommand,IShareLinkRepository,IStorageLimitRepository,ISystemClock,IUnitOfWork,CancellationToken)",
    ];
}
