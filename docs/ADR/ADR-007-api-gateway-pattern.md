# ADR-007: API Gateway Pattern

## Status
Proposed

## Context
With the migration to microservices, we need a strategy for external clients (frontend, mobile apps, external partners) to access backend services. Without a gateway, clients would need to:

1. Know the location of each service (service discovery)
2. Handle authentication/authorization per service
3. Deal with different protocols/formats per service
4. Manage cross-cutting concerns (logging, rate limiting) in each client

This creates tight coupling between clients and services, making the system brittle and hard to evolve.

## Decision

We will implement an **API Gateway** as a single entry point for all external traffic.

### Gateway Technology: YARP (Yet Another Reverse Proxy)

- **Open-source** .NET reverse proxy framework
- **Configuration-driven** (JSON or code-based routing)
- **High performance** (built on Kestrel)
- **Extensible** (custom middleware for auth, rate limiting, logging)

### Responsibilities

The API Gateway will handle:

1. **Request Routing**: Route requests to appropriate backend services
2. **Protocol Translation**: Expose REST APIs externally, regardless of internal protocols
3. **Authentication**: Validate JWT tokens before forwarding requests
4. **Rate Limiting**: Throttle requests per client to prevent abuse
5. **Request/Response Logging**: Centralized logging with correlation IDs
6. **Circuit Breaking**: Fail fast when backend services are down
7. **SSL Termination**: Handle HTTPS externally, HTTP internally

The API Gateway will **NOT** handle:
- Business logic (stays in services)
- Data transformation (services return correct format)
- Heavy computation (lightweight proxy only)

### Deployment

- Deployed as a separate service in Kubernetes
- 3 replicas for high availability
- Horizontal auto-scaling based on request rate
- Public LoadBalancer service for external access

## Architecture

```
┌─────────────┐
│   Frontend  │
│  (Browser)  │
└──────┬──────┘
       │ HTTPS
       ▼
┌─────────────────────────────────┐
│      API Gateway (YARP)         │
│  - Authentication (JWT)         │
│  - Rate Limiting                │
│  - Routing                      │
│  - Circuit Breaking             │
│  - Logging (correlation ID)     │
└─────────────────────────────────┘
       │ HTTP (internal)
       ├────────────────┬───────────────┬────────────────┐
       ▼                ▼               ▼                ▼
┌────────────┐  ┌────────────┐  ┌────────────┐  ┌────────────┐
│  Product   │  │ Inventory  │  │   Order    │  │    Cart    │
│  Service   │  │  Service   │  │  Service   │  │  Service   │
└────────────┘  └────────────┘  └────────────┘  └────────────┘
```

## Routing Configuration

### YARP Configuration (appsettings.json)

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
      "carts-route": {
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
      },
      "monolith-fallback": {
        "ClusterId": "monolith",
        "Match": {
          "Path": "/api/{**catch-all}"
        },
        "Order": 999
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
      },
      "monolith": {
        "Destinations": {
          "destination1": {
            "Address": "http://monolith:80"
          }
        }
      }
    }
  }
}
```

**Key Points**:
- Specific routes checked first (Order: default 0)
- Monolith fallback route matches all unmatched paths (Order: 999)
- Service discovery via Kubernetes DNS (e.g., `http://product-service:80`)
- Configuration hot-reloads on change (no restart needed)

### Strangler Pattern Support

During migration, routing evolves:

**Phase 0** (Before microservices):
```
/api/** → Monolith
```

**Phase 1** (Product service extracted):
```
/api/products/** → Product Service
/api/** → Monolith (fallback)
```

**Phase 6** (Monolith decommissioned):
```
/api/products/** → Product Service
/api/inventory/** → Inventory Service
/api/orders/** → Order Service
/api/carts/** → Cart Service
/api/checkout/** → Checkout Service
(No monolith fallback)
```

## Cross-Cutting Concerns

### 1. Authentication (JWT)

**Custom Middleware**:
```csharp
app.Use(async (context, next) =>
{
    // Skip auth for health checks
    if (context.Request.Path.StartsWithSegments("/health"))
    {
        await next();
        return;
    }

    // Validate JWT token
    var token = context.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
    if (token == null)
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsync("Unauthorized");
        return;
    }

    // Validate token signature and expiry
    var principal = ValidateToken(token);
    if (principal == null)
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsync("Invalid token");
        return;
    }

    // Add user identity to request headers for downstream services
    context.Request.Headers.Add("X-User-Id", principal.FindFirst("sub")?.Value);
    context.Request.Headers.Add("X-User-Role", principal.FindFirst("role")?.Value);

    await next();
});
```

**Note**: Initial implementation will skip JWT validation (guest-only model from ADR-003). JWT validation added when authentication is implemented.

### 2. Rate Limiting

**Strategy**: Token bucket algorithm per client IP

