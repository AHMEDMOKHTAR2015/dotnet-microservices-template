namespace Orders.Application.Features.RejectOrder;

public class RejectOrderCommandHandler(OrderRepository _orderRepository, OrderStateMachineFactory _stateMachineFactory)
    : IRequestHandler<RejectOrderCommand, IdResponse>
{
    public async Task<IdResponse> Handle(RejectOrderCommand command, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdOrThrowAsync(command.AggregateId, ct);

        order.Reject(command, _stateMachineFactory);

        await _orderRepository.SaveChangesAsync(ct);

        return new IdResponse(order.Id);
    }
}
