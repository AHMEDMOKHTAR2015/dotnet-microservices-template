namespace Orders.Application.Features.SubmitOrder;

public record SubmitOrderCommand : OrderCommand
{
    public override OrderActionType ActionType => OrderActionType.SubmitOrder;
}

public class SubmitOrderCommandValidator : OrderCommandValidator<SubmitOrderCommand>;
