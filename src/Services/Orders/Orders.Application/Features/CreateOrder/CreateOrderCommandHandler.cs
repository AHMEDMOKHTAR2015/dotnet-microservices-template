namespace Orders.Application.Features.CreateOrder;

public class CreateOrderCommandHandler(OrderRepository _orderRepository)
    : IRequestHandler<CreateOrderCommand, IdResponse>
{
    public async Task<IdResponse> Handle(CreateOrderCommand command, CancellationToken ct)
    {
        var order = Order.Create(command.CustomerName, EmailAddress.Create(command.CustomerEmail), command);

        await _orderRepository.AddAsync(order, ct);
        await _orderRepository.SaveChangesAsync(ct);

        return new IdResponse(order.Id);
    }
}
