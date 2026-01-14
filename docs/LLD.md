# Low-Level Design (LLD) - Retail Monolith Application

## Document Information
- **Last Updated**: 2026-01-14
- **Application**: RetailMonolith
- **Version**: 1.0

---

## 1. Module Organization

The application is organized into the following namespaces/modules:

```
RetailMonolith/
├── Data/               # Database context and EF Core configuration
├── Models/             # Domain entities
├── Services/           # Business logic services
├── Pages/              # Razor Pages (UI + page models)
│   ├── Products/
│   ├── Cart/
│   ├── Checkout/
│   └── Orders/
├── Migrations/         # EF Core migrations
└── Program.cs          # Application entry point
```

---

## 2. Key Classes by Module

### 2.1 Data Module (`RetailMonolith.Data`)

#### AppDbContext
**Purpose**: Central EF Core database context managing all entities

**Key Responsibilities**:
- Define DbSet properties for all entities
- Configure entity relationships and constraints
- Provide data seeding logic

**Key Members**:
```csharp
public class AppDbContext : DbContext
{
    public DbSet<Product> Products
    public DbSet<InventoryItem> Inventory
    public DbSet<Cart> Carts
    public DbSet<CartLine> CartLines
    public DbSet<Order> Orders
    public DbSet<OrderLine> OrderLines
    
    protected override void OnModelCreating(ModelBuilder b)
    public static async Task SeedAsync(AppDbContext db)
}
```

**Constraints Configured**:
- Unique index on `Product.Sku`
- Unique index on `InventoryItem.Sku`

**Seeding Behavior**:
- Only seeds if `Products` table is empty
- Creates 50 products with random categories and prices
- Creates corresponding inventory items with random quantities (10-200)

#### DesignTimeDbContextFactory
**Purpose**: Enable EF Core tools to create context at design time

**Key Responsibilities**:
- Provide AppDbContext instance for migrations
- Use hardcoded connection string for design-time operations

---

### 2.2 Models Module (`RetailMonolith.Models`)

#### Product
**Purpose**: Represent product catalog items

**Properties**:
- `int Id` (PK)
- `string Sku` (unique, indexed)
- `string Name`
- `string? Description`
- `decimal Price`
- `string Currency`
- `bool IsActive`
- `string? Category`

#### InventoryItem
**Purpose**: Track stock levels for products

**Properties**:
- `int Id` (PK)
- `string Sku` (unique, indexed)
- `int Quantity`

**Coupling**: Linked to Product via SKU (not enforced FK)

#### Cart
**Purpose**: Represent a customer's shopping cart

**Properties**:
- `int Id` (PK)
- `string CustomerId` (default: "guest")
- `List<CartLine> Lines`

#### CartLine
**Purpose**: Individual line item in a cart

**Properties**:
- `int Id` (PK)
- `int CartId` (FK)
- `Cart? Cart` (navigation property)
- `string Sku`
- `string Name`
- `decimal UnitPrice`
- `int Quantity`

**Note**: Stores denormalized product data (Name, UnitPrice) to capture values at time of addition

#### Order
**Purpose**: Represent a completed or attempted order

**Properties**:
- `int Id` (PK)
- `DateTime CreatedUtc` (default: UtcNow)
- `string CustomerId` (default: "guest")
- `string Status` (Created|Paid|Failed|Shipped)
- `decimal Total`
- `List<OrderLine> Lines`

#### OrderLine
**Purpose**: Individual line item in an order

**Properties**:
- `int Id` (PK)
- `int OrderId` (FK)
- `Order? Order` (navigation property)
- `string Sku`
- `string Name`
- `decimal UnitPrice`
- `int Quantity`

**Note**: Snapshot of cart line at checkout time

---

### 2.3 Services Module (`RetailMonolith.Services`)

#### ICartService / CartService
**Purpose**: Manage shopping cart operations

**Key Methods**:
```csharp
Task<Cart> GetOrCreateCartAsync(string customerId, CancellationToken ct = default)
Task AddToCartAsync(string customerId, int productId, int quantity = 1, CancellationToken ct = default)
Task<Cart> GetCartWithLinesAsync(string customerId, CancellationToken ct = default)
Task ClearCartAsync(string customerId, CancellationToken ct = default)
```

