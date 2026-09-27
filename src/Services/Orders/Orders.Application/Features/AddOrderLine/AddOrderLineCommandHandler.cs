namespace Orders.Application.Features.AddOrderLine;

public class AddOrderLineCommandHandler(OrderRepository _orderRepository, OrderStateMachineFactory _stateMachineFactory)
    : IRequestHandler<AddOrderLineCommand, IdResponse>
{
    public async Task<IdResponse> Handle(AddOrderLineCommand command, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdOrThrowAsync(command.AggregateId, ct);   // loads the lines (Query())

        order.AddLine(command.ProductName, command.Quantity, command.UnitPrice, command, _stateMachineFactory);

        await _orderRepository.SaveChangesAsync(ct);

        return new IdResponse(order.Id);
    }
}
