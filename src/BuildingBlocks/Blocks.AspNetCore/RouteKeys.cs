namespace Blocks.AspNetCore;

/// <summary>
/// Route parameter names the building blocks rely on.
/// </summary>
public static class RouteKeys
{
    //insight - the resource-authorization handler reads the aggregate id from this route value (IRouteProvider.GetAggregateId),
    // so every aggregate-scoped route must name its id parameter accordingly: /orders/{id:int}:submit, /orders/{id:int}/lines
    public const string AggregateId = "id";
}
