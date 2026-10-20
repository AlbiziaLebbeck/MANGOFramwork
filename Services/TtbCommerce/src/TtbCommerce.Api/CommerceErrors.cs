using Microsoft.AspNetCore.Diagnostics;
using TtbCommerce.Core;

namespace TtbCommerce.Api;

public sealed class CommerceErrors(IProblemDetailsService problems, ILogger<CommerceErrors> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not CommerceException commerce) return false;
        context.Response.StatusCode = commerce.Status;
        if (commerce.Status >= 500) logger.LogWarning("Commerce service unavailable: {Code}, trace {TraceId}", commerce.Code, context.TraceIdentifier);
        return await problems.TryWriteAsync(new()
        {
            HttpContext = context,
            ProblemDetails = new()
            {
                Status = commerce.Status, Title = commerce.Message,
                Extensions = { ["code"] = commerce.Code, ["traceId"] = context.TraceIdentifier }
            }
        });
    }
}
