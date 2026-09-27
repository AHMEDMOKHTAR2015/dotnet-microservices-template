# Orders Service (sample)

**Endpoint framework:** Minimal APIs + MediatR
**Database:** SQL Server (OrdersDb)
**Port:** 4401 / 4451
**Domain-event dispatch:** post-save interceptor (`DispatchDomainEventsInterceptor`)

## Purpose

Reference slice of the template: shows every pattern end to end. Replace or delete once your first real service exists.

## Domain model

- **Aggregate:** `Order` (state in `Orders/Order.cs`, behavior in `Orders/Behaviors/Order.cs`)
- **Entities:** `OrderLine` (created only through `Order.AddLine`), `OrderAction` (audit), `StageHistory`
- **Value object:** `EmailAddress`
- **Enum entity:** `Stage : EnumEntity<OrderStage>`
- **Lifecycle:** Draft → Submitted → Approved | Rejected, as data in `Orders.Persistence/Data/Master/StageTransition.json`

## Existing features

CreateOrder, AddOrderLine, SubmitOrder, ApproveOrder (+ integration event `OrderApprovedEvent`, confirmation email), RejectOrder, GetOrder

## Authorization

Customers act on their own orders (`OrderAccessChecker`: `CreatedById == userId`); `ORDERMANAGER`/`ADMIN` bypass.
