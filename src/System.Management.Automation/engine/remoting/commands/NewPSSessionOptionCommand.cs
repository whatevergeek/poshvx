/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Management.Automation;
using System.Management.Automation.Remoting;
using System.Management.Automation.Runspaces;

namespace System.Management.Automation.Remoting
{
    /// <summary>
    /// IMPORTANT: proxy configuration is supported for HTTPS only; for HTTP, the direct 
    /// connection to the server is used 
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1027:MarkEnumsWithFlags")]
    public enum ProxyAccessType
    {
        /// <summary>
        /// ProxyAccessType is not specified. That means Proxy information (ProxyAccessType, ProxyAuthenticationMechanism 
        /// and ProxyCredential)is not passed to WSMan at all.
        /// </summary>
        None = 0,
        /// <summary>
        /// use the Internet Explorer proxy configuration for the current user.
        ///  Internet Explorer proxy settings for the current active network connection. 
        ///  This option requires the user profile to be loaded, so the option can 
        ///  be directly used when called within a process that is running under 
        ///  an interactive user account identity; if the client application is running 
        ///  under a user context different than the interactive user, the client 
        ///  application has to explicitly load the user profile prior to using this option.
        /// </summary>
        IEConfig = 1,
        /// <summary>
        /// proxy settings configured for WinHTTP, using the ProxyCfg.exe utility
        /// </summary>
        WinHttpConfig = 2,
        /// <summary>
        /// Force autodetection of proxy
        /// </summary>
        AutoDetect = 4,
        /// <summary>
        /// do not use a proxy server - resolves all host names locally
        /// </summary>
        NoProxyServer = 8
    }
    /// <summary>
    /// Options for a remote PSSession
    /// </summary>
    public sealed class PSSessionOption
    {
        /// <summary>
        /// Creates a new instance of <see cref="PSSessionOption"/>
        /// </summary>
        public PSSessionOption()
        {
        }

        /// <summary>
        /// The MaximumConnectionRedirectionCount parameter enables the implicit redirection functionality.
        /// -1 = no limit
        ///  0 = no redirection
        /// </summary>
        public int MaximumConnectionRedirectionCount { get; set; } = WSManConnectionInfo.defaultMaximumConnectionRedirectionCount;

        /// <summary>
        /// If false, underlying WSMan infrastructure will compress data sent on the network.
        /// If true, data will not be compressed. Compression improves performance by 
        /// reducing the amount of data sent on the network. Compression my require extra
        /// memory consumption and CPU usage. In cases where available memory / CPU is less, 
        /// set this property to "true".
        /// By default the value of this property is "false".
        /// </summary>
        public bool NoCompression { get; set; } = false;

        /// <summary>
        /// If <c>true</c> then Operating System won't load the user profile (i.e. registry keys under HKCU) on the remote server
        /// which can result in a faster session creation time.  This option won't have any effect if the remote machine has
        /// already loaded the profile (i.e. in another session). 
        /// </summary>
        public bool NoMachineProfile { get; set; } = false;

        /// <summary>
        /// By default, ProxyAccessType is None, that means Proxy information (ProxyAccessType, 
        /// ProxyAuthenticationMechanism and ProxyCredential)is not passed to WSMan at all.
        /// </summary>
        public ProxyAccessType ProxyAccessType { get; set; } = ProxyAccessType.None;

