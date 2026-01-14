# High-Level Design (HLD) - Retail Monolith Application

## Document Information
- **Last Updated**: 2026-01-14
- **Application**: RetailMonolith
- **Version**: 1.0
- **Framework**: ASP.NET Core 8.0

---

## 1. System Overview

The Retail Monolith is a traditional e-commerce application built as a single deployable unit using ASP.NET Core 8.0 with Razor Pages. It implements a complete retail workflow including product catalog browsing, shopping cart management, and order checkout with payment processing.

### 1.1 Purpose
This application serves as a baseline monolithic architecture designed to demonstrate:
- Traditional monolithic application patterns
- Domain boundaries within a single codebase
- Areas suitable for future decomposition into microservices

### 1.2 Architecture Style
- **Pattern**: Monolithic Application
- **UI Technology**: ASP.NET Core Razor Pages
- **API Style**: Minimal APIs (limited endpoints)
- **Data Access**: Entity Framework Core with Code-First approach

---

## 2. Domain Boundaries

The application is organized around four primary domain areas:

### 2.1 Products Domain
- **Responsibility**: Product catalog management
- **Key Entities**: `Product`, `InventoryItem`
- **Operations**:
  - Browse active products
  - View product details (SKU, name, description, price, category)
  - Check product availability

### 2.2 Cart Domain
- **Responsibility**: Shopping cart management for customer sessions
- **Key Entities**: `Cart`, `CartLine`
- **Operations**:
  - Create/retrieve cart for customer
  - Add products to cart
  - View cart contents
  - Clear cart after checkout

### 2.3 Orders Domain
- **Responsibility**: Order lifecycle management
- **Key Entities**: `Order`, `OrderLine`
- **Operations**:
  - Create orders from cart contents
  - Track order status (Created, Paid, Failed, Shipped)
  - Retrieve order history

### 2.4 Checkout Domain
- **Responsibility**: Orchestrate the checkout process
- **Key Services**: `CheckoutService`, `IPaymentGateway`
- **Operations**:
  - Validate cart contents
  - Reserve inventory
  - Process payment
  - Create order
  - Clear cart post-checkout

---

## 3. System Components

### 3.1 Presentation Layer
- **Technology**: Razor Pages (.cshtml + .cshtml.cs)
- **Pages**:
  - `/` - Home page
  - `/Products` - Product catalog listing
  - `/Cart` - Shopping cart view
  - `/Checkout` - Checkout page (placeholder)
  - `/Orders` - Order history (placeholder)

### 3.2 API Layer
Two minimal API endpoints for potential external integration:
- `POST /api/checkout` - Process checkout for a customer
- `GET /api/orders/{id}` - Retrieve order details

### 3.3 Service Layer
Core business logic services:
- **CartService** (`ICartService`): Cart operations
- **CheckoutService** (`ICheckoutService`): Checkout orchestration
- **MockPaymentGateway** (`IPaymentGateway`): Payment processing simulation

### 3.4 Data Access Layer
- **AppDbContext**: EF Core DbContext managing all entities
- **Entities**: Product, InventoryItem, Cart, CartLine, Order, OrderLine
- **Migrations**: Code-first migrations for schema management

---

## 4. Data Stores

### 4.1 Primary Database
- **Type**: SQL Server
- **Default Configuration**: LocalDB (for development)
- **Connection**: 
  - LocalDB: `Server=(localdb)\\MSSQLLocalDB;Database=RetailMonolith;Trusted_Connection=True;MultipleActiveResultSets=true`
  - Configurable via `ConnectionStrings:DefaultConnection` in appsettings.json
- **ORM**: Entity Framework Core 9.0.9

### 4.2 Database Schema

**Products Table**
- Stores product catalog information
- Unique index on `Sku`
- Fields: Id, Sku, Name, Description, Price, Currency, IsActive, Category

**Inventory Table**
- Tracks stock levels by SKU
- Unique index on `Sku`
- Fields: Id, Sku, Quantity

**Carts Table**
- Stores shopping cart sessions
- Fields: Id, CustomerId

**CartLines Table**
- Individual line items in carts
- Foreign key to Carts
- Fields: Id, CartId, Sku, Name, UnitPrice, Quantity

