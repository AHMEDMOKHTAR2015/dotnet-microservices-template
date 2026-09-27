namespace Orders.Domain.Orders;

public class StageHistory : Entity
{
    public required int OrderId { get; init; }
    public required OrderStage StageId { get; init; }
    public required DateTime StartDate { get; init; }
}
