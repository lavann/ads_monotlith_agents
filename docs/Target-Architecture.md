# Target Architecture - Retail Microservices

## Document Information
- **Last Updated**: 2026-01-14
- **Application**: RetailMonolith → Retail Microservices
- **Version**: 2.0 (Target)
- **Status**: Proposed

---

## 1. Executive Summary

This document defines the target architecture for decomposing the current monolithic retail application into a microservices-based system. The architecture prioritizes:

- **Incremental Migration**: Strangler pattern to gradually extract services
- **Operational Simplicity**: Container-based deployment with proven patterns
- **Behavior Preservation**: No functionality changes during migration
- **Scalability**: Independent scaling of high-demand services
- **Maintainability**: Clear service boundaries with well-defined interfaces

---

## 2. Target Service Boundaries

Based on the domain analysis from HLD.md and LLD.md, we propose the following service decomposition:

### 2.1 Product Catalog Service
**Responsibility**: Product information and catalog management

**Bounded Context**:
- Product entity (SKU, name, description, price, category)
- Product activation/deactivation
- Product search and filtering
- Category management

**API Surface**:
- `GET /api/products` - List active products
- `GET /api/products/{id}` - Get product details
- `GET /api/products/sku/{sku}` - Get product by SKU
- `POST /api/products` - Create product (admin)
- `PUT /api/products/{id}` - Update product (admin)
- `DELETE /api/products/{id}` - Deactivate product (admin)

**Data Ownership**:
- Products table
- Read-only access for other services via API

**Technology Stack**:
- ASP.NET Core 8 Web API
- Entity Framework Core
- SQL Server (or PostgreSQL)
- Redis caching for catalog data

**Scaling Characteristics**:
- Read-heavy workload
- High cache hit ratio potential
- Horizontal scaling with load balancer

---

### 2.2 Inventory Service
**Responsibility**: Stock level tracking and reservation

**Bounded Context**:
- Inventory entity (SKU, quantity)
- Stock reservation and release
- Stock level queries
- Inventory replenishment

**API Surface**:
- `GET /api/inventory/sku/{sku}` - Get stock level
- `POST /api/inventory/reserve` - Reserve stock (idempotent)
- `POST /api/inventory/release` - Release reservation
- `POST /api/inventory/confirm` - Confirm reservation (after payment)
- `PUT /api/inventory/restock` - Update stock levels (admin)

**Event Publishing**:
- `StockReserved` - Published when stock is reserved
- `StockReleased` - Published when reservation is released
- `LowStockAlert` - Published when stock falls below threshold

**Data Ownership**:
- Inventory table
- Reservation tracking table (new)

**Technology Stack**:
- ASP.NET Core 8 Web API
- Entity Framework Core
- SQL Server with row-level locking
- RabbitMQ/Azure Service Bus for events

**Scaling Characteristics**:
- Write-heavy during checkout
- Requires strong consistency
- Vertical scaling with optimized database

**Concurrency Handling**:
- Optimistic concurrency control with row versioning
- Idempotency keys for reservation operations
- Timeout for unreleased reservations (15 minutes)

---

### 2.3 Order Service
**Responsibility**: Order lifecycle and history

**Bounded Context**:
- Order entity (customer, status, total, timestamp)
- OrderLine entity (SKU, name, price, quantity - snapshot)
- Order status transitions
- Order history queries

**API Surface**:
- `GET /api/orders` - List customer orders
- `GET /api/orders/{id}` - Get order details
- `POST /api/orders` - Create order (internal)
- `PUT /api/orders/{id}/status` - Update order status (internal)

**Event Subscription**:
- `PaymentSucceeded` → Update order status to "Paid"
- `PaymentFailed` → Update order status to "Failed"
- `OrderShipped` → Update order status to "Shipped"

**Event Publishing**:
- `OrderCreated` - Published when order is created
- `OrderPaid` - Published when payment succeeds
- `OrderFailed` - Published when payment fails

