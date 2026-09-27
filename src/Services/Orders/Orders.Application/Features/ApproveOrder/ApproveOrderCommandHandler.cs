namespace Orders.Application.Features.ApproveOrder;

public class ApproveOrderCommandHandler(OrderRepository _orderRepository, OrderStateMachineFactory _stateMachineFactory)
    : IRequestHandler<ApproveOrderCommand, IdResponse>
{
    public async Task<IdResponse> Handle(ApproveOrderCommand command, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdOrThrowAsync(command.AggregateId, ct);

        order.Approve(command, _stateMachineFactory);

        await _orderRepository.SaveChangesAsync(ct);

        return new IdResponse(order.Id);
    }
}
