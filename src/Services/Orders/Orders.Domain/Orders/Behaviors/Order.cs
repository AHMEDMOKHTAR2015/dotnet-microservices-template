namespace Orders.Domain.Orders;

public partial class Order
{
    public const int MaxLines = 50;

    public static Order Create(string customerName, EmailAddress customerEmail, IOrderAction action)
    {
        //insight - initialization is not a transition: the order starts in Draft without a StageChanged event
        var order = new Order
        {
            CustomerName = customerName,
            CustomerEmail = customerEmail,
            Stage = OrderStage.Draft,
            CreatedById = action.CreatedById,
            CreatedOn = action.CreatedOn
        };

        order.AddDomainEvent(new OrderCreated(order, action));
        order.AddAction(action);
        return order;
    }

    public OrderLine AddLine(string productName, int quantity, decimal unitPrice, IOrderAction action, OrderStateMachineFactory stateMachineFactory)
    {
        SetStage(Stage, action, stateMachineFactory);            // validates that AddLine is legal in the current stage (re-entry)

        if (_lines.Exists(l => l.ProductName.Equals(productName.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new DomainException($"Product '{productName}' is already on the order; change its quantity instead");

        if (_lines.Count >= MaxLines)
            throw new DomainException($"An order cannot have more than {MaxLines} lines");

        var line = OrderLine.Create(this, productName, quantity, unitPrice);
        _lines.Add(line);

        AddDomainEvent(new OrderLineAdded(this, line, action));
        AddAction(action);
        return line;
    }

    public void Submit(IOrderAction action, OrderStateMachineFactory stateMachineFactory)
    {
        if (_lines.Count == 0)
            throw new DomainException("Cannot submit an order without lines");

        SetStage(OrderStage.Submitted, action, stateMachineFactory);
        SubmittedOn = action.CreatedOn;

        AddDomainEvent(new OrderSubmitted(this, action));
        AddAction(action);
    }

    public void Approve(IOrderAction action, OrderStateMachineFactory stateMachineFactory)
    {
        SetStage(OrderStage.Approved, action, stateMachineFactory);

        AddDomainEvent(new OrderApproved(this, action));
        AddAction(action);
    }

    public void Reject(IOrderAction action, OrderStateMachineFactory stateMachineFactory)
    {
        SetStage(OrderStage.Rejected, action, stateMachineFactory);

        AddDomainEvent(new OrderRejected(this, action));
        AddAction(action);
    }

    // The ONLY place the stage changes. It validates first, always (also for re-entry).
    private void SetStage(OrderStage newStage, IOrderAction action, OrderStateMachineFactory stateMachineFactory)
    {
        stateMachineFactory.ValidateStageTransition(Stage, action.ActionType);

        if (newStage == Stage)
            return;

        var currentStage = Stage;
        Stage = newStage;
        LastModifiedOn = action.CreatedOn;
        LastModifiedById = action.CreatedById;

        _stageHistories.Add(new StageHistory { OrderId = Id, StageId = newStage, StartDate = action.CreatedOn });
        AddDomainEvent(new StageChanged<OrderStage>(currentStage, newStage, action));
    }

    private void AddAction(IOrderAction action)
    {
        _actions.Add(OrderAction.From(action));                  // the command becomes an audit row
        AddDomainEvent(new OrderActionExecuted(this, action));
    }
}
