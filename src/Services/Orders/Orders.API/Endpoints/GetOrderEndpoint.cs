using Orders.Application.Features.GetOrder;

namespace Orders.API.Endpoints;

public static class GetOrderEndpoint
{
    public static void Map(this IEndpointRouteBuilder app)
    {
        app.MapGet("/orders/{id:int}", async ([AsParameters] GetOrderQuery query, ISender sender) =>
        {
            var response = await sender.Send(query);
            return Results.Ok(response);
        })
        .RequireRoleAuthorization(Role.Customer, Role.OrderManager, Role.Admin)
        .WithName("GetOrder")
        .WithTags("Orders")
        .Produces<GetOrderResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