**Dependencies**:
- `AppDbContext` (injected)

**Key Behaviors**:
- Creates cart if it doesn't exist for customer
- Updates quantity if product already in cart
- Uses eager loading for cart lines (`Include(c => c.Lines)`)
- Returns new Cart instance (not persisted) if cart not found in `GetCartWithLinesAsync`

#### ICheckoutService / CheckoutService
**Purpose**: Orchestrate the checkout process

**Key Methods**:
```csharp
Task<Order> CheckoutAsync(string customerId, string paymentToken, CancellationToken ct = default)
```

**Dependencies**:
- `AppDbContext` (injected)
- `IPaymentGateway` (injected)

**Checkout Flow**:
1. Retrieve cart with lines (throws if not found)
2. Calculate total from cart lines
3. Reserve inventory (decrement stock for each line)
4. Process payment via gateway
5. Create order with status based on payment result
6. Clear cart lines
7. Save all changes in single transaction

**Error Handling**:
- Throws `InvalidOperationException` if cart not found
- Throws `InvalidOperationException` if out of stock
- No rollback mechanism on payment failure (inventory already decremented)

#### IPaymentGateway / MockPaymentGateway
**Purpose**: Abstract payment processing

**Key Types**:
```csharp
public record PaymentRequest(decimal Amount, string Currency, string Token)
public record PaymentResult(bool Succeeded, string? ProviderRef, string? Error)
```

**Key Methods**:
```csharp
Task<PaymentResult> ChargeAsync(PaymentRequest req, CancellationToken ct = default)
```

**Mock Behavior**:
- Always returns successful payment
- Generates mock transaction ID: `MOCK-{Guid}`
- No actual payment processing

---

### 2.4 Pages Module (Razor Pages)

#### Pages/Products/Index.cshtml.cs
**Purpose**: Display product catalog and handle add-to-cart

**Key Members**:
```csharp
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ICartService _cartService;
    
    public IList<Product> Products { get; set; }
    
    public async Task OnGetAsync()      // Load active products
    public async Task OnPostAsync(int productId)  // Add to cart
}
```

**OnPostAsync Flow**:
1. Find product by ID
2. Get or create cart for "guest"
3. Manually add CartLine to cart (legacy code)
4. Call `CartService.AddToCartAsync` (duplicate add - bug!)
5. Redirect to /Cart

**⚠️ HOTSPOT**: Duplicate cart line addition logic - adds item twice!

#### Pages/Cart/Index.cshtml.cs
**Purpose**: Display shopping cart contents

**Key Members**:
```csharp
public class IndexModel : PageModel
{
    private readonly ICartService _cartService;
    
    public List<(string Name, int Quantity, decimal Price)> Lines { get; set; }
    public decimal Total => Lines.Sum(line => line.Price * line.Quantity);
    
    public async Task OnGetAsync()      // Load cart for "guest"
}
```

**Note**: Uses tuple projection to avoid exposing domain entities directly

#### Pages/Checkout/Index.cshtml.cs
**Purpose**: Checkout page (not implemented)

**Status**: Placeholder only, no actual logic

#### Pages/Orders/Index.cshtml.cs
**Purpose**: Order history page (not implemented)

**Status**: Placeholder only, no actual logic

---

## 3. Main Request Flows

### 3.1 Browse Products Flow

```
User → GET /Products
    ↓
IndexModel.OnGetAsync()
    ↓
AppDbContext.Products.Where(p => p.IsActive).ToListAsync()
    ↓
Render product list with "Add to Cart" buttons
```

**Database Queries**: 1 query to fetch all active products

---

### 3.2 Add to Cart Flow

```
User → POST /Products (productId parameter)
    ↓
IndexModel.OnPostAsync(productId)
    ↓
1. Find product in DB
2. Get/create cart for "guest"
3. [BUG] Manually add CartLine to cart
4. [BUG] Call CartService.AddToCartAsync (adds again!)
5. SaveChanges
    ↓
Redirect to /Cart
```

