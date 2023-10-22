﻿using System.Collections.Generic;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs
{

    public class TokenUserDataDTO
    {
        [ShiftEntity.Model.HashIds.UserHashIdConverter]
        public string ID { get; set; }

        public string Username { get; set; } = default!;

        public string FullName { get; set; } = default!;

        public List<EmailDTO> Emails { get; set; } = default!;

        public List<PhoneDTO> Phones { get; set; } = default!;

    }
}