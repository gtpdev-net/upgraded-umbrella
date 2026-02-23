# Source Database Catalogue — Implementation Plan

**TL;DR:** Build a Blazor Server 8 application in three projects (`Catalogue.Core`, `Catalogue.Infrastructure`, `Catalogue.Web`) targeting Azure SQL, secured via Microsoft Entra ID. Work is divided into seven sequential phases: solution scaffolding → data layer → repository → Blazor shell + auth → catalogue pages (servers → databases → tables → columns → global view → dashboard) → import features (DACPAC + Excel) → deployment / CI-CD. Each phase produces a runnable, deployable increment.

---

## Phase 1 — Solution Scaffolding

1. At the repo root, generate a `global.json` to pin the SDK to .NET 8 and prevent accidental use of any other installed SDK version:
   ```bash
   dotnet new globaljson --sdk-version 8.0.418 --roll-forward latestPatch
   ```
2. Create solution file `Catalogue.sln` and three projects under `src/`:
   - `Catalogue.Core` — class library (net8.0)
   - `Catalogue.Infrastructure` — class library (net8.0)
   - `Catalogue.Web` — Blazor Server app (net8.0)
3. Add project references: `Web → Infrastructure → Core`.
4. Add NuGet packages:
   - `Core`: `FluentValidation`, `FluentValidation.DependencyInjectionExtensions`
   - `Infrastructure`: `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Design`, `ClosedXML` (Excel), `System.IO.Packaging` (DACPAC zip reading)
   - `Web`: `Microsoft.Identity.Web`, `Microsoft.Identity.Web.UI`, `Microsoft.AspNetCore.Authentication.OpenIdConnect`
5. Add `.gitignore` entries for `*.dacpac`, `*.xlsx` test artefacts, EF migrations bundles.

---

## Phase 2 — Core Models, DTOs, Interfaces

Located in `src/Catalogue.Core/`.

6. Create EF entity classes in `Models/`:
   - `Source` — `SourceId`, `ServerName`, `Description`, `IsActive`, `CreatedAt`, `CreatedBy`, `ModifiedAt`, `ModifiedBy`
   - `SourceDatabase` — `DatabaseId`, `SourceId`, `DatabaseName`, `Description`, `IsActive`, audit fields
   - `SourceTable` — `TableId`, `DatabaseId`, `SchemaName`, `TableName`, `EstimatedRowCount`, `Notes`, `IsActive`, audit fields
   - `SourceColumn` — `ColumnId`, `TableId`, `ColumnName`, `PersistenceType` (char), `IsInDaoAnalysis`, `IsAddedByApi`, `IsSelectedForLoad`, `SortOrder`, audit fields
7. Create DTOs in `DTOs/`: `SourceDatabaseInfo`, `SourceTableInfo`, `SourceColumnInfo`, `CatalogueSummaryDto`, `ImportPreviewRow`, `ImportResultDto`.
8. Create `Interfaces/ICatalogueRepository.cs` with `GetInScopeDatabasesAsync`, `GetInScopeTablesAsync`, `GetColumnsAsync` and the `ColumnFilter` enum per spec §9:
   ```csharp
   public enum ColumnFilter
   {
       All,                // All active columns regardless of intent
       InScopeRelational,  // (IsInDaoAnalysis OR IsAddedByApi) AND PersistenceType = 'R'
       InScopeDocument,    // (IsInDaoAnalysis OR IsAddedByApi) AND PersistenceType = 'D'
       SelectedForLoad     // IsSelectedForLoad = true
   }
   ```
9. Create FluentValidation validators in `Validation/` for `Source`, `SourceDatabase`, `SourceTable`, `SourceColumn` (enforce max-length and uniqueness-at-service-layer constraints).

---

## Phase 3 — Infrastructure: DbContext, Migrations, Repositories

Located in `src/Catalogue.Infrastructure/`.

10. Implement `CatalogueDbContext` with `DbSet<>` for all four entities; configure:
   - Unique indexes matching spec DDL constraints (`UQ_Sources_ServerName`, `UQ_SourceDatabases_ServerDb`, `UQ_SourceTables_SchemaTable`, `UQ_SourceColumns_TableColumn`)
   - `PersistenceType` value conversion + `CK_SourceColumns_PersistenceType` check constraint (`'R'` / `'D'`)
   - Default values for `IsActive`, `PersistenceType` (`'R'`), `SortOrder`
   - `CreatedAt` / `ModifiedAt` auto-set via `SaveChangesAsync` override using injected `ICurrentUserService`
11. Define the `vw_InScopeRelationalColumns` view as a Keyless Entity and configure it in `OnModelCreating`.
12. Run `dotnet ef migrations add InitialCreate` to generate the `Migrations/` folder.
13. Implement `EfCatalogueRepository` in `Repositories/` satisfying `ICatalogueRepository`.
14. Implement `ICurrentUserService` / `HttpContextCurrentUserService` (resolves `HttpContext.User.Identity.Name` for audit fields).
15. Register `CatalogueDbContext`, `EfCatalogueRepository`, `HttpContextCurrentUserService` in an `InfrastructureServiceExtensions.AddInfrastructure()` extension method.

