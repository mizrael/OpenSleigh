# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository Overview

OpenSleigh is a distributed saga management library for .NET that enables reliable, event-driven orchestration across microservices. It implements the saga pattern with choreography using an outbox pattern for guaranteed message delivery.

**Key Facts:**
- **Language:** C# 12 with nullable reference types enabled
- **Target Frameworks:** net8.0 and net9.0 (multi-targeting)
- **Main Solution:** `src/OpenSleigh.sln`
- **Primary Branch:** `develop` (use this for PRs, not main/master)
- **License:** Apache 2.0
- **Current Version:** 3.0.6 (defined in `Versions.props`)

## Build and Test Commands

**CRITICAL: Always work from the `src/` directory for build operations.**

### Standard Development Workflow

```bash
cd src/

# 1. Restore dependencies (ALWAYS run first to avoid package resolution issues)
dotnet restore

# 2. Build the solution
dotnet build -c Release        # Expect ~174 nullable warnings (acceptable), 0 errors required

# 3. Run unit tests (~105 tests, no infrastructure needed, ~5-10 seconds)
dotnet test --framework net9.0 --filter "Category!=E2E&Category!=Integration"

# 4. Run a specific test class or method
dotnet test --framework net9.0 --filter "FullyQualifiedName~SagaRunnerTests"

# 5. Verify code formatting
dotnet format --verify-no-changes

# 6. Create NuGet packages (outputs to repo root packages/ directory)
dotnet pack -c Release
```

### Integration Tests

Integration tests require Docker infrastructure (MongoDB, RabbitMQ, Kafka, SQL Server, PostgreSQL):

```bash
# From repo root, start infrastructure
cd tests/infra
docker-compose up -d           # Wait 10-15 seconds for services to initialize

# Return to src/ and run integration tests (~20-30 seconds)
cd ../../src
dotnet test --framework net9.0 --filter "FullyQualifiedName!~Cosmos&Category=Integration"

# If tests fail, clean up and restart infrastructure
cd ../tests/infra
docker-compose down && docker-compose up -d
```

**Docker credentials** (for debugging connection issues):
- SQL Server: `SA_PASSWORD=Sup3r_p4ssword123`
- PostgreSQL: `POSTGRES_PASSWORD=Sup3r_p4ssword123`
- RabbitMQ vhost: `/opensleigh-tests`

**Note:** Cosmos tests are excluded (not fully implemented). E2E tests are disabled in CI.

## High-Level Architecture

### Core Concepts

**Saga:** A long-running, distributed transaction coordinated through messages. Each saga instance maintains state and reacts to messages by executing handlers and publishing new messages.

**Outbox Pattern:** All messages published by sagas are stored in an outbox table/collection and processed asynchronously by a background service. This ensures at-least-once delivery with exactly-once processing guarantees.

**Correlation:** Each saga flow is identified by a `CorrelationId` that flows through all related messages, enabling distributed tracing and state management.

### Three-Layer Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Application Layer                         │
│  - Saga implementations (ISaga<TState>)                      │
│  - Message handlers (IHandleMessage<TMessage>)               │
│  - Message definitions (IMessage)                            │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌─────────────────────────────────────────────────────────────┐
│                    OpenSleigh Core                           │
│  - Saga orchestration (SagaRunner, SagaExecutionService)     │
│  - Message processing pipeline (MessageProcessor)            │
│  - Outbox management (OutboxBackgroundService)               │
│  - Instance lifecycle (SagaInstance, SagaInstanceFactory)    │
└─────────────────────────────────────────────────────────────┘
                            ↓
