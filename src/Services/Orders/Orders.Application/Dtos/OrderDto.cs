namespace Orders.Application.Dtos;

public record OrderDto(
    int Id,
    string CustomerName,
    string CustomerEmail,
    OrderStage Stage,
    decimal Total,
    DateTime CreatedOn,
    DateTime? SubmittedOn,
    List<OrderLineDto> Lines);

public record OrderLineDto(int Id, string ProductName, int Quantity, decimal UnitPrice, decimal LineTotal);
