using Starter.IntegrationEvents.Contracts.Orders.Dtos;

namespace Orders.Application.Features.ApproveOrder;

public class IntegrationEventsMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Order, OrderDto>()
            .Map(dest => dest.CustomerEmail, src => src.CustomerEmail.Value);

        config.NewConfig<OrderLine, OrderLineDto>();
    }
}
