//
//    Copyright (C) Microsoft.  All rights reserved.
//

using System.Management.Automation;

namespace Microsoft.PowerShell.Commands
{
    /// <summary>
    /// This class implements Remove-PSBreakpoint 
    /// </summary>
    [Cmdlet(VerbsCommon.Remove, "PSBreakpoint", SupportsShouldProcess = true, DefaultParameterSetName = "Breakpoint",
        HelpUri = "http://go.microsoft.com/fwlink/?LinkID=113375")]
    public class RemovePSBreakpointCommand : PSBreakpointCommandBase
    {
        /// <summary>
        /// Removes the given breakpoint
        /// </summary>
        protected override void ProcessBreakpoint(Breakpoint breakpoint)
        {
            this.Context.Debugger.RemoveBreakpoint(breakpoint);
        }
    }
}