# AGENTS.md — ParentalGuard

## Project Overview

ParentalGuard is a Windows desktop application that enforces DNS policies, website/application access controls, safe search, usage time limits, and restricted-mode lockdown. It consists of a background Windows Service and a WPF admin UI communicating through a shared SQLite database and signed config file.

**Tech stack:** C# / .NET 8, WPF, SQLite (Microsoft.Data.Sqlite), xUnit

## Build & Run Commands

```bash
# Build entire solution
dotnet build ParentalGuard.sln

# Build a specific project
dotnet build ParentalGuard.App/ParentalGuard.App.csproj
dotnet build src/ParentalGuard.Service/ParentalGuard.Service.csproj
dot build src/ParentalGuard.UI/ParentalGuard.UI.csproj

# Run the console app
dotnet run --project ParentalGuard.App

# Run the background service
dotnet run --project src/ParentalGuard.Service
```

## Test Commands

```bash
# Run all tests
dotnet test ParentalGuard.sln

# Run tests with verbose output
dotnet test ParentalGuard.sln --verbosity normal

# Run a single test by name
dotnet test ParentalGuard.sln --filter "FullyQualifiedName~ScreenTimePolicyTests.Evaluate_BlocksUsage_WhenBedtimeHasStarted"

# Run all tests in a specific class
dotnet test ParentalGuard.sln --filter "FullyQualifiedName~ScreenTimePolicyTests"

# Run tests with detailed logger
dotnet test ParentalGuard.sln --logger "console;verbosity=detailed"
```

## Lint & Format

```bash
# No dedicated lint command is configured. Use dotnet format for style enforcement:
dotnet format ParentalGuard.sln

# Format with verbosity (verify only, no changes)
dotnet format ParentalGuard.sln --verify-no-changes --verbosity diagnostic
```

## Solution Structure

```
ParentalGuard.sln
├── ParentalGuard.App/              Console app (screen-time policy     demo)
├── ParentalGuard.App.Tests/        xUnit test project
└── src/
    ├── ParentalGuard.Common/       Shared library (config schema, constants — mostly scaffolded)
    ├── ParentalGuard.Service/      Windows Service (DNS, app guard, web filter, hosts file)
    └── ParentalGuard.UI/           WPF admin panel (usage dashboard, block rule editor)
```

## Code Style Guidelines

### Language & Framework

- **Target framework:** .NET 8 (`net8.0` for libraries/service, `net8.0-windows` for WPF)
- **C# version:** Latest (C# 12+ features used: primary constructors on records, collection expressions, raw string literals)
- **Implicit usings:** Enabled (`<ImplicitUsings>enable</ImplicitUsings>`) — do not add redundant `using System;` etc.
- **Nullable reference types:** Enabled (`<Nullable>enable</Nullable>`) — all reference types are non-nullable by default; use `?` suffix for nullable references

### Naming Conventions

- **Namespaces:** Match project name — `ParentalGuard.App`, `ParentalGuard.Service`, `ParentalGuard.UI`, `ParentalGuard.Common`
- **Classes/records:** PascalCase (`ActivityStore`, `ScreenTimePolicy`, `BlockRuleRecord`)
- **Methods:** PascalCase (`Evaluate`, `EnsureRuleExists`, `SyncHostsFileIfNeeded`)
- **Private fields:** camelCase with underscore prefix (`_logger`, `_connectionString`, `_lastBlockActionAt`)
- **Parameters/locals:** camelCase (`profile`, `targetKey`, `processId`)
- **Constants:** PascalCase for `const` fields (`StartMarker`, `EndMarker`) or `private static readonly` (`BrowserProcesses`, `KnownDomains`)
- **Record types:** Used for immutable data models (`ActivitySample`, `BlockRuleRecord`, `ChildProfile`, `ScreenTimeDecision`)

### Types & Data Models

- Prefer `sealed record` for immutable DTOs and data models
- Prefer `sealed class` for services and stateful objects
- Use C# raw string literals (`"""..."""`) for SQL queries
- Use `required` keyword for init-only properties that must be set (`required string TargetType`)
- Use collection expressions `[]` for inline collection initialization

### Imports & File Layout

- Namespace-scoped files: `namespace ParentalGuard.Service;` (file-scoped namespace, no braces)
- One primary type per file; filename matches type name
- Using directives at top of file, inside namespace is NOT used
- Order: using directives → namespace → type definition

### Error Handling

- Use `ArgumentNullException.ThrowIfNull()` for parameter validation
- Wrap OS/file I/O operations in try-catch with `ILogger` calls (`_logger.LogError`, `_logger.LogDebug`)
- Do not let exceptions propagate from background service loops — catch and log, then continue
- Use specific exception logging: `_logger.LogError(ex, "Failed to sync the hosts file for blocked websites")`
- Return fallback/default values on failure (e.g., `CreateIdleSample()` on Win32 API failure)

### Patterns

- **Service pattern:** Dependency injection via `Microsoft.Extensions.Hosting` (`Host.CreateApplicationBuilder`)
- **BackgroundService:** Long-running services inherit `BackgroundService` and implement `ExecuteAsync`
- **INotifyPropertyChanged:** WPF ViewModels implement `INotifyPropertyChanged` with `SetField` helper method
- **Repository pattern:** `ActivityStore` handles all SQLite access with `using`-scoped connections
- **Observable collections:** WPF uses `ObservableCollection<T>` for data-bound lists

### SQLite Conventions

- Use `Microsoft.Data.Sqlite` (not `System.Data.Sqlite`)
- Parameterized queries with `$paramName` syntax and `AddWithValue`
- `CREATE TABLE IF NOT EXISTS` for idempotent schema creation
- `ON CONFLICT ... DO UPDATE SET` for upsert patterns
- Open/close connections per operation (no persistent connection pooling in current code)

### WPF Conventions

- View-ViewModel in same file for simpler views (code-behind acts as ViewModel)
- `INotifyPropertyChanged` for bindable properties with `SetField<T>` helper
- `ObservableCollection<T>` for dynamic UI lists
- XAML uses descriptive `x:Name` attributes (e.g., `HeaderCard`, `SettingsCard`, `BlockPopupHost`)

### P/Invoke (Win32 Interop)

- DllImport attributes at bottom of class, `private static extern`
- Use `DllImport("user32.dll")` style with explicit `CharSet = CharSet.Unicode` when needed
- Handle `IntPtr.Zero` gracefully with fallback returns

## Key Conventions

- No comments in production code unless explicitly requested
- Async/await in background service loops: `await Task.Delay(1000, stoppingToken)`
- String interpolation with `$""` for user-facing messages and log messages
- `StringComparison.OrdinalIgnoreCase` for all case-insensitive comparisons
- `StringComparison.Ordinal` for exact/invariant comparisons
- Range operator `[^4..]` and `[4..]` used for substring operations instead of `.Substring()`

