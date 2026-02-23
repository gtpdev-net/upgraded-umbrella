# Source Database Catalogue Web Application — Specification

## 1. Purpose

This document specifies the **Source Database Catalogue**, a web application for cataloguing source SQL Server databases and recording the intent attributed to each datapoint (column) within them.

The application serves two distinct functions:

1. **Schema cataloguing** — Populating and maintaining a structured inventory of source servers, databases, tables, and columns. Schema structure is imported from DACPAC files extracted from the source databases.
2. **Intent recording** — For each catalogued column, capturing the decisions made about it during analysis and solution design:
   - Was it identified during the DAO analysis?
   - Was it added into scope when building the API?
   - Does its data fit a relational (`R`) or document (`D`) structure?
   - Is it selected to be loaded and used in the target system?

The catalogue is the authoritative record of what the source databases contain and what has been decided about each datapoint.

---

## 2. Scope

| In scope | Out of scope |
|---|---|
| Full CRUD for the catalogue (servers, databases, tables, columns) | Executing or scheduling data movement jobs |
| DACPAC import to populate schema structure from source databases | Monitoring pipeline execution |
| Filtering, searching, and sorting the catalogue | Schema comparison or drift detection |
| Per-column intent flag management (DAO analysis, API scope, persistence type, load selection) | Azure resource provisioning |
| Basic audit trail (created/modified timestamps and user) | Row-level security between teams |
| Excel workbook import (one-time migration from legacy catalogue) | |

---

## 3. Technology Stack

| Layer | Choice | Rationale |
|---|---|---|
| Framework | ASP.NET Core 8 — Blazor Server | Single codebase; no separate API layer needed for an internal admin tool |
| ORM | Entity Framework Core 8 | Code-first migrations; strongly typed queries |
| Database | Azure SQL | Managed, scalable; can share a logical SQL server with downstream systems |
| Authentication | Microsoft Entra ID (Azure AD) via `Microsoft.Identity.Web` | Organisation SSO; no separate user store |
| Hosting | Azure App Service (Linux, B2 or higher) | Simple deployment; managed TLS |
| Styling | Bootstrap 5 (included with Blazor template) | Low overhead; sufficient for an internal admin tool |

---

## 4. Solution Structure

```
/src
  /Catalogue.Core                   ← Shared class library
    /Models                         ← EF Core entity classes
    /Interfaces
      ICatalogueRepository.cs       ← Abstraction for reading catalogue data
    /DTOs                           ← View/transfer models (separate from EF entities)
    /Validation                     ← FluentValidation rules

  /Catalogue.Infrastructure         ← EF Core, migrations, repository implementations
    CatalogueDbContext.cs
    /Repositories
    /Migrations

  /Catalogue.Web                    ← Blazor Server application
    /Components
      /Shared                       ← Layout, NavMenu, ConfirmDialog, Toast
      /Servers                      ← Server list and edit pages
      /Databases                    ← Database list and edit pages
      /Tables                       ← Table list, edit, and intent-flag pages
      /Columns                      ← Column management per table
      /Import                       ← DACPAC and Excel import components
    /Pages                          ← Top-level routable pages
    Program.cs
    appsettings.json
```

---

## 5. Database Schema

### 5.1 Entity Relationship Overview

```
Sources ──< SourceDatabases ──< SourceTables ──< SourceColumns
```

### 5.2 DDL