**Orders Table**
- Completed/attempted orders
- Fields: Id, CreatedUtc, CustomerId, Status, Total

**OrderLines Table**
- Order line items (snapshot of cart at checkout)
- Foreign key to Orders
- Fields: Id, OrderId, Sku, Name, UnitPrice, Quantity

### 4.3 Data Seeding
- Automatic seeding on application startup
- Seeds 50 sample products across 6 categories (Apparel, Footwear, Accessories, Electronics, Home, Beauty)
- Random pricing between £5-£105 (GBP)
- Random inventory quantities (10-200 units)

---

## 5. External Dependencies

### 5.1 NuGet Packages
- **Microsoft.EntityFrameworkCore.SqlServer** (9.0.9): SQL Server database provider
- **Microsoft.EntityFrameworkCore.Design** (9.0.9): Design-time tools for migrations
- **Microsoft.AspNetCore.Diagnostics.HealthChecks** (2.2.0): Health monitoring
- **Microsoft.Extensions.Http.Polly** (9.0.9): HTTP resilience (not currently used in code)

### 5.2 Payment Gateway
- **Current Implementation**: Mock gateway (`MockPaymentGateway`)
- **Behavior**: Always returns successful payment with mock transaction ID
- **Future**: Interface allows swapping to real payment provider

---

## 6. Runtime Assumptions

### 6.1 Deployment Model
- Single process ASP.NET Core application
- Runs on Kestrel web server
- HTTPS redirection enabled
- Static file serving from wwwroot

### 6.2 Environment Configuration
- **Development**: Uses LocalDB, exception page, no HSTS
- **Production**: Exception handler at /Error, HSTS enabled

### 6.3 Startup Behavior
- Automatic database migration on startup (`db.Database.MigrateAsync()`)
- Automatic data seeding if database is empty
- Health check endpoint registered at `/health`

### 6.4 Customer Model
- **Current State**: Single "guest" customer model
- **No Authentication**: All operations use hardcoded "guest" CustomerId
- **No Session Management**: Customer identity not tied to HTTP sessions

### 6.5 Concurrency
- No explicit concurrency control on inventory updates
- Risk of overselling in high-traffic scenarios
- Database uses `MultipleActiveResultSets=true`

### 6.6 Transaction Management
- Single transaction per checkout operation
- Inventory reservation and order creation occur in same SaveChanges call
- No distributed transaction support

---

## 7. Key Architectural Characteristics

### 7.1 Strengths
- Simple deployment model (single artifact)
- Consistent data transactions (single database)
- Easy local development setup
- Clear separation of concerns via services and interfaces

### 7.2 Current Limitations
- No authentication/authorization
- Shared database creates coupling between domains
- Synchronous checkout flow (blocking)
- Mock payment gateway only
- No caching layer
- No event-driven architecture
- Limited scalability (vertical scaling only)

---

## 8. Deployment View

```
┌─────────────────────────────────────┐
│     ASP.NET Core Application        │
│  ┌───────────────────────────────┐  │
│  │   Razor Pages (UI)            │  │
│  │   /Products, /Cart, /Checkout │  │
│  └───────────────────────────────┘  │
│  ┌───────────────────────────────┐  │
│  │   Minimal APIs                │  │
│  │   /api/checkout, /api/orders  │  │
│  └───────────────────────────────┘  │
│  ┌───────────────────────────────┐  │
│  │   Service Layer               │  │
│  │   CartService, CheckoutService│  │
│  └───────────────────────────────┘  │
│  ┌───────────────────────────────┐  │
│  │   Data Layer (EF Core)        │  │
│  │   AppDbContext                │  │
│  └───────────────────────────────┘  │
└─────────────────────────────────────┘
            │
            ▼
┌─────────────────────────────────────┐
│   SQL Server / LocalDB              │
│   Database: RetailMonolith          │
└─────────────────────────────────────┘
```

---

## 9. Future Considerations

Based on the current architecture, potential decomposition targets include:
1. **Product Catalog Service**: Independent product management
2. **Inventory Service**: Stock management with eventual consistency
3. **Order Service**: Order processing and history
4. **Payment Service**: Integration with real payment providers

The code already contains hints for this evolution (e.g., comment about "publish events here" in CheckoutService).
