using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence;

/// <summary>
/// Minutos entre dos instantes como función de base: en SQL Server se traduce a
/// <c>DATEDIFF(minute, from, to)</c> (agrupable); en memoria (InMemory de los tests) es la resta.
/// <c>EF.Functions.DateDiffMinute</c> no sirve aquí porque no tiene implementación en memoria.
/// </summary>
public static class FormInsightsDbFunctions
{
    public static int MinutesBetween(DateTime fromUtc, DateTime toUtc) =>
        (int)Math.Floor((toUtc - fromUtc).TotalMinutes);

    internal static void Register(ModelBuilder modelBuilder) =>
        modelBuilder
            .HasDbFunction(typeof(FormInsightsDbFunctions).GetMethod(nameof(MinutesBetween))!)
            .HasTranslation(arguments => new SqlFunctionExpression(
                "DATEDIFF",
                [new SqlFragmentExpression("minute"), arguments[0], arguments[1]],
                nullable: true,
                argumentsPropagateNullability: [false, true, true],
                typeof(int),
                null
            ));
}
