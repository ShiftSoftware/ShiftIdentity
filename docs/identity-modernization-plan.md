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
| **B. CRUD attribute + light config** | No custom endpoints, but needs `Include`s, data-level access, or a small mapper tweak. | Rung A **plus** `IConfiguresShiftRepository<E,L,V>` on the entity (includes / `UseGeneratedMapper(cfg)` / data-level). Still no repo class, no controller. |
| **C. CRUD attribute + custom repository** | Genuine business logic in `Upsert`/`Delete`/`SaveChanges`/`ApplyPostODataProcessing` (validation, TypeAuth tree generation, M:N child sync, uniqueness, side-effects). | `[ShiftEntitySecureEndpoint<List,View,ActionTree,TRepository>(…)]` → keep a custom `TRepository`, but **strip all pure-mapping code out of it** into the generated mapper. |
| **D. CRUD extension + custom endpoints** | Needs extra endpoints beyond the 8 standard CRUD routes. | Do **not** rely on the attribute alone. Call `MapShiftEntitySecureCrud<…>(prefix, action)` manually in wiring, then add sibling `MapPost/MapGet` for the custom endpoints on the same prefix. Combine with rung B or C for the repo/mapper. **Delete the MVC controller entirely** — base CRUD *and* the custom endpoints are all minimal APIs. |

**The mapping-vs-Upsert rule (the key cleanup):**
- Code in a repo `Upsert` that just copies `dto.X → entity.X` (field assignment, FK↔`ShiftEntitySelectDTO`, file/JSON columns) is **the mapper's job** → move to `MapToEntity` (generated).
- Code that *validates*, *checks uniqueness*, *generates TypeAuth trees*, *syncs M:N join rows*, *hashes passwords*, or *enforces guards* is **genuine business logic** → stays in the custom repo `Upsert`.
- The AutoMapper `Profile` (view + list projections) → moves to the generated mapper (`MapToView` / `MapToList`), with `ForView`/`ForList`/`ForViewChild(ren)` config only for the non-conventional members.

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

> **Rule for the whole effort:** if adopting a pattern needs a framework capability that doesn't exist yet, **build it in `ShiftEntity` first**, land it, then continue the entity migration. Record every such framework change in the Progress Log (§6) and in the eventual `.shift` roll-up.

---

## 4. Entity inventory & classification

Repos live in `ShiftIdentity.Data/Repositories/`, controllers in `ShiftIdentity.Dashboard.AspNetCore/Controllers/`, AutoMapper profiles in `ShiftIdentity.Data/AutoMapperProfiles/`.

Legend — **Rung** is the target from §2. **Guards** = BuiltIn(B)/FeatureLock(L). **Blockers** = what must exist before it can move.

| Entity | Repo overrides today | Custom endpoints | Target rung | Notes |
|--------|----------------------|------------------|-------------|-------|
| **Brand** | SaveChanges(L) | 0 | **A** | Simplest. Only blocker: §3.1. Pilot for Tier 1. |
| **Service** | SaveChanges(L) | 0 | **A** | As Brand. |
| **Department** | SaveChanges(L) | 0 | **A** | As Brand. |
| **Country** | Upsert(B), Delete(B), SaveChanges(L) | 0 | **A** | Needs §3.1 + §3.2. Mirrors the already-modernized `StockPlusPlus` Country sample — copy that. |
| **Region** | Upsert(B), Delete(B), SaveChanges(L), Include(Country) | 0 | **B** | As Country + `Include` via `IConfiguresShiftRepository`. |
| **City** | Upsert(B), Delete(B), SaveChanges(L), Include(Region→Country) | 0 | **B** | As Region. |
| **App** | Upsert(?), SaveChanges(L) | 0 | **B/C** | `App,AppDTO,AppDTO` (same DTO for list+view). **Read `AppRepository.Upsert` at implementation** to decide B vs C. |
| **AccessTree** | Upsert(TypeAuth tree gen — business), SaveChanges(L) | 0 | **C** | Keep repo for tree generation + name-uniqueness. Mapper is trivial → generated. |
| **CompanyCalendar** | SaveChanges(L), Include | 1 (`GetCalendarEvents`) | **D** (+B) | Custom endpoint → CRUD extension. Otherwise light. |
| **Company** | Upsert(business: phone, circular-ref), Delete(B), ApplyPostODataProcessing, SaveChanges(L), CustomFields map | 0 | **C** | Keep repo for phone/circular-ref/post-OData. Move CustomFields + `ParentCompany`/`Brands` projections to generated mapper w/ config. |
| **Team** | Upsert(M:N Users + Branches sync, business), SaveChanges(L) | 0 | **C** | Keep repo for M:N sync + duplicate-user check. Mapper → generated (has `Include`s). |
| **CompanyBranch** | Upsert(B, M:N Services/Departments/Brands), Delete(B), ApplyPostODataProcessing, SaveChanges(L) | 0 | **C** | Largest non-User (206 lines). Heavy mapper (lat/long parse, CustomFields, display-orders, M:N select projections) → generated + `ForView/ForList/ForViewChildren` config. Is BuiltIn-guarded (was missing from the earlier §3.2 list). |
| **User** | Upsert(466-line business), Delete(B), SaveChanges | **6** (+ `UserManagerController` 9) | **D** (+C) | **Last.** CRUD extension for base + custom endpoints; keep heavy repo; move field-copy + view/list projection to generated mapper. |

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
- [ ] **0.4** ⏸ **Deferred to Phase 1 (Brand).** Wiring `RegisterShiftRepositories(dataAssembly)` + the host's `MapShiftEntityEndpoints<…>()` is only meaningful once the first entity carries an attribute, and the route mapping lives in the *consuming app's* Program.cs (ShiftIdentity ships libraries, not a host). Feasibility confirmed: refs resolve, discovery is additive, and it coexists with the classic controllers. Do it together with Brand.
- [ ] **0.5** ⏸ **Partially verified.** `ShiftIdentityActions.*` are `public readonly static ReadWriteDeleteAction` fields, which `nameof(...)` + the framework's `ShiftEntityEndpointActionResolver` resolve. Runtime confirmation lands with Brand (Phase 1).
- [ ] **0.6** ⏸ **Deferred to Phase 1.** `Blazor`/`Dashboard.Blazor` generator changes (if any) are only needed once discovery is driven from the data assembly — revisit with the first attribute entity.

