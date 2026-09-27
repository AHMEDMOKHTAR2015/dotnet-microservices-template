using Orders.Application.Dtos;

namespace Orders.Application.Features.GetOrder;

public class GetOrderQueryHandler(OrderRepository _orderRepository)
    : IRequestHandler<GetOrderQuery, GetOrderResponse>
{
    public async Task<GetOrderResponse> Handle(GetOrderQuery query, CancellationToken ct)
    {
        var order = Guard.NotFound(await _orderRepository.GetFullOrderByIdAsync(query.Id, ct));

        return new GetOrderResponse(order.Adapt<OrderDto>());
    }
}