**Data Ownership**:
- Orders table
- OrderLines table

**Technology Stack**:
- ASP.NET Core 8 Web API
- Entity Framework Core
- SQL Server (or PostgreSQL)
- RabbitMQ/Azure Service Bus for events

**Scaling Characteristics**:
- Read-heavy for order history
- Append-only writes
- Horizontal scaling with database read replicas

---

### 2.4 Cart Service
**Responsibility**: Shopping cart session management

**Bounded Context**:
- Cart entity (customer ID, expiry)
- CartLine entity (SKU, name, price, quantity)
- Cart operations (add, update, remove, clear)
- Cart expiration

**API Surface**:
- `GET /api/carts/{customerId}` - Get customer cart
- `POST /api/carts/{customerId}/lines` - Add item to cart
- `PUT /api/carts/{customerId}/lines/{sku}` - Update quantity
- `DELETE /api/carts/{customerId}/lines/{sku}` - Remove item
- `DELETE /api/carts/{customerId}` - Clear cart

**Event Subscription**:
- `OrderCreated` → Clear cart for customer

**Data Ownership**:
- Carts table
- CartLines table

**Technology Stack**:
- ASP.NET Core 8 Web API
- Redis as primary datastore (with SQL Server backup)
- Short TTL (24 hours) for cart expiration

**Scaling Characteristics**:
- Read/write balanced
- Session-based data
- Horizontal scaling with Redis cluster

---

### 2.5 Checkout Orchestration Service (BFF)
**Responsibility**: Coordinate checkout workflow

**Bounded Context**:
- Checkout orchestration (saga pattern)
- Payment integration
- Workflow state management
- Compensation logic

**API Surface**:
- `POST /api/checkout` - Initiate checkout (returns workflow ID)
- `GET /api/checkout/{workflowId}` - Get checkout status

**Workflow Steps**:
1. Validate cart (call Cart Service)
2. Reserve inventory (call Inventory Service)
3. Process payment (call Payment Gateway)
4. Create order (call Order Service)
5. Clear cart (call Cart Service)
6. Confirm inventory reservation (call Inventory Service)

**Compensation Logic**:
- If payment fails → Release inventory reservation
- If order creation fails → Refund payment + release inventory
- Timeout handling for each step (30 seconds max)

**Technology Stack**:
- ASP.NET Core 8 Web API
- MassTransit or NServiceBus for saga orchestration
- State stored in SQL Server or Redis
- Azure Service Bus for async messaging

**Scaling Characteristics**:
- Stateless service (state in datastore)
- Horizontal scaling behind load balancer

---

### 2.6 Payment Gateway Service
**Responsibility**: Payment processing abstraction

**Bounded Context**:
- Payment request handling
- Provider integration (Stripe, PayPal)
- Transaction tracking
- Webhook handling

**API Surface**:
- `POST /api/payments/charge` - Process payment
- `POST /api/payments/refund` - Refund payment
- `GET /api/payments/{transactionId}` - Get transaction status
- `POST /api/payments/webhooks/{provider}` - Handle provider webhooks

**Event Publishing**:
- `PaymentSucceeded` - Published on successful payment
- `PaymentFailed` - Published on payment failure
- `RefundProcessed` - Published on refund

**Data Ownership**:
- Transactions table (transaction ID, amount, status, provider reference)

**Technology Stack**:
- ASP.NET Core 8 Web API
- Stripe/PayPal SDK
- SQL Server for transaction log
- Polly for retry/circuit breaker

**Scaling Characteristics**:
- Low volume (relative to browsing)
- Requires reliability over throughput
- Vertical scaling with circuit breakers

---

### 2.7 Frontend/BFF (Backend for Frontend)
**Responsibility**: Serve UI and aggregate backend calls

**Bounded Context**:
- Razor Pages UI
- API aggregation for page rendering
- Session management
- Authentication/Authorization