        /// <summary>
        /// The following is the definition of the input parameter "ProxyAuthentication".
        /// This parameter takes a set of authentication methods the user can select 
        /// from.  The available options should be as follows:
        /// - Negotiate: Use the default authentication (as defined by the underlying 
        /// protocol) for establishing a remote connection.
        /// - Basic:  Use basic authentication for establishing a remote connection
        /// - Digest: Use Digest authentication for establishing a remote connection
        /// 
        /// Default is Negotiate.
        /// </summary>
        public AuthenticationMechanism ProxyAuthentication
        {
            get { return _proxyAuthentication; }
            set
            {
                switch (value)
                {
                    case AuthenticationMechanism.Basic:
                    case AuthenticationMechanism.Negotiate:
                    case AuthenticationMechanism.Digest:
                        _proxyAuthentication = value;
                        break;
                    default:
                        string message = PSRemotingErrorInvariants.FormatResourceString(RemotingErrorIdStrings.ProxyAmbiguosAuthentication,
                            value,
                            AuthenticationMechanism.Basic.ToString(),
                            AuthenticationMechanism.Negotiate.ToString(),
                            AuthenticationMechanism.Digest.ToString());
                        throw new ArgumentException(message);
                }
            }
        }
        private AuthenticationMechanism _proxyAuthentication = AuthenticationMechanism.Negotiate;

        /// <summary>
        /// The following is the definition of the input parameter "ProxyCredential".
        /// </summary>
        public PSCredential ProxyCredential { get; set; }


        /// <summary>
        /// When connecting over HTTPS, the client does not validate that the server 
        /// certificate is signed by a trusted certificate authority (CA). Use only when 
        /// the remote computer is trusted by other means, for example, if the remote 
        /// computer is part of a network that is physically secure and isolated or the 
        /// remote computer is listed as a trusted host in WinRM configuration
        /// </summary>
        public bool SkipCACheck { get; set; }

        /// <summary>
        /// Indicates that certificate common name (CN) of the server need not match the 
        /// hostname of the server. Used only in remote operations using https. This 
        /// option should only be used for trusted machines.
        /// </summary>
        public bool SkipCNCheck { get; set; }

        /// <summary>
        /// Indicates that certificate common name (CN) of the server need not match the 
        /// hostname of the server. Used only in remote operations using https. This 
        /// option should only be used for trusted machines
        /// </summary>
        public bool SkipRevocationCheck { get; set; }

        /// <summary>
        /// The duration for which PowerShell remoting waits before timing out 
        /// for any operation. The user would like to tweak this timeout 
        /// depending on whether he/she is connecting to a machine in the data
        /// center or across a slow WAN.
        /// 
        /// Default: 3*60*1000 == 3minutes
        /// </summary>
        public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromMilliseconds(BaseTransportManager.ClientDefaultOperationTimeoutMs);

        /// <summary>
        /// Specifies that no encryption will be used when doing remote operations over 
        /// http. Unencrypted traffic is not allowed by default and must be enabled in 
        /// the local configuration
        /// </summary>
        public bool NoEncryption { get; set; }

        /// <summary>
        /// Indicates the request is encoded in UTF16 format rather than UTF8 format; 
        /// UTF8 is the default.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", MessageId = "UTF")]
        public bool UseUTF16 { get; set; }

        /// <summary>
        /// Uses Service Principal Name (SPN) along with the Port number during authentication.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", MessageId = "SPN")]
        public bool IncludePortInSPN { get; set; }

        /// <summary>
        /// Determines how server in disconnected state deals with cached output
        /// data when the cache becomes filled.
        /// Default value is 'block mode' where command execution is blocked after
        /// the server side data cache becomes filled.
        /// </summary>
        public OutputBufferingMode OutputBufferingMode { get; set; } = WSManConnectionInfo.DefaultOutputBufferingMode;

        /// <summary>
        /// Number of times a connection will be re-attempted when a connection fails due to network
        /// issues.
        /// </summary>
        public int MaxConnectionRetryCount { get; set; } = WSManConnectionInfo.DefaultMaxConnectionRetryCount;

        /// <summary>
        /// Culture that the remote session should use
        /// </summary>
        public CultureInfo Culture { get; set; }

        /// <summary>
        /// UI culture that the remote session should use
        /// </summary>
        public CultureInfo UICulture { get; set; }

