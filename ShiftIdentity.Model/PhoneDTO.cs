using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Model;

public class PhoneDTO
{
    [DataType(DataType.PhoneNumber)]
    public string Phone { get; set; } = default!;

    public bool IsVerified { get; set; }
}