---

## Phase 4 — Blazor Shell, Auth, Shared Components

Located in `src/Catalogue.Web/`.

16. Configure `Program.cs`:
    - `AddMicrosoftIdentityWebApp` with `AzureAd` config section
    - Role-based authorisation policies: `RequireRole("Catalogue.Reader")` as baseline; `Catalogue.Editor` required for all write actions; `Catalogue.Admin` required for configuration management
    - `AddInfrastructure(...)` call
    - `AddFluentValidationAutoValidation()`
    - Connection string from `appsettings.json` (dev); Key Vault reference in production via managed identity
    - HTTPS redirection enforced; no anonymous access to any route
17. Build shared components in `Components/Shared/`:
    - `MainLayout.razor` / `NavMenu.razor` — Bootstrap 5 sidebar; highlight active nav item
    - `BreadcrumbNav.razor` — accepts `IEnumerable<BreadcrumbItem>` parameter; renders consistent hierarchy trail
    - `ConfirmDialog.razor` — Bootstrap modal; exposes `ShowAsync(string message)` returning `Task<bool>`
    - `Toast.razor` + `NotificationService.cs` — scoped service with `Success` / `Error` / `Warning` methods; rendered in `MainLayout`
    - `PaginatedTable<T>.razor` — generic; accepts `IQueryable<T>` or `IEnumerable<T>`, `PageSize`, column definitions via `RenderFragment`; emits sort/page events
    - `FlagBadge.razor` — green tick / grey dash based on `bool` parameter
    - `PersistenceTypeBadge.razor` — blue "R" / amber "D"
    - `AuditFooter.razor` — displays `Created by … on … / Modified by … on …` on detail pages

---

## Phase 5 — Catalogue Pages

### 5a — Servers (`/servers`, `/servers/{id}`)

18. `Servers/ServerList.razor` — paginated sortable table with columns per spec §6.4 (`ServerName`, description, `# Databases`, `Active`, `Last Modified`, Actions); inline `IsActive` toggle; Add New / Edit / Delete with `ConfirmDialog` guard (delete blocked if child databases exist — deactivate instead); search/filter by server name.
19. `Servers/ServerEdit.razor` — form with FluentValidation; `ServerName` uniqueness check on save; breadcrumb.

### 5b — Databases (`/servers/{id}/databases`, `/servers/{id}/databases/{dbId}`)

20. `Databases/DatabaseList.razor` — breadcrumb (`Servers > {ServerName}`); paginated table per spec §6.6 including `# Tables`, `# In-Scope Columns`, and `# Selected for Load` aggregate counts; quick link to table list per row.
21. `Databases/DatabaseEdit.razor` — form; `DatabaseName` uniqueness scoped to parent server.

### 5c — Tables (`/servers/{id}/databases/{dbId}/tables`, `.../tables/{tableId}`)

22. `Tables/TableList.razor` — columns per spec §6.8; intent-coverage indicator badges (In Scope, Selected for Load, Unreviewed, R/D counts); filter chips ("Has In-Scope Columns", "Has Unreviewed Columns", "Has Load-Selected Columns", PersistenceType mix); bulk `IsActive` toggle with multi-select; inline `IsActive` toggle per row.
23. `Tables/TableEdit.razor` — table fields (`SchemaName`, `TableName`, `EstimatedRowCount`, `Notes`, `IsActive`) plus inline column sub-panel:
    - Drag-to-reorder list (JS interop via SortableJS or equivalent) updating `SortOrder`
    - Per-column inline editors (checkbox / radio) for all four intent flags: `IsInDaoAnalysis`, `IsAddedByApi`, `PersistenceType`, `IsSelectedForLoad`
    - Add column inline form with Add button
    - "Paste columns" textarea (newline-separated → parse and bulk-add; intent flags default to unset)
    - Delete column with `ConfirmDialog` (warn if last in-scope column on active table)
    - Visual summary badge: e.g. `4 in scope · 2 selected for load · 3 R · 1 D · 5 unreviewed`

### 5d — Global Catalogue View (`/catalogue`)

24. `Catalogue/GlobalCatalogueView.razor`:
    - Server-side paginated, sortable, filterable flat table across all `SourceColumns` rows
    - Filter bar: multi-select dropdowns for Server and Database; text filter for Schema, Table, Column; Yes/No/Any for `IsInDaoAnalysis`, `IsAddedByApi`, `IsSelectedForLoad`; R/D/Any for `PersistenceType`
    - Preset filter shortcuts as clickable chips per spec §6.2: Unreviewed, In Scope, Selected for Load, Document Columns, DAO Only, API Added
    - Filter/sort state serialised to URL query string via `NavigationManager` (bookmarkable, shareable)
    - Inline checkbox/radio intent flag editing; individual row saved on change
    - Multi-select rows → bulk action toolbar (Set `IsInDaoAnalysis`, `IsAddedByApi`, `PersistenceType`, `IsSelectedForLoad`); `ConfirmDialog` before bulk write
    - "Export CSV" button — streams all rows matching current filter (not page-limited) as `text/csv`

### 5e — Dashboard (`/`)

