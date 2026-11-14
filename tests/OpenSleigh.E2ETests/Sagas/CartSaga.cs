using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Sagas;

public record AddItemToCart(string CorrelationId, string Sku) : IMessage, IHasCorrelationId;

public record ApplyCoupon(string CorrelationId, string Code) : IMessage, IHasCorrelationId;

public class CartState
{
    public List<string> Skus { get; set; } = [];
    public List<string> Coupons { get; set; } = [];
}

/// <summary>
/// a cart that exists from whatever reaches it first: an item or a coupon. Either starts it, and none of them knows
/// whether it is the first.
/// </summary>
public class CartSaga(ISagaInstance<CartState> context, HandlerRuns runs) :
    Saga<CartState>(context),
    IStartedBy<AddItemToCart>,
    IStartedBy<ApplyCoupon>
{
    public ValueTask HandleAsync(IMessageContext<AddItemToCart> context, CancellationToken cancellationToken = default)
    {
        runs.Record(context, Context);
        Context.State.Skus.Add(context.Message.Sku);
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(IMessageContext<ApplyCoupon> context, CancellationToken cancellationToken = default)
    {
        runs.Record(context, Context);
        Context.State.Coupons.Add(context.Message.Code);
        return ValueTask.CompletedTask;
    }
}
