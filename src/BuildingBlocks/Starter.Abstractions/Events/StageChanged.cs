namespace Starter.Abstractions.Events;

// Raised by every aggregate whose lifecycle is table-driven (SetStage is its only mutation point).
public record StageChanged<TStage>(TStage CurrentStage, TStage NewStage, IAggregateAction Action)
    : DomainEvent<IAggregateAction>(Action)
    where TStage : struct, Enum;