**Configuration**:
```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("api", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 10;
    });
});

app.UseRateLimiter();
```

**Limits**:
- **Anonymous users**: 100 requests/minute
- **Authenticated users**: 500 requests/minute
- **Admin users**: 1000 requests/minute

**Exceeded Response**: `429 Too Many Requests` with `Retry-After` header

### 3. Correlation ID

**Middleware**:
```csharp
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault() 
                        ?? Guid.NewGuid().ToString();
    
    context.Request.Headers["X-Correlation-ID"] = correlationId;
    context.Response.Headers.Add("X-Correlation-ID", correlationId);
    
    // Add to logs
    using (LogContext.PushProperty("CorrelationId", correlationId))
    {
        await next();
    }
});
```

**Usage**: Trace requests across services in logs and distributed tracing

### 4. Circuit Breaker

**Polly Configuration**:
```csharp
services.AddReverseProxy()
    .LoadFromConfig(Configuration.GetSection("ReverseProxy"))
    .ConfigureHttpClient((context, handler) =>
    {
        handler.Timeout = TimeSpan.FromSeconds(30);
    })
    .AddTransforms(transformBuilderContext =>
    {
        transformBuilderContext.AddRequestTransform(async transformContext =>
        {
            // Add circuit breaker via Polly
            var policy = Policy
                .Handle<HttpRequestException>()
                .CircuitBreakerAsync(
                    exceptionsAllowedBeforeBreaking: 5,
                    durationOfBreak: TimeSpan.FromSeconds(30)
                );
            
            await policy.ExecuteAsync(async () => await Task.CompletedTask);
        });
    });
```

**Configuration per Service**:
- **Threshold**: 5 consecutive failures
- **Break Duration**: 30 seconds
- **Timeout**: 30 seconds per request

**Behavior**:
- After 5 failures, circuit opens (stop sending requests)
- Wait 30 seconds, then try 1 request (half-open state)
- If successful, circuit closes (resume normal operation)
- If failed, circuit opens again

### 5. Request/Response Logging

**Structured Logging**:
```csharp
app.Use(async (context, next) =>
{
    var sw = Stopwatch.StartNew();
    
    Log.Information("Request started: {Method} {Path}", 
        context.Request.Method, 
        context.Request.Path);
    
    await next();
    
    sw.Stop();
    
    Log.Information("Request completed: {Method} {Path} {StatusCode} {Duration}ms",
        context.Request.Method,
        context.Request.Path,
        context.Response.StatusCode,
        sw.ElapsedMilliseconds);
});
```

**Logged Fields**:
- Timestamp
- Correlation ID
- Method (GET, POST, etc.)
- Path (`/api/products/123`)
- Status code (200, 404, 500)
- Duration (milliseconds)
- Client IP
- User ID (if authenticated)

## Rationale

### Why API Gateway?

1. **Single Entry Point**: Clients call one endpoint, not N services
2. **Loose Coupling**: Clients don't need to know service locations
3. **Centralized Auth**: Validate tokens once at gateway, not per service
4. **Cross-Cutting Concerns**: Logging, rate limiting, circuit breaking in one place
5. **Protocol Flexibility**: Gateway can translate protocols (REST to gRPC)
6. **Service Evolution**: Add/remove/rename services without breaking clients

**Alternative considered**: **No gateway, direct service access** → Rejected because it creates tight coupling between clients and services, duplicates cross-cutting concerns, and makes evolution difficult.

### Why YARP?

