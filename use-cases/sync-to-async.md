---
layout: default
title: Synchronous to Asynchronous
description: How to configure OpenSleigh to transform a synchronous process in an asynchronous one
---

# [Use Cases](/use-cases/) / Synchronous to Asynchronous

Imagine having an old service, exposing a complex, cumbersome, synchronous API. Maybe it's even SOAP.

**OpenSleigh** can be used to wrap this service, dispatch calls from a more modern API to this legacy service in a resilient and asynchronous way.

The basic idea is to create a regular REST API (but you can use whatever protocol you like) that receives requests, maps them to messages, and sends them to **OpenSleigh**. 

For every message, the library will spawn a Saga. This, in turn, takes care of handling requests, mapping them to the expected format, and sending them to the underline service. 

Once a request is complete, the Saga can publish an event, containing the final status.
The initial caller can be informed via webhooks or by directly subscribing to the event.