        /// <summary>
        /// Total data (in bytes) that can be received from a remote machine
        /// targeted towards a command. If null, then the size is unlimited.
        /// Default is unlimited data.
        /// </summary>
        public Nullable<int> MaximumReceivedDataSizePerCommand { get; set; }

        /// <summary>
        /// Maximum size (in bytes) of a deserialized object received from a remote machine.
        /// If null, then the size is unlimited. Default is 200MB object size.
        /// </summary>
        public Nullable<int> MaximumReceivedObjectSize { get; set; } = 200 << 20;

        /// <summary>
        /// Application arguments the server can see in <see cref="System.Management.Automation.Remoting.PSSenderInfo.ApplicationArguments"/>
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public PSPrimitiveDictionary ApplicationArguments { get; set; }

        /// <summary>
        /// The duration for which PowerShell remoting waits before timing out on a connection to a remote machine. 
        /// Simply put, the timeout for a remote runspace creation. 
        /// The user would like to tweak this timeout depending on whether 
        /// he/she is connecting to a machine in the data center or across a slow WAN.
        /// 
        /// Default: 3 * 60 * 1000 = 3 minutes
        /// </summary>
        public TimeSpan OpenTimeout { get; set; } = TimeSpan.FromMilliseconds(RunspaceConnectionInfo.DefaultOpenTimeout);

        /// <summary>
        /// The duration for which PowerShell should wait before it times out on cancel operations 
        /// (close runspace or stop powershell). For instance, when the user hits ctrl-C, 
        /// New-PSSession cmdlet tries to call a stop on all remote runspaces which are in the Opening state. 
        /// The user wouldn�t mind waiting for 15 seconds, but this should be time bound and of a shorter duration. 
        /// A high timeout here like 3 minutes will give the user a feeling that the PowerShell client has hung.
        /// 
        /// Default: 60 * 1000 = 1 minute
        /// </summary>
        public TimeSpan CancelTimeout { get; set; } = TimeSpan.FromMilliseconds(RunspaceConnectionInfo.defaultCancelTimeout);

        /// <summary>
        /// The duration for which a Runspace on server needs to wait before it declares the client dead and closes itself down. 
        /// This is especially important as these values may have to be configured differently for enterprise administration 
        /// and exchange scenarios.
        /// 
        /// Default: -1 -> Use current server value for IdleTimeout.
        /// </summary>
        public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMilliseconds(RunspaceConnectionInfo.DefaultIdleTimeout);
    }
}

namespace Microsoft.PowerShell.Commands
{
    /// <summary>
    /// This class implements New-PSSessionOption cmdlet.  
    /// Spec: TBD
    /// </summary>
    [Cmdlet(VerbsCommon.New, "PSSessionOption", HelpUri = "http://go.microsoft.com/fwlink/?LinkID=144305", RemotingCapability = RemotingCapability.None)]
    [OutputType(typeof(PSSessionOption))]
    public sealed class NewPSSessionOptionCommand : PSCmdlet
    {
        #region Parameters (specific to PSSessionOption)

        /// <summary>
        /// The MaximumRedirection parameter enables the implicit redirection functionality
        /// -1 = no limit
        ///  0 = no redirection
        /// </summary>
        [Parameter]
        public int MaximumRedirection
        {
            get { return _maximumRedirection.Value; }
            set { _maximumRedirection = value; }
        }
        private int? _maximumRedirection;

        /// <summary>
        /// If false, underlying WSMan infrastructure will compress data sent on the network.
        /// If true, data will not be compressed. Compression improves performance by 
        /// reducing the amount of data sent on the network. Compression my require extra
        /// memory consumption and CPU usage. In cases where available memory / CPU is less, 
        /// set this property to "true".
        /// By default the value of this property is "false".
        /// </summary>
        [Parameter]
        public SwitchParameter NoCompression { get; set; }

