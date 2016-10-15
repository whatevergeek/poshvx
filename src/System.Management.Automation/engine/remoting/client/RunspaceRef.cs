/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System.Collections.ObjectModel;
using System.Management.Automation.Internal;
using System.Management.Automation.Runspaces;
using System.Management.Automation.Runspaces.Internal;
using Dbg = System.Management.Automation.Diagnostics;

namespace System.Management.Automation.Remoting
{
    /// <summary>
    /// Provides a reference to a runspace that can be used to temporarily
    /// push a remote runspace on top of a local runspace. This is
    /// primary used by Start-PSSession. The purpose of this class is to hide
    /// the CreatePipeline method and force it to be used as defined in this
    /// class.
    /// </summary>
    internal class RunspaceRef
    {
        /// <summary>
        /// Runspace ref.
        /// </summary>
        private ObjectRef<Runspace> _runspaceRef;
        private bool _stopInvoke;
        private object _localSyncObject;
        private static RobustConnectionProgress s_RCProgress = new RobustConnectionProgress();

        /// <summary>
        /// Constructor for RunspaceRef.
        /// </summary>
        internal RunspaceRef(Runspace runspace)
        {
            Dbg.Assert(runspace != null, "Expected runspace != null");
            _runspaceRef = new ObjectRef<Runspace>(runspace);
            _stopInvoke = false;
            _localSyncObject = new object();
        }

        /// <summary>
        /// Revert.
        /// </summary>
        internal void Revert()
        {
            _runspaceRef.Revert();

            lock (_localSyncObject)
            {
                _stopInvoke = true;
            }
        }

        /// <summary>
        /// Runspace.
        /// </summary>
        internal Runspace Runspace
        {
            get
            {
                return _runspaceRef.Value;
            }
        }

        internal Runspace OldRunspace
        {
            get { return _runspaceRef.OldValue; }
        }

        /// <summary>
        /// Is runspace overridden.
        /// </summary>
        internal bool IsRunspaceOverridden
        {
            get
            {
                return _runspaceRef.IsOverridden;
            }
        }

        /// <summary>
        /// Parse ps command using script block.
        /// </summary>
        private PSCommand ParsePsCommandUsingScriptBlock(string line, bool? useLocalScope)
        {
            try
            {
                // Extract execution context from local runspace.
                Runspace localRunspace = _runspaceRef.OldValue;
                ExecutionContext context = localRunspace.ExecutionContext;

                // This is trusted input as long as we're in FullLanguage mode
                bool isTrustedInput = (localRunspace.ExecutionContext.LanguageMode == PSLanguageMode.FullLanguage);

                // Create PowerShell from ScriptBlock.
                ScriptBlock scriptBlock = ScriptBlock.Create(context, line);
                PowerShell powerShell = scriptBlock.GetPowerShell(context, isTrustedInput, useLocalScope, null);
                return powerShell.Commands;
            }
            catch (ScriptBlockToPowerShellNotSupportedException e)
            {
                CommandProcessorBase.CheckForSevereException(e);
            }
            catch (System.Management.Automation.RuntimeException e)
            {
                CommandProcessorBase.CheckForSevereException(e);
            }

            // If parsing failed return null.
            return null;
        }

        /// <summary>
        /// Create ps command.
        /// </summary>
        internal PSCommand CreatePsCommand(string line, bool isScript, bool? useNewScope)
        {
            // Fall-back to traditional approach if runspace is not pushed.
            if (!this.IsRunspaceOverridden)
            {
                return CreatePsCommandNotOverriden(line, isScript, useNewScope);
            }

            // Try to parse commands as script-block.
            PSCommand psCommand = ParsePsCommandUsingScriptBlock(line, useNewScope);

            // If that didn't work fall back to traditional approach.
            if (psCommand == null)
            {
                return CreatePsCommandNotOverriden(line, isScript, useNewScope);
            }

            // Otherwise return the psCommandCollection we got.
            return psCommand;
        }

        /// <summary>
        /// Creates the PSCommand when the runspace is not overridden
        /// </summary>
        private PSCommand CreatePsCommandNotOverriden(string line, bool isScript, bool? useNewScope)
        {
            PSCommand command = new PSCommand();

            if (isScript)
            {
                if (useNewScope.HasValue)
                {
                    command.AddScript(line, useNewScope.Value);
                }
                else
                {
                    command.AddScript(line);
                }
            }
            else
            {
                if (useNewScope.HasValue)
                {
                    command.AddCommand(line, useNewScope.Value);
                }
                else
                {
                    command.AddCommand(line);
                }
            }

            return command;
        }

