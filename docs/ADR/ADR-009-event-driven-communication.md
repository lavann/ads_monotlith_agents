# ADR-009: Event-Driven Communication Pattern

## Status
Proposed

## Context
In a microservices architecture, services need to communicate with each other. There are two primary patterns:

1. **Synchronous (Request-Response)**: Service A calls Service B's API and waits for response
2. **Asynchronous (Event-Driven)**: Service A publishes event, Service B subscribes and reacts

The current monolith (ADR-004) uses synchronous, blocking calls within a single process. As we decompose, we must decide when to use synchronous vs. asynchronous communication.

## Decision

We will use a **hybrid approach**:

### Use Synchronous Communication (HTTP REST) When:

1. **Immediate Response Required**: Frontend needs product list now, can't wait for event
2. **Simple Request-Response**: "Get product by SKU" is a straightforward query
3. **Strong Consistency Needed**: Cart validation requires current inventory level

**Examples**:
- Frontend → Product Service: `GET /api/products`
- Cart Service → Product Service: `GET /api/products/sku/{sku}` (get current price)
- Checkout Service → Inventory Service: `POST /api/inventory/reserve` (reserve stock)

### Use Asynchronous Communication (Events) When:

1. **No Immediate Response Needed**: Order created, other services react eventually
2. **Decoupling Services**: Order Service shouldn't call Cart Service directly to clear cart
3. **Fan-Out**: One event triggers multiple actions (OrderCreated → update inventory, send email, update analytics)
4. **Eventual Consistency Acceptable**: Cart price update can take 1-2 seconds

**Examples**:
- Payment Service publishes `PaymentSucceeded` → Order Service updates order status
- Order Service publishes `OrderCreated` → Cart Service clears cart
- Product Service publishes `ProductPriceChanged` → Cart Service updates cart lines

### Message Broker: RabbitMQ or Azure Service Bus

**Initial Choice**: RabbitMQ (open-source, self-hosted)

**Alternative**: Azure Service Bus (managed, fully-hosted by Azure)

**Decision**: Start with **RabbitMQ** for dev/staging (cost savings, learning), migrate to **Azure Service Bus** for production (managed, enterprise features).

### Messaging Library: MassTransit

**Why MassTransit**:
- .NET native (integrates with ASP.NET Core DI)
- Supports multiple brokers (RabbitMQ, Azure Service Bus, Amazon SQS)
- Saga orchestration built-in (needed for checkout flow)
- Automatic retry, error handling, dead letter queues
- Strong community and documentation

**Alternative considered**: NServiceBus → Rejected due to licensing cost (free tier too limited).

## Architecture

```
┌─────────────┐
│   Product   │
│   Service   │
└──────┬──────┘
       │ Publishes: ProductPriceChanged
       ▼
┌─────────────────────────────────┐
│   Message Broker (RabbitMQ)     │
│   - Exchange: product-events    │
│   - Queue: cart-service-queue   │
│   - Queue: order-service-queue  │
└─────────────────────────────────┘
       │ Subscribes
       ├────────────────┬────────────────┐
       ▼                ▼                ▼
┌──────────┐     ┌──────────┐    ┌──────────┐
│   Cart   │     │  Order   │    │ Analytics│
│ Service  │     │ Service  │    │ Service  │
└──────────┘     └──────────┘    └──────────┘
```

**Key Concepts**:
- **Publisher**: Service that emits events (Product Service publishes `ProductPriceChanged`)
- **Subscriber/Consumer**: Service that reacts to events (Cart Service subscribes to `ProductPriceChanged`)
- **Exchange**: Routes messages to queues (topic exchange for pub/sub)
- **Queue**: Holds messages for each subscriber (cart-service-queue, order-service-queue)
- **Dead Letter Queue**: Failed messages go here for manual review

## Event Schema

### Standard Event Structure

All events follow this structure:

```json
{
  "eventId": "uuid",
  "eventType": "ProductPriceChanged",
  "timestamp": "2026-01-14T16:00:00Z",
  "correlationId": "uuid",
  "source": "product-service",
  "version": "1.0",
  "data": {
    "sku": "PROD-123",
    "oldPrice": 99.99,
    "newPrice": 79.99,
    "currency": "GBP"
  }
}
```

