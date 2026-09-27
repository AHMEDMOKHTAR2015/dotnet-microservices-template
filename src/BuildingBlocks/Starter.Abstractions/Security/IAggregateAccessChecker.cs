using Starter.Abstractions.Enums;

namespace Starter.Security;

// Implemented once per service against that service's OWN data (never a synchronous cross-service call).
public interface IAggregateAccessChecker
{
    Task<bool> HasAccessAsync(int? aggregateId, int? userId, IReadOnlySet<UserRoleType> roles, CancellationToken ct = default);
}
