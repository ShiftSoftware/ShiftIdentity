using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Models;

public class SASTokenModel
{
    public string Key { get; set; }
    public int ExpireInSeconds { get; set; }
}
