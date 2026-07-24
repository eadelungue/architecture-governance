# Requirements Document

## Introduction

This document specifies the requirements for the Transactional Outbox Worker feature of the CorePoints platform. The system implements two dedicated background workers that reliably publish domain events from outbox tables to their respective SNS topics, ensuring guaranteed delivery without dual-writes.

The **Ledger Outbox Worker** polls the Ledger database outbox table and publishes read-only transaction notification events to the `{env}-ledger-events-topic`. The **Product Outbox Worker** polls the Product Service database outbox table and publishes domain events (e.g., CashbackCredited, TransferCompleted) to the `{env}-events-topic`.

Both workers run as separate ECS Fargate tasks, isolated from their respective API containers, and follow the .NET 8 BackgroundService pattern with Dapper for database access.

## Glossary

- **Outbox_Worker**: A .NET 8 BackgroundService that polls an outbox table in a PostgreSQL database, reads unpublished events, publishes them to an SNS topic, and marks them as published
- **Ledger_Outbox_Worker**: The Outbox_Worker instance dedicated to the Ledger Core database, publishing to the `{env}-ledger-events-topic`
- **Product_Outbox_Worker**: The Outbox_Worker instance dedicated to the Product Service database, publishing to the `{env}-events-topic`
- **Outbox_Table**: A PostgreSQL table that stores events pending publication, written within the same ACID transaction that modifies business state
- **Outbox_Event**: A single row in the Outbox_Table representing a domain or notification event awaiting publication
- **SNS_Topic**: An Amazon Simple Notification Service topic that receives published events for fan-out distribution
- **Polling_Interval**: The configurable time period between consecutive polls of the Outbox_Table (default: 5 seconds)
- **Batch**: A group of Outbox_Events fetched and processed together in a single polling cycle
- **Exponential_Backoff**: A retry strategy where the delay between consecutive retries increases exponentially
- **MessageDeduplicationId**: A unique identifier sent with each SNS publish to enable deduplication and idempotent delivery
- **SIGTERM**: The Unix termination signal sent by ECS to indicate graceful shutdown
- **Health_Check_Endpoint**: An HTTP endpoint exposed by the worker for ECS to verify liveness

## Requirements

### Requirement 1: Outbox Table Polling

**User Story:** As a platform operator, I want the outbox workers to continuously poll their respective outbox tables at a configurable interval, so that events are published with minimal delay after being persisted.

#### Acceptance Criteria

1. WHEN the Polling_Interval elapses, THE Ledger_Outbox_Worker SHALL query the Ledger Outbox_Table for unpublished Outbox_Events ordered by creation timestamp
2. WHEN the Polling_Interval elapses, THE Product_Outbox_Worker SHALL query the Product Outbox_Table for unpublished Outbox_Events ordered by creation timestamp
3. THE Outbox_Worker SHALL use a configurable Polling_Interval with a default value of 5 seconds
4. WHEN no unpublished Outbox_Events exist in the Outbox_Table, THE Outbox_Worker SHALL wait for the next Polling_Interval without performing additional work
5. THE Outbox_Worker SHALL process Outbox_Events in Batches with a configurable batch size

### Requirement 2: Event Publication to SNS

**User Story:** As a platform operator, I want the outbox workers to reliably publish events to the correct SNS topics, so that downstream consumers receive domain notifications.

#### Acceptance Criteria

1. WHEN the Ledger_Outbox_Worker fetches unpublished Outbox_Events, THE Ledger_Outbox_Worker SHALL publish each event to the SNS_Topic `{env}-ledger-events-topic`
2. WHEN the Product_Outbox_Worker fetches unpublished Outbox_Events, THE Product_Outbox_Worker SHALL publish each event to the SNS_Topic `{env}-events-topic`
3. THE Outbox_Worker SHALL include a MessageDeduplicationId derived from the Outbox_Event unique identifier in each SNS publish request
4. THE Outbox_Worker SHALL format Ledger events following the Event Notification pattern containing eventType, transactionId, debitAccountId, creditAccountId, amount, timestamp, and correlationId
5. THE Outbox_Worker SHALL format Product events following the Event Notification pattern containing eventType, entity identifier, and correlationId

### Requirement 3: Publication Acknowledgment

**User Story:** As a platform operator, I want processed events to be marked as published, so that the same event is not published multiple times under normal operation.

#### Acceptance Criteria

1. WHEN an Outbox_Event is successfully published to the SNS_Topic, THE Outbox_Worker SHALL mark the Outbox_Event as published in the Outbox_Table with the publication timestamp
2. WHEN a Batch of Outbox_Events is processed, THE Outbox_Worker SHALL mark only the successfully published events as published
3. IF an Outbox_Event fails to publish, THEN THE Outbox_Worker SHALL leave the Outbox_Event unmarked so it is retried in a subsequent polling cycle

### Requirement 4: Retry and Error Handling

**User Story:** As a platform operator, I want the workers to handle transient failures gracefully with exponential backoff, so that temporary SNS or network issues do not cause permanent event loss.

#### Acceptance Criteria

1. IF an SNS publish operation fails with a transient error, THEN THE Outbox_Worker SHALL retry the operation using Exponential_Backoff with jitter
2. THE Outbox_Worker SHALL limit retries to a configurable maximum attempt count per Outbox_Event within a single polling cycle
3. IF an Outbox_Event exceeds the maximum retry attempts within a single polling cycle, THEN THE Outbox_Worker SHALL log the failure and leave the event for the next polling cycle
4. IF the database connection fails during polling, THEN THE Outbox_Worker SHALL log the error and wait for the next Polling_Interval before retrying
5. THE Outbox_Worker SHALL use Polly resilience policies for all external calls including SNS publish and database access