**Pages**:
- `/` - Home page
- `/products` - Product catalog (calls Product Service)
- `/cart` - Shopping cart (calls Cart Service)
- `/checkout` - Checkout page (calls Checkout Service)
- `/orders` - Order history (calls Order Service)

**Technology Stack**:
- ASP.NET Core 8 Razor Pages
- HTTP Client with Polly for resilience
- ASP.NET Core Identity for auth (future)
- Output caching for product pages

**Scaling Characteristics**:
- Stateless (session in Redis)
- Horizontal scaling behind load balancer

---

## 3. Deployment Model

### 3.1 Container Strategy

**Container Technology**: Docker + Kubernetes (or Azure Container Apps)

**Container per Service**:
```
retail/product-service:latest
retail/inventory-service:latest
retail/order-service:latest
retail/cart-service:latest
retail/checkout-service:latest
retail/payment-service:latest
retail/frontend:latest
```

**Base Images**:
- .NET services: `mcr.microsoft.com/dotnet/aspnet:8.0-alpine`
- Build: `mcr.microsoft.com/dotnet/sdk:8.0`

**Container Configuration**:
- Multi-stage Dockerfile for optimal size
- Non-root user for security
- Health check endpoints (`/health`)
- Graceful shutdown handling

---

### 3.2 Orchestration: Kubernetes

**Cluster Setup**:
- **Development**: Local Kubernetes (Docker Desktop, Minikube)
- **Staging/Production**: Azure Kubernetes Service (AKS) or managed K8s

**Deployment Resources**:
- **Deployment**: One per service (3 replicas for HA)
- **Service**: ClusterIP for internal, LoadBalancer for ingress
- **ConfigMap**: Environment-specific configuration
- **Secret**: Database credentials, API keys
- **HorizontalPodAutoscaler**: CPU-based scaling (70% threshold)

**Namespace Strategy**:
- `retail-dev` - Development environment
- `retail-staging` - Staging environment
- `retail-prod` - Production environment

**Example Deployment (Product Service)**:
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: product-service
  namespace: retail-prod
spec:
  replicas: 3
  selector:
    matchLabels:
      app: product-service
  template:
    metadata:
      labels:
        app: product-service
    spec:
      containers:
      - name: product-service
        image: retail/product-service:1.2.3
        ports:
        - containerPort: 8080
        env:
        - name: ConnectionStrings__DefaultConnection
          valueFrom:
            secretKeyRef:
              name: product-db-secret
              key: connection-string
        - name: Redis__ConnectionString
          valueFrom:
            secretKeyRef:
              name: redis-secret
              key: connection-string
        livenessProbe:
          httpGet:
            path: /health
            port: 8080
          initialDelaySeconds: 10
          periodSeconds: 10
        readinessProbe:
          httpGet:
            path: /health
            port: 8080
          initialDelaySeconds: 5
          periodSeconds: 5
        resources:
          requests:
            memory: "256Mi"
            cpu: "100m"
          limits:
            memory: "512Mi"
            cpu: "500m"
---
apiVersion: v1
kind: Service
metadata:
  name: product-service
  namespace: retail-prod
spec:
  selector:
    app: product-service
  ports:
  - protocol: TCP
    port: 80
    targetPort: 8080
