# ADR-010: Product Catalog Service Extraction

**Status**: Implemented  
**Date**: 2026-01-14  
**Decision Makers**: Architecture Team  
**Related**: ADR-005 (Service Decomposition Strategy), ADR-007 (API Gateway Pattern)

## Context

As part of our microservices migration strategy, we need to extract the first service from the monolith. The Product Catalog domain has been identified as the optimal first slice for extraction based on low risk, high value, and clear boundaries.

## Decision

We will extract the Product Catalog Service as the first microservice from the RetailMonolith application.

### Service Characteristics

**Bounded Context**: Product catalog management
- Product entity (SKU, name, description, price, category)
- Product activation/deactivation
- Product search and filtering

**API Endpoints**:
- `GET /api/products` - List active products
- `GET /api/products/{id}` - Get product by ID
- `GET /api/products/sku/{sku}` - Get product by SKU

**Technology Stack**:
- ASP.NET Core 10 Web API
- Entity Framework Core 10
- SQL Server (separate database)
- Redis caching (1-hour TTL)
- Serilog for structured logging

### Migration Approach

**Phase 1A: Dual Read (Current Implementation)**
```
Writes:  Monolith DB ← Monolith
Reads:   Product Service DB ← Product Service
Sync:    Monolith DB → Product Service DB (background job - to be implemented)
```

**Phase 1B: Dual Write (Future)**
```
Writes:  Monolith DB ← Monolith
         Product Service DB ← Product Service
Reads:   Product Service DB ← Product Service
```

**Phase 1C: Full Ownership (Future)**
```
Writes:  Product Service DB ← Product Service
Reads:   Product Service DB ← Product Service
Monolith: No longer accesses Products table
```

## Rationale

### Why Product Catalog First?

1. **Low Risk**:
   - Read-heavy workload (minimal write operations)
   - No complex business logic (simple CRUD)
   - No external dependencies (no payment gateway, no complex orchestration)
   - No critical transactional requirements

2. **High Value**:
   - Enables independent scaling of most-accessed domain
   - Product catalog is browsed 100x more than checkout
   - Caching opportunities for significant performance gains (80%+ hit ratio expected)
   - Simple API surface reduces integration complexity

3. **Clear Boundaries**:
   - Products domain well-isolated in current code
   - Single table (`Products`) with no foreign keys to other domains
   - No bidirectional dependencies
   - Other services reference products by SKU (string), not foreign key

4. **Learning Opportunity**:
   - Team learns microservices patterns with low-risk domain
   - Establish patterns for future extractions (API contracts, deployment, testing)
   - Validate infrastructure and tooling choices
   - Build confidence before tackling complex domains

5. **No Data Coupling**:
   - Cart and Order services store product snapshots (SKU, name, price) at transaction time
   - No need for distributed transactions or complex data synchronization
   - Can duplicate product data in other services without breaking referential integrity

### Cache-Aside Pattern

Implemented Redis caching with cache-aside pattern:
- On read: Check cache → If miss, query DB → Store in cache
- 1-hour TTL (configurable)
- Automatic cache invalidation on updates (future)
- Expected cache hit ratio: > 80%

Benefits:
- Reduced database load
- Improved response times (p95 < 200ms target)
- Better handling of read-heavy traffic

## Consequences

### Positive

- **Independent Deployment**: Product Service can be deployed without monolith changes
- **Independent Scaling**: Scale product reads independently of other services
- **Performance**: Caching reduces database load and improves response times
- **Resilience**: Service failures isolated from monolith
- **Team Learning**: First hands-on experience with microservices patterns

### Negative

- **Operational Complexity**: Additional service to monitor and maintain
- **Data Synchronization**: Need to implement and monitor data sync from monolith
- **Eventual Consistency**: Product data may be slightly stale in Product Service
- **Network Latency**: Inter-service calls add network overhead (mitigated by caching)

### Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Data sync lag causes stale data | Medium | Monitor sync lag, alert if > 1 minute, display "last updated" timestamp |
| Cache invalidation issues | Medium | Conservative 1-hour TTL, manual cache clear endpoint |
| Service downtime impacts browsing | High | Health checks, automatic restarts, fallback to monolith via API Gateway |
| Performance degradation | Medium | Extensive load testing, auto-scaling, monitoring |

## Implementation Details

### Database

**Database**: `ProductServiceDB` (separate from monolith)

**Schema**: 
- Products table (same schema as monolith)
- Unique index on SKU

**Migration**: EF Core Migrations

### Caching

**Technology**: Redis (StackExchange.Redis)

**Strategy**: Cache-aside with 1-hour TTL

**Cache Keys**:
- `all-products` - All active products
- `product-{id}` - Single product by ID
- `product-sku-{sku}` - Single product by SKU

### Logging

**Technology**: Serilog

**Log Format**: Structured JSON logs

**Key Fields**:
- Timestamp
- Level (Debug, Information, Warning, Error)
- Message
- CorrelationId (for distributed tracing)
- Service name ("ProductService")

### Health Checks

**Endpoint**: `/health`

**Checks**:
- Database connectivity (EF Core health check)
- Redis connectivity (future)

**Kubernetes Integration**:
- Liveness probe: Restart pod if unhealthy
- Readiness probe: Remove from load balancer if unhealthy

## Deployment

### Local Development
```bash
dotnet run
```

### Docker
```bash
docker build -t product-service:latest .
docker run -p 8080:8080 product-service:latest
```

### Kubernetes
```bash
kubectl apply -f k8s-deployment.yaml
```

**Deployment Strategy**: Rolling update (zero downtime)

**Replicas**: 1 (development), 3 (production)

**Resources**:
- Requests: 256Mi memory, 100m CPU
- Limits: 512Mi memory, 500m CPU

## Testing

### Unit Tests
- ✅ Database context tests (3 tests passing)
- 🔲 API endpoint tests (to be implemented)

### Integration Tests
- 🔲 End-to-end API tests (to be implemented)
- 🔲 Cache behavior tests (to be implemented)

### Performance Tests
- 🔲 Load test: 1000 req/min for 10 minutes
- 🔲 Cache hit ratio validation (target > 80%)
- 🔲 Response time validation (p95 < 200ms)

## Success Criteria

- ✅ Service builds and runs locally
- ✅ All unit tests passing
- ✅ Health check endpoint operational
- ✅ Database migrations working
- ✅ Redis caching implemented
- 🔲 Data sync from monolith operational
- 🔲 API Gateway routing configured
- 🔲 Deployed to Kubernetes
- 🔲 Performance SLAs met
- 🔲 Zero errors in production for 48 hours

## Next Steps

1. Implement data sync from monolith to Product Service
2. Set up YARP API Gateway and routing rules
3. Deploy to Kubernetes cluster
4. Run load tests and validate performance
5. Monitor for 48 hours before declaring Phase 1 complete
6. Document lessons learned
7. Begin Phase 2 (Inventory Service extraction)

## References

- [Migration Plan](/docs/Migration-Plan.md) - Section 3: Phase 1
- [Target Architecture](/docs/Target-Architecture.md) - Section 2.1: Product Catalog Service
- [Test Strategy](/docs/Test-Strategy.md)
- [Strangler Fig Pattern](https://martinfowler.com/bliki/StranglerFigApplication.html)
