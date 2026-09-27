namespace Orders.Application.Features.Shared;

public abstract record OrderCommand : AggregateCommandBase<OrderActionType>, IOrderAction, ICommand<IdResponse>;
public abstract record OrderCommand<TResponse> : AggregateCommandBase<OrderActionType>, IOrderAction, ICommand<TResponse>;

public abstract class OrderCommandValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : IAggregateAction
{
    protected OrderCommandValidator()
    {
        RuleFor(c => c.AggregateId).GreaterThan(0).WithMessageForInvalidId("id");
    }
}
