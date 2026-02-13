---
layout: default
title: Use Cases
nav_order: 2
---

# Use Cases

## Workflow Orchestration

In a regular, microservice-based e-commerce, we have multiple services involved in the Order processing workflow. When the customer clicks "Buy" and finalizes her order, there's a whole list of steps our system has to do.

We might have, for example:

- Inventory service, called to make sure we have products in stock
- Pricing service, called to fetch the exact price of the items
- Localization service, to retrieve the localized text to send back to the customer
- Shipping service, called to calculate the final shipping price

This is, of course, an oversimplification of what could happen. There are many other steps involved, just think of Coupons, Credit check, Customer loyalty and so on.

Organizing this workflow, or multiple workflows like this can be quite overwhelming. **OpenSleigh** can be leveraged as central node, turning our architecture into an event-based, asynchronous, reactive system.

We can use a Saga to handle an `OrderPlaced` event from the main user-facing application. The event handler can send multiple commands to the underlying microservices, for example, to check the inventory and the payment and when all the validation is complete, trigger the shipping.

For a practical example, you can take a look at the [E-Commerce Sample](https://github.com/mizrael/OpenSleigh/tree/develop/samples/OpenSleigh.Samples.ECommerce), which implements exactly this scenario.

## Synchronous to Asynchronous

Imagine having an old service, exposing a complex, cumbersome, synchronous API. Maybe it's even SOAP.

**OpenSleigh** can be used to wrap this service, dispatch calls from a more modern API to this legacy service in a resilient and asynchronous way.

The basic idea is to create a regular REST API (but you can use whatever protocol you like) that receives requests, maps them to messages, and sends them to **OpenSleigh**.

For every message, the library will spawn a Saga. This, in turn, takes care of handling requests, mapping them to the expected format, and sending them to the underline service.

Once a request is complete, the Saga can publish an event, containing the final status. The initial caller can be informed via webhooks or by directly subscribing to the event.
