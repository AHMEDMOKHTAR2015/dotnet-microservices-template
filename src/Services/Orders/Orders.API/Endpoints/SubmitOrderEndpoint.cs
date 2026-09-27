using Orders.Application.Features.SubmitOrder;

namespace Orders.API.Endpoints;

public static class SubmitOrderEndpoint
{
    public static void Map(this IEndpointRouteBuilder app)
    {
        app.MapPost("/orders/{id:int}:submit", async (int id, SubmitOrderCommand command, ISender sender) =>
        {
            var response = await sender.Send(command with { AggregateId = id });
            return Results.Ok(response);
        })
        .RequireRoleAuthorization(Role.Customer)
        .WithName("SubmitOrder")
        .WithTags("Orders")
        .Produces<IdResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
