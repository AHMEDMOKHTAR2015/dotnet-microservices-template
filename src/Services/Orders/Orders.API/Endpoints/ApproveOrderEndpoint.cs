using Orders.Application.Features.ApproveOrder;

namespace Orders.API.Endpoints;

public static class ApproveOrderEndpoint
{
    public static void Map(this IEndpointRouteBuilder app)
    {
        app.MapPost("/orders/{id:int}:approve", async (int id, ApproveOrderCommand command, ISender sender) =>
        {
            var response = await sender.Send(command with { AggregateId = id });
            return Results.Ok(response);
        })
        .RequireRoleAuthorization(Role.OrderManager, Role.Admin)
        .WithName("ApproveOrder")
        .WithTags("Orders")
        .Produces<IdResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
