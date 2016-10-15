/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics; // Process class
using System.ComponentModel; // Win32Exception
using System.Runtime.Serialization;
using System.Threading;
using System.Management.Automation;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using System.Management.Automation.Internal;
using Microsoft.PowerShell.Commands.Internal;
using Microsoft.Management.Infrastructure;

using FileNakedHandle = System.IntPtr;
using DWORD = System.UInt32;

#if CORECLR
// Use stubs for SafeHandleZeroOrMinusOneIsInvalid and SerializableAttribute
using Microsoft.PowerShell.CoreClr.Stubs;
using Environment = System.Management.Automation.Environment;
#else
using System.Runtime.ConstrainedExecution;
using System.Security.Permissions;
#endif

namespace Microsoft.PowerShell.Commands
{
    // 2004/12/17-JonN ProcessNameGlobAttribute was deeply wrong.
    // For example, if you pass in a single Process, it will match
    // all processes with the same name.
    // I have removed the globbing code.

    #region ProcessBaseCommand
    /// <summary>
    /// This class implements the base for process commands
    /// </summary>
    public abstract class ProcessBaseCommand : Cmdlet
    {
        #region Parameters
        /// <summary>
        /// The various process selection modes
        /// </summary>
        internal enum MatchMode
        {
            /// <summary>
            /// Select all processes
            /// </summary>
            All,
            /// <summary>
            /// Select processes matching the supplied names
            /// </summary>
            ByName,
            /// <summary>
            /// Select the processes matching the id
            /// </summary>
            ById,
            /// <summary>
            /// Select the processes specified as input.
            /// </summary>
            ByInput
        };
        /// <summary>
        /// The current process selection mode.
        /// </summary>
        internal MatchMode myMode = MatchMode.All;

        /// <summary>
        /// The computer from which to retrieve processes.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        protected string[] SuppliedComputerName { get; set; } = Utils.EmptyArray<string>();

        /// <remarks>
        /// The Name parameter is declared in subclasses,
        /// since it is optional for GetProcess and mandatory for StopProcess.
        /// </remarks>
        internal string[] processNames = null;

        // The Id parameter is declared in subclasses,
        // since it is positional for StopProcess but not for GetProcess.
        internal int[] processIds = null;

        /// <summary>
        /// If the input is a stream of [collections of]
        /// Process objects, we bypass the Name and
        /// Id parameters and read the Process objects
        /// directly.  This allows us to deal with processes which
        /// have wildcard characters in their name.
        /// </summary>
        /// <value>Process objects</value>
        [Parameter(
            ParameterSetName = "InputObject",
            Mandatory = true,
            ValueFromPipeline = true)]
        public virtual Process[] InputObject
        {
            get
            {
                return _input;
            }
            set
            {
                myMode = MatchMode.ByInput;
                _input = value;
            }
        }
        private Process[] _input = null;
        #endregion Parameters

        #region Internal

        // We use a Dictionary to optimize the check whether the object
        // is already in the list.
        private List<Process> _matchingProcesses = new List<Process>();
        private Dictionary<int, Process> _keys = new Dictionary<int, Process>();

        /// <summary>
        /// Retrieve the list of all processes matching the Name, Id
        /// and InputObject parameters, sorted by Id.
        /// </summary>
        /// <returns></returns>
        internal List<Process> MatchingProcesses()
        {
            _matchingProcesses.Clear();
            switch (myMode)
            {
                case MatchMode.ById:
                    RetrieveMatchingProcessesById();
                    break;

                case MatchMode.ByInput:
                    RetrieveProcessesByInput();
                    break;
                default:
                    // Default is "Name":
                    RetrieveMatchingProcessesByProcessName();
                    break;
            }
            // 2004/12/16 Note that the processes will be sorted
            //  before being stopped.  PM confirms that this is fine.
            _matchingProcesses.Sort(ProcessComparison);
            return _matchingProcesses;
        } // MatchingProcesses

        /// <summary>
        /// sort function to sort by Name first, then Id
        /// </summary>
        /// <param name="x">first Process object</param>
        /// <param name="y">second Process object</param>
        /// <returns>
        /// as String.Compare: returns less than zero if x less than y,
        /// greater than 0 if x greater than y, 0 if x == y
        /// </returns>
        private static int ProcessComparison(Process x, Process y)
        {
            int diff = String.Compare(
                SafeGetProcessName(x),
                SafeGetProcessName(y),
                StringComparison.CurrentCultureIgnoreCase);
            if (0 != diff)
                return diff;
            return SafeGetProcessId(x) - SafeGetProcessId(y);
        }

        /// <summary>
        /// Retrieves the list of all processes matching the Name
        /// parameter.
        /// Generates a non-terminating error for each specified
        /// process name which is not found even though it contains
        /// no wildcards.
        /// </summary>
        /// <returns></returns>
        private void RetrieveMatchingProcessesByProcessName()
        {
            if (null == processNames)
            {
                _matchingProcesses = new List<Process>(AllProcesses);
                return;
            }
            foreach (string pattern in processNames)
            {
                WildcardPattern wildcard =
                    WildcardPattern.Get(pattern, WildcardOptions.IgnoreCase);
                bool found = false;
                foreach (Process process in AllProcesses)
                {
                    if (!wildcard.IsMatch(SafeGetProcessName(process)))
                        continue;
                    found = true;
                    AddIdempotent(process);
                }
                if (!found &&
                    !WildcardPattern.ContainsWildcardCharacters(pattern))
                {
                    WriteNonTerminatingError(
                        pattern,
                        0,
                        pattern,
                        null,
                        ProcessResources.NoProcessFoundForGivenName,
                        "NoProcessFoundForGivenName",
                        ErrorCategory.ObjectNotFound);
                }
            }
        } // MatchingProcessesByProcessName

        /// <summary>
        /// Retrieves the list of all processes matching the Id
        /// parameter.
        /// Generates a non-terminating error for each specified
        /// process ID which is not found.
        /// </summary>
        /// <returns></returns>
        private void RetrieveMatchingProcessesById()
        {
            if (null == processIds)
            {
                Diagnostics.Assert(false, "null processIds");
                throw PSTraceSource.NewInvalidOperationException();
            }
            foreach (int processId in processIds)
            {
                Process process;
                try
                {
                    if (SuppliedComputerName.Length > 0)
                    {
                        foreach (string computerName in SuppliedComputerName)
                        {
                            process = Process.GetProcessById(processId, computerName);
                            AddIdempotent(process);
                        }
                    }
                    else
                    {
                        process = Process.GetProcessById(processId);
                        AddIdempotent(process);
                    }
                }
                catch (ArgumentException)
                {
                    WriteNonTerminatingError(
                        "",
                        processId,
                        processId,
                        null,
                        ProcessResources.NoProcessFoundForGivenId,
                        "NoProcessFoundForGivenId",
                        ErrorCategory.ObjectNotFound);
                    continue;
                }
            }
        } // MatchingProcessesById

        /// <summary>
        /// Retrieves the list of all processes matching the InputObject
        /// parameter.
        /// </summary>
        /// <returns></returns>
        private void RetrieveProcessesByInput()
        {
            if (null == InputObject)
            {
                Diagnostics.Assert(false, "null InputObject");
                throw PSTraceSource.NewInvalidOperationException();
            }
            foreach (Process process in InputObject)
            {
                SafeRefresh(process);
                AddIdempotent(process);
            }
        } // MatchingProcessesByInput

        /// <summary>
        /// Retrieve the master list of all processes
        /// </summary>
        /// <value></value>
        /// <exception cref="System.Security.SecurityException">
        /// MSDN does not document the list of exceptions,
        /// but it is reasonable to expect that SecurityException is
        /// among them.  Errors here will terminate the cmdlet.
        /// </exception>
        internal Process[] AllProcesses
        {
            get
            {
                if (null == _allProcesses)
                {
                    List<Process> processes = new List<Process>();

                    if (SuppliedComputerName.Length > 0)
                    {
                        foreach (string computerName in SuppliedComputerName)
                        {
                            processes.AddRange(Process.GetProcesses(computerName));
                        }
                    }
                    else
                    {
                        processes.AddRange(Process.GetProcesses());
                    }

                    _allProcesses = processes.ToArray();
                }
                return _allProcesses;
            }
        }
        private Process[] _allProcesses = null;

        /// <summary>
        /// Add <paramref name="process"/> to <see cref="_matchingProcesses"/>,
        /// but only if it is not already on  <see cref="_matchingProcesses"/>.
        /// We use a Dictionary to optimize the check whether the object
        /// is already in the list.
        /// </summary>
        /// <param name="process">process to add to list</param>
        private void AddIdempotent(
            Process process)
        {
            int hashCode = SafeGetProcessName(process).GetHashCode()
                ^ SafeGetProcessId(process); // XOR
            if (!_keys.ContainsKey(hashCode))
            {
                _keys.Add(hashCode, process);
                _matchingProcesses.Add(process);
            }
        }

        /// <summary>
        /// Writes a non-terminating error.
        /// </summary>
        /// <param name="process"></param>
        /// <param name="innerException"></param>
        /// <param name="resourceId"></param>
        /// <param name="errorId"></param>
        /// <param name="category"></param>
        internal void WriteNonTerminatingError(
            Process process,
            Exception innerException,
            string resourceId, string errorId,
            ErrorCategory category)
        {
            WriteNonTerminatingError(
                SafeGetProcessName(process),
                SafeGetProcessId(process),
                process,
                innerException,
                resourceId,
                errorId,
                category);
        }

        /// <summary>
        /// Writes a non-terminating error.
        /// </summary>
        /// <param name="processName"></param>
        /// <param name="processId"></param>
        /// <param name="targetObject"></param>
        /// <param name="innerException"></param>
        /// <param name="resourceId"></param>
        /// <param name="errorId"></param>
        /// <param name="category"></param>
        internal void WriteNonTerminatingError(
            string processName,
            int processId,
            object targetObject,
            Exception innerException,
            string resourceId,
            string errorId,
            ErrorCategory category)
        {
            string message = StringUtil.Format(resourceId,
                processName,
                processId,
                (null == innerException) ? "" : innerException.Message);
            ProcessCommandException exception =
                new ProcessCommandException(message, innerException);
            exception.ProcessName = processName;

            WriteError(new ErrorRecord(
                exception, errorId, category, targetObject));
        }

        // The Name property is not always available, even for
        // live processes (such as the Idle process).
        internal static string SafeGetProcessName(Process process)
        {
            try
            {
                return process.ProcessName;
            }
            catch (Win32Exception)
            {
                return "";
            }
            catch (InvalidOperationException)
            {
                return "";
            }
        }

        // 2004/12/17-JonN I saw this fail once too, so we'll play it safe
        internal static int SafeGetProcessId(Process process)
        {
            try
            {
                return process.Id;
            }
            catch (Win32Exception)
            {
                return int.MinValue;
            }
            catch (InvalidOperationException)
            {
                return int.MinValue;
            }
        }

        internal static void SafeRefresh(Process process)
        {
            try
            {
                process.Refresh();
            }
            catch (Win32Exception)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        /// <summary>
        /// TryHasExited is a helper function used to detect if the process has aready exited or not.
        /// </summary>
        /// <param name="process">
        /// Process whose exit status has to be checked.
        /// </param>
        /// <returns>Tre if the process has exited or else returns false.</returns>
        internal static bool TryHasExited(Process process)
        {
            bool hasExited = true;

            try
            {
                hasExited = process.HasExited;
            }
            catch (Win32Exception)
            {
                hasExited = false;
            }
            catch (InvalidOperationException)
            {
                hasExited = false;
            }

            return hasExited;
        }

        #endregion Internal
    }//ProcessBaseCommand
    #endregion ProcessBaseCommand