**Fields**:
- `eventId`: Unique ID for this event (idempotency key)
- `eventType`: Type of event (used for routing and handling)
- `timestamp`: When event occurred (UTC)
- `correlationId`: Links related events (e.g., all events from single checkout)
- `source`: Which service published event
- `version`: Schema version (for evolution)
- `data`: Event-specific payload

### Event Types Defined

#### Product Domain Events

```csharp
public record ProductCreated(string Sku, string Name, decimal Price, string Currency);
public record ProductUpdated(string Sku, string Name, decimal Price, string Currency);
public record ProductPriceChanged(string Sku, decimal OldPrice, decimal NewPrice, string Currency);
public record ProductDeactivated(string Sku);
```

#### Inventory Domain Events

```csharp
public record StockReserved(string Sku, int Quantity, string ReservationId, string CustomerId);
public record StockReleased(string Sku, int Quantity, string ReservationId);
public record StockConfirmed(string Sku, int Quantity, string ReservationId);
public record LowStockAlert(string Sku, int CurrentQuantity, int Threshold);
```

#### Order Domain Events

```csharp
public record OrderCreated(int OrderId, string CustomerId, decimal Total, List<OrderLineDto> Lines);
public record OrderPaid(int OrderId, string PaymentRef);
public record OrderFailed(int OrderId, string Reason);
public record OrderShipped(int OrderId, string TrackingNumber);
```

#### Payment Domain Events

```csharp
public record PaymentSucceeded(string PaymentRef, decimal Amount, string Currency, int OrderId);
public record PaymentFailed(string PaymentRef, decimal Amount, string Reason, int OrderId);
public record RefundProcessed(string RefundRef, string PaymentRef, decimal Amount);
```

#### Cart Domain Events

```csharp
public record CartCleared(string CustomerId);
public record CartExpired(string CustomerId);
```

## Implementation Examples

### Publishing Events (Product Service)

```csharp
// Product Service - Publish event when price changes
public class ProductService
{
    private readonly AppDbContext _db;
    private readonly IPublishEndpoint _publishEndpoint;
    
    public async Task UpdatePriceAsync(string sku, decimal newPrice)
    {
        var product = await _db.Products.SingleAsync(p => p.Sku == sku);
        var oldPrice = product.Price;
        
        product.Price = newPrice;
        await _db.SaveChangesAsync();
        
        // Publish event AFTER database commit
        await _publishEndpoint.Publish(new ProductPriceChanged(
            Sku: sku,
            OldPrice: oldPrice,
            NewPrice: newPrice,
            Currency: product.Currency
        ));
    }
}
```

### Consuming Events (Cart Service)

```csharp
// Cart Service - Subscribe to ProductPriceChanged event
public class ProductPriceChangedConsumer : IConsumer<ProductPriceChanged>
{
    private readonly AppDbContext _db;
    private readonly ILogger<ProductPriceChangedConsumer> _logger;
    
    public async Task Consume(ConsumeContext<ProductPriceChanged> context)
    {
        var evt = context.Message;
        
        _logger.LogInformation("Updating cart prices for SKU {Sku}: {OldPrice} → {NewPrice}",
            evt.Sku, evt.OldPrice, evt.NewPrice);
        
        // Find all carts with this product
        var carts = await _db.Carts
            .Include(c => c.Lines)
            .Where(c => c.Lines.Any(l => l.Sku == evt.Sku))
            .ToListAsync();
        
        foreach (var cart in carts)
        {
            var line = cart.Lines.First(l => l.Sku == evt.Sku);
            line.UnitPrice = evt.NewPrice;
        }
        
        await _db.SaveChangesAsync();
        
        _logger.LogInformation("Updated {CartCount} carts with new price for SKU {Sku}",
            carts.Count, evt.Sku);
    }
}
```

### Configuring MassTransit (Startup)

