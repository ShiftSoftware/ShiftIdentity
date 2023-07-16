﻿using ShiftSoftware.ShiftEntity.Core;
using System.ComponentModel.DataAnnotations;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Entities;

[TemporalShiftEntity]
[Table("AccessTrees", Schema = "ShiftIdentity")]
public class AccessTree : ShiftEntity<AccessTree>
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = default!;

    [Required]
    public string Tree { get; set; } = default!;

    public bool BuiltIn { get; set; }

    //public IEnumerable<UserAccessTree> Users { get; set; }

    public AccessTree()
    {
        //Users = new List<UserAccessTree>();
    }

}