    #region GetProcessCommand
    /// <summary>
    /// This class implements the get-process command
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "Process", DefaultParameterSetName = NameParameterSet,
        HelpUri = "http://go.microsoft.com/fwlink/?LinkID=113324", RemotingCapability = RemotingCapability.SupportedByCommand)]
    [OutputType(typeof(ProcessModule), typeof(FileVersionInfo), typeof(Process))]
    public sealed class GetProcessCommand : ProcessBaseCommand
    {
        #region ParameterSetStrings

        private const string NameParameterSet = "Name";
        private const string IdParameterSet = "Id";
        private const string InputObjectParameterSet = "InputObject";
        private const string NameWithUserNameParameterSet = "NameWithUserName";
        private const string IdWithUserNameParameterSet = "IdWithUserName";
        private const string InputObjectWithUserNameParameterSet = "InputObjectWithUserName";

        #endregion ParameterSetStrings

        #region Parameters

        /// <summary>
        /// Has the list of process names on which to this command will work
        /// </summary>
        // [ProcessNameGlobAttribute]
        [Parameter(Position = 0, ParameterSetName = NameParameterSet, ValueFromPipelineByPropertyName = true)]
        [Parameter(Position = 0, ParameterSetName = NameWithUserNameParameterSet, ValueFromPipelineByPropertyName = true)]
        [Alias("ProcessName")]
        [ValidateNotNullOrEmpty]
        public string[] Name
        {
            get { return processNames; }
            set
            {
                myMode = MatchMode.ByName;
                processNames = value;
            }
        }

        /// <summary>
        /// gets/sets an array of process IDs
        /// </summary>
        [Parameter(ParameterSetName = IdParameterSet, Mandatory = true, ValueFromPipelineByPropertyName = true)]
        [Parameter(ParameterSetName = IdWithUserNameParameterSet, Mandatory = true, ValueFromPipelineByPropertyName = true)]
        [Alias("PID")]
        public int[] Id
        {
            get
            {
                return processIds;
            }
            set
            {
                myMode = MatchMode.ById;
                processIds = value;
            }
        }

        /// <summary>
        /// Input is a stream of [collections of] Process objects
        /// </summary>
        [Parameter(ParameterSetName = InputObjectParameterSet, Mandatory = true, ValueFromPipeline = true)]
        [Parameter(ParameterSetName = InputObjectWithUserNameParameterSet, Mandatory = true, ValueFromPipeline = true)]
        public override Process[] InputObject
        {
            get
            {
                return base.InputObject;
            }
            set
            {
                base.InputObject = value;
            }
        }

        /// <summary>
        /// Include the UserName
        /// </summary>
        [Parameter(ParameterSetName = NameWithUserNameParameterSet, Mandatory = true)]
        [Parameter(ParameterSetName = IdWithUserNameParameterSet, Mandatory = true)]
        [Parameter(ParameterSetName = InputObjectWithUserNameParameterSet, Mandatory = true)]
        public SwitchParameter IncludeUserName
        {
            get { return _includeUserName; }
            set { _includeUserName = value; }
        }
        private bool _includeUserName = false;


        /// <summary>
        /// gets/sets the destination computer name
        /// </summary>
        [Parameter(Mandatory = false, ParameterSetName = NameParameterSet, ValueFromPipelineByPropertyName = true)]
        [Parameter(Mandatory = false, ParameterSetName = IdParameterSet, ValueFromPipelineByPropertyName = true)]
        [Parameter(Mandatory = false, ParameterSetName = InputObjectParameterSet, ValueFromPipelineByPropertyName = true)]
        [Alias("Cn")]
        [ValidateNotNullOrEmpty()]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public string[] ComputerName
        {
            get
            {
                return SuppliedComputerName;
            }
            set
            {
                SuppliedComputerName = value;
            }
        }


        ///<summary>
        ///To display the modules of a process
        ///</summary>

        [Parameter(ParameterSetName = NameParameterSet)]
        [Parameter(ParameterSetName = IdParameterSet)]
        [Parameter(ParameterSetName = InputObjectParameterSet)]
        [ValidateNotNull]
        public SwitchParameter Module { get; set; }

        ///<summary>
        ///To display the fileversioninfo of the main module of a process
        ///</summary>
        [Parameter(ParameterSetName = NameParameterSet)]
        [Parameter(ParameterSetName = IdParameterSet)]
        [Parameter(ParameterSetName = InputObjectParameterSet)]
        [Alias("FV", "FVI")]
        [ValidateNotNull]
        public SwitchParameter FileVersionInfo { get; set; }

        #endregion Parameters

        #region Overrides

        /// <summary>
        /// Check the elevation mode if IncludeUserName is specified
        /// </summary>
        protected override void BeginProcessing()
        {
            // The parameter 'IncludeUserName' requires administrator privilege
            if (IncludeUserName.IsPresent && !Utils.IsAdministrator())
            {
                var ex = new InvalidOperationException(ProcessResources.IncludeUserNameRequiresElevation);
                var er = new ErrorRecord(ex, "IncludeUserNameRequiresElevation", ErrorCategory.InvalidOperation, null);
                ThrowTerminatingError(er);
            }
        }

        /// <summary>
        /// Write the process objects
        /// </summary>
        protected override void ProcessRecord()
        {
            if (ComputerName.Length > 0 && (FileVersionInfo.IsPresent || Module.IsPresent))
            {
                Exception ex = new InvalidOperationException(ProcessResources.NoComputerNameWithFileVersion);
                ErrorRecord er = new ErrorRecord(ex, "InvalidOperationException", ErrorCategory.InvalidOperation, ComputerName);
                ThrowTerminatingError(er);
            }

            foreach (Process process in MatchingProcesses())
            {
                //if module and fileversion are to be displayed
                if (Module.IsPresent && FileVersionInfo.IsPresent)
                {
                    ProcessModule tempmodule = null;
                    try
                    {
                        ProcessModuleCollection modules = process.Modules;
                        foreach (ProcessModule pmodule in modules)
                        {
                            //assigning to tempmodule to rethrow for exceptions on 64 bit machines
                            tempmodule = pmodule;
                            WriteObject(ClrFacade.GetProcessModuleFileVersionInfo(pmodule), true);
                        }
                    }
                    catch (InvalidOperationException exception)
                    {
                        WriteNonTerminatingError(process, exception, ProcessResources.CouldnotEnumerateModuleFileVer, "CouldnotEnumerateModuleFileVer", ErrorCategory.PermissionDenied);
                    }
                    catch (ArgumentException exception)
                    {
                        WriteNonTerminatingError(process, exception, ProcessResources.CouldnotEnumerateModuleFileVer, "CouldnotEnumerateModuleFileVer", ErrorCategory.PermissionDenied);
                    }
                    catch (Win32Exception exception)
                    {
                        try
                        {
                            if (exception.HResult == 299)
                            {
                                WriteObject(ClrFacade.GetProcessModuleFileVersionInfo(tempmodule), true);
                            }
                            else
                            {
                                WriteNonTerminatingError(process, exception, ProcessResources.CouldnotEnumerateModuleFileVer, "CouldnotEnumerateModuleFileVer", ErrorCategory.PermissionDenied);
                            }
                        }
                        catch (Win32Exception ex)
                        {
                            WriteNonTerminatingError(process, ex, ProcessResources.CouldnotEnumerateModuleFileVer, "CouldnotEnumerateModuleFileVer", ErrorCategory.PermissionDenied);
                        }
                    }
                    catch (Exception exception)
                    {
                        CommandsCommon.CheckForSevereException(this, exception);
                        WriteNonTerminatingError(process, exception, ProcessResources.CouldnotEnumerateModuleFileVer, "CouldnotEnumerateModuleFileVer", ErrorCategory.PermissionDenied);
                    }
                }
                else if (Module.IsPresent)
                {
                    //if only modules are to be displayed 
                    try
                    {
                        WriteObject(process.Modules, true);
                    }
                    catch (Win32Exception exception)
                    {
                        try
                        {
                            if (exception.HResult == 299)
                            {
                                WriteObject(process.Modules, true);
                            }
                            else
                            {
                                WriteNonTerminatingError(process, exception, ProcessResources.CouldnotEnumerateModules, "CouldnotEnumerateModules", ErrorCategory.PermissionDenied);
                            }
                        }
                        catch (Win32Exception ex)
                        {
                            WriteNonTerminatingError(process, ex, ProcessResources.CouldnotEnumerateModules, "CouldnotEnumerateModules", ErrorCategory.PermissionDenied);
                        }
                    }
                    catch (Exception exception)
                    {
                        CommandsCommon.CheckForSevereException(this, exception);
                        WriteNonTerminatingError(process, exception, ProcessResources.CouldnotEnumerateModules, "CouldnotEnumerateModules", ErrorCategory.PermissionDenied);
                    }
                }
                else if (FileVersionInfo.IsPresent)
                {
                    //if fileversion of each process is to be displayed
                    try
                    {
                        WriteObject(ClrFacade.GetProcessModuleFileVersionInfo(PsUtils.GetMainModule(process)), true);
                    }
                    catch (InvalidOperationException exception)
                    {
                        WriteNonTerminatingError(process, exception, ProcessResources.CouldnotEnumerateFileVer, "CouldnotEnumerateFileVer", ErrorCategory.PermissionDenied);
                    }
                    catch (ArgumentException exception)
                    {
                        WriteNonTerminatingError(process, exception, ProcessResources.CouldnotEnumerateFileVer, "CouldnotEnumerateFileVer", ErrorCategory.PermissionDenied);
                    }
                    catch (Win32Exception exception)
                    {
                        try
                        {
                            if (exception.HResult == 299)
                            {
                                WriteObject(ClrFacade.GetProcessModuleFileVersionInfo(PsUtils.GetMainModule(process)), true);
                            }
                            else
                            {
                                WriteNonTerminatingError(process, exception, ProcessResources.CouldnotEnumerateFileVer, "CouldnotEnumerateFileVer", ErrorCategory.PermissionDenied);
                            }
                        }
                        catch (Win32Exception ex)
                        {
                            WriteNonTerminatingError(process, ex, ProcessResources.CouldnotEnumerateFileVer, "CouldnotEnumerateFileVer", ErrorCategory.PermissionDenied);
                        }
                    }
                    catch (Exception exception)
                    {
                        CommandsCommon.CheckForSevereException(this, exception);
                        WriteNonTerminatingError(process, exception, ProcessResources.CouldnotEnumerateFileVer, "CouldnotEnumerateFileVer", ErrorCategory.PermissionDenied);
                    }
                }
                else
                {
#if CORECLR
                    WriteObject(AddProperties(IncludeUserName.IsPresent, process, this));
#else
                    WriteObject(IncludeUserName.IsPresent ? AddUserNameToProcess(process, this) : process);
#endif
                }
            }//for loop
        } // ProcessRecord

        #endregion Overrides

        #region IncludeUserName

        /// <summary>
        /// New PSTypeName added to the process object
        /// </summary>
        private const string TypeNameForProcessWithUserName = "System.Diagnostics.Process#IncludeUserName";

        /// <summary>
        /// Add the 'UserName' NoteProperty to the Process object
        /// </summary>
        /// <param name="process"></param>
        /// <param name="cmdlet"></param>
        /// <returns></returns>
        internal object AddUserNameToProcess(Process process, Cmdlet cmdlet)
        {
            // Return null if we failed to get the owner information
            string userName = RetrieveProcessUserName(process, cmdlet);

            PSObject processAsPsobj = PSObject.AsPSObject(process);
            PSNoteProperty noteProperty = new PSNoteProperty("UserName", userName);

            processAsPsobj.Properties.Add(noteProperty, true);
            processAsPsobj.TypeNames.Insert(0, TypeNameForProcessWithUserName);

            return processAsPsobj;
        }

#if CORECLR
        /// <summary>
        /// Add the 'UserName' and 'HandleCount' NoteProperties  to the Process object.
        /// </summary>
        /// <param name="process"></param>
        /// <param name="cmdlet"></param>
        /// <param name="includeUserName"></param>
        /// <returns></returns>
        internal object AddProperties(bool includeUserName, Process process, Cmdlet cmdlet)
        {
            PSObject processAsPsobj = PSObject.AsPSObject(process);
            if (includeUserName)
            {
                // Return null if we failed to get the owner information
                string userName = RetrieveProcessUserName(process, cmdlet);
                PSNoteProperty noteProperty = new PSNoteProperty("UserName", userName);
                processAsPsobj.Properties.Add(noteProperty, true);
                processAsPsobj.TypeNames.Insert(0, TypeNameForProcessWithUserName);
            }

            // In CoreCLR, the System.Diagnostics.Process.HandleCount property does not exist.
            // I am adding a note property HandleCount and temporarily setting it to zero.
            // This issue will be fix for RTM and it is tracked by 5024994: Get-process does not populate the Handles field.
            PSMemberInfo hasHandleCount = processAsPsobj.Properties["HandleCount"];
            if (hasHandleCount == null)
            {
                PSNoteProperty noteProperty = new PSNoteProperty("HandleCount", 0);
                processAsPsobj.Properties.Add(noteProperty, true);
                processAsPsobj.TypeNames.Insert(0, "System.Diagnostics.Process#HandleCount");
            }

            return processAsPsobj;
        }
#endif

        /// <summary>
        /// Retrieve the UserName through PInvoke
        /// </summary>
        /// <param name="process"></param>
        /// <param name="cmdlet"></param>
        /// <returns></returns>
        private static string RetrieveProcessUserName(Process process, Cmdlet cmdlet)
        {
            string userName = null;
#if UNIX
            userName = Platform.NonWindowsGetUserFromPid(process.Id);
#else
            IntPtr tokenUserInfo = IntPtr.Zero;
            IntPtr processTokenHandler = IntPtr.Zero;

            const uint TOKEN_QUERY = 0x0008;

            try
            {
                do
                {
                    int error;
                    if (!Win32Native.OpenProcessToken(ClrFacade.GetSafeProcessHandle(process), TOKEN_QUERY, out processTokenHandler)) { break; }

                    // Set the default length to be 256, so it will be sufficient for most cases
                    int tokenInfoLength = 256;
                    tokenUserInfo = Marshal.AllocHGlobal(tokenInfoLength);
                    if (!Win32Native.GetTokenInformation(processTokenHandler, Win32Native.TOKEN_INFORMATION_CLASS.TokenUser, tokenUserInfo, tokenInfoLength, out tokenInfoLength))
                    {
                        error = Marshal.GetLastWin32Error();
                        if (error == Win32Native.ERROR_INSUFFICIENT_BUFFER)
                        {
                            Marshal.FreeHGlobal(tokenUserInfo);
                            tokenUserInfo = Marshal.AllocHGlobal(tokenInfoLength);

                            if (!Win32Native.GetTokenInformation(processTokenHandler, Win32Native.TOKEN_INFORMATION_CLASS.TokenUser, tokenUserInfo, tokenInfoLength, out tokenInfoLength)) { break; }
                        }
                        else
                        {
                            break;
                        }
                    }

                    var tokenUser = ClrFacade.PtrToStructure<Win32Native.TOKEN_USER>(tokenUserInfo);

                    // Set the default length to be 256, so it will be sufficient for most cases
                    int userNameLength = 256, domainNameLength = 256;
                    var userNameStr = new StringBuilder(userNameLength);
                    var domainNameStr = new StringBuilder(domainNameLength);
                    Win32Native.SID_NAME_USE accountType;

                    if (!Win32Native.LookupAccountSid(null, tokenUser.User.Sid, userNameStr, ref userNameLength, domainNameStr, ref domainNameLength, out accountType))
                    {
                        error = Marshal.GetLastWin32Error();
                        if (error == Win32Native.ERROR_INSUFFICIENT_BUFFER)
                        {
                            userNameStr.EnsureCapacity(userNameLength);
                            domainNameStr.EnsureCapacity(domainNameLength);

                            if (!Win32Native.LookupAccountSid(null, tokenUser.User.Sid, userNameStr, ref userNameLength, domainNameStr, ref domainNameLength, out accountType)) { break; }
                        }
                        else
                        {
                            break;
                        }
                    }

                    userName = domainNameStr + "\\" + userNameStr;
                } while (false);
            }
            catch (NotSupportedException)
            {
                // The Process not started yet, or it's a process from a remote machine
            }
            catch (InvalidOperationException)
            {
                // The Process has exited, Process.Handle will raise this exception
            }
            catch (Win32Exception)
            {
                // We might get an AccessDenied error
            }
            catch (Exception ex)
            {
                // I don't expect to get other exceptions,
                CommandsCommon.CheckForSevereException(cmdlet, ex);
            }
            finally
            {
                if (tokenUserInfo != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(tokenUserInfo);
                }

                if (processTokenHandler != IntPtr.Zero)
                {
                    Win32Native.CloseHandle(processTokenHandler);
                }
            }

#endif
            return userName;
        }

        #endregion IncludeUserName
    }//GetProcessCommand
    #endregion GetProcessCommand

    #region WaitProcessCommand
    /// <summary>
    /// This class implements the Wait-process command
    /// </summary>
    [Cmdlet(VerbsLifecycle.Wait, "Process", DefaultParameterSetName = "Name", HelpUri = "http://go.microsoft.com/fwlink/?LinkID=135277")]
    public sealed class WaitProcessCommand : ProcessBaseCommand
    {
        #region Parameters

        /// <summary>
        /// Specifies the process IDs of the processes to be waited on.
        /// </summary>
        [Parameter(
            ParameterSetName = "Id",
            Position = 0,
            Mandatory = true,
            ValueFromPipelineByPropertyName = true)]
        [ValidateNotNullOrEmpty]
        [Alias("PID", "ProcessId")]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public int[] Id
        {
            get
            {
                return processIds;
            }
            set
            {
                myMode = MatchMode.ById;
                processIds = value;
            }
        }

        /// <summary>
        /// Name of the processes to wait on for termination
        /// </summary>
        [Parameter(
            ParameterSetName = "Name",
            Position = 0,
            Mandatory = true,
            ValueFromPipelineByPropertyName = true)]
        [Alias("ProcessName")]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public string[] Name
        {
            get { return processNames; }
            set
            {
                myMode = MatchMode.ByName;
                processNames = value;
            }
        }

        /// <summary>
        /// If specified, wait for this number of seconds
        /// </summary>
        [Parameter(Position = 1)]
        [Alias("TimeoutSec")]
        [ValidateNotNullOrEmpty]
        [ValidateRange(0, 32767)]
        public int Timeout
        {
            get
            {
                return _timeout;
            }
            set
            {
                _timeout = value;
                _timeOutSpecified = true;
            }
        }
        private int _timeout = 0;
        private bool _timeOutSpecified;


        #endregion Parameters

        private bool _disposed = false;

        #region IDisposable
        /// <summary>
        ///  Dispose method of IDisposable interface.
        /// </summary>
        public void Dispose()
        {
            if (_disposed == false)
            {
                if (_waitHandle != null)
                {
                    _waitHandle.Dispose();
                    _waitHandle = null;
                }
                _disposed = true;
            }
        }

        #endregion

        #region private methods
        // Handle Exited event and display process information.
        private void myProcess_Exited(object sender, System.EventArgs e)
        {
            if (0 == System.Threading.Interlocked.Decrement(ref _numberOfProcessesToWaitFor))
            {
                if (_waitHandle != null)
                {
                    _waitHandle.Set();
                }
            }
        }

        #endregion

        #region Overrides

        private List<Process> _processList = new List<Process>();

        //Wait handle which is used by thread to sleep.
        private ManualResetEvent _waitHandle;
        private int _numberOfProcessesToWaitFor;


        /// <summary>
        /// gets the list of process
        /// </summary>
        protected override void ProcessRecord()
        {
            //adding the processes into the list
            foreach (Process process in MatchingProcesses())
            {
                // Idle process has processid zero,so handle that because we cannot wait on it.
                if (process.Id == 0)
                {
                    WriteNonTerminatingError(process, null, ProcessResources.WaitOnIdleProcess, "WaitOnIdleProcess", ErrorCategory.ObjectNotFound);
                    continue;
                }

                // It cannot wait on itself
                if (process.Id.Equals(System.Diagnostics.Process.GetCurrentProcess().Id))
                {
                    WriteNonTerminatingError(process, null, ProcessResources.WaitOnItself, "WaitOnItself", ErrorCategory.ObjectNotFound);
                    continue;
                }
                _processList.Add(process);
            }
        } // ProcessRecord

        /// <summary>
        /// Wait for the process to terminate
        /// </summary>
        protected override void EndProcessing()
        {
            _waitHandle = new ManualResetEvent(false);
            foreach (Process process in _processList)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.EnableRaisingEvents = true;
                        process.Exited += new EventHandler(myProcess_Exited);
                        if (!process.HasExited)
                        {
                            System.Threading.Interlocked.Increment(ref _numberOfProcessesToWaitFor);
                        }
                    }
                }
                catch (Win32Exception exception)
                {
                    WriteNonTerminatingError(process, exception, ProcessResources.Process_is_not_terminated, "ProcessNotTerminated", ErrorCategory.CloseError);
                }
            }

            if (_numberOfProcessesToWaitFor > 0)
            {
                if (_timeOutSpecified)
                {
                    _waitHandle.WaitOne(_timeout * 1000);
                }
                else
                {
                    _waitHandle.WaitOne();
                }
            }
            foreach (Process process in _processList)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        //write the error
                        string message = StringUtil.Format(ProcessResources.ProcessNotTerminated, new object[] { process.ProcessName, process.Id });
                        ErrorRecord errorRecord = new ErrorRecord(new TimeoutException(message), "ProcessNotTerminated", ErrorCategory.CloseError, process);
                        WriteError(errorRecord);
                    }
                }
                catch (Win32Exception exception)
                {
                    WriteNonTerminatingError(process, exception, ProcessResources.Process_is_not_terminated, "ProcessNotTerminated", ErrorCategory.CloseError);
                }
            }
        }

        /// <summary>
        /// StopProcessing
        /// </summary>
        protected override void StopProcessing()
        {
            if (_waitHandle != null)
            {
                _waitHandle.Set();
            }
        }
        #endregion Overrides

    }//WaitProcessCommand
    #endregion WaitProcessCommand

    #region StopProcessCommand
    /// <summary>
    /// This class implements the stop-process command
    /// </summary>
    /// <remarks>
    /// Processes will be sorted before being stopped.  PM confirms
    /// that this should be fine.
    /// </remarks>
    [Cmdlet(VerbsLifecycle.Stop, "Process",
        DefaultParameterSetName = "Id",
        SupportsShouldProcess = true, HelpUri = "http://go.microsoft.com/fwlink/?LinkID=113412")]
    [OutputType(typeof(Process))]
    public sealed class StopProcessCommand : ProcessBaseCommand
    {
        #region Parameters
        /// <summary>
        /// Has the list of process names on which to this command will work
        /// </summary>
        // [ProcessNameGlobAttribute]
        [Parameter(
            ParameterSetName = "Name",
            Mandatory = true,
            ValueFromPipelineByPropertyName = true)]
        [Alias("ProcessName")]
        public string[] Name
        {
            get
            {
                return processNames;
            }
            set
            {
                processNames = value;
                myMode = MatchMode.ByName;
            }
        }

        /// <summary>
        /// gets/sets an array of process IDs
        /// </summary>
        [Parameter(
           Position = 0,
           ParameterSetName = "Id",
           Mandatory = true,
           ValueFromPipelineByPropertyName = true)]
        public int[] Id
        {
            get
            {
                return processIds;
            }
            set
            {
                myMode = MatchMode.ById;
                processIds = value;
            }
        }

        /// <summary>
        /// gets/sets an array of objects
        /// </summary>
        [Parameter(
            Position = 0,
            ParameterSetName = "InputObject",
            Mandatory = true,
            ValueFromPipeline = true)]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public new Process[] InputObject
        {
            get
            {
                return base.InputObject;
            }
            set
            {
                base.InputObject = value;
            }
        }

        private bool _passThru;
        /// <summary>
        /// The updated process object should be passed down the pipeline.
        /// </summary>
        [Parameter]
        public SwitchParameter PassThru
        {
            get { return _passThru; }
            set { _passThru = value; }
        }


        //Addition by v-ramch Mar 18 2008
        //Added force parameter 
        /// <summary>
        /// Specifies whether to force a process to kill
        /// even if it has dependent services.
        /// </summary>
        /// <value></value>
        [Parameter]
        [ValidateNotNullOrEmpty]
        public SwitchParameter Force { get; set; }

        #endregion Parameters

        #region Overrides
        /// <summary>
        /// Kill the processes.
        /// It is a non-terminating error if the Process.Kill() operation fails.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (myMode == MatchMode.All || (myMode == MatchMode.ByName && processNames == null))
            {
                Diagnostics.Assert(false, "trying to kill all processes");
                throw PSTraceSource.NewInvalidOperationException();
            }

            foreach (Process process in MatchingProcesses())
            {
                // confirm the operation first
                // this is always false if WhatIf is set
                // 2005-06-21 Moved this ahead of the hasExited check
                string targetString = StringUtil.Format(
                            ProcessResources.ProcessNameForConfirmation,
                            SafeGetProcessName(process),
                            SafeGetProcessId(process));

                if (!ShouldProcess(targetString)) { continue; }

                try
                {
                    // Many properties including Name are not available if the process has exited.  
                    // If this is the case, we skip the process. If the process is from a remote
                    // machine, then we generate a non-terminating error because .NET doesn't support
                    // terminate a remote process.
                    if (process.HasExited)
                    {
                        if (PassThru)
                            WriteObject(process);
                        continue;
                    }
                }
                catch (NotSupportedException ex)
                {
                    WriteNonTerminatingError(
                        process, ex, ProcessResources.CouldNotStopProcess,
                        "CouldNotStopProcess", ErrorCategory.InvalidOperation);
                    continue;
                }
                catch (Win32Exception ex)
                {
                    // This process could not be stopped, so write a non-terminating error.
                    WriteNonTerminatingError(
                        process, ex, ProcessResources.CouldNotStopProcess,
                        "CouldNotStopProcess", ErrorCategory.CloseError);
                    continue;
                }

                try
                {
                    //check if the process is current process and kill it at last
                    if (Process.GetCurrentProcess().Id == SafeGetProcessId(process))
                    {
                        _shouldKillCurrentProcess = true;
                        continue;
                    }

                    if (Platform.IsWindows && !Force)
                    {
                        // Check if the process is owned by current user
                        if (!IsProcessOwnedByCurrentUser(process))
                        {
                            string message = StringUtil.Format(
                                        ProcessResources.ConfirmStopProcess,
                                        SafeGetProcessName(process),
                                        SafeGetProcessId(process));

                            // caption: null = default caption
                            if (!ShouldContinue(message, null, ref _yesToAll, ref _noToAll))
                                continue;
                        }
                    }

                    //if the process is svchost stop all the dependent services before killing process
                    if (string.Equals(SafeGetProcessName(process), "SVCHOST", StringComparison.CurrentCultureIgnoreCase))
                    {
                        StopDependentService(process);
                    }

                    // kill the process
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
                catch (Win32Exception exception)
                {
                    if (!TryHasExited(process))
                    {
                        // This process could not be stopped,
                        // so write a non-terminating error.
                        WriteNonTerminatingError(
                            process, exception, ProcessResources.CouldNotStopProcess,
                            "CouldNotStopProcess", ErrorCategory.CloseError);
                        continue;
                    }
                }
                catch (InvalidOperationException exception)
                {
                    if (!TryHasExited(process))
                    {
                        // This process could not be stopped,
                        // so write a non-terminating error.
                        WriteNonTerminatingError(
                            process, exception, ProcessResources.CouldNotStopProcess,
                            "CouldNotStopProcess", ErrorCategory.CloseError);
                        continue;
                    }
                }

                if (PassThru)
                    WriteObject(process);
            }
        } // ProcessRecord


        /// <summary>
        /// Kill the current process here.
        /// </summary>
        protected override void EndProcessing()
        {
            if (_shouldKillCurrentProcess)
            {
                StopProcess(Process.GetCurrentProcess());
            }
        }//EndProcessing

        #endregion Overrides

        #region Private
        /// <summary>
        /// should the current powershell process to be killed
        /// </summary>
        private bool _shouldKillCurrentProcess;

        /// <summary>
        /// Boolean variables to display the warning using shouldcontinue
        /// </summary>
        private bool _yesToAll,_noToAll;

        /// <summary>
        /// Current windows user name
        /// </summary>
        private string _currentUserName;

        /// <summary>
        /// gets the owner of the process
        /// </summary>
        /// <param name="process"></param>
        /// <returns>returns the owner</returns>
        private bool IsProcessOwnedByCurrentUser(Process process)
        {
            const uint TOKEN_QUERY = 0x0008;
            IntPtr ph = IntPtr.Zero;
            try
            {
                if (Win32Native.OpenProcessToken(ClrFacade.GetSafeProcessHandle(process), TOKEN_QUERY, out ph))
                {
                    if (_currentUserName == null)
                    {
                        using (var currentUser = WindowsIdentity.GetCurrent())
                        {
                            _currentUserName = currentUser.Name;
                        }
                    }

                    using (var processUser = new WindowsIdentity(ph))
                    {
                        return string.Equals(processUser.Name, _currentUserName, StringComparison.CurrentCultureIgnoreCase);
                    }
                }
            }
            catch (IdentityNotMappedException)
            {
                //Catching IdentityMappedException
                //Need not throw error.
            }
            catch (ArgumentException)
            {
                //Catching ArgumentException. In Win2k3 Token is zero
                //Need not throw error.
            }
            finally
            {
                if (ph != IntPtr.Zero) { Win32Native.CloseHandle(ph); }
            }

            return false;
        }

        /// <summary>
        /// Stop the service that depends on the process and its child services
        /// </summary>
        /// <param name="process"></param>
        private void StopDependentService(Process process)
        {
            string queryString = "Select * From Win32_Service Where ProcessId=" + SafeGetProcessId(process) + " And State !='Stopped'";

            try
            {
                using (CimSession cimSession = CimSession.Create(null))
                {
                    IEnumerable<CimInstance> serviceList =
                        cimSession.QueryInstances("root/cimv2", "WQL", queryString);
                    foreach (CimInstance oService in serviceList)
                    {
                        string serviceName = oService.CimInstanceProperties["Name"].Value.ToString();
                        using (var service = new System.ServiceProcess.ServiceController(serviceName))
                        {
                            //try stopping the service, if cant we are not writing exception 
                            try
                            {
                                service.Stop();
                                // Wait 2 sec for the status to become 'Stopped'
                                service.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, new TimeSpan(0, 0, 2));
                            }
                            catch (Win32Exception) { }
                            catch (InvalidOperationException) { }
                            catch (System.ServiceProcess.TimeoutException) { }
                        }
                    }
                }
            }
            catch (CimException ex)
            {
                var errorRecord = new ErrorRecord(ex, "GetCimException", ErrorCategory.InvalidOperation, null);
                WriteError(errorRecord);
            }
        }

        /// <summary>
        /// stops the given process throws non terminating error if cant
        /// <param name="process" >process to be stopped</param>
        /// <returns>true if process stopped successfully else false</returns>
        /// </summary>
        private void StopProcess(Process process)
        {
            Exception exception = null;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Win32Exception e)
            {
                exception = e;
            }
            catch (InvalidOperationException e)
            {
                exception = e;
            }

            if (null != exception)
            {
                if (!TryHasExited(process))
                {
                    // This process could not be stopped,
                    // so write a non-terminating error.
                    WriteNonTerminatingError(
                        process, exception, ProcessResources.CouldNotStopProcess,
                        "CouldNotStopProcess", ErrorCategory.CloseError);
                }
            }
        }

        #endregion Private
    }//StopProcessCommand
    #endregion StopProcessCommand

    #region DebugProcessCommand
    /// <summary>
    /// This class implements the Debug-process command
    /// </summary>
    [Cmdlet(VerbsDiagnostic.Debug, "Process", DefaultParameterSetName = "Name", SupportsShouldProcess = true, HelpUri = "http://go.microsoft.com/fwlink/?LinkID=135206")]
    public sealed class DebugProcessCommand : ProcessBaseCommand
    {
        #region Parameters

        /// <summary>
        /// Specifies the process IDs of the processes to be waited on.
        /// </summary>
        [Parameter(
            ParameterSetName = "Id",
            Position = 0,
            Mandatory = true,
            ValueFromPipelineByPropertyName = true)]
        [ValidateNotNullOrEmpty]
        [Alias("PID", "ProcessId")]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public int[] Id
        {
            get
            {
                return processIds;
            }
            set
            {
                myMode = MatchMode.ById;
                processIds = value;
            }
        }

        /// <summary>
        /// Name of the processes to wait on for termination
        /// </summary>
        [Parameter(
            ParameterSetName = "Name",
            Position = 0,
            Mandatory = true,
            ValueFromPipelineByPropertyName = true)]
        [Alias("ProcessName")]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public string[] Name
        {
            get { return processNames; }
            set
            {
                myMode = MatchMode.ByName;
                processNames = value;
            }
        }

        #endregion Parameters

        #region Overrides

        /// <summary>
        /// gets the list of process and attach the debugger to the processes
        /// </summary>
        protected override void ProcessRecord()
        {
            //for the processes
            foreach (Process process in MatchingProcesses())
            {
                string targetMessage = StringUtil.Format(
                            ProcessResources.ProcessNameForConfirmation,
                            SafeGetProcessName(process),
                            SafeGetProcessId(process));

                if (!ShouldProcess(targetMessage)) { continue; }

                // sometimes Idle process has processid zero,so handle that because we cannot attach debugger to it.
                if (process.Id == 0)
                {
                    WriteNonTerminatingError(
                        process, null, ProcessResources.NoDebuggerFound,
                        "NoDebuggerFound", ErrorCategory.ObjectNotFound);
                    continue;
                }

                try
                {
                    // If the process has exited, we skip it. If the process is from a remote
                    // machine, then we generate a non-terminating error.
                    if (process.HasExited) { continue; }
                }
                catch (NotSupportedException ex)
                {
                    WriteNonTerminatingError(
                        process, ex, ProcessResources.CouldNotDebugProcess,
                        "CouldNotDebugProcess", ErrorCategory.InvalidOperation);
                    continue;
                }
                catch (Win32Exception ex)
                {
                    // This process could not be stopped, so write a non-terminating error.
                    WriteNonTerminatingError(
                        process, ex, ProcessResources.CouldNotDebugProcess,
                        "CouldNotDebugProcess", ErrorCategory.CloseError);
                    continue;
                }

                AttachDebuggerToProcess(process);
            }
        } // ProcessRecord

        #endregion Overrides

        /// <summary>
        /// Attach debugger to the process
        /// </summary>
        private void AttachDebuggerToProcess(Process process)
        {
            string searchQuery = "Select * From Win32_Process Where ProcessId=" + SafeGetProcessId(process);
            using (CimSession cimSession = CimSession.Create(null))
            {
                IEnumerable<CimInstance> processCollection =
                    cimSession.QueryInstances("root/cimv2", "WQL", searchQuery);
                foreach (CimInstance processInstance in processCollection)
                {
                    try
                    {
                        // Call the AttachDebugger method
                        CimMethodResult result = cimSession.InvokeMethod(processInstance, "AttachDebugger", null);
                        int returnCode = Convert.ToInt32(result.ReturnValue.Value, System.Globalization.CultureInfo.CurrentCulture);
                        if (returnCode != 0)
                        {
                            var ex = new InvalidOperationException(MapReturnCodeToErrorMessage(returnCode));
                            WriteNonTerminatingError(
                                process, ex, ProcessResources.CouldNotDebugProcess,
                                "CouldNotDebugProcess", ErrorCategory.InvalidOperation);
                        }
                    }
                    catch (CimException e)
                    {
                        string message = e.Message;
                        if (!string.IsNullOrEmpty(message)) { message = message.Trim(); }

                        var errorRecord = new ErrorRecord(
                                new InvalidOperationException(StringUtil.Format(ProcessResources.DebuggerError, message)),
                                "GetCimException", ErrorCategory.InvalidOperation, null);
                        WriteError(errorRecord);
                    }
                }
            }
        }

        /// <summary>
        /// Map the return code from 'AttachDebugger' to error message
        /// </summary>
        private string MapReturnCodeToErrorMessage(int returnCode)
        {
            string errorMessage = string.Empty;
            switch (returnCode)
            {
                case 2: errorMessage = ProcessResources.AttachDebuggerReturnCode2; break;
                case 3: errorMessage = ProcessResources.AttachDebuggerReturnCode3; break;
                case 8: errorMessage = ProcessResources.AttachDebuggerReturnCode8; break;
                case 9: errorMessage = ProcessResources.AttachDebuggerReturnCode9; break;
                case 21: errorMessage = ProcessResources.AttachDebuggerReturnCode21; break;
                default: Diagnostics.Assert(false, "Unreachable code."); break;
            }
            return errorMessage;
        }
    }
    #endregion DebugProcessCommand

    #region StartProcessCommand

    /// <summary>
    /// This class implements the Start-process command
    /// </summary>
    [Cmdlet(VerbsLifecycle.Start, "Process", DefaultParameterSetName = "Default", HelpUri = "http://go.microsoft.com/fwlink/?LinkID=135261")]
    [OutputType(typeof(Process))]
    public sealed class StartProcessCommand : PSCmdlet, IDisposable
    {
        private ManualResetEvent _waithandle = null;
        private bool _isDefaultSetParameterSpecified = false;

        #region Parameters

        /// <summary>
        /// Path/FileName of the process to start
        /// </summary>
        [Parameter(Mandatory = true, Position = 0)]
        [ValidateNotNullOrEmpty]
        [Alias("PSPath")]
        public string FilePath { get; set; }


        /// <summary>
        /// Arguments for the process
        /// </summary>
        [Parameter(Position = 1)]
        [Alias("Args")]
        [ValidateNotNullOrEmpty]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public string[] ArgumentList { get; set; }


        /// <summary>
        /// Credentials for the process
        /// </summary>
        [Parameter(ParameterSetName = "Default")]
        [Alias("RunAs")]
        [ValidateNotNullOrEmpty]
        [Credential]
        public PSCredential Credential
        {
            get { return _credential; }
            set
            {
                _credential = value;
                _isDefaultSetParameterSpecified = true;
            }
        }
        private PSCredential _credential;

        /// <summary>
        ///  working directory of the process
        /// </summary>
        [Parameter]
        [ValidateNotNullOrEmpty]
        public string WorkingDirectory { get; set; }


        /// <summary>
        /// load user profile from registry
        /// </summary>
        [Parameter(ParameterSetName = "Default")]
        [Alias("Lup")]
        public SwitchParameter LoadUserProfile
        {
            get { return _loaduserprofile; }
            set
            {
                _loaduserprofile = value;
                _isDefaultSetParameterSpecified = true;
            }
        }
        private SwitchParameter _loaduserprofile = SwitchParameter.Present;

        /// <summary>
        /// starts process in a new window
        /// </summary>
        [Parameter(ParameterSetName = "Default")]
        [Alias("nnw")]
        public SwitchParameter NoNewWindow
        {
            get { return _nonewwindow; }
            set
            {
                _nonewwindow = value;
                _isDefaultSetParameterSpecified = true;
            }
        }
        private SwitchParameter _nonewwindow;

        /// <summary>
        /// passthru parameter
        /// </summary>
        [Parameter]
        public SwitchParameter PassThru { get; set; }


        /// <summary>
        /// Redirect error
        /// </summary>
        [Parameter(ParameterSetName = "Default")]
        [Alias("RSE")]
        [ValidateNotNullOrEmpty]
        public string RedirectStandardError
        {
            get { return _redirectstandarderror; }
            set
            {
                _redirectstandarderror = value;
                _isDefaultSetParameterSpecified = true;
            }
        }
        private string _redirectstandarderror;


        /// <summary>
        /// Redirect input 
        /// </summary>
        [Parameter(ParameterSetName = "Default")]
        [Alias("RSI")]
        [ValidateNotNullOrEmpty]
        public string RedirectStandardInput
        {
            get { return _redirectstandardinput; }
            set
            {
                _redirectstandardinput = value;
                _isDefaultSetParameterSpecified = true;
            }
        }
        private string _redirectstandardinput;


        /// <summary>
        /// Redirect output 
        /// </summary>
        [Parameter(ParameterSetName = "Default")]
        [Alias("RSO")]
        [ValidateNotNullOrEmpty]
        public string RedirectStandardOutput
        {
            get { return _redirectstandardoutput; }
            set
            {
                _redirectstandardoutput = value;
                _isDefaultSetParameterSpecified = true;
            }
        }
        private string _redirectstandardoutput;

#if !CORECLR
        /// <summary>
        /// Verb
        /// </summary>
        /// <remarks>
        /// The 'Verb' parameter is not supported in OneCore PowerShell 
        /// because 'UseShellExecute' is not supported in CoreCLR.
        /// </remarks>
        [Parameter(ParameterSetName = "UseShellExecute")]
        [ValidateNotNullOrEmpty]
        public string Verb { get; set; }


        /// <summary>
        /// Window style of the process window
        /// </summary>
        /// <remarks>
        /// The 'WindowStyle' is not supported in CoreCLR
        /// </remarks>
        [Parameter]
        [ValidateNotNullOrEmpty]
        public ProcessWindowStyle WindowStyle
        {
            get { return _windowstyle; }
            set
            {
                _windowstyle = value;
                _windowstyleSpecified = true;
            }
        }
        private ProcessWindowStyle _windowstyle = ProcessWindowStyle.Normal;
        private bool _windowstyleSpecified = false;
#endif

        /// <summary>
        ///  wait for th eprocess to terminate
        /// </summary>
        [Parameter]
        public SwitchParameter Wait { get; set; }

        /// <summary>
        ///  Default Environment
        /// </summary>
        [Parameter(ParameterSetName = "Default")]
        public SwitchParameter UseNewEnvironment
        {
            get { return _UseNewEnvironment; }
            set
            {
                _UseNewEnvironment = value;
                _isDefaultSetParameterSpecified = true;
            }
        }
        private SwitchParameter _UseNewEnvironment;

        private StreamWriter _outputWriter;
        private StreamWriter _errorWriter;

        #endregion

        #region overrides

        /// <summary>
        /// 
        /// </summary>
        protected override void BeginProcessing()
        {
            //create an instance of the ProcessStartInfo Class
            ProcessStartInfo startInfo = new ProcessStartInfo();
            string message = String.Empty;

            //Path = Mandatory parameter -> Will not be empty.
            try
            {
                CommandInfo cmdinfo = CommandDiscovery.LookupCommandInfo(
                    FilePath, CommandTypes.Application | CommandTypes.ExternalScript,
                    SearchResolutionOptions.None, CommandOrigin.Internal, this.Context);

                startInfo.FileName = cmdinfo.Definition;
            }
            catch (CommandNotFoundException)
            {
                startInfo.FileName = FilePath;
            }
            //Arguments
            if (ArgumentList != null)
            {
                StringBuilder sb = new StringBuilder();
                foreach (string str in ArgumentList)
                {
                    sb.Append(str);
                    sb.Append(' ');
                }
                startInfo.Arguments = sb.ToString(); ;
            }


            //WorkingDirectory
            if (WorkingDirectory != null)
            {
                //WorkingDirectory -> Not Exist -> Throw Error
                WorkingDirectory = ResolveFilePath(WorkingDirectory);
                if (!Directory.Exists(WorkingDirectory))
                {
                    message = StringUtil.Format(ProcessResources.InvalidInput, "WorkingDirectory");
                    ErrorRecord er = new ErrorRecord(new DirectoryNotFoundException(message), "DirectoryNotFoundException", ErrorCategory.InvalidOperation, null);
                    WriteError(er);
                    return;
                }
                startInfo.WorkingDirectory = WorkingDirectory;
            }
            else
            {
                //Working Directory not specified -> Assign Current Path.
                startInfo.WorkingDirectory = ResolveFilePath(this.SessionState.Path.CurrentFileSystemLocation.Path);
            }

            if (this.ParameterSetName.Equals("Default"))
            {
                if (_isDefaultSetParameterSpecified)
                {
                    startInfo.UseShellExecute = false;
                }

                //UseNewEnvironment
                if (_UseNewEnvironment)
                {
                    ClrFacade.GetProcessEnvironment(startInfo).Clear();
                    LoadEnvironmentVariable(startInfo, Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Machine));
                    LoadEnvironmentVariable(startInfo, Environment.GetEnvironmentVariables(EnvironmentVariableTarget.User));
                }

#if !CORECLR    // 'WindowStyle' not supported in CoreCLR
                if (_nonewwindow && _windowstyleSpecified)
                {
                    message = StringUtil.Format(ProcessResources.ContradictParametersSpecified, "-NoNewWindow", "-WindowStyle");
                    ErrorRecord er = new ErrorRecord(new InvalidOperationException(message), "InvalidOperationException", ErrorCategory.InvalidOperation, null);
                    WriteError(er);
                    return;
                }

                //WindowStyle
                startInfo.WindowStyle = _windowstyle;
#endif
                //NewWindow
                if (_nonewwindow)
                {
                    startInfo.CreateNoWindow = _nonewwindow;
                }

                //LoadUserProfile.
                if (Platform.IsWindows)
                {
                    startInfo.LoadUserProfile = _loaduserprofile;
                }

                if (_credential != null)
                {
                    //Gets NetworkCredentials
                    NetworkCredential nwcredential = _credential.GetNetworkCredential();
                    startInfo.UserName = nwcredential.UserName;
                    if (String.IsNullOrEmpty(nwcredential.Domain))
                    {
                        startInfo.Domain = ".";
                    }
                    else
                    {
                        startInfo.Domain = nwcredential.Domain;
                    }
#if CORECLR
                    startInfo.PasswordInClearText = ClrFacade.ConvertSecureStringToString(_credential.Password);
#else
                    startInfo.Password = _credential.Password;
#endif
                }

                //RedirectionInput File Check -> Not Exist -> Throw Error
                if (_redirectstandardinput != null)
                {
                    _redirectstandardinput = ResolveFilePath(_redirectstandardinput);
                    if (!File.Exists(_redirectstandardinput))
                    {
                        message = StringUtil.Format(ProcessResources.InvalidInput, "RedirectStandardInput '" + this.RedirectStandardInput + "'");
                        ErrorRecord er = new ErrorRecord(new FileNotFoundException(message), "FileNotFoundException", ErrorCategory.InvalidOperation, null);
                        WriteError(er);
                        return;
                    }
                }

                //RedirectionInput == RedirectionOutput -> Throw Error
                if (_redirectstandardinput != null && _redirectstandardoutput != null)
                {
                    _redirectstandardinput = ResolveFilePath(_redirectstandardinput);
                    _redirectstandardoutput = ResolveFilePath(_redirectstandardoutput);
                    if (_redirectstandardinput.Equals(_redirectstandardoutput, StringComparison.CurrentCultureIgnoreCase))
                    {
                        message = StringUtil.Format(ProcessResources.DuplicateEntry, "RedirectStandardInput", "RedirectStandardOutput");
                        ErrorRecord er = new ErrorRecord(new InvalidOperationException(message), "InvalidOperationException", ErrorCategory.InvalidOperation, null);
                        WriteError(er);
                        return;
                    }
                }

                //RedirectionInput == RedirectionError -> Throw Error
                if (_redirectstandardinput != null && _redirectstandarderror != null)
                {
                    _redirectstandardinput = ResolveFilePath(_redirectstandardinput);
                    _redirectstandarderror = ResolveFilePath(_redirectstandarderror);
                    if (_redirectstandardinput.Equals(_redirectstandarderror, StringComparison.CurrentCultureIgnoreCase))
                    {
                        message = StringUtil.Format(ProcessResources.DuplicateEntry, "RedirectStandardInput", "RedirectStandardError");
                        ErrorRecord er = new ErrorRecord(new InvalidOperationException(message), "InvalidOperationException", ErrorCategory.InvalidOperation, null);
                        WriteError(er);
                        return;
                    }
                }

                //RedirectionOutput == RedirectionError -> Throw Error
                if (_redirectstandardoutput != null && _redirectstandarderror != null)
                {
                    _redirectstandarderror = ResolveFilePath(_redirectstandarderror);
                    _redirectstandardoutput = ResolveFilePath(_redirectstandardoutput);
                    if (_redirectstandardoutput.Equals(_redirectstandarderror, StringComparison.CurrentCultureIgnoreCase))
                    {
                        message = StringUtil.Format(ProcessResources.DuplicateEntry, "RedirectStandardOutput", "RedirectStandardError");
                        ErrorRecord er = new ErrorRecord(new InvalidOperationException(message), "InvalidOperationException", ErrorCategory.InvalidOperation, null);
                        WriteError(er);
                        return;
                    }
                }
            }
#if !CORECLR // 'UseShellExecute' is not supported in CoreCLR
            else if (ParameterSetName.Equals("UseShellExecute"))
            {
                startInfo.UseShellExecute = true;
                //Verb
                if (Verb != null)
                {
                    startInfo.Verb = Verb;
                }

                //WindowStyle
                startInfo.WindowStyle = _windowstyle;
            }
#endif
            //Starts the Process
            Process process;
            if (Platform.IsWindows)
            {
                process = start(startInfo);
            }
            else
            {
                process = new Process();
                process.StartInfo = startInfo;
                SetupInputOutputRedirection(process);
                process.Start();
                if (process.StartInfo.RedirectStandardOutput)
                {
                    process.BeginOutputReadLine();
                }
                if (process.StartInfo.RedirectStandardError)
                {
                    process.BeginErrorReadLine();
                }
                if (process.StartInfo.RedirectStandardInput)
                {
                    WriteToStandardInput(process);
                }
            }
            //Wait and Passthru Implementation.

            if (PassThru.IsPresent)
            {
                if (process != null)
                {
                    WriteObject(process);
                }
                else
                {
                    message = StringUtil.Format(ProcessResources.CannotStarttheProcess);
                    ErrorRecord er = new ErrorRecord(new InvalidOperationException(message), "InvalidOperationException", ErrorCategory.InvalidOperation, null);
                    ThrowTerminatingError(er);
                }
            }

            if (Wait.IsPresent)
            {
                if (process != null)
                {
                    if (!process.HasExited)
                    {
                        if (Platform.IsWindows)
                        {
                            _waithandle = new ManualResetEvent(false);

                            // Create and start the job object
                            ProcessCollection jobObject = new ProcessCollection();
                            if (jobObject.AssignProcessToJobObject(process))
                            {
                                // Wait for the job object to finish
                                jobObject.WaitOne(_waithandle);
                            }
                            else if (!process.HasExited)
                            {
                                // WinBlue: 27537 Start-Process -Wait doesn't work in a remote session on Windows 7 or lower.
                                process.Exited += new EventHandler(myProcess_Exited);
                                process.EnableRaisingEvents = true;
                                process.WaitForExit();
                            }
                        }
                        else
                        {
                            process.WaitForExit();
                        }
                    }
                }
                else
                {
                    message = StringUtil.Format(ProcessResources.CannotStarttheProcess);
                    ErrorRecord er = new ErrorRecord(new InvalidOperationException(message), "InvalidOperationException", ErrorCategory.InvalidOperation, null);
                    ThrowTerminatingError(er);
                }
            }
        }
        /// <summary>
        /// Implements ^c, after creating a process.
        /// </summary>
        protected override void StopProcessing()
        {
            if (_waithandle != null)
            {
                _waithandle.Set();
            }
        }

        /// <summary>
        /// When Process exits the wait handle is set. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void myProcess_Exited(object sender, System.EventArgs e)
        {
            if (_waithandle != null)
            {
                _waithandle.Set();
            }
        }

        #region Private Methods

        private void LoadEnvironmentVariable(ProcessStartInfo startinfo, IDictionary EnvironmentVariables)
        {
            var processEnvironment = ClrFacade.GetProcessEnvironment(startinfo);
            foreach (DictionaryEntry entry in EnvironmentVariables)
            {
                if (processEnvironment.ContainsKey(entry.Key.ToString()))
                {
                    processEnvironment.Remove(entry.Key.ToString());
                }
                if (entry.Key.ToString().Equals("PATH"))
                {
                    processEnvironment.Add(entry.Key.ToString(), Environment.GetEnvironmentVariable(entry.Key.ToString(), EnvironmentVariableTarget.Machine) + ";" + Environment.GetEnvironmentVariable(entry.Key.ToString(), EnvironmentVariableTarget.User));
                }
                else
                {
                    processEnvironment.Add(entry.Key.ToString(), entry.Value.ToString());
                }
            }
        }

        private void StdOutputHandler(object sendingProcess, DataReceivedEventArgs outLine)
        {
            if (!String.IsNullOrEmpty(outLine.Data))
            {
                _outputWriter.WriteLine(outLine.Data);
                _outputWriter.Flush();
            }
        }

        private void StdErrorHandler(object sendingProcess, DataReceivedEventArgs outLine)
        {
            if (!String.IsNullOrEmpty(outLine.Data))
            {
                _errorWriter.WriteLine(outLine.Data);
                _errorWriter.Flush();
            }
        }

        private void ExitHandler(object sendingProcess, System.EventArgs e)
        {
            // To avoid a race condition with Std*Handler, let's wait a bit before closing the streams
            // System.Timer is not supported in CoreCLR, so let's spawn a new thread to do the wait

            Thread delayedStreamClosing = new Thread(StreamClosing);
            delayedStreamClosing.Start();
        }

        private void StreamClosing()
        {
            Thread.Sleep(1000);

            if (_outputWriter != null)
            {
                _outputWriter.Dispose();
            }
            if (_errorWriter != null)
            {
                _errorWriter.Dispose();
            }
        }

        private void SetupInputOutputRedirection(Process p)
        {
            if (_redirectstandardinput != null)
            {
                p.StartInfo.RedirectStandardInput = true;
                _redirectstandardinput = ResolveFilePath(_redirectstandardinput);
            }
            else
            {
                p.StartInfo.RedirectStandardInput = false;
            }

            if (_redirectstandardoutput != null)
            {
                p.StartInfo.RedirectStandardOutput = true;
                _redirectstandardoutput = ResolveFilePath(_redirectstandardoutput);
                p.OutputDataReceived += new DataReceivedEventHandler(StdOutputHandler);

                // Can't do StreamWriter(string) in coreCLR
                _outputWriter = new StreamWriter(new FileStream(_redirectstandardoutput, FileMode.Create));
            }
            else
            {
                p.StartInfo.RedirectStandardOutput = false;
                _outputWriter = null;
            }

            if (_redirectstandarderror != null)
            {
                p.StartInfo.RedirectStandardError = true;
                _redirectstandarderror = ResolveFilePath(_redirectstandarderror);
                p.ErrorDataReceived += new DataReceivedEventHandler(StdErrorHandler);

                // Can't do StreamWriter(string) in coreCLR
                _errorWriter = new StreamWriter(new FileStream(_redirectstandarderror, FileMode.Create));
            }
            else
            {
                p.StartInfo.RedirectStandardError = false;
                _errorWriter = null;
            }

            p.EnableRaisingEvents = true;
            p.Exited += new EventHandler(ExitHandler);
        }

        private void WriteToStandardInput(Process p)
        {
            StreamWriter writer = p.StandardInput;
            using (StreamReader reader = new StreamReader(new FileStream(_redirectstandardinput, FileMode.Open)))
            {
                string line = reader.ReadToEnd();
                writer.WriteLine(line);
            }
            writer.Dispose();
        }

        private Process StartWithCreateProcess(ProcessStartInfo startinfo)
        {
            ProcessNativeMethods.STARTUPINFO lpStartupInfo = new ProcessNativeMethods.STARTUPINFO();
            SafeNativeMethods.PROCESS_INFORMATION lpProcessInformation = new SafeNativeMethods.PROCESS_INFORMATION();
            int error = 0;
            GCHandle pinnedEnvironmentBlock = new GCHandle();
            string message = String.Empty;

            //building the cmdline with the file name given and it's arguments
            StringBuilder cmdLine = BuildCommandLine(startinfo.FileName, startinfo.Arguments);

            try
            {
                //RedirectionStandardInput
                if (_redirectstandardinput != null)
                {
                    startinfo.RedirectStandardInput = true;
                    _redirectstandardinput = ResolveFilePath(_redirectstandardinput);
                    lpStartupInfo.hStdInput = GetSafeFileHandleForRedirection(_redirectstandardinput, ProcessNativeMethods.OPEN_EXISTING);
                }
                else
                {
                    lpStartupInfo.hStdInput = new SafeFileHandle(ProcessNativeMethods.GetStdHandle(-10), false);
                }
                //RedirectionStandardOutput
                if (_redirectstandardoutput != null)
                {
                    startinfo.RedirectStandardOutput = true;
                    _redirectstandardoutput = ResolveFilePath(_redirectstandardoutput);
                    lpStartupInfo.hStdOutput = GetSafeFileHandleForRedirection(_redirectstandardoutput, ProcessNativeMethods.CREATE_ALWAYS);
                }
                else
                {
                    lpStartupInfo.hStdOutput = new SafeFileHandle(ProcessNativeMethods.GetStdHandle(-11), false);
                }
                //RedirectionStandardError
                if (_redirectstandarderror != null)
                {
                    startinfo.RedirectStandardError = true;
                    _redirectstandarderror = ResolveFilePath(_redirectstandarderror);
                    lpStartupInfo.hStdError = GetSafeFileHandleForRedirection(_redirectstandarderror, ProcessNativeMethods.CREATE_ALWAYS);
                }
                else
                {
                    lpStartupInfo.hStdError = new SafeFileHandle(ProcessNativeMethods.GetStdHandle(-12), false);
                }
                //STARTF_USESTDHANDLES 
                lpStartupInfo.dwFlags = 0x100;

                int creationFlags = 0;

                if (startinfo.CreateNoWindow)
                {
                    //No new window: Inherit the parent process's console window
                    creationFlags = 0x00000000;
                }
                else
                {
                    //CREATE_NEW_CONSOLE
                    creationFlags |= 0x00000010;
                    //STARTF_USESHOWWINDOW
                    lpStartupInfo.dwFlags |= 0x00000001;

#if CORECLR
                    //SW_SHOWNORMAL
                    lpStartupInfo.wShowWindow = 1;
#else
                    switch (startinfo.WindowStyle)
                    {
                        case ProcessWindowStyle.Normal:
                            //SW_SHOWNORMAL
                            lpStartupInfo.wShowWindow = 1;
                            break;
                        case ProcessWindowStyle.Minimized:
                            //SW_SHOWMINIMIZED
                            lpStartupInfo.wShowWindow = 2;
                            break;
                        case ProcessWindowStyle.Maximized:
                            //SW_SHOWMAXIMIZED
                            lpStartupInfo.wShowWindow = 3;
                            break;
                        case ProcessWindowStyle.Hidden:
                            //SW_HIDE
                            lpStartupInfo.wShowWindow = 0;
                            break;
                    }
#endif
                }

                // Create the new process suspended so we have a chance to get a corresponding Process object in case it terminates quickly.
                creationFlags |= 0x00000004;

                IntPtr AddressOfEnvironmentBlock = IntPtr.Zero;
                var environmentVars = ClrFacade.GetProcessEnvironment(startinfo);
                if (environmentVars != null)
                {
                    if (this.UseNewEnvironment)
                    {
                        bool unicode = false;
                        if (ProcessManager.IsNt)
                        {
                            creationFlags |= 0x400;
                            unicode = true;
                        }
                        pinnedEnvironmentBlock = GCHandle.Alloc(EnvironmentBlock.ToByteArray(environmentVars, unicode), GCHandleType.Pinned);
                        AddressOfEnvironmentBlock = pinnedEnvironmentBlock.AddrOfPinnedObject();
                    }
                }
                bool flag;

                if (_credential != null)
                {
                    ProcessNativeMethods.LogonFlags logonFlags = 0;
                    if (startinfo.LoadUserProfile)
                    {
                        logonFlags = ProcessNativeMethods.LogonFlags.LOGON_WITH_PROFILE;
                    }

                    IntPtr password = IntPtr.Zero;
                    try
                    {
#if CORECLR
                        password = (startinfo.PasswordInClearText == null) ? Marshal.StringToCoTaskMemUni(string.Empty) : Marshal.StringToCoTaskMemUni(startinfo.PasswordInClearText);
#else
                        password = (startinfo.Password == null) ? Marshal.StringToCoTaskMemUni(string.Empty) : ClrFacade.SecureStringToCoTaskMemUnicode(startinfo.Password);
#endif
                        flag = ProcessNativeMethods.CreateProcessWithLogonW(startinfo.UserName, startinfo.Domain, password, logonFlags, null, cmdLine, creationFlags, AddressOfEnvironmentBlock, startinfo.WorkingDirectory, lpStartupInfo, lpProcessInformation);
                        if (!flag)
                        {
                            error = Marshal.GetLastWin32Error();
                            ErrorRecord er = null;

                            if (error == 0xc1)
                            {
                                message = StringUtil.Format(ProcessResources.InvalidApplication, FilePath);
                            }
                            else if (error == 0x424)
                            {
                                // The API 'CreateProcessWithLogonW' depends on the 'Secondary Logon' service, but the component 'Microsoft-Windows-SecondaryLogonService' 
                                // is not installed in OneCoreUAP. We will get error code 0x424 when the service is not available.
                                message = StringUtil.Format(ProcessResources.ParameterNotSupported, "-Credential", "Start-Process");
                                er = new ErrorRecord(new NotSupportedException(message), "NotSupportedException", ErrorCategory.NotInstalled, null);
                            }
                            else
                            {
                                Win32Exception win32ex = new Win32Exception(error);
                                message = StringUtil.Format(ProcessResources.InvalidStartProcess, win32ex.Message);
                            }

                            er = er ?? new ErrorRecord(new InvalidOperationException(message), "InvalidOperationException", ErrorCategory.InvalidOperation, null);
                            ThrowTerminatingError(er);
                        }
                        goto Label_03AE;
                    }
                    finally
                    {
                        if (password != IntPtr.Zero)
                        {
                            Marshal.ZeroFreeCoTaskMemUnicode(password);
                        }
                    }
                }//end of if

                ProcessNativeMethods.SECURITY_ATTRIBUTES lpProcessAttributes = new ProcessNativeMethods.SECURITY_ATTRIBUTES();
                ProcessNativeMethods.SECURITY_ATTRIBUTES lpThreadAttributes = new ProcessNativeMethods.SECURITY_ATTRIBUTES();
                flag = ProcessNativeMethods.CreateProcess(null, cmdLine, lpProcessAttributes, lpThreadAttributes, true, creationFlags, AddressOfEnvironmentBlock, startinfo.WorkingDirectory, lpStartupInfo, lpProcessInformation);
                if (!flag)
                {
                    error = Marshal.GetLastWin32Error();

                    Win32Exception win32ex = new Win32Exception(error);
                    message = StringUtil.Format(ProcessResources.InvalidStartProcess, win32ex.Message);
                    ErrorRecord er = new ErrorRecord(new InvalidOperationException(message), "InvalidOperationException", ErrorCategory.InvalidOperation, null);
                    ThrowTerminatingError(er);
                }

            Label_03AE:

                // At this point, we should have a suspended process.  Get the .Net Process object, resume the process, and return.
                Process result = Process.GetProcessById(lpProcessInformation.dwProcessId);
                ProcessNativeMethods.ResumeThread(lpProcessInformation.hThread);

                return result;
            }
            finally
            {
                if (pinnedEnvironmentBlock.IsAllocated)
                {
                    pinnedEnvironmentBlock.Free();
                }

                lpStartupInfo.Dispose();
                lpProcessInformation.Dispose();
            }
        }

        private Process StartWithShellExecute(ProcessStartInfo startInfo)
        {
            string message = String.Empty;
            Process result = null;
            try
            {
                result = Process.Start(startInfo);
            }
            catch (Win32Exception ex)
            {
                message = StringUtil.Format(ProcessResources.InvalidStartProcess, ex.Message);
                ErrorRecord er = new ErrorRecord(new InvalidOperationException(message), "InvalidOperationException", ErrorCategory.InvalidOperation, null);
                ThrowTerminatingError(er);
            }
            return result;
        }

        private Process start(ProcessStartInfo startInfo)
        {
            Process process = null;
            if (startInfo.UseShellExecute)
            {
                process = StartWithShellExecute(startInfo);
            }
            else
            {
                process = StartWithCreateProcess(startInfo);
            }
            return process;
        }
        #endregion

        #endregion

        #region IDisposable Overrides

        /// <summary>
        /// Dispose WaitHandle used to honor -Wait parameter
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            System.GC.SuppressFinalize(this);
        }

        private void Dispose(bool isDisposing)
        {
            if (_waithandle != null)
            {
                _waithandle.Dispose();
                _waithandle = null;
            }
        }

        #endregion

        #region Private Methods

        private string ResolveFilePath(string path)
        {
            string filepath = PathUtils.ResolveFilePath(path, this);
            return filepath;
        }

        private SafeFileHandle GetSafeFileHandleForRedirection(string RedirectionPath, uint dwCreationDisposition)
        {
            System.IntPtr hFileHandle = System.IntPtr.Zero;
            ProcessNativeMethods.SECURITY_ATTRIBUTES lpSecurityAttributes = new ProcessNativeMethods.SECURITY_ATTRIBUTES();


            hFileHandle = ProcessNativeMethods.CreateFileW(RedirectionPath,
                ProcessNativeMethods.GENERIC_READ | ProcessNativeMethods.GENERIC_WRITE,
                ProcessNativeMethods.FILE_SHARE_WRITE | ProcessNativeMethods.FILE_SHARE_READ,
                lpSecurityAttributes,
                dwCreationDisposition,
                ProcessNativeMethods.FILE_ATTRIBUTE_NORMAL,
                System.IntPtr.Zero);
            if (hFileHandle == System.IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Win32Exception win32ex = new Win32Exception(error);
                string message = StringUtil.Format(ProcessResources.InvalidStartProcess, win32ex.Message);
                ErrorRecord er = new ErrorRecord(new InvalidOperationException(message), "InvalidOperationException", ErrorCategory.InvalidOperation, null);
                ThrowTerminatingError(er);
            }
            SafeFileHandle sf = new SafeFileHandle(hFileHandle, true);
            return sf;
        }

        internal static class ProcessManager
        {
            // Properties
            public static bool IsNt
            {
                get
                {
#if CORECLR
                    return true;
#else
                    return (Environment.OSVersion.Platform == PlatformID.Win32NT);
#endif
                }
            }
        }

        internal static class EnvironmentBlock
        {
#if CORECLR
            public static byte[] ToByteArray(IDictionary<string, string> sd, bool unicode)
#else
            public static byte[] ToByteArray(StringDictionary sd, bool unicode)
#endif
            {
                string[] array = new string[sd.Count];
                byte[] bytes = null;
                sd.Keys.CopyTo(array, 0);
                string[] strArray2 = new string[sd.Count];
                sd.Values.CopyTo(strArray2, 0);
                Array.Sort(array, strArray2, StringComparer.OrdinalIgnoreCase);
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < sd.Count; i++)
                {
                    builder.Append(array[i]);//
                    builder.Append('=');
                    builder.Append(strArray2[i]);
                    builder.Append('\0');
                }
                builder.Append('\0');
                if (unicode)
                {
                    bytes = Encoding.Unicode.GetBytes(builder.ToString());
                }
                else
                {
                    bytes = ClrFacade.GetDefaultEncoding().GetBytes(builder.ToString());
                }
                if (bytes.Length > 0xffff)
                {
                    throw new InvalidOperationException("EnvironmentBlockTooLong");
                }
                return bytes;
            }
        }


        private static StringBuilder BuildCommandLine(string executableFileName, string arguments)
        {
            StringBuilder builder = new StringBuilder();
            string str = executableFileName.Trim();
            bool flag = str.StartsWith("\"", StringComparison.Ordinal) && str.EndsWith("\"", StringComparison.Ordinal);
            if (!flag)
            {
                builder.Append("\"");
            }
            builder.Append(str);
            if (!flag)
            {
                builder.Append("\"");
            }
            if (!string.IsNullOrEmpty(arguments))
            {
                builder.Append(" ");
                builder.Append(arguments);
            }
            return builder;
        }

        #endregion
    }

    /// <summary>
    /// ProcessCollection is a helper class used by Start-Process -Wait cmdlet to monitor the
    /// child processes created by the main process hosted by the Start-process cmdlet.
    /// </summary>
    internal class ProcessCollection
    {
        /// <summary>
        /// JobObjectHandle is a reference to the job object used to track 
        /// the child processes created by the main process hosted by the Start-Process cmdlet.
        /// </summary>
        private Microsoft.PowerShell.Commands.SafeJobHandle _jobObjectHandle;

        /// <summary>
        /// ProcessCollection constructor.
        /// </summary>
        internal ProcessCollection()
        {
            IntPtr jobObjectHandleIntPtr = NativeMethods.CreateJobObject(IntPtr.Zero, null);
            _jobObjectHandle = new SafeJobHandle(jobObjectHandleIntPtr);
        }

        /// <summary>
        /// Start API assigns the process to the JobObject and starts monitoring 
        /// the child processes hosted by the process created by Start-Process cmdlet.
        /// </summary>
        internal bool AssignProcessToJobObject(Process process)
        {
            // Add the process to the job object
            bool result = NativeMethods.AssignProcessToJobObject(_jobObjectHandle, ClrFacade.GetRawProcessHandle(process));
            return result;
        }

        /// <summary>
        /// Checks to see if the JobObject is empty (has no assigned processes).
        /// If job is empty the auto reset event supplied as input would be set.
        /// </summary>
        internal void CheckJobStatus(Object stateInfo)
        {
            ManualResetEvent emptyJobAutoEvent = (ManualResetEvent)stateInfo;
            int dwSize = 0;
            const int JOB_OBJECT_BASIC_PROCESS_ID_LIST = 3;
            JOBOBJECT_BASIC_PROCESS_ID_LIST JobList = new JOBOBJECT_BASIC_PROCESS_ID_LIST();

            dwSize = Marshal.SizeOf(JobList);
            if (NativeMethods.QueryInformationJobObject(_jobObjectHandle,
                JOB_OBJECT_BASIC_PROCESS_ID_LIST,
                ref JobList, dwSize, IntPtr.Zero))
            {
                if (JobList.NumberOfAssignedProcess == 0)
                {
                    emptyJobAutoEvent.Set();
                }
            }
        }

        /// <summary>
        /// WaitOne blocks the current thread until the current instance receives a signal, using
        /// a System.TimeSpan to measure the time interval and specifying whether to
        /// exit the synchronization domain before the wait.
        /// </summary>
        /// <param name="waitHandleToUse">
        /// WaitHandle to use for waiting on the job object.
        /// </param>
        internal void WaitOne(ManualResetEvent waitHandleToUse)
        {
            TimerCallback jobObjectStatusCb = this.CheckJobStatus;
            using (Timer stateTimer = new Timer(jobObjectStatusCb, waitHandleToUse, 0, 1000))
            {
                waitHandleToUse.WaitOne();
            }
        }
    }

    /// <summary>
    /// JOBOBJECT_BASIC_PROCESS_ID_LIST Contains the process identifier list for a job object. 
    /// If the job is nested, the process identifier list consists of all 
    /// processes associated with the job and its child jobs.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct JOBOBJECT_BASIC_PROCESS_ID_LIST
    {
        /// <summary>
        /// The number of process identifiers to be stored in ProcessIdList.
        /// </summary>
        public uint NumberOfAssignedProcess;

        /// <summary>
        /// The number of process identifiers returned in the ProcessIdList buffer. 
        /// If this number is less than NumberOfAssignedProcesses, increase 
        /// the size of the buffer to accommodate the complete list.
        /// </summary>
        public uint NumberOfProcessIdsInList;

        /// <summary>
        /// A variable-length array of process identifiers returned by this call. 
        /// Array elements 0 through NumberOfProcessIdsInList� 1 
        /// contain valid process identifiers.
        /// </summary>
        public IntPtr ProcessIdList;
    }

    internal static class ProcessNativeMethods
    {
        // Fields
        internal static readonly IntPtr INVALID_HANDLE_VALUE = IntPtr.Zero;
        internal static UInt32 GENERIC_READ = 0x80000000;
        internal static UInt32 GENERIC_WRITE = 0x40000000;
        internal static UInt32 FILE_ATTRIBUTE_NORMAL = 0x80000000;
        internal static UInt32 CREATE_ALWAYS = 2;
        internal static UInt32 FILE_SHARE_WRITE = 0x00000002;
        internal static UInt32 FILE_SHARE_READ = 0x00000001;
        internal static UInt32 OF_READWRITE = 0x00000002;
        internal static UInt32 OPEN_EXISTING = 3;


        // Methods
        //    static NativeMethods();


        [DllImport(PinvokeDllNames.GetStdHandleDllName, CharSet = CharSet.Ansi, SetLastError = true)]
        public static extern IntPtr GetStdHandle(int whichHandle);

        [DllImport(PinvokeDllNames.CreateProcessWithLogonWDllName, CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        internal static extern bool CreateProcessWithLogonW(string userName,
            string domain,
            IntPtr password,
            LogonFlags logonFlags,
            [MarshalAs(UnmanagedType.LPWStr)] string appName,
            StringBuilder cmdLine,
            int creationFlags,
            IntPtr environmentBlock,
            [MarshalAs(UnmanagedType.LPWStr)] string lpCurrentDirectory,
            STARTUPINFO lpStartupInfo,
            SafeNativeMethods.PROCESS_INFORMATION lpProcessInformation);

        [DllImport(PinvokeDllNames.CreateProcessDllName, CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CreateProcess([MarshalAs(UnmanagedType.LPWStr)] string lpApplicationName,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder lpCommandLine,
            SECURITY_ATTRIBUTES lpProcessAttributes,
            SECURITY_ATTRIBUTES lpThreadAttributes,
            bool bInheritHandles,
            int dwCreationFlags,
            IntPtr lpEnvironment,
            [MarshalAs(UnmanagedType.LPWStr)] string lpCurrentDirectory,
            STARTUPINFO lpStartupInfo,
            SafeNativeMethods.PROCESS_INFORMATION lpProcessInformation);

        [DllImport(PinvokeDllNames.ResumeThreadDllName, CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint ResumeThread(IntPtr threadHandle);

        [DllImport(PinvokeDllNames.CreateFileDllName, CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern FileNakedHandle CreateFileW(
            [In, MarshalAs(UnmanagedType.LPWStr)] string lpFileName,
            DWORD dwDesiredAccess,
            DWORD dwShareMode,
            ProcessNativeMethods.SECURITY_ATTRIBUTES lpSecurityAttributes,
            DWORD dwCreationDisposition,
            DWORD dwFlagsAndAttributes,
            System.IntPtr hTemplateFile
            );


        [Flags]
        internal enum LogonFlags
        {
            LOGON_NETCREDENTIALS_ONLY = 2,
            LOGON_WITH_PROFILE = 1
        }

        [StructLayout(LayoutKind.Sequential)]
        internal class SECURITY_ATTRIBUTES
        {
            public int nLength;
            public SafeLocalMemHandle lpSecurityDescriptor;
            public bool bInheritHandle;
            public SECURITY_ATTRIBUTES()
            {
                this.nLength = 12;
                this.bInheritHandle = true;
                this.lpSecurityDescriptor = new SafeLocalMemHandle(IntPtr.Zero, true);
            }
        }

        internal sealed class SafeLocalMemHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            // Methods
            internal SafeLocalMemHandle()
                : base(true)
            {
            }
            [SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
            internal SafeLocalMemHandle(IntPtr existingHandle, bool ownsHandle)
                : base(ownsHandle)
            {
                base.SetHandle(existingHandle);
            }
            [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success), DllImport(PinvokeDllNames.LocalFreeDllName)]
            private static extern IntPtr LocalFree(IntPtr hMem);
            protected override bool ReleaseHandle()
            {
                return (LocalFree(base.handle) == IntPtr.Zero);
            }
        }


        [StructLayout(LayoutKind.Sequential)]
        internal class STARTUPINFO
        {
            public int cb;
            public IntPtr lpReserved;
            public IntPtr lpDesktop;
            public IntPtr lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public SafeFileHandle hStdInput;
            public SafeFileHandle hStdOutput;
            public SafeFileHandle hStdError;
            public STARTUPINFO()
            {
                this.lpReserved = IntPtr.Zero;
                this.lpDesktop = IntPtr.Zero;
                this.lpTitle = IntPtr.Zero;
                this.lpReserved2 = IntPtr.Zero;
                this.hStdInput = new SafeFileHandle(IntPtr.Zero, false);
                this.hStdOutput = new SafeFileHandle(IntPtr.Zero, false);
                this.hStdError = new SafeFileHandle(IntPtr.Zero, false);
                this.cb = Marshal.SizeOf(this);
            }

            public void Dispose(bool disposing)
            {
                if (disposing)
                {
                    if ((this.hStdInput != null) && !this.hStdInput.IsInvalid)
                    {
                        this.hStdInput.Dispose();
                        this.hStdInput = null;
                    }
                    if ((this.hStdOutput != null) && !this.hStdOutput.IsInvalid)
                    {
                        this.hStdOutput.Dispose();
                        this.hStdOutput = null;
                    }
                    if ((this.hStdError != null) && !this.hStdError.IsInvalid)
                    {
                        this.hStdError.Dispose();
                        this.hStdError = null;
                    }
                }
            }

            public void Dispose()
            {
                Dispose(true);
            }
        }
    }

    internal static class SafeNativeMethods
    {
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success), DllImport(PinvokeDllNames.CloseHandleDllName, SetLastError = true, ExactSpelling = true)]
        public static extern bool CloseHandle(IntPtr handle);

        [StructLayout(LayoutKind.Sequential)]
        internal class PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;

            public PROCESS_INFORMATION()
            {
                this.hProcess = IntPtr.Zero;
                this.hThread = IntPtr.Zero;
            }

            /// <summary>
            /// Dispose
            /// </summary>
            public void Dispose()
            {
                Dispose(true);
            }

            /// <summary>
            /// Dispose
            /// </summary>
            /// <param name="disposing"></param>
            private void Dispose(bool disposing)
            {
                if (disposing)
                {
                    if (this.hProcess != IntPtr.Zero)
                    {
                        CloseHandle(this.hProcess);
                        this.hProcess = IntPtr.Zero;
                    }

                    if (this.hThread != IntPtr.Zero)
                    {
                        CloseHandle(this.hThread);
                        this.hThread = IntPtr.Zero;
                    }
                }
            }
        }
    }

    [SuppressUnmanagedCodeSecurity]
    internal sealed class SafeThreadHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        // Methods
        internal SafeThreadHandle()
            : base(true)
        {
        }
        protected override bool ReleaseHandle()
        {
            return SafeNativeMethods.CloseHandle(base.handle);
        }
    }

    [SuppressUnmanagedCodeSecurity, HostProtection(SecurityAction.LinkDemand, MayLeakOnAbort = true)]
    internal sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal SafeJobHandle(IntPtr jobHandle)
            : base(true)
        {
            base.SetHandle(jobHandle);
        }

        protected override bool ReleaseHandle()
        {
            return SafeNativeMethods.CloseHandle(base.handle);
        }
    }

    #endregion

    #region ProcessCommandException
    /// <summary>
    /// Non-terminating errors occurring in the process noun commands
    /// </summary>
    [Serializable]
    public class ProcessCommandException : SystemException
    {
        #region ctors
        /// <summary>
        /// unimplemented standard constructor
        /// </summary>
        /// <returns> doesn't return </returns>
        public ProcessCommandException() : base()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// standard constructor
        /// </summary>
        /// <param name="message"></param>
        /// <returns> constructed object </returns>
        public ProcessCommandException(string message) : base(message)
        {
        }

        /// <summary>
        /// standard constructor
        /// </summary>
        /// <param name="message"></param>
        /// <param name="innerException"></param>
        public ProcessCommandException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
        #endregion ctors

        #region Serialization
        /// <summary>
        /// serialization constructor
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        /// <returns> constructed object </returns>
        protected ProcessCommandException(
            SerializationInfo info,
            StreamingContext context)
            : base(info, context)
        {
            _processName = info.GetString("ProcessName");
        }
        /// <summary>
        /// Serializer
        /// </summary>
        /// <param name="info"> serialization information </param>
        /// <param name="context"> streaming context </param>
        [SecurityPermissionAttribute(
            SecurityAction.Demand,
            SerializationFormatter = true)]
        public override void GetObjectData(
            SerializationInfo info,
            StreamingContext context)
        {
            base.GetObjectData(info, context);

            if (info == null)
                throw new ArgumentNullException("info");

            info.AddValue("ProcessName", _processName);
        }
        #endregion Serialization

        #region Properties
        /// <summary>
        /// Name of the process which could not be found or operated upon
        /// </summary>
        /// <value></value>
        public string ProcessName
        {
            get { return _processName; }
            set { _processName = value; }
        }
        private string _processName = String.Empty;
        #endregion Properties
    } // class ProcessCommandException

    #endregion ProcessCommandException
}//Microsoft.PowerShell.Commands

