using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs;

public class CustomFieldBase
{
    public bool IsEncrypted { get; set; }

    public CustomFieldBase()
    {}

    public CustomFieldBase(bool isEncrypted)
    {
        this.IsEncrypted = isEncrypted;
    }
}

public class CustomField: CustomFieldBase
{
    public string Value { get; set; } = default!;

    public CustomField()
    {}

    public CustomField(bool isEncrypted) : base(isEncrypted)
    {}

    public CustomField(string value, bool isEncrypted=false): base(isEncrypted)
    {
        this.Value= value;
    }
}
