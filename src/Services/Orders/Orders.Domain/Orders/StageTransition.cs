using Blocks.Core.Cache;

namespace Orders.Domain.Orders;

// Lifecycle rules as DATA: (current stage, action) -> destination stage. Seeded from Data/Master/StageTransition.json,
// cached for the process lifetime (ICacheable) and evaluated by IOrderStateMachine. Same stage on both sides = re-entry.
public class StageTransition : IMetadataEntity, ICacheable
{
    public OrderStage CurrentStage { get; set; }
    public OrderActionType ActionType { get; set; }
    public OrderStage DestinationStage { get; set; }
}
