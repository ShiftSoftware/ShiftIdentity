using ShiftMapper;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using System.Globalization;

namespace ShiftSoftware.ShiftIdentity.Data.Replication;

/// <summary>
/// The <c>Entity → *Model</c> maps Cosmos replication uses for the ShiftIdentity domain, as a ShiftMapper profile.
/// The trigger side (<c>SetUpAllIdentityReplications</c>) and the catch-up side (<c>ReplicateAllAsync</c>) register
/// their <c>Replicate</c> / <c>UpdateReference</c> / <c>UpdatePropertyReference</c> calls WITHOUT a mapping delegate,
/// and the replication pipeline maps every document through the host's registered <see cref="IShiftMapper"/> — so
/// the host has to carry these 19 pairs. It does so without asking: the identity registrations
/// (<c>AddShiftIdentityDashboard&lt;TDbContext&gt;()</c> on the API side, the Functions worker's
/// <c>AddShiftIdentity(issuer, key)</c>) register the ready-made <see cref="ShiftIdentityReplicationMapper"/> — this
/// profile and nothing else — through <c>AddShiftIdentityReplicationMapper()</c>. A host that wires replication
/// without either calls that method itself; a host with a mapper of its own may also
/// <c>AddProfile&lt;IdentityReplicationProfile&gt;()</c> there, and the pipeline uses the last registered mapper that
/// declares the pair it needs.
/// <para>
/// Every pair reproduces the AutoMapper profile it descends from EXACTLY — the members AutoMapper filled by
/// convention, its explicit <c>ForMember</c> overrides, and the <c>ReplicationModel</c> audit fields (<c>IsDeleted</c>,
/// <c>CreateDate</c>, <c>LastSaveDate</c>, <c>CreatedByUserID</c>, <c>LastSavedByUserID</c>, all matched by name and
/// converted by the built-in table, <c>long?</c> → <c>string</c> keeping its null). The documents are pinned by the
/// goldens in <c>StockPlusPlus.Test/Tests/ReplicationMappingParityTests.cs</c>: change a map only when the document is
/// MEANT to change, and change the golden with it.
/// </para>
/// <para>
/// The transcription rules every map below follows, because getting one wrong is invisible — replication swallows
/// per-row failures and still stamps a clean watermark:
/// </para>
/// <list type="number">
/// <item><description><b><c>ID</c> is ignored on every map.</b> <c>ReplicationModel.ID</c> is an override whose setter
/// writes <c>id</c>. Both would match the entity's <c>ID</c> (one exactly, one case-insensitively), and a join row's
/// <c>id</c> is customised to a FOREIGN key — so a convention-written <c>ID</c> could land after it and overwrite it
/// with the row's own key. Ignoring <c>ID</c> leaves exactly one writer.</description></item>
/// <item><description><b>Navigation reads are ternaries</b>, never <c>?.</c> (not allowed in an expression tree).
/// The M:N join is inserted carrying only its FK, so its navigation is null at replication time; AutoMapper
/// null-propagated to a null name, and so does the ternary.</description></item>
/// <item><description><b>A value-typed key behind a null navigation is <c>0</c>, not null</b> — AutoMapper's
/// <c>default(long)</c> — so <c>TeamCompanyBranch</c>'s <c>BranchID</c> is <c>"0"</c>. Live document content in a
/// partitioned store; do not "fix" it.</description></item>
/// <item><description><b><c>BranchID</c> is ignored on the three merge maps</b> (Brand/Service/Department →
/// sub-item). Those run through the update overload ONTO the stored document, and <c>BranchID</c> is that
/// container's partition key: ignored means untouched, so it survives the merge.</description></item>
/// </list>
/// </summary>
public class IdentityReplicationProfile : ShiftMapperProfile
{
    public IdentityReplicationProfile()
    {
        // ─────────────────────────────── Brand ───────────────────────────────
        CreateMap<Brand, BrandModel>()
            .ForMember(d => d.ID, o => o.Ignore());

        // Merge map (UpdateReference): applied ONTO the existing sub-item document, BranchID untouched.
        CreateMap<Brand, CompanyBranchSubItemModel>()
            .ForMember(d => d.ID, o => o.Ignore())
            .ForMember(d => d.BranchID, o => o.Ignore())
            .ForMember(d => d.ItemType, o => o.MapFrom(s => CompanyBranchContainerItemTypes.Brand));

        // ─────────────────────────────── Service ───────────────────────────────
        CreateMap<Service, ServiceModel>()
            .ForMember(d => d.ID, o => o.Ignore());

        CreateMap<Service, CompanyBranchSubItemModel>()
            .ForMember(d => d.ID, o => o.Ignore())
            .ForMember(d => d.BranchID, o => o.Ignore())
            .ForMember(d => d.ItemType, o => o.MapFrom(s => CompanyBranchContainerItemTypes.Service));

        // ─────────────────────────────── Department ───────────────────────────────
        CreateMap<Department, DepartmentModel>()
            .ForMember(d => d.ID, o => o.Ignore());

        CreateMap<Department, CompanyBranchSubItemModel>()
            .ForMember(d => d.ID, o => o.Ignore())
            .ForMember(d => d.BranchID, o => o.Ignore())
            .ForMember(d => d.ItemType, o => o.MapFrom(s => CompanyBranchContainerItemTypes.Department));

        // ───────── Branch sub-items produced FRESH from the M:N join entities ─────────
        // `id` is the FOREIGN key (the service/department/brand the row points at), not the join row's own ID —
        // MapFromSource hands the long to the conversion table, which formats it invariantly like every other id.
        // Name/IntegrationId come through the navigation, which is null on the bare join (see the class remarks).
        CreateMap<CompanyBranchService, CompanyBranchSubItemModel>()
            .ForMember(d => d.ID, o => o.Ignore())
            .ForMember(d => d.id, o => o.MapFromSource(s => s.ServiceID))
            .ForMember(d => d.Name, o => o.MapFrom(s => s.Service == null ? null! : s.Service.Name))
            .ForMember(d => d.IntegrationId, o => o.MapFrom(s => s.Service == null ? null : s.Service.IntegrationId))
            .ForMember(d => d.BranchID, o => o.MapFromSource(s => s.CompanyBranchID))
            .ForMember(d => d.ItemType, o => o.MapFrom(s => CompanyBranchContainerItemTypes.Service));

        CreateMap<CompanyBranchDepartment, CompanyBranchSubItemModel>()
            .ForMember(d => d.ID, o => o.Ignore())
            .ForMember(d => d.id, o => o.MapFromSource(s => s.DepartmentID))
            .ForMember(d => d.Name, o => o.MapFrom(s => s.Department == null ? null! : s.Department.Name))
            .ForMember(d => d.IntegrationId, o => o.MapFrom(s => s.Department == null ? null : s.Department.IntegrationId))
            .ForMember(d => d.BranchID, o => o.MapFromSource(s => s.CompanyBranchID))
            .ForMember(d => d.ItemType, o => o.MapFrom(s => CompanyBranchContainerItemTypes.Department));

        CreateMap<CompanyBranchBrand, CompanyBranchSubItemModel>()
            .ForMember(d => d.ID, o => o.Ignore())
            .ForMember(d => d.id, o => o.MapFromSource(s => s.BrandID))
            .ForMember(d => d.Name, o => o.MapFrom(s => s.Brand == null ? null! : s.Brand.Name))
            .ForMember(d => d.IntegrationId, o => o.MapFrom(s => s.Brand == null ? null : s.Brand.IntegrationId))
            .ForMember(d => d.BranchID, o => o.MapFromSource(s => s.CompanyBranchID))
            .ForMember(d => d.ItemType, o => o.MapFrom(s => CompanyBranchContainerItemTypes.Brand));

        // Embedded under TeamModel.CompanyBranches. Here `id` IS the join row's own ID (convention), and BranchID
        // is the branch's ID through the navigation — "0" when it is not loaded (rule 3 in the class remarks).
        CreateMap<TeamCompanyBranch, CompanyBranchSubItemModel>()
            .ForMember(d => d.ID, o => o.Ignore())
            .ForMember(d => d.Name, o => o.MapFrom(s => s.CompanyBranch == null ? null! : s.CompanyBranch.Name))
            .ForMember(d => d.IntegrationId, o => o.MapFrom(s => s.CompanyBranch == null ? null : s.CompanyBranch.IntegrationId))
            .ForMember(d => d.BranchID, o => o.MapFromSource(s => s.CompanyBranch == null ? 0 : s.CompanyBranch.ID))
            .ForMember(d => d.ItemType, o => o.MapFrom(s => CompanyBranchContainerItemTypes.Branch));

        // ─────────────────────────────── Country ───────────────────────────────
        // CountryID is the entity's own ID, NOT its same-named CountryID column (the AutoMapper override). RegionID
        // has no source on Country and stays null — it exists only because Country, Region and City share a container.
        CreateMap<Country, CountryModel>()
            .ForMember(d => d.ID, o => o.Ignore())
            .ForMember(d => d.CountryID, o => o.MapFromSource(s => s.ID))
            .ForMember(d => d.RegionID, o => o.Ignore())
            .ForMember(d => d.ItemType, o => o.MapFrom(s => CountryContainerItemTypes.Country));

        // ─────────────────────────────── Region ───────────────────────────────
        // RegionID is the entity's own ID, not its RegionID column (same override as Country).
        CreateMap<Region, RegionModel>()
            .ForMember(d => d.ID, o => o.Ignore())
            .ForMember(d => d.RegionID, o => o.MapFromSource(s => s.ID))
            .ForMember(d => d.ItemType, o => o.MapFrom(s => CountryContainerItemTypes.Region));

        // Nested under CompanyBranchModel.City.Region: no ItemType, and the ids are STRINGS here (the table converts
        // CountryID's long? and RegionID's long). Country composes through the Country → CountryModel map above.
        CreateMap<Region, CityRegionModel>()
            .ForMember(d => d.ID, o => o.Ignore())
            .ForMember(d => d.RegionID, o => o.MapFromSource(s => s.ID));

        // ─────────────────────────────── City ───────────────────────────────
        CreateMap<City, CityModel>()
            .ForMember(d => d.ID, o => o.Ignore())
            .ForMember(d => d.ItemType, o => o.MapFrom(s => CountryContainerItemTypes.City));

        // Nested under CompanyBranchModel.City. Region composes through Region → CityRegionModel.
        CreateMap<City, CityCompanyBranchModel>()
            .ForMember(d => d.ID, o => o.Ignore());

        // ─────────────────────────────── Company ───────────────────────────────
        CreateMap<Company, CompanyModel>()
            .ForMember(d => d.ID, o => o.Ignore());

        // ─────────────────────────────── CompanyBranch ───────────────────────────────
        // City (→ Region → Country) and Company compose through the maps above; both navigations are nullable on the
        // entity, which is what makes the in-memory map null-guard them. WebsiteURL has no source and stays null.
        // The coordinates are parsed with the INVARIANT culture — the repository writes them that way, so this is
        // the symmetric read; a culture-blind parse read "44.1" as 441 on a comma-decimal server.
        CreateMap<CompanyBranch, CompanyBranchModel>()
            .ForMember(d => d.ID, o => o.Ignore())
            .ForMember(d => d.BranchID, o => o.MapFromSource(s => s.ID))
            .ForMember(d => d.ItemType, o => o.MapFrom(s => CompanyBranchContainerItemTypes.Branch))
            .ForMember(d => d.WebsiteURL, o => o.Ignore())
            .ForMember(d => d.Location, o => o.MapFrom(s =>
                string.IsNullOrWhiteSpace(s.Longitude) || string.IsNullOrWhiteSpace(s.Latitude)
                    ? null
                    : new Location(new decimal[]
                    {
                        decimal.Parse(s.Longitude, CultureInfo.InvariantCulture),
                        decimal.Parse(s.Latitude, CultureInfo.InvariantCulture),
                    })));

        // ─────────────────────────────── Team ───────────────────────────────
        // CompanyBranches is the one nested collection whose names differ: TeamModel.CompanyBranches is filled from
        // Team.TeamCompanyBranches. RecognizePrefixes("Team") is how ShiftMapper says that — the bare name is always
        // tried first, so TeamID still binds to TeamID and `id` to ID — and the elements compose through the
        // TeamCompanyBranch → CompanyBranchSubItemModel map above.
        CreateMap<Team, TeamModel>(o => o.RecognizePrefixes("Team"))
            .ForMember(d => d.ID, o => o.Ignore());

        // ─────────────────────────────── User ───────────────────────────────
        CreateMap<User, UserModel>()
            .ForMember(d => d.ID, o => o.Ignore());
    }
}
