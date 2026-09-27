using Microsoft.Extensions.Options;
using EmailService.Contracts;
using Orders.Domain.Orders.Events;
using EmailAddress = EmailService.Contracts.EmailAddress;

namespace Orders.Application.Features.ApproveOrder;

// In-service reaction to a domain event: {Effect}On{Event}Handler, colocated with the slice that raises it.
public class SendConfirmationEmailOnOrderApprovedHandler(IEmailService _emailService, IOptions<EmailOptions> _emailOptions)
    : INotificationHandler<OrderApproved>
{
    public async Task Handle(OrderApproved notification, CancellationToken ct)
    {
        var order = notification.Order;
        const string Body = "Dear {0},<br/>Your order #{1} ({2:C}) has been approved.";

        await _emailService.SendEmailAsync(new EmailMessage(
            "Your order was approved",
            new Content(ContentType.Html, string.Format(Body, order.CustomerName, order.Id, order.Total)),
            new EmailAddress("orders", _emailOptions.Value.EmailFromAddress),
            new List<EmailAddress> { new EmailAddress(order.CustomerName, order.CustomerEmail.Value) }), ct);
    }
}