        /// <summary>
        /// Create pipeline.
        /// </summary>
        internal Pipeline CreatePipeline(string line, bool addToHistory, bool useNestedPipelines)
        {
            // This method allows input commands to work against no-language runspaces. If a runspace
            // is pushed, it tries to parse the line using a ScriptBlock object. If a runspace is not
            // pushed, or if the parsing fails, in these cases it reverts to calling CreatePipeline
            // using the unparsed line.

            Pipeline pipeline = null;

            // In Start-PSSession scenario try to create a pipeline by parsing the line as a script block.
            if (this.IsRunspaceOverridden)
            {
                // Win8: exit should work to escape from the restrictive session
                if ((_runspaceRef.Value is RemoteRunspace) &&
                    (!string.IsNullOrEmpty(line) && string.Equals(line.Trim(), "exit", StringComparison.OrdinalIgnoreCase)))
                {
                    line = "Exit-PSSession";
                }

                PSCommand psCommand = ParsePsCommandUsingScriptBlock(line, null);
                if (psCommand != null)
                {
                    pipeline = useNestedPipelines ?
                        _runspaceRef.Value.CreateNestedPipeline(psCommand.Commands[0].CommandText, addToHistory) :
                        _runspaceRef.Value.CreatePipeline(psCommand.Commands[0].CommandText, addToHistory);

                    pipeline.Commands.Clear();

                    foreach (Command command in psCommand.Commands)
                    {
                        pipeline.Commands.Add(command);
                    }
                }
            }

            // If that didn't work out fall-back to the traditional approach.
            if (pipeline == null)
            {
                pipeline = useNestedPipelines ?
                    _runspaceRef.Value.CreateNestedPipeline(line, addToHistory) :
                    _runspaceRef.Value.CreatePipeline(line, addToHistory);
            }

            // Add robust connection callback if this is a pushed runspace.
            RemotePipeline remotePipeline = pipeline as RemotePipeline;
            if (this.IsRunspaceOverridden && remotePipeline != null)
            {
                PowerShell shell = remotePipeline.PowerShell;
                if (shell.RemotePowerShell != null)
                {
                    shell.RemotePowerShell.RCConnectionNotification +=
                        new EventHandler<PSConnectionRetryStatusEventArgs>(HandleRCConnectionNotification);
                }

                // Add callback to write robust connection errors from stream.
                shell.ErrorBuffer.DataAdded += (sender, eventArgs) =>
                {
                    RemoteRunspace remoteRunspace = _runspaceRef.Value as RemoteRunspace;
                    PSDataCollection<ErrorRecord> erBuffer = sender as PSDataCollection<ErrorRecord>;
                    if (remoteRunspace != null && erBuffer != null &&
                        remoteRunspace.RunspacePool.RemoteRunspacePoolInternal.Host != null)
                    {
                        Collection<ErrorRecord> erRecords = erBuffer.ReadAll();
                        foreach (var er in erRecords)
                        {
                            remoteRunspace.RunspacePool.RemoteRunspacePoolInternal.Host.UI.WriteErrorLine(er.ToString());
                        }
                    }
                };
            }

            pipeline.SetHistoryString(line);

            return pipeline;
        }

        /// <summary>
        /// Create pipeline.
        /// </summary>
        internal Pipeline CreatePipeline()
        {
            return _runspaceRef.Value.CreatePipeline();
        }

        /// <summary>
        /// Create nested pipeline.
        /// </summary>
        internal Pipeline CreateNestedPipeline()
        {
            return _runspaceRef.Value.CreateNestedPipeline();
        }

        /// <summary>
        /// Override.
        /// </summary>
        internal void Override(RemoteRunspace remoteRunspace)
        {
            bool isRunspacePushed = false;
            Override(remoteRunspace, null, out isRunspacePushed);
        }

