namespace Orders.Domain.Orders.Events;

public record OrderLineAdded(Order Order, OrderLine Line, IOrderAction Action) : DomainEvent(Action);
