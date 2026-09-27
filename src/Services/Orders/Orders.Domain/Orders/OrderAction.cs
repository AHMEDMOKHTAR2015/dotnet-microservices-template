namespace Orders.Domain.Orders;

//insight - an action is never modified: it is the audit trail of every command executed on the order
public class OrderAction : Entity
{
    private OrderAction() {}

    public int OrderId { get; private set; }                     // set by EF through the Order.Actions relationship
    public OrderActionType ActionType { get; private set; }
    public string? Comment { get; private set; }
    public int CreatedById { get; private set; }
    public DateTime CreatedOn { get; private set; }

    internal static OrderAction From(IOrderAction action) => new()
    {
        ActionType = action.ActionType,
        Comment = action.Comment,
        CreatedById = action.CreatedById,
        CreatedOn = action.CreatedOn
    };
}
