# 🍕 Pizza Order Tracker

A sample application demonstrating OpenSleigh's **saga state query API and reporting endpoints**. 

Place pizza orders via a REST API and watch them progress through a multi-step saga in real time — from order received through preparation, baking, quality check, delivery, and completion.

## Running the Sample

```bash
cd samples/OpenSleigh.Samples.PizzaTracker
dotnet run
```

The application starts on `http://localhost:5200`.

## API Endpoints

### Place an Order

```bash
curl -X POST http://localhost:5200/orders \
  -H "Content-Type: application/json" \
  -d '{"customerName": "Alice", "pizzaType": "Margherita"}'
```

### Query All Saga Instances

```bash
# All orders
curl http://localhost:5200/opensleigh/sagas

# Only active (in-progress) orders
curl "http://localhost:5200/opensleigh/sagas?isCompleted=false"

# Only completed orders
curl "http://localhost:5200/opensleigh/sagas?isCompleted=true"

# With pagination
curl "http://localhost:5200/opensleigh/sagas?page=1&pageSize=5"
```

### Query a Specific Order by Instance ID

```bash
curl http://localhost:5200/opensleigh/sagas/{instanceId}
```

The response includes the full `StateData` — the live `OrderState` object showing the current step, timestamps for each phase, customer name, and pizza type.

### Query by Correlation ID

```bash
curl "http://localhost:5200/opensleigh/sagas/correlation/{correlationId}?sagaType=OrderSaga"
```

### List Registered Saga Types

```bash
curl http://localhost:5200/opensleigh/sagas/types
```

## Order Saga Flow

```
PlaceOrder → PreparePizza → BakePizza → QualityCheck → SendOutForDelivery → ConfirmDelivery
   📥            👨‍🍳            🔥            ✅               🚗                🎉
```

Each step takes ~2 seconds (simulated), so you can query the reporting endpoints mid-flow to see the saga's intermediate state.

## What This Demonstrates

- **Saga pattern** with multi-step state transitions
- **`ISagaStateQuery`** for querying saga instances at runtime
- **`MapOpenSleighReporting()`** for exposing saga state via Minimal API endpoints
- **`StateData`** showing the live saga state object (customer name, pizza type, current status, timestamps)
- **Filtering and pagination** on the query endpoints
