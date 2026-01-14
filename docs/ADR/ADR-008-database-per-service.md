# ADR-008: Database per Service Pattern

## Status
Proposed

## Context
In the monolithic architecture (ADR-001, ADR-002), all domains share a single database (`RetailMonolith`). This creates tight coupling between domains and prevents independent service deployment and scaling.

As we decompose into microservices, we must decide on a data ownership strategy:

1. **Shared Database**: All services continue to access the same database
2. **Database per Service**: Each service has its own isolated database
3. **Hybrid**: Some services share, others have dedicated databases

## Decision

We will implement the **Database per Service** pattern: each microservice will own and exclusively access its own database.

### Database Assignments

| Service | Database | Technology | Rationale |
|---------|----------|------------|-----------|
| Product Service | `ProductsDB` | SQL Server or PostgreSQL | Relational data, ACID transactions |
| Inventory Service | `InventoryDB` | SQL Server | Strong consistency, row-level locking |
| Order Service | `OrdersDB` | SQL Server or PostgreSQL | Relational data, historical records |
| Cart Service | `CartsDB` (+ Redis) | Redis (primary) + SQL (backup) | Session data, fast access, TTL |
| Checkout Service | `CheckoutDB` | SQL Server | Saga state, workflow tracking |
| Payment Service | `PaymentsDB` | SQL Server | Transaction log, audit trail |

### Data Access Rules

1. **Exclusive Ownership**: Only the owning service can read/write its database
2. **No Direct Database Access**: Other services MUST use APIs or events, never direct SQL
3. **No Foreign Keys Across Services**: Use logical references (e.g., SKU string, not Product.Id FK)
4. **Data Duplication Allowed**: Services may cache or snapshot data from other services
5. **Eventual Consistency**: Services synchronize via events, not immediate consistency

### Example: Order References Product

**Before (Monolith)**:
```csharp
// Order has FK to Product
public class Order
{
    public int ProductId { get; set; }  // FK to Products table
    public Product Product { get; set; } // EF navigation property
}
```

**After (Microservices)**:
```csharp
// Order stores snapshot of product data
public class OrderLine
{
    public string Sku { get; set; }      // Logical reference (no FK)
    public string Name { get; set; }     // Snapshot at order time
    public decimal UnitPrice { get; set; } // Snapshot at order time
    public int Quantity { get; set; }
}
```

**Rationale**: Order Service doesn't depend on Product Service being available to render order history. Price/name captured at order time (correct business behavior).

## Rationale

### Why Database per Service?

1. **Independent Deployment**: Change Product schema without touching Order Service
2. **Independent Scaling**: Scale Product database (read-heavy) differently than Order database (write-heavy)
3. **Technology Freedom**: Use SQL for orders, Redis for carts, MongoDB for logs (if needed)
4. **Failure Isolation**: Product database failure doesn't affect Order Service
5. **Team Autonomy**: Product team owns schema, no cross-team coordination
6. **Bounded Context**: Aligns with DDD principles, each service owns its domain data

**Industry Precedent**: Netflix, Amazon, Uber all use database per service pattern.

### Why NOT Shared Database?

**Shared database creates problems**:
- **Tight Coupling**: Schema changes require coordinating all services
- **Deployment Dependencies**: Can't deploy Product Service without testing impact on Order Service
- **Conflicting Needs**: Product needs read replicas, Inventory needs row-level locking
- **Single Point of Failure**: Database down = all services down
- **Team Conflicts**: Multiple teams modifying same schema (merge conflicts, accidental breaking changes)

**Exception**: We will share SQL Server **instances** (cost optimization), but each service has its own **database** on that instance. This provides logical isolation while reducing infrastructure cost.

## Consequences

### Positive

- **Service Independence**: Deploy, scale, and evolve services without coordination
- **Resilience**: Database failure affects only one service, not entire system
- **Performance**: Optimize database for service-specific access patterns
- **Team Velocity**: Teams work independently without stepping on each other
- **Technology Diversity**: Choose best database for each use case

### Negative

- **No ACID Transactions Across Services**: Can't update Order and Inventory in same transaction
- **Data Duplication**: Order stores product name/price (not normalized)
- **Eventual Consistency**: Product price change takes time to propagate to active carts
- **Increased Complexity**: Must implement distributed data patterns (saga, event sourcing)
- **Higher Infrastructure Cost**: 6 databases instead of 1 (mitigated by shared instances)

### Trade-offs Accepted

1. **Accept data duplication** for performance and independence (order snapshots product data)
2. **Accept eventual consistency** for cross-service data (cart may show old price briefly)
3. **Accept higher cost** for increased scalability and team velocity
4. **Accept saga complexity** for distributed transactions (checkout workflow)

## Data Consistency Strategies