┌────────────────────────────┬────────────────────────────────┐
│   Persistence Layer         │     Transport Layer            │
│  - ISagaStateRepository     │  - IMessageSubscriber          │
│  - IOutboxRepository        │  - Message routing             │
│  - Implementations:         │  - Implementations:            │
│    • Mongo                  │    • RabbitMQ                  │
│    • PostgreSQL             │    • Kafka                     │
│    • SQL Server             │                                │
│    • InMemory               │                                │
└────────────────────────────┴────────────────────────────────┘
```

### Key Abstractions and Their Locations

**Saga Definition:**
- `ISaga` / `ISaga<TState>`: Marker interface for sagas (src/OpenSleigh/ISaga.cs)
- `Saga<TState>`: Base class providing `Publish<TMessage>()` method (src/OpenSleigh/Saga.cs)
- `IStartedBy<TMessage>`: Marks the message that initiates a saga (src/OpenSleigh/Transport/IStartedBy.cs)
- `IHandleMessage<TMessage>`: Interface for handling messages (src/OpenSleigh/Transport/IHandleMessage.cs)

**Saga Lifecycle:**
- `ISagaInstance` / `SagaInstance<TState>`: Execution context with state, outbox, correlation tracking, and lock management (src/OpenSleigh/ISagaInstance.cs, SagaInstance.cs)
- `ISagaInstanceFactory`: Creates new saga instances with Guid v7 IDs (src/OpenSleigh/SagaInstanceFactory.cs)
- `SagaDescriptor`: Metadata describing saga type, initiator message, and state type (src/OpenSleigh/SagaDescriptor.cs)

**Message Processing Pipeline:**
1. **MessageProcessor**: Entry point that routes messages from outbox to applicable sagas (src/OpenSleigh/Transport/MessageProcessor.cs)
2. **SagaRunner**: Orchestrates processing for a single saga/message pair (src/OpenSleigh/SagaRunner.cs)
3. **SagaExecutionService**: Two-phase execution: BeginProcessingAsync (fetch/lock) + CommitAsync (persist/unlock) (src/OpenSleigh/SagaExecutionService.cs)
4. **MessageHandlerManager**: Invokes the actual handler with error handling and rollback (src/OpenSleigh/MessageHandlerManager.cs)

**Outbox Pattern:**
- `MessageEnvelope`: Wraps messages with metadata (correlation ID, message ID, sender) (src/OpenSleigh/Outbox/MessageEnvelope.cs)
- `IOutboxRepository`: Persistence for pending messages with AppendAsync/ReadPendingAsync/DeleteAsync (src/OpenSleigh/Outbox/IOutboxRepository.cs)
- `OutboxBackgroundService`: Polls outbox repository at configured intervals (src/OpenSleigh/Outbox/OutboxBackgroundService.cs)

**Configuration:**
- `ServiceCollectionExtensions.AddOpenSleigh()`: Single entry point for DI registration (src/OpenSleigh/DependencyInjection/ServiceCollectionExtensions.cs)
- `BusConfigurator`: Fluent builder for saga registration with `.AddSaga<TSaga>()` or `.AddSaga<TSaga, TState>()` (src/OpenSleigh/DependencyInjection/BusConfigurator.cs)
- `SetPublishOnly()`: Configures publish-only mode (no message processing, useful for API endpoints that only publish messages)

### Critical Patterns

**1. Idempotency**
- Sagas track processed messages in `SagaInstance.ProcessedMessages` dictionary
- `IIdempotentMessage` interface allows custom idempotency keys via SHA256 hash
- `NoOpSagaInstance` returned for duplicate messages (src/OpenSleigh/NoOpSagaInstance.cs)
- Prevents double-processing even with at-least-once delivery

**2. Pessimistic Locking**
- `ISagaStateRepository.LockAsync()` acquires lock, returns lock ID stored in saga instance
- Lock held during entire message processing
- Released via `ReleaseAsync()` in commit phase
- Prevents concurrent processing of same saga instance

**3. Message Correlation**
- `CorrelationId` flows through all related messages in a saga flow
- First message (IStartedBy) establishes correlation ID
- All published messages inherit correlation ID from saga instance
- Enables distributed tracing and saga instance routing

**4. Two-Phase Processing**
- Phase 1: `BeginProcessingAsync()` - fetch/lock saga, check idempotency, execute handler
- Phase 2: `CommitAsync()` - persist state and outbox messages, release lock
- Allows rollback without persisting partial state

**5. Discovery via Reflection**
- `TypeExtensions.GetHandledMessageTypes()` discovers all `IHandleMessage<>` implementations
- `TypeExtensions.GetInitiatorMessageType()` finds the `IStartedBy<>` message
- `SagaDescriptorsResolver` builds message-to-saga mappings at startup

### Message Lifecycle Flow

Understanding how messages flow through the system:

```
1. Application publishes message via IMessageBus.PublishAsync()
   ↓
2. Message wrapped in MessageEnvelope and stored in IOutboxRepository
   ↓
3. OutboxBackgroundService polls outbox at configured interval
   ↓
4. MessageProcessor routes message to applicable sagas (multiple sagas can handle same message)
   ↓
5. For each saga: SagaRunner → SagaExecutionService.BeginProcessingAsync()
   ↓
6. BeginProcessingAsync: fetch/create saga instance → check idempotency → acquire lock
   ↓
7. SagaInstance.ProcessAsync() → MessageHandlerManager → IHandleMessage.HandleAsync()
   ↓
8. Handler publishes new messages via Saga.Publish() (queued in instance outbox)
   ↓
9. SagaExecutionService.CommitAsync() → persist state + outbox messages → release lock
   ↓