```sql
CREATE TABLE Sources (
    SourceId      INT           IDENTITY(1,1) PRIMARY KEY,
    ServerName    NVARCHAR(255) NOT NULL,
    Description   NVARCHAR(500) NULL,
    IsActive      BIT           NOT NULL DEFAULT 1,
    CreatedAt     DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy     NVARCHAR(255) NOT NULL,
    ModifiedAt    DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    ModifiedBy    NVARCHAR(255) NOT NULL,
    CONSTRAINT UQ_Sources_ServerName UNIQUE (ServerName)
);

CREATE TABLE SourceDatabases (
    DatabaseId    INT           IDENTITY(1,1) PRIMARY KEY,
    SourceId      INT           NOT NULL REFERENCES Sources(SourceId) ON DELETE CASCADE,
    DatabaseName  NVARCHAR(255) NOT NULL,
    Description   NVARCHAR(500) NULL,
    IsActive      BIT           NOT NULL DEFAULT 1,
    CreatedAt     DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy     NVARCHAR(255) NOT NULL,
    ModifiedAt    DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    ModifiedBy    NVARCHAR(255) NOT NULL,
    CONSTRAINT UQ_SourceDatabases_ServerDb UNIQUE (SourceId, DatabaseName)
);

CREATE TABLE SourceTables (
    TableId             INT           IDENTITY(1,1) PRIMARY KEY,
    DatabaseId          INT           NOT NULL REFERENCES SourceDatabases(DatabaseId) ON DELETE CASCADE,
    SchemaName          NVARCHAR(128) NOT NULL DEFAULT 'dbo',
    TableName           NVARCHAR(255) NOT NULL,
    EstimatedRowCount   BIGINT        NULL,
    Notes               NVARCHAR(1000) NULL,
    IsActive            BIT           NOT NULL DEFAULT 1,
    CreatedAt           DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy           NVARCHAR(255) NOT NULL,
    ModifiedAt          DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    ModifiedBy          NVARCHAR(255) NOT NULL,
    CONSTRAINT UQ_SourceTables_SchemaTable UNIQUE (DatabaseId, SchemaName, TableName)
);

CREATE TABLE SourceColumns (
    ColumnId           INT           IDENTITY(1,1) PRIMARY KEY,
    TableId            INT           NOT NULL REFERENCES SourceTables(TableId) ON DELETE CASCADE,
    ColumnName         NVARCHAR(255) NOT NULL,
    PersistenceType    CHAR(1)       NOT NULL DEFAULT 'R'
                         CONSTRAINT CK_SourceColumns_PersistenceType CHECK (PersistenceType IN ('R','D')),
    IsInDaoAnalysis    BIT           NOT NULL DEFAULT 0,
    IsAddedByApi       BIT           NOT NULL DEFAULT 0,
    IsSelectedForLoad  BIT           NOT NULL DEFAULT 0,
    SortOrder          INT           NOT NULL DEFAULT 0,
    CreatedAt          DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy          NVARCHAR(255) NOT NULL,
    ModifiedAt         DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    ModifiedBy         NVARCHAR(255) NOT NULL,
    CONSTRAINT UQ_SourceColumns_TableColumn UNIQUE (TableId, ColumnName)
);
```

### 5.3 Convenience View — In-Scope Relational Columns

```sql
-- Returns all active columns that are in scope (identified via DAO analysis or API)
-- and have a Relational persistence type. Useful for downstream consumers
-- that need to query which columns have been confirmed for relational load.
CREATE VIEW vw_InScopeRelationalColumns AS
SELECT
    s.ServerName,
    d.DatabaseName,
    t.SchemaName,
    t.TableName,
    t.EstimatedRowCount,
    c.ColumnName,
    c.PersistenceType,
    c.IsInDaoAnalysis,
    c.IsAddedByApi,
    c.IsSelectedForLoad,
    c.SortOrder
FROM   SourceColumns c
JOIN   SourceTables    t ON t.TableId    = c.TableId    AND t.IsActive = 1
JOIN   SourceDatabases d ON d.DatabaseId = t.DatabaseId AND d.IsActive = 1
JOIN   Sources         s ON s.SourceId   = d.SourceId   AND s.IsActive = 1
WHERE  (c.IsInDaoAnalysis = 1 OR c.IsAddedByApi = 1)
AND    c.PersistenceType = 'R';
```

---

## 6. Application Pages & Components

### 6.1 Navigation Structure

```
/ (Dashboard)
/catalogue                          ← Global flat view of all data
/servers
/servers/{id}
/servers/{id}/databases
/servers/{id}/databases/{dbId}
/servers/{id}/databases/{dbId}/tables
/servers/{id}/databases/{dbId}/tables/{tableId}
/import
```

---

### 6.2 Global Catalogue View (`/catalogue`)

**Purpose:** A single flat table presenting every column in the catalogue across all servers, databases, tables, and schemas — with all structural and intent attributes visible simultaneously. This is the primary read and bulk-edit surface for reviewing and updating intent across the entire source estate.

**Features:**
- Single paginated, sortable, filterable table — no hierarchy navigation required
- **Inline intent flag editing** directly in the table row (no need to navigate to table detail)
- **Column-level bulk edit**: select multiple rows, apply intent flags in one action
- Export to CSV (current filtered view)
- Persistent filter/sort state (stored in URL query string so views can be bookmarked and shared)

**Columns displayed:**

| Column | Sortable | Filterable | Inline Edit |
|---|---|---|---|
| Server | ✓ | ✓ (multi-select) | |
| Database | ✓ | ✓ (multi-select) | |
| Schema | ✓ | ✓ | |
| Table | ✓ | ✓ | |
| Column | ✓ | ✓ | |
| In DAO Analysis | ✓ | ✓ (Yes / No / Any) | ✓ checkbox |
| Added by API | ✓ | ✓ (Yes / No / Any) | ✓ checkbox |
| Persistence Type | ✓ | ✓ (R / D / Any) | ✓ radio |
| Selected for Load | ✓ | ✓ (Yes / No / Any) | ✓ checkbox |
| Last Modified | ✓ | | |
| Modified By | | | |

