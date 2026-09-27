using Starter.Abstractions.Enums;
using Blocks.AspNetCore;
using Microsoft.AspNetCore.Authorization;

namespace Starter.Security;

// Layer 2 of authorization: "does the user hold one of the allowed roles ON THIS aggregate?"
// The aggregate id comes from the route value named RouteKeys.AggregateId ("id").
public class AggregateAccessAuthorizationHandler(HttpContextProvider _httpProvider, IAggregateAccessChecker _accessChecker)
    : AuthorizationHandler<AggregateRoleRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AggregateRoleRequirement requirement)
    {
        var userRoles = _httpProvider.GetUserRoles<UserRoleType>()
                            .Where(requirement.AllowedRoles.Contains)
                            .ToHashSet();

        if (userRoles.Count > 0 && await HasUserRoleForAggregate(userRoles))
            context.Succeed(requirement);
    }

    private async Task<bool> HasUserRoleForAggregate(IReadOnlySet<UserRoleType> userRoles)
        => await _accessChecker.HasAccessAsync(
            _httpProvider.GetAggregateId(), _httpProvider.TryGetUserId(), userRoles, _httpProvider.HttpContext.RequestAborted);
}
