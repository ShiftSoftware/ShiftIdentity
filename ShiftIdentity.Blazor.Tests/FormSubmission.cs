using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Shared;

namespace ShiftIdentity.Blazor.Tests;

/// <summary>Submits a rendered security form the way a press does.</summary>
internal static class FormSubmission
{
    /// <summary>
    /// A step form (<see cref="AuthForm"/>) is submitted through its form element, so its checks, the closing of a shown
    /// error and the one-attempt-at-a-time rule all run. A deployed form (a plain EditForm) goes straight to its
    /// valid-submit handler, as these tests have always done.
    /// </summary>
    public static Task SubmitAsync<T>(IRenderedComponent<T> cut, string selector = "form") where T : class, IComponent =>
        cut.FindComponents<AuthForm>().Count > 0 ? cut.Find(selector).SubmitAsync()
            : cut.InvokeAsync(() => cut.FindComponent<EditForm>().Instance.OnValidSubmit.InvokeAsync(new EditContext(new object())));
}