```

---

### 3.3 API Gateway

**Technology**: YARP (Yet Another Reverse Proxy) or Kong

**Responsibilities**:
- Request routing to backend services
- Authentication/Authorization enforcement
- Rate limiting per client
- Request/response logging
- Circuit breaking
- SSL termination

**Routing Configuration** (YARP example):
```json
{
  "ReverseProxy": {
    "Routes": {
      "products-route": {
        "ClusterId": "product-service",
        "Match": {
          "Path": "/api/products/{**catch-all}"
        }
      },
      "inventory-route": {
        "ClusterId": "inventory-service",
        "Match": {
          "Path": "/api/inventory/{**catch-all}"
        }
      },
      "orders-route": {
        "ClusterId": "order-service",
        "Match": {
          "Path": "/api/orders/{**catch-all}"
        }
      },
      "cart-route": {
        "ClusterId": "cart-service",
        "Match": {
          "Path": "/api/carts/{**catch-all}"
        }
      },
      "checkout-route": {
        "ClusterId": "checkout-service",
        "Match": {
          "Path": "/api/checkout/{**catch-all}"
        }
      }
    },
    "Clusters": {
      "product-service": {
        "Destinations": {
          "destination1": {
            "Address": "http://product-service:80"
          }
        }
      },
      "inventory-service": {
        "Destinations": {
          "destination1": {
            "Address": "http://inventory-service:80"
          }
        }
      },
      "order-service": {
        "Destinations": {
          "destination1": {
            "Address": "http://order-service:80"
          }
        }
      },
      "cart-service": {
        "Destinations": {
          "destination1": {
            "Address": "http://cart-service:80"
          }
        }
      },
      "checkout-service": {
        "Destinations": {
          "destination1": {
            "Address": "http://checkout-service:80"
          }
        }
      }
    }
  }
}
```

**External Access**:
- Ingress controller (NGINX or Azure Application Gateway)
- Public DNS: `api.retail.example.com`
- HTTPS with Let's Encrypt certificates
- WAF for security

---

### 3.4 Service Mesh (Optional for Production)

**Technology**: Istio or Linkerd

**Benefits**:
- Mutual TLS between services
- Distributed tracing (Jaeger)
- Traffic management (canary, blue/green)
- Automatic retry and circuit breaking
- Observability without code changes

**Initial Decision**: Defer service mesh until after initial microservices deployment to reduce complexity.

---

## 4. Communication Patterns

### 4.1 Synchronous Communication (HTTP/REST)

**Use Cases**:
- Client-to-service calls (Frontend → Services)
- Request-response queries
- Read operations (GET /api/products)
- Immediate consistency required

**Implementation**:
- RESTful APIs with JSON payloads
- HTTP/1.1 (upgrade to HTTP/2 later)
- Standard HTTP status codes
- Correlation ID header for tracing (`X-Correlation-ID`)

**Client Libraries**:
- `HttpClient` with `IHttpClientFactory`
- Polly for retry, timeout, circuit breaker
- Service discovery via Kubernetes DNS

**Example (Frontend calling Product Service)**:
```csharp
// In Frontend/BFF
var response = await _httpClient.GetAsync("http://product-service/api/products");
response.EnsureSuccessStatusCode();
var products = await response.Content.ReadFromJsonAsync<List<Product>>();
```

**Retry Policy**:
- 3 retries with exponential backoff
- Only for idempotent operations (GET, PUT, DELETE)
- Circuit breaker after 5 consecutive failures

---

### 4.2 Asynchronous Communication (Events)

**Use Cases**:
- Cross-service notifications
- Eventual consistency
- Long-running workflows
- Decoupling services

**Message Broker**: RabbitMQ or Azure Service Bus

**Event Schema**:
```json
{
  "eventId": "uuid",
  "eventType": "OrderCreated",
  "timestamp": "2026-01-14T16:00:00Z",
  "correlationId": "uuid",
  "data": {
    "orderId": 123,
    "customerId": "cust-456",
    "total": 99.99
  }
}
```

**Event Publishing** (using MassTransit):
```csharp
// In Order Service
await _publishEndpoint.Publish(new OrderCreated
{
    OrderId = order.Id,
    CustomerId = order.CustomerId,
    Total = order.Total
});
```

**Event Subscription**:
```csharp
// In Cart Service
public class OrderCreatedConsumer : IConsumer<OrderCreated>
{
    public async Task Consume(ConsumeContext<OrderCreated> context)
    {
        await _cartService.ClearCartAsync(context.Message.CustomerId);
    }
}
```

**Event Types**:
- `ProductCreated`, `ProductUpdated`, `ProductDeactivated`
- `StockReserved`, `StockReleased`, `LowStockAlert`
- `OrderCreated`, `OrderPaid`, `OrderFailed`, `OrderShipped`
- `PaymentSucceeded`, `PaymentFailed`, `RefundProcessed`
- `CartCleared`

**Message Reliability**:
- At-least-once delivery
- Idempotent consumers (track processed event IDs)
- Dead letter queue for failed messages
- Retry with exponential backoff

---

### 4.3 Saga Pattern for Checkout

**Pattern**: Choreography-based saga (event-driven)

**Workflow**:
1. **Checkout Service** receives `POST /api/checkout`
2. Publishes `CheckoutInitiated` event
3. **Inventory Service** reserves stock → Publishes `StockReserved`
4. **Payment Service** processes payment → Publishes `PaymentSucceeded` or `PaymentFailed`
5. **Order Service** creates order → Publishes `OrderCreated`
6. **Cart Service** clears cart → Publishes `CartCleared`
7. **Inventory Service** confirms reservation → Publishes `StockConfirmed`
8. **Checkout Service** returns result to client

**Compensation (if payment fails)**:
1. **Payment Service** publishes `PaymentFailed`
2. **Inventory Service** releases reservation → Publishes `StockReleased`
3. **Checkout Service** returns error to client

**State Management**:
- Checkout Service maintains saga state (WorkflowId, CurrentStep, Status)
- Timeout per step (30 seconds)
- Overall timeout (2 minutes)

**Alternative**: Orchestration-based saga with NServiceBus if complexity grows

---

## 5. Data Access Approach

### 5.1 Database per Service Pattern

**Principle**: Each service owns its data exclusively

**Benefits**:
- Independent schema evolution
- Service-specific database optimization
- Loose coupling between services
- Prevents accidental data dependencies

**Database Assignments**:
- **Product Service** → `ProductsDB` (SQL Server)
- **Inventory Service** → `InventoryDB` (SQL Server)
- **Order Service** → `OrdersDB` (SQL Server or PostgreSQL)
- **Cart Service** → Redis (with SQL backup)
- **Checkout Service** → `CheckoutDB` (workflow state)
- **Payment Service** → `PaymentsDB` (transaction log)

**Schema Isolation**:
- No foreign keys across services
- No shared database views or stored procedures
- All cross-service data access via APIs or events

---

### 5.2 Data Consistency Strategy

**Eventual Consistency**:
- Services synchronize via events
- Accept temporary inconsistencies (seconds to minutes)
- Example: Product price change takes time to reflect in active carts

**Strong Consistency (where required)**:
- Within service boundary (ACID transactions)
- Checkout workflow (saga with compensation)
- Inventory reservation (row-level locking)

**Data Replication**:
- Order Service stores snapshot of product data (SKU, name, price) at order time
- Cart Service stores snapshot of product data at add-to-cart time
- No joins across service databases

---

### 5.3 Data Migration from Monolith

**Phase 1: Duplicate Data**
- Keep monolith database intact
- New service creates own database
- Synchronize data from monolith to service (one-way)
- Service reads from own DB, writes to both (dual-write)

**Phase 2: Switch Reads**
- Frontend reads from new service API
- Monolith still owns writes
- Validate data consistency

**Phase 3: Switch Writes**
- New service becomes write authority
- Monolith becomes read-only for that domain
- Stop dual-write

**Phase 4: Remove Monolith Tables**
- Monolith no longer accesses domain tables
- Drop tables from monolith database
- Service fully autonomous

---

### 5.4 Caching Strategy

**Product Catalog**:
- Redis cache with 1-hour TTL
- Cache-aside pattern
- Invalidate on product update events

**Inventory**:
- No caching (strong consistency required)
- Short-lived client-side cache (5 seconds) for display only

**Orders**:
- Redis cache for recent orders (24-hour TTL)
- Cache customer's last 10 orders

**Cart**:
- Redis as primary datastore (24-hour TTL)
- No secondary cache needed

---

## 6. Configuration Management

### 6.1 Configuration Sources

**Kubernetes ConfigMaps**:
- Non-sensitive configuration
- Service endpoints
- Feature flags
- Logging levels

**Kubernetes Secrets**:
- Database connection strings
- API keys (payment gateway)
- Redis passwords
- Signing keys

**Azure Key Vault** (Production):
- SSL certificates
- Encryption keys
- Service principal credentials

**Example (Product Service appsettings)**:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "${PRODUCT_DB_CONNECTION}"
  },
  "Redis": {
    "ConnectionString": "${REDIS_CONNECTION}",
    "InstanceName": "Products:"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "ApiGateway": {
    "BaseUrl": "http://api-gateway"
  }
}
```

