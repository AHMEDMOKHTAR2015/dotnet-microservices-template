using MassTransit;
using Orders.Domain.Orders.Events;
using Starter.IntegrationEvents.Contracts.Orders;
using Starter.IntegrationEvents.Contracts.Orders.Dtos;

namespace Orders.Application.Features.ApproveOrder;

// The ONLY place this service publishes to the bus. Runs after the commit (post-save interceptor).
public class PublishIntegrationEventOnOrderApprovedHandler(OrderRepository _orderRepository, IPublishEndpoint _publishEndpoint)
    : INotificationHandler<OrderApproved>
{
    public async Task Handle(OrderApproved notification, CancellationToken ct)
    {
        var order = Guard.NotFound(await _orderRepository.GetFullOrderByIdAsync(notification.Order.Id, ct));   // re-fetch the full graph

        var orderDto = order.Adapt<OrderDto>();

        await _publishEndpoint.Publish(new OrderApprovedEvent(orderDto), ct);
    }
}
