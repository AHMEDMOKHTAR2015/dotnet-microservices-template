namespace Orders.Domain.Orders.Events;

public record OrderApproved(Order Order, IOrderAction Action) : DomainEvent(Action);
