using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.Replication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.ReplicationModels;

public class CompanyBranchModel : ReplicationModel
{
    public override string? ID { get; set; }
    public string Name { get; set; } = default!;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? ExternalId { get; set; } = default!;
    public string? ShortCode { get; set; }

    public string? Latitude { get; set; }
    public string? Longitude { get; set; }

    public string? Photos { get; set; }

    public bool BuiltIn { get; set; }

    public CityCompanyBranchModel City { get; set; }
    public CompanyModel Company { get; set; }

    //Partition keys
    public string BranchID { get; set; } = default!;
    public string ItemType { get; set; } = default!;
    public Dictionary<string, CustomField>? CustomFields { get; set; }
}
