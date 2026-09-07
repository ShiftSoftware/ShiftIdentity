using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Data.IdentityReference;
using ShiftSoftware.ShiftIdentity.Data.IdentityReference.Cosmos;
using ShiftIdentity.Data.Tests.Infrastructure;
using Xunit;

namespace ShiftIdentity.Data.Tests;

/// <summary>
/// What the identity reference source is allowed to remember, for how long, and what it must never
/// remember in a filtered shape.
/// </summary>
public class IdentityReferenceCacheTests
{
    // ---- The performance rule ------------------------------------------------------------------------

    [Fact]
    public async Task A_read_that_is_already_cached_allocates_nothing()
    {
        var source = SeededSource();

        // Warm every path the loop below takes, and let the generic instantiations be jitted: the first
        // call through each one does allocate, and that is not what this test is about.
        for (var warmup = 0; warmup < 3; warmup++)
        {
            await source.GetCompaniesAsync();
            await source.GetCompaniesAsync(IdentityLifecycleFilter.ActiveOnly);
            await source.ResolveCompanyAsync("1");
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        // Deliberately not awaited: a cached read hands back the task it already holds, and the point is
        // that producing it costs nothing at all. Awaiting a completed task is free, but not measuring it
        // keeps this test about the source rather than about the compiler's async machinery.
        for (var i = 0; i < 100; i++)
        {
            _ = source.GetCompaniesAsync();
            _ = source.GetCompaniesAsync(IdentityLifecycleFilter.ActiveOnly);
            _ = source.ResolveCompanyAsync("1");
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public async Task A_read_that_is_already_cached_hands_back_the_same_objects()
    {
        var source = SeededSource();

        var firstRoster = await source.GetCompaniesAsync();
        var firstActive = await source.GetCompaniesAsync(IdentityLifecycleFilter.ActiveOnly);
        var firstCompany = await source.ResolveCompanyAsync("1");

        // Same instances, not equal copies — this is what makes a hit free, and it is why nothing may write
        // to a dictionary after it has been handed out.
        Assert.Same(firstRoster, await source.GetCompaniesAsync());
        Assert.Same(firstActive, await source.GetCompaniesAsync(IdentityLifecycleFilter.ActiveOnly));
        Assert.Same(firstCompany, await source.ResolveCompanyAsync("1"));

        Assert.Equal(1, source.FamilyReads);
    }

    [Fact]
    public async Task What_a_roster_hands_back_cannot_be_written_to()
    {
        var source = SeededSource();

        var companies = await source.GetCompaniesAsync();

        // The same dictionary goes to every later caller, and with a shared cache that means every request.
        // It has to be read-only, or one caller could edit what everyone else sees.
        Assert.True(((ICollection<KeyValuePair<string, CompanyModel>>)companies).IsReadOnly);
    }

    // ---- Single-flight -------------------------------------------------------------------------------

    [Fact]
    public async Task Concurrent_misses_on_one_family_produce_one_query()
    {
        var source = SeededSource();
        source.Gate = new TaskCompletionSource();

        var readers = Enumerable
            .Range(0, 8)
            .Select(_ => Task.Run(async () => await source.GetCompaniesAsync()))
            .ToArray();

        // Let them all arrive at the load before it is allowed to finish.
        while (source.FamilyReads == 0)
            await Task.Delay(5);

        await Task.Delay(50);

        source.Gate.SetResult();

        var rosters = await Task.WhenAll(readers);

        Assert.Equal(1, source.FamilyReads);
        Assert.All(rosters, roster => Assert.Same(rosters[0], roster));
    }

    [Fact]
    public async Task Concurrent_misses_on_one_row_produce_one_query()
    {
        var source = SeededSource();
        source.Gate = new TaskCompletionSource();

        var readers = Enumerable
            .Range(0, 8)
            .Select(_ => Task.Run(async () => await source.ResolveCompanyAsync("1")))
            .ToArray();

        while (source.ItemReads == 0)
            await Task.Delay(5);

        await Task.Delay(50);

        source.Gate.SetResult();

        await Task.WhenAll(readers);

        Assert.Equal(1, source.ItemReads);
    }

    // ---- Failure -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_failed_family_read_is_not_kept()
    {
        var source = SeededSource();
        source.FailNextRead = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.GetCompaniesAsync());

        // The retry has to reach the store. If the failed load had been kept, this would either throw the
        // same exception again or answer from nothing.
        var companies = await source.GetCompaniesAsync();

        Assert.Equal(2, source.FamilyReads);
        Assert.Equal(3, companies.Count);
    }

    [Fact]
    public async Task A_failed_row_read_is_not_kept()
    {
        var source = SeededSource();
        source.FailNextRead = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ResolveCompanyAsync("1"));

        var company = await source.ResolveCompanyAsync("1");

        Assert.Equal(2, source.ItemReads);
        Assert.NotNull(company);
    }

    [Fact]
    public async Task A_row_that_does_not_exist_is_remembered_as_missing()
    {
        var source = SeededSource();

        Assert.Null(await source.ResolveCompanyAsync("404"));
        Assert.Null(await source.ResolveCompanyAsync("404"));

        // A miss is an answer. Asking again must not go back to the store for it.
        Assert.Equal(1, source.ItemReads);
    }

    // ---- What is cached, and what a resolve is allowed to read ---------------------------------------

    [Fact]
    public async Task A_filtered_roster_read_does_not_stop_a_terminated_or_deleted_row_resolving()
    {
        var source = SeededSource();

        // The poisoning attempt: fill the cache by way of the narrowest filter there is, then resolve the
        // rows that filter excluded. This is the estate-wide blank-name defect in miniature — if the
        // filtered set were what got cached, both of these would come back null.
        var active = await source.GetCompaniesAsync(IdentityLifecycleFilter.ActiveOnly);

        Assert.Equal(["1"], active.Keys);

        var terminated = await source.ResolveCompanyAsync("2");
        var deleted = await source.ResolveCompanyAsync("3");

        Assert.NotNull(terminated);
        Assert.Equal("Closed Branch Co", terminated.Name);
        Assert.NotNull(deleted);

        // And it answered them out of the superset that was already loaded, not by going back to the store.
        Assert.Equal(0, source.ItemReads);
    }

    [Fact]
    public async Task Every_filter_is_served_from_one_read_of_the_store()
    {
        var source = SeededSource();

        var all = await source.GetCompaniesAsync(IdentityLifecycleFilter.All);
        var excludeDeleted = await source.GetCompaniesAsync(IdentityLifecycleFilter.ExcludeDeleted);
        var activeOnly = await source.GetCompaniesAsync(IdentityLifecycleFilter.ActiveOnly);

        Assert.Equal(3, all.Count);
        Assert.Equal(["1", "2"], excludeDeleted.Keys.Order());
        Assert.Equal(["1"], activeOnly.Keys);

        Assert.Equal(1, source.FamilyReads);
    }

    // ---- Lifetimes -----------------------------------------------------------------------------------

    [Fact]
    public async Task Two_sources_do_not_share_what_they_remember_when_no_time_to_live_is_set()
    {
        // Two sources stand in for two requests: with no TTL, the second one starts knowing nothing, which
        // is what makes the absence of any other invalidation safe.
        var first = SeededSource();
        var second = SeededSource();

        await first.GetCompaniesAsync();
        await second.GetCompaniesAsync();

        Assert.Equal(1, first.FamilyReads);
        Assert.Equal(1, second.FamilyReads);
    }

    [Fact]
    public async Task Two_sources_share_what_they_remember_when_a_time_to_live_is_set()
    {
        var clock = new TestClock();
        var shared = new IdentityReferenceCache(TimeSpan.FromMinutes(5), clock.Read);

        var first = SeededSource(shared);
        var second = SeededSource(shared);

        await first.GetCompaniesAsync();
        var companies = await second.GetCompaniesAsync();

        Assert.Equal(1, first.FamilyReads);
        Assert.Equal(0, second.FamilyReads);
        Assert.Equal(3, companies.Count);
    }

    [Fact]
    public async Task A_shared_row_is_read_again_once_its_time_to_live_has_passed()
    {
        var clock = new TestClock();
        var shared = new IdentityReferenceCache(TimeSpan.FromMinutes(5), clock.Read);
        var source = SeededSource(shared);

        await source.GetCompaniesAsync();
        await source.ResolveCompanyAsync("1");

        clock.Advance(TimeSpan.FromMinutes(4));

        await source.GetCompaniesAsync();

        Assert.Equal(1, source.FamilyReads);

        clock.Advance(TimeSpan.FromMinutes(2));

        await source.GetCompaniesAsync();
        await source.ResolveCompanyAsync("1");

        Assert.Equal(2, source.FamilyReads);
    }

    // ---- Refresh -------------------------------------------------------------------------------------

    [Fact]
    public async Task Refreshing_a_family_replaces_the_roster_and_the_rows_under_it()
    {
        var source = SeededSource();

        await source.GetCompaniesAsync();
        Assert.Equal("Trading Co", (await source.ResolveCompanyAsync("1"))!.Name);

        source.Seed(Company("1", "Renamed Co"), Company("2", "Closed Branch Co", terminated: true), Company("3", "Deleted Co", deleted: true));

        await source.RefreshAsync(IdentityReferenceFamily.Companies);

        var companies = await source.GetCompaniesAsync();

        Assert.Equal("Renamed Co", companies["1"].Name);

        // The row a resolve would have answered from is replaced too — otherwise a refreshed family would
        // list one name and resolve another.
        Assert.Equal("Renamed Co", (await source.ResolveCompanyAsync("1"))!.Name);
        Assert.Equal(2, source.FamilyReads);
    }

    [Fact]
    public async Task Refreshing_one_row_updates_both_the_row_and_the_roster()
    {
        var source = SeededSource();

        await source.GetCompaniesAsync();

        source.Seed(Company("1", "Renamed Co"), Company("2", "Closed Branch Co", terminated: true), Company("3", "Deleted Co", deleted: true));

        await source.RefreshAsync(IdentityReferenceFamily.Companies, 1L);

        Assert.Equal("Renamed Co", (await source.ResolveCompanyAsync("1"))!.Name);

        var companies = await source.GetCompaniesAsync();

        Assert.Equal("Renamed Co", companies["1"].Name);

        // One row re-read, and the family not scanned again.
        Assert.Equal(1, source.FamilyReads);
        Assert.Equal(1, source.ItemReads);
    }

    [Fact]
    public async Task Refreshing_names_a_family_rather_than_clearing_everything()
    {
        var source = SeededSource();

        await source.GetCompaniesAsync();
        await source.GetCountriesAsync();
        await source.RefreshAsync(IdentityReferenceFamily.Companies);

        var readsBefore = source.FamilyReads;

        await source.GetCountriesAsync();

        // Countries were not asked about, so refreshing companies must not have cost them their rows.
        Assert.Equal(readsBefore, source.FamilyReads);
    }

    // ---- Snapshots -----------------------------------------------------------------------------------

    [Fact]
    public async Task A_snapshot_reads_through_to_the_store_and_then_stops_changing()
    {
        var source = SeededSource();

        await source.GetCompaniesAsync();

        source.Seed(Company("1", "Renamed Co"), Company("2", "Closed Branch Co", terminated: true), Company("3", "Deleted Co", deleted: true));

        using var snapshot = await source.LoadCompaniesSnapshotAsync();

        // Read through: a run pins rows as of its own start, not as of whenever something else warmed them.
        Assert.Equal("Renamed Co", snapshot.Find("1")!.Name);

        source.Seed(Company("1", "Renamed Again Co"), Company("2", "Closed Branch Co", terminated: true), Company("3", "Deleted Co", deleted: true));

        await source.RefreshAsync(IdentityReferenceFamily.Companies);

        // Pinned: a run that reads the same branch on its first row and its last must see the same name.
        Assert.Equal("Renamed Co", snapshot.Find("1")!.Name);
        Assert.Equal("Renamed Again Co", (await source.GetCompaniesAsync())["1"].Name);
    }

    [Fact]
    public async Task A_snapshot_leaves_what_it_read_behind_for_everyone_else()
    {
        var source = SeededSource();

        using var snapshot = await source.LoadCompaniesSnapshotAsync();

        await source.GetCompaniesAsync();

        // The run already paid for the scan; nobody should pay for it twice.
        Assert.Equal(1, source.FamilyReads);
    }

    // ---- Fixtures ------------------------------------------------------------------------------------

    private static StubIdentityReferenceSource SeededSource(IdentityReferenceCache? cache = null)
    {
        var source = new StubIdentityReferenceSource(cache);

        source.Seed(
            Company("1", "Trading Co"),
            Company("2", "Closed Branch Co", terminated: true),
            Company("3", "Deleted Co", deleted: true));

        source.Seed(new CountryModel { id = "1", Name = "Iraq", ItemType = CountryContainerItemTypes.Country, CallingCode = "964" });

        return source;
    }

    private static CompanyModel Company(string id, string name, bool terminated = false, bool deleted = false)
        => new()
        {
            id = id,
            Name = name,
            IsDeleted = deleted,
            TerminationDate = terminated ? DateTime.UtcNow.AddYears(-1) : null,
        };

    private sealed class TestClock
    {
        private long milliseconds = 1_000_000;

        public long Read() => Volatile.Read(ref this.milliseconds);

        public void Advance(TimeSpan by) => Volatile.Write(ref this.milliseconds, this.milliseconds + (long)by.TotalMilliseconds);
    }
}
