using Blocks.Core.Security;
using Blocks.Domain;
using FastEndpoints;

namespace Blocks.FastEnpoints;

public class AssignUserIdPreProcessor : IGlobalPreProcessor
{
    public Task PreProcessAsync(IPreProcessorContext context, CancellationToken ct)
    {
        if (context.Request is IAuditableAction auditableAction)
        {
            var claimsProvider = context.HttpContext.Resolve<IClaimsProvider>();
            auditableAction.CreatedById = claimsProvider.GetUserId();
        }

        return Task.CompletedTask;
    }
}
