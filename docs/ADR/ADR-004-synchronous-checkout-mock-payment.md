# ADR-004: Synchronous Checkout with Mock Payment Gateway

## Status
Accepted (with known limitations)

## Context
The checkout flow needs to orchestrate multiple operations: validate cart, reserve inventory, process payment, create order, and clear cart. Payment processing typically involves external payment gateway APIs (Stripe, PayPal, etc.) that introduce network latency and potential failures.

## Decision
Implement checkout as a synchronous, blocking operation with all steps executed sequentially within a single database transaction. Use a mock payment gateway (`MockPaymentGateway`) that always succeeds immediately, rather than integrating a real payment provider.

## Rationale

### Why Synchronous Checkout?
1. **Simplicity**: Easier to implement and understand
2. **Consistency**: All operations succeed or fail together (single transaction)
3. **Immediate Response**: User gets order confirmation immediately
4. **No Queue Infrastructure**: Avoid complexity of message brokers (RabbitMQ, Azure Service Bus)
5. **MVP Focus**: Get core flow working before adding asynchronous complexity

### Why Mock Payment Gateway?
1. **No External Dependencies**: No API keys, sandbox accounts, or external service setup
2. **Deterministic Testing**: Always succeeds, no flaky tests due to network issues
3. **Fast Development**: No need to handle payment webhooks, retries, or provider-specific error codes
4. **Interface-Based**: `IPaymentGateway` interface allows easy swap to real provider later
5. **Demo-Friendly**: No real credit cards or payment tokens needed

## Implementation Details

### Checkout Flow (CheckoutService.cs)
```csharp
1. Get cart with lines (throws if not found)
2. Calculate total from cart lines
3. For each line: reserve inventory (decrement stock)
4. Call PaymentGateway.ChargeAsync()
5. Create Order with status based on payment result
6. Clear cart lines
7. SaveChanges() - commits everything in single transaction
```

### Mock Gateway Implementation
```csharp
public Task<PaymentResult> ChargeAsync(PaymentRequest req, CancellationToken ct = default)
{
    return Task.FromResult(new PaymentResult(
        Succeeded: true, 
        ProviderRef: $"MOCK-{Guid.NewGuid():N}", 
        Error: null
    ));
}
```

### Transaction Boundary
- Entire checkout happens within single EF Core `SaveChanges()` call
- Includes inventory updates, order creation, cart deletion
- Payment call happens **outside** transaction (no rollback on payment failure)

## Consequences

### Positive
- **Simple Code**: Linear flow, easy to read and debug
- **Fast Response**: No async delays, immediate user feedback
- **No Lost Updates**: ACID transaction prevents partial checkouts
- **Easy Testing**: Predictable mock gateway, no test flakiness
- **No Infrastructure**: No message queues, workers, or retry logic needed

### Negative
- **Blocking Request Thread**: Request thread held during entire checkout
- **No Scalability**: Cannot handle high concurrency (thread pool exhaustion)
- **Inventory Risk**: Inventory decremented even if payment fails (bug!)
- **No Retry**: Payment failures are permanent (no retry mechanism)
- **No Idempotency**: Duplicate checkout calls create duplicate orders
- **Mock-Only**: Cannot process real payments without code changes

## Critical Issues

### Issue 1: No Inventory Rollback on Payment Failure
**Location**: `CheckoutService.cs`, lines 28-36

**Problem**: Inventory is decremented before payment processing:
```csharp
// 2) reserve/decrement stock
foreach (var line in cart.Lines) {
    inv.Quantity -= line.Quantity;  // Decremented here
}
// 3) charge
var pay = await _payments.ChargeAsync(...);  // Fails?
var status = pay.Succeeded ? "Paid" : "Failed";  // Order marked "Failed"
```

**Impact**: If payment fails, inventory is still decremented but no order is paid. Stock is "lost".

**Workaround**: Mock gateway never fails, so issue is latent

**Resolution Required**: 
- Move inventory decrement **after** successful payment, OR
- Use database transaction that rolls back on payment failure, OR
- Implement compensating transaction to restore inventory

### Issue 2: Thread Blocking Under Load
**Problem**: Synchronous checkout holds request thread for duration of:
- Database queries (cart, inventory lookups)
- Payment gateway call (simulated, but could be 1-5 seconds for real gateway)
- Database writes (order creation)

**Impact**: Under high load, thread pool exhaustion leads to request timeouts

**Workaround**: Low traffic in demo/MVP scenario

**Resolution Required**: Move to asynchronous processing with job queue

### Issue 3: No Idempotency
**Problem**: Submitting same checkout twice creates two orders

**Impact**: User double-charged, duplicate orders

**Workaround**: UI doesn't allow double-submit (weak protection)

**Resolution Required**: Idempotency key pattern (e.g., `POST /checkout` with `Idempotency-Key` header)

## Alternatives Considered

### Asynchronous Checkout with Queue
**Approach**: 
- POST /checkout returns order ID immediately with status "Pending"
- Background worker processes checkout asynchronously
- User polls GET /orders/{id} for status updates

**Pros**: 
- Non-blocking, better scalability
- Can retry payment failures
- Can implement timeouts and circuit breakers

**Cons**: 
- More complex (queue infrastructure, worker processes)
- User doesn't get immediate confirmation
- Need webhook/polling for status updates

**Decision**: Rejected for MVP complexity

### Saga Pattern (Choreography)
**Approach**:
- Publish CartCheckedOut event
- InventoryService reserves stock
- PaymentService processes payment
- OrderService creates order
- Services coordinate via events

**Pros**: 
- Highly scalable
- Domain services decoupled
- Each service can scale independently

**Cons**: 
- Significant complexity (event bus, event sourcing, eventual consistency)
- Harder to debug (distributed trace required)
- Overkill for monolith

**Decision**: Rejected as premature for monolith architecture

### Two-Phase Commit (2PC)
**Approach**: Coordinate database and payment gateway with 2PC protocol

**Pros**: Strong consistency across resources

**Cons**: 
- Payment gateways don't support 2PC
- Slow, reduces availability
- Rarely used in modern systems

**Decision**: Rejected as not feasible with payment APIs

## Future Migration Path

To move to production-ready checkout:

1. **Integrate Real Payment Gateway**
   - Implement Stripe/PayPal adapter implementing `IPaymentGateway`
   - Handle API errors, timeouts, network failures
   - Store payment provider reference in Order

2. **Fix Inventory Rollback**
   - Move inventory decrement after payment success
   - Or implement compensating transaction

3. **Add Idempotency**
   - Accept idempotency key in checkout request
   - Store in Order table to detect duplicates

4. **Consider Async Processing** (if traffic grows)
   - Use Azure Service Bus or RabbitMQ
   - Implement background worker for checkout processing
   - Add order status polling endpoint

## Date
2025-10-19 (inferred from initial implementation)

## Participants
Development Team

## Related ADRs
- ADR-001: Monolithic Architecture (checkout orchestration centralized in monolith)
- ADR-002: EF Core with SQL Server (transaction boundary is EF Core SaveChanges)
