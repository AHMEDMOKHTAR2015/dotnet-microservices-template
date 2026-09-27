namespace Orders.Domain.Tests;

// Naming: {Method}_{Scenario}_{ExpectedResult}
public class OrderTests
{
    [Fact]
    public void Create_ValidInput_StartsInDraftAndRaisesOrderCreated()
    {
        var order = TestData.DraftOrder(userId: 42);

        Assert.Equal(OrderStage.Draft, order.Stage);
        Assert.Equal(42, order.CreatedById);
        Assert.Contains(order.DomainEvents, e => e is OrderCreated);
        Assert.Single(order.Actions);                                           // the create command is recorded
    }

    [Fact]
    public void AddLine_InDraft_AddsLineAndRecomputesTotal()
    {
        var order = TestData.OrderWithOneLine();

        Assert.Single(order.Lines);
        Assert.Equal(99.80m, order.Total);
        Assert.Contains(order.DomainEvents, e => e is OrderLineAdded);
    }

    [Fact]
    public void AddLine_SameProductTwice_ThrowsDomainException()
    {
        var order = TestData.OrderWithOneLine();

        Assert.Throws<DomainException>(() =>
            order.AddLine("keyboard", 1, 10m, TestData.Action(OrderActionType.AddLine), TestData.StateMachineFactory));
    }

    [Fact]
    public void AddLine_NonPositiveQuantity_ThrowsArgumentException()
    {
        var order = TestData.DraftOrder();

        Assert.Throws<ArgumentException>(() =>
            order.AddLine("Mouse", 0, 10m, TestData.Action(OrderActionType.AddLine), TestData.StateMachineFactory));
    }

    [Fact]
    public void Submit_WithoutLines_ThrowsDomainException()
    {
        var order = TestData.DraftOrder();

        Assert.Throws<DomainException>(() =>
            order.Submit(TestData.Action(OrderActionType.SubmitOrder), TestData.StateMachineFactory));
    }

    [Fact]
    public void Submit_FromDraft_MovesToSubmittedAndRecordsHistory()
    {
        var order = TestData.OrderWithOneLine();

        order.Submit(TestData.Action(OrderActionType.SubmitOrder), TestData.StateMachineFactory);

        Assert.Equal(OrderStage.Submitted, order.Stage);
        Assert.NotNull(order.SubmittedOn);
        Assert.Single(order.StageHistories);
        Assert.Contains(order.DomainEvents, e => e is StageChanged<OrderStage> { CurrentStage: OrderStage.Draft, NewStage: OrderStage.Submitted });
    }

    [Fact]
    public void Approve_FromDraft_IsRejectedByTheStateMachine()
    {
        var order = TestData.OrderWithOneLine();

        var exception = Assert.Throws<DomainException>(() =>
            order.Approve(TestData.Action(OrderActionType.ApproveOrder), TestData.StateMachineFactory));

        Assert.Contains("not allowed", exception.Message);
        Assert.Equal(OrderStage.Draft, order.Stage);                            // nothing changed
    }

    [Fact]
    public void AddLine_AfterSubmit_IsRejectedByTheStateMachine()
    {
        var order = TestData.OrderWithOneLine();
        order.Submit(TestData.Action(OrderActionType.SubmitOrder), TestData.StateMachineFactory);

        Assert.Throws<DomainException>(() =>
            order.AddLine("Mouse", 1, 10m, TestData.Action(OrderActionType.AddLine), TestData.StateMachineFactory));
    }

    [Fact]
    public void Approve_FromSubmitted_RaisesOrderApproved()
    {
        var order = TestData.OrderWithOneLine();
        order.Submit(TestData.Action(OrderActionType.SubmitOrder), TestData.StateMachineFactory);

        order.Approve(TestData.Action(OrderActionType.ApproveOrder, userId: 2), TestData.StateMachineFactory);

        Assert.Equal(OrderStage.Approved, order.Stage);
        Assert.Equal(2, order.LastModifiedById);
        Assert.Contains(order.DomainEvents, e => e is OrderApproved);
    }
}
