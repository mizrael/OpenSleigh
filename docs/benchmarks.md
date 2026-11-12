---
layout: default
title: Benchmarks
nav_order: 4
---

# Benchmarks

Every commit that lands on `develop` is benchmarked on CircleCI against real SQL Server, PostgreSQL, MongoDB, RabbitMQ and Kafka containers. This page shows the most recent runs.

> **These are not capacity figures.** The numbers come from a shared CI runner that hosts the benchmark and all five services at once. They are useful for comparing one commit with another; expect different absolute numbers on your hardware.

How to read them:

- Throughput scenarios (`msg/s`, `sagas/s`) are better when higher; time scenarios (`ms`) are better when lower.
- Each run measures a commit and the commit before it in the same job, in alternating rounds, so the *Change* column compares two builds on the same machine at the same time.
- Runs of different commits happen on different runners. Small differences between them are noise.
- CircleCI keeps the results of a run for a limited time (30 days by default), so the history only reaches that far back.

The harness, the scenarios and the pull request gate that uses them are described in the [benchmarks README](https://github.com/mizrael/OpenSleigh/blob/develop/benchmarks/README.md).

{% include benchmark-results.md %}
