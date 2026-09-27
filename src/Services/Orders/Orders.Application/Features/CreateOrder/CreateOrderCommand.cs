namespace Orders.Application.Features.CreateOrder;

public record CreateOrderCommand(string CustomerName, string CustomerEmail) : OrderCommand
{
    public override OrderActionType ActionType => OrderActionType.CreateOrder;
}

// No aggregate id yet, so this validator doesn't derive OrderCommandValidator.
public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(c => c.CustomerName)
            .NotEmptyWithMessage(nameof(CreateOrderCommand.CustomerName))
            .MaximumLengthWithMessage(MaxLength.C128, nameof(CreateOrderCommand.CustomerName));

        RuleFor(c => c.CustomerEmail)
            .NotEmptyWithMessage(nameof(CreateOrderCommand.CustomerEmail))
            .MaximumLengthWithMessage(MaxLength.C64, nameof(CreateOrderCommand.CustomerEmail))
            .EmailAddress();
    }
}
