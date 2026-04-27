# dejting-yarp

API gateway and reverse proxy for the DatingApp platform.

## What It Does

This service fronts the internal microservices and provides:
- Route aggregation
- Centralized API entrypoint
- Policy and middleware enforcement
- Environment-specific routing configuration

## Why It Is Interesting

This repo demonstrates platform-level engineering:
- Gateway-driven architecture with YARP
- Cross-service routing and boundary management
- Operational concerns in distributed systems

## Stack

- .NET 8
- ASP.NET Core
- YARP (Yet Another Reverse Proxy)

## Project Layout

```text
dejting-yarp/
  src/dejting-yarp/           # Gateway implementation
  src/dejting-yarp.Tests/     # Unit/integration style tests
  Contracts/                  # API and SignalR specs
```

## Build and Test

```bash
dotnet restore src/dejting-yarp/dejting-yarp.csproj
dotnet build src/dejting-yarp/dejting-yarp.csproj
dotnet test src/dejting-yarp.Tests/dejting-yarp.Tests.csproj
```

## Run Locally

```bash
dotnet run --project src/dejting-yarp/dejting-yarp.csproj
```

## Key Files to Review

- `src/dejting-yarp/appsettings.json`
- `src/dejting-yarp/appsettings.Development.json`
- `Contracts/api-spec.md`
- `Contracts/signalr-spec.md`

## Related Repositories

All backend services route through this gateway in normal platform usage.
