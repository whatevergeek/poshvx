/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System.Collections.ObjectModel;
using System.Management.Automation.Provider;
using System.Reflection;
using System.Linq;
using System.Threading;
using Dbg = System.Management.Automation;
using System.Collections.Generic;

namespace System.Management.Automation
{
    /// <summary>
    /// Information about a loaded Cmdlet Provider
    /// </summary>
    ///
    /// <remarks>
    /// A cmdlet provider may want to derive from this class to provide their
    /// own public members to expose to the user or to cache information related to the provider.
    /// </remarks>
    public class ProviderInfo
    {
        /// <summary>
        /// Gets the System.Type of the class that implements the provider.
        /// </summary>
        public Type ImplementingType { get; }

        /// <summary>
        /// Gets the help file path for the provider.
        /// </summary>
        public string HelpFile { get; } = "";

        /// <summary>
        /// The instance of session state the provider belongs to.
        /// </summary>
        private SessionState _sessionState;


        /// <summary>
        /// Gets the name of the provider. 
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the full name of the provider including the pssnapin name if available
        /// </summary>
        /// 
        internal string FullName
        {
            get
            {
                string result = this.Name;
                if (!String.IsNullOrEmpty(this.PSSnapInName))
                {
                    result =
                        String.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "{0}\\{1}",
                            this.PSSnapInName,
                            this.Name);
                }

                // After converting core snapins to load as modules, the providers will have Module property populated
                else if (!string.IsNullOrEmpty(this.ModuleName))
                {
                    result =
                        String.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "{0}\\{1}",
                            this.ModuleName,
                            this.Name);
                }
                return result;
            }
        }

        /// <summary>
        /// Gets the Snap-in in which the provider is implemented.
        /// </summary>
        public PSSnapInInfo PSSnapIn { get; }

        /// <summary>
        /// Gets the pssnapin name that the provider is implemented in.
        /// </summary>
        /// 
        internal string PSSnapInName
        {
            get
            {
                string result = null;
                if (PSSnapIn != null)
                {
                    result = PSSnapIn.Name;
                }
                return result;
            }
        }

        internal string ApplicationBase
        {
            get
            {
                string psHome = null;
                try
                {
                    psHome = Utils.GetApplicationBase(Utils.DefaultPowerShellShellID);
                }
                catch (System.Security.SecurityException)
                {
                    psHome = null;
                }
                return psHome;
            }
        }

        /// <summary>
        /// Get the name of the module exporting this provider.
        /// </summary>
        public string ModuleName
        {
            get
            {
                if (PSSnapIn != null)
                    return PSSnapIn.Name;
                if (Module != null)
                    return Module.Name;
                return String.Empty;
            }
        }

        /// <summary>
        /// Gets the module the defined this provider.
        /// </summary>
        public PSModuleInfo Module { get; private set; }

        internal void SetModule(PSModuleInfo module)
        {
            Module = module;
        }

        /// <summary>
        /// Gets or sets the description for the provider
        /// </summary>
        public String Description { get; set; }

        /// <summary>
        /// Gets the capabilities that are implemented by the provider.
        /// </summary>
        public Provider.ProviderCapabilities Capabilities
        {
            get
            {
                if (!_capabilitiesRead)
                {
                    try
                    {
                        // Get the CmdletProvider declaration attribute

                        Type providerType = this.ImplementingType;

                        var attrs = providerType.GetCustomAttributes<CmdletProviderAttribute>(false);
                        var cmdletProviderAttributes = attrs as CmdletProviderAttribute[] ?? attrs.ToArray();

                        if (cmdletProviderAttributes.Length == 1)
                        {
                            _capabilities = cmdletProviderAttributes[0].ProviderCapabilities;
                            _capabilitiesRead = true;
                        }
                    }
                    catch (Exception e) // Catch-all OK, 3rd party callout
                    {
                        CommandProcessorBase.CheckForSevereException(e);
                        // Assume no capabilities for now
                    }
                }
                return _capabilities;
            } // get
        } // Capabilities
        private ProviderCapabilities _capabilities = ProviderCapabilities.None;
        private bool _capabilitiesRead;

        /// <summary>
        /// Gets or sets the home for the provider. 
        /// </summary>
        /// 
        /// <remarks>
        /// The location can be either a fully qualified provider path 
        /// or an Msh path. This is the location that is substituted for the ~.
        /// </remarks>
        public string Home { get; set; } // Home

        /// <summary>
        /// Gets an enumeration of drives that are available for
        /// this provider.
        /// </summary>
        public Collection<PSDriveInfo> Drives
        {
            get
            {
                return _sessionState.Drive.GetAllForProvider(FullName);
            } // get
        } // Drives

        /// <summary>
        /// A hidden drive for the provider that is used for setting
        /// the location to a provider-qualified path.
        /// </summary>
        private PSDriveInfo _hiddenDrive;

        /// <summary>
        /// Gets the hidden drive for the provider that is used
        /// for setting a location to a provider-qualified path.
        /// </summary>
        /// 
        internal PSDriveInfo HiddenDrive
        {
            get
            {
                return _hiddenDrive;
            } // get
        } // HiddenDrive

        /// <summary>
        /// Gets the string representation of the instance which is the name of the provider.
        /// </summary>
        /// 
        /// <returns>
        /// The name of the provider. If single-shell, the name is pssnapin-qualified. If custom-shell,
        /// the name is just the provider name.
        /// </returns>
        public override string ToString()
        {
            return FullName;
        }

