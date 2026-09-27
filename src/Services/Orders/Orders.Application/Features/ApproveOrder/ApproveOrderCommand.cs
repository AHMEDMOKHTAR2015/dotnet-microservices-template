namespace Orders.Application.Features.ApproveOrder;

public record ApproveOrderCommand : OrderCommand
{
    public override OrderActionType ActionType => OrderActionType.ApproveOrder;
}

public class ApproveOrderCommandValidator : OrderCommandValidator<ApproveOrderCommand>;
