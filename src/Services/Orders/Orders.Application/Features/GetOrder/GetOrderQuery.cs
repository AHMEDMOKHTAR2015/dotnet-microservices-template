using Orders.Application.Dtos;

namespace Orders.Application.Features.GetOrder;

public record GetOrderQuery(int Id) : IQuery<GetOrderResponse>;
public record GetOrderResponse(OrderDto Order);

public class GetOrderQueryValidator : AbstractValidator<GetOrderQuery>
{
    public GetOrderQueryValidator()
        => RuleFor(q => q.Id).GreaterThan(0).WithMessageForInvalidId(nameof(GetOrderQuery.Id));
}
