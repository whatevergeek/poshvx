/********************************************************************++
 * Copyright (c) Microsoft Corporation.  All rights reserved.
 * --********************************************************************/

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation.Tracing;
using Microsoft.PowerShell.Commands;
using Microsoft.Win32;
using System.Reflection;
using System.IO;
using System.Xml;
using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Management.Automation.Internal;
using System.Management.Automation.Runspaces;
using Dbg = System.Management.Automation.Diagnostics;
using System.Collections;


namespace System.Management.Automation.Remoting
{
    /// <summary>
    /// This struct is used to represent contents from configuration xml. The
    /// XML is passed to plugins by WSMan API.
    /// This helper does not validate XML content as it is already validated
    /// by WSMan.
    /// </summary>
    internal class ConfigurationDataFromXML
    {
        #region Config XML Constants

        internal const string INITPARAMETERSTOKEN = "InitializationParameters";
        internal const string PARAMTOKEN = "Param";
        internal const string NAMETOKEN = "Name";
        internal const string VALUETOKEN = "Value";
        internal const string APPBASETOKEN = "applicationbase";
        internal const string ASSEMBLYTOKEN = "assemblyname";
        internal const string SHELLCONFIGTYPETOKEN = "pssessionconfigurationtypename";
        internal const string STARTUPSCRIPTTOKEN = "startupscript";
        internal const string MAXRCVDOBJSIZETOKEN = "psmaximumreceivedobjectsizemb";
        internal const string MAXRCVDOBJSIZETOKEN_CamelCase = "PSMaximumReceivedObjectSizeMB";
        internal const string MAXRCVDCMDSIZETOKEN = "psmaximumreceiveddatasizepercommandmb";
        internal const string MAXRCVDCMDSIZETOKEN_CamelCase = "PSMaximumReceivedDataSizePerCommandMB";
        internal const string THREADOPTIONSTOKEN = "pssessionthreadoptions";
#if !CORECLR // No ApartmentState In CoreCLR
        internal const string THREADAPTSTATETOKEN = "pssessionthreadapartmentstate";
#endif
        internal const string SESSIONCONFIGTOKEN = "sessionconfigurationdata";
        internal const string PSVERSIONTOKEN = "PSVersion";
        internal const string MAXPSVERSIONTOKEN = "MaxPSVersion";
        internal const string MODULESTOIMPORT = "ModulesToImport";
        internal const string HOSTMODE = "hostmode";
        internal const string ENDPOINTCONFIGURATIONTYPE = "sessiontype";
        internal const string WORKFLOWCOREASSEMBLY = "Microsoft.PowerShell.Workflow.ServiceCore, Version=3.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35, processorArchitecture=MSIL";
        internal const string WORKFLOWCORETYPENAME = "Microsoft.PowerShell.Workflow.PSWorkflowSessionConfiguration";
        internal const string PSWORKFLOWMODULE = "%windir%\\system32\\windowspowershell\\v1.0\\Modules\\PSWorkflow";
        internal const string CONFIGFILEPATH = "configfilepath";
        internal const string CONFIGFILEPATH_CamelCase = "ConfigFilePath";

        #endregion

        internal string StartupScript;
        // this field is used only by an Out-Of-Process (IPC) server process
        internal string InitializationScriptForOutOfProcessRunspace;
        internal string ApplicationBase;
        internal string AssemblyName;
        internal string EndPointConfigurationTypeName;
        internal Type EndPointConfigurationType;
        internal Nullable<int> MaxReceivedObjectSizeMB;
        internal Nullable<int> MaxReceivedCommandSizeMB;
        // Used to set properties on the RunspacePool created for this shell.
        internal Nullable<PSThreadOptions> ShellThreadOptions;

#if !CORECLR // No ApartmentState In CoreCLR
        internal Nullable<System.Threading.ApartmentState> ShellThreadApartmentState;
#endif
        internal PSSessionConfigurationData SessionConfigurationData;
        internal string ConfigFilePath;

