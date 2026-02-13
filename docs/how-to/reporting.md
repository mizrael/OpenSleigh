---
layout: default
title: Reporting & Monitoring
parent: How-To
nav_order: 6
---

# Reporting & Monitoring

The `OpenSleigh.Reporting` package provides ready-to-use ASP.NET Core Minimal API endpoints for querying saga state at runtime. This is useful for building dashboards, debugging distributed workflows, and monitoring saga progress.

## Installation

Install the NuGet package:

```
dotnet add package OpenSleigh.Reporting
```

## Setup

Register the reporting services and map the endpoints in your ASP.NET Core application:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenSleigh(cfg =>
{
    // your transport and persistence configuration
});

builder.Services.AddOpenSleighReporting();

var app = builder.Build();

app.MapOpenSleighReporting(); // default prefix: /opensleigh

app.Run();
```

You can customize the endpoint prefix:

```csharp
app.MapOpenSleighReporting("/api/sagas");
```

## REST Endpoints

All endpoints are grouped under the configured prefix (default `/opensleigh`):

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/sagas` | List saga instances (paginated, with optional filters) |
| `GET` | `/sagas/{instanceId}` | Get a saga instance by its unique ID |
| `GET` | `/sagas/correlation/{correlationId}?sagaType=...` | Get a saga instance by correlation ID and saga type |
| `GET` | `/sagas/types` | List all registered message types with saga handlers |

### Query Parameters

The **list sagas** endpoint (`GET /sagas`) supports the following query parameters:

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `sagaType` | `string?` | `null` | Filter by saga type name |
| `isCompleted` | `bool?` | `null` | Filter by completion status |
| `page` | `int` | `1` | Page number (must be ≥ 1) |
| `pageSize` | `int` | `20` | Results per page (1–100) |

The **get by correlation ID** endpoint (`GET /sagas/correlation/{correlationId}`) requires a `sagaType` query parameter.

### Response Format

The list endpoint returns a `PagedResult<SagaInstanceInfo>`:

```json
{
  "items": [
    {
      "instanceId": "0193a7e2-...",
      "correlationId": "0193a7e2-...",
      "triggerMessageId": "0193a7e2-...",
      "sagaType": "OrderSaga",
      "sagaStateType": "OrderState",
      "isCompleted": false,
      "isLocked": false,
      "processedMessages": [
        { "messageId": "0193a7e2-...", "when": "2025-01-10T12:00:00Z" }
      ],
      "stateData": { ... }
    }
  ],
  "totalCount": 42,
  "page": 1,
  "pageSize": 20
}
```

## OpenAPI Support

On .NET 9+, `AddOpenSleighReporting()` automatically registers OpenAPI services, and `MapOpenSleighReporting()` maps the OpenAPI document endpoint. The generated document is available at `/openapi/v1.json`.

## Security

{: .warning }
> The reporting endpoints expose saga state data, which may contain sensitive information. Always configure appropriate authentication and authorization middleware **before** calling `MapOpenSleighReporting()` in production environments.

For example:

```csharp
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenSleighReporting()
   .RequireAuthorization();
```

## Example: PizzaTracker Sample

The [PizzaTracker sample](https://github.com/mizrael/OpenSleigh/tree/develop/samples/OpenSleigh.Samples.PizzaTracker) demonstrates a complete setup with reporting endpoints. It uses in-memory transport and persistence with a pizza order saga that progresses through multiple stages (Received → Preparing → Baking → Quality Check → Out for Delivery → Delivered).

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenSleigh(cfg =>
{
    cfg.UseInMemoryTransport()
       .UseInMemoryPersistence()
       .AddSaga<OrderSaga, OrderState>();
});

builder.Services.AddOpenSleighReporting();

var app = builder.Build();

app.MapOpenSleighReporting();

app.MapPost("/orders", async (PlaceOrderRequest request, IMessageBus bus) =>
{
    var message = new PlaceOrder(request.CustomerName, request.PizzaType);
    await bus.PublishAsync(message);
    return Results.Accepted();
});

app.Run();
```

After placing an order, use `GET /opensleigh/sagas` to track its progress through the workflow.
