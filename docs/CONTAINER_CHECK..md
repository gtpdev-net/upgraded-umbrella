# Container Validation Report

**Date:** 2026-02-23  
**Plan:** `docs/PLAN.md` — Source Database Catalogue (Blazor Server 8, Azure SQL, Entra ID)

---

## Summary

| Area | Status | Detail |
|---|---|---|
| .NET SDK | ⚠️ Wrong version | 10.0.103 installed; plan targets `net8.0` |
| EF Core CLI (`dotnet ef`) | ❌ Missing | Not installed globally |
| Azure CLI (`az`) | ✅ | 2.83.0 |
| Git | ✅ | 2.53.0 |
| zip / unzip / curl / wget | ✅ | All present |
| Node.js / npm | ⚠️ Missing | Required for SortableJS (Phase 5c drag-to-reorder) |

---

## Findings

### 1 — .NET SDK version mismatch (Blocking)

The container provides **only .NET 10.0.103**. The plan specifies `net8.0` for all three projects (`Catalogue.Core`, `Catalogue.Infrastructure`, `Catalogue.Web`).

**Options (choose one):**

- **Option A — Retarget the plan to `net10.0`** *(recommended)*: .NET 10 is current, LTS-eligible, and fully backward-compatible with all NuGet packages referenced in the plan (`Microsoft.Identity.Web`, `Microsoft.EntityFrameworkCore.SqlServer`, `ClosedXML`, etc.). No container changes needed.
- **Option B — Install .NET 8 SDK alongside .NET 10**: The `dotnet-install.sh` script is reachable. Run:
  ```bash
  curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir /usr/local/dotnet8
  export PATH="/usr/local/dotnet8:$PATH"
  ```
  This keeps both SDKs available via a `global.json` pin.

### 2 — EF Core CLI tools missing (Blocking for Phase 3)

`dotnet ef` is required for `dotnet ef migrations add InitialCreate` (Phase 3, step 11) and `dotnet ef database update` (Phase 7, CI/CD step).

**Fix:**
```bash
dotnet tool install --global dotnet-ef
export PATH="$PATH:$HOME/.dotnet/tools"
```

### 3 — Node.js / npm missing (Blocking for Phase 5c)

Phase 5c calls for SortableJS (or equivalent) via JS interop for drag-to-reorder column sort order in `Tables/TableEdit.razor`. npm is needed to pull the SortableJS package.

**Fix (apt):**
```bash
curl -fsSL https://deb.nodesource.com/setup_lts.x | sudo -E bash -
sudo apt-get install -y nodejs
```

---

## NuGet Package Compatibility (net10.0 target)

All packages named in the plan are compatible with .NET 10:

| Package | Plan Phase | net10.0 compatible |
|---|---|---|
| `FluentValidation` + extensions | 1, 4 | ✅ |
| `Microsoft.EntityFrameworkCore.SqlServer` | 1, 3 | ✅ |
| `Microsoft.EntityFrameworkCore.Design` | 1, 3 | ✅ |
| `ClosedXML` | 1, 6 | ✅ |
| `System.IO.Packaging` | 1, 6 | ✅ (inbox on .NET 10) |
| `Microsoft.Identity.Web` | 1, 4 | ✅ |
| `Microsoft.Identity.Web.UI` | 1, 4 | ✅ |
| `Microsoft.AspNetCore.Authentication.OpenIdConnect` | 1, 4 | ✅ (included in ASP.NET Core 10) |

---

## Recommended Pre-Work Steps

Run the following before starting Phase 1:

```bash
# 1. Install EF Core CLI tools
dotnet tool install --global dotnet-ef
export PATH="$PATH:$HOME/.dotnet/tools"

# 2. Install Node.js (LTS)
curl -fsSL https://deb.nodesource.com/setup_lts.x | sudo -E bash -
sudo apt-get install -y nodejs

# 3. Verify
dotnet ef --version
node --version
npm --version
```

Then update all `<TargetFramework>net8.0</TargetFramework>` references in the plan and generated `.csproj` files to `net10.0`.

---

## Conclusion

The container is **not immediately ready** — two blockers and one gap exist. Fixing them requires approximately 5 minutes of setup. The fastest path forward is to **retarget to `net10.0`** (eliminating the SDK version mismatch entirely) and then install `dotnet-ef` and Node.js as described above.
