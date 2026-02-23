# Source Database Catalogue

A Blazor Server 8 application for cataloguing source databases, tables, and columns used by data pipelines. Supports importing metadata from DACPAC files and Excel spreadsheets, inline intent-flag editing, and a global filterable catalogue view.

---

## Architecture

```
Catalogue.sln
├── src/
│   ├── Catalogue.Core            # Domain models, DTOs, interfaces, validators
│   ├── Catalogue.Infrastructure  # EF Core DbContext, repositories, import services
│   └── Catalogue.Web             # Blazor Server application (UI + auth)
└── tests/
    └── Catalogue.Tests           # xUnit unit and integration tests
```

**Key technologies:**
- .NET 8 / Blazor Server (Interactive Server rendering)
- Entity Framework Core 8 with Azure SQL
- Microsoft Entra ID (Azure AD) for authentication via Microsoft.Identity.Web
- Bootstrap 5 for UI
- ClosedXML for Excel import (MIT-licensed, Linux-compatible)
- System.IO.Packaging for DACPAC parsing (no Windows-only DacFx dependency)

---

## Local Development Setup

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- SQL Server (local) or Azure SQL — or SQL Server Express/LocalDB
- An Azure AD / Entra ID tenant with an app registration (see below)

### 1. Clone and restore

```bash
git clone <repo-url>
cd upgraded-umbrella
dotnet restore Catalogue.sln
```

### 2. Configure the connection string

Edit `src/Catalogue.Web/appsettings.json`:

```json
"ConnectionStrings": {
  "CatalogueDb": "Server=(localdb)\\MSSQLLocalDB;Database=CatalogueDb;Trusted_Connection=True;TrustServerCertificate=True;"
}
```

Or use .NET User Secrets (recommended):

```bash
cd src/Catalogue.Web
dotnet user-secrets set "ConnectionStrings:CatalogueDb" "Server=...;Database=CatalogueDb;..."
```

### 3. Run EF Core migrations

```bash
dotnet ef database update \
  --project src/Catalogue.Infrastructure \
  --startup-project src/Catalogue.Web
```

### 4. Create an Entra ID app registration

1. Go to **Azure Portal → Microsoft Entra ID → App registrations → New registration**.
2. Name: `Source Database Catalogue (Dev)`.
3. Redirect URI: `https://localhost:5001/signin-oidc` (type: Web).
4. Add App Roles under **Expose an API**:

   | Display Name      | Value               | Allowed member types |
   |-------------------|---------------------|----------------------|
   | Catalogue Reader  | `Catalogue.Reader`  | Users/Groups         |
   | Catalogue Editor  | `Catalogue.Editor`  | Users/Groups         |
   | Catalogue Admin   | `Catalogue.Admin`   | Users/Groups         |

5. Note the Application (client) ID and Tenant ID.
6. Under **Certificates & secrets**, create a client secret.

### 5. Configure Entra ID

Use User Secrets (recommended for development):

```bash
cd src/Catalogue.Web
dotnet user-secrets set "AzureAd:TenantId" "<your-tenant-id>"
dotnet user-secrets set "AzureAd:ClientId" "<your-client-id>"
dotnet user-secrets set "AzureAd:ClientSecret" "<your-client-secret>"
dotnet user-secrets set "AzureAd:Domain" "<yourtenant.onmicrosoft.com>"
```

### 6. Assign roles

In the portal: **Entra ID → Enterprise Applications → Source Database Catalogue (Dev) → Users and groups** — assign your account the `Catalogue.Admin` role.

### 7. Run the application

```bash
dotnet run --project src/Catalogue.Web
```

Navigate to `https://localhost:5001`.

---

## Running Tests

```bash
dotnet test Catalogue.sln
```

| Category           | Tests | Description                                               |
|--------------------|-------|-----------------------------------------------------------|
| Validator tests    | 10    | FluentValidation rules for all four entity types          |
| DACPAC parser      |  7    | In-memory DACPAC archive parsing                          |
| Excel import       | 11    | ClosedXML import service with bool mapping and edge cases |
| Integration smoke  | 13    | WebApplicationFactory — protected routes reject anonymous |

---

## Deployment (Azure)

### Infrastructure provisioning

```bash
az deployment sub create \
  --location uksouth \
  --template-file infra/main.bicep \
  --parameters infra/main.bicepparam \
  --parameters sqlAdminPassword="$(openssl rand -base64 32)"
```

Outputs include the web app name, Key Vault name, and SQL server FQDN.

### CI/CD

The `.github/workflows/deploy.yml` workflow runs on push to `main`:
1. Build and test
2. Publish and deploy to Azure App Service
3. Run `dotnet ef database update`

Required GitHub secrets: `AZURE_CREDENTIALS`, `AZURE_SUBSCRIPTION_ID`, `AZURE_SQL_CONNECTION_STRING`.

---

## Feature Overview

| Route                                     | Description                              | Min Role |
|-------------------------------------------|------------------------------------------|----------|
| `/`                                       | Dashboard — aggregate counts             | Reader   |
| `/servers`                                | Server list with search and toggle       | Reader   |
| `/servers/{id}/databases`                | Databases for a server                   | Reader   |
| `/servers/{id}/databases/{id}/tables`    | Tables with intent badges                | Reader   |
| `/catalogue`                              | Global flat view, inline editing, export | Reader   |
| `/import/dacpac`                          | Import from DACPAC archive               | Admin    |
| `/import/excel`                           | Import from Excel workbook               | Admin    |

Write operations require `Catalogue.Editor`. Import operations require `Catalogue.Admin`.

---

## Key Design Decisions

| Decision | Rationale |
|---|---|
| Blazor Server | No public API; simpler Entra ID auth; mandated by spec |
| ClosedXML for Excel | MIT-licensed; no Office interop; runs on Linux |
| DACPAC as ZIP + XML | Avoids Windows-only `Microsoft.SqlServer.Dac` |
| Soft-delete (`IsActive`) | Deactivate parents before deleting; children preserved |
| Key Vault via managed identity | Zero-secret deployment |
