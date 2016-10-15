/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Management.Automation;
using System.Management.Automation.Internal;

namespace Microsoft.PowerShell.Commands
{
    /// <summary>
    /// The implementation of the "new-alias" cmdlet
    /// </summary>
    /// 
    [Cmdlet(VerbsCommon.New, "Alias", SupportsShouldProcess = true, HelpUri = "http://go.microsoft.com/fwlink/?LinkID=113352")]
    [OutputType(typeof(AliasInfo))]
    public class NewAliasCommand : WriteAliasCommandBase
    {
        #region Command code

        /// <summary>
        /// The main processing loop of the command.
        /// </summary>
        /// 
        protected override void ProcessRecord()
        {
            // If not force, then see if the alias already exists

            if (!Force)
            {
                AliasInfo existingAlias = null;
                if (String.IsNullOrEmpty(Scope))
                {
                    existingAlias = SessionState.Internal.GetAlias(Name);
                }
                else
                {
                    existingAlias = SessionState.Internal.GetAliasAtScope(Name, Scope);
                }


                if (existingAlias != null)
                {
                    // Throw if alias exists and is private...
                    SessionState.ThrowIfNotVisible(this.CommandOrigin, existingAlias);

                    // Since the alias already exists, write an error.

                    SessionStateException aliasExists =
                        new SessionStateException(
                            Name,
                            SessionStateCategory.Alias,
                            "AliasAlreadyExists",
                            SessionStateStrings.AliasAlreadyExists,
                            ErrorCategory.ResourceExists);

                    WriteError(
                        new ErrorRecord(
                            aliasExists.ErrorRecord,
                            aliasExists));
                    return;
                }
            }

            // Create the alias info

            AliasInfo newAlias =
                new AliasInfo(
                    Name,
                    Value,
                    Context,
                    Option);

            newAlias.Description = Description;

            string action =
                AliasCommandStrings.NewAliasAction;

            string target =
                    StringUtil.Format(AliasCommandStrings.NewAliasTarget, Name, Value);

            if (ShouldProcess(target, action))
            {
                // Set the alias in the specified scope or the
                // current scope.

                AliasInfo result = null;

                try
                {
                    if (String.IsNullOrEmpty(Scope))
                    {
                        result = SessionState.Internal.SetAliasItem(newAlias, Force, MyInvocation.CommandOrigin);
                    }
                    else
                    {
                        result = SessionState.Internal.SetAliasItemAtScope(newAlias, Scope, Force, MyInvocation.CommandOrigin);
                    }
                }
                catch (SessionStateException sessionStateException)
                {
                    WriteError(
                        new ErrorRecord(
                            sessionStateException.ErrorRecord,
                            sessionStateException));
                    return;
                }
                catch (PSArgumentOutOfRangeException argOutOfRange)
                {
                    WriteError(
                        new ErrorRecord(
                            argOutOfRange.ErrorRecord,
                            argOutOfRange));
                    return;
                }
                catch (PSArgumentException argException)
                {
                    WriteError(
                        new ErrorRecord(
                            argException.ErrorRecord,
                            argException));
                    return;
                }

                // Write the alias to the pipeline if PassThru was specified

                if (PassThru && result != null)
                {
                    WriteObject(result);
                }
            }
        } // ProcessRecord
        #endregion Command code
    } // class NewAliasCommand
}//Microsoft.PowerShell.Commands