**Database Queries**: 
- 1 query to find product
- 1 query to get cart with lines
- 1 query to save changes

**⚠️ ISSUE**: Duplicate addition logic results in double quantity

---

### 3.3 View Cart Flow

```
User → GET /Cart
    ↓
Cart.IndexModel.OnGetAsync()
    ↓
CartService.GetCartWithLinesAsync("guest")
    ↓
Project cart lines to tuple list
    ↓
Render cart with total
```

**Database Queries**: 1 query to fetch cart with lines

---

### 3.4 Checkout Flow (API)

```
External Client → POST /api/checkout
    ↓
CheckoutService.CheckoutAsync("guest", "tok_test")
    ↓
1. Get cart with lines (single query)
2. Calculate total
3. For each cart line:
   - Get inventory item (N queries - N+1 problem!)
   - Check stock availability
   - Decrement quantity
4. Call PaymentGateway.ChargeAsync
5. Create Order with OrderLines
6. Remove CartLines
7. SaveChanges (single transaction)
    ↓
Return { order.Id, order.Status, order.Total }
```

**Database Queries**: 
- 1 query to get cart
- N queries to get inventory items (one per cart line)
- 1 query to save all changes

**⚠️ HOTSPOT**: N+1 query problem in inventory check loop

---

### 3.5 Get Order Flow (API)

```
External Client → GET /api/orders/{id}
    ↓
AppDbContext.Orders
    .Include(o => o.Lines)
    .SingleOrDefaultAsync(o => o.Id == id)
    ↓
Return order JSON or 404
```

**Database Queries**: 1 query with eager loading of order lines

---

## 4. Areas of Coupling

### 4.1 Shared Database
**Description**: All domains (Products, Inventory, Cart, Orders) share single AppDbContext

**Impact**:
- Cannot independently scale domains
- Schema changes affect entire application
- Cannot use different database technologies per domain

**Location**: `Data/AppDbContext.cs`

---

### 4.2 SKU-Based Coupling
**Description**: Product and InventoryItem linked via SKU string, not foreign key

**Impact**:
- No referential integrity enforcement
- Possible orphaned inventory records
- Product deletion doesn't cascade to inventory

**Location**: 
- `Models/Product.cs` (Sku property)
- `Models/InventoryItem.cs` (Sku property)
- `Services/CheckoutService.cs` (line 29: inventory lookup by SKU)

---

### 4.3 Denormalized Cart/Order Data
**Description**: CartLine and OrderLine store product name and price, not just SKU reference

**Impact**:
- Product price changes don't affect existing carts/orders (good for orders, questionable for carts)
- No single source of truth for product data
- Potential data inconsistency

**Location**:
- `Models/Cart.cs` (CartLine properties)
- `Models/Order.cs` (OrderLine properties)

---

### 4.4 Hardcoded Customer ID
**Description**: All operations use "guest" as CustomerId

**Impact**:
- No multi-user support
- Single cart shared across all users
- Cannot track orders by user

**Locations**:
- `Program.cs` (line 53: API checkout)
- `Pages/Products/Index.cshtml.cs` (lines 32, 34, 49)
- `Pages/Cart/Index.cshtml.cs` (line 32)

---

### 4.5 Direct DbContext Usage in Pages
**Description**: Product/Index page model injects AppDbContext directly alongside CartService

**Impact**:
- Business logic in presentation layer
- Inconsistent abstraction (sometimes service, sometimes direct DB)
- Harder to test

**Location**: `Pages/Products/Index.cshtml.cs` (lines 14, 18, 29-40)

---

### 4.6 Checkout Service Orchestration Coupling
**Description**: CheckoutService tightly couples inventory, payment, and order creation

**Impact**:
- Cannot independently scale these operations
- All must succeed in single transaction
- Difficult to add async/event-driven patterns

**Location**: `Services/CheckoutService.cs` (CheckoutAsync method)

---

## 5. Performance Hotspots

### 5.1 N+1 Query in Checkout
**Location**: `CheckoutService.cs`, lines 28-32

**Issue**: Loops through cart lines and queries inventory one at a time

