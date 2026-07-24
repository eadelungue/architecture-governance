# Requirements Document

## Introduction

This document specifies the requirements for a Feature Toggles System for the CorePoints platform. The system provides a centralized mechanism to control feature visibility at runtime without code redeployment. Feature flags are stored externally in a PostgreSQL table (accessed via Dapper), evaluated at the Use Case layer, and cached in-memory with a short TTL to minimize database hits.

The system supports canary releases by enabling flags per user group (e.g., internal employees first). An Admin REST API allows flag management (create, update, enable/disable, delete). Application code checks flag state through an `IFeatureToggleService` interface.

## Glossary

- **Feature_Toggle_Service**: The application service that evaluates whether a feature flag is enabled for a given user or group context
- **Feature_Flag**: A named toggle stored in the `feature_flags` database table that controls feature visibility at runtime
- **Flag_Repository**: The data access component that reads and writes Feature_Flag records from PostgreSQL via Dapper
- **Flag_Cache**: An in-memory cache (IMemoryCache) that stores Feature_Flag state with a 60-second TTL to reduce database load
- **Admin_API**: A set of REST endpoints for managing Feature_Flags (create, read, update, delete)
- **Target_Group**: A named user segment (stored as JSONB array) that a Feature_Flag can be scoped to for canary releases
- **Flag_Evaluation**: The process of determining whether a Feature_Flag is enabled for a specific user or group context
- **Use_Case_Layer**: The application/business logic layer where Flag_Evaluation occurs, never exposed to frontend clients

## Requirements

### Requirement 1: Feature Flag Storage

**User Story:** As a platform operator, I want feature flags stored in a PostgreSQL table, so that flag state is external to code and manageable without redeployment.

#### Acceptance Criteria

1. THE Flag_Repository SHALL persist Feature_Flags in a `feature_flags` table with columns: id (UUID), name (VARCHAR unique), description (TEXT), is_enabled (BOOLEAN), target_groups (JSONB), created_at (TIMESTAMPTZ), updated_at (TIMESTAMPTZ)
2. THE Flag_Repository SHALL use Dapper for all database operations
3. THE Flag_Repository SHALL use asynchronous database access methods with CancellationToken propagation
4. WHEN a Feature_Flag is created, THE Flag_Repository SHALL set created_at and updated_at to the current UTC timestamp
5. WHEN a Feature_Flag is updated, THE Flag_Repository SHALL set updated_at to the current UTC timestamp

### Requirement 2: Flag Evaluation Service

**User Story:** As a developer, I want to check if a feature is enabled for a given user or group through a simple service interface, so that I can gate features in Use Case code without coupling to storage details.

#### Acceptance Criteria

1. THE Feature_Toggle_Service SHALL expose an `IsEnabledAsync(string flagName, string? userId, string? group)` method returning a boolean
2. WHEN a Feature_Flag with the given name does not exist, THE Feature_Toggle_Service SHALL return false
3. WHEN a Feature_Flag exists and is_enabled is false, THE Feature_Toggle_Service SHALL return false regardless of target_groups
4. WHEN a Feature_Flag exists, is_enabled is true, and target_groups is empty or null, THE Feature_Toggle_Service SHALL return true for all users
5. WHEN a Feature_Flag exists, is_enabled is true, and target_groups contains entries, THE Feature_Toggle_Service SHALL return true only if the provided group matches one of the target_groups
6. WHEN a Feature_Flag exists, is_enabled is true, target_groups contains entries, and no group is provided, THE Feature_Toggle_Service SHALL return false

### Requirement 3: In-Memory Caching

**User Story:** As a platform operator, I want flag state cached in memory with a short TTL, so that the system avoids a database query on every flag check while still reflecting changes within a reasonable time.

#### Acceptance Criteria

1. THE Feature_Toggle_Service SHALL read Feature_Flag state from the Flag_Cache before querying the Flag_Repository
2. WHEN the Flag_Cache does not contain the requested Feature_Flag, THE Feature_Toggle_Service SHALL query the Flag_Repository and store the result in the Flag_Cache
3. THE Flag_Cache SHALL expire entries after a configurable TTL with a default of 60 seconds
4. WHEN a Feature_Flag is created, updated, or deleted via the Admin_API, THE Flag_Cache SHALL invalidate the affected entry immediately
5. THE Flag_Cache SHALL use IMemoryCache from Microsoft.Extensions.Caching.Memory

### Requirement 4: Admin API — List Flags

**User Story:** As a platform administrator, I want to list all feature flags, so that I can see the current state of all toggles.

#### Acceptance Criteria

