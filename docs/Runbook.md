# Runbook - Retail Monolith Application

## Document Information
- **Last Updated**: 2026-01-14
- **Application**: RetailMonolith
- **Framework**: ASP.NET Core 8.0

---

## 1. Prerequisites

### 1.1 Required Software
- **.NET 8 SDK**: [Download here](https://dotnet.microsoft.com/download/dotnet/8.0)
- **SQL Server LocalDB**: Included with Visual Studio, or install separately
  - Comes with Visual Studio 2022 (any edition)
  - Standalone installer: [SQL Server Express](https://www.microsoft.com/en-us/sql-server/sql-server-downloads)

### 1.2 Verify Installation
```bash
# Check .NET SDK version
dotnet --version
# Should show 8.0.x

# Check LocalDB installation
sqllocaldb info
# Should list available instances (e.g., "MSSQLLocalDB")
```

### 1.3 Development Tools (Optional but Recommended)
- **Visual Studio 2022** or **Visual Studio Code**
- **Azure Data Studio** or **SQL Server Management Studio** (for database inspection)
- **Git** (for version control)

---

## 2. Getting Started

### 2.1 Clone Repository
```bash
git clone https://github.com/lavann/ads_monotlith_agents.git
cd ads_monotlith_agents
```

### 2.2 Restore Dependencies
```bash
dotnet restore
```

### 2.3 Database Setup
The application uses automatic migrations on startup, so no manual database setup is required. On first run, it will:
1. Create the `RetailMonolith` database in LocalDB
2. Apply all migrations
3. Seed 50 sample products with inventory

**If you want to manually control migrations:**
```bash
# View migration status
dotnet ef migrations list

# Apply migrations manually (not needed due to auto-migration)
dotnet ef database update

# Create a new migration (if you modify models)
dotnet ef migrations add YourMigrationName
```

### 2.4 Run Application
```bash
dotnet run
```

Expected output:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:5001
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
```

### 2.5 Access Application
Open browser and navigate to:
- **HTTPS**: https://localhost:5001
- **HTTP**: http://localhost:5000

---

## 3. Application Endpoints

### 3.1 Web Pages (Razor Pages)
| URL | Description | Status |
|-----|-------------|--------|
| `/` | Home page | ✅ Working |
| `/Products` | Product catalog with "Add to Cart" | ✅ Working |
| `/Cart` | View shopping cart | ✅ Working |
| `/Checkout` | Checkout page | ⚠️ Placeholder only |
| `/Orders` | Order history | ⚠️ Placeholder only |
| `/Privacy` | Privacy policy page | ✅ Working |

### 3.2 API Endpoints (Minimal APIs)
| Method | URL | Description | Status |
|--------|-----|-------------|--------|
| POST | `/api/checkout` | Process checkout for "guest" customer | ✅ Working |
| GET | `/api/orders/{id}` | Retrieve order by ID | ✅ Working |
| GET | `/health` | Health check endpoint | ✅ Working |

### 3.3 Testing API Endpoints

**Checkout API:**
```bash
# Prerequisites: Add items to cart via UI first at /Products

curl -X POST https://localhost:5001/api/checkout \
  -H "Content-Type: application/json" \
  --insecure

# Example response:
# {"id":1,"status":"Paid","total":123.45}
```

**Get Order API:**
```bash
curl https://localhost:5001/api/orders/1 --insecure

# Example response:
# {
#   "id": 1,
#   "createdUtc": "2026-01-14T16:00:00Z",
#   "customerId": "guest",
#   "status": "Paid",
#   "total": 123.45,
#   "lines": [...]
# }
```

**Health Check:**
```bash
curl https://localhost:5001/health --insecure

# Response: "Healthy"
```

---

## 4. Common Commands

### 4.1 Build
```bash
# Build in Debug mode
dotnet build

# Build in Release mode
dotnet build --configuration Release
```

### 4.2 Run with Specific Environment
```bash
# Development environment (default)
dotnet run --environment Development

# Production environment
dotnet run --environment Production
```

### 4.3 Watch Mode (Auto-Reload)
```bash
# Automatically rebuilds and restarts on code changes
dotnet watch run
```

### 4.4 Database Commands

**View migrations:**
```bash
dotnet ef migrations list
```

**Create new migration:**
```bash
dotnet ef migrations add YourMigrationName
```

**Apply migrations:**
```bash
dotnet ef database update
```

**Rollback to specific migration:**
```bash
dotnet ef database update PreviousMigrationName
```

**Reset database (drop and recreate):**
```bash
dotnet ef database drop --force
dotnet run
# Auto-migration will recreate database and seed data
```

**Generate SQL script from migrations:**
```bash
dotnet ef migrations script --output migration.sql
```

### 4.5 Testing

**Note**: Currently no test project exists in the solution.

To add tests in the future:
```bash
# Create test project
dotnet new xunit -n RetailMonolith.Tests

# Add reference to main project
cd RetailMonolith.Tests
dotnet add reference ../RetailMonolith.csproj

# Run tests
dotnet test
```

---

## 5. Configuration

### 5.1 Connection String
Default connection string (LocalDB):
```
Server=(localdb)\\MSSQLLocalDB;Database=RetailMonolith;Trusted_Connection=True;MultipleActiveResultSets=true
```

**To override**, edit `appsettings.json` or `appsettings.Development.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Your-Connection-String-Here"
  }
}
```

**Using environment variable:**
```bash
# Linux/macOS
export ConnectionStrings__DefaultConnection="Server=...;Database=...;"
dotnet run

