namespace Orders.Domain.Orders;

public class OrderLine : Entity
{
    private OrderLine() {}

    public int OrderId { get; private set; }
    public required string ProductName { get; init; }
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }

    public decimal LineTotal => Quantity * UnitPrice;

    //insight - internal factory: a line can only be created through its aggregate (Order.AddLine), which enforces cross-line rules
    internal static OrderLine Create(Order order, string productName, int quantity, decimal unitPrice)
    {
        Guard.ThrowIfNullOrWhiteSpace(productName);
        Guard.ThrowIfFalse(quantity > 0, "Quantity must be greater than zero.");
        Guard.ThrowIfFalse(unitPrice >= 0, "Unit price cannot be negative.");

        return new OrderLine
        {
            OrderId = order.Id,
            ProductName = productName.Trim(),
            Quantity = quantity,
            UnitPrice = unitPrice
        };
    }
}