### 1. Snapshot Pattern (Order → Product)

**Use Case**: Order needs product data at order time

**Strategy**: Copy product data into OrderLine at checkout
```csharp
var product = await _productService.GetProductAsync(sku);
var orderLine = new OrderLine
{
    Sku = product.Sku,
    Name = product.Name,        // Snapshot
    UnitPrice = product.Price,  // Snapshot
    Quantity = quantity
};
```

**Why**: Historical orders should reflect price at purchase time, not current price.

### 2. Cached Reference Data (Cart → Product)

**Use Case**: Cart displays product name and price

**Strategy**: Cache product data in Cart Service (short TTL)
```csharp
// Cart Service
var product = await _cache.GetOrCreateAsync($"product:{sku}", async () =>
{
    var p = await _productService.GetProductAsync(sku);
    return new { p.Name, p.Price };
}, ttl: TimeSpan.FromMinutes(10));
```

**Why**: Reduce API calls to Product Service, accept stale data for brief period.

### 3. Event-Driven Synchronization (Product → Cart)

**Use Case**: Product price changes, update carts

**Strategy**: Product Service publishes `ProductPriceChanged` event, Cart Service subscribes
```csharp
// Product Service
await _eventBus.PublishAsync(new ProductPriceChanged
{
    Sku = product.Sku,
    OldPrice = oldPrice,
    NewPrice = newPrice
});

// Cart Service
public async Task Handle(ProductPriceChanged evt)
{
    var carts = await _db.Carts.Where(c => c.Lines.Any(l => l.Sku == evt.Sku)).ToListAsync();
    foreach (var cart in carts)
    {
        var line = cart.Lines.First(l => l.Sku == evt.Sku);
        line.UnitPrice = evt.NewPrice;
    }
    await _db.SaveChangesAsync();
}
```

**Why**: Keep carts reasonably up-to-date without synchronous API calls.

### 4. Saga Pattern (Checkout)

**Use Case**: Checkout spans multiple services (Cart, Inventory, Payment, Order)

**Strategy**: Orchestrate with saga pattern (compensating transactions)

**Checkout Saga Flow**:
1. Reserve inventory (Inventory Service)
2. Process payment (Payment Service)
3. Create order (Order Service)
4. Confirm inventory (Inventory Service)
5. Clear cart (Cart Service)

**Compensation (if payment fails)**:
1. Release inventory reservation (Inventory Service)
2. Return error to client

**Implementation**: Use MassTransit or NServiceBus for saga orchestration

**Why**: Achieve consistency across services without distributed transactions (2PC).

## Data Migration Strategy

### Phase 1: Duplicate Data (Dual Write)

**During migration**, services run in dual-write mode:
- Monolith writes to monolith database
- Service writes to service database
- Data sync job keeps them consistent

Example (Product Service extraction):
```
Writes:
  Admin updates product → Monolith DB ← Monolith
  Sync job → Service DB ← Product Service
  
Reads:
  Frontend → Product Service API → Service DB
```

**Duration**: 1-2 weeks per service (validation period)

### Phase 2: Switch Write Authority

After validation, switch write authority to service:
```
Writes:
  Admin updates product → Service DB ← Product Service
  Sync job → Monolith DB (reverse sync, optional)
  
Reads:
  Frontend → Product Service API → Service DB
```

**Duration**: 1 week (monitoring)

### Phase 3: Full Ownership

Service fully owns data, monolith no longer accesses it:
```
Writes & Reads:
  All operations → Service DB ← Product Service
  
Monolith:
  No longer accesses Products table
```

**Final Step**: Drop Products table from monolith database (after 30-day safety period)

## Cross-Service Queries

### Problem: Display Order with Product Details

**Old (Monolith)**:
```csharp
var order = await _db.Orders
    .Include(o => o.Lines)
    .ThenInclude(l => l.Product)  // Join across tables
    .FirstAsync(o => o.Id == orderId);
```

**New (Microservices) - Option 1: Snapshot**:
```csharp
// Order already stores product name/price snapshot in OrderLine
var order = await _db.Orders
    .Include(o => o.Lines)  // No join needed
    .FirstAsync(o => o.Id == orderId);
```
**Pros**: Simple, fast, no network calls
**Cons**: Data may be outdated (but correct for orders - reflects purchase-time data)

**New (Microservices) - Option 2: API Composition (BFF)**:
```csharp
// BFF/Frontend aggregates data from multiple services
var order = await _orderService.GetOrderAsync(orderId);
var productDetails = await Task.WhenAll(
    order.Lines.Select(line => _productService.GetProductAsync(line.Sku))
);
// Merge order + current product details
```
**Pros**: Always shows current product data
**Cons**: Slower (N+1 API calls), more complex, requires all services available

