# ADR-003: Guest-Only Customer Model

## Status
Accepted (with known limitations for MVP)

## Context
The application needs to support shopping cart and order functionality. A complete customer identity system with authentication, authorization, and profile management would add significant complexity and development time.

## Decision
Implement a simplified "guest-only" customer model where all users operate under a single hardcoded customer identifier: `"guest"`. No authentication, no user sessions, no customer profiles.

## Rationale

### Why Guest-Only?
1. **MVP Speed**: Allows rapid development of core e-commerce flows without identity infrastructure
2. **Focus**: Team can focus on domain logic (products, cart, orders) rather than auth
3. **Simplicity**: No session management, no auth middleware, no user database tables
4. **Demo-Friendly**: Easier to demonstrate and test core functionality
5. **Deferred Complexity**: Authentication can be added later without changing domain logic

### Implementation Details
- `CustomerId` field exists on `Cart` and `Order` entities
- Default value: `"guest"` (hardcoded throughout codebase)
- No validation or lookup of customer identity
- Single shared cart for all application users
- No authorization checks on order access

## Consequences

### Positive
- **Fast Development**: Eliminated weeks of auth implementation work
- **Simple Testing**: No need to manage test users or auth tokens
- **Clear Domain Models**: Customer concept exists in data model for future expansion
- **Easy Demo**: Single cart state makes demonstrations straightforward

### Negative
- **Single Shared Cart**: All users share the same cart (major limitation)
- **No Privacy**: Any user can see any order (no order history isolation)
- **No Personalization**: Cannot track customer preferences or history
- **Production Unsuitable**: Cannot be used for real multi-user scenarios
- **Technical Debt**: Hardcoded `"guest"` string scattered throughout codebase

### Code Impact
Hardcoded `"guest"` appears in:
- `Program.cs` (API endpoint, line 53)
- `Pages/Products/Index.cshtml.cs` (lines 32, 34, 49)
- `Pages/Cart/Index.cshtml.cs` (line 32)
- `Models/Cart.cs` (default property value)
- `Models/Order.cs` (default property value)

### Future Migration Path
To add real authentication:
1. Add ASP.NET Core Identity or external auth provider (Auth0, Azure AD B2C)
2. Replace hardcoded `"guest"` with `User.Identity.Name` or authenticated user ID
3. Add authorization policies for cart and order access
4. Add customer profile table with FK to Cart and Order
5. Migrate existing "guest" data or mark as test/sample data

## Alternatives Considered

### ASP.NET Core Identity
- **Pros**: Built-in, well-supported, comprehensive user management
- **Cons**: Significant complexity (user tables, password hashing, email confirmation, etc.)
- **Decision**: Rejected for MVP to accelerate development

### Anonymous Session-Based Carts
- **Pros**: Each browser session gets own cart, better than single shared cart
- **Cons**: Still no persistent identity across sessions/devices
- **Decision**: Rejected as still requires session management without solving authentication

### External Auth Provider (Auth0, Azure AD B2C)
- **Pros**: Offloads auth complexity, modern patterns (OAuth, OIDC)
- **Cons**: External dependency, setup required, learning curve
- **Decision**: Rejected for MVP but recommended for future

## Known Issues

### Issue: Shared Cart State
- **Description**: All users see and modify the same cart
- **Impact**: High - makes multi-user usage impossible
- **Workaround**: Application is demonstration/development only
- **Resolution**: Required for production - add proper authentication

### Issue: No Order Privacy
- **Description**: `/api/orders/{id}` returns any order without authorization check
- **Impact**: High - potential data leak in multi-user scenario
- **Workaround**: Application not exposed to multiple users
- **Resolution**: Add authorization filter checking order.CustomerId == User.Identity.Name

### Issue: Magic String Scattered Throughout Code
- **Description**: `"guest"` string repeated in multiple files
- **Impact**: Medium - hard to refactor, potential for typos
- **Workaround**: Consistent usage so far
- **Resolution**: Extract to constant or configuration value

## Date
2025-10-19 (inferred from initial implementation)

## Participants
Development Team

## Notes
This decision is explicitly marked as **temporary for MVP/demo purposes**. Authentication and proper customer identity management are recognized as necessary for any production deployment.