        /// <summary>
        /// Using optionName and optionValue updates the current object
        /// </summary>
        /// <param name="optionName"></param>
        /// <param name="optionValue"></param>
        /// <exception cref="ArgumentException">
        /// 1. "optionName" is not valid in "InitializationParameters" section.
        /// 2. "startupscript" must specify a PowerShell script file that ends with extension ".ps1".
        /// </exception>
        private void Update(string optionName, string optionValue)
        {
            switch (optionName.ToLowerInvariant())
            {
                case APPBASETOKEN:
                    AssertValueNotAssigned(APPBASETOKEN, ApplicationBase);
                    // this is a folder pointing to application base of the plugin shell
                    // allow the folder path to use environment variables.
                    ApplicationBase = Environment.ExpandEnvironmentVariables(optionValue);
                    break;
                case ASSEMBLYTOKEN:
                    AssertValueNotAssigned(ASSEMBLYTOKEN, AssemblyName);
                    AssemblyName = optionValue;
                    break;
                case SHELLCONFIGTYPETOKEN:
                    AssertValueNotAssigned(SHELLCONFIGTYPETOKEN, EndPointConfigurationTypeName);
                    EndPointConfigurationTypeName = optionValue;
                    break;
                case STARTUPSCRIPTTOKEN:
                    AssertValueNotAssigned(STARTUPSCRIPTTOKEN, StartupScript);
                    if (!optionValue.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
                    {
                        throw PSTraceSource.NewArgumentException(STARTUPSCRIPTTOKEN,
                            RemotingErrorIdStrings.StartupScriptNotCorrect,
                            STARTUPSCRIPTTOKEN);
                    }
                    // allow the script file to exist in any path..and support
                    // environment variable expansion.
                    StartupScript = Environment.ExpandEnvironmentVariables(optionValue);
                    break;
                case MAXRCVDOBJSIZETOKEN:
                    AssertValueNotAssigned(MAXRCVDOBJSIZETOKEN, MaxReceivedObjectSizeMB);
                    MaxReceivedObjectSizeMB = GetIntValueInBytes(optionValue);
                    break;
                case MAXRCVDCMDSIZETOKEN:
                    AssertValueNotAssigned(MAXRCVDCMDSIZETOKEN, MaxReceivedCommandSizeMB);
                    MaxReceivedCommandSizeMB = GetIntValueInBytes(optionValue);
                    break;
                case THREADOPTIONSTOKEN:
                    AssertValueNotAssigned(THREADOPTIONSTOKEN, ShellThreadOptions);
                    ShellThreadOptions = (PSThreadOptions)LanguagePrimitives.ConvertTo(
                        optionValue, typeof(PSThreadOptions), CultureInfo.InvariantCulture);
                    break;
#if !CORECLR // No ApartmentState In CoreCLR
                case THREADAPTSTATETOKEN:
                    AssertValueNotAssigned(THREADAPTSTATETOKEN, ShellThreadApartmentState);
                    ShellThreadApartmentState = (System.Threading.ApartmentState)LanguagePrimitives.ConvertTo(
                        optionValue, typeof(System.Threading.ApartmentState), CultureInfo.InvariantCulture);
                    break;
#endif
                case SESSIONCONFIGTOKEN:
                    {
                        AssertValueNotAssigned(SESSIONCONFIGTOKEN, SessionConfigurationData);
                        SessionConfigurationData = PSSessionConfigurationData.Create(optionValue);
                    }
                    break;
                case CONFIGFILEPATH:
                    {
                        AssertValueNotAssigned(CONFIGFILEPATH, ConfigFilePath);
                        ConfigFilePath = optionValue.ToString();
                    }
                    break;
                default:
                    // we dont need to evaluate PSVersion and other custom authz
                    // related tokens
                    break;
            }
        }

        /// <summary>
        /// Checks if the originalValue is empty. If not throws an exception
        /// </summary>
        /// <param name="optionName"></param>
        /// <param name="originalValue"></param>
        /// <exception cref="ArgumentException">
        /// 1. "optionName" is already defined
        /// </exception>
        private void AssertValueNotAssigned(string optionName, object originalValue)
        {
            if (originalValue != null)
            {
                throw PSTraceSource.NewArgumentException(optionName,
                    RemotingErrorIdStrings.DuplicateInitializationParameterFound, optionName, INITPARAMETERSTOKEN);
            }
        }

        /// <summary>
        /// Converts the value specified by <paramref name="optionValue"/> to int.
        /// Multiplies the value by 1MB (1024*1024) to get the number in bytes.
        /// </summary>
        /// <param name="optionValueInMB"></param>
        /// <returns>
        /// If value is specified, specified value as int . otherwise null.
        /// </returns>
        private static Nullable<int> GetIntValueInBytes(string optionValueInMB)
        {
            Nullable<int> result = null;
            try
            {
                double variableValue = (double)LanguagePrimitives.ConvertTo(optionValueInMB,
                    typeof(double), System.Globalization.CultureInfo.InvariantCulture);

                result = unchecked((int)(variableValue * 1024 * 1024)); // Multiply by 1MB
            }
            catch (InvalidCastException)
            {
            }

            if (result < 0)
            {
                result = null;
            }

            return result;
        }


        /// <summary>
        /// Creates the struct from initialization parameters xml.
        /// </summary>
        /// <param name="initializationParameters">
        /// Initialization Parameters xml passed by WSMan API. This data is read from the config
        /// xml and is in the following format:
        /// </param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">
        /// 1. "optionName" is already defined
        /// </exception>
        /* 
                  <InitializationParameters>
                    <Param Name="PSVersion" Value="2.0" />
                    <Param Name="ApplicationBase" Value="<folder path>" />
                    ...
                  </InitializationParameters> 
        */
        /* The following extensions have been added in V3 providing the user
         * the ability to pass data to the session configuration for initialization
         * 
                <Param Name="SessionConfigurationData" Value="<SessionConfigurationData with XML escaping>" />
         * 
         * The session configuration data blob can be defined as under
                    <SessionConfigurationData>
                        <Param Name="ModulesToImport" Value="<folder path>" />
                        <Param Name="PrivateData" />
                            <PrivateData>
                            ...
                            </PrivateData>
                        </Param>
                    </SessionConfigurationData>                    
         */
        internal static ConfigurationDataFromXML Create(string initializationParameters)
        {
            ConfigurationDataFromXML result = new ConfigurationDataFromXML();
            if (string.IsNullOrEmpty(initializationParameters))
            {
                return result;
            }

            XmlReaderSettings readerSettings = new XmlReaderSettings();
            readerSettings.CheckCharacters = false;
            readerSettings.IgnoreComments = true;
            readerSettings.IgnoreProcessingInstructions = true;
            readerSettings.MaxCharactersInDocument = 10000;
            readerSettings.ConformanceLevel = ConformanceLevel.Fragment;
#if !CORECLR // No XmlReaderSettings.XmlResolver in CoreCLR
            readerSettings.XmlResolver = null;
#endif

            using (XmlReader reader = XmlReader.Create(new StringReader(initializationParameters), readerSettings))
            {
                // read the header <InitializationParameters>
                if (reader.ReadToFollowing(INITPARAMETERSTOKEN))
                {
                    bool isParamFound = reader.ReadToDescendant(PARAMTOKEN);
                    while (isParamFound)
                    {
                        if (!reader.MoveToAttribute(NAMETOKEN))
                        {
                            throw PSTraceSource.NewArgumentException(initializationParameters,
                                RemotingErrorIdStrings.NoAttributesFoundForParamElement,
                                NAMETOKEN, VALUETOKEN, PARAMTOKEN);
                        }

                        string optionName = reader.Value;

                        if (!reader.MoveToAttribute(VALUETOKEN))
                        {
                            throw PSTraceSource.NewArgumentException(initializationParameters,
                                                                        RemotingErrorIdStrings.NoAttributesFoundForParamElement,
                                                                        NAMETOKEN, VALUETOKEN, PARAMTOKEN);
                        }

                        string optionValue = reader.Value;
                        result.Update(optionName, optionValue);

                        // move to next Param token.
                        isParamFound = reader.ReadToFollowing(PARAMTOKEN);
                    }
                }
            }

            // assign defaults after parsing the xml content.
            if (null == result.MaxReceivedObjectSizeMB)
            {
                result.MaxReceivedObjectSizeMB = BaseTransportManager.MaximumReceivedObjectSize;
            }

            if (null == result.MaxReceivedCommandSizeMB)
            {
                result.MaxReceivedCommandSizeMB = BaseTransportManager.MaximumReceivedDataSize;
            }

            return result;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        /// <exception cref="ArgumentException">
        /// 1. Unable to load type "{0}" specified in "InitializationParameters" section.
        /// </exception>
        internal PSSessionConfiguration CreateEndPointConfigurationInstance()
        {
            try
            {
                return (PSSessionConfiguration)Activator.CreateInstance(EndPointConfigurationType);
            }
            catch (TypeLoadException)
            {
            }
            catch (ArgumentException)
            {
            }
            catch (MissingMethodException)
            {
            }
            catch (InvalidCastException)
            {
            }
            catch (TargetInvocationException)
            {
            }

            // if we are here, that means we are unable to load the type specified
            // in the config xml.. notify the same.
            throw PSTraceSource.NewArgumentException("typeToLoad", RemotingErrorIdStrings.UnableToLoadType,
                    EndPointConfigurationTypeName, ConfigurationDataFromXML.INITPARAMETERSTOKEN);
        }
    }

    /// <summary>
    /// InitialSessionStateProvider is used by 3rd parties to provide shell configuration
    /// on the remote server.
    /// </summary>
    public abstract class PSSessionConfiguration : IDisposable
    {
        #region tracer
        /// <summary>
        /// Tracer for Server Remote session
        /// </summary>
        [TraceSourceAttribute("ServerRemoteSession", "ServerRemoteSession")]
        private static readonly PSTraceSource s_tracer = PSTraceSource.GetTracer("ServerRemoteSession", "ServerRemoteSession");
        #endregion tracer

        #region public interfaces

        /// <summary>
        /// Derived classes must override this to supply an InitialSessionState
        /// to be used to construct a Runspace for the user
        /// </summary>
        /// <param name="senderInfo">
        /// User Identity for which this information is requested
        /// </param>
        /// <returns></returns>
        public abstract InitialSessionState GetInitialSessionState(PSSenderInfo senderInfo);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sessionConfigurationData"></param>
        /// <param name="senderInfo"></param>
        /// <param name="configProviderId"></param>
        /// <returns></returns>
        public virtual InitialSessionState GetInitialSessionState(PSSessionConfigurationData sessionConfigurationData,
            PSSenderInfo senderInfo, string configProviderId)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Maximum size (in bytes) of a deserialized object received from a remote machine.
        /// If null, then the size is unlimited. Default is 10MB.
        /// </summary>
        /// <param name="senderInfo">
        /// User Identity for which this information is requested
        /// </param>
        /// <returns></returns>
        public virtual Nullable<int> GetMaximumReceivedObjectSize(PSSenderInfo senderInfo)
        {
            return BaseTransportManager.MaximumReceivedObjectSize;
        }

        /// <summary>
        /// Total data (in bytes) that can be received from a remote machine
        /// targeted towards a command. If null, then the size is unlimited.
        /// Default is 50MB.
        /// </summary>
        /// <param name="senderInfo">
        /// User Identity for which this information is requested
        /// </param>
        /// <returns></returns>
        public virtual Nullable<int> GetMaximumReceivedDataSizePerCommand(PSSenderInfo senderInfo)
        {
            return BaseTransportManager.MaximumReceivedDataSize;
        }

        /// <summary>
        /// Derived classes can override this method to provide application private data
        /// that is going to be sent to the client and exposed via 
        /// <see cref="System.Management.Automation.Runspaces.PSSession.ApplicationPrivateData"/>,
        /// <see cref="System.Management.Automation.Runspaces.Runspace.GetApplicationPrivateData"/> and
        /// <see cref="System.Management.Automation.Runspaces.RunspacePool.GetApplicationPrivateData"/>
        /// </summary>
        /// <param name="senderInfo">
        /// User Identity for which this information is requested
        /// </param>
        /// <returns>Application private data or <c>null</c></returns>
        public virtual PSPrimitiveDictionary GetApplicationPrivateData(PSSenderInfo senderInfo)
        {
            return null;
        }

        #endregion

        #region IDisposable Overrides

        /// <summary>
        /// Dispose this configuration object. This will be called when a Runspace/RunspacePool
        /// created using InitialSessionState from this object is Closed.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="isDisposing"></param>
        protected virtual void Dispose(bool isDisposing)
        {
        }

        #endregion

        #region GetInitialSessionState from 3rd party shell ids

        /// <summary>
        /// 
        /// </summary>
        /// <param name="shellId"></param>
        /// <param name="initializationParameters">
        /// Initialization Parameters xml passed by WSMan API. This data is read from the config
        /// xml and is in the following format:  
        /// </param>   
        /// <returns></returns>        
        /// <exception cref="InvalidOperationException">
        /// 1. Non existent InitialSessionState provider for the shellID
        /// </exception>
        /* 
                  <InitializationParameters>
                    <Param Name="PSVersion" Value="2.0" />
                    <Param Name="ApplicationBase" Value="<folder path>" />
                    ...
                  </InitializationParameters>
         */
        internal static ConfigurationDataFromXML LoadEndPointConfiguration(string shellId,
            string initializationParameters)
        {
            ConfigurationDataFromXML configData = null;

            if (!s_ssnStateProviders.ContainsKey(initializationParameters))
            {
                LoadRSConfigProvider(shellId, initializationParameters);
            }

            lock (s_syncObject)
            {
                if (!s_ssnStateProviders.TryGetValue(initializationParameters, out configData))
                {
                    throw PSTraceSource.NewInvalidOperationException(RemotingErrorIdStrings.NonExistentInitialSessionStateProvider, shellId);
                }
            }

            return configData;
        }

        private static void LoadRSConfigProvider(string shellId, string initializationParameters)
        {
            ConfigurationDataFromXML configData = ConfigurationDataFromXML.Create(initializationParameters);

            Type endPointConfigType = LoadAndAnalyzeAssembly(shellId,
                configData.ApplicationBase,
                configData.AssemblyName,
                configData.EndPointConfigurationTypeName);
            Dbg.Assert(endPointConfigType != null, "EndPointConfiguration type cannot be null");
            configData.EndPointConfigurationType = endPointConfigType;
            lock (s_syncObject)
            {
                if (!s_ssnStateProviders.ContainsKey(initializationParameters))
                {
                    s_ssnStateProviders.Add(initializationParameters, configData);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="shellId">
        /// shellId for which the assembly is getting loaded
        /// </param>
        /// <param name="applicationBase"></param>
        /// <param name="assemblyName"></param>
        /// <param name="typeToLoad">
        /// type which is supplying the configuration.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// </exception>
        /// <returns>
        /// Type instance representing the EndPointConfiguration to load.
        /// This Type can be instantiated when needed.
        /// </returns>
        private static Type LoadAndAnalyzeAssembly(string shellId, string applicationBase,
            string assemblyName, string typeToLoad)
        {
            if ((string.IsNullOrEmpty(assemblyName) && !string.IsNullOrEmpty(typeToLoad)) ||
                 (!string.IsNullOrEmpty(assemblyName) && string.IsNullOrEmpty(typeToLoad)))
            {
                throw PSTraceSource.NewInvalidOperationException(RemotingErrorIdStrings.TypeNeedsAssembly,
                    ConfigurationDataFromXML.ASSEMBLYTOKEN,
                    ConfigurationDataFromXML.SHELLCONFIGTYPETOKEN,
                    ConfigurationDataFromXML.INITPARAMETERSTOKEN);
            }

            Assembly assembly = null;
            if (!string.IsNullOrEmpty(assemblyName))
            {
                PSEtwLog.LogAnalyticVerbose(PSEventId.LoadingPSCustomShellAssembly,
                    PSOpcode.Connect, PSTask.None, PSKeyword.Transport | PSKeyword.UseAlwaysAnalytic,
                    assemblyName, shellId);

                assembly = LoadSsnStateProviderAssembly(applicationBase, assemblyName);
                if (null == assembly)
                {
                    throw PSTraceSource.NewArgumentException("assemblyName", RemotingErrorIdStrings.UnableToLoadAssembly,
                        assemblyName, ConfigurationDataFromXML.INITPARAMETERSTOKEN);
                }
            }

            // configuration xml specified an assembly and typetoload.
            if (null != assembly)
            {
                try
                {
                    PSEtwLog.LogAnalyticVerbose(PSEventId.LoadingPSCustomShellType,
                        PSOpcode.Connect, PSTask.None, PSKeyword.Transport | PSKeyword.UseAlwaysAnalytic,
                        typeToLoad, shellId);

                    Type type = assembly.GetType(typeToLoad, true, true);
                    if (null == type)
                    {
                        throw PSTraceSource.NewArgumentException("typeToLoad", RemotingErrorIdStrings.UnableToLoadType,
                            typeToLoad, ConfigurationDataFromXML.INITPARAMETERSTOKEN);
                    }

                    return type;
                }
                catch (ReflectionTypeLoadException)
                {
                }
                catch (TypeLoadException)
                {
                }
                catch (ArgumentException)
                {
                }
                catch (MissingMethodException)
                {
                }
                catch (InvalidCastException)
                {
                }
                catch (TargetInvocationException)
                {
                }

                // if we are here, that means we are unable to load the type specified
                // in the config xml.. notify the same.
                throw PSTraceSource.NewArgumentException("typeToLoad", RemotingErrorIdStrings.UnableToLoadType,
                        typeToLoad, ConfigurationDataFromXML.INITPARAMETERSTOKEN);
            }

            // load the default PowerShell since plugin config
            // did not specify a typename to load.
            return typeof(DefaultRemotePowerShellConfiguration);
        }

        /// <summary>
        /// Sets the application's current working directory to <paramref name="applicationBase"/> and 
        /// loads the assembly <paramref name="assemblyName"/>. Once the assembly is loaded, the application's
        /// current working directory is set back to the original value.
        /// </summary>
        /// <param name="applicationBase"></param>
        /// <param name="assemblyName"></param>
        /// <returns></returns>
        // TODO: Send the exception message back to the client.
        [SuppressMessage("Microsoft.Reliability", "CA2001:AvoidCallingProblematicMethods", MessageId = "System.Reflection.Assembly.LoadFrom")]
        private static Assembly LoadSsnStateProviderAssembly(string applicationBase, string assemblyName)
        {
            Dbg.Assert(!string.IsNullOrEmpty(assemblyName), "AssemblyName cannot be null.");

            string originalDirectory = string.Empty;

            if (!string.IsNullOrEmpty(applicationBase))
            {
                // changing current working directory allows CLR loader to load dependent assemblies
                try
                {
                    originalDirectory = Directory.GetCurrentDirectory();
                    Directory.SetCurrentDirectory(applicationBase);
                }
                catch (ArgumentException e)
                {
                    s_tracer.TraceWarning("Not able to change curent working directory to {0}: {1}",
                        applicationBase, e.Message);
                }
                catch (PathTooLongException e)
                {
                    s_tracer.TraceWarning("Not able to change curent working directory to {0}: {1}",
                           applicationBase, e.Message);
                }
                catch (FileNotFoundException e)
                {
                    s_tracer.TraceWarning("Not able to change curent working directory to {0}: {1}",
                        applicationBase, e.Message);
                }
                catch (IOException e)
                {
                    s_tracer.TraceWarning("Not able to change curent working directory to {0}: {1}",
                        applicationBase, e.Message);
                }
                catch (System.Security.SecurityException e)
                {
                    s_tracer.TraceWarning("Not able to change curent working directory to {0}: {1}",
                           applicationBase, e.Message);
                }
                catch (UnauthorizedAccessException e)
                {
                    s_tracer.TraceWarning("Not able to change curent working directory to {0}: {1}",
                        applicationBase, e.Message);
                }
            }

            // Even if there is error changing current working directory..try to load the assembly
            // This is to allow assembly loading from GAC
            Assembly result = null;
            try
            {
                try
                {
                    result = Assembly.Load(new AssemblyName(assemblyName));
                }
                catch (FileLoadException e)
                {
                    s_tracer.TraceWarning("Not able to load assembly {0}: {1}", assemblyName, e.Message);
                }
                catch (BadImageFormatException e)
                {
                    s_tracer.TraceWarning("Not able to load assembly {0}: {1}", assemblyName, e.Message);
                }
                catch (FileNotFoundException e)
                {
                    s_tracer.TraceWarning("Not able to load assembly {0}: {1}", assemblyName, e.Message);
                }

                if (null != result)
                {
                    return result;
                }
                s_tracer.WriteLine("Loading assembly from path {0}", applicationBase);
                try
                {
                    String assemblyPath;
                    if (!Path.IsPathRooted(assemblyName))
                    {
                        if (!String.IsNullOrEmpty(applicationBase) && Directory.Exists(applicationBase))
                        {
                            assemblyPath = Path.Combine(applicationBase, assemblyName);
                        }
                        else
                        {
                            assemblyPath = Path.Combine(Directory.GetCurrentDirectory(), assemblyName);
                        }
                    }
                    else
                    {
                        //Rooted path of dll is provided.
                        assemblyPath = assemblyName;
                    }
                    result = ClrFacade.LoadFrom(assemblyPath);
                }
                catch (FileLoadException e)
                {
                    s_tracer.TraceWarning("Not able to load assembly {0}: {1}", assemblyName, e.Message);
                }
                catch (BadImageFormatException e)
                {
                    s_tracer.TraceWarning("Not able to load assembly {0}: {1}", assemblyName, e.Message);
                }
                catch (FileNotFoundException e)
                {
                    s_tracer.TraceWarning("Not able to load assembly {0}: {1}", assemblyName, e.Message);
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(applicationBase))
                {
                    // set the application's directory back to the original directory
                    Directory.SetCurrentDirectory(originalDirectory);
                }
            }
            return result;
        }

        // TODO: I think this should be moved to Utils..this way all versioning related
        // logic will be in one place.
        private static RegistryKey GetConfigurationProvidersRegistryKey()
        {
            try
            {
                RegistryKey monadRootKey = PSSnapInReader.GetMonadRootKey();
                RegistryKey versionRoot = PSSnapInReader.GetVersionRootKey(monadRootKey, Utils.GetCurrentMajorVersion());
                RegistryKey configProviderKey = versionRoot.OpenSubKey(configProvidersKeyName);
                return configProviderKey;
            }
            catch (ArgumentException)
            {
            }
            catch (System.Security.SecurityException)
            {
            }

            return null;
        }

        /// <summary>
        /// Read value from the property <paramref name="name"/> for registry <paramref name="registryKey"/>
        /// as string.
        /// </summary>
        /// <param name="registryKey">
        /// Registry key from which the value is read.
        /// Caller should make sure this is not null.
        /// </param>
        /// <param name="name">
        /// Name of the property.
        /// Caller should make sure this is not null.
        /// </param>
        /// <param name="mandatory">
        /// True, if the property should exist.
        /// False, otherwise.
        /// </param>
        /// <returns>
        /// Value of the property.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// </exception>
        /// <exception cref="System.Security.SecurityException">
        /// </exception>
        private static string
        ReadStringValue(RegistryKey registryKey, string name, bool mandatory)
        {
            Dbg.Assert(!string.IsNullOrEmpty(name), "caller should validate the name parameter");
            Dbg.Assert(registryKey != null, "Caller should validate the registryKey parameter");

            object value = registryKey.GetValue(name);
            if (value == null && mandatory == true)
            {
                s_tracer.TraceError("Mandatory property {0} not specified for registry key {1}",
                        name, registryKey.Name);
                throw PSTraceSource.NewArgumentException("name", RemotingErrorIdStrings.MandatoryValueNotPresent, name, registryKey.Name);
            }

            string s = value as string;
            if (string.IsNullOrEmpty(s) && mandatory == true)
            {
                s_tracer.TraceError("Value is null or empty for mandatory property {0} in {1}",
                        name, registryKey.Name);
                throw PSTraceSource.NewArgumentException("name", RemotingErrorIdStrings.MandatoryValueNotInCorrectFormat, name, registryKey.Name);
            }

            return s;
        }

        private const string configProvidersKeyName = "PSConfigurationProviders";
        private const string configProviderApplicationBaseKeyName = "ApplicationBase";
        private const string configProviderAssemblyNameKeyName = "AssemblyName";
        private static Dictionary<string, ConfigurationDataFromXML> s_ssnStateProviders =
            new Dictionary<string, ConfigurationDataFromXML>(StringComparer.OrdinalIgnoreCase);
        private static object s_syncObject = new object();

        #endregion
    }

    /// <summary>
    /// Provides Default InitialSessionState.
    /// </summary>
    internal sealed class DefaultRemotePowerShellConfiguration : PSSessionConfiguration
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="senderInfo"></param>
        /// <returns></returns>
        public override InitialSessionState GetInitialSessionState(PSSenderInfo senderInfo)
        {
            InitialSessionState result = InitialSessionState.CreateDefault2();
            // TODO: Remove this after RDS moved to $using
            if (senderInfo.ConnectionString != null && senderInfo.ConnectionString.Contains("MSP=7a83d074-bb86-4e52-aa3e-6cc73cc066c8")) { PSSessionConfigurationData.IsServerManager = true; }

            return result;
        }

        public override InitialSessionState GetInitialSessionState(PSSessionConfigurationData sessionConfigurationData, PSSenderInfo senderInfo, string configProviderId)
        {
            if (sessionConfigurationData == null)
                throw new ArgumentNullException("sessionConfigurationData");

            if (senderInfo == null)
                throw new ArgumentNullException("senderInfo");

            if (configProviderId == null)
                throw new ArgumentNullException("configProviderId");

            InitialSessionState sessionState = InitialSessionState.CreateDefault2();
            // now get all the modules in the specified path and import the same
            if (sessionConfigurationData != null && sessionConfigurationData.ModulesToImportInternal != null)
            {
                foreach (var module in sessionConfigurationData.ModulesToImportInternal)
                {
                    var moduleName = module as string;
                    if (moduleName != null)
                    {
                        moduleName = Environment.ExpandEnvironmentVariables(moduleName);

                        sessionState.ImportPSModule(new[] { moduleName });
                    }
                    else
                    {
                        var moduleSpec = module as ModuleSpecification;
                        if (moduleSpec != null)
                        {
                            var modulesToImport = new Collection<ModuleSpecification> { moduleSpec };
                            sessionState.ImportPSModule(modulesToImport);
                        }
                    }
                }
            }

            // TODO: Remove this after RDS moved to $using
            if (senderInfo.ConnectionString != null && senderInfo.ConnectionString.Contains("MSP=7a83d074-bb86-4e52-aa3e-6cc73cc066c8")) { PSSessionConfigurationData.IsServerManager = true; }

            return sessionState;
        }
    }

    #region Declarative Initial Session Configuration

    /// <summary>
    /// Specifies type of initial session state to use. Valid values are Empty and Default.
    /// </summary>
    public enum SessionType
    {
        /// <summary>
        /// Empty session state
        /// </summary>
        Empty,

        /// <summary>
        /// Restricted remote server
        /// </summary>
        RestrictedRemoteServer,

        /// <summary>
        /// Default session state
        /// </summary>
        Default
    }

    /// <summary>
    /// Configuration type entry
    /// </summary>
    internal class ConfigTypeEntry
    {
        internal delegate bool TypeValidationCallback(string key, object obj, PSCmdlet cmdlet, string path);

        internal string Key;
        internal TypeValidationCallback ValidationCallback;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="callback"></param>
        internal ConfigTypeEntry(string key, TypeValidationCallback callback)
        {
            this.Key = key;
            this.ValidationCallback = callback;
        }
    }

    /// <summary>
    /// Configuration file constants
    /// </summary>
    internal static class ConfigFileConstants
    {
        internal static readonly string AliasDefinitions = "AliasDefinitions";
        internal static readonly string AliasDescriptionToken = "Description";
        internal static readonly string AliasNameToken = "Name";
        internal static readonly string AliasOptionsToken = "Options";
        internal static readonly string AliasValueToken = "Value";
        internal static readonly string AssembliesToLoad = "AssembliesToLoad";
        internal static readonly string Author = "Author";
        internal static readonly string CompanyName = "CompanyName";
        internal static readonly string Copyright = "Copyright";
        internal static readonly string Description = "Description";
        internal static readonly string EnforceInputParameterValidation = "EnforceInputParameterValidation";
        internal static readonly string EnvironmentVariables = "EnvironmentVariables";
        internal static readonly string ExecutionPolicy = "ExecutionPolicy";
        internal static readonly string FormatsToProcess = "FormatsToProcess";
        internal static readonly string FunctionDefinitions = "FunctionDefinitions";
        internal static readonly string FunctionNameToken = "Name";
        internal static readonly string FunctionOptionsToken = "Options";
        internal static readonly string FunctionValueToken = "ScriptBlock";
        internal static readonly string GMSAAccount = "GroupManagedServiceAccount";
        internal static readonly string Guid = "GUID";
        internal static readonly string LanguageMode = "LanguageMode";
        internal static readonly string ModulesToImport = "ModulesToImport";
        internal static readonly string MountUserDrive = "MountUserDrive";
        internal static readonly string PowerShellVersion = "PowerShellVersion";
        internal static readonly string RequiredGroups = "RequiredGroups";
        internal static readonly string RoleDefinitions = "RoleDefinitions";
        internal static readonly string SchemaVersion = "SchemaVersion";
        internal static readonly string ScriptsToProcess = "ScriptsToProcess";
        internal static readonly string SessionType = "SessionType";
        internal static readonly string RoleCapabilities = "RoleCapabilities";
        internal static readonly string RunAsVirtualAccount = "RunAsVirtualAccount";
        internal static readonly string RunAsVirtualAccountGroups = "RunAsVirtualAccountGroups";
        internal static readonly string TranscriptDirectory = "TranscriptDirectory";
        internal static readonly string TypesToProcess = "TypesToProcess";
        internal static readonly string UserDriveMaxSize = "UserDriveMaximumSize";
        internal static readonly string VariableDefinitions = "VariableDefinitions";
        internal static readonly string VariableNameToken = "Name";
        internal static readonly string VariableValueToken = "Value";
        internal static readonly string VisibleAliases = "VisibleAliases";
        internal static readonly string VisibleCmdlets = "VisibleCmdlets";
        internal static readonly string VisibleFunctions = "VisibleFunctions";
        internal static readonly string VisibleProviders = "VisibleProviders";
        internal static readonly string VisibleExternalCommands = "VisibleExternalCommands";

        internal static ConfigTypeEntry[] ConfigFileKeys = new ConfigTypeEntry[] {
            new ConfigTypeEntry(AliasDefinitions,               new ConfigTypeEntry.TypeValidationCallback(AliasDefinitionsTypeValidationCallback)),
            new ConfigTypeEntry(AssembliesToLoad,               new ConfigTypeEntry.TypeValidationCallback(StringArrayTypeValidationCallback)),
            new ConfigTypeEntry(Author,                         new ConfigTypeEntry.TypeValidationCallback(StringTypeValidationCallback)),
            new ConfigTypeEntry(CompanyName,                    new ConfigTypeEntry.TypeValidationCallback(StringTypeValidationCallback)),
            new ConfigTypeEntry(Copyright,                      new ConfigTypeEntry.TypeValidationCallback(StringTypeValidationCallback)),
            new ConfigTypeEntry(Description,                    new ConfigTypeEntry.TypeValidationCallback(StringTypeValidationCallback)),
            new ConfigTypeEntry(EnforceInputParameterValidation,new ConfigTypeEntry.TypeValidationCallback(BooleanTypeValidationCallback)),
            new ConfigTypeEntry(EnvironmentVariables,           new ConfigTypeEntry.TypeValidationCallback(HashtableTypeValiationCallback)),
            new ConfigTypeEntry(ExecutionPolicy,                new ConfigTypeEntry.TypeValidationCallback(ExecutionPolicyValidationCallback)),
            new ConfigTypeEntry(FormatsToProcess,               new ConfigTypeEntry.TypeValidationCallback(StringArrayTypeValidationCallback)),
            new ConfigTypeEntry(FunctionDefinitions,            new ConfigTypeEntry.TypeValidationCallback(FunctionDefinitionsTypeValidationCallback)),
            new ConfigTypeEntry(GMSAAccount,                    new ConfigTypeEntry.TypeValidationCallback(StringTypeValidationCallback)),
            new ConfigTypeEntry(Guid,                           new ConfigTypeEntry.TypeValidationCallback(StringTypeValidationCallback)),
            new ConfigTypeEntry(LanguageMode,                   new ConfigTypeEntry.TypeValidationCallback(LanugageModeValidationCallback)),
            new ConfigTypeEntry(ModulesToImport,                new ConfigTypeEntry.TypeValidationCallback(StringOrHashtableArrayTypeValidationCallback)),
            new ConfigTypeEntry(MountUserDrive,                 new ConfigTypeEntry.TypeValidationCallback(BooleanTypeValidationCallback)),
            new ConfigTypeEntry(PowerShellVersion,              new ConfigTypeEntry.TypeValidationCallback(StringTypeValidationCallback)),
            new ConfigTypeEntry(RequiredGroups,                 new ConfigTypeEntry.TypeValidationCallback(HashtableTypeValiationCallback)),
            new ConfigTypeEntry(RoleCapabilities,               new ConfigTypeEntry.TypeValidationCallback(StringArrayTypeValidationCallback)),
            new ConfigTypeEntry(RoleDefinitions,                new ConfigTypeEntry.TypeValidationCallback(HashtableTypeValiationCallback)),
            new ConfigTypeEntry(RunAsVirtualAccount,            new ConfigTypeEntry.TypeValidationCallback(BooleanTypeValidationCallback)),
            new ConfigTypeEntry(RunAsVirtualAccountGroups,      new ConfigTypeEntry.TypeValidationCallback(StringArrayTypeValidationCallback)),
            new ConfigTypeEntry(SchemaVersion,                  new ConfigTypeEntry.TypeValidationCallback(StringTypeValidationCallback)),
            new ConfigTypeEntry(ScriptsToProcess,               new ConfigTypeEntry.TypeValidationCallback(StringArrayTypeValidationCallback)),
            new ConfigTypeEntry(SessionType,                    new ConfigTypeEntry.TypeValidationCallback(ISSValidationCallback)),
            new ConfigTypeEntry(TranscriptDirectory,            new ConfigTypeEntry.TypeValidationCallback(StringTypeValidationCallback)),
            new ConfigTypeEntry(TypesToProcess,                 new ConfigTypeEntry.TypeValidationCallback(StringArrayTypeValidationCallback)),
            new ConfigTypeEntry(UserDriveMaxSize,               new ConfigTypeEntry.TypeValidationCallback(IntegerTypeValidationCallback)),
            new ConfigTypeEntry(VariableDefinitions,            new ConfigTypeEntry.TypeValidationCallback(VariableDefinitionsTypeValidationCallback)),
            new ConfigTypeEntry(VisibleAliases,                 new ConfigTypeEntry.TypeValidationCallback(StringArrayTypeValidationCallback)),
            new ConfigTypeEntry(VisibleCmdlets,                 new ConfigTypeEntry.TypeValidationCallback(StringArrayTypeValidationCallback)),
            new ConfigTypeEntry(VisibleFunctions,               new ConfigTypeEntry.TypeValidationCallback(StringArrayTypeValidationCallback)),
            new ConfigTypeEntry(VisibleProviders,               new ConfigTypeEntry.TypeValidationCallback(StringArrayTypeValidationCallback)),
            new ConfigTypeEntry(VisibleExternalCommands,        new ConfigTypeEntry.TypeValidationCallback(StringArrayTypeValidationCallback)),
        };

        /// <summary>
        /// Checks if the given key is a valid key
        /// </summary>
        /// <param name="de"></param>
        /// <param name="cmdlet"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        internal static bool IsValidKey(DictionaryEntry de, PSCmdlet cmdlet, string path)
        {
            bool validKey = false;

            foreach (ConfigTypeEntry configEntry in ConfigFileKeys)
            {
                if (String.Equals(configEntry.Key, de.Key.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    validKey = true;

                    if (configEntry.ValidationCallback(de.Key.ToString(), de.Value, cmdlet, path))
                    {
                        return true;
                    }
                }
            }

            if (!validKey)
            {
                cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.DISCInvalidKey, de.Key.ToString(), path));
            }

            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="obj"></param>
        /// <param name="cmdlet"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        private static bool ISSValidationCallback(string key, object obj, PSCmdlet cmdlet, string path)
        {
            string value = obj as string;

            if (!String.IsNullOrEmpty(value))
            {
                try
                {
                    Enum.Parse(typeof(SessionType), value, true);

                    return true;
                }
                catch (ArgumentException)
                {
                    // Do nothing here
                }
            }

            cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.DISCTypeMustBeValidEnum, key, typeof(SessionType).FullName,
                LanguagePrimitives.EnumSingleTypeConverter.EnumValues(typeof(SessionType)), path));

            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="obj"></param>
        /// <param name="cmdlet"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        private static bool LanugageModeValidationCallback(string key, object obj, PSCmdlet cmdlet, string path)
        {
            string value = obj as string;

            if (!String.IsNullOrEmpty(value))
            {
                try
                {
                    Enum.Parse(typeof(PSLanguageMode), value, true);

                    return true;
                }
                catch (ArgumentException)
                {
                    // Do nothing here
                }
            }

            cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.DISCTypeMustBeValidEnum, key, typeof(PSLanguageMode).FullName,
                LanguagePrimitives.EnumSingleTypeConverter.EnumValues(typeof(PSLanguageMode)), path));

            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="obj"></param>
        /// <param name="cmdlet"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        private static bool ExecutionPolicyValidationCallback(string key, object obj, PSCmdlet cmdlet, string path)
        {
            string value = obj as string;

            if (!String.IsNullOrEmpty(value))
            {
                try
                {
                    Enum.Parse(DISCUtils.ExecutionPolicyType, value, true);

                    return true;
                }
                catch (ArgumentException)
                {
                    // Do nothing here
                }
            }

            cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.DISCTypeMustBeValidEnum, key, DISCUtils.ExecutionPolicyType.FullName,
                LanguagePrimitives.EnumSingleTypeConverter.EnumValues(DISCUtils.ExecutionPolicyType), path));

            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="obj"></param>
        /// <param name="cmdlet"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        private static bool HashtableTypeValiationCallback(string key, object obj, PSCmdlet cmdlet, string path)
        {
            Hashtable hash = obj as Hashtable;

            if (hash == null)
            {
                cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.DISCTypeMustBeHashtable, key, path));
                return false;
            }

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="obj"></param>
        /// <param name="cmdlet"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        private static bool AliasDefinitionsTypeValidationCallback(string key, object obj, PSCmdlet cmdlet, string path)
        {
            Hashtable[] hashtables = DISCPowerShellConfiguration.TryGetHashtableArray(obj);

            if (hashtables == null)
            {
                cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.DISCTypeMustBeHashtableArray, key, path));
                return false;
            }

