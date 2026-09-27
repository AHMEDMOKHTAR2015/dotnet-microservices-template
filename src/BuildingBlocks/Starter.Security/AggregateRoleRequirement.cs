using Starter.Abstractions.Enums;
using Blocks.Core;
using Microsoft.AspNetCore.Authorization;

namespace Starter.Security;

public class AggregateRoleRequirement : IAuthorizationRequirement
{
    public IReadOnlySet<UserRoleType> AllowedRoles { get; }

    public AggregateRoleRequirement(IEnumerable<string> allowedRoles)
        => AllowedRoles = allowedRoles.Select(r => r.ToEnum<UserRoleType>()).ToHashSet();

    public AggregateRoleRequirement(IEnumerable<UserRoleType> allowedRoles)
        => AllowedRoles = allowedRoles.ToHashSet();
}
