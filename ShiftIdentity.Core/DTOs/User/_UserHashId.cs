﻿using ShiftSoftware.ShiftEntity.Model.HashId;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.User;

internal class _UserHashId : JsonHashIdConverterAttribute
{
    public _UserHashId() : base(HashId.UserIdsSalt, HashId.UserIdsMinHashLength, HashId.UserIdsAlphabet)
    {

    }
}