        /// <summary>
        /// Override inside a safe lock
        /// </summary>
        /// <param name="remoteRunspace">runspace to override</param>
        /// <param name="syncObject">object to use in synchronization</param>
        /// <param name="isRunspacePushed">set is runspace pushed</param>
        internal void Override(RemoteRunspace remoteRunspace, object syncObject, out bool isRunspacePushed)
        {
            lock (_localSyncObject)
            {
                _stopInvoke = false;
            }

            try
            {
                if (syncObject != null)
                {
                    lock (syncObject)
                    {
                        _runspaceRef.Override(remoteRunspace);
                        isRunspacePushed = true;
                    }
                }
                else
                {
                    _runspaceRef.Override(remoteRunspace);
                    isRunspacePushed = true;
                }

                if ((remoteRunspace.GetCurrentlyRunningPipeline() != null))
                {
                    // Don't execute command if pushed runspace is already running one.
                    return;
                }

                using (PowerShell powerShell = PowerShell.Create())
                {
                    powerShell.AddCommand("Get-Command");
                    powerShell.AddParameter("Name", new string[] { "Out-Default", "Exit-PSSession" });
                    powerShell.Runspace = _runspaceRef.Value;

                    bool isReleaseCandidateBackcompatibilityMode =
                        _runspaceRef.Value.GetRemoteProtocolVersion() == RemotingConstants.ProtocolVersionWin7RC;
                    powerShell.IsGetCommandMetadataSpecialPipeline = !isReleaseCandidateBackcompatibilityMode;
                    int expectedNumberOfResults = isReleaseCandidateBackcompatibilityMode ? 2 : 3;

                    powerShell.RemotePowerShell.HostCallReceived += new EventHandler<RemoteDataEventArgs<RemoteHostCall>>(HandleHostCall);

                    IAsyncResult asyncResult = powerShell.BeginInvoke();
                    PSDataCollection<PSObject> results = new PSDataCollection<PSObject>();

                    while (!_stopInvoke)
                    {
                        asyncResult.AsyncWaitHandle.WaitOne(1000);

                        if (asyncResult.IsCompleted)
                        {
                            results = powerShell.EndInvoke(asyncResult);
                            break;
                        }
                    }

                    if (powerShell.Streams.Error.Count > 0 || results.Count < expectedNumberOfResults)
                    {
                        throw RemoteHostExceptions.NewRemoteRunspaceDoesNotSupportPushRunspaceException();
                    }
                }
            }
            catch (Exception)
            {
                _runspaceRef.Revert();
                isRunspacePushed = false;
                throw;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eventArgs"></param>
        private void HandleHostCall(object sender, RemoteDataEventArgs<RemoteHostCall> eventArgs)
        {
            System.Management.Automation.Runspaces.Internal.ClientRemotePowerShell.ExitHandler(sender, eventArgs);
        }

        #region Robust Connection Support

        private void HandleRCConnectionNotification(object sender, PSConnectionRetryStatusEventArgs e)
        {
            switch (e.Notification)
            {
                case PSConnectionRetryStatus.NetworkFailureDetected:
                    StartProgressBar(sender.GetHashCode(), e.ComputerName, (e.MaxRetryConnectionTime / 1000));
                    break;

                case PSConnectionRetryStatus.AutoDisconnectStarting:
                case PSConnectionRetryStatus.ConnectionRetrySucceeded:
                    StopProgressBar(sender.GetHashCode());
                    break;

                case PSConnectionRetryStatus.AutoDisconnectSucceeded:
                case PSConnectionRetryStatus.InternalErrorAbort:
                    WriteRCFailedError();
                    StopProgressBar(sender.GetHashCode());
                    break;
            }
        }

        private void WriteRCFailedError()
        {
            RemoteRunspace remoteRunspace = _runspaceRef.Value as RemoteRunspace;
            if (remoteRunspace != null &&
                remoteRunspace.RunspacePool.RemoteRunspacePoolInternal.Host != null)
            {
                remoteRunspace.RunspacePool.RemoteRunspacePoolInternal.Host.UI.WriteErrorLine(
                    StringUtil.Format(RemotingErrorIdStrings.RCAutoDisconnectingError,
                    remoteRunspace.ConnectionInfo.ComputerName));
            }
        }

        private void StartProgressBar(
            long sourceId,
            string computerName,
            int totalSeconds)
        {
            RemoteRunspace remoteRunspace = _runspaceRef.Value as RemoteRunspace;
            if (remoteRunspace != null)
            {
                s_RCProgress.StartProgress(
                    sourceId,
                    computerName,
                    totalSeconds,
                    remoteRunspace.RunspacePool.RemoteRunspacePoolInternal.Host);
            }
        }

        private void StopProgressBar(
            long sourceId)
        {
            s_RCProgress.StopProgress(sourceId);
        }

        #endregion
    }
}
