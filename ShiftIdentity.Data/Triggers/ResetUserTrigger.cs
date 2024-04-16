using EntityFrameworkCore.Triggered;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Data.Triggers;

internal class ResetUserTrigger : IBeforeSaveTrigger<User>
{
    public Task BeforeSave(ITriggerContext<User> context, CancellationToken cancellationToken)
    {
        if (context.ChangeType != ChangeType.Modified)
            return Task.CompletedTask;

        //If email is changed, reset email-verfied
        if (context.Entity?.Email?.ToLower() != context.UnmodifiedEntity?.Email?.ToLower())
        {
            context.Entity!.EmailVerified = false;
            context.Entity!.VerificationSASToken = null;
        }

        //If phone is changed, reset phone-verfied
        if (context.Entity?.Phone?.ToLower() != context.UnmodifiedEntity?.Phone?.ToLower())
        {
            context.Entity!.PhoneVerified = false;
        }

        return Task.CompletedTask;
    }
}