**Environment Variables**:
- Injected by Kubernetes from ConfigMaps/Secrets
- Override appsettings values
- Per-environment customization

---

### 6.2 Service Discovery

**Kubernetes DNS**:
- Services accessible via DNS name: `http://product-service.retail-prod.svc.cluster.local`
- Simplified to: `http://product-service` within same namespace
- Load balancing by Kubernetes Service

**No External Service Registry**:
- Kubernetes DNS sufficient for initial deployment
- Consul/Eureka deferred until multi-cluster needs

---

## 7. Observability

### 7.1 Logging

**Strategy**: Structured logging with correlation IDs

**Technology**: Serilog + Seq or ELK Stack

**Log Levels**:
- **Debug**: Detailed diagnostic info
- **Information**: General flow (request start/end)
- **Warning**: Unexpected but handled (e.g., cache miss)
- **Error**: Exceptions and failures
- **Critical**: Service down or data corruption

**Correlation IDs**:
- Generated at API Gateway
- Passed as `X-Correlation-ID` header
- Logged in every service for request tracing

**Example Log Entry**:
```json
{
  "timestamp": "2026-01-14T16:30:00Z",
  "level": "Information",
  "message": "Product retrieved",
  "correlationId": "abc-123",
  "service": "product-service",
  "productId": 42,
  "duration": 23
}
```

