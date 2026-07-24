# Implementation Plan: Event-Driven Outbox Worker

## Overview

This plan implements a .NET 8 BackgroundService-based Outbox Worker that polls PostgreSQL outbox tables and publishes events to SNS topics. The implementation covers application code (C#), infrastructure (Terraform), and database migrations (SQL). A single codebase supports two deployment variants (Ledger and Product) via environment configuration.

## Tasks

- [ ] 1. Set up project structure and core interfaces
  - [ ] 1.1 Create .NET 8 Worker Service project with required dependencies
    - Create solution and project files for `OutboxWorker` (Worker Service template)
    - Add NuGet packages: Dapper, Npgsql, AWSSDK.SimpleNotificationService, Serilog.AspNetCore, Serilog.Sinks.Console, Serilog.Formatting.Compact, Microsoft.Extensions.Http.Resilience (Polly v8), FsCheck.Xunit
    - Configure `Program.cs` shell with Generic Host builder
    - _Requirements: 1.1, 1.2, 4.5, 8.1_

  - [ ] 1.2 Define domain models and configuration classes
    - Create `OutboxEvent` sealed record with Id, EventType, Payload, CorrelationId, CreatedAt, PublishedAt
    - Create `BatchResult` sealed record with TotalFetched, SuccessCount, FailureCount, Duration
    - Create `PublishResult` sealed record with Success, MessageId, ErrorMessage
    - Create `OutboxWorkerOptions` class with PollingInterval, BatchSize, SnsTopicArn, MaxRetryAttempts, HealthCheckPort, ShutdownTimeout (with defaults)
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6_

  - [ ] 1.3 Define core interfaces (IOutboxRepository, IEventPublisher, IOutboxProcessor)
    - Create `IOutboxRepository` with `GetUnpublishedEventsAsync(int batchSize, CancellationToken)` and `MarkAsPublishedAsync(Guid eventId, DateTimeOffset publishedAt, CancellationToken)`
    - Create `IEventPublisher` with `PublishAsync(OutboxEvent, CancellationToken)` returning `PublishResult`
    - Create `IOutboxProcessor` with `ProcessBatchAsync(CancellationToken)` returning `BatchResult`
    - _Requirements: 1.5, 2.1, 2.2, 3.1_

- [ ] 2. Implement configuration binding and host setup
  - [ ] 2.1 Implement environment variable binding to OutboxWorkerOptions
    - Bind `OUTBOX_POLLING_INTERVAL_SECONDS`, `OUTBOX_BATCH_SIZE`, `OUTBOX_SNS_TOPIC_ARN`, `OUTBOX_MAX_RETRY_ATTEMPTS`, `OUTBOX_HEALTH_PORT`, `OUTBOX_SHUTDOWN_TIMEOUT_SECONDS` to `OutboxWorkerOptions`
    - Apply defaults when environment variables are absent
    - Register `OutboxWorkerOptions` in DI as singleton
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5, 11.6_

  - [ ]* 2.2 Write property test for configuration parsing (Property 9)
    - **Property 9: Configuration Parsing with Defaults**
    - For any valid positive integer set as an environment variable, verify the resulting OutboxWorkerOptions contains that value; when absent, verify documented defaults are used
    - **Validates: Requirements 11.1, 11.2, 11.4**

  - [ ] 2.3 Configure Serilog JSON structured logging
    - Configure Serilog with JSON formatter writing to stdout
    - Add enrichers for correlation context
    - Register Serilog as the logging provider
    - _Requirements: 8.1_

  - [ ] 2.4 Configure Polly v8 resilience pipelines
    - Register `sns-publish` pipeline: exponential backoff + jitter, configurable max retries, 10s timeout
    - Register `database` pipeline: exponential backoff + jitter, 2 retries, 5s timeout
    - Handle `AmazonSimpleNotificationServiceException` (5xx), `TaskCanceledException`, `HttpRequestException` for SNS
    - Handle `NpgsqlException` (transient), `TimeoutException` for database
    - _Requirements: 4.1, 4.2, 4.5_

- [ ] 3. Implement database layer
  - [ ] 3.1 Implement OutboxRepository with Dapper
    - Implement `GetUnpublishedEventsAsync`: query `outbox_events` where `published_at IS NULL` ordered by `created_at ASC`, limited by batch size
    - Implement `MarkAsPublishedAsync`: update `published_at` for a given event ID
    - Use async methods with CancellationToken propagation throughout
    - Apply `database` resilience pipeline to all queries
    - _Requirements: 1.1, 1.2, 1.5, 3.1, 10.3, 10.4_

  - [ ]* 3.2 Write property test for event ordering (Property 1)
    - **Property 1: Event Ordering Invariant**
    - For any set of unpublished events with varying timestamps, verify `GetUnpublishedEventsAsync` returns them sorted ascending by CreatedAt
    - **Validates: Requirements 1.1, 1.2**

  - [ ]* 3.3 Write property test for batch size limit (Property 2)
    - **Property 2: Batch Size Limit**
    - For any configured batch size B and any number of events N, verify `GetUnpublishedEventsAsync(B)` returns at most B events
    - **Validates: Requirements 1.5**

- [ ] 4. Implement SNS event publisher
  - [ ] 4.1 Implement SnsEventPublisher with AWS SDK
    - Implement `PublishAsync` wrapping `IAmazonSimpleNotificationService.PublishAsync`
    - Set `MessageDeduplicationId` to the event's `Id.ToString()`
    - Set `MessageAttributes` with `EventType` and `CorrelationId`
    - Format message body from event payload
    - Apply `sns-publish` resilience pipeline
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 5.1, 5.3_

  - [ ]* 4.2 Write property test for deduplication ID correctness (Property 4)
    - **Property 4: Deduplication ID Correctness**
    - For any outbox event with a random GUID, verify the SNS publish request contains MessageDeduplicationId equal to the event Id string
    - **Validates: Requirements 2.3, 5.1**

  - [ ]* 4.3 Write property test for event format completeness (Property 5)
    - **Property 5: Event Format Completeness**
    - For any outbox event, verify the formatted SNS message contains all required fields for its event type
    - **Validates: Requirements 2.4, 2.5**

  - [ ]* 4.4 Write property test for CorrelationId propagation (Property 8)
    - **Property 8: CorrelationId Propagation**
    - For any outbox event with a correlationId, verify the published SNS message attributes include a CorrelationId attribute with the same value
    - **Validates: Requirements 5.3**

- [ ] 5. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 6. Implement outbox processor
  - [ ] 6.1 Implement OutboxProcessor orchestrating fetch → publish → acknowledge
    - Inject `IOutboxRepository`, `IEventPublisher`, `OutboxWorkerOptions`, `ILogger`
    - Fetch batch of unpublished events from repository
    - Iterate through events, publishing each via `IEventPublisher`
    - On success: call `MarkAsPublishedAsync` with current timestamp
    - On failure: log error, skip event (leave unmarked for next cycle)
    - Return `BatchResult` with counts and duration
    - _Requirements: 1.5, 2.1, 2.2, 3.1, 3.2, 3.3, 4.3, 8.2, 8.3, 8.4_

  - [ ]* 6.2 Write property test for publish-per-event guarantee (Property 3)
    - **Property 3: Publish-Per-Event Guarantee**
    - For any batch of N events, verify `ProcessBatchAsync` invokes `PublishAsync` exactly N times, once per event in order
    - **Validates: Requirements 2.1, 2.2**

  - [ ]* 6.3 Write property test for selective acknowledgment (Property 6)
    - **Property 6: Selective Acknowledgment**
    - For any batch with random success/failure patterns, verify `MarkAsPublishedAsync` is called exactly once per successful event and zero times per failed event
    - **Validates: Requirements 3.1, 3.2, 3.3**

  - [ ]* 6.4 Write property test for retry attempt limit (Property 7)
    - **Property 7: Retry Attempt Limit**
    - For any configured max retry R, when an event persistently fails, verify total publish attempts ≤ R + 1
    - **Validates: Requirements 4.2**

- [ ] 7. Implement worker service and health check
  - [ ] 7.1 Implement OutboxWorkerService (BackgroundService polling loop)
    - Override `ExecuteAsync` with polling loop using `PeriodicTimer` at configured interval
    - Delegate each cycle to `IOutboxProcessor.ProcessBatchAsync`
    - Respect `CancellationToken` for graceful shutdown (stop accepting new cycles, complete current batch)
    - Log configuration values on startup
    - Handle database failures during poll: log and wait for next interval
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 4.4, 6.1, 6.2, 6.3, 6.4, 6.5, 8.5_

  - [ ] 7.2 Implement health check endpoint (minimal API /health)
    - Create ASP.NET Core minimal API on configurable port (default 8080)
    - `GET /health` returns HTTP 200 when database connection is valid
    - `GET /health` returns HTTP 503 when database connection fails
    - Respond within 5 seconds timeout
    - _Requirements: 7.1, 7.2, 7.3, 7.4_

  - [ ]* 7.3 Write unit tests for worker service and health check
    - Test: no publish calls when no events exist (empty batch)
    - Test: health check returns 200 when DB reachable
    - Test: health check returns 503 when DB unreachable
    - Test: worker stops polling after cancellation token triggered
    - Test: in-progress batch completes before shutdown
    - Test: startup logs configuration values
    - Test: DB failure during poll logs error, no crash, waits for next interval
    - _Requirements: 1.4, 4.4, 6.1, 6.2, 6.3, 7.2, 7.3, 8.5_

- [ ] 8. Wire DI container and Program.cs
  - [ ] 8.1 Complete Program.cs with full DI registration and host configuration
    - Register `IOutboxRepository` → `OutboxRepository`
    - Register `IEventPublisher` → `SnsEventPublisher`
    - Register `IOutboxProcessor` → `OutboxProcessor`
    - Register `OutboxWorkerService` as hosted service
    - Register `IAmazonSimpleNotificationService` from AWS SDK
    - Configure `IDbConnectionFactory` with connection string from SSM Parameter Store
    - Configure shutdown timeout from `OutboxWorkerOptions`
    - Map `/health` endpoint
    - _Requirements: 10.1, 10.2, 10.5_

- [ ] 9. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 10. Create SQL migration for outbox_events table
  - [ ] 10.1 Create SQL migration script for outbox_events schema
    - Create `outbox_events` table with columns: id (UUID PK), event_type (VARCHAR 100), payload (JSONB), correlation_id (VARCHAR 100), created_at (TIMESTAMPTZ), published_at (TIMESTAMPTZ nullable)
    - Create partial index `idx_outbox_unpublished` on `created_at ASC WHERE published_at IS NULL`
    - _Requirements: 1.1, 1.2, 3.1_

- [ ] 11. Create Terraform module for ECS task definitions
  - [ ] 11.1 Create Terraform module for outbox worker ECS tasks
    - Define ECS task definition for Ledger Outbox Worker (0.25 vCPU, 0.5 GB RAM)
    - Define ECS task definition for Product Outbox Worker (0.25 vCPU, 0.5 GB RAM)
    - Configure private subnet placement (no public IP)
    - Configure task IAM role with SNS publish permission scoped to specific topic
    - Configure execution role with SSM read permission scoped to environment path
    - Configure environment variables for each variant (topic ARN, connection string path)
    - Configure health check using container health check command
    - Reference shared `{env}-ecs-cluster`
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5_

  - [ ]* 11.2 Write Terraform validation tests
    - Validate task CPU/memory configuration (0.25 vCPU / 0.5 GB)
    - Validate no public IP assignment in network configuration
    - Validate IAM policy scoping for SNS and SSM
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5_

- [ ] 12. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document (FsCheck + xUnit)
- Unit tests validate specific examples and edge cases
- The single codebase supports both Ledger and Product variants via environment variables
- Terraform module and SQL migration are independent of the application code and can be developed in parallel

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "10.1"] },
    { "id": 1, "tasks": ["1.2", "1.3"] },
    { "id": 2, "tasks": ["2.1", "2.3", "2.4"] },
    { "id": 3, "tasks": ["2.2", "3.1"] },
    { "id": 4, "tasks": ["3.2", "3.3", "4.1"] },
    { "id": 5, "tasks": ["4.2", "4.3", "4.4", "6.1"] },
    { "id": 6, "tasks": ["6.2", "6.3", "6.4", "7.1", "7.2"] },
    { "id": 7, "tasks": ["7.3", "8.1"] },
    { "id": 8, "tasks": ["11.1"] },
    { "id": 9, "tasks": ["11.2"] }
  ]
}
```
