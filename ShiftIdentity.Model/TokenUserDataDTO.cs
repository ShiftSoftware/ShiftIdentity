using System.Collections.Generic;
using System;

namespace ShiftSoftware.ShiftIdentity.Model;

public class TokenUserDataDTO
{
    public long ID { get; set; } = default!;

    public string Username { get; set; } = default!;

    public string FullName { get; set; } = default!;

    public IEnumerable<string> Roles { get; set; }

    public List<EmailDTO>? Emails { get; set; }

    public List<PhoneDTO>? Phones { get; set; }

    public TokenUserDataDTO()
    {
        Roles = new List<string>();
    }
}
