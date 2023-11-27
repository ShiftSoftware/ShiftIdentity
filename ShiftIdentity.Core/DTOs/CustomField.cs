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

    public CustomFieldBase()
    {}

    public CustomFieldBase(bool isPassword, bool isEncrypted)
    {
        this.IsPassword = isPassword;
        this.IsEncrypted = isEncrypted;
    }
}

public class CustomField: CustomFieldBase
{
    public string Value { get; set; } = default!;

    public CustomField()
    {}

    public CustomField(bool isPassword, bool isEncrypted) : base(isPassword, isEncrypted)
    {}

    public CustomField(CustomFieldBase customFieldBase)
    { 
        this.Set(customFieldBase);
    }

    public CustomField(string value, bool isPassword = false, bool isEncrypted = false) : base(isPassword, isEncrypted)
    {
        this.Value= value;
    }

    public CustomField Set(CustomFieldBase customFieldBase)
    {
        this.IsPassword = customFieldBase.IsPassword;
        this.IsEncrypted = customFieldBase.IsEncrypted;
        return this;
    }
}
