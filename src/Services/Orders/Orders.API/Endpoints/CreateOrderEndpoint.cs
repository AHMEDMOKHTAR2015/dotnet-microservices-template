using Orders.Application.Features.CreateOrder;

namespace Orders.API.Endpoints;

public static class CreateOrderEndpoint
{
    public static void Map(this IEndpointRouteBuilder app)
    {
        app.MapPost("/orders", async (CreateOrderCommand command, ISender sender) =>
        {
            var response = await sender.Send(command);
            return Results.Created($"/api/orders/{response.Id}", response);
        })
        .RequireRoleAuthorization(Role.Customer)
        .WithName("CreateOrder")
        .WithTags("Orders")
        .Produces<IdResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