**Current Code**:
```csharp
foreach (var line in cart.Lines)
{
    var inv = await _db.Inventory.SingleAsync(i => i.Sku == line.Sku, ct);
    // ...
}
```

**Impact**: For cart with N items, executes N+1 database queries

**Recommendation**: Load all inventory items in single query before loop

---

### 5.2 Missing Indexes
**Location**: Database schema

**Issue**: No indexes on CartLine.CartId, OrderLine.OrderId (foreign keys)

**Impact**: Slow joins when loading cart/order with lines

**Recommendation**: Add indexes on FK columns (EF Core usually does this, but not explicitly configured)

---

### 5.3 No Caching
**Location**: Throughout application

**Issue**: Product catalog queried on every page load, no caching layer

**Impact**: Unnecessary database load for read-heavy product browsing

**Recommendation**: Add response caching or distributed cache for product catalog

---

### 5.4 Synchronous Checkout
**Location**: `Program.cs` line 51, `CheckoutService.cs`

**Issue**: Checkout is synchronous blocking call

**Impact**: Request thread blocked during payment processing

**Recommendation**: Move to async/queue-based processing for better throughput

---

## 6. Code Quality Issues

### 6.1 Duplicate Cart Add Logic
**Location**: `Pages/Products/Index.cshtml.cs`, OnPostAsync method

**Issue**: Manually builds cart and adds to DB, then calls CartService.AddToCartAsync

**Result**: Product added to cart twice!

**Lines**: 
- 32-48: Manual cart manipulation
- 49: Service call (should be only method used)

---

### 6.2 Unused Code
**Location**: Multiple places

**Examples**:
- `Polly` NuGet package referenced but never used
- Checkout and Orders pages exist but have no functionality
- Health checks registered but no custom health check logic

---

### 6.3 Missing Error Handling
**Location**: API endpoints in `Program.cs`

**Issue**: No try-catch blocks, errors propagate as 500 responses

**Lines**: 51-63 (minimal APIs)

---

### 6.4 No Validation
**Location**: Throughout application

**Issue**: No input validation on:
- Product quantity (could be negative)
- Payment token format
- Order IDs

---

### 6.5 Magic Strings
**Location**: Throughout codebase

**Examples**:
- "guest" customer ID repeated in many places
- "GBP" currency hardcoded
- "Created", "Paid", "Failed", "Shipped" status strings (should be enum)

---

## 7. Testing Considerations

### 7.1 Testability Issues
- Direct DbContext usage in pages makes unit testing harder
- No repository pattern to mock data access
- Payment gateway is mockable (good use of interface)

### 7.2 Integration Test Requirements
Key flows to test:
1. Add product to cart
2. Complete checkout with successful payment
3. Handle out-of-stock scenario
4. Verify inventory decrement

---

## 8. Security Considerations

### 8.1 Current State
- No authentication/authorization
- No input sanitization
- SQL injection protected by EF Core parameterization
- No rate limiting on API endpoints

### 8.2 Future Requirements
- Customer authentication system
- Authorization for order access
- Payment token encryption
- API throttling

---

## 9. Technical Debt Summary

| Issue | Severity | Location | Impact |
|-------|----------|----------|--------|
| Duplicate cart add | High | Products/Index.cshtml.cs | Functionality bug |
| N+1 query in checkout | High | CheckoutService.cs | Performance |
| Hardcoded "guest" customer | High | Multiple files | No multi-user support |
| No inventory rollback on payment failure | Medium | CheckoutService.cs | Data consistency |
| Missing error handling in APIs | Medium | Program.cs | Poor error responses |
| Unused Polly package | Low | Project file | Dependency bloat |
| Placeholder pages | Low | Checkout/Orders pages | Incomplete features |

---

## 10. Class Dependency Diagram

```
Program.cs
    ↓ configures
AppDbContext ← CartService ← Pages/Products/Index
    ↓              ↓              Pages/Cart/Index
    ↓              ↓
    ↓         CheckoutService ← Program.cs (minimal API)
    ↓              ↓
    ↓         IPaymentGateway (MockPaymentGateway)
    ↓
Models (Product, Cart, Order, Inventory)
```
