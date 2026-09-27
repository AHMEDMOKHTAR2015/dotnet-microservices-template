using Orders.Application.Features.AddOrderLine;

namespace Orders.API.Endpoints;

public static class AddOrderLineEndpoint
{
    public static void Map(this IEndpointRouteBuilder app)
    {
        // the aggregate id route parameter is named "id" (RouteKeys.AggregateId): the resource-authorization check reads it
        app.MapPost("/orders/{id:int}/lines", async (int id, AddOrderLineCommand command, ISender sender) =>
        {
            var response = await sender.Send(command with { AggregateId = id });
            return Results.Ok(response);
        })
        .RequireRoleAuthorization(Role.Customer)
        .WithName("AddOrderLine")
        .WithTags("Orders")
        .Produces<IdResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
