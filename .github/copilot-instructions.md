# OpenSleigh Copilot Instructions

## Repository Overview

OpenSleigh is a distributed saga management library written in C# for .NET Core. It enables reliable, fast, and extensible saga pattern implementations with support for multiple persistence backends (MongoDB, PostgreSQL, SQL Server) and message transports (RabbitMQ, Kafka).

**Repository Size:** ~280 C# files (~4,500 lines of code)  
**Languages:** C# 12  
**Target Frameworks:** net8.0 and net9.0 (multi-targeting)  
**Runtime Version:** .NET SDK 10.0.101 (local), .NET 9.0.203 (CI)  
**Package Manager:** NuGet  
**Primary Solution:** `src/OpenSleigh.sln`

## Project Structure

### Main Projects (src/)
- `OpenSleigh/` - Core saga library (main project)
- `OpenSleigh.InMemory/` - In-memory implementations
- **Persistence Layer:**
  - `OpenSleigh.Persistence.SQL/` - Base SQL persistence
  - `OpenSleigh.Persistence.SQLServer/` - SQL Server implementation
  - `OpenSleigh.Persistence.PostgreSQL/` - PostgreSQL implementation
  - `OpenSleigh.Persistence.Mongo/` - MongoDB implementation
- **Transport Layer:**
  - `OpenSleigh.Transport.RabbitMQ/` - RabbitMQ transport
  - `OpenSleigh.Transport.Kafka/` - Kafka transport

### Tests (tests/)
- `OpenSleigh.Tests/` - Unit tests
- `OpenSleigh.E2ETests/` - End-to-end tests
- `OpenSleigh.Persistence.*.Tests/` - Persistence layer tests
- `OpenSleigh.Transport.*.Tests/` - Transport layer tests
- `tests/infra/docker-compose.yml` - Infrastructure for integration tests

### Configuration Files
- `Directory.Build.props` - Shared MSBuild properties (language version, nullable)
- `Versions.props` - Package version (3.0.6) and target frameworks
- `test.runsettings` - Test configuration for code coverage
- `.vscode/tasks.json` - VS Code build tasks

## Build Instructions

### Prerequisites
- .NET SDK 9.0 or later (10.0 recommended)
- Docker and Docker Compose (for integration tests only)

### Critical Build Steps

**ALWAYS work from the `src/` directory for build operations:**

```bash
cd src/
```

**1. Restore Dependencies** (ALWAYS run this first):
```bash
dotnet restore
```
Takes ~5-10 seconds. Must complete successfully before building.

**2. Build the Solution:**
```bash
# Debug build
dotnet build

# Release build (recommended for validation)
dotnet build -c Release
```
Takes ~20-30 seconds. Expect ~174 compiler warnings (mostly CS8602, CS8625 nullable warnings) - these are known and acceptable. Build must complete with 0 errors.

**3. Run Unit Tests:**
```bash
# Run unit tests only (no infrastructure needed)
dotnet test --framework net9.0 --filter "Category!=E2E&Category!=Integration"
```
Takes ~5-10 seconds. Tests: ~105 unit tests should pass.

**4. Run Integration Tests** (requires Docker infrastructure):
```bash
# Start infrastructure first (from repo root)
cd ../tests/infra
docker-compose up -d

# Wait 10-15 seconds for services to be ready, then run tests
cd ../../src
dotnet test --framework net9.0 --filter "FullyQualifiedName!~Cosmos&Category=Integration"
```
Integration tests require MongoDB, RabbitMQ, Kafka, SQL Server, and PostgreSQL running locally.

**5. Code Formatting:**
```bash
# Check formatting (verify no changes needed)
dotnet format --verify-no-changes

# Apply formatting
dotnet format
```
Takes ~30-60 seconds. No specific formatting rules defined beyond defaults.

**6. Create NuGet Packages:**
```bash
dotnet pack -c Release
```
Packages are output to `packages/` directory at repo root.

## Continuous Integration

### CircleCI (Primary CI)
**Location:** `.circleci/config.yml`  
**Triggers:** All commits on all branches  
**Docker Images Used:**
- `mcr.microsoft.com/dotnet/sdk:9.0.203`
- `mongo:latest`
- `rabbitmq:3-management-alpine`
- `mcr.microsoft.com/mssql/server:2022-latest`
- `postgres:13.4`
- `apache/kafka:latest`