---

### 7.2 Metrics

**Technology**: Prometheus + Grafana

**Metrics per Service**:
- Request count (by endpoint, status code)
- Request duration (p50, p95, p99)
- Error rate
- Active connections
- Database query duration
- Cache hit ratio

**ASP.NET Core Metrics**:
- Built-in metrics via `dotnet-monitor`
- Exposed at `/metrics` endpoint (Prometheus format)

**Dashboards**:
- Service health overview
- Request throughput per service
- Error rates and alerts
- Resource utilization (CPU, memory)

---

### 7.3 Distributed Tracing

**Technology**: OpenTelemetry + Jaeger or Azure Application Insights

**Trace Propagation**:
- W3C Trace Context standard
- Automatic HTTP header propagation
- Span created per service call

**Trace Visualization**:
- See full request flow across services
- Identify slow services
- Debug failures in distributed workflows

**Example Trace** (Checkout flow):
```
POST /api/checkout [200ms]
  ├─ GET http://cart-service/api/carts/guest [30ms]
  ├─ POST http://inventory-service/api/inventory/reserve [50ms]
  ├─ POST http://payment-service/api/payments/charge [80ms]
  ├─ POST http://order-service/api/orders [30ms]
  └─ DELETE http://cart-service/api/carts/guest [10ms]
```

---

### 7.4 Health Checks

**Endpoint**: `/health` per service

**Checks**:
- Database connectivity
- Redis connectivity (if applicable)
- Message broker connectivity
- Downstream service availability (optional)

**Status Codes**:
- `200 OK` - Healthy
- `503 Service Unavailable` - Unhealthy

**Kubernetes Integration**:
- Liveness probe → Restart pod if unhealthy
- Readiness probe → Remove from load balancer if unhealthy

**Example Health Check**:
```csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>()
    .AddRedis(builder.Configuration["Redis:ConnectionString"]);

app.MapHealthChecks("/health");
```

---

## 8. Security Considerations

### 8.1 Authentication & Authorization

