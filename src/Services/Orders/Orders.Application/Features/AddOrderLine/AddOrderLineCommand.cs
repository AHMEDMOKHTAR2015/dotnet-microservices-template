namespace Orders.Application.Features.AddOrderLine;

public record AddOrderLineCommand(string ProductName, int Quantity, decimal UnitPrice) : OrderCommand
{
    public override OrderActionType ActionType => OrderActionType.AddLine;
}

public class AddOrderLineCommandValidator : OrderCommandValidator<AddOrderLineCommand>
{
    public AddOrderLineCommandValidator()
    {
        RuleFor(c => c.ProductName)
            .NotEmptyWithMessage(nameof(AddOrderLineCommand.ProductName))
            .MaximumLengthWithMessage(MaxLength.C128, nameof(AddOrderLineCommand.ProductName));

        RuleFor(c => c.Quantity).GreaterThan(0);
        RuleFor(c => c.UnitPrice).GreaterThanOrEqualTo(0);
    }
}
