using Starter.Abstractions.Enums;
using Microsoft.AspNetCore.Builder;

namespace Starter.Security;

public static class Extensions
{
    // Two layers in one call: the role itself (RequireRole) + the role on this aggregate (AggregateRoleRequirement).
    public static TBuilder RequireRoleAuthorization<TBuilder>(this TBuilder builder, params string[] roles)
        where TBuilder : IEndpointConventionBuilder
        => builder.RequireAuthorization(policy =>
        {
            policy.RequireRole(roles);
            policy.Requirements.Add(new AggregateRoleRequirement(roles));
        });

    public static TBuilder RequireRoleAuthorization<TBuilder>(this TBuilder builder, params UserRoleType[] roles)
        where TBuilder : IEndpointConventionBuilder
        => builder.RequireAuthorization(policy =>
        {
            policy.RequireRole(roles.Select(r => r.ToString()));
            policy.Requirements.Add(new AggregateRoleRequirement(roles));
        });
}
