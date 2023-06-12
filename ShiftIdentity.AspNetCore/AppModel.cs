using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.AspNetCore;

public class AppModel
{
    public string AppId { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string RedirectUri { get; set; } = default!;
}
