---
layout: default
title: Workflow Orchestration
description: How to configure OpenSleigh to handle Workflow Orchestration
---

# [Use Cases](/use-cases/) / Workflow Orchestration

In a regular, microservice-based e-commerce, we have multiple services involved in the Order processing workflow. When the customer clicks "Buy" and finalizes her order, there's a whole list of steps our system has to do. 

We might have, for example:

- Inventory service, called to make sure we have products in stock
- Pricing service, called to fetch the exact price of the items
- Localization service, to retrieve the localized text to send back to the customer
- Shipping service, called to calculate the final shipping price

This is, of course, an oversimplification of what could happen. There are many other steps involved, just think of Coupons, Credit check, Customer loyalty and so on.

Organizing this workflow, or multiple workflows like this can be quite overwhelming. **OpenSleigh** can be leveraged as central node, turning our architecture into an event-based, asynchronous, reactive system.

We can use a Saga to handle an `OrderPlaced` event from the main user-facing application. The event handler can send multiple commands to the underlying microservices, for example, to check the inventory and the payment and when all the validation is complete, trigger the shipping.

For a practical example, you can take a look at [Sample 4](https://github.com/mizrael/OpenSleigh/tree/develop/samples/Sample4), which implements exactly this scenario.