        /// <summary>
        /// If <c>true</c> then Operating System won't load the user profile (i.e. registry keys under HKCU) on the remote server
        /// which can result in a faster session creation time.  This option won't have any effect if the remote machine has
        /// already loaded the profile (i.e. in another session). 
        /// </summary>
        [Parameter]
        public SwitchParameter NoMachineProfile { get; set; }

        /// <summary>
        /// Culture that the remote session should use
        /// </summary>
        [Parameter]
        [ValidateNotNull]
        public CultureInfo Culture { get; set; }

        /// <summary>
        /// UI culture that the remote session should use
        /// </summary>
        [Parameter]
        [ValidateNotNull]
        public CultureInfo UICulture { get; set; }

        /// <summary>
        /// Total data (in bytes) that can be received from a remote machine
        /// targeted towards a command. If null, then the size is unlimited.
        /// Default is unlimited data.
        /// </summary>
        [Parameter]
        public int MaximumReceivedDataSizePerCommand
        {
            get { return _maxRecvdDataSizePerCommand.Value; }
            set { _maxRecvdDataSizePerCommand = value; }
        }
        private Nullable<int> _maxRecvdDataSizePerCommand;

        /// <summary>
        /// Maximum size (in bytes) of a deserialized object received from a remote machine.
        /// If null, then the size is unlimited. Default is unlimited object size.
        /// </summary>
        [Parameter]
        public int MaximumReceivedObjectSize
        {
            get { return _maxRecvdObjectSize.Value; }
            set { _maxRecvdObjectSize = value; }
        }
        private Nullable<int> _maxRecvdObjectSize;

        /// <summary>
        /// Specifies the output mode on the server when it is in Disconnected mode
        /// and its output data cache becomes full.
        /// </summary>
        [Parameter]
        public OutputBufferingMode OutputBufferingMode { get; set; }

        /// <summary>
        /// Maximum number of times a connection will be re-attempted when a connection fails due to network
        /// issues.
        /// </summary>
        [Parameter]
        [ValidateRange(0, Int32.MaxValue)]
        public int MaxConnectionRetryCount { get; set; }

        /// <summary>
        /// Application arguments the server can see in <see cref="System.Management.Automation.Remoting.PSSenderInfo.ApplicationArguments"/>
        /// </summary>
        [Parameter]
        [ValidateNotNull]
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public PSPrimitiveDictionary ApplicationArguments { get; set; }

        /// <summary>
        /// The duration for which PowerShell remoting waits (in milliseconds) before timing 
        /// out on a connection to a remote machine. Simply put, the timeout for a remote 
        /// runspace creation. 
        /// 
        /// The user would like to tweak this timeout depending on whether 
        /// he/she is connecting to a machine in the data center or across a slow WAN.
        /// </summary>
        [Parameter]
        [Alias("OpenTimeoutMSec")]
        [ValidateRange(0, Int32.MaxValue)]
        public int OpenTimeout
        {
            get
            {
                return _openTimeout.HasValue ? _openTimeout.Value :
                    RunspaceConnectionInfo.DefaultOpenTimeout;
            }
            set { _openTimeout = value; }
        }
        private int? _openTimeout;

        /// <summary>
        /// The duration for which PowerShell should wait (in milliseconds) before it 
        /// times out on cancel operations (close runspace or stop powershell). For
        /// instance, when the user hits ctrl-C, New-PSSession cmdlet tries to call a
        /// stop on all remote runspaces which are in the Opening state. The user 
        /// wouldn�t mind waiting for 15 seconds, but this should be time bound and of a 
        /// shorter duration. A high timeout here like 3 minutes will give the user
        /// a feeling that the PowerShell client has hung.
        /// </summary>
        [Parameter]
        [Alias("CancelTimeoutMSec")]
        [ValidateRange(0, Int32.MaxValue)]
        public int CancelTimeout
        {
            get
            {
                return _cancelTimeout.HasValue ? _cancelTimeout.Value :
                    BaseTransportManager.ClientCloseTimeoutMs;
            }
            set { _cancelTimeout = value; }
        }
        private int? _cancelTimeout;