10. Outbox messages picked up in next poll cycle (back to step 3)
```

### Extension Points

**For Persistence:** Implement `ISagaStateRepository` and `IOutboxRepository`, create `IBusConfigurator` extension method (see `OpenSleigh.Persistence.Mongo` for example)

**For Transport:** Implement `IMessageSubscriber` (start/stop subscription), create message parser to `MessageEnvelope`, route via `IMessageProcessor` (see `OpenSleigh.Transport.RabbitMQ` for example)

## Project Structure

### Source Projects (src/)

- **OpenSleigh/** - Core library with saga orchestration, message processing, DI setup
- **OpenSleigh.InMemory/** - In-memory implementations for development/testing

**Persistence Implementations:**
- **OpenSleigh.Persistence.SQL/** - Base SQL abstractions (Entity Framework Core)
- **OpenSleigh.Persistence.SQLServer/** - SQL Server provider
- **OpenSleigh.Persistence.PostgreSQL/** - PostgreSQL provider
- **OpenSleigh.Persistence.Mongo/** - MongoDB provider (no EF Core, uses MongoDB.Driver)

**Transport Implementations:**
- **OpenSleigh.Transport.RabbitMQ/** - RabbitMQ transport with persistent connections and channel pooling
- **OpenSleigh.Transport.Kafka/** - Kafka transport with consumer groups

### Test Projects (tests/)

- **OpenSleigh.Tests/** - Unit tests (no external dependencies)
- **OpenSleigh.E2ETests/** - End-to-end tests (currently disabled in CI)
- **OpenSleigh.Persistence.*.Tests/** - Persistence layer integration tests (require Docker)
- **OpenSleigh.Transport.*.Tests/** - Transport layer integration tests (require Docker)
- **tests/infra/docker-compose.yml** - Infrastructure services for integration tests

### Sample Projects (samples/)

- **OpenSleigh.Samples.Sample1/** - Basic single-service saga example
- **OpenSleigh.Samples.Sample2/** - Multi-service example with API and Worker
- **OpenSleigh.Samples.ECommerce/** - Complex distributed saga example (Orchestrator, Payment, Shipping, Inventory, Notifications)
- **OpenSleigh.Samples.Blazor/** - Web UI example with Blazor

Samples demonstrate various persistence and transport configurations. Use as reference for integration patterns.

## Debugging and Testing

### Debugging Saga Execution Issues

1. **Check logs** - `SagaRunner` and `MessageProcessor` have extensive logging for message routing and handler invocation
2. **Verify saga registration** - Ensure saga appears in `SagaDescriptorsResolver` message-to-saga mappings
3. **Trace correlation IDs** - Follow `CorrelationId` through message flow to identify where saga instance diverges
4. **Check locks** - Verify `ISagaStateRepository.LockAsync()`/`ReleaseAsync()` calls; stuck locks prevent processing
5. **Inspect outbox** - Query `IOutboxRepository` for pending messages; background service polls at configured interval
6. **Verify idempotency** - Check if message already in `ProcessedMessages` dictionary (would return `NoOpSagaInstance`)

### Test Organization

Tests use xUnit with NSubstitute for mocking. Categorize with traits:
- `[Trait("Category", "Integration")]` - Requires Docker infrastructure
- `[Trait("Category", "E2E")]` - End-to-end scenarios (currently disabled in CI)

### Code Coverage

Enforced via codecov.yaml: **70% minimum** for both project and patch coverage, with 1% threshold.

### CI/CD

- **CircleCI** (primary): Runs build + unit tests + integration tests on every push; SonarCloud quality scan
- **GitHub Actions**: CodeQL security scanning (develop/releases branches); NuGet publishing (manual/releases)

## Critical Reminders

1. **Multi-targeting requires `--framework net9.0`** when running tests/commands to avoid ambiguity
2. **Persistence implementations differ** - SQL providers use EF Core with transactions; Mongo uses native driver without EF
3. **Saga state serialization** - State classes must be serializable (used by persistence layer); avoid circular references
4. **Lock lifetime** - Locks are held for entire message processing; long-running handlers can create bottlenecks
5. **Outbox polling interval** - Default configured via `OutboxProcessorOptions.Interval`; balance responsiveness vs. database load
6. **Message routing is many-to-many** - One message type can trigger multiple sagas; one saga can handle multiple message types
7. **Correlation ID immutability** - Once set on saga instance, correlation ID never changes; all published messages inherit it
8. **Do not modify** `Versions.props`, `Directory.Build.props`, or `test.runsettings` unless the task specifically requires it

## Contributing

Discuss changes via issue before implementing. Fork from **`develop`** branch (not main). Add tests for new code. Ensure `dotnet format` passes. Submit PR to `develop`.

## Git Workflow
- Every new implementation MUST happen in a feature branch
- Never commit new features directly to main
- If already on a feature branch, ask the user whether a sub-feature branch is needed before starting new work
- NEVER commit changes - the user will commit manually after code review
- **MANDATORY: Always create a Pull Request to merge into main** - never merge directly to main, even locally

## Code Review
- After completing any implementation, run the `superpowers:requesting-code-review` skill to review the changes
- When writing implementation plans, include a final task for code review using the `superpowers:code-reviewer` subagent
- Code review should check: plan compliance, code quality, architecture, and codebase conventions
- Address Critical and Important issues before considering work complete

## Coding Conventions
- write comments only when strictly necessary (eg. the implementation is not obvious and the code is not self-explanatory)
- prefer clear and descriptive names for classes, methods, variables
- organize code into small, single-responsibility methods
- use consistent formatting and indentation
- follow SOLID principles and best practices
- classes should be small and focused on a single responsibility
- avoid magic strings; use `const string`, `nameof()`, or similar approaches instead
- favor interfaces over concrete implementations (e.g., `IEnumerable<T>` over `List<T>`, `IReadOnlyList<T>` over arrays)