**Two Jobs:**

1. **build** - Runs on every push
   ```bash
   cd ./src
   dotnet build
   dotnet test --framework net9.0 --filter "Category!=E2E&Category!=Integration"
   dotnet test --framework net9.0 --filter "FullyQualifiedName!~Cosmos&Category=Integration"
   ```
   E2E tests are currently disabled (commented out).

2. **sonarscan** - Code quality analysis via SonarCloud
   - Uses SonarCloud orb for scanning
   - Requires SONAR_TOKEN (from context)
   - Filters exclude Cosmos-related tests

**Environment Variables in CI:**
- `TARGET_FRAMEWORK=net9.0`
- `SA_PASSWORD=Sup3r_p4ssword123` (SQL Server)
- `POSTGRES_PASSWORD=Sup3r_p4ssword123`
- `RABBITMQ_DEFAULT_VHOST=/opensleigh-tests`

### GitHub Actions
**Location:** `.github/workflows/`

1. **codeql-analysis.yml** - Security scanning
   - Triggers: Push/PR to develop or releases/** branches, affecting src/ or tests/
   - Runs: CodeQL analysis on C# code
   - Command: `cd ./src && dotnet build -c Release`
   - Uses: .NET 7.0 (note: this is older than the main CI which uses .NET 9.0.203)

2. **nuget.yml** - Package publishing
   - Triggers: Manual or on release/prerelease events
   - Uses: .NET 9.0.x
   - Commands:
     ```bash
     cd ./src && dotnet pack -c Release
     cd ./packages && dotnet nuget push "*.nupkg" -k $NUGET_API_KEY -s https://api.nuget.org/v3/index.json --skip-duplicate
     ```

## Known Issues and Workarounds

### Build Warnings
- **174 nullable reference warnings** are present and acceptable (CS8602, CS8625, CS8618, CS8604, CS0252, CS4014)
- These warnings exist in test projects and some implementation files
- Do NOT try to fix these unless specifically requested

### Test Categories
- Unit tests: No category or `Category!=E2E&Category!=Integration`
- Integration tests: `Category=Integration` (requires Docker infrastructure)
- E2E tests: `Category=E2E` (currently skipped in CI)
- Tests excluding Cosmos: `FullyQualifiedName!~Cosmos`

### Docker Infrastructure
- If integration tests fail, ensure Docker Compose services are running:
  ```bash
  cd tests/infra
  docker-compose down && docker-compose up -d
  # Wait 10-15 seconds for services to initialize
  ```
- Services listen on standard ports: MongoDB (27017), RabbitMQ (5672, 15672), Kafka (9092), SQL Server (1433), PostgreSQL (5432)

### Multi-Targeting
- Projects target both net8.0 and net9.0
- Always specify `--framework net9.0` when running tests to avoid ambiguity
- CircleCI uses `TARGET_FRAMEWORK=net9.0` environment variable

## Common Commands Summary

```bash
# Standard workflow from repo root:
cd src/

# Full build and test cycle:
dotnet restore
dotnet build -c Release
dotnet test --framework net9.0 --filter "Category!=E2E&Category!=Integration"

# Verify formatting:
dotnet format --verify-no-changes

# Create packages:
dotnet pack -c Release
```

## Code Conventions

- **Language:** C# 12 with nullable reference types enabled
- **Style:** Follow [Microsoft C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/inside-a-program/coding-conventions)
- **License:** Apache 2.0
- **Versioning:** Current version in `Versions.props`: 3.0.6

## Important Notes

1. **ALWAYS run `dotnet restore` before building** - This prevents package resolution issues
2. **Work from `src/` directory** for all build/test operations
3. **Expect compiler warnings** - 174 warnings are normal, 0 errors required
4. **Integration tests require Docker** - Unit tests do not
5. **Use `--framework net9.0`** when running tests to avoid multi-targeting ambiguity
6. **E2E tests are disabled** in CI - do not attempt to run them unless specifically requested
7. **Do not modify** Versions.props, Directory.Build.props, or test.runsettings unless the task specifically requires it
8. **CircleCI is the primary CI** - GitHub Actions only run for security scanning and releases

## Trust These Instructions

These instructions have been validated by running all commands successfully. If you encounter an issue not documented here, investigate the specific error before searching extensively. The build process is straightforward when commands are run in the correct order from the correct directory.