**Pilot proof (this phase):** the three framework enablers are proven end-to-end in `ShiftEntity.Tests/Repository/Phase0EnablerTests.cs` (9 tests, all green; full ShiftEntity suite 356 green). The full attribute-endpoint end-to-end on a *real identity entity* is folded into Phase 1's Brand (ShiftIdentity has no test host yet, and wiring the attribute pipeline before an entity uses it is premature). **Exit criterion for the remaining 0.4–0.6:** Brand (Rung A) serves list/view/create/update/delete + TypeAuth + feature-lock + BuiltIn guard with no repo/controller/profile.

### Phase 1 — Tier 1: pure CRUD attribute (Brand → Service → Department)
For **each** entity, in order:
- [ ] **Brand**: add `[ShiftEntitySecureEndpoint<BrandListDTO,BrandDTO,ShiftIdentityActions>("api/identitybrand", nameof(ShiftIdentityActions.Brands))] { UseGeneratedMapper = true }` on `Brand`; delete `BrandRepository`, `IdentityBrandController`, `AutoMapperProfiles/Brand.cs`; verify feature-lock still enforced (via Phase 0.1). Run tests.
- [ ] **Service**: same shape.
- [ ] **Department**: same shape.
- [ ] Keep the **route strings identical** to today's `api/[controller]` output so clients/Blazor don't break (e.g. `IdentityBrand`). Verify the produced route matches the old controller route exactly.

### Phase 2 — Tier 2: CRUD attribute + BuiltIn guard + light config (Country → Region → City → App)
- [ ] **Country**: Rung A. Delete repo/controller/profile; `UseGeneratedMapper = true`. Cross-check against the `StockPlusPlus` `api/country-generated` sample as the reference implementation.
- [ ] **Region**: Rung B. Add `IConfiguresShiftRepository<Region,RegionListDTO,RegionDTO>` on `Region` for `Include(Country)` + `UseGeneratedMapper`. Delete repo/controller/profile.
- [ ] **City**: Rung B. `Include(Region→Country)`. Delete repo/controller/profile.
- [ ] **App**: read `AppRepository.Upsert` → if guard-only, Rung B; if it has real logic, Rung C (keep a slim repo). Note `App` uses `AppDTO` for both list & view.

### Phase 3 — Tier 3: custom repository, mapping extracted (AccessTree → Company → Team → CompanyBranch → CompanyCalendar)
For each: keep a custom `TRepository` wired via `[ShiftEntitySecureEndpoint<List,View,ActionTree,TRepository>(…)]`, **delete the controller**, **delete the AutoMapper profile** (replace with generated mapper + config), and **strip pure mapping out of `Upsert`**.
- [ ] **AccessTree**: repo keeps TypeAuth tree generation + name-uniqueness; `Name` assignment stays only if it's post-tree logic; mapper generated. Remove feature-lock override (Phase 0.1).
- [ ] **Company**: repo keeps phone validation + circular-reference check + `ApplyPostODataProcessing`; move `ParentCompany`/`Brands`/`CustomFields` projections to the generated mapper (`ForView`/`ForList` for `CustomFields` password-stripping and the `Brands` aggregation). Remove BuiltIn + lock overrides.
- [ ] **Team**: repo keeps duplicate-user check + M:N `TeamUsers`/`TeamCompanyBranches` sync + `Tags`; mapper generated (with `Include`s via config). Remove lock override.
- [ ] **CompanyBranch**: repo keeps M:N children sync + `ApplyPostODataProcessing`; generated mapper handles lat/long parse (`ForView`), display-order projections (`ForList`), CustomFields, and the M:N select-DTO projections (`ForViewChildren`/`ForListChildren`). Remove BuiltIn + lock overrides. This is the hardest mapper — budget extra time.
- [ ] **CompanyCalendar** (rung B + D — the simplest custom-endpoint case; do it here as the warm-up for the minimal-API pattern that Phase 4/5 reuse): base CRUD via the CRUD extension (light repo → `IConfiguresShiftRepository` for the `Include`; remove lock override); re-add the one custom endpoint `GetCalendarEvents` (`POST`) as a sibling minimal API on the same prefix; delete `IdentityCompanyCalendarController` and the profile.