```csharp
// Program.cs or Startup.cs
builder.Services.AddMassTransit(x =>
{
    // Register consumers
    x.AddConsumer<ProductPriceChangedConsumer>();
    x.AddConsumer<OrderCreatedConsumer>();
    
    // Configure RabbitMQ
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("rabbitmq://rabbitmq-service", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });
        
        // Configure endpoints
        cfg.ConfigureEndpoints(context);
        
        // Retry policy
        cfg.UseMessageRetry(r => r.Exponential(
            retryLimit: 5,
            minInterval: TimeSpan.FromSeconds(1),
            maxInterval: TimeSpan.FromMinutes(5),
            intervalDelta: TimeSpan.FromSeconds(2)
        ));
    });
});
```

## Idempotency

**Problem**: Events may be delivered multiple times (at-least-once delivery guarantee)

**Solution**: Make consumers idempotent by tracking processed event IDs

```csharp
public class ProductPriceChangedConsumer : IConsumer<ProductPriceChanged>
{
    private readonly AppDbContext _db;
    
    public async Task Consume(ConsumeContext<ProductPriceChanged> context)
    {
        var eventId = context.MessageId.ToString();
        
        // Check if already processed
        if (await _db.ProcessedEvents.AnyAsync(e => e.EventId == eventId))
        {
            _logger.LogInformation("Event {EventId} already processed, skipping", eventId);
            return;
        }
        
        // Process event
        // ... update cart prices ...
        
        // Mark as processed
        _db.ProcessedEvents.Add(new ProcessedEvent 
        { 
            EventId = eventId, 
            ProcessedAt = DateTime.UtcNow 
        });
        await _db.SaveChangesAsync();
    }
}
```

**ProcessedEvents Table**:
```sql
CREATE TABLE ProcessedEvents (
    EventId NVARCHAR(50) PRIMARY KEY,
    ProcessedAt DATETIME2 NOT NULL
);

-- Cleanup old entries (> 7 days) periodically
DELETE FROM ProcessedEvents WHERE ProcessedAt < DATEADD(day, -7, GETUTCDATE());
```

## Saga Pattern for Checkout

**Use Case**: Checkout spans multiple services, requires coordination

**Pattern**: Choreography (event-driven) or Orchestration (central coordinator)

**Decision**: Use **Orchestration** with MassTransit State Machine for checkout

### Checkout Saga State Machine

```csharp
public class CheckoutSaga : MassTransitStateMachine<CheckoutState>
{
    public CheckoutSaga()
    {
        InstanceState(x => x.CurrentState);
        
        Initially(
            When(CheckoutInitiated)
                .Then(context => { /* Reserve inventory */ })
                .TransitionTo(InventoryReserved)
        );
        
        During(InventoryReserved,
            When(StockReserved)
                .Then(context => { /* Process payment */ })
                .TransitionTo(PaymentProcessing),
            When(StockReservationFailed)
                .Then(context => { /* Return error */ })
                .TransitionTo(Failed)
        );
        
        During(PaymentProcessing,
            When(PaymentSucceeded)
                .Then(context => { /* Create order */ })
                .TransitionTo(OrderCreating),
            When(PaymentFailed)
                .Then(context => { /* Release inventory */ })
                .TransitionTo(Failed)
        );
        
        During(OrderCreating,
            When(OrderCreated)
                .Then(context => { /* Confirm inventory, clear cart */ })
                .TransitionTo(Completed)
        );
    }
    
    public State InventoryReserved { get; private set; }
    public State PaymentProcessing { get; private set; }
    public State OrderCreating { get; private set; }
    public State Completed { get; private set; }
    public State Failed { get; private set; }
    
    public Event<CheckoutInitiated> CheckoutInitiated { get; private set; }
    public Event<StockReserved> StockReserved { get; private set; }
    public Event<StockReservationFailed> StockReservationFailed { get; private set; }
    public Event<PaymentSucceeded> PaymentSucceeded { get; private set; }
    public Event<PaymentFailed> PaymentFailed { get; private set; }
    public Event<OrderCreated> OrderCreated { get; private set; }
}

public class CheckoutState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; }
    public string CustomerId { get; set; }
    public string ReservationId { get; set; }
    public string PaymentRef { get; set; }
    public int OrderId { get; set; }
}
```

