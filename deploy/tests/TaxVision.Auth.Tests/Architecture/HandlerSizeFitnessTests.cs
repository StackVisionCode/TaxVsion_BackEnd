using System.Reflection;
using BuildingBlocks.Persistence;
using TaxVision.Auth.Application.Users.Commands;
using Wolverine;

namespace TaxVision.Auth.Tests.Architecture;

/// <summary>
/// AUTH-7: tope de dependencias de los handlers que crecían sin freno. Cuenta los parámetros de <c>Handle</c> sin el
/// mensaje ni el token. <c>IUnitOfWork</c> y <c>IMessageBus</c> se quedan en el handler: el primero es el que le da
/// la transacción y el segundo es el del contexto del mensaje; resuelto del contenedor podría ser otra instancia.
/// </summary>
public sealed class HandlerSizeFitnessTests
{
    private const int MaxDependencies = 10;

    public static TheoryData<Type> Handlers => [typeof(OffboardUserHandler), typeof(LoginHandler)];

    [Theory]
    [MemberData(nameof(Handlers))]
    public void UserAndLoginHandlers_HaveAtMost10Dependencies(Type handler)
    {
        var dependencies = Dependencies(handler);

        Assert.True(
            dependencies.Length <= MaxDependencies,
            $"{handler.Name}.Handle recibe {dependencies.Length} dependencias (máximo {MaxDependencies})."
        );
    }

    [Theory]
    [MemberData(nameof(Handlers))]
    public void Handlers_KeepTheirTransactionAndTheirBus(Type handler)
    {
        var dependencies = Dependencies(handler);

        Assert.Contains(typeof(IUnitOfWork), dependencies);
        Assert.Contains(typeof(IMessageBus), dependencies);
    }

    private static Type[] Dependencies(Type handler) =>
        handler
            .GetMethod("Handle", BindingFlags.Public | BindingFlags.Static)!
            .GetParameters()
            .Skip(1)
            .Select(parameter => parameter.ParameterType)
            .Where(type => type != typeof(CancellationToken))
            .ToArray();
}
