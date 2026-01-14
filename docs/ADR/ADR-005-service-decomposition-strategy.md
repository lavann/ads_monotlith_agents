# ADR-005: Service Decomposition Strategy

## Status
Proposed

## Context
The current monolithic architecture (ADR-001) has served well for initial development, but we now need to decompose it into microservices to enable independent scaling, deployment, and team autonomy. We must decide on:

1. **Decomposition approach**: How to identify service boundaries
2. **Migration pattern**: How to safely transition from monolith to services
3. **Service granularity**: How fine-grained should services be
4. **Data ownership**: How to handle data across services

## Decision

### 1. Domain-Driven Design (DDD) for Service Boundaries

We will use Domain-Driven Design principles to identify service boundaries based on **bounded contexts**:

- **Product Catalog Bounded Context** → Product Catalog Service
- **Inventory Management Bounded Context** → Inventory Service  
- **Order Management Bounded Context** → Order Service
- **Shopping Cart Bounded Context** → Cart Service
- **Checkout Orchestration Bounded Context** → Checkout Service
- **Payment Processing Bounded Context** → Payment Service

Each service owns its domain entities and business logic exclusively.

### 2. Strangler Fig Pattern for Migration

We will **NOT** do a big-bang rewrite. Instead, we will use the Strangler Fig pattern:

1. Build new microservices alongside existing monolith
2. Gradually route traffic from monolith to new services
3. Monolith and services coexist during migration
4. Eventually decommission the monolith

This approach minimizes risk and allows continuous delivery of value.

### 3. Start with Read-Heavy, Low-Risk Domains

**First slice**: Product Catalog Service
- Read-heavy (low risk)
- Simple API surface
- No complex business logic
- Clear boundaries (single table)
- High learning value for team

**Subsequent slices**: Inventory → Orders → Cart → Checkout/Payment

### 4. Database per Service

Each microservice will have its own database:
- No shared database between services
- Services communicate via APIs and events
- Data duplication is acceptable (order snapshots product data)
- Eventual consistency between services

### 5. API-First with REST and Events

**Synchronous**: REST APIs for request-response
**Asynchronous**: Events for notifications and eventual consistency

Example:
- Cart Service calls Product Service API to get current price (sync)
- Order Service subscribes to PaymentSucceeded event (async)

## Rationale

### Why DDD for Boundaries?

- **Clear Ownership**: Each team owns a bounded context
- **Loose Coupling**: Bounded contexts are naturally decoupled
- **Business Alignment**: Services map to business capabilities
- **Established Pattern**: Widely used in industry with proven success

Alternative considered: **Technical decomposition** (e.g., UI service, data service, business logic service) → Rejected because it creates tight coupling and doesn't align with business domains.

### Why Strangler Fig?

- **Low Risk**: Monolith remains operational throughout migration
- **Incremental**: Deliver value in small batches, learn and adapt
- **Reversible**: Can rollback each step independently
- **Team Learning**: Team learns microservices gradually, not all at once

Alternative considered: **Big-bang rewrite** → Rejected due to high risk, long timeline, and inability to deliver value during rewrite.

### Why Product Catalog First?

- **80/20 Rule**: Product browsing is 80% of traffic, checkout is 20%
- **Low Risk**: Read-heavy workload, no critical transactions
- **High Value**: Caching can significantly improve performance
- **Learning**: Team learns patterns with low-risk domain before tackling checkout

Alternative considered: **Checkout first** → Rejected because checkout is the most complex flow with highest risk.

### Why Database per Service?

- **Independent Scaling**: Each service scales database independently
- **Technology Freedom**: Can use different database types (SQL, Redis, MongoDB)
- **Schema Evolution**: Change schema without coordinating with other services
- **Failure Isolation**: Database failure affects only one service

Alternative considered: **Shared database** → Rejected because it creates tight coupling, prevents independent deployment, and makes schema changes risky.

## Consequences

### Positive

- **Independent Deployment**: Deploy services without coordinating with other teams
- **Independent Scaling**: Scale high-demand services (product catalog) separately
- **Technology Diversity**: Use best database for each domain (Redis for cart, SQL for orders)
- **Team Autonomy**: Teams can work independently on their services
- **Resilience**: Failure in one service doesn't bring down entire system
- **Learning**: Team learns microservices incrementally with low risk

### Negative

- **Complexity**: Distributed system is more complex than monolith
- **Eventual Consistency**: Must handle data being temporarily out of sync
- **Data Duplication**: Order stores product name/price (snapshot), not foreign key
- **No ACID Across Services**: Checkout becomes a saga (multi-step workflow)
- **Operational Overhead**: More services to monitor, deploy, and maintain
- **Learning Curve**: Team must learn new patterns (saga, event-driven, distributed tracing)

### Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| **Data inconsistency** between services | Reconciliation jobs, monitoring alerts, eventual consistency accepted |
| **Distributed transactions** too complex | Start with simple saga pattern, add tooling (MassTransit) if needed |
| **Team lacks microservices skills** | Training, pair programming, start with simple slice (Product Catalog) |
| **Over-decomposition** (too many services) | Start with 6-7 services, don't split further without strong justification |
| **Under-decomposition** (services too large) | Review service boundaries after Phase 3, refactor if needed |

### Trade-offs Accepted

1. **Accept eventual consistency** between services (e.g., product price change takes time to propagate to carts)
2. **Accept data duplication** for performance and decoupling (e.g., order stores product snapshot)
3. **Accept increased operational complexity** for improved scalability and team autonomy
4. **Accept longer initial development** (migration takes 4-6 months) for faster future feature development

## Implementation Guidelines

### Service Sizing Principles

A service should:
- Be owned by one team (< 10 people)
- Have a single, well-defined bounded context
- Be independently deployable
- Have clear API boundaries
- Own its data exclusively

A service should **NOT**:
- Require coordination with other services for deployment
- Share a database with other services
- Be so small that overhead exceeds value (avoid nano-services)

### When to Split a Service

Consider splitting if:
- Service has multiple distinct responsibilities (violates Single Responsibility Principle)
- Different parts have significantly different scaling needs
- Team is too large (> 10 people) to own service
- Deployment coordination is required between logical sub-components

### When NOT to Split a Service

Avoid splitting if:
- It would require distributed transactions across services
- Services would have very high communication overhead (chatty)
- Splitting doesn't align with business domain boundaries
- Operational complexity increase isn't justified by benefits

## Validation Criteria

After Phase 1 (Product Catalog extraction), validate:
- [ ] Service can be deployed independently
- [ ] Frontend can call service through API Gateway
- [ ] Monolith and service coexist without issues
- [ ] Team understands patterns and can replicate for other services
- [ ] Performance is better or equal to monolith
- [ ] No data corruption or inconsistencies

If validation fails, **pause migration** and address issues before proceeding.

## Related ADRs

- **ADR-001**: Monolithic Architecture (current state)
- **ADR-006**: Container Deployment Model (how services are deployed)
- **ADR-007**: API Gateway Pattern (how services are accessed)
- **ADR-008**: Database per Service (data ownership)
- **ADR-009**: Event-Driven Communication (async patterns)

## Date
2026-01-14

## Participants
- Architecture Team
- Development Team
- DevOps Team

## Review
This ADR should be reviewed after Phase 1 completion to validate assumptions and update based on lessons learned.
