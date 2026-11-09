using System.Reflection;
using BuildingBlocks.Persistence;
using TaxVision.Subscription.Application.AddOns.Commands.PurchaseAddOn;
using TaxVision.Subscription.Application.Billing.Commands.SetBillingPreference;
using TaxVision.Subscription.Application.Seats.Commands.PurchaseSeats;
using TaxVision.Subscription.Application.Subscriptions.Commands.ChangePlan;
using Wolverine;

namespace TaxVision.Subscription.Tests.Architecture;

/// <summary>
/// SUB-11: tope de dependencias y de líneas de los handlers de compra y cambio de plan. La regla va al dominio
/// (<c>AddOnPurchasePolicy</c>, <c>SeatPurchasePolicy</c>); el handler carga, pide, guarda y publica.
/// </summary>
public sealed class SubscriptionHandlerSizeFitnessTests
{
    private const int MaxDependencies = 10;
    private const int MaxLines = 220;

    public static TheoryData<Type> Handlers =>
        [
            typeof(PurchaseAddOnHandler),
            typeof(PurchaseSeatsHandler),
            typeof(SetBillingPreferenceHandler),
            typeof(ChangePlanHandler),
        ];

    public static TheoryData<Type> TransactionalHandlers =>
        [typeof(PurchaseAddOnHandler), typeof(PurchaseSeatsHandler), typeof(SetBillingPreferenceHandler)];

    public static TheoryData<string> HandlerFiles =>
        [
            "AddOns/Commands/PurchaseAddOn/PurchaseAddOnHandler.cs",
            "Seats/Commands/PurchaseSeats/PurchaseSeatsHandler.cs",
            "Billing/Commands/SetBillingPreference/SetBillingPreferenceHandler.cs",
            "Subscriptions/Commands/ChangePlan/ChangePlanHandler.cs",
        ];

    [Theory]
    [MemberData(nameof(Handlers))]
    public void PurchaseHandlers_HaveAtMostTenDependencies(Type handler)
    {
        var dependencies = Dependencies(handler);

        Assert.True(
            dependencies.Length <= MaxDependencies,
            $"{handler.Name}.Handle recibe {dependencies.Length} dependencias (máximo {MaxDependencies})."
        );
    }

    // IUnitOfWork le da la transacción al handler, y IMessageBus es el del contexto del mensaje.
    [Theory]
    [MemberData(nameof(TransactionalHandlers))]
    public void PurchaseHandlers_KeepTheirTransactionAndTheirBus(Type handler)
    {
        var dependencies = Dependencies(handler);

        Assert.Contains(typeof(IUnitOfWork), dependencies);
        Assert.Contains(typeof(IMessageBus), dependencies);
    }

    [Theory]
    [MemberData(nameof(HandlerFiles))]
    public void PurchaseHandlers_StayUnderTheLineBudget(string relativePath)
    {
        var path = Path.Combine(
            RepositoryRoot(),
            "src",
            "Services",
            "Subscription",
            "TaxVision.Subscription.Application",
            relativePath
        );
        var lines = File.ReadLines(path).Count();

        Assert.True(lines <= MaxLines, $"{Path.GetFileName(path)} tiene {lines} líneas (máximo {MaxLines}).");
    }

    private static Type[] Dependencies(Type handler) =>
        handler
            .GetMethod("Handle", BindingFlags.Public | BindingFlags.Static)!
            .GetParameters()
            .Skip(1)
            .Select(parameter => parameter.ParameterType)
            .Where(type => type != typeof(CancellationToken))
            .ToArray();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TaxVision.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("No se encontró TaxVision.slnx.");
    }
}