1. **.NET Native**: Same stack as our services (C#, ASP.NET Core)
2. **High Performance**: Built on Kestrel (one of fastest web servers)
3. **Configuration-Driven**: No code changes for routing updates
4. **Extensible**: Easy to add custom middleware
5. **Microsoft-Backed**: Official Microsoft project
6. **Hot Reload**: Configuration changes without restart

**Alternative considered**: **Kong** → Rejected because it's Lua-based (different language), heavier (requires PostgreSQL), and team lacks Lua expertise.

**Alternative considered**: **Traefik** → Rejected because it's Go-based, configuration is more complex (labels + YAML), and team prefers .NET stack.

**Alternative considered**: **NGINX** → Rejected because it's C-based, configuration is less intuitive (conf files), and lacks built-in .NET integration for custom logic.

### Why Kubernetes Service Discovery?

1. **Built-in**: No external service registry (Consul, Eureka) needed
2. **Simple**: Services accessible via DNS name (`http://product-service`)
3. **Load Balancing**: Kubernetes Service distributes traffic across pods
4. **Dynamic**: Service IP changes don't affect gateway (DNS resolves)

**Alternative considered**: **Consul** → Deferred until multi-cluster or multi-cloud needs arise.

## Consequences

### Positive

- **Simplified Clients**: Clients call single endpoint, gateway handles routing
- **Centralized Auth**: Token validation once at gateway
- **Easy Service Evolution**: Add/remove services by updating gateway config
- **Observability**: All requests logged and traced through gateway
- **Security**: Single point to enforce rate limiting, CORS, etc.
- **Resilience**: Circuit breaker prevents cascading failures

### Negative

- **Single Point of Failure**: Gateway down = entire API down (mitigated by HA)
- **Latency**: Extra network hop adds ~5-10ms latency
- **Bottleneck**: Gateway must handle all traffic (mitigated by scaling)
- **Complexity**: Another component to manage and monitor

### Operational Impact

**New Responsibilities**:
- Monitor gateway health and performance
- Update routing configuration during migrations
- Manage SSL certificates (Let's Encrypt automation)
- Tune rate limiting and circuit breaker parameters

**Monitoring**:
- Gateway request rate (requests/second)
- Gateway error rate (% of 5xx responses)
- Gateway latency (p50, p95, p99)
- Circuit breaker state (open/closed per service)
- Rate limit rejections (429 responses)

## High Availability

### Deployment Configuration

- **Replicas**: 3 pods minimum
- **Anti-Affinity**: Spread pods across nodes
- **PodDisruptionBudget**: Always keep 2 pods running during updates
- **Auto-Scaling**: Scale up if CPU > 70%

### Failover Behavior

- Kubernetes Service load balances across healthy gateway pods
- If 1 pod crashes, traffic routes to remaining 2
- New pod spun up automatically (self-healing)
- No downtime if at least 1 pod is healthy

### Example Configuration

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: api-gateway
spec:
  replicas: 3
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxUnavailable: 1
      maxSurge: 1
  template:
    spec:
      affinity:
        podAntiAffinity:
          preferredDuringSchedulingIgnoredDuringExecution:
          - weight: 100
            podAffinityTerm:
              labelSelector:
                matchLabels:
                  app: api-gateway
              topologyKey: kubernetes.io/hostname
      containers:
      - name: api-gateway
        image: retail/api-gateway:1.0.0
        ports:
        - containerPort: 8080
        resources:
          requests:
            memory: "256Mi"
            cpu: "100m"
          limits:
            memory: "512Mi"
            cpu: "500m"
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
---
apiVersion: v1
kind: Service
metadata:
  name: api-gateway
spec:
  type: LoadBalancer
  ports:
  - port: 80
    targetPort: 8080
    protocol: TCP
  selector:
    app: api-gateway
---
apiVersion: policy/v1
kind: PodDisruptionBudget
metadata:
  name: api-gateway-pdb
spec:
  minAvailable: 2
  selector:
    matchLabels:
      app: api-gateway
```

## Security

### SSL/TLS

- **External**: HTTPS with TLS 1.2+ (Let's Encrypt certificates)
- **Internal**: HTTP (within Kubernetes cluster, consider mTLS in future)

### CORS

Configure CORS policy for browser-based frontends:

```csharp
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("https://retail.example.com")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

app.UseCors();
```

### API Keys (Future)

For external partners accessing APIs:
- Issue API keys via admin portal
- Validate API key at gateway
- Rate limit per API key
- Revoke keys instantly

## Testing

### Unit Tests

- Test routing configuration parsing
- Test custom middleware (auth, rate limiting)
- Mock backend services

### Integration Tests

- Deploy gateway + mock backend services
- Test routing to correct services
- Test auth enforcement
- Test rate limiting thresholds
- Test circuit breaker behavior

### Load Tests

- Baseline: 1000 req/sec with < 50ms p95 latency
- Stress test: Ramp up to failure point
- Test auto-scaling triggers

## Migration Strategy

### Phase 0: Gateway Deployment

1. Deploy API Gateway to Kubernetes
2. Configure routing to monolith only (pass-through mode)
3. Point frontend to API Gateway instead of monolith directly
4. Validate all functionality works through gateway
5. Monitor performance (should be same as monolith + ~10ms)

### Phase 1-5: Incremental Service Extraction

For each extracted service:
1. Add route to gateway configuration
2. Deploy service
3. Test via gateway
4. Switch traffic (canary: 10% → 100%)
5. Keep monolith fallback route

### Phase 6: Decommission Monolith

1. Verify no traffic hitting monolith fallback route
2. Remove monolith route from gateway config
3. Scale monolith to 0 replicas
4. Monitor for 1 week
5. Delete monolith deployment

## Related ADRs

- **ADR-005**: Service Decomposition Strategy (what services the gateway routes to)
- **ADR-006**: Container Deployment Model (how gateway is deployed)
- **ADR-009**: Event-Driven Communication (async patterns outside gateway)

## Date
2026-01-14

## Participants
- Architecture Team
- Development Team
- DevOps Team

## Review
This ADR should be reviewed after Phase 0 completion to validate gateway performance and usability.
