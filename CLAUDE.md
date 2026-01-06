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
# Navigate to source directory (required)
cd src/

# 1. Restore dependencies (ALWAYS run first)
dotnet restore

# 2. Build the solution
dotnet build                    # Debug build
dotnet build -c Release        # Release build (recommended)

# 3. Run unit tests (no infrastructure needed)
dotnet test --framework net9.0 --filter "Category!=E2E&Category!=Integration"

# 4. Check code formatting
dotnet format --verify-no-changes

# 5. Apply formatting
dotnet format

# 6. Create NuGet packages (output to repo root packages/ directory)
dotnet pack -c Release
```

### Integration Tests

Integration tests require Docker infrastructure:

```bash
# From repo root, start infrastructure
cd tests/infra
docker-compose up -d

# Wait 10-15 seconds for services to initialize

# Return to src/ and run integration tests
cd ../../src
dotnet test --framework net9.0 --filter "FullyQualifiedName!~Cosmos&Category=Integration"
```

**Infrastructure Services:**
- MongoDB (27017)
- RabbitMQ (5672, 15672 management UI)
- Kafka (9092)
- SQL Server (1433, password: `Sup3r_p4ssword123`)
- PostgreSQL (5432, password: `Sup3r_p4ssword123`)

### Known Build Warnings

**Expect ~174 compiler warnings** (mostly nullable reference warnings: CS8602, CS8625, CS8618, CS8604). These are known and acceptable. The build must complete with **0 errors**.

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

### Extension Points

When adding new persistence or transport implementations:

**For Persistence:**
1. Implement `ISagaStateRepository` - handles saga state CRUD and locking
2. Implement `IOutboxRepository` - handles message outbox storage
3. Create extension method on `IBusConfigurator` (e.g., `UseMongoDbPersistence()`)
4. Register implementations in DI container
5. Examples: `OpenSleigh.Persistence.Mongo`, `OpenSleigh.Persistence.PostgreSQL`

**For Transport:**
1. Implement `IMessageSubscriber` - starts/stops message subscription
2. Create message parser to convert transport format to `MessageEnvelope`
3. Use `IMessageProcessor` to route incoming messages
4. Create extension method on `IBusConfigurator` (e.g., `UseRabbitMQTransport()`)
5. Examples: `OpenSleigh.Transport.RabbitMQ`, `OpenSleigh.Transport.Kafka`

**Pattern:** Both layers follow provider model with fluent configuration APIs.

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

## Development Workflow

### Typical Development Tasks

**Adding a new feature to core:**
1. Read existing abstractions and implementations
2. Add tests in OpenSleigh.Tests first (TDD approach)
3. Implement feature in src/OpenSleigh
4. Run unit tests to verify
5. Update integration tests if persistence/transport affected

**Adding a new persistence provider:**
1. Create new project: `OpenSleigh.Persistence.[Provider]`
2. Implement `ISagaStateRepository` and `IOutboxRepository`
3. Create extension method on `IBusConfigurator`
4. Create test project with `[Trait("Category", "Integration")]`
5. Update docker-compose.yml if needed

**Adding a new transport provider:**
1. Create new project: `OpenSleigh.Transport.[Provider]`
2. Implement `IMessageSubscriber`
3. Create message parser for provider's message format
4. Create extension method on `IBusConfigurator`
5. Create test project with integration tests

**Debugging saga execution:**
1. Check logs - extensive logging in `SagaRunner` and `MessageProcessor`
2. Verify saga registration in `SagaDescriptorsResolver`
3. Check correlation IDs in message flow
4. Verify lock acquisition/release in repository
5. Check outbox for pending messages

### Test Categories

Use xUnit traits to categorize tests:

```csharp
[Trait("Category", "Integration")]  // Requires Docker infrastructure
[Trait("Category", "E2E")]           // End-to-end scenario tests
```

Run specific categories:
```bash
# Unit tests only
dotnet test --filter "Category!=E2E&Category!=Integration"

# Integration tests only
dotnet test --filter "Category=Integration"

# Exclude Cosmos tests (not fully implemented)
dotnet test --filter "FullyQualifiedName!~Cosmos"
```

### CI/CD Context

**CircleCI (Primary CI):**
- Runs on every push to any branch
- Job 1: Build + unit tests + integration tests (with Docker services)
- Job 2: SonarCloud code quality scan
- Configuration: `.circleci/config.yml`

**GitHub Actions:**
- CodeQL security scanning (on develop and releases/** branches)
- NuGet package publishing (manual trigger or releases)
- Configuration: `.github/workflows/`

**Important:** E2E tests are currently commented out in CI - do not attempt to run them unless explicitly requested.

## Important Notes

1. **ALWAYS run `dotnet restore` first** - prevents package resolution issues
2. **Work from `src/` directory** for all build/test operations
3. **Specify `--framework net9.0`** when running tests to avoid multi-targeting ambiguity
4. **Fork from `develop` branch** - this is the main development branch, not main/master
5. **Do not modify version files** (`Versions.props`, `Directory.Build.props`) unless task specifically requires it
6. **174 compiler warnings are expected** - these are known nullable reference warnings; focus on zero errors
7. **Integration tests need Docker** - start via `cd tests/infra && docker-compose up -d`
8. **Multi-targeting affects test runs** - always specify framework to avoid ambiguous references
9. **Persistence implementations vary** - SQL providers use EF Core, Mongo uses native driver
10. **Saga state must be serializable** - used by persistence layer for storage

## Contributing Guidelines

From CONTRIBUTING.md:
- Discuss changes via issue/email before implementing
- Fork from `develop` branch
- Add tests for new code
- Update documentation for API changes
- Ensure test suite passes
- Verify code formatting with `dotnet format`
- Submit PR to `develop` branch
- Follow Microsoft C# coding conventions
- Use xUnit for tests, NSubstitute for mocking