# Windows PowerShell
$env:ConnectionStrings__DefaultConnection="Server=...;Database=...;"
dotnet run

# Windows CMD
set ConnectionStrings__DefaultConnection=Server=...;Database=...;
dotnet run
```

### 5.2 Logging
Logging levels can be configured in `appsettings.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  }
}
```

To see EF Core SQL queries, set:
```json
"Microsoft.EntityFrameworkCore.Database.Command": "Information"
```

---

## 6. Troubleshooting

### 6.1 LocalDB Not Found
**Error**: `A network-related or instance-specific error occurred while establishing a connection to SQL Server`

**Solutions**:
1. Check if LocalDB is installed:
   ```bash
   sqllocaldb info
   ```

2. Create LocalDB instance if needed:
   ```bash
   sqllocaldb create MSSQLLocalDB
   sqllocaldb start MSSQLLocalDB
   ```

3. Use full SQL Server or Azure SQL by updating connection string

### 6.2 Port Already in Use
**Error**: `Failed to bind to address https://localhost:5001`

**Solutions**:
1. Kill process using the port:
   ```bash
   # Linux/macOS
   lsof -ti:5001 | xargs kill -9
   
   # Windows PowerShell
   Get-Process -Id (Get-NetTCPConnection -LocalPort 5001).OwningProcess | Stop-Process
   ```

2. Change port in `Properties/launchSettings.json`:
   ```json
   "applicationUrl": "https://localhost:7001;http://localhost:7000"
   ```

### 6.3 Migration Failures
**Error**: `A migration is already in progress`

**Solution**:
```bash
# Remove lock
dotnet ef database update --force

# Or reset database
dotnet ef database drop --force
dotnet run
```

### 6.4 Certificate Trust Issues
**Error**: `The SSL connection could not be established`

**Solution**:
```bash
# Trust development certificate
dotnet dev-certs https --trust
```

### 6.5 Items Added to Cart Twice
**Known Bug**: When adding product to cart via `/Products` page, item is added twice.

**Root Cause**: `Pages/Products/Index.cshtml.cs` has duplicate add logic (manual + service call)

**Workaround**: Be aware that quantity will be double expected. Fix is documented in LLD.md.

---

## 7. Known Issues and Technical Debt

### 7.1 High Priority Issues

#### Issue: Duplicate Cart Addition
- **Location**: `Pages/Products/Index.cshtml.cs`, `OnPostAsync` method
- **Impact**: Products added twice when using "Add to Cart" button
- **Workaround**: Use API or manually adjust quantities
- **Fix**: Remove manual cart manipulation, keep only `CartService.AddToCartAsync` call

#### Issue: No Inventory Rollback on Payment Failure
- **Location**: `Services/CheckoutService.cs`
- **Impact**: If payment fails, inventory is still decremented (stock "lost")
- **Workaround**: Mock gateway never fails, so not visible in demo
- **Fix**: Move inventory decrement after successful payment