### Phase 4 — Tier 4: User (custom endpoints + heavy repo)
- [ ] **4.1** Map base CRUD via **CRUD extension** (`MapShiftEntitySecureCrud<UserRepository,User,UserListDTO,UserDTO>(…)`) so custom endpoints can sit alongside.
- [ ] **4.2** Re-add the 6 custom `IdentityUserController` endpoints (`EffectivePermissions`, `AssignRandomPasswords`, `ResetTotp`, `VerifyEmails`, `VerifyPhones`, `ImportUsers`) as sibling minimal-API endpoints on the same prefix, preserving routes + TypeAuth attributes.
- [ ] **4.3** Keep `UserRepository` business logic (uniqueness, password hashing, access-tree generation, verification-flag resets); move the plain `dto.X → entity.X` field copies + view/list projection into the generated mapper.
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
- [ ] Remove now-dead AutoMapper profiles and the `General.cs` bits that only served migrated entities (keep replication `…Model` maps that are still used).
- [ ] Ensure `AzureFunctions` + `Blazor` hosts discover the attribute endpoints consistently.
- [ ] Assert **no `Controller`/`ControllerBase` classes remain** in ShiftIdentity (except any deliberately-kept view-rendering MVC controller, recorded as an exception).
- [ ] Full regression: `dotnet build` + all identity tests; smoke-test the Dashboard Blazor UI against each migrated route.
- [ ] Write the **conclusion** (see §7) and execute the **§Housekeeping** roll-up into `.shift`.

---

## 6. Testing strategy

- After **every** entity: `dotnet build` the identity solution + run the identity test suite.
- Add/keep an integration test per migrated entity asserting the 5 CRUD operations + TypeAuth (Read/Write/Delete) + the feature-lock and BuiltIn guards still fire.
- **Route-compatibility check:** assert each migrated entity's produced routes byte-match the old `api/[controller]` routes (clients depend on them). Same for every controller converted to a minimal API (Phases 3–5) — the route, verb, TypeAuth requirement, and response shape must be unchanged.
- **No-controllers assertion:** a test that reflects over the ShiftIdentity assemblies and fails if any `ControllerBase` subclass remains (allow-list any deliberately-kept view-rendering MVC controller).
- Reference tests already in the `StockPlusPlus` sample (in the `ShiftTemplates` repo) for the generated-mapper patterns: `Tests/SourceGeneratedMappingTests.cs`, `Tests/DeepMappingTests.cs`, `Tests/AttributeEndpointTests.cs`, `Tests/AttributeEndpointMapperDiscoveryTests.cs`.

---

## 7. Conclusion (fill in when done — then roll up into `.shift`)

_To be written at the end. Capture: which entities landed on which rung; the final mechanism chosen for feature-locking and the BuiltIn guard; any new `ShiftEntity` APIs added; the minimal-API conversion of all controllers (and any MVC controller kept as a deliberate exception); surprises; and remaining follow-ups (join entities). Then copy this into `C:\repos\ShiftSoftware\.shift\repos\shift-identity\` and thin this file to a pointer._

---

## 8. Progress log

| Date | Phase/Entity | What changed | Framework changes | Notes |
|------|--------------|--------------|-------------------|-------|
| 2026-07-13 | — | Plan created | — | Inventory + classification complete; no code changed yet. |
| 2026-07-14 | — | Scope change: **all** controllers → minimal APIs | — | Bespoke/standalone controllers (UserManager, PublicUser, Sync, ReverseTypeAuthLookup, both AuthControllers) now in scope as new **Phase 5**; CRUD+custom-endpoint controllers (User, CompanyCalendar) fully deleted, custom endpoints become minimal APIs; end state = zero MVC controllers. Cleanup renumbered to Phase 6. |
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
Light per-entity config without a repo class: implement `IConfiguresShiftRepository<E,L,V>` **on the entity** (`Include`s, `UseGeneratedMapper(cfg)`, data-level, filters).
Generated mapper: `[ShiftEntityMapper]` partial (customize via `Configure`) or `UseGeneratedMapper()`; conventions covered = scalars, `long↔string`, `enum↔int`, FK↔`ShiftEntitySelectDTO`, file JSON↔`List<ShiftFileDTO>`, audit fields, `MapToList` projection, deep children via pair mappers. Per-property overrides via `ShiftMapperBuilder`: `ForView/ForEntity/ForList/ForCopy` + `ForViewChild(ren)/ForEntityChild(ren)/ForListChild(ren)`.
Mapping seam in `ShiftRepository`: the four `MapToView/MapToEntity/MapToList/CopyEntity` virtuals are mapper concerns (absorb into the generated mapper); `Upsert/Delete/SaveChanges/GetIQueryable/ApplyPostODataProcessing/Print` are business/persistence (keep in a custom repo).

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
