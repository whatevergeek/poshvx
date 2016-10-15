/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Remoting;
using System.Management.Automation.Runspaces;
using System.Management.Automation.Internal;
using Dbg = System.Management.Automation.Diagnostics;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Management.Automation.Language;

namespace Microsoft.PowerShell.Commands
{
    /// <summary>
    /// This class defines most of the common functionality used
    /// across remoting cmdlets. 
    /// 
    /// It contains tons of utility functions which are used all
    /// across the remoting cmdlets
    /// </summary>
    public abstract partial class PSRemotingCmdlet : PSCmdlet
    {
        #region Overrides

        /// <summary>
        /// Verifies if remoting cmdlets can be used
        /// </summary>
        protected override void BeginProcessing()
        {
            if (!SkipWinRMCheck)
            {
                RemotingCommandUtil.CheckRemotingCmdletPrerequisites();
            }
        }

        #endregion Overrides

        #region Utility functions

        /// <summary>
        /// Handle the object obtained from an ObjectStream's reader
        /// based on its type
        /// </summary>
        internal void WriteStreamObject(Action<Cmdlet> action)
        {
            action(this);
        }// WriteStreamObject

        /// <summary>
        /// Resolve all the machine names provided. Basically, if a machine 
        /// name is '.' assume localhost
        /// </summary>
        /// <param name="computerNames">array of computer names to resolve</param>
        /// <param name="resolvedComputerNames">resolved array of machine names</param>
        protected void ResolveComputerNames(string[] computerNames, out string[] resolvedComputerNames)
        {
            if (computerNames == null)
            {
                resolvedComputerNames = new string[1];

                resolvedComputerNames[0] = ResolveComputerName(".");
            }
            else if (computerNames.Length == 0)
            {
                resolvedComputerNames = Utils.EmptyArray<string>();
            }
            else
            {
                resolvedComputerNames = new string[computerNames.Length];

                for (int i = 0; i < resolvedComputerNames.Length; i++)
                {
                    resolvedComputerNames[i] = ResolveComputerName(computerNames[i]);
                }
            }
        }// ResolveComputerNames

        /// <summary>
        /// Resolves a computer name. If its null or empty
        /// its assumed to be localhost
        /// </summary>
        /// <param name="computerName">computer name to resolve</param>
        /// <returns>resolved computer name</returns>
        protected string ResolveComputerName(string computerName)
        {
            Diagnostics.Assert(computerName != null, "Null ComputerName");

            if (String.Equals(computerName, ".", StringComparison.OrdinalIgnoreCase))
            {
                //tracer.WriteEvent(ref PSEventDescriptors.PS_EVENT_HOSTNAMERESOLVE);
                //tracer.Dispose();
                //tracer.OperationalChannel.WriteVerbose(PSEventId.HostNameResolve, PSOpcode.Method, PSTask.CreateRunspace);
                return s_LOCALHOST;
            }
            else
            {
                return computerName;
            }
        }

        /// <summary>
        /// Load the resource corresponding to the specified errorId and 
        /// return the message as a string
        /// </summary>
        /// <param name="resourceString">resource String which holds the message
        /// </param>
        /// <returns>Error message loaded from appropriate resource cache</returns>
        internal String GetMessage(string resourceString)
        {
            String message = GetMessage(resourceString, null);

            return message;
        }// GetMessage

        /// <summary>
        /// 
        /// </summary>
        /// <param name="resourceString"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        internal String GetMessage(string resourceString, params object[] args)
        {
            String message;

            if (args != null)
            {
                message = StringUtil.Format(resourceString, args);
            }
            else
            {
                message = resourceString;
            }
            return message;
        }

        #endregion Utility functions

        #region Private Members

        private static string s_LOCALHOST = "localhost";

        //private PSETWTracer tracer = PSETWTracer.GetETWTracer(PSKeyword.Cmdlets);

        #endregion Private Members

        #region Protected Members

        /// <summary>
        /// Computername parameter set
        /// </summary>
        protected const string ComputerNameParameterSet = "ComputerName";

        /// <summary>
        /// Computername with session instance ID parameter set
        /// </summary>
        protected const string ComputerInstanceIdParameterSet = "ComputerInstanceId";

        /// <summary>
        /// Container ID parameter set
        /// </summary>
        protected const string ContainerIdParameterSet = "ContainerId";

        /// <summary>
        /// VM guid parameter set
        /// </summary>
        protected const string VMIdParameterSet = "VMId";

        /// <summary>
        /// VM name parameter set
        /// </summary>
        protected const string VMNameParameterSet = "VMName";

        /// <summary>
        /// SSH host parameter set
        /// </summary>
        protected const string SSHHostParameterSet = "SSHHost";

        /// <summary>
        /// runspace parameter set
        /// </summary>
        protected const string SessionParameterSet = "Session";

        /// <summary>
        /// Default shellname
        /// </summary>
        protected const string DefaultPowerShellRemoteShellName = System.Management.Automation.Remoting.Client.WSManNativeApi.ResourceURIPrefix + "Microsoft.PowerShell";

        /// <summary>
        /// default application name for the connection uri
        /// </summary>
        protected const string DefaultPowerShellRemoteShellAppName = "WSMan";

        #endregion Protected Members

        #region Internal Members

        /// <summary>
        /// Skip checking for WinRM
        /// </summary>
        internal bool SkipWinRMCheck { get; set; } = false;

        #endregion Internal Members

        #region Protected Methods

        /// <summary>
        /// Determines the shellname to use based on the following order:
        ///     1. ShellName parameter specified
        ///     2. DEFAULTREMOTESHELLNAME variable set 
        ///     3. PowerShell
        /// </summary>
        /// <returns>The shell to launch in the remote machine</returns>
        protected String ResolveShell(String shell)
        {
            String resolvedShell;

            if (!String.IsNullOrEmpty(shell))
            {
                resolvedShell = shell;
            }
            else
            {
                resolvedShell = (string)SessionState.Internal.ExecutionContext.GetVariableValue(
                    SpecialVariables.PSSessionConfigurationNameVarPath, DefaultPowerShellRemoteShellName);
            }

            return resolvedShell;
        }

        /// <summary>
        /// Determines the appname to be used based on the following order:
        ///     1. AppName parameter specified
        ///     2. DEFAULTREMOTEAPPNAME variable set
        ///     3. WSMan
        /// </summary>
        /// <param name="appName">application name to resolve</param>
        /// <returns>resolved appname</returns>
        protected String ResolveAppName(String appName)
        {
            String resolvedAppName;

            if (!String.IsNullOrEmpty(appName))
            {
                resolvedAppName = appName;
            }
            else
            {
                resolvedAppName = (string)SessionState.Internal.ExecutionContext.GetVariableValue(
                    SpecialVariables.PSSessionApplicationNameVarPath,
                    DefaultPowerShellRemoteShellAppName);
            }

            return resolvedAppName;
        }

        #endregion
    }

    /// <summary>
    /// Base class for any cmdlet which takes a -Session parameter
    /// or a -ComputerName parameter (along with its other associated
    /// parameters). The following cmdlets currently fall under this
    /// category:
    ///     1. New-PSSession
    ///     2. Invoke-Expression
    ///     3. Start-PSJob
    /// </summary>
    public abstract partial class PSRemotingBaseCmdlet : PSRemotingCmdlet
    {
        /// <summary>
        /// State of virtual machine. This is the same as VMState in 
        /// \vm\ux\powershell\objects\common\Types.cs
        /// </summary>
        internal enum VMState
        {
            /// <summary>
            /// Other. Corresponds to CIM_EnabledLogicalElement.EnabledState = Other.
            /// </summary>
            Other = 1,

            /// <summary>
            /// Running. Corresponds to CIM_EnabledLogicalElement.EnabledState = Enabled.
            /// </summary>
            Running = 2,

            /// <summary>
            /// Off. Corresponds to CIM_EnabledLogicalElement.EnabledState = Disabled.
            /// </summary>
            Off = 3,

            /// <summary>
            /// Stopping. Corresponds to CIM_EnabledLogicalElement.EnabledState = ShuttingDown.
            /// </summary>
            Stopping = 4,

            /// <summary>
            /// Saved. Corresponds to CIM_EnabledLogicalElement.EnabledState = Enabled but offline.
            /// </summary>
            Saved = 6,

            /// <summary>
            /// Paused. Corresponds to CIM_EnabledLogicalElement.EnabledState = Quiesce.
            /// </summary>
            Paused = 9,

            /// <summary>
            /// Starting. EnabledStateStarting. State transition from PowerOff or Saved to Running.
            /// </summary>
            Starting = 10,

            /// <summary>
            /// Reset. Corresponds to CIM_EnabledLogicalElement.EnabledState = Reset.
            /// </summary>
            Reset = 11,

            /// <summary>
            /// Saving. Corresponds to EnabledStateSaving.
            /// </summary>
            Saving = 32773,

            /// <summary>
            /// Pausing. Corresponds to EnabledStatePausing.
            /// </summary>
            Pausing = 32776,

            /// <summary>
            /// Resuming. Corresponds to EnabledStateResuming.
            /// </summary>
            Resuming = 32777,

            /// <summary>
            /// FastSaved. EnabledStateFastSuspend.
            /// </summary>
            FastSaved = 32779,

            /// <summary>
            /// FastSaving. EnabledStateFastSuspending.
            /// </summary>
            FastSaving = 32780,

            /// <summary>
            /// ForceShutdown. Used to force a graceful shutdown of the virtual machine.
            /// </summary>
            ForceShutdown = 32781,

            /// <summary>
            /// ForceReboot. Used to force a graceful reboot of the virtual machine.
            /// </summary>
            ForceReboot = 32782,

            /// <summary>
            /// RunningCritical. Critical states.
            /// </summary>
            RunningCritical,

            /// <summary>
            /// OffCritical. Critical states.
            /// </summary>
            OffCritical,

            /// <summary>
            /// StoppingCritical. Critical states.
            /// </summary>
            StoppingCritical,

            /// <summary>
            /// SavedCritical. Critical states.
            /// </summary>
            SavedCritical,

            /// <summary>
            /// PausedCritical. Critical states.
            /// </summary>
            PausedCritical,

            /// <summary>
            /// StartingCritical. Critical states.
            /// </summary>
            StartingCritical,

            /// <summary>
            /// ResetCritical. Critical states.
            /// </summary>
            ResetCritical,

            /// <summary>
            /// SavingCritical. Critical states.
            /// </summary>
            SavingCritical,

            /// <summary>
            /// PausingCritical. Critical states.
            /// </summary>
            PausingCritical,

            /// <summary>
            /// ResumingCritical. Critical states.
            /// </summary>
            ResumingCritical,

            /// <summary>
            /// FastSavedCritical. Critical states.
            /// </summary>
            FastSavedCritical,

            /// <summary>
            /// FastSavingCritical. Critical states.
            /// </summary>
            FastSavingCritical,
        }

        #region Tracer

        //PSETWTracer tracer = PSETWTracer.GetETWTracer(PSKeyword.Runspace);

        #endregion Tracer

        #region Properties

        /// <summary>
        /// The PSSession object describing the remote runspace
        /// using which the specified cmdlet operation will be performed
        /// </summary>
        [Parameter(Position = 0,
                   ValueFromPipelineByPropertyName = true,
                   ParameterSetName = PSRemotingBaseCmdlet.SessionParameterSet)]
        [ValidateNotNullOrEmpty]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public virtual PSSession[] Session { get; set; }

        /// <summary>
        /// This parameter represents the address(es) of the remote
        /// computer(s). The following formats are supported:
        ///      (a) Computer name 
        ///      (b) IPv4 address : 132.3.4.5
        ///      (c) IPv6 address: 3ffe:8311:ffff:f70f:0:5efe:172.30.162.18
        /// 
        /// </summary>
        [Parameter(Position = 0,
                   ValueFromPipelineByPropertyName = true,
                   ParameterSetName = PSRemotingBaseCmdlet.ComputerNameParameterSet)]
        [Alias("Cn")]
        public virtual String[] ComputerName { get; set; }

        /// <summary>
        /// Computer names after they have been resolved
        /// (null, empty string, "." resolves to localhost)
        /// </summary>
        /// <remarks>If Null or empty string is specified, then localhost is assumed.
        /// The ResolveComputerNames will include this.
        /// </remarks>
        protected String[] ResolvedComputerNames { get; set; }

