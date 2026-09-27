using Starter.Abstractions.Enums;

namespace Starter.IntegrationEvents.Contracts.Orders.Dtos;

// Only primitives, shared-kernel enums and nested DTOs cross a service boundary — never domain types.
public record OrderDto(
    int Id,
    string CustomerName,
    string CustomerEmail,
    OrderStage Stage,
    decimal Total,
    DateTime CreatedOn,
    DateTime? SubmittedOn,
    int CreatedById,
    List<OrderLineDto> Lines);

public record OrderLineDto(
    int Id,
    string ProductName,
    int Quantity,
    decimal UnitPrice);
