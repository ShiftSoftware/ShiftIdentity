namespace ShiftSoftware.ShiftIdentity.Core;

public enum ShiftIdentityHostingTypes
{
    /// <summary>
    /// The Identity is Hosted Alongside an Existing App. Sharing the Server, UI, and the Database
    /// </summary>
    Internal = 1,

    /// <summary>
    /// The Identity is Hosted as a Stand-Alone (External App). With it's own Server, UI, and Database
    /// </summary>
    External = 2,
}
