using Starter.Abstractions.Enums;

namespace Starter.Security;

// String constants for [Authorize(Roles = …)] and RequireRoleAuthorization(…), derived from the closed enum.
public static class Role
{
    public const string Admin = nameof(UserRoleType.ADMIN);

    public const string Customer = nameof(UserRoleType.CUSTOMER);
    public const string OrderManager = nameof(UserRoleType.ORDERMANAGER);
}