**Bulk Actions (on multi-select):**
- Set `IsInDaoAnalysis` = true / false
- Set `IsAddedByApi` = true / false
- Set `PersistenceType` = R / D
- Set `IsSelectedForLoad` = true / false

**Preset filter shortcuts (accessible from the filter bar):**

| Shortcut | Filter applied |
|---|---|
| Unreviewed | All intent flags at default (no flags set, `PersistenceType = 'R'` default) |
| In Scope | `(IsInDaoAnalysis OR IsAddedByApi) AND PersistenceType = 'R'` |
| Selected for Load | `IsSelectedForLoad = true` |
| Document Columns | `PersistenceType = 'D'` |
| DAO Only | `IsInDaoAnalysis = true AND IsAddedByApi = false` |
| API Added | `IsAddedByApi = true` |

**Export:**
- "Export CSV" button exports all rows matching the current filter (not just the current page)
- CSV columns match the displayed table columns

---


**Purpose:** At-a-glance summary of the catalogue state.

**Displays:**
- Total counts: servers / databases / tables / columns
- Columns in scope: count where `(IsInDaoAnalysis OR IsAddedByApi) AND PersistenceType = 'R'`
- Columns selected for load: count where `IsSelectedForLoad = true`
- Breakdown by `PersistenceType` (R vs D) across all columns
- Columns with no intent flags set (unreviewed columns)
- Last-modified timestamp and user (most recent change across any entity)

---

### 6.4 Servers List (`/servers`)

**Purpose:** View and manage source SQL Server entries.

**Features:**
- Paginated, sortable table: `ServerName`, `# Databases`, `Active`, `Modified`
- Inline `IsActive` toggle
- Add New / Edit / Delete (delete disabled if child databases exist — deactivate instead)
- Search / filter by server name

**Columns displayed:**

| Column | Sortable | Filterable |
|---|---|---|
| Server Name | ✓ | ✓ |
| Description | | |
| Databases | ✓ | |
| Active | ✓ | ✓ |
| Last Modified | ✓ | |
| Actions | | |

---

### 6.5 Server Detail / Edit (`/servers/{id}`)

**Fields:**
- `ServerName` — required, max 255, unique
- `Description` — optional, max 500
- `IsActive` — checkbox

**Validation:**
- `ServerName` must be unique across all servers
- Cannot delete a server with child databases; must deactivate

---

### 6.6 Databases List (`/servers/{id}/databases`)

**Purpose:** Manage databases registered under a server.

**Features:**
- Breadcrumb: `Servers > {ServerName}`
- Paginated table: `DatabaseName`, `# Tables`, `# In-Scope Columns`, `# Selected for Load`, `Active`, `Modified`
- Add New / Edit / Delete (same deactivate-instead-of-delete rule)
- Quick link to Table list per database

---

### 6.7 Database Detail / Edit (`/servers/{id}/databases/{dbId}`)

**Fields:**
- `DatabaseName` — required, max 255, unique per server
- `Description` — optional
- `IsActive` — checkbox

---

### 6.8 Tables List (`/servers/{id}/databases/{dbId}/tables`)

**Purpose:** View tables within a database and navigate to per-column intent recording.

**Features:**
- Breadcrumb: `Servers > {ServerName} > {DatabaseName}`
- Paginated, sortable, filterable table
- At-a-glance intent coverage indicators per table
- Inline `IsActive` toggle per row
- Column count badge with link to column detail
- Filter chips: "Has In-Scope Columns", "Has Unreviewed Columns", "Has Load-Selected Columns", `PersistenceType` mix (Any R / Any D / All R / All D)

**Columns displayed:**

| Column | Sortable | Filterable | Notes |
|---|---|---|---|
| Schema | ✓ | ✓ | |
| Table Name | ✓ | ✓ | |
| Est. Rows | ✓ | | |
| In Scope | ✓ | | Count where `(IsInDaoAnalysis OR IsAddedByApi) AND PersistenceType='R'` |
| Selected for Load | ✓ | | Count where `IsSelectedForLoad=1` |
| Unreviewed | ✓ | | Count where all intent flags are default/unset |
| Columns (R) | ✓ | | Count where `PersistenceType='R'` |
| Columns (D) | ✓ | | Count where `PersistenceType='D'` |
| Active | ✓ | ✓ | |
| Last Modified | ✓ | | |
| Actions | | | |

