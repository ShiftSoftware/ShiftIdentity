using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs;

public class CustomFieldBase
{
    public bool IsPassword { get; set; }
    public bool IsEncrypted { get; set; }
    public string DisplayName { get; set; } = default!;

    public CustomFieldBase()
    {}

    public CustomFieldBase(string displayName, bool isPassword, bool isEncrypted)
    {
        this.IsPassword = isPassword;
        this.IsEncrypted = isEncrypted;
        this.DisplayName = displayName;
    }
}

public class CustomField: CustomFieldBase
{
    public string Value { get; set; } = default!;

    public CustomField()
    {}

    public CustomField(string displayName, bool isPassword, bool isEncrypted) : base(displayName, isPassword, isEncrypted)
    {}

    public CustomField(CustomFieldBase customFieldBase)
    { 
        this.Set(customFieldBase);
    }

    public CustomField(string value, CustomFieldBase customFieldBase) : this(customFieldBase)
    {
        this.Value= value;
    }

    public CustomField Set(CustomFieldBase customFieldBase)
    {
        this.IsPassword = customFieldBase.IsPassword;
        this.IsEncrypted = customFieldBase.IsEncrypted;
        this.DisplayName = customFieldBase.DisplayName;
        return this;
    }
}