**State Persistence**: Store saga state in CheckoutDB (SQL Server)

**Timeout Handling**: Each state has 30-second timeout, saga fails if step doesn't complete

## Rationale

### Why Event-Driven for Some Flows?

**Benefits**:
1. **Loose Coupling**: Order Service doesn't need to know Cart Service exists
2. **Scalability**: Cart Service can be down temporarily, events queued and processed later
3. **Extensibility**: Add new consumer (Analytics Service) without changing publisher
4. **Resilience**: Automatic retry, dead letter queue for failures
5. **Audit Trail**: All events logged, can replay for debugging

**Example**: When order is created, multiple things happen:
- Order Service publishes `OrderCreated` event
- Cart Service subscribes → clears cart
- Email Service subscribes → sends confirmation email
- Analytics Service subscribes → updates sales dashboard
- Inventory Service subscribes → marks reservation as confirmed

All these happen **independently**, Order Service doesn't need to call 4 different services synchronously.

### Why NOT Event-Driven for Everything?

**Drawbacks**:
1. **Complexity**: Harder to debug than synchronous calls
2. **Latency**: Events take time to propagate (seconds, not milliseconds)
3. **Eventual Consistency**: Data temporarily out of sync
4. **Operational Overhead**: Message broker adds another component to manage

**When Synchronous is Better**:
- User waiting for response (e.g., product list)
- Strong consistency required (e.g., inventory reservation)
- Simple request-response (e.g., get product by SKU)

### Why RabbitMQ?

**Pros**:
- Open-source (no licensing cost)
- Mature (15+ years in production)
- High throughput (10,000+ messages/sec)
- Flexible routing (topic exchange, direct exchange)
- Management UI (monitor queues, messages)
- Strong community

**Cons**:
- Self-hosted (we manage infrastructure)
- Manual scaling (vs. Azure Service Bus auto-scale)
- No built-in geo-replication

**Decision**: Start with RabbitMQ (cost, learning), migrate to Azure Service Bus if operational burden too high.

### Why MassTransit?

**Pros**:
- .NET native (familiar to team)
- Supports multiple brokers (easy to switch RabbitMQ → Azure Service Bus)
- Saga orchestration built-in
- Automatic retries, dead letter queues
- Request-response over messaging (if needed)

**Cons**:
- Learning curve (saga state machines)
- Opinionated (conventions must be followed)

**Alternative considered**: Raw RabbitMQ client → Rejected due to lack of saga support and more boilerplate code.

## Consequences

### Positive

- **Decoupled Services**: Services don't know about each other, only events
- **Scalable**: Consumers process events at their own pace
- **Extensible**: Add new consumers without changing publishers
- **Resilient**: Automatic retry, dead letter queue
- **Audit Trail**: All domain events logged

### Negative

- **Complexity**: Distributed system harder to debug than monolith
- **Eventual Consistency**: Data temporarily out of sync
- **Message Broker**: Another component to operate and monitor
- **Learning Curve**: Team must learn async patterns, saga orchestration
- **Testing**: Harder to test event flows than synchronous calls

### Operational Impact

**New Infrastructure**:
- RabbitMQ cluster (3 nodes for HA)
- Monitoring: Queue depth, message rate, consumer lag
- Alerting: Dead letter queue not empty, consumer down

**New Skills**:
- RabbitMQ administration
- MassTransit saga patterns
- Event-driven debugging (distributed tracing)

## Message Broker Deployment

### RabbitMQ in Kubernetes

