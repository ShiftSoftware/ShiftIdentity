using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using ShiftSoftware.ShiftIdentity.Core.Localization;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Shared;

/// <summary>
/// Validates a form model with its data annotations, like <see cref="DataAnnotationsValidator"/>, and shows each message
/// through the identity localizer. An attribute with its own ErrorMessage uses that text as the localizer key. An
/// attribute without one gets a short message for its kind, because the default messages contain the English property
/// name and cannot be translated.
/// </summary>
public sealed class LocalizedValidator : ComponentBase, IDisposable
{
    [CascadingParameter] private EditContext? CurrentEditContext { get; set; }
    [Inject] private ShiftIdentityLocalizer Loc { get; set; } = null!;
    private EditContext? editContext;
    private ValidationMessageStore? messages;

    protected override void OnParametersSet()
    {
        if (CurrentEditContext is null)
            throw new InvalidOperationException($"{nameof(LocalizedValidator)} must be placed inside an {nameof(EditForm)}.");
        if (ReferenceEquals(CurrentEditContext, editContext)) return;
        Detach();
        editContext = CurrentEditContext;
        messages = new ValidationMessageStore(editContext);
        editContext.OnValidationRequested += ValidateModel;
        editContext.OnFieldChanged += ValidateField;
    }

    private void ValidateModel(object? sender, ValidationRequestedEventArgs args)
    {
        messages!.Clear();
        var model = editContext!.Model;
        foreach (var property in model.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            Validate(new FieldIdentifier(model, property.Name), property);
        editContext.NotifyValidationStateChanged();
    }

    private void ValidateField(object? sender, FieldChangedEventArgs args)
    {
        var field = args.FieldIdentifier;
        var property = field.Model.GetType().GetProperty(field.FieldName, BindingFlags.Public | BindingFlags.Instance);
        if (property is null) return;
        messages!.Clear(field);
        Validate(field, property);
        editContext!.NotifyValidationStateChanged();
    }

    private void Validate(FieldIdentifier field, PropertyInfo property)
    {
        if (property.GetIndexParameters().Length > 0) return;
        var value = property.GetValue(field.Model);
        // Compare and similar attributes read other properties through the context's object instance.
        var context = new ValidationContext(field.Model) { MemberName = property.Name };
        foreach (var attribute in property.GetCustomAttributes<ValidationAttribute>(true))
        {
            if (attribute.GetValidationResult(value, context) != ValidationResult.Success)
                messages!.Add(field, Loc[attribute.ErrorMessage ?? DefaultMessage(attribute, value)]);
        }
    }

    private static string DefaultMessage(ValidationAttribute attribute, object? value) => attribute switch
    {
        RequiredAttribute => "This field is required.",
        StringLengthAttribute length when value is string text && text.Length < length.MinimumLength => "This value is too short.",
        MinLengthAttribute => "This value is too short.",
        StringLengthAttribute or MaxLengthAttribute => "This value is too long.",
        _ => "This value is not valid."
    };

    private void Detach()
    {
        if (editContext is null) return;
        editContext.OnValidationRequested -= ValidateModel;
        editContext.OnFieldChanged -= ValidateField;
        messages?.Clear();
    }

    public void Dispose() => Detach();
}
