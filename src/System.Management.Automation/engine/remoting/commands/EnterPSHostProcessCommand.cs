﻿/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Management.Automation.Internal;
using System.Management.Automation.Runspaces;
using System.Management.Automation.Remoting;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.PowerShell.Commands
{
    /// <summary>
    /// This cmdlet enters into an interactive session with the specified local process by
    /// creating a remote runspace to the process and pushing it on the current PSHost.
    /// If the selected process does not contain PowerShell then an error message will result.
    /// If the current user does not have sufficient privileges to attach to the selected process
    /// then an error message will result.
    /// </summary>
    [Cmdlet(VerbsCommon.Enter, "PSHostProcess", DefaultParameterSetName = EnterPSHostProcessCommand.ProcessIdParameterSet,
        HelpUri = "http://go.microsoft.com/fwlink/?LinkId=403736")]
    public sealed class EnterPSHostProcessCommand : PSCmdlet
    {
        #region Members

        private IHostSupportsInteractiveSession _interactiveHost;
        private RemoteRunspace _connectingRemoteRunspace;

        #region Strings

        private const string ProcessParameterSet = "ProcessParameterSet";
        private const string ProcessNameParameterSet = "ProcessNameParameterSet";
        private const string ProcessIdParameterSet = "ProcessIdParameterSet";
        private const string PSHostProcessInfoParameterSet = "PSHostProcessInfoParameterSet";

        private const string NamedPipeRunspaceName = "PSAttachRunspace";

        #endregion

        #endregion

        #region Parameters

        /// <summary>
        /// Process to enter.
        /// </summary>
        [Parameter(Position = 0, Mandatory = true, ValueFromPipeline = true, ParameterSetName = EnterPSHostProcessCommand.ProcessParameterSet)]
        [ValidateNotNull()]
        public Process Process
        {
            get;
            set;
        }

        /// <summary>
        /// Id of process to enter.
        /// </summary>
        [Parameter(Position = 0, Mandatory = true, ParameterSetName = EnterPSHostProcessCommand.ProcessIdParameterSet)]
        [ValidateRange(0, int.MaxValue)]
        public int Id
        {
            get;
            set;
        }

        /// <summary>
        /// Name of process to enter.  An error will result if more than one such process exists.
        /// </summary>
        [Parameter(Position = 0, Mandatory = true, ParameterSetName = EnterPSHostProcessCommand.ProcessNameParameterSet)]
        [ValidateNotNullOrEmpty()]
        public string Name
        {
            get;
            set;
        }

        /// <summary>
        /// Host Process Info object that describes a connectible process.
        /// </summary>
        [Parameter(Position = 0, Mandatory = true, ValueFromPipeline = true, ParameterSetName = EnterPSHostProcessCommand.PSHostProcessInfoParameterSet)]
        [ValidateNotNull()]
        public PSHostProcessInfo HostProcessInfo
        {
            get;
            set;
        }

        /// <summary>
        /// Optional name of AppDomain in process to enter.  If not specified then the default AppDomain is used.
        /// </summary>
        [Parameter(Position = 1, ParameterSetName = EnterPSHostProcessCommand.ProcessParameterSet)]
        [Parameter(Position = 1, ParameterSetName = EnterPSHostProcessCommand.ProcessIdParameterSet)]
        [Parameter(Position = 1, ParameterSetName = EnterPSHostProcessCommand.ProcessNameParameterSet)]
        [Parameter(Position = 1, ParameterSetName = EnterPSHostProcessCommand.PSHostProcessInfoParameterSet)]
        [ValidateNotNullOrEmpty]
        public string AppDomainName
        {
            get;
            set;
        }

        #endregion

        #region Overrides

        /// <summary>
        /// End Processing
        /// </summary>
        protected override void EndProcessing()
        {
            // Check for host that supports interactive remote sessions.
            _interactiveHost = this.Host as IHostSupportsInteractiveSession;
            if (_interactiveHost == null)
            {
                WriteError(
                    new ErrorRecord(
                        new ArgumentException(RemotingErrorIdStrings.HostDoesNotSupportIASession),
                        "EnterPSHostProcessHostDoesNotSupportIASession",
                        ErrorCategory.InvalidArgument,
                        null));

                return;
            }

            // Check selected process for existence, and whether it hosts PowerShell.
            switch (ParameterSetName)
            {
                case ProcessIdParameterSet:
                    Process = GetProcessById(Id);
                    break;

                case ProcessNameParameterSet:
                    Process = GetProcessByName(Name);
                    break;

                case PSHostProcessInfoParameterSet:
                    Process = GetProcessByHostProcessInfo(HostProcessInfo);
                    break;
            }
            VerifyProcess(Process);

            // Create named pipe runspace for selected process and open.
            Runspace namedPipeRunspace = CreateNamedPipeRunspace(Process.Id, AppDomainName);

            // Set runspace prompt.  The runspace is closed on pop so we don't 
            // have to reverse this change.
            PrepareRunspace(namedPipeRunspace);

            try
            {
                // Push runspace onto host.
                _interactiveHost.PushRunspace(namedPipeRunspace);
            }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                namedPipeRunspace.Close();

                ThrowTerminatingError(
                    new ErrorRecord(
                        e,
                        "EnterPSHostProcessCannotPushRunspace",
                        ErrorCategory.InvalidOperation,
                        this));
            }
        }

        /// <summary>
        /// Stop Processing
        /// </summary>
        protected override void StopProcessing()
        {
            RemoteRunspace connectingRunspace = _connectingRemoteRunspace;
            if (connectingRunspace != null)
            {
                connectingRunspace.AbortOpen();
            }
        }

        #endregion

        #region Private Methods

        private Runspace CreateNamedPipeRunspace(int procId, string appDomainName)
        {
            NamedPipeConnectionInfo connectionInfo = new NamedPipeConnectionInfo(procId, appDomainName);
            TypeTable typeTable = TypeTable.LoadDefaultTypeFiles();
            RemoteRunspace remoteRunspace = RunspaceFactory.CreateRunspace(connectionInfo, this.Host, typeTable) as RemoteRunspace;
            remoteRunspace.Name = NamedPipeRunspaceName;
            remoteRunspace.ShouldCloseOnPop = true;
            _connectingRemoteRunspace = remoteRunspace;

            try
            {
                remoteRunspace.Open();
                remoteRunspace.Debugger.SetDebugMode(DebugModes.LocalScript | DebugModes.RemoteScript);
            }
            catch (RuntimeException e)
            {
                string msgAppDomainName = (!string.IsNullOrEmpty(appDomainName)) ? appDomainName : NamedPipeUtils.DefaultAppDomainName;

                // Unwrap inner exception for original error message, if any.
                string errorMessage = (e.InnerException != null) ? (e.InnerException.Message ?? string.Empty) : string.Empty;

                ThrowTerminatingError(
                    new ErrorRecord(
                        new RuntimeException(
                            StringUtil.Format(RemotingErrorIdStrings.EnterPSHostProcessCannotConnectToProcess,
                                              msgAppDomainName, procId, errorMessage),
                            e.InnerException),
                        "EnterPSHostProcessCannotConnectToProcess",
                        ErrorCategory.OperationTimeout,
                        this));
            }
            finally
            {
                _connectingRemoteRunspace = null;
            }

            return remoteRunspace;
        }

        private void PrepareRunspace(Runspace runspace)
        {
            string promptFn = StringUtil.Format(RemotingErrorIdStrings.EnterPSHostProcessPrompt,
                @"function global:prompt { """,
                @"$($PID)",
                @"PS $($executionContext.SessionState.Path.CurrentLocation)> "" }"
            );

            // Set prompt in pushed named pipe runspace.
            using (System.Management.Automation.PowerShell ps = System.Management.Automation.PowerShell.Create())
            {
                ps.Runspace = runspace;

                try
                {
                    // Set pushed runspace prompt.
                    ps.AddScript(promptFn).Invoke();
                }
                catch (Exception e)
                {
                    // Ignore all non-severe errors.
                    CommandProcessorBase.CheckForSevereException(e);
                }
            }
        }

        private Process GetProcessById(int procId)
        {
            try
            {
                return Process.GetProcessById(procId);
            }
            catch (System.ArgumentException)
            {
                ThrowTerminatingError(
                        new ErrorRecord(
                            new PSArgumentException(StringUtil.Format(RemotingErrorIdStrings.EnterPSHostProcessNoProcessFoundWithId, procId)),
                            "EnterPSHostProcessNoProcessFoundWithId",
                            ErrorCategory.InvalidArgument,
                            this)
                        );

                return null;
            }
        }

        private Process GetProcessByHostProcessInfo(PSHostProcessInfo hostProcessInfo)
        {
            return GetProcessById(hostProcessInfo.ProcessId);
        }

        private Process GetProcessByName(string name)
        {
            Collection<Process> foundProcesses;

            using (System.Management.Automation.PowerShell ps = System.Management.Automation.PowerShell.Create(RunspaceMode.CurrentRunspace))
            {
                ps.AddCommand("Get-Process").AddParameter("Name", name);
                foundProcesses = ps.Invoke<Process>();
            }

            if (foundProcesses.Count == 0)
            {
                ThrowTerminatingError(
                        new ErrorRecord(
                            new PSArgumentException(StringUtil.Format(RemotingErrorIdStrings.EnterPSHostProcessNoProcessFoundWithName, name)),
                            "EnterPSHostProcessNoProcessFoundWithName",
                            ErrorCategory.InvalidArgument,
                            this)
                        );
            }
            else if (foundProcesses.Count > 1)
            {
                ThrowTerminatingError(
                        new ErrorRecord(
                            new PSArgumentException(StringUtil.Format(RemotingErrorIdStrings.EnterPSHostProcessMultipleProcessesFoundWithName, name)),
                            "EnterPSHostProcessMultipleProcessesFoundWithName",
                            ErrorCategory.InvalidArgument,
                            this)
                        );
            }

            return foundProcesses[0];
        }

        private void VerifyProcess(Process process)
        {
            if (process.Id == Process.GetCurrentProcess().Id)
            {
                ThrowTerminatingError(
                        new ErrorRecord(
                            new PSInvalidOperationException(RemotingErrorIdStrings.EnterPSHostProcessCantEnterSameProcess),
                            "EnterPSHostProcessCantEnterSameProcess",
                            ErrorCategory.InvalidOperation,
                            this)
                        );
            }

            bool hostsSMA = false;
            IReadOnlyCollection<PSHostProcessInfo> availableProcInfo = GetPSHostProcessInfoCommand.GetAppDomainNamesFromProcessId(null);
            foreach (var procInfo in availableProcInfo)
            {
                if (process.Id == procInfo.ProcessId)
                {
                    hostsSMA = true;
                    break;
                }
            }

            if (!hostsSMA)
            {
                ThrowTerminatingError(
                        new ErrorRecord(
                            new PSInvalidOperationException(StringUtil.Format(RemotingErrorIdStrings.EnterPSHostProcessNoPowerShell, Process.ProcessName)),
                            "EnterPSHostProcessNoPowerShell",
                            ErrorCategory.InvalidOperation,
                            this)
                        );
            }
        }

        #endregion
    }

    /// <summary>
    /// This cmdlet exits an interactive session with a local process.
    /// </summary>
    [Cmdlet(VerbsCommon.Exit, "PSHostProcess",
        HelpUri = "http://go.microsoft.com/fwlink/?LinkId=403737")]
    public sealed class ExitPSHostProcessCommand : PSCmdlet
    {
        #region Overrides

        /// <summary>
        /// Process Record
        /// </summary>
        protected override void ProcessRecord()
        {
            var _interactiveHost = this.Host as IHostSupportsInteractiveSession;
            if (_interactiveHost == null)
            {
                WriteError(
                    new ErrorRecord(
                        new ArgumentException(RemotingErrorIdStrings.HostDoesNotSupportIASession),
                        "ExitPSHostProcessHostDoesNotSupportIASession",
                        ErrorCategory.InvalidArgument,
                        null));

                return;
            }

            _interactiveHost.PopRunspace();
        }

        #endregion
    }

    /// <summary>
    /// This cmdlet returns a collection of PSHostProcessInfo objects containing 
    /// process and AppDomain name information for processes that have PowerShell loaded.  
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "PSHostProcessInfo", DefaultParameterSetName = GetPSHostProcessInfoCommand.ProcessNameParameterSet,
        HelpUri = "http://go.microsoft.com/fwlink/?LinkId=517012")]
    [OutputType(typeof(PSHostProcessInfo))]
    public sealed class GetPSHostProcessInfoCommand : PSCmdlet
    {
        #region Strings

        private const string ProcessParameterSet = "ProcessParameterSet";
        private const string ProcessIdParameterSet = "ProcessIdParameterSet";
        private const string ProcessNameParameterSet = "ProcessNameParameterSet";
        private const string NamedPipePath = @"\\.\pipe\";

        #endregion

        #region Parameters

        /// <summary>
        /// Name of Process
        /// </summary>
        [Parameter(Position = 0, ParameterSetName = GetPSHostProcessInfoCommand.ProcessNameParameterSet)]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [ValidateNotNullOrEmpty()]
        public string[] Name
        {
            get;
            set;
        }

        /// <summary>
        /// Process
        /// </summary>
        [Parameter(Position = 0, Mandatory = true, ValueFromPipeline = true, ParameterSetName = GetPSHostProcessInfoCommand.ProcessParameterSet)]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [ValidateNotNullOrEmpty()]
        public Process[] Process
        {
            get;
            set;
        }

        /// <summary>
        /// Id of process
        /// </summary>
        [Parameter(Position = 0, Mandatory = true, ParameterSetName = GetPSHostProcessInfoCommand.ProcessIdParameterSet)]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [ValidateNotNullOrEmpty()]
        public int[] Id
        {
            get;
            set;
        }

        #endregion

        #region Overrides

        /// <summary>
        /// End bock processing
        /// </summary>
        protected override void EndProcessing()
        {
            IReadOnlyCollection<PSHostProcessInfo> processAppDomainInfo;
            switch (ParameterSetName)
            {
                case ProcessNameParameterSet:
                    processAppDomainInfo = GetAppDomainNamesFromProcessId(GetProcIdsFromNames(Name));
                    break;

                case ProcessIdParameterSet:
                    processAppDomainInfo = GetAppDomainNamesFromProcessId(Id);
                    break;

                case ProcessParameterSet:
                    processAppDomainInfo = GetAppDomainNamesFromProcessId(GetProcIdsFromProcs(Process));
                    break;

                default:
                    Debug.Assert(false, "Unknown parameter set.");
                    processAppDomainInfo = new ReadOnlyCollection<PSHostProcessInfo>(new Collection<PSHostProcessInfo>());
                    break;
            }

            WriteObject(processAppDomainInfo, true);
        }

        #endregion

        #region Private Methods

        private int[] GetProcIdsFromProcs(Process[] processes)
        {
            List<int> returnIds = new List<int>();
            foreach (Process process in processes)
            {
                returnIds.Add(process.Id);
            }

            return returnIds.ToArray();
        }

        private int[] GetProcIdsFromNames(string[] names)
        {
            if ((names == null) || (names.Length == 0))
            {
                return null;
            }

            List<int> returnIds = new List<int>();
            System.Diagnostics.Process[] processes = System.Diagnostics.Process.GetProcesses();
            foreach (string name in names)
            {
                WildcardPattern namePattern = WildcardPattern.Get(name, WildcardOptions.IgnoreCase);
                foreach (var proc in processes)
                {
                    if (namePattern.IsMatch(proc.ProcessName))
                    {
                        returnIds.Add(proc.Id);
                    }
                }
            }

            return returnIds.ToArray();
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// Returns all named pipe AppDomain names for given process Ids or all PowerShell
        /// processes if procIds parameter is null.
        /// PowerShell pipe name example:
        ///     PSHost.130566795082911445.8224.DefaultAppDomain.powershell
        /// </summary>
        /// <param name="procIds">Process Ids or null</param>
        /// <returns>Collection of process AppDomain info</returns>
        internal static IReadOnlyCollection<PSHostProcessInfo> GetAppDomainNamesFromProcessId(int[] procIds)
        {
            var procAppDomainInfo = new List<PSHostProcessInfo>();

            // Get all named pipe 'files' on local machine.
            List<string> directories;
            List<string> namedPipes;
            Utils.NativeEnumerateDirectory(NamedPipePath, out directories, out namedPipes);

            // Collect all PowerShell named pipes for given process Ids.
            foreach (string namedPipe in namedPipes)
            {
                int startIndex = namedPipe.IndexOf(NamedPipeUtils.NamedPipeNamePrefix, StringComparison.OrdinalIgnoreCase);
                if (startIndex > -1)
                {
                    // This is a PowerShell named pipe.  Parse the process Id, AppDomain name, and process name.
                    int pStartTimeIndex = namedPipe.IndexOf(".", startIndex, StringComparison.OrdinalIgnoreCase);
                    if (pStartTimeIndex > -1)
                    {
                        int pIdIndex = namedPipe.IndexOf(".", pStartTimeIndex + 1, StringComparison.OrdinalIgnoreCase);
                        if (pIdIndex > -1)
                        {
                            int pAppDomainIndex = namedPipe.IndexOf(".", pIdIndex + 1, StringComparison.OrdinalIgnoreCase);
                            if (pAppDomainIndex > -1)
                            {
                                string idString = namedPipe.Substring(pIdIndex + 1, (pAppDomainIndex - pIdIndex - 1));
                                int id = -1;
                                if (int.TryParse(idString, out id))
                                {
                                    // Filter on provided proc Ids.
                                    if (procIds != null)
                                    {
                                        bool found = false;
                                        foreach (int procId in procIds)
                                        {
                                            if (id == procId)
                                            {
                                                found = true;
                                                break;
                                            }
                                        }

                                        if (!found) { continue; }
                                    }
                                }

                                int pNameIndex = namedPipe.IndexOf(".", pAppDomainIndex + 1, StringComparison.OrdinalIgnoreCase);
                                if (pNameIndex > -1)
                                {
                                    string appDomainName = namedPipe.Substring(pAppDomainIndex + 1, (pNameIndex - pAppDomainIndex - 1));
                                    string pName = namedPipe.Substring(pNameIndex + 1);

                                    procAppDomainInfo.Add(
                                        new PSHostProcessInfo(pName, id, appDomainName));
                                }
                            }
                        }
                    }
                }
            }

            if (procAppDomainInfo.Count > 1)
            {
                // Sort list by process name.
                var comparerInfo = CultureInfo.InvariantCulture.CompareInfo;
                procAppDomainInfo.Sort((firstItem, secondItem) => comparerInfo.Compare(firstItem.ProcessName, secondItem.ProcessName, CompareOptions.IgnoreCase));
            }

            return new ReadOnlyCollection<PSHostProcessInfo>(procAppDomainInfo);
        }

        #endregion
    }

    #region PSHostProcessInfo class

    /// <summary>
    /// PowerShell host process information class.
    /// </summary>
    public sealed class PSHostProcessInfo
    {
        #region Properties

        /// <summary>
        /// Name of process
        /// </summary>
        public string ProcessName
        {
            get;
            private set;
        }

        /// <summary>
        /// Id of process
        /// </summary>
        public int ProcessId
        {
            get;
            private set;
        }

        /// <summary>
        /// Name of PowerShell AppDomain in process
        /// </summary>
        public string AppDomainName
        {
            get;
            private set;
        }

#if !CORECLR
        /// <summary>
        /// Main window title of the process
        /// </summary>
        public string MainWindowTitle
        {
            get;
            private set;
        }
#endif

        #endregion

        #region Constructors

        private PSHostProcessInfo() { }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="processName">Name of process</param>
        /// <param name="processId">Id of process</param>
        /// <param name="appDomainName">Name of process AppDomain</param>
        internal PSHostProcessInfo(string processName, int processId, string appDomainName)
        {
            if (string.IsNullOrEmpty(processName)) { throw new PSArgumentNullException("processName"); }
            if (string.IsNullOrEmpty(appDomainName)) { throw new PSArgumentNullException("appDomainName"); }

#if !CORECLR
            MainWindowTitle = String.Empty;
            try
            {
                var proc = System.Diagnostics.Process.GetProcessById(processId);
                MainWindowTitle = proc.MainWindowTitle ?? string.Empty;
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
#endif

            this.ProcessName = processName;
            this.ProcessId = processId;
            this.AppDomainName = appDomainName;
        }

        #endregion
    }

    #endregion
}