**User Authentication**:
- ASP.NET Core Identity (future enhancement)
- Or external provider: Auth0, Azure AD B2C
- JWT tokens for API access
- Replace hardcoded "guest" customer ID

**Service-to-Service Authentication**:
- Mutual TLS (mTLS) via service mesh
- Or API keys for internal services
- No public exposure of internal APIs

**Authorization**:
- Role-based access control (RBAC)
- Customer can only access own cart/orders
- Admin endpoints require elevated privileges

---

### 8.2 Data Protection

**Encryption in Transit**:
- HTTPS/TLS for all external communication
- TLS 1.2+ with strong cipher suites
- Certificate management via cert-manager (Kubernetes)

**Encryption at Rest**:
- Database encryption (Transparent Data Encryption)
- Redis encryption (TLS connection)
- Secrets encrypted in etcd (Kubernetes)

**Secrets Management**:
- Kubernetes Secrets (base64 encoded)
- Azure Key Vault for production secrets
- Never commit secrets to Git

---

### 8.3 Network Security

**Network Policies** (Kubernetes):
- Restrict inter-pod communication
- Only allow necessary service-to-service calls
- Block external access to internal services

**API Gateway Security**:
- Rate limiting per client (100 req/min)
- IP whitelisting for admin endpoints
- DDoS protection (Azure Front Door or Cloudflare)

**Firewall Rules**:
- Database only accessible from service pods
- Redis only accessible from service pods
- No public internet access to datastores

---

## 9. CI/CD Pipeline

### 9.1 Build Pipeline

**Trigger**: Git push to `main` or `develop` branch

**Steps**:
1. Checkout code
2. Restore dependencies (`dotnet restore`)
3. Build solution (`dotnet build`)
4. Run unit tests (`dotnet test`)
5. Run code analysis (SonarQube)
6. Build Docker images (multi-stage)
7. Push images to container registry (Azure ACR or Docker Hub)
8. Tag images with commit SHA

**Tools**: GitHub Actions or Azure DevOps

---

### 9.2 Deployment Pipeline

**Trigger**: Manual approval or automatic (for dev/staging)

**Steps**:
1. Pull latest images from registry
2. Update Kubernetes manifests with new image tag
3. Apply manifests (`kubectl apply`)
4. Wait for rollout completion
5. Run smoke tests (health checks)
6. Notify team (Slack/Teams)

**Deployment Strategies**:
- **Rolling Update**: Default, zero-downtime (maxUnavailable: 1, maxSurge: 1)
- **Blue-Green**: Swap traffic after full deployment
- **Canary**: Gradual rollout (10% → 50% → 100%)

---

### 9.3 Testing Strategy

**Unit Tests**:
- Per service
- Mock external dependencies
- Run in build pipeline

**Integration Tests**:
- Test service with real database (TestContainers)
- Test API contracts
- Run in build pipeline

**Contract Tests**:
- Pact for consumer-driven contracts
- Ensure API compatibility

**End-to-End Tests**:
- Full checkout flow
- Run in staging environment
- Automated nightly tests

**Performance Tests**:
- JMeter or k6 load tests
- Run before production deployment
- Baseline: 1000 req/min, p95 < 500ms

---

## 10. Non-Functional Requirements

### 10.1 Performance

- **Response Time**: p95 < 300ms for read APIs, p95 < 1s for write APIs
- **Throughput**: 1000 concurrent users, 10,000 requests/minute
- **Database**: < 100ms query time (indexed queries)
- **Cache**: > 80% hit ratio for product catalog

### 10.2 Availability

- **Target**: 99.9% uptime (8.76 hours downtime/year)
- **Strategy**: Multi-replica deployment (3+ replicas per service)
- **Failure Handling**: Circuit breakers, retries, fallbacks

### 10.3 Scalability

- **Horizontal Scaling**: All services stateless (scale with HPA)
- **Database**: Read replicas for high-read services (Product, Order)
- **Cache**: Redis cluster for high availability

