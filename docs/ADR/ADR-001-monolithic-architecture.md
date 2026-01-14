# ADR-001: Monolithic Architecture

## Status
Accepted

## Context
The Retail application needs to support core e-commerce functionality including product catalog, shopping cart, order management, and payment processing. The application is in early stages with moderate expected traffic and a small development team.

## Decision
We will build the application as a monolithic ASP.NET Core application with all domains (Products, Cart, Orders, Checkout) deployed together as a single unit.

## Rationale

### Why Monolith?
1. **Team Size**: Small team can work effectively in a single codebase
2. **Simplicity**: Single deployment artifact, simpler CI/CD pipeline
3. **Development Speed**: No need for inter-service communication patterns
4. **Consistent Transactions**: ACID transactions across all domains in single database
5. **Easier Debugging**: All code runs in single process
6. **Infrastructure Cost**: Single application server and database reduces hosting costs

### Design Boundaries
Even within the monolith, we maintain clear domain boundaries:
- Separate namespaces for Products, Cart, Orders
- Service layer abstracts business logic (CartService, CheckoutService)
- Interface-based design (ICartService, IPaymentGateway) allows future extraction

## Consequences

### Positive
- Fast initial development
- Easy local development setup (single project to run)
- Strong consistency across domains
- Simple deployment model
- Reduced operational complexity

### Negative
- All domains must scale together (cannot scale independently)
- Entire application redeployed for any change
- Shared database creates coupling
- Single technology stack for all domains
- Potential for tight coupling if boundaries aren't respected

### Migration Path
The codebase is structured to allow future decomposition:
- Service interfaces can become API contracts
- Domain models are isolated
- Checkout service already has comment: "publish events here" indicating event-driven future
- Minimal APIs (`/api/checkout`, `/api/orders/{id}`) show intent for service extraction

## Date
2025-10-19 (inferred from initial migration date)

## Participants
Development Team
