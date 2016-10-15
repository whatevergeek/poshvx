﻿/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Management.Automation;
using System.Management.Automation.Internal;
using System.Management.Automation.Host;
using System.Threading;
using System.Diagnostics;

namespace Microsoft.PowerShell.ScheduledJob
{
    /// <summary>
    /// This cmdlet enables triggers on a ScheduledJobDefinition object.
    /// </summary>
    [Cmdlet(VerbsLifecycle.Disable, "JobTrigger", SupportsShouldProcess = true, DefaultParameterSetName = DisableJobTriggerCommand.EnabledParameterSet,
        HelpUri = "http://go.microsoft.com/fwlink/?LinkID=223918")]
    public sealed class DisableJobTriggerCommand : EnableDisableScheduledJobCmdletBase
    {
        #region Enabled Implementation

        /// <summary>
        /// Property to determine if trigger should be enabled or disabled.
        /// </summary>
        internal override bool Enabled
        {
            get { return false; }
        }

        #endregion
    }
}
