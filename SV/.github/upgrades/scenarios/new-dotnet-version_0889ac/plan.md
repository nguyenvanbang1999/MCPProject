# .NET 10.0 Upgrade Plan — MicroservicesServer

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Migration Strategy](#2-migration-strategy)
3. [Detailed Dependency Analysis](#3-detailed-dependency-analysis)
4. [Project-by-Project Plans](#4-project-by-project-plans)
5. [Package Update Reference](#5-package-update-reference)
6. [Breaking Changes Catalog](#6-breaking-changes-catalog)
7. [Testing Strategy](#7-testing-strategy)
8. [Risk Management](#8-risk-management)
9. [Complexity & Effort Assessment](#9-complexity--effort-assessment)
10. [Source Control Strategy](#10-source-control-strategy)
11. [Success Criteria](#11-success-criteria)

---

## 1. Executive Summary

| Metric | Value |
|--------|-------|
| Solution | MicroservicesServer |
| Target Framework | .NET 10.0 (LTS) |
| Total Projects | 8 |
| Projects Upgrading TFM | 6 (net8.0 → net10.0) |
| Projects Staying on netstandard2.1 | 2 (SharedContracts, AuthService.Contracts) |
| Total Issues Found | 34 |
| Mandatory Issues | 10 |
| Potential Issues | 23 |
| Optional Issues | 1 |
| Affected Files | 14 |
| Estimated Effort | Low–Medium (1–2 days) |
| Migration Strategy | All-At-Once |

### Issue Distribution

| Rule | Description | Severity | Count |
|------|-------------|----------|-------|
| Project.0002 | Target framework needs changing | Mandatory | 6 |
| Api.0001 | Binary incompatible API | Mandatory | 4 |
| Api.0002 | Source incompatible API | Potential | 5 |
| Api.0003 | Behavioral change | Potential | 4 |
| NuGet.0002 | Package upgrade recommended | Potential | 14 |
| NuGet.0005 | Deprecated package | Optional | 1 |

**Key highlights:**
- Aspire packages in AppHost (Aspire.Hosting.AppHost, Aspire.Hosting.Kafka) upgrade 13.1.0 → 13.1.2.
- `Aspire.Hosting 9.5.1` in AuthService is DEPRECATED — must be removed and replaced by `Aspire.Hosting.AppHost 13.1.2`.
- `TimeSpan.FromSeconds(int)` causes source incompatibility in 5 places due to new overload ambiguity in .NET 7+.
- `OptionsConfigurationServiceCollectionExtensions.Configure<T>` (explicit static invocation) is binary incompatible — replace with extension method syntax.
- `ConfigurationBinder.GetValue<T>` in GateWayTCP is binary incompatible — resolved by recompiling against updated packages.

---

## 2. Migration Strategy

### Strategy Selected: All-At-Once

**Rationale:** All 8 projects are small-to-medium in size, rated Low difficulty, with no circular dependencies, no legacy interop constraints, and no security vulnerabilities. The dependency graph is shallow (4 levels), and all breaking changes are mechanical and well-understood. Upgrading the entire solution in a single coordinated pass is the most efficient approach.

### Execution Phases

```
Phase 1 — Foundation (Level 0 + Level 1)
  SharedContracts          → package update only
  ServiceShare             → TFM + packages + API fixes
  ServiceDefaults          → TFM + packages
  AuthService.Contracts    → no changes required
  ServiceRegistry          → TFM change only

Phase 2 — Application Services (Level 2)
  AuthService              → TFM + packages + API fixes
  GateWayTCP               → TFM + API fixes (behavioral + binary)

Phase 3 — Entry Point (Level 3)
  MicroservicesServer.AppHost → TFM + Aspire 13.1.2 packages
```

---

## 3. Detailed Dependency Analysis

```
Level 0 — Foundation (no project dependencies)
  SharedContracts             [netstandard2.1]  → stays, 1 package issue
  ServiceShare                [net8.0→10.0]     → 10 issues (3 mandatory)
  MicroservicesServer.ServiceDefaults [net8.0→10.0] → 5 issues (1 mandatory)

Level 1 — depends on Level 0
  AuthService.Contracts       [netstandard2.1]  → stays, 0 issues
  ServiceRegistry             [net8.0→10.0]     → 1 issue (1 mandatory)

Level 2 — depends on Levels 0-1
  AuthService                 [net8.0→10.0]     → 8 issues (2 mandatory)
  GateWayTCP                  [net8.0→10.0]     → 6 issues (2 mandatory)

Level 3 — Top-level entry point
  MicroservicesServer.AppHost [net8.0→10.0]     → 3 issues (1 mandatory)
```

### Compatible Packages (No Changes Required)

| Package | Version |
|---------|---------|
| Confluent.Kafka | 2.8.0 |
| MessagePack | 3.1.4 |
| Microsoft.IdentityModel.JsonWebTokens | 8.14.0 |
| MongoDB.Driver | 3.5.0 |
| Newtonsoft.Json | 13.0.4 |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.9.0 |
| OpenTelemetry.Extensions.Hosting | 1.9.0 |
| OpenTelemetry.Instrumentation.Runtime | 1.9.0 |

---

## 4. Project-by-Project Plans

### 4.1 SharedContracts
- TFM: netstandard2.1 → NO CHANGE
- Actions: Update Microsoft.Extensions.Logging.Abstractions 9.0.9 → 10.0.5
- Files: SharedContracts\SharedContracts.csproj

### 4.2 ServiceShare
- TFM: net8.0 → net10.0
- Package updates:
  - Microsoft.Extensions.Hosting.Abstractions 9.0.9 → 10.0.5
  - Microsoft.Extensions.Logging.Abstractions 9.0.9 → 10.0.5
  - Microsoft.Extensions.Options 9.0.9 → 10.0.5
  - Microsoft.Extensions.Options.ConfigurationExtensions 9.0.0 → 10.0.5
- BC-001 Fix (TimeSpan.FromSeconds): KafkaConsumerService.cs lines 149,164 and KafkaEventBus.cs line 110
  Change: TimeSpan.FromSeconds(5) → TimeSpan.FromSeconds(5.0)
  Change: TimeSpan.FromSeconds(10) → TimeSpan.FromSeconds(10.0)
- BC-002 Fix (OptionsConfigurationServiceCollectionExtensions): KafkaEventBusExtensions.cs lines 22,59
  Change: OptionsConfigurationServiceCollectionExtensions.Configure<KafkaSettings>(services, config)
       → services.Configure<KafkaSettings>(config)
- Files Modified: ServiceShare.csproj, KafkaEventBusExtensions.cs, KafkaConsumerService.cs, KafkaEventBus.cs

### 4.3 MicroservicesServer.ServiceDefaults
- TFM: net8.0 → net10.0
- Package updates:
  - Microsoft.Extensions.Http.Resilience 9.4.0 → 10.4.0
  - Microsoft.Extensions.ServiceDiscovery 9.3.1 → 10.4.0
  - OpenTelemetry.Instrumentation.AspNetCore 1.9.0 → 1.15.1
  - OpenTelemetry.Instrumentation.Http 1.9.0 → 1.15.0
- Files Modified: MicroservicesServer.ServiceDefaults.csproj

### 4.4 AuthService.Contracts
- TFM: netstandard2.1 → NO CHANGE
- NO CHANGES REQUIRED — 0 issues

### 4.5 ServiceRegistry
- TFM: net8.0 → net10.0
- No package or code changes required
- Files Modified: ServiceRegistry.csproj

### 4.6 AuthService
- TFM: net8.0 → net10.0
- Package changes:
  - REMOVE Aspire.Hosting 9.5.1 (DEPRECATED)
  - ADD Aspire.Hosting.AppHost 13.1.2 (replacement)
  - Microsoft.AspNetCore.Authentication.JwtBearer 8.0.20 → 10.0.5
  - Microsoft.Extensions.Hosting.Abstractions 9.0.9 → 10.0.5
- BC-001 Fix (TimeSpan.FromSeconds): DemoPublisherService.cs lines 35,68
  Change: TimeSpan.FromSeconds(INTERVAL_SECONDS) → TimeSpan.FromSeconds((double)INTERVAL_SECONDS)
- BC-002 Fix (OptionsConfigurationServiceCollectionExtensions): Program.cs line 21
  Ensure extension method syntax is used: builder.Services.Configure<MongoDBSettings>(...)
- Files Modified: AuthService.csproj, Program.cs, Services\DemoPublisherService.cs

### 4.7 GateWayTCP
- TFM: net8.0 → net10.0
- No NuGet package changes required
- BC-003 Fix (ConfigurationBinder.GetValue<T>): TcpGatewayService.cs line 55
  If compile error: _config.GetValue<string>(key) → _config[key] ?? "localhost"
- BC-004,005 (Uri, AddSimpleConsole, AddHttpClient): Program.cs lines 17,22,80
  Runtime verification only — test service startup and HTTP behavior
- Files Modified: GateWayTCP.csproj, TcpGatewayService.cs (if needed), Program.cs (runtime verify)

### 4.8 MicroservicesServer.AppHost
- TFM: net8.0 → net10.0
- Package updates:
  - Aspire.Hosting.AppHost 13.1.0 → 13.1.2
  - Aspire.Hosting.Kafka 13.1.0 → 13.1.2
- Files Modified: MicroservicesServer.AppHost.csproj

---

## 5. Package Update Reference

| Package | Project | Current | Target | Action |
|---------|---------|---------|--------|--------|
| Aspire.Hosting | AuthService | 9.5.1 | — | REMOVE (deprecated) |
| Aspire.Hosting.AppHost | AuthService | — | 13.1.2 | ADD (replacement) |
| Aspire.Hosting.AppHost | AppHost | 13.1.0 | 13.1.2 | UPGRADE |
| Aspire.Hosting.Kafka | AppHost | 13.1.0 | 13.1.2 | UPGRADE |
| Microsoft.AspNetCore.Authentication.JwtBearer | AuthService | 8.0.20 | 10.0.5 | UPGRADE |
| Microsoft.Extensions.Hosting.Abstractions | AuthService | 9.0.9 | 10.0.5 | UPGRADE |
| Microsoft.Extensions.Hosting.Abstractions | ServiceShare | 9.0.9 | 10.0.5 | UPGRADE |
| Microsoft.Extensions.Http.Resilience | ServiceDefaults | 9.4.0 | 10.4.0 | UPGRADE |
| Microsoft.Extensions.Logging.Abstractions | ServiceShare | 9.0.9 | 10.0.5 | UPGRADE |
| Microsoft.Extensions.Logging.Abstractions | SharedContracts | 9.0.9 | 10.0.5 | UPGRADE |
| Microsoft.Extensions.Options | ServiceShare | 9.0.9 | 10.0.5 | UPGRADE |
| Microsoft.Extensions.Options.ConfigurationExtensions | ServiceShare | 9.0.0 | 10.0.5 | UPGRADE |
| Microsoft.Extensions.ServiceDiscovery | ServiceDefaults | 9.3.1 | 10.4.0 | UPGRADE |
| OpenTelemetry.Instrumentation.AspNetCore | ServiceDefaults | 1.9.0 | 1.15.1 | UPGRADE |
| OpenTelemetry.Instrumentation.Http | ServiceDefaults | 1.9.0 | 1.15.0 | UPGRADE |

---

## 6. Breaking Changes Catalog

### BC-001 — TimeSpan.FromSeconds(int) Source Incompatible
- Rule: Api.0002 | Severity: Potential (compile error)
- Projects: ServiceShare, AuthService
- Files: KafkaConsumerService.cs, KafkaEventBus.cs, DemoPublisherService.cs | 5 occurrences
- Root Cause: .NET 7 added TimeSpan.FromSeconds(long) overload. Integer literals are now ambiguous → CS0121
- Fix: TimeSpan.FromSeconds(5) → TimeSpan.FromSeconds(5.0) or TimeSpan.FromSeconds((double)value)

### BC-002 — OptionsConfigurationServiceCollectionExtensions.Configure<T> Binary Incompatible
- Rule: Api.0001 | Severity: Mandatory
- Projects: ServiceShare, AuthService
- Files: KafkaEventBusExtensions.cs, Program.cs | 3 occurrences
- Fix: Replace explicit static call with extension method: services.Configure<T>(config.GetSection(...))

### BC-003 — ConfigurationBinder.GetValue<T> Binary Incompatible
- Rule: Api.0001 | Severity: Mandatory
- Projects: GateWayTCP
- Files: TcpGatewayService.cs | 1 occurrence
- Fix: Recompile with net10.0. If errors persist: _config["KEY"] ?? "default"

### BC-004 — Uri / Uri(string) Behavioral Change
- Rule: Api.0003 | Severity: Potential (runtime only)
- Projects: GateWayTCP | Files: Program.cs line 80
- Fix: Runtime test. Add try/catch for UriFormatException if URL is externally provided.

### BC-005 — AddSimpleConsole / AddHttpClient Behavioral Change
- Rule: Api.0003 | Severity: Potential (runtime only)
- Projects: GateWayTCP | Files: Program.cs lines 17,22
- Fix: Runtime verification only. Verify log format and HTTP client behavior.

---

## 7. Testing Strategy

### Build Verification
  dotnet build MicroservicesServer.sln

### Runtime Validation Checklist
| Area | What to Verify | Related BC |
|------|---------------|-----------|
| Kafka Consumer | Message consumption resumes after delay | BC-001 |
| Kafka Producer | Producer flush completes without timeout | BC-001 |
| MongoDB Settings | Bound correctly from appsettings.json | BC-002 |
| Kafka Settings | KafkaSettings section bound correctly | BC-002 |
| TCP Gateway | Internal host resolved from config | BC-003 |
| TCP Gateway | URI constructed without UriFormatException | BC-004 |
| All Services | Console log output is readable | BC-005 |
| GateWayTCP | HTTP calls succeed with expected behavior | BC-005 |
| Aspire Dashboard | All services appear and health checks pass | Package upgrade |
| Aspire Kafka | Kafka resource starts and is reachable | Package upgrade |

---

## 8. Risk Management

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Aspire 13.1.2 API changes from 13.1.0 | Low | Medium | Review 13.1.2 release notes; test AppHost startup |
| TimeSpan fix missed for non-literal values | Medium | High | Search all TimeSpan.FromSeconds usages after upgrade |
| AddHttpClient behavior causes silent HTTP failures | Low | High | Add integration/smoke test for HTTP paths in GateWayTCP |
| Uri construction throws at runtime | Low | High | Add defensive try/catch; validate URL format in tests |
| netstandard2.1 projects incompatible with net10.0 consumers | Very Low | Low | netstandard2.1 is fully compatible with .NET 10.0 |

---

## 9. Complexity & Effort Assessment

| Project | Difficulty | TFM Change | Code Changes | Package Changes |
|---------|-----------|-----------|--------------|-----------------|
| SharedContracts | Minimal | No | 0 files | 1 |
| AuthService.Contracts | None | No | 0 files | 0 |
| ServiceRegistry | Minimal | net8→10 | 0 files | 0 |
| ServiceDefaults | Minimal | net8→10 | 0 files | 4 |
| AppHost | Minimal | net8→10 | 0 files | 2 Aspire |
| AuthService | Low | net8→10 | 2 files | 3 (1 deprecated) |
| GateWayTCP | Low | net8→10 | 2 files | 0 |
| ServiceShare | Low-Medium | net8→10 | 3 files | 4 |

Total estimated effort: 1–2 developer days

---

## 10. Source Control Strategy

| Step | Action |
|------|--------|
| Pre-upgrade | Pending changes committed to develop |
| Upgrade branch | All changes on upgrade-to-NET10 |
| Commit strategy | One commit per project or per phase |
| PR | Open PR from upgrade-to-NET10 → develop after tests pass |
| Rollback | git checkout develop at any point before PR merge |

Suggested commit messages:
  feat(upgrade): SharedContracts — Microsoft.Extensions.Logging.Abstractions 10.0.5
  feat(upgrade): ServiceShare — net10.0 TFM + packages + API fixes
  feat(upgrade): ServiceDefaults — net10.0 TFM + Aspire/OTel packages
  feat(upgrade): ServiceRegistry — net10.0 TFM
  feat(upgrade): AuthService — net10.0 TFM + replace deprecated Aspire.Hosting
  feat(upgrade): GateWayTCP — net10.0 TFM + ConfigurationBinder fix
  feat(upgrade): AppHost — net10.0 TFM + Aspire 13.1.2 packages

---

## 11. Success Criteria

### Build
- [ ] dotnet build exits code 0 — zero errors
- [ ] All 6 projects target net10.0; SharedContracts and AuthService.Contracts stay on netstandard2.1

### Packages
- [ ] No deprecated packages remain (Aspire.Hosting 9.5.1 removed from AuthService)
- [ ] All Aspire packages on 13.1.2
- [ ] All Microsoft.Extensions.* on 10.x for net10.0 projects

### Code Quality
- [ ] No TimeSpan.FromSeconds(int) ambiguous calls remain
- [ ] No explicit OptionsConfigurationServiceCollectionExtensions.Configure<T>(services,config) calls remain
- [ ] ConfigurationBinder.GetValue<T> usage is nullable-safe

### Runtime
- [ ] Aspire AppHost starts and Dashboard is accessible
- [ ] All services appear healthy in Aspire Dashboard
- [ ] Kafka resource starts; DemoHeartbeatEvent published/consumed successfully
- [ ] MongoDB connection established in AuthService
- [ ] TCP Gateway resolves host and proxies requests

### Source Control
- [ ] upgrade-to-NET10 branch has clean commits ahead of develop
- [ ] PR created and ready for review
