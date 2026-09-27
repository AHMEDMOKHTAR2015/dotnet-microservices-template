namespace Orders.Application.Features.SubmitOrder;

public class SubmitOrderCommandHandler(OrderRepository _orderRepository, OrderStateMachineFactory _stateMachineFactory)
    : IRequestHandler<SubmitOrderCommand, IdResponse>
{
    public async Task<IdResponse> Handle(SubmitOrderCommand command, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdOrThrowAsync(command.AggregateId, ct);

        order.Submit(command, _stateMachineFactory);

        await _orderRepository.SaveChangesAsync(ct);

        return new IdResponse(order.Id);
    }
}
