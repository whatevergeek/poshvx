/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System.Management.Automation;
using Dbg = System.Management.Automation;

namespace Microsoft.PowerShell.Commands
{
    /// <summary>
    /// A command that gets the active transaction.
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "Transaction", HelpUri = "http://go.microsoft.com/fwlink/?LinkID=135220")]
    [OutputType(typeof(PSTransaction))]
    public class GetTransactionCommand : PSCmdlet
    {
        /// <summary>
        /// Creates a new transaction.
        /// </summary>
        protected override void EndProcessing()
        {
            WriteObject(this.Context.TransactionManager.GetCurrent());
        }
    } // GetTransactionCommand
} // namespace Microsoft.PowerShell.Commands

