using ShiftSoftware.ShiftEntity.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core;

public class CustomFieldDTO
{
    public bool IsPassword { get; set; }
    public bool IsEncrypted { get; set; }
    public string DisplayName { get; set; } = default!;
    public string? Value { get; set; } = default!;
    public bool HasValue { get; set; }

    public CustomFieldDTO()
    {
        
    }

    public CustomFieldDTO(CustomFieldBase customFieldBase)
    {
        Set(customFieldBase);
    }

    public CustomFieldDTO Set(CustomFieldBase customFieldBase)
    {
        IsPassword = customFieldBase.IsPassword;
        IsEncrypted = customFieldBase.IsEncrypted;
        DisplayName = customFieldBase.DisplayName;
        return this;
    }
}
