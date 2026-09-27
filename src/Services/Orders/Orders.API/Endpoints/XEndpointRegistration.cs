namespace Orders.API.Endpoints;

public static class EndpointRegistration
{
    public static IEndpointRouteBuilder MapAllEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        GetOrderEndpoint.Map(api);
        CreateOrderEndpoint.Map(api);
        AddOrderLineEndpoint.Map(api);
        SubmitOrderEndpoint.Map(api);
        ApproveOrderEndpoint.Map(api);
        RejectOrderEndpoint.Map(api);

        return app;
    }
}