**Bulk Actions (on multi-select):**
- Set `IsActive` = true / false

> All intent flags (`IsInDaoAnalysis`, `IsAddedByApi`, `IsSelectedForLoad`, `PersistenceType`) are managed per column on the Table Detail page.

---

### 6.9 Table Detail / Edit (`/servers/{id}/databases/{dbId}/tables/{tableId}`)

**Fields:**

| Field | Control | Validation |
|---|---|---|
| Schema Name | Text input | Required, max 128 |
| Table Name | Text input | Required, max 255, unique per database+schema |
| Estimated Row Count | Number input | Optional, ≥ 0 |
| Notes | Textarea | Optional, max 1000 |
| Active | Checkbox | |

**Column sub-panel** (inline on this page):
- List of columns with drag-to-reorder (sets `SortOrder`)
- Per-column fields:
  | Field | Control | Description |
  |---|---|---|
  | Column Name | Text input (read-only if imported) | Required, max 255, unique per table |
  | In DAO Analysis | Checkbox | Was this column identified during the DAO analysis? |
  | Added by API | Checkbox | Was this column added into scope when building the API? |
  | Persistence Type | Radio (R / D) | Does this column's data fit a Relational or Document structure? |
  | Selected for Load | Checkbox | Is this column selected to be loaded and used in the target system? |
- Add column (inline form + Add button)
- Delete column (with confirmation if it is the last in-scope column on an active table)
- Paste multiple columns (newline-separated textarea → parse and bulk-add; intent flags all default to unset, editable after import)
- Visual summary badge for the table (e.g. `4 in scope · 2 selected for load · 3 R · 1 D · 5 unreviewed`)

---

### 6.10 Schema Import (`/import`)

**Purpose:** Populate or refresh the catalogue's schema structure (servers, databases, tables, columns) from source databases. Two import modes are supported.

#### Mode A — DACPAC Import (primary)

DACPAC files contain the complete schema of a source database extracted by `sqlpackage`. This is the primary mechanism for populating the catalogue.

**Features:**
- File upload control (`.dacpac` only)
- Server / Database name entry (or auto-detected from DACPAC metadata)
- Preview of tables and columns parsed from the DACPAC before commit
- Conflict strategy selector: `Skip existing` | `Add new only` | `Full sync (remove columns no longer present)`
- Dry-run toggle — shows what would be inserted/updated/removed without writing
- Import result summary: tables added / columns added / columns removed / skipped / errors

**Parsing rules:**
- Extracts `<Element Type="SqlSimpleColumn">` entries from the DACPAC model
- Creates `SourceTables` entries on first encounter; adds `SourceColumns` for each column
- All intent flags (`IsInDaoAnalysis`, `IsAddedByApi`, `PersistenceType`, `IsSelectedForLoad`) default to unset for newly imported columns — intent is recorded separately via the UI
- Existing intent flag values are preserved on re-import unless `Full sync` is selected with explicit override

#### Mode B — Excel Workbook Import (legacy migration)

**Purpose:** One-time migration from the legacy `AllDatabasesTables.xlsx` workbook, which includes both schema structure and previously recorded intent flags.

**Features:**
- File upload control (`.xlsx` only)
- Preview table showing parsed rows before commit
- Column mapping confirmation (auto-maps known column names, flags any mismatches)
- All four column-level intent flags (`In DAO Analysis`, `Added by API`, `Persistence Type`, `Selected for Load`) are read from the workbook row and stored against each `SourceColumns` record
- Conflict strategy selector: `Skip existing` | `Overwrite existing` | `Merge intent flags only`
- Dry-run toggle
- Import result summary: rows added / updated / skipped / errors

**Parsing rules:**
- Reads sheet named `All`
- Groups rows by `(Server, Database, Schema, Table)` — creates parent entities on first encounter
- Treats empty `Column` values as a warning (table registered but no columns)
- `TRUE`/`FALSE` boolean cells accepted; also `1`/`0` and `Yes`/`No`
- Legacy `Generate SQL INSERTS` column maps to `IsSelectedForLoad`

---

## 7. Shared UI Components

| Component | Purpose |
|---|---|
| `ConfirmDialog` | Modal confirmation for destructive actions (delete, bulk overwrite) |
| `Toast` / `NotificationService` | Non-blocking success / error / warning messages |
| `BreadcrumbNav` | Consistent breadcrumb reflecting current hierarchy position |
| `PaginatedTable<T>` | Generic sortable/paginated table with slot-based column definitions |
| `FlagBadge` | Coloured badge for boolean flags (green tick / grey dash) |
| `PersistenceTypeBadge` | `R` in blue, `D` in amber — used on individual column rows |
| `AuditFooter` | Displays `Created by … on … / Modified by … on …` on detail pages |

