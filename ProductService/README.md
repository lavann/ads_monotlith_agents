# Product Service

This is the first microservice extracted from the RetailMonolith application as part of Phase 1 of the migration plan.

## Overview

The Product Service manages the product catalog for the retail application. It provides RESTful APIs for browsing and querying product information.

## Features

- **Product Catalog API**: RESTful endpoints for product data
- **Redis Caching**: 1-hour cache TTL for optimal performance
- **Health Checks**: Database connectivity monitoring
- **Structured Logging**: Serilog integration with correlation IDs
- **EF Core Migrations**: Database schema management

## API Endpoints

### GET /api/products
Returns all active products from the catalog.

**Response**: `200 OK`
```json
[
  {
    "id": 1,
    "sku": "SKU-0001",
    "name": "Product Name",
    "description": "Product Description",
    "price": 99.99,
    "currency": "GBP",
    "isActive": true,
    "category": "Category Name"
  }
]
```

### GET /api/products/{id}
Returns a specific product by ID.

**Parameters**: 
- `id` (int): Product ID

**Response**: `200 OK` or `404 Not Found`

### GET /api/products/sku/{sku}
Returns a specific product by SKU.

**Parameters**:
- `sku` (string): Product SKU

**Response**: `200 OK` or `404 Not Found`

### GET /health
Health check endpoint for monitoring.

**Response**: `200 OK` (healthy) or `503 Service Unavailable` (unhealthy)

## Configuration

### appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=ProductServiceDB;Trusted_Connection=True;MultipleActiveResultSets=true",
    "Redis": "localhost:6379"
  }
}
```

## Running Locally

### Prerequisites
- .NET 10 SDK
- SQL Server LocalDB
- Redis (optional - service will work without it but without caching)

### Steps

1. Apply database migrations:
```bash
dotnet ef database update
```

2. Run the service:
```bash
dotnet run
```

3. Access the API at `http://localhost:5000` (or the port specified in launchSettings.json)

## Running Tests

```bash
cd ProductService.Tests
dotnet test
```

## Docker

### Build Image
```bash
docker build -t product-service:latest .
```

### Run Container
```bash
docker run -p 8080:8080 product-service:latest
```

## Kubernetes Deployment

Apply the Kubernetes manifests:

```bash
kubectl apply -f k8s-deployment.yaml
```

This creates:
- A Deployment with 1 replica (scalable)
- A Service (LoadBalancer type)
- Health check probes (liveness and readiness)

## Caching Strategy

The Product Service implements a **cache-aside** pattern using Redis:

1. On GET request, check Redis cache first
2. If cache miss, query database
3. Store result in cache with 1-hour TTL
4. Return data to client

Cache hit ratio should exceed 80% in production workloads.

## Monitoring

Key metrics to monitor:
- Request rate (requests/minute)
- Error rate (% of 5xx responses)
- Response time (p50, p95, p99)
- Cache hit ratio (%)
- Database query time (ms)
- Pod CPU and memory usage

## Migration Status

**Phase 1**: Product Catalog Service extraction (CURRENT)
- ✅ Service created with REST API
- ✅ Redis caching implemented
- ✅ Health checks added
- ✅ Unit tests passing
- ⏳ Data sync from monolith (pending)
- ⏳ API Gateway routing (pending)
- ⏳ Production deployment (pending)

## Architecture

This service follows the **Strangler Fig pattern**:
- Initially reads from separate database (data synced from monolith)
- Monolith continues to handle writes
- Eventually will take over full read/write ownership

## Dependencies

- Microsoft.EntityFrameworkCore.SqlServer: Database access
- StackExchange.Redis: Caching
- Serilog.AspNetCore: Logging
- Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore: Health checks

## Related Documentation

- [Migration Plan](/docs/Migration-Plan.md) - Phase 1 details
- [Target Architecture](/docs/Target-Architecture.md) - Overall microservices design
- [Test Strategy](/docs/Test-Strategy.md) - Testing approach
