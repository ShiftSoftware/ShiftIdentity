using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Model;

public class EmailDTO
{
    [DataType(DataType.EmailAddress)]
    public string Email { get; set; } = default!;

    public bool IsVerified { get; set; }
}
