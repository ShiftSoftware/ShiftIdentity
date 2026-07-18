# ShiftIdentity Modernization Plan

> **Status:** DRAFT — planning only, no implementation started.
> **Owner:** Aza / Shift Framework team
> **Created:** 2026-07-13
> **Scope:** `ShiftIdentity` repo (all CRUD entities), plus enabling changes in the sibling `ShiftEntity` framework where needed.

---

## ⚠️ Housekeeping reminder (do NOT forget)

This plan currently lives **inside the ShiftIdentity repo** by explicit request, to make it easy to work on step by step.

**When this effort is finished (or reaches a stable milestone):**
1. Write a **conclusion / summary section** at the bottom of this doc (what changed, what patterns emerged, what framework features were added).
2. **Move that conclusion into the cross-repo roadmap** at
   `C:\repos\ShiftSoftware\.shift\repos\shift-identity\` (per the enforced convention that plans/roadmaps live in `.shift`, not in project repos).
3. Then delete or thin this file down to a pointer.

> Per global convention, individual repos should **not** hold long-lived plan/roadmap files — this one is a temporary working copy. The `.shift` repo is not currently cloned at `C:\repos\ShiftSoftware\.shift`; clone/restore it before doing the roll-up.

---

## 1. Goal

Modernize ShiftIdentity's data/endpoint layer to use the **newer ShiftEntity patterns** that were introduced after ShiftIdentity was originally written:

- **Attribute-driven ("CRUD") endpoints** — `[ShiftEntityEndpoint<…>]` / `[ShiftEntitySecureEndpoint<…>]` placed on the entity, instead of a hand-written `ShiftEntitySecureControllerAsync<…>` per entity.
- **Built-in (default) repository** — `ShiftRepository<DB,E,L,V>` resolved automatically, instead of an empty/near-empty hand-written repository subclass.
- **Source-generated mappers** — `UseGeneratedMapper()` / `[ShiftEntityMapper]`, instead of hand-written AutoMapper `Profile`s.
- **Mapping/repository separation** — move pure *mapping* out of repository `Upsert`/AutoMapper profiles into the mapper; keep only genuine *business logic* in a custom repository.
- **Zero MVC controllers — everything becomes a minimal API.** Pure-CRUD controllers are replaced by the endpoint attributes; CRUD controllers that *also* carry custom endpoints, and *every* standalone (non-CRUD) controller, are rewritten as `MapGet/MapPost` minimal-API endpoints. **End state: no `Controller` / `ControllerBase` class remains in ShiftIdentity** (the one possible exception is a view-rendering MVC controller that cannot be expressed as an API — flag it if found; see §5 Phase 5).

Do it **one entity at a time, simplest → most complex**, so each step is small, reviewable, and independently shippable. When a step reveals a gap in the framework, **fix the framework** (`ShiftEntity`) as part of that step.

---

## 2. The decision ladder (how to classify each entity)

For each entity, pick the **lowest rung** that satisfies its needs:

| Rung | When to use | Target shape |
|------|-------------|--------------|
| **A. Pure CRUD attribute** | No custom endpoints. No custom repo logic (after cross-cutting guards are framework-handled — see §3). Mapping is convention-friendly. | `[ShiftEntitySecureEndpoint<List,View,ActionTree>("api/…", nameof(Actions.X))] { UseGeneratedMapper = true }` on the entity. **No repository class, no controller, no AutoMapper profile.** |
| **B. CRUD attribute + entity-side hooks** | No custom endpoints, but needs `Include`s / data-level / a mapper tweak, **and/or genuine write logic** (validation, duplicate checks, TypeAuth tree generation, M:N child sync). | Rung A **plus**, on the entity: `IConfiguresShiftRepository<E,L,V>` (includes / `UseGeneratedMapper(cfg)` / data-level) and/or `IUpsertsShiftRepository<E,L,V>` / `IDeletesShiftRepository<E,L,V>` (§3.4). **Still no repo class, no controller.** |
| **C. CRUD attribute + custom repository** | Only for what has **no entity-side hook**: `ApplyPostODataProcessing`, `GetIQueryable`, `PrintAsync`, `SaveChangesAsync`, or extra public methods other code calls. | `[ShiftEntitySecureEndpoint<List,View,ActionTree,TRepository>(…)]` → keep a **thin** `TRepository` holding *only* those; push write logic to the entity hooks and mapping to the generated mapper. |
| **D. CRUD extension + custom endpoints** | Needs extra endpoints beyond the 8 standard CRUD routes. | Do **not** rely on the attribute alone. Call `MapShiftEntitySecureCrud<…>(prefix, action)` manually in wiring, then add sibling `MapPost/MapGet` for the custom endpoints on the same prefix. Combine with rung B or C for the repo/mapper. **Delete the MVC controller entirely** — base CRUD *and* the custom endpoints are all minimal APIs. |

**The three-way split (the key cleanup).** For every line of an existing repo `Upsert`/`Delete`, decide which of these it is:
1. **Mapping** — copies `dto.X → entity.X` (field assignment, FK↔`ShiftEntitySelectDTO`, file/JSON columns, child objects/collections) ⇒ **the mapper's job**, and mostly *automatic* now (see below). Delete it.
2. **Write logic** — *validates*, *checks duplicates/uniqueness*, *generates TypeAuth trees*, *syncs M:N join rows*, *hashes passwords* ⇒ **the entity's job** via `IUpsertsShiftRepository` / `IDeletesShiftRepository` (§3.4). It does **not** need a repository class any more.
3. **Persistence/query concerns** — `ApplyPostODataProcessing`, `GetIQueryable`, `PrintAsync` ⇒ the only things that still justify a **custom repository** (rung C).

**Mapping is mostly automatic (§3.5).** The generated mapper composes child objects/collections **automatically to depth 10** — `ForViewChild(ren)`/`ForEntityChild(ren)`/`ForListChild(ren)` are **no longer needed** for ordinary deep mapping. Explicit `ForView`/`ForList` config is still needed only for **non-conventional** members: M:N join → `List<ShiftEntitySelectDTO>` projections, parsed values (lat/long), `CustomFields` password-stripping, and flattened display-orders.

### 🚨 AutoMapper profiles: TRIM, never DELETE

**The generated mapper only replaces the migrated endpoint's triple — nothing else in the profile.** A profile typically also carries **Cosmos replication maps** (`Entity → *Model`), which have **no source-generated equivalent** and are still driven by the replication pipeline. Deleting the file removes them **silently** — it still compiles, and replication breaks at runtime. Some profiles also hold auxiliary DTO maps used by non-CRUD flows.

**Rule — remove ONLY these two lines per migrated triple, keep everything else:**
```csharp
CreateMap<Core.Entities.X, XDTO>().ReverseMap();   // ← remove (generated mapper's MapToView/MapToEntity)
CreateMap<Core.Entities.X, XListDTO>();            // ← remove (generated mapper's MapToList)
// KEEP: CreateMap<Core.Entities.X, XModel>()                  ← Cosmos replication
// KEEP: CreateMap<Core.Entities.X, CompanyBranchSubItemModel>() ← Cosmos replication
// KEEP: any other DTO map (child/sub-object maps, UserDataDTO, UserInfoDTO, …)
```
Delete the profile **file** only if trimming leaves it empty.

**Per-profile inventory (verified 2026-07-16) — what must survive:**

| Profile | Remove (triple) | **KEEP** | File deletable when done? |
|---|---|---|---|
| `Brand` / `Service` / `Department` | *(already removed — Phase 1)* | `→ {X}Model`, `→ CompanyBranchSubItemModel` | **No** |
| `Country` | `→ CountryDTO`, `→ CountryListDTO` | `→ CountryModel` | No |
| `Region` | `→ RegionDTO`, `→ RegionListDTO` | `→ RegionModel`, `→ CityRegionModel` | No |
| `City` | `→ CityDTO`, `→ CityListDTO` | `→ CityModel`, `→ CityCompanyBranchModel` | No |
| `Company` | `→ CompanyDTO` (+ the reverse `CompanyDTO → Company`), `→ CompanyListDTO` | `→ CompanyModel` | No |
| `CompanyBranch` | `→ CompanyBranchDTO`, `→ CompanyBranchListDTO` | `→ CompanyBranchModel` | No |
| `Team` | `→ TeamDTO`, `→ TeamListDTO` | `→ TeamModel`, `TeamCompanyBranch → CompanyBranchSubItemModel` | No |
| `User` | `→ UserDTO`, `→ UserListDTO` | `→ UserModel` (replication) **and `→ UserDataDTO`, `→ UserInfoDTO`** — used by the UserManager / password / verification flows | No |
| `CompanyCalendar` | `→ CompanyCalendarDTO`, `→ CompanyCalendarListDTO` | the 4 child maps (`CompanyCalendarShiftGroup`, `CompanyCalendarShift`, `CompanyCalendarWeekendGroup`, `CompanyCalendarWeekendRule`) — **re-check**: auto-deep (§3.5) may cover them for the endpoint, but verify nothing else uses them before removing | No |
| `AccessTree` | `→ AccessTreeDTO`, `→ AccessTreeListDTO` | *(nothing)* | **Yes — becomes empty** |
| `App` | `→ AppDTO` | *(nothing)* | **Yes — becomes empty** |
| `General` | *(nothing — no triple maps)* | all 3 `→ CompanyBranchSubItemModel` maps | **No — never touch** |

**Verify after each entity:** the entity's `→ *Model` map still exists, and replication for that entity still produces a populated document.

---

## 3. Cross-cutting framework work (do FIRST — it unblocks the simple tiers)

Three patterns are duplicated across almost every ShiftIdentity repository. Today they *force* a hand-written repo even when nothing else does. Generalizing them in the framework is what lets the simplest entities drop to **Rung A**.

### 3.1 Feature locking (blocks Rung A for ~all entities) — ✅ IMPLEMENTED (Phase 0)
Every repo overrides `SaveChangesAsync()` to throw when a `ShiftIdentityFeatureLocking.XFeatureIsLocked` flag is set (13 near-identical overrides; see `ShiftIdentity.Core/ShiftIdentityFeatureLocking.cs`).

**Chosen mechanism — framework save-validator hook:** ShiftEntity now defines `IShiftEntitySaveValidator` (`ShiftEntity.EFCore/IShiftEntitySaveValidator.cs`); the built-in `ShiftRepository.SaveChangesAsync` invokes every DI-registered validator with the pending unit of work before persisting (repository save path only — not direct DbContext seeding/replication). ShiftIdentity implements `FeatureLockSaveValidator` (`ShiftIdentity.Data/FeatureLockSaveValidator.cs`) over `ShiftIdentityFeatureLocking.GetLockedMessageKey(Type)` and registers it in `AddShiftIdentityDashboard`. Preserves today's semantics (blocks only repository CRUD saves), works for built-in AND custom repos, and reuses the exact existing localized messages. As each entity migrates, delete its per-repo `SaveChangesAsync` override (the validator already covers it). Acceptance met — proven in `ShiftEntity.Tests/Repository/Phase0EnablerTests.cs`.

### 3.2 Built-in data guard (blocks Rung A for Country/Region/City/Company/CompanyBranch/User) — ✅ IMPLEMENTED (Phase 0)
`Upsert`/`Delete` throw `"Built-In Data can't be modified."` when `entity.BuiltIn` is true. Duplicated in exactly **6** repos: Country, Region, City, Company, **CompanyBranch**, User. *(Correction: the earlier draft wrongly listed `App` — App has no `BuiltIn` property/guard — and omitted `CompanyBranch`.)*

**Implemented:** ShiftEntity defines the marker `IShiftEntityProtectable` (`ShiftEntity.Core/IShiftEntityProtectable.cs`) with a `bool IsProtected { get; }` member; the built-in repo's `UpsertAsync`/`DeleteAsync` reject an edit/delete of a protected row with 403 (checked before mapping / before the data-level check, matching the old per-repo behavior). The 6 entities implement the marker via a plain `bool IsProtected` property (no `[Column]` override), so EF maps it to a **`IsProtected` SQL column** — the column is renamed too. Each consuming DB needs a one-line `RenameColumn("BuiltIn" → "IsProtected")` migration on the 6 ShiftIdentity-schema tables (ShiftIdentity ships no migrations; consumers own the schema). The sample's is `StockPlusPlus.Data/Migrations/20260714083851_RenameBuiltInToIsProtected.cs` (EF-generated; temporal history handled automatically). The **8 Cosmos replication models were also renamed** `BuiltIn` → `IsProtected` (a deliberate breaking wire change — coordinate with downstream microservices + stored documents), so AutoMapper maps `IsProtected`→`IsProtected` by convention and no explicit `ForMember` is needed. Per-repo guards can be deleted as each entity migrates. Acceptance met — see `Phase0EnablerTests`.

> **Naming note (2026-07-14):** the marker/flag was originally added as `IShiftEntityBuiltInProtected` / `bool BuiltIn`, then renamed to `IShiftEntityProtectable` / `IsProtected` — effect-based naming for a general framework feature ("built-in" was one identity-specific *reason* a row is protected). **Everything is `IsProtected` now**: the C# interface + member, the SQL column (via a `RenameColumn` migration per consumer), and the Cosmos replication wire field (a deliberate breaking replication change). Only the identity `Constants.BuiltIn*` **row-name strings** (e.g. `"Built-in System Country"`) stay — they name seeded rows, unrelated to the flag.

### 3.3 Default data-level access options (needed for the built-in repo path) — ✅ IMPLEMENTED (Phase 0)
Every repo sets `ShiftRepositoryOptions.DefaultDataLevelAccessOptions = shiftIdentityDefaultDataLevelAccessOptions`. The built-in-repo path must apply this too.

**Implemented:** the built-in `ShiftRepository` (`InitCommon`) now resolves a DI-registered `DefaultDataLevelAccessOptions` and uses it as the baseline (a custom repo's ctor-body assignment still wins; absent a registration the old `new()` default stands, so other consumers are unaffected). ShiftIdentity additionally registers its `ShiftIdentityDefaultDataLevelAccessOptions` under the base type in `AddShiftIdentityDashboard`. Acceptance met — see `Phase0EnablerTests`.

### 3.4 Entity-driven upsert/delete hooks — ✅ ALREADY IN THE FRAMEWORK (no work needed; **changes the ladder**)
Write logic no longer forces a repository class. An entity can take over upsert/delete directly:

```csharp
public class AccessTree : ShiftEntity<AccessTree>,
    IUpsertsShiftRepository<AccessTree, AccessTreeListDTO, AccessTreeDTO>
{
    public async ValueTask<AccessTree> UpsertAsync(
        AccessTree entity, AccessTreeDTO dto, ActionTypes actionType, long? userId, Guid? idempotencyKey,
        bool disableDefaultDataLevelAccess, bool disableGlobalFilters,
        ShiftRepositoryUpsertContext<AccessTree, AccessTreeListDTO, AccessTreeDTO> context)
    {
        // duplicate check / validation / tree generation here …
        var saved = await context.Base();   // == base.UpsertAsync(…): mapping, audit, protected-row guard, data-level
        // … post-logic
        return saved;
    }
}
```

- Interfaces: `IUpsertsShiftRepository<E,L,V>` / `IDeletesShiftRepository<E,L,V>` (`ShiftEntity.EFCore`). Signature = the repository method verbatim + a trailing `ShiftRepositoryUpsertContext`/`ShiftRepositoryDeleteContext` exposing `Services`, `Repository`, and `Base()`.
- `context.Base()` is **optional** — calling it runs the framework default (mapping, tagging, audit stamping, protected-row guard, data-level check); skipping it replaces the default wholesale. Insert *and* update both arrive at the upsert hook — branch on `actionType`.
- **Keyed by the DTO triple**, like `IConfiguresShiftRepository` — implement once per triple.
- **Fires even with a custom repository** (unlike `IConfiguresShiftRepository`, which is built-in-only): repo override → entity hook → default, as long as the override calls `base`. So an entity's rule survives someone later adding a repository for an unrelated reason.
- Working precedent to copy: `StockPlusPlus.Data/Entities/Country.cs` (implements both, for the `CountryGeneratedDTO` triple).

> **Ladder impact:** the only entity-side hooks that exist are `IConfiguresShiftRepository`, `IUpsertsShiftRepository`, `IDeletesShiftRepository`. There is **no** entity hook for `ApplyPostODataProcessing`, `GetIQueryable`, `PrintAsync` or `SaveChangesAsync` — those (and extra public methods) are the *only* remaining reasons to keep a repository class (rung C).

### 3.5 Automatic deep mapping (max depth 10) — ✅ ALREADY IN THE FRAMEWORK
The source-generated mapper composes child objects/collections **automatically, up to 10 nested levels** (`ShiftEntityMapperDefaults.MaxDepth = 10`) in the View, Entity and List directions — **no explicit `ForXxxChild(ren)` needed**.
- Tune with `[ShiftEntityMapperMaxDepth(n)]` (on a `ShiftRepository<…>` subclass, a `[ShiftEntityMapper]` partial, or the assembly) or fluent `map.MaxDepth(n)`. Explicit `ForXxxChild(ren)` still composes **beyond** the cap and makes auto-deep step aside for that member.
- Prune with `[ShiftEntityMapperIgnore]` on a property or `map.Ignore(…)` / `IgnoreView/Entity/List/Copy` — the member is omitted from the generated code entirely (complex subtrees pruned).
- **Still needs explicit config** (auto-deep does *not* cover these): `List<ShiftEntitySelectDTO>` projections over M:N join rows, parsed scalars (lat/long), `CustomFields` password-stripping, flattened display-orders. (`ShiftEntitySelectDTO`/`ShiftFileDTO` aren't "pairable" children — they have their own FK / file-JSON conventions.)

> **Rule for the whole effort:** if adopting a pattern needs a framework capability that doesn't exist yet, **build it in `ShiftEntity` first**, land it, then continue the entity migration. Record every such framework change in the Progress Log (§6) and in the eventual `.shift` roll-up.

---

## 4. Entity inventory & classification

Repos live in `ShiftIdentity.Data/Repositories/`, controllers in `ShiftIdentity.Dashboard.AspNetCore/Controllers/`, AutoMapper profiles in `ShiftIdentity.Data/AutoMapperProfiles/`.

Legend — **Rung** is the target from §2. **Guards** = Protected-row(P, §3.2)/FeatureLock(L, §3.1). **Blockers** = what must exist before it can move.

| Entity | Repo overrides today | Custom endpoints | Target rung | Notes |
|--------|----------------------|------------------|-------------|-------|
| **Brand** | SaveChanges(L) | 0 | **A** | Simplest. Only blocker: §3.1. Pilot for Tier 1. |
| **Service** | SaveChanges(L) | 0 | **A** | As Brand. |
| **Department** | SaveChanges(L) | 0 | **A** | As Brand. |
| **Country** | Upsert(P), Delete(P), SaveChanges(L) | 0 | **A** | Needs §3.1 + §3.2. Mirrors the already-modernized `StockPlusPlus` Country sample — copy that. |
| **Region** | Upsert(P), Delete(P), SaveChanges(L), Include(Country) | 0 | **B** | As Country + `Include` via `IConfiguresShiftRepository`. |
| **City** | Upsert(P), Delete(P), SaveChanges(L), Include(Region→Country) | 0 | **B** | As Region. |
| **App** | Upsert(?), SaveChanges(L) | 0 | **B** | `App,AppDTO,AppDTO` (same DTO for list+view). **Read `AppRepository.Upsert` at implementation**: whatever logic it holds now goes in `IUpsertsShiftRepository` on the entity — no repo class either way. |
| **AccessTree** | Upsert(TypeAuth tree gen — business), SaveChanges(L) | 0 | **B** ⬅ *was C* | Tree generation + name-uniqueness → `IUpsertsShiftRepository` on the entity; lock → §3.1 validator. Nothing left ⇒ **delete the repo**. Mapper trivial → generated. |
| **CompanyCalendar** | SaveChanges(L), Include | 1 (`GetCalendarEvents`) | **D** (+B) | Custom endpoint → CRUD extension. `Include` → `IConfiguresShiftRepository`; lock → validator ⇒ **no repo class**. |
| **Company** | Upsert(business: phone, circular-ref), Delete(P), ApplyPostODataProcessing, SaveChanges(L), CustomFields map | 0 | **C** (thin) | Phone/circular-ref → `IUpsertsShiftRepository`; Delete guard → §3.2; lock → validator. Repo survives **only** for `ApplyPostODataProcessing`. Mapper: `CustomFields` + `ParentCompany`/`Brands` still need `ForView`/`ForList`. |
| **Team** | Upsert(M:N Users + Branches sync, business), SaveChanges(L) | 0 | **B** ⬅ *was C* | Duplicate-user check + M:N sync → `IUpsertsShiftRepository`; `Include`s → `IConfiguresShiftRepository`; lock → validator. Nothing left ⇒ **delete the repo**. |
| **CompanyBranch** | Upsert(P, M:N Services/Departments/Brands), Delete(P), ApplyPostODataProcessing, SaveChanges(L) | 0 | **C** (thin) | M:N sync → `IUpsertsShiftRepository`; guards → §3.2/§3.1. Repo survives **only** for `ApplyPostODataProcessing`. Mapper is lighter than feared (auto-deep, §3.5) — only lat/long parse, CustomFields, display-orders and the M:N `ShiftEntitySelectDTO` projections need `ForView`/`ForList`. |
| **User** | Upsert(466-line business), Delete(P), SaveChanges | **6** (+ `UserManagerController` 9) | **D** (+C) | **Last.** CRUD extension for base + custom endpoints. Upsert logic → `IUpsertsShiftRepository`; but the repo **stays** for its many public methods (`GenerateEffectiveAccessTreeAsync`, `ChangePasswordAsync`, `SetTotpSecret`, `UpdateUserDataAsync`, `AssignRandomPasswords`, `VerifyPhonesAsync`, `UserImportAsync`, `GetUserBy…`) + `IUserRepository`. |

**Not entity CRUD (off the entity ladder), but still in scope for minimal-API conversion:**
- **Standalone (non-CRUD) controllers** → converted to minimal APIs in **Phase 5**, not left as controllers:
  - `ShiftIdentity.Dashboard.AspNetCore/Controllers/`: `UserManagerController` (9 endpoints — user-facing account/verification flows, tied to User), `IdentityPublicUserController` (1), `IdentitySyncController` (1), `ReverseTypeAuthLookupController` (1).
  - `ShiftIdentity.AspNetCore/Controllers/API/AuthController.cs` (5 endpoints — login/refresh/token; **most sensitive**, convert last with care) and `ShiftIdentity.AspNetCore/Controllers/MVC/AuthController.cs` (1 endpoint — verify whether it renders a view/redirect; if it's not a pure API it may stay, flag it).

**Genuinely out of scope (leave untouched):**
- **Join/replication entities** (no CRUD endpoint): `CompanyBranchBrand`, `CompanyBranchDepartment`, `CompanyBranchService`, `CompanyCalendarBranch`, `TeamCompanyBranch`, `TeamUser`, `UserAccessTree`. `CompanyBranchDepartmentRepository`/`CompanyBranchServiceRepository` are `IShiftEntityPrepareForReplicationAsync` hooks — leave untouched.
- **`UserLog`, `AccessTree` sub-DTOs**, etc. — not endpoint-bearing.

---

## 5. Phased execution plan

Each phase is independently shippable. Do not start a phase until the previous one is green (build + tests). Check the boxes as you go.

### Phase 0 — Framework enablers & pilot proof (prerequisite)
- [x] **0.1** ✅ **feature-locking** generalization (§3.1) — framework `IShiftEntitySaveValidator` + identity `FeatureLockSaveValidator`. Tested.
- [x] **0.2** ✅ **Protected-row guard** generalization (§3.2) — framework `IShiftEntityProtectable` (`IsProtected`) + guard; 6 entities implement it. Tested.
- [x] **0.3** ✅ **default data-level access** on the built-in repo path (§3.3) — built-in repo resolves a DI-registered `DefaultDataLevelAccessOptions`; identity registers it under the base type. Tested.
- [x] **0.4** ✅ **Done with Phase 1.** DI: `RegisterShiftRepositories(typeof(ShiftIdentityActions).Assembly)` now runs inside `AddShiftIdentityDashboard`. Routes: the host's existing `app.MapShiftEntityEndpoints<DB>()` picks identity up once it calls the new `AddShiftEntityWeb(x => x.AddShiftIdentityDataAssembly())` (added to `ShiftEntityOptionsExtensions`, mirroring `AddShiftIdentityAutoMapper`). Attribute routes coexist with the remaining classic controllers (`app.MapControllers()` + `MapShiftEntityEndpoints<DB>()` both run).
- [x] **0.5** ✅ **Confirmed at runtime.** Discovery resolves `nameof(ShiftIdentityActions.Brands/Services/Departments)` to real `ReadWriteDeleteAction` fields — verified for all three endpoints (see Phase 1).
- [x] **0.6** ✅ **Nothing needed.** `Blazor`/`Dashboard.Blazor` consume these endpoints over HTTP (e.g. `ActionTree.razor.cs` calls `IdentityBrand?$orderby=Name`); no generator/discovery wiring exists there. Because the routes are unchanged, the Blazor clients are unaffected. (`ShiftEntityODataOptionsExtensions.RegisterShiftIdentityDashboardEntitySets` is entirely commented-out dead code — OData is served by the endpoint itself.)

> **⚠ Infra prerequisite discovered in Phase 1 — the source generator must run on `ShiftIdentity.Core`.** `UseGeneratedMapper = true` resolves the mapper from `ShiftEntityMapperRegistry`, which a `[ModuleInitializer]` in the *generated* code populates — so the generator has to run on the assembly declaring the endpoint attributes (that's `ShiftIdentity.Core`, where the entities live). In **package** mode it ships inside the `ShiftSoftware.ShiftEntity` package, but in **dev** mode (sibling repos cloned ⇒ ProjectReferences) analyzers don't flow, so the generator silently never ran and discovery would have thrown at startup. Fixed by adding a dev-mode `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` ProjectReference to `ShiftIdentity.Core.csproj`, mirroring `StockPlusPlus.Data`.

**Pilot proof:** the three framework enablers are proven in `ShiftEntity.Tests/Repository/Phase0EnablerTests.cs` (9 tests; full ShiftEntity suite green). The attribute pipeline itself is proven on real identity entities in Phase 1 — discovery yields the 3 expected routes, each secure with a resolved TypeAuth action, bound to its generated mapper and the built-in repository.

### Phase 1 — Tier 1: pure CRUD attribute (Brand → Service → Department) — ✅ DONE (2026-07-16)
All three landed on **Rung A**: attribute on the entity, **no repository, no controller**, source-generated mapper.
- [x] **Brand**: `[ShiftEntitySecureEndpoint<BrandListDTO, BrandDTO, ShiftIdentityActions>("api/IdentityBrand", nameof(ShiftIdentityActions.Brands), UseGeneratedMapper = true)]`; deleted `BrandRepository` + `IdentityBrandController`; **trimmed** (not deleted) `AutoMapperProfiles/Brand.cs`; dropped its `AddScoped<BrandRepository>()`.
- [x] **Service**: same shape → `api/IdentityService`, `nameof(ShiftIdentityActions.Services)`.
- [x] **Department**: same shape → `api/IdentityDepartment`, `nameof(ShiftIdentityActions.Departments)`.
- [x] **Route strings identical** — verified by running discovery: `api/IdentityBrand`, `api/IdentityService`, `api/IdentityDepartment` reproduce the old `api/[controller]` output exactly, so the Blazor dashboard (which calls `IdentityBrand?$orderby=Name`) is unaffected.
- [x] **Feature lock** now comes from the §3.1 `FeatureLockSaveValidator` (entity-type → flag map already covers Brand/Service/Department), so deleting the `SaveChangesAsync` overrides loses nothing.

> **⚠ Where the "TRIM, never DELETE" rule came from.** This plan originally said "delete `AutoMapperProfiles/Brand.cs`" — which would have **silently broken Cosmos replication**, because the profile also holds `Brand→BrandModel` / `Brand→CompanyBranchSubItemModel`. Caught during Phase 1; the profiles were trimmed instead. The rule + the full per-profile keep/remove inventory now live in **§2 "AutoMapper profiles: TRIM, never DELETE"** — read that before every subsequent entity.
>
> Also note `BrandListDTO`/`ServiceListDTO`/… are still referenced elsewhere purely as **hashid type-keys** (`hashIdService.Encode<BrandListDTO>(…)`), which is unaffected by removing the AutoMapper maps.

### Phase 2 — Tier 2: CRUD attribute + protected-row guard + light config (Country → Region → City → App) — ✅ DONE (2026-07-18)

> **🚨 Prerequisite that reshaped the whole approach — ENTITIES MOVED `ShiftIdentity.Core` → `ShiftIdentity.Data`.**
> Unlike StockPlusPlus (entities live in `.Data`, next to the DbContext + EF Core), ShiftIdentity's entities were in `.Core`, which is referenced by the **Blazor RCLs** (`ShiftIdentity.Blazor`, `Dashboard.Blazor`). The entity-side hooks (`IConfiguresShiftRepository`/`IUpserts`/`IDeletes`) live in `ShiftEntity.EFCore`, so implementing them on a `.Core` entity would drag EF Core into the Blazor clients; naming a `.Data` repo from a `.Core` entity attribute is a circular reference; and the built-in-repo closed generic is invariant over the host's concrete DbContext (unregisterable from ShiftIdentity). **Country migrates as pure Rung A regardless, but Region/City/App cannot use hooks until the entities live where EF Core does.** Chosen fix (Aza, 2026-07-18): move the **whole entity graph** to `ShiftIdentity.Data` (Blazor uses zero entity types — verified — so it's unaffected). This is a **one-time enabler for Phases 3–4 too**: every future entity hook now works directly. See the Progress Log entry for the mechanics (generator + endpoint discovery re-keyed to the Data assembly; 3 Core stragglers resolved).
>
> **Namespace renamed `…Core.Entities` → `…Data.Entities` (+ `…Core.IRepositories` → `…Data.IRepositories`) — a BREAKING API change (Aza, 2026-07-18).** The entities are public package surface (e.g. `ADP` references `ShiftSoftware.ShiftIdentity.Core.Entities` in seed data + EF migration snapshots, via the published NuGet packages). The clean namespace was chosen over source-compatibility. **Downstream consumers must, on upgrade:** update their `using`s / fully-qualified references. **EF migration snapshots need NO action** — verified empirically: EF's differ compares the *relational* model (tables/columns, keyed by `[Table("…")]` names, unchanged), not entity CLR names, so `dotnet ef migrations add` after the rename produces an **empty migration** (proven with a probe on the sample). The snapshot's entity-key strings (`…Core.Entities.X`) are harmless string literals that EF rewrites automatically on the next real migration. ADP was **not** touched here (it's package-decoupled and 17/18 of its references are auto-generated migration files) — it updates its `using`s when it bumps the ShiftIdentity version; its migrations need nothing. In this repo, the rename likewise touches **zero** migration files (only `using`s/references in live code). Endpoint entities gained an explicit `using ShiftSoftware.ShiftIdentity.Core;` (for `ShiftIdentityActions`, previously resolved by namespace nesting).

- [x] **Country**: Rung A. `[ShiftEntitySecureEndpoint<CountryListDTO, CountryDTO, ShiftIdentityActions>("api/IdentityCountry", nameof(ShiftIdentityActions.Countries), UseGeneratedMapper = true)]`; deleted `CountryRepository` + `IdentityCountryController`; **trimmed** the profile (kept `→ CountryModel`). Protected-row guard now central (built-in repo), lock via `FeatureLockSaveValidator`.
- [x] **Region**: Rung B. `IConfiguresShiftRepository<Region,RegionListDTO,RegionDTO>` moves the `Include(Country)` **and** the two flattened list columns (`Country` name, `CountryDisplayOrder`) as `UseGeneratedMapper(map => map.ForList(...))`. The view `Country` `ShiftEntitySelectDTO` **Text** is preserved automatically — the generated FK convention fills `Text = Country.Name` when the navigation is loaded (verified in generated code), so no `ForView` was needed. Deleted repo + controller; **trimmed** the profile (kept `→ RegionModel`, `→ CityRegionModel`).
- [x] **City**: Rung B — **BOTH hooks** (plan under-specified this: City is not "as Region"). `IConfiguresShiftRepository` for `Include(Region→Country)` + four flattened `ForList` columns; **plus** `IUpsertsShiftRepository<City,CityListDTO,CityDTO>` for genuine write logic — `entity.CountryID` is derived from the selected Region (set **before** `context.Base()` so the country-scoped data-level write check authorises against the real country; `CityDTO` has no Country member so mapping never clobbers it). The Region lookup is a direct `db.Regions` query (the entity is in `.Data` now, so it resolves `ShiftIdentityDbContext` via `context.Services`). Deleted repo + controller; **trimmed** the profile (kept `→ CityModel`, `→ CityCompanyBranchModel`).
- [x] **App**: **Rung C, not Rung B** (plan under-specified this). `AppRepository` implements `IAppRepository.GetAppAsync`, consumed by the OAuth `AuthCodeService` (+ a `FakeAppRepository` in the external host) — so a repository MUST survive. Landed: `[ShiftEntitySecureEndpoint<AppDTO, AppDTO, ShiftIdentityActions>("api/IdentityApp", nameof(ShiftIdentityActions.Apps), UseGeneratedMapper = true)]` (built-in repo + generated mapper); the duplicate-`AppId` check → `IUpsertsShiftRepository<App,AppDTO,AppDTO>` on the entity; `AppRepository` **converted to a plain class** (no longer a `ShiftRepository`) holding only `GetAppAsync`, registered only as `IAppRepository` (dropped its concrete registration). Deleted `IdentityAppController`; deleted the App profile (empty after trim — no `→ AppModel`).
- [x] **Cross-phase unblock**: `CompanyBranchRepository` (Phase 3) injected the deleted `CityRepository`/`RegionRepository` for two `FindAsync` lookups → now injects the framework's **built-in `ShiftRepository<DB, City, …>` / `ShiftRepository<DB, Region, …>`** (the open generic registered by `RegisterShiftRepositories`, per the 2026-07-17 change), keeping the original `FindAsync(…, disableDefaultDataLevelAccess: true, disableGlobalFilters: true)` calls verbatim. This preserves the repository abstraction + full `FindAsync` semantics rather than reaching into `db.Cities`/`db.Regions` directly — so the two hand-written repos could be deleted with a minimal, faithful diff.
- [x] **Tests**: added `StockPlusPlus.Test/Tests/IdentityAttributeEndpointDiscoveryTests.cs` (12 DB-free tests — all 7 identity endpoints discover from the Data assembly, the 4 migrated triples resolve `Generated_*` mappers with no custom repository, routes byte-match, actions resolve). Full `StockPlusPlus.Test` suite: 124/126 green (the 2 failures are Cosmos-emulator-unavailable at `localhost:8081`, unrelated). App-boot integration tests pass → the host maps all identity endpoints without a startup throw.

### Phase 3 — Tier 3: write logic → entity hooks, mapping → generated (AccessTree → Team → Company → CompanyBranch → CompanyCalendar) — ✅ DONE (2026-07-18)

> **🚨 The Rung-C shape refined (important for the ladder + Phase 4).** Company and CompanyBranch are the first entities where the endpoint **consumes the surviving custom repository** (App's kept repo was CRUD-orthogonal — `IAppRepository.GetAppAsync`). That forces the **repository-typed** attribute `[ShiftEntitySecureEndpoint<L, V, TActionTree, TRepository>]`, and two framework contracts bind: `UseGeneratedMapper = true` is **illegal on the repo variant** (discovery throws), and `IConfiguresShiftRepository` is built-in-only + would trip **SHENGEN006** against the repo's builder. So for a true Rung-C entity the **Include + mapper config live in the repo's base-ctor `UseGeneratedMapper(map => …)` builder**, and only `IUpsertsShiftRepository` goes on the entity (it still fires through the non-overridden built-in upsert). Rung A/B entities keep the attribute's `UseGeneratedMapper = true` + entity-side `IConfiguresShiftRepository`.

- [x] **AccessTree** → **Rung B, repo deleted**. `IUpsertsShiftRepository`: name-uniqueness + TypeAuth tree generation (resolve `ITypeAuthService` via `context.Services`); the tree is generated **before** `Base()` (the update preserver reads the loaded `entity.Tree`) and re-assigned **after** `Base()` (else the scalar convention overwrites it with `dto.Tree`). Profile deleted (no `→ *Model` map).
- [x] **Team** → **Rung B, repo deleted**. `IConfiguresShiftRepository` (3 Includes + `ForView` M:N Users/CompanyBranches + `ForEntity` Tags + `ForList` Company name/CompanyId) + `IUpsertsShiftRepository` (duplicate-user + M:N `TeamUsers`/`TeamCompanyBranches` diff-sync, unified for insert **and** update). Profile **trimmed** (kept `→ TeamModel`, `TeamCompanyBranch → CompanyBranchSubItemModel`).
- [x] **Company** → **Rung C (thin repo for `ApplyPostODataProcessing`)**. Custom-repo attribute; mapper config in the repo builder (`ForView` CustomFields-strip/ParentCompany, `IgnoreEntity` CustomFields, `ForList` ParentCompanyName/**ParentCompanyID**/Brands). `IUpsertsShiftRepository` on the entity: phone/circular-ref **before** `Base()`, ParentCompanyID + CustomFields password-preserving merge **after**. Delete guard + lock central. Profile **trimmed** (kept `→ CompanyModel`). **LIST is fully generated** (no AutoMapper) — see the scope-id note below.
- [x] **CompanyBranch** → **Rung C (thin repo for `ApplyPostODataProcessing`)**. Heaviest mapper (lat/long parse `ForView`+`ForEntity`, CustomFields, M:N Departments/Services/Brands `ForView`+`ForList`, flattened names/display-orders + `CompanyTerminationDate` + **scope-ids CompanyId/CityId/RegionId** `ForList`). `IUpsertsShiftRepository`: City→RegionID / Region→CountryID derivation (resolve the built-in `ShiftRepository<…,City/Region,…>` via `context.Services`) **before** `Base()`, Region/Country immutability (the old CompanyID-immutability clause was dead code — dropped), CustomFields merge, M:N sync, phone. Profile **trimmed** (kept `→ CompanyBranchModel`). **LIST is fully generated** (see below).

> **🚨 Scope-id projections in the LIST (Company + CompanyBranch) — runtime bug found + fixed (2026-07-18).** These two are the **only** migrated entities with **collection members in the LIST DTO** (CompanyBranch: `Brands`/`Departments`/`Services`; Company: the `Brands` aggregation), which is what made the bug **visible**, but the collections are NOT the cause. A **FILTERED** list call — the Team-form CompanyBranch `<ShiftAutocomplete>` sends `GET api/IdentityCompanyBranch?$filter=CompanyId eq X`, composed as `.Where(dto.CompanyId == …)` on the projection — threw `InvalidOperationException: could not be translated`. **Real root cause (proven by a `ToQueryString()` diagnostic):** the filter target `CompanyId` was **never projected**. The list DTO calls it `CompanyId` (string, `[CompanyHashIdConverter]`) but the entity calls it `CompanyID` (`long?`); the generated list convention is **case-sensitive** AND doesn't do `long?→string`, so it skipped it. With no projected scalar to bind, EF inlines the whole collection-bearing member-init into the `WHERE` → untranslatable. (AutoMapper's `ProjectTo` "worked" pre-migration only because AutoMapper's convention is case-insensitive + does `long→string`, so it projected `CompanyId`.) A plain hand-written flat `Select` **without** `CompanyId` throws identically; **with** it, translates — so it was never a shape/`Include`/collection problem. **Fix (chosen over reverting to AutoMapper or a generator change — see §8 progress log):** project the case+type-mismatched IDs explicitly via `ForList` — `CompanyId`/`CityId`/`RegionId` on CompanyBranch, `ParentCompanyID` on Company (`e.CompanyID.HasValue ? e.CompanyID.Value.ToString() : null`). EF then binds the filter to `e.CompanyID`, pushes it down, and the collections stay in the SELECT. The **generated mapper serves the entire list** — AutoMapper is fully out of these two (no `ProjectTo`, no `IMapper`, no restored profile maps). **Verified:** `StockPlusPlus.Test/Tests/CompanyBranchListTranslationTests.cs` (3 tests) drive the repo's real generated `MapToList` + the filter through `ToQueryString()`, asserting the SQL translates and `CompanyId` reaches `CONVERT(varchar(20),[c].[CompanyID])`. **Generator gap (deferred, see progress log):** the true AutoMapper-parity fix is to make the generator's convention exact-first-then-case-insensitive + add `long?→string` to the list convention — a ~20-site framework-wide refactor (every convention emits the entity accessor from the DTO's name, so all must switch to the matched member's real name). Deferred in favour of the local `ForList` fix; **until then, any list DTO whose scalar name/type differs from its entity member (e.g. `Xxxid`←`XxxID`, string←long) must project it with `ForList` or it won't be filterable.**
- [x] **CompanyCalendar** → **Rung B + D, repo + profile deleted**. `IConfiguresShiftRepository`: Include(Branches) + `ForView(Branches)` + `IgnoreEntity(Branches)` + **nested `ForViewChildren`/`ForEntityChildren`** on ShiftGroups/WeekendGroups to encode/decode the hashid `Departments`/`Brands` via `IHashIdService` (auto-deep covers the rest of the JSON child structure). `IUpsertsShiftRepository`: Branches M:N merge-by-key **after** `Base()`. The custom `GetCalendarEvents` became a **sibling minimal API** (see below); base CRUD stays attribute-driven. Profile **deleted** (no `→ *Model` map — corrects the §2 inventory row).
- [x] **Minimal-API infra (new).** `MapShiftIdentityDashboard(this IEndpointRouteBuilder)` (`Extentsions/EndpointRouteBuilderExtensions.cs`) is a **thin aggregator** — the app-side companion to `AddShiftIdentityDashboard`. Each feature keeps its custom endpoints in **its own class under `Endpoints/`** (e.g. `Endpoints/CompanyCalendarEndpoints.cs` → `MapCompanyCalendarEndpoints`), so a route is obvious to find; adding a feature = drop a `*Endpoints` class + one line in the aggregator (Phases 4/5 do exactly this). Host calls `app.MapShiftIdentityDashboard()` once under `#if internalShiftIdentityHosting` (right after `MapShiftEntityEndpoints<DB>()`). `GetCalendarEvents` ported verbatim (route/verb byte-identical). **⚠ auth:** the old controller action had **no explicit per-action TypeAuth check** (only ambient `RequireAuthorization`), so the minimal API preserves authenticated-only — a comment marks how to tighten to `.RequireTypeAuthRead(ShiftIdentityActions.CompanyCalendars)` if the team wants it (recommended, but a deliberate behavior change so left for confirmation).
- [x] **DI:** dropped `AddScoped` for AccessTree/Team/CompanyCalendar repos (deleted); **kept** Company/CompanyBranch repos (the Rung-C endpoints resolve them) + `CalendarService`. **Tests:** extended `IdentityAttributeEndpointDiscoveryTests` to all 12 endpoints (built-in-repo vs custom-repo). `ShiftIdentity.sln` + `StockPlusPlus.API` build clean; full `StockPlusPlus.Test` **136/136** green. **Residual:** the entity hooks' runtime behavior (CustomFields merge, City/Region derivation, M:N sync, CompanyCalendar hashid children) is verified by generated-code inspection + no-SHENGEN + app-boot, but not by an identity CRUD integration test (ShiftIdentity ships none) — smoke-test the dashboard UI against these routes, or add a per-entity round-trip test.