        /// <summary>
        /// Guid of target virtual machine.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays",
            Justification = "This is by spec.")]
        [Parameter(Position = 0,
                   Mandatory = true,
                   ValueFromPipelineByPropertyName = true,
                   ParameterSetName = PSRemotingBaseCmdlet.VMIdParameterSet)]
        [ValidateNotNullOrEmpty]
        [Alias("VMGuid")]
        public virtual Guid[] VMId { get; set; }

        /// <summary>
        /// Name of target virtual machine.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays",
            Justification = "This is by spec.")]
        [Parameter(Mandatory = true,
                   ValueFromPipelineByPropertyName = true,
                   ParameterSetName = PSRemotingBaseCmdlet.VMNameParameterSet)]
        [ValidateNotNullOrEmpty]
        public virtual string[] VMName { get; set; }

        /// <summary>
        /// Specifies the credentials of the user to impersonate in the 
        /// remote machine. If this parameter is not specified then the 
        /// credentials of the current user process will be assumed.
        /// </summary>     
        [Parameter(ValueFromPipelineByPropertyName = true,
                   ParameterSetName = PSRemotingBaseCmdlet.ComputerNameParameterSet)]
        [Parameter(ValueFromPipelineByPropertyName = true,
                   ParameterSetName = PSRemotingBaseCmdlet.UriParameterSet)]
        [Parameter(ValueFromPipelineByPropertyName = true,
                   ParameterSetName = PSRemotingBaseCmdlet.VMIdParameterSet)]
        [Parameter(ValueFromPipelineByPropertyName = true,
                   ParameterSetName = PSRemotingBaseCmdlet.VMNameParameterSet)]
        [Credential()]
        public virtual PSCredential Credential
        {
            get
            {
                return _pscredential;
            }
            set
            {
                _pscredential = value;
                ValidateSpecifiedAuthentication(Credential, CertificateThumbprint, Authentication);
            }
        }

        private PSCredential _pscredential;

        /// <summary>
        /// ID of target container.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays",
            Justification = "This is by spec.")]
        [Parameter(Mandatory = true,
                   ValueFromPipelineByPropertyName = true,
                   ParameterSetName = PSRemotingBaseCmdlet.ContainerIdParameterSet)]
        [ValidateNotNullOrEmpty]
        public virtual string[] ContainerId { get; set; }

        /// <summary>
        /// When set, PowerShell process inside container will be launched with
        /// high privileged account.
        /// Otherwise (default case), PowerShell process inside container will be launched
        /// with low privileged account.
        /// </summary>
        [Parameter(ParameterSetName = PSRemotingBaseCmdlet.ContainerIdParameterSet)]
        public virtual SwitchParameter RunAsAdministrator { get; set; }

        /// <summary>
        /// Port specifies the alternate port to be used in case the 
        /// default ports are not used for the transport mechanism
        /// (port 80 for http and port 443 for useSSL)
        /// </summary>
        /// <remarks>
        /// Currently this is being accepted as a parameter. But in future
        /// support will be added to make this a part of a policy setting.
        /// When a policy setting is in place this parameter can be used
        /// to override the policy setting
        /// </remarks>
        [Parameter(ParameterSetName = PSRemotingBaseCmdlet.ComputerNameParameterSet)]
        [ValidateRange((Int32)1, (Int32)UInt16.MaxValue)]
        public virtual Int32 Port { get; set; }

        /// <summary>
        /// This parameter suggests that the transport scheme to be used for
        /// remote connections is useSSL instead of the default http.Since
        /// there are only two possible transport schemes that are possible
        /// at this point, a SwitchParameter is being used to switch between
        /// the two.
        /// </summary>
        [Parameter(ParameterSetName = PSRemotingBaseCmdlet.ComputerNameParameterSet)]
        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", MessageId = "SSL")]
        public virtual SwitchParameter UseSSL { get; set; }

        /// <summary>
        /// This parameters specifies the appname which identifies the connection
        /// end point on the remote machine. If this parameter is not specified
        /// then the value specified in DEFAULTREMOTEAPPNAME will be used. If thats
        /// not specified as well, then "WSMAN" will be used
        /// </summary>
        [Parameter(ValueFromPipelineByPropertyName = true,
                   ParameterSetName = PSRemotingBaseCmdlet.ComputerNameParameterSet)]
        public virtual String ApplicationName
        {
            get
            {
                return _appName;
            }
            set
            {
                _appName = ResolveAppName(value);
            }
        }

        private String _appName;

        /// <summary>
        /// Allows the user of the cmdlet to specify a throttling value
        /// for throttling the number of remote operations that can
        /// be executed simultaneously
        /// </summary>
        [Parameter(ParameterSetName = PSRemotingBaseCmdlet.ComputerNameParameterSet)]
        [Parameter(ParameterSetName = PSRemotingBaseCmdlet.SessionParameterSet)]
        [Parameter(ParameterSetName = PSRemotingBaseCmdlet.UriParameterSet)]
        [Parameter(ParameterSetName = PSRemotingBaseCmdlet.ContainerIdParameterSet)]
        [Parameter(ParameterSetName = PSRemotingBaseCmdlet.VMIdParameterSet)]
        [Parameter(ParameterSetName = PSRemotingBaseCmdlet.VMNameParameterSet)]
        public virtual Int32 ThrottleLimit { set; get; } = 0;

        /// <summary>
        /// A complete URI(s) specified for the remote computer and shell to 
        /// connect to and create runspace for
        /// </summary>
        [Parameter(Position = 0, Mandatory = true,
                   ValueFromPipelineByPropertyName = true,
                   ParameterSetName = PSRemotingBaseCmdlet.UriParameterSet)]
        [ValidateNotNullOrEmpty]
        [Alias("URI", "CU")]
        public virtual Uri[] ConnectionUri { get; set; }

        /// <summary>
        /// The AllowRedirection parameter enables the implicit redirection functionality
        /// </summary>
        [Parameter(ParameterSetName = PSRemotingBaseCmdlet.UriParameterSet)]
        public virtual SwitchParameter AllowRedirection
        {
            get { return _allowRedirection; }
            set { _allowRedirection = value; }
        }
        private bool _allowRedirection = false;

        /// <summary>
        /// Extended Session Options for controlling the session creation. Use 
        /// "New-WSManSessionOption" cmdlet to supply value for this parameter.
        /// </summary>
        [Parameter(ParameterSetName = PSRemotingBaseCmdlet.ComputerNameParameterSet)]
        [Parameter(ParameterSetName = PSRemotingBaseCmdlet.UriParameterSet)]
        [ValidateNotNull]
        public virtual PSSessionOption SessionOption
        {
            get
            {
                if (_sessionOption == null)
                {
                    object tmp = this.SessionState.PSVariable.GetValue(DEFAULT_SESSION_OPTION);
                    if (tmp == null || !LanguagePrimitives.TryConvertTo<PSSessionOption>(tmp, out _sessionOption))
                    {
                        _sessionOption = new PSSessionOption();
                    }
                }
                return _sessionOption;
            }

            set { _sessionOption = value; }
        }
        private PSSessionOption _sessionOption;
        internal const string DEFAULT_SESSION_OPTION = "PSSessionOption";

        // Quota related variables.
        /// <summary>
        /// Use basic authentication to authenticate the user.
        /// </summary>
        [Parameter(ParameterSetName = PSRemotingBaseCmdlet.ComputerNameParameterSet)]
        [Parameter(ParameterSetName = PSRemotingBaseCmdlet.UriParameterSet)]
        public virtual AuthenticationMechanism Authentication
        {
            get
            {
                return _authMechanism;
            }
            set
            {
                _authMechanism = value;
                // Validate if a user can specify this authentication.
                ValidateSpecifiedAuthentication(Credential, CertificateThumbprint, Authentication);
            }
        }
        private AuthenticationMechanism _authMechanism = AuthenticationMechanism.Default;

        /// <summary>
        /// Specifies the certificate thumbprint to be used to impersonate the user on the 
        /// remote machine. 
        /// </summary>
        [Parameter(ParameterSetName = NewPSSessionCommand.ComputerNameParameterSet)]
        [Parameter(ParameterSetName = NewPSSessionCommand.UriParameterSet)]
        public virtual string CertificateThumbprint
        {
            get { return _thumbPrint; }
            set
            {
                _thumbPrint = value;
                ValidateSpecifiedAuthentication(Credential, CertificateThumbprint, Authentication);
            }
        }
        private string _thumbPrint = null;

        #region SSHHostParameters

        /// <summary>
        /// SSH Target Host Name
        /// </summary>
        [Parameter(ParameterSetName = PSRemotingBaseCmdlet.SSHHostParameterSet)]
        [ValidateNotNullOrEmpty()]
        public virtual string HostName
        {
            get;
            set;
        }

        /// <summary>
        /// SSH User Name
        /// </summary>
        [Parameter(ParameterSetName = PSRemotingBaseCmdlet.SSHHostParameterSet)]
        [ValidateNotNullOrEmpty()]
        public virtual string UserName
        {
            get;
            set;
        }

        /// <summary>
        /// SSH Key Path
        /// </summary>
        [Parameter(ParameterSetName = PSRemotingBaseCmdlet.SSHHostParameterSet)]
        [ValidateNotNullOrEmpty()]
        public virtual string KeyPath
        {
            get;
            set;
        }

        #endregion

        #endregion Properties

        #region Internal Static Methods

        /// <summary>
        /// Used to resolve authentication from the parameters chosen by the user.
        /// User has the following options:
        /// 1. AuthMechanism + Credential
        /// 2. CertificateThumbPrint
        /// 
        /// All the above are mutually exclusive.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// If there is ambiguity as specified above.
        /// </exception>
        internal static void ValidateSpecifiedAuthentication(PSCredential credential, string thumbprint, AuthenticationMechanism authentication)
        {
            if ((credential != null) && (thumbprint != null))
            {
                String message = PSRemotingErrorInvariants.FormatResourceString(
                    RemotingErrorIdStrings.NewRunspaceAmbiguosAuthentication,
                        "CertificateThumbPrint", "Credential");

                throw new InvalidOperationException(message);
            }

            if ((authentication != AuthenticationMechanism.Default) && (thumbprint != null))
            {
                String message = PSRemotingErrorInvariants.FormatResourceString(
                    RemotingErrorIdStrings.NewRunspaceAmbiguosAuthentication,
                        "CertificateThumbPrint", authentication.ToString());

                throw new InvalidOperationException(message);
            }

            if ((authentication == AuthenticationMechanism.NegotiateWithImplicitCredential) &&
                (credential != null))
            {
                string message = PSRemotingErrorInvariants.FormatResourceString(
                    RemotingErrorIdStrings.NewRunspaceAmbiguosAuthentication,
                    "Credential", authentication.ToString());
                throw new InvalidOperationException(message);
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Validate the PSSession objects specified and write 
        /// appropriate error records. 
        /// </summary>
        /// <remarks>This function will lead in terminating errors when any of
        /// the validations fail</remarks>
        protected void ValidateRemoteRunspacesSpecified()
        {
            Dbg.Assert(Session != null && Session.Length != 0,
                    "Remote Runspaces specified must not be null or empty");

            // Check if there are duplicates in the specified PSSession objects
            if (RemotingCommandUtil.HasRepeatingRunspaces(Session))
            {
                ThrowTerminatingError(new ErrorRecord(new ArgumentException(
                    GetMessage(RemotingErrorIdStrings.RemoteRunspaceInfoHasDuplicates)),
                        PSRemotingErrorId.RemoteRunspaceInfoHasDuplicates.ToString(),
                            ErrorCategory.InvalidArgument, Session));
            }

            //BUGBUG: The following is a bogus check
            // Check if the number of PSSession objects specified is greater
            // than the maximum allowable range
            if (RemotingCommandUtil.ExceedMaximumAllowableRunspaces(Session))
            {
                ThrowTerminatingError(new ErrorRecord(new ArgumentException(
                    GetMessage(RemotingErrorIdStrings.RemoteRunspaceInfoLimitExceeded)),
                        PSRemotingErrorId.RemoteRunspaceInfoLimitExceeded.ToString(),
                            ErrorCategory.InvalidArgument, Session));
            }
        } // ValidateRemoteRunspacesSpecified

        /// <summary>
        /// Updates connection info with the data read from cmdlet's parameters and
        /// sessions variables.
        /// The following data is updated:
        /// 1. MaxURIRedirectionCount
        /// 2. MaxRecvdDataSizePerSession
        /// 3. MaxRecvdDataSizePerCommand
        /// 4. MaxRecvdObjectSize
        /// </summary>
        /// <param name="connectionInfo"></param>
        internal void UpdateConnectionInfo(WSManConnectionInfo connectionInfo)
        {
            Dbg.Assert(connectionInfo != null, "connectionInfo cannot be null.");

            connectionInfo.SetSessionOptions(this.SessionOption);

            if (!ParameterSetName.Equals(PSRemotingBaseCmdlet.UriParameterSet, StringComparison.OrdinalIgnoreCase))
            {
                // uri redirection is supported only with URI parameter set
                connectionInfo.MaximumConnectionRedirectionCount = 0;
            }

            if (!_allowRedirection)
            {
                // uri redirection required explicit user consent
                connectionInfo.MaximumConnectionRedirectionCount = 0;
            }
        }

        /// <summary>
        /// Uri parameter set
        /// </summary>
        protected const string UriParameterSet = "Uri";

        /// <summary>
        /// Validates computer names to check if none of them
        /// happen to be a Uri. If so this throws an error
        /// </summary>
        /// <param name="computerNames">collection of computer
        /// names to validate</param>
        protected void ValidateComputerName(String[] computerNames)
        {
            foreach (String computerName in computerNames)
            {
                UriHostNameType nametype = Uri.CheckHostName(computerName);
                if (!(nametype == UriHostNameType.Dns || nametype == UriHostNameType.IPv4 ||
                    nametype == UriHostNameType.IPv6))
                {
                    ThrowTerminatingError(new ErrorRecord(
                        new ArgumentException(PSRemotingErrorInvariants.FormatResourceString(
                            RemotingErrorIdStrings.InvalidComputerName)), "PSSessionInvalidComputerName",
                                ErrorCategory.InvalidArgument, computerNames));
                }
            }
        }

        #endregion Private Methods

        #region Overrides

        /// <summary>
        /// Resolves shellname and appname
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            // Validate IdleTimeout parameter.
            int idleTimeout = (int)SessionOption.IdleTimeout.TotalMilliseconds;
            if (idleTimeout != BaseTransportManager.UseServerDefaultIdleTimeout &&
                idleTimeout < BaseTransportManager.MinimumIdleTimeout)
            {
                throw new PSArgumentException(
                    StringUtil.Format(RemotingErrorIdStrings.InvalidIdleTimeoutOption,
                    idleTimeout / 1000, BaseTransportManager.MinimumIdleTimeout / 1000));
            }

            if (String.IsNullOrEmpty(_appName))
            {
                _appName = ResolveAppName(null);
            }
        }

        #endregion Overrides
    }

    /// <summary>
    /// Base class for any cmdlet which has to execute a pipeline. The 
    /// following cmdlets currently fall under this category:
    ///     1. Invoke-Expression
    ///     2. Start-PSJob
    /// </summary>
    public abstract partial class PSExecutionCmdlet : PSRemotingBaseCmdlet
    {
        #region Strings

        /// <summary>
        /// VM guid file path parameter set.
        /// </summary>
        protected const string FilePathVMIdParameterSet = "FilePathVMId";

        /// <summary>
        /// VM name file path parameter set.
        /// </summary>
        protected const string FilePathVMNameParameterSet = "FilePathVMName";

        /// <summary>
        /// Container ID file path parameter set.
        /// </summary>
        protected const string FilePathContainerIdParameterSet = "FilePathContainerId";

        /// <summary>
        /// SSH Host file path parameter set.
        /// </summary>
        protected const string FilePathSSHHostParameterSet = "FilePathSSHHost";

        #endregion

        #region Parameters

        /// <summary>
        /// Input object which gets assigned to $input when executed
        /// on the remote machine. This is the only parameter in 
        /// this cmdlet which will bind with a ValueFromPipeline=true
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public virtual PSObject InputObject { get; set; } = AutomationNull.Value;

        /// <summary>
        /// Command to execute specified as a string. This can be a single
        /// cmdlet, an expression or anything that can be internally 
        /// converted into a ScriptBlock
        /// </summary>
        public virtual ScriptBlock ScriptBlock
        {
            get
            {
                return _scriptBlock;
            }
            set
            {
                _scriptBlock = value;
            }
        }
        private ScriptBlock _scriptBlock;

        /// <summary>
        /// The file containing the script that the user has specified in the 
        /// cmdlet. This will be converted to a powershell before
        /// its actually sent to the remote end
        /// </summary>
        [Parameter(Position = 1,
                   Mandatory = true,
                   ParameterSetName = FilePathComputerNameParameterSet)]
        [Parameter(Position = 1,
                   Mandatory = true,
                   ParameterSetName = FilePathSessionParameterSet)]
        [Parameter(Position = 1,
                   Mandatory = true,
                   ParameterSetName = FilePathUriParameterSet)]
        [ValidateNotNull]
        public virtual string FilePath
        {
            get
            {
                return _filePath;
            }
            set
            {
                _filePath = value;
            }
        }
        private string _filePath;

        /// <summary>
        /// True if FilePath should be processed as a literal path
        /// </summary>
        protected bool IsLiteralPath { get; set; }

        /// <summary>
        /// Arguments that are passed to this scriptblock
        /// </summary>
        [Parameter()]
        [Alias("Args")]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public virtual Object[] ArgumentList
        {
            get
            {
                return _args;
            }
            set
            {
                _args = value;
            }
        }
        private Object[] _args;


        /// <summary>
        /// Indicates that if a job/command is invoked remotely the connection should be severed 
        /// right have invocation of job/command.
        /// </summary>
        ///
        protected bool InvokeAndDisconnect { get; set; } = false;

        /// <summary>
        /// Session names optionally provided for Disconnected parameter.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        protected string[] DisconnectedSessionName { get; set; }

        /// <summary>
        /// When set and in loopback scenario (localhost) this enables creation of WSMan
        /// host process with the user interactive token, allowing PowerShell script network access, 
        /// i.e., allows going off box.  When this property is true and a PSSession is disconnected, 
        /// reconnection is allowed only if reconnecting from a PowerShell session on the same box.
        /// </summary>
        public virtual SwitchParameter EnableNetworkAccess { get; set; }

        /// <summary>
        /// Guid of target virtual machine.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays",
            Justification = "This is by spec.")]
        [Parameter(Position = 0, Mandatory = true,
                   ValueFromPipelineByPropertyName = true,
                   ParameterSetName = InvokeCommandCommand.VMIdParameterSet)]
        [Parameter(Position = 0, Mandatory = true,
                   ValueFromPipelineByPropertyName = true,
                   ParameterSetName = InvokeCommandCommand.FilePathVMIdParameterSet)]
        [ValidateNotNullOrEmpty]
        [Alias("VMGuid")]
        public override Guid[] VMId { get; set; }

        /// <summary>
        /// Name of target virtual machine.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays",
            Justification = "This is by spec.")]
        [Parameter(Mandatory = true,
                   ValueFromPipelineByPropertyName = true,
                   ParameterSetName = InvokeCommandCommand.VMNameParameterSet)]
        [Parameter(Mandatory = true,
                   ValueFromPipelineByPropertyName = true,
                   ParameterSetName = InvokeCommandCommand.FilePathVMNameParameterSet)]
        [ValidateNotNullOrEmpty]
        public override string[] VMName { get; set; }

        /// <summary>
        /// ID of target container.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays",
            Justification = "This is by spec.")]
        [Parameter(Mandatory = true,
                   ValueFromPipelineByPropertyName = true,
                   ParameterSetName = InvokeCommandCommand.ContainerIdParameterSet)]
        [Parameter(Mandatory = true,
                   ValueFromPipelineByPropertyName = true,
                   ParameterSetName = InvokeCommandCommand.FilePathContainerIdParameterSet)]
        [ValidateNotNullOrEmpty]
        public override string[] ContainerId { get; set; }

        /// <summary>
        /// For WSMan session:
        /// If this parameter is not specified then the value specified in
        /// the environment variable DEFAULTREMOTESHELLNAME will be used. If 
        /// this is not set as well, then Microsoft.PowerShell is used.
        ///
        /// For VM/Container sessions:
        /// If this parameter is not specified then no configuration is used.
        /// </summary>      
        [Parameter(ValueFromPipelineByPropertyName = true,
                   ParameterSetName = InvokeCommandCommand.ComputerNameParameterSet)]
        [Parameter(ValueFromPipelineByPropertyName = true,
                   ParameterSetName = InvokeCommandCommand.UriParameterSet)]
        [Parameter(ValueFromPipelineByPropertyName = true,
                   ParameterSetName = InvokeCommandCommand.FilePathComputerNameParameterSet)]
        [Parameter(ValueFromPipelineByPropertyName = true,
                   ParameterSetName = InvokeCommandCommand.FilePathUriParameterSet)]
        [Parameter(ValueFromPipelineByPropertyName = true,
                   ParameterSetName = InvokeCommandCommand.ContainerIdParameterSet)]
        [Parameter(ValueFromPipelineByPropertyName = true,
                   ParameterSetName = InvokeCommandCommand.VMIdParameterSet)]
        [Parameter(ValueFromPipelineByPropertyName = true,
                   ParameterSetName = InvokeCommandCommand.VMNameParameterSet)]
        [Parameter(ValueFromPipelineByPropertyName = true,
                   ParameterSetName = InvokeCommandCommand.FilePathContainerIdParameterSet)]
        [Parameter(ValueFromPipelineByPropertyName = true,
                   ParameterSetName = InvokeCommandCommand.FilePathVMIdParameterSet)]
        [Parameter(ValueFromPipelineByPropertyName = true,
                   ParameterSetName = InvokeCommandCommand.FilePathVMNameParameterSet)]
        public virtual String ConfigurationName { get; set; }

        #endregion Parameters

        #region Private Methods

        /// <summary>
        /// Creates helper objects with the command for the specified
        /// remote computer names
        /// </summary>
        protected virtual void CreateHelpersForSpecifiedComputerNames()
        {
            ValidateComputerName(ResolvedComputerNames);

            // create helper objects for computer names
            RemoteRunspace remoteRunspace = null;
            string scheme = UseSSL.IsPresent ? WSManConnectionInfo.HttpsScheme : WSManConnectionInfo.HttpScheme;

            for (int i = 0; i < ResolvedComputerNames.Length; i++)
            {
                try
                {
                    WSManConnectionInfo connectionInfo = new WSManConnectionInfo();
                    connectionInfo.Scheme = scheme;
                    connectionInfo.ComputerName = ResolvedComputerNames[i];
                    connectionInfo.Port = Port;
                    connectionInfo.AppName = ApplicationName;
                    connectionInfo.ShellUri = ConfigurationName;
                    if (CertificateThumbprint != null)
                    {
                        connectionInfo.CertificateThumbprint = CertificateThumbprint;
                    }
                    else
                    {
                        connectionInfo.Credential = Credential;
                    }
                    connectionInfo.AuthenticationMechanism = Authentication;

                    UpdateConnectionInfo(connectionInfo);

                    connectionInfo.EnableNetworkAccess = EnableNetworkAccess;

                    // Use the provided session name or create one for this remote runspace so that 
                    // it can be easily identified if it becomes disconnected and is queried on the server.
                    int rsId = PSSession.GenerateRunspaceId();
                    string rsName = (DisconnectedSessionName != null && DisconnectedSessionName.Length > i) ?
                        DisconnectedSessionName[i] : PSSession.ComposeRunspaceName(rsId);

                    remoteRunspace = new RemoteRunspace(Utils.GetTypeTableFromExecutionContextTLS(), connectionInfo,
                        this.Host, this.SessionOption.ApplicationArguments, rsName, rsId);

                    remoteRunspace.Events.ReceivedEvents.PSEventReceived += OnRunspacePSEventReceived;
                }
                catch (UriFormatException uriException)
                {
                    ErrorRecord errorRecord = new ErrorRecord(uriException, "CreateRemoteRunspaceFailed",
                            ErrorCategory.InvalidArgument, ResolvedComputerNames[i]);

                    WriteError(errorRecord);

                    continue;
                }

                Pipeline pipeline = CreatePipeline(remoteRunspace);

                IThrottleOperation operation =
                    new ExecutionCmdletHelperComputerName(remoteRunspace, pipeline, InvokeAndDisconnect);

                Operations.Add(operation);
            }
        }// CreateHelpersForSpecifiedComputerNames

        /// <summary>
        /// Creates helper objects for host names for PSRP over SSH
        /// remoting.
        /// </summary>
        protected void CreateHelpersForSpecifiedHostNames()
        {
            var sshConnectionInfo = new SSHConnectionInfo(this.UserName, this.HostName, this.KeyPath);
            var typeTable = TypeTable.LoadDefaultTypeFiles();
            var remoteRunspace = RunspaceFactory.CreateRunspace(sshConnectionInfo, this.Host, typeTable) as RemoteRunspace;
            var pipeline = CreatePipeline(remoteRunspace);

            var operation = new ExecutionCmdletHelperComputerName(remoteRunspace, pipeline);
            Operations.Add(operation);
        }

        /// <summary>
        /// Creates helper objects with the specified command for 
        /// the specified remote runspaceinfo objects
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Runspaces")]
        protected void CreateHelpersForSpecifiedRunspaces()
        {
            RemoteRunspace[] remoteRunspaces;
            Pipeline[] pipelines;

            // extract RemoteRunspace out of the PSSession objects
            int length = Session.Length;
            remoteRunspaces = new RemoteRunspace[length];

            for (int i = 0; i < length; i++)
            {
                remoteRunspaces[i] = (RemoteRunspace)Session[i].Runspace;
            }

            // create the set of pipelines from the RemoteRunspace objects and 
            // create IREHelperRunspace helper class to create operations
            pipelines = new Pipeline[length];

            for (int i = 0; i < length; i++)
            {
                pipelines[i] = CreatePipeline(remoteRunspaces[i]);

                // create the operation object
                IThrottleOperation operation = new ExecutionCmdletHelperRunspace(pipelines[i]);
                Operations.Add(operation);
            }
        } // CreateHelpersForSpecifiedRunspaces

        /// <summary>
        /// Creates helper objects with the command for the specified
        /// remote connection uris
        /// </summary>
        protected void CreateHelpersForSpecifiedUris()
        {
            // create helper objects for computer names
            RemoteRunspace remoteRunspace = null;
            for (int i = 0; i < ConnectionUri.Length; i++)
            {
                try
                {
                    WSManConnectionInfo connectionInfo = new WSManConnectionInfo();

                    connectionInfo.ConnectionUri = ConnectionUri[i];
                    connectionInfo.ShellUri = ConfigurationName;

                    if (CertificateThumbprint != null)
                    {
                        connectionInfo.CertificateThumbprint = CertificateThumbprint;
                    }
                    else
                    {
                        connectionInfo.Credential = Credential;
                    }

                    connectionInfo.AuthenticationMechanism = Authentication;

                    UpdateConnectionInfo(connectionInfo);

                    connectionInfo.EnableNetworkAccess = EnableNetworkAccess;

                    remoteRunspace = (RemoteRunspace)RunspaceFactory.CreateRunspace(connectionInfo, this.Host,
                        Utils.GetTypeTableFromExecutionContextTLS(),
                        this.SessionOption.ApplicationArguments);

                    Dbg.Assert(remoteRunspace != null,
                            "RemoteRunspace object created using URI is null");

                    remoteRunspace.Events.ReceivedEvents.PSEventReceived += OnRunspacePSEventReceived;
                }
                catch (UriFormatException e)
                {
                    WriteErrorCreateRemoteRunspaceFailed(e, ConnectionUri[i]);
                    continue;
                }
                catch (InvalidOperationException e)
                {
                    WriteErrorCreateRemoteRunspaceFailed(e, ConnectionUri[i]);
                    continue;
                }
                catch (ArgumentException e)
                {
                    WriteErrorCreateRemoteRunspaceFailed(e, ConnectionUri[i]);
                    continue;
                }

                Pipeline pipeline = CreatePipeline(remoteRunspace);

                IThrottleOperation operation =
                    new ExecutionCmdletHelperComputerName(remoteRunspace, pipeline, InvokeAndDisconnect);

                Operations.Add(operation);
            }
        } // CreateHelpersForSpecifiedUris

        /// <summary>
        /// Creates helper objects with the command for the specified
        /// VM GUIDs or VM names.
        /// </summary>
        protected virtual void CreateHelpersForSpecifiedVMSession()
        {
            int inputArraySize;
            int index;
            string command;
            bool[] vmIsRunning;
            Collection<PSObject> results;

            if ((ParameterSetName == PSExecutionCmdlet.VMIdParameterSet) ||
                (ParameterSetName == PSExecutionCmdlet.FilePathVMIdParameterSet))
            {
                inputArraySize = this.VMId.Length;
                this.VMName = new string[inputArraySize];
                vmIsRunning = new bool[inputArraySize];

                for (index = 0; index < inputArraySize; index++)
                {
                    vmIsRunning[index] = false;

                    command = "Get-VM -Id $args[0]";

                    try
                    {
                        results = this.InvokeCommand.InvokeScript(
                            command, false, PipelineResultTypes.None, null, this.VMId[index]);
                    }
                    catch (CommandNotFoundException)
                    {
                        ThrowTerminatingError(
                            new ErrorRecord(
                                new ArgumentException(RemotingErrorIdStrings.HyperVModuleNotAvailable),
                                PSRemotingErrorId.HyperVModuleNotAvailable.ToString(),
                                ErrorCategory.NotInstalled,
                                null));

                        return;
                    }

                    if (results.Count != 1)
                    {
                        this.VMName[index] = string.Empty;
                    }
                    else
                    {
                        this.VMName[index] = (string)results[0].Properties["VMName"].Value;

                        if ((VMState)results[0].Properties["State"].Value == VMState.Running)
                        {
                            vmIsRunning[index] = true;
                        }
                    }
                }
            }
            else
            {
                Dbg.Assert((ParameterSetName == PSExecutionCmdlet.VMNameParameterSet) ||
                           (ParameterSetName == PSExecutionCmdlet.FilePathVMNameParameterSet),
                           "Expected ParameterSetName == VMName or FilePathVMName");

                inputArraySize = this.VMName.Length;
                this.VMId = new Guid[inputArraySize];
                vmIsRunning = new bool[inputArraySize];

                for (index = 0; index < inputArraySize; index++)
                {
                    vmIsRunning[index] = false;

                    command = "Get-VM -Name $args";

                    try
                    {
                        results = this.InvokeCommand.InvokeScript(
                            command, false, PipelineResultTypes.None, null, this.VMName[index]);
                    }
                    catch (CommandNotFoundException)
                    {
                        ThrowTerminatingError(
                            new ErrorRecord(
                                new ArgumentException(RemotingErrorIdStrings.HyperVModuleNotAvailable),
                                PSRemotingErrorId.HyperVModuleNotAvailable.ToString(),
                                ErrorCategory.NotInstalled,
                                null));

                        return;
                    }

                    if (results.Count != 1)
                    {
                        this.VMId[index] = Guid.Empty;
                    }
                    else
                    {
                        this.VMId[index] = (Guid)results[0].Properties["VMId"].Value;
                        this.VMName[index] = (string)results[0].Properties["VMName"].Value;

                        if ((VMState)results[0].Properties["State"].Value == VMState.Running)
                        {
                            vmIsRunning[index] = true;
                        }
                    }
                }
            }

            ResolvedComputerNames = this.VMName;

            for (index = 0; index < ResolvedComputerNames.Length; index++)
            {
                if ((this.VMId[index] == Guid.Empty) &&
                    ((ParameterSetName == PSExecutionCmdlet.VMNameParameterSet) ||
                     (ParameterSetName == PSExecutionCmdlet.FilePathVMNameParameterSet)))
                {
                    WriteError(
                        new ErrorRecord(
                            new ArgumentException(GetMessage(RemotingErrorIdStrings.InvalidVMNameNotSingle,
                                                             this.VMName[index])),
                            PSRemotingErrorId.InvalidVMNameNotSingle.ToString(),
                            ErrorCategory.InvalidArgument,
                            null));

                    continue;
                }
                else if ((this.VMName[index] == string.Empty) &&
                         ((ParameterSetName == PSExecutionCmdlet.VMIdParameterSet) ||
                          (ParameterSetName == PSExecutionCmdlet.FilePathVMIdParameterSet)))
                {
                    WriteError(
                        new ErrorRecord(
                            new ArgumentException(GetMessage(RemotingErrorIdStrings.InvalidVMIdNotSingle,
                                                             this.VMId[index].ToString(null))),
                            PSRemotingErrorId.InvalidVMIdNotSingle.ToString(),
                            ErrorCategory.InvalidArgument,
                            null));

                    continue;
                }
                else if (!vmIsRunning[index])
                {
                    WriteError(
                        new ErrorRecord(
                            new ArgumentException(GetMessage(RemotingErrorIdStrings.InvalidVMState,
                                                             this.VMName[index])),
                            PSRemotingErrorId.InvalidVMState.ToString(),
                            ErrorCategory.InvalidArgument,
                            null));

                    continue;
                }

                // create helper objects for VM GUIDs or names
                RemoteRunspace remoteRunspace = null;
                VMConnectionInfo connectionInfo;

                try
                {
                    connectionInfo = new VMConnectionInfo(this.Credential, this.VMId[index], this.VMName[index], this.ConfigurationName);

                    remoteRunspace = new RemoteRunspace(Utils.GetTypeTableFromExecutionContextTLS(),
                        connectionInfo, this.Host, null, null, -1);

                    remoteRunspace.Events.ReceivedEvents.PSEventReceived += OnRunspacePSEventReceived;
                }
                catch (InvalidOperationException e)
                {
                    ErrorRecord errorRecord = new ErrorRecord(e,
                        "CreateRemoteRunspaceForVMFailed",
                        ErrorCategory.InvalidOperation,
                        null);

                    WriteError(errorRecord);
                }
                catch (ArgumentException e)
                {
                    ErrorRecord errorRecord = new ErrorRecord(e,
                        "CreateRemoteRunspaceForVMFailed",
                        ErrorCategory.InvalidArgument,
                        null);

                    WriteError(errorRecord);
                }

                Pipeline pipeline = CreatePipeline(remoteRunspace);

                IThrottleOperation operation =
                    new ExecutionCmdletHelperComputerName(remoteRunspace, pipeline, false);

                Operations.Add(operation);
            }
        }// CreateHelpersForSpecifiedVMSession

        /// <summary>
        /// Creates helper objects with the command for the specified
        /// container IDs or names.
        /// </summary>
        protected virtual void CreateHelpersForSpecifiedContainerSession()
        {
            List<string> resolvedNameList = new List<string>();

            Dbg.Assert((ParameterSetName == PSExecutionCmdlet.ContainerIdParameterSet) ||
                       (ParameterSetName == PSExecutionCmdlet.FilePathContainerIdParameterSet),
                       "Expected ParameterSetName == ContainerId or FilePathContainerId");

            foreach (var input in ContainerId)
            {
                //
                // Create helper objects for container ID or name.
                //
                RemoteRunspace remoteRunspace = null;
                ContainerConnectionInfo connectionInfo = null;

                try
                {
                    //
                    // Hyper-V container uses Hype-V socket as transport.
                    // Windows Server container uses named pipe as transport.
                    //
                    connectionInfo = ContainerConnectionInfo.CreateContainerConnectionInfo(input, RunAsAdministrator.IsPresent, this.ConfigurationName);

                    resolvedNameList.Add(connectionInfo.ComputerName);

                    connectionInfo.CreateContainerProcess();

                    remoteRunspace = new RemoteRunspace(Utils.GetTypeTableFromExecutionContextTLS(),
                        connectionInfo, this.Host, null, null, -1);

                    remoteRunspace.Events.ReceivedEvents.PSEventReceived += OnRunspacePSEventReceived;
                }
                catch (InvalidOperationException e)
                {
                    ErrorRecord errorRecord = new ErrorRecord(e,
                        "CreateRemoteRunspaceForContainerFailed",
                        ErrorCategory.InvalidOperation,
                        null);

                    WriteError(errorRecord);
                    continue;
                }
                catch (ArgumentException e)
                {
                    ErrorRecord errorRecord = new ErrorRecord(e,
                        "CreateRemoteRunspaceForContainerFailed",
                        ErrorCategory.InvalidArgument,
                        null);

                    WriteError(errorRecord);
                    continue;
                }
                catch (Exception e)
                {
                    ErrorRecord errorRecord = new ErrorRecord(e,
                        "CreateRemoteRunspaceForContainerFailed",
                        ErrorCategory.InvalidOperation,
                        null);

                    WriteError(errorRecord);
                    continue;
                }

                Pipeline pipeline = CreatePipeline(remoteRunspace);

                IThrottleOperation operation =
                    new ExecutionCmdletHelperComputerName(remoteRunspace, pipeline, false);

                Operations.Add(operation);
            }

            ResolvedComputerNames = resolvedNameList.ToArray();
        }// CreateHelpersForSpecifiedContainerSession

        /// <summary>
        /// Creates a pipeline from the powershell
        /// </summary>
        /// <param name="remoteRunspace">runspace on which to create the pipeline</param>
        /// <returns>a pipeline</returns>
        internal Pipeline CreatePipeline(RemoteRunspace remoteRunspace)
        {
            // The fix to WinBlue#475223 changed how UsingExpression is handled on the client/server sides, if the remote end is PSv5 
            // or later, we send the dictionary-form using values to the remote end. If the remote end is PSv3 or PSv4, then we send 
            // the array-form using values if all UsingExpressions are in the same scope, otherwise, we handle the UsingExpression as
            // if the remote end is PSv2.
            string serverPsVersion = GetRemoteServerPsVersion(remoteRunspace);
            System.Management.Automation.PowerShell powershellToUse = (serverPsVersion == PSv2)
                                                                          ? GetPowerShellForPSv2()
                                                                          : GetPowerShellForPSv3OrLater(serverPsVersion);
            Pipeline pipeline =
                remoteRunspace.CreatePipeline(powershellToUse.Commands.Commands[0].CommandText, true);

            pipeline.Commands.Clear();

            foreach (Command command in powershellToUse.Commands.Commands)
            {
                pipeline.Commands.Add(command);
            }

            pipeline.RedirectShellErrorOutputPipe = true;

            return pipeline;
        }

        /// <summary>
        /// Check the powershell version of the remote server 
        /// </summary>
        private string GetRemoteServerPsVersion(RemoteRunspace remoteRunspace)
        {
            if (remoteRunspace.ConnectionInfo is NewProcessConnectionInfo)
            {
                // This is for Start-Job. The remote end is actually a child local powershell process, so it must be PSv5 or later
                return PSv5OrLater;
            }

            PSPrimitiveDictionary psApplicationPrivateData = remoteRunspace.GetApplicationPrivateData();
            if (psApplicationPrivateData == null)
            {
                // The remote runspace is not opened yet, or it's disconnected before the private data is retrieved. 
                // In this case we cannot validate if the remote server is running PSv5 or later, so for safety purpose, 
                // we will handle the $using expressions as if the remote server is PSv2.
                return PSv2;
            }

            // Unfortunately, the PSVersion value in the private data from PSv3 and PSv4 server is always 2.0.
            // This got fixed in PSv5, so a PSv5 server will return 5.0. That means we need other way to tell
            // if the remote server is PSv2 or PSv3+. After PSv3, remote runspace supports connect/disconnect,
            // so we can use it to differentiate PSv2 from PSv3+.
            if (remoteRunspace.CanDisconnect)
            {
                Version serverPsVersion = null;
                PSPrimitiveDictionary.TryPathGet(
                    psApplicationPrivateData,
                    out serverPsVersion,
                    PSVersionInfo.PSVersionTableName,
                    PSVersionInfo.PSVersionName);

                if (serverPsVersion != null)
                {
                    return serverPsVersion.Major >= 5 ? PSv5OrLater : PSv3Orv4;
                }

                // The private data is available but we failed to get the server powershell version.
                // This should never happen, but in case it happens, handle the $using expressions
                // as if the remote server is PSv2.
                Dbg.Assert(false, "Application private data is available but we failed to get the server powershell version. This should never happen.");
            }

            return PSv2;
        }

        /// <summary>
        /// Adds forwarded events to the local queue
        /// </summary>
        internal void OnRunspacePSEventReceived(object sender, PSEventArgs e)
        {
            if (this.Events != null)
                this.Events.AddForwardedEvent(e);
        }

        #endregion Private Methods

        #region Protected Members / Methods

        /// <summary>
        /// List of operations
        /// </summary>
        internal List<IThrottleOperation> Operations { get; } = new List<IThrottleOperation>();


        /// <summary>
        /// Closes the input streams on all the pipelines
        /// </summary>
        protected void CloseAllInputStreams()
        {
            foreach (IThrottleOperation operation in Operations)
            {
                ExecutionCmdletHelper helper = (ExecutionCmdletHelper)operation;
                helper.Pipeline.Input.Close();
            }
        } // CloseAllInputStreams

        /// <summary>
        /// Writes an error record specifying that creation of remote runspace
        /// failed
        /// </summary>
        /// <param name="e">exception which is causing this error record
        /// to be written</param>
        /// <param name="uri">Uri which caused this exception</param>
        private void WriteErrorCreateRemoteRunspaceFailed(Exception e, Uri uri)
        {
            Dbg.Assert(e is UriFormatException || e is InvalidOperationException ||
                       e is ArgumentException,
                       "Exception has to be of type UriFormatException or InvalidOperationException or ArgumentException");

            ErrorRecord errorRecord = new ErrorRecord(e, "CreateRemoteRunspaceFailed",
                ErrorCategory.InvalidArgument, uri);

            WriteError(errorRecord);
        } // WriteErrorCreateRemoteRunspaceFailed

        /// <summary>
        /// FilePathComputername parameter set
        /// </summary>
        protected const string FilePathComputerNameParameterSet = "FilePathComputerName";

        /// <summary>
        /// LiteralFilePathComputername parameter set
        /// </summary>
        protected const string LiteralFilePathComputerNameParameterSet = "LiteralFilePathComputerName";

        /// <summary>
        /// FilePathRunspace parameter set
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Runspace")]
        protected const string FilePathSessionParameterSet = "FilePathRunspace";

        /// <summary>
        /// FilePathUri parameter set
        /// </summary>
        protected const string FilePathUriParameterSet = "FilePathUri";

        /// <summary>
        /// PS version of the remote server
        /// </summary>
        private const string PSv5OrLater = "PSv5OrLater";
        private const string PSv3Orv4 = "PSv3Orv4";
        private const string PSv2 = "PSv2";

        private System.Management.Automation.PowerShell _powershellV2;
        private System.Management.Automation.PowerShell _powershellV3;

        /// <summary>
        /// Reads content of file and converts it to a scriptblock
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="isLiteralPath"></param>
        /// <returns></returns>
        protected ScriptBlock GetScriptBlockFromFile(string filePath, bool isLiteralPath)
        {
            //Make sure filepath doesn't contain wildcards
            if ((!isLiteralPath) && WildcardPattern.ContainsWildcardCharacters(filePath))
            {
                throw new ArgumentException(PSRemotingErrorInvariants.FormatResourceString(RemotingErrorIdStrings.WildCardErrorFilePathParameter), "filePath");
            }

            if (!filePath.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(PSRemotingErrorInvariants.FormatResourceString(RemotingErrorIdStrings.FilePathShouldPS1Extension), "filePath");
            }

            //Resolve file path
            PathResolver resolver = new PathResolver();
            string resolvedPath = resolver.ResolveProviderAndPath(filePath, isLiteralPath, this, false, RemotingErrorIdStrings.FilePathNotFromFileSystemProvider);

            //read content of file
            ExternalScriptInfo scriptInfo = new ExternalScriptInfo(filePath, resolvedPath, this.Context);

            // Skip ShouldRun check for .psd1 files.
            // Use ValidateScriptInfo() for explicitly validating the checkpolicy for psd1 file.
            //
            if (!filePath.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase))
            {
                this.Context.AuthorizationManager.ShouldRunInternal(scriptInfo, CommandOrigin.Internal, this.Context.EngineHostInterface);
            }

            return scriptInfo.ScriptBlock;
        }

        #endregion Protected Members / Methods

        #region Overrides

        /// <summary>
        /// Creates the helper classes for the specified
        /// parameter set
        /// </summary>
        protected override void BeginProcessing()
        {
            if ((ParameterSetName == PSExecutionCmdlet.VMIdParameterSet) ||
                (ParameterSetName == PSExecutionCmdlet.VMNameParameterSet) ||
                (ParameterSetName == PSExecutionCmdlet.ContainerIdParameterSet) ||
                (ParameterSetName == PSExecutionCmdlet.FilePathVMIdParameterSet) ||
                (ParameterSetName == PSExecutionCmdlet.FilePathVMNameParameterSet) ||
                (ParameterSetName == PSExecutionCmdlet.FilePathContainerIdParameterSet))
            {
                SkipWinRMCheck = true;
            }

            base.BeginProcessing();

            if (_filePath != null)
            {
                _scriptBlock = GetScriptBlockFromFile(_filePath, IsLiteralPath);
            }

            switch (ParameterSetName)
            {
                case PSExecutionCmdlet.FilePathComputerNameParameterSet:
                case PSExecutionCmdlet.LiteralFilePathComputerNameParameterSet:
                case PSExecutionCmdlet.ComputerNameParameterSet:
                    {
                        String[] resolvedComputerNames = null;
                        ResolveComputerNames(ComputerName, out resolvedComputerNames);
                        ResolvedComputerNames = resolvedComputerNames;

                        CreateHelpersForSpecifiedComputerNames();
                    }
                    break;

                case PSExecutionCmdlet.SSHHostParameterSet:
                case PSExecutionCmdlet.FilePathSSHHostParameterSet:
                    CreateHelpersForSpecifiedHostNames();
                    break;

                case PSExecutionCmdlet.FilePathSessionParameterSet:
                case PSExecutionCmdlet.SessionParameterSet:
                    {
                        ValidateRemoteRunspacesSpecified();

                        CreateHelpersForSpecifiedRunspaces();
                    }
                    break;

                case PSExecutionCmdlet.FilePathUriParameterSet:
                case PSExecutionCmdlet.UriParameterSet:
                    {
                        CreateHelpersForSpecifiedUris();
                    }
                    break;

                case PSExecutionCmdlet.VMIdParameterSet:
                case PSExecutionCmdlet.VMNameParameterSet:
                case PSExecutionCmdlet.FilePathVMIdParameterSet:
                case PSExecutionCmdlet.FilePathVMNameParameterSet:
                    {
                        CreateHelpersForSpecifiedVMSession();
                    }
                    break;

                case PSExecutionCmdlet.ContainerIdParameterSet:
                case PSExecutionCmdlet.FilePathContainerIdParameterSet:
                    {
                        CreateHelpersForSpecifiedContainerSession();
                    }
                    break;
            }
        }


        #endregion Overrides

        #region "Get PowerShell instance"

        /// <summary>
        /// Get the PowerShell instance for the PSv2 remote end
        /// Generate the PowerShell instance by using the text of the scriptblock
        /// </summary>
        /// <remarks>
        /// PSv2 doesn't understand the '$using' prefix. To make UsingExpression work on PSv2 remote end, we will have to
        /// alter the script, and send the altered script to the remote end. Since the script is altered, when there is an
        /// error, the error message will show the altered script, and that could be confusing to the user. So if the remote
        /// server is PSv3 or later version, we will use a different approach to handle UsingExpression so that we can keep
        /// the script unchanged.
        /// 
        /// However, on PSv3 and PSv4 remote server, it's not well supported if UsingExpressions are used in different scopes (fixed in PSv5).
        /// If the remote end is PSv3 or PSv4, and there are UsingExpressions in different scopes, then we have to revert back to the approach 
        /// used for PSv2 remote server.
        /// </remarks>
        /// <returns></returns>
        private System.Management.Automation.PowerShell GetPowerShellForPSv2()
        {
            if (_powershellV2 != null) { return _powershellV2; }

            // Try to convert the scriptblock to powershell commands.
            _powershellV2 = ConvertToPowerShell();
            if (_powershellV2 != null)
            {
                // Look for EndOfStatement tokens.
                foreach (var command in _powershellV2.Commands.Commands)
                {
                    if (command.IsEndOfStatement)
                    {
                        // PSv2 cannot process this.  Revert to sending script.
                        _powershellV2 = null;
                        break;
                    }
                }
                if (_powershellV2 != null) { return _powershellV2; }
            }

            List<string> newParameterNames;
            List<object> newParameterValues;

            string scriptTextAdaptedForPSv2 = GetConvertedScript(out newParameterNames, out newParameterValues);
            _powershellV2 = System.Management.Automation.PowerShell.Create().AddScript(scriptTextAdaptedForPSv2);

            if (_args != null)
            {
                foreach (object arg in _args)
                {
                    _powershellV2.AddArgument(arg);
                }
            }

            if (newParameterNames != null)
            {
                Dbg.Assert(newParameterValues != null && newParameterNames.Count == newParameterValues.Count, "We should get the value for each using variable");
                for (int i = 0; i < newParameterNames.Count; i++)
                {
                    _powershellV2.AddParameter(newParameterNames[i], newParameterValues[i]);
                }
            }

            return _powershellV2;
        }

        /// <summary>
        /// Get the PowerShell instance for the PSv3 (or later) remote end
        /// Generate the PowerShell instance by using the text of the scriptblock
        /// </summary>
        /// <remarks>
        /// In PSv3 and PSv4, if the remote server is PSv3 or later, we generate an object array that contains the value of each using expression in
        /// the parsing order, and then pass the array to the remote end as a special argument. On the remote end, the using expressions will be indexed 
        /// in the same parsing order during the variable analysis process, and the index is used to get the value of the corresponding using expression
        /// from the special array. There is a limitation in that approach -- $using cannot be used in different scopes with Invoke-Command/Start-Job 
        /// (see WinBlue#475223), because the variable analysis process can only index using expressions within the same scope (this is by design), and a 
        /// using expression from a different scope may be assigned with an index that conflicts with other using expressions.
        /// 
        /// To fix the limitation described above, we changed to pass a dictionary with key/value pairs for the using expressions on the client side. The key
        /// is an unique base64 encoded string generated based on the text of the using expression. On the remote end, it can always get the unique key of a 
        /// using expression because the text passed to the server side is the same, and thus the value of the using expression can be retrieved from the special 
        /// dictionary. With this approach, $using in different scopes can be supported for Invoke-Command/Start-Job.
        /// 
        /// This fix involved changes on the server side, so the fix will work only if the remote end is PSv5 or later. In order to avoid possible breaking
        /// change in 'PSv5 client - PSv3 server' and 'PSv5 client - PSv4 server' scenarios, we should keep sending the array-form using values if the remote
        /// end is PSv3 or PSv4 as long as no UsingExpression is in a different scope. If the remote end is PSv3 or PSv4 and we do have UsingExpressions
        /// in different scopes, then we will revert back to the approach we use to handle UsingExpression for PSv2 remote server.
        /// </remarks>
        /// <returns></returns>
        private System.Management.Automation.PowerShell GetPowerShellForPSv3OrLater(string serverPsVersion)
        {
            if (_powershellV3 != null) { return _powershellV3; }

            // Try to convert the scriptblock to powershell commands.
            _powershellV3 = ConvertToPowerShell();

            if (_powershellV3 != null) { return _powershellV3; }

            // Using expressions can be a variable, as well as property and / or array references. E.g.
            //
            // icm { echo $using:a }
            // icm { echo $using:a[3] }
            // icm { echo $using:a.Length }
            //
            // Semantic checks on the using statement have already validated that there are no arbitrary expressions,
            // so we'll allow these expressions in everything but NoLanguage mode.

            bool allowUsingExpressions = (Context.SessionState.LanguageMode != PSLanguageMode.NoLanguage);
            object[] usingValuesInArray = null;
            IDictionary usingValuesInDict = null;

            // Value of 'serverPsVersion' should be either 'PSv3Orv4' or 'PSv5OrLater'
            if (serverPsVersion == PSv3Orv4)
            {
                usingValuesInArray = ScriptBlockToPowerShellConverter.GetUsingValuesAsArray(_scriptBlock, allowUsingExpressions, Context, null);
                if (usingValuesInArray == null)
                {
                    // 'usingValuesInArray' will be null only if there are UsingExpressions used in different scopes. 
                    // PSv3 and PSv4 remote server cannot handle this, so we revert back to the approach we use for PSv2 remote end.
                    return GetPowerShellForPSv2();
                }
            }
            else
            {
                // Remote server is PSv5 or later version
                usingValuesInDict = ScriptBlockToPowerShellConverter.GetUsingValuesAsDictionary(_scriptBlock, allowUsingExpressions, Context, null);
            }

            string textOfScriptBlock = this.MyInvocation.ExpectingInput
                ? _scriptBlock.GetWithInputHandlingForInvokeCommand()
                : _scriptBlock.ToString();

            _powershellV3 = System.Management.Automation.PowerShell.Create().AddScript(textOfScriptBlock);

            if (_args != null)
            {
                foreach (object arg in _args)
                {
                    _powershellV3.AddArgument(arg);
                }
            }

            if (usingValuesInDict != null && usingValuesInDict.Count > 0)
            {
                _powershellV3.AddParameter(Parser.VERBATIM_ARGUMENT, usingValuesInDict);
            }
            else if (usingValuesInArray != null && usingValuesInArray.Length > 0)
            {
                _powershellV3.AddParameter(Parser.VERBATIM_ARGUMENT, usingValuesInArray);
            }

            return _powershellV3;
        }

        private System.Management.Automation.PowerShell ConvertToPowerShell()
        {
            System.Management.Automation.PowerShell powershell = null;

            try
            {
                // This is trusted input as long as we're in FullLanguage mode
                bool isTrustedInput = (Context.LanguageMode == PSLanguageMode.FullLanguage);
                powershell = _scriptBlock.GetPowerShell(isTrustedInput, _args);
            }
            catch (ScriptBlockToPowerShellNotSupportedException)
            {
                // conversion failed, we need to send the script to the remote end.
                // since the PowerShell instance would be different according to the PSVersion of the remote end,
                // we generate it when we know which version we are talking to.
            }

            return powershell;
        }

        #endregion "Get PowerShell instance"

        #region "UsingExpression Utilities"


        /// <summary>
        /// Get the converted script for a remote PSv2 end
        /// </summary>
        /// <param name="newParameterNames">
        /// The new parameter names that we added to the param block
        /// </param>
        /// <param name="newParameterValues">
        /// The new parameter values that need to be added to the powershell instance
        /// </param>
        /// <returns></returns>
        private string GetConvertedScript(out List<string> newParameterNames, out List<object> newParameterValues)
        {
            newParameterNames = null; newParameterValues = null;
            string textOfScriptBlock = null;

            // Scan for the using variables
            List<VariableExpressionAst> usingVariables = GetUsingVariables(_scriptBlock);

            if (usingVariables == null || usingVariables.Count == 0)
            {
                // No using variable is used, then we don't change the script
                textOfScriptBlock = this.MyInvocation.ExpectingInput
                    ? _scriptBlock.GetWithInputHandlingForInvokeCommand()
                    : _scriptBlock.ToString();
            }
            else
            {
                newParameterNames = new List<string>();
                var paramNamesWithDollarSign = new List<string>();
                var paramUsingVars = new List<VariableExpressionAst>();
                var nameHashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var varAst in usingVariables)
                {
                    string varName = varAst.VariablePath.UserPath;
                    string paramName = UsingExpressionAst.UsingPrefix + varName;
                    string paramNameWithDollar = "$" + paramName;

                    if (!nameHashSet.Contains(paramNameWithDollar))
                    {
                        newParameterNames.Add(paramName);
                        paramNamesWithDollarSign.Add(paramNameWithDollar);
                        paramUsingVars.Add(varAst);
                        nameHashSet.Add(paramNameWithDollar);
                    }
                }

                // Retrieve the value for each using variable
                newParameterValues = GetUsingVariableValues(paramUsingVars);

                // Generate the wrapped script
                string additionalNewParams = string.Join(", ", paramNamesWithDollarSign);
                textOfScriptBlock = this.MyInvocation.ExpectingInput
                    ? _scriptBlock.GetWithInputHandlingForInvokeCommandWithUsingExpression(Tuple.Create(usingVariables, additionalNewParams))
                    : _scriptBlock.ToStringWithDollarUsingHandling(Tuple.Create(usingVariables, additionalNewParams));
            }

            return textOfScriptBlock;
        }

        /// <summary>
        /// Get the values for the using variables that are passed in.
        /// </summary>
        /// <param name="paramUsingVars"></param>
        /// <returns></returns>
        private List<object> GetUsingVariableValues(List<VariableExpressionAst> paramUsingVars)
        {
            var values = new List<object>(paramUsingVars.Count);
            VariableExpressionAst currentVarAst = null;
            Version oldStrictVersion = Context.EngineSessionState.CurrentScope.StrictModeVersion;

            try
            {
                Context.EngineSessionState.CurrentScope.StrictModeVersion = PSVersionInfo.PSVersion;

                // GetExpressionValue ensures that it only does variable access when supplied a VariableExpressionAst.
                // So, this is still safe to use in ConstrainedLanguage and will not result in arbitrary code
                // execution.
                bool allowVariableAccess = (Context.SessionState.LanguageMode != PSLanguageMode.NoLanguage);

                foreach (var varAst in paramUsingVars)
                {
                    currentVarAst = varAst;
                    object value = Compiler.GetExpressionValue(varAst, allowVariableAccess, Context);
                    values.Add(value);
                }
            }
            catch (RuntimeException rte)
            {
                if (rte.ErrorRecord.FullyQualifiedErrorId.Equals("VariableIsUndefined", StringComparison.Ordinal))
                {
                    throw InterpreterError.NewInterpreterException(null, typeof(RuntimeException),
                        currentVarAst.Extent, "UsingVariableIsUndefined", AutomationExceptions.UsingVariableIsUndefined, rte.ErrorRecord.TargetObject);
                }
            }
            finally
            {
                Context.EngineSessionState.CurrentScope.StrictModeVersion = oldStrictVersion;
            }

            return values;
        }

        /// <summary>
        /// Get all Using expressions that we care about
        /// </summary>
        /// <param name="localScriptBlock"></param>
        /// <returns>a list of UsingExpressionAsts ordered by the StartOffset</returns>
        private List<VariableExpressionAst> GetUsingVariables(ScriptBlock localScriptBlock)
        {
            if (localScriptBlock == null)
            {
                throw new ArgumentNullException("localScriptBlock", "Caller needs to make sure the parameter value is not null");
            }
            var allUsingExprs = UsingExpressionAstSearcher.FindAllUsingExpressionExceptForWorkflow(localScriptBlock.Ast);
            return allUsingExprs.Select(usingExpr => UsingExpressionAst.ExtractUsingVariable((UsingExpressionAst)usingExpr)).ToList();
        }

        #endregion "UsingExpression Utilities"
    }

    /// <summary>
    /// Base class for any cmdlet which operates on a runspace. The 
    /// following cmdlets currently fall under this category:
    ///     1. Get-PSSession
    ///     2. Remove-PSSession
    ///     3. Disconnect-PSSession
    ///     4. Connect-PSSession
    /// </summary>
    public abstract partial class PSRunspaceCmdlet : PSRemotingCmdlet
    {
        #region Parameters

        /// <summary>
        /// ContainerIdInstanceId parameter set: container id + session instance id
        /// </summary>
        protected const string ContainerIdInstanceIdParameterSet = "ContainerIdInstanceId";

        /// <summary>
        /// VMIdInstanceId parameter set: vm id + session instance id
        /// </summary>
        protected const string VMIdInstanceIdParameterSet = "VMIdInstanceId";

        /// <summary>
        /// VMNameInstanceId parameter set: vm name + session instance id
        /// </summary>
        protected const string VMNameInstanceIdParameterSet = "VMNameInstanceId";

        /// <summary>
        /// RemoteRunspaceId to retrieve corresponding PSSession
        /// object
        /// </summary>
        [Parameter(Mandatory = true, ValueFromPipelineByPropertyName = true,
                   ParameterSetName = PSRunspaceCmdlet.InstanceIdParameterSet)]
        [ValidateNotNull]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public virtual Guid[] InstanceId
        {
            get
            {
                return _remoteRunspaceIds;
            }
            set
            {
                _remoteRunspaceIds = value;
            }
        }

        private Guid[] _remoteRunspaceIds;

        /// <summary>
        /// Session Id of the remoterunspace info object
        /// </summary>
        [Parameter(Position = 0,
                   ValueFromPipelineByPropertyName = true,
                   Mandatory = true,
                   ParameterSetName = PSRunspaceCmdlet.IdParameterSet)]
        [ValidateNotNull]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public int[] Id { get; set; }

        /// <summary>
        /// Name of the remote runspaceinfo object
        /// </summary>
        [Parameter(Mandatory = true, ValueFromPipelineByPropertyName = true,
                   ParameterSetName = PSRunspaceCmdlet.NameParameterSet)]
        [ValidateNotNullOrEmpty()]
        public virtual String[] Name
        {
            get
            {
                return _names;
            }
            set
            {
                _names = value;
            }
        }

        private String[] _names;

        /// <summary>
        /// Name of the computer for which the runspace needs to be
        /// returned
        /// </summary>
        [Parameter(Position = 0,
                   Mandatory = true,
                   ValueFromPipelineByPropertyName = true,
                   ParameterSetName = PSRunspaceCmdlet.ComputerNameParameterSet)]
        [ValidateNotNullOrEmpty]
        [Alias("Cn")]
        public virtual String[] ComputerName
        {
            get
            {
                return _computerNames;
            }
            set
            {
                _computerNames = value;
            }
        }

        private String[] _computerNames;

        /// <summary>
        /// ID of target container.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays",
            Justification = "This is by spec.")]
        [Parameter(Mandatory = true,
                   ValueFromPipelineByPropertyName = true,
                   ParameterSetName = PSRunspaceCmdlet.ContainerIdParameterSet)]
        [Parameter(Mandatory = true,
                   ValueFromPipelineByPropertyName = true,
                   ParameterSetName = PSRunspaceCmdlet.ContainerIdInstanceIdParameterSet)]
        [ValidateNotNullOrEmpty]
        public virtual string[] ContainerId { get; set; }

        /// <summary>
        /// Guid of target virtual machine.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays",
            Justification = "This is by spec.")]
        [Parameter(Mandatory = true,
                   ValueFromPipelineByPropertyName = true,
                   ParameterSetName = PSRunspaceCmdlet.VMIdParameterSet)]
        [Parameter(Mandatory = true,
                   ValueFromPipelineByPropertyName = true,
                   ParameterSetName = PSRunspaceCmdlet.VMIdInstanceIdParameterSet)]
        [ValidateNotNullOrEmpty]
        [Alias("VMGuid")]
        public virtual Guid[] VMId { get; set; }

        /// <summary>
        /// Name of target virtual machine.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays",
            Justification = "This is by spec.")]
        [Parameter(Mandatory = true,
                   ValueFromPipelineByPropertyName = true,
                   ParameterSetName = PSRunspaceCmdlet.VMNameParameterSet)]
        [Parameter(Mandatory = true,
                   ValueFromPipelineByPropertyName = true,
                   ParameterSetName = PSRunspaceCmdlet.VMNameInstanceIdParameterSet)]
        [ValidateNotNullOrEmpty]
        public virtual string[] VMName { get; set; }

        #endregion Parameters

        #region Private / Protected Methods

        /// <summary>
        /// Gets the matching runspaces based on the parameterset
        /// </summary>
        /// <param name="writeErrorOnNoMatch">write an error record when
        /// no matches are found</param>
        /// <param name="writeobject">if true write the object down
        /// the pipeline</param>
        /// <returns>list of matching runspaces</returns>
        [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Runspaces")]
        protected Dictionary<Guid, PSSession> GetMatchingRunspaces(bool writeobject,
            bool writeErrorOnNoMatch)
        {
            return GetMatchingRunspaces(writeobject, writeErrorOnNoMatch, SessionFilterState.All, null);
        }

        /// <summary>
        /// Gets the matching runspaces based on the parameterset
        /// </summary>
        /// <param name="writeErrorOnNoMatch">write an error record when
        /// no matches are found</param>
        /// <param name="writeobject">if true write the object down
        /// the pipeline</param>
        /// <param name="filterState">Runspace state filter value.</param>
        /// <param name="configurationName">Runspace configuration name filter value.</param>
        /// <returns>list of matching runspaces</returns>
        [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Runspaces")]
        protected Dictionary<Guid, PSSession> GetMatchingRunspaces(bool writeobject,
            bool writeErrorOnNoMatch,
            SessionFilterState filterState,
            String configurationName)
        {
            switch (ParameterSetName)
            {
                case PSRunspaceCmdlet.ComputerNameParameterSet:
                    {
                        return GetMatchingRunspacesByComputerName(writeobject, writeErrorOnNoMatch);
                    }

                case PSRunspaceCmdlet.InstanceIdParameterSet:
                    {
                        return GetMatchingRunspacesByRunspaceId(writeobject, writeErrorOnNoMatch);
                    }

                case PSRunspaceCmdlet.NameParameterSet:
                    {
                        return GetMatchingRunspacesByName(writeobject, writeErrorOnNoMatch);
                    }

                case PSRunspaceCmdlet.IdParameterSet:
                    {
                        return GetMatchingRunspacesBySessionId(writeobject, writeErrorOnNoMatch);
                    }

                //
                // writeErrorOnNoMatch should always be false for container/vm id/name inputs
                // in Get-PSSession/Remove-PSSession cmdlets
                //

                // container id + optional session name
                case PSRunspaceCmdlet.ContainerIdParameterSet:
                    {
                        return GetMatchingRunspacesByVMNameContainerId(writeobject, filterState, configurationName, true);
                    }

                // container id + session instanceid
                case PSRunspaceCmdlet.ContainerIdInstanceIdParameterSet:
                    {
                        return GetMatchingRunspacesByVMNameContainerIdSessionInstanceId(writeobject, filterState, configurationName, true);
                    }

                // vm Guid + optional session name
                case PSRunspaceCmdlet.VMIdParameterSet:
                    {
                        return GetMatchingRunspacesByVMId(writeobject, filterState, configurationName);
                    }

                // vm Guid + session instanceid
                case PSRunspaceCmdlet.VMIdInstanceIdParameterSet:
                    {
                        return GetMatchingRunspacesByVMIdSessionInstanceId(writeobject, filterState, configurationName);
                    }

                // vm name + optional session name
                case PSRunspaceCmdlet.VMNameParameterSet:
                    {
                        return GetMatchingRunspacesByVMNameContainerId(writeobject, filterState, configurationName, false);
                    }

                // vm name + session instanceid
                case PSRunspaceCmdlet.VMNameInstanceIdParameterSet:
                    {
                        return GetMatchingRunspacesByVMNameContainerIdSessionInstanceId(writeobject, filterState, configurationName, false);
                    }
            }

            return null;
        }

        internal Dictionary<Guid, PSSession> GetAllRunspaces(bool writeobject,
            bool writeErrorOnNoMatch)
        {
            Dictionary<Guid, PSSession> matches = new Dictionary<Guid, PSSession>();
            List<PSSession> remoteRunspaceInfos = this.RunspaceRepository.Runspaces;
            foreach (PSSession remoteRunspaceInfo in remoteRunspaceInfos)
            {
                // return all remote runspace info objects
                if (writeobject)
                {
                    WriteObject(remoteRunspaceInfo);
                }
                else
                {
                    matches.Add(remoteRunspaceInfo.InstanceId, remoteRunspaceInfo);
                }
            }

            return matches;
        }

        /// <summary>
        /// Gets the matching runspaces by computernames
        /// </summary>
        /// <param name="writeErrorOnNoMatch">write an error record when
        /// no matches are found</param>
        /// <param name="writeobject">if true write the object down
        /// the pipeline</param>
        /// <returns>list of matching runspaces</returns>
        private Dictionary<Guid, PSSession> GetMatchingRunspacesByComputerName(bool writeobject,
            bool writeErrorOnNoMatch)
        {
            if (_computerNames == null || _computerNames.Length == 0)
            {
                return GetAllRunspaces(writeobject, writeErrorOnNoMatch);
            }

            Dictionary<Guid, PSSession> matches = new Dictionary<Guid, PSSession>();

            List<PSSession> remoteRunspaceInfos = this.RunspaceRepository.Runspaces;

            // Loop through all computer-name patterns and runspaces to find matches.
            foreach (string computerName in _computerNames)
            {
                WildcardPattern computerNamePattern = WildcardPattern.Get(computerName, WildcardOptions.IgnoreCase);

                // Match the computer-name patterns against all the runspaces and remember the matches.
                bool found = false;
                foreach (PSSession remoteRunspaceInfo in remoteRunspaceInfos)
                {
                    if (computerNamePattern.IsMatch(remoteRunspaceInfo.ComputerName))
                    {
                        found = true;

                        if (writeobject)
                        {
                            WriteObject(remoteRunspaceInfo);
                        }
                        else
                        {
                            try
                            {
                                matches.Add(remoteRunspaceInfo.InstanceId, remoteRunspaceInfo);
                            }
                            catch (ArgumentException)
                            {
                                // if match already found ignore
                            }
                        }
                    }
                }

                // If no match found write an error record.
                if (!found && writeErrorOnNoMatch)
                {
                    WriteInvalidArgumentError(PSRemotingErrorId.RemoteRunspaceNotAvailableForSpecifiedComputer, RemotingErrorIdStrings.RemoteRunspaceNotAvailableForSpecifiedComputer,
                        computerName);
                }
            }

            return matches;
        }

        /// <summary>
        /// Gets the matching runspaces based on name
        /// </summary>
        /// <param name="writeErrorOnNoMatch">write an error record when
        /// no matches are found</param>
        /// <param name="writeobject">if true write the object down
        /// the pipeline</param>
        /// <returns>list of matching runspaces</returns>
        [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Runspaces")]
        [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "writeobject")]
        protected Dictionary<Guid, PSSession> GetMatchingRunspacesByName(bool writeobject,
            bool writeErrorOnNoMatch)
        {
            Dictionary<Guid, PSSession> matches = new Dictionary<Guid, PSSession>();

            List<PSSession> remoteRunspaceInfos = this.RunspaceRepository.Runspaces;

            // Loop through all computer-name patterns and runspaces to find matches.
            foreach (string name in _names)
            {
                WildcardPattern namePattern = WildcardPattern.Get(name, WildcardOptions.IgnoreCase);

                // Match the computer-name patterns against all the runspaces and remember the matches.
                bool found = false;
                foreach (PSSession remoteRunspaceInfo in remoteRunspaceInfos)
                {
                    if (namePattern.IsMatch(remoteRunspaceInfo.Name))
                    {
                        found = true;

                        if (writeobject)
                        {
                            WriteObject(remoteRunspaceInfo);
                        }
                        else
                        {
                            try
                            {
                                matches.Add(remoteRunspaceInfo.InstanceId, remoteRunspaceInfo);
                            }
                            catch (ArgumentException)
                            {
                                // if match already found ignore
                            }
                        }
                    }
                }

                // If no match found write an error record.
                if (!found && writeErrorOnNoMatch && !WildcardPattern.ContainsWildcardCharacters(name))
                {
                    WriteInvalidArgumentError(PSRemotingErrorId.RemoteRunspaceNotAvailableForSpecifiedName, RemotingErrorIdStrings.RemoteRunspaceNotAvailableForSpecifiedName,
                        name);
                }
            }

            return matches;
        }

        /// <summary>
        /// Gets the matching runspaces based on the runspaces instance id
        /// </summary>
        /// <param name="writeErrorOnNoMatch">write an error record when
        /// no matches are found</param>
        /// <param name="writeobject">if true write the object down
        /// the pipeline</param>
        /// <returns>list of matching runspaces</returns>
        [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Runspaces")]
        [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "writeobject")]
        protected Dictionary<Guid, PSSession> GetMatchingRunspacesByRunspaceId(bool writeobject,
            bool writeErrorOnNoMatch)
        {
            Dictionary<Guid, PSSession> matches = new Dictionary<Guid, PSSession>();

            List<PSSession> remoteRunspaceInfos = this.RunspaceRepository.Runspaces;

            // Loop through all computer-name patterns and runspaces to find matches.
            foreach (Guid remoteRunspaceId in _remoteRunspaceIds)
            {
                // Match the computer-name patterns against all the runspaces and remember the matches.
                bool found = false;
                foreach (PSSession remoteRunspaceInfo in remoteRunspaceInfos)
                {
                    if (remoteRunspaceId.Equals(remoteRunspaceInfo.InstanceId))
                    {
                        found = true;

                        if (writeobject)
                        {
                            WriteObject(remoteRunspaceInfo);
                        }
                        else
                        {
                            try
                            {
                                matches.Add(remoteRunspaceInfo.InstanceId, remoteRunspaceInfo);
                            }
                            catch (ArgumentException)
                            {
                                // if match already found ignore
                            }
                        }
                    }
                }

                // If no match found write an error record.
                if (!found && writeErrorOnNoMatch)
                {
                    WriteInvalidArgumentError(PSRemotingErrorId.RemoteRunspaceNotAvailableForSpecifiedRunspaceId, RemotingErrorIdStrings.RemoteRunspaceNotAvailableForSpecifiedRunspaceId,
                        remoteRunspaceId);
                }
            }

            return matches;
        }

        /// <summary>
        /// Gets the matching runspaces based on the session id (the
        /// short integer id which is unique for a runspace)
        /// </summary>
        /// <param name="writeErrorOnNoMatch">write an error record when
        /// no matches are found</param>
        /// <param name="writeobject">if true write the object down
        /// the pipeline</param>
        /// <returns>list of matching runspaces</returns>
        private Dictionary<Guid, PSSession> GetMatchingRunspacesBySessionId(bool writeobject,
            bool writeErrorOnNoMatch)
        {
            Dictionary<Guid, PSSession> matches = new Dictionary<Guid, PSSession>();

            List<PSSession> remoteRunspaceInfos = this.RunspaceRepository.Runspaces;

            // Loop through all computer-name patterns and runspaces to find matches.
            foreach (int sessionId in Id)
            {
                // Match the computer-name patterns against all the runspaces and remember the matches.
                bool found = false;
                foreach (PSSession remoteRunspaceInfo in remoteRunspaceInfos)
                {
                    if (sessionId == remoteRunspaceInfo.Id)
                    {
                        found = true;

                        if (writeobject)
                        {
                            WriteObject(remoteRunspaceInfo);
                        }
                        else
                        {
                            try
                            {
                                matches.Add(remoteRunspaceInfo.InstanceId, remoteRunspaceInfo);
                            }
                            catch (ArgumentException)
                            {
                                // if match already found ignore
                            }
                        }
                    }
                }

                // If no match found write an error record.
                if (!found && writeErrorOnNoMatch)
                {
                    WriteInvalidArgumentError(PSRemotingErrorId.RemoteRunspaceNotAvailableForSpecifiedSessionId, RemotingErrorIdStrings.RemoteRunspaceNotAvailableForSpecifiedSessionId,
                        sessionId);
                }
            }

            return matches;
        }

        /// <summary>
        /// Gets the matching runspaces by vm name or container id with optional session name
        /// </summary>
        /// <param name="writeobject">if true write the object down the pipeline</param>
        /// <param name="filterState">Runspace state filter value.</param>
        /// <param name="configurationName">Runspace configuration name filter value.</param>
        /// <param name="isContainer">if true the target is a container instead of virtual machine</param>
        /// <returns>list of matching runspaces</returns>
        private Dictionary<Guid, PSSession> GetMatchingRunspacesByVMNameContainerId(bool writeobject,
            SessionFilterState filterState,
            String configurationName,
            bool isContainer)
        {
            String[] inputNames;
            TargetMachineType computerType;
            bool supportWildChar;
            String[] sessionNames = { "*" };
            WildcardPattern configurationNamePattern =
                String.IsNullOrEmpty(configurationName) ? null : WildcardPattern.Get(configurationName, WildcardOptions.IgnoreCase);
            Dictionary<Guid, PSSession> matches = new Dictionary<Guid, PSSession>();
            List<PSSession> remoteRunspaceInfos = this.RunspaceRepository.Runspaces;

            // vm name support wild characters, while container id does not. 
            // vm id does not apply in this method, which does not support wild characters either.
            if (isContainer)
            {
                inputNames = ContainerId;
                computerType = TargetMachineType.Container;
                supportWildChar = false;
            }
            else
            {
                inputNames = VMName;
                computerType = TargetMachineType.VirtualMachine;
                supportWildChar = true;
            }

            // When "-name" is not set, we use "*" that means matching all.
            if (Name != null)
            {
                sessionNames = Name;
            }

            foreach (string inputName in inputNames)
            {
                WildcardPattern inputNamePattern = WildcardPattern.Get(inputName, WildcardOptions.IgnoreCase);

                foreach (string sessionName in sessionNames)
                {
                    WildcardPattern sessionNamePattern =
                        String.IsNullOrEmpty(sessionName) ? null : WildcardPattern.Get(sessionName, WildcardOptions.IgnoreCase);

                    var matchingRunspaceInfos = remoteRunspaceInfos
                        .Where<PSSession>(session => (supportWildChar ? inputNamePattern.IsMatch(session.VMName)
                                                                      : inputName.Equals(session.ContainerId)) &&
                                                     ((sessionNamePattern == null) ? true : sessionNamePattern.IsMatch(session.Name)) &&
                                                     QueryRunspaces.TestRunspaceState(session.Runspace, filterState) &&
                                                     ((configurationNamePattern == null) ? true : configurationNamePattern.IsMatch(session.ConfigurationName)) &&
                                                     (session.ComputerType == computerType))
                        .ToList<PSSession>();

                    WriteOrAddMatches(matchingRunspaceInfos, writeobject, ref matches);
                }
            }

            return matches;
        }

        /// <summary>
        /// Gets the matching runspaces by vm name or container id with session instanceid
        /// </summary>
        /// <param name="writeobject">if true write the object down the pipeline</param>
        /// <param name="filterState">Runspace state filter value.</param>
        /// <param name="configurationName">Runspace configuration name filter value.</param>
        /// <param name="isContainer">if true the target is a container instead of virtual machine</param>
        /// <returns>list of matching runspaces</returns>
        private Dictionary<Guid, PSSession> GetMatchingRunspacesByVMNameContainerIdSessionInstanceId(bool writeobject,
            SessionFilterState filterState,
            String configurationName,
            bool isContainer)
        {
            String[] inputNames;
            TargetMachineType computerType;
            bool supportWildChar;
            WildcardPattern configurationNamePattern =
                String.IsNullOrEmpty(configurationName) ? null : WildcardPattern.Get(configurationName, WildcardOptions.IgnoreCase);
            Dictionary<Guid, PSSession> matches = new Dictionary<Guid, PSSession>();
            List<PSSession> remoteRunspaceInfos = this.RunspaceRepository.Runspaces;

            // vm name support wild characters, while container id does not. 
            // vm id does not apply in this method, which does not support wild characters either.
            if (isContainer)
            {
                inputNames = ContainerId;
                computerType = TargetMachineType.Container;
                supportWildChar = false;
            }
            else
            {
                inputNames = VMName;
                computerType = TargetMachineType.VirtualMachine;
                supportWildChar = true;
            }

            foreach (string inputName in inputNames)
            {
                WildcardPattern inputNamePattern = WildcardPattern.Get(inputName, WildcardOptions.IgnoreCase);

                foreach (Guid sessionInstanceId in InstanceId)
                {
                    var matchingRunspaceInfos = remoteRunspaceInfos
                        .Where<PSSession>(session => (supportWildChar ? inputNamePattern.IsMatch(session.VMName)
                                                                      : inputName.Equals(session.ContainerId)) &&
                                                     sessionInstanceId.Equals(session.InstanceId) &&
                                                     QueryRunspaces.TestRunspaceState(session.Runspace, filterState) &&
                                                     ((configurationNamePattern == null) ? true : configurationNamePattern.IsMatch(session.ConfigurationName)) &&
                                                     (session.ComputerType == computerType))
                        .ToList<PSSession>();

                    WriteOrAddMatches(matchingRunspaceInfos, writeobject, ref matches);
                }
            }

            return matches;
        }

        /// <summary>
        /// Gets the matching runspaces by vm guid and optional session name
        /// </summary>
        /// <param name="writeobject">if true write the object down the pipeline</param>
        /// <param name="filterState">Runspace state filter value.</param>
        /// <param name="configurationName">Runspace configuration name filter value.</param>
        /// <returns>list of matching runspaces</returns>
        private Dictionary<Guid, PSSession> GetMatchingRunspacesByVMId(bool writeobject,
            SessionFilterState filterState,
            String configurationName)
        {
            String[] sessionNames = { "*" };
            WildcardPattern configurationNamePattern =
                String.IsNullOrEmpty(configurationName) ? null : WildcardPattern.Get(configurationName, WildcardOptions.IgnoreCase);
            Dictionary<Guid, PSSession> matches = new Dictionary<Guid, PSSession>();
            List<PSSession> remoteRunspaceInfos = this.RunspaceRepository.Runspaces;

            // When "-name" is not set, we use "*" that means matching all .
            if (Name != null)
            {
                sessionNames = Name;
            }

            foreach (Guid vmId in VMId)
            {
                foreach (string sessionName in sessionNames)
                {
                    WildcardPattern sessionNamePattern =
                        String.IsNullOrEmpty(sessionName) ? null : WildcardPattern.Get(sessionName, WildcardOptions.IgnoreCase);

                    var matchingRunspaceInfos = remoteRunspaceInfos
                        .Where<PSSession>(session => vmId.Equals(session.VMId) &&
                                                     ((sessionNamePattern == null) ? true : sessionNamePattern.IsMatch(session.Name)) &&
                                                     QueryRunspaces.TestRunspaceState(session.Runspace, filterState) &&
                                                     ((configurationNamePattern == null) ? true : configurationNamePattern.IsMatch(session.ConfigurationName)) &&
                                                     (session.ComputerType == TargetMachineType.VirtualMachine))
                        .ToList<PSSession>();

                    WriteOrAddMatches(matchingRunspaceInfos, writeobject, ref matches);
                }
            }

            return matches;
        }

        /// <summary>
        /// Gets the matching runspaces by vm guid and session instanceid
        /// </summary>
        /// <param name="writeobject">if true write the object down the pipeline</param>
        /// <param name="filterState">Runspace state filter value.</param>
        /// <param name="configurationName">Runspace configuration name filter value.</param>
        /// <returns>list of matching runspaces</returns>
        private Dictionary<Guid, PSSession> GetMatchingRunspacesByVMIdSessionInstanceId(bool writeobject,
            SessionFilterState filterState,
            String configurationName)
        {
            WildcardPattern configurationNamePattern =
                String.IsNullOrEmpty(configurationName) ? null : WildcardPattern.Get(configurationName, WildcardOptions.IgnoreCase);
            Dictionary<Guid, PSSession> matches = new Dictionary<Guid, PSSession>();
            List<PSSession> remoteRunspaceInfos = this.RunspaceRepository.Runspaces;

            foreach (Guid vmId in VMId)
            {
                foreach (Guid sessionInstanceId in InstanceId)
                {
                    var matchingRunspaceInfos = remoteRunspaceInfos
                        .Where<PSSession>(session => vmId.Equals(session.VMId) &&
                                                     sessionInstanceId.Equals(session.InstanceId) &&
                                                     QueryRunspaces.TestRunspaceState(session.Runspace, filterState) &&
                                                     ((configurationNamePattern == null) ? true : configurationNamePattern.IsMatch(session.ConfigurationName)) &&
                                                     (session.ComputerType == TargetMachineType.VirtualMachine))
                        .ToList<PSSession>();

                    WriteOrAddMatches(matchingRunspaceInfos, writeobject, ref matches);
                }
            }

            return matches;
        }

        /// <summary>
        /// Write the matching runspace objects down the pipeline, or add to the list.
        /// </summary>
        /// <param name="matchingRunspaceInfos">The matching runspaces</param>
        /// <param name="writeobject">if true write the object down the pipeline. Otherwise, add to the list</param>
        /// <param name="matches">The list we add the matching runspaces to</param>        
        private void WriteOrAddMatches(List<PSSession> matchingRunspaceInfos,
            bool writeobject,
            ref Dictionary<Guid, PSSession> matches)
        {
            foreach (PSSession remoteRunspaceInfo in matchingRunspaceInfos)
            {
                if (writeobject)
                {
                    WriteObject(remoteRunspaceInfo);
                }
                else
                {
                    try
                    {
                        matches.Add(remoteRunspaceInfo.InstanceId, remoteRunspaceInfo);
                    }
                    catch (ArgumentException)
                    {
                        // if match already found ignore
                    }
                }
            }
        }

        /// <summary>
        /// Write invalid argument error.
        /// </summary>
        private void WriteInvalidArgumentError(PSRemotingErrorId errorId, string resourceString, object errorArgument)
        {
            String message = GetMessage(resourceString, errorArgument);

            WriteError(new ErrorRecord(new ArgumentException(message), errorId.ToString(),
                ErrorCategory.InvalidArgument, errorArgument));
        }

        #endregion Private / Protected Methods

        #region Protected Members

        /// <summary>
        /// Runspace Id parameter set
        /// </summary>
        protected const string InstanceIdParameterSet = "InstanceId";

        /// <summary>
        /// session id parameter set
        /// </summary>
        protected const string IdParameterSet = "Id";

        /// <summary>
        /// name parameter set
        /// </summary>
        protected const string NameParameterSet = "Name";

        #endregion Protected Members
    }

    #region Helper Classes

    /// <summary>
    /// Base class for both the helpers. This is an abstract class
    /// and the helpers need to derive from this
    /// </summary>
    internal abstract partial class ExecutionCmdletHelper : IThrottleOperation
    {
        /// <summary>
        /// Pipeline associated with this operation
        /// </summary>
        internal Pipeline Pipeline
        {
            get
            {
                return pipeline;
            }
        }
        protected Pipeline pipeline;

        /// <summary>
        /// Exception raised internally when any method of this class
        /// is executed
        /// </summary>
        internal Exception InternalException
        {
            get
            {
                return internalException;
            }
        }
        protected Exception internalException;

        /// <summary>
        /// Internal access to Runspace and Computer helper runspace.
        /// </summary>
        internal Runspace PipelineRunspace
        {
            set;
            get;
        }
    } // ExecutionCmdletHelper

    /// <summary>
    /// Contains a pipeline and calls InvokeAsync on the pipeline
    /// on StartOperation. On StopOperation it calls StopAsync.
    /// The handler sends a StopComplete message in OperationComplete
    /// for both the functions. This is because, there is only a
    /// single state of the pipeline which raises an event on
    /// a method call. There are no separate events raised as 
    /// part of method calls
    /// </summary>
    internal class ExecutionCmdletHelperRunspace : ExecutionCmdletHelper
    {
        /// <summary>
        /// Indicates whether or not the server should be using the steppable pipeline
        /// </summary>
        internal bool ShouldUseSteppablePipelineOnServer;

        /// <summary>
        /// Internal constructor
        /// </summary>
        /// <param name="pipeline">pipeline object associated with this operation</param>
        internal ExecutionCmdletHelperRunspace(Pipeline pipeline)
        {
            this.pipeline = pipeline;
            PipelineRunspace = pipeline.Runspace;
            this.pipeline.StateChanged += new EventHandler<PipelineStateEventArgs>(HandlePipelineStateChanged);
        }

        /// <summary>
        /// Invokes the pipeline asynchronously
        /// </summary>
        internal override void StartOperation()
        {
            try
            {
                if (ShouldUseSteppablePipelineOnServer)
                {
                    RemotePipeline rPipeline = pipeline as RemotePipeline;
                    rPipeline.SetIsNested(true);
                    rPipeline.SetIsSteppable(true);
                }
                pipeline.InvokeAsync();
            }
            catch (InvalidRunspaceStateException e)
            {
                internalException = e;
                RaiseOperationCompleteEvent();
            }
            catch (InvalidPipelineStateException e)
            {
                internalException = e;
                RaiseOperationCompleteEvent();
            }
            catch (InvalidOperationException e)
            {
                internalException = e;
                RaiseOperationCompleteEvent();
            }
        } // StartOperation

        /// <summary>
        /// Closes the pipeline asynchronously
        /// </summary>
        internal override void StopOperation()
        {
            if (pipeline.PipelineStateInfo.State == PipelineState.Running ||
                pipeline.PipelineStateInfo.State == PipelineState.Disconnected ||
                pipeline.PipelineStateInfo.State == PipelineState.NotStarted)
            {
                // If the pipeline state has reached Complete/Failed/Stopped
                // by the time control reaches here, then this operation
                // becomes a no-op. However, an OperationComplete would have
                // already been raised from the handler 
                pipeline.StopAsync();
            }
            else
            {
                // will have to raise OperationComplete from here,
                // else ThrottleManager will have
                RaiseOperationCompleteEvent();
            }
        } // StopOperation

        internal override event EventHandler<OperationStateEventArgs> OperationComplete;

        /// <summary>
        /// Handles the state changed events for the pipeline. This is registered in both
        /// StartOperation and StopOperation. Here nothing more is done excepting raising
        /// the OperationComplete event appropriately which will be handled by the cmdlet
        /// </summary>
        /// <param name="sender">source of this event</param>
        /// <param name="stateEventArgs">object describing state information about the
        /// pipeline</param>
        private void HandlePipelineStateChanged(object sender, PipelineStateEventArgs stateEventArgs)
        {
            PipelineStateInfo stateInfo = stateEventArgs.PipelineStateInfo;

            switch (stateInfo.State)
            {
                case PipelineState.Running:
                case PipelineState.NotStarted:
                case PipelineState.Stopping:
                case PipelineState.Disconnected:
                    return;
            }

            RaiseOperationCompleteEvent(stateEventArgs);
        } // HandlePipelineStateChanged

        /// <summary>
        /// Raise an OperationComplete Event. The base event will be
        /// null in this case
        /// </summary>
        private void RaiseOperationCompleteEvent()
        {
            RaiseOperationCompleteEvent(null);
        } // RaiseOperationCompleteEvent

        /// <summary>
        /// Raise an operation complete event.
        /// </summary>
        /// <param name="baseEventArgs">The event args which actually
        /// raises this operation complete</param>
        private void RaiseOperationCompleteEvent(EventArgs baseEventArgs)
        {
            if (pipeline != null)
            {
                // Dispose the pipeline object and release data and remoting resources.
                // Pipeline object remains to provide information on final state and any errors incurred.
                pipeline.StateChanged -= new EventHandler<PipelineStateEventArgs>(HandlePipelineStateChanged);
                pipeline.Dispose();
            }

            OperationStateEventArgs operationStateEventArgs =
                    new OperationStateEventArgs();
            operationStateEventArgs.OperationState =
                    OperationState.StopComplete;
            operationStateEventArgs.BaseEvent = baseEventArgs;

            if (OperationComplete != null)
            {
                OperationComplete.SafeInvoke(this, operationStateEventArgs);
            }
        } // RaiseOperationCompleteEvent
    } // ExecutionCmdletHelperRunspace

    /// <summary>
    /// This helper class contains a runspace and 
    /// an associated pipeline. On StartOperation it calls
    /// OpenAsync on the runspace. In the handler for runspace,
    /// when the runspace is successfully opened it calls 
    /// InvokeAsync on the pipeline. StartOperation
    /// is assumed complete when both the operations complete.
    /// StopOperation will call StopAsync first on the pipeline
    /// and then close the associated runspace. StopOperation
    /// is considered complete when both these operations
    /// complete. The handler sends a StopComplete message in
    /// OperationComplete for both the calls.
    /// </summary>
    internal class ExecutionCmdletHelperComputerName : ExecutionCmdletHelper
    {
        /// <summary>
        /// Determines if the command should be invoked and then disconnect the
        /// remote runspace from the client.
        /// </summary>
        private bool _invokeAndDisconnect;

        /// <summary>
        /// The remote runspace created using the computer name
        /// parameter set details
        /// </summary>
        internal RemoteRunspace RemoteRunspace { get; private set; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="remoteRunspace">RemoteRunspace that is associated
        /// with this operation</param>
        /// <param name="pipeline">pipeline created from the remote runspace</param>
        /// <param name="invokeAndDisconnect">Indicates if pipeline should be disconnected after invoking command.</param>
        internal ExecutionCmdletHelperComputerName(RemoteRunspace remoteRunspace, Pipeline pipeline, bool invokeAndDisconnect = false)
        {
            Dbg.Assert(remoteRunspace != null,
                    "RemoteRunspace reference cannot be null");
            PipelineRunspace = remoteRunspace;

            _invokeAndDisconnect = invokeAndDisconnect;

            RemoteRunspace = remoteRunspace;
            remoteRunspace.StateChanged +=
                new EventHandler<RunspaceStateEventArgs>(HandleRunspaceStateChanged);

            Dbg.Assert(pipeline != null,
                    "Pipeline cannot be null or empty");

            this.pipeline = pipeline;
            pipeline.StateChanged +=
                new EventHandler<PipelineStateEventArgs>(HandlePipelineStateChanged);
        } // IREHelperComputerName

        /// <summary>
        /// Call OpenAsync() on the RemoteRunspace
        /// </summary>
        internal override void StartOperation()
        {
            try
            {
                RemoteRunspace.OpenAsync();
            }
            catch (PSRemotingTransportException e)
            {
                internalException = e;
                RaiseOperationCompleteEvent();
            }
        } // StartOperation

        /// <summary>
        /// StopAsync on the pipeline
        /// </summary>
        internal override void StopOperation()
        {
            bool needToStop = false; // indicates whether to call StopAsync 

            if (pipeline.PipelineStateInfo.State == PipelineState.Running ||
                pipeline.PipelineStateInfo.State == PipelineState.NotStarted)
            {
                needToStop = true;
            }

            if (needToStop)
            {
                // If the pipeline state has reached Complete/Failed/Stopped
                // by the time control reaches here, then this operation
                // becomes a no-op. However, an OperationComplete would have
                // already been raised from the handler 
                pipeline.StopAsync();
            }
            else
            {
                // raise an OperationComplete event here. Else the
                // throttle manager will hang as it will be waiting for
                // this StopOperation to complete
                RaiseOperationCompleteEvent();
            }
        } // StopOperation

        internal override event EventHandler<OperationStateEventArgs> OperationComplete;

        /// <summary>
        /// Handles the state changed event for runspace operations
        /// </summary>
        /// <param name="sender">sender of this information</param>
        /// <param name="stateEventArgs">object describing this event</param>
        private void HandleRunspaceStateChanged(object sender,
                RunspaceStateEventArgs stateEventArgs)
        {
            RunspaceState state = stateEventArgs.RunspaceStateInfo.State;

            switch (state)
            {
                case RunspaceState.BeforeOpen:
                case RunspaceState.Opening:
                case RunspaceState.Closing:
                    return;

                case RunspaceState.Opened:
                    {
                        // if successfully opened
                        // Call InvokeAsync() on the pipeline
                        try
                        {
                            if (_invokeAndDisconnect)
                            {
                                pipeline.InvokeAsyncAndDisconnect();
                            }
                            else
                            {
                                pipeline.InvokeAsync();
                            }
                        }
                        catch (InvalidPipelineStateException)
                        {
                            RemoteRunspace.CloseAsync();
                        }
                        catch (InvalidRunspaceStateException e)
                        {
                            internalException = e;
                            RemoteRunspace.CloseAsync();
                        }
                    }
                    break;

                case RunspaceState.Broken:
                    {
                        RaiseOperationCompleteEvent(stateEventArgs);
                    }
                    break;
                case RunspaceState.Closed:
                    {
                        // raise a OperationComplete event with
                        // StopComplete message 
                        if (stateEventArgs.RunspaceStateInfo.Reason != null)
                        {
                            RaiseOperationCompleteEvent(stateEventArgs);
                        }
                        else
                        {
                            RaiseOperationCompleteEvent();
                        }
                    }
                    break;
            } // switch (state...
        } // HandleRunspaceStateChanged

        /// <summary>
        /// Handles the state changed event for the pipeline.
        /// </summary>
        /// <param name="sender">sender of this information</param>
        /// <param name="stateEventArgs">object describing this event</param>
        private void HandlePipelineStateChanged(object sender,
                        PipelineStateEventArgs stateEventArgs)
        {
            PipelineState state = stateEventArgs.PipelineStateInfo.State;

            switch (state)
            {
                case PipelineState.Running:
                case PipelineState.NotStarted:
                case PipelineState.Stopping:
                    return;

                case PipelineState.Completed:
                case PipelineState.Stopped:
                case PipelineState.Failed:
                    if (RemoteRunspace != null)
                    {
                        RemoteRunspace.CloseAsync();
                    }
                    break;
            } // switch(state...
        } // HandlePipelineStateChanged

        /// <summary>
        /// Raise an OperationComplete Event. The base event will be
        /// null in this case
        /// </summary>
        private void RaiseOperationCompleteEvent()
        {
            RaiseOperationCompleteEvent(null);
        } // RaiseOperationCompleteEvent

        /// <summary>
        /// Raise an operation complete event.
        /// </summary>
        /// <param name="baseEventArgs">The event args which actually
        /// raises this operation complete</param>
        private void RaiseOperationCompleteEvent(EventArgs baseEventArgs)
        {
            if (pipeline != null)
            {
                // Dispose the pipeline object and release data and remoting resources.
                // Pipeline object remains to provide information on final state and any errors incurred.
                pipeline.StateChanged -= new EventHandler<PipelineStateEventArgs>(HandlePipelineStateChanged);
                pipeline.Dispose();
            }

            if (RemoteRunspace != null)
            {
                // Dispose of the runspace object.
                RemoteRunspace.Dispose();
                RemoteRunspace = null;
            }

            OperationStateEventArgs operationStateEventArgs =
                    new OperationStateEventArgs();
            operationStateEventArgs.OperationState =
                    OperationState.StopComplete;
            operationStateEventArgs.BaseEvent = baseEventArgs;
            OperationComplete.SafeInvoke(this, operationStateEventArgs);
        } // RaiseOperationCompleteEvent
    } // ExecutionCmdletHelperComputerName

    /// <summary>
    /// A helper class to resolve the path
    /// </summary>
    internal class PathResolver
    {
        /// <summary>
        /// Resolves the specified path and verifies the path belongs to
        /// FileSystemProvider.
        /// </summary>
        /// <param name="path">Path to resolve</param>
        /// <param name="isLiteralPath">True if wildcard expansion should be suppressed for this path.</param>
        /// <param name="cmdlet">reference to calling cmdlet. This will be used for
        /// for writing errors</param>
        /// <param name="allowNonexistingPaths"></param>
        /// <param name="resourceString">resource string for error when path is not from filesystem provider</param>
        /// <returns>A fully qualified string representing filename.</returns>
        internal string ResolveProviderAndPath(string path, bool isLiteralPath, PSCmdlet cmdlet, bool allowNonexistingPaths, string resourceString)
        {
            // First resolve path
            PathInfo resolvedPath = ResolvePath(path, isLiteralPath, allowNonexistingPaths, cmdlet);

            if (resolvedPath.Provider.ImplementingType == typeof(FileSystemProvider))
            {
                return resolvedPath.ProviderPath;
            }

            throw PSTraceSource.NewInvalidOperationException(resourceString, resolvedPath.Provider.Name);
        }

        /// <summary>
        /// Resolves the specified path to PathInfo objects
        /// </summary>
        /// 
        /// <param name="pathToResolve">
        /// The path to be resolved. Each path may contain glob characters.
        /// </param>
        /// 
        /// <param name="isLiteralPath">
        /// True if wildcard expansion should be suppressed for pathToResolve.
        /// </param>
        /// 
        /// <param name="allowNonexistingPaths">
        /// If true, resolves the path even if it doesn't exist.
        /// </param>
        /// 
        /// <param name="cmdlet">
        /// Calling cmdlet
        /// </param>
        /// 
        /// <returns>
        /// A string representing the resolved path.
        /// </returns>
        /// 
        private PathInfo ResolvePath(
            string pathToResolve,
            bool isLiteralPath,
            bool allowNonexistingPaths,
            PSCmdlet cmdlet)
        {
            // Construct cmdletprovidercontext
            CmdletProviderContext cmdContext = new CmdletProviderContext(cmdlet);
            cmdContext.SuppressWildcardExpansion = isLiteralPath;

            Collection<PathInfo> results = new Collection<PathInfo>();

            try
            {
                // First resolve path
                Collection<PathInfo> pathInfos =
                    cmdlet.SessionState.Path.GetResolvedPSPathFromPSPath(
                        pathToResolve,
                        cmdContext);

                foreach (PathInfo pathInfo in pathInfos)
                {
                    results.Add(pathInfo);
                }
            }
            catch (PSNotSupportedException notSupported)
            {
                cmdlet.ThrowTerminatingError(
                    new ErrorRecord(
                        notSupported.ErrorRecord,
                        notSupported));
            }
            catch (System.Management.Automation.DriveNotFoundException driveNotFound)
            {
                cmdlet.ThrowTerminatingError(
                    new ErrorRecord(
                        driveNotFound.ErrorRecord,
                        driveNotFound));
            }
            catch (ProviderNotFoundException providerNotFound)
            {
                cmdlet.ThrowTerminatingError(
                    new ErrorRecord(
                        providerNotFound.ErrorRecord,
                        providerNotFound));
            }
            catch (ItemNotFoundException pathNotFound)
            {
                if (allowNonexistingPaths)
                {
                    ProviderInfo provider = null;
                    System.Management.Automation.PSDriveInfo drive = null;
                    string unresolvedPath =
                        cmdlet.SessionState.Path.GetUnresolvedProviderPathFromPSPath(
                            pathToResolve,
                            cmdContext,
                            out provider,
                            out drive);

                    PathInfo pathInfo =
                        new PathInfo(
                            drive,
                            provider,
                            unresolvedPath,
                            cmdlet.SessionState);
                    results.Add(pathInfo);
                }
                else
                {
                    cmdlet.ThrowTerminatingError(
                        new ErrorRecord(
                            pathNotFound.ErrorRecord,
                            pathNotFound));
                }
            }

            if (results.Count == 1)
            {
                return results[0];
            }
            else //if (results.Count > 1)
            {
                Exception e = PSTraceSource.NewNotSupportedException();
                cmdlet.ThrowTerminatingError(
                    new ErrorRecord(e,
                    "NotSupported",
                    ErrorCategory.NotImplemented,
                    results));
                return null;
            }
        } // ResolvePath
    }

    #endregion Helper Classes
}