**Decision**: Use **Snapshot pattern** for historical data (orders), **API composition** for real-time data (cart).

## Schema Changes

### Within Service (Safe)

Service can change its own schema freely:
- Add/remove columns
- Rename tables
- Change indexes
- Modify constraints

**Process**:
1. Create EF Core migration
2. Test locally
3. Apply to staging
4. Deploy service with schema change
5. Apply to production (auto-migration or manual)

**No coordination needed** with other services.

### Cross-Service Impact (Requires Coordination)

If a service changes its **API contract** (not just schema), must coordinate:
- Add new API endpoint (v2) while keeping old (v1) for backward compatibility
- Notify consuming services of deprecation
- Migrate consumers to v2
- Retire v1 after grace period

**Example**: Product Service changes SKU format from string to GUID
1. Add `GetProductBySkuV2(Guid)` alongside `GetProductBySku(string)`
2. Order Service continues using v1
3. Eventually migrate Order Service to v2
4. Remove v1 after all consumers migrated

## Infrastructure

### Database Hosting

**Development**:
- LocalDB for local development (per developer)
- Azure SQL Basic tier for shared dev environment

**Staging/Production**:
- Azure SQL (Standard or Premium tier)
- **Option A**: 1 SQL Server instance per service (higher cost, maximum isolation)
- **Option B**: Shared SQL Server instance, separate databases per service (lower cost, logical isolation)

**Initial Decision**: **Option B** (shared instance) to control costs. Migrate to Option A if contention issues arise.

**Example Configuration** (Shared Instance):
```
SQL Server: retail-prod-sql.database.windows.net
Databases:
  - ProductsDB
  - InventoryDB
  - OrdersDB
  - CheckoutDB
  - PaymentsDB
  
Each service has own DB user with permissions ONLY to its database:
  - product-user → ProductsDB (no access to InventoryDB)
  - inventory-user → InventoryDB (no access to ProductsDB)
```

### Backup & Recovery

**Backup Strategy**:
- Automated daily backups (Azure SQL built-in)
- 30-day retention
- Point-in-time restore (last 7 days)

**Disaster Recovery**:
- Each database backed up independently
- Service can be restored without affecting others
- Test restore procedure quarterly

**Cross-Service Consistency**: In disaster recovery, databases may be restored to different points in time → Accept temporary inconsistency, run reconciliation jobs.

## Cost Analysis

**Monolith (Current)**:
- 1 SQL Server database → $5/month (Basic) or $120/month (Standard S2)

**Microservices (Target)**:
- 6 SQL Server databases (shared instance) → $600-$840/month (Standard S2)
- Redis for Cart Service → $20-$75/month
- **Total**: $620-$915/month (12-18x increase)

**Cost Optimization Strategies**:
- Use Azure SQL Elastic Pool (share resources across databases)
- Use lower tiers for non-critical services (Cart backup DB can be Basic)
- Use PostgreSQL on Azure (cheaper than SQL Server)
- Scale down dev/staging environments (Basic tier)

**Cost-Benefit Analysis**:
- Higher cost, but enables independent scaling (cheaper than scaling entire monolith)
- Higher cost, but reduces development time (faster feature delivery)
- Higher cost, but improves reliability (service isolation)

**Recommendation**: Accept higher database cost as investment in scalability and team velocity.

## Monitoring

**Per-Database Metrics**:
- Connection count (detect connection pool exhaustion)
- Query performance (slow query log)
- Storage usage (disk space)
- DTU/vCore utilization (Azure SQL)
- Backup status (ensure backups successful)

**Cross-Database Metrics**:
- Data consistency checks (reconciliation job results)
- Replication lag (if using read replicas)

**Alerts**:
- Connection pool exhausted → Scale up or investigate connection leaks
- Storage > 80% → Increase storage or archive old data
- Backup failed → Page on-call immediately

## Security

### Network Isolation

- Databases only accessible from within Kubernetes cluster (private network)
- No public internet access to databases
- Firewall rules restrict access to specific service IPs

### Authentication

- Each service has dedicated database user (principle of least privilege)
- Connection strings stored in Kubernetes Secrets or Azure Key Vault
- Rotate credentials quarterly

### Encryption

- Encryption in transit (TLS for all connections)
- Encryption at rest (Azure SQL Transparent Data Encryption)
- Key management via Azure Key Vault

## Related ADRs

- **ADR-002**: EF Core with SQL Server (ORM technology)
- **ADR-005**: Service Decomposition Strategy (which services need databases)
- **ADR-009**: Event-Driven Communication (data sync between services)

## Date
2026-01-14

## Participants
- Architecture Team
- Database Team
- Development Team

## Review
This ADR should be reviewed after Phase 2 (Inventory Service extraction) to validate data consistency strategies and adjust based on lessons learned.
