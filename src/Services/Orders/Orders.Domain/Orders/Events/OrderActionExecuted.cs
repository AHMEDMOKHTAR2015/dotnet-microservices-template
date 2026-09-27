namespace Orders.Domain.Orders.Events;

public record OrderActionExecuted(Order Order, IOrderAction Action) : DomainEvent(Action);
