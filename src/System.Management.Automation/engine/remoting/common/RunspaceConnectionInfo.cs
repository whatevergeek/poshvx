/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.ComponentModel; // Win32Exception
using System.Management.Automation.Tracing;
using System.Management.Automation.Remoting;
using System.Management.Automation.Internal;
using System.Management.Automation.Remoting.Client;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Security.AccessControl;
using Microsoft.Win32.SafeHandles;
using Dbg = System.Management.Automation.Diagnostics;
using WSManAuthenticationMechanism = System.Management.Automation.Remoting.Client.WSManNativeApi.WSManAuthenticationMechanism;

// ReSharper disable CheckNamespace

namespace System.Management.Automation.Runspaces
// ReSharper restore CheckNamespace
{
    /// <summary>
    /// Different Authentication Mechanisms supported by New-Runspace command to connect
    /// to remote server.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1027:MarkEnumsWithFlags")]
    [SuppressMessage("Microsoft.Naming", "CA1724:TypeNamesShouldNotMatchNamespaces")]
    public enum AuthenticationMechanism
    {
        /// <summary>
        /// Use the default authentication (as defined by the underlying protocol) 
        /// for establishing a remote connection.
        /// </summary>
        Default = 0x0,
        /// <summary>
        /// Use Basic authentication for establishing a remote connection.
        /// </summary>
        Basic = 0x1,
        /// <summary>
        /// Use Negotiate authentication for establishing a remote connection.
        /// </summary>
        Negotiate = 0x2,
        /// <summary>
        /// Use Negotiate authentication for establishing a remote connection.
        /// Allow implicit credentials for Negotiate.
        /// </summary>
        NegotiateWithImplicitCredential = 0x3,
        /// <summary>
        /// Use CredSSP authentication for establishing a remote connection.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Credssp")]
        Credssp = 0x4,
        /// <summary>
        /// Use Digest authentication mechanism. Digest authentication operates much 
        /// like Basic authentication. However, unlike Basic authentication, Digest authentication 
        /// transmits credentials across the network as a hash value, also known as a message digest.
        /// The user name and password cannot be deciphered from the hash value. Conversely, Basic 
        /// authentication sends a Base 64 encoded password, essentially in clear text, across the 
        /// network.
        /// </summary>
        Digest = 0x5,
        /// <summary>
        /// Use Kerberos authentication for establishing a remote connection.
        /// </summary>
        Kerberos = 0x6,
    }

    /// <summary>
    /// Specifies the type of session configuration that
    /// should be used for creating a connection info
    /// </summary>
    public enum PSSessionType
    {
        /// <summary>
        /// Default PowerShell remoting
        /// endpoint
        /// </summary>
        DefaultRemoteShell = 0,

        /// <summary>
        /// Default Workflow endpoint
        /// </summary>
        Workflow = 1,
    }

    /// <summary>
    /// Specify the type of access mode that should be
    /// used when creating a session configuration
    /// </summary>
    public enum PSSessionConfigurationAccessMode
    {
        /// <summary>
        /// Disable the configuration
        /// </summary>
        Disabled = 0,

        /// <summary>
        /// Allow local access
        /// </summary>
        Local = 1,

        /// <summary>
        /// Default allow remote access
        /// </summary>
        Remote = 2,
    }

    /// <summary>
    /// WSManTransportManager supports disconnected PowerShell sessions.
    /// When a remote PS session server is in disconnected state, output 
    /// from the running command pipeline is cached on the server.  This 
    /// enum determines what the server does when the cache is full.
    /// </summary>
    public enum OutputBufferingMode
    {
        /// <summary>
        /// No output buffering mode specified.  Output buffering mode on server will 
        /// default to Block if a new session is created, or will retain its current
        /// mode for non-creation scenarios (e.g., disconnect/connect operations).
        /// </summary>
        None = 0,

        /// <summary>
        /// Command pipeline execution continues, excess output is dropped in FIFO manner.
        /// </summary>
        Drop = 1,

        /// <summary>
        /// Command pipeline execution on server is blocked until session is reconnected.
        /// </summary>
        Block = 2
    }

    /// <summary>
    /// Class which defines connection path to a remote runspace 
    /// that needs to be created. Transport specific connection
    /// paths will be derived from this
    /// </summary>
    public abstract class RunspaceConnectionInfo
    {
        #region Public Properties

        /// <summary>
        /// Name of the computer
        /// </summary>
        public abstract String ComputerName { get; set; }

        /// <summary>
        /// Credential used for the connection
        /// </summary>
        public abstract PSCredential Credential { get; set; }

        /// <summary>
        /// Authentication mechanism to use while connecting to the server
        /// </summary>
        public abstract AuthenticationMechanism AuthenticationMechanism { get; set; }

        /// <summary>
        /// ThumbPrint of a certificate used for connecting to a remote machine.
        /// When this is specified, you dont need to supply credential and authentication
        /// mechanism.
        /// </summary>
        public abstract string CertificateThumbprint { get; set; }

        /// <summary>
        /// Culture that the remote session should use
        /// </summary>
        public CultureInfo Culture
        {
            get
            {
                return _culture;
            }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException("value");
                }
                _culture = value;
            }
        }

        private CultureInfo _culture = CultureInfo.CurrentCulture;

        /// <summary>
        /// UI culture that the remote session should use
        /// </summary>
        public CultureInfo UICulture
        {
            get
            {
                return _uiCulture;
            }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException("value");
                }
                _uiCulture = value;
            }
        }

        private CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

        /// <summary>
        /// The duration (in ms) for which PowerShell remoting waits before timing out on a connection to a remote machine. 
        /// Simply put, the timeout for a remote runspace creation. 
        /// The administrator would like to tweak this timeout depending on whether 
        /// he/she is connecting to a machine in the data center or across a slow WAN.
        /// </summary>
        public int OpenTimeout
        {
            get { return _openTimeout; }
            set
            {
                _openTimeout = value;

                if (this is WSManConnectionInfo && _openTimeout == DefaultTimeout)
                {
                    _openTimeout = DefaultOpenTimeout;
                }
                else if (this is WSManConnectionInfo && _openTimeout == InfiniteTimeout)
                {
                    // this timeout value gets passed to a 
                    // timer associated with the session 
                    // data structure handler state machine. 
                    // The timer constructor will throw an exception
                    // for any value greater than Int32.MaxValue
                    // hence this is the maximum possible limit
                    _openTimeout = Int32.MaxValue;
                }
            }
        }
        private int _openTimeout = DefaultOpenTimeout;
        internal const int DefaultOpenTimeout = 3 * 60 * 1000; // 3 minutes
        internal const int DefaultTimeout = -1;
        internal const int InfiniteTimeout = 0;


        /// <summary>
        /// The duration (in ms) for which PowerShell should wait before it times out on cancel operations 
        /// (close runspace or stop powershell). For instance, when the user hits ctrl-C, 
        /// New-PSSession cmdlet tries to call a stop on all remote runspaces which are in the Opening state. 
        /// The administrator wouldn�t mind waiting for 15 seconds, but this should be time bound and of a shorter duration. 
        /// A high timeout here like 3 minutes will give the administrator a feeling that the PowerShell client has hung.
        /// </summary>
        public int CancelTimeout { get; set; } = defaultCancelTimeout;

        internal const int defaultCancelTimeout = BaseTransportManager.ClientCloseTimeoutMs;

        /// <summary>
        /// The duration for which PowerShell remoting waits before timing out 
        /// for any operation. The user would like to tweak this timeout 
        /// depending on whether he/she is connecting to a machine in the data
        /// center or across a slow WAN.
        /// 
        /// Default: 3*60*1000 == 3minutes
        /// </summary>
        public int OperationTimeout { get; set; } = BaseTransportManager.ClientDefaultOperationTimeoutMs;

        /// <summary>
        /// The duration (in ms) for which a Runspace on server needs to wait before it declares the client dead and closes itself down. 
        /// This is especially important as these values may have to be configured differently for enterprise administration 
        /// and exchange scenarios.
        /// </summary>
        public int IdleTimeout { get; set; } = DefaultIdleTimeout;

        internal const int DefaultIdleTimeout = BaseTransportManager.UseServerDefaultIdleTimeout;

        /// <summary>
        /// The maximum allowed idle timeout duration (in ms) that can be set on a Runspace.  This is a read-only property
        /// that is set once the Runspace is successfully created and opened.
        /// </summary>
        public int MaxIdleTimeout { get; internal set; } = Int32.MaxValue;

        /// <summary>
        /// Populates session options from a PSSessionOption instance.
        /// </summary>
        /// <param name="options"></param>
        public virtual void SetSessionOptions(PSSessionOption options)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            if (options.Culture != null)
            {
                this.Culture = options.Culture;
            }

            if (options.UICulture != null)
            {
                this.UICulture = options.UICulture;
            }

            _openTimeout = TimeSpanToTimeOutMs(options.OpenTimeout);
            CancelTimeout = TimeSpanToTimeOutMs(options.CancelTimeout);
            OperationTimeout = TimeSpanToTimeOutMs(options.OperationTimeout);

            // Special case for idle timeout.  A value of milliseconds == -1 
            // (BaseTransportManager.UseServerDefaultIdleTimeout) is allowed for 
            // specifying the default value on the server.
            IdleTimeout = (options.IdleTimeout.TotalMilliseconds >= BaseTransportManager.UseServerDefaultIdleTimeout &&
                                options.IdleTimeout.TotalMilliseconds < int.MaxValue)
                                ? (int)(options.IdleTimeout.TotalMilliseconds) : int.MaxValue;
        }

        #endregion Public Properties

        #region Internal Methods

        internal int TimeSpanToTimeOutMs(TimeSpan t)
        {
            if ((t.TotalMilliseconds > int.MaxValue) || (t == TimeSpan.MaxValue) || (t.TotalMilliseconds < 0))
            {
                return int.MaxValue;
            }
            else
            {
                return (int)(t.TotalMilliseconds);
            }
        }

        /// <summary>
        /// Creates the appropriate client session transportmanager.
        /// </summary>
        /// <param name="instanceId">Runspace/Pool instance Id</param>
        /// <param name="sessionName">Session name</param>
        /// <param name="cryptoHelper">PSRemotingCryptoHelper</param>
        internal virtual BaseClientSessionTransportManager CreateClientSessionTransportManager(
            Guid instanceId,
            string sessionName,
            PSRemotingCryptoHelper cryptoHelper)
        {
            throw new PSNotImplementedException();
        }

        /// <summary>
        /// Create a copy of the connection info object.
        /// </summary>
        /// <returns>Copy of the connection info object.</returns>
        internal virtual RunspaceConnectionInfo InternalCopy()
        {
            throw new PSNotImplementedException();
        }

        #endregion
    }

    /// <summary>
    /// Class which defines path to a remote runspace that
    /// need to be created
    /// </summary>
    public sealed class WSManConnectionInfo : RunspaceConnectionInfo
    {
        #region Public Properties

        /// <summary>
        /// Uri associated with this connection path
        /// </summary>
        public Uri ConnectionUri
        {
            get
            {
                return _connectionUri;
            }
            set
            {
                if (value == null)
                {
                    throw PSTraceSource.NewArgumentNullException("value");
                }
                UpdateUri(value);
            }
        }

        /// <summary>
        /// Name of the computer
        /// </summary>
        public override String ComputerName
        {
            get
            {
                return _computerName;
            }
            set
            {
                // null or empty value allowed
                ConstructUri(_scheme, value, null, _appName);
            }
        }

        /// <summary>
        /// Scheme used for connection
        /// </summary>
        public String Scheme
        {
            get
            {
                return _scheme;
            }
            set
            {
                // null or empty value allowed
                ConstructUri(value, _computerName, null, _appName);
            }
        }

        /// <summary>
        /// Port in which to connect
        /// </summary>
        public Int32 Port
        {
            get
            {
                return ConnectionUri.Port;
            }
            set
            {
                ConstructUri(_scheme, _computerName, value, _appName);
            }
        }

        /// <summary>
        /// AppName which identifies the connection 
        /// end point in the machine
        /// </summary>
        public String AppName
        {
            get
            {
                return _appName;
            }
            set
            {
                //null or empty value allowed
                ConstructUri(_scheme, _computerName, null, value);
            }
        }

        /// <summary>
        /// Credential used for the connection
        /// </summary>
        public override PSCredential Credential
        {
            get
            {
                return _credential;
            }
            set
            {
                // null or empty value allowed
                _credential = value;
            }
        }

        /// <summary>
        /// 
        /// </summary> 
        [SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings", Scope = "member", Target = "System.Management.Automation.Runspaces.WSManConnectionInfo.#ShellUri")]
        public string ShellUri
        {
            get
            {
                return _shellUri;
            }
            set
            {
                _shellUri = ResolveShellUri(value);
            }
        }

        /// <summary>
        /// Authentication mechanism to use while connecting to the server
        /// </summary>
        public override AuthenticationMechanism AuthenticationMechanism
        {
            get
            {
                switch (WSManAuthenticationMechanism)
                {
                    case WSManAuthenticationMechanism.WSMAN_FLAG_DEFAULT_AUTHENTICATION:
                        return AuthenticationMechanism.Default;
                    case WSManAuthenticationMechanism.WSMAN_FLAG_AUTH_BASIC:
                        return AuthenticationMechanism.Basic;
                    case WSManAuthenticationMechanism.WSMAN_FLAG_AUTH_CREDSSP:
                        return AuthenticationMechanism.Credssp;
                    case WSManAuthenticationMechanism.WSMAN_FLAG_AUTH_NEGOTIATE:
                        if (AllowImplicitCredentialForNegotiate)
                        {
                            return AuthenticationMechanism.NegotiateWithImplicitCredential;
                        }
                        return AuthenticationMechanism.Negotiate;
                    case WSManAuthenticationMechanism.WSMAN_FLAG_AUTH_DIGEST:
                        return AuthenticationMechanism.Digest;
                    case WSManAuthenticationMechanism.WSMAN_FLAG_AUTH_KERBEROS:
                        return AuthenticationMechanism.Kerberos;
                    default:
                        Dbg.Assert(false, "Invalid authentication mechanism detected.");
                        return AuthenticationMechanism.Default;
                }
            }
            set
            {
                switch (value)
                {
                    case AuthenticationMechanism.Default:
                        WSManAuthenticationMechanism = WSManAuthenticationMechanism.WSMAN_FLAG_DEFAULT_AUTHENTICATION;
                        break;
                    case AuthenticationMechanism.Basic:
                        WSManAuthenticationMechanism = WSManAuthenticationMechanism.WSMAN_FLAG_AUTH_BASIC;
                        break;
                    case AuthenticationMechanism.Negotiate:
                        WSManAuthenticationMechanism = WSManAuthenticationMechanism.WSMAN_FLAG_AUTH_NEGOTIATE;
                        break;
                    case AuthenticationMechanism.NegotiateWithImplicitCredential:
                        WSManAuthenticationMechanism = WSManAuthenticationMechanism.WSMAN_FLAG_AUTH_NEGOTIATE;
                        AllowImplicitCredentialForNegotiate = true;
                        break;
                    case AuthenticationMechanism.Credssp:
                        WSManAuthenticationMechanism = WSManAuthenticationMechanism.WSMAN_FLAG_AUTH_CREDSSP;
                        break;
                    case AuthenticationMechanism.Digest:
                        WSManAuthenticationMechanism = WSManAuthenticationMechanism.WSMAN_FLAG_AUTH_DIGEST;
                        break;
                    case AuthenticationMechanism.Kerberos:
                        WSManAuthenticationMechanism = WSManAuthenticationMechanism.WSMAN_FLAG_AUTH_KERBEROS;
                        break;
                    default:
                        throw new PSNotSupportedException();
                }
                ValidateSpecifiedAuthentication();
            }
        }

        /// <summary>
        /// AuthenticationMechanism converted to WSManAuthenticationMechanism type.
        /// This is internal.
        /// </summary>
        internal WSManAuthenticationMechanism WSManAuthenticationMechanism { get; private set; } = WSManAuthenticationMechanism.WSMAN_FLAG_DEFAULT_AUTHENTICATION;

        /// <summary>
        /// Allow default credentials for Negotiate
        /// </summary>
        internal bool AllowImplicitCredentialForNegotiate { get; private set; }

        /// <summary>
        /// Returns the actual port property value and not the ConnectionUri port.
        /// Internal only.
        /// </summary>
        internal int PortSetting { get; private set; } = -1;

        /// <summary>
        /// ThumbPrint of a certificate used for connecting to a remote machine.
        /// When this is specified, you dont need to supply credential and authentication
        /// mechanism.
        /// </summary>
        public override string CertificateThumbprint
        {
            get { return _thumbPrint; }
            set
            {
                if (value == null)
                {
                    throw PSTraceSource.NewArgumentNullException("value");
                }
                _thumbPrint = value;
            }
        }

        /// <summary>
        /// Maximum uri redirection count
        /// </summary>
        public int MaximumConnectionRedirectionCount { get; set; }

        internal const int defaultMaximumConnectionRedirectionCount = 5;

        /// <summary>
        /// Total data (in bytes) that can be received from a remote machine
        /// targeted towards a command. If null, then the size is unlimited.
        /// Default is unlimited data.
        /// </summary>
        public Nullable<int> MaximumReceivedDataSizePerCommand { get; set; }

        /// <summary>
        /// Maximum size (in bytes) of a deserialized object received from a remote machine.
        /// If null, then the size is unlimited. Default is unlimited object size.
        /// </summary>
        public Nullable<int> MaximumReceivedObjectSize { get; set; }

        /// <summary>
        /// If true, underlying WSMan infrastructure will compress data sent on the network.
        /// If false, data will not be compressed. Compression improves performance by 
        /// reducing the amount of data sent on the network. Compression my require extra
        /// memory consumption and CPU usage. In cases where available memory / CPU is less, 
        /// set this property to false.
        /// By default the value of this property is "true".
        /// </summary>
        public bool UseCompression { get; set; } = true;

        /// <summary>
        /// If <c>true</c> then Operating System won't load the user profile (i.e. registry keys under HKCU) on the remote server
        /// which can result in a faster session creation time.  This option won't have any effect if the remote machine has
        /// already loaded the profile (i.e. in another session). 
        /// </summary>
        public bool NoMachineProfile { get; set; }

        // BEGIN: Session Options

        /// <summary>
        /// By default, wsman uses IEConfig - the current user 
        ///  Internet Explorer proxy settings for the current active network connection. 
        ///  This option requires the user profile to be loaded, so the option can 
        ///  be directly used when called within a process that is running under 
        ///  an interactive user account identity; if the client application is running 
        ///  under a user context different then the interactive user, the client 
        ///  application has to explicitly load the user profile prior to using this option.
        ///  
        /// IMPORTANT: proxy configuration is supported for HTTPS only; for HTTP, the direct 
        /// connection to the server is used 
        /// </summary>
        public ProxyAccessType ProxyAccessType { get; set; } = ProxyAccessType.None;

        /// <summary>
        /// The following is the definition of the input parameter "ProxyAuthentication".
        /// This parameter takes a set of authentication methods the user can select 
        /// from.  The available options should be as follows:
        /// - Negotiate: Use the default authentication (ad defined by the underlying 
        /// protocol) for establishing a remote connection.
        /// - Basic:  Use basic authentication for establishing a remote connection
        /// - Digest: Use Digest authentication for establishing a remote connection
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

        /// <summary>
        /// The following is the definition of the input parameter "ProxyCredential".
        /// </summary>
        public PSCredential ProxyCredential
        {
            get { return _proxyCredential; }
            set
            {
                if (ProxyAccessType == ProxyAccessType.None)
                {
                    string message = PSRemotingErrorInvariants.FormatResourceString(RemotingErrorIdStrings.ProxyCredentialWithoutAccess,
                                ProxyAccessType.None);
                    throw new ArgumentException(message);
                }

                _proxyCredential = value;
            }
        }


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

        // END: Session Options

        /// <summary>
        /// Determines how server in disconnected state deals with cached output
        /// data when the cache becomes filled.
        /// </summary>
        public OutputBufferingMode OutputBufferingMode { get; set; } = DefaultOutputBufferingMode;

        /// <summary>
        /// Uses Service Principal Name (SPN) along with the Port number during authentication.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", MessageId = "SPN")]
        public bool IncludePortInSPN { get; set; }

        /// <summary>
        /// When true and in loopback scenario (localhost) this enables creation of WSMan
        /// host process with the user interactive token, allowing PowerShell script network access, 
        /// i.e., allows going off box.  When this property is true and a PSSession is disconnected, 
        /// reconnection is allowed only if reconnecting from a PowerShell session on the same box.
        /// </summary>
        public bool EnableNetworkAccess { get; set; }

        /// <summary>
        /// Specifies the maximum number of connection retries if previous connection attempts fail
        /// due to network issues.
        /// </summary>
        public int MaxConnectionRetryCount { get; set; } = DefaultMaxConnectionRetryCount;

        #endregion Public Properties

        #region Constructors

        /// <summary>
        /// Constructor used to create a WSManConnectionInfo
        /// </summary>
        /// <param name="computerName">computer to connect to</param>
        /// <param name="scheme">scheme to be used for connection</param>
        /// <param name="port">port to connect to</param>
        /// <param name="appName">application end point to connect to</param>
        /// <param name="shellUri">remote shell to launch 
        /// on connection</param>
        /// <param name="credential">credential to be used 
        /// for connection</param>
        /// <param name="openTimeout">Timeout in milliseconds for open
        /// call on Runspace to finish</param>
        /// <exception cref="ArgumentException">Invalid
        /// scheme or invalid port is specified</exception>
        [SuppressMessage("Microsoft.Design", "CA1054:UriParametersShouldNotBeStrings", Scope = "member", Target = "System.Management.Automation.Runspaces.WSManConnectionInfo.#.ctor(System.String,System.String,System.Int32,System.String,System.String,System.Management.Automation.PSCredential,System.Int64,System.Int64)", MessageId = "4#")]
        public WSManConnectionInfo(String scheme, String computerName, Int32 port, String appName,
                String shellUri, PSCredential credential,
                    int openTimeout)
        {
            Scheme = scheme;
            ComputerName = computerName;
            Port = port;
            AppName = appName;
            ShellUri = shellUri;
            Credential = credential;
            OpenTimeout = openTimeout;
        }

        /// <summary>
        /// Constructor used to create a WSManConnectionInfo
        /// </summary>
        /// <param name="computerName">computer to connect to</param>
        /// <param name="scheme">Scheme to be used for connection.</param>
        /// <param name="port">port to connect to</param>
        /// <param name="appName">application end point to connect to</param>
        /// <param name="shellUri">remote shell to launch 
        /// on connection</param>
        /// <param name="credential">credential to be used 
        /// for connection</param>
        /// <exception cref="ArgumentException">Invalid
        /// scheme or invalid port is specified</exception>
        /// <remarks>max server life timeout and open timeout are
        /// default in this case</remarks>
        [SuppressMessage("Microsoft.Design", "CA1054:UriParametersShouldNotBeStrings", Scope = "member", Target = "System.Management.Automation.Runspaces.WSManConnectionInfo.#.ctor(System.String,System.String,System.Int32,System.String,System.String,System.Management.Automation.PSCredential)", MessageId = "4#")]
        public WSManConnectionInfo(String scheme, String computerName, Int32 port, String appName,
                    String shellUri, PSCredential credential) :
                        this(scheme, computerName, port, appName, shellUri, credential,
                            DefaultOpenTimeout)
        {
        }

        /// <summary>
        /// Constructor used to create a WSManConnectionInfo
        /// </summary>
        /// <param name="useSsl"></param>
        /// <param name="computerName"></param>
        /// <param name="port"></param>
        /// <param name="appName"></param>
        /// <param name="shellUri"></param>
        /// <param name="credential"></param>
        [SuppressMessage("Microsoft.Design", "CA1054:UriParametersShouldNotBeStrings", MessageId = "4#")]
        public WSManConnectionInfo(bool useSsl, string computerName, Int32 port, string appName, string shellUri,
            PSCredential credential) :
            this(useSsl ? DefaultSslScheme : DefaultScheme, computerName, port, appName, shellUri, credential)
        {
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="useSsl"></param>
        /// <param name="computerName"></param>
        /// <param name="port"></param>
        /// <param name="appName"></param>
        /// <param name="shellUri"></param>
        /// <param name="credential"></param>
        /// <param name="openTimeout"></param>
        [SuppressMessage("Microsoft.Design", "CA1054:UriParametersShouldNotBeStrings", MessageId = "4#")]
        public WSManConnectionInfo(bool useSsl, String computerName, Int32 port, String appName,
                String shellUri, PSCredential credential,
                    int openTimeout) :
            this(useSsl ? DefaultSslScheme : DefaultScheme, computerName,
                port, appName, shellUri, credential, openTimeout)
        {
        }

        /// <summary>
        /// Creates a WSManConnectionInfo for the following URI
        /// and with the default credentials, default server
        /// life time and default open timeout
        ///        http://localhost/
        /// The default shellname Microsoft.PowerShell will be 
        /// used
        /// </summary>
        public WSManConnectionInfo()
        {
            //ConstructUri(DefaultScheme, DefaultComputerName, DefaultPort, DefaultAppName);
            UseDefaultWSManPort = true;
        }

        /// <summary>
        /// Constructor to create a WSManConnectionInfo with a uri
        /// and explicit credentials - server life time is
        /// default and open timeout is default
        /// </summary>
        /// <param name="uri">uri of remote runspace</param>
        /// <param name="shellUri"></param>
        /// <param name="credential">credentials to use to 
        /// connect to the remote runspace</param>
        /// <exception cref="ArgumentException">When an
        /// uri representing an invalid path is specified</exception>
        [SuppressMessage("Microsoft.Design", "CA1054:UriParametersShouldNotBeStrings", Scope = "member", Target = "System.Management.Automation.Runspaces.WSManConnectionInfo.#.ctor(System.Uri,System.String,System.Management.Automation.PSCredential)", MessageId = "1#")]
        public WSManConnectionInfo(Uri uri, String shellUri, PSCredential credential)
        {
            if (uri == null)
            {
                // if the uri is null..apply wsman default logic for port
                // resolution..BUG 542726
                ShellUri = shellUri;
                Credential = credential;
                UseDefaultWSManPort = true;
                return;
            }

            if (!uri.IsAbsoluteUri)
            {
                throw new NotSupportedException(PSRemotingErrorInvariants.FormatResourceString
                                                    (RemotingErrorIdStrings.RelativeUriForRunspacePathNotSupported));
            }

            // This check is needed to make sure we connect to WSMan app in the 
            // default case (when user did not specify any appname) like
            // http://localhost , http://127.0.0.1 etc.
            if (uri.AbsolutePath.Equals("/", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment))
            {
                ConstructUri(uri.Scheme,
                             uri.Host,
                             uri.Port,
                             s_defaultAppName);
            }
            else
            {
                ConnectionUri = uri;
            }
            ShellUri = shellUri;
            Credential = credential;
        }

        /// <summary>
        /// Constructor used to create a WSManConnectionInfo. This constructor supports a certificate thumbprint to
        /// be used while connecting to a remote machine instead of credential.
        /// </summary>
        /// <param name="uri">uri of remote runspace</param>
        /// <param name="shellUri"></param>
        /// <param name="certificateThumbprint">
        /// A thumb print of the certificate to use while connecting to the remote machine.
        /// </param>
        [SuppressMessage("Microsoft.Design", "CA1054:UriParametersShouldNotBeStrings", MessageId = "1#")]
        public WSManConnectionInfo(Uri uri, String shellUri, String certificateThumbprint)
            : this(uri, shellUri, (PSCredential)null)
        {
            _thumbPrint = certificateThumbprint;
        }

        /// <summary>
        /// constructor to create a WSManConnectionInfo with a 
        /// uri specified and the default credentials,
        /// default server life time and default open
        /// timeout
        /// </summary>
        /// <param name="uri">uri of remote runspace</param>
        /// <exception cref="ArgumentException">When an
        /// uri representing an invalid path is specified</exception>
        public WSManConnectionInfo(Uri uri)
            : this(uri, DefaultShellUri, DefaultCredential)
        {
        }

        #endregion Constructors

        #region Public Methods

        /// <summary>
        /// Populates session options from a PSSessionOption instance.
        /// </summary>
        /// <param name="options"></param>
        /// <exception cref="ArgumentException">
        /// 1. Proxy credential cannot be specified when proxy accesstype is None. 
        /// Either specify a valid proxy accesstype other than None or do not specify proxy credential.
        /// </exception>
        public override void SetSessionOptions(PSSessionOption options)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            if ((options.ProxyAccessType == ProxyAccessType.None) && (options.ProxyCredential != null))
            {
                string message = PSRemotingErrorInvariants.FormatResourceString(RemotingErrorIdStrings.ProxyCredentialWithoutAccess,
                            ProxyAccessType.None);
                throw new ArgumentException(message);
            }

            base.SetSessionOptions(options);

            this.MaximumConnectionRedirectionCount =
                options.MaximumConnectionRedirectionCount >= 0
                        ? options.MaximumConnectionRedirectionCount : int.MaxValue;

            this.MaximumReceivedDataSizePerCommand = options.MaximumReceivedDataSizePerCommand;
            this.MaximumReceivedObjectSize = options.MaximumReceivedObjectSize;

            this.UseCompression = !(options.NoCompression);
            this.NoMachineProfile = options.NoMachineProfile;

            ProxyAccessType = options.ProxyAccessType;
            _proxyAuthentication = options.ProxyAuthentication;
            _proxyCredential = options.ProxyCredential;
            SkipCACheck = options.SkipCACheck;
            SkipCNCheck = options.SkipCNCheck;
            SkipRevocationCheck = options.SkipRevocationCheck;
            NoEncryption = options.NoEncryption;
            UseUTF16 = options.UseUTF16;
            IncludePortInSPN = options.IncludePortInSPN;

            OutputBufferingMode = options.OutputBufferingMode;
            MaxConnectionRetryCount = options.MaxConnectionRetryCount;
        }

        /// <summary>
        /// Shallow copy of the current instance.
        /// </summary>
        /// <returns>RunspaceConnectionInfo</returns>
        internal override RunspaceConnectionInfo InternalCopy()
        {
            return Copy();
        }

        /// <summary>
        /// Does a shallow copy of the current instance
        /// </summary>
        /// <returns></returns>
        public WSManConnectionInfo Copy()
        {
            WSManConnectionInfo result = new WSManConnectionInfo();
            result._connectionUri = _connectionUri;
            result._computerName = _computerName;
            result._scheme = _scheme;
            result.PortSetting = PortSetting;
            result._appName = _appName;
            result._shellUri = _shellUri;
            result._credential = _credential;
            result.UseDefaultWSManPort = UseDefaultWSManPort;
            result.WSManAuthenticationMechanism = WSManAuthenticationMechanism;
            result.MaximumConnectionRedirectionCount = MaximumConnectionRedirectionCount;
            result.MaximumReceivedDataSizePerCommand = MaximumReceivedDataSizePerCommand;
            result.MaximumReceivedObjectSize = MaximumReceivedObjectSize;
            result.OpenTimeout = this.OpenTimeout;
            result.IdleTimeout = this.IdleTimeout;
            result.MaxIdleTimeout = this.MaxIdleTimeout;
            result.CancelTimeout = this.CancelTimeout;
            result.OperationTimeout = base.OperationTimeout;
            result.Culture = this.Culture;
            result.UICulture = this.UICulture;
            result._thumbPrint = _thumbPrint;
            result.AllowImplicitCredentialForNegotiate = AllowImplicitCredentialForNegotiate;
            result.UseCompression = UseCompression;
            result.NoMachineProfile = NoMachineProfile;
            result.ProxyAccessType = this.ProxyAccessType;
            result._proxyAuthentication = this.ProxyAuthentication;
            result._proxyCredential = this.ProxyCredential;
            result.SkipCACheck = this.SkipCACheck;
            result.SkipCNCheck = this.SkipCNCheck;
            result.SkipRevocationCheck = this.SkipRevocationCheck;
            result.NoEncryption = this.NoEncryption;
            result.UseUTF16 = this.UseUTF16;
            result.IncludePortInSPN = this.IncludePortInSPN;
            result.EnableNetworkAccess = this.EnableNetworkAccess;
            result.UseDefaultWSManPort = this.UseDefaultWSManPort;
            result.OutputBufferingMode = OutputBufferingMode;
            result.DisconnectedOn = this.DisconnectedOn;
            result.ExpiresOn = this.ExpiresOn;
            result.MaxConnectionRetryCount = this.MaxConnectionRetryCount;

            return result;
        }

        /// <summary>
        /// string for http scheme
        /// </summary>
        public const string HttpScheme = "http";

        /// <summary>
        /// string for https scheme
        /// </summary>
        public const string HttpsScheme = "https";

        #endregion

        #region Internal Methods

        internal override BaseClientSessionTransportManager CreateClientSessionTransportManager(Guid instanceId, string sessionName, PSRemotingCryptoHelper cryptoHelper)
        {
            return new WSManClientSessionTransportManager(
                instanceId,
                this,
                cryptoHelper,
                sessionName);
        }

        #endregion

        #region Private Methods

        private String ResolveShellUri(string shell)
        {
            string resolvedShellUri = shell;
            if (String.IsNullOrEmpty(resolvedShellUri))
            {
                resolvedShellUri = DefaultShellUri;
            }

            if (resolvedShellUri.IndexOf(
                System.Management.Automation.Remoting.Client.WSManNativeApi.ResourceURIPrefix, StringComparison.OrdinalIgnoreCase) == -1)
            {
                resolvedShellUri = System.Management.Automation.Remoting.Client.WSManNativeApi.ResourceURIPrefix + resolvedShellUri;
            }

            return resolvedShellUri;
        }

        /// <summary>
        /// Converts <paramref name="rsCI"/> to a WSManConnectionInfo. If conversion succeeds extracts
        /// the property..otherwise returns default value
        /// </summary>
        /// <param name="rsCI"></param>
        /// <param name="property"></param>
        /// <param name="defaultValue"></param>
        /// <returns></returns>
        internal static T ExtractPropertyAsWsManConnectionInfo<T>(RunspaceConnectionInfo rsCI,
            string property, T defaultValue)
        {
            WSManConnectionInfo wsCI = rsCI as WSManConnectionInfo;
            if (null == wsCI)
            {
                return defaultValue;
            }

            return (T)typeof(WSManConnectionInfo).GetProperty(property, typeof(T)).GetValue(wsCI, null);
        }

        internal void SetConnectionUri(Uri newUri)
        {
            Dbg.Assert(null != newUri, "newUri cannot be null.");
            _connectionUri = newUri;
        }

        /// <summary>
        /// Constructs a Uri from the supplied parameters.
        /// </summary>
        /// <param name="scheme"></param>
        /// <param name="computerName"></param>
        /// <param name="port">
        /// Making the port nullable to make sure the UseDefaultWSManPort variable is protected and updated
        /// only when Port is updated. Usages that dont update port, should use null for this parameter.
        /// </param>
        /// <param name="appName"></param>
        /// <returns></returns>
        internal void ConstructUri(String scheme, String computerName, Nullable<Int32> port, String appName)
        {
            // Default scheme is http
            _scheme = scheme;
            if (string.IsNullOrEmpty(_scheme))
            {
                _scheme = DefaultScheme;
            }

            // Valid values for scheme are "http" and "https"
            if (!(_scheme.Equals(HttpScheme, StringComparison.OrdinalIgnoreCase)
                || _scheme.Equals(HttpsScheme, StringComparison.OrdinalIgnoreCase)
                || _scheme.Equals(DefaultScheme, StringComparison.OrdinalIgnoreCase)))
            {
                String message =
                    PSRemotingErrorInvariants.FormatResourceString(
                        RemotingErrorIdStrings.InvalidSchemeValue, _scheme);
                ArgumentException e = new ArgumentException(message);
                throw e;
            }

            //default host is localhost
            if (String.IsNullOrEmpty(computerName) || String.Equals(computerName, ".", StringComparison.OrdinalIgnoreCase))
            {
                _computerName = DefaultComputerName;
            }
            else
            {
                _computerName = computerName.Trim();

                // According to RFC3513, an Ipv6 address in URI needs to be bracketed.
                IPAddress ipAddress = null;

                bool isIPAddress = IPAddress.TryParse(_computerName, out ipAddress);
                if (isIPAddress && ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    if ((_computerName.Length == 0) || (_computerName[0] != '['))
                    {
                        _computerName = @"[" + _computerName + @"]";
                    }
                }
            }
            PSEtwLog.LogAnalyticVerbose(PSEventId.ComputerName, PSOpcode.Method,
                PSTask.CreateRunspace, PSKeyword.Runspace | PSKeyword.UseAlwaysAnalytic,
                _computerName);

            if (port.HasValue)
            {
                // resolve to default ports if required
                if (port.Value == DefaultPort)
                {
                    // this is needed so that the OriginalString on 
                    // connection uri is fine
                    PortSetting = -1;
                    UseDefaultWSManPort = true;
                }
                else if (port.Value == DefaultPortHttp || port.Value == DefaultPortHttps)
                {
                    PortSetting = port.Value;
                    UseDefaultWSManPort = false;
                }
                else if ((port.Value < MinPort || port.Value > MaxPort))
                {
                    String message =
                        PSRemotingErrorInvariants.FormatResourceString(
                            RemotingErrorIdStrings.PortIsOutOfRange, port);
                    ArgumentException e = new ArgumentException(message);
                    throw e;
                }
                else
                {
                    PortSetting = port.Value;
                    UseDefaultWSManPort = false;
                }
            }

            // default appname is WSMan
            _appName = appName;
            if (String.IsNullOrEmpty(_appName))
            {
                _appName = s_defaultAppName;
            }

            // construct Uri
            UriBuilder uriBuilder = new UriBuilder(_scheme, _computerName,
                                PortSetting, _appName);

            _connectionUri = uriBuilder.Uri;
        }

        /// <summary>
        /// returns connection string without the scheme portion.
        /// </summary>
        /// <param name="connectionUri">
        /// The uri from which the string will be extracted
        /// </param>
        /// <param name="isSSLSpecified">
        /// returns true if https scheme is specified
        /// </param>
        /// <returns>
        /// returns connection string without the scheme portion.
        /// </returns>
        internal static string GetConnectionString(Uri connectionUri,
            out bool isSSLSpecified)
        {
            isSSLSpecified =
                connectionUri.Scheme.Equals(WSManConnectionInfo.HttpsScheme);
            string result = connectionUri.OriginalString.TrimStart();
            if (isSSLSpecified)
            {
                return result.Substring(WSManConnectionInfo.HttpsScheme.Length + 3);
            }
            else
            {
                return result.Substring(WSManConnectionInfo.HttpScheme.Length + 3);
            }
        }

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
        private void ValidateSpecifiedAuthentication()
        {
            if ((WSManAuthenticationMechanism != WSManAuthenticationMechanism.WSMAN_FLAG_DEFAULT_AUTHENTICATION)
                && (_thumbPrint != null))
            {
                throw PSTraceSource.NewInvalidOperationException(RemotingErrorIdStrings.NewRunspaceAmbiguosAuthentication,
                      "CertificateThumbPrint", this.AuthenticationMechanism.ToString());
            }
        }

        private void UpdateUri(Uri uri)
        {
            if (!uri.IsAbsoluteUri)
            {
                throw new NotSupportedException(PSRemotingErrorInvariants.FormatResourceString
                                                    (RemotingErrorIdStrings.RelativeUriForRunspacePathNotSupported));
            }

            if (uri.OriginalString.LastIndexOf(":", StringComparison.OrdinalIgnoreCase) >
                uri.AbsoluteUri.IndexOf("//", StringComparison.OrdinalIgnoreCase))
            {
                UseDefaultWSManPort = false;
            }

            // This check is needed to make sure we connect to WSMan app in the 
            // default case (when user did not specify any appname) like
            // http://localhost , http://127.0.0.1 etc.
            string appname;

            if (uri.AbsolutePath.Equals("/", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment))
            {
                appname = s_defaultAppName;
                ConstructUri(uri.Scheme,
                                uri.Host,
                                uri.Port,
                                appname);
            }
            else
            {
                _connectionUri = uri;
                _scheme = uri.Scheme;
                _appName = uri.AbsolutePath;
                PortSetting = uri.Port;
                _computerName = uri.Host;
                UseDefaultWSManPort = false;
            }
        }

        #endregion Private Methods

        #region Private Members

        private string _scheme = HttpScheme;
        private string _computerName = DefaultComputerName;
        private string _appName = s_defaultAppName;
        private Uri _connectionUri = new Uri(LocalHostUriString);          // uri of this connection
        private PSCredential _credential;    // credentials to be used for this connection
        private string _shellUri = DefaultShellUri;            // shell thats specified by the user
        private string _thumbPrint;
        private AuthenticationMechanism _proxyAuthentication;
        private PSCredential _proxyCredential;

        #endregion Private Members

        #region constants

        /// <summary>
        /// Default disconnected server output mode is set to None.  This mode allows the 
        /// server to set the buffering mode to Block for new sessions and retain its 
        /// current mode during disconnect/connect operations.
        /// </summary>
        internal const OutputBufferingMode DefaultOutputBufferingMode = OutputBufferingMode.None;

        /// <summary>
        /// Default maximum connection retry count.
        /// </summary>
        internal const int DefaultMaxConnectionRetryCount = 5;

#if NOT_APPLY_PORT_DCR
        private static string DEFAULT_SCHEME = HTTP_SCHEME;
        internal static string DEFAULT_SSL_SCHEME = HTTPS_SCHEME;
        private static String DEFAULT_APP_NAME = "wsman";
        /// <summary>
        /// See below for explanation.
        /// </summary>
        internal bool UseDefaultWSManPort
        {
            get { return false; }
            set { }
        }
#else
        private const string DefaultScheme = HttpScheme;
        private const string DefaultSslScheme = HttpsScheme;
        /// <summary>
        /// Default appname. This is empty as WSMan configuration has support 
        /// for this. Look at
        /// get-item WSMan:\localhost\Client\URLPrefix
        /// </summary>
        private static readonly String s_defaultAppName = "/wsman";

        /// <summary>
        /// Default scheme.
        /// As part of port DCR, WSMan changed the default ports
        /// from 80,443 to 5985,5986 respectively no-SSL,SSL 
        /// connections. Since the standards say http,https use
        /// 80,443 as defaults..we came up with new mechanism
        /// to specify scheme as empty. For SSL, WSMan introduced
        /// a new SessionOption. In order to make scheme empty
        /// in the connection string passed to WSMan, we use
        /// this internal boolean.
        /// </summary>
        internal bool UseDefaultWSManPort { get; set; }

#endif

        /// <summary>
        /// default port for http scheme
        /// </summary>
        private const int DefaultPortHttp = 80;

        /// <summary>
        /// default port for https scheme
        /// </summary>
        private const int DefaultPortHttps = 443;

        /// <summary>
        /// This is the default port value which when specified
        /// results in the default port for the scheme to be
        /// assumed
        /// </summary>
        private const int DefaultPort = 0;

        /// <summary>
        /// default remote host name
        /// </summary>
        private const string DefaultComputerName = "localhost";

        /// <summary>
        /// Maximum value for port
        /// </summary>
        private const int MaxPort = 0xFFFF;

        /// <summary>
        /// Minimum value for port
        /// </summary>
        private const int MinPort = 0;

        /// <summary>
        /// String that represents the local host Uri
        /// </summary>
        private const String LocalHostUriString = "http://localhost/wsman";

        /// <summary>
        /// Default value for shell
        /// </summary>
        private const String DefaultShellUri =
             System.Management.Automation.Remoting.Client.WSManNativeApi.ResourceURIPrefix + RemotingConstants.DefaultShellName;

        /// <summary>
        /// Default credentials - null indicates credentials of
        /// current user
        /// </summary>
        private const PSCredential DefaultCredential = null;

        #endregion constants

        #region Internal members

        /// <summary>
        /// Helper property that returns true when the connection has EnableNetworkAccess set
        /// and the connection is localhost (loopback), i.e., not a network connection.
        /// </summary>
        /// <returns></returns>
        internal bool IsLocalhostAndNetworkAccess
        {
            get
            {
                return (EnableNetworkAccess &&                                                              // Interactive token requested
                        (Credential == null &&                                                              // No credential provided
                         (ComputerName.Equals(DefaultComputerName, StringComparison.OrdinalIgnoreCase) ||   // Localhost computer name
                          ComputerName.IndexOf('.') == -1)));                                               // Not FQDN computer name
            }
        }

        /// <summary>
        /// DisconnectedOn property applies to disconnnected runspaces.
        /// This property is publicly exposed only through Runspace class.
        /// </summary>
        internal DateTime? DisconnectedOn
        {
            get;
            set;
        }

        /// <summary>
        /// ExpiresOn property applies to disconnnected runspaces.
        /// This property is publicly exposed only through Runspace class.
        /// </summary>
        internal DateTime? ExpiresOn
        {
            get;
            set;
        }

        /// <summary>
        /// Helper method to reset DisconnectedOn/ExpiresOn properties to null.
        /// </summary>
        internal void NullDisconnectedExpiresOn()
        {
            this.DisconnectedOn = null;
            this.ExpiresOn = null;
        }

        /// <summary>
        /// Helper method to set the DisconnectedOn/ExpiresOn properties based
        /// on current date/time and session idletimeout value.
        /// </summary>
        internal void SetDisconnectedExpiresOnToNow()
        {
            TimeSpan idleTimeoutTime = TimeSpan.FromSeconds(this.IdleTimeout / 1000);
            DateTime now = DateTime.Now;
            this.DisconnectedOn = now;
            this.ExpiresOn = now.Add(idleTimeoutTime);
        }

        #endregion Internal members

        #region V3 Extensions

        private const String DefaultM3PShellName = "Microsoft.PowerShell.Workflow";
        private const String DefaultM3PEndpoint = Remoting.Client.WSManNativeApi.ResourceURIPrefix + DefaultM3PShellName;

        /// <summary>
        /// Constructor that constructs the configuration name from its type
        /// </summary>
        /// <param name="configurationType">type of configuration to construct</param>
        public WSManConnectionInfo(PSSessionType configurationType) : this()
        {
            ComputerName = string.Empty;
            switch (configurationType)
            {
                case PSSessionType.DefaultRemoteShell:
                    {
                        // it is already the default
                    }
                    break;

                case PSSessionType.Workflow:
                    {
                        ShellUri = DefaultM3PEndpoint;
                    }
                    break;
                default:
                    {
                        Diagnostics.Assert(false, "Unknown value for PSSessionType");
                    }
                    break;
            }
        }

        #endregion V3 Extensions
    }

    /// <summary>
    /// Class which is used to create an Out-Of-Process Runspace/RunspacePool.
    /// This does not have a dependency on WSMan. *-Job cmdlets use Out-Of-Proc
    /// Runspaces to support background jobs.
    /// </summary>
    internal sealed class NewProcessConnectionInfo : RunspaceConnectionInfo
    {
        #region Private Data

        private PSCredential _credential;
        private AuthenticationMechanism _authMechanism;

        #endregion

        #region Properties

        /// <summary>
        /// Script to run while starting the background process.
        /// </summary>
        public ScriptBlock InitializationScript { get; set; }

        /// <summary>
        /// On a 64bit machine, specifying true for this will launch a 32 bit process
        /// for the background process.
        /// </summary>
        public bool RunAs32 { get; set; }

        /// <summary>
        /// Powershell version to execute the job in
        /// </summary>
        public Version PSVersion { get; set; }

        internal PowerShellProcessInstance Process { get; set; }

        #endregion

        #region Overrides

        /// <summary>
        /// Name of the computer. Will always be "localhost" to signify local machine.
        /// </summary>
        public override string ComputerName
        {
            get { return "localhost"; }
            set { throw new NotImplementedException(); }
        }

        /// <summary>
        /// Credential used for the connection
        /// </summary>
        public override PSCredential Credential
        {
            get { return _credential; }
            set
            {
                _credential = value;
                _authMechanism = AuthenticationMechanism.Default;
            }
        }

        /// <summary>
        /// Authentication mechanism to use while connecting to the server.
        /// Only Default is supported.
        /// </summary>
        public override AuthenticationMechanism AuthenticationMechanism
        {
            get
            {
                return _authMechanism;
            }
            set
            {
                if (value != AuthenticationMechanism.Default)
                {
                    throw PSTraceSource.NewInvalidOperationException(RemotingErrorIdStrings.IPCSupportsOnlyDefaultAuth,
                        value.ToString(), AuthenticationMechanism.Default.ToString());
                }

                _authMechanism = value;
            }
        }

        /// <summary>
        /// ThumbPrint of a certificate used for connecting to a remote machine.
        /// When this is specified, you dont need to supply credential and authentication
        /// mechanism.
        /// Will always be empty to signify that this is not supported.
        /// </summary>
        public override string CertificateThumbprint
        {
            get { return string.Empty; }
            set { throw new NotImplementedException(); }
        }

        public NewProcessConnectionInfo Copy()
        {
            NewProcessConnectionInfo result = new NewProcessConnectionInfo(_credential);
            result.AuthenticationMechanism = this.AuthenticationMechanism;
            result.InitializationScript = this.InitializationScript;
            result.RunAs32 = this.RunAs32;
            result.PSVersion = this.PSVersion;
            result.Process = Process;
            return result;
        }

        internal override RunspaceConnectionInfo InternalCopy()
        {
            return Copy();
        }

        internal override BaseClientSessionTransportManager CreateClientSessionTransportManager(Guid instanceId, string sessionName, PSRemotingCryptoHelper cryptoHelper)
        {
            return new OutOfProcessClientSessionTransportManager(
                instanceId,
                this,
                cryptoHelper);
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a connection info instance used to create a runspace on a different
        /// process on the local machine
        /// </summary>
        internal NewProcessConnectionInfo(PSCredential credential)
        {
            _credential = credential;
            _authMechanism = AuthenticationMechanism.Default;
        }

        #endregion
    }

    /// <summary>
    /// Class used to create an Out-Of-Process Runspace/RunspacePool between
    /// two local processes using a named pipe for IPC.
    /// This class does not have a dependency on WSMan and is used to implement
    /// the PowerShell attach-to-process feature.
    /// </summary>
    public sealed class NamedPipeConnectionInfo : RunspaceConnectionInfo
    {
        #region Private Data

        private PSCredential _credential;
        private AuthenticationMechanism _authMechanism;
        private string _appDomainName = string.Empty;
        private const int _defaultOpenTimeout = 60000;      /* 60 seconds. */

        #endregion

        #region Properties

        /// <summary>
        /// Process Id of process to attach to.
        /// </summary>
        public int ProcessId
        {
            get;
            set;
        }

        /// <summary>
        /// Optional application domain name.  If not specified then the 
        /// default application domain is used.
        /// </summary>
        public string AppDomainName
        {
            get { return _appDomainName; }
            set
            {
                _appDomainName = value ?? string.Empty;
            }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Constructor
        /// </summary>
        public NamedPipeConnectionInfo()
        {
            OpenTimeout = _defaultOpenTimeout;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="processId">Process Id to connect to.</param>
        public NamedPipeConnectionInfo(
            int processId) :
            this(processId, string.Empty, _defaultOpenTimeout)
        { }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="processId">Process Id to connect to</param>
        /// <param name="appDomainName">Application domain name to connect to, or default AppDomain if blank.</param>
        public NamedPipeConnectionInfo(
            int processId,
            string appDomainName) :
            this(processId, appDomainName, _defaultOpenTimeout)
        { }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="processId">Process Id to connect to</param>
        /// <param name="appDomainName">Name of application domain to connect to.  Connection is to default application domain if blank.</param>
        /// <param name="openTimeout">Open time out in Milliseconds</param>
        public NamedPipeConnectionInfo(
            int processId,
            string appDomainName,
            int openTimeout)
        {
            ProcessId = processId;
            AppDomainName = appDomainName;
            OpenTimeout = openTimeout;
        }

        #endregion

        #region Overrides

        /// <summary>
        /// Computer is always localhost.
        /// </summary>
        public override string ComputerName
        {
            get { return "localhost"; }
            set { throw new NotImplementedException(); }
        }

        /// <summary>
        /// Credential
        /// </summary>
        public override PSCredential Credential
        {
            get { return _credential; }

            set
            {
                _credential = value;
                _authMechanism = Runspaces.AuthenticationMechanism.Default;
            }
        }

        /// <summary>
        /// Authentication
        /// </summary>
        public override AuthenticationMechanism AuthenticationMechanism
        {
            get
            {
                return _authMechanism;
            }
            set
            {
                if (value != Runspaces.AuthenticationMechanism.Default)
                {
                    throw PSTraceSource.NewInvalidOperationException(RemotingErrorIdStrings.IPCSupportsOnlyDefaultAuth,
                        value.ToString(), AuthenticationMechanism.Default.ToString());
                }

                _authMechanism = value;
            }
        }

        /// <summary>
        /// CertificateThumbprint
        /// </summary>
        public override string CertificateThumbprint
        {
            get { return string.Empty; }
            set { throw new NotImplementedException(); }
        }

        /// <summary>
        /// Shallow copy of current instance.
        /// </summary>
        /// <returns>NamedPipeConnectionInfo</returns>
        internal override RunspaceConnectionInfo InternalCopy()
        {
            NamedPipeConnectionInfo newCopy = new NamedPipeConnectionInfo();
            newCopy._authMechanism = this.AuthenticationMechanism;
            newCopy._credential = this.Credential;
            newCopy.ProcessId = this.ProcessId;
            newCopy._appDomainName = _appDomainName;
            newCopy.OpenTimeout = this.OpenTimeout;

            return newCopy;
        }

        internal override BaseClientSessionTransportManager CreateClientSessionTransportManager(Guid instanceId, string sessionName, PSRemotingCryptoHelper cryptoHelper)
        {
            return new NamedPipeClientSessionTransportManager(
                this,
                instanceId,
                cryptoHelper);
        }

        #endregion
    }

    /// <summary>
    /// Class used to create a connection through an SSH.exe client to a remote host machine.
    /// Connection information includes SSH target (user name and host machine) along with
    /// client key used for key based user authorization.
    /// </summary>
    public sealed class SSHConnectionInfo : RunspaceConnectionInfo
    {
        #region Properties

        /// <summary>
        /// User Name
        /// </summary>
        public string UserName
        {
            get;
            private set;
        }

        /// <summary>
        /// Key Path
        /// </summary>
        private string KeyPath
        {
            get;
            set;
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Constructor
        /// </summary>
        private SSHConnectionInfo()
        { }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="userName">User Name</param>
        /// <param name="computerName">Computer Name</param>
        /// <param name="keyPath">Key Path</param>
        public SSHConnectionInfo(
            string userName,
            string computerName,
            string keyPath)
        {
            if (userName == null) { throw new PSArgumentNullException("userName"); }
            if (computerName == null) { throw new PSArgumentNullException("computerName"); }

            this.UserName = userName;
            this.ComputerName = computerName;
            this.KeyPath = keyPath;
        }

        #endregion

        #region Overrides

        /// <summary>
        /// Computer is always localhost.
        /// </summary>
        public override string ComputerName
        {
            get;
            set;
        }

        /// <summary>
        /// Credential
        /// </summary>
        public override PSCredential Credential
        {
            get { return null; }
            set { throw new NotImplementedException(); }
        }

        /// <summary>
        /// Authentication
        /// </summary>
        public override AuthenticationMechanism AuthenticationMechanism
        {
            get { return AuthenticationMechanism.Default; }
            set { throw new NotImplementedException(); }
        }

        /// <summary>
        /// CertificateThumbprint
        /// </summary>
        public override string CertificateThumbprint
        {
            get { return string.Empty; }
            set { throw new NotImplementedException(); }
        }

        /// <summary>
        /// Shallow copy of current instance.
        /// </summary>
        /// <returns>NamedPipeConnectionInfo</returns>
        internal override RunspaceConnectionInfo InternalCopy()
        {
            SSHConnectionInfo newCopy = new SSHConnectionInfo();
            newCopy.ComputerName = this.ComputerName;
            newCopy.UserName = this.UserName;
            newCopy.KeyPath = this.KeyPath;

            return newCopy;
        }

        /// <summary>
        /// CreateClientSessionTransportManager
        /// </summary>
        /// <param name="instanceId"></param>
        /// <param name="sessionName"></param>
        /// <param name="cryptoHelper"></param>
        /// <returns></returns>
        internal override BaseClientSessionTransportManager CreateClientSessionTransportManager(Guid instanceId, string sessionName, PSRemotingCryptoHelper cryptoHelper)
        {
            return new SSHClientSessionTransportManager(
                this,
                instanceId,
                cryptoHelper);
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// StartSSHProcess
        /// </summary>
        /// <returns></returns>
        internal System.Diagnostics.Process StartSSHProcess(
            out StreamWriter stdInWriterVar,
            out StreamReader stdOutReaderVar,
            out StreamReader stdErrReaderVar)
        {
            string filePath = string.Empty;
#if !UNIX
            var context = Runspaces.LocalPipeline.GetExecutionContextFromTLS();
            if (context != null)
            {
                var cmdInfo = context.CommandDiscovery.LookupCommandInfo("ssh.exe", CommandOrigin.Internal) as ApplicationInfo;
                if (cmdInfo != null)
                {
                    filePath = cmdInfo.Path;
                }
            }
#else
            filePath = @"ssh";
#endif

            // Extract an optional domain name if provided.
            string domainName = null;
            string userName = this.UserName;
#if !UNIX
            var parts = this.UserName.Split(Utils.Separators.Backslash);
            if (parts.Length == 2)
            {
                domainName = parts[0];
                userName = parts[1];
            }
#endif

            // Create client ssh process that hosts powershell.exe as a subsystem and is configured
            // to be in server mode for PSRP over SSHD:
            //   powershell -Version 5.1 -sshs -NoLogo -NoProfile
            //   See sshd_configuration file, subsystems section and it will have this entry:
            //     Subsystem       powershell C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -Version 5.1 -sshs -NoLogo -NoProfile
            string arguments;
            if (!string.IsNullOrEmpty(this.KeyPath))
            {
                arguments = (string.IsNullOrEmpty(domainName)) ?
                    string.Format(CultureInfo.InvariantCulture, @"-i ""{0}"" {1}@{2} -s powershell", this.KeyPath, userName, this.ComputerName) :
                    string.Format(CultureInfo.InvariantCulture, @"-i ""{0}"" -l {1}@{2} {3} -s powershell", this.KeyPath, userName, domainName, this.ComputerName);
            }
            else
            {
                arguments = (string.IsNullOrEmpty(domainName)) ?
                    string.Format(CultureInfo.InvariantCulture, @"{0}@{1} -s powershell", userName, this.ComputerName) :
                    string.Format(CultureInfo.InvariantCulture, @"-l {0}@{1} {2} -s powershell", userName, domainName, this.ComputerName);
            }

            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo(
                filePath,
                arguments);
            startInfo.WorkingDirectory = System.IO.Path.GetDirectoryName(filePath);
            startInfo.CreateNoWindow = true;
            startInfo.UseShellExecute = false;

            return StartSSHProcessImpl(startInfo, out stdInWriterVar, out stdOutReaderVar, out stdErrReaderVar);
        }

        #endregion

        #region SSH Process Creation

#if UNIX

        /// <summary>
        /// Create a process through managed APIs and return StdIn, StdOut, StdError reader/writers
        /// This works for non-Windows platforms and is simpler.
        /// </summary>
        private static System.Diagnostics.Process StartSSHProcessImpl(
            System.Diagnostics.ProcessStartInfo startInfo,
            out StreamWriter stdInWriterVar,
            out StreamReader stdOutReaderVar,
            out StreamReader stdErrReaderVar)
        {
            startInfo.RedirectStandardInput = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            System.Diagnostics.Process process = new Process();
            process.StartInfo = startInfo;

            process.Start();

            stdInWriterVar = process.StandardInput;
            stdOutReaderVar = process.StandardOutput;
            stdErrReaderVar = process.StandardError;

            return process;
        }

#else

        /// <summary>
        /// Create a process through native Win32 APIs and return StdIn, StdOut, StdError reader/writers
        /// This needs to be done via Win32 APIs because managed code creates anonymous synchronous pipes 
        /// for redirected StdIn/Out and SSH (and PSRP) require asynchronous (overlapped) pipes, which must
        /// be through named pipes.  Managed code for named pipes is unreliable and so this is done via
        /// P-Invoking native APIs.
        /// </summary>
        private static System.Diagnostics.Process StartSSHProcessImpl(
            System.Diagnostics.ProcessStartInfo startInfo,
            out StreamWriter stdInWriterVar,
            out StreamReader stdOutReaderVar,
            out StreamReader stdErrReaderVar)
        {
            Exception ex = null;
            System.Diagnostics.Process sshProcess = null;
            //
            // These std pipe handles are bound to managed Reader/Writer objects and returned to the transport
            // manager object, which uses them for PSRP communication.  The lifetime of these handles are then
            // tied to the reader/writer objects which the transport is responsible for disposing (see 
            // SSHClientSessionTransportManger and the CloseConnection() method.
            //
            SafePipeHandle stdInPipeServer = null;
            SafePipeHandle stdOutPipeServer = null;
            SafePipeHandle stdErrPipeServer = null;
            try
            {
                sshProcess = CreateProcessWithRedirectedStd(
                    startInfo,
                    out stdInPipeServer,
                    out stdOutPipeServer,
                    out stdErrPipeServer);
            }
            catch (InvalidOperationException e) { ex = e; }
            catch (ArgumentException e) { ex = e; }
            catch (FileNotFoundException e) { ex = e; }
            catch (System.ComponentModel.Win32Exception e) { ex = e; }

            if ((ex != null) ||
                (sshProcess == null) ||
                (sshProcess.HasExited == true))
            {
                throw new InvalidOperationException(RemotingErrorIdStrings.CannotStartSSHClient, ex);
            }

            // Create the std in writer/readers needed for communication with ssh.exe.
            stdInWriterVar = null;
            stdOutReaderVar = null;
            stdErrReaderVar = null;
            try
            {
                stdInWriterVar = new StreamWriter(new NamedPipeServerStream(PipeDirection.Out, true, true, stdInPipeServer));
                stdOutReaderVar = new StreamReader(new NamedPipeServerStream(PipeDirection.In, true, true, stdOutPipeServer));
                stdErrReaderVar = new StreamReader(new NamedPipeServerStream(PipeDirection.In, true, true, stdErrPipeServer));
            }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                if (stdInWriterVar != null) { stdInWriterVar.Dispose(); } else { stdInPipeServer.Dispose(); }
                if (stdOutReaderVar != null) { stdInWriterVar.Dispose(); } else { stdOutPipeServer.Dispose(); }
                if (stdErrReaderVar != null) { stdInWriterVar.Dispose(); } else { stdErrPipeServer.Dispose(); }

                throw;
            }

            return sshProcess;
        }

        /// <summary>
        /// CreateProcessWithRedirectedStd
        /// </summary>
        private static Process CreateProcessWithRedirectedStd(
            ProcessStartInfo startInfo,
            out SafePipeHandle stdInPipeServer,
            out SafePipeHandle stdOutPipeServer,
            out SafePipeHandle stdErrPipeServer)
        {
            //
            // Create named (async) pipes for reading/writing to std.
            //
            stdInPipeServer = null;
            stdOutPipeServer = null;
            stdErrPipeServer = null;
            SafePipeHandle stdInPipeClient = null;
            SafePipeHandle stdOutPipeClient = null;
            SafePipeHandle stdErrPipeClient = null;
            string randomName = System.IO.Path.GetFileNameWithoutExtension(System.IO.Path.GetRandomFileName());

            try
            {
                // Get default pipe security (Admin and current user access)
                var securityDesc = RemoteSessionNamedPipeServer.GetServerPipeSecurity();

                var stdInPipeName = @"\\.\pipe\StdIn" + randomName;
                stdInPipeServer = CreateNamedPipe(stdInPipeName, securityDesc);
                stdInPipeClient = GetNamedPipeHandle(stdInPipeName);

                var stdOutPipeName = @"\\.\pipe\StdOut" + randomName;
                stdOutPipeServer = CreateNamedPipe(stdOutPipeName, securityDesc);
                stdOutPipeClient = GetNamedPipeHandle(stdOutPipeName);

                var stdErrPipeName = @"\\.\pipe\StdErr" + randomName;
                stdErrPipeServer = CreateNamedPipe(stdErrPipeName, securityDesc);
                stdErrPipeClient = GetNamedPipeHandle(stdErrPipeName);
            }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);

                if (stdInPipeServer != null) { stdInPipeServer.Dispose(); }
                if (stdInPipeClient != null) { stdInPipeClient.Dispose(); }
                if (stdOutPipeServer != null) { stdOutPipeServer.Dispose(); }
                if (stdOutPipeClient != null) { stdOutPipeClient.Dispose(); }
                if (stdErrPipeServer != null) { stdErrPipeServer.Dispose(); }
                if (stdErrPipeClient != null) { stdErrPipeClient.Dispose(); }

                throw;
            }

            // Create process
            PlatformInvokes.STARTUPINFO lpStartupInfo = new PlatformInvokes.STARTUPINFO();
            PlatformInvokes.PROCESS_INFORMATION lpProcessInformation = new PlatformInvokes.PROCESS_INFORMATION();
            int creationFlags = 0;

            try
            {
                var cmdLine = String.Format(CultureInfo.InvariantCulture, @"""{0}"" {1}", startInfo.FileName, startInfo.Arguments);

                lpStartupInfo.hStdInput = new SafeFileHandle(stdInPipeClient.DangerousGetHandle(), false);
                lpStartupInfo.hStdOutput = new SafeFileHandle(stdOutPipeClient.DangerousGetHandle(), false);
                lpStartupInfo.hStdError = new SafeFileHandle(stdErrPipeClient.DangerousGetHandle(), false);
                lpStartupInfo.dwFlags = 0x100;

                // No new window: Inherit the parent process's console window
                creationFlags = 0x00000000;

                // Create the new process suspended so we have a chance to get a corresponding Process object in case it terminates quickly.
                creationFlags |= 0x00000004;

                PlatformInvokes.SECURITY_ATTRIBUTES lpProcessAttributes = new PlatformInvokes.SECURITY_ATTRIBUTES();
                PlatformInvokes.SECURITY_ATTRIBUTES lpThreadAttributes = new PlatformInvokes.SECURITY_ATTRIBUTES();
                bool success = PlatformInvokes.CreateProcess(
                    null,
                    cmdLine,
                    lpProcessAttributes,
                    lpThreadAttributes,
                    true,
                    creationFlags,
                    IntPtr.Zero,
                    startInfo.WorkingDirectory,
                    lpStartupInfo,
                    lpProcessInformation);

                if (!success)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                // At this point, we should have a suspended process.  Get the .Net Process object, resume the process, and return.
                Process result = Process.GetProcessById(lpProcessInformation.dwProcessId);
                PlatformInvokes.ResumeThread(lpProcessInformation.hThread);

                return result;
            }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                if (stdInPipeServer != null) { stdInPipeServer.Dispose(); }
                if (stdInPipeClient != null) { stdInPipeClient.Dispose(); }
                if (stdOutPipeServer != null) { stdOutPipeServer.Dispose(); }
                if (stdOutPipeClient != null) { stdOutPipeClient.Dispose(); }
                if (stdErrPipeServer != null) { stdErrPipeServer.Dispose(); }
                if (stdErrPipeClient != null) { stdErrPipeClient.Dispose(); }

                throw;
            }
            finally
            {
                lpProcessInformation.Dispose();
            }
        }

        private static SafePipeHandle GetNamedPipeHandle(string pipeName)
        {
            // Create pipe flags for asynchronous pipes.
            uint pipeFlags = NamedPipeNative.FILE_FLAG_OVERLAPPED;

            // We want an inheritable handle.
            PlatformInvokes.SECURITY_ATTRIBUTES securityAttributes = new PlatformInvokes.SECURITY_ATTRIBUTES();

            // Get handle to pipe.
            var fileHandle = PlatformInvokes.CreateFileW(
                pipeName,
                NamedPipeNative.GENERIC_READ | NamedPipeNative.GENERIC_WRITE,
                0,
                securityAttributes,
                NamedPipeNative.OPEN_EXISTING,
                pipeFlags,
                IntPtr.Zero);

            int lastError = Marshal.GetLastWin32Error();
            if (fileHandle == PlatformInvokes.INVALID_HANDLE_VALUE)
            {
                throw new System.ComponentModel.Win32Exception(lastError);
            }

            return new SafePipeHandle(fileHandle, true);
        }

        private static SafePipeHandle CreateNamedPipe(
            string pipeName,
            CommonSecurityDescriptor securityDesc)
        {
            // Create optional security attributes based on provided PipeSecurity.
            NamedPipeNative.SECURITY_ATTRIBUTES securityAttributes = null;
            GCHandle? securityDescHandle = null;
            if (securityDesc != null)
            {
                byte[] securityDescBuffer = new byte[securityDesc.BinaryLength];
                securityDesc.GetBinaryForm(securityDescBuffer, 0);
                securityDescHandle = GCHandle.Alloc(securityDescBuffer, GCHandleType.Pinned);
                securityAttributes = NamedPipeNative.GetSecurityAttributes(securityDescHandle.Value, true); ;
            }

            // Create async named pipe.
            SafePipeHandle pipeHandle = NamedPipeNative.CreateNamedPipe(
                pipeName,
                NamedPipeNative.PIPE_ACCESS_DUPLEX | NamedPipeNative.FILE_FLAG_FIRST_PIPE_INSTANCE | NamedPipeNative.FILE_FLAG_OVERLAPPED,
                NamedPipeNative.PIPE_TYPE_MESSAGE | NamedPipeNative.PIPE_READMODE_MESSAGE,
                1,
                32768,
                32768,
                0,
                securityAttributes);

            int lastError = Marshal.GetLastWin32Error();
            if (securityDescHandle != null)
            {
                securityDescHandle.Value.Free();
            }

            if (pipeHandle.IsInvalid)
            {
                throw new Win32Exception(lastError);
            }

            return pipeHandle;
        }

#endif

        #endregion
    }

    /// <summary>
    /// The class that contains connection information for a remote session between a local host
    /// and VM. The local host can be a VM in nested scenario.
    /// </summary>
    public sealed class VMConnectionInfo : RunspaceConnectionInfo
    {
        #region Private Data

        private AuthenticationMechanism _authMechanism;
        private PSCredential _credential;
        private const int _defaultOpenTimeout = 20000; /* 20 seconds. */

        #endregion

        #region Properties

        /// <summary>
        /// GUID of the target VM.
        /// </summary>
        public Guid VMGuid { get; set; }

        /// <summary>
        /// Configuration name of the VM session.
        /// </summary>
        public string ConfigurationName { get; set; }

        #endregion

        #region Overrides

        /// <summary>
        /// Authentication mechanism to use while connecting to the server.
        /// Only Default is supported.
        /// </summary>
        public override AuthenticationMechanism AuthenticationMechanism
        {
            get
            {
                return _authMechanism;
            }
            set
            {
                if (value != AuthenticationMechanism.Default)
                {
                    throw PSTraceSource.NewInvalidOperationException(RemotingErrorIdStrings.IPCSupportsOnlyDefaultAuth,
                        value.ToString(), AuthenticationMechanism.Default.ToString());
                }

                _authMechanism = value;
            }
        }

        /// <summary>
        /// ThumbPrint of a certificate used for connecting to a remote machine.
        /// When this is specified, you dont need to supply credential and authentication
        /// mechanism.
        /// Will always be null to signify that this is not supported.
        /// </summary>
        public override string CertificateThumbprint
        {
            get { return null; }
            set { throw new NotImplementedException(); }
        }

        /// <summary>
        /// Credential used for the connection.
        /// </summary>
        public override PSCredential Credential
        {
            get { return _credential; }
            set
            {
                _credential = value;
                _authMechanism = AuthenticationMechanism.Default;
            }
        }

        /// <summary>
        /// Name of the target VM.
        /// </summary>
        public override string ComputerName { get; set; }

        internal override RunspaceConnectionInfo InternalCopy()
        {
            VMConnectionInfo result = new VMConnectionInfo(Credential, VMGuid, ComputerName, ConfigurationName);
            return result;
        }

        internal override BaseClientSessionTransportManager CreateClientSessionTransportManager(Guid instanceId, string sessionName, PSRemotingCryptoHelper cryptoHelper)
        {
            return new VMHyperVSocketClientSessionTransportManager(
                this,
                instanceId,
                cryptoHelper,
                VMGuid,
                ConfigurationName);
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a connection info instance used to create a runspace on target VM.
        /// </summary>
        internal VMConnectionInfo(
            PSCredential credential,
            Guid vmGuid,
            string vmName,
            string configurationName)
            : base()
        {
            Credential = credential;
            VMGuid = vmGuid;
            ComputerName = vmName;
            ConfigurationName = configurationName;

            AuthenticationMechanism = AuthenticationMechanism.Default;
            OpenTimeout = _defaultOpenTimeout;
        }

        #endregion
    }

    /// <summary>
    /// The class that contains connection information for a remote session between a local
    /// container host and container.
    /// For Windows Server container, the transport is based on named pipe for now.
    /// For Hyper-V container, the transport is based on Hyper-V socket.
    /// </summary>
    public sealed class ContainerConnectionInfo : RunspaceConnectionInfo
    {
        #region Private Data

        private AuthenticationMechanism _authMechanism;
        private PSCredential _credential;
        private const int _defaultOpenTimeout = 20000; /* 20 seconds. */

        #endregion

        #region Properties

        /// <summary>
        /// ContainerProcess class instance.
        /// </summary>
        internal ContainerProcess ContainerProc { get; set; }

        #endregion

        #region Overrides

        /// <summary>
        /// Authentication mechanism to use while connecting to the server.
        /// Only Default is supported.
        /// </summary>
        public override AuthenticationMechanism AuthenticationMechanism
        {
            get
            {
                return _authMechanism;
            }
            set
            {
                if (value != AuthenticationMechanism.Default)
                {
                    throw PSTraceSource.NewInvalidOperationException(RemotingErrorIdStrings.IPCSupportsOnlyDefaultAuth,
                        value.ToString(), AuthenticationMechanism.Default.ToString());
                }

                _authMechanism = value;
            }
        }

        /// <summary>
        /// ThumbPrint of a certificate used for connecting to a remote machine.
        /// When this is specified, you dont need to supply credential and authentication
        /// mechanism.
        /// Will always be null to signify that this is not supported.
        /// </summary>
        public override string CertificateThumbprint
        {
            get { return null; }
            set { throw new NotImplementedException(); }
        }

        /// <summary>
        /// Credential used for the connection.
        /// </summary>
        public override PSCredential Credential
        {
            get { return _credential; }
            set
            {
                _credential = value;
                _authMechanism = Runspaces.AuthenticationMechanism.Default;
            }
        }

        /// <summary>
        /// Name of the target container.
        /// </summary>
        public override string ComputerName
        {
            get { return ContainerProc.ContainerId; }
            set { throw new PSNotSupportedException(); }
        }

        internal override RunspaceConnectionInfo InternalCopy()
        {
            ContainerConnectionInfo newCopy = new ContainerConnectionInfo(ContainerProc);
            return newCopy;
        }

        internal override BaseClientSessionTransportManager CreateClientSessionTransportManager(Guid instanceId, string sessionName, PSRemotingCryptoHelper cryptoHelper)
        {
            if (ContainerProc.RuntimeId != Guid.Empty)
            {
                return new ContainerHyperVSocketClientSessionTransportManager(
                    this,
                    instanceId,
                    cryptoHelper,
                    ContainerProc.RuntimeId);
            }
            else
            {
                return new ContainerNamedPipeClientSessionTransportManager(
                    this,
                    instanceId,
                    cryptoHelper);
            }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a connection info instance used to create a runspace on target container.
        /// </summary>
        internal ContainerConnectionInfo(
            ContainerProcess containerProc)
            : base()
        {
            ContainerProc = containerProc;

            AuthenticationMechanism = AuthenticationMechanism.Default;
            Credential = null;
            OpenTimeout = _defaultOpenTimeout;
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Create ContainerConnectionInfo object based on container id.
        /// </summary>
        public static ContainerConnectionInfo CreateContainerConnectionInfo(
            string containerId,
            bool runAsAdmin,
            string configurationName)
        {
            ContainerProcess containerProc = new ContainerProcess(containerId, null, 0, runAsAdmin, configurationName);

            return new ContainerConnectionInfo(containerProc);
        }

        /// <summary>
        /// Create process inside container.
        /// </summary>
        public void CreateContainerProcess()
        {
            ContainerProc.CreateContainerProcess();
        }

        /// <summary>
        /// Terminate process inside container.
        /// </summary>
        public bool TerminateContainerProcess()
        {
            return ContainerProc.TerminateContainerProcess();
        }

        #endregion
    }

    /// <summary>
    /// Class used to create/terminate process inside container, which can be either
    /// Windows Server Container or Hyper-V container.
    /// - Windows Server Container does not require Hyper-V.
    /// - Hyper-V container requires Hyper-V and utility VM, which is different from normal VM.
    /// </summary>
    internal class ContainerProcess
    {
        #region Private Data

        private const uint NoError = 0;
        private const uint InvalidContainerId = 1;
        private const uint ContainersFeatureNotEnabled = 2;
        private const uint OtherError = 9999;

        #endregion

        #region Properties

        /// <summary>
        /// For Hyper-V container, it is Guid of utility VM hosting Hyper-V container.
        /// For Windows Server Container, it is empty.
        /// </summary>
        public Guid RuntimeId { get; set; }

        /// <summary>
        /// OB root of the container.
        /// </summary>
        public string ContainerObRoot { get; set; }

        /// <summary>
        /// ID of the container.
        /// </summary>
        public string ContainerId { get; set; }

        /// <summary>
        /// Process ID of the process created in container.
        /// </summary>
        internal int ProcessId { get; set; }

        /// <summary>
        /// Whether the process in container should be launched as high privileged account
        /// (RunAsAdmin being true) or low privileged account (RunAsAdmin being false).
        /// </summary>
        internal bool RunAsAdmin { get; set; } = false;

        /// <summary>
        /// The configuration name of the container session.
        /// </summary>
        internal string ConfigurationName { get; set; }

        /// <summary>
        /// Whether the process in container has terminated.
        /// </summary>
        internal bool ProcessTerminated { get; set; } = false;

        /// <summary>
        /// The error code.
        /// </summary>
        internal uint ErrorCode { get; set; } = 0;

        /// <summary>
        /// The error message for other errors.
        /// </summary>
        internal string ErrorMessage { get; set; } = string.Empty;

        #endregion

        #region Native HCS (i.e., host compute service) methods

        [StructLayout(LayoutKind.Sequential)]
        internal struct HCS_PROCESS_INFORMATION
        {
            /// <summary>
            /// The process id.
            /// </summary>
            public uint ProcessId;

            /// <summary>
            /// Reserved.
            /// </summary>
            public uint Reserved;

            /// <summary>
            /// If created, standard input handle of the process.
            /// </summary>
            public IntPtr StdInput;

            /// <summary>
            /// If created, standard output handle of the process.
            /// </summary>
            public IntPtr StdOutput;

            /// <summary>
            /// If created, standard error handle of the process.
            /// </summary>
            public IntPtr StdError;
        }

        [DllImport(PinvokeDllNames.CreateProcessInComputeSystemDllName, SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern uint HcsOpenComputeSystem(
            string id,
            ref IntPtr computeSystem,
            ref string result);

        [DllImport(PinvokeDllNames.CreateProcessInComputeSystemDllName, SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern uint HcsGetComputeSystemProperties(
            IntPtr computeSystem,
            string propertyQuery,
            ref string properties,
            ref string result);

        [DllImport(PinvokeDllNames.CreateProcessInComputeSystemDllName, SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern uint HcsCreateProcess(
            IntPtr computeSystem,
            string processParameters,
            ref HCS_PROCESS_INFORMATION processInformation,
            ref IntPtr process,
            ref string reslt);

        [DllImport(PinvokeDllNames.CreateProcessInComputeSystemDllName, SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern uint HcsOpenProcess(
            IntPtr computeSystem,
            int processId,
            ref IntPtr process,
            ref string result);

        [DllImport(PinvokeDllNames.CreateProcessInComputeSystemDllName, SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern uint HcsTerminateProcess(
            IntPtr process,
            ref string result);

        #endregion

        #region Constructors

        /// <summary>
        /// Creates an instance used for PowerShell Direct for container.
        /// </summary>
        public ContainerProcess(string containerId, string containerObRoot, int processId, bool runAsAdmin, string configurationName)
        {
            this.ContainerId = containerId;
            this.ContainerObRoot = containerObRoot;
            this.ProcessId = processId;
            this.RunAsAdmin = runAsAdmin;
            this.ConfigurationName = configurationName;

            Dbg.Assert(!String.IsNullOrEmpty(containerId), "containerId input cannot be empty.");

            GetContainerProperties();
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Create process inside container.
        /// </summary>
        public void CreateContainerProcess()
        {
            RunOnMTAThread(CreateContainerProcessInternal);

            //
            // Report error. More error reporting will come later.
            //
            switch (ErrorCode)
            {
                case NoError:
                    break;

                case InvalidContainerId:
                    throw new PSInvalidOperationException(StringUtil.Format(RemotingErrorIdStrings.InvalidContainerId,
                                                                            ContainerId));

                case ContainersFeatureNotEnabled:
                    throw new PSInvalidOperationException(StringUtil.Format(RemotingErrorIdStrings.ContainersFeatureNotEnabled));

                // other errors caught with exception
                case OtherError:
                    throw new PSInvalidOperationException(ErrorMessage);

                // other errors caught without exception
                default:
                    throw new PSInvalidOperationException(StringUtil.Format(RemotingErrorIdStrings.CannotCreateProcessInContainer,
                                                                            ContainerId));
            }
        }

        /// <summary>
        /// Terminate process inside container.
        /// </summary>
        public bool TerminateContainerProcess()
        {
            RunOnMTAThread(TerminateContainerProcessInternal);

            return ProcessTerminated;
        }

        /// <summary>
        /// Get object root based on given container id.
        /// </summary>
        public void GetContainerProperties()
        {
            RunOnMTAThread(GetContainerPropertiesInternal);

            //
            // Report error.
            //
            switch (ErrorCode)
            {
                case NoError:
                    break;

                case ContainersFeatureNotEnabled:
                    throw new PSInvalidOperationException(StringUtil.Format(RemotingErrorIdStrings.ContainersFeatureNotEnabled));

                case OtherError:
                    throw new PSInvalidOperationException(ErrorMessage);
            }
        }

        #endregion

        #region Private methods

        /// <summary>
        /// Dynamically load the Host Compute interop assemblies and return useful types.
        /// </summary>
        /// <param name="computeSystemPropertiesType">The Microsoft.HyperV.Schema.Compute.System.Properties type.</param>
        /// <param name="hostComputeInteropType">The Microsoft.HostCompute.Interop.HostComputeInterop type.</param>
        private static void GetHostComputeInteropTypes(out Type computeSystemPropertiesType, out Type hostComputeInteropType)
        {
            Assembly schemaAssembly = Assembly.Load(new AssemblyName("Microsoft.HyperV.Schema, Version=10.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"));
            computeSystemPropertiesType = schemaAssembly.GetType("Microsoft.HyperV.Schema.Compute.System.Properties");

            Assembly hostComputeInteropAssembly = Assembly.Load(new AssemblyName("Microsoft.HostCompute.Interop, Version=10.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"));
            hostComputeInteropType = hostComputeInteropAssembly.GetType("Microsoft.HostCompute.Interop.HostComputeInterop");
        }

        /// <summary>
        /// Create process inside container.
        /// </summary>
        private void CreateContainerProcessInternal()
        {
            uint result;
            string cmd;
            int processId = 0;
            uint error = 0;

            //
            // Check whether the given container id exists.
            //
            try
            {
                IntPtr ComputeSystem = IntPtr.Zero;
                string resultString = String.Empty;

                result = HcsOpenComputeSystem(ContainerId, ref ComputeSystem, ref resultString);
                if (result != 0)
                {
                    processId = 0;
                    error = InvalidContainerId;
                }
                else
                {
                    //
                    // Hyper-V container (i.e., RuntimeId is not empty) uses Hyper-V socket transport.
                    // Windows Server container (i.e., RuntimeId is empty) uses named pipe transport for now.
                    //
                    cmd = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        @"{{""CommandLine"": ""powershell.exe {0} -NoLogo {1}"",""RestrictedToken"": {2}}}",
                        (RuntimeId != Guid.Empty) ? "-so -NoProfile" : "-NamedPipeServerMode",
                        String.IsNullOrEmpty(ConfigurationName) ? String.Empty : String.Concat("-Config ", ConfigurationName),
                        (RunAsAdmin) ? "false" : "true");

                    HCS_PROCESS_INFORMATION ProcessInformation = new HCS_PROCESS_INFORMATION();
                    IntPtr Process = IntPtr.Zero;

                    //
                    // Create PowerShell process inside the container.
                    //
                    result = HcsCreateProcess(ComputeSystem, cmd, ref ProcessInformation, ref Process, ref resultString);
                    if (result != 0)
                    {
                        processId = 0;
                        error = result;
                    }
                    else
                    {
                        processId = Convert.ToInt32(ProcessInformation.ProcessId);
                    }
                }
            }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);

                if (e is FileNotFoundException || e is FileLoadException)
                {
                    //
                    // The ComputeSystemExists call depends on the existence of microsoft.hostcompute.interop.dll,
                    // which requires Containers feature to be enabled. In case Containers feature is 
                    // not enabled, we need to output a corresponding error message to inform user.
                    //
                    ProcessId = 0;
                    ErrorCode = ContainersFeatureNotEnabled;
                    return;
                }
                else
                {
                    ProcessId = 0;
                    ErrorCode = OtherError;
                    ErrorMessage = GetErrorMessageFromException(e);
                    return;
                }
            }

            ProcessId = processId;
            ErrorCode = error;
        }

        /// <summary>
        /// Terminate the process inside container.
        /// </summary>
        private void TerminateContainerProcessInternal()
        {
            IntPtr ComputeSystem = IntPtr.Zero;
            string resultString = String.Empty;
            IntPtr process = IntPtr.Zero;

            ProcessTerminated = false;

            if (HcsOpenComputeSystem(ContainerId, ref ComputeSystem, ref resultString) == 0)
            {
                if (HcsOpenProcess(ComputeSystem, ProcessId, ref process, ref resultString) == 0)
                {
                    if (HcsTerminateProcess(process, ref resultString) == 0)
                    {
                        ProcessTerminated = true;
                    }
                }
            }
        }

        /// <summary>
        /// Get object root based on given container id.
        /// </summary>
        private void GetContainerPropertiesInternal()
        {
            try
            {
                IntPtr ComputeSystem = IntPtr.Zero;
                string resultString = String.Empty;

                if (HcsOpenComputeSystem(ContainerId, ref ComputeSystem, ref resultString) == 0)
                {
                    Type computeSystemPropertiesType;
                    Type hostComputeInteropType;

                    GetHostComputeInteropTypes(out computeSystemPropertiesType, out hostComputeInteropType);

                    MethodInfo getComputeSystemPropertiesInfo = hostComputeInteropType.GetMethod("HcsGetComputeSystemProperties");

                    var computeSystemPropertiesHandle = getComputeSystemPropertiesInfo.Invoke(null, new object[] { ComputeSystem });

                    RuntimeId = (Guid)computeSystemPropertiesType.GetProperty("RuntimeId").GetValue(computeSystemPropertiesHandle);

                    //
                    // Get container object root for Windows Server container.
                    //
                    if (RuntimeId == Guid.Empty)
                    {
                        ContainerObRoot = (string)computeSystemPropertiesType.GetProperty("ObRoot").GetValue(computeSystemPropertiesHandle);
                    }
                }
            }
            catch (FileNotFoundException)
            {
                ErrorCode = ContainersFeatureNotEnabled;
            }
            catch (FileLoadException)
            {
                ErrorCode = ContainersFeatureNotEnabled;
            }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);

                if (e.InnerException != null &&
                    StringComparer.Ordinal.Equals(
                        e.InnerException.GetType().FullName,
                        "Microsoft.HostCompute.Interop.ObjectNotFoundException"))
                {
                    ErrorCode = InvalidContainerId;
                }
                else
                {
                    ErrorCode = OtherError;
                    ErrorMessage = GetErrorMessageFromException(e);
                }
            }
        }

        /// <summary>
        /// Run some tasks on MTA thread if needed.
        /// </summary>
        private void RunOnMTAThread(ThreadStart threadProc)
        {
            //
            // By default, non-OneCore PowerShell is launched with ApartmentState being STA.
            // In this case, we need to create a separate thread, set its ApartmentState to MTA,
            // and do the work.
            //
            // For OneCore PowerShell, its ApartmentState is always MTA.
            //        
#if CORECLR
            threadProc();
#else
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.MTA)
            {
                threadProc();
            }
            else
            {
                Thread executionThread = new Thread(new ThreadStart(threadProc));

                executionThread.SetApartmentState(ApartmentState.MTA);
                executionThread.Start();
                executionThread.Join();
            }
#endif
        }

        /// <summary>
        /// Get error message from the thrown exception.
        /// </summary>
        private string GetErrorMessageFromException(Exception e)
        {
            string errorMessage = e.Message;

            if (e.InnerException != null)
            {
                errorMessage += " " + e.InnerException.Message;
            }

            return errorMessage;
        }

        #endregion
    }
}
