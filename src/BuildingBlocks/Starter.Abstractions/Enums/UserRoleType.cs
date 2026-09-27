using System.ComponentModel;

namespace Starter.Abstractions.Enums;

// One closed role vocabulary for the whole system. Reserve a numeric range per domain/service so codes stay grouped
// and future services have room: 1–9 cross-domain, 11–19 Orders, 21–29 next service, ... 91–99 identity/admin.
public enum UserRoleType : int
{
    // Cross-domain: 1–9
    [Description("System Administrator")]
    ADMIN = 1,

    // Orders: 11–19
    [Description("Customer")]
    CUSTOMER = 11,
    [Description("Order Manager")]
    ORDERMANAGER = 12,
}
