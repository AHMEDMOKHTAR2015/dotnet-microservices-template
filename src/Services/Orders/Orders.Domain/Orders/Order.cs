namespace Orders.Domain.Orders;

public partial class Order : AggregateRoot
{
    private Order() {}                                           // use Order.Create(...)

    public required string CustomerName { get; init; }
    public required EmailAddress CustomerEmail { get; init; }

    public OrderStage Stage { get; private set; }                // changed only by SetStage
    public DateTime? SubmittedOn { get; private set; }

    private readonly List<OrderLine> _lines = new();
    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly();

    private readonly List<StageHistory> _stageHistories = new();
    public IReadOnlyList<StageHistory> StageHistories => _stageHistories.AsReadOnly();

    private readonly List<OrderAction> _actions = new();
    public IReadOnlyList<OrderAction> Actions => _actions.AsReadOnly();

    public decimal Total => _lines.Sum(line => line.LineTotal);
}
