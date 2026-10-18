using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.TypeAuth.Core;
using Xunit;

namespace ShiftIdentity.Data.Tests;

/// <summary>
/// An access tree save (an access tree, or a user's own tree) stores exactly what was submitted, or refuses: an admin
/// changes only what is within their reach, in either direction, so whatever they remove they can give back.
/// </summary>
public class ExactAccessTreeSaveTests
{
    private const string Admin = """{"ShiftIdentityActions":{"Users":["r","w"]}}""";

    private static TypeAuthContext Build(string tree) =>
        new TypeAuthContextBuilder().AddActionTree<ShiftIdentityActions>().AddAccessTree(tree).Build();

    private static string Save(string submitted, string? stored = null) =>
        ExactAccessTreeSave.Generate(Build(submitted), Build(Admin), stored == null ? null : Build(stored), Localizer());

    private static Message Refusal(string submitted, string? stored = null) =>
        Assert.Throws<ShiftEntityException>(() => Save(submitted, stored)).Message;

    private static string Lines(Message refusal) =>
        string.Join(" | ", refusal.SubMessages!.Select(x => x.Title + ": " + string.Join("; ", x.SubMessages!.Select(y => $"{y.Title}={y.Body}"))));

    [Fact]
    public void A_tree_within_the_admins_reach_is_stored()
    {
        var tree = Save("""{"ShiftIdentityActions":{"Users":["r"]}}""", """{"ShiftIdentityActions":{"Users":["r","w"]}}""");

        Assert.True(Build(tree).CanRead(ShiftIdentityActions.Users));
        Assert.False(Build(tree).CanWrite(ShiftIdentityActions.Users));
    }

    // As the dashboard posts it: the stored tree as it is, with the grants beyond the admin left untouched.
    [Fact]
    public void Grants_beyond_the_admin_left_as_they_are_are_stored()
    {
        const string stored = """{"ShiftIdentityActions":{"Users":["r","w","d"],"AccessTrees":["r"]}}""";

        var tree = Save("""{"ShiftIdentityActions":{"Users":["r","w","d"],"AccessTrees":["r"]}}""", stored);

        Assert.True(Build(tree).CanDelete(ShiftIdentityActions.Users));
        Assert.True(Build(tree).CanRead(ShiftIdentityActions.AccessTrees));
    }

    [Fact]
    public void A_grant_the_admin_does_not_hold_refuses_the_save()
    {
        var refusal = Refusal("""{"ShiftIdentityActions":{"Users":["r","w","d"]}}""");

        Assert.Equal("Error", refusal.Title);
        Assert.Equal("This access tree has changes that you can not make", refusal.Body);
        Assert.Equal("You can not grant: Users=Delete", Lines(refusal));
    }

    // The admin couldn't give Delete back, so they can't remove it.
    [Fact]
    public void Removing_a_grant_the_admin_could_not_give_back_refuses_the_save()
    {
        var refusal = Refusal("""{"ShiftIdentityActions":{"Users":["r"]}}""", """{"ShiftIdentityActions":{"Users":["r","w","d"]}}""");

        Assert.Equal("You can not remove: Users=Delete", Lines(refusal));
    }

    [Fact]
    public void Both_kinds_are_listed_together()
    {
        var refusal = Refusal("""{"ShiftIdentityActions":{"Users":["r","w","d"]}}""", """{"ShiftIdentityActions":{"AccessTrees":["r"]}}""");

        Assert.Equal("You can not grant: Users=Delete | You can not remove: Access Trees=Read", Lines(refusal));
    }

    private static ShiftIdentityLocalizer Localizer() =>
        new(new ServiceCollection().AddSingleton<IStringLocalizerFactory>(new KeysAsTexts()).BuildServiceProvider(), typeof(object));

    // Each text is its key, as for a key with no translation.
    private sealed class KeysAsTexts : IStringLocalizerFactory, IStringLocalizer
    {
        public IStringLocalizer Create(Type resourceSource) => this;
        public IStringLocalizer Create(string baseName, string location) => this;
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
