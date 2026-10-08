using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using TaxVision.Signature.Infrastructure.Persistence.Migrations;

namespace TaxVision.Signature.Tests.Persistence;

public sealed class TemplateMultiDocumentMigrationSafetyTests
{
    [Fact]
    public void Up_quarantines_documentless_templates_instead_of_blocking_the_deploy()
    {
        var operations = BuildOperations("Up");
        var sql = string.Join(
            Environment.NewLine,
            operations.OfType<SqlOperation>().Select(operation => operation.Sql)
        );

        Assert.Contains("SignatureTemplateMigrationQuarantines", sql, StringComparison.Ordinal);
        Assert.Contains("SET t.Status = N'Draft'", sql, StringComparison.Ordinal);
        Assert.Contains("00000000-0000-0000-0000-000000000000", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("Cannot migrate template fields", sql, StringComparison.Ordinal);
        Assert.Contains(
            operations.OfType<CreateTableOperation>(),
            operation => operation.Name == "SignatureTemplateMigrationQuarantines"
        );
    }

    [Fact]
    public void Down_restores_the_original_lifecycle_before_dropping_the_quarantine_ledger()
    {
        var operations = BuildOperations("Down");
        var restore = Assert.Single(operations.OfType<SqlOperation>());
        var quarantineDrop = Assert.Single(
            operations.OfType<DropTableOperation>(),
            operation => operation.Name == "SignatureTemplateMigrationQuarantines"
        );

        Assert.Contains("t.Status = q.OriginalStatus", restore.Sql, StringComparison.Ordinal);
        var orderedOperations = operations.ToList();
        Assert.True(orderedOperations.IndexOf(restore) < orderedOperations.IndexOf(quarantineDrop));
    }

    private static IReadOnlyList<MigrationOperation> BuildOperations(string methodName)
    {
        var migration = new F8_TemplateMultiDocument();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var method = typeof(F8_TemplateMultiDocument).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        Assert.NotNull(method);
        method.Invoke(migration, [builder]);
        return builder.Operations;
    }
}
