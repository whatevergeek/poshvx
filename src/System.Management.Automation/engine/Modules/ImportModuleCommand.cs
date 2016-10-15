/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management.Automation.Runspaces;
using System.Reflection;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Internal;
using System.Diagnostics.CodeAnalysis;
using System.Security;
using System.Threading;
using Microsoft.Management.Infrastructure;
using Microsoft.PowerShell.Cmdletization;
using Dbg = System.Management.Automation.Diagnostics;

using System.Management.Automation.Language;
using Parser = System.Management.Automation.Language.Parser;
using ScriptBlock = System.Management.Automation.ScriptBlock;
using Token = System.Management.Automation.Language.Token;
using Microsoft.PowerShell.Telemetry.Internal;

#if CORECLR
// Use stub for SecurityZone.
using Microsoft.PowerShell.CoreClr.Stubs;
#endif

//
// Now define the set of commands for manipulating modules.
//
namespace Microsoft.PowerShell.Commands
{
    /// <summary>
    /// Implements a cmdlet that loads a module
    /// </summary>
    [Cmdlet("Import", "Module", DefaultParameterSetName = ParameterSet_Name, HelpUri = "http://go.microsoft.com/fwlink/?LinkID=141553")]
    [OutputType(typeof(PSModuleInfo))]
    public sealed class ImportModuleCommand : ModuleCmdletBase, IDisposable
    {
        #region Cmdlet parameters

        private const string ParameterSet_Name = "Name";
        private const string ParameterSet_FQName = "FullyQualifiedName";
        private const string ParameterSet_ModuleInfo = "ModuleInfo";
        private const string ParameterSet_Assembly = "Assembly";

        private const string ParameterSet_ViaPsrpSession = "PSSession";
        private const string ParameterSet_ViaCimSession = "CimSession";
        private const string ParameterSet_FQName_ViaPsrpSession = "FullyQualifiedNameAndPSSession";

        /// <summary>
        /// This parameter specifies whether to import to the current session state
        /// or to the global / top-level session state
        /// </summary>
        [Parameter]
        public SwitchParameter Global
        {
            set { base.BaseGlobal = value; }
            get { return base.BaseGlobal; }
        }

        /// <summary>
        /// This parameter specified a prefix used to modify names of imported commands
        /// </summary>
        [Parameter]
        [ValidateNotNull]
        public string Prefix
        {
            set { BasePrefix = value; }
            get { return BasePrefix; }
        }

        /// <summary>
        /// This parameter names the module to load.
        /// </summary>
        [Parameter(ParameterSetName = ParameterSet_Name, Mandatory = true, ValueFromPipeline = true, Position = 0)]
        [Parameter(ParameterSetName = ParameterSet_ViaPsrpSession, Mandatory = true, ValueFromPipeline = true, Position = 0)]
        [Parameter(ParameterSetName = ParameterSet_ViaCimSession, Mandatory = true, ValueFromPipeline = true, Position = 0)]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        public string[] Name { set; get; } = Utils.EmptyArray<string>();

        /// <summary>
        /// This parameter specifies the current pipeline object 
        /// </summary>
        [Parameter(ParameterSetName = ParameterSet_FQName, Mandatory = true, ValueFromPipeline = true, Position = 0)]
        [Parameter(ParameterSetName = ParameterSet_FQName_ViaPsrpSession, Mandatory = true, ValueFromPipeline = true, Position = 0)]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        public ModuleSpecification[] FullyQualifiedName { get; set; }

        /// <summary>
        /// A list of assembly objects to process as modules.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        [Parameter(ParameterSetName = ParameterSet_Assembly, Mandatory = true, ValueFromPipeline = true, Position = 0)]
        public Assembly[] Assembly { get; set; }

        /// <summary>
        /// This patterns matching the names of functions to import from the module...
        /// </summary>
        [Parameter]
        [ValidateNotNull]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        public string[] Function
        {
            set
            {
                if (value == null)
                    return;
                _functionImportList = value;
                // Create the list of patterns to match at parameter bind time
                // so errors will be reported before loading the module...
                BaseFunctionPatterns = new List<WildcardPattern>();
                foreach (string pattern in _functionImportList)
                {
                    BaseFunctionPatterns.Add(WildcardPattern.Get(pattern, WildcardOptions.IgnoreCase));
                }
            }
            get { return _functionImportList; }
        }
        private string[] _functionImportList = Utils.EmptyArray<string>();

        /// <summary>
        /// This patterns matching the names of cmdlets to import from the module...
        /// </summary>
        [Parameter]
        [ValidateNotNull]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        public string[] Cmdlet
        {
            set
            {
                if (value == null)
                    return;

                _cmdletImportList = value;
                // Create the list of patterns to match at parameter bind time
                // so errors will be reported before loading the module...
                BaseCmdletPatterns = new List<WildcardPattern>();
                foreach (string pattern in _cmdletImportList)
                {
                    BaseCmdletPatterns.Add(WildcardPattern.Get(pattern, WildcardOptions.IgnoreCase));
                }
            }
            get { return _cmdletImportList; }
        }
        private string[] _cmdletImportList = Utils.EmptyArray<string>();

        /// <summary>
        /// This parameter specifies the variables to import from the module...
        /// </summary>
        [Parameter]
        [ValidateNotNull]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        public string[] Variable
        {
            set
            {
                if (value == null)
                    return;
                _variableExportList = value;
                // Create the list of patterns to match at parameter bind time
                // so errors will be reported before loading the module...
                BaseVariablePatterns = new List<WildcardPattern>();
                foreach (string pattern in _variableExportList)
                {
                    BaseVariablePatterns.Add(WildcardPattern.Get(pattern, WildcardOptions.IgnoreCase));
                }
            }
            get { return _variableExportList; }
        }
        private string[] _variableExportList;

        /// <summary>
        /// This parameter specifies the aliases to import from the module...
        /// </summary>
        [Parameter]
        [ValidateNotNull]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        public string[] Alias
        {
            set
            {
                if (value == null)
                    return;

                _aliasExportList = value;
                // Create the list of patterns to match at parameter bind time
                // so errors will be reported before loading the module...
                BaseAliasPatterns = new List<WildcardPattern>();
                foreach (string pattern in _aliasExportList)
                {
                    BaseAliasPatterns.Add(WildcardPattern.Get(pattern, WildcardOptions.IgnoreCase));
                }
            }
            get { return _aliasExportList; }
        }
        private string[] _aliasExportList;

        /// <summary>
        /// This parameter causes a module to be loaded over top of the current one...
        /// </summary>
        [Parameter]
        public SwitchParameter Force
        {
            get { return (SwitchParameter)BaseForce; }
            set { BaseForce = value; }
        }

        /// <summary>
        /// This parameter causes the session state instance to be written...
        /// </summary>
        [Parameter]
        public SwitchParameter PassThru
        {
            get { return (SwitchParameter)BasePassThru; }
            set { BasePassThru = value; }
        }

        /// <summary>
        /// This parameter causes the session state instance to be written as a custom object...
        /// </summary>
        [Parameter]
        public SwitchParameter AsCustomObject
        {
            get { return (SwitchParameter)BaseAsCustomObject; }
            set { BaseAsCustomObject = value; }
        }

        /// <summary>
        /// The minimum version of the module to load.
        /// </summary>
        [Parameter(ParameterSetName = ParameterSet_Name)]
        [Parameter(ParameterSetName = ParameterSet_ViaPsrpSession)]
        [Parameter(ParameterSetName = ParameterSet_ViaCimSession)]
        [Alias("Version")]
        public Version MinimumVersion
        {
            get { return BaseMinimumVersion; }
            set { BaseMinimumVersion = value; }
        }

        /// <summary>
        /// The maximum version of the module to load.
        /// </summary>
        [Parameter(ParameterSetName = ParameterSet_Name)]
        [Parameter(ParameterSetName = ParameterSet_ViaPsrpSession)]
        [Parameter(ParameterSetName = ParameterSet_ViaCimSession)]
        public string MaximumVersion
        {
            get
            {
                if (BaseMaximumVersion == null)
                    return null;
                else
                    return BaseMaximumVersion.ToString();
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    BaseMaximumVersion = null;
                }
                else
                {
                    BaseMaximumVersion = GetMaximumVersion(value);
                }
            }
        }

