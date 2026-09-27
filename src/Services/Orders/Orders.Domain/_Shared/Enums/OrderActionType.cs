namespace Orders.Domain.Shared.Enums;

// One verb per command. It is the audit "what" and the state-machine trigger.
public enum OrderActionType
{
    CreateOrder,
    AddLine,
    SubmitOrder,
    ApproveOrder,
    RejectOrder
}
