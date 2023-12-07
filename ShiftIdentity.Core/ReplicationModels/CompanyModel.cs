using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using ShiftSoftware.ShiftEntity.Model.Enums;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.ReplicationModels;

public class CompanyModel : ReplicationModel
{
    public override string? ID { get; set; }

    public string Name { get; set; } = default!;
    public string? LegalName { get; set; }
    public string? ExternalId { get; set; }
    public string? AlternativeExternalId { get; set; }
    public string? ShortCode { get; set; }
    public CompanyTypes CompanyType { get; set; }
    public string? Logo { get; set; }
    public string? HQPhone { get; set; }
    public string? HQEmail { get; set; }
    public string? HQAddress { get; set; }
    public bool BuiltIn { get; set; }
    public Dictionary<string, CustomField>? CustomFields { get; set; }
}
