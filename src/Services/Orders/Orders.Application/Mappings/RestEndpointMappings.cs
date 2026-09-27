using Orders.Application.Dtos;

namespace Orders.Application.Mappings;

public class RestEndpointMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Order, OrderDto>()
            .Map(dest => dest.CustomerEmail, src => src.CustomerEmail.Value);

        config.NewConfig<OrderLine, OrderLineDto>();

        // map primitives to value objects THROUGH their factories, never around them
        config.ForType<string, EmailAddress>().MapWith(src => EmailAddress.Create(src));
    }
}