### 10.4 Disaster Recovery

- **Backup**: Daily database backups, retained for 30 days
- **RTO**: 4 hours (Recovery Time Objective)
- **RPO**: 1 hour (Recovery Point Objective - max data loss)
- **Strategy**: Multi-region deployment (future)

---

## 11. Cost Estimation

**Development Environment** (Azure):
- AKS Cluster: 2 nodes (D2s_v3) → $140/month
- Azure SQL: Basic tier → $5/month per DB ($35 total)
- Redis Cache: Basic → $20/month
- Service Bus: Basic → $10/month
- **Total**: ~$205/month

**Production Environment** (Azure):
- AKS Cluster: 6 nodes (D4s_v3) → $840/month
- Azure SQL: Standard S2 → $120/month per DB ($840 total)
- Redis Cache: Standard C1 → $75/month
- Service Bus: Standard → $10/month
- Application Gateway: Standard → $125/month
- **Total**: ~$1,890/month

**Cost Optimization**:
- Use spot instances for non-critical workloads
- Auto-scale down during low traffic
- Reserved instances for predictable workloads (30-50% savings)

---

## 12. Migration Compatibility

### 12.1 Monolith Coexistence

During migration, the monolith and microservices will coexist:
- **Routing**: API Gateway routes to monolith or service based on endpoint
- **Data Sync**: Dual-write pattern during transition
- **Rollback**: API Gateway can switch traffic back to monolith

### 12.2 Behavior Preservation

- **No Functionality Changes**: Exact same API contracts
- **No Schema Changes**: Data format remains identical
- **No UI Changes**: Frontend behavior unchanged

### 12.3 Rollback Strategy

- Keep monolith running in production during migration
- API Gateway feature flag to switch traffic
- Database backups before each phase
- 24-hour monitoring period after each deployment

---

## 13. Success Criteria

### 13.1 Technical Metrics

- [ ] All services containerized and running in Kubernetes
- [ ] Independent deployment of each service
- [ ] API Gateway routing to all services
- [ ] Event-driven communication for async flows
- [ ] Health checks passing for all services
- [ ] Monitoring dashboards operational
- [ ] Database per service implemented

### 13.2 Operational Metrics

- [ ] Zero downtime during migration
- [ ] No functionality regressions
- [ ] Performance meets SLAs (p95 < 500ms)
- [ ] Successful rollback test performed
- [ ] Team trained on new architecture

### 13.3 Business Metrics

- [ ] Feature development velocity increased
- [ ] Deployment frequency increased (daily deployments)
- [ ] Mean time to recovery (MTTR) reduced
- [ ] Infrastructure costs within budget

---

## 14. Open Questions

1. **Service Mesh**: When to introduce Istio/Linkerd? (Proposal: After Phase 3)
2. **Multi-Region**: Do we need multi-region deployment? (Proposal: Phase 5+)
3. **Database Technology**: Stick with SQL Server or adopt PostgreSQL? (Recommendation: PostgreSQL for cost)
4. **Event Store**: Do we need event sourcing for audit trail? (Proposal: Defer until needed)
5. **API Versioning**: How to handle breaking changes? (Proposal: Use URL versioning `/api/v1/products`)

---

## 15. References

- **HLD.md**: Current monolith high-level design
- **LLD.md**: Current monolith low-level design
- **ADR-001**: Monolithic architecture decision
- **ADR-002**: EF Core with SQL Server
- **ADR-003**: Guest-only customer model
- **ADR-004**: Synchronous checkout with mock payment
- **Migration-Plan.md**: Detailed migration plan with phases
- **Microservices Patterns** by Chris Richardson
- **Building Microservices** by Sam Newman

---

## Document Approval

- [ ] Architecture Review Board approval
- [ ] Development Team review
- [ ] DevOps Team review
- [ ] Security Team review
- [ ] Management approval for first slice extraction

**Next Steps**: Review Migration-Plan.md for detailed implementation phases.
