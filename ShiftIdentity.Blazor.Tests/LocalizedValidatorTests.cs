using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftBlazor.Extensions;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Shared;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

[Trait("Category", "Ui")]
public sealed class LocalizedValidatorTests
{
    // The neutral culture shows the English keys. The other cultures prove that the messages go through the identity
    // localizer, including an attribute's own ErrorMessage and the short messages for attributes without one.
    [Theory]
    [InlineData("", "This field is required.", "This value is too long.", "Passwords must match.")]
    [InlineData("ar-IQ", "هذا الحقل مطلوب.", "هذه القيمة طويلة جدًا.", "يجب أن تتطابق كلمتا المرور.")]
    [InlineData("ku", "ئەم خانەیە پێویستە.", "ئەم بەهایە زۆر درێژە.", "دەبێت هەردوو وشە نهێنییەکە وەک یەک بن.")]
    [InlineData("ru", "Это поле обязательно.", "Слишком длинное значение.", "Пароли должны совпадать.")]
    public void Submitting_shows_each_message_in_the_current_culture(string culture, string required, string tooLong, string mismatch)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            using var context = Context();
            var cut = RenderForm(context, new Entry { Code = "12345", Password = "one", Confirm = "two" });
            cut.Find("form").Submit();
            Assert.Equal([required, tooLong, mismatch], cut.FindAll(".validation-message").Select(x => x.TextContent));
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public void A_changed_field_is_validated_again_and_only_its_own_message_changes()
    {
        using var context = Context();
        var entry = new Entry { Code = "12345", Password = "one", Confirm = "two" };
        EditContext? editContext = null;
        var cut = RenderForm(context, entry, x => editContext = x);
        cut.Find("form").Submit();
        entry.Confirm = "one";
        cut.InvokeAsync(() => editContext!.NotifyFieldChanged(FieldIdentifier.Create(() => entry.Confirm)));
        Assert.Equal(["This field is required.", "This value is too long."], cut.FindAll(".validation-message").Select(x => x.TextContent));
    }

    private static IRenderedComponent<EditForm> RenderForm(BunitContext context, Entry entry, Action<EditContext>? capture = null) =>
        context.Render<EditForm>(p => p.Add(x => x.Model, entry).Add(x => x.ChildContent, (RenderFragment<EditContext>)(editContext => builder =>
        {
            capture?.Invoke(editContext);
            builder.OpenComponent<LocalizedValidator>(0);
            builder.CloseComponent();
            var sequence = 1;
            foreach (var field in new[] { "Name", "Code", "Confirm" })
            {
                builder.OpenComponent<ValidationMessage<string>>(sequence++);
                builder.AddAttribute(sequence++, "For", field switch
                {
                    "Name" => (System.Linq.Expressions.Expression<Func<string>>)(() => entry.Name),
                    "Code" => () => entry.Code,
                    _ => () => entry.Confirm
                });
                builder.CloseComponent();
            }
        })));

    private static BunitContext Context()
    {
        var context = new BunitContext();
        context.Services.AddShiftBlazor(o => o.ShiftConfiguration = c => c.BaseAddress = "https://identity.invalid/");
        context.Services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    private sealed class Entry
    {
        [Required] public string Name { get; set; } = "";
        [Required, MaxLength(4)] public string Code { get; set; } = "";
        public string Password { get; set; } = "";
        [Compare(nameof(Password), ErrorMessage = "Passwords must match.")] public string Confirm { get; set; } = "";
    }
}
