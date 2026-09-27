using Orders.Application.Features.RejectOrder;

namespace Orders.API.Endpoints;

public static class RejectOrderEndpoint
{
    public static void Map(this IEndpointRouteBuilder app)
    {
        app.MapPost("/orders/{id:int}:reject", async (int id, RejectOrderCommand command, ISender sender) =>
        {
            var response = await sender.Send(command with { AggregateId = id });
            return Results.Ok(response);
        })
        .RequireRoleAuthorization(Role.OrderManager, Role.Admin)
        .WithName("RejectOrder")
        .WithTags("Orders")
        .Produces<IdResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