#if USE_TLS
        /// <summary>
        /// Allocates some thread local storage to an instance of the
        /// provider. We don't want to cache a single instance of the
        /// provider because that could lead to problems in a multi-threaded
        /// environment.
        /// </summary>
        private LocalDataStoreSlot instance = 
            Thread.AllocateDataSlot();
#endif

        /// <summary>
        /// Gets or sets if the drive-root relative paths on drives of this provider
        ///  are separated by a colon or not.
        ///
        /// This is true for all PSDrives on all platforms, except for filesystems on
        /// non-windows platforms
        /// </summary>
        public bool VolumeSeparatedByColon { get; internal set; } = true;

        /// <summary>
        /// Constructs an instance of the class using an existing reference
        /// as a template.
        /// </summary>
        /// 
        /// <param name="providerInfo">
        /// The provider information to copy to this instance.
        /// </param>
        /// 
        /// <remarks>
        /// This constructor should be used by derived types to easily copying
        /// the base class members from an existing ProviderInfo.
        /// This is designed for use by a <see cref="System.Management.Automation.Provider.CmdletProvider"/>
        /// during calls to their <see cref="System.Management.Automation.Provider.CmdletProvider.Start(ProviderInfo)"/> method.
        /// </remarks>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="providerInfo"/> is null.
        /// </exception>
        protected ProviderInfo(ProviderInfo providerInfo)
        {
            if (providerInfo == null)
            {
                throw PSTraceSource.NewArgumentNullException("providerInfo");
            }

            Name = providerInfo.Name;
            ImplementingType = providerInfo.ImplementingType;
            _capabilities = providerInfo._capabilities;
            Description = providerInfo.Description;
            _hiddenDrive = providerInfo._hiddenDrive;
            Home = providerInfo.Home;
            HelpFile = providerInfo.HelpFile;
            PSSnapIn = providerInfo.PSSnapIn;
            _sessionState = providerInfo._sessionState;
            VolumeSeparatedByColon = providerInfo.VolumeSeparatedByColon;
        }

        /// <summary>
        /// Constructor for the ProviderInfo class.
        /// </summary>
        /// 
        /// <param name="sessionState">
        /// The instance of session state that the provider is being added to.
        /// </param>
        /// 
        /// <param name="implementingType">
        /// The type that implements the provider
        /// </param>
        /// 
        /// <param name="name">
        /// The name of the provider.
        /// </param>
        /// 
        /// <param name="helpFile">
        /// The help file for the provider.
        /// </param>
        /// 
        /// <param name="psSnapIn">
        /// The Snap-In name for the provider.
        /// </param>
        /// 
        /// <exception cref="ArgumentException">
        /// If <paramref name="name"/> is null or empty.
        /// </exception>
        ///
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="sessionState"/> is null.
        /// </exception>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="implementingType"/> is null.
        /// </exception>
        /// 
        internal ProviderInfo(
            SessionState sessionState,
            Type implementingType,
            string name,
            string helpFile,
            PSSnapInInfo psSnapIn)
            : this(sessionState, implementingType, name, String.Empty, String.Empty, helpFile, psSnapIn)
        {
        }


        /// <summary>
        /// Constructor for the ProviderInfo class.
        /// </summary>
        /// 
        /// <param name="sessionState">
        /// The instance of session state that the provider is being added to.
        /// </param>
        /// 
        /// <param name="implementingType">
        /// The type that implements the provider
        /// </param>
        /// 
        /// <param name="name">
        /// The alternate name to use for the provider instead of the one specified
        /// in the .cmdletprovider file.
        /// </param>
        /// 
        /// <param name="description">
        /// The description of the provider.
        /// </param>
        /// 
        /// <param name="home">
        /// The home path for the provider. This must be an MSH path.
        /// </param>
        /// 
        /// <param name="helpFile">
        /// The help file for the provider.
        /// </param>
        /// 
        /// <param name="psSnapIn">
        /// The Snap-In for the provider.
        /// </param>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="implementingType"/> or <paramref name="sessionState"/> is null.
        /// </exception>
        /// 
        /// <exception cref="ArgumentException">
        /// If <paramref name="name"/> is null or empty.
        /// </exception>
        /// 
        internal ProviderInfo(
            SessionState sessionState,
            Type implementingType,
            string name,
            string description,
            string home,
            string helpFile,
            PSSnapInInfo psSnapIn)
        {
            // Verify parameters
            if (sessionState == null)
            {
                throw PSTraceSource.NewArgumentNullException("sessionState");
            }

            if (implementingType == null)
            {
                throw PSTraceSource.NewArgumentNullException("implementingType");
            }

            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }


            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }

            _sessionState = sessionState;

            Name = name;
            Description = description;
            Home = home;
            ImplementingType = implementingType;
            HelpFile = helpFile;
            PSSnapIn = psSnapIn;