            foreach (Hashtable hashtable in hashtables)
            {
                if (!hashtable.ContainsKey(AliasNameToken))
                {
                    cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.DISCTypeMustContainKey, key, AliasNameToken, path));
                    return false;
                }

                if (!hashtable.ContainsKey(AliasValueToken))
                {
                    cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.DISCTypeMustContainKey, key, AliasValueToken, path));
                    return false;
                }

                foreach (string aliasKey in hashtable.Keys)
                {
                    if (!String.Equals(aliasKey, AliasNameToken, StringComparison.OrdinalIgnoreCase) &&
                       !String.Equals(aliasKey, AliasValueToken, StringComparison.OrdinalIgnoreCase) &&
                       !String.Equals(aliasKey, AliasDescriptionToken, StringComparison.OrdinalIgnoreCase) &&
                       !String.Equals(aliasKey, AliasOptionsToken, StringComparison.OrdinalIgnoreCase))
                    {
                        cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.DISCTypeContainsInvalidKey, aliasKey, key, path));
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="obj"></param>
        /// <param name="cmdlet"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        private static bool FunctionDefinitionsTypeValidationCallback(string key, object obj, PSCmdlet cmdlet, string path)
        {
            Hashtable[] hashtables = DISCPowerShellConfiguration.TryGetHashtableArray(obj);

            if (hashtables == null)
            {
                cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.DISCTypeMustBeHashtableArray, key, path));
                return false;
            }

            foreach (Hashtable hashtable in hashtables)
            {
                if (!hashtable.ContainsKey(FunctionNameToken))
                {
                    cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.DISCTypeMustContainKey, key, FunctionNameToken, path));
                    return false;
                }

                if (!hashtable.ContainsKey(FunctionValueToken))
                {
                    cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.DISCTypeMustContainKey, key, FunctionValueToken, path));
                    return false;
                }

                if ((hashtable[FunctionValueToken] as ScriptBlock) == null)
                {
                    cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.DISCKeyMustBeScriptBlock, FunctionValueToken, key, path));
                    return false;
                }

                foreach (string functionKey in hashtable.Keys)
                {
                    if (!String.Equals(functionKey, FunctionNameToken, StringComparison.OrdinalIgnoreCase) &&
                        !String.Equals(functionKey, FunctionValueToken, StringComparison.OrdinalIgnoreCase) &&
                        !String.Equals(functionKey, FunctionOptionsToken, StringComparison.OrdinalIgnoreCase))
                    {
                        cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.DISCTypeContainsInvalidKey, functionKey, key, path));
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="obj"></param>
        /// <param name="cmdlet"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        private static bool VariableDefinitionsTypeValidationCallback(string key, object obj, PSCmdlet cmdlet, string path)
        {
            Hashtable[] hashtables = DISCPowerShellConfiguration.TryGetHashtableArray(obj);

            if (hashtables == null)
            {
                cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.DISCTypeMustBeHashtableArray, key, path));
                return false;
            }

            foreach (Hashtable hashtable in hashtables)
            {
                if (!hashtable.ContainsKey(VariableNameToken))
                {
                    cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.DISCTypeMustContainKey, key, VariableNameToken, path));
                    return false;
                }

                if (!hashtable.ContainsKey(VariableValueToken))
                {
                    cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.DISCTypeMustContainKey, key, VariableValueToken, path));
                    return false;
                }

                foreach (string variableKey in hashtable.Keys)
                {
                    if (!String.Equals(variableKey, VariableNameToken, StringComparison.OrdinalIgnoreCase) &&
                        !String.Equals(variableKey, VariableValueToken, StringComparison.OrdinalIgnoreCase))
                    {
                        cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.DISCTypeContainsInvalidKey, variableKey, key, path));
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Verifies a string type
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="cmdlet"></param>
        /// <param name="key"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        private static bool StringTypeValidationCallback(string key, object obj, PSCmdlet cmdlet, string path)
        {
            if (!(obj is string))
            {
                cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.DISCTypeMustBeString, key, path));
                return false;
            }

            return true;
        }

        /// <summary>
        /// Verifies a string array type
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="cmdlet"></param>
        /// <param name="key"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        private static bool StringArrayTypeValidationCallback(string key, object obj, PSCmdlet cmdlet, string path)
        {
            if (DISCPowerShellConfiguration.TryGetStringArray(obj) == null)
            {
                cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.DISCTypeMustBeStringArray, key, path));
                return false;
            }

            return true;
        }

        private static bool BooleanTypeValidationCallback(string key, object obj, PSCmdlet cmdlet, string path)
        {
            if (!(obj is bool))
            {
                cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.DISCTypeMustBeBoolean, key, path));
                return false;
            }

            return true;
        }

        private static bool IntegerTypeValidationCallback(string key, object obj, PSCmdlet cmdlet, string path)
        {
            if (!(obj is int) && !(obj is long))
            {
                cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.DISCTypeMustBeInteger, key, path));
                return false;
            }

            return true;
        }

        /// <summary>
        /// Verifies that an array contains only string or hashtable elements
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="cmdlet"></param>
        /// <param name="key"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        private static bool StringOrHashtableArrayTypeValidationCallback(string key, object obj, PSCmdlet cmdlet, string path)
        {
            if (DISCPowerShellConfiguration.TryGetObjectsOfType<object>(obj, new Type[] { typeof(string), typeof(Hashtable) }) == null)
            {
                cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.DISCTypeMustBeStringOrHashtableArrayInFile, key, path));
                return false;
            }

            return true;
        }
    }

    #region DISC Utilities

    /// <summary>
    /// DISC utilities
    /// </summary>
    internal static class DISCUtils
    {
        #region Private data

        internal static Type ExecutionPolicyType = null;

        /// <summary>
        /// !! NOTE that this list MUST be updated when new capability session configuration properties are added.
        /// </summary>
        private static readonly HashSet<string> s_allowedRoleCapabilityKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "RoleCapabilities",
            "ModulesToImport",
            "VisibleAliases",
            "VisibleCmdlets",
            "VisibleFunctions",
            "VisibleExternalCommands",
            "VisibleProviders",
            "ScriptsToProcess",
            "AliasDefinitions",
            "FunctionDefinitions",
            "VariableDefinitions",
            "EnvironmentVariables",
            "TypesToProcess",
            "FormatsToProcess",
            "AssembliesToLoad"
        };

        #endregion

        /// <summary>
        /// Create an ExternalScriptInfo object from a file path.
        /// </summary>
        /// <param name="context">execution context</param>
        /// <param name="fileName">The path to the file</param>
        /// <param name="scriptName">The base name of the script</param>
        /// <returns>The ExternalScriptInfo object.</returns>
        internal static ExternalScriptInfo GetScriptInfoForFile(ExecutionContext context, string fileName, out string scriptName)
        {
            scriptName = Path.GetFileName(fileName);
            ExternalScriptInfo scriptInfo = new ExternalScriptInfo(scriptName, fileName, context);

            // Skip ShouldRun check for .psd1 files.
            // Use ValidateScriptInfo() for explicitly validating the checkpolicy for psd1 file.
            //
            if (!scriptName.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase))
            {
                context.AuthorizationManager.ShouldRunInternal(scriptInfo, CommandOrigin.Internal,
                    context.EngineHostInterface);

                // Verify that the PSversion is correct...
                CommandDiscovery.VerifyPSVersion(scriptInfo);

                // If we got this far, the check succeeded and we don't need to check again.
                scriptInfo.SignatureChecked = true;
            }

            return scriptInfo;
        }

        /// <summary>
        /// Loads the configuration file into a hashtable
        /// </summary>
        /// <param name="context">execution context</param>
        /// <param name="scriptInfo">the ExternalScriptInfo object</param>
        /// <returns>configuration hashtable</returns>
        internal static Hashtable LoadConfigFile(ExecutionContext context, ExternalScriptInfo scriptInfo)
        {
            object result;
            object oldPSScriptRoot = context.GetVariableValue(SpecialVariables.PSScriptRootVarPath);
            object oldPSCommandPath = context.GetVariableValue(SpecialVariables.PSCommandPathVarPath);
            try
            {
                // Set the PSScriptRoot variable in the modules session state
                context.SetVariable(SpecialVariables.PSScriptRootVarPath, Path.GetDirectoryName(scriptInfo.Definition));

                context.SetVariable(SpecialVariables.PSCommandPathVarPath, scriptInfo.Definition);

                result = PSObject.Base(scriptInfo.ScriptBlock.InvokeReturnAsIs());
            }
            finally
            {
                context.SetVariable(SpecialVariables.PSScriptRootVarPath, oldPSScriptRoot);
                context.SetVariable(SpecialVariables.PSCommandPathVarPath, oldPSCommandPath);
            }

            return result as Hashtable;
        }

        /// <summary>
        /// Verifies the configuration hashtable
        /// </summary>
        /// <param name="table">configuration hashtable</param>
        /// <param name="cmdlet"></param>
        /// <param name="path"></param>
        /// <returns>true if valid, false otherwise</returns>
        internal static bool VerifyConfigTable(Hashtable table, PSCmdlet cmdlet, string path)
        {
            bool hasSchemaVersion = false;

            foreach (DictionaryEntry de in table)
            {
                if (!ConfigFileConstants.IsValidKey(de, cmdlet, path))
                {
                    return false;
                }

                if (de.Key.ToString().Equals(ConfigFileConstants.SchemaVersion, StringComparison.OrdinalIgnoreCase))
                {
                    hasSchemaVersion = true;
                }
            }

            if (!hasSchemaVersion)
            {
                cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.DISCMissingSchemaVersion, path));
                return false;
            }

            try
            {
                ValidateAbsolutePaths(cmdlet.SessionState, table, path);
                ValidateExtensions(table, path);
            }
            catch (InvalidOperationException e)
            {
                cmdlet.WriteVerbose(e.Message);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        private static void ValidatePS1XMLExtension(string key, string[] paths, string filePath)
        {
            if (paths == null)
            {
                return;
            }

            foreach (string path in paths)
            {
                try
                {
                    string ext = System.IO.Path.GetExtension(path);

                    if (!ext.Equals(".ps1xml", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(StringUtil.Format(RemotingErrorIdStrings.DISCInvalidExtension, key, ext, ".ps1xml"));
                    }
                }
                catch (ArgumentException argumentException)
                {
                    throw new InvalidOperationException(StringUtil.Format(RemotingErrorIdStrings.ErrorParsingTheKeyInPSSessionConfigurationFile, key, filePath), argumentException);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        private static void ValidatePS1OrPSM1Extension(string key, string[] paths, string filePath)
        {
            if (paths == null)
            {
                return;
            }

            foreach (string path in paths)
            {
                try
                {
                    string ext = System.IO.Path.GetExtension(path);

                    if (!ext.Equals(StringLiterals.PowerShellScriptFileExtension, StringComparison.OrdinalIgnoreCase) &&
                        !ext.Equals(StringLiterals.PowerShellModuleFileExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(StringUtil.Format(RemotingErrorIdStrings.DISCInvalidExtension, key, ext,
                            String.Join(", ", StringLiterals.PowerShellScriptFileExtension, StringLiterals.PowerShellModuleFileExtension)));
                    }
                }
                catch (ArgumentException argumentException)
                {
                    throw new InvalidOperationException(StringUtil.Format(RemotingErrorIdStrings.ErrorParsingTheKeyInPSSessionConfigurationFile, key, filePath), argumentException);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="table"></param>
        /// <param name="filePath"></param>
        internal static void ValidateExtensions(Hashtable table, string filePath)
        {
            if (table.ContainsKey(ConfigFileConstants.TypesToProcess))
            {
                ValidatePS1XMLExtension(ConfigFileConstants.TypesToProcess, DISCPowerShellConfiguration.TryGetStringArray(table[ConfigFileConstants.TypesToProcess]), filePath);
            }

            if (table.ContainsKey(ConfigFileConstants.FormatsToProcess))
            {
                ValidatePS1XMLExtension(ConfigFileConstants.FormatsToProcess, DISCPowerShellConfiguration.TryGetStringArray(table[ConfigFileConstants.FormatsToProcess]), filePath);
            }

            if (table.ContainsKey(ConfigFileConstants.ScriptsToProcess))
            {
                ValidatePS1OrPSM1Extension(ConfigFileConstants.ScriptsToProcess, DISCPowerShellConfiguration.TryGetStringArray(table[ConfigFileConstants.ScriptsToProcess]), filePath);
            }
        }

        /// <summary>
        /// Checks if all paths are absolute paths
        /// </summary>
        /// <param name="state"></param>
        /// <param name="table"></param>
        /// <param name="filePath"></param>
        internal static void ValidateAbsolutePaths(SessionState state, Hashtable table, string filePath)
        {
            if (table.ContainsKey(ConfigFileConstants.TypesToProcess))
            {
                ValidateAbsolutePath(state, ConfigFileConstants.TypesToProcess, DISCPowerShellConfiguration.TryGetStringArray(table[ConfigFileConstants.TypesToProcess]), filePath);
            }

            if (table.ContainsKey(ConfigFileConstants.FormatsToProcess))
            {
                ValidateAbsolutePath(state, ConfigFileConstants.FormatsToProcess, DISCPowerShellConfiguration.TryGetStringArray(table[ConfigFileConstants.FormatsToProcess]), filePath);
            }

            if (table.ContainsKey(ConfigFileConstants.ScriptsToProcess))
            {
                ValidateAbsolutePath(state, ConfigFileConstants.ScriptsToProcess, DISCPowerShellConfiguration.TryGetStringArray(table[ConfigFileConstants.ScriptsToProcess]), filePath);
            }
        }

        /// <summary>
        /// Checks if a path is an absolute path
        /// </summary>
        /// <param name="key"></param>
        /// <param name="state"></param>
        /// <param name="paths"></param>
        /// <param name="filePath"></param>
        internal static void ValidateAbsolutePath(SessionState state, string key, string[] paths, string filePath)
        {
            if (paths == null)
            {
                return;
            }

            string driveName;
            foreach (string path in paths)
            {
                if (!state.Path.IsPSAbsolute(path, out driveName))
                {
                    throw new InvalidOperationException(StringUtil.Format(RemotingErrorIdStrings.DISCPathsMustBeAbsolute, key, path, filePath));
                }
            }
        }

        /// <summary>
        /// Validates Role Definition hash entries
        ///
        /// RoleDefinitions = @{
        ///     'Everyone' = @{
        ///         'RoIeCapabilities' = 'Basic' };
        ///     'Administrators' = @{
        ///         'VisibleCmdlets' = 'Get-Process','Get-Location'; 'VisibleFunctions = 'TabExpansion2' } }
        /// </summary>
        /// <param name="roleDefinitions"></param>
        internal static void ValidateRoleDefinitions(IDictionary roleDefinitions)
        {
            foreach (var roleKey in roleDefinitions.Keys)
            {
                if (!(roleKey is string))
                {
                    var invalidOperationEx = new PSInvalidOperationException(
                        string.Format(RemotingErrorIdStrings.InvalidRoleKeyType, roleKey.GetType().FullName));
                    invalidOperationEx.SetErrorId("InvalidRoleKeyType");
                    throw invalidOperationEx;
                }

                //
                // Each role capability in the role definition item should contain a hash table with allowed role capability key.
                //

                IDictionary roleDefinition = roleDefinitions[roleKey] as IDictionary;
                if (roleDefinition == null)
                {
                    var invalidOperationEx = new PSInvalidOperationException(
                        StringUtil.Format(RemotingErrorIdStrings.InvalidRoleValue, roleKey));
                    invalidOperationEx.SetErrorId("InvalidRoleEntryNotHashtable");
                    throw invalidOperationEx;
                }

                foreach (var key in roleDefinition.Keys)
                {
                    // Ensure each role capability key is valid.
                    string roleCapabilityKey = key as string;
                    if (roleCapabilityKey == null)
                    {
                        var invalidOperationEx = new PSInvalidOperationException(
                            string.Format(RemotingErrorIdStrings.InvalidRoleCapabilityKeyType, key.GetType().FullName));
                        invalidOperationEx.SetErrorId("InvalidRoleCapabilityKeyType");
                        throw invalidOperationEx;
                    }

                    if (!s_allowedRoleCapabilityKeys.Contains(roleCapabilityKey))
                    {
                        var invalidOperationEx = new PSInvalidOperationException(
                            string.Format(RemotingErrorIdStrings.InvalidRoleCapabilityKey, roleCapabilityKey));
                        invalidOperationEx.SetErrorId("InvalidRoleCapabilityKey");
                        throw invalidOperationEx;
                    }
                }
            }
        }
    }

    #endregion

    /// <summary>
    /// Creates an initial session state based on the configuration language for PSSC files
    /// </summary>
    internal sealed class DISCPowerShellConfiguration : PSSessionConfiguration
    {
        private string _configFile;
        private Hashtable _configHash;

        /// <summary>
        /// Gets the configuration hashtable that results from parsing the specified configuration file
        /// </summary>
        internal Hashtable ConfigHash
        {
            get { return _configHash; }
        }

        /// <summary>
        /// Creates a new instance of a Declarative Initial Session State Configuration
        /// </summary>
        /// <param name="configFile">The path to the .pssc file representing the initial session state</param>
        /// <param name="roleVerifier">
        /// The verifier that PowerShell should call to determine if groups in the Role entry apply to the
        /// target session. If you have a WindowsPrincipal for a user, for example, create a Function that
        /// checks windowsPrincipal.IsInRole().
        /// </param>
        internal DISCPowerShellConfiguration(string configFile, Func<string, bool> roleVerifier)
        {
            _configFile = configFile;
            if (roleVerifier == null)
            {
                roleVerifier = (role) => false;
            }

            Runspace backupRunspace = Runspace.DefaultRunspace;

            try
            {
                Runspace.DefaultRunspace = RunspaceFactory.CreateRunspace();
                Runspace.DefaultRunspace.Open();

                string scriptName;
                ExternalScriptInfo script = DISCUtils.GetScriptInfoForFile(Runspace.DefaultRunspace.ExecutionContext,
                    configFile, out scriptName);

                _configHash = DISCUtils.LoadConfigFile(Runspace.DefaultRunspace.ExecutionContext, script);
                MergeRoleRulesIntoConfigHash(roleVerifier);
                MergeRoleCapabilitiesIntoConfigHash();

                Runspace.DefaultRunspace.Close();
            }
            catch (PSSecurityException e)
            {
                string message = StringUtil.Format(RemotingErrorIdStrings.InvalidPSSessionConfigurationFilePath, configFile);
                PSInvalidOperationException ioe = new PSInvalidOperationException(message, e);
                ioe.SetErrorId("InvalidPSSessionConfigurationFilePath");

                throw ioe;
            }
            finally
            {
                Runspace.DefaultRunspace = backupRunspace;
            }
        }

        // Takes the "Roles" node in the config hash, and merges all that apply into the base configuration.
        private void MergeRoleRulesIntoConfigHash(Func<string, bool> roleVerifier)
        {
            if (_configHash.ContainsKey(ConfigFileConstants.RoleDefinitions))
            {
                // Extract the 'Roles' hashtable
                IDictionary roleEntry = _configHash[ConfigFileConstants.RoleDefinitions] as IDictionary;
                if (roleEntry == null)
                {
                    string message = StringUtil.Format(RemotingErrorIdStrings.InvalidRoleEntry, _configHash["Roles"].GetType().FullName);
                    PSInvalidOperationException ioe = new PSInvalidOperationException(message);
                    ioe.SetErrorId("InvalidRoleDefinitionNotHashtable");
                    throw ioe;
                }

                // Ensure that role definitions contain valid entries.
                DISCUtils.ValidateRoleDefinitions(roleEntry);

                // Go through the Roles hashtable
                foreach (Object role in roleEntry.Keys)
                {
                    // Check if this role applies to the connected user
                    if (roleVerifier(role.ToString()))
                    {
                        // Extract their specific configuration
                        IDictionary roleCustomizations = roleEntry[role] as IDictionary;

                        if (roleCustomizations == null)
                        {
                            string message = StringUtil.Format(RemotingErrorIdStrings.InvalidRoleValue, role.ToString());
                            PSInvalidOperationException ioe = new PSInvalidOperationException(message);
                            ioe.SetErrorId("InvalidRoleValueNotHashtable");
                            throw ioe;
                        }

                        MergeConfigHashIntoConfigHash(roleCustomizations);
                    }
                }
            }
        }

        // Takes the "RoleCapabilities" node in the config hash, and merges its values into the base configuration.
        private void MergeRoleCapabilitiesIntoConfigHash()
        {
            if (_configHash.ContainsKey(ConfigFileConstants.RoleCapabilities))
            {
                string[] roleCapabilities = TryGetStringArray(_configHash[ConfigFileConstants.RoleCapabilities]);

                if (roleCapabilities != null)
                {
                    foreach (string roleCapability in roleCapabilities)
                    {
                        string roleCapabilityPath = GetRoleCapabilityPath(roleCapability);
                        if (String.IsNullOrEmpty(roleCapabilityPath))
                        {
                            string message = StringUtil.Format(RemotingErrorIdStrings.CouldNotFindRoleCapability, roleCapability, roleCapability + ".psrc");
                            PSInvalidOperationException ioe = new PSInvalidOperationException(message);
                            ioe.SetErrorId("CouldNotFindRoleCapability");
                            throw ioe;
                        }

                        DISCPowerShellConfiguration roleCapabilityConfiguration = new DISCPowerShellConfiguration(roleCapabilityPath, null);
                        IDictionary roleCapabilityConfigurationItems = roleCapabilityConfiguration.ConfigHash;
                        MergeConfigHashIntoConfigHash(roleCapabilityConfigurationItems);
                    }
                }
            }
        }

        // Merge a role / role capability hashtable into the master configuration hashtable
        private void MergeConfigHashIntoConfigHash(IDictionary childConfigHash)
        {
            foreach (Object customization in childConfigHash.Keys)
            {
                string customizationString = customization.ToString();

                ArrayList customizationValue = new ArrayList();

                // First, take all values from the master config table
                if (_configHash.ContainsKey(customizationString))
                {
                    IEnumerable existingValueAsCollection = LanguagePrimitives.GetEnumerable(_configHash[customization]);
                    if (existingValueAsCollection != null)
                    {
                        foreach (Object value in existingValueAsCollection)
                        {
                            customizationValue.Add(value);
                        }
                    }
                    else
                    {
                        customizationValue.Add(_configHash[customization]);
                    }
                }

                // Then add the current role's values
                IEnumerable newValueAsCollection = LanguagePrimitives.GetEnumerable(childConfigHash[customization]);
                if (newValueAsCollection != null)
                {
                    foreach (Object value in newValueAsCollection)
                    {
                        customizationValue.Add(value);
                    }
                }
                else
                {
                    customizationValue.Add(childConfigHash[customization]);
                }

                // Now update the config table for this role.
                _configHash[customization] = customizationValue.ToArray();
            }
        }

        private string GetRoleCapabilityPath(string roleCapability)
        {
            string moduleName = "*";
            if (roleCapability.IndexOf('\\') != -1)
            {
                string[] components = roleCapability.Split(Utils.Separators.Backslash, 2);
                moduleName = components[0];
                roleCapability = components[1];
            }

            // Go through each directory in the module path
            string[] modulePaths = ModuleIntrinsics.GetModulePath().Split(Utils.Separators.PathSeparator);
            foreach (string path in modulePaths)
            {
                try
                {
                    // And then each module in that directory
                    foreach (string directory in Directory.EnumerateDirectories(path, moduleName))
                    {
                        string roleCapabilitiesPath = Path.Combine(directory, "RoleCapabilities");
                        if (Directory.Exists(roleCapabilitiesPath))
                        {
                            // If the role capabilities directory exists, look for .psrc files with the role capability name
                            foreach (string roleCapabilityPath in Directory.EnumerateFiles(roleCapabilitiesPath, roleCapability + ".psrc"))
                            {
                                return roleCapabilityPath;
                            }
                        }
                    }
                }
                catch (IOException)
                {
                    // Could not enumerate the directories for a broken module path element. Just try the next.
                }
                catch (UnauthorizedAccessException)
                {
                    // Could not enumerate the directories for a broken module path element. Just try the next.
                }
            }

            return null;
        }

        /// <summary>
        /// Creates an initial session state from a configuration file (DISC)
        /// </summary>
        /// <param name="senderInfo"></param>
        /// <returns></returns>
        public override InitialSessionState GetInitialSessionState(PSSenderInfo senderInfo)
        {
            InitialSessionState iss = null;

            // Create the initial session state
            string initialSessionState = TryGetValue(_configHash, ConfigFileConstants.SessionType);
            SessionType sessionType = SessionType.Default;
            bool cmdletVisibilityApplied = IsNonDefaultVisibiltySpecified(ConfigFileConstants.VisibleCmdlets);
            bool functionVisiblityApplied = IsNonDefaultVisibiltySpecified(ConfigFileConstants.VisibleFunctions);
            bool aliasVisibilityApplied = IsNonDefaultVisibiltySpecified(ConfigFileConstants.VisibleAliases);
            bool providerVisibiltyApplied = IsNonDefaultVisibiltySpecified(ConfigFileConstants.VisibleProviders);
            bool processDefaultSessionStateVisibility = false;

            if (!String.IsNullOrEmpty(initialSessionState))
            {
                sessionType = (SessionType)Enum.Parse(typeof(SessionType), initialSessionState, true);

                if (sessionType == SessionType.Empty)
                {
                    iss = InitialSessionState.Create();
                }
                else if (sessionType == SessionType.RestrictedRemoteServer)
                {
                    iss = InitialSessionState.CreateRestricted(SessionCapabilities.RemoteServer);
                }
                else
                {
                    iss = InitialSessionState.CreateDefault2();
                    processDefaultSessionStateVisibility = true;
                }
            }
            else
            {
                iss = InitialSessionState.CreateDefault2();
                processDefaultSessionStateVisibility = true;
            }

            if (cmdletVisibilityApplied || functionVisiblityApplied || aliasVisibilityApplied || providerVisibiltyApplied ||
                IsNonDefaultVisibiltySpecified(ConfigFileConstants.VisibleExternalCommands))
            {
                iss.DefaultCommandVisibility = SessionStateEntryVisibility.Private;

                // If visibility is applied on a default runspace then set initial ISS 
                // commands visibility to private.
                if (processDefaultSessionStateVisibility)
                {
                    foreach (var cmd in iss.Commands)
                    {
                        cmd.Visibility = iss.DefaultCommandVisibility;
                    }
                }
            }

            // Add providers
            if (providerVisibiltyApplied)
            {
                string[] providers = TryGetStringArray(_configHash[ConfigFileConstants.VisibleProviders]);

                if (providers != null)
                {
                    System.Collections.Generic.HashSet<string> addedProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string provider in providers)
                    {
                        if (!String.IsNullOrEmpty(provider))
                        {
                            // Look up providers from provider name including wildcards.
                            var providersFound = iss.Providers.LookUpByName(provider);

                            foreach (var providerFound in providersFound)
                            {
                                if (!addedProviders.Contains(providerFound.Name))
                                {
                                    addedProviders.Add(providerFound.Name);
                                    providerFound.Visibility = SessionStateEntryVisibility.Public;
                                }
                            }
                        }
                    }
                }
            }

            // Add assemblies and modules);
            if (_configHash.ContainsKey(ConfigFileConstants.AssembliesToLoad))
            {
                string[] assemblies = TryGetStringArray(_configHash[ConfigFileConstants.AssembliesToLoad]);

                if (assemblies != null)
                {
                    foreach (string assembly in assemblies)
                    {
                        iss.Assemblies.Add(new SessionStateAssemblyEntry(assembly));
                    }
                }
            }

            if (_configHash.ContainsKey(ConfigFileConstants.ModulesToImport))
            {
                object[] modules = TryGetObjectsOfType<object>(_configHash[ConfigFileConstants.ModulesToImport],
                                                               new Type[] { typeof(string), typeof(Hashtable) });
                if ((_configHash[ConfigFileConstants.ModulesToImport] != null) && (modules == null))
                {
                    string message = StringUtil.Format(RemotingErrorIdStrings.DISCTypeMustBeStringOrHashtableArray,
                        ConfigFileConstants.ModulesToImport);
                    PSInvalidOperationException ioe = new PSInvalidOperationException(message);
                    ioe.SetErrorId("InvalidModulesToImportKeyEntries");

                    throw ioe;
                }

                if (null != modules)
                {
                    Collection<ModuleSpecification> modulesToImport = new Collection<ModuleSpecification>();
                    foreach (object module in modules)
                    {
                        ModuleSpecification moduleSpec = null;
                        string moduleName = module as string;
                        if (!string.IsNullOrEmpty(moduleName))
                        {
                            moduleSpec = new ModuleSpecification(moduleName);
                        }
                        else
                        {
                            Hashtable moduleHash = module as Hashtable;
                            if (null != moduleHash)
                            {
                                moduleSpec = new ModuleSpecification(moduleHash);
                            }
                        }

                        // Now add the moduleSpec to modulesToImport
                        if (null != moduleSpec)
                        {
                            if (string.Equals(InitialSessionState.CoreModule, moduleSpec.Name,
                                              StringComparison.OrdinalIgnoreCase))
                            {
                                if (sessionType == SessionType.Empty)
                                {
                                    // Win8: 627752 Cannot load microsoft.powershell.core module as part of DISC
                                    // Convert Microsoft.PowerShell.Core module -> Microsoft.PowerShell.Core snapin.
                                    // Doing this Import only in SessionType.Empty case, because other cases already do this.
                                    // In V3, Microsoft.PowerShell.Core module is not installed externally.
                                    iss.ImportCorePSSnapIn();
                                }
                                // silently ignore Microsoft.PowerShell.Core for other cases ie., SessionType.RestrictedRemoteServer && SessionType.Default
                            }
                            else
                            {
                                modulesToImport.Add(moduleSpec);
                            }
                        }
                    }
                    iss.ImportPSModule(modulesToImport);
                }
            }

            // Define members
            if (_configHash.ContainsKey(ConfigFileConstants.VisibleCmdlets))
            {
                object[] cmdlets = TryGetObjectsOfType<object>(_configHash[ConfigFileConstants.VisibleCmdlets],
                                                               new Type[] { typeof(string), typeof(Hashtable) });

                if (cmdlets == null)
                {
                    string message = StringUtil.Format(RemotingErrorIdStrings.DISCTypeMustBeStringOrHashtableArray,
                        ConfigFileConstants.VisibleCmdlets);
                    PSInvalidOperationException ioe = new PSInvalidOperationException(message);
                    ioe.SetErrorId("InvalidVisibleCmdletsKeyEntries");

                    throw ioe;
                }

                ProcessVisibleCommands(iss, cmdlets);
            }

            if (_configHash.ContainsKey(ConfigFileConstants.AliasDefinitions))
            {
                Hashtable[] aliases = TryGetHashtableArray(_configHash[ConfigFileConstants.AliasDefinitions]);

                if (aliases != null)
                {
                    foreach (Hashtable alias in aliases)
                    {
                        SessionStateAliasEntry entry = CreateSessionStateAliasEntry(alias, aliasVisibilityApplied);

                        if (entry != null)
                        {
                            // Indexing iss.Commands with a command that does not exist returns 'null', rather
                            // than some sort of KeyNotFound exception.
                            if (iss.Commands[entry.Name] != null)
                            {
                                iss.Commands.Remove(entry.Name, typeof(SessionStateAliasEntry));
                            }

                            iss.Commands.Add(entry);
                        }
                    }
                }
            }

            if (_configHash.ContainsKey(ConfigFileConstants.VisibleAliases))
            {
                string[] aliases = DISCPowerShellConfiguration.TryGetStringArray(_configHash[ConfigFileConstants.VisibleAliases]);

                if (aliases != null)
                {
                    foreach (string alias in aliases)
                    {
                        if (!String.IsNullOrEmpty(alias))
                        {
                            bool found = false;

                            // Look up aliases using alias name including wildcards.
                            Collection<SessionStateCommandEntry> existingEntries = iss.Commands.LookUpByName(alias);
                            foreach (SessionStateCommandEntry existingEntry in existingEntries)
                            {
                                if (existingEntry.CommandType == CommandTypes.Alias)
                                {
                                    existingEntry.Visibility = SessionStateEntryVisibility.Public;
                                    found = true;
                                }
                            }

                            if (!found || WildcardPattern.ContainsWildcardCharacters(alias))
                            {
                                iss.UnresolvedCommandsToExpose.Add(alias);
                            }
                        }
                    }
                }
            }

            if (_configHash.ContainsKey(ConfigFileConstants.FunctionDefinitions))
            {
                Hashtable[] functions = TryGetHashtableArray(_configHash[ConfigFileConstants.FunctionDefinitions]);

                if (functions != null)
                {
                    foreach (Hashtable function in functions)
                    {
                        SessionStateFunctionEntry entry = CreateSessionStateFunctionEntry(function, functionVisiblityApplied);

                        if (entry != null)
                        {
                            iss.Commands.Add(entry);
                        }
                    }
                }
            }

            if (_configHash.ContainsKey(ConfigFileConstants.VisibleFunctions))
            {
                object[] functions = TryGetObjectsOfType<object>(_configHash[ConfigFileConstants.VisibleFunctions],
                                                               new Type[] { typeof(string), typeof(Hashtable) });

                if (functions == null)
                {
                    string message = StringUtil.Format(RemotingErrorIdStrings.DISCTypeMustBeStringOrHashtableArray,
                        ConfigFileConstants.VisibleFunctions);
                    PSInvalidOperationException ioe = new PSInvalidOperationException(message);
                    ioe.SetErrorId("InvalidVisibleFunctionsKeyEntries");

                    throw ioe;
                }

                ProcessVisibleCommands(iss, functions);
            }

            if (_configHash.ContainsKey(ConfigFileConstants.VariableDefinitions))
            {
                Hashtable[] variables = TryGetHashtableArray(_configHash[ConfigFileConstants.VariableDefinitions]);

                if (variables != null)
                {
                    foreach (Hashtable variable in variables)
                    {
                        if (variable.ContainsKey(ConfigFileConstants.VariableValueToken) &&
                            ((variable[ConfigFileConstants.VariableValueToken] as ScriptBlock) != null))
                        {
                            iss.DynamicVariablesToDefine.Add(variable);
                            continue;
                        }

                        SessionStateVariableEntry entry = CreateSessionStateVariableEntry(variable, iss.LanguageMode);

                        if (entry != null)
                        {
                            iss.Variables.Add(entry);
                        }
                    }
                }
            }

            if (_configHash.ContainsKey(ConfigFileConstants.EnvironmentVariables))
            {
                Hashtable[] variablesList = TryGetHashtableArray(_configHash[ConfigFileConstants.EnvironmentVariables]);

                if (variablesList != null)
                {
                    foreach (Hashtable variables in variablesList)
                    {
                        foreach (DictionaryEntry variable in variables)
                        {
                            SessionStateVariableEntry entry = new SessionStateVariableEntry(variable.Key.ToString(), variable.Value.ToString(), null);
                            iss.EnvironmentVariables.Add(entry);
                        }
                    }
                }
            }

            // Update type data
            if (_configHash.ContainsKey(ConfigFileConstants.TypesToProcess))
            {
                string[] types = DISCPowerShellConfiguration.TryGetStringArray(_configHash[ConfigFileConstants.TypesToProcess]);

                if (types != null)
                {
                    foreach (string type in types)
                    {
                        if (!String.IsNullOrEmpty(type))
                        {
                            iss.Types.Add(new SessionStateTypeEntry(type));
                        }
                    }
                }
            }

            // Update format data
            if (_configHash.ContainsKey(ConfigFileConstants.FormatsToProcess))
            {
                string[] formats = DISCPowerShellConfiguration.TryGetStringArray(_configHash[ConfigFileConstants.FormatsToProcess]);

                if (formats != null)
                {
                    foreach (string format in formats)
                    {
                        if (!String.IsNullOrEmpty(format))
                        {
                            iss.Formats.Add(new SessionStateFormatEntry(format));
                        }
                    }
                }
            }

            // Add external commands
            if (_configHash.ContainsKey(ConfigFileConstants.VisibleExternalCommands))
            {
                string[] externalCommands = TryGetStringArray(_configHash[ConfigFileConstants.VisibleExternalCommands]);
                if (externalCommands != null)
                {
                    foreach (string command in externalCommands)
                    {
                        if (command.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
                        {
                            iss.Commands.Add(
                                new SessionStateScriptEntry(command, SessionStateEntryVisibility.Public));
                        }
                        else
                        {
                            if (command == "*")
                            {
                                iss.Commands.Add(
                                    new SessionStateScriptEntry(command, SessionStateEntryVisibility.Public));
                            }

                            iss.Commands.Add(
                                new SessionStateApplicationEntry(command, SessionStateEntryVisibility.Public));
                        }
                    }
                }
            }

            // Register startup scripts
            if (_configHash.ContainsKey(ConfigFileConstants.ScriptsToProcess))
            {
                string[] startupScripts = DISCPowerShellConfiguration.TryGetStringArray(_configHash[ConfigFileConstants.ScriptsToProcess]);

                if (startupScripts != null)
                {
                    foreach (string script in startupScripts)
                    {
                        if (!String.IsNullOrEmpty(script))
                        {
                            iss.StartupScripts.Add(script);
                        }
                    }
                }
            }

            // Now apply visibility logic
            if (cmdletVisibilityApplied || functionVisiblityApplied || aliasVisibilityApplied || providerVisibiltyApplied)
            {
                if (sessionType == SessionType.Default)
                {
                    // autoloading preference is none, so modules cannot be autoloaded. Since the session type is default,
                    // load PowerShell default modules
                    iss.ImportPSCoreModule(InitialSessionState.EngineModules.ToArray());
                }

                if (cmdletVisibilityApplied)
                {
                    // Import-Module is needed for internal *required modules* processing, so make visibility private.
                    var importModuleEntry = iss.Commands["Import-Module"];
                    if (importModuleEntry.Count == 1)
                    {
                        importModuleEntry[0].Visibility = SessionStateEntryVisibility.Private;
                    }
                }

                if (aliasVisibilityApplied)
                {
                    // Import-Module is needed for internal *required modules* processing, so make visibility private.
                    var importModuleAliasEntry = iss.Commands["ipmo"];
                    if (importModuleAliasEntry.Count == 1)
                    {
                        importModuleAliasEntry[0].Visibility = SessionStateEntryVisibility.Private;
                    }
                }

                iss.DefaultCommandVisibility = SessionStateEntryVisibility.Private;
                iss.Variables.Add(new SessionStateVariableEntry(SpecialVariables.PSModuleAutoLoading, PSModuleAutoLoadingPreference.None, string.Empty, ScopedItemOptions.None));
            }

            // Set the execution policy
            if (_configHash.ContainsKey(ConfigFileConstants.ExecutionPolicy))
            {
                Microsoft.PowerShell.ExecutionPolicy executionPolicy = (Microsoft.PowerShell.ExecutionPolicy)Enum.Parse(
                    typeof(Microsoft.PowerShell.ExecutionPolicy), _configHash[ConfigFileConstants.ExecutionPolicy].ToString(), true);
                iss.ExecutionPolicy = executionPolicy;
            }

            // Set the language mode
            if (_configHash.ContainsKey(ConfigFileConstants.LanguageMode))
            {
                System.Management.Automation.PSLanguageMode languageMode = (System.Management.Automation.PSLanguageMode)Enum.Parse(
                    typeof(System.Management.Automation.PSLanguageMode), _configHash[ConfigFileConstants.LanguageMode].ToString(), true);
                iss.LanguageMode = languageMode;
            }

            // Set the transcript directory
            if (_configHash.ContainsKey(ConfigFileConstants.TranscriptDirectory))
            {
                iss.TranscriptDirectory = _configHash[ConfigFileConstants.TranscriptDirectory].ToString();
            }

            // Process User Drive
            if (_configHash.ContainsKey(ConfigFileConstants.MountUserDrive))
            {
                if (Convert.ToBoolean(_configHash[ConfigFileConstants.MountUserDrive], CultureInfo.InvariantCulture) == true)
                {
                    iss.UserDriveEnabled = true;
                    iss.UserDriveUserName = (senderInfo != null) ? senderInfo.UserInfo.Identity.Name : null;

                    // Set user drive max drive if provided.
                    if (_configHash.ContainsKey(ConfigFileConstants.UserDriveMaxSize))
                    {
                        long userDriveMaxSize = Convert.ToInt64(_configHash[ConfigFileConstants.UserDriveMaxSize]);
                        if (userDriveMaxSize > 0)
                        {
                            iss.UserDriveMaximumSize = userDriveMaxSize;
                        }
                    }

                    // Input parameter validation enforcement is always true when a User drive is created.
                    iss.EnforceInputParameterValidation = true;

                    // Add function definitions for Copy-Item support.
                    ProcessCopyItemFunctionDefinitions(iss);
                }
            }

            return iss;
        }

        // Adds Copy-Item remote session helper functions to the ISS.
        private static void ProcessCopyItemFunctionDefinitions(InitialSessionState iss)
        {
            // Copy file to remote helper functions.
            foreach (var copyToRemoteFn in CopyFileRemoteUtils.GetAllCopyToRemoteScriptFunctions())
            {
                var functionEntry = new SessionStateFunctionEntry(
                    copyToRemoteFn["Name"] as string,
                    copyToRemoteFn["Definition"] as string);
                iss.Commands.Add(functionEntry);
            }

            // Copy file from remote helper functions.
            foreach (var copyFromRemoteFn in CopyFileRemoteUtils.GetAllCopyFromRemoteScriptFunctions())
            {
                var functionEntry = new SessionStateFunctionEntry(
                    copyFromRemoteFn["Name"] as string,
                    copyFromRemoteFn["Definition"] as string);
                iss.Commands.Add(functionEntry);
            }
        }

        private static void ProcessVisibleCommands(InitialSessionState iss, object[] commands)
        {
            // A dictionary of: function name -> Parameters
            // Parameters = A dictionary of parameter names -> Modifications
            // Modifications = A dictionary of modification types (ValidatePattern, ValidateSet) to the interim value
            //  for that attribute, as a HashSet of strings. For ValidateSet, this will be used as a collection of strings
            //  directly during proxy generation. For For ValidatePattern, it will be combined into a regex
            //  like: '^(Pattern1|Pattern2|Pattern3)$' during proxy generation.
            Dictionary<string, Hashtable> commandModifications = new Dictionary<string, Hashtable>(StringComparer.OrdinalIgnoreCase);

            // Create a hash set of current modules to import so that fully qualified commands can include their
            // module if needed.
            HashSet<string> commandModuleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var moduleSpec in iss.ModuleSpecificationsToImport)
            {
                commandModuleNames.Add(moduleSpec.Name);
            }

            foreach (Object commandObject in commands)
            {
                if (commandObject == null)
                {
                    continue;
                }

                // If it's just a string, this is a visible command
                String command = commandObject as String;
                if (!String.IsNullOrEmpty(command))
                {
                    ProcessVisibleCommand(iss, command, commandModuleNames);
                }
                else
                {
                    // If it's a hashtable, it represents a customization to a cmdlet.
                    // (I.e.: Exposed parameter with ValidateSet and / or ValidatePattern)
                    // Collect these so that we can post-process them.
                    IDictionary commandModification = commandObject as IDictionary;
                    if (commandModification != null)
                    {
                        ProcessCommandModification(commandModifications, commandModification);
                    }
                }
            }

            // Now, save the commandModifications table for post-processing during runspace creation,
            // where we have the command info
            foreach (var pair in commandModifications)
            {
                iss.CommandModifications.Add(pair.Key, pair.Value);
            }
        }

        private static void ProcessCommandModification(Dictionary<string, Hashtable> commandModifications, IDictionary commandModification)
        {
            string commandName = commandModification["Name"] as string;
            Hashtable[] parameters = null;

            if (commandModification.Contains("Parameters"))
            {
                parameters = TryGetHashtableArray(commandModification["Parameters"]);

                if (parameters != null)
                {
                    // Validate that the parameter restriction has the right keys
                    foreach (Hashtable parameter in parameters)
                    {
                        if (!parameter.ContainsKey("Name"))
                        {
                            parameters = null;
                            break;
                        }
                    }
                }
            }

            // Validate that we got the Name and Parameters keys
            if ((commandName == null) || (parameters == null))
            {
                string hashtableKey = commandName;
                if (String.IsNullOrEmpty(hashtableKey))
                {
                    IEnumerator errorKey = commandModification.Keys.GetEnumerator();
                    errorKey.MoveNext();

                    hashtableKey = errorKey.Current.ToString();
                }

                string message = StringUtil.Format(RemotingErrorIdStrings.DISCCommandModificationSyntax, hashtableKey);
                PSInvalidOperationException ioe = new PSInvalidOperationException(message);
                ioe.SetErrorId("InvalidVisibleCommandKeyEntries");

                throw ioe;
            }

            // Ensure we have the hashtable representing the current command being modified
            Hashtable parameterModifications;
            if (!commandModifications.TryGetValue(commandName, out parameterModifications))
            {
                parameterModifications = new Hashtable(StringComparer.OrdinalIgnoreCase);
                commandModifications[commandName] = parameterModifications;
            }

            foreach (IDictionary parameter in parameters)
            {
                // Ensure we have the hashtable representing the current parameter being modified
                string parameterName = parameter["Name"].ToString();
                Hashtable currentParameterModification = parameterModifications[parameterName] as Hashtable;
                if (currentParameterModification == null)
                {
                    currentParameterModification = new Hashtable(StringComparer.OrdinalIgnoreCase);
                    parameterModifications[parameterName] = currentParameterModification;
                }

                foreach (string parameterModification in parameter.Keys)
                {
                    if (String.Equals("Name", parameterModification, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Go through the keys, adding them to the current parameter modification
                    if (!currentParameterModification.Contains(parameterModification))
                    {
                        currentParameterModification[parameterModification] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    }
                    HashSet<string> currentParameterModificationValue = (HashSet<string>)currentParameterModification[parameterModification];

                    foreach (string parameterModificationValue in TryGetStringArray(parameter[parameterModification]))
                    {
                        if (!String.IsNullOrEmpty(parameterModificationValue))
                        {
                            currentParameterModificationValue.Add(parameterModificationValue);
                        }
                    }
                }
            }
        }

        private static void ProcessVisibleCommand(InitialSessionState iss, string command, HashSet<string> moduleNames)
        {
            bool found = false;

            // Defer module restricted command processing to runspace bind time.
            // Module restricted command names are fully qualified with the module name:
            // <ModuleName>\<CommandName>, e.g., PSScheduledJob\*JobTrigger*
            if (command.IndexOf('\\') < 0)
            {
                // Look up commands from command name including wildcards.
                Collection<SessionStateCommandEntry> existingEntries = iss.Commands.LookUpByName(command);
                foreach (SessionStateCommandEntry existingEntry in existingEntries)
                {
                    if ((existingEntry.CommandType == CommandTypes.Cmdlet) || (existingEntry.CommandType == CommandTypes.Function))
                    {
                        existingEntry.Visibility = SessionStateEntryVisibility.Public;
                        found = true;
                    }
                }
            }
            else
            {
                // Extract the module name and ensure it is part of the ISS modules to process list.
                string moduleName;
                Utils.ParseCommandName(command, out moduleName);
                if (!string.IsNullOrEmpty(moduleName) && !moduleNames.Contains(moduleName))
                {
                    moduleNames.Add(moduleName);
                    iss.ImportPSModule(new string[] { moduleName });
                }
            }

            if (!found || WildcardPattern.ContainsWildcardCharacters(command))
            {
                iss.UnresolvedCommandsToExpose.Add(command);
            }
        }

        /// <summary>
        /// Creates an alias entry
        /// </summary>
        private SessionStateAliasEntry CreateSessionStateAliasEntry(Hashtable alias, bool isAliasVisibilityDefined)
        {
            string name = TryGetValue(alias, ConfigFileConstants.AliasNameToken);

            if (String.IsNullOrEmpty(name))
            {
                return null;
            }

            string value = TryGetValue(alias, ConfigFileConstants.AliasValueToken);

            if (String.IsNullOrEmpty(value))
            {
                return null;
            }

            string description = TryGetValue(alias, ConfigFileConstants.AliasDescriptionToken);

            ScopedItemOptions options = ScopedItemOptions.None;

            string optionsString = TryGetValue(alias, ConfigFileConstants.AliasOptionsToken);

            if (!String.IsNullOrEmpty(optionsString))
            {
                options = (ScopedItemOptions)Enum.Parse(typeof(ScopedItemOptions), optionsString, true);
            }

            SessionStateEntryVisibility visibility = SessionStateEntryVisibility.Private;
            if (!isAliasVisibilityDefined)
            {
                visibility = SessionStateEntryVisibility.Public;
            }

            return new SessionStateAliasEntry(name, value, description, options, visibility);
        }

        /// <summary>
        /// Creates a function entry
        /// </summary>
        /// <returns></returns>
        private SessionStateFunctionEntry CreateSessionStateFunctionEntry(Hashtable function, bool isFunctionVisibilityDefined)
        {
            string name = TryGetValue(function, ConfigFileConstants.FunctionNameToken);

            if (String.IsNullOrEmpty(name))
            {
                return null;
            }

            string value = TryGetValue(function, ConfigFileConstants.FunctionValueToken);

            if (String.IsNullOrEmpty(value))
            {
                return null;
            }

            ScopedItemOptions options = ScopedItemOptions.None;

            string optionsString = TryGetValue(function, ConfigFileConstants.FunctionOptionsToken);

            if (!String.IsNullOrEmpty(optionsString))
            {
                options = (ScopedItemOptions)Enum.Parse(typeof(ScopedItemOptions), optionsString, true);
            }

            ScriptBlock newFunction = ScriptBlock.Create(value);
            newFunction.LanguageMode = PSLanguageMode.FullLanguage;

            SessionStateEntryVisibility functionVisibility = SessionStateEntryVisibility.Private;
            if (!isFunctionVisibilityDefined)
            {
                functionVisibility = SessionStateEntryVisibility.Public;
            }

            return new SessionStateFunctionEntry(name, value, options, functionVisibility, newFunction, null);
        }

        /// <summary>
        /// Creates a variable entry
        /// </summary>
        private SessionStateVariableEntry CreateSessionStateVariableEntry(Hashtable variable, PSLanguageMode languageMode)
        {
            string name = TryGetValue(variable, ConfigFileConstants.VariableNameToken);

            if (String.IsNullOrEmpty(name))
            {
                return null;
            }

            string value = TryGetValue(variable, ConfigFileConstants.VariableValueToken);

            if (String.IsNullOrEmpty(value))
            {
                return null;
            }

            string description = TryGetValue(variable, ConfigFileConstants.AliasDescriptionToken);

            ScopedItemOptions options = ScopedItemOptions.None;

            string optionsString = TryGetValue(variable, ConfigFileConstants.AliasOptionsToken);

            if (!String.IsNullOrEmpty(optionsString))
            {
                options = (ScopedItemOptions)Enum.Parse(typeof(ScopedItemOptions), optionsString, true);
            }

            SessionStateEntryVisibility visibility = SessionStateEntryVisibility.Private;
            if (languageMode == PSLanguageMode.FullLanguage)
            {
                visibility = SessionStateEntryVisibility.Public;
            }

            return new SessionStateVariableEntry(name, value, description, options, new Collections.ObjectModel.Collection<Attribute>(), visibility);
        }

        /// <summary>
        /// Applies the command (cmdlet/function/alias) visibility settings to the <paramref name="iss"/>
        /// </summary>
        /// <param name="configFileKey"></param>
        /// <returns></returns>
        private bool IsNonDefaultVisibiltySpecified(string configFileKey)
        {
            if (_configHash.ContainsKey(configFileKey))
            {
                string[] commands =
                    DISCPowerShellConfiguration.TryGetStringArray(_configHash[configFileKey]);

                if ((commands == null) || (commands.Length == 0))
                {
                    return false;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to get a value from a hashtable
        /// </summary>
        /// <param name="table"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        internal static string TryGetValue(Hashtable table, string key)
        {
            if (table.ContainsKey(key))
            {
                return table[key].ToString();
            }

            return String.Empty;
        }

        /// <summary>
        /// Attempts to get a hashtable array from an object
        /// </summary>
        /// <param name="hashObj"></param>
        /// <returns></returns>
        internal static Hashtable[] TryGetHashtableArray(object hashObj)
        {
            // Scalar case
            Hashtable hashtable = hashObj as Hashtable;

            if (hashtable != null)
            {
                return new[] { hashtable };
            }

            // 1. Direct conversion
            Hashtable[] hashArray = hashObj as Hashtable[];

            if (hashArray == null)
            {
                // 2. Convert from object array
                object[] objArray = hashObj as object[];

                if (objArray != null)
                {
                    hashArray = new Hashtable[objArray.Length];

                    for (int i = 0; i < hashArray.Length; i++)
                    {
                        Hashtable hash = objArray[i] as Hashtable;

                        if (hash == null)
                        {
                            return null;
                        }

                        hashArray[i] = hash;
                    }
                }
            }

            return hashArray;
        }

        /// <summary>
        /// Attempts to get a string array from a hashtable
        /// </summary>
        /// <param name="hashObj"></param>
        /// <returns></returns>
        internal static string[] TryGetStringArray(object hashObj)
        {
            object[] objs = hashObj as object[];

            if (objs == null)
            {
                // Scalar case
                object obj = hashObj as object;

                if (obj != null)
                {
                    return new string[] { obj.ToString() };
                }
                else
                {
                    return null;
                }
            }

            string[] result = new string[objs.Length];

            for (int i = 0; i < objs.Length; i++)
            {
                result[i] = objs[i].ToString();
            }

            return result;
        }

        internal static T[] TryGetObjectsOfType<T>(object hashObj, IEnumerable<Type> types) where T : class
        {
            object[] objs = hashObj as object[];
            if (objs == null)
            {
                // Scalar case
                object obj = hashObj;
                if (obj != null)
                {
                    foreach (Type type in types)
                    {
                        if (obj.GetType().Equals(type))
                        {
                            return new T[] { obj as T };
                        }
                    }
                }
                return null;
            }

            T[] result = new T[objs.Length];
            for (int i = 0; i < objs.Length; i++)
            {
                int i1 = i;
                if (types.Any(type => objs[i1].GetType().Equals(type)))
                {
                    result[i] = objs[i] as T;
                }
                else
                {
                    return null;
                }
            }
            return result;
        }
    }
    #endregion
}
