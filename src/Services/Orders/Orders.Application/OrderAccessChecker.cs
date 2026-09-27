using Microsoft.EntityFrameworkCore;
using Starter.Security;

namespace Orders.Application;

// Layer 2 of authorization, answered from this service's OWN data.
public class OrderAccessChecker(OrdersDbContext _dbContext) : IAggregateAccessChecker
{
    public async Task<bool> HasAccessAsync(int? aggregateId, int? userId, IReadOnlySet<UserRoleType> roles, CancellationToken ct = default)
    {
        if (aggregateId is null)                                              // the endpoint is not aggregate-specific
            return true;

        if (userId is null || roles.IsNullOrEmpty())
            return false;

        if (roles.Overlaps([UserRoleType.ADMIN, UserRoleType.ORDERMANAGER]))  // service-specific bypass: managers see every order
            return true;

        return await _dbContext.Orders.AsNoTracking()
            .AnyAsync(o => o.Id == aggregateId && o.CreatedById == userId, ct);   // customers only their own orders
    }
}