        /// <summary>
        /// The version of the module to load.
        /// </summary>
        [Parameter(ParameterSetName = ParameterSet_Name)]
        [Parameter(ParameterSetName = ParameterSet_ViaPsrpSession)]
        [Parameter(ParameterSetName = ParameterSet_ViaCimSession)]
        public Version RequiredVersion
        {
            get { return BaseRequiredVersion; }
            set { BaseRequiredVersion = value; }
        }

        /// <summary>
        /// This parameter specifies the current pipeline object 
        /// </summary>
        [Parameter(ParameterSetName = ParameterSet_ModuleInfo, Mandatory = true, ValueFromPipeline = true, Position = 0)]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        public PSModuleInfo[] ModuleInfo { set; get; } = Utils.EmptyArray<PSModuleInfo>();

        /// <summary>
        /// The arguments to pass to the module script.
        /// </summary>
        [Parameter]
        [Alias("Args")]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        public object[] ArgumentList
        {
            get { return BaseArgumentList; }
            set { BaseArgumentList = value; }
        }

        /// <summary>
        /// Disable warnings on cmdlet and function names that have non-standard verbs
        /// or non-standard characters in the noun.
        /// </summary>
        [Parameter]
        public SwitchParameter DisableNameChecking
        {
            get { return BaseDisableNameChecking; }
            set { BaseDisableNameChecking = value; }
        }

        /// <summary>
        /// Does not import a command if a command with same name exists on the target sessionstate.
        /// </summary>
        [Parameter, Alias("NoOverwrite")]
        public SwitchParameter NoClobber { get; set; }

        /// <summary>
        /// Imports a command to the scope specified
        /// </summary>
        [Parameter]
        [ValidateSet("Local", "Global")]
        public String Scope
        {
            get { return _scope; }
            set
            {
                _scope = value;
                _isScopeSpecified = true;
            }
        }
        private string _scope = string.Empty;
        private bool _isScopeSpecified = false;

        /// <summary>
        /// If specified, then Import-Module will attempt to import PowerShell modules from a remote computer using the specified session
        /// </summary>
        [Parameter(ParameterSetName = ParameterSet_ViaPsrpSession, Mandatory = true)]
        [Parameter(ParameterSetName = ParameterSet_FQName_ViaPsrpSession, Mandatory = true)]
        [ValidateNotNull]
        public PSSession PSSession { get; set; }

        /// Construct the Import-Module cmdlet object
        public ImportModuleCommand()
        {
            base.BaseDisableNameChecking = false;
        }

        /// <summary>
        /// If specified, then Import-Module will attempt to import PS-CIM modules from a remote computer using the specified session
        /// </summary>
        [Parameter(ParameterSetName = ParameterSet_ViaCimSession, Mandatory = true)]
        [ValidateNotNull]
        public CimSession CimSession { get; set; }

        /// <summary>
        /// For interoperability with 3rd party CIM servers, user can specify custom resource URI
        /// </summary>
        [Parameter(ParameterSetName = ParameterSet_ViaCimSession, Mandatory = false)]
        [ValidateNotNull]
        public Uri CimResourceUri { get; set; }

        /// <summary>
        /// For interoperability with 3rd party CIM servers, user can specify custom namespace
        /// </summary>
        [Parameter(ParameterSetName = ParameterSet_ViaCimSession, Mandatory = false)]
        [ValidateNotNullOrEmpty]
        public string CimNamespace { get; set; }

        #endregion Cmdlet parameters

        #region Local import

        private void ImportModule_ViaLocalModuleInfo(ImportModuleOptions importModuleOptions, PSModuleInfo module)
        {
            try
            {
                PSModuleInfo alreadyLoadedModule = null;
                Context.Modules.ModuleTable.TryGetValue(module.Path, out alreadyLoadedModule);
                if (!BaseForce && IsModuleAlreadyLoaded(alreadyLoadedModule))
                {
                    AddModuleToModuleTables(this.Context, this.TargetSessionState.Internal, alreadyLoadedModule);

                    // Even if the module has been loaded, import the specified members...
                    ImportModuleMembers(alreadyLoadedModule, this.BasePrefix, importModuleOptions);

                    if (BaseAsCustomObject)
                    {
                        if (alreadyLoadedModule.ModuleType != ModuleType.Script)
                        {
                            string message = StringUtil.Format(Modules.CantUseAsCustomObjectWithBinaryModule, alreadyLoadedModule.Path);
                            InvalidOperationException invalidOp = new InvalidOperationException(message);
                            ErrorRecord er = new ErrorRecord(invalidOp, "Modules_CantUseAsCustomObjectWithBinaryModule",
                                                             ErrorCategory.PermissionDenied, null);
                            WriteError(er);
                        }
                        else
                        {
                            WriteObject(alreadyLoadedModule.AsCustomObject());
                        }
                    }
                    else if (BasePassThru)
                    {
                        WriteObject(alreadyLoadedModule);
                    }
                }
                else
                {
                    PSModuleInfo moduleToRemove;
                    if (Context.Modules.ModuleTable.TryGetValue(module.Path, out moduleToRemove))
                    {
                        Dbg.Assert(BaseForce, "We should only remove and reload if -Force was specified");
                        RemoveModule(moduleToRemove);
                    }

                    PSModuleInfo moduleToProcess = module;
                    try
                    {
                        // If we're passing in a dynamic module, then the session state will not be
                        // null and we want to just add the module to the module table. Otherwise, it's
                        // a module info from Get-Module -list so we need to read the actual module file.
                        if (module.SessionState == null)
                        {
                            if (File.Exists(module.Path))
                            {
                                bool found;
                                moduleToProcess = LoadModule(module.Path, null, this.BasePrefix, /*SessionState*/ null,
                                                             ref importModuleOptions,
                                                             ManifestProcessingFlags.LoadElements | ManifestProcessingFlags.WriteErrors | ManifestProcessingFlags.NullOnFirstError,
                                                             out found);
                                Dbg.Assert(found, "Module should be found when referenced by its absolute path");
                            }
                        }
                        else if (!string.IsNullOrEmpty(module.Name))
                        {
                            // It has a session state and a name but it's not in the module
                            // table so it's ok to add it

                            // Add it to the all module tables
                            AddModuleToModuleTables(this.Context, this.TargetSessionState.Internal, moduleToProcess);

                            if (moduleToProcess.SessionState != null)
                            {
                                ImportModuleMembers(moduleToProcess, this.BasePrefix, importModuleOptions);
                            }

                            if (BaseAsCustomObject && moduleToProcess.SessionState != null)
                            {
                                WriteObject(module.AsCustomObject());
                            }
                            else if (BasePassThru)
                            {
                                WriteObject(moduleToProcess);
                            }
                        }
                    }
                    catch (IOException)
                    {
                        ;
                    }
                }
            }
            catch (PSInvalidOperationException e)
            {
                ErrorRecord er = new ErrorRecord(e.ErrorRecord, e);
                WriteError(er);
            }
        }