```yaml
apiVersion: apps/v1
kind: StatefulSet
metadata:
  name: rabbitmq
spec:
  serviceName: rabbitmq
  replicas: 3
  selector:
    matchLabels:
      app: rabbitmq
  template:
    metadata:
      labels:
        app: rabbitmq
    spec:
      containers:
      - name: rabbitmq
        image: rabbitmq:3.12-management-alpine
        ports:
        - containerPort: 5672  # AMQP
        - containerPort: 15672 # Management UI
        env:
        - name: RABBITMQ_DEFAULT_USER
          value: "admin"
        - name: RABBITMQ_DEFAULT_PASS
          valueFrom:
            secretKeyRef:
              name: rabbitmq-secret
              key: password
        volumeMounts:
        - name: rabbitmq-data
          mountPath: /var/lib/rabbitmq
  volumeClaimTemplates:
  - metadata:
      name: rabbitmq-data
    spec:
      accessModes: ["ReadWriteOnce"]
      resources:
        requests:
          storage: 10Gi
```

**Access**:
- AMQP: `rabbitmq-service:5672` (internal)
- Management UI: `http://rabbitmq-service:15672` (port-forward for dev)

### High Availability

- **Cluster**: 3 RabbitMQ nodes in cluster
- **Mirrored Queues**: Queues replicated across nodes
- **Load Balancing**: Services connect to any node, cluster handles routing
- **Failover**: If 1 node down, cluster continues operating

## Monitoring & Alerts

### RabbitMQ Metrics

- **Queue Depth**: Number of unprocessed messages (should be near 0)
- **Message Rate**: Messages published/consumed per second
- **Consumer Lag**: Time between publish and consume
- **Connection Count**: Number of active connections
- **Node Health**: CPU, memory, disk of each node

### Dead Letter Queue

**Purpose**: Failed messages (after all retries) go to DLQ for manual review

**Alerts**:
- DLQ not empty → Investigate and fix consumer
- DLQ > 100 messages → Page on-call

**Manual Review**:
- View messages in RabbitMQ Management UI
- Determine failure cause (bug, invalid data)
- Fix issue
- Republish messages from DLQ

### Service-Level Metrics

- **Event Processing Time**: Time to process event (p50, p95)
- **Event Failure Rate**: % of events that failed after all retries
- **Event Throughput**: Events processed per second

## Testing

### Unit Tests

- Test consumer logic in isolation (mock MassTransit context)
- Test event serialization/deserialization
- Test idempotency (process same event twice)

### Integration Tests

- Use MassTransit's InMemoryTestHarness
- Publish event, assert consumer was called
- Test saga state transitions

```csharp
[Fact]
public async Task ProductPriceChanged_UpdatesCartPrices()
{
    var harness = new InMemoryTestHarness();
    var consumerHarness = harness.Consumer<ProductPriceChangedConsumer>();
    
    await harness.Start();
    
    await harness.InputQueueSendEndpoint.Send(new ProductPriceChanged(
        Sku: "PROD-123",
        OldPrice: 99.99m,
        NewPrice: 79.99m,
        Currency: "GBP"
    ));
    
    Assert.True(await consumerHarness.Consumed.Any<ProductPriceChanged>());
    
    await harness.Stop();
}
```

### End-to-End Tests

- Full checkout flow with real message broker (dev environment)
- Verify events published and consumed
- Verify saga completes successfully

## Migration Strategy

### Phase 0-1: No Events Yet

- Services use synchronous HTTP calls only
- No message broker deployed

### Phase 2: First Event (Inventory Service)

- Deploy RabbitMQ to Kubernetes
- Inventory Service publishes `StockReserved`, `StockReleased`
- No consumers yet (events logged but not processed)
- Validate event publishing works

### Phase 3: First Consumer (Order Service)

- Order Service subscribes to `PaymentSucceeded`, `PaymentFailed`
- Test end-to-end event flow
- Validate idempotency, retries, dead letter queue

### Phase 4-5: Full Event-Driven

- All services publish and consume events
- Checkout saga orchestration implemented
- Monitor event flow with distributed tracing

## Related ADRs

- **ADR-005**: Service Decomposition Strategy (which services communicate)
- **ADR-007**: API Gateway Pattern (synchronous communication)
- **ADR-008**: Database per Service (why events needed for data sync)

## Date
2026-01-14

## Participants
- Architecture Team
- Development Team

## Review
This ADR should be reviewed after Phase 3 (Order Service extraction) to validate event-driven patterns and adjust based on lessons learned.
