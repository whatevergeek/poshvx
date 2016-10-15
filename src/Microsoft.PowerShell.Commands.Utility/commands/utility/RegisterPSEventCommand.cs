//
//    Copyright (C) Microsoft.  All rights reserved.
//

using System;
using System.Management.Automation;

namespace Microsoft.PowerShell.Commands
{
    /// <summary>
    /// Registers for an event coming from the engine.
    /// </summary>
    [Cmdlet(VerbsLifecycle.Register, "EngineEvent", HelpUri = "http://go.microsoft.com/fwlink/?LinkID=135243")]
    [OutputType(typeof(PSEventJob))]
    public class RegisterEngineEventCommand : ObjectEventRegistrationBase
    {
        /// <summary>
        /// Parameter for an identifier for this event subscription
        /// </summary>
        [Parameter(Mandatory = true, Position = 100)]
        public new string SourceIdentifier
        {
            get
            {
                return base.SourceIdentifier;
            }
            set
            {
                base.SourceIdentifier = value;
            }
        }

        /// <summary>
        /// Returns the object that generates events to be monitored
        /// </summary>
        protected override Object GetSourceObject()
        {
            // If it's not a forwarded event, the user must specify
            // an action
            if (
                (Action == null) &&
                (!(bool)Forward)
               )
            {
                ErrorRecord errorRecord = new ErrorRecord(
                    new ArgumentException(EventingStrings.ActionMandatoryForLocal),
                    "ACTION_MANDATORY_FOR_LOCAL",
                    ErrorCategory.InvalidArgument,
                    null);

                ThrowTerminatingError(errorRecord);
            }

            return null;
        }

        /// <summary>
        /// Returns the event name to be monitored on the input object
        /// </summary>
        protected override String GetSourceObjectEventName()
        {
            return null;
        }
    }
}