        /// <summary>
        /// The duration for which a Runspace on server needs to wait (in milliseconds) before it
        /// declares the client dead and closes itself down. 
        /// This is especially important as these values may have to be configured differently 
        /// for enterprise administration scenarios.
        /// </summary>
        [Parameter]
        [ValidateRange(-1, Int32.MaxValue)]
        [Alias("IdleTimeoutMSec")]
        public int IdleTimeout
        {
            get
            {
                return _idleTimeout.HasValue ? _idleTimeout.Value
                    : RunspaceConnectionInfo.DefaultIdleTimeout;
            }
            set { _idleTimeout = value; }
        }
        private int? _idleTimeout;

        #endregion Parameters

        #region Parameters copied from New-WSManSessionOption

        /// <summary>
        /// By default, ProxyAccessType is None, that means Proxy information (ProxyAccessType, 
        /// ProxyAuthenticationMechanism and ProxyCredential)is not passed to WSMan at all.
        /// </summary>
        [Parameter]
        [ValidateNotNullOrEmpty]
        public ProxyAccessType ProxyAccessType { get; set; } = ProxyAccessType.None;

        /// <summary>
        /// The following is the definition of the input parameter "ProxyAuthentication".
        /// This parameter takes a set of authentication methods the user can select 
        /// from.  The available options should be as follows:
        /// - Negotiate: Use the default authentication (as defined by the underlying 
        /// protocol) for establishing a remote connection.
        /// - Basic:  Use basic authentication for establishing a remote connection
        /// - Digest: Use Digest authentication for establishing a remote connection
        /// </summary>
        [Parameter]
        public AuthenticationMechanism ProxyAuthentication { get; set; } = AuthenticationMechanism.Negotiate;

        /// <summary>
        /// The following is the definition of the input parameter "ProxyCredential".
        /// </summary>
        [Parameter]
        [ValidateNotNullOrEmpty]
        [Credential]
        public PSCredential ProxyCredential { get; set; }

        /// <summary>
        /// The following is the definition of the input parameter "SkipCACheck".
        /// When connecting over HTTPS, the client does not validate that the server 
        /// certificate is signed by a trusted certificate authority (CA). Use only when 
        /// the remote computer is trusted by other means, for example, if the remote 
        /// computer is part of a network that is physically secure and isolated or the 
        /// remote computer is listed as a trusted host in WinRM configuration
        /// </summary>
        [Parameter]
        public SwitchParameter SkipCACheck
        {
            get { return _skipcacheck; }
            set { _skipcacheck = value; }
        }
        private bool _skipcacheck;

        /// <summary>
        /// The following is the definition of the input parameter "SkipCNCheck".
        /// Indicates that certificate common name (CN) of the server need not match the 
        /// hostname of the server. Used only in remote operations using https. This 
        /// option should only be used for trusted machines
        /// </summary>
        [Parameter]
        public SwitchParameter SkipCNCheck
        {
            get { return _skipcncheck; }
            set { _skipcncheck = value; }
        }
        private bool _skipcncheck;

        /// <summary>
        /// The following is the definition of the input parameter "SkipRevocation".
        /// Indicates that certificate common name (CN) of the server need not match the 
        /// hostname of the server. Used only in remote operations using https. This 
        /// option should only be used for trusted machines
        /// </summary>
        [Parameter]
        public SwitchParameter SkipRevocationCheck
        {
            get { return _skiprevocationcheck; }
            set { _skiprevocationcheck = value; }
        }
        private bool _skiprevocationcheck;

        /// <summary>
        /// The following is the definition of the input parameter "Timeout".
        /// Defines the timeout in milliseconds for the wsman operation
        /// </summary>
        [Parameter]
        [Alias("OperationTimeoutMSec")]
        [ValidateRange(0, Int32.MaxValue)]
        public Int32 OperationTimeout
        {
            get
            {
                return (_operationtimeout.HasValue ? _operationtimeout.Value :
                    BaseTransportManager.ClientDefaultOperationTimeoutMs);
            }
            set { _operationtimeout = value; }
        }
        private Int32? _operationtimeout;