        private void ImportModule_ViaAssembly(ImportModuleOptions importModuleOptions, Assembly suppliedAssembly)
        {
            bool moduleLoaded = false;
            // Loop through Module Cache to ensure that the module is not already imported.
            if (suppliedAssembly != null && Context.Modules.ModuleTable != null)
            {
                foreach (KeyValuePair<string, PSModuleInfo> pair in Context.Modules.ModuleTable)
                {
                    // if the module in the moduleTable is an assembly module without path, the moduleName is the key.
                    string moduleName = "dynamic_code_module_" + suppliedAssembly;
                    if (pair.Value.Path == "")
                    {
                        if (pair.Key.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                        {
                            moduleLoaded = true;
                            if (BasePassThru)
                            {
                                WriteObject(pair.Value);
                            }
                            break;
                        }
                        else
                        {
                            continue;
                        }
                    }

                    if (pair.Value.Path.Equals(suppliedAssembly.Location, StringComparison.OrdinalIgnoreCase))
                    {
                        moduleLoaded = true;
                        if (BasePassThru)
                        {
                            WriteObject(pair.Value);
                        }
                        break;
                    }
                }
            }

            if (!moduleLoaded)
            {
                bool found;
                PSModuleInfo module = LoadBinaryModule(false, null, null, suppliedAssembly, null, null,
                    importModuleOptions,
                    ManifestProcessingFlags.LoadElements | ManifestProcessingFlags.WriteErrors | ManifestProcessingFlags.NullOnFirstError,
                    this.BasePrefix, false /* loadTypes */ , false /* loadFormats */, out found);

                if (found && module != null)
                {
                    // Add it to all module tables ...
                    AddModuleToModuleTables(this.Context, this.TargetSessionState.Internal, module);
                    if (BasePassThru)
                    {
                        WriteObject(module);
                    }
                }
            }
        }

        private PSModuleInfo ImportModule_LocallyViaName(ImportModuleOptions importModuleOptions, string name)
        {
            try
            {
                if (name.Equals("PSWorkflow", StringComparison.OrdinalIgnoreCase) && Utils.IsRunningFromSysWOW64())
                {
                    throw new NotSupportedException(AutomationExceptions.WorkflowDoesNotSupportWOW64);
                }

                bool found = false;
                PSModuleInfo foundModule = null;

                string cachedPath = null;
                string rootedPath = null;

                // See if we can use the cached path for the file. If a version number has been specified, then
                // we won't look in the cache
                if (this.MinimumVersion == null && this.MaximumVersion == null && this.RequiredVersion == null && PSModuleInfo.UseAppDomainLevelModuleCache && !this.BaseForce)
                {
                    // See if the name is in the appdomain-level module path name cache...
                    cachedPath = PSModuleInfo.ResolveUsingAppDomainLevelModuleCache(name);
                }

                if (!string.IsNullOrEmpty(cachedPath))
                {
                    if (File.Exists(cachedPath))
                    {
                        rootedPath = cachedPath;
                    }
                    else
                    {
                        PSModuleInfo.RemoveFromAppDomainLevelCache(name);
                    }
                }

                if (rootedPath == null)
                {
                    // Check for full-qualified paths - either absolute or relative
                    rootedPath = ResolveRootedFilePath(name, this.Context);
                }

                bool alreadyLoaded = false;
                if (!String.IsNullOrEmpty(rootedPath))
                {
                    // TODO/FIXME: use IsModuleAlreadyLoaded to get consistent behavior 
                    // TODO/FIXME: (for example checking ModuleType != Manifest below seems incorrect - cdxml modules also declare their own version)
                    // PSModuleInfo alreadyLoadedModule = null;
                    // Context.Modules.ModuleTable.TryGetValue(rootedPath, out alreadyLoadedModule);
                    // if (!BaseForce && IsModuleAlreadyLoaded(alreadyLoadedModule))

                    // If the module has already been loaded, just emit it and continue...
                    PSModuleInfo module;
                    if (!BaseForce && Context.Modules.ModuleTable.TryGetValue(rootedPath, out module))
                    {
                        if (RequiredVersion == null
                            || module.Version.Equals(RequiredVersion)
                            || (BaseMinimumVersion == null && BaseMaximumVersion == null)
                            || module.ModuleType != ModuleType.Manifest
                            || (BaseMinimumVersion == null && BaseMaximumVersion != null && module.Version <= BaseMaximumVersion)
                            || (BaseMinimumVersion != null && BaseMaximumVersion == null && module.Version >= BaseMinimumVersion)
                            || (BaseMinimumVersion != null && BaseMaximumVersion != null && module.Version >= BaseMinimumVersion && module.Version <= BaseMaximumVersion))
                        {
                            alreadyLoaded = true;
                            AddModuleToModuleTables(this.Context, this.TargetSessionState.Internal, module);
                            ImportModuleMembers(module, this.BasePrefix, importModuleOptions);

                            if (BaseAsCustomObject)
                            {
                                if (module.ModuleType != ModuleType.Script)
                                {
                                    string message = StringUtil.Format(Modules.CantUseAsCustomObjectWithBinaryModule, module.Path);
                                    InvalidOperationException invalidOp = new InvalidOperationException(message);
                                    ErrorRecord er = new ErrorRecord(invalidOp, "Modules_CantUseAsCustomObjectWithBinaryModule",
                                                                     ErrorCategory.PermissionDenied, null);
                                    WriteError(er);
                                }
                                else
                                {
                                    WriteObject(module.AsCustomObject());
                                }
                            }
                            else if (BasePassThru)
                            {
                                WriteObject(module);
                            }
                            found = true;
                            foundModule = module;
                        }
                    }

                    if (!alreadyLoaded)
                    {
                        // If the path names a file, load that file...
                        if (File.Exists(rootedPath))
                        {
                            PSModuleInfo moduleToRemove;
                            if (Context.Modules.ModuleTable.TryGetValue(rootedPath, out moduleToRemove))
                            {
                                RemoveModule(moduleToRemove);
                            }

                            foundModule = LoadModule(rootedPath, null, this.BasePrefix, null, ref importModuleOptions,
                                                     ManifestProcessingFlags.LoadElements | ManifestProcessingFlags.WriteErrors | ManifestProcessingFlags.NullOnFirstError,
                                                     out found);
                        }
                        else if (Directory.Exists(rootedPath))
                        {
                            // Load the latest valid version if it is a multi-version module directory
                            foundModule = LoadUsingMultiVersionModuleBase(rootedPath,
                                                                            ManifestProcessingFlags.LoadElements |
                                                                            ManifestProcessingFlags.WriteErrors |
                                                                            ManifestProcessingFlags.NullOnFirstError,
                                                                            importModuleOptions, out found);

                            if (!found)
                            {
                                // If the path is a directory, double up the end of the string
                                // then try to load that using extensions...
                                rootedPath = Path.Combine(rootedPath, Path.GetFileName(rootedPath));
                                foundModule = LoadUsingExtensions(null, rootedPath, rootedPath, null, null, this.BasePrefix, /*SessionState*/ null,
                                                                  importModuleOptions,
                                                                  ManifestProcessingFlags.LoadElements | ManifestProcessingFlags.WriteErrors | ManifestProcessingFlags.NullOnFirstError,
                                                                  out found);
                            }
                        }
                    }
                }
                else
                {
                    // Check if module could be a snapin. This was the case for PowerShell version 2 engine modules.
                    if (InitialSessionState.IsEngineModule(name))
                    {
                        PSSnapInInfo snapin = ModuleCmdletBase.GetEngineSnapIn(Context, name);

                        // Return the command if we found a module
                        if (snapin != null)
                        {
                            // warn that this module already exists as a snapin 
                            string warningMessage = string.Format(
                                CultureInfo.InvariantCulture,
                                Modules.ModuleLoadedAsASnapin,
                                snapin.Name);
                            WriteWarning(warningMessage);
                            found = true;
                            return foundModule;
                        }
                    }

                    // At this point, the name didn't resolve to an existing file or directory.
                    // It may still be rooted (relative or absolute). If it is, then we'll only use
                    // the extension search. If it's not rooted, use a path-based search.
                    if (IsRooted(name))
                    {
                        // If there is no extension, we'll have to search using the extensions
                        if (!string.IsNullOrEmpty(Path.GetExtension(name)))
                        {
                            foundModule = LoadModule(name, null, this.BasePrefix, null, ref importModuleOptions,
                                                     ManifestProcessingFlags.LoadElements | ManifestProcessingFlags.WriteErrors | ManifestProcessingFlags.NullOnFirstError,
                                                     out found);
                        }
                        else
                        {
                            foundModule = LoadUsingExtensions(null, name, name, null, null, this.BasePrefix, /*SessionState*/ null,
                                                              importModuleOptions,
                                                              ManifestProcessingFlags.LoadElements | ManifestProcessingFlags.WriteErrors | ManifestProcessingFlags.NullOnFirstError,
                                                              out found);
                        }
                    }
                    else
                    {
                        IEnumerable<string> modulePath = ModuleIntrinsics.GetModulePath(false, this.Context);

                        if (this.MinimumVersion == null && this.RequiredVersion == null && this.MaximumVersion == null)
                        {
                            this.AddToAppDomainLevelCache = true;
                        }

                        found = LoadUsingModulePath(found, modulePath, name, /* SessionState*/ null,
                                                    importModuleOptions,
                                                    ManifestProcessingFlags.LoadElements | ManifestProcessingFlags.WriteErrors | ManifestProcessingFlags.NullOnFirstError,
                                                    out foundModule);
                    }
                }

                if (!found)
                {
                    ErrorRecord er = null;
                    string message = null;
                    if (BaseRequiredVersion != null)
                    {
                        message = StringUtil.Format(Modules.ModuleWithVersionNotFound, name, BaseRequiredVersion);
                    }
                    else if (BaseMinimumVersion != null && BaseMaximumVersion != null)
                    {
                        message = StringUtil.Format(Modules.MinimumVersionAndMaximumVersionNotFound, name, BaseMinimumVersion, BaseMaximumVersion);
                    }
                    else if (BaseMinimumVersion != null)
                    {
                        message = StringUtil.Format(Modules.ModuleWithVersionNotFound, name, BaseMinimumVersion);
                    }
                    else if (BaseMaximumVersion != null)
                    {
                        message = StringUtil.Format(Modules.MaximumVersionNotFound, name, BaseMaximumVersion);
                    }
                    if (BaseRequiredVersion != null || BaseMinimumVersion != null || BaseMaximumVersion != null)
                    {
                        FileNotFoundException fnf = new FileNotFoundException(message);
                        er = new ErrorRecord(fnf, "Modules_ModuleWithVersionNotFound",
                                             ErrorCategory.ResourceUnavailable, name);
                    }
                    else
                    {
                        message = StringUtil.Format(Modules.ModuleNotFound, name);
                        FileNotFoundException fnf = new FileNotFoundException(message);
                        er = new ErrorRecord(fnf, "Modules_ModuleNotFound",
                                             ErrorCategory.ResourceUnavailable, name);
                    }
                    WriteError(er);
                }

                return foundModule;
            }
            catch (PSInvalidOperationException e)
            {
                ErrorRecord er = new ErrorRecord(e.ErrorRecord, e);
                WriteError(er);
            }

            return null;
        }

        #endregion Local import

        #region Remote import

        #region PSSession parameterset

        private IList<PSModuleInfo> ImportModule_RemotelyViaPsrpSession(
            ImportModuleOptions importModuleOptions,
            IEnumerable<string> moduleNames,
            IEnumerable<ModuleSpecification> fullyQualifiedNames,
            PSSession psSession)
        {
            var remotelyImportedModules = new List<PSModuleInfo>();
            if (moduleNames != null)
            {
                foreach (string moduleName in moduleNames)
                {
                    var tmp = ImportModule_RemotelyViaPsrpSession(importModuleOptions, moduleName, null, psSession);
                    remotelyImportedModules.AddRange(tmp);
                }
            }

            if (fullyQualifiedNames != null)
            {
                foreach (var fullyQualifiedName in fullyQualifiedNames)
                {
                    var tmp = ImportModule_RemotelyViaPsrpSession(importModuleOptions, null, fullyQualifiedName, psSession);
                    remotelyImportedModules.AddRange(tmp);
                }
            }

            return remotelyImportedModules;
        }

        private IList<PSModuleInfo> ImportModule_RemotelyViaPsrpSession(
            ImportModuleOptions importModuleOptions,
            string moduleName,
            ModuleSpecification fullyQualifiedName,
            PSSession psSession)
        {
            //
            // import the module in the remote session first
            //
            List<PSObject> remotelyImportedModules;
            using (var powerShell = System.Management.Automation.PowerShell.Create())
            {
                powerShell.Runspace = psSession.Runspace;
                powerShell.AddCommand("Import-Module");
                powerShell.AddParameter("DisableNameChecking", this.DisableNameChecking);
                powerShell.AddParameter("PassThru", true);

                if (fullyQualifiedName != null)
                {
                    powerShell.AddParameter("FullyQualifiedName", fullyQualifiedName);
                }
                else
                {
                    powerShell.AddParameter("Name", moduleName);

                    if (this.MinimumVersion != null)
                    {
                        powerShell.AddParameter("Version", this.MinimumVersion);
                    }
                    if (this.RequiredVersion != null)
                    {
                        powerShell.AddParameter("RequiredVersion", this.RequiredVersion);
                    }
                    if (this.MaximumVersion != null)
                    {
                        powerShell.AddParameter("MaximumVersion", this.MaximumVersion);
                    }
                }
                if (this.ArgumentList != null)
                {
                    powerShell.AddParameter("ArgumentList", this.ArgumentList);
                }
                if (this.BaseForce)
                {
                    powerShell.AddParameter("Force", true);
                }

                string errorMessageTemplate = string.Format(
                    CultureInfo.InvariantCulture,
                    Modules.RemoteDiscoveryRemotePsrpCommandFailed,
                    string.Format(CultureInfo.InvariantCulture, "Import-Module -Name '{0}'", moduleName));
                remotelyImportedModules = RemoteDiscoveryHelper.InvokePowerShell(
                    powerShell,
                    this.CancellationToken,
                    this,
                    errorMessageTemplate).ToList();
            }

            List<PSModuleInfo> result = new List<PSModuleInfo>();
            foreach (PSObject remotelyImportedModule in remotelyImportedModules)
            {
                PSPropertyInfo nameProperty = remotelyImportedModule.Properties["Name"];
                if (nameProperty != null)
                {
                    string remoteModuleName = (string)LanguagePrimitives.ConvertTo(
                        nameProperty.Value,
                        typeof(string),
                        CultureInfo.InvariantCulture);

                    PSPropertyInfo helpInfoProperty = remotelyImportedModule.Properties["HelpInfoUri"];
                    string remoteHelpInfoUri = null;
                    if (helpInfoProperty != null)
                    {
                        remoteHelpInfoUri = (string)LanguagePrimitives.ConvertTo(
                            helpInfoProperty.Value,
                            typeof(string),
                            CultureInfo.InvariantCulture);
                    }

                    PSPropertyInfo guidProperty = remotelyImportedModule.Properties["Guid"];
                    Guid remoteModuleGuid = Guid.Empty;
                    if (guidProperty != null)
                    {
                        LanguagePrimitives.TryConvertTo(guidProperty.Value, out remoteModuleGuid);
                    }

                    PSPropertyInfo versionProperty = remotelyImportedModule.Properties["Version"];
                    Version remoteModuleVersion = null;
                    if (versionProperty != null)
                    {
                        Version tmp;
                        if (LanguagePrimitives.TryConvertTo<Version>(versionProperty.Value, CultureInfo.InvariantCulture, out tmp))
                        {
                            remoteModuleVersion = tmp;
                        }
                    }

                    PSModuleInfo moduleInfo = ImportModule_RemotelyViaPsrpSession_SinglePreimportedModule(
                        importModuleOptions,
                        remoteModuleName,
                        remoteModuleVersion,
                        psSession);

                    // Set the HelpInfoUri and Guid as necessary, so that Save-Help can work with this module object
                    // to retrieve help files from the remote site.
                    if (moduleInfo != null)
                    {
                        // set the HelpInfoUri if it's needed
                        if (string.IsNullOrEmpty(moduleInfo.HelpInfoUri) && !string.IsNullOrEmpty(remoteHelpInfoUri))
                        {
                            moduleInfo.SetHelpInfoUri(remoteHelpInfoUri);
                        }

                        // set the Guid if it's needed
                        if (remoteModuleGuid != Guid.Empty)
                        {
                            moduleInfo.SetGuid(remoteModuleGuid);
                        }

                        result.Add(moduleInfo);
                    }
                }
            }

            return result;
        }

        private PSModuleInfo ImportModule_RemotelyViaPsrpSession_SinglePreimportedModule(
            ImportModuleOptions importModuleOptions,
            string remoteModuleName,
            Version remoteModuleVersion,
            PSSession psSession)
        {
            string temporaryModulePath = RemoteDiscoveryHelper.GetModulePath(
                remoteModuleName,
                remoteModuleVersion,
                psSession.ComputerName,
                this.Context.CurrentRunspace);
            string wildcardEscapedPath = WildcardPattern.Escape(temporaryModulePath);
            try
            {
                //
                // avoid importing a module twice
                //
                string localPsm1File = Path.Combine(temporaryModulePath, Path.GetFileName(temporaryModulePath) + ".psm1");
                PSModuleInfo alreadyImportedModule = this.IsModuleImportUnnecessaryBecauseModuleIsAlreadyLoaded(
                    localPsm1File, this.BasePrefix, importModuleOptions);
                if (alreadyImportedModule != null)
                {
                    return alreadyImportedModule;
                }

                //
                // create proxy module in a temporary folder
                //
                using (var powerShell = System.Management.Automation.PowerShell.Create(RunspaceMode.CurrentRunspace))
                {
                    powerShell.AddCommand("Export-PSSession");
                    powerShell.AddParameter("OutputModule", wildcardEscapedPath);
                    powerShell.AddParameter("AllowClobber", true);
                    powerShell.AddParameter("Module", remoteModuleName); // remoteModulePath is currently unsupported by Get-Command and implicit remoting
                    powerShell.AddParameter("Force", true);
                    powerShell.AddParameter("FormatTypeName", "*");
                    powerShell.AddParameter("Session", psSession);

                    string errorMessageTemplate = string.Format(
                        CultureInfo.InvariantCulture,
                        Modules.RemoteDiscoveryFailedToGenerateProxyForRemoteModule,
                        remoteModuleName);
                    int numberOfLocallyCreatedFiles = RemoteDiscoveryHelper.InvokePowerShell(powerShell, this.CancellationToken, this, errorMessageTemplate).Count();
                    if (numberOfLocallyCreatedFiles == 0)
                    {
                        return null;
                    }
                }

                //
                // rename the psd1 file
                //
                string localPsd1File = Path.Combine(temporaryModulePath, remoteModuleName + ".psd1");
                if (File.Exists(localPsd1File))
                {
                    File.Delete(localPsd1File);
                }
                File.Move(
                    sourceFileName: Path.Combine(temporaryModulePath, Path.GetFileName(temporaryModulePath) + ".psd1"),
                    destFileName: localPsd1File);
                string wildcardEscapedPsd1Path = WildcardPattern.Escape(localPsd1File);

                //
                // import the proxy module just as any other local module
                //
                object[] oldArgumentList = this.ArgumentList;
                try
                {
                    this.ArgumentList = new object[] { psSession };
                    ImportModule_LocallyViaName(importModuleOptions, wildcardEscapedPsd1Path);
                }
                finally
                {
                    this.ArgumentList = oldArgumentList;
                }

                //
                // make sure the temporary folder gets removed when the module is removed
                //
                PSModuleInfo moduleInfo;
                string psm1Path = Path.Combine(temporaryModulePath, Path.GetFileName(temporaryModulePath) + ".psm1");
                if (!this.Context.Modules.ModuleTable.TryGetValue(psm1Path, out moduleInfo))
                {
                    if (Directory.Exists(temporaryModulePath))
                    {
                        Directory.Delete(temporaryModulePath, recursive: true);
                    }
                    return null;
                }

                const string onRemoveScriptBody = @"
                    Microsoft.PowerShell.Management\Remove-Item `
                        -LiteralPath $temporaryModulePath `
                        -Force `
                        -Recurse `
                        -ErrorAction SilentlyContinue

                    if ($previousOnRemoveScript -ne $null)
                    {
                        & $previousOnRemoveScript $args
                    }
                    ";
                ScriptBlock onRemoveScriptBlock = this.Context.Engine.ParseScriptBlock(onRemoveScriptBody, false);
                onRemoveScriptBlock = onRemoveScriptBlock.GetNewClosure(); // create a separate scope for variables set below
                onRemoveScriptBlock.Module.SessionState.PSVariable.Set("temporaryModulePath", temporaryModulePath);
                onRemoveScriptBlock.Module.SessionState.PSVariable.Set("previousOnRemoveScript", moduleInfo.OnRemove);
                moduleInfo.OnRemove = onRemoveScriptBlock;

                return moduleInfo;
            }
            catch
            {
                if (Directory.Exists(temporaryModulePath))
                {
                    Directory.Delete(temporaryModulePath, recursive: true);
                }
                throw;
            }
        }

        #endregion PSSession parameterset

        #region CimSession parameterset

        private static bool IsNonEmptyManifestField(Hashtable manifestData, string key)
        {
            object value = manifestData[key];
            if (value == null)
            {
                return false;
            }

            object[] array;
            if (LanguagePrimitives.TryConvertTo(value, CultureInfo.InvariantCulture, out array))
            {
                return array.Length != 0;
            }
            else
            {
                return true;
            }
        }

        private bool IsMixedModePsCimModule(RemoteDiscoveryHelper.CimModule cimModule)
        {
            string temporaryModuleManifestPath = RemoteDiscoveryHelper.GetModulePath(cimModule.ModuleName, null, string.Empty, this.Context.CurrentRunspace);
            bool containedErrors = false;
            RemoteDiscoveryHelper.CimModuleFile mainManifestFile = cimModule.MainManifest;
            if (mainManifestFile == null)
            {
                return true;
            }
            Hashtable manifestData = RemoteDiscoveryHelper.ConvertCimModuleFileToManifestHashtable(
                    mainManifestFile,
                    temporaryModuleManifestPath,
                    this,
                    ref containedErrors);

            if (containedErrors || manifestData == null)
            {
                return false;
            }

            if (IsNonEmptyManifestField(manifestData, "ScriptsToProcess") ||
                IsNonEmptyManifestField(manifestData, "RequiredAssemblies"))
            {
                return true;
            }

            int numberOfSubmodules = 0;

            string[] nestedModules = null;
            if (LanguagePrimitives.TryConvertTo(manifestData["NestedModules"], CultureInfo.InvariantCulture, out nestedModules))
            {
                if (nestedModules != null)
                {
                    numberOfSubmodules += nestedModules.Length;
                }
            }

            object rootModuleValue = manifestData["RootModule"];
            if (rootModuleValue != null)
            {
                string rootModule;
                if (LanguagePrimitives.TryConvertTo(rootModuleValue, CultureInfo.InvariantCulture, out rootModule))
                {
                    if (!string.IsNullOrEmpty(rootModule))
                    {
                        numberOfSubmodules += 1;
                    }
                }
            }
            else
            {
                object moduleToProcessValue = manifestData["ModuleToProcess"];
                string moduleToProcess;
                if (moduleToProcessValue != null && LanguagePrimitives.TryConvertTo(moduleToProcessValue, CultureInfo.InvariantCulture, out moduleToProcess))
                {
                    if (!string.IsNullOrEmpty(moduleToProcess))
                    {
                        numberOfSubmodules += 1;
                    }
                }
            }

            int numberOfCmdletizationFiles = 0;
            foreach (var moduleFile in cimModule.ModuleFiles)
            {
                if (moduleFile.FileCode == RemoteDiscoveryHelper.CimFileCode.CmdletizationV1)
                    numberOfCmdletizationFiles++;
            }

            bool isMixedModePsCimModule = numberOfSubmodules > numberOfCmdletizationFiles;
            return isMixedModePsCimModule;
        }

        private void ImportModule_RemotelyViaCimSession(
            ImportModuleOptions importModuleOptions,
            string[] moduleNames,
            CimSession cimSession,
            Uri resourceUri,
            string cimNamespace)
        {
            //
            // find all remote PS-CIM modules
            //
            IEnumerable<RemoteDiscoveryHelper.CimModule> remoteModules = RemoteDiscoveryHelper.GetCimModules(
                cimSession,
                resourceUri,
                cimNamespace,
                moduleNames,
                false /* onlyManifests */,
                this,
                this.CancellationToken).ToList();

            IEnumerable<RemoteDiscoveryHelper.CimModule> remotePsCimModules = remoteModules.Where(cimModule => cimModule.IsPsCimModule);
            IEnumerable<string> remotePsrpModuleNames = remoteModules.Where(cimModule => !cimModule.IsPsCimModule).Select(cimModule => cimModule.ModuleName);
            foreach (string psrpModuleName in remotePsrpModuleNames)
            {
                string errorMessage = string.Format(
                    CultureInfo.InvariantCulture,
                    Modules.PsModuleOverCimSessionError,
                    psrpModuleName);
                ErrorRecord errorRecord = new ErrorRecord(
                    new ArgumentException(errorMessage),
                    "PsModuleOverCimSessionError",
                    ErrorCategory.InvalidArgument,
                    psrpModuleName);
                this.WriteError(errorRecord);
            }

            //
            // report an error if some modules were not found
            //
            IEnumerable<string> allFoundModuleNames = remoteModules.Select(cimModule => cimModule.ModuleName).ToList();
            foreach (string requestedModuleName in moduleNames)
            {
                var wildcardPattern = WildcardPattern.Get(requestedModuleName, WildcardOptions.IgnoreCase | WildcardOptions.CultureInvariant);
                bool requestedModuleWasFound = allFoundModuleNames.Any(foundModuleName => wildcardPattern.IsMatch(foundModuleName));
                if (!requestedModuleWasFound)
                {
                    string message = StringUtil.Format(Modules.ModuleNotFound, requestedModuleName);
                    FileNotFoundException fnf = new FileNotFoundException(message);
                    ErrorRecord er = new ErrorRecord(fnf, "Modules_ModuleNotFound",
                        ErrorCategory.ResourceUnavailable, requestedModuleName);
                    WriteError(er);
                }
            }

            //
            // import the PS-CIM modules
            //
            foreach (RemoteDiscoveryHelper.CimModule remoteCimModule in remotePsCimModules)
            {
                ImportModule_RemotelyViaCimModuleData(importModuleOptions, remoteCimModule, cimSession);
            }
        }

        private bool IsPs1xmlFileHelper_IsPresentInEntries(RemoteDiscoveryHelper.CimModuleFile cimModuleFile, IEnumerable<string> manifestEntries)
        {
            if (manifestEntries.Any(s => s.EndsWith(cimModuleFile.FileName, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (manifestEntries.Any(s => FixupFileName("", s, ".ps1xml").EndsWith(cimModuleFile.FileName, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return false;
        }

        private bool IsPs1xmlFileHelper(RemoteDiscoveryHelper.CimModuleFile cimModuleFile, Hashtable manifestData, string goodKey, string badKey)
        {
            if (!Path.GetExtension(cimModuleFile.FileName).Equals(".ps1xml", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            List<string> goodEntries;
            if (!this.GetListOfStringsFromData(manifestData, null, goodKey, 0, out goodEntries))
            {
                goodEntries = new List<string>();
            }
            if (goodEntries == null)
            {
                goodEntries = new List<string>();
            }

            List<string> badEntries;
            if (!this.GetListOfStringsFromData(manifestData, null, badKey, 0, out badEntries))
            {
                badEntries = new List<string>();
            }
            if (badEntries == null)
            {
                badEntries = new List<string>();
            }

            bool presentInGoodEntries = IsPs1xmlFileHelper_IsPresentInEntries(cimModuleFile, goodEntries);
            bool presentInBadEntries = IsPs1xmlFileHelper_IsPresentInEntries(cimModuleFile, badEntries);
            return presentInGoodEntries && !presentInBadEntries;
        }

        private bool IsTypesPs1XmlFile(RemoteDiscoveryHelper.CimModuleFile cimModuleFile, Hashtable manifestData)
        {
            return IsPs1xmlFileHelper(cimModuleFile, manifestData, goodKey: "TypesToProcess", badKey: "FormatsToProcess");
        }

        private bool IsFormatPs1XmlFile(RemoteDiscoveryHelper.CimModuleFile cimModuleFile, Hashtable manifestData)
        {
            return IsPs1xmlFileHelper(cimModuleFile, manifestData, goodKey: "FormatsToProcess", badKey: "TypesToProcess");
        }

        private static bool IsCmdletizationFile(RemoteDiscoveryHelper.CimModuleFile cimModuleFile)
        {
            return cimModuleFile.FileCode == RemoteDiscoveryHelper.CimFileCode.CmdletizationV1;
        }

        private IEnumerable<string> CreateCimModuleFiles(
            RemoteDiscoveryHelper.CimModule remoteCimModule,
            RemoteDiscoveryHelper.CimFileCode fileCode,
            Func<RemoteDiscoveryHelper.CimModuleFile, bool> filesFilter,
            string temporaryModuleDirectory)
        {
            string fileNameTemplate = null;
            switch (fileCode)
            {
                case RemoteDiscoveryHelper.CimFileCode.CmdletizationV1:
                    fileNameTemplate = "{0}_{1}.cdxml";
                    break;
                case RemoteDiscoveryHelper.CimFileCode.TypesV1:
                    fileNameTemplate = "{0}_{1}.types.ps1xml";
                    break;
                case RemoteDiscoveryHelper.CimFileCode.FormatV1:
                    fileNameTemplate = "{0}_{1}.format.ps1xml";
                    break;
                default:
                    Dbg.Assert(false, "Unrecognized file code");
                    break;
            }

            List<string> relativePathsToCreatedFiles = new List<string>();
            foreach (RemoteDiscoveryHelper.CimModuleFile file in remoteCimModule.ModuleFiles)
            {
                if (!filesFilter(file))
                {
                    continue;
                }

                string originalFileName = Path.GetFileName(file.FileName);
                string fileName = string.Format(
                    CultureInfo.InvariantCulture,
                    fileNameTemplate,
                    originalFileName.Substring(0, Math.Min(originalFileName.Length, 20)),
                    Path.GetRandomFileName());
                relativePathsToCreatedFiles.Add(fileName);

                string fullPath = Path.Combine(temporaryModuleDirectory, fileName);
                File.WriteAllBytes(
                    fullPath,
                    file.RawFileData);

                AlternateDataStreamUtilities.SetZoneOfOrigin(fullPath, SecurityZone.Intranet);
            }

            return relativePathsToCreatedFiles;
        }

        private PSModuleInfo ImportModule_RemotelyViaCimModuleData(
            ImportModuleOptions importModuleOptions,
            RemoteDiscoveryHelper.CimModule remoteCimModule,
            CimSession cimSession)
        {
            try
            {
                if (remoteCimModule.MainManifest == null)
                {
                    string errorMessage = string.Format(
                        CultureInfo.InvariantCulture,
                        Modules.EmptyModuleManifest,
                        remoteCimModule.ModuleName + ".psd1");
                    ArgumentException argumentException = new ArgumentException(errorMessage);
                    throw argumentException;
                }

                bool containedErrors = false;
                PSModuleInfo moduleInfo = null;

                //
                // read the original manifest
                //
                string temporaryModuleDirectory = RemoteDiscoveryHelper.GetModulePath(
                    remoteCimModule.ModuleName,
                    null,
                    cimSession.ComputerName,
                    this.Context.CurrentRunspace);
                string temporaryModuleManifestPath = Path.Combine(
                    temporaryModuleDirectory,
                    remoteCimModule.ModuleName + ".psd1");

                Hashtable data = null;
                Hashtable localizedData = null;
                {
                    ScriptBlockAst scriptBlockAst = null;
                    Token[] throwAwayTokens;
                    ParseError[] parseErrors;
                    scriptBlockAst = Parser.ParseInput(
                        remoteCimModule.MainManifest.FileData,
                        temporaryModuleManifestPath,
                        out throwAwayTokens,
                        out parseErrors);
                    if ((scriptBlockAst == null) ||
                        (parseErrors != null && parseErrors.Length > 0))
                    {
                        throw new ParseException(parseErrors);
                    }

                    ScriptBlock scriptBlock = new ScriptBlock(scriptBlockAst, isFilter: false);
                    data = LoadModuleManifestData(
                        temporaryModuleManifestPath,
                        scriptBlock,
                        ModuleManifestMembers,
                        ManifestProcessingFlags.NullOnFirstError | ManifestProcessingFlags.WriteErrors, /* - don't load elements */
                        ref containedErrors);

                    if ((data == null) || containedErrors)
                    {
                        return null;
                    }
                    localizedData = data;
                }

                // 
                // flatten module contents and rewrite the manifest to point to the flattened file hierarchy
                //

                // recalculate module path, taking into account the module version fetched above
                Version moduleVersion;
                if (!GetScalarFromData<Version>(data, null, "ModuleVersion", 0, out moduleVersion))
                {
                    moduleVersion = null;
                }
                temporaryModuleDirectory = RemoteDiscoveryHelper.GetModulePath(
                    remoteCimModule.ModuleName,
                    moduleVersion,
                    cimSession.ComputerName,
                    this.Context.CurrentRunspace);
                temporaryModuleManifestPath = Path.Combine(
                    temporaryModuleDirectory,
                    remoteCimModule.ModuleName + ".psd1");
                // avoid loading an already loaded module
                PSModuleInfo alreadyImportedModule = this.IsModuleImportUnnecessaryBecauseModuleIsAlreadyLoaded(
                    temporaryModuleManifestPath, this.BasePrefix, importModuleOptions);
                if (alreadyImportedModule != null)
                {
                    return alreadyImportedModule;
                }
                try
                {
                    Directory.CreateDirectory(temporaryModuleDirectory);

                    IEnumerable<string> typesToProcess = CreateCimModuleFiles(
                        remoteCimModule,
                        RemoteDiscoveryHelper.CimFileCode.TypesV1,
                        cimModuleFile => IsTypesPs1XmlFile(cimModuleFile, data),
                        temporaryModuleDirectory);
                    IEnumerable<string> formatsToProcess = CreateCimModuleFiles(
                        remoteCimModule,
                        RemoteDiscoveryHelper.CimFileCode.FormatV1,
                        cimModuleFile => IsFormatPs1XmlFile(cimModuleFile, data),
                        temporaryModuleDirectory);
                    IEnumerable<string> nestedModules = CreateCimModuleFiles(
                        remoteCimModule,
                        RemoteDiscoveryHelper.CimFileCode.CmdletizationV1,
                        IsCmdletizationFile,
                        temporaryModuleDirectory);
                    data = RemoteDiscoveryHelper.RewriteManifest(
                        data,
                        nestedModules: nestedModules,
                        typesToProcess: typesToProcess,
                        formatsToProcess: formatsToProcess);
                    localizedData = RemoteDiscoveryHelper.RewriteManifest(localizedData);

                    //
                    // import the module 
                    // (from memory - this avoids the authenticode signature problems 
                    // that would be introduced by rewriting the contents of the manifest)
                    //
                    moduleInfo = LoadModuleManifest(
                        temporaryModuleManifestPath,
                        null, //scriptInfo
                        data,
                        localizedData,
                        ManifestProcessingFlags.LoadElements | ManifestProcessingFlags.WriteErrors | ManifestProcessingFlags.NullOnFirstError,
                        BaseMinimumVersion,
                        BaseMaximumVersion,
                        BaseRequiredVersion,
                        BaseGuid,
                        ref importModuleOptions,
                        ref containedErrors);
                    if (moduleInfo == null)
                    {
                        return null;
                    }
                    foreach (PSModuleInfo nestedModule in moduleInfo.NestedModules)
                    {
                        Type cmdletAdapter;
                        bool gotCmdletAdapter = PSPrimitiveDictionary.TryPathGet(
                            nestedModule.PrivateData as IDictionary,
                            out cmdletAdapter,
                            "CmdletsOverObjects",
                            "CmdletAdapter");
                        Dbg.Assert(gotCmdletAdapter, "PrivateData from cdxml should always include cmdlet adapter");
                        if (!cmdletAdapter.AssemblyQualifiedName.Equals(StringLiterals.DefaultCmdletAdapter, StringComparison.OrdinalIgnoreCase))
                        {
                            string errorMessage = string.Format(
                                CultureInfo.InvariantCulture,
                                CmdletizationCoreResources.ImportModule_UnsupportedCmdletAdapter,
                                cmdletAdapter.FullName);
                            ErrorRecord errorRecord = new ErrorRecord(
                                new InvalidOperationException(errorMessage),
                                "UnsupportedCmdletAdapter",
                                ErrorCategory.InvalidData,
                                cmdletAdapter);
                            this.ThrowTerminatingError(errorRecord);
                        }
                    }
                    if (IsMixedModePsCimModule(remoteCimModule))
                    {
                        // warn that some commands have not been imported
                        string warningMessage = string.Format(
                            CultureInfo.InvariantCulture,
                            Modules.MixedModuleOverCimSessionWarning,
                            remoteCimModule.ModuleName);
                        this.WriteWarning(warningMessage);
                    }

                    //
                    // store the default session 
                    //
                    Dbg.Assert(moduleInfo.ModuleType == ModuleType.Manifest, "Remote discovery should always produce a 'manifest' module");
                    Dbg.Assert(moduleInfo.NestedModules != null, "Remote discovery should always produce a 'manifest' module with nested modules entry");
                    Dbg.Assert(moduleInfo.NestedModules.Count > 0, "Remote discovery should always produce a 'manifest' module with some nested modules");
                    foreach (PSModuleInfo nestedModule in moduleInfo.NestedModules)
                    {
                        IDictionary cmdletsOverObjectsPrivateData;
                        bool cmdletsOverObjectsPrivateDataWasFound = PSPrimitiveDictionary.TryPathGet<IDictionary>(
                            nestedModule.PrivateData as IDictionary,
                            out cmdletsOverObjectsPrivateData,
                            ScriptWriter.PrivateDataKey_CmdletsOverObjects);
                        Dbg.Assert(cmdletsOverObjectsPrivateDataWasFound, "Cmdletization should always set the PrivateData properly");
                        cmdletsOverObjectsPrivateData[ScriptWriter.PrivateDataKey_DefaultSession] = cimSession;
                    }

                    //
                    // make sure the temporary folder gets removed when the module is removed
                    //
                    const string onRemoveScriptBody =
                        @"
                        Microsoft.PowerShell.Management\Remove-Item `
                            -LiteralPath $temporaryModulePath `
                            -Force `
                            -Recurse `
                            -ErrorAction SilentlyContinue

                        if ($previousOnRemoveScript -ne $null)
                        {
                            & $previousOnRemoveScript $args
                        }
                        ";
                    ScriptBlock onRemoveScriptBlock = this.Context.Engine.ParseScriptBlock(onRemoveScriptBody, false);
                    onRemoveScriptBlock = onRemoveScriptBlock.GetNewClosure();
                    // create a separate scope for variables set below
                    onRemoveScriptBlock.Module.SessionState.PSVariable.Set("temporaryModulePath", temporaryModuleDirectory);
                    onRemoveScriptBlock.Module.SessionState.PSVariable.Set("previousOnRemoveScript", moduleInfo.OnRemove);
                    moduleInfo.OnRemove = onRemoveScriptBlock;

                    //
                    // Some processing common for local and remote modules
                    //
                    AddModuleToModuleTables(
                        this.Context,
                        this.TargetSessionState.Internal,
                        moduleInfo);
                    if (BasePassThru)
                    {
                        WriteObject(moduleInfo);
                    }

                    return moduleInfo;
                }
                catch
                {
                    if (Directory.Exists(temporaryModuleDirectory))
                    {
                        Directory.Delete(temporaryModuleDirectory, recursive: true);
                    }
                    throw;
                }
                finally
                {
                    if (moduleInfo == null)
                    {
                        if (Directory.Exists(temporaryModuleDirectory))
                        {
                            Directory.Delete(temporaryModuleDirectory, recursive: true);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ErrorRecord errorRecord = RemoteDiscoveryHelper.GetErrorRecordForProcessingOfCimModule(e, remoteCimModule.ModuleName);
                this.WriteError(errorRecord);
                return null;
            }
        }

        #endregion CimSession parameterset

        #endregion Remote import

        #region Cancellation support

        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private CancellationToken CancellationToken
        {
            get
            {
                return _cancellationTokenSource.Token;
            }
        }

        /// <summary>
        /// When overridden in the derived class, interrupts currently
        /// running code within the command. It should interrupt BeginProcessing,
        /// ProcessRecord, and EndProcessing.
        /// Default implementation in the base class just returns.
        /// </summary>
        protected override void StopProcessing()
        {
            _cancellationTokenSource.Cancel();
        }

        #endregion

        #region IDisposable Members

        /// <summary>
        /// Releases resources associated with this object
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases resources associated with this object
        /// </summary>
        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _cancellationTokenSource.Dispose();
            }

            _disposed = true;
        }

        private bool _disposed;

        #endregion

        /// <summary>
        /// BeginProcessing override
        /// </summary>
        protected override void BeginProcessing()
        {
            // Make sure that only one of (Global | Scope) is specified
            if (Global.IsPresent && _isScopeSpecified)
            {
                InvalidOperationException ioe = new InvalidOperationException(Modules.GlobalAndScopeParameterCannotBeSpecifiedTogether);
                ErrorRecord er = new ErrorRecord(ioe, "Modules_GlobalAndScopeParameterCannotBeSpecifiedTogether",
                                                 ErrorCategory.InvalidOperation, null);
                ThrowTerminatingError(er);
            }
            if (!string.IsNullOrEmpty(Scope) && Scope.Equals(StringLiterals.Global, StringComparison.OrdinalIgnoreCase))
            {
                base.BaseGlobal = true;
            }
        }

        /// <summary>
        /// Load the specified modules...
        /// </summary>
        /// <remarks>
        /// Examples:
        ///     c:\temp\mdir\mdir.psm1  # load absolute path
        ///     ./mdir.psm1             # load relative path
        ///     c:\temp\mdir\mdir       # resolve by using extensions. mdir is a directory, mdir.xxx is a file.
        ///     c:\temp\mdir            # load default module if mdir is directory
        ///     module                  # $PSScriptRoot/module/module.psd1 (ps1,psm1,dll)
        ///     module/foobar.psm1      # $PSScriptRoot/module/module.psm1
        ///     module/foobar           # $PSScriptRoot/module/foobar.XXX if foobar is not a directory...
        ///     module/foobar           # $PSScriptRoot/module/foobar is a directory and $PSScriptRoot/module/foobar/foobar.XXX exists
        ///     module/foobar/foobar.XXX
        /// </remarks>
        protected override void ProcessRecord()
        {
            if (BaseMaximumVersion != null && BaseMaximumVersion != null && BaseMaximumVersion < BaseMinimumVersion)
            {
                string message = StringUtil.Format(Modules.MinimumVersionAndMaximumVersionInvalidRange, BaseMinimumVersion, BaseMaximumVersion);
                throw new PSArgumentOutOfRangeException(message);
            }
            ImportModuleOptions importModuleOptions = new ImportModuleOptions();
            importModuleOptions.NoClobber = NoClobber;
            if (!string.IsNullOrEmpty(Scope) && Scope.Equals(StringLiterals.Local, StringComparison.OrdinalIgnoreCase))
            {
                importModuleOptions.Local = true;
            }

            if (this.ParameterSetName.Equals(ParameterSet_ModuleInfo, StringComparison.OrdinalIgnoreCase))
            {
                // Process all of the specified PSModuleInfo objects. These would typically be coming in as a result
                // of doing Get-Module -list
                foreach (PSModuleInfo module in ModuleInfo)
                {
                    RemoteDiscoveryHelper.DispatchModuleInfoProcessing(
                        module,
                        localAction: delegate ()
                                         {
                                             ImportModule_ViaLocalModuleInfo(importModuleOptions, module);
                                             SetModuleBaseForEngineModules(module.Name, this.Context);
                                         },

                        cimSessionAction: (cimSession, resourceUri, cimNamespace) => ImportModule_RemotelyViaCimSession(
                            importModuleOptions,
                            new string[] { module.Name },
                            cimSession,
                            resourceUri,
                            cimNamespace),

                        psSessionAction: psSession => ImportModule_RemotelyViaPsrpSession(
                            importModuleOptions,
                            new string[] { module.Path },
                            null,
                            psSession));
                }
            }
            else if (this.ParameterSetName.Equals(ParameterSet_Assembly, StringComparison.OrdinalIgnoreCase))
            {
                // Now load all of the supplied assemblies...
                if (Assembly != null)
                {
                    foreach (Assembly suppliedAssembly in Assembly)
                    {
                        ImportModule_ViaAssembly(importModuleOptions, suppliedAssembly);
                    }
                }
            }
            else if (this.ParameterSetName.Equals(ParameterSet_Name, StringComparison.OrdinalIgnoreCase))
            {
                foreach (string name in Name)
                {
                    PSModuleInfo foundModule = ImportModule_LocallyViaName(importModuleOptions, name);
                    if (null != foundModule)
                    {
                        SetModuleBaseForEngineModules(foundModule.Name, this.Context);

                        TelemetryAPI.ReportModuleLoad(foundModule);
                    }
                }
            }
            else if (this.ParameterSetName.Equals(ParameterSet_ViaPsrpSession, StringComparison.OrdinalIgnoreCase))
            {
                ImportModule_RemotelyViaPsrpSession(importModuleOptions, this.Name, null, this.PSSession);
            }
            else if (this.ParameterSetName.Equals(ParameterSet_ViaCimSession, StringComparison.OrdinalIgnoreCase))
            {
                ImportModule_RemotelyViaCimSession(importModuleOptions, this.Name, this.CimSession, this.CimResourceUri, this.CimNamespace);
            }
            else if (this.ParameterSetName.Equals(ParameterSet_FQName, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var modulespec in FullyQualifiedName)
                {
                    RequiredVersion = modulespec.RequiredVersion;
                    MinimumVersion = modulespec.Version;
                    MaximumVersion = modulespec.MaximumVersion;
                    BaseGuid = modulespec.Guid;

                    PSModuleInfo foundModule = ImportModule_LocallyViaName(importModuleOptions, modulespec.Name);
                    if (null != foundModule)
                    {
                        SetModuleBaseForEngineModules(foundModule.Name, this.Context);
                    }
                }
            }
            else if (this.ParameterSetName.Equals(ParameterSet_FQName_ViaPsrpSession, StringComparison.OrdinalIgnoreCase))
            {
                ImportModule_RemotelyViaPsrpSession(importModuleOptions, null, FullyQualifiedName, this.PSSession);
            }
            else
            {
                Dbg.Assert(false, "Unrecognized parameter set");
            }
        }

        private void SetModuleBaseForEngineModules(string moduleName, System.Management.Automation.ExecutionContext context)
        {
            // Set modulebase of engine modules to point to $pshome
            // This is so that Get-Help can load the correct help. 
            if (InitialSessionState.IsEngineModule(moduleName))
            {
                foreach (var m in context.EngineSessionState.ModuleTable.Values)
                {
                    if (m.Name.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                    {
                        m.SetModuleBase(Utils.GetApplicationBase(Utils.DefaultPowerShellShellID));
                        // Also set  ModuleBase for nested modules of Engine modules
                        foreach (var nestedModule in m.NestedModules)
                        {
                            nestedModule.SetModuleBase(Utils.GetApplicationBase(Utils.DefaultPowerShellShellID));
                        }
                    }
                }

                foreach (var m in context.Modules.ModuleTable.Values)
                {
                    if (m.Name.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                    {
                        m.SetModuleBase(Utils.GetApplicationBase(Utils.DefaultPowerShellShellID));
                        // Also set  ModuleBase for nested modules of Engine modules
                        foreach (var nestedModule in m.NestedModules)
                        {
                            nestedModule.SetModuleBase(Utils.GetApplicationBase(Utils.DefaultPowerShellShellID));
                        }
                    }
                }
            }
        }
    }
} // Microsoft.PowerShell.Commands
