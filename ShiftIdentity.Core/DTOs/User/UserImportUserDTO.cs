using System.ComponentModel.DataAnnotations;
using System;
using FileHelpers;
using ShiftSoftware.ShiftEntity.Model.HashIds;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.User;

[FileHelpers.DelimitedRecord(","), FileHelpers.IgnoreFirst]
public class UserImportUserDTO
{
    [Required]
    [MaxLength(255)]
    [FieldQuoted(QuoteMode.OptionalForBoth)]
    public string FullName { get; set; } = default!;

    [Required]
    [MaxLength(255)]
    [FieldQuoted(QuoteMode.OptionalForBoth)]
    public string Username { get; set; } = default!;

    [MaxLength(30)]
    [Phone]
    [FieldQuoted(QuoteMode.OptionalForBoth)]
    public string? Phone { get; set; }

    [MaxLength(255)]
    [EmailAddress]
    [Required]
    [FieldQuoted(QuoteMode.OptionalForBoth)]
    public string Email { get; set; } = default!;

    [DataType(DataType.Date)]
    [FieldQuoted(QuoteMode.OptionalForBoth)]
    [FieldConverter(ConverterKind.Date, "yyyy/MM/dd")]
    public DateTime? BirthDate { get; set; }

    [Required]
    [FieldQuoted(QuoteMode.OptionalForBoth)]
    [CompanyBranchHashIdConverter]
    public string CompanyBranchID { get; set; } = default!;
}
