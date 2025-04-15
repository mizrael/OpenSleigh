# OpenSleigh ECommerce Sample

This sample shows how to coordinate multiple microservices. It simulates an ecommerce application placing an order and follows it through several steps:
- inventory check
- credit check
- shipping

The order can be placed with an API call. This will trigger the `OrderSaga` in the Orchestrator project via the `SaveOrder` message. 

## Prerequisites

Docker is required to run this sample. The docker compose file will spin up a Postgres database and a RabbitMQ server.

## Running the sample

Load the solution file in Visual Studio and run the followin projects:
- API
- InventoryService
- NotificationsService
- Orchestrator
- PaymentService
- ShippingService