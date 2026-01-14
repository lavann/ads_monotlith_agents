# Documentation - Retail Monolith Application

This directory contains comprehensive documentation for the Retail Monolith application.

## Document Overview

### [HLD.md](HLD.md) - High-Level Design
**Audience**: Architects, technical leads, stakeholders

**Contents**:
- System overview and architecture style
- Domain boundaries (Products, Cart, Orders, Checkout)
- System components and layers
- Data stores and database schema
- External dependencies
- Runtime assumptions and deployment model
- Key architectural characteristics and limitations

**When to read**: Understanding overall system structure, planning decomposition, onboarding new team members

---

### [LLD.md](LLD.md) - Low-Level Design
**Audience**: Developers, code reviewers

**Contents**:
- Detailed module organization
- Key classes and their responsibilities
- Main request flows with step-by-step breakdowns
- Areas of coupling and hotspots
- Performance issues and code quality concerns
- Technical debt inventory
- Class dependency diagrams

**When to read**: Implementing features, debugging issues, understanding implementation details, code reviews

---

### [Runbook.md](Runbook.md) - Operations Guide
**Audience**: Developers, DevOps, support engineers

**Contents**:
- Prerequisites and setup instructions
- Build and run commands
- Application endpoints and API testing
- Configuration options
- Troubleshooting common issues
- Known bugs and workarounds
- Database inspection queries
- Development workflow

**When to read**: Setting up local environment, deploying application, troubleshooting runtime issues

---

### [ADR/](ADR/) - Architecture Decision Records
**Audience**: Architects, technical leads, developers

**Contents**:
- **[ADR-001: Monolithic Architecture](ADR/ADR-001-monolithic-architecture.md)**
  - Why monolith was chosen over microservices
  - Domain boundaries within the monolith
  - Future migration path to microservices

- **[ADR-002: EF Core with SQL Server LocalDB](ADR/ADR-002-ef-core-sqlserver.md)**
  - Why Entity Framework Core as ORM
  - Why SQL Server for persistence
  - Migration and seeding strategies
  - Performance considerations

- **[ADR-003: Guest-Only Customer Model](ADR/ADR-003-guest-only-customer.md)**
  - Decision to use hardcoded "guest" customer
  - Known limitations (shared cart, no privacy)
  - Future authentication strategy
  - Impact on code and user experience

- **[ADR-004: Synchronous Checkout with Mock Payment](ADR/ADR-004-synchronous-checkout-mock-payment.md)**
  - Synchronous vs asynchronous checkout
  - Mock payment gateway for MVP
  - Critical issues (inventory rollback, idempotency)
  - Alternatives considered (saga pattern, async processing)

**When to read**: Understanding why specific design choices were made, planning refactoring, onboarding architects

---

## Documentation Standards

### Document Structure
All documents follow this structure:
1. **Document Information**: Metadata (last updated, version, etc.)
2. **Overview**: Purpose and scope
3. **Main Content**: Organized by logical sections
4. **Diagrams**: Where applicable (ASCII art for portability)
5. **Future Considerations**: Evolution paths

### ADR Format (following [Michael Nygard's template](https://github.com/joelparkerhenderson/architecture-decision-record))
- **Status**: Accepted | Rejected | Deprecated | Superseded
- **Context**: Problem and constraints
- **Decision**: What was decided
- **Rationale**: Why this decision was made
- **Consequences**: Positive and negative outcomes
- **Alternatives Considered**: Other options and why they were rejected
- **Date**: When the decision was made
- **Participants**: Who was involved

---

## How to Use This Documentation

### For New Team Members
1. Start with **README.md** (project root) for project overview
2. Read **HLD.md** to understand system architecture
3. Read **Runbook.md** to set up local environment
4. Scan **LLD.md** to understand code organization
5. Review **ADR/** documents to understand key decisions

### For Feature Development
1. Check **LLD.md** for affected modules and coupling points
2. Review relevant **ADR** for design constraints
3. Follow patterns in existing code
4. Update documentation if adding new domain or significant changes

### For Troubleshooting
1. Start with **Runbook.md** troubleshooting section
2. Check **LLD.md** for known issues and hotspots
3. Consult **ADR** documents for design rationale that might explain behavior

### For Refactoring / Decomposition
1. Review **HLD.md** domain boundaries
2. Study **LLD.md** coupling analysis
3. Read **ADR-001** for migration path to microservices
4. Identify service extraction candidates from domain modules

---

## Maintaining Documentation

### When to Update

**HLD.md** - Update when:
- Adding/removing major components
- Changing data stores or external dependencies
- Modifying deployment model
- Changing domain boundaries

**LLD.md** - Update when:
- Adding new services or significant classes
- Changing request flows
- Identifying new coupling or hotspots
- Resolving technical debt items

**Runbook.md** - Update when:
- Adding new endpoints
- Changing configuration options
- Discovering new issues/workarounds
- Updating deployment procedures

**ADR/** - Create new ADR when:
- Making architectural decisions (can't be easily reversed)
- Choosing between alternatives with significant trade-offs
- Making decisions that affect multiple modules/domains
- Deferring complexity for MVP (like guest-only model)

### Review Cycle
- Review documentation during code reviews
- Update after each sprint/milestone
- Quarterly review for accuracy and completeness
- Archive or mark as deprecated when superseded

---

## Document Status

| Document | Last Updated | Status | Completeness |
|----------|--------------|--------|--------------|
| HLD.md | 2026-01-14 | Current | Complete |
| LLD.md | 2026-01-14 | Current | Complete |
| Runbook.md | 2026-01-14 | Current | Complete |
| ADR-001 | 2026-01-14 | Accepted | Complete |
| ADR-002 | 2026-01-14 | Accepted | Complete |
| ADR-003 | 2026-01-14 | Accepted | Complete |
| ADR-004 | 2026-01-14 | Accepted | Complete |

---

## Related Documentation

- **[../README.md](../README.md)**: Project overview and quick start
- **Code Comments**: Inline documentation for complex logic
- **Git Commits**: Historical context for changes

---

## Feedback

Documentation improvements are welcome. Consider:
- Is something unclear or missing?
- Have you found inaccuracies?
- Do you need different documentation formats (diagrams, videos)?

Update documentation alongside code changes to keep it current and valuable.