#if SUPPORTS_CMDLETPROVIDER_FILE
            LoadProviderFromPath(path);
#endif
            // Create the hidden drive. The name doesn't really
            // matter since we are not adding this drive to a scope.

            _hiddenDrive =
                new PSDriveInfo(
                    this.FullName,
                    this,
                    "",
                    "",
                    null);

            _hiddenDrive.Hidden = true;

            // TODO:PSL
            // this is probably not right here
            if (implementingType == typeof(Microsoft.PowerShell.Commands.FileSystemProvider) && !Platform.IsWindows)
            {
                VolumeSeparatedByColon = false;
            }
        }

#if SUPPORTS_CMDLETPROVIDER_FILE
        /// <summary>
        /// Loads the provider from the specified path.
        /// </summary>
        /// 
        /// <param name="path">
        /// The path to a .cmdletprovider file to load the provider from.
        /// </param>
        /// 
        /// <exception cref="ArgumentException">
        /// If <paramref name="path"/> is null or empty.
        /// </exception>
        /// 
        /// <exception cref="FileLoadException">
        /// The file specified by <paramref name="path"/> could
        /// not be loaded as an XML document.
        /// </exception>
        /// 
        /// <exception cref="FormatException">
        /// If <paramref name="path"/> refers to a file that does
        /// not adhere to the appropriate CmdletProvider file format.
        /// </exception>
        /// 
        private void LoadProviderFromPath(string path)
        {
            if (String.IsNullOrEmpty(path))
            {
                throw tracer.NewArgumentException("path");
            }

            Internal.CmdletProviderFileReader reader =
                Internal.CmdletProviderFileReader.CreateCmdletProviderFileReader(path);

            // Read the assembly info from the file
            assemblyInfo = reader.AssemblyInfo;

            // Read the type name from the file
            providerImplementationClassName = reader.TypeName;

            helpFile = reader.HelpFilePath;

            // Read the capabilities from the file
            capabilities = reader.Capabilities;
            capabilitiesRead = true;

            if (String.IsNullOrEmpty(name))
            {
                name = reader.Name;
            }
        } // LoadProviderFromPath
