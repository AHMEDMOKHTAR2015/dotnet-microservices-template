using Stateless;
using Blocks.Core.Cache;
using Microsoft.Extensions.Caching.Memory;

namespace Orders.Application.StateMachines;

// Legal (stage, action) pairs come from the cached StageTransition table (seeded from Data/Master/StageTransition.json).
public class OrderStateMachine : IOrderStateMachine
{
    private readonly StateMachine<OrderStage, OrderActionType> _stateMachine;

    public OrderStateMachine(OrderStage orderStage, IMemoryCache cache)
    {
        _stateMachine = new(orderStage);

        var transitions = cache.Get<List<StageTransition>>();
        foreach (var transition in transitions)
        {
            if (transition.CurrentStage != transition.DestinationStage)
                _stateMachine.Configure(transition.CurrentStage).Permit(transition.ActionType, transition.DestinationStage);
            else
                _stateMachine.Configure(transition.CurrentStage).PermitReentry(transition.ActionType);
        }
    }

    public bool CanFire(OrderActionType actionType) => _stateMachine.CanFire(actionType);
}
