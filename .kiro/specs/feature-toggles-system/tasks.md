# Implementation Plan: Feature Toggles System

## Overview

This plan implements a feature flag/toggle system for the CorePoints platform. The system provides database-backed flag storage (PostgreSQL via Dapper), in-memory caching (IMemoryCache, 60s TTL), a service interface for flag evaluation at the Use Case layer, canary release support via target groups, an Admin REST API for flag management, and a declarative endpoint filter for flag-gating routes.

## Tasks

- [x] 1. Set up project structure, models, and interfaces
  - [x] 1.1 Create project structure and add dependencies
    - Create class library project for `FeatureToggles` (or integrate into existing service project)
    - Add NuGet packages: Dapper, Npgsql, Microsoft.Extensions.Caching.Memory, FsCheck.Xunit (test project)
    - Create folder structure: Models/, Interfaces/, Services/, Repositories/, Filters/, Admin/
    - _Requirements: 1.2, 3.5, 9.1_

  - [x] 1.2 Define domain models and DTOs
    - Create `FeatureFlag` sealed record with Id, Name, Description, IsEnabled, TargetGroups, CreatedAt, UpdatedAt
    - Create `CreateFeatureFlagRequest` record with Name (required), Description, IsEnabled (default false), TargetGroups
    - Create `UpdateFeatureFlagRequest` record with Description, IsEnabled, TargetGroups (all nullable for partial update)
    - Create `FeatureToggleOptions` class with CacheTtl (default 60s) and ConnectionString
    - _Requirements: 1.1, 5.4, 6.3_

  - [x] 1.3 Define interfaces (IFeatureToggleService, IFeatureFlagRepository)
    - Create `IFeatureToggleService` with `IsEnabledAsync(string flagName, string? userId, string? group, CancellationToken)`
    - Create `IFeatureFlagRepository` with `GetByNameAsync`, `GetAllAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`
    - _Requirements: 2.1, 9.2_

- [x] 2. Implement database layer
  - [x] 2.1 Create SQL migration for feature_flags table
    - Create `feature_flags` table: id (UUID PK default gen_random_uuid()), name (VARCHAR 200 UNIQUE NOT NULL), description (TEXT), is_enabled (BOOLEAN NOT NULL default false), target_groups (JSONB NOT NULL default '[]'), created_at (TIMESTAMPTZ NOT NULL default NOW()), updated_at (TIMESTAMPTZ NOT NULL default NOW())
    - Create unique index `idx_feature_flags_name` on name column
    - _Requirements: 1.1, 10.1_

  - [x] 2.2 Implement FeatureFlagRepository with Dapper
    - Implement `GetByNameAsync`: SELECT by name with JSONB deserialization for target_groups
    - Implement `GetAllAsync`: SELECT all flags ordered by name
    - Implement `CreateAsync`: INSERT with conflict detection (unique name), set created_at/updated_at
    - Implement `UpdateAsync`: UPDATE by name, set updated_at, return updated row or null
    - Implement `DeleteAsync`: DELETE by name, return affected rows > 0
    - Register Dapper JSONB type handler for List<string> ↔ JSONB mapping
    - All methods async with CancellationToken propagation
    - _Requirements: 1.2, 1.3, 1.4, 1.5_

  - [ ]* 2.3 Write property test for CRUD round-trip (Property 8)
    - **Property 8: Admin CRUD Round-Trip**
    - For any valid CreateFeatureFlagRequest, creating a flag then reading it back SHALL return an equivalent object with matching name, description, is_enabled, and target_groups
    - **Validates: Requirements 1.4, 4.1, 5.1**

  - [ ]* 2.4 Write property test for unique name constraint (Property 9)
    - **Property 9: Unique Name Constraint**
    - For any two CreateFeatureFlagRequests with the same name, the second create SHALL fail
    - **Validates: Requirement 5.2**

- [x] 3. Implement flag evaluation service
  - [x] 3.1 Implement FeatureToggleService (cache-first, DB fallback, fail-closed)
    - Inject IFeatureFlagRepository, IMemoryCache, ILogger, FeatureToggleOptions
    - Implement `IsEnabledAsync`: check cache → on miss query repository → store in cache with TTL
    - Evaluation logic: flag not found → false; is_enabled false → false; is_enabled true + no target_groups → true; is_enabled true + target_groups → match group
    - Wrap repository call in try/catch: on error return false and log (fail-closed)
    - On cache error, fall through to repository
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 3.1, 3.2, 3.3, 11.1, 11.2_

  - [ ]* 3.2 Write property test for disabled flag returns false (Property 1)
    - **Property 1: Flag Evaluation — Disabled Flag Always Returns False**
    - For any Feature_Flag where is_enabled is false, IsEnabledAsync SHALL return false regardless of userId or group
    - **Validates: Requirement 2.3**

  - [ ]* 3.3 Write property test for enabled flag without groups returns true (Property 2)
    - **Property 2: Flag Evaluation — Enabled Flag Without Target Groups Returns True**
    - For any Feature_Flag where is_enabled is true and target_groups is empty, IsEnabledAsync SHALL return true for any userId/group
    - **Validates: Requirement 2.4**

  - [ ]* 3.4 Write property test for group matching (Property 3)
    - **Property 3: Flag Evaluation — Group Matching**
    - For any enabled flag with target_groups, IsEnabledAsync returns true if and only if provided group is in target_groups
    - **Validates: Requirements 2.5, 2.6, 10.2, 10.3, 10.4**

  - [ ]* 3.5 Write property test for flag not found returns false (Property 4)
    - **Property 4: Flag Not Found Returns False**
    - For any non-existent flag name, IsEnabledAsync SHALL return false
    - **Validates: Requirement 2.2**

  - [ ]* 3.6 Write property test for fail-closed on error (Property 7)
    - **Property 7: Fail-Closed on Repository Error**
    - For any repository failure, IsEnabledAsync SHALL return false
    - **Validates: Requirement 11.1**