        /// <summary>
        /// The following is the definition of the input parameter "UnEncrypted".
        /// Specifies that no encryption will be used when doing remote operations over 
        /// http. Unencrypted traffic is not allowed by default and must be enabled in 
        /// the local configuration
        /// </summary>
        [Parameter]
        public SwitchParameter NoEncryption
        {
            get { return _noencryption; }
            set
            {
                _noencryption = value;
            }
        }
        private bool _noencryption;

        /// <summary>
        /// The following is the definition of the input parameter "UTF16".
        /// Indicates the request is encoded in UTF16 format rather than UTF8 format; 
        /// UTF8 is the default.
        /// </summary>
        [Parameter]
        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", MessageId = "UTF")]
        public SwitchParameter UseUTF16
        {
            get { return _useutf16; }
            set
            {
                _useutf16 = value;
            }
        }
        private bool _useutf16;

        /// <summary>
        /// Uses Service Principal Name (SPN) along with the Port number during authentication.
        /// </summary>
        [Parameter]
        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", MessageId = "SPN")]
        public SwitchParameter IncludePortInSPN
        {
            get { return _includePortInSPN; }
            set { _includePortInSPN = value; }
        }
        private bool _includePortInSPN;

        #endregion

        #region Implementation

        /// <summary>
        /// Performs initialization of cmdlet execution.
        /// </summary>
        protected override void BeginProcessing()
        {
            PSSessionOption result = new PSSessionOption();
            // Begin: WSMan specific options
            result.ProxyAccessType = this.ProxyAccessType;
            result.ProxyAuthentication = this.ProxyAuthentication;
            result.ProxyCredential = this.ProxyCredential;
            result.SkipCACheck = this.SkipCACheck;
            result.SkipCNCheck = this.SkipCNCheck;
            result.SkipRevocationCheck = this.SkipRevocationCheck;
            if (_operationtimeout.HasValue)
            {
                result.OperationTimeout = TimeSpan.FromMilliseconds(_operationtimeout.Value);
            }
            result.NoEncryption = this.NoEncryption;
            result.UseUTF16 = this.UseUTF16;
            result.IncludePortInSPN = this.IncludePortInSPN;
            // End: WSMan specific options
            if (_maximumRedirection.HasValue)
            {
                result.MaximumConnectionRedirectionCount = this.MaximumRedirection;
            }

            result.NoCompression = this.NoCompression.IsPresent;
            result.NoMachineProfile = this.NoMachineProfile.IsPresent;

            result.MaximumReceivedDataSizePerCommand = _maxRecvdDataSizePerCommand;
            result.MaximumReceivedObjectSize = _maxRecvdObjectSize;

            if (this.Culture != null)
            {
                result.Culture = this.Culture;
            }
            if (this.UICulture != null)
            {
                result.UICulture = this.UICulture;
            }

            if (_openTimeout.HasValue)
            {
                result.OpenTimeout = TimeSpan.FromMilliseconds(_openTimeout.Value);
            }
            if (_cancelTimeout.HasValue)
            {
                result.CancelTimeout = TimeSpan.FromMilliseconds(_cancelTimeout.Value);
            }
            if (_idleTimeout.HasValue)
            {
                result.IdleTimeout = TimeSpan.FromMilliseconds(_idleTimeout.Value);
            }

            result.OutputBufferingMode = OutputBufferingMode;

            result.MaxConnectionRetryCount = MaxConnectionRetryCount;

            if (this.ApplicationArguments != null)
            {
                result.ApplicationArguments = this.ApplicationArguments;
            }

            this.WriteObject(result);
        }

        #endregion Methods
    }
}
