using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core;

public class DynamicActionFilters
{
    public bool DisableDefaultCountryFilter { get; set; }
    public bool DisableDefaultRegionFilter { get; set; }
    public bool DisableDefaultCompanyFilter { get; set; }
    public bool DisableDefaultCompanyBranchFilter { get; set; }
    public bool DisableDefaultTeamFilter { get; set; }
    public bool DisableDefaultBrandFilter { get; set; }
    public bool DisableDefaultCityFilter { get; set; }
}
