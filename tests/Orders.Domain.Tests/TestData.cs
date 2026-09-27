using Blocks.Core;
using Microsoft.Extensions.Caching.Memory;
using Orders.Application.StateMachines;

namespace Orders.Domain.Tests;

// A command is also the audit record the domain needs: any IOrderAction works as a test input.
public record TestAction(OrderActionType Type, int UserId = 1) : AggregateCommandBase<OrderActionType>, IOrderAction
{
    public override OrderActionType ActionType => Type;
    public TestAction Stamped() { CreatedById = UserId; return this; }
}

public static class TestData
{
    // The production state machine, fed from the production transition table (Data/Master/StageTransition.json).
    public static OrderStateMachineFactory StateMachineFactory { get; } = CreateFactory();

    public static IOrderAction Action(OrderActionType type, int userId = 1) => new TestAction(type, userId).Stamped();

    public static Order DraftOrder(int userId = 1)
        => Order.Create("Ada Lovelace", EmailAddress.Create("ada@example.com"), Action(OrderActionType.CreateOrder, userId));

    public static Order OrderWithOneLine()
    {
        var order = DraftOrder();
        order.AddLine("Keyboard", 2, 49.90m, Action(OrderActionType.AddLine), StateMachineFactory);
        return order;
    }

    public static List<StageTransition> LoadTransitions()
        => JsonExtensions.DeserializeCaseInsensitive<List<StageTransition>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "StageTransition.json")));

    private static OrderStateMachineFactory CreateFactory()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set(typeof(List<StageTransition>).FullName!, LoadTransitions());   // same key GetAllCached<StageTransition>() uses
        return stage => new OrderStateMachine(stage, cache);
    }
}