- [x] 4. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Implement cache invalidation and integration
  - [x] 5.1 Implement cache invalidation on Admin writes
    - After CreateAsync: invalidate cache entry for the new flag name
    - After UpdateAsync: invalidate cache entry for the updated flag name
    - After DeleteAsync: invalidate cache entry for the deleted flag name
    - Use consistent cache key format: `"feature_flag:{flagName}"`
    - _Requirements: 3.4_

  - [ ]* 5.2 Write unit tests for cache behavior
    - Test: cache hit path does not call repository
    - Test: cache miss calls repository and populates cache
    - Test: cache invalidated after create
    - Test: cache invalidated after update
    - Test: cache invalidated after delete
    - _Requirements: 3.1, 3.2, 3.4_

- [x] 6. Implement Admin API endpoints
  - [x] 6.1 Implement GET /admin/flags endpoint
    - Return HTTP 200 with JSON array of all FeatureFlags
    - Include all fields: id, name, description, is_enabled, target_groups, created_at, updated_at
    - _Requirements: 4.1, 4.2_

  - [x] 6.2 Implement POST /admin/flags endpoint
    - Validate required name field → 400 if missing
    - Check name uniqueness → 409 Conflict if exists
    - Create flag with defaults (is_enabled=false, target_groups=[])
    - Return HTTP 201 with created flag
    - Invalidate cache for the flag name
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 3.4_

  - [x] 6.3 Implement PUT /admin/flags/{name} endpoint
    - Look up flag by name → 404 if not found
    - Update provided fields (description, is_enabled, target_groups)
    - Set updated_at to current UTC
    - Return HTTP 200 with updated flag
    - Invalidate cache for the flag name
    - _Requirements: 6.1, 6.2, 6.3, 3.4_

  - [x] 6.4 Implement DELETE /admin/flags/{name} endpoint
    - Look up flag by name → 404 if not found
    - Delete flag
    - Return HTTP 204 No Content
    - Invalidate cache for the flag name
    - _Requirements: 7.1, 7.2, 3.4_

  - [ ]* 6.5 Write unit tests for Admin API endpoints
    - Test: GET returns all flags with 200
    - Test: POST valid request returns 201
    - Test: POST duplicate name returns 409 Problem Details
    - Test: POST missing name returns 400 Problem Details
    - Test: PUT existing flag returns 200
    - Test: PUT non-existent flag returns 404 Problem Details
    - Test: DELETE existing flag returns 204
    - Test: DELETE non-existent flag returns 404 Problem Details
    - _Requirements: 4.1, 5.1, 5.2, 5.3, 6.1, 6.2, 7.1, 7.2, 11.3_

- [x] 7. Implement FeatureGate endpoint filter
  - [x] 7.1 Implement FeatureGateAttribute and FeatureGateFilter
    - Create `FeatureGateAttribute` with FlagName property
    - Implement `FeatureGateFilter : IEndpointFilter`
    - Extract userId and group from HttpContext.User claims
    - Call IFeatureToggleService.IsEnabledAsync with extracted context
    - If disabled: return Results.NotFound()
    - If enabled: call next(context)
    - _Requirements: 8.1, 8.2, 8.3, 8.4_

  - [ ]* 7.2 Write unit tests for FeatureGateFilter
    - Test: filter returns 404 when flag is disabled
    - Test: filter passes through when flag is enabled
    - Test: filter extracts userId/group from claims correctly
    - Test: filter handles missing claims gracefully (no group → pass as null)
    - _Requirements: 8.1, 8.2, 8.3, 8.4_

- [x] 8. Wire DI container and register services
  - [x] 8.1 Create extension method for DI registration
    - Create `AddFeatureToggles(this IServiceCollection, Action<FeatureToggleOptions>)` extension
    - Register IFeatureFlagRepository → FeatureFlagRepository (scoped)
    - Register IFeatureToggleService → FeatureToggleService (scoped)
    - Register IMemoryCache via AddMemoryCache()
    - Register FeatureToggleOptions from configuration
    - Register Dapper JSONB type handler
    - _Requirements: 9.1, 9.2, 3.5_

  - [x] 8.2 Create extension method for Admin API route mapping
    - Create `MapFeatureToggleAdmin(this IEndpointRouteBuilder)` extension
    - Map GET, POST, PUT, DELETE routes under `/admin/flags`
    - _Requirements: 4.1, 5.1, 6.1, 7.1_

- [x] 9. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are property-based or unit test tasks (optional for faster MVP but recommended)
- Each task references specific requirements for traceability
- The system integrates into existing .NET 8 services via DI extension methods
- Admin API authentication/authorization is delegated to the API Gateway layer (out of scope for this spec)
- The JSONB type handler for Dapper maps `List<string>` ↔ PostgreSQL JSONB arrays

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "2.1"] },
    { "id": 1, "tasks": ["1.2", "1.3"] },
    { "id": 2, "tasks": ["2.2"] },
    { "id": 3, "tasks": ["2.3", "2.4", "3.1"] },
    { "id": 4, "tasks": ["3.2", "3.3", "3.4", "3.5", "3.6", "5.1"] },
    { "id": 5, "tasks": ["5.2", "6.1", "6.2", "6.3", "6.4"] },
    { "id": 6, "tasks": ["6.5", "7.1"] },
    { "id": 7, "tasks": ["7.2", "8.1", "8.2"] }
  ]
}
```
