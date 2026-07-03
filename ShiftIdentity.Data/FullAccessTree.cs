using ShiftSoftware.TypeAuth.Core;

namespace ShiftSoftware.ShiftIdentity.Data;

public static class FullAccessTree
{
    /// <summary>
    /// Builds an access tree JSON that grants full access (Read, Write, Delete, Maximum) at the root of
    /// every given action tree — a wildcard grant that cascades to every action within them. This is the
    /// "super admin" equivalent expressed purely as a TypeAuth access tree, used both when seeding the
    /// built-in admin user and when granting existing users full access.
    /// </summary>
    public static string BuildJson(IEnumerable<Type> actionTrees)
    {
        var tree = new Dictionary<string, object>();

        foreach (var actionTree in actionTrees)
            tree[actionTree.Name] = new List<Access> { Access.Read, Access.Write, Access.Delete, Access.Maximum };

        return System.Text.Json.JsonSerializer.Serialize(tree);
    }
}