25. `Pages/Dashboard.razor` — summary cards per spec §6.3:
    - Total counts: servers / databases / tables / columns
    - Columns in scope: `(IsInDaoAnalysis OR IsAddedByApi) AND PersistenceType = 'R'`
    - Columns selected for load: `IsSelectedForLoad = true`
    - Breakdown by `PersistenceType` (R vs D) across all columns
    - Unreviewed columns (all intent flags at default)
    - Last-modified timestamp and user (most recent change across any entity)

---

## Phase 6 — Import Features (`/import`)

Located in `Components/Import/`.

26. `ImportDacpac.razor` (Mode A — primary):
    - `InputFile` accepting `.dacpac` only
    - Server / Database name inputs (auto-populated from DACPAC metadata if present)
    - `DacpacParserService` in `Catalogue.Infrastructure`: opens `.dacpac` as a ZIP archive, reads `model.xml`, extracts `<Element Type="SqlSimpleColumn">` nodes → builds `ImportPreviewRow` list. (Uses `System.IO.Packaging` / `System.IO.Compression` — no dependency on Windows-only `Microsoft.SqlServer.Dac`.)
    - Conflict strategy selector: `Skip existing` | `Add new only` | `Full sync (remove columns no longer present)`
    - Dry-run toggle — shows diff without writing
    - Preview table before commit
    - On confirm: upsert `Sources`, `SourceDatabases`, `SourceTables`, `SourceColumns` using chosen strategy; preserve existing intent flag values unless `Full sync` with explicit override
    - Result summary: tables added / columns added / columns removed / skipped / errors

27. `ImportExcel.razor` (Mode B — legacy migration):
    - `InputFile` accepting `.xlsx` only
    - `ExcelImportService` in `Catalogue.Infrastructure` using `ClosedXML` (MIT-licensed, Linux-compatible): reads sheet named `All`; groups rows by `(Server, Database, Schema, Table)`; auto-maps known column headers; parses `TRUE`/`FALSE`/`1`/`0`/`Yes`/`No`; maps legacy `Generate SQL INSERTS` column → `IsSelectedForLoad`; treats empty `Column` values as a warning
    - Column mapping confirmation step (flags unrecognised headers)
    - Conflict strategy selector: `Skip existing` | `Overwrite existing` | `Merge intent flags only`
    - Dry-run toggle
    - Preview table + result summary: rows added / updated / skipped / errors

---

## Phase 7 — Deployment & CI/CD

28. Add `appsettings.Production.json` with Key Vault references for the connection string (consumed via App Service managed identity).
29. Configure `DefaultAzureCredential` in `Program.cs` for Key Vault secret access and, where applicable, Azure SQL token authentication.
30. Create `.github/workflows/deploy.yml`:
    ```
    on: push to main
      → dotnet build & test
      → dotnet publish
      → az webapp deploy
      → dotnet ef database update (migrations)
    ```
    CI gate: all tests must pass before deployment step executes.
31. Create infrastructure-as-code templates under `infra/` (Bicep preferred) for:
    - App Service Plan (B2 Linux minimum)
    - App Service (.NET 8 runtime stack)
    - Azure SQL Database (General Purpose S2 minimum)
    - Azure Key Vault (connection string secret; web app uses managed identity)
    - Entra App Registration skeleton exposing `Catalogue.Reader`, `Catalogue.Editor`, `Catalogue.Admin` app roles
32. Update `README.md` with local dev setup instructions: connection string configuration, Entra app registration steps, role assignment.

---

## Verification

- **Unit tests** (`Catalogue.Tests` xUnit project): `ICatalogueRepository` stub tests; FluentValidation rule tests; DACPAC parser tests and Excel import parser tests using sample fixture files.
- **Integration tests**: `WebApplicationFactory<Program>` smoke tests asserting HTTP 200 for each routable page (with a test identity satisfying `Catalogue.Reader`).
- **Manual smoke test checklist**: create server → add database → add table → add columns via paste → verify global catalogue view filters and inline edits → run DACPAC dry-run → run Excel dry-run → verify dashboard counts.
- **CI gate**: all unit and integration tests must pass before `az webapp deploy` executes.

---

## Key Decisions

| Decision | Rationale |
|---|---|
| Blazor Server (not WASM) | No public API surface; simpler Entra ID auth model; mandated by spec §3 |
| `ClosedXML` for Excel | MIT-licensed; no Office interop; runs on Linux App Service |
| DACPAC parsed as ZIP + raw XML | Avoids Windows-only `Microsoft.SqlServer.Dac`; `model.xml` format is stable across DACPAC versions |
| URL query string for filter/sort state | Bookmarkable, shareable, JS-storage-free; works across browser sessions |
| Soft-delete only on parents with children | Matches spec §10 data retention rule; `IsActive = false` preferred over `DELETE` when child rows exist |
| `ICurrentUserService` abstraction | Decouples `CatalogueDbContext` from `HttpContext`; simplifies unit testing of the data layer |
| `global.json` SDK pin (8.0.418) | Container ships .NET 10 alongside the manually installed .NET 8 SDK; pinning prevents accidental compilation against the wrong runtime |
