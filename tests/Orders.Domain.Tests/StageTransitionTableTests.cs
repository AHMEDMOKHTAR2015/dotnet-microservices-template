namespace Orders.Domain.Tests;

// Guards the lifecycle DATA: every row in the table is allowed, everything else is not.
public class StageTransitionTableTests
{
    public static TheoryData<OrderStage, OrderActionType> AllowedTransitions()
    {
        var data = new TheoryData<OrderStage, OrderActionType>();
        foreach (var transition in TestData.LoadTransitions())
            data.Add(transition.CurrentStage, transition.ActionType);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllowedTransitions))]
    public void CanFire_RowInTable_IsAllowed(OrderStage stage, OrderActionType action)
        => Assert.True(TestData.StateMachineFactory(stage).CanFire(action));

    [Fact]
    public void CanFire_PairNotInTable_IsNotAllowed()
    {
        var allowed = TestData.LoadTransitions().Select(t => (t.CurrentStage, t.ActionType)).ToHashSet();

        foreach (var stage in Enum.GetValues<OrderStage>())
            foreach (var action in Enum.GetValues<OrderActionType>())
                if (!allowed.Contains((stage, action)))
                    Assert.False(TestData.StateMachineFactory(stage).CanFire(action), $"{action} should not be allowed in {stage}");
    }
}
