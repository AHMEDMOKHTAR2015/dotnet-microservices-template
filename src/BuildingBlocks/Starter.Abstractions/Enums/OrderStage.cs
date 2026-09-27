using System.ComponentModel;

namespace Starter.Abstractions.Enums;

// Lifecycle vocabulary shared by every service that tracks an order (it travels in integration events).
// Numeric ranges per owning service keep the lifecycle ordered: 1xx Orders, 2xx the next stage's service, ...
public enum OrderStage : int
{
    None = 0,

    // Orders: 101–199
    [Description("The customer created the order")]
    Draft = 101,
    [Description("The customer submitted the order for approval")]
    Submitted = 102,
    [Description("An order manager approved the order")]
    Approved = 103,
    [Description("An order manager rejected the order")]
    Rejected = 104,
}
