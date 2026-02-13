# OpenSleigh.Reporting

ASP.NET Core Minimal API endpoints for monitoring and querying [OpenSleigh](https://github.com/mizrael/OpenSleigh) saga state at runtime.

## Installation

```bash
dotnet add package OpenSleigh.Reporting
```

## Setup

Register reporting services and map endpoints in your `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOpenSleigh(cfg =>
    {
        // ... configure sagas and persistence ...
    })
    .AddOpenSleighReporting();

var app = builder.Build();

app.MapOpenSleighReporting(); // default prefix: /opensleigh

app.Run();
```

You can customise the route prefix:

```csharp
app.MapOpenSleighReporting(prefix: "/api/sagas");
```

## REST Endpoints

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/opensleigh/sagas` | List saga instances (paginated, filterable) |
| `GET` | `/opensleigh/sagas/{instanceId}` | Get a saga instance by its ID |
| `GET` | `/opensleigh/sagas/correlation/{correlationId}?sagaType=` | Get a saga instance by correlation ID (requires `sagaType` query parameter) |
| `GET` | `/opensleigh/sagas/types` | List registered message types with saga handlers |

### Query Parameters

The **list** endpoint (`/opensleigh/sagas`) supports:

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `page` | `int` | `1` | Page number (must be ≥ 1) |
| `pageSize` | `int` | `20` | Items per page (1–100) |
| `sagaType` | `string?` | — | Filter by saga type name |
| `isCompleted` | `bool?` | — | Filter by completion status |

### Response Shape

List queries return a `PagedResult<SagaInstanceInfo>`:

```json
{
  "items": [
    {
      "instanceId": "...",
      "correlationId": "...",
      "triggerMessageId": "...",
      "sagaType": "OrderSaga",
      "sagaStateType": "OrderSagaState",
      "isCompleted": false,
      "isLocked": false,
      "processedMessages": [
        { "messageId": "...", "when": "2025-01-15T10:30:00Z" }
      ],
      "stateData": { /* saga-specific state */ }
    }
  ],
  "totalCount": 42,
  "page": 1,
  "pageSize": 20
}
```

## Security

> **⚠️ Warning:** These endpoints expose saga state data, which may contain sensitive information. Add authentication and authorization middleware **before** calling `MapOpenSleighReporting()` in production environments.

```csharp
app.MapOpenSleighReporting()
   .RequireAuthorization("AdminPolicy");
```

## OpenAPI Support

On **.NET 9+**, `AddOpenSleighReporting()` automatically registers OpenAPI services and `MapOpenSleighReporting()` maps the OpenAPI document endpoint. You can browse the generated API description at `/openapi/v1.json`.

## Documentation

For full documentation, samples, and configuration guides, see the [OpenSleigh repository](https://github.com/mizrael/OpenSleigh).