#endif

        /// <summary>
        /// Determines if the passed in name is either the fully-qualified pssnapin name or
        /// short name of the provider.
        /// </summary>
        /// 
        /// <param name="providerName">
        /// The name to compare with the provider name.
        /// </param>
        /// 
        /// <returns>
        /// True if the name is the fully-qualified pssnapin name or the short name of the provider.
        /// </returns>
        /// 
        internal bool NameEquals(string providerName)
        {
            PSSnapinQualifiedName qualifiedProviderName = PSSnapinQualifiedName.GetInstance(providerName);

            bool result = false;
            if (qualifiedProviderName != null)
            {
                // If the pssnapin name and provider name are specified, then both must match
                do // false loop
                {
                    if (!String.IsNullOrEmpty(qualifiedProviderName.PSSnapInName))
                    {
                        // After converting core snapins to load as modules, the providers will have Module property populated
                        if (!String.Equals(qualifiedProviderName.PSSnapInName, this.PSSnapInName, StringComparison.OrdinalIgnoreCase) &&
                            !String.Equals(qualifiedProviderName.PSSnapInName, this.ModuleName, StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }
                    }

                    result = String.Equals(qualifiedProviderName.ShortName, this.Name, StringComparison.OrdinalIgnoreCase);
                } while (false);
            }
            else
            {
                // If only the provider name is specified, then only the name must match
                result = String.Equals(providerName, Name, StringComparison.OrdinalIgnoreCase);
            }
            return result;
        }

        internal bool IsMatch(string providerName)
        {
            PSSnapinQualifiedName psSnapinQualifiedName = PSSnapinQualifiedName.GetInstance(providerName);

            WildcardPattern namePattern = null;

            if (psSnapinQualifiedName != null && WildcardPattern.ContainsWildcardCharacters(psSnapinQualifiedName.ShortName))
            {
                namePattern = WildcardPattern.Get(psSnapinQualifiedName.ShortName, WildcardOptions.IgnoreCase);
            }

            return IsMatch(namePattern, psSnapinQualifiedName);
        }

        internal bool IsMatch(WildcardPattern namePattern, PSSnapinQualifiedName psSnapinQualifiedName)
        {
            bool result = false;

            if (psSnapinQualifiedName == null)
            {
                result = true;
            }
            else
            {
                if (namePattern == null)
                {
                    if (String.Equals(Name, psSnapinQualifiedName.ShortName, StringComparison.OrdinalIgnoreCase) &&
                        IsPSSnapinNameMatch(psSnapinQualifiedName))
                    {
                        result = true;
                    }
                }
                else if (namePattern.IsMatch(Name) && IsPSSnapinNameMatch(psSnapinQualifiedName))
                {
                    result = true;
                }
            }
            return result;
        }

        private bool IsPSSnapinNameMatch(PSSnapinQualifiedName psSnapinQualifiedName)
        {
            bool result = false;

            if (String.IsNullOrEmpty(psSnapinQualifiedName.PSSnapInName) ||
                String.Equals(psSnapinQualifiedName.PSSnapInName, PSSnapInName, StringComparison.OrdinalIgnoreCase))
            {
                result = true;
            }

            return result;
        }

        /// <summary>
        /// Creates an instance of the provider
        /// </summary>
        /// 
        /// <returns>
        /// An instance of the provider or null if one could not be created.
        /// </returns>
        /// 
        /// <exception cref="ProviderNotFoundException">
        /// If an instance of the provider could not be created because the
        /// type could not be found in the assembly.
        /// </exception>
        /// 
        internal Provider.CmdletProvider CreateInstance()
        {
            // It doesn't really seem that using thread local storage to store an
            // instance of the provider is really much of a performance gain and it
            // still causes problems with the CmdletProviderContext when piping two
            // commands together that use the same provider.
            // get-child -filter a*.txt | get-content
            // This pipeline causes problems when using a cached provider instance because
            // the CmdletProviderContext gets changed when get-content gets called. 
            // When get-content finishes writing content from the first output of get-child
            // get-child gets control back and writes out a FileInfo but the WriteObject
            // from get-content gets used because the CmdletProviderContext is still from
            // that cmdlet.
            // Possible solutions are to not cache the provider instance, or to maintain
            // a CmdletProviderContext stack in ProviderBase.  Each method invocation pushes
            // the current context and the last action of the method pops back to the
            // previous context.
#if USE_TLS
            // Next see if we already have an instance in thread local storage

            object providerInstance = Thread.GetData(instance);

            if (providerInstance == null)
            {
#else
            object providerInstance = null;
#endif
            // Finally create an instance of the class
            Exception invocationException = null;

            try
            {
                providerInstance =
                    Activator.CreateInstance(this.ImplementingType);
            }
            catch (TargetInvocationException targetException)
            {
                invocationException = targetException.InnerException;
            }
            catch (MissingMethodException)
            {
            }
            catch (MemberAccessException)
            {
            }
            catch (ArgumentException)
            {
            }
#if USE_TLS
                // cache the instance in thread local storage

                Thread.SetData(instance, providerInstance);
            }
#endif

            if (providerInstance == null)
            {
                ProviderNotFoundException e = null;

                if (invocationException != null)
                {
                    e =
                        new ProviderNotFoundException(
                            this.Name,
                            SessionStateCategory.CmdletProvider,
                            "ProviderCtorException",
                            SessionStateStrings.ProviderCtorException,
                            invocationException.Message);
                }
                else
                {
                    e =
                        new ProviderNotFoundException(
                            this.Name,
                            SessionStateCategory.CmdletProvider,
                            "ProviderNotFoundInAssembly",
                            SessionStateStrings.ProviderNotFoundInAssembly);
                }
                throw e;
            }

            Provider.CmdletProvider result = providerInstance as Provider.CmdletProvider;

            Dbg.Diagnostics.Assert(
                result != null,
                "DiscoverProvider should verify that the class is derived from CmdletProvider so this is just validation of that");

            result.SetProviderInformation(this);
            return result;
        }

        /// <summary>
        /// Get the output types specified on this provider for the cmdlet requested.
        /// </summary>
        internal void GetOutputTypes(string cmdletname, List<PSTypeName> listToAppend)
        {
            if (_providerOutputType == null)
            {
                _providerOutputType = new Dictionary<string, List<PSTypeName>>();
                foreach (OutputTypeAttribute outputType in ImplementingType.GetCustomAttributes<OutputTypeAttribute>(false))
                {
                    if (string.IsNullOrEmpty(outputType.ProviderCmdlet))
                    {
                        continue;
                    }
                    List<PSTypeName> l;
                    if (!_providerOutputType.TryGetValue(outputType.ProviderCmdlet, out l))
                    {
                        l = new List<PSTypeName>();
                        _providerOutputType[outputType.ProviderCmdlet] = l;
                    }
                    l.AddRange(outputType.Type);
                }
            }

            List<PSTypeName> cmdletOutputType = null;
            if (_providerOutputType.TryGetValue(cmdletname, out cmdletOutputType))
            {
                listToAppend.AddRange(cmdletOutputType);
            }
        }
        private Dictionary<string, List<PSTypeName>> _providerOutputType;

        private PSNoteProperty _noteProperty;
        internal PSNoteProperty GetNotePropertyForProviderCmdlets(string name)
        {
            if (_noteProperty == null)
            {
                Interlocked.CompareExchange(ref _noteProperty,
                                            new PSNoteProperty(name, this), null);
            }
            return _noteProperty;
        }
    } // class ProviderInfo
} // namespace System.Management.Automation