### Phase 4 — Tier 4: User (custom endpoints + heavy repo)
- [ ] **4.1** Map base CRUD via **CRUD extension** (`MapShiftEntitySecureCrud<UserRepository,User,UserListDTO,UserDTO>(…)`) so custom endpoints can sit alongside.
- [ ] **4.2** Re-add the 6 custom `IdentityUserController` endpoints (`EffectivePermissions`, `AssignRandomPasswords`, `ResetTotp`, `VerifyEmails`, `VerifyPhones`, `ImportUsers`) as sibling minimal-API endpoints on the same prefix, preserving routes + TypeAuth attributes.
- [ ] **4.3** Split the 466-line `UserRepository`: the **upsert logic** (uniqueness, password hashing, access-tree generation, verification-flag resets) → `IUpsertsShiftRepository<User,UserListDTO,UserDTO>` on the `User` entity (§3.4); the plain `dto.X → entity.X` field copies + view/list projection → the generated mapper (§3.5). The repo **stays** (it's `IUserRepository` and holds the public methods the endpoints call), but shrinks to those methods — no `UpsertAsync`/`DeleteAsync` overrides.
- [ ] **4.4** Delete `IdentityUserController` — nothing MVC remains for User. (`UserManagerController`, which is tied to User's account flows, is converted in **Phase 5**.)

### Phase 5 — Standalone (non-CRUD) controllers → minimal APIs
Convert every remaining `ControllerBase` controller to minimal-API endpoint groups (`app.MapGroup(...).MapGet/MapPost(...)`), preserving routes, TypeAuth attributes/filters, and response shapes. After each, delete the controller and verify the route is byte-identical.
- [ ] **5.1 `UserManagerController`** (9 endpoints — account/verification flows tied to User; do first, right after Phase 4). Port each endpoint to a minimal-API group; keep the `UserRepository`/services it depends on.
- [ ] **5.2 `IdentityPublicUserController`** (1 endpoint).
- [ ] **5.3 `IdentitySyncController`** (1 endpoint).
- [ ] **5.4 `ReverseTypeAuthLookupController`** (1 endpoint).
- [ ] **5.5 `ShiftIdentity.AspNetCore/Controllers/API/AuthController`** (5 endpoints — login/refresh/token; **most sensitive**, convert last, add extra tests before/after). Confirm auth pipeline (cookies/JWT/anti-forgery) behaves identically under minimal APIs.
- [ ] **5.6 `ShiftIdentity.AspNetCore/Controllers/MVC/AuthController`** (1 endpoint): first determine if it returns a view/redirect. If it's a pure API, convert; if it renders UI, **leave it and record the exception** (see §1 end-state caveat).
- [ ] **5.7** Remove now-unused MVC plumbing (`AddControllers`/`MapControllers`, `IMvcBuilder` extensions) once no controllers remain — but keep it if the MVC AuthController stays.

### Phase 6 — Cleanup & consolidation
- [ ] Delete only the AutoMapper profile **files that trimming left empty** (expected: `AccessTree`, `App` — see the §2 inventory). **Do NOT touch `General.cs`** — it holds only Cosmos replication maps (`CompanyBranchService/Department/Brand → CompanyBranchSubItemModel`) and no entity triple maps. Every other profile must still contain its `→ *Model` replication maps.
- [ ] Ensure `AzureFunctions` + `Blazor` hosts discover the attribute endpoints consistently.
- [ ] Assert **no `Controller`/`ControllerBase` classes remain** in ShiftIdentity (except any deliberately-kept view-rendering MVC controller, recorded as an exception).
- [ ] Full regression: `dotnet build` + all identity tests; smoke-test the Dashboard Blazor UI against each migrated route.
- [ ] Write the **conclusion** (see §7) and execute the **§Housekeeping** roll-up into `.shift`.

---

## 6. Testing strategy

- After **every** entity: `dotnet build` the identity solution + run the identity test suite.
- Add/keep an integration test per migrated entity asserting the 5 CRUD operations + TypeAuth (Read/Write/Delete) + the feature-lock and protected-row guards still fire, **and that any logic moved to an entity `IUpserts`/`IDeletes` hook still runs** (e.g. the duplicate/uniqueness checks).
- **Route-compatibility check:** assert each migrated entity's produced routes byte-match the old `api/[controller]` routes (clients depend on them). Same for every controller converted to a minimal API (Phases 3–5) — the route, verb, TypeAuth requirement, and response shape must be unchanged.
- **No-controllers assertion:** a test that reflects over the ShiftIdentity assemblies and fails if any `ControllerBase` subclass remains (allow-list any deliberately-kept view-rendering MVC controller).
- **🚨 Replication-map guard (per migrated entity):** assert the entity's Cosmos `Entity → *Model` map **still resolves** after trimming its profile — e.g. `mapper.ConfigurationProvider.AssertConfigurationIsValid()` plus an explicit `mapper.Map<BrandModel>(new Brand{…})` returning a populated document. This is the one regression that **compiles fine and only fails in production replication**, so it must be a test, not a code review. See §2 "TRIM, never DELETE".
- Reference tests already in the `StockPlusPlus` sample (in the `ShiftTemplates` repo) for the generated-mapper patterns: `Tests/SourceGeneratedMappingTests.cs`, `Tests/DeepMappingTests.cs`, `Tests/AttributeEndpointTests.cs`, `Tests/AttributeEndpointMapperDiscoveryTests.cs`.

---

## 7. Conclusion (fill in when done — then roll up into `.shift`)

_To be written at the end. Capture: which entities landed on which rung; how many repository classes were deleted outright (entity hooks vs thin repos); the final mechanism chosen for feature-locking and the protected-row guard; any new `ShiftEntity` APIs added; the minimal-API conversion of all controllers (and any MVC controller kept as a deliberate exception); surprises; and remaining follow-ups (join entities). Then copy this into `C:\repos\ShiftSoftware\.shift\repos\shift-identity\` and thin this file to a pointer._

---

## 8. Progress log

| Date | Phase/Entity | What changed | Framework changes | Notes |
|------|--------------|--------------|-------------------|-------|
| 2026-07-18 | **Phase 3 fix (FINAL)** — Company/CompanyBranch LIST scope-ids | Runtime `InvalidOperationException: could not be translated` on a **filtered** `GET api/IdentityCompanyBranch` (the Team-form branch `<ShiftAutocomplete>`'s `$filter=CompanyId eq X`). **Final fix: project the case+type-mismatched scalar IDs via `ForList`** — `CompanyId`/`CityId`/`RegionId` on CompanyBranch, `ParentCompanyID` on Company — so the **generated mapper serves the whole list and AutoMapper is fully removed** (no `ProjectTo`/`IMapper`/restored profile maps). | **None** — deferred the framework-wide generator change (see Notes). | **Two false starts before the real cause.** (1) First "fixed" it by reverting the LIST to AutoMapper `ProjectTo`; the unfiltered grid worked, so it looked done. (2) The Team dropdown still failed → traced it to the **filtered** path. **Real root cause (proven by `ToQueryString()`):** the filter target `CompanyId` was **never projected** — DTO `CompanyId` (string) vs entity `CompanyID` (`long?`), and the generated list convention is **case-sensitive** AND lacks `long?→string`, so it skipped it. No projected scalar → EF inlines the collection-bearing member-init into the `WHERE` → untranslatable. A hand-written flat `Select` **without** `CompanyId` throws identically; **with** it, translates — so NOT a shape/`Include`/collection issue (my earlier "member-init shape" note was wrong). AutoMapper only worked because its convention is case-insensitive + does `long→string`. **Considered but rejected:** full generator fix (exact-first case-insensitive match + `long?→string` in the list convention) = ~20-site framework-wide refactor (every convention emits the entity accessor from the DTO name, so all must switch to the matched member's real name); **deferred** to a focused ShiftEntity task in favour of the local `ForList`. **Verified:** `CompanyBranchListTranslationTests.cs` (3) drive the repo's real generated `MapToList` + filter through `ToQueryString()`, asserting translation + `CONVERT(varchar(20),[c].[CompanyID])`. Full `StockPlusPlus.Test` **139/139**; host + `ShiftIdentity.Data` build clean (no new SHENGEN004). **Rule of thumb going forward:** any list-DTO scalar whose name/type differs from its entity member (`Xxxid`←`XxxID`, string←long) must be `ForList`-projected or it won't be filterable. |
| 2026-07-18 | **Phase 3** (AccessTree, Team, Company, CompanyBranch, CompanyCalendar) | All five migrated; **AccessTree/Team/CompanyCalendar repos deleted** (Rung B), **Company/CompanyBranch kept as thin Rung-C repos** for `ApplyPostODataProcessing`; **5 controllers deleted**; profiles trimmed (AccessTree + CompanyCalendar deleted — no `*Model` map). Routes unchanged. `GetCalendarEvents` → minimal API. | **None** (used existing `IConfigures`/`IUpserts`/`IDeletes` hooks, `ForView`/`ForList`/`ForEntity`, `ForViewChildren`/`ForEntityChildren` nested child config, and the repo-builder `UseGeneratedMapper`). | **Refined the Rung-C shape:** Company/CompanyBranch are the first entities whose endpoint **consumes** the surviving custom repo (App's was CRUD-orthogonal) → repository-typed attribute `[…, TRepository]`, mapper config in the **repo builder** (not `IConfiguresShiftRepository`, which is built-in-only + SHENGEN006; `UseGeneratedMapper=true` illegal on the repo variant), entity implements only `IUpsertsShiftRepository`. **AccessTree**: tree-gen before `Base()`, re-assign Tree after. **Team**: M:N sync unified for insert+update. **CompanyBranch**: City/Region derivation via injected built-in `ShiftRepository<…,City/Region,…>`; dead CompanyID-immutability clause dropped. **CompanyCalendar** was the HARDEST mapper (not "simplest"): nested hashid `Departments`/`Brands` in the JSON children needed `ForViewChildren`/`ForEntityChildren` + `IHashIdService`; the profile had no `*Model` map so it's fully deleted (corrects §2 inventory). **New infra:** `MapShiftIdentityDashboard()` (`EndpointRouteBuilderExtensions.cs`) hosts custom minimal APIs; host calls it under `#if internalShiftIdentityHosting`. ⚠ `GetCalendarEvents` kept authenticated-only (old action had no explicit TypeAuth gate) — tighten to `.RequireTypeAuthRead(CompanyCalendars)` if desired. Tests: `IdentityAttributeEndpointDiscoveryTests` extended to **12 endpoints** (built-in vs custom-repo). `ShiftIdentity.sln` + `StockPlusPlus.API` build clean; full suite **136/136**. Residual: entity-hook runtime behaviour lacks an identity CRUD integration test (ShiftIdentity ships none). |
| 2026-07-18 | **Entity relocation `.Core` → `.Data`** (Phase 2 prerequisite) | Moved the **entire** `ShiftIdentity.Core/Entities/` (21 entities) **and** `IRepositories/` (`IAppRepository`, `IUserRepository`) into `ShiftIdentity.Data`, so entities can carry EF Core repository hooks without leaking EF Core into the Blazor RCLs. **Namespaces renamed** `…Core.Entities` → `…Data.Entities` and `…Core.IRepositories` → `…Data.IRepositories` (a **breaking** package-API change — see the Phase 2 note; ~90 files updated across ShiftIdentity + the StockPlusPlus sample via a word-boundary-anchored replace so `ShiftEntity.EFCore.Entities` was spared). | **None** (no ShiftEntity change — the blocker was ShiftIdentity's layering, not a framework gap). | Straggler fixes (things in `.Core` that referenced entities): `ShiftIdentityFeatureLocking` (stays in Core — host config surface) rekeyed its type→flag map from `typeof(Country)` to the entity **name** string (behaviourally identical, entity-free); `IAppRepository`/`IUserRepository` moved to Data (only server-side consumers). Endpoint entities gained an explicit `using ShiftSoftware.ShiftIdentity.Core;` (for `ShiftIdentityActions`, previously resolved by namespace nesting). Wiring: generator analyzer ProjectReference moved Core.csproj → **Data.csproj**; `AddShiftIdentityDataAssembly()` + `RegisterShiftRepositories(...)` re-keyed from the Core assembly to the **Data** (`Marker`) assembly; `ShiftIdentity.AspNetCore` gained a `→ ShiftIdentity.Data` ProjectReference (its auth/token services use the `User`/`App` entities). `ShiftIdentity.sln` + `StockPlusPlus.API` build clean; verified the generator emits `Generated_*` mappers on the Data assembly. **This is the one-time enabler that makes the entity-hook ladder (Phases 3–4) work for ShiftIdentity.** ⚠ Downstream (ADP + any uncloned consumer) must update `using`s on upgrade; migration snapshots need nothing (EF diff is empty — verified — the entity-key strings are string literals that auto-refresh on the next migration). |
| 2026-07-18 | **Phase 2** (Country, Region, City, App) | All four migrated to attribute-driven endpoints; **3 repositories deleted** (Country/Region/City), App's repo slimmed to a plain `IAppRepository`; **4 controllers deleted**; profiles trimmed (App's deleted). Routes unchanged (`api/Identity{Country,Region,City,App}`). | **None.** | **Country** → Rung A. **Region** → Rung B (`IConfiguresShiftRepository`: Include + `ForList` for flattened `Country`/`CountryDisplayOrder`; view select-DTO Text preserved by the generated FK convention). **City** → Rung B with **both** hooks (plan said "as Region" but City also denormalises `CountryID` from the Region → `IUpsertsShiftRepository`, set before `Base()` for the data-level check). **App** → **Rung C** (plan said Rung B, but `IAppRepository.GetAppAsync` for the OAuth `AuthCodeService` forces a surviving repo): attribute + built-in repo + generated mapper, dup-`AppId` check → `IUpsertsShiftRepository`, `AppRepository` reduced to a plain-class `GetAppAsync`. `CompanyBranchRepository` (Phase 3) decoupled from the deleted City/Region repos by injecting the built-in open-generic `ShiftRepository<DB, City/Region, …>` (FindAsync calls kept verbatim). New tests: `IdentityAttributeEndpointDiscoveryTests` (12, DB-free). Full suite 124/126 (2 = Cosmos emulator down). |
| 2026-07-14 | — | Scope change: **all** controllers → minimal APIs | — | Bespoke/standalone controllers (UserManager, PublicUser, Sync, ReverseTypeAuthLookup, both AuthControllers) now in scope as new **Phase 5**; CRUD+custom-endpoint controllers (User, CompanyCalendar) fully deleted, custom endpoints become minimal APIs; end state = zero MVC controllers. Cleanup renumbered to Phase 6. |
| 2026-07-17 | Framework | Register the built-in repository **unconditionally**. | **ShiftEntity:** in `RegisterShiftRepositories` (`ShiftEntity.EFCore/Extensions/IServiceCollectionExtensions.cs`), moved `services.TryAddScoped(typeof(ShiftRepository<,,,>))` **out of** the `if (endpointSpecs.Count > 0)` guard. The open generic is now registered whenever `RegisterShiftRepositories` is called, regardless of whether the scanned assembly declares any `[ShiftEntity*Endpoint*]`. Rationale: `ShiftRepository<,,,>` carries no per-entity state, so one open-generic registration serves every entity — and a programmer can now inject `ShiftRepository<DB, Entity, ListDTO, ViewDTO>` directly without a repository subclass or an attribute endpoint. Harmless when unused, idempotent (`TryAdd`). Full ShiftEntity suite 384 green; ShiftIdentity.sln + StockPlusPlus.API build clean. Does **not** remove ShiftIdentity's own `RegisterShiftRepositories(ShiftIdentity.Core)` call — that is still needed for the per-endpoint generated-mapper / DTO-map / TypeAuth-action registration. | — |
| 2026-07-16 | Plan hardening | Made **"AutoMapper profiles: TRIM, never DELETE"** a first-class rule (§2) so the Cosmos-replication trap can't recur. | — | Added the keep/remove rule + a **verified per-profile inventory** of every `CreateMap` (which to remove, which to keep, which files legitimately become empty: only `AccessTree`, `App`; `General.cs` = never touch). Rewrote the 5 remaining "delete the profile" instructions in Phases 2/3/6 to "trim". Added a **replication-map guard** to §6 testing (assert `Entity → *Model` still resolves per migrated entity) — this is the one regression that compiles fine and only fails in production replication. Found the rule is **broader than replication**: `User`'s profile must also keep `→ UserDataDTO` / `→ UserInfoDTO` (UserManager/password/verification flows), and `CompanyCalendar` has 4 child maps to re-check. Verified Phase 1's 3 trimmed profiles still hold both `*Model` maps. |
| 2026-07-16 | **Phase 1 + 0.4–0.6** | Brand, Service, Department → Rung A (attribute-driven, **3 repositories + 3 controllers deleted**). 0.4/0.5/0.6 closed. | **ShiftEntity:** none (no framework change needed). **Infra:** `ShiftIdentity.Core.csproj` gained a dev-mode `OutputItemType="Analyzer"` ProjectReference to `ShiftEntity.SourceGenerator` — without it the generator never runs in dev mode and `UseGeneratedMapper=true` throws at startup. Verified 3 mappers emitted (`Generated_{Brand,Service,Department}_…g.cs`). | **ShiftIdentity:** endpoint attributes on the 3 entities (routes `api/IdentityBrand|IdentityService|IdentityDepartment`, unchanged); deleted `Brand/Service/DepartmentRepository` + `Identity{Brand,Service,Department}Controller`; **trimmed** their AutoMapper profiles (kept the Cosmos `*Model` replication maps — deleting them would break replication); dropped their `AddScoped<…Repository>()`; `AddShiftIdentityDashboard` now calls `RegisterShiftRepositories(ShiftIdentity.Core)`; new `ShiftEntityOptions.AddShiftIdentityDataAssembly()`. **Host/template:** `StockPlusPlus.API/Program.cs` calls `x.AddShiftIdentityDataAssembly()` under `#if internalShiftIdentityHosting`. **Verified:** ShiftIdentity.sln + StockPlusPlus.API build clean; ShiftEntity suite 384 green; discovery harness confirms 3 secure endpoints with resolved TypeAuth actions + generated mappers + built-in repository. |
| 2026-07-16 | Plan revision | Absorbed two framework capabilities that landed independently (ShiftEntity commits `9888442` entity-driven upsert/delete hooks, `32be85d`/`1349fa4` auto deep-mapping). | **§3.4** entity write hooks (`IUpsertsShiftRepository`/`IDeletesShiftRepository`, `context.Base()`); **§3.5** auto deep mapping to depth 10 (`ShiftEntityMapperDefaults.MaxDepth`, `[ShiftEntityMapperMaxDepth]`, `[ShiftEntityMapperIgnore]`). No code written — plan-only update. | **Ladder changed:** write logic no longer forces a repository class, so rung B now covers it and rung C is reserved for `ApplyPostODataProcessing`/`GetIQueryable`/`Print`/`SaveChanges`/extra public methods. **Reclassified: AccessTree C→B and Team C→B (repos deleted entirely); Company + CompanyBranch → thin C (only `ApplyPostODataProcessing`); User keeps its repo for `IUserRepository` + public methods.** Explicit `ForXxxChild(ren)` dropped from the CompanyBranch/Team/User mapper steps (deep children are automatic). |
| 2026-07-14 | Rename (full) | `IShiftEntityBuiltInProtected`/`BuiltIn` → **`IShiftEntityProtectable`/`IsProtected`** everywhere (effect-based framework name). | **ShiftEntity:** renamed interface + member + 2 guard sites + message + Phase0 tests; renamed all **8 Cosmos replication models** `BuiltIn`→`IsProtected` (breaking wire change, per decision). | **ShiftIdentity:** 6 entities → plain `bool IsProtected` (**SQL column renamed**, no `[Column]` shim); renamed 15 repo + 11 sync + 6 seed call sites; removed the 8 temporary AutoMapper `ForMember` bridges (convention now maps by name). `Constants.BuiltIn*` row-name strings kept. **Migration:** `StockPlusPlus.Data/Migrations/20260714083851_RenameBuiltInToIsProtected.cs` (EF-generated, 6 `RenameColumn`s, reversible). ShiftEntity 356 tests green; ShiftIdentity.sln + StockPlusPlus.Data build clean. ⚠ downstream Cosmos consumers/stored docs must be coordinated. |
| 2026-07-14 | Phase 0 (0.1–0.3) | Implemented + tested the three framework enablers; identity glue wired. 0.4–0.6 deferred to Phase 1 (Brand). | **ShiftEntity:** `IShiftEntityBuiltInProtected` (Core) + Upsert/Delete guard; `IShiftEntitySaveValidator` (EFCore) + `RunSaveValidators()` in `ShiftRepository.SaveChangesAsync`; built-in repo baselines `DefaultDataLevelAccessOptions` from DI in `InitCommon`. Tests: `ShiftEntity.Tests/Repository/Phase0EnablerTests.cs` (9 green; full suite 356 green). | **ShiftIdentity:** Country/Region/City/Company/CompanyBranch/User implement the marker; `ShiftIdentityFeatureLocking.GetLockedMessageKey(Type)`; `FeatureLockSaveValidator`; `AddShiftIdentityDashboard` registers the validator + the base-type data-level default. `ShiftIdentity.sln` + `StockPlusPlus.Data` build clean against the modified framework. **Note:** per-repo feature-lock overrides & BuiltIn guards still present (redundant, harmless) — removed per entity in Phases 1–4. Corrected §3.2 BuiltIn set (App→out, CompanyBranch→in). |

---

## Appendix A — Reference: the new ShiftEntity features (opt-in shapes)

Entity-attribute variants (placed **on the entity class**, `AllowMultiple`):
- `[ShiftEntityEndpoint<List,View>("route")]` — built-in repo + AutoMapper.
- `… { UseGeneratedMapper = true }` — built-in repo + generated mapper (cannot combine with a custom repo or `WithMapper`).
- `[ShiftEntityEndpoint<List,View,TRepository>("route")]` — custom repo + AutoMapper.
- `[ShiftEntityEndpointWithMapper<List,View,TMapper>("route")]` — built-in repo + explicit mapper.
- `[ShiftEntitySecureEndpoint<List,View,TActionTree>("route", nameof(Tree.Action))]` — secure; `+TRepository` and `WithMapper` secure variants exist.

Wiring: `services.RegisterShiftRepositories(dataAssembly)` + `app.MapShiftEntityEndpoints<ShiftIdentityDbContext>()`.
Built-in repo fallback: when no `TRepository`, the map path closes `ShiftRepository<DB,E,L,V>` automatically.

**The three entity-side hooks (all keyed by the DTO triple — this is what removes repository classes):**
- `IConfiguresShiftRepository<E,L,V>` — `Include`s, `UseGeneratedMapper(cfg)`, data-level, filters. **Built-in-repo only** (a custom repo configures itself via its options builder).
- `IUpsertsShiftRepository<E,L,V>` — take over upsert; `context.Base()` = `base.UpsertAsync(…)`. **Fires even with a custom repo** (if it calls base).
- `IDeletesShiftRepository<E,L,V>` — take over delete; `context.Base()` = `base.DeleteAsync(…)`. Same firing rules.
- There is **no** entity hook for `ApplyPostODataProcessing` / `GetIQueryable` / `PrintAsync` / `SaveChangesAsync` — those are the only remaining reasons for a repository class.

Generated mapper: `[ShiftEntityMapper]` partial (customize via `Configure`) or `UseGeneratedMapper()`; conventions covered = scalars, `long↔string`, `enum↔int`, FK↔`ShiftEntitySelectDTO`, file JSON↔`List<ShiftFileDTO>`, audit fields, `MapToList` projection, and **automatic deep child composition to depth 10** (`ShiftEntityMapperDefaults.MaxDepth`; tune via `[ShiftEntityMapperMaxDepth(n)]` / `map.MaxDepth(n)`; prune via `[ShiftEntityMapperIgnore]` / `map.Ignore(…)`). Per-property overrides via `ShiftMapperBuilder`: `ForView/ForEntity/ForList/ForCopy`; `ForViewChild(ren)/ForEntityChild(ren)/ForListChild(ren)` are only for composing **beyond** the depth cap or overriding a member's auto-deep.
Mapping seam in `ShiftRepository`: the four `MapToView/MapToEntity/MapToList/CopyEntity` virtuals are mapper concerns (absorb into the generated mapper); `Upsert`/`Delete` now have entity hooks; `SaveChanges/GetIQueryable/ApplyPostODataProcessing/Print` remain repository-only.

## Appendix B — Key source locations
- Attributes: `ShiftEntity.Core/Endpoints/ShiftEntityEndpointAttributes.cs`
- Discovery: `ShiftEntity.Core/Endpoints/ShiftEntityEndpointDiscovery.cs`
- DI registration: `ShiftEntity.EFCore/Extensions/IServiceCollectionExtensions.cs` (`RegisterShiftRepositories`)
- Route mapping: `ShiftEntity.Web/Endpoints/ShiftEntityEndpointsMapExtensions.cs`, `ShiftEntityGeneratedEndpoints.cs`, `ShiftEntityEndpointRouteBuilderExtensions.cs`
- Built-in repo + virtual seam: `ShiftEntity.EFCore/ShiftRepository.cs`
- Light config hook: `ShiftEntity.EFCore/IConfiguresShiftRepository.cs`
- Options surface: `ShiftEntity.EFCore/ShiftRepositoryOptions.cs`
- Generator: `ShiftEntity.SourceGenerator/ShiftEntityMapperGenerator.cs`
- Mapper interfaces/registry: `ShiftEntity.Core/IShiftEntityMapper.cs`, `IShiftObjectMapper.cs`, `ShiftEntityMapperRegistry.cs`, `ShiftMapperBuilder.cs`
- Working precedents (StockPlusPlus sample, in `ShiftTemplates` repo): `StockPlusPlus.Data/Repositories/CountryRepository.cs`, `Entities/Country.cs`, `Mappers/CountryMapper.cs`, and the mapping/attribute test suites.
