namespace Orders.Domain.StateMachines;

// The Domain owns the seam; the Application implements it over the cached StageTransition table.
public interface IOrderStateMachine
{
    bool CanFire(OrderActionType actionType);
}

public delegate IOrderStateMachine OrderStateMachineFactory(OrderStage orderStage);

public static class Extensions
{
    public static void ValidateStageTransition(this OrderStateMachineFactory factory, OrderStage stage, OrderActionType actionType)
    {
        if (!factory(stage).CanFire(actionType))
            throw new DomainException($"Action {actionType} not allowed in the {stage} order stage");
    }
}
