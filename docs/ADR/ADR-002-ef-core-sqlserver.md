# ADR-002: Entity Framework Core with SQL Server LocalDB

## Status
Accepted

## Context
The application requires persistent storage for products, inventory, carts, and orders. The team needs a data access technology that supports:
- Rapid development
- Type-safe queries
- Database schema versioning
- Easy local development

## Decision
Use Entity Framework Core 9.0 as the ORM with SQL Server as the database engine. For local development, use SQL Server LocalDB as the default database.

## Rationale

### Why EF Core?
1. **Code-First Development**: Define schema in C# models, generate migrations automatically
2. **Type Safety**: LINQ queries are compile-time checked
3. **Productivity**: Reduces boilerplate data access code
4. **Migrations**: Built-in schema versioning with `dotnet ef migrations` commands
5. **Ecosystem**: First-class support in ASP.NET Core

### Why SQL Server?
1. **Maturity**: Battle-tested relational database
2. **ACID Transactions**: Strong consistency guarantees for checkout flow
3. **Tooling**: Excellent tools (SQL Server Management Studio, Azure Data Studio)
4. **Azure Integration**: Easy path to Azure SQL Database for cloud deployment
5. **Team Familiarity**: Team has SQL Server experience

### Why LocalDB for Development?
1. **Zero Configuration**: Comes with Visual Studio, no separate install
2. **Lightweight**: Runs on-demand, minimal resource usage
3. **Isolated**: Each developer has own instance
4. **Connection String**: Simple connection string in appsettings

## Implementation Details

### Connection String Strategy
- **Default**: LocalDB for development
  ```
  Server=(localdb)\\MSSQLLocalDB;Database=RetailMonolith;Trusted_Connection=True;MultipleActiveResultSets=true
  ```
- **Configurable**: Can override via appsettings.json or environment variables for Azure SQL or other SQL Server instances
- **Design-Time**: Separate `DesignTimeDbContextFactory` uses hardcoded LocalDB connection for migration commands

### Migration Strategy
- Migrations stored in `/Migrations` directory
- Automatic migration on application startup: `await db.Database.MigrateAsync()`
- Initial migration created: `20251019185248_Initial.cs`

### Seeding Strategy
- Automatic seeding on startup via `AppDbContext.SeedAsync(db)`
- Only seeds if Products table is empty (idempotent)
- Seeds 50 sample products with inventory

## Consequences

### Positive
- **Rapid Development**: No manual SQL required for CRUD operations
- **Type Safety**: Refactoring models updates queries automatically
- **Easy Testing**: In-memory database provider available for tests
- **Cloud Ready**: Same code works with LocalDB and Azure SQL
- **Auto Migration**: Developers always have latest schema on startup

### Negative
- **Performance**: ORM abstraction can generate non-optimal queries (e.g., N+1 problem in CheckoutService)
- **Learning Curve**: Team must understand EF Core query translation and tracking behavior
- **Vendor Lock-in**: Tied to SQL Server (though EF Core supports multiple providers)
- **Startup Delay**: Auto-migration on startup adds latency to application start
- **LocalDB Limitations**: Not suitable for production; requires separate DB for deployment

### Mitigations
- Use `.AsNoTracking()` for read-only queries to improve performance
- Use eager loading (`.Include()`) to avoid N+1 queries
- Monitor generated SQL with logging for optimization opportunities
- Document LocalDB requirement in README for developer onboarding

## Alternatives Considered

### Dapper (Micro-ORM)
- **Pros**: Faster than EF Core, more control over SQL
- **Cons**: More boilerplate, no automatic migrations, manual mapping
- **Decision**: Rejected due to slower development velocity

### PostgreSQL
- **Pros**: Open-source, excellent performance, JSON support
- **Cons**: Team less familiar, fewer cloud integration options in Azure ecosystem
- **Decision**: Rejected due to team expertise and Azure alignment

### NoSQL (CosmosDB, MongoDB)
- **Pros**: Flexible schema, horizontal scaling
- **Cons**: No ACID transactions across domains, eventual consistency complexity
- **Decision**: Rejected due to need for strong consistency in checkout flow

## Date
2025-10-19 (inferred from initial migration and EF Core package references)

## Participants
Development Team
