namespace Orders.Application.Features.RejectOrder;

public record RejectOrderCommand : OrderCommand
{
    public override OrderActionType ActionType => OrderActionType.RejectOrder;
}

public class RejectOrderCommandValidator : OrderCommandValidator<RejectOrderCommand>;