---

## 8. Authentication & Authorisation

- **Provider:** Microsoft Entra ID (Azure AD) via `Microsoft.Identity.Web`
- **Flow:** OpenID Connect; redirect-to-login on unauthenticated access
- **Roles** (defined as App Roles in the Entra app registration):

| Role | Permissions |
|---|---|
| `Catalogue.Reader` | View all pages; no create/edit/delete |
| `Catalogue.Editor` | Full CRUD on all entities; run import |
| `Catalogue.Admin` | Editor permissions + manage app configuration |

- Audit fields (`CreatedBy`, `ModifiedBy`) are populated from `HttpContext.User.Identity.Name`.
- No anonymous access to any route.

---

## 9. Consumer Interface — `ICatalogueRepository`

Downstream tools (such as a data movement pipeline) that need to query the catalogue's recorded intent can depend on this interface rather than coupling directly to the database schema:

```csharp
public interface ICatalogueRepository
{
    // Returns all active (Server, Database) pairs that have at least one in-scope
    // relational column (IsInDaoAnalysis or IsAddedByApi, PersistenceType = 'R')
    Task<IEnumerable<SourceDatabaseInfo>> GetInScopeDatabasesAsync(CancellationToken ct = default);

    // Returns all in-scope tables for a given database
    Task<IEnumerable<SourceTableInfo>> GetInScopeTablesAsync(int databaseId, CancellationToken ct = default);

    // Returns columns for a specific table, optionally filtered by intent flags
    Task<IEnumerable<SourceColumnInfo>> GetColumnsAsync(int tableId, ColumnFilter filter = ColumnFilter.InScopeRelational, CancellationToken ct = default);
}

public enum ColumnFilter
{
    All,                // All active columns regardless of intent
    InScopeRelational,  // (IsInDaoAnalysis OR IsAddedByApi) AND PersistenceType = 'R'
    InScopeDocument,    // (IsInDaoAnalysis OR IsAddedByApi) AND PersistenceType = 'D'
    SelectedForLoad     // IsSelectedForLoad = true
}
```

- The SQL-backed implementation (`EfCatalogueRepository`) is provided by `Catalogue.Infrastructure`.
- Unit tests use an in-memory stub.
- Consumers do not need knowledge of the catalogue's internal schema or intent flag semantics.

---

## 10. Non-Functional Requirements

| Requirement | Target |
|---|---|
| Page load time | < 1 s for lists up to 1 000 rows (server-side pagination) |
| Concurrent users | 10–20 (internal admin tool) |
| Availability | Azure App Service standard SLA (99.95 %) |
| Data retention | Soft-delete via `IsActive` flag; no hard deletes on parent entities with children |
| Audit | All create/modify operations record user UPN and UTC timestamp |
| TLS | Enforced end-to-end; HTTP redirected to HTTPS |
| Secrets | Connection strings stored in Azure Key Vault; referenced via App Service managed identity |
| CORS | Not applicable (Blazor Server; no public API) |

---

## 11. Deployment

### Azure Resources Required

| Resource | SKU / Notes |
|---|---|
| App Service Plan | B2 Linux minimum |
| App Service | .NET 8 runtime stack |
| Azure SQL Database | General Purpose S2 minimum |
| Azure Key Vault | Stores connection string; web app uses managed identity |
| Entra App Registration | For OIDC authentication; expose `Catalogue.Reader/Editor/Admin` app roles |

### CI/CD (GitHub Actions suggested)

```
on: push to main
  → dotnet build & test
  → dotnet publish
  → az webapp deploy
  → dotnet ef database update (migrations)
```

---

## 12. Relationship to the Batch-Handling Pipeline

The Source Database Catalogue is an independent application. It does not execute, schedule, or monitor any data movement. Its relationship to the batch-handling pipeline is as a **data source**: the pipeline reads recorded intent from the catalogue rather than from an Excel file.

| Pipeline concern | Catalogue's role |
|---|---|
| Which databases to analyse | Provides active `(Server, Database)` pairs via `ICatalogueRepository` |
| Which tables to process | Provides in-scope tables filtered by intent flags |
| Which columns to extract | Provides the column list per table, filtered as required |
| Batch sizing and strategy | Not held in the catalogue — determined by the pipeline's analysis stage |
| Execution, scheduling, monitoring | Entirely outside the catalogue's scope |