### Requirement 5: Idempotent Publication

**User Story:** As a platform operator, I want event publication to be idempotent, so that duplicate publishes caused by retries or reprocessing do not create duplicate downstream effects.

#### Acceptance Criteria

1. THE Outbox_Worker SHALL use the Outbox_Event unique identifier as the MessageDeduplicationId when publishing to SNS
2. WHEN the same Outbox_Event is published more than once due to a failure between publish and mark-as-published, THE SNS_Topic SHALL deduplicate based on the MessageDeduplicationId
3. THE Outbox_Worker SHALL include the original correlationId from the Outbox_Event in the published message attributes for end-to-end tracing

### Requirement 6: Graceful Shutdown

**User Story:** As a platform operator, I want the workers to shut down gracefully when receiving SIGTERM, so that in-progress work completes without data corruption.

#### Acceptance Criteria

1. WHEN the Outbox_Worker receives a SIGTERM signal, THE Outbox_Worker SHALL stop accepting new polling cycles
2. WHEN the Outbox_Worker receives a SIGTERM signal, THE Outbox_Worker SHALL complete the current in-progress Batch before terminating
3. WHEN the Outbox_Worker completes graceful shutdown, THE Outbox_Worker SHALL exit with code 0
4. THE Outbox_Worker SHALL complete graceful shutdown within a configurable timeout (default: 30 seconds)
5. IF the graceful shutdown timeout is exceeded, THEN THE Outbox_Worker SHALL terminate immediately and log a warning

### Requirement 7: Health Check

**User Story:** As a platform operator, I want each worker to expose a health check endpoint, so that ECS can determine whether the worker task is healthy.

#### Acceptance Criteria

1. THE Outbox_Worker SHALL expose a Health_Check_Endpoint on a configurable HTTP port
2. WHILE the Outbox_Worker is running and able to connect to the database, THE Health_Check_Endpoint SHALL respond with HTTP 200
3. WHILE the Outbox_Worker cannot connect to the database, THE Health_Check_Endpoint SHALL respond with HTTP 503
4. THE Health_Check_Endpoint SHALL respond within 5 seconds

### Requirement 8: Observability and Logging

**User Story:** As a platform operator, I want structured logging and metrics from the workers, so that I can monitor event publication health and troubleshoot issues.

#### Acceptance Criteria

1. THE Outbox_Worker SHALL emit structured JSON logs to stdout for consumption by CloudWatch Logs
2. WHEN an Outbox_Event is successfully published, THE Outbox_Worker SHALL log the event identifier, event type, and publication latency
3. WHEN an Outbox_Event fails to publish, THE Outbox_Worker SHALL log the event identifier, error details, and retry attempt number
4. THE Outbox_Worker SHALL log the number of events processed and the batch processing duration at the end of each polling cycle
5. WHEN the Outbox_Worker starts, THE Outbox_Worker SHALL log the configuration values including Polling_Interval, batch size, and target SNS_Topic ARN

### Requirement 9: Deployment as ECS Fargate Task

**User Story:** As a platform operator, I want the workers deployed as standalone ECS Fargate tasks, so that they run independently from the API containers with minimal resource usage.

#### Acceptance Criteria

1. THE Ledger_Outbox_Worker SHALL run as an ECS Fargate task in the shared `{env}-ecs-cluster` with 0.25 vCPU and 0.5 GB RAM
2. THE Product_Outbox_Worker SHALL run as an ECS Fargate task in the shared `{env}-ecs-cluster` with 0.25 vCPU and 0.5 GB RAM
3. THE Outbox_Worker ECS tasks SHALL run in private subnets without public IP assignment
4. THE Outbox_Worker ECS task role SHALL have permissions restricted to publishing to its designated SNS_Topic only
5. THE Outbox_Worker ECS execution role SHALL have permissions to read connection strings from SSM Parameter Store restricted to its environment path

### Requirement 10: Database Access

**User Story:** As a platform operator, I want each worker to connect to its respective database via RDS Proxy using Dapper, so that connection pooling is managed efficiently and consistent with project standards.

#### Acceptance Criteria

1. THE Ledger_Outbox_Worker SHALL connect to the Ledger PostgreSQL database through the Ledger RDS Proxy endpoint
2. THE Product_Outbox_Worker SHALL connect to the Product PostgreSQL database through the Product RDS Proxy endpoint
3. THE Outbox_Worker SHALL use Dapper for all database queries to the Outbox_Table
4. THE Outbox_Worker SHALL use asynchronous database access methods with CancellationToken propagation
5. THE Outbox_Worker SHALL retrieve database connection strings from SSM Parameter Store at startup

### Requirement 11: Configuration Management

**User Story:** As a platform operator, I want all worker configuration to be externalized, so that I can adjust behavior per environment without redeployment.

#### Acceptance Criteria

1. THE Outbox_Worker SHALL read the Polling_Interval from environment variables with a default of 5 seconds
2. THE Outbox_Worker SHALL read the batch size from environment variables with a configurable default
3. THE Outbox_Worker SHALL read the target SNS_Topic ARN from environment variables
4. THE Outbox_Worker SHALL read the maximum retry attempts from environment variables with a default of 3
5. THE Outbox_Worker SHALL read the Health_Check_Endpoint port from environment variables with a default of 8080
6. THE Outbox_Worker SHALL read the graceful shutdown timeout from environment variables with a default of 30 seconds
