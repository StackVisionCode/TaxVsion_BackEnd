namespace BuildingBlocks;

public interface IDomainEvent
{
    DateTime OccurredOn { get; }
}
