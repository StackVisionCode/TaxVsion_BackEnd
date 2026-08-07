using BuildingBlocks.ActorTypeAuthorization;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Architecture;

/// <summary>
/// H-08 — <see cref="UserActorType"/> (Auth, 4 valores humanos) y <see cref="ActorType"/>
/// (BuildingBlocks, los mismos 4 más <see cref="ActorType.Service"/> para M2M) son los dos extremos
/// del claim <c>actor_type</c>. Auth emite, los otros 17 servicios leen.
///
/// <para>
/// Tenían los ordinales de <c>TenantAdmin</c> y <c>CustomerPortal</c> invertidos y nadie se enteraba
/// porque el claim viaja como string y la persistencia usa <c>HasConversion&lt;string&gt;()</c>. Estos
/// tests fijan la paridad para que un transporte numérico futuro no convierta un cliente en admin.
/// </para>
/// </summary>
public sealed class ActorTypeParityTests
{
    [Fact]
    public void Cada_UserActorType_existe_en_ActorType_con_el_mismo_nombre_y_ordinal()
    {
        var divergentes = Enum.GetValues<UserActorType>()
            .Where(actorType =>
                !Enum.TryParse<ActorType>(actorType.ToString(), out var shared) || (int)shared != (int)actorType
            )
            .Select(actorType => $"{actorType} = {(int)actorType}")
            .ToArray();

        Assert.True(
            divergentes.Length == 0,
            $"Estos valores de UserActorType no casan con ActorType por nombre y ordinal: {string.Join(", ", divergentes)}."
        );
    }

    [Fact]
    public void ActorType_solo_agrega_Service_sobre_los_actores_humanos()
    {
        var humanos = Enum.GetNames<UserActorType>();
        var extras = Enum.GetNames<ActorType>().Except(humanos).ToArray();

        // Si aparece un actor nuevo en BuildingBlocks que no sea M2M, Auth tiene que poder emitirlo.
        Assert.Equal([nameof(ActorType.Service)], extras);
    }
}
