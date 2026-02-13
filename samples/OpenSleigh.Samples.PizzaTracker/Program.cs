using OpenSleigh.DependencyInjection;
using OpenSleigh.InMemory;
using OpenSleigh.Reporting;
using OpenSleigh.Samples.PizzaTracker;
using OpenSleigh.Transport;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenSleigh(cfg =>
{
    cfg.UseInMemoryTransport()
       .UseInMemoryPersistence()
       .AddSaga<OrderSaga, OrderState>();
});

builder.Services.AddOpenSleighReporting();

var app = builder.Build();

// OpenSleigh reporting endpoints + OpenAPI document at /openapi/v1.json
app.MapOpenSleighReporting();

// POST /orders — place a new pizza order
app.MapPost("/orders", async (PlaceOrderRequest request, IMessageBus bus) =>
{
    var message = new PlaceOrder(request.CustomerName, request.PizzaType);
    await bus.PublishAsync(message);

    return Results.Accepted(value: new
    {
        Message = $"Order placed! {request.PizzaType} for {request.CustomerName}.",
        Tip = "Use GET /opensleigh/sagas to track all orders, or GET /opensleigh/sagas/{instanceId} for a specific order."
    });
})
.WithTags("Orders")
.WithSummary("Place a pizza order")
.WithDescription("Publishes a PlaceOrder message that starts the OrderSaga.");

app.Run();

public record PlaceOrderRequest(string CustomerName, string PizzaType);