1. WHEN a GET request is sent to `/admin/flags`, THE Admin_API SHALL return HTTP 200 with a JSON array of all Feature_Flags
2. THE Admin_API SHALL include id, name, description, is_enabled, target_groups, created_at, and updated_at for each flag in the response

### Requirement 5: Admin API — Create Flag

**User Story:** As a platform administrator, I want to create new feature flags, so that I can prepare toggles for upcoming features.

#### Acceptance Criteria

1. WHEN a POST request with a valid body is sent to `/admin/flags`, THE Admin_API SHALL create the Feature_Flag and return HTTP 201 with the created flag
2. WHEN a POST request contains a flag name that already exists, THE Admin_API SHALL return HTTP 409 Conflict with a Problem Details response
3. WHEN a POST request is missing the name field, THE Admin_API SHALL return HTTP 400 Bad Request with a Problem Details response
4. THE Admin_API SHALL accept name (required), description (optional), is_enabled (optional, default false), and target_groups (optional, default empty array) in the POST body

### Requirement 6: Admin API — Update Flag

**User Story:** As a platform administrator, I want to update existing feature flags (enable/disable, change target groups), so that I can control feature rollout.

#### Acceptance Criteria

1. WHEN a PUT request with a valid body is sent to `/admin/flags/{name}`, THE Admin_API SHALL update the Feature_Flag and return HTTP 200 with the updated flag
2. WHEN a PUT request references a flag name that does not exist, THE Admin_API SHALL return HTTP 404 Not Found with a Problem Details response
3. THE Admin_API SHALL allow updating description, is_enabled, and target_groups fields via the PUT body

### Requirement 7: Admin API — Delete Flag

**User Story:** As a platform administrator, I want to delete feature flags that are no longer needed, so that the flag list stays clean.

#### Acceptance Criteria

1. WHEN a DELETE request is sent to `/admin/flags/{name}`, THE Admin_API SHALL delete the Feature_Flag and return HTTP 204 No Content
2. WHEN a DELETE request references a flag name that does not exist, THE Admin_API SHALL return HTTP 404 Not Found with a Problem Details response

### Requirement 8: Flag-Gated Endpoint Filter

**User Story:** As a developer, I want an attribute or filter that gates an endpoint behind a feature flag, so that I can declaratively protect endpoints without manual flag checks.

#### Acceptance Criteria

1. THE Feature_Toggle_Service SHALL provide a `FeatureGateAttribute` or endpoint filter that accepts a flag name parameter
2. WHEN a request reaches a flag-gated endpoint and the Feature_Flag is disabled for the current user context, THE filter SHALL return HTTP 404 Not Found
3. WHEN a request reaches a flag-gated endpoint and the Feature_Flag is enabled for the current user context, THE filter SHALL allow the request to proceed normally
4. THE filter SHALL resolve user context (userId, group) from the current HTTP request claims

### Requirement 9: Use Case Layer Integration

**User Story:** As a developer, I want the flag evaluation to occur at the Use Case layer, so that frontend clients never see flag logic and the system follows governance standards.

#### Acceptance Criteria

1. THE Feature_Toggle_Service SHALL be injectable via dependency injection (IServiceCollection) into Use Case classes
2. THE Feature_Toggle_Service SHALL expose the IFeatureToggleService interface for consumers
3. THE Feature_Toggle_Service SHALL evaluate flags exclusively at the Use_Case_Layer without exposing flag state or logic to API response payloads

### Requirement 10: Canary Release Support

**User Story:** As a platform operator, I want to enable features for specific user groups before a full rollout, so that I can validate new features with a subset of users first.

#### Acceptance Criteria

1. THE Feature_Flag target_groups field SHALL store group names as a JSONB array of strings (e.g., ["internal-employees", "beta-testers"])
2. WHEN evaluating a flag with target_groups, THE Feature_Toggle_Service SHALL match the user's group claim against the target_groups array
3. WHEN a user belongs to a matching Target_Group, THE Feature_Toggle_Service SHALL return true (flag is enabled for that user)
4. WHEN a user does not belong to any Target_Group in the flag, THE Feature_Toggle_Service SHALL return false (flag is disabled for that user)

### Requirement 11: Error Handling

**User Story:** As a platform operator, I want the system to handle errors gracefully, so that flag evaluation failures do not break application flow.

#### Acceptance Criteria

1. IF the Flag_Repository is unreachable during evaluation, THEN THE Feature_Toggle_Service SHALL return false (fail-closed) and log the error
2. IF the Flag_Cache encounters an error, THEN THE Feature_Toggle_Service SHALL fall through to the Flag_Repository
3. THE Admin_API SHALL return RFC 7807 Problem Details responses for all error scenarios