#### Issue: Shared "Guest" Cart
- **Location**: Throughout application
- **Impact**: All users share single cart (no multi-user support)
- **Workaround**: Application is demo/single-user only
- **Fix**: Implement proper authentication and customer management

### 7.2 Medium Priority Issues

#### Issue: N+1 Query in Checkout
- **Location**: `Services/CheckoutService.cs`, inventory lookup loop
- **Impact**: Performance degradation with large carts
- **Fix**: Load all inventory items in single query before loop

#### Issue: No Error Handling in APIs
- **Location**: `Program.cs`, minimal API endpoints
- **Impact**: Unhandled exceptions return 500 with stack trace
- **Fix**: Add try-catch blocks and return appropriate error responses

#### Issue: Placeholder Pages
- **Location**: `/Checkout` and `/Orders` pages
- **Impact**: Pages exist but have no functionality
- **Fix**: Implement checkout UI and order history display

### 7.3 Low Priority Issues

#### Issue: Unused Dependencies
- **Package**: `Microsoft.Extensions.Http.Polly`
- **Impact**: Unnecessary dependency bloat
- **Fix**: Remove from `.csproj` or implement resilient HTTP calls

#### Issue: Magic Strings
- **Examples**: "guest" customer ID, "GBP" currency, order status strings
- **Impact**: Potential typos, harder to refactor
- **Fix**: Extract to constants or enums

---

## 8. Database Inspection

### 8.1 Connect to LocalDB
**Using Azure Data Studio / SSMS:**
```
Server name: (localdb)\MSSQLLocalDB
Authentication: Windows Authentication
```

### 8.2 Useful Queries
```sql
-- View all products
SELECT * FROM Products WHERE IsActive = 1;

-- View inventory levels
SELECT Sku, Quantity FROM Inventory ORDER BY Quantity;

-- View current cart for guest user
SELECT c.Id, c.CustomerId, cl.Sku, cl.Name, cl.Quantity, cl.UnitPrice
FROM Carts c
JOIN CartLines cl ON c.Id = cl.CartId
WHERE c.CustomerId = 'guest';

-- View all orders
SELECT Id, CreatedUtc, CustomerId, Status, Total
FROM Orders
ORDER BY CreatedUtc DESC;

-- View order details
SELECT o.Id, o.Status, o.Total, ol.Sku, ol.Name, ol.Quantity, ol.UnitPrice
FROM Orders o
JOIN OrderLines ol ON o.Id = ol.OrderId
WHERE o.Id = 1;

-- Check low stock items
SELECT p.Name, i.Sku, i.Quantity
FROM Products p
JOIN Inventory i ON p.Sku = i.Sku
WHERE i.Quantity < 20
ORDER BY i.Quantity;
```

---

## 9. Development Workflow

### 9.1 Typical Development Session
```bash
# 1. Pull latest changes
git pull

# 2. Restore dependencies (if packages changed)
dotnet restore

# 3. Run in watch mode
dotnet watch run

# 4. Make code changes (auto-reloads)

# 5. If models changed, create migration
dotnet ef migrations add YourMigrationName

# 6. Test changes in browser
# https://localhost:5001

# 7. Commit changes
git add .
git commit -m "Your commit message"
git push
```

### 9.2 Adding a New Feature
1. Create new service interface and implementation in `/Services`
2. Register service in `Program.cs` dependency injection
3. Add new page models in `/Pages` if needed
4. Add database entities in `/Models` if needed
5. Create migration: `dotnet ef migrations add FeatureName`
6. Test locally with `dotnet watch run`

---

## 10. Deployment Considerations

### 10.1 Azure App Service
1. Create Azure SQL Database (LocalDB not available in Azure)
2. Update connection string in Azure App Service configuration
3. Deploy via Visual Studio, CLI, or GitHub Actions
4. Automatic migration will run on first startup

### 10.2 Docker (Future)
Currently no Dockerfile exists. To containerize:
1. Create `Dockerfile` using `mcr.microsoft.com/dotnet/aspnet:8.0` base image
2. Use external SQL Server (cannot use LocalDB in container)
3. Update connection string to point to container-accessible SQL Server

---

## 11. Support and Resources

