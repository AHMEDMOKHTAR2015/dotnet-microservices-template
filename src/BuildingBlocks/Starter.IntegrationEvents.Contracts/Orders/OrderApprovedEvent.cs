using Starter.IntegrationEvents.Contracts.Orders.Dtos;

namespace Starter.IntegrationEvents.Contracts.Orders;

// Fat event: a full snapshot, so consumers never have to call back to the Orders service.
public record OrderApprovedEvent(OrderDto Order);
