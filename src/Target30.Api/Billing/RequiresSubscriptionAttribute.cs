using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Target30.Api.Billing;

// Marca ações que custam dinheiro (conectar/sincronizar bancos). Sem acesso: 402 Payment Required.
public class RequiresSubscriptionAttribute : TypeFilterAttribute
{
    public RequiresSubscriptionAttribute() : base(typeof(RequiresSubscriptionFilter))
    {
    }
}

public class RequiresSubscriptionFilter : IAsyncActionFilter
{
    private readonly BillingService _billing;

    public RequiresSubscriptionFilter(BillingService billing) => _billing = billing;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            await next();
            return;
        }

        var (_, access) = await _billing.EvaluateAsync(userId, user.FindFirstValue(ClaimTypes.Email));
        if (!access.HasAccess)
        {
            context.Result = new ObjectResult(new { code = "subscription_required", reason = access.Reason })
            {
                StatusCode = StatusCodes.Status402PaymentRequired,
            };
            return;
        }

        await next();
    }
}