### 11.1 Documentation
- [ASP.NET Core Documentation](https://learn.microsoft.com/en-us/aspnet/core/)
- [Entity Framework Core Documentation](https://learn.microsoft.com/en-us/ef/core/)
- [SQL Server LocalDB Documentation](https://learn.microsoft.com/en-us/sql/database-engine/configure-windows/sql-server-express-localdb)

### 11.2 Project-Specific Documentation
- `/docs/HLD.md` - High-Level Design
- `/docs/LLD.md` - Low-Level Design
- `/docs/ADR/` - Architecture Decision Records
- `/README.md` - Project overview
- `/docs/Migration-Plan.md` - Microservices migration strategy
- `/docs/Target-Architecture.md` - Target microservices architecture
- `/ProductService/README.md` - Product Service documentation

---

## 12. Microservices

### 12.1 Product Service

**Status**: Phase 1 - In Development

The Product Service is the first microservice extracted from the monolith as part of our migration to microservices architecture.

#### 12.1.1 Running Product Service Locally

```bash
cd ProductService
dotnet run
```

The service will be available at `http://localhost:5000` (or the port specified in launchSettings.json)

#### 12.1.2 Product Service Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/products` | GET | List all active products |
| `/api/products/{id}` | GET | Get product by ID |
| `/api/products/sku/{sku}` | GET | Get product by SKU |
| `/health` | GET | Health check |

#### 12.1.3 Product Service Configuration

**Database**: Separate database (`ProductServiceDB`) from the monolith

**Connection String** (appsettings.json):
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=ProductServiceDB;Trusted_Connection=True;MultipleActiveResultSets=true"
}
```

**Redis** (optional for caching):
```json
"ConnectionStrings": {
  "Redis": "localhost:6379"
}
```

#### 12.1.4 Running Product Service Tests

```bash
cd ProductService.Tests
dotnet test
```

#### 12.1.5 Product Service Docker

**Build Image**:
```bash
cd ProductService
docker build -t product-service:latest .
```

**Run Container**:
```bash
docker run -p 8080:8080 product-service:latest
```

#### 12.1.6 Product Service Kubernetes

**Deploy**:
```bash
cd ProductService
kubectl apply -f k8s-deployment.yaml
```

**Check Status**:
```bash
kubectl get pods -l app=product-service
kubectl get services product-service
```

**Access Service**:
```bash
# Get external IP (if LoadBalancer type)
kubectl get service product-service
# Or use port-forward for local access
kubectl port-forward service/product-service 8080:80
```

#### 12.1.7 Monitoring Product Service

**Health Check**:
```bash
curl http://localhost:8080/health
```

**View Logs**:
```bash
# Local
dotnet run --verbosity detailed

# Docker
docker logs <container-id>

# Kubernetes
kubectl logs -l app=product-service -f
```

**Key Metrics to Monitor**:
- Request rate (requests/minute)
- Error rate (% of 5xx responses)
- Response time (p50, p95, p99)
- Cache hit ratio (%)
- Database connection pool utilization

---

## 13. Quick Reference

### 13.1 Essential Commands

**Monolith**:
| Task | Command |
|------|---------|
| Run application | `dotnet run` |
| Run with auto-reload | `dotnet watch run` |
| Build application | `dotnet build` |
| Reset database | `dotnet ef database drop --force && dotnet run` |
| Create migration | `dotnet ef migrations add MigrationName` |
| View migrations | `dotnet ef migrations list` |
| Trust dev certificate | `dotnet dev-certs https --trust` |

**Product Service**:
| Task | Command |
|------|---------|
| Run service | `cd ProductService && dotnet run` |
| Run tests | `cd ProductService.Tests && dotnet test` |
| Create migration | `cd ProductService && dotnet ef migrations add MigrationName` |
| Build Docker image | `cd ProductService && docker build -t product-service:latest .` |
| Deploy to K8s | `cd ProductService && kubectl apply -f k8s-deployment.yaml` |

### 13.2 Default Credentials
- **Customer ID**: `"guest"` (hardcoded, no login required)
- **Database**: Windows Authentication (LocalDB)
- **Payment Token**: Any value (mock gateway always succeeds)

---

## Appendix: Sample Data

After seeding, the database contains:
- **50 products** across 6 categories (Apparel, Footwear, Accessories, Electronics, Home, Beauty)
- **Prices**: £5 - £105 (GBP)
- **Inventory**: 10-200 units per product
- **SKU format**: `SKU-0001` through `SKU-0050`
