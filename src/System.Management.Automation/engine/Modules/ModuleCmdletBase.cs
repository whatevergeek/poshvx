/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Internal;
using System.Management.Automation.Runspaces;
using System.Management.Automation.Security;
using System.Reflection;
using System.Text;
using System.Xml;
using Microsoft.PowerShell.Cmdletization;
using Dbg = System.Management.Automation.Diagnostics;

#if CORECLR
// Some APIs are missing from System.Environment. We use System.Management.Automation.Environment as a proxy type:
//  - for missing APIs, System.Management.Automation.Environment has extension implementation.
//  - for existing APIs, System.Management.Automation.Environment redirect the call to System.Environment.
using Environment = System.Management.Automation.Environment;
#endif

//
// Now define the set of commands for manipulating modules.
//
namespace Microsoft.PowerShell.Commands
{
    #region ModuleCmdletBase class

    /// <summary>
    /// This is the base class for some of the module cmdlets. It contains a number of
    /// utility functions for these classes.
    /// </summary>
    public class ModuleCmdletBase : PSCmdlet
    {
        /// <summary>
        /// Flags defining how a module manifest should be processed
        /// </summary>
        [Flags]
        internal enum ManifestProcessingFlags
        {
            /// <summary>
            /// Write errors (otherwise no non-terminating-errors are written)
            /// </summary>
            WriteErrors = 0x1,

            /// <summary>
            /// Return null on first error (otherwise we try to process other elements of the manifest)
            /// </summary>
            NullOnFirstError = 0x2,

            /// <summary>
            /// Load elements of the manifest (i.e. types/format.ps1xml, nested modules, etc.)
            /// </summary>
            LoadElements = 0x4,

            /// <summary>
            /// Write warnings 
            /// </summary>
            WriteWarnings = 0x8,

            /// <summary>
            /// Force full module manifest processing.
            /// </summary>
            Force = 0x10,

            /// <summary>
            /// Ignore PowerShellHostName and PowerShellHostVersion while
            /// processing Manifest fields.
            /// This is used for GetModule where loading elements does not happen.
            /// </summary>
            IgnoreHostNameAndHostVersion = 0x20,
        }

        /// <summary>
        /// Options set during module import
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        protected internal struct ImportModuleOptions
        {
            /// <summary>
            /// Holds the value of NoClobber parameter in Import-Module
            /// This is used when importing modules.
            /// </summary>
            internal bool NoClobber;

            /// <summary>
            /// If Scope parameter is Local, this is true. 
            /// </summary>
            internal bool Local;

            /// <summary>
            /// Win8:90779
            /// ServiceCore assembly is automatically imported as a nested module to process the NestedModules/RootModules/RequiredAssemblies fields in module manifests.
            /// This property is set to ensure that Import-Module of a manifest module does not expose "Import-psworkflow" cmdlet. 
            ///  </summary>
            internal bool ServiceCoreAutoAdded;
        }

        /// <summary>
        /// This parameter specified a prefix used to modify names of imported commands
        /// </summary>
        internal string BasePrefix { set; get; } = string.Empty;

        /// <summary>
        /// Flags -force operations
        /// </summary>
        internal bool BaseForce { get; set; }

        /// <summary>
        /// Flags -global operations (affects what gets returned by TargetSessionState)
        /// </summary>
        internal bool BaseGlobal { get; set; }

        internal SessionState TargetSessionState
        {
            get
            {
                if (BaseGlobal)
                {
                    return this.Context.TopLevelSessionState.PublicSessionState;
                }
                else
                {
                    return this.Context.SessionState;
                }
            }
        }

        /// <summary>
        /// Flags -passthru operations
        /// </summary>
        internal bool BasePassThru { get; set; }

        /// <summary>
        /// Flags -passthru operations
        /// </summary>
        internal bool BaseAsCustomObject { get; set; }

        /// <summary>
        /// Wildcard patterns for the function to import
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Cmdlet parameters.")]
        internal List<WildcardPattern> BaseFunctionPatterns { get; set; }

        /// <summary>
        /// Wildcard patterns for the cmdlets to import
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Cmdlet parameters.")]
        internal List<WildcardPattern> BaseCmdletPatterns { get; set; }

        /// <summary>
        /// Wildcard patterns for the variables to import
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Cmdlet parameters.")]
        internal List<WildcardPattern> BaseVariablePatterns { get; set; }

        /// <summary>
        /// Wildcard patterns for the aliases to import
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Cmdlet parameters.")]
        internal List<WildcardPattern> BaseAliasPatterns { get; set; }

        /// <summary>
        /// The minimum version number to check the module against. Used the underlying property
        /// for derived cmdlet parameters.
        /// </summary>
        internal Version BaseMinimumVersion { get; set; }

        /// <summary>
        /// The maximum version number to check the module against. Used the underlying property
        /// for derived cmdlet parameters.
        /// </summary>
        internal Version BaseMaximumVersion { get; set; }

        /// <summary>
        /// The version number to check the module against. Used the underlying property
        /// for derived cmdlet parameters.
        /// </summary>
        internal Version BaseRequiredVersion { get; set; }

        /// <summary>
        /// The Guid to check the module against. Used the underlying property
        /// for derived cmdlet parameters.
        /// </summary>
        internal Guid? BaseGuid { get; set; }

        /// <summary>
        /// The arguments to pass to the scriptblock used to create the module
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        protected object[] BaseArgumentList { get; set; }

        /// <summary>
        /// Disable warnings on cmdlet and function names that have non-standard verbs
        /// or non-standard characters in the noun.
        /// </summary>
        protected bool BaseDisableNameChecking { get; set; } = true;

        /// <summary>
        /// Add module path to app domain level module path cache if name is not rooted
        /// </summary>
        protected bool AddToAppDomainLevelCache { get; set; } = false;

        /// <summary>
        /// A handy match all pattern used to initialize various import and export lists...
        /// </summary>
        internal List<WildcardPattern> MatchAll
        {
            get
            {
                if (_matchAll == null)
                {
                    _matchAll = new List<WildcardPattern>();
                    _matchAll.Add(WildcardPattern.Get("*", WildcardOptions.IgnoreCase));
                }
                return _matchAll;
            }
        }
        private List<WildcardPattern> _matchAll;

        // The list of commands permitted in a module manifest
        internal static string[] PermittedCmdlets = new string[] {
            "Import-LocalizedData", "ConvertFrom-StringData", "Write-Host", "Out-Host", "Join-Path" };

        internal static string[] ModuleManifestMembers = new string[] {
            "ModuleToProcess",
            "NestedModules",
            "GUID",
            "Author",
            "CompanyName",
            "Copyright",
            "ModuleVersion",
            "Description",
            "PowerShellVersion",
            "PowerShellHostName",
            "PowerShellHostVersion",
            "CLRVersion",
            "DotNetFrameworkVersion",
            "ProcessorArchitecture",
            "RequiredModules",
            "TypesToProcess",
            "FormatsToProcess",
            "ScriptsToProcess",
            "PrivateData",
            "RequiredAssemblies",
            "ModuleList",
            "FileList",
            "FunctionsToExport",
            "VariablesToExport",
            "AliasesToExport",
            "CmdletsToExport",
            "DscResourcesToExport",
            "CompatiblePSEditions",
            "HelpInfoURI",
            "RootModule",
            "DefaultCommandPrefix"
        };

        private static string[] s_moduleVersionMembers = new string[] {
            "ModuleName",
            "GUID",
            "ModuleVersion"
        };

        private static List<string> s_serviceCoreAssemblyCmdlets = new List<string>(new string[] {
            "Microsoft.PowerShell.Workflow.ServiceCore\\Import-PSWorkflow",
            "Microsoft.PowerShell.Workflow.ServiceCore\\New-PSWorkflowExecutionOption",
        });

        private Dictionary<string, PSModuleInfo> _currentlyProcessingModules = new Dictionary<string, PSModuleInfo>();

        internal bool LoadUsingModulePath(bool found, IEnumerable<string> modulePath, string name, SessionState ss,
            ImportModuleOptions options, ManifestProcessingFlags manifestProcessingFlags, out PSModuleInfo module)
        {
            return LoadUsingModulePath(null, found, modulePath, name, ss, options, manifestProcessingFlags, out module);
        }

        internal bool LoadUsingModulePath(PSModuleInfo parentModule, bool found, IEnumerable<string> modulePath, string name, SessionState ss,
            ImportModuleOptions options, ManifestProcessingFlags manifestProcessingFlags, out PSModuleInfo module)
        {
            string extension = Path.GetExtension(name);
            string fileBaseName;
            module = null;
            if (String.IsNullOrEmpty(extension) || !ModuleIntrinsics.IsPowerShellModuleExtension(extension))
            {
                fileBaseName = name;
                extension = null;
            }
            else
                fileBaseName = name.Substring(0, name.Length - extension.Length);

            // Now search using the module path...
            foreach (string path in modulePath)
            {
                // a name takes the form of .../moduleDir/moduleName.<ext>
                // if only one name has been specified, then the moduleDir
                // and moduleName are identical and repeated...
                // Also append th ename if the path currently points at a directory
                string qualifiedPath = Path.Combine(path, fileBaseName);

                // Load the latest valid version if it is a multi-version module directory
                module = LoadUsingMultiVersionModuleBase(qualifiedPath, manifestProcessingFlags, options, out found);

                if (!found)
                {
                    if (name.IndexOfAny(Utils.Separators.Directory) == -1)
                    {
                        qualifiedPath = Path.Combine(qualifiedPath, fileBaseName);
                    }
                    else if (Directory.Exists(qualifiedPath))
                    {
                        // if it points to a directory, add the basename back onto the path...
                        qualifiedPath = Path.Combine(qualifiedPath, Path.GetFileName(fileBaseName));
                    }

                    module = LoadUsingExtensions(parentModule, name, qualifiedPath, extension, null, this.BasePrefix, ss, options, manifestProcessingFlags, out found);
                }

                if (found)
                {
                    break;
                }
            }

            if (found)
            {
                // Cache the module's exported commands after importing it, or if the -Refresh flag is used on
                // "Get-Module -List"
                if ((module != null) && !module.HadErrorsLoading)
                {
                    if (module.ExportedWorkflows != null && module.ExportedWorkflows.Count > 0 && Utils.IsRunningFromSysWOW64())
                    {
                        throw new NotSupportedException(AutomationExceptions.WorkflowDoesNotSupportWOW64);
                    }

                    AnalysisCache.CacheModuleExports(module, Context);
                }
            }
            return found;
        }

        /// <summary>
        /// Loads the latest valid version if moduleBase is a multi-versioned module directory
        /// </summary>
        /// <param name="moduleBase">module directory path</param>
        /// <param name="manifestProcessingFlags">The flag that indicate manifest processing option</param>
        /// <param name="importModuleOptions">The set of options that are used while importing a module</param>
        /// <param name="found">True if a module was found</param>
        /// <returns></returns>
        internal PSModuleInfo LoadUsingMultiVersionModuleBase(string moduleBase, ManifestProcessingFlags manifestProcessingFlags, ImportModuleOptions importModuleOptions, out bool found)
        {
            PSModuleInfo foundModule = null;
            found = false;

            foreach (var version in ModuleUtils.GetModuleVersionSubfolders(moduleBase))
            {
                // Skip the version folder if it is not equal to the required version or does not satisfy the minimum/maximum version criteria
                if ((BaseRequiredVersion != null && !BaseRequiredVersion.Equals(version))
                    || (BaseMinimumVersion != null && BaseRequiredVersion == null && version < BaseMinimumVersion)
                    || (BaseMaximumVersion != null && BaseRequiredVersion == null && version > BaseMaximumVersion))
                {
                    continue;
                }

                var qualifiedPathWithVersion = Path.Combine(moduleBase,
                                                            Path.Combine(version.ToString(), Path.GetFileName(moduleBase)));
                string manifestPath = qualifiedPathWithVersion + StringLiterals.PowerShellDataFileExtension;
                var isValidModuleVersion = false;
                if (File.Exists(manifestPath))
                {
                    isValidModuleVersion = version.Equals(ModuleIntrinsics.GetManifestModuleVersion(manifestPath));

                    if (isValidModuleVersion)
                    {
                        foundModule = LoadUsingExtensions(null, moduleBase, qualifiedPathWithVersion,
                                                            StringLiterals.PowerShellDataFileExtension,
                                                            null,
                                                            this.BasePrefix, /*SessionState*/ null,
                                                            importModuleOptions,
                                                            manifestProcessingFlags,
                                                            out found);
                        if (found)
                        {
                            break;
                        }
                    }
                }

                if (!isValidModuleVersion)
                {
                    WriteVerbose(String.Format(CultureInfo.InvariantCulture, Modules.SkippingInvalidModuleVersionFolder,
                                                version.ToString(), moduleBase));
                }
            }

            return foundModule;
        }

        /// <summary>
        /// Load and execute the manifest psd1 file or a localized manifest psd1 file.
        /// </summary>
        private Hashtable LoadModuleManifestData(
            ExternalScriptInfo scriptInfo,
            string[] validMembers,
            ManifestProcessingFlags manifestProcessingFlags,
            ref bool containedErrors)
        {
            try
            {
                return LoadModuleManifestData(scriptInfo.Path, scriptInfo.ScriptBlock, validMembers, manifestProcessingFlags, ref containedErrors);
            }
            catch (RuntimeException pe)
            {
                if (0 != (manifestProcessingFlags & ManifestProcessingFlags.WriteErrors))
                {
                    string message = StringUtil.Format(Modules.InvalidModuleManifest, scriptInfo.Path, pe.Message);
                    MissingMemberException mm = new MissingMemberException(message);
                    ErrorRecord er = new ErrorRecord(mm, "Modules_InvalidManifest",
                        ErrorCategory.ResourceUnavailable, scriptInfo.Path);
                    WriteError(er);
                }
                containedErrors = true;
                return null;
            }
        }

        /// <summary>
        /// Extra variables that are allowed to be referenced in module manifest file
        /// </summary>
        private static readonly string[] s_extraAllowedVariables = new string[] { "PSScriptRoot", "PSEdition" };

        /// <summary>
        /// Load and execute the manifest psd1 file or a localized manifest psd1 file.
        /// </summary>
        internal Hashtable LoadModuleManifestData(
            string moduleManifestPath,
            ScriptBlock scriptBlock,
            string[] validMembers,
            ManifestProcessingFlags manifestProcessingFlags,
            ref bool containedErrors)
        {
            string message;

            var importingModule = 0 != (manifestProcessingFlags & ManifestProcessingFlags.LoadElements);
            var writingErrors = 0 != (manifestProcessingFlags & ManifestProcessingFlags.WriteErrors);

            // Load the data file(s) to get the module info...
            try
            {
                scriptBlock.CheckRestrictedLanguage(PermittedCmdlets, s_extraAllowedVariables, true);
            }
            catch (RuntimeException pe)
            {
                if (writingErrors)
                {
                    message = StringUtil.Format(Modules.InvalidModuleManifest, moduleManifestPath, pe.Message);
                    MissingMemberException mm = new MissingMemberException(message);
                    ErrorRecord er = new ErrorRecord(mm, "Modules_InvalidManifest",
                        ErrorCategory.ResourceUnavailable, moduleManifestPath);
                    WriteError(er);
                }
                containedErrors = true;
                return null;
            }

            object result;
            object oldPSScriptRoot = Context.GetVariableValue(SpecialVariables.PSScriptRootVarPath);
            object oldPSCommandPath = Context.GetVariableValue(SpecialVariables.PSCommandPathVarPath);
            ArrayList errors = (ArrayList)Context.GetVariableValue(SpecialVariables.ErrorVarPath);
            int oldErrorCount = errors.Count;

            try
            {
                // Set the PSScriptRoot variable in the modules session state
                Context.SetVariable(SpecialVariables.PSScriptRootVarPath, Path.GetDirectoryName(moduleManifestPath));
                Context.SetVariable(SpecialVariables.PSCommandPathVarPath, moduleManifestPath);

                result = PSObject.Base(scriptBlock.InvokeReturnAsIs());
            }
            finally
            {
                Context.SetVariable(SpecialVariables.PSScriptRootVarPath, oldPSScriptRoot);
                Context.SetVariable(SpecialVariables.PSCommandPathVarPath, oldPSCommandPath);

                // We do not want any exceptions to show up in user's session state. So, we are removing errors in the finally block.
                // If we're not loading the manifest (and are examining it), prevent errors from showing up in
                // the user's session.
                if (!importingModule)
                {
                    while (errors.Count > oldErrorCount) { errors.RemoveAt(0); }
                }
            }

            Hashtable data = result as Hashtable;
            if (data == null)
            {
                if (writingErrors)
                {
                    message = StringUtil.Format(Modules.EmptyModuleManifest, moduleManifestPath);
                    ArgumentException ae = new ArgumentException(message);
                    ErrorRecord er = new ErrorRecord(ae, "Modules_InvalidManifest",
                        ErrorCategory.ResourceUnavailable, moduleManifestPath);
                    WriteError(er);
                }
                containedErrors = true;
                return null;
            }

            // MSFT:873446 Create a case insensitive comparer based hashtable to help
            // with case-insensitive comparison of keys.
            data = new Hashtable(data, StringComparer.OrdinalIgnoreCase);
            if (validMembers != null && !ValidateManifestHash(data, validMembers, moduleManifestPath, manifestProcessingFlags))
            {
                containedErrors = true;
                if (0 != (manifestProcessingFlags & ManifestProcessingFlags.NullOnFirstError))
                    return null;
            }

            return data;
        }

        /// <summary>
        /// Verify the hash contains only valid members.  Write an error and return false if it is not valid.
        /// </summary>
        private bool ValidateManifestHash(
            Hashtable data,
            string[] validMembers,
            string moduleManifestPath,
            ManifestProcessingFlags manifestProcessingFlags)
        {
            bool result = true;

            StringBuilder badKeys = new StringBuilder();
            foreach (string s in data.Keys)
            {
                bool found = false;

                foreach (string member in validMembers)
                {
                    if (s.Equals(member, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                    }
                }

                if (!found)
                {
                    if (badKeys.Length > 0)
                        badKeys.Append(", ");
                    badKeys.Append("'");
                    badKeys.Append(s);
                    badKeys.Append("'");
                }
            }
            if (badKeys.Length > 0)
            {
                result = false;
                string message = null;

                if (0 != (manifestProcessingFlags & ManifestProcessingFlags.WriteErrors))
                {
                    // Check for PowerShell Version before checking other keys
                    // If a PowerShellVersion exists and does not match the requirements, then the error is InsufficientPowerShellVersion
                    // Else, the error is InvalidManifestMember

                    Version powerShellVersion;
                    Version currentPowerShellVersion = PSVersionInfo.PSVersion;
                    if (GetScalarFromData<Version>(data, moduleManifestPath, "PowerShellVersion", manifestProcessingFlags, out powerShellVersion) &&
                        currentPowerShellVersion < powerShellVersion)
                    {
                        message = StringUtil.Format(Modules.ModuleManifestInsufficientPowerShellVersion,
                                                    currentPowerShellVersion,
                                                    moduleManifestPath, powerShellVersion);
                        InvalidOperationException ioe = new InvalidOperationException(message);
                        ErrorRecord er = new ErrorRecord(ioe, "Modules_InsufficientPowerShellVersion",
                                                         ErrorCategory.ResourceUnavailable, moduleManifestPath);
                        WriteError(er);
                    }
                    else
                    {
                        StringBuilder validMembersString = new StringBuilder("'");
                        validMembersString.Append(validMembers[0]);
                        for (int i = 1; i < validMembers.Length; i++)
                        {
                            validMembersString.Append("', '");
                            validMembersString.Append(validMembers[i]);
                        }
                        validMembersString.Append("'");
                        message = StringUtil.Format(Modules.InvalidModuleManifestMember, moduleManifestPath, validMembersString, badKeys);
                        InvalidOperationException ioe = new InvalidOperationException(message);
                        ErrorRecord er = new ErrorRecord(ioe, "Modules_InvalidManifestMember",
                            ErrorCategory.InvalidData, moduleManifestPath);
                        WriteError(er);
                    }
                }
            }
            return result;
        }

        private PSModuleInfo LoadModuleNamedInManifest(PSModuleInfo parentModule, ModuleSpecification moduleSpecification, string moduleBase, bool searchModulePath,
            string prefix, SessionState ss, ImportModuleOptions options, ManifestProcessingFlags manifestProcessingFlags, bool loadTypesFiles,
            bool loadFormatFiles, object privateData, out bool found, string shortModuleName)
        {
            PSModuleInfo module = null;
            PSModuleInfo tempModuleInfoFromVerification = null;
            found = false;
            bool moduleFileFound = false;
            bool wasRooted = false;
            Version savedBaseMinimumVersion = BaseMinimumVersion;
            Version savedBaseMaximumVersion = BaseMaximumVersion;
            Version savedBaseRequiredVersion = BaseRequiredVersion;
            Guid? savedBaseGuid = BaseGuid;

            var importingModule = 0 != (manifestProcessingFlags & ManifestProcessingFlags.LoadElements);

            // First check for fully-qualified paths - either absolute or relative
            string rootedPath = ResolveRootedFilePath(moduleSpecification.Name, this.Context);
            if (String.IsNullOrEmpty(rootedPath))
            {
                rootedPath = Path.Combine(moduleBase, moduleSpecification.Name);
            }
            else
            {
                wasRooted = true;
            }

            string extension = Path.GetExtension(moduleSpecification.Name);
            try
            {
                this.Context.Modules.IncrementModuleNestingDepth(this, rootedPath);
                BaseMinimumVersion = null;
                BaseMaximumVersion = null;
                BaseRequiredVersion = null;
                BaseGuid = null;

                // See if it's one of the powershell module extensions...
                if (!ModuleIntrinsics.IsPowerShellModuleExtension(extension))
                {
                    // If the file exits, and it does not have a powershell module extension, we know for sure that the file is not a valid module
                    if (File.Exists(rootedPath))
                    {
                        PSInvalidOperationException invalidOperation = PSTraceSource.NewInvalidOperationException(
                                                                Modules.ManifestMemberNotValid,
                                                                moduleSpecification.Name,
                                                                "NestedModules",
                                                                parentModule.Path,
                                                                StringUtil.Format(Modules.InvalidModuleExtension, extension, moduleSpecification.Name),
                                                                                    ModuleIntrinsics.GetModuleName(parentModule.Path));
                        invalidOperation.SetErrorId("Modules_InvalidModuleExtension");
                        throw invalidOperation;
                    }
                    extension = null;
                }

                // Now load the module from the module directory first...
                if (extension == null)
                {
                    // No extension so we'll have to search using the extensions
                    // 
                    if (VerifyIfNestedModuleIsAvailable(moduleSpecification, rootedPath, /*extension*/null, out tempModuleInfoFromVerification))
                    {
                        module = LoadUsingExtensions(
                            parentModule,
                            moduleSpecification.Name,
                            rootedPath, // fileBaseName
                            /*extension*/null,
                            moduleBase, // not using base from tempModuleInfoFromVerification as we are looking under moduleBase directory
                            prefix,
                            ss,
                            options,
                            manifestProcessingFlags,
                            out found,
                            out moduleFileFound);
                    }

                    // Win8: 262157 - Import-Module is giving errors while loading Nested Modules. (This is a V2 bug)
                    // NestedModules = 'test2' ---> test2 is a directory under current module directory (e.g - Test1)
                    // We also need to look for Test1\Test2\Test2.(psd1/psm1/dll)
                    // With the call above, we are only looking at Test1\Test2.(psd1/psm1/dll)
                    if (found == false && moduleFileFound == false)
                    {
                        string newRootedPath = Path.Combine(rootedPath, moduleSpecification.Name);
                        string newModuleBase = Path.Combine(moduleBase, moduleSpecification.Name);
                        if (VerifyIfNestedModuleIsAvailable(moduleSpecification, newRootedPath, /*extension*/null, out tempModuleInfoFromVerification))
                        {
                            module = LoadUsingExtensions(
                                parentModule,
                                moduleSpecification.Name,
                                newRootedPath, // fileBaseName
                                /*extension*/ null,
                                newModuleBase, // not using base from tempModuleInfoFromVerification as we are looking under moduleBase directory
                                prefix,
                                ss,
                                options,
                                manifestProcessingFlags,
                                out found,
                                out moduleFileFound);
                        }
                    }
                }
                else
                {
                    // Ok - we have a complete file name so load that...
                    if (VerifyIfNestedModuleIsAvailable(moduleSpecification, rootedPath, extension, out tempModuleInfoFromVerification))
                    {
                        module = LoadModule(
                            parentModule,
                            rootedPath, // fileName
                            moduleBase, // not using base from tempModuleInfoFromVerification as we have a complete file name
                            prefix,
                            ss,
                            privateData,
                            ref options,
                            manifestProcessingFlags,
                            out found,
                            out moduleFileFound);
                    }

                    // Win8: 262157 - Import-Module is giving errors while loading Nested Modules. (This is a V2 bug)
                    // Only look for the file if the file was not found with the previous search
                    if (found == false && moduleFileFound == false)
                    {
                        string newRootedPath = Path.Combine(rootedPath, moduleSpecification.Name);
                        string newModuleBase = Path.Combine(moduleBase, moduleSpecification.Name);
                        if (VerifyIfNestedModuleIsAvailable(moduleSpecification, newRootedPath, extension, out tempModuleInfoFromVerification))
                        {
                            module = LoadModule(
                                parentModule,
                                newRootedPath, // fileName
                                newModuleBase, // not using base from tempModuleInfoFromVerification as we are looking under moduleBase directory
                                prefix,
                                ss,
                                privateData,
                                ref options,
                                manifestProcessingFlags,
                                out found,
                                out moduleFileFound);
                        }
                    }
                }

                // The rooted files wasn't found, so don't search anymore...
                if (found == false && wasRooted == true)
                    return null;

                if (searchModulePath && found == false && moduleFileFound == false)
                {
                    if (VerifyIfNestedModuleIsAvailable(moduleSpecification, null, null, out tempModuleInfoFromVerification))
                    {
                        IEnumerable<string> modulePath = null;
                        if (tempModuleInfoFromVerification != null)
                        {
                            var subdirName = Path.GetFileName(tempModuleInfoFromVerification.ModuleBase);
                            Version version;
                            if (Version.TryParse(subdirName, out version))
                            {
                                var modueBaseWithoutVersion = Path.GetDirectoryName(tempModuleInfoFromVerification.ModuleBase);

                                modulePath = new string[]
                                {
                                    Path.GetDirectoryName(modueBaseWithoutVersion),
                                    modueBaseWithoutVersion
                                };
                            }
                            else
                            {
                                modulePath = new string[]
                                {
                                    Path.GetDirectoryName(tempModuleInfoFromVerification.ModuleBase),
                                    tempModuleInfoFromVerification.ModuleBase
                                };
                            }
                        }
                        else
                        {
                            modulePath = ModuleIntrinsics.GetModulePath(false, this.Context);
                        }

                        // Otherwise try the module path
                        found = LoadUsingModulePath(parentModule, found, modulePath,
                                                    moduleSpecification.Name, ss,
                                                    options, manifestProcessingFlags, out module);
                    }
                }

                // At this point, we haven't found an actual module, so try loading it as a
                // PSSnapIn and then finally as an assembly in the GAC...
                if ((found == false) && (moduleSpecification.Guid == null) && (moduleSpecification.Version == null) && (moduleSpecification.RequiredVersion == null) && (moduleSpecification.MaximumVersion == null))
                {
                    // If we are in module analysis and the parent module declares non-wildcarded ExportedCmdlets, then we don't need to
                    // actually process the binary module.
                    bool shouldLoadModule = true;

                    if ((parentModule != null) && !importingModule)
                    {
                        if ((parentModule.ExportedCmdlets != null) && (parentModule.ExportedCmdlets.Count > 0))
                        {
                            shouldLoadModule = false;

                            foreach (string exportedCmdlet in parentModule.ExportedCmdlets.Keys)
                            {
                                if (WildcardPattern.ContainsWildcardCharacters(exportedCmdlet))
                                {
                                    shouldLoadModule = true;
                                    break;
                                }
                            }

                            found = true;
                        }
                    }

                    if (shouldLoadModule)
                    {
                        try
                        {
                            // At this point, we are already exhaust all possible ways to load the nested module. The last option is to load it as a binary module/snapin.
                            module = LoadBinaryModule(
                                parentModule,
                                true, // trySnapInName 
                                moduleSpecification.Name,
                                null, // fileName
                                null, // assemblyToLoad
                                moduleBase,
                                ss,
                                options,
                                manifestProcessingFlags,
                                prefix,
                                loadTypesFiles,
                                loadFormatFiles,
                                out found,
                                shortModuleName, false);
                        }
                        catch (FileNotFoundException)
                        {
                            // Loading the nested module as a pssnapin or assembly is our last attempt to resolve the nested module. 
                            // If we catch 'FileNotFoundException', it simply means that our last attempt failed because we couldn't find such a pssnapin or assembly. 
                            // In this case, we can safely ignore this exception and throw a 'Modules_ModuleFileNotFound' error.
                        }

                        if ((module != null) && importingModule)
                        {
                            AddModuleToModuleTables(this.Context, this.TargetSessionState.Internal, module);
                        }
                    }
                }

                return module;
            }
            finally
            {
                BaseMinimumVersion = savedBaseMinimumVersion;
                BaseMaximumVersion = savedBaseMaximumVersion;
                BaseRequiredVersion = savedBaseRequiredVersion;
                BaseGuid = savedBaseGuid;
                this.Context.Modules.DecrementModuleNestingCount();
            }
        }

        internal List<PSModuleInfo> GetModule(string[] names, bool all, bool refresh)
        {
            List<PSModuleInfo> modulesToReturn = new List<PSModuleInfo>();

            // Two lists - one to hold Module Paths and one to hold Module Names
            // For Module Paths, we don't do any path resolution
            List<String> modulePaths = new List<String>();
            List<String> moduleNames = new List<String>();

            if (names != null)
            {
                foreach (var n in names)
                {
                    if (n.IndexOf(StringLiterals.DefaultPathSeparator) != -1 || n.IndexOf(StringLiterals.AlternatePathSeparator) != -1)
                    {
                        modulePaths.Add(n);
                    }
                    else
                    {
                        moduleNames.Add(n);
                    }
                }
                modulesToReturn.AddRange(GetModuleForRootedPaths(modulePaths.ToArray(), all, refresh));
            }

            // If no names were passed to this function, then this API will return list of all available modules
            if (names == null || moduleNames.Count > 0)
            {
                modulesToReturn.AddRange(GetModuleForNonRootedPaths(moduleNames.ToArray(), all, refresh));
            }

            return modulesToReturn;
        }

        private IEnumerable<PSModuleInfo> GetModuleForNonRootedPaths(string[] names, bool all, bool refresh)
        {
            const WildcardOptions wildcardOptions = WildcardOptions.IgnoreCase | WildcardOptions.CultureInvariant;
            IEnumerable<WildcardPattern> patternList = SessionStateUtilities.CreateWildcardsFromStrings(names, wildcardOptions);

            Dictionary<String, List<PSModuleInfo>> availableModules = GetAvailableLocallyModulesCore(names, all, refresh);
            foreach (var entry in availableModules)
            {
                foreach (PSModuleInfo module in entry.Value)
                {
                    if (SessionStateUtilities.MatchesAnyWildcardPattern(module.Name, patternList, true))
                    {
                        yield return module;
                    }
                }
            }
        }

        private IEnumerable<PSModuleInfo> GetModuleForRootedPaths(string[] modulePaths, bool all, bool refresh)
        {
            // This is to filter out duplicate modules
            var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string mp in modulePaths)
            {
                bool containsWildCards = false;

                string modulePath = mp.TrimEnd(Utils.Separators.Backslash);

                // If the given path contains wildcards, we won't throw error if no match module path is found.
                if (WildcardPattern.ContainsWildcardCharacters(modulePath))
                {
                    containsWildCards = true;
                }

                //Now we resolve the possible paths in case it is relative path/path contains wildcards
                var modulePathCollection = GetResolvedPathCollection(modulePath, this.Context);


                if (modulePathCollection != null)
                {
                    foreach (string resolvedModulePath in modulePathCollection)
                    {
                        string moduleName = Path.GetFileName(resolvedModulePath);
                        bool isDirectory = Utils.NativeDirectoryExists(resolvedModulePath);

                        // If the given path is a valid module file, we will load the specific file
                        if (!isDirectory && ModuleIntrinsics.IsPowerShellModuleExtension(Path.GetExtension(moduleName)))
                        {
                            PSModuleInfo module = CreateModuleInfoForGetModule(resolvedModulePath, refresh);
                            if (module != null)
                            {
                                if (!modules.Contains(resolvedModulePath))
                                {
                                    modules.Add(resolvedModulePath);
                                    yield return module;
                                }
                            }
                        }
                        else
                        {
                            // Given path is a directory, we first check if it is end with module version.
                            Version version;
                            if (Version.TryParse(moduleName, out version))
                            {
                                moduleName = Path.GetFileName(Directory.GetParent(resolvedModulePath).Name);
                            }

                            var availableModuleFiles = all
                                ? ModuleUtils.GetAllAvailableModuleFiles(resolvedModulePath)
                                : ModuleUtils.GetModuleVersionsFromAbsolutePath(resolvedModulePath);

                            bool foundModule = false;
                            foreach (string file in availableModuleFiles)
                            {
                                PSModuleInfo module = CreateModuleInfoForGetModule(file, refresh);
                                if (module != null)
                                {
                                    if (String.Equals(moduleName, module.Name, StringComparison.OrdinalIgnoreCase))
                                    {
                                        foundModule = true;
                                        // We need to list all versions of the module.
                                        string subModulePath = Path.GetDirectoryName(file);
                                        if (!modules.Contains(subModulePath))
                                        {
                                            modules.Add(subModulePath);
                                            yield return module;
                                        }
                                    }
                                }
                            }

                            // Write error only if Name has no wild cards
                            if (!foundModule && !containsWildCards)
                            {
                                WriteError(CreateModuleNotFoundError(resolvedModulePath));
                            }
                        }
                    }
                }
                else
                {
                    if (!containsWildCards)
                    {
                        WriteError(CreateModuleNotFoundError(modulePath));
                    }
                }
            }
        }

        private ErrorRecord CreateModuleNotFoundError(string modulePath)
        {
            string errorMessage = StringUtil.Format(Modules.ModuleNotFoundForGetModule, modulePath);
            FileNotFoundException fnf = new FileNotFoundException(errorMessage);
            ErrorRecord er = new ErrorRecord(fnf, "Modules_ModuleNotFoundForGetModule", ErrorCategory.ResourceUnavailable, modulePath);
            return er;
        }

        private Dictionary<String, List<PSModuleInfo>> GetAvailableLocallyModulesCore(string[] names, bool all, bool refresh)
        {
            var modules = new Dictionary<string, List<PSModuleInfo>>(StringComparer.OrdinalIgnoreCase);
            var modulePaths = ModuleIntrinsics.GetModulePath(false, Context);

            bool cleanupModuleAnalysisAppDomain = Context.TakeResponsibilityForModuleAnalysisAppDomain();

            try
            {
                foreach (string path in ModuleIntrinsics.GetModulePath(false, Context))
                {
                    try
                    {
                        var availableModules = all
                            ? GetAllAvailableModules(path, refresh)
                            : GetDefaultAvailableModules(names, path, refresh);

                        // Add the path in $env:PSModulePath as the keys of the dictionary
                        // If the paths are repeated, ignore the repetitions
                        string uniquePath = path.TrimEnd(Utils.Separators.Directory);
                        if (!modules.ContainsKey(uniquePath))
                        {
                            modules.Add(uniquePath, availableModules.OrderBy(m => m.Name).ToList());
                        }
                    }
                    catch (IOException)
                    {
                        continue; // ignore directories that can't be accessed
                    }
                    catch (UnauthorizedAccessException)
                    {
                        continue; // ignore directories that can't be accessed
                    }
                }
            }
            finally
            {
                if (cleanupModuleAnalysisAppDomain)
                {
                    Context.ReleaseResponsibilityForModuleAnalysisAppDomain();
                }
            }

            return modules;
        }


        /// <summary>
        /// Get a list of all modules 
        /// which can be imported just by specifying a non rooted file name of the module
        /// (Import-Module foo\bar.psm1;  but not Import-Module .\foo\bar.psm1)
        /// </summary>
        private IEnumerable<PSModuleInfo> GetAllAvailableModules(string directory, bool refresh)
        {
            var availableModuleFiles = ModuleUtils.GetAllAvailableModuleFiles(directory);

            foreach (string file in availableModuleFiles)
            {
                PSModuleInfo module = CreateModuleInfoForGetModule(file, refresh);
                if (module != null)
                {
                    yield return module;
                }
            }
        }

        /// <summary>
        /// Get a list of the available modules 
        /// which can be imported just by specifying a non rooted directory name of the module
        /// (Import-Module foo\bar;  but not Import-Module .\foo\bar or Import-Module .\foo\bar.psm1)
        /// </summary>
        private List<PSModuleInfo> GetDefaultAvailableModules(string[] name, string directory, bool refresh)
        {
            List<PSModuleInfo> availableModules = new List<PSModuleInfo>();

            const WildcardOptions wildcardOptions = WildcardOptions.IgnoreCase | WildcardOptions.CultureInvariant;
            IEnumerable<WildcardPattern> patternList = SessionStateUtilities.CreateWildcardsFromStrings(name, wildcardOptions);

            var availableModuleFiles = ModuleUtils.GetDefaultAvailableModuleFiles(directory);

            foreach (string file in availableModuleFiles)
            {
                string actualModuleName = System.IO.Path.GetFileNameWithoutExtension(file);
                if (SessionStateUtilities.MatchesAnyWildcardPattern(actualModuleName, patternList, true))
                {
                    PSModuleInfo module = CreateModuleInfoForGetModule(file, refresh);

                    if (module != null)
                    {
                        Version directoryVersion;
                        if (!ModuleUtils.IsModuleInVersionSubdirectory(file, out directoryVersion)
                            || directoryVersion == module.Version)
                        {
                            availableModules.Add(module);
                        }
                    }
                }
            }

            ClearAnalysisCaches();

            return availableModules;
        }

        /// <summary>
        /// Get the version type of input string MaximumVersion, translate "*" if there's any.
        /// </summary>
        internal static Version GetMaximumVersion(string stringVersion)
        {
            Version maxVersion;
            // First, try to convert maxVersion to version directly
            if (System.Version.TryParse(stringVersion, out maxVersion))
            {
                return maxVersion;
            }
            else
            {
                // If first conversion fails, try to convert * to maximum version
                string maxRange = "999999999";
                if (stringVersion[stringVersion.Length - 1] == '*')
                {
                    stringVersion = stringVersion.Substring(0, stringVersion.Length - 1);
                    stringVersion = stringVersion + maxRange;
                    int starNum = stringVersion.Count(x => x == '.');
                    for (int i = 0; i < (3 - starNum); i++)
                    {
                        stringVersion = stringVersion + '.' + maxRange;
                    }
                }
            }

            if (Version.TryParse(stringVersion, out maxVersion))
            {
                return new Version(stringVersion);
            }
            else
            {
                string message = StringUtil.Format(Modules.MaximumVersionFormatIncorrect, stringVersion);
                throw new PSArgumentException(message);
            }
        }

        /// <summary>
        /// Helper function for building a module info for Get-Module -List
        /// </summary>
        /// <param name="file">The module file</param>
        /// <param name="refresh">True if we should update any cached module info for this module</param>
        /// <returns></returns>
        private PSModuleInfo CreateModuleInfoForGetModule(string file, bool refresh)
        {
            // Ensure we don't have any recursion in module lookup
            PSModuleInfo moduleInfo = null;
            if (_currentlyProcessingModules.TryGetValue(file, out moduleInfo))
            {
                return moduleInfo;
            }

            _currentlyProcessingModules[file] = null;

            // Create a fake module info for this file
            string extension;
            file = file.TrimEnd();

            // In case the file is a Ngen Assembly.
            if (file.EndsWith(StringLiterals.PowerShellNgenAssemblyExtension, StringComparison.OrdinalIgnoreCase))
            {
                extension = StringLiterals.PowerShellNgenAssemblyExtension;
            }
            else
            {
                extension = Path.GetExtension(file);
            }

            ManifestProcessingFlags flags = ManifestProcessingFlags.NullOnFirstError;
            // We are creating the ModuleInfo for Get-Module..so ignoring
            // PowerShellHostName and PowerShellHostVersion.
            flags |= ManifestProcessingFlags.IgnoreHostNameAndHostVersion;
            if (refresh) { flags |= ManifestProcessingFlags.Force; }

            try
            {
                if (extension.Equals(StringLiterals.PowerShellDataFileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    string throwAwayScriptName;
                    ExternalScriptInfo scriptInfo = GetScriptInfoForFile(file, out throwAwayScriptName, true);

                    // At this place, we used to check if the module is already loaded. If so, return that. 
                    // This was done to fix the issue where we were not getting the ExportedCommands since they are populated only after an import.
                    // But, after the auto-loading feature, we get ExportedCommands even before an import. 
                    // Plus, the issue of returning the loaded module was introducing a formatting bug (Win8: 284599).
                    // So, removing that logic.
                    moduleInfo = LoadModuleManifest(
                            scriptInfo,
                            flags /* - don't write errors, don't load elements */,
                            null,
                            null,
                            null,
                            null);
                }
                else
                {
                    // It's not a module manifest, process the individual module
                    ImportModuleOptions options = new ImportModuleOptions();
                    bool found = false;

                    moduleInfo = LoadModule(file, null, String.Empty, null, ref options, flags, out found);
                }

                // return fake PSModuleInfo if can't read the file for any reason
                if (moduleInfo == null)
                {
                    moduleInfo = new PSModuleInfo(file, null, null);
                    moduleInfo.HadErrorsLoading = true;     // Prevent analysis cache from caching a bad module.
                }

                if (extension.Equals(StringLiterals.PowerShellDataFileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    if (moduleInfo.RootModuleForManifest != null)
                    {
                        if (moduleInfo.RootModuleForManifest.EndsWith(StringLiterals.DependentWorkflowAssemblyExtension, StringComparison.OrdinalIgnoreCase))
                        {
                            moduleInfo.SetModuleType(ModuleType.Binary);
                        }
                        else if (moduleInfo.RootModuleForManifest.EndsWith(StringLiterals.PowerShellModuleFileExtension, StringComparison.OrdinalIgnoreCase))
                        {
                            moduleInfo.SetModuleType(ModuleType.Script);
                        }
                        else if (moduleInfo.RootModuleForManifest.EndsWith(StringLiterals.WorkflowFileExtension, StringComparison.OrdinalIgnoreCase))
                        {
                            moduleInfo.SetModuleType(ModuleType.Workflow);
                        }
                        else if (moduleInfo.RootModuleForManifest.EndsWith(StringLiterals.PowerShellCmdletizationFileExtension, StringComparison.OrdinalIgnoreCase))
                        {
                            moduleInfo.SetModuleType(ModuleType.Cim);
                        }
                        else
                        {
                            moduleInfo.SetModuleType(ModuleType.Manifest);
                        }
                        moduleInfo.RootModule = moduleInfo.RootModuleForManifest;
                    }
                    else
                    {
                        moduleInfo.SetModuleType(ModuleType.Manifest);
                        moduleInfo.RootModule = moduleInfo.Path;
                    }
                }
                else if (extension.Equals(StringLiterals.DependentWorkflowAssemblyExtension, StringComparison.OrdinalIgnoreCase) || extension.Equals(StringLiterals.PowerShellNgenAssemblyExtension, StringComparison.OrdinalIgnoreCase))
                {
                    moduleInfo.SetModuleType(ModuleType.Binary);
                    moduleInfo.RootModule = moduleInfo.Path;
                }
                else if (extension.Equals(StringLiterals.WorkflowFileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    moduleInfo.SetModuleType(ModuleType.Workflow);
                    moduleInfo.RootModule = moduleInfo.Path;
                }
                else if (extension.Equals(StringLiterals.PowerShellCmdletizationFileExtension))
                {
                    moduleInfo.SetModuleType(ModuleType.Cim);

                    string moduleName;
                    ExternalScriptInfo scriptInfo = GetScriptInfoForFile(file, out moduleName, true);
                    var cmdletizationXmlReader = new StringReader(scriptInfo.ScriptContents);
                    var scriptWriter = new ScriptWriter(
                        cmdletizationXmlReader,
                        moduleName,
                        StringLiterals.DefaultCmdletAdapter,
                        this.MyInvocation,
                        ScriptWriter.GenerationOptions.HelpXml);
                    scriptWriter.PopulatePSModuleInfo(moduleInfo);
                    moduleInfo.RootModule = moduleInfo.Path;
                }
                else
                {
                    moduleInfo.SetModuleType(ModuleType.Script);
                    moduleInfo.RootModule = moduleInfo.Path;
                }
            }
            catch (Exception e)
            {
                // 3rd-part call out, catch-all OK
                CommandProcessorBase.CheckForSevereException(e);

                // return fake PSModuleInfo if can't read the file for any reason
                if (moduleInfo == null)
                {
                    moduleInfo = new PSModuleInfo(file, null, null);
                    moduleInfo.HadErrorsLoading = true;     // Prevent analysis cache from caching a bad module.
                }
            }

            if (!moduleInfo.HadErrorsLoading)
            {
                // Cache the module's exported commands
                AnalysisCache.CacheModuleExports(moduleInfo, Context);
            }
            else
            {
                ModuleIntrinsics.Tracer.WriteLine("Caching skipped for {0} because it had errors while loading.", moduleInfo.Name);
            }

            _currentlyProcessingModules[file] = moduleInfo;
            return moduleInfo;
        }

        /// <summary>
        /// Routine to process the module manifest data language script.
        /// </summary>
        /// <param name="scriptInfo">The script info for the manifest script</param>
        /// <param name="manifestProcessingFlags">processing flags (whether to write errors / load elements)</param>
        /// <param name="minimumVersion">The minimum version to check the manifest against</param>
        /// <param name="maximumVersion">The maximum version to check the manifest against</param>
        /// <param name="requiredVersion">The version to check the manifest against</param>
        /// <param name="requiredModuleGuid">The module guid to check the manifest against</param>
        /// <returns></returns>
        /// 
        internal PSModuleInfo LoadModuleManifest(
            ExternalScriptInfo scriptInfo,
            ManifestProcessingFlags manifestProcessingFlags,
            Version minimumVersion,
            Version maximumVersion,
            Version requiredVersion,
            Guid? requiredModuleGuid)
        {
            ImportModuleOptions options = new ImportModuleOptions();
            return LoadModuleManifest(scriptInfo, manifestProcessingFlags, minimumVersion, maximumVersion, requiredVersion, requiredModuleGuid, ref options);
        }

        /// <summary>
        /// Routine to process the module manifest data language script.
        /// </summary>
        /// <param name="scriptInfo">The script info for the manifest script</param>
        /// <param name="manifestProcessingFlags">processing flags (whether to write errors / load elements)</param>
        /// <param name="minimumVersion">The minimum version to check the manifest against</param>
        /// <param name="maximumVersion">The maximum version to check the manifest against</param>
        /// <param name="requiredVersion">The version to check the manifest against</param>
        /// <param name="requiredModuleGuid">The module guid to check the manifest against</param>
        /// <param name="options">The set of options that are used while importing a module</param>
        /// <returns></returns>
        /// 
        internal PSModuleInfo LoadModuleManifest(
            ExternalScriptInfo scriptInfo,
            ManifestProcessingFlags manifestProcessingFlags,
            Version minimumVersion,
            Version maximumVersion,
            Version requiredVersion,
            Guid? requiredModuleGuid,
            ref ImportModuleOptions options)
        {
            Dbg.Assert(scriptInfo != null, "scriptInfo for module (.psd1) can't be null");
            bool containedErrors = false;

            Hashtable data = null;
            Hashtable localizedData = null;
            if (!LoadModuleManifestData(scriptInfo, manifestProcessingFlags, out data, out localizedData, ref containedErrors))
            {
                return null;
            }

            return LoadModuleManifest(scriptInfo.Path, scriptInfo, data, localizedData, manifestProcessingFlags, minimumVersion, maximumVersion, requiredVersion, requiredModuleGuid, ref options, ref containedErrors);
        }

        internal bool LoadModuleManifestData(ExternalScriptInfo scriptInfo, ManifestProcessingFlags manifestProcessingFlags, out Hashtable data, out Hashtable localizedData, ref bool containedErrors)
        {
            // load the .psd1 files into hashtables
            localizedData = null;
            data = LoadModuleManifestData(scriptInfo, ModuleManifestMembers, manifestProcessingFlags, ref containedErrors);
            if (data == null)
            {
                return false;
            }
            ExternalScriptInfo localizedScriptInfo = FindLocalizedModuleManifest(scriptInfo.Path);
            localizedData = null;
            if (localizedScriptInfo != null)
            {
                localizedData = LoadModuleManifestData(localizedScriptInfo, null, manifestProcessingFlags, ref containedErrors);
                if (localizedData == null)
                {
                    return false;
                }
            }
            return true;
        }

        private ErrorRecord GetErrorRecordIfUnsupportedRootCdxmlAndNestedModuleScenario(
            Hashtable data,
            string moduleManifestPath,
            string rootModulePath)
        {
            if (rootModulePath == null)
            {
                return null;
            }
            if (!rootModulePath.EndsWith(StringLiterals.PowerShellCmdletizationFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            if (!data.ContainsKey("NestedModules"))
            {
                return null;
            }

            string nameOfRootModuleKey = data.ContainsKey("ModuleToProcess") ? "ModuleToProcess" : "RootModule";
            string message = StringUtil.Format(
                Modules.CmdletizationDoesSupportRexportingNestedModules,
                nameOfRootModuleKey,
                moduleManifestPath,
                rootModulePath);
            InvalidOperationException ioe = new InvalidOperationException(message);
            ErrorRecord er = new ErrorRecord(
                ioe,
                "Modules_CmdletizationDoesSupportRexportingNestedModules",
                ErrorCategory.InvalidOperation,
                moduleManifestPath);

            return er;
        }

        /// <summary>
        /// Routine to process the module manifest data language script.
        /// </summary>
        /// <param name="moduleManifestPath">The path to the manifest file</param>
        /// <param name="scriptInfo">The script info for the manifest script</param>
        /// <param name="data">Contents of the module manifest</param>
        /// <param name="localizedData">Contents of the localized module manifest</param>
        /// <param name="manifestProcessingFlags">processing flags (whether to write errors / load elements)</param>
        /// <param name="minimumVersion">The minimum version to check the manifest against</param>
        /// <param name="maximumVersion">The maximum version to check the manifest against</param>
        /// <param name="requiredVersion">The version to check the manifest against</param>
        /// <param name="requiredModuleGuid">The module guid to check the manifest against</param>
        /// <param name="options">The set of options that are used while importing a module</param>
        /// <param name="containedErrors">Tracks if there were errors in the file</param>
        /// <returns></returns>
        /// 
        internal PSModuleInfo LoadModuleManifest(
            string moduleManifestPath,
            ExternalScriptInfo scriptInfo,
            Hashtable data,
            Hashtable localizedData,
            ManifestProcessingFlags manifestProcessingFlags,
            Version minimumVersion,
            Version maximumVersion,
            Version requiredVersion,
            Guid? requiredModuleGuid,
            ref ImportModuleOptions options,
            ref bool containedErrors)
        {
            string message;

            var bailOnFirstError = 0 != (manifestProcessingFlags & ManifestProcessingFlags.NullOnFirstError);
            var importingModule = 0 != (manifestProcessingFlags & ManifestProcessingFlags.LoadElements);
            var writingErrors = 0 != (manifestProcessingFlags & ManifestProcessingFlags.WriteErrors);

            Dbg.Assert(moduleManifestPath != null, "moduleManifestPath for module (.psd1) can't be null");
            string moduleBase = Path.GetDirectoryName(moduleManifestPath);

            if ((manifestProcessingFlags &
                 (ManifestProcessingFlags.LoadElements | ManifestProcessingFlags.WriteErrors |
                  ManifestProcessingFlags.WriteWarnings)) != 0)
            {
                Context.ModuleBeingProcessed = moduleManifestPath;
            }

            // START: Check if the ModuleToProcess is already loaded..if it is, ignore the this load manifest 
            // call and return

            // Workflows specified in NestedModules from the manifest
            List<string> workflowsToProcess = new List<string>();

            // Workflows specified in RequiredAssemblies
            List<string> dependentWorkflows = new List<string>();

            string moduleToProcess = null;

            if (
                !GetScalarFromData<string>(data, moduleManifestPath, "ModuleToProcess", manifestProcessingFlags,
                    out moduleToProcess))
            {
                containedErrors = true;
                if (bailOnFirstError) return null;
            }
            string rootModule = null;
            if (
                !GetScalarFromData<string>(data, moduleManifestPath, "RootModule", manifestProcessingFlags,
                    out rootModule))
            {
                containedErrors = true;
                if (bailOnFirstError) return null;
            }

            if (!string.IsNullOrEmpty(moduleToProcess))
            {
                // If we've found ourselves, then skip writing the warning and move on.
                // Win8: 297326
                if (string.IsNullOrEmpty(Context.ModuleBeingProcessed) ||
                    (!Context.ModuleBeingProcessed.Equals(moduleManifestPath, StringComparison.OrdinalIgnoreCase) ||
                     !Context.ModuleBeingProcessed.Equals(Context.PreviousModuleProcessed,
                         StringComparison.OrdinalIgnoreCase)))
                {
                    if (0 != (manifestProcessingFlags & ManifestProcessingFlags.WriteWarnings))
                    {
                        WriteWarning(Modules.ModuleToProcessFieldDeprecated);
                    }
                }
            }

            if (!string.IsNullOrEmpty(moduleToProcess) && !string.IsNullOrEmpty(rootModule))
            {
                // If we've found ourselves, then skip writing the error and move on.
                // Win8 :330612
                if (string.IsNullOrEmpty(Context.ModuleBeingProcessed) ||
                    (!Context.ModuleBeingProcessed.Equals(moduleManifestPath, StringComparison.OrdinalIgnoreCase) ||
                     !Context.ModuleBeingProcessed.Equals(Context.PreviousModuleProcessed,
                         StringComparison.OrdinalIgnoreCase)))
                {
                    // Having both ModuleToProcess and RootModule is not allowed
                    if (writingErrors)
                    {
                        message = StringUtil.Format(Modules.ModuleManifestCannotContainBothModuleToProcessAndRootModule,
                            moduleManifestPath);
                        InvalidOperationException ioe = new InvalidOperationException(message);
                        ErrorRecord er = new ErrorRecord(ioe,
                            "Modules_ModuleManifestCannotContainBothModuleToProcessAndRootModule",
                            ErrorCategory.InvalidOperation, moduleManifestPath);
                        WriteError(er);
                    }
                }
                if (bailOnFirstError) return null;
            }

            string actualRootModule = moduleToProcess ?? rootModule;

            bool actualRootModuleIsXaml = false;
            // For workflow modules, the actualRootModule is added to workflowsToProcess and is nulled out. 
            // We need to save this name so that we can assign this to the RootModule property of ModuleInfo
            string savedActualRootModule = actualRootModule;

            if (string.Equals(System.IO.Path.GetExtension(actualRootModule),
                StringLiterals.WorkflowFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                if (WildcardPattern.ContainsWildcardCharacters(actualRootModule))
                {
                    PSInvalidOperationException invalidOperation = PSTraceSource.NewInvalidOperationException(
                        Modules.WildCardNotAllowedInModuleToProcessAndInNestedModules,
                        moduleManifestPath);
                    invalidOperation.SetErrorId("Modules_WildCardNotAllowedInModuleToProcessAndInNestedModules");
                    throw invalidOperation;
                }

                workflowsToProcess.Add(actualRootModule);
                actualRootModule = null;
                actualRootModuleIsXaml = true;
            }

            // extract defaultCommandPrefix from the manifest
            string defaultCommandPrefix = null;
            if (
                !GetScalarFromData<string>(data, moduleManifestPath, "DefaultCommandPrefix", manifestProcessingFlags,
                    out defaultCommandPrefix))
            {
                containedErrors = true;
                if (bailOnFirstError) return null;
            }

            string resolvedCommandPrefix = string.Empty;

            if (!string.IsNullOrEmpty(defaultCommandPrefix))
            {
                resolvedCommandPrefix = defaultCommandPrefix;
            }

            if (!string.IsNullOrEmpty(this.BasePrefix))
            {
                resolvedCommandPrefix = this.BasePrefix;
            }

            if (!string.IsNullOrEmpty(actualRootModule))
            {
                if (WildcardPattern.ContainsWildcardCharacters(actualRootModule))
                {
                    PSInvalidOperationException invalidOperation = PSTraceSource.NewInvalidOperationException(
                        Modules.WildCardNotAllowedInModuleToProcessAndInNestedModules,
                        moduleManifestPath);
                    invalidOperation.SetErrorId("Modules_WildCardNotAllowedInModuleToProcessAndInNestedModules");
                    throw invalidOperation;
                }
                // See if this module is already loaded. Since the manifest entry may not
                // have an extension and the module table is indexed by full names, we
                // may have search through all the extensions.
                PSModuleInfo loadedModule = null;
                string rootedPath = this.FixupFileName(moduleBase, actualRootModule, null);
                string mtpExtension = Path.GetExtension(rootedPath);
                if (!string.IsNullOrEmpty(mtpExtension) && ModuleIntrinsics.IsPowerShellModuleExtension(mtpExtension))
                {
                    Context.Modules.ModuleTable.TryGetValue(rootedPath, out loadedModule);
                }
                else
                {
                    foreach (string extensionToTry in ModuleIntrinsics.PSModuleExtensions)
                    {
                        rootedPath = this.FixupFileName(moduleBase, actualRootModule, extensionToTry);
                        Context.Modules.ModuleTable.TryGetValue(rootedPath, out loadedModule);
                        if (loadedModule != null)
                            break;
                    }
                }

                // TODO/FIXME: use IsModuleAlreadyLoaded to get consistent behavior
                // if (IsModuleAlreadyLoaded(loadedModule) &&
                //    ((manifestProcessingFlags & ManifestProcessingFlags.LoadElements) == ManifestProcessingFlags.LoadElements))
                if (loadedModule != null &&
                    (BaseRequiredVersion == null || loadedModule.Version.Equals(BaseRequiredVersion)) &&
                    ((BaseMinimumVersion == null && BaseMaximumVersion == null)
                     ||
                     (BaseMaximumVersion != null && BaseMinimumVersion == null &&
                      loadedModule.Version <= BaseMaximumVersion)
                     ||
                     (BaseMaximumVersion == null && BaseMinimumVersion != null &&
                      loadedModule.Version >= BaseMinimumVersion)
                     ||
                     (BaseMaximumVersion != null && BaseMinimumVersion != null &&
                      loadedModule.Version >= BaseMinimumVersion && loadedModule.Version <= BaseMaximumVersion)) &&
                    (BaseGuid == null || loadedModule.Guid.Equals(BaseGuid)) && importingModule)
                {
                    if (!BaseForce)
                    {
                        AddModuleToModuleTables(this.Context, this.TargetSessionState.Internal, loadedModule);
                        // Even if the module has been loaded, import the specified members...
                        ImportModuleMembers(loadedModule, resolvedCommandPrefix, options);

                        return loadedModule;
                    }
                    // remove the module if force is specified  (and if module is already loaded)
                    else if (Utils.NativeFileExists(rootedPath))
                    {
                        RemoveModule(loadedModule);
                    }
                }
            }

            // END: Check if the ModuleToProcess is already loaded..

            string author = string.Empty;
            if (!GetScalarFromData(data, moduleManifestPath, "Author", manifestProcessingFlags, out author))
            {
                containedErrors = true;
                if (bailOnFirstError) return null;
            }

            string companyName = string.Empty;
            if (!GetScalarFromData(data, moduleManifestPath, "CompanyName", manifestProcessingFlags, out companyName))
            {
                containedErrors = true;
                if (bailOnFirstError) return null;
            }

            string copyright = string.Empty;
            if (!GetScalarFromData(data, moduleManifestPath, "Copyright", manifestProcessingFlags, out copyright))
            {
                containedErrors = true;
                if (bailOnFirstError) return null;
            }

            Guid? manifestGuid;
            if (!GetScalarFromData(data, moduleManifestPath, "guid", manifestProcessingFlags, out manifestGuid))
            {
                containedErrors = true;
                if (bailOnFirstError) return null;
            }

            // Verify that the module manifest contains the module version information
            Version moduleVersion;
            if (
                !GetScalarFromData<Version>(data, moduleManifestPath, "ModuleVersion", manifestProcessingFlags,
                    out moduleVersion))
            {
                containedErrors = true;
                if (bailOnFirstError) return null;
            }
            else
            {
                if (moduleVersion == null)
                {
                    containedErrors = true;
                    if (writingErrors)
                    {
                        message = StringUtil.Format(Modules.ModuleManifestMissingModuleVersion, moduleManifestPath);
                        MissingMemberException mm = new MissingMemberException(message);
                        ErrorRecord er = new ErrorRecord(mm, "Modules_InvalidManifest",
                            ErrorCategory.ResourceUnavailable, moduleManifestPath);
                        WriteError(er);
                    }
                    if (bailOnFirstError) return null;
                }
                else if (requiredVersion != null && !moduleVersion.Equals(requiredVersion))
                {
                    if (bailOnFirstError) return null;
                }
                else
                {
                    if (moduleVersion < minimumVersion || (maximumVersion != null && moduleVersion > maximumVersion))
                    {
                        if (bailOnFirstError) return null;
                    }
                }

                if (requiredModuleGuid != null && !requiredModuleGuid.Equals(manifestGuid))
                {
                    if (bailOnFirstError) return null;
                }

                // Verify that the module version from the module manifest is equal to module version folder.
                DirectoryInfo parent = null;
                try
                {
                    parent = Directory.GetParent(moduleManifestPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (ArgumentException)
                {
                }

                Version moduleVersionFromFolderName;
                if (parent != null &&
                    Version.TryParse(parent.Name, out moduleVersionFromFolderName) &&
                    parent.Parent != null &&
                    parent.Parent.Name.Equals(Path.GetFileNameWithoutExtension(moduleManifestPath)))
                {
                    if (!moduleVersionFromFolderName.Equals(moduleVersion))
                    {
                        containedErrors = true;
                        if (writingErrors)
                        {
                            message = StringUtil.Format(Modules.InvalidModuleManifestVersion, moduleManifestPath,
                                moduleVersion.ToString(), parent.FullName);
                            var ioe = new InvalidOperationException(message);
                            var er = new ErrorRecord(ioe, "Modules_InvalidModuleManifestVersion",
                                ErrorCategory.InvalidArgument, moduleManifestPath);
                            WriteError(er);
                        }

                        if (bailOnFirstError) return null;
                    }
                }
            }

            // Test that the PowerShellVersion version is valid...
            Version powerShellVersion;
            if (
                !GetScalarFromData<Version>(data, moduleManifestPath, "PowerShellVersion", manifestProcessingFlags,
                    out powerShellVersion))
            {
                containedErrors = true;
                if (bailOnFirstError) return null;
            }
            else if (powerShellVersion != null)
            {
                Version currentPowerShellVersion = PSVersionInfo.PSVersion;
                if (currentPowerShellVersion < powerShellVersion)
                {
                    containedErrors = true;
                    if (writingErrors)
                    {
                        message = StringUtil.Format(Modules.ModuleManifestInsufficientPowerShellVersion,
                            currentPowerShellVersion,
                            moduleManifestPath, powerShellVersion);
                        InvalidOperationException ioe = new InvalidOperationException(message);
                        ErrorRecord er = new ErrorRecord(ioe, "Modules_InsufficientPowerShellVersion",
                            ErrorCategory.ResourceUnavailable, moduleManifestPath);
                        WriteError(er);
                    }
                    if (bailOnFirstError) return null;
                }
            }

            // Test that the PowerShellHostVersion version is valid...
            string requestedHostName;
            if (
                !GetScalarFromData(data, moduleManifestPath, "PowerShellHostName", manifestProcessingFlags,
                    out requestedHostName))
            {
                containedErrors = true;
                // Ignore errors related to HostVersion as per the ManifestProcessingFlags
                // doing this at this place because we have to set "containedErrors"
                if ((0 == (manifestProcessingFlags & ManifestProcessingFlags.IgnoreHostNameAndHostVersion)) &&
                    bailOnFirstError)
                    return null;
            }
            else if (requestedHostName != null)
            {
                string currentHostName = this.Context.InternalHost.Name;
                if (!string.Equals(currentHostName, requestedHostName, StringComparison.OrdinalIgnoreCase))
                {
                    containedErrors = true;
                    // Ignore errors related to HostVersion as per the ManifestProcessingFlags
                    // doing this at this place because we have to set "containedErrors"
                    if (0 == (manifestProcessingFlags & ManifestProcessingFlags.IgnoreHostNameAndHostVersion))
                    {
                        if (writingErrors)
                        {
                            message = StringUtil.Format(Modules.InvalidPowerShellHostName,
                                currentHostName, moduleManifestPath, requestedHostName);
                            InvalidOperationException ioe = new InvalidOperationException(message);
                            ErrorRecord er = new ErrorRecord(ioe, "Modules_InvalidPowerShellHostName",
                                ErrorCategory.ResourceUnavailable, moduleManifestPath);
                            WriteError(er);
                        }
                        if (bailOnFirstError) return null;
                    }
                }
            }

            // Test that the PowerShellHostVersion version is valid...
            Version requestedHostVersion;
            if (
                !GetScalarFromData(data, moduleManifestPath, "PowerShellHostVersion", manifestProcessingFlags,
                    out requestedHostVersion))
            {
                containedErrors = true;
                // Ignore errors related to HostVersion as per the ManifestProcessingFlags
                // doing this at this place because we have to set "containedErrors"
                if ((0 == (manifestProcessingFlags & ManifestProcessingFlags.IgnoreHostNameAndHostVersion)) &&
                    bailOnFirstError)
                    return null;
            }
            else if (requestedHostVersion != null)
            {
                Version currentHostVersion = this.Context.InternalHost.Version;
                if (currentHostVersion < requestedHostVersion)
                {
                    containedErrors = true;
                    // Ignore errors related to HostVersion as per the ManifestProcessingFlags
                    // doing this at this place because we have to set "containedErrors"
                    if (0 == (manifestProcessingFlags & ManifestProcessingFlags.IgnoreHostNameAndHostVersion))
                    {
                        if (writingErrors)
                        {
                            string currentHostName = this.Context.InternalHost.Name;
                            message = StringUtil.Format(Modules.InvalidPowerShellHostVersion,
                                currentHostName, currentHostVersion, moduleManifestPath, requestedHostVersion);
                            InvalidOperationException ioe = new InvalidOperationException(message);
                            ErrorRecord er = new ErrorRecord(ioe, "Modules_InsufficientPowerShellHostVersion",
                                ErrorCategory.ResourceUnavailable, moduleManifestPath);
                            WriteError(er);
                        }
                        if (bailOnFirstError) return null;
                    }
                }
            }

            // Test the required processor architecture
            ProcessorArchitecture requiredProcesorArchitecture;
            if (
                !GetScalarFromData(data, moduleManifestPath, "ProcessorArchitecture", manifestProcessingFlags,
                    out requiredProcesorArchitecture))
            {
                containedErrors = true;
                if (bailOnFirstError) return null;
            }
            else if ((requiredProcesorArchitecture != ProcessorArchitecture.None) &&
                     (requiredProcesorArchitecture != ProcessorArchitecture.MSIL))
            {
                bool isRunningOnArm = false;
                ProcessorArchitecture currentArchitecture = PsUtils.GetProcessorArchitecture(out isRunningOnArm);

                // For ARM Architectures, we need to do additional string-level comparison
                if ((currentArchitecture != requiredProcesorArchitecture && !isRunningOnArm) ||
                    (isRunningOnArm &&
                     !requiredProcesorArchitecture.ToString()
                         .Equals(PsUtils.ArmArchitecture, StringComparison.OrdinalIgnoreCase)))
                {
                    containedErrors = true;
                    if (writingErrors)
                    {
                        string actualCurrentArchitecture = isRunningOnArm
                            ? PsUtils.ArmArchitecture
                            : currentArchitecture.ToString();
                        message = StringUtil.Format(Modules.InvalidProcessorArchitecture,
                            actualCurrentArchitecture, moduleManifestPath, requiredProcesorArchitecture);
                        InvalidOperationException ioe = new InvalidOperationException(message);
                        ErrorRecord er = new ErrorRecord(ioe, "Modules_InvalidProcessorArchitecture",
                            ErrorCategory.ResourceUnavailable, moduleManifestPath);
                        WriteError(er);
                    }
                    if (bailOnFirstError) return null;
                }
            }

            // Test the required CLR version
            Version requestedClrVersion;
            if (
                !GetScalarFromData(data, moduleManifestPath, "CLRVersion", manifestProcessingFlags,
                    out requestedClrVersion))
            {
                containedErrors = true;
                if (bailOnFirstError) return null;
            }
#if !CORECLR // CLR version is not applicable to CoreCLR
            else if (requestedClrVersion != null)
            {
                Version currentClrVersion = Environment.Version;
                if (currentClrVersion < requestedClrVersion)
                {
                    containedErrors = true;
                    if (writingErrors)
                    {
                        message = StringUtil.Format(Modules.ModuleManifestInsufficientCLRVersion, currentClrVersion,
                            moduleManifestPath, requestedClrVersion);
                        InvalidOperationException ioe = new InvalidOperationException(message);
                        ErrorRecord er = new ErrorRecord(ioe, "Modules_InsufficientCLRVersion",
                            ErrorCategory.ResourceUnavailable, moduleManifestPath);
                        WriteError(er);
                    }
                    if (bailOnFirstError) return null;
                }
            }
#endif

            // Test the required .NET Framework version
            Version requestedDotNetFrameworkVersion;
            if (
                !GetScalarFromData(data, moduleManifestPath, "DotNetFrameworkVersion", manifestProcessingFlags,
                    out requestedDotNetFrameworkVersion))
            {
                containedErrors = true;
                if (bailOnFirstError) return null;
            }
#if !CORECLR // .NET Framework Version is not applicable to CoreCLR
            else if (requestedDotNetFrameworkVersion != null)
            {
                bool higherThanKnownHighestVersion = false;
                if (
                    !Utils.IsNetFrameworkVersionSupported(requestedDotNetFrameworkVersion,
                        out higherThanKnownHighestVersion))
                {
                    containedErrors = true;
                    if (writingErrors)
                    {
                        message = StringUtil.Format(Modules.InvalidDotNetFrameworkVersion,
                            moduleManifestPath, requestedDotNetFrameworkVersion);
                        InvalidOperationException ioe = new InvalidOperationException(message);
                        ErrorRecord er = new ErrorRecord(ioe, "Modules_InsufficientDotNetFrameworkVersion",
                            ErrorCategory.ResourceUnavailable, moduleManifestPath);
                        WriteError(er);
                    }
                    if (bailOnFirstError) return null;
                }
                else if (higherThanKnownHighestVersion)
                {
                    string cannotDetectNetFrameworkVersionMessage =
                        StringUtil.Format(Modules.CannotDetectNetFrameworkVersion, requestedDotNetFrameworkVersion);
                    WriteVerbose(cannotDetectNetFrameworkVersionMessage);
                }
            }
#endif

            // HelpInfo URI
            string helpInfoUri = null;

            GetScalarFromData<string>(data, moduleManifestPath, "HelpInfoURI", manifestProcessingFlags, out helpInfoUri);

            List<PSModuleInfo> requiredModulesLoaded = new List<PSModuleInfo>();

            // In case of Get-Module -List and Test-ModuleManifest, we populate this list with fake PSModuleInfo for each of the required modules specified
            List<PSModuleInfo> requiredModulesSpecifiedInModuleManifest = new List<PSModuleInfo>();

            ModuleSpecification[] requiredModules;
            if (
                !GetScalarFromData(data, moduleManifestPath, "RequiredModules", manifestProcessingFlags,
                    out requiredModules))
            {
                containedErrors = true;
                if (bailOnFirstError) return null;
            }
            else if ((requiredModules != null))
            {
                if (importingModule)
                {
                    // We need to pass in the moduleInfo of current module so that we can check for cyclic dependency in RequiredModules specification.
                    // When a module A that has a required module B is imported (either explicitly or via auto-loading), 
                    // the current scope's module gets set to module A incorrectly. This leads to issues where the exported commands 
                    // of a module that imports module A) get added to module A instead of the actual module that is getting imported. 
                    // (Direct cause of Win8:336812)
                    PSModuleInfo fakeManifestInfo = new PSModuleInfo(moduleManifestPath, Context, null);
                    if (manifestGuid.HasValue)
                    {
                        fakeManifestInfo.SetGuid(manifestGuid.Value);
                    }
                    if (moduleVersion != null)
                    {
                        fakeManifestInfo.SetVersion(moduleVersion);
                    }

                    foreach (ModuleSpecification requiredModule in requiredModules)
                    {
                        ErrorRecord error = null;
                        PSModuleInfo module = LoadRequiredModule(fakeManifestInfo, requiredModule, moduleManifestPath,
                            manifestProcessingFlags, containedErrors, out error);
                        if (module == null && error != null)
                        {
                            WriteError(error);
                            return null;
                        }
                        // module can be null (in case the RequiredModule has already been added as a snapin.
                        // In that case, we just emit a warning and do not add the snapin to the list of requiredmodules)
                        if (module != null)
                        {
                            requiredModulesLoaded.Add(module);
                        }
                    }
                }
                else
                {
                    PSModuleInfo fakeRequiredModuleInfo = null;
                    foreach (ModuleSpecification requiredModule in requiredModules)
                    {
                        fakeRequiredModuleInfo = new PSModuleInfo(requiredModule.Name, Context, null);
                        if (requiredModule.Guid.HasValue)
                        {
                            fakeRequiredModuleInfo.SetGuid(requiredModule.Guid.Value);
                        }
                        fakeRequiredModuleInfo.SetVersion(requiredModule.RequiredVersion ?? requiredModule.Version);

                        requiredModulesSpecifiedInModuleManifest.Add(fakeRequiredModuleInfo);
                    }
                }
            }

            // Validate the list of nestedModules...
            ModuleSpecification[] tmpNestedModules;
            if (
                !GetScalarFromData(data, moduleManifestPath, "NestedModules", manifestProcessingFlags,
                    out tmpNestedModules))
            {
                containedErrors = true;
                if (bailOnFirstError) return null;
            }

            List<ModuleSpecification> nestedModules = new List<ModuleSpecification>();

            if (tmpNestedModules != null && tmpNestedModules.Length > 0)
            {
                foreach (ModuleSpecification s in tmpNestedModules)
                {
                    s.Name = GetAbsolutePath(moduleBase, s.Name);
                    if (WildcardPattern.ContainsWildcardCharacters(s.Name))
                    {
                        PSInvalidOperationException invalidOperation = PSTraceSource.NewInvalidOperationException(
                            Modules.WildCardNotAllowedInModuleToProcessAndInNestedModules,
                            moduleManifestPath);
                        invalidOperation.SetErrorId("Modules_WildCardNotAllowedInModuleToProcessAndInNestedModules");
                        throw invalidOperation;
                    }

                    if (string.Equals(System.IO.Path.GetExtension(s.Name),
                        StringLiterals.WorkflowFileExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        workflowsToProcess.Add(s.Name);
                    }
                    else
                    {
                        nestedModules.Add(s);
                    }
                }
                Array.Clear(tmpNestedModules, 0, tmpNestedModules.Length);
            }


            // Set the private data member for the module if the manifest contains
            // this member
            object privateData = null;
            if (data.Contains("PrivateData"))
            {
                privateData = data["PrivateData"];
            }

            // Process all of the exports...
            List<WildcardPattern> exportedFunctions;
            if (
                !GetListOfWildcardsFromData(data, moduleManifestPath, "FunctionsToExport", manifestProcessingFlags,
                    out exportedFunctions))
            {
                containedErrors = true;
                if (bailOnFirstError) return null;
            }

            List<WildcardPattern> exportedVariables;
            if (
                !GetListOfWildcardsFromData(data, moduleManifestPath, "VariablesToExport", manifestProcessingFlags,
                    out exportedVariables))
            {
                containedErrors = true;
                if (bailOnFirstError) return null;
            }

            List<WildcardPattern> exportedAliases;
            if (
                !GetListOfWildcardsFromData(data, moduleManifestPath, "AliasesToExport", manifestProcessingFlags,
                    out exportedAliases))
            {
                containedErrors = true;
                if (bailOnFirstError) return null;
            }

            List<WildcardPattern> exportedCmdlets;
            if (
                !GetListOfWildcardsFromData(data, moduleManifestPath, "CmdletsToExport", manifestProcessingFlags,
                    out exportedCmdlets))
            {
                containedErrors = true;
                if (bailOnFirstError) return null;
            }

            List<WildcardPattern> exportedDscResources;
            if (
                !GetListOfWildcardsFromData(data, moduleManifestPath, "DscResourcesToExport", manifestProcessingFlags,
                    out exportedDscResources))
            {
                containedErrors = true;
                if (bailOnFirstError) return null;
            }

            InitialSessionState iss = null;
            if (importingModule)
            {
                iss = InitialSessionState.Create();
                if (Context.InitialSessionState != null)
                {
                    iss.DisableFormatUpdates = Context.InitialSessionState.DisableFormatUpdates;
                }
                // We want the processing errors to terminate module import. 
                iss.ThrowOnRunspaceOpenError = true;
            }

            // Indicates the ISS.Bind() should be called...
            bool doBind = false;
            // Indicates that RunspaceConfig.Update() should be called.
            bool doUpdate = false;

            // Set up to load any required assemblies  that have been specified...
            List<string> tmpAssemblyList;
            List<string> assemblyList = new List<string>();
            List<string> fixedUpAssemblyPathList = new List<string>();

            if (
                !GetListOfStringsFromData(data, moduleManifestPath, "RequiredAssemblies", manifestProcessingFlags,
                    out tmpAssemblyList))
            {
                containedErrors = true;
                if (bailOnFirstError) return null;
            }
            else
            {
                if (tmpAssemblyList != null && tmpAssemblyList.Count > 0)
                {
                    foreach (string assembly in tmpAssemblyList)
                    {
                        if (string.Equals(System.IO.Path.GetExtension(assembly),
                            StringLiterals.WorkflowFileExtension, StringComparison.OrdinalIgnoreCase))
                        {
                            dependentWorkflows.Add(assembly);
                        }
                        else
                        {
                            assemblyList.Add(assembly);
                        }
                    }
                }

                if ((assemblyList != null) && importingModule)
                {
                    foreach (string assembly in assemblyList)
                    {
                        if (WildcardPattern.ContainsWildcardCharacters(assembly))
                        {
                            PSInvalidOperationException invalidOperation = PSTraceSource.NewInvalidOperationException(
                                Modules.WildCardNotAllowedInRequiredAssemblies,
                                moduleManifestPath);
                            invalidOperation.SetErrorId("Modules_WildCardNotAllowedInRequiredAssemblies");
                            throw invalidOperation;
                        }
                        else
                        {
                            string fileName = FixupFileName(moduleBase, assembly, StringLiterals.PowerShellNgenAssemblyExtension);
                            string loadMessage = StringUtil.Format(Modules.LoadingFile, "Assembly", fileName);
                            WriteVerbose(loadMessage);
                            iss.Assemblies.Add(new SessionStateAssemblyEntry(assembly, fileName));
                            fixedUpAssemblyPathList.Add(fileName);

                            fileName = FixupFileName(moduleBase, assembly, StringLiterals.DependentWorkflowAssemblyExtension);

                            loadMessage = StringUtil.Format(Modules.LoadingFile, "Assembly", fileName);
                            WriteVerbose(loadMessage);
                            iss.Assemblies.Add(new SessionStateAssemblyEntry(assembly, fileName));
                            fixedUpAssemblyPathList.Add(fileName);
                            doBind = true;
                        }
                    }
                }
            }

            // Set up to load any types files that have been specified...
            List<string> exportedTypeFiles;
            if (
                !GetListOfFilesFromData(data, moduleManifestPath, "TypesToProcess", manifestProcessingFlags, moduleBase,
                    ".ps1xml", true, out exportedTypeFiles))
            {
                containedErrors = true;
                if (bailOnFirstError) return null;
            }
            else
            {
                if ((exportedTypeFiles != null) && importingModule)
                {
                    foreach (string fileName in exportedTypeFiles)
                    {
                        string loadMessage = StringUtil.Format(Modules.LoadingFile, "TypesToProcess", fileName);
                        WriteVerbose(loadMessage);

                        if (Context.RunspaceConfiguration != null)
                        {
                            this.Context.RunspaceConfiguration.Types.Append(new TypeConfigurationEntry(fileName));
                            doUpdate = true;
                        }
                        else
                        {
                            bool isAlreadyLoaded = false;
                            string resolvedFileName = ResolveRootedFilePath(fileName, Context) ?? fileName;
                            foreach (var entry in Context.InitialSessionState.Types)
                            {
                                if (entry.FileName == null)
                                {
                                    continue;
                                }

                                string resolvedEntryFileName = ResolveRootedFilePath(entry.FileName, Context) ??
                                                               entry.FileName;
                                if (resolvedEntryFileName.Equals(resolvedFileName, StringComparison.OrdinalIgnoreCase))
                                {
                                    isAlreadyLoaded = true;
                                    break;
                                }
                            }

                            if (!isAlreadyLoaded)
                            {
                                iss.Types.Add(new SessionStateTypeEntry(fileName));
                                doBind = true;
                            }
                        }
                    }
                }
            }

            // Set up to load any format files that have been specified...
            List<string> exportedFormatFiles;
            if (
                !GetListOfFilesFromData(data, moduleManifestPath, "FormatsToProcess", manifestProcessingFlags,
                    moduleBase, ".ps1xml", true, out exportedFormatFiles))
            {
                containedErrors = true;
                if (bailOnFirstError) return null;
            }
            else
            {
                if ((exportedFormatFiles != null) && importingModule)
                {
                    foreach (string fileName in exportedFormatFiles)
                    {
                        string loadMessage = StringUtil.Format(Modules.LoadingFile, "FormatsToProcess", fileName);
                        WriteVerbose(loadMessage);

                        if (Context.RunspaceConfiguration != null)
                        {
                            this.Context.RunspaceConfiguration.Formats.Append(new FormatConfigurationEntry(fileName));
                            doUpdate = true;
                        }
                        else
                        {
                            bool isAlreadyLoaded = false;
                            foreach (var entry in Context.InitialSessionState.Formats)
                            {
                                if (entry.FileName == null)
                                {
                                    continue;
                                }
                                if (entry.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                                {
                                    isAlreadyLoaded = true;
                                    break;
                                }
                            }

                            if (!isAlreadyLoaded)
                            {
                                iss.Formats.Add(new SessionStateFormatEntry(fileName));
                                doBind = true;
                            }
                        }
                    }
                }
            }

            // scripts to process
            List<string> scriptsToProcess;
            if (
                !GetListOfFilesFromData(data, moduleManifestPath, "ScriptsToProcess", manifestProcessingFlags,
                    moduleBase, ".ps1", true, out scriptsToProcess))
            {
                containedErrors = true;
                if (bailOnFirstError) return null;
            }
            else
            {
                if ((scriptsToProcess != null) && importingModule)
                {
                    foreach (string scriptFile in scriptsToProcess)
                    {
                        if (!Path.GetExtension(scriptFile).Equals(".ps1", StringComparison.OrdinalIgnoreCase))
                        {
                            string errorMessage = StringUtil.Format(Modules.ScriptsToProcessIncorrectExtension,
                                scriptFile);
                            InvalidOperationException ioe = new InvalidOperationException(errorMessage);
                            WriteInvalidManifestMemberError(this, "ScriptsToProcess", moduleManifestPath, ioe,
                                manifestProcessingFlags);

                            containedErrors = true;
                            if (bailOnFirstError) return null;
                        }
                    }
                }
            }

            // Now add the metadata to the module info object for the manifest...
            string description = String.Empty;
            if (data.Contains("Description"))
            {
                if (localizedData != null && localizedData.Contains("Description"))
                {
                    description = (string)LanguagePrimitives.ConvertTo(localizedData["Description"],
                        typeof(string), CultureInfo.InvariantCulture);
                }
                if (string.IsNullOrEmpty(description))
                {
                    description = (string)LanguagePrimitives.ConvertTo(data["Description"],
                        typeof(string), CultureInfo.InvariantCulture);
                }
            }

            // Process "FileList"
            List<string> fileList;
            if (
                !GetListOfFilesFromData(data, moduleManifestPath, "FileList", manifestProcessingFlags, moduleBase, ""
                    /*extension*/,
                    false
                    /* don't check file existence - don't want to change current behavior without feature team discussion */,
                    out fileList))
            {
                containedErrors = true;
                if (bailOnFirstError) return null;
            }

            // Process "ModuleList
            ModuleSpecification[] moduleList;
            if (
                !GetScalarFromData<ModuleSpecification[]>(data, moduleManifestPath, "ModuleList",
                    manifestProcessingFlags, out moduleList))
            {
                containedErrors = true;
                if (bailOnFirstError) return null;
            }

            // Process "CompatiblePSEditions"
            string[] compatiblePSEditions;
            if (
                !GetScalarFromData<string[]>(data, moduleManifestPath, "CompatiblePSEditions",
                    manifestProcessingFlags, out compatiblePSEditions))
            {
                containedErrors = true;
                if (bailOnFirstError) return null;
            }

            // Process format.ps1xml / types.ps1.xml / RequiredAssemblies
            // as late as possible, but before ModuleToProcess, ScriptToProcess, NestedModules
            if (importingModule)
            {
                // Do the InitialSessionState binding...
                if (doBind)
                {
                    try
                    {
                        iss.Bind(Context, /*updateOnly*/ true);
                    }
                    catch (Exception e)
                    {
                        CommandProcessorBase.CheckForSevereException(e);
                        this.RemoveTypesAndFormatting(exportedFormatFiles, exportedTypeFiles);
                        ErrorRecord er = new ErrorRecord(e, "FormatXmlUpdateException", ErrorCategory.InvalidOperation,
                            null);
                        if (bailOnFirstError)
                        {
                            this.ThrowTerminatingError(er);
                        }
                        else if (writingErrors)
                        {
                            this.WriteError(er);
                        }
                    }
                }

                // Do the runspaceconfig binding...
                if (doUpdate)
                {
                    try
                    {
                        this.Context.CurrentRunspace.RunspaceConfiguration.Types.Update(true);
                        this.Context.CurrentRunspace.RunspaceConfiguration.Formats.Update(true);
                    }
                    catch (Exception e)
                    {
                        CommandProcessorBase.CheckForSevereException(e);
                        this.RemoveTypesAndFormatting(exportedFormatFiles, exportedTypeFiles);
                        ErrorRecord er = new ErrorRecord(e, "FormatXmlUpdateException", ErrorCategory.InvalidOperation,
                            null);
                        if (bailOnFirstError)
                        {
                            this.ThrowTerminatingError(er);
                        }
                        else if (writingErrors)
                        {
                            this.WriteError(er);
                        }
                    }
                }
            }

            // Create a PSModuleInfo for psd1 file or take one from ModuleToProcess manifest field
            // SessionState is created for psd1 file or taken from ModuleToProcess module (unless it is a binary module)
            // If we're loading the manifest, allocate a session state object for it...
            var ss = importingModule ? new SessionState(Context, true, true) : null;
            var manifestInfo = new PSModuleInfo(moduleManifestPath, Context, ss);

            // Default to a Manifest module type...
            manifestInfo.SetModuleType(ModuleType.Manifest);

            if (importingModule)
            {
                SetModuleLoggingInformation(manifestInfo);
            }

            if (requiredModulesSpecifiedInModuleManifest != null && requiredModulesSpecifiedInModuleManifest.Count > 0)
            {
                foreach (var module in requiredModulesSpecifiedInModuleManifest)
                {
                    manifestInfo.AddRequiredModule(module);
                }
            }

            // If there is a session state, set up to import/export commands and variables
            if (ss != null)
            {
                ss.Internal.SetVariable(SpecialVariables.PSScriptRootVarPath, Path.GetDirectoryName(moduleManifestPath),
                    true, CommandOrigin.Internal);
                ss.Internal.SetVariable(SpecialVariables.PSCommandPathVarPath, moduleManifestPath, true,
                    CommandOrigin.Internal);
                ss.Internal.Module = manifestInfo;

                // without ModuleToProcess a manifest will export everything by default
                // (otherwise we want to honour exports from ModuleToProcess)
                if (exportedFunctions == null)
                {
                    exportedFunctions = MatchAll;
                }
                if (exportedCmdlets == null)
                {
                    exportedCmdlets = MatchAll;
                }
                if (exportedVariables == null)
                {
                    exportedVariables = MatchAll;
                }
                if (exportedAliases == null)
                {
                    exportedAliases = MatchAll;
                }
                if (exportedDscResources == null)
                {
                    exportedDscResources = MatchAll;
                }
            }

            manifestInfo.Description = description;
            manifestInfo.PrivateData = privateData;
            manifestInfo.SetExportedTypeFiles(new ReadOnlyCollection<string>(exportedTypeFiles ?? new List<string>()));
            manifestInfo.SetExportedFormatFiles(new ReadOnlyCollection<string>(exportedFormatFiles ?? new List<string>()));
            manifestInfo.SetVersion(moduleVersion);
            manifestInfo.Author = author;
            manifestInfo.CompanyName = companyName;
            manifestInfo.Copyright = copyright;
            manifestInfo.DotNetFrameworkVersion = requestedDotNetFrameworkVersion;
            manifestInfo.ClrVersion = requestedClrVersion;
            manifestInfo.PowerShellHostName = requestedHostName;
            manifestInfo.PowerShellHostVersion = requestedHostVersion;
            manifestInfo.PowerShellVersion = powerShellVersion;
            manifestInfo.ProcessorArchitecture = requiredProcesorArchitecture;
            manifestInfo.Prefix = resolvedCommandPrefix;
            if (assemblyList != null)
            {
                foreach (var a in assemblyList)
                {
                    manifestInfo.AddRequiredAssembly(a);
                }
            }
            if (fileList != null)
            {
                foreach (var f in fileList)
                {
                    string absuluteFilePath = GetAbsolutePath(moduleBase, f);
                    manifestInfo.AddToFileList(absuluteFilePath);
                }
            }
            if (moduleList != null)
            {
                foreach (var m in moduleList)
                {
                    m.Name = GetAbsolutePath(moduleBase, m.Name);
                    manifestInfo.AddToModuleList(m);
                }
            }
            if (compatiblePSEditions != null)
            {
                foreach (var psEdition in compatiblePSEditions)
                {
                    manifestInfo.AddToCompatiblePSEditions(psEdition);
                }
            }
            if (scriptsToProcess != null)
            {
                foreach (var s in scriptsToProcess)
                {
                    manifestInfo.AddScript(s);
                }
            }
            manifestInfo.RootModule = savedActualRootModule;
            manifestInfo.RootModuleForManifest = savedActualRootModule;
            if (manifestGuid != null)
            {
                manifestInfo.SetGuid((Guid)manifestGuid);
            }
            if (helpInfoUri != null)
            {
                manifestInfo.SetHelpInfoUri(helpInfoUri);
            }
            foreach (PSModuleInfo module in requiredModulesLoaded)
            {
                manifestInfo.AddRequiredModule(module);
            }
            if (requiredModules != null)
            {
                foreach (ModuleSpecification moduleSpecification in requiredModules)
                {
                    manifestInfo.AddRequiredModuleSpecification(moduleSpecification);
                }
            }

            // Populate the RepositorySourceLocation property from PSGetModuleInfo.xml file if it is available under the module base folder.
            var psgetItemInfoXml = Path.Combine(moduleBase, "PSGetModuleInfo.xml");
            if (File.Exists(psgetItemInfoXml))
            {
                WriteVerbose(StringUtil.Format(Modules.PopulatingRepositorySourceLocation, manifestInfo.Name));
                try
                {
                    using (TextReader reader = File.OpenText(psgetItemInfoXml))
                    {
                        PSObject xml = PSSerializer.Deserialize(reader.ReadToEnd()) as PSObject;
                        if (xml != null && xml.Properties["RepositorySourceLocation"] != null)
                        {
                            var repositorySourceLocation = xml.Properties["RepositorySourceLocation"].Value.ToString();
                            Uri repositorySourceLocationUri;

                            if (!String.IsNullOrWhiteSpace(repositorySourceLocation) &&
                                Uri.TryCreate(repositorySourceLocation, UriKind.RelativeOrAbsolute,
                                    out repositorySourceLocationUri))
                            {
                                manifestInfo.RepositorySourceLocation = repositorySourceLocationUri;
                            }
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (ArgumentException)
                {
                }
                catch (PathTooLongException)
                {
                }
                catch (NotSupportedException)
                {
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.Xml.XmlException)
                {
                }
            }

            // If there are any wildcards, or any of the exported* are empty,
            // we need to do static analysis.
            if (!importingModule)
            {
                bool usedWildcard = false;
                bool sawExportedFunctions = false;
                bool sawExportedCmdlets = false;
                bool sawExportedAliases = false;

                if (exportedFunctions != null)
                {
                    manifestInfo.DeclaredFunctionExports = new Collection<string>();

                    if (exportedFunctions.Count > 0)
                    {
                        foreach (WildcardPattern p in exportedFunctions)
                        {
                            string name = p.Pattern;
                            if (!WildcardPattern.ContainsWildcardCharacters(name))
                            {
                                manifestInfo.DeclaredFunctionExports.Add(AddPrefixToCommandName(name, defaultCommandPrefix));
                            }
                            else
                            {
                                usedWildcard = true;
                            }
                        }

                        if (manifestInfo.DeclaredFunctionExports.Count == 0)
                        {
                            manifestInfo.DeclaredFunctionExports = null;
                        }
                    }

                    sawExportedFunctions = true;
                }

                if (exportedCmdlets != null)
                {
                    manifestInfo.DeclaredCmdletExports = new Collection<string>();

                    if (exportedCmdlets.Count > 0)
                    {
                        foreach (WildcardPattern p in exportedCmdlets)
                        {
                            string name = p.Pattern;
                            if (!WildcardPattern.ContainsWildcardCharacters(name))
                            {
                                manifestInfo.DeclaredCmdletExports.Add(AddPrefixToCommandName(name, defaultCommandPrefix));
                            }
                            else
                            {
                                usedWildcard = true;
                            }
                        }

                        if (manifestInfo.DeclaredCmdletExports.Count == 0)
                        {
                            manifestInfo.DeclaredCmdletExports = null;
                        }
                    }

                    sawExportedCmdlets = true;
                }

                if (exportedAliases != null)
                {
                    manifestInfo.DeclaredAliasExports = new Collection<string>();

                    if (exportedAliases.Count > 0)
                    {
                        foreach (WildcardPattern p in exportedAliases)
                        {
                            string name = p.Pattern;
                            if (!WildcardPattern.ContainsWildcardCharacters(name))
                            {
                                manifestInfo.DeclaredAliasExports.Add(AddPrefixToCommandName(name, defaultCommandPrefix));
                            }
                            else
                            {
                                usedWildcard = true;
                            }
                        }

                        if (manifestInfo.DeclaredAliasExports.Count == 0)
                        {
                            manifestInfo.DeclaredAliasExports = null;
                        }
                    }

                    sawExportedAliases = true;
                }

                if (exportedVariables != null)
                {
                    manifestInfo.DeclaredVariableExports = new Collection<string>();

                    if (exportedVariables.Count > 0)
                    {
                        foreach (WildcardPattern p in exportedVariables)
                        {
                            string name = p.Pattern;
                            if (!WildcardPattern.ContainsWildcardCharacters(name))
                            {
                                manifestInfo.DeclaredVariableExports.Add(name);
                            }
                        }

                        if (manifestInfo.DeclaredVariableExports.Count == 0)
                        {
                            manifestInfo.DeclaredVariableExports = null;
                        }
                    }
                }

                var needToAnalyzeScriptModules = usedWildcard;

                if (!needToAnalyzeScriptModules)
                {
                    foreach (var nestedModule in nestedModules)
                    {
                        if (AnalysisCache.ModuleAnalysisViaGetModuleRequired(
                                nestedModule, sawExportedCmdlets, sawExportedFunctions, sawExportedAliases))
                        {
                            needToAnalyzeScriptModules = true;
                            break;
                        }
                    }

                    if (!needToAnalyzeScriptModules)
                    {
                        needToAnalyzeScriptModules = AnalysisCache.ModuleAnalysisViaGetModuleRequired(
                            actualRootModule, sawExportedCmdlets, sawExportedFunctions, sawExportedAliases);
                    }
                }

                bool etwEnabled = CommandDiscoveryEventSource.Log.IsEnabled();
                if (etwEnabled) CommandDiscoveryEventSource.Log.ModuleManifestAnalysisResult(manifestInfo.Path, !needToAnalyzeScriptModules);

                if (!needToAnalyzeScriptModules)
                {
                    return manifestInfo;
                }
            }

            if (scriptsToProcess != null)
            {
                foreach (string scriptFile in scriptsToProcess)
                {
                    bool found = false;
                    PSModuleInfo module = LoadModule(scriptFile,
                        moduleBase,
                        string.Empty, // prefix (-Prefix shouldn't be applied to dot sourced scripts)
                        null,
                        ref options,
                        manifestProcessingFlags,
                        out found);

                    // If we're in analysis, add the detected exports to this module's
                    // exports
                    if (found && (ss == null))
                    {
                        foreach (string detectedCmdlet in module.ExportedCmdlets.Keys)
                        {
                            manifestInfo.AddDetectedCmdletExport(detectedCmdlet);
                        }

                        foreach (string detectedFunction in module.ExportedFunctions.Keys)
                        {
                            manifestInfo.AddDetectedFunctionExport(detectedFunction);
                        }

                        foreach (string detectedAlias in module.ExportedAliases.Keys)
                        {
                            manifestInfo.AddDetectedAliasExport(detectedAlias,
                                module.ExportedAliases[detectedAlias].Definition);
                        }
                    }
                }
            }

            // Process nested modules

            // If we have no session state and are loading a module, return an error
            if ((ss == null) && importingModule)
            {
                containedErrors = true;
                if (writingErrors)
                {
                    string errorMessage =
                        StringUtil.Format(Modules.ModuleManifestNestedModulesCantGoWithModuleToProcess,
                            moduleManifestPath);
                    ErrorRecord errorRecord = new ErrorRecord(
                        new ArgumentException(errorMessage),
                        "Modules_BinaryModuleAndNestedModules",
                        ErrorCategory.InvalidArgument,
                        moduleManifestPath);
                    this.WriteError(errorRecord);
                }
            }

            // We have a session state, or are in module analysis

            // Save the various base cmdlet parameters so that we can import
            // all of the exported members from each nested module into this module...
            bool oldPassThru = BasePassThru;
            BasePassThru = false;
            List<WildcardPattern> oldVariablePatterns = BaseVariablePatterns;
            List<WildcardPattern> oldFunctionPatterns = BaseFunctionPatterns;
            List<WildcardPattern> oldAliasPatterns = BaseAliasPatterns;
            List<WildcardPattern> oldCmdletPatterns = BaseCmdletPatterns;
            BaseVariablePatterns = BaseFunctionPatterns = BaseAliasPatterns = BaseCmdletPatterns = MatchAll;
            bool oldBaseDisableNameChecking = BaseDisableNameChecking;
            BaseDisableNameChecking = true;

            SessionStateInternal oldSessionState = Context.EngineSessionState;
            try
            {
                if (importingModule)
                {
                    Dbg.Assert(ss != null, "ss should not be null");
                    Dbg.Assert(ss.Internal != null, "ss.Internal should not be null");
                }

                if (ss != null)
                {
                    Context.EngineSessionState = ss.Internal;
                }

                // Load all of the nested modules, which may have relative or absolute paths...

                // For nested modules, we need to set importmoduleoptions to false as they should not use the options set for parent module
                ImportModuleOptions nestedModuleOptions = new ImportModuleOptions();

                foreach (ModuleSpecification nestedModuleSpecification in nestedModules)
                {
                    bool found = false;
                    // Never load nested modules to the global scope.
                    bool oldGLobal = this.BaseGlobal;
                    this.BaseGlobal = false;

                    string shortModuleName = null;
                    if (nestedModuleSpecification.Name == _serviceCoreAssemblyFullName)
                    {
                        shortModuleName = _serviceCoreAssemblyShortName;
                    }

                    PSModuleInfo nestedModule;
                    if (
                        string.Equals(nestedModuleSpecification.Name, _serviceCoreAssemblyFullName,
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(nestedModuleSpecification.Name, _serviceCoreAssemblyShortName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        nestedModule = LoadServiceCoreModule(
                            manifestInfo,
                            moduleBase,
                            null, //SessionState
                            nestedModuleOptions,
                            manifestProcessingFlags,
                            false, // addToParentModueIfFound
                            out found);
                    }
                    else
                    {
                        nestedModule = LoadModuleNamedInManifest(
                            manifestInfo,
                            nestedModuleSpecification, // moduleName
                            moduleBase,
                            true, // searchModulePath
                            string.Empty, // prefix: no -Prefix added for nested modules
                            null,
                            nestedModuleOptions,
                            manifestProcessingFlags,
                            true,
                            true,
                            privateData,
                            out found,
                            shortModuleName);
                    }

                    this.BaseGlobal = oldGLobal;

                    // If found, add it to the parent's list of NestedModules
                    if (found)
                    {
                        // If we're in analysis, add the detected exports to this module's
                        // exports
                        if ((ss == null) && (nestedModule != null))
                        {
                            // If this was ServiceCore, don't process its exports - as these
                            // should not show up when a module defines WorkflowsToProcess
                            if (
                                !String.Equals(nestedModule.Name, _serviceCoreAssemblyShortName,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                foreach (string detectedCmdlet in nestedModule.ExportedCmdlets.Keys)
                                {
                                    manifestInfo.AddDetectedCmdletExport(detectedCmdlet);
                                }

                                foreach (var detectedFunction in nestedModule.ExportedFunctions.Keys)
                                {
                                    manifestInfo.AddDetectedFunctionExport(detectedFunction);
                                }

                                foreach (string detectedAlias in nestedModule.ExportedAliases.Keys)
                                {
                                    manifestInfo.AddDetectedAliasExport(detectedAlias,
                                        nestedModule.ExportedAliases[detectedAlias].Definition);
                                }
                            }
                        }
                        // If the NestedModules was a .ps1 script no module object would have been generated
                        // so there's nothing to add, otherwise add it to the list.
                        // When we are in analysis mode and we do not analyze the nested module (because the parent module has explicit exports), the nested modules do not get added to the list
                        if (nestedModule != null)
                        {
                            manifestInfo.AddNestedModule(nestedModule);
                        }
                    }
                    else
                    {
                        containedErrors = true;
                        string errorMessage = StringUtil.Format(Modules.ManifestMemberNotFound,
                            nestedModuleSpecification.Name, "NestedModules", moduleManifestPath);
                        FileNotFoundException fnf = new FileNotFoundException(errorMessage);
                        PSInvalidOperationException invalidOperation = new PSInvalidOperationException(
                            errorMessage, fnf, "Modules_ModuleFileNotFound", ErrorCategory.ResourceUnavailable,
                            ModuleIntrinsics.GetModuleName(moduleManifestPath));
                        throw invalidOperation;
                    }
                }


                if (actualRootModuleIsXaml)
                {
                    manifestInfo.SetModuleType(ModuleType.Workflow);
                }

                if (workflowsToProcess != null && workflowsToProcess.Count > 0)
                {
#if CORECLR // Workflow Not Supported On CSS
                    PSNotSupportedException workflowModuleNotSupported =
                        PSTraceSource.NewNotSupportedException(
                            Modules.WorkflowModuleNotSupportedInPowerShellCore,
                            ModuleIntrinsics.GetModuleName(moduleManifestPath));
                    throw workflowModuleNotSupported;
#else
                    // Depending on current execution policy, check the signature of Module manifest file if required.
                    // Reusing already created ScriptInfo object to avoid race condition when modulemanifest was not signed during first check and signed before this step.
                    //
                    scriptInfo.ValidateScriptInfo(Host);

                    nestedModuleOptions.ServiceCoreAutoAdded = true;

                    if (!importingModule)
                    {
                        ProcessWorkflowsToProcess(moduleBase, workflowsToProcess, new List<string>(),
                            new List<string>(), null, manifestInfo, nestedModuleOptions);
                    }
                    else
                    {
                        // Never load nested modules to the global scope.
                        bool oldGLobal = this.BaseGlobal;
                        this.BaseGlobal = false;
                        bool found = false;

                        foreach (string workflowFileName in workflowsToProcess)
                        {
                            List<string> wfToProcess = new List<string>();
                            wfToProcess.Add(workflowFileName);
                            SessionState wfSS = new SessionState(Context, true, true);

                            PSModuleInfo module = new PSModuleInfo(
                                ModuleIntrinsics.GetModuleName(workflowFileName), workflowFileName, Context, wfSS);
                            wfSS.Internal.Module = module;
                            module.PrivateData = privateData;
                            module.SetModuleType(ModuleType.Workflow);
                            module.SetModuleBase(moduleBase);

                            LoadServiceCoreModule(module, String.Empty, wfSS, nestedModuleOptions,
                                manifestProcessingFlags, true, out found);

                            ProcessWorkflowsToProcess(moduleBase, wfToProcess, dependentWorkflows,
                                fixedUpAssemblyPathList, wfSS, module, nestedModuleOptions);

                            // And import the members from this module into the callers context...
                            if (importingModule)
                            {
                                ImportModuleMembers(module, this.BasePrefix, options);
                            }

                            // Add it to all the module tables
                            AddModuleToModuleTables(this.Context, this.TargetSessionState.Internal, module);
                            manifestInfo.AddNestedModule(module);
                        }

                        this.BaseGlobal = oldGLobal;
                    }
#endif
                }
            }
            catch (Exception)
            {
                // Remove the types and format files
                this.RemoveTypesAndFormatting(exportedFormatFiles, exportedTypeFiles);
                throw;
            }
            finally
            {
                // Restore the old session state object...
                Context.EngineSessionState = oldSessionState;

                // Restore the various cmdlet parameters to what the user passed in...
                BasePassThru = oldPassThru;
                BaseVariablePatterns = oldVariablePatterns;
                BaseFunctionPatterns = oldFunctionPatterns;
                BaseAliasPatterns = oldAliasPatterns;
                BaseCmdletPatterns = oldCmdletPatterns;
                BaseDisableNameChecking = oldBaseDisableNameChecking;
            }

            // Now see if the ModuleToProcess field was set. This radically
            // changes how the exports are done.
            if (actualRootModule != null)
            {
                PSModuleInfo newManifestInfo;

                // do not write out ModuleToProcess inside LoadModuleNamedInManifest
                // - wait until all attributes have been taken from psd1 into PSModuleInfo/manifestInfo
                // and then write out manifestInfo
                BasePassThru = false;

                // do not import anything from "ModuleToProcess" at this time
                // below we will call ImportModuleMembers(manifestInfo) after all nested modules have been processed
                BaseVariablePatterns = new List<WildcardPattern>();
                BaseFunctionPatterns = new List<WildcardPattern>();
                BaseAliasPatterns = new List<WildcardPattern>();
                BaseCmdletPatterns = new List<WildcardPattern>();

                try
                {
                    bool found;
                    newManifestInfo = LoadModuleNamedInManifest(null, new ModuleSpecification(actualRootModule),
                        moduleBase, /* searchModulePath */ false,
                        resolvedCommandPrefix, ss, options, manifestProcessingFlags,
                        // If types files already loaded, don't load snapin files
                        (exportedTypeFiles == null || 0 == exportedTypeFiles.Count),
                        // if format files already loaded, don't load snapin files
                        (exportedFormatFiles == null || 0 == exportedFormatFiles.Count),
                        privateData,
                        out found,
                        null);

                    if (!found || (newManifestInfo == null))
                    {
                        containedErrors = true;
                        string errorMessage = StringUtil.Format(Modules.ManifestMemberNotFound, actualRootModule,
                            "ModuleToProcess/RootModule", moduleManifestPath);
                        FileNotFoundException fnf = new FileNotFoundException(errorMessage);
                        PSInvalidOperationException invalidOperation = new PSInvalidOperationException(errorMessage, fnf,
                            "Modules_ModuleFileNotFound", ErrorCategory.ResourceUnavailable,
                            ModuleIntrinsics.GetModuleName(moduleManifestPath));
                        throw invalidOperation;
                    }

                    ErrorRecord errorRecord = GetErrorRecordIfUnsupportedRootCdxmlAndNestedModuleScenario(data,
                        moduleManifestPath, newManifestInfo.Path);
                    if (errorRecord != null)
                    {
                        containedErrors = true;
                        RemoveModule(newManifestInfo);
                        PSInvalidOperationException invalidOperation =
                            new PSInvalidOperationException(errorRecord.Exception.Message, errorRecord.Exception,
                                errorRecord.FullyQualifiedErrorId, ErrorCategory.InvalidOperation, moduleManifestPath);
                        throw invalidOperation;
                    }
                }
                catch (Exception)
                {
                    // Remove the types and format files
                    this.RemoveTypesAndFormatting(exportedFormatFiles, exportedTypeFiles);
                    throw;
                }
                finally
                {
                    // Restore the various cmdlet parameters to what the user passed in...
                    BasePassThru = oldPassThru;
                    BaseVariablePatterns = oldVariablePatterns;
                    BaseFunctionPatterns = oldFunctionPatterns;
                    BaseAliasPatterns = oldAliasPatterns;
                    BaseCmdletPatterns = oldCmdletPatterns;
                }

                // If there is an existing session state and the new module info
                // session state is empty, then use the existing session state.
                // This will be the case when ModuleToProcess is a binary module
                // and there were nested modules.
                if (newManifestInfo.SessionState == null && ss != null)
                {
                    newManifestInfo.SessionState = ss;
                    ss.Internal.Module = newManifestInfo;
                }
                // If the new module info session state is not empty but there is no
                // existing session state, then use the new module info's sessions state;
                // This will be the case if the module to process is a script module
                // but there where no nested modules.
                else if (newManifestInfo.SessionState != null && ss == null)
                {
                    ss = newManifestInfo.SessionState;
                }

                // We don't need to care if they are both null which will be the
                // case when there is only a binary module in ModuleToProcess and
                // no nested modules. At this point the moduleInfo and the value in
                // ss should be identical.
                Dbg.Assert(newManifestInfo.SessionState == ss,
                    "ss and newManifestInfo.SessionState should be the same after handling ModuleToProcess");

                // Change the module name to match the manifest name, not the original name
                newManifestInfo.SetName(manifestInfo.Name);

                // Copy in any nested modules...
                foreach (PSModuleInfo nm in manifestInfo.NestedModules)
                {
                    newManifestInfo.AddNestedModule(nm);
                }

                // Copy the required modules...
                foreach (PSModuleInfo rm in manifestInfo.RequiredModules)
                {
                    newManifestInfo.AddRequiredModule(rm);
                }

                // Copy in the version number - this over-write anything extracted
                // from the dll in the case of a binary module
                newManifestInfo.SetVersion(manifestInfo.Version);

                // Set the various bits of metadata from the manifest
                if (string.IsNullOrEmpty(newManifestInfo.Description))
                {
                    newManifestInfo.Description = description;
                }
                if (newManifestInfo.Version.Equals(new Version(0, 0)))
                {
                    newManifestInfo.SetVersion(moduleVersion);
                }
                if (newManifestInfo.Guid.Equals(Guid.Empty) && (manifestGuid != null))
                {
                    newManifestInfo.SetGuid((Guid)manifestGuid);
                }
                if (newManifestInfo.HelpInfoUri == null && (helpInfoUri != null))
                {
                    newManifestInfo.SetHelpInfoUri(helpInfoUri);
                }
                if (requiredModules != null)
                {
                    foreach (ModuleSpecification moduleSpecification in requiredModules)
                    {
                        newManifestInfo.AddRequiredModuleSpecification(moduleSpecification);
                    }
                }
                if (newManifestInfo.RootModule == null)
                {
                    newManifestInfo.RootModule = manifestInfo.RootModule;
                }
                // If may be the case that a script has already set the PrivateData field in the module
                // info object, in which case we won't overwrite it.
                if (newManifestInfo.PrivateData == null)
                {
                    newManifestInfo.PrivateData = manifestInfo.PrivateData;
                }

                // Assign the PowerShellGet related properties from the module manifest
                foreach (var tag in manifestInfo.Tags)
                {
                    newManifestInfo.AddToTags(tag);
                }
                newManifestInfo.ReleaseNotes = manifestInfo.ReleaseNotes;
                newManifestInfo.ProjectUri = manifestInfo.ProjectUri;
                newManifestInfo.LicenseUri = manifestInfo.LicenseUri;
                newManifestInfo.IconUri = manifestInfo.IconUri;
                newManifestInfo.RepositorySourceLocation = manifestInfo.RepositorySourceLocation;

                // If we are in module discovery, then fix the path.
                if (ss == null)
                {
                    newManifestInfo.Path = manifestInfo.Path;
                }

                if (string.IsNullOrEmpty(newManifestInfo.Author))
                {
                    newManifestInfo.Author = author;
                }

                if (string.IsNullOrEmpty(newManifestInfo.CompanyName))
                {
                    newManifestInfo.CompanyName = companyName;
                }

                if (string.IsNullOrEmpty(newManifestInfo.Copyright))
                {
                    newManifestInfo.Copyright = copyright;
                }

                if (newManifestInfo.PowerShellVersion == null)
                {
                    newManifestInfo.PowerShellVersion = powerShellVersion;
                }

                if (string.IsNullOrEmpty(newManifestInfo.PowerShellHostName))
                {
                    newManifestInfo.PowerShellHostName = requestedHostName;
                }

                if (newManifestInfo.PowerShellHostVersion == null)
                {
                    newManifestInfo.PowerShellHostVersion = requestedHostVersion;
                }

                if (newManifestInfo.DotNetFrameworkVersion == null)
                {
                    newManifestInfo.DotNetFrameworkVersion = requestedDotNetFrameworkVersion;
                }

                if (newManifestInfo.ClrVersion == null)
                {
                    newManifestInfo.ClrVersion = requestedClrVersion;
                }

                if (string.IsNullOrEmpty(newManifestInfo.Prefix))
                {
                    newManifestInfo.Prefix = resolvedCommandPrefix;
                }

                if (newManifestInfo.FileList == null || newManifestInfo.FileList.LongCount() == 0)
                {
                    if (fileList != null)
                    {
                        foreach (var f in fileList)
                        {
                            newManifestInfo.AddToFileList(f);
                        }
                    }
                }

                if (newManifestInfo.ModuleList == null || newManifestInfo.ModuleList.LongCount() == 0)
                {
                    if (moduleList != null)
                    {
                        foreach (var m in moduleList)
                        {
                            newManifestInfo.AddToModuleList(m);
                        }
                    }
                }

                if (newManifestInfo.CompatiblePSEditions == null || newManifestInfo.CompatiblePSEditions.LongCount() == 0)
                {
                    if (compatiblePSEditions != null)
                    {
                        foreach (var psEdition in compatiblePSEditions)
                        {
                            newManifestInfo.AddToCompatiblePSEditions(psEdition);
                        }
                    }
                }

                if (newManifestInfo.ProcessorArchitecture == ProcessorArchitecture.None)
                {
                    newManifestInfo.ProcessorArchitecture = requiredProcesorArchitecture;
                }

                if (newManifestInfo.RequiredAssemblies == null || newManifestInfo.RequiredAssemblies.LongCount() == 0)
                {
                    if (assemblyList != null)
                    {
                        foreach (var a in assemblyList)
                        {
                            newManifestInfo.AddRequiredAssembly(a);
                        }
                    }
                }

                if (newManifestInfo.Scripts == null || newManifestInfo.Scripts.LongCount() == 0)
                {
                    if (scriptsToProcess != null)
                    {
                        foreach (var s in scriptsToProcess)
                        {
                            newManifestInfo.AddScript(s);
                        }
                    }
                }

                if (newManifestInfo.RootModuleForManifest == null)
                {
                    newManifestInfo.RootModuleForManifest = manifestInfo.RootModuleForManifest;
                }

                if (newManifestInfo.DeclaredCmdletExports == null || newManifestInfo.DeclaredCmdletExports.Count == 0)
                {
                    newManifestInfo.DeclaredCmdletExports = manifestInfo.DeclaredCmdletExports;
                }

                if (manifestInfo._detectedCmdletExports != null)
                {
                    foreach (string detectedExport in manifestInfo._detectedCmdletExports)
                    {
                        newManifestInfo.AddDetectedCmdletExport(detectedExport);
                    }
                }

                if (newManifestInfo.DeclaredFunctionExports == null ||
                    newManifestInfo.DeclaredFunctionExports.Count == 0)
                {
                    newManifestInfo.DeclaredFunctionExports = manifestInfo.DeclaredFunctionExports;
                }

                if (manifestInfo._detectedFunctionExports != null)
                {
                    foreach (string detectedExport in manifestInfo._detectedFunctionExports)
                    {
                        newManifestInfo.AddDetectedFunctionExport(detectedExport);
                    }
                }

                if (newManifestInfo.DeclaredAliasExports == null || newManifestInfo.DeclaredAliasExports.Count == 0)
                {
                    newManifestInfo.DeclaredAliasExports = manifestInfo.DeclaredAliasExports;
                }

                if (manifestInfo._detectedAliasExports != null)
                {
                    foreach (var pair in manifestInfo._detectedAliasExports)
                    {
                        newManifestInfo.AddDetectedAliasExport(pair.Key, pair.Value);
                    }
                }


                if (newManifestInfo.DeclaredVariableExports == null ||
                    newManifestInfo.DeclaredVariableExports.Count == 0)
                {
                    newManifestInfo.DeclaredVariableExports = manifestInfo.DeclaredVariableExports;
                }

                if (manifestInfo._detectedWorkflowExports != null)
                {
                    foreach (string detectedExport in manifestInfo._detectedWorkflowExports)
                    {
                        newManifestInfo.AddDetectedWorkflowExport(detectedExport);
                    }
                }

                if (newManifestInfo.DeclaredWorkflowExports == null ||
                    newManifestInfo.DeclaredWorkflowExports.Count == 0)
                {
                    newManifestInfo.DeclaredWorkflowExports = manifestInfo.DeclaredWorkflowExports;
                }

                // If there are types/formats entries in the ModuleToProcess use them
                // only if there are no entries from the manifest. The manifest entries
                // completely override the module's entries.
                if (manifestInfo.ExportedTypeFiles.Count > 0)
                {
                    newManifestInfo.SetExportedTypeFiles(manifestInfo.ExportedTypeFiles);
                }
                if (manifestInfo.ExportedFormatFiles.Count > 0)
                {
                    newManifestInfo.SetExportedFormatFiles(manifestInfo.ExportedFormatFiles);
                }

                // And switch to using the new manifest info
                manifestInfo = newManifestInfo;

                // mark the binary module exports...
                if (manifestInfo.ModuleType == ModuleType.Binary)
                {
                    if ((exportedCmdlets != null) && (ss != null))
                    {
                        Dbg.Assert(ss.Internal.ExportedCmdlets != null, "ss.Internal.ExportedCmdlets should not be null");
                        manifestInfo.ExportedCmdlets.Clear();

                        // Mark stuff for export
                        if (ss != null)
                        {
                            ModuleIntrinsics.ExportModuleMembers(this,
                                ss.Internal,
                                exportedFunctions, exportedCmdlets,
                                exportedAliases, exportedVariables, null);
                        }
                    }
                }
                else
                {
                    // If the script didn't call Export-ModuleMember explicitly, then
                    // implicitly export functions and cmdlets.
                    if ((ss != null) && (!ss.Internal.UseExportList))
                    {
                        ModuleIntrinsics.ExportModuleMembers(this, ss.Internal, MatchAll,
                            MatchAll, null, null, options.ServiceCoreAutoAdded ? s_serviceCoreAssemblyCmdlets : null);
                    }

                    // Export* fields in .psd1 subset Export-ModuleMember calls from ModuleToProcess=psm1
                    if (exportedFunctions != null)
                    {
                        if (ss != null)
                        {
                            Dbg.Assert(ss.Internal.ExportedFunctions != null,
                                "ss.Internal.ExportedFunctions should not be null");

                            // Update the exports to only contain things that are also in the manifest export list
                            UpdateCommandCollection<FunctionInfo>(ss.Internal.ExportedFunctions, exportedFunctions);
                        }
                        else
                        {
                            Collection<string> v = new Collection<string>();
                            if (manifestInfo.DeclaredFunctionExports != null)
                            {
                                foreach (var f in manifestInfo.DeclaredFunctionExports)
                                {
                                    v.Add(f);
                                }
                            }
                            UpdateCommandCollection(v, exportedFunctions);
                        }
                    }

                    if (exportedCmdlets != null)
                    {
                        if (ss != null)
                        {
                            Dbg.Assert(ss.Internal.ExportedCmdlets != null,
                                "ss.Internal.ExportedCmdlets should not be null");

                            // Update the exports to only contain things that are also in the manifest export list
                            UpdateCommandCollection<CmdletInfo>(manifestInfo.CompiledExports, exportedCmdlets);
                        }
                        else
                        {
                            UpdateCommandCollection(manifestInfo.DeclaredCmdletExports, exportedCmdlets);
                        }
                    }

                    if (exportedAliases != null)
                    {
                        if (ss != null)
                        {
                            Dbg.Assert(ss.Internal.ExportedAliases != null,
                                "ss.Internal.ExportedAliases should not be null");

                            // Update the exports to only contain things that are also in the manifest export list
                            UpdateCommandCollection<AliasInfo>(ss.Internal.ExportedAliases, exportedAliases);
                        }
                        else
                        {
                            UpdateCommandCollection(manifestInfo.DeclaredAliasExports, exportedAliases);
                        }
                    }

                    if (exportedVariables != null)
                    {
                        if (ss != null)
                        {
                            Dbg.Assert(ss.Internal.ExportedVariables != null,
                                "ss.Internal.ExportedVariables should not be null");

                            // Update the exports to only contain things that are also in the manifest export list
                            List<PSVariable> updated = new List<PSVariable>();
                            foreach (PSVariable element in ss.Internal.ExportedVariables)
                            {
                                if (SessionStateUtilities.MatchesAnyWildcardPattern(element.Name, exportedVariables,
                                    false))
                                {
                                    updated.Add(element);
                                }
                            }
                            ss.Internal.ExportedVariables.Clear();
                            ss.Internal.ExportedVariables.AddRange(updated);
                        }
                    }
                }
            }
            else
            {
                if (ss != null)
                {
                    // In the case where there are only nested modules,
                    // the members of the manifest are canonical...
                    ModuleIntrinsics.ExportModuleMembers(this,
                        ss.Internal,
                        exportedFunctions, exportedCmdlets,
                        exportedAliases, exportedVariables,
                        options.ServiceCoreAutoAdded ? s_serviceCoreAssemblyCmdlets : null);
                }
            }

            PropagateExportedTypesFromNestedModulesToRootModuleScope(options, manifestInfo);
            SetDeclaredDscResources(exportedDscResources, manifestInfo);

            // And import the members from this module into the callers context...
            if (importingModule)
            {
                ImportModuleMembers(manifestInfo, resolvedCommandPrefix, options);
            }

            return manifestInfo;
        }

        private static void PropagateExportedTypesFromNestedModulesToRootModuleScope(ImportModuleOptions options, PSModuleInfo manifestInfo)
        {
            if (manifestInfo.NestedModules == null)
            {
                return;
            }

            if (manifestInfo.SessionState == null)
            {
                // i.e. Get-Module -ListAvailable, there is no state.
                return;
            }

            var scope = options.Local
                        ? manifestInfo.SessionState.Internal.CurrentScope
                        : manifestInfo.SessionState.Internal.ModuleScope;

            // The last one name wins! It's the same for command names in nested modules.
            // For rootModule C with Two nested modules (A, B) the order is: A, B, C
            // We re-adding rootModule types to the scope again to override any possible name collision.
            var modulesToProcess = manifestInfo.NestedModules.ToList();
            modulesToProcess.Add(manifestInfo);

            foreach (var nestedModule in modulesToProcess)
            {
                if (nestedModule != null)
                {
                    var exportedTypes = nestedModule.GetExportedTypeDefinitions();
                    if (exportedTypes != null && exportedTypes.Count != 0)
                    {
                        foreach (var t in exportedTypes)
                        {
                            scope.AddType(t.Key, t.Value.Type);
                        }
                    }
                }
            }
        }

        private static void SetDeclaredDscResources(List<WildcardPattern> exportedDscResources, PSModuleInfo manifestInfo)
        {
            if (exportedDscResources != null)
            {
                manifestInfo._declaredDscResourceExports = new Collection<string>();

                if (exportedDscResources.Count > 0)
                {
                    foreach (WildcardPattern p in exportedDscResources)
                    {
                        string name = p.Pattern;
                        if (!WildcardPattern.ContainsWildcardCharacters(name))
                        {
                            manifestInfo._declaredDscResourceExports.Add(name);
                        }
                    }
                }
            }

            // if it is empty it means there is a wildcard pattern
            if (manifestInfo._declaredDscResourceExports != null && manifestInfo._declaredDscResourceExports.Count == 0)
            {
                var exportedTypes = manifestInfo.GetExportedTypeDefinitions();
                var exportedClassDscResources = exportedTypes.Values.Where(typeAst =>
                {
                    for (int i = 0; i < typeAst.Attributes.Count; i++)
                    {
                        var a = typeAst.Attributes[i];
                        if (a.TypeName.GetReflectionAttributeType() == typeof(DscResourceAttribute))
                        {
                            return true;
                        }
                    }
                    return false;
                });
                foreach (var exportedResource in exportedClassDscResources)
                {
                    manifestInfo._declaredDscResourceExports.Add(exportedResource.Name);
                }
            }
        }

        private readonly string _serviceCoreAssemblyFullName = "Microsoft.Powershell.Workflow.ServiceCore, Version=3.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35, processorArchitecture=MSIL";
        private readonly string _serviceCoreAssemblyShortName = "Microsoft.Powershell.Workflow.ServiceCore";

        private PSModuleInfo LoadServiceCoreModule(PSModuleInfo parentModule, string moduleBase, SessionState ss, ImportModuleOptions nestedModuleOptions, ManifestProcessingFlags manifestProcessingFlags, bool addToParentModueIfFound, out bool found)
        {
            SessionStateInternal oldSessionState = Context.EngineSessionState;

            if (ss != null)
                Context.EngineSessionState = ss.Internal;

            try
            {
                found = false;
                // Never load nested modules to the global scope.
                bool oldGLobal = this.BaseGlobal;
                this.BaseGlobal = false;

                PSModuleInfo nestedModule = LoadBinaryModule(
                                                            parentModule,                // parentModule
                                                            false,                       // trySnapInName 
                                                            _serviceCoreAssemblyFullName, // moduleName
                                                            null,                        // fileName
                                                            null,                        // assemblyToLoad
                                                            moduleBase,                  // moduleBase
                                                            ss,                          // SessionState
                                                            nestedModuleOptions,         // ImportModuleOptions
                                                            manifestProcessingFlags,     // ManifestProcessingFlags
                                                            string.Empty,                // prefix: no -Prefix added for nested modules
                                                            true,                        // loadTypes
                                                            true,                        // loadFormats
                                                            out found,                   // found
                                                            _serviceCoreAssemblyShortName,// shortModuleName
                                                            true                         // disableFormatUpdates 
                                                            );
                this.BaseGlobal = oldGLobal;

                // If found, add it to the parent's list of NestedModules
                if (found)
                {
                    if (addToParentModueIfFound)
                    {
                        parentModule.AddNestedModule(nestedModule);
                    }

                    return nestedModule;
                }
                else
                {
                    string errorMessage = StringUtil.Format(Modules.ManifestMemberNotFound, _serviceCoreAssemblyFullName, "NestedModules", parentModule.Name);
                    FileNotFoundException fnf = new FileNotFoundException(errorMessage);
                    PSInvalidOperationException invalidOperation = new PSInvalidOperationException(errorMessage, fnf, "Modules_ModuleFileNotFound", ErrorCategory.ResourceUnavailable, parentModule.Name);
                    throw invalidOperation;
                }
            }
            finally
            {
                Context.EngineSessionState = oldSessionState;
            }
        }

#if !CORECLR // Workflow Not Supported On CSS
        private void ProcessWorkflowsToProcess(string moduleBase, List<string> workflowsToProcess, List<string> dependentWorkflows, List<string> assemblyList, SessionState ss, PSModuleInfo manifestInfo, ImportModuleOptions options)
        {
            // If we are actually processing
            if (ss != null)
            {
                if (workflowsToProcess != null && workflowsToProcess.Count > 0)
                {
                    // In ConstrainedLanguage, XAML workflows are not supported (even from a trusted FullLanguage state),
                    // unless they are signed in-box OS binaries.
                    if ((SystemPolicy.GetSystemLockdownPolicy() == SystemEnforcementMode.Enforce) ||
                        (Context.LanguageMode == PSLanguageMode.ConstrainedLanguage))
                    {
                        // However, this static internal property can be changed by tests that can already run a script
                        // in full-language mode.
                        if (!SystemPolicy.XamlWorkflowSupported)
                        {
                            foreach (string workflowPath in ResolveWorkflowFiles(moduleBase, workflowsToProcess))
                            {
                                if (!SecuritySupport.IsProductBinary(workflowPath))
                                {
                                    throw new NotSupportedException(Modules.XamlWorkflowsNotSupported);
                                }
                            }
                        }
                    }

                    SessionStateInternal oldSessionStateWF = Context.EngineSessionState;
                    PSLanguageMode? savedLanguageMode = null;
                    try
                    {
                        Context.EngineSessionState = ss.Internal;

                        // Always run workflow import script as trusted since only signed in-box files can be imported
                        // on locked down machines.
                        if (Context.LanguageMode != PSLanguageMode.FullLanguage)
                        {
                            savedLanguageMode = Context.LanguageMode;
                            Context.LanguageMode = PSLanguageMode.FullLanguage;
                        }

                        if (dependentWorkflows != null && dependentWorkflows.Count > 0)
                        {
                            ScriptBlock importWorkflow = ScriptBlock.Create(Context,
                                    "param($files, $dependentFiles, $assemblyList) Microsoft.PowerShell.Workflow.ServiceCore\\Import-PSWorkflow -Path \"$files\" -DependentWorkflow $dependentFiles -DependentAssemblies $assemblyList -Force:$" + BaseForce
                                );

                            List<string> dependentFiles = new List<string>(ResolveDependentWorkflowFiles(moduleBase, dependentWorkflows));

                            foreach (string workflowPath in ResolveWorkflowFiles(moduleBase, workflowsToProcess))
                            {
                                WriteVerbose(StringUtil.Format(Modules.LoadingWorkflow, workflowPath));
                                importWorkflow.Invoke(workflowPath, dependentFiles.ToArray(), assemblyList.ToArray());
                            }
                        }
                        else
                        {
                            ScriptBlock importWorkflow = ScriptBlock.Create(Context,
                                    "param($files, $dependentFiles) Microsoft.PowerShell.Workflow.ServiceCore\\Import-PSWorkflow -Path \"$files\" -Force:$" + BaseForce
                                );

                            foreach (string workflowPath in ResolveWorkflowFiles(moduleBase, workflowsToProcess))
                            {
                                WriteVerbose(StringUtil.Format(Modules.LoadingWorkflow, workflowPath));
                                importWorkflow.Invoke(workflowPath);
                            }
                        }

                        // In the case where there are only nested modules,
                        // the members of the manifest are canonical...
                        ModuleIntrinsics.ExportModuleMembers(this, ss.Internal,
                            MatchAll, MatchAll, MatchAll, MatchAll,
                            options.ServiceCoreAutoAdded ? s_serviceCoreAssemblyCmdlets : null);
                    }
                    finally
                    {
                        Context.EngineSessionState = oldSessionStateWF;

                        if (savedLanguageMode != null)
                        {
                            Context.LanguageMode = savedLanguageMode.Value;
                        }
                    }
                }
            }
            else
            {
                if (workflowsToProcess != null)
                {
                    // We are in analysis
                    foreach (string workflowToProcess in workflowsToProcess)
                    {
                        manifestInfo.AddDetectedWorkflowExport(System.IO.Path.GetFileNameWithoutExtension(workflowToProcess));
                    }
                }
            }
        }

        private ICollection<string> ResolveWorkflowFiles(string moduleBase, List<string> workflowsToProcess)
        {
            Dictionary<string, string> resolvedWorkflowsToProcess = new Dictionary<string, string>();
            foreach (string workflowToProcess in workflowsToProcess)
            {
                if (string.Equals(System.IO.Path.GetExtension(workflowToProcess),
                    StringLiterals.WorkflowFileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    string wf = workflowToProcess;
                    if (!Path.IsPathRooted(wf))
                    {
                        wf = Path.Combine(moduleBase, wf);
                    }

                    foreach (string resolvedWorkflow in Context.SessionState.Path.GetResolvedProviderPathFromProviderPath(wf, Context.ProviderNames.FileSystem))
                    {
                        resolvedWorkflowsToProcess[resolvedWorkflow] = resolvedWorkflow;
                    }
                }
                else
                {
                    PSInvalidOperationException invalidOperation = PSTraceSource.NewInvalidOperationException(
                                Modules.InvalidWorkflowExtensionDuringManifestProcessing,
                                workflowToProcess);
                    invalidOperation.SetErrorId("Modules_InvalidWorkflowExtensionDuringManifestProcessing");
                    throw invalidOperation;
                }
            }
            return resolvedWorkflowsToProcess.Values;
        }

        private ICollection<string> ResolveDependentWorkflowFiles(string moduleBase, List<string> dependentWorkflowsToProcess)
        {
            Dictionary<string, string> resolvedWorkflowsToProcess = new Dictionary<string, string>();

            if (dependentWorkflowsToProcess.Count == 1 && string.Equals(System.IO.Path.GetExtension(dependentWorkflowsToProcess[0]), StringLiterals.DependentWorkflowAssemblyExtension, StringComparison.OrdinalIgnoreCase))
            {
                string wf = dependentWorkflowsToProcess[0];
                if (!Path.IsPathRooted(wf))
                {
                    wf = Path.Combine(moduleBase, wf);
                }

                resolvedWorkflowsToProcess[wf] = wf;
            }
            else
            {
                foreach (string workflowToProcess in dependentWorkflowsToProcess)
                {
                    if (string.Equals(System.IO.Path.GetExtension(workflowToProcess),
                        StringLiterals.WorkflowFileExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        string wf = workflowToProcess;
                        if (!Path.IsPathRooted(wf))
                        {
                            wf = Path.Combine(moduleBase, wf);
                        }

                        foreach (string resolvedWorkflow in Context.SessionState.Path.GetResolvedProviderPathFromProviderPath(wf, Context.ProviderNames.FileSystem))
                        {
                            resolvedWorkflowsToProcess[resolvedWorkflow] = resolvedWorkflow;
                        }
                    }
                    else
                    {
                        PSInvalidOperationException invalidOperation = PSTraceSource.NewInvalidOperationException(
                                Modules.InvalidWorkflowExtensionDuringManifestProcessing,
                                workflowToProcess);
                        invalidOperation.SetErrorId("Modules_InvalidWorkflowExtensionDuringManifestProcessing");
                        throw invalidOperation;
                    }
                }
            }

            return resolvedWorkflowsToProcess.Values;
        }
#endif

        private static void UpdateCommandCollection<T>(List<T> list, List<WildcardPattern> patterns) where T : CommandInfo
        {
            List<T> updated = new List<T>();
            foreach (T element in list)
            {
                if (SessionStateUtilities.MatchesAnyWildcardPattern(element.Name, patterns, false))
                {
                    updated.Add(element);
                }
            }
            list.Clear();
            list.AddRange(updated);
        }

        private static void UpdateCommandCollection(Collection<string> list, List<WildcardPattern> patterns)
        {
            if (list == null)
            {
                return;
            }

            List<string> updated = new List<string>();

            // Add any patterns that don't have wildcard characters - they
            // may have been declared but then created in a way that we couldn't detect
            // them statically
            foreach (WildcardPattern pattern in patterns)
            {
                if (!WildcardPattern.ContainsWildcardCharacters(pattern.Pattern))
                {
                    if (!list.Contains(pattern.Pattern, StringComparer.OrdinalIgnoreCase))
                    {
                        list.Add(pattern.Pattern);
                    }
                }
            }

            foreach (string element in list)
            {
                if (SessionStateUtilities.MatchesAnyWildcardPattern(element, patterns, false))
                {
                    updated.Add(element);
                }
            }
            list.Clear();

            foreach (string element in updated)
            {
                list.Add(element);
            }
        }

        private static void WriteInvalidManifestMemberError(
            PSCmdlet cmdlet,
            string manifestElement,
            string moduleManifestPath,
            Exception e,
            ManifestProcessingFlags manifestProcessingFlags)
        {
            CommandProcessorBase.CheckForSevereException(e);
            if (0 != (manifestProcessingFlags & ManifestProcessingFlags.WriteErrors))
            {
                ErrorRecord er = GenerateInvalidModuleMemberErrorRecord(manifestElement, moduleManifestPath, e);
                cmdlet.WriteError(er);
            }
        }

        private static ErrorRecord GenerateInvalidModuleMemberErrorRecord(string manifestElement, string moduleManifestPath, Exception e)
        {
            string message = StringUtil.Format(Modules.ModuleManifestInvalidManifestMember, manifestElement, e.Message, moduleManifestPath);
            ArgumentException newAe = new ArgumentException(message);
            ErrorRecord er = new ErrorRecord(newAe, "Modules_InvalidManifest",
                ErrorCategory.ResourceUnavailable, moduleManifestPath);
            return er;
        }

        /// <summary>
        /// Check if a module has been loaded.
        /// If loadElements is false, we check requireModule for correctness but do
        /// not check if the modules are loaded.
        /// </summary>
        /// <param name="context">Execution Context.</param>
        /// <param name="requiredModule">Either a string or a hash of ModuleName, optional Guid, and ModuleVersion.</param>
        /// <param name="wrongVersion">Sets if the module cannot be found due to incorrect version.</param>
        /// <param name="wrongGuid">Sets if the module cannot be found due to incorrect guid.</param>
        /// <param name="loaded">Sets if the module/snapin is already present.</param>
        /// <returns>null if the module is not loaded or loadElements is false, the loaded module otherwise.</returns>
        internal static object IsModuleLoaded(ExecutionContext context, ModuleSpecification requiredModule, out bool wrongVersion, out bool wrongGuid, out bool loaded)
        {
            loaded = false;
            Dbg.Assert(requiredModule != null, "Caller should verify requiredModuleSpecification != null");

            // Assume the module is not loaded.
            object result = null;
            wrongVersion = false;
            wrongGuid = false;

            string moduleName = requiredModule.Name;
            Guid? moduleGuid = requiredModule.Guid;

            Dbg.Assert(moduleName != null, "GetModuleSpecification should guarantee that moduleName != null");
            foreach (PSModuleInfo module in context.Modules.GetModules(new string[] { "*" }, false))
            {
                if (moduleName.Equals(module.Name, StringComparison.OrdinalIgnoreCase))
                {
                    if (!moduleGuid.HasValue || moduleGuid.Value.Equals(module.Guid))
                    {
                        if (requiredModule.RequiredVersion != null)
                        {
                            if (requiredModule.RequiredVersion.Equals(module.Version))
                            {
                                result = module;
                                loaded = true;
                                break;
                            }

                            wrongVersion = true;
                        }
                        else if (requiredModule.Version != null)
                        {
                            if (requiredModule.MaximumVersion != null)
                            {
                                if (GetMaximumVersion(requiredModule.MaximumVersion) >= module.Version && requiredModule.Version <= module.Version)
                                {
                                    result = module;
                                    loaded = true;
                                    break;
                                }
                                else
                                {
                                    wrongVersion = true;
                                }
                            }
                            else if (requiredModule.Version <= module.Version)
                            {
                                result = module;
                                loaded = true;
                                break;
                            }
                            else
                            {
                                wrongVersion = true;
                            }
                        }
                        else if (requiredModule.MaximumVersion != null)
                        {
                            if (GetMaximumVersion(requiredModule.MaximumVersion) >= module.Version)
                            {
                                result = module;
                                loaded = true;
                                break;
                            }
                            else
                            {
                                wrongVersion = true;
                            }
                        }
                        else
                        {
                            result = module;
                            loaded = true;
                            break;
                        }
                    }
                    else
                    {
                        wrongGuid = true;
                    }
                }
            }

            // If the RequiredModule is one of the Engine modules, then they could have been loaded as snapins (using RunspaceConfiguration or InitialSessionState.CreateDefault())
            if (result == null && InitialSessionState.IsEngineModule(requiredModule.Name))
            {
                result = ModuleCmdletBase.GetEngineSnapIn(context, requiredModule.Name);
                if (result != null)
                {
                    loaded = true;
                }
            }

            return result;
        }

        /// <summary>
        /// Check if a module has been loaded.
        /// If loadElements is false, we check requireModule for correctness but do
        /// not check if the modules are loaded.
        /// </summary>
        /// <param name="currentModule">The current module being loaded.</param>
        /// <param name="requiredModule">Either a string or a hash of ModuleName, optional Guid, and ModuleVersion.</param>
        /// <param name="moduleManifestPath">Used for error messages.</param>
        /// <param name="manifestProcessingFlags">Specifies how to treat errors and whether to load elements</param>
        /// <param name="containedErrors">Set if any errors are found.</param>
        /// <param name="error">Contains error record information.</param>
        /// <returns>null if the module is not loaded or loadElements is false, the loaded module otherwise.</returns>
        internal PSModuleInfo LoadRequiredModule(PSModuleInfo currentModule, ModuleSpecification requiredModule, string moduleManifestPath, ManifestProcessingFlags manifestProcessingFlags, bool containedErrors, out ErrorRecord error)
        {
            Dbg.Assert(moduleManifestPath != null, "Caller should verify moduleManifestPath != null");
            error = null;
            if (!containedErrors)
            {
                return LoadRequiredModule(Context, currentModule, requiredModule, moduleManifestPath, manifestProcessingFlags, out error);
            }
            return null;
        }

        /// <summary>
        /// Check if a module has been loaded.
        /// If loadElements is false, we check requireModule for correctness but do
        /// not check if the modules are loaded.
        /// </summary>
        /// <param name="context">Execution Context.</param>
        /// <param name="currentModule">The current module being loaded.</param>
        /// <param name="requiredModuleSpecification">Either a string or a hash of ModuleName, optional Guid, and ModuleVersion.</param>
        /// <param name="moduleManifestPath">Used for error messages.</param>
        /// <param name="manifestProcessingFlags">Specifies how to treat errors and whether to load elements</param>
        /// <param name="error">Contains error record information.</param>
        /// <returns>null if the module is not loaded or loadElements is false, the loaded module otherwise.</returns>
        internal static PSModuleInfo LoadRequiredModule(ExecutionContext context,
            PSModuleInfo currentModule,
            ModuleSpecification requiredModuleSpecification,
            string moduleManifestPath,
            ManifestProcessingFlags manifestProcessingFlags,
            out ErrorRecord error)
        {
            Dbg.Assert(0 != (manifestProcessingFlags & ManifestProcessingFlags.LoadElements), "LoadRequiredModule / RequiredModules checks should only be done when actually loading a module");

            error = null;

            string moduleName = requiredModuleSpecification.Name;
            Guid? moduleGuid = requiredModuleSpecification.Guid;
            PSModuleInfo result = null;

            bool wrongVersion = false;
            bool wrongGuid = false;
            bool loaded = false;
            object loadedModule = IsModuleLoaded(context, requiredModuleSpecification, out wrongVersion, out wrongGuid, out loaded);

            if (loadedModule == null)
            {
                // Load the module
                PSModuleInfo requiredModuleInfo = null;
                Collection<PSModuleInfo> moduleInfo = GetModuleIfAvailable(requiredModuleSpecification);

                if (moduleInfo != null && moduleInfo.Count > 0)
                {
                    requiredModuleInfo = moduleInfo[0];

                    // Check for cyclic references of RequiredModule 
                    bool hasRequiredModulesCyclicReference = false;
                    Dictionary<ModuleSpecification, List<ModuleSpecification>> requiredModules = new Dictionary<ModuleSpecification, List<ModuleSpecification>>(new ModuleSpecificationComparer());
                    if (currentModule != null)
                    {
                        requiredModules.Add(new ModuleSpecification(currentModule), new List<ModuleSpecification> { requiredModuleSpecification });
                    }

                    // We always need to check against the module name and not the file name
                    hasRequiredModulesCyclicReference = HasRequiredModulesCyclicReference(requiredModuleInfo.Name,
                                                                                          new List<ModuleSpecification>(requiredModuleInfo.RequiredModulesSpecification),
                                                                                          new Collection<PSModuleInfo> { requiredModuleInfo },
                                                                                          requiredModules,
                                                                                          out error);
                    if (!hasRequiredModulesCyclicReference)
                    {
                        result = ImportRequiredModule(context, requiredModuleSpecification, out error);
                    }
                    else
                    {
                        // Error Record
                        if (moduleManifestPath != null)
                        {
                            Dbg.Assert(error != null, "Error message should be populated if there is cyclic dependency");
                            MissingMemberException mm = null;
                            if (error != null && error.Exception != null)
                            {
                                mm = new MissingMemberException(error.Exception.Message);
                            }
                            error = new ErrorRecord(mm, "Modules_InvalidManifest",
                                                    ErrorCategory.ResourceUnavailable, moduleManifestPath);
                        }
                    }
                }
                else
                {
                    string message;
                    if (moduleManifestPath != null)
                    {
                        if (0 != (manifestProcessingFlags & ManifestProcessingFlags.WriteErrors))
                        {
                            if (wrongVersion)
                            {
                                if (requiredModuleSpecification.RequiredVersion != null)
                                {
                                    message = StringUtil.Format(Modules.RequiredModuleNotLoadedWrongVersion, moduleManifestPath, moduleName, requiredModuleSpecification.RequiredVersion);
                                }
                                else if (requiredModuleSpecification.Version != null && requiredModuleSpecification.MaximumVersion == null)
                                {
                                    message = StringUtil.Format(Modules.RequiredModuleNotLoadedWrongVersion, moduleManifestPath, moduleName, requiredModuleSpecification.Version);
                                }
                                else if (requiredModuleSpecification.Version == null && requiredModuleSpecification.MaximumVersion != null)
                                {
                                    message = StringUtil.Format(Modules.RequiredModuleNotLoadedWrongMaximumVersion, moduleManifestPath, moduleName, requiredModuleSpecification.MaximumVersion);
                                }
                                else
                                {
                                    message = StringUtil.Format(Modules.RequiredModuleNotLoadedWrongMinimumVersionAndMaximumVersion, moduleManifestPath, moduleName, requiredModuleSpecification.Version, requiredModuleSpecification.MaximumVersion);
                                }
                            }
                            else if (wrongGuid)
                            {
                                message = StringUtil.Format(Modules.RequiredModuleNotLoadedWrongGuid, moduleManifestPath, moduleName, moduleGuid.Value);
                            }
                            else
                            {
                                message = StringUtil.Format(Modules.RequiredModuleNotLoaded, moduleManifestPath, moduleName);
                            }
                            MissingMemberException mm = new MissingMemberException(message);
                            error = new ErrorRecord(mm, "Modules_InvalidManifest",
                                                    ErrorCategory.ResourceUnavailable, moduleManifestPath);
                        }
                    }
                    else
                    {
                        message = StringUtil.Format(Modules.RequiredModuleNotFound, requiredModuleSpecification.Name);
                        MissingMemberException mm = new MissingMemberException(message);
                        error = new ErrorRecord(mm, "Modules_RequiredModuleNotFound", ErrorCategory.ResourceUnavailable, null);
                    }
                }
            }
            else
            {
                // Either module/snapin is already loaded
                if (loadedModule is PSModuleInfo)
                {
                    result = (PSModuleInfo)loadedModule;
                }
                else if (!loaded)
                {
                    // This should never happen because one of the two should be true
                    // 1) loadedModule should either be PSModuleInfo 
                    // 2) loaded = true
                    Dbg.Assert(false, "loadedModule should either be PSModuleInfo or loaded should be true");
                }
            }

            return result;
        }

        private static PSModuleInfo ImportRequiredModule(ExecutionContext context, ModuleSpecification requiredModule, out ErrorRecord error)
        {
            error = null;
            PSModuleInfo result = null;
            using (var powerShell = System.Management.Automation.PowerShell.Create(RunspaceMode.CurrentRunspace))
            {
                powerShell.AddCommand("Import-Module");
                powerShell.AddParameter("Name", requiredModule.Name);

                if (requiredModule.RequiredVersion != null)
                {
                    powerShell.AddParameter("RequiredVersion", requiredModule.RequiredVersion);
                }
                else if (requiredModule.MaximumVersion != null)
                {
                    powerShell.AddParameter("MaximumVersion", requiredModule.MaximumVersion);
                }
                else
                {
                    powerShell.AddParameter("Version", requiredModule.Version);
                }


                powerShell.Invoke();
                if (powerShell.Streams.Error != null && powerShell.Streams.Error.Count > 0)
                {
                    error = powerShell.Streams.Error[0];
                }
                else
                {
                    bool wrongVersion = false;
                    bool wrongGuid = false;
                    // Check if the correct module is loaded using Version , Guid Information.
                    string moduleNameToCheckAgainst = requiredModule.Name;
                    string manifestPath = string.Empty;
                    if (moduleNameToCheckAgainst.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase))
                    {
                        manifestPath = moduleNameToCheckAgainst;
                        moduleNameToCheckAgainst = Path.GetFileNameWithoutExtension(moduleNameToCheckAgainst);
                    }
                    ModuleSpecification ms = new ModuleSpecification(moduleNameToCheckAgainst);
                    if (requiredModule.Guid != null)
                    {
                        ms.Guid = requiredModule.Guid.Value;
                    }
                    if (requiredModule.RequiredVersion != null)
                    {
                        ms.RequiredVersion = requiredModule.RequiredVersion;
                    }
                    if (requiredModule.Version != null)
                    {
                        ms.Version = requiredModule.Version;
                    }
                    if (requiredModule.MaximumVersion != null)
                    {
                        ms.MaximumVersion = requiredModule.MaximumVersion;
                    }
                    bool loaded = false;
                    object r = IsModuleLoaded(context, ms, out wrongVersion, out wrongGuid, out loaded);
                    Dbg.Assert(r is PSModuleInfo, "The returned value should be PSModuleInfo");
                    result = r as PSModuleInfo;
                    if (result == null)
                    {
                        string message = StringUtil.Format(Modules.RequiredModuleNotFound, moduleNameToCheckAgainst);
                        if (!string.IsNullOrEmpty(manifestPath))
                        {
                            MissingMemberException mm = new MissingMemberException(message);
                            error = new ErrorRecord(mm, "Modules_InvalidManifest",
                                                    ErrorCategory.ResourceUnavailable, manifestPath);
                        }
                        else
                        {
                            InvalidOperationException ioe = new InvalidOperationException(message);
                            error = new ErrorRecord(ioe, "Modules_RequiredModuleNotLoadedWithoutManifest", ErrorCategory.InvalidOperation, requiredModule);
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Verifies if a nested module is available using GetModuleIfAvailable.
        /// <paramref name="extension"/> and <paramref name="rootedModulePath"/>
        /// will be used to create module name for searching. 
        /// If <paramref name="rootedModulePath"/> is null, the name specified in
        /// nestedModuleSpec will be used.
        /// </summary>
        /// <param name="nestedModuleSpec"></param>
        /// <param name="rootedModulePath"></param>
        /// <param name="extension"></param>
        /// <param name="nestedModuleInfoIfAvailable"></param>
        /// <returns></returns>
        internal bool VerifyIfNestedModuleIsAvailable(ModuleSpecification nestedModuleSpec,
            string rootedModulePath,
            string extension,
            out PSModuleInfo nestedModuleInfoIfAvailable)
        {
            Dbg.Assert(null != nestedModuleSpec, "nestedModuleSpec cannot be null.");
            nestedModuleInfoIfAvailable = null;
            if ((nestedModuleSpec.Guid != null) || (nestedModuleSpec.Version != null) || (nestedModuleSpec.RequiredVersion != null) || (nestedModuleSpec.MaximumVersion != null))
            {
                if (!string.IsNullOrEmpty(extension) &&
                    (!string.Equals(extension, StringLiterals.PowerShellDataFileExtension)))
                {
                    // A module can declare its GUID/Version only in the PSD1 file.
                    return false;
                }

                string tempModuleName = rootedModulePath;
                if (string.IsNullOrEmpty(extension))
                {
                    tempModuleName = rootedModulePath + StringLiterals.PowerShellDataFileExtension;
                }
                ModuleSpecification tempSpec = new ModuleSpecification(
                    string.IsNullOrEmpty(rootedModulePath) ? nestedModuleSpec.Name : tempModuleName);
                if (nestedModuleSpec.Guid.HasValue)
                {
                    tempSpec.Guid = nestedModuleSpec.Guid.Value;
                }

                if (nestedModuleSpec.Version != null)
                {
                    tempSpec.Version = nestedModuleSpec.Version;
                }

                if (nestedModuleSpec.RequiredVersion != null)
                {
                    tempSpec.RequiredVersion = nestedModuleSpec.RequiredVersion;
                }

                if (nestedModuleSpec.MaximumVersion != null)
                {
                    tempSpec.MaximumVersion = nestedModuleSpec.MaximumVersion;
                }

                Collection<PSModuleInfo> availableModules = GetModuleIfAvailable(tempSpec);

                // With Side-b-Side module version support, more than one module will be returned by GetModuleIfAvailable
                if (availableModules.Count < 1)
                {
                    return false;
                }

                // First element is the highest available version under a module base folder
                nestedModuleInfoIfAvailable = availableModules[0];
            }

            return true;
        }

        // Checks if module is available to be loaded
        // ModuleName ---> checks if module can be loaded using Module loading rules
        // ModuleManifest --> checks if manifest is valid
        internal static Collection<PSModuleInfo> GetModuleIfAvailable(ModuleSpecification requiredModule,
            Runspace rsToUse = null)
        {
            Collection<PSModuleInfo> result = new Collection<PSModuleInfo>();
            Collection<PSModuleInfo> tempResult = null;
            System.Management.Automation.PowerShell powerShell = null;
            if (rsToUse == null)
            {
                powerShell = System.Management.Automation.PowerShell.Create(RunspaceMode.CurrentRunspace);
            }
            else
            {
                powerShell = System.Management.Automation.PowerShell.Create();
                powerShell.Runspace = rsToUse;
            }

            using (powerShell)
            {
                if (requiredModule.Name.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase))
                {
                    powerShell.AddCommand("Test-ModuleManifest");
                    powerShell.AddParameter("Path", requiredModule.Name);
                }
                else
                {
                    powerShell.AddCommand("Get-Module");
                    powerShell.AddParameter("Name", requiredModule.Name);
                    powerShell.AddParameter("ListAvailable");
                }

                tempResult = powerShell.Invoke<PSModuleInfo>();
            }
            // Check if the available module is of the correct version and GUID
            foreach (var m in tempResult)
            {
                if (!requiredModule.Guid.HasValue || requiredModule.Guid.Value.Equals(m.Guid))
                {
                    if (requiredModule.RequiredVersion != null)
                    {
                        if (requiredModule.RequiredVersion.Equals(m.Version))
                        {
                            result.Add(m);
                        }
                    }
                    else if (requiredModule.Version != null)
                    {
                        if (requiredModule.MaximumVersion != null)
                        {
                            if (GetMaximumVersion(requiredModule.MaximumVersion) >= m.Version && requiredModule.Version <= m.Version)
                            {
                                result.Add(m);
                            }
                        }
                        else if (requiredModule.Version <= m.Version)
                        {
                            result.Add(m);
                        }
                    }
                    else if (requiredModule.MaximumVersion != null)
                    {
                        if (GetMaximumVersion(requiredModule.MaximumVersion) >= m.Version)
                        {
                            result.Add(m);
                        }
                    }
                    else
                    {
                        result.Add(m);
                    }
                }
            }
            return result;
        }

        private static bool HasRequiredModulesCyclicReference(string currentModuleName, List<ModuleSpecification> requiredModules, IEnumerable<PSModuleInfo> moduleInfoList, Dictionary<ModuleSpecification, List<ModuleSpecification>> nonCyclicRequiredModules, out ErrorRecord error)
        {
            error = null;
            if (requiredModules == null || requiredModules.Count == 0)
            {
                return false;
            }

            foreach (var moduleSpecification in requiredModules)
            {
                // The dictionary holds the key-value pair with the following convention
                // Key --> Module
                // Values --> RequiredModules
                // If we find that the module we are trying to import is already present in the dictionary, there is a cyclic reference

                // No cycle
                // 1---->2------>3-------->4
                //       |--> 5             |--> 5

                // Cycle
                // 1 --->2---->3---->4---> 2
                if (nonCyclicRequiredModules.ContainsKey(moduleSpecification))
                {
                    // Error out saying there is a cyclic reference
                    PSModuleInfo mo = null;
                    foreach (var i in moduleInfoList)
                    {
                        if (i.Name.Equals(currentModuleName, StringComparison.OrdinalIgnoreCase))
                        {
                            mo = i;
                            break;
                        }
                    }
                    Dbg.Assert(mo != null, "The moduleInfo should be present");
                    string message = StringUtil.Format(Modules.RequiredModulesCyclicDependency, currentModuleName, moduleSpecification.Name, mo.Path);
                    MissingMemberException mm = new MissingMemberException(message);
                    error = new ErrorRecord(mm, "Modules_InvalidManifest", ErrorCategory.ResourceUnavailable, mo.Path);
                    return true;
                }
                else // Go for the recursive check for the RequiredModules of current module
                {
                    // Add required Modules of m to the list
                    Collection<PSModuleInfo> availableModules = GetModuleIfAvailable(moduleSpecification);
                    List<ModuleSpecification> list = new List<ModuleSpecification>();
                    string moduleName = null;
                    if (availableModules.Count == 1)
                    {
                        moduleName = availableModules[0].Name;
                        foreach (var rm in availableModules[0].RequiredModulesSpecification)
                        {
                            list.Add(rm);
                        }
                        // Only add if this element has a required module (meaning, it could lead to a circular reference)
                        if (list.Count > 0)
                        {
                            nonCyclicRequiredModules.Add(moduleSpecification, list);
                        }
                    }

                    // We always need to check against the module name and not the file name
                    if (HasRequiredModulesCyclicReference(moduleName, list, availableModules, nonCyclicRequiredModules, out error))
                    {
                        return true;
                    }
                }
            }

            // Once depth recursive returns a value, we should remove the current module from the CyclicRequiredModules check list.
            // This prevent non related modules get involved in the cycle list.
            ModuleSpecification currentModule = new ModuleSpecification(currentModuleName);
            nonCyclicRequiredModules.Remove(currentModule);
            return false;
        }

        /// <summary>
        /// Search for a localized psd1 manifest file, using the same algorithm
        /// as Import-LocalizedData.
        /// </summary>
        private ExternalScriptInfo FindLocalizedModuleManifest(string path)
        {
            string dir = Path.GetDirectoryName(path);
            string file = Path.GetFileName(path);
            string localizedFile = null;

            CultureInfo culture = System.Globalization.CultureInfo.CurrentUICulture;
            CultureInfo currentCulture = culture;
            while (currentCulture != null && !String.IsNullOrEmpty(currentCulture.Name))
            {
                StringBuilder stringBuilder = new StringBuilder(dir);
                stringBuilder.Append("\\");
                stringBuilder.Append(currentCulture.Name);
                stringBuilder.Append("\\");
                stringBuilder.Append(file);

                String filePath = stringBuilder.ToString();

                if (Utils.NativeFileExists(filePath))
                {
                    localizedFile = filePath;
                    break;
                }

                currentCulture = currentCulture.Parent;
            }

            ExternalScriptInfo result = null;
            if (localizedFile != null)
            {
                result = new ExternalScriptInfo(Path.GetFileName(localizedFile), localizedFile);
            }

            return result;
        }

        /// <summary>
        /// Checks to see if the module manifest contains the specified key. 
        /// If it does and it's valid, it returns true otherwise it returns false.
        /// If the key wasn't there or wasn't valid, then <paramref name="list"/> is set to <c>null</c>
        /// </summary>
        /// <param name="data">The hashtable to look for the key in</param>
        /// <param name="moduleManifestPath">The manifest that generated the hashtable</param>
        /// <param name="key">the table key to use</param>
        /// <param name="manifestProcessingFlags">Specifies how to treat errors and whether to load elements</param>
        /// <param name="list">Returns the extracted version</param>
        /// <returns></returns>
        internal bool GetListOfStringsFromData(
            Hashtable data,
            string moduleManifestPath,
            string key,
            ManifestProcessingFlags manifestProcessingFlags,
            out List<string> list)
        {
            list = null;
            if (data.Contains(key))
            {
                if (data[key] != null)
                {
                    try
                    {
                        string[] stringData = (string[])LanguagePrimitives.ConvertTo(data[key], typeof(string[]), CultureInfo.InvariantCulture);
                        list = new List<string>(stringData);
                    }
                    catch (Exception e)
                    {
                        CommandProcessorBase.CheckForSevereException(e);

                        WriteInvalidManifestMemberError(this, key, moduleManifestPath, e, manifestProcessingFlags);
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Checks to see if the module manifest contains the specified key. 
        /// If it does and it's valid, it returns true otherwise it returns false.
        /// If the key wasn't there or wasn't valid, then <paramref name="list"/> is set to <c>null</c>.
        /// </summary>
        /// <param name="data">The hashtable to look for the key in</param>
        /// <param name="moduleManifestPath">The manifest that generated the hashtable</param>
        /// <param name="key">the table key to use</param>
        /// <param name="manifestProcessingFlags">Specifies how to treat errors and whether to load elements</param>
        /// <param name="list">Returns the extracted version</param>
        /// <returns></returns>
        private bool GetListOfWildcardsFromData(
            Hashtable data,
            string moduleManifestPath,
            string key,
            ManifestProcessingFlags manifestProcessingFlags,
            out List<WildcardPattern> list)
        {
            list = null;

            List<string> listOfStrings;
            if (!GetListOfStringsFromData(data, moduleManifestPath, key, manifestProcessingFlags, out listOfStrings))
            {
                return false;
            }

            if (listOfStrings != null)
            {
                list = new List<WildcardPattern>();
                foreach (string s in listOfStrings)
                {
                    // Win8: 622263
                    if (string.IsNullOrEmpty(s))
                    {
                        continue;
                    }

                    try
                    {
                        list.Add(WildcardPattern.Get(s, WildcardOptions.IgnoreCase));
                    }
                    catch (Exception e)
                    {
                        CommandProcessorBase.CheckForSevereException(e);

                        list = null;
                        WriteInvalidManifestMemberError(this, key, moduleManifestPath, e, manifestProcessingFlags);
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Checks to see if the module manifest contains the specified key. 
        /// If it does and it's valid, it returns true otherwise it returns false.
        /// If the key wasn't there or wasn't valid, then <paramref name="list"/> is set to <c>null</c>
        /// </summary>
        /// <param name="data">The hashtable to look for the key in</param>
        /// <param name="moduleManifestPath">The manifest that generated the hashtable</param>
        /// <param name="key">the table key to use</param>
        /// <param name="manifestProcessingFlags">Specifies how to treat errors and whether to load elements</param>
        /// <param name="moduleBase">base directory of a module</param>
        /// <param name="extension">expected file extension (added to strings that didn't have an extension)</param>
        /// <param name="verifyFilesExist">if <c>true</c> then we want to error out if the specified files don't exist</param>
        /// <param name="list">Returns the extracted version</param>
        /// <returns></returns>
        private bool GetListOfFilesFromData(
            Hashtable data,
            string moduleManifestPath,
            string key,
            ManifestProcessingFlags manifestProcessingFlags,
            string moduleBase,
            string extension,
            bool verifyFilesExist,
            out List<string> list)
        {
            list = null;

            List<string> listOfStrings;
            if (!GetListOfStringsFromData(data, moduleManifestPath, key, manifestProcessingFlags, out listOfStrings))
            {
                return false;
            }

            if (listOfStrings != null)
            {
                var psHome = Utils.GetApplicationBase(Utils.DefaultPowerShellShellID);
                string alternateDirToCheck = null;
                if (moduleBase.StartsWith(psHome, StringComparison.OrdinalIgnoreCase))
                {
                    // alternateDirToCheck is an ugly hack for how Microsoft.PowerShell.Diagnostics and
                    // Microsoft.WSMan.Management refer to the ps1xml that was in $PSHOME but removed.
                    alternateDirToCheck = moduleBase + "\\..\\..";
                }

                list = new List<string>();
                foreach (string s in listOfStrings)
                {
                    try
                    {
                        string fixedFileName = FixupFileName(moduleBase, s, extension);
                        var dir = Path.GetDirectoryName(fixedFileName);

                        if (string.Equals(psHome, dir, StringComparison.OrdinalIgnoreCase) ||
                            (alternateDirToCheck != null && string.Equals(alternateDirToCheck, dir, StringComparison.OrdinalIgnoreCase)))
                        {
                            // The ps1xml file no longer exists in $PSHOME.  Downstream, we expect a resolved path,
                            // which we can't really do b/c the file doesn't exist.
                            fixedFileName = psHome + "\\" + Path.GetFileName(s);
                        }
                        else if (verifyFilesExist && !Utils.NativeFileExists(fixedFileName))
                        {
                            string message = StringUtil.Format(SessionStateStrings.PathNotFound, fixedFileName);
                            throw new FileNotFoundException(message, fixedFileName);
                        }
                        list.Add(fixedFileName);
                    }
                    catch (Exception e)
                    {
                        CommandProcessorBase.CheckForSevereException(e);
                        if (0 != (manifestProcessingFlags & ManifestProcessingFlags.WriteErrors))
                        {
                            this.ThrowTerminatingError(GenerateInvalidModuleMemberErrorRecord(key, moduleManifestPath, e));
                        }
                        list = null;
                        WriteInvalidManifestMemberError(this, key, moduleManifestPath, e, manifestProcessingFlags);
                        return false;
                    }
                }
            }

            return true;
        }

        [Flags]
        internal enum ModuleLoggingGroupPolicyStatus
        {
            Undefined = 0x00,
            Enabled = 0x01,
            Disabled = 0x02,
        }

        /// <summary>
        /// Enable Module logging based on group policy
        /// </summary>
        internal void SetModuleLoggingInformation(PSModuleInfo m)
        {
            IEnumerable<string> moduleNames;
            ModuleLoggingGroupPolicyStatus status = GetModuleLoggingInformation(out moduleNames);
            if (status != ModuleLoggingGroupPolicyStatus.Undefined)
            {
                SetModuleLoggingInformation(status, m, moduleNames);
            }
        }

        private void SetModuleLoggingInformation(ModuleLoggingGroupPolicyStatus status, PSModuleInfo m, IEnumerable<string> moduleNames)
        {
            // TODO, insivara : What happens when Enabled but none of the other options (DefaultSystemModules, NonDefaultSystemModule, NonSystemModule, SpecificModules) are set?
            // After input from GP team for this behavior, need to revisit the commented out part
            //if ((status & ModuleLoggingGroupPolicyStatus.Enabled) != 0)
            //{

            //}

            if (((status & ModuleLoggingGroupPolicyStatus.Enabled) != 0) && moduleNames != null)
            {
                foreach (string currentGPModuleName in moduleNames)
                {
                    if (string.Equals(m.Name, currentGPModuleName, StringComparison.OrdinalIgnoreCase))
                    {
                        m.LogPipelineExecutionDetails = true;
                    }
                    else if (WildcardPattern.ContainsWildcardCharacters(currentGPModuleName))
                    {
                        WildcardPattern wildcard = WildcardPattern.Get(currentGPModuleName, WildcardOptions.IgnoreCase);
                        if (wildcard.IsMatch(m.Name))
                        {
                            m.LogPipelineExecutionDetails = true;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Get Module Logging information from the group policy. 
        /// </summary>
        internal static ModuleLoggingGroupPolicyStatus GetModuleLoggingInformation(out IEnumerable<string> moduleNames)
        {
            moduleNames = null;
            ModuleLoggingGroupPolicyStatus status = ModuleLoggingGroupPolicyStatus.Undefined;
            Dictionary<string, object> groupPolicySettings = Utils.GetGroupPolicySetting("ModuleLogging", Utils.RegLocalMachineThenCurrentUser);

            if (groupPolicySettings != null)
            {
                object enableModuleLoggingValue = null;
                if (groupPolicySettings.TryGetValue("EnableModuleLogging", out enableModuleLoggingValue))
                {
                    if (String.Equals(enableModuleLoggingValue.ToString(), "0", StringComparison.OrdinalIgnoreCase))
                    {
                        return ModuleLoggingGroupPolicyStatus.Disabled;
                    }

                    if (String.Equals(enableModuleLoggingValue.ToString(), "1", StringComparison.OrdinalIgnoreCase))
                    {
                        status = ModuleLoggingGroupPolicyStatus.Enabled;

                        object moduleNamesValue = null;
                        if (groupPolicySettings.TryGetValue("ModuleNames", out moduleNamesValue))
                        {
                            moduleNames = new List<String>((string[])moduleNamesValue);
                        }
                    }
                }
            }

            return status;
        }

        /// <summary>
        /// Checks to see if the module manifest contains the specified key. 
        /// If it does and it can be converted to the expected type, then it returns <c>true</c> and sets <paramref name="result"/> to the value.
        /// If the key is missing it returns <c>true</c> and sets <paramref name="result"/> to <c>default(<typeparam name="T"/>)</c>
        /// If the key is invalid then it returns <c>false</c>
        /// </summary>
        /// <param name="data">The hashtable to look for the key in</param>
        /// <param name="moduleManifestPath">The manifest that generated the hashtable</param>
        /// <param name="key">the table key to use</param>
        /// <param name="manifestProcessingFlags">Specifies how to treat errors and whether to load elements</param>
        /// <param name="result">Value from the manifest converted to the right type</param>
        /// <returns><c>true</c> if success; <c>false</c> if there were errors</returns>
        internal bool GetScalarFromData<T>(
            Hashtable data,
            string moduleManifestPath,
            string key,
            ManifestProcessingFlags manifestProcessingFlags,
            out T result)
        {
            object value = data[key];
            if ((value == null) || (value is string && string.IsNullOrEmpty((string)value)))
            {
                result = default(T);
                return true;
            }

            try
            {
                result = (T)LanguagePrimitives.ConvertTo(value, typeof(T), CultureInfo.InvariantCulture);
                return true;
            }
            catch (PSInvalidCastException e)
            {
                result = default(T);
                if (0 != (manifestProcessingFlags & ManifestProcessingFlags.WriteErrors))
                {
                    string message = StringUtil.Format(Modules.ModuleManifestInvalidValue, key, e.Message, moduleManifestPath);
                    ArgumentException newAe = new ArgumentException(message);
                    ErrorRecord er = new ErrorRecord(newAe, "Modules_InvalidManifest",
                        ErrorCategory.ResourceUnavailable, moduleManifestPath);
                    WriteError(er);
                }
                return false;
            }
        }

        /// <summary>
        /// A utility routine to fix up a file name so it's rooted and has an extension
        /// </summary>
        /// <param name="moduleBase">The base path to use if the file is not rooted</param>
        /// <param name="name">The file name to resolve.</param>
        /// <param name="extension">The extension to use.</param>
        /// 
        /// <returns></returns>
        internal string FixupFileName(string moduleBase, string name, string extension)
        {
            // First check for full-qualified paths - either absolute or relative
            string resolvedName;
            if (!IsRooted(name))
            {
                // The manifest file should only be related to moduleBase path.
                resolvedName = ResolveRootedFilePath(Path.Combine(moduleBase, name), this.Context);
            }
            else
            {
                resolvedName = ResolveRootedFilePath(name, this.Context);
            }

            if (string.IsNullOrEmpty(resolvedName))
            {
                resolvedName = Path.Combine(moduleBase, name);
            }
            // Again resolve the file path
            // This is so that any relative path references are expanded to give the absolute path 
            // C:\Windows\System32\WindowsPowerShell\V1.0\Modules\Microsoft.PowerShell.WSMAN\..\..\WSMan.format.ps1xml is expanded to 
            // C:\Windows\System32\WindowsPowerShell\V1.0\WSMan.format.ps1xml
            string resolvedName2 = ResolveRootedFilePath(resolvedName, this.Context);
            string ext = Path.GetExtension(name);
            string result = !string.IsNullOrEmpty(resolvedName2) ? resolvedName2 : resolvedName;
            if (string.IsNullOrEmpty(ext))
            {
                result += extension;
            }

            //For dlls, we cannot get the path from the provider.
            //We need to load the assembly and then get the path. 
            //If the module is already loaded, this is not expensive since the assembly is already loaded in the AppDomain
            if (!string.IsNullOrEmpty(ext) && ext.Equals(".dll", StringComparison.OrdinalIgnoreCase))
            {
                Exception ignored = null;
                Assembly assembly = ExecutionContext.LoadAssembly(name, null, out ignored);
                if (assembly != null)
                {
                    result = assembly.Location;
                }
            }

            return result;
        }

        /// <summary>
        /// A utility routine to fix up a file name, if it is relative path convert it to absolute path combining moduleBase, if it is not a relative path, leave as it is.
        /// </summary>
        /// <param name="moduleBase">The base path to use if the file is not rooted</param>
        /// <param name="path">The file name to resolve.</param>
        /// 
        /// <returns></returns>
        internal string GetAbsolutePath(string moduleBase, string path)
        {
            if (!IsRooted(path) && (path.Contains('/') || path.Contains('\\')))
            {
                return Path.Combine(moduleBase, path);
            }
            else
            {
                return path;
            }
        }

        internal static bool IsRooted(string filePath)
        {
            return (Path.IsPathRooted(filePath) ||
                filePath.StartsWith(@".\", StringComparison.Ordinal) ||
                filePath.StartsWith(@"./", StringComparison.Ordinal) ||
                filePath.StartsWith(@"..\", StringComparison.Ordinal) ||
                filePath.StartsWith(@"../", StringComparison.Ordinal) ||
                filePath.StartsWith(@"~/", StringComparison.Ordinal) ||
                filePath.StartsWith(@"~\", StringComparison.Ordinal) ||
                filePath.IndexOf(":", StringComparison.Ordinal) >= 0);
        }

        /// <summary>
        /// This utility resolves a rooted file name using the provider
        /// routines. It will only work if the path exists.
        /// </summary>
        /// <param name="filePath">The filename to resolve</param>
        /// <param name="context">Execution context</param>
        /// <returns>The resolved filename</returns>
        internal static String ResolveRootedFilePath(string filePath, ExecutionContext context)
        {
            // If the path is not fully qualified or relative rooted, then
            // we need to do path-based resolution...
            if (!IsRooted(filePath))
            {
                return null;
            }

            ProviderInfo provider = null;

            Collection<string> filePaths = null;

            if (context.EngineSessionState.IsProviderLoaded(context.ProviderNames.FileSystem))
            {
                try
                {
                    filePaths =
                        context.SessionState.Path.GetResolvedProviderPathFromPSPath(filePath, out provider);
                }
                catch (ItemNotFoundException)
                {
                    return null;
                }

                // Make sure that the path is in the file system - that's all we can handle currently...
                if (!provider.NameEquals(context.ProviderNames.FileSystem))
                {
                    // "The current provider ({0}) cannot open a file"
                    throw InterpreterError.NewInterpreterException(filePath, typeof(RuntimeException),
                        null, "FileOpenError", ParserStrings.FileOpenError, provider.FullName);
                }
            }

            // Make sure at least one file was found...
            if (filePaths == null || filePaths.Count < 1)
            {
                return null;
            }

            if (filePaths.Count > 1)
            {
                // "The path resolved to more than one file; can only process one file at a time."
                throw InterpreterError.
                    NewInterpreterException(filePaths, typeof(RuntimeException),
                    null, "AmbiguousPath", ParserStrings.AmbiguousPath);
            }

            return filePaths[0];
        }

        internal static string GetResolvedPath(string filePath, ExecutionContext context)
        {
            ProviderInfo provider = null;

            Collection<string> filePaths;


            if (context != null && context.EngineSessionState != null && context.EngineSessionState.IsProviderLoaded(context.ProviderNames.FileSystem))
            {
                try
                {
                    filePaths = context.SessionState.Path.GetResolvedProviderPathFromPSPath(filePath, true /* allowNonExistentPaths */, out provider);
                }
                catch (Exception e)
                {
                    CommandProcessor.CheckForSevereException(e);
                    return null;
                }
                // Make sure that the path is in the file system - that's all we can handle currently...
                if ((provider == null) || !provider.NameEquals(context.ProviderNames.FileSystem))
                {
                    return null;
                }
            }
            else
            {
                filePaths = new Collection<string>();
                filePaths.Add(filePath);
            }

            // Make sure at least one file was found...
            if (filePaths == null || filePaths.Count < 1 || filePaths.Count > 1)
            {
                return null;
            }

            return filePaths[0];
        }

        internal static Collection<string> GetResolvedPathCollection(string filePath, ExecutionContext context)
        {
            ProviderInfo provider = null;

            Collection<string> filePaths;


            if (context != null && context.EngineSessionState != null && context.EngineSessionState.IsProviderLoaded(context.ProviderNames.FileSystem))
            {
                try
                {
                    filePaths = context.SessionState.Path.GetResolvedProviderPathFromPSPath(filePath, true /* allowNonExistentPaths */, out provider);
                }
                catch (Exception e)
                {
                    CommandProcessor.CheckForSevereException(e);
                    return null;
                }
                // Make sure that the path is in the file system - that's all we can handle currently...
                if ((provider == null) || !provider.NameEquals(context.ProviderNames.FileSystem))
                {
                    return null;
                }
            }
            else
            {
                filePaths = new Collection<string>();
                filePaths.Add(filePath);
            }

            // Make sure at least one file was found...
            if (filePaths == null || filePaths.Count < 1)
            {
                return null;
            }

            return filePaths;
        }

        private void RemoveTypesAndFormatting(
            IList<string> formatFilesToRemove,
            IList<string> typeFilesToRemove)
        {
            try
            {
                if (this.Context.InitialSessionState != null)
                {
                    if (((formatFilesToRemove != null) && formatFilesToRemove.Count > 0) ||
                        ((typeFilesToRemove != null) && typeFilesToRemove.Count > 0))
                    {
                        bool oldRefreshTypeFormatSetting = this.Context.InitialSessionState.RefreshTypeAndFormatSetting;
                        try
                        {
                            this.Context.InitialSessionState.RefreshTypeAndFormatSetting = true;
                            InitialSessionState.RemoveTypesAndFormats(this.Context, formatFilesToRemove, typeFilesToRemove);
                        }
                        finally
                        {
                            this.Context.InitialSessionState.RefreshTypeAndFormatSetting = oldRefreshTypeFormatSetting;
                        }
                    }
                }
                else // RunspaceConfiguration
                {
                    if (formatFilesToRemove != null && formatFilesToRemove.Count > 0)
                    {
                        HashSet<string> formatRemoveSet = new HashSet<string>(formatFilesToRemove, StringComparer.OrdinalIgnoreCase);
                        List<int> formatIndicesToRemove = new List<int>();

                        for (int index = 0; index < this.Context.RunspaceConfiguration.Formats.Count; index++)
                        {
                            string fileName = Context.RunspaceConfiguration.Formats[index].FileName;
                            if (fileName != null && formatRemoveSet.Contains(fileName))
                            {
                                formatIndicesToRemove.Add(index);
                            }
                        }

                        for (int i = formatIndicesToRemove.Count - 1; i >= 0; i--)
                        {
                            Context.RunspaceConfiguration.Formats.RemoveItem(formatIndicesToRemove[i]);
                        }

                        this.Context.RunspaceConfiguration.Formats.Update();
                    }

                    // Remove the imported types.ps1xml files
                    if (typeFilesToRemove != null && typeFilesToRemove.Count > 0)
                    {
                        HashSet<string> typeRemoveSet = new HashSet<string>(typeFilesToRemove, StringComparer.OrdinalIgnoreCase);
                        List<int> typeIndicesToRemove = new List<int>();

                        for (int index = 0; index < this.Context.RunspaceConfiguration.Types.Count; index++)
                        {
                            string fileName = Context.RunspaceConfiguration.Types[index].FileName;
                            if (fileName != null && typeRemoveSet.Contains(fileName))
                            {
                                typeIndicesToRemove.Add(index);
                            }
                        }

                        for (int i = typeIndicesToRemove.Count - 1; i >= 0; i--)
                        {
                            Context.RunspaceConfiguration.Types.RemoveItem(typeIndicesToRemove[i]);
                        }

                        this.Context.RunspaceConfiguration.Types.Update();
                    }
                }
            }
            catch (RuntimeException rte)
            {
                string fullErrorId = rte.ErrorRecord.FullyQualifiedErrorId;
                if (fullErrorId.Equals("ErrorsUpdatingTypes", StringComparison.Ordinal) ||
                    fullErrorId.Equals("ErrorsUpdatingFormats", StringComparison.Ordinal))
                {
                    return;
                }

                throw;
            }
        }

        /// <summary>
        /// Removes a module from the session state
        /// </summary>
        /// <param name="module">module to remove</param>
        internal void RemoveModule(PSModuleInfo module)
        {
            RemoveModule(module, null);
        }

        /// <summary>
        /// Removes a module from the session state
        /// </summary>
        /// <param name="module">module to remove</param>
        ///  <param name="moduleNameInRemoveModuleCmdlet">module name specified in the cmdlet</param>
        internal void RemoveModule(PSModuleInfo module, string moduleNameInRemoveModuleCmdlet)
        {
            bool isTopLevelModule = false;

            // if the module path is empty string, means it is a dynamically generated assembly. 
            // We have set the module path to be module name as key to make it unique, we need update here as well in case the module can be removed.
            if (module.Path == "")
            {
                module.Path = module.Name;
            }
            bool shouldModuleBeRemoved = ShouldModuleBeRemoved(module, moduleNameInRemoveModuleCmdlet, out isTopLevelModule);

            if (shouldModuleBeRemoved)
            {
                // We don't check for dups in the remove list so it may already be removed
                if (Context.Modules.ModuleTable.ContainsKey(module.Path))
                {
                    // We should try to run OnRemove as the very first thing
                    if (module.OnRemove != null)
                    {
                        module.OnRemove.InvokeUsingCmdlet(
                            contextCmdlet: this,
                            useLocalScope: true,
                            errorHandlingBehavior: ScriptBlock.ErrorHandlingBehavior.WriteToCurrentErrorPipe,
                            dollarUnder: AutomationNull.Value,
                            input: AutomationNull.Value,
                            scriptThis: AutomationNull.Value,
                            args: new object[] { module });
                    }

                    if (module.ImplementingAssembly != null && module.ImplementingAssembly.IsDynamic == false)
                    {
                        var exportedTypes = PSSnapInHelpers.GetAssemblyTypes(module.ImplementingAssembly, module.Name);
                        foreach (var type in exportedTypes)
                        {
                            if (typeof(IModuleAssemblyCleanup).IsAssignableFrom(type) && type != typeof(IModuleAssemblyCleanup))
                            {
                                var moduleCleanup = (IModuleAssemblyCleanup)Activator.CreateInstance(type, true);
                                moduleCleanup.OnRemove(module);
                            }
                        }
                    }

                    // First remove cmdlets from the session state
                    // (can't just go through module.ExportedCmdlets
                    //  because the names of the cmdlets might have been changed by the -Prefix parameter of Import-Module)

                    List<string> keysToRemoveFromCmdletCache = new List<string>();
                    foreach (KeyValuePair<string, List<CmdletInfo>> cmdlet in Context.EngineSessionState.GetCmdletTable())
                    {
                        List<CmdletInfo> matches = cmdlet.Value;
                        // If the entry's module name matches, then remove it from the list...
                        for (int i = matches.Count - 1; i >= 0; i--)
                        {
                            if (matches[i].Module == null)
                            {
                                continue;
                            }
                            if (matches[i].Module.Path.Equals(module.Path, StringComparison.OrdinalIgnoreCase))
                            {
                                string name = matches[i].Name;
                                matches.RemoveAt(i);
                                Context.EngineSessionState.RemoveCmdlet(name, i, true);
                            }
                        }
                        // And finally remove the name from the cache if the list is now empty...
                        if (matches.Count == 0)
                        {
                            keysToRemoveFromCmdletCache.Add(cmdlet.Key);
                        }
                    }
                    foreach (string keyToRemove in keysToRemoveFromCmdletCache)
                    {
                        Context.EngineSessionState.RemoveCmdletEntry(keyToRemove, true);
                    }


                    // Remove any providers imported by this module. Providers are always imported into
                    // the top level session state. Only binary modules can import providers.
                    if (module.ModuleType == ModuleType.Binary)
                    {
                        Dictionary<string, List<ProviderInfo>> providers = Context.TopLevelSessionState.Providers;
                        List<string> keysToRemoveFromProviderTable = new List<string>();

                        foreach (KeyValuePair<string, System.Collections.Generic.List<ProviderInfo>> pl in providers)
                        {
                            Dbg.Assert(pl.Value != null, "There should never be a null list of entries in the provider table");

                            // For each provider with this name, if it was imported from the module,
                            // remove it from the list.
                            for (int i = pl.Value.Count - 1; i >= 0; i--)
                            {
                                ProviderInfo pi = pl.Value[i];

                                // If it was implemented by this module, remove it
                                string implAssemblyLocation = pi.ImplementingType.GetTypeInfo().Assembly.Location;
                                if (implAssemblyLocation.Equals(module.Path, StringComparison.OrdinalIgnoreCase))
                                {
                                    // Remove all drives from the top level session state
                                    InitialSessionState.RemoveAllDrivesForProvider(pi, Context.TopLevelSessionState);
                                    // Remove all drives from the current module context
                                    if (Context.EngineSessionState != Context.TopLevelSessionState)
                                        InitialSessionState.RemoveAllDrivesForProvider(pi, Context.EngineSessionState);
                                    // Remove drives from all other module contexts.
                                    foreach (PSModuleInfo psmi in Context.Modules.ModuleTable.Values)
                                    {
                                        if (psmi.SessionState != null)
                                        {
                                            SessionStateInternal mssi = psmi.SessionState.Internal;
                                            if (mssi != Context.TopLevelSessionState &&
                                                mssi != Context.EngineSessionState)
                                            {
                                                InitialSessionState.RemoveAllDrivesForProvider(pi, Context.EngineSessionState);
                                            }
                                        }
                                    }

                                    pl.Value.RemoveAt(i);
                                }
                            }

                            // If there are no providers left with this name, add this key to the list
                            // of entries to remove.
                            if (pl.Value.Count == 0)
                            {
                                keysToRemoveFromProviderTable.Add(pl.Key);
                            }
                        }

                        // Finally remove all of the empty table entries.
                        foreach (string keyToRemove in keysToRemoveFromProviderTable)
                        {
                            providers.Remove(keyToRemove);
                        }
                    }

                    SessionStateInternal ss = Context.EngineSessionState;

                    if (module.SessionState != null)
                    {
                        // Remove the imported functions from SessionState...
                        // (can't just go through module.SessionState.Internal.ExportedFunctions,
                        //  because the names of the functions might have been changed by the -Prefix parameter of Import-Module)
                        foreach (DictionaryEntry entry in ss.GetFunctionTable())
                        {
                            FunctionInfo func = (FunctionInfo)entry.Value;
                            if (func.Module == null)
                            {
                                continue;
                            }
                            if (func.Module.Path.Equals(module.Path, StringComparison.OrdinalIgnoreCase))
                            {
                                try
                                {
                                    ss.RemoveFunction(func.Name, true);

                                    string memberMessage = StringUtil.Format(Modules.RemovingImportedFunction, func.Name);
                                    WriteVerbose(memberMessage);
                                }
                                catch (SessionStateUnauthorizedAccessException e)
                                {
                                    string message = StringUtil.Format(Modules.UnableToRemoveModuleMember, func.Name, module.Name, e.Message);
                                    InvalidOperationException memberNotRemoved = new InvalidOperationException(message, e);
                                    ErrorRecord er = new ErrorRecord(memberNotRemoved, "Modules_MemberNotRemoved",
                                                                     ErrorCategory.PermissionDenied, func.Name);
                                    WriteError(er);
                                }
                            }
                        }

                        // Remove the imported variables from SessionState...
                        foreach (PSVariable mv in module.SessionState.Internal.ExportedVariables)
                        {
                            PSVariable sv = ss.GetVariable(mv.Name);
                            if (sv != null && sv == mv)
                            {
                                ss.RemoveVariable(sv, BaseForce);
                                string memberMessage = StringUtil.Format(Modules.RemovingImportedVariable, sv.Name);
                                WriteVerbose(memberMessage);
                            }
                        }

                        // Remove the imported aliases from SessionState... 
                        // (can't just go through module.SessionState.Internal.ExportedAliases,
                        //  because the names of the aliases might have been changed by the -Prefix parameter of Import-Module)
                        foreach (KeyValuePair<string, AliasInfo> entry in ss.GetAliasTable())
                        {
                            AliasInfo ai = entry.Value;
                            if (ai.Module == null)
                            {
                                continue;
                            }
                            if (ai.Module.Path.Equals(module.Path, StringComparison.OrdinalIgnoreCase))
                            {
                                // Remove the alias with force...
                                ss.RemoveAlias(ai.Name, true);
                                string memberMessage = StringUtil.Format(Modules.RemovingImportedAlias, ai.Name);
                                WriteVerbose(memberMessage);
                            }
                        }
                    }

                    this.RemoveTypesAndFormatting(module.ExportedFormatFiles, module.ExportedTypeFiles);
                    // resetting the help caches. This is needed as the help content cached is cached in process.
                    // When a module is removed there is no need to cache the help content for the commands in
                    // the module. The HelpSystem is not designed to track per module help content...so resetting
                    // all of the cache. HelpSystem knows how to build this cache back when needed.
                    Context.HelpSystem.ResetHelpProviders();

                    // Remove the module from all session state module tables...
                    foreach (KeyValuePair<string, PSModuleInfo> e in Context.Modules.ModuleTable)
                    {
                        PSModuleInfo m = e.Value;
                        if (m.SessionState != null)
                        {
                            if (m.SessionState.Internal.ModuleTable.ContainsKey(module.Path))
                            {
                                m.SessionState.Internal.ModuleTable.Remove(module.Path);
                                m.SessionState.Internal.ModuleTableKeys.Remove(module.Path);
                            }
                        }
                    }
                    if (isTopLevelModule)
                    {
                        // Remove it from the top level session state
                        Context.TopLevelSessionState.ModuleTable.Remove(module.Path);
                        Context.TopLevelSessionState.ModuleTableKeys.Remove(module.Path);
                    }
                    // And finally from the global module table...
                    Context.Modules.ModuleTable.Remove(module.Path);

                    // And the appdomain level module path cache.
                    PSModuleInfo.RemoveFromAppDomainLevelCache(module.Name);
                }
            }
        }

        private bool ShouldModuleBeRemoved(PSModuleInfo module, string moduleNameInRemoveModuleCmdlet, out bool isTopLevelModule)
        {
            isTopLevelModule = false;

            if (Context.TopLevelSessionState.ModuleTable.ContainsKey(module.Path))
            {
                isTopLevelModule = true;
                // This check makes sure that this module is removed only when we do a Remove-Module <moduleName> of this module
                // If this module is a nested module of some other module but is also imported as a top level module, then we should not be removing it.
                if (moduleNameInRemoveModuleCmdlet == null || module.Name.Equals(moduleNameInRemoveModuleCmdlet, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else
            {
                return true;
            }
            return false;
        }

        internal bool IsModuleAlreadyLoaded(PSModuleInfo alreadyLoadedModule)
        {
            if (alreadyLoadedModule == null)
            {
                return false;
            }
            if (this.BaseRequiredVersion != null && !alreadyLoadedModule.Version.Equals(this.BaseRequiredVersion))
            {
                // "alreadyLoadedModule" is a different module (than the one we want to load)
                return false;
            }
            if (this.BaseMinimumVersion != null && alreadyLoadedModule.Version < this.BaseMinimumVersion)
            {
                // "alreadyLoadedModule" is a different module (than the one we want to load)
                return false;
            }
            if (this.BaseMaximumVersion != null && alreadyLoadedModule.Version > this.BaseMaximumVersion)
            {
                // "alreadyLoadedModule" is a different module (than the one we want to load)
                return false;
            }
            if (BaseGuid != null && !alreadyLoadedModule.Guid.Equals(this.BaseGuid))
            {
                // "alreadyLoadedModule" is a different module (than the one we want to load)
                return false;
            }

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="modulePath"></param>
        /// <param name="prefix"></param>
        /// <param name="options"></param>
        /// <returns>
        /// Returns PSModuleInfo of an already loaded module if that module can be simply reimported and there is no need to proceed with a regular import
        /// Returns <c>null</c> if the caller should proceed with a regular import (either because there is no previously loaded module, or because the -Force flag was specified and the previously loaded module has been removed by this method)
        /// </returns>
        internal PSModuleInfo IsModuleImportUnnecessaryBecauseModuleIsAlreadyLoaded(string modulePath, string prefix, ImportModuleOptions options)
        {
            PSModuleInfo alreadyLoadedModule;
            if (this.Context.Modules.ModuleTable.TryGetValue(modulePath, out alreadyLoadedModule))
            {
                if (this.IsModuleAlreadyLoaded(alreadyLoadedModule))
                {
                    if (this.BaseForce) // remove the previously imported module + return null (null = please proceed with regular import)
                    {
                        // remove the module
                        RemoveModule(alreadyLoadedModule);

                        // proceed with regular import
                        return null;
                    }
                    else // reimport the module + return alreadyLoadedModule (alreadyLoadedModule = no need to proceed with regular import)
                    {
                        // If the module has already been loaded, then while loading it the second time, we should load it with the DefaultCommandPrefix specified in the module manifest. (If there is no Prefix from command line)
                        if (string.IsNullOrEmpty(prefix) && Utils.NativeFileExists(alreadyLoadedModule.Path))
                        {
                            string defaultPrefix = GetDefaultPrefix(alreadyLoadedModule);
                            if (!string.IsNullOrEmpty(defaultPrefix))
                            {
                                prefix = defaultPrefix;
                            }
                        }

                        AddModuleToModuleTables(this.Context, this.TargetSessionState.Internal, alreadyLoadedModule);

                        // Even if the alreadyLoadedModule has been loaded, import the specified members...
                        ImportModuleMembers(alreadyLoadedModule, prefix, options);

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

                        // no need to proceed with regular import
                        return alreadyLoadedModule;
                    }
                }
            }

            // no previously imported module (or already loaded module has wrong version) - proceed with regular import
            return null;
        }

        /// <summary>
        /// Loads a module file after searching for it using the known extension list
        /// </summary>
        /// <param name="parentModule">The parent module for which this module is a nested module</param>
        /// <param name="moduleName">The name to use for the module</param>
        /// <param name="fileBaseName">The file basename for this module</param>
        /// <param name="extension">The module's extension</param>
        /// <param name="moduleBase">The module base which comes from the module manifest</param>
        /// <param name="prefix">Command name prefix</param>
        /// <param name="ss">
        /// The session state instance to use for this module - may be null
        /// in which case a session state will be allocated if necessary
        /// </param>
        /// <param name="options">The set of options that are used while importing a module</param>
        /// <param name="manifestProcessingFlags">The processing flags to use when processing the module</param>
        /// <param name="found">True if a module was found</param>
        /// <returns></returns>
        internal PSModuleInfo LoadUsingExtensions(PSModuleInfo parentModule,
            string moduleName, string fileBaseName, string extension, string moduleBase,
            string prefix, SessionState ss, ImportModuleOptions options, ManifestProcessingFlags manifestProcessingFlags, out bool found)
        {
            bool throwAwayModuleFileFound = false;
            return LoadUsingExtensions(parentModule, moduleName, fileBaseName, extension, moduleBase, prefix, ss,
                                       options, manifestProcessingFlags, out found, out throwAwayModuleFileFound);
        }

        /// <summary>
        /// Loads a module file after searching for it using the known extension list
        /// </summary>
        /// <param name="parentModule">The parent module for which this module is a nested module</param>
        /// <param name="moduleName">The name to use for the module</param>
        /// <param name="fileBaseName">The file basename for this module</param>
        /// <param name="extension">The module's extension</param>
        /// <param name="moduleBase">The module base which comes from the module manifest</param>
        /// <param name="prefix">Command name prefix</param>
        /// <param name="ss">
        /// The session state instance to use for this module - may be null
        /// in which case a session state will be allocated if necessary
        /// </param>
        /// <param name="options">The set of options that are used while importing a module</param>
        /// <param name="manifestProcessingFlags">The processing flags to use when processing the module</param>
        /// <param name="found">True if a module was found</param>
        /// <param name="moduleFileFound">True if a module file was found</param>
        /// <returns></returns>
        internal PSModuleInfo LoadUsingExtensions(PSModuleInfo parentModule,
            string moduleName, string fileBaseName, string extension, string moduleBase,
            string prefix, SessionState ss, ImportModuleOptions options, ManifestProcessingFlags manifestProcessingFlags, out bool found, out bool moduleFileFound)
        {
            string[] extensions;
            PSModuleInfo module;
            moduleFileFound = false;

            if (!string.IsNullOrEmpty(extension))
                extensions = new string[] { extension };
            else
                extensions = ModuleIntrinsics.PSModuleExtensions;

            var importingModule = 0 != (manifestProcessingFlags & ManifestProcessingFlags.LoadElements);

            // "ni.dll" has a higher priority then ".dll" to be loaded.
            for (int i = 0; i < extensions.Length; i++)
            {
                string ext = extensions[i];
                string fileName = fileBaseName + ext;

                // Get the resolved file name
                fileName = GetResolvedPath(fileName, Context);

                if (fileName == null)
                    continue;

                // If we've found ourselves, then skip and move on to the next extension.
                // This will be the case where a scriptmodule foo.psm1 does an "Import-Module foo" intending to get foo.dll.
                if (!string.IsNullOrEmpty(Context.ModuleBeingProcessed) &&
                    Context.ModuleBeingProcessed.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // If the module has already been loaded, just emit it and continue...
                Context.Modules.ModuleTable.TryGetValue(fileName, out module);
                // TODO/FIXME: use IsModuleAlreadyLoaded to get consistent behavior
                //if (!BaseForce && 
                //    IsModuleAlreadyLoaded(module) &&
                //    ((manifestProcessingFlags & ManifestProcessingFlags.LoadElements) == ManifestProcessingFlags.LoadElements))
                if (!BaseForce && module != null &&
                    (BaseRequiredVersion == null || module.Version.Equals(BaseRequiredVersion)) &&
                    ((BaseMinimumVersion == null && BaseMaximumVersion == null)
                        || (BaseMaximumVersion != null && BaseMinimumVersion == null && module.Version <= BaseMaximumVersion)
                        || (BaseMaximumVersion == null && BaseMinimumVersion != null && module.Version >= BaseMinimumVersion)
                        || (BaseMaximumVersion != null && BaseMinimumVersion != null && module.Version >= BaseMinimumVersion && module.Version <= BaseMaximumVersion)) &&
                    (BaseGuid == null || module.Guid.Equals(BaseGuid)) && importingModule)
                {
                    moduleFileFound = true;
                    module = Context.Modules.ModuleTable[fileName];
                    // If the module has already been loaded, then while loading it the second time, we should load it with the DefaultCommandPrefix specified in the module manifest. (If there is no Prefix from command line)
                    if (string.IsNullOrEmpty(prefix))
                    {
                        string defaultPrefix = GetDefaultPrefix(module);
                        if (!string.IsNullOrEmpty(defaultPrefix))
                        {
                            prefix = defaultPrefix;
                        }
                    }

                    AddModuleToModuleTables(this.Context, this.TargetSessionState.Internal, module);

                    // Even if the module has been loaded, import the specified members...
                    ImportModuleMembers(module, prefix, options);

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
                    return module;
                }
                else if (Utils.NativeFileExists(fileName))
                {
                    moduleFileFound = true;
                    // Win8: 325243 - Added the version check so that we do not unload modules with the same name but different version 
                    if (BaseForce && module != null &&
                        (this.BaseRequiredVersion == null || module.Version.Equals(this.BaseRequiredVersion)) &&
                        (BaseGuid == null || module.Guid.Equals(BaseGuid)))
                    // TODO/FIXME: use IsModuleAlreadyLoaded to get consistent behavior
                    // TODO/FIXME: (for example the checks above are not lookint at this.BaseMinimumVersion)
                    //if (BaseForce && IsModuleAlreadyLoaded(module))
                    {
                        RemoveModule(module);
                    }
                    module = LoadModule(parentModule, fileName, moduleBase, prefix, ss, null, ref options, manifestProcessingFlags, out found, out moduleFileFound);
                    if (found)
                    {
                        return module;
                    }
                }
            }

            found = false;
            return null;
        }

        internal string GetDefaultPrefix(PSModuleInfo module)
        {
            string prefix = string.Empty;
            string extension = Path.GetExtension(module.Path);
            if (!string.IsNullOrEmpty(extension) && extension.Equals(StringLiterals.PowerShellDataFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                string throwAwayScriptName;
                ExternalScriptInfo scriptInfo = GetScriptInfoForFile(module.Path, out throwAwayScriptName, true);
                Dbg.Assert(scriptInfo != null, "scriptInfo for module (.psd1) can't be null");
                bool containedErrors = false;
                Hashtable data = null;
                Hashtable localizedData = null;

                if (LoadModuleManifestData(scriptInfo, ManifestProcessingFlags.NullOnFirstError, out data, out localizedData, ref containedErrors))
                {
                    // Get Default prefix
                    if (data.Contains("DefaultCommandPrefix"))
                    {
                        if (localizedData != null && localizedData.Contains("DefaultCommandPrefix"))
                        {
                            prefix = (string)LanguagePrimitives.ConvertTo(localizedData["DefaultCommandPrefix"],
                                typeof(string), CultureInfo.InvariantCulture);
                        }
                        if (string.IsNullOrEmpty(prefix))
                        {
                            prefix = (string)LanguagePrimitives.ConvertTo(data["DefaultCommandPrefix"],
                                typeof(string), CultureInfo.InvariantCulture);
                        }
                    }
                }
            }
            return prefix;
        }

        /// <summary>
        /// Create an ExternalScriptInfo object from a file path.
        /// </summary>
        /// <param name="fileName">The path to the file</param>
        /// <param name="scriptName">The base name of the script</param>
        /// <param name="checkExecutionPolicy">check the current execution policy</param>
        /// <returns>The ExternalScriptInfo object.</returns>
        internal ExternalScriptInfo GetScriptInfoForFile(string fileName, out string scriptName, bool checkExecutionPolicy)
        {
            scriptName = Path.GetFileName(fileName);
            ExternalScriptInfo scriptInfo = new ExternalScriptInfo(scriptName, fileName, Context);

            // Skip ShouldRun check for .psd1 files.
            // Use ValidateScriptInfo() for explicitly validating the checkpolicy for psd1 file.
            //
            if (!scriptName.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase))
            {
                if (checkExecutionPolicy)
                {
                    Context.AuthorizationManager.ShouldRunInternal(scriptInfo, CommandOrigin.Runspace,
                        Context.EngineHostInterface);
                }
                else
                {
                    Context.AuthorizationManager.ShouldRunInternal(scriptInfo, CommandOrigin.Internal,
                        Context.EngineHostInterface);
                }

                if (!scriptName.EndsWith(".cdxml", StringComparison.OrdinalIgnoreCase))
                {
                    CommandDiscovery.VerifyScriptRequirements(scriptInfo, Context);

                    // Verify that the NetFrameWorkVersion is correct...

                    #region comment out RequiresNetFrameworkVersion feature 8/10/2010

                    /*
                     * The "#requires -NetFrameworkVersion" feature is CUT OFF.
                     * The call of "VerifyNetFrameworkVersion" will be CUT OFF too.
                    /*
                    CommandDiscovery.VerifyNetFrameworkVersion(scriptInfo);
                    */

                    #endregion
                }

                // If we got this far, the check succeeded and we don't need to check again.
                scriptInfo.SignatureChecked = true;
            }

            return scriptInfo;
        }

        /// <summary>
        /// Load a module from a file...
        /// </summary>
        /// <param name="fileName">The resolved path to load the module from</param>
        /// <param name="moduleBase">The module base path to use for this module</param>
        /// <param name="prefix">Command name prefix</param>
        /// <param name="ss">The session state instance to use for this module - may be null in which case a session state will be allocated if necessary</param>
        /// <param name="options">The set of options that are used while importing a module</param>
        /// <param name="manifestProcessingFlags">The manifest processing flags to use when processing the module</param>
        /// <param name="found">True if a module was found</param>
        /// <returns>True if the module was successfully loaded</returns>
        internal PSModuleInfo LoadModule(string fileName, string moduleBase, string prefix, SessionState ss, ref ImportModuleOptions options,
            ManifestProcessingFlags manifestProcessingFlags, out bool found)
        {
            bool throwAwayModuleFileFound = false;
            return LoadModule(null, fileName, moduleBase, prefix, ss, null, ref options, manifestProcessingFlags, out found, out throwAwayModuleFileFound);
        }

        /// <summary>
        /// Load a module from a file...
        /// </summary>
        /// <param name="parentModule">The parent module, if any</param>
        /// <param name="fileName">The resolved path to load the module from</param>
        /// <param name="moduleBase">The module base path to use for this module</param>
        /// <param name="prefix">Command name prefix</param>
        /// <param name="ss">The session state instance to use for this module - may be null in which case a session state will be allocated if necessary</param>
        /// <param name="privateData">Private Data for the module</param>
        /// <param name="options">The set of options that are used while importing a module</param>
        /// <param name="manifestProcessingFlags">The manifest processing flags to use when processing the module</param>
        /// <param name="found">True if a module was found</param>
        /// <param name="moduleFileFound">True if a module file was found</param>
        /// <returns>True if the module was successfully loaded</returns>
        internal PSModuleInfo LoadModule(PSModuleInfo parentModule, string fileName, string moduleBase, string prefix,
            SessionState ss, object privateData, ref ImportModuleOptions options,
            ManifestProcessingFlags manifestProcessingFlags, out bool found, out bool moduleFileFound)
        {
            Dbg.Assert(fileName != null, "Filename argument to LoadModule() shouldn't be null");

            if (!Utils.NativeFileExists(fileName))
            {
                found = false;
                moduleFileFound = false;
                return null;
            }

            var importingModule = 0 != (manifestProcessingFlags & ManifestProcessingFlags.LoadElements);
            var writingErrors = 0 != (manifestProcessingFlags & ManifestProcessingFlags.WriteErrors);

            // In case the file is a Ngen Assembly.
            string ext;
            if (fileName.EndsWith(StringLiterals.PowerShellNgenAssemblyExtension, StringComparison.OrdinalIgnoreCase))
            {
                ext = StringLiterals.PowerShellNgenAssemblyExtension;
            }
            else
            {
                ext = Path.GetExtension(fileName);
            }
            PSModuleInfo module = null;

            // If  MinimumVersion/RequiredVersion/MaximumVersion has been specified, then only try to process manifest modules...
            if (BaseMinimumVersion != null || BaseMaximumVersion != null || BaseRequiredVersion != null || BaseGuid != null)
            {
                // If the -Version flag was specified, don't look for non-manifest modules
                if (string.IsNullOrEmpty(ext) || !ext.Equals(StringLiterals.PowerShellDataFileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    found = false;
                    moduleFileFound = false;
                    return null;
                }

                // If the module is in memory and the versions don't match don't return it.
                // This will allow the search to continue and load a different version of the module.
                if (Context.Modules.ModuleTable.TryGetValue(fileName, out module))
                {
                    if (BaseMinimumVersion != null && module.Version >= BaseMinimumVersion)
                    {
                        if (BaseMaximumVersion == null || module.Version <= BaseMaximumVersion)
                        {
                            found = false;
                            moduleFileFound = false;
                            return null;
                        }
                    }
                }
            }

            found = false;
            string scriptName;
            ExternalScriptInfo scriptInfo = null;

            string _origModuleBeingProcessed = Context.ModuleBeingProcessed;
            try
            {
                // Set the name of the module currently being processed...
                Context.PreviousModuleProcessed = Context.ModuleBeingProcessed;
                Context.ModuleBeingProcessed = fileName;

                string message = StringUtil.Format(Modules.LoadingModule, fileName);
                WriteVerbose(message);

                moduleFileFound = true;

                if (ext.Equals(StringLiterals.PowerShellModuleFileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    if (!importingModule)
                    {
                        bool shouldProcessModule = ShouldProcessScriptModule(parentModule, ref found);

                        if (shouldProcessModule)
                        {
                            bool force = (manifestProcessingFlags & ManifestProcessingFlags.Force) == ManifestProcessingFlags.Force;
                            module = AnalyzeScriptFile(fileName, force, Context);
                            found = true;
                        }
                    }
                    else
                    {
                        scriptInfo = GetScriptInfoForFile(fileName, out scriptName, true);
                        try
                        {
                            Context.Modules.IncrementModuleNestingDepth(this, scriptInfo.Path);

                            // Create the module object...
                            try
                            {
                                module = Context.Modules.CreateModule(fileName, scriptInfo, MyInvocation.ScriptPosition, ss, privateData, BaseArgumentList);
                                module.SetModuleBase(moduleBase);

                                SetModuleLoggingInformation(module);

                                // If the script didn't call Export-ModuleMember explicitly, then
                                // implicitly export functions and cmdlets.
                                if (!module.SessionState.Internal.UseExportList)
                                {
                                    ModuleIntrinsics.ExportModuleMembers(this, module.SessionState.Internal, MatchAll,
                                        MatchAll, MatchAll, null, options.ServiceCoreAutoAdded ? ModuleCmdletBase.s_serviceCoreAssemblyCmdlets : null);
                                }

                                // Add it to the all module tables
                                if (importingModule)
                                {
                                    ImportModuleMembers(module, prefix, options);
                                    AddModuleToModuleTables(this.Context, this.TargetSessionState.Internal, module);
                                }

                                found = true;
                                if (BaseAsCustomObject)
                                {
                                    WriteObject(module.AsCustomObject());
                                }
                                else
                                    // If -pass has been specified, emit a module info...
                                    if (BasePassThru)
                                {
                                    WriteObject(module);
                                }
                            }
                            catch (RuntimeException e)
                            {
                                if (writingErrors)
                                {
                                    e.ErrorRecord.PreserveInvocationInfoOnce = true;
                                }

                                if (e.WasThrownFromThrowStatement)
                                {
                                    ThrowTerminatingError(e.ErrorRecord);
                                }
                                else
                                {
                                    WriteError(e.ErrorRecord);
                                }
                            }
                        }
                        finally
                        {
                            Context.Modules.DecrementModuleNestingCount();
                        }
                    }
                }
                else if (ext.Equals(StringLiterals.PowerShellScriptFileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    if (!importingModule)
                    {
                        bool shouldProcessModule = ShouldProcessScriptModule(parentModule, ref found);

                        if (shouldProcessModule)
                        {
                            bool force = (manifestProcessingFlags & ManifestProcessingFlags.Force) == ManifestProcessingFlags.Force;
                            module = AnalyzeScriptFile(fileName, force, Context);
                            found = true;
                        }
                    }
                    else
                    {
                        // Create a dummy module info
                        // The module info created for a .ps1 file will not have ExportedCommands populated. 
                        // Removing the module will not remove the commands dot-sourced from the .ps1 file.
                        // This module info is created so that we can keep the behavior consistent between scripts imported as modules and other kind of modules(all of them should have a PSModuleInfo).
                        // Auto-loading expects we always have a PSModuleInfo object for any module. This is how this issue was found.
                        module = new PSModuleInfo(ModuleIntrinsics.GetModuleName(fileName), fileName, Context, ss);

                        scriptInfo = GetScriptInfoForFile(fileName, out scriptName, true);

                        message = StringUtil.Format(Modules.DottingScriptFile, fileName);
                        WriteVerbose(message);

                        try
                        {
                            Dbg.Assert(scriptInfo != null, "Scriptinfo for dotted file can't be null");
                            found = true;

                            InvocationInfo oldInvocationInfo = (InvocationInfo)Context.GetVariableValue(SpecialVariables.MyInvocationVarPath);
                            object oldPSScriptRoot = Context.GetVariableValue(SpecialVariables.PSScriptRootVarPath);
                            object oldPSCommandPath = Context.GetVariableValue(SpecialVariables.PSCommandPathVarPath);

                            try
                            {
                                InvocationInfo invocationInfo = new InvocationInfo(scriptInfo, scriptInfo.ScriptBlock.Ast.Extent,
                                                                                    Context);
                                scriptInfo.ScriptBlock.InvokeWithPipe(
                                    useLocalScope: false,
                                    errorHandlingBehavior: ScriptBlock.ErrorHandlingBehavior.WriteToCurrentErrorPipe,
                                    dollarUnder: AutomationNull.Value,
                                    input: AutomationNull.Value,
                                    scriptThis: AutomationNull.Value,
                                    outputPipe: ((MshCommandRuntime)this.CommandRuntime).OutputPipe,
                                    invocationInfo: invocationInfo,
                                    args: this.BaseArgumentList ?? Utils.EmptyArray<object>());
                            }
                            finally
                            {
                                // since useLocalScope is set false while calling InvokeWithPipe, we need to 
                                // revert the changes made to LocalsTuple
                                if (Context.EngineSessionState.CurrentScope.LocalsTuple != null)
                                {
                                    Context.EngineSessionState.CurrentScope.LocalsTuple.SetAutomaticVariable(
                                        AutomaticVariable.PSScriptRoot, oldPSScriptRoot, Context);
                                    Context.EngineSessionState.CurrentScope.LocalsTuple.SetAutomaticVariable(AutomaticVariable.PSCommandPath, oldPSCommandPath, Context);
                                    Context.EngineSessionState.CurrentScope.LocalsTuple.SetAutomaticVariable(AutomaticVariable.MyInvocation, oldInvocationInfo, Context);
                                }
                            }

                            AddModuleToModuleTables(this.Context, this.TargetSessionState.Internal, module);

                            if (BaseAsCustomObject)
                            {
                                WriteObject(module.AsCustomObject());
                            }
                            else
                                // If -pass has been specified, emit a module info...
                                if (BasePassThru)
                            {
                                WriteObject(module);
                            }
                        }
                        catch (RuntimeException e)
                        {
                            if (writingErrors)
                            {
                                e.ErrorRecord.PreserveInvocationInfoOnce = true;
                            }
                            if (e.WasThrownFromThrowStatement)
                            {
                                ThrowTerminatingError(e.ErrorRecord);
                            }
                            else
                            {
                                WriteError(e.ErrorRecord);
                            }
                        }
                        catch (ExitException ee)
                        {
                            int exitCode = (int)ee.Argument;
                            Context.SetVariable(SpecialVariables.LastExitCodeVarPath, exitCode);
                        }
                    }
                }
                else if (ext.Equals(StringLiterals.PowerShellDataFileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    scriptInfo = GetScriptInfoForFile(fileName, out scriptName, true);
                    found = true;
                    Dbg.Assert(scriptInfo != null, "Scriptinfo for module manifest (.psd1) can't be null");
                    module = LoadModuleManifest(
                        scriptInfo,
                        manifestProcessingFlags,
                        BaseMinimumVersion,
                        BaseMaximumVersion,
                        BaseRequiredVersion,
                        BaseGuid,
                        ref options);

                    if (module != null)
                    {
                        if (importingModule)
                        {
                            // Add it to all the module tables
                            AddModuleToModuleTables(this.Context, this.TargetSessionState.Internal, module);
                        }

                        if (BasePassThru)
                        {
                            WriteObject(module);
                        }
                    }
                    else if (BaseMinimumVersion != null || BaseRequiredVersion != null || BaseGuid != null || BaseMaximumVersion != null)
                    {
                        found = false;
                    }
                }
                else if (ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) || ext.Equals(StringLiterals.PowerShellNgenAssemblyExtension))
                {
                    module = LoadBinaryModule(false, ModuleIntrinsics.GetModuleName(fileName), fileName, null,
                        moduleBase, ss, options, manifestProcessingFlags, prefix, true, true, out found);
                    if (found && module != null)
                    {
                        if (importingModule)
                        {
                            // Add it to all the module tables
                            AddModuleToModuleTables(this.Context, this.TargetSessionState.Internal, module);
                        }

                        if (BaseAsCustomObject)
                        {
                            message = StringUtil.Format(Modules.CantUseAsCustomObjectWithBinaryModule, fileName);
                            InvalidOperationException invalidOp = new InvalidOperationException(message);
                            ErrorRecord er = new ErrorRecord(invalidOp, "Modules_CantUseAsCustomObjectWithBinaryModule",
                                ErrorCategory.PermissionDenied, null);
                            WriteError(er);
                        }
                        else if (BasePassThru)
                        {
                            WriteObject(module);
                        }
                    }
                }
                else if (ext.Equals(StringLiterals.WorkflowFileExtension, StringComparison.OrdinalIgnoreCase))
                {
#if CORECLR         // Workflow Not Supported On CSS
                    PSNotSupportedException workflowModuleNotSupported =
                        PSTraceSource.NewNotSupportedException(
                            Modules.WorkflowModuleNotSupportedInPowerShellCore,
                            ModuleIntrinsics.GetModuleName(fileName));
                    throw workflowModuleNotSupported;
#else
                    if (Utils.IsRunningFromSysWOW64())
                    {
                        throw new NotSupportedException(AutomationExceptions.WorkflowDoesNotSupportWOW64);
                    }

                    scriptInfo = GetScriptInfoForFile(fileName, out scriptName, true);

                    ImportModuleOptions nestedModuleOptions = new ImportModuleOptions();
                    List<string> wfToProcess = new List<string>();
                    wfToProcess.Add(fileName);

                    found = true;

                    if (!importingModule)
                    {
                        module = new PSModuleInfo(ModuleIntrinsics.GetModuleName(fileName), fileName, null, null);

                        ProcessWorkflowsToProcess(moduleBase, wfToProcess, new List<string>(), new List<string>(), null, module, nestedModuleOptions);
                    }
                    else
                    {
                        if (ss == null)
                        {
                            ss = new SessionState(Context, true, true);
                        }

                        module = new PSModuleInfo(ModuleIntrinsics.GetModuleName(fileName), fileName, Context, ss);
                        ss.Internal.Module = module;
                        module.PrivateData = privateData;
                        module.SetModuleType(ModuleType.Workflow);
                        module.SetModuleBase(moduleBase);

                        nestedModuleOptions.ServiceCoreAutoAdded = true;

                        LoadServiceCoreModule(module, String.Empty, ss, nestedModuleOptions, manifestProcessingFlags, true, out found);

                        ProcessWorkflowsToProcess(moduleBase, wfToProcess, new List<string>(), new List<string>(), ss, module, nestedModuleOptions);

                        // And import the members from this module into the callers context...
                        if (importingModule)
                        {
                            ImportModuleMembers(module, prefix, options);
                        }

                        // Add it to all the module tables
                        AddModuleToModuleTables(this.Context, this.TargetSessionState.Internal, module);
                    }

                    if (BaseAsCustomObject)
                    {
                        WriteObject(module.AsCustomObject());
                    }
                    else if (BasePassThru)
                    {
                        WriteObject(module);
                    }
#endif
                }
                else if (ext.Equals(StringLiterals.PowerShellCmdletizationFileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;

                    // Create the module object...
                    try
                    {
                        string moduleName = ModuleIntrinsics.GetModuleName(fileName);
                        scriptInfo = GetScriptInfoForFile(fileName, out scriptName, true);

                        try
                        {
                            // generate cmdletization proxies
                            var cmdletizationXmlReader = new StringReader(scriptInfo.ScriptContents);
                            var cmdletizationProxyModuleWriter = new StringWriter(CultureInfo.InvariantCulture);
                            var scriptWriter = new ScriptWriter(
                                cmdletizationXmlReader,
                                moduleName,
                                StringLiterals.DefaultCmdletAdapter,
                                this.MyInvocation,
                                ScriptWriter.GenerationOptions.HelpXml);

                            if (!importingModule)
                            {
                                module = new PSModuleInfo(fileName, null, null);
                                scriptWriter.PopulatePSModuleInfo(module);
                                scriptWriter.ReportExportedCommands(module, prefix);
                            }
                            else
                            {
                                scriptWriter.WriteScriptModule(cmdletizationProxyModuleWriter);
                                ScriptBlock sb = ScriptBlock.Create(this.Context, cmdletizationProxyModuleWriter.ToString());

                                // CDXML doesn't allow script injection, so it is trusted.
                                sb.LanguageMode = PSLanguageMode.FullLanguage;

                                // proceed with regular module import
                                List<object> results;
                                module = Context.Modules.CreateModule(moduleName, fileName, sb, ss, out results, BaseArgumentList);
                                module.SetModuleBase(moduleBase);
                                scriptWriter.PopulatePSModuleInfo(module);
                                scriptWriter.ReportExportedCommands(module, prefix);

                                ImportModuleMembers(module, prefix, options);

                                AddModuleToModuleTables(this.Context, this.TargetSessionState.Internal, module);
                            }
                        }
                        catch (Exception e)
                        {
                            CommandProcessor.CheckForSevereException(e);

                            string xmlErrorMessage = string.Format(
                                CultureInfo.InvariantCulture, // file name is culture agnostic, we want to copy exception message verbatim
                                CmdletizationCoreResources.ExportCimCommand_ErrorInCmdletizationXmlFile,
                                fileName,
                                e.Message);
                            throw new XmlException(xmlErrorMessage, e);
                        }

                        if (BaseAsCustomObject)
                        {
                            WriteObject(module.AsCustomObject());
                        }
                        else
                            // If -pass has been specified, emit a module info...
                            if (BasePassThru)
                        {
                            WriteObject(module);
                        }
                    }
                    catch (RuntimeException e)
                    {
                        if (writingErrors)
                        {
                            e.ErrorRecord.PreserveInvocationInfoOnce = true;
                            WriteError(e.ErrorRecord);
                        }
                    }
                }
                else
                {
                    found = true;
                    message = StringUtil.Format(Modules.InvalidModuleExtension, ext, fileName);
                    InvalidOperationException invalidOp = new InvalidOperationException(message);
                    ErrorRecord er = new ErrorRecord(invalidOp, "Modules_InvalidModuleExtension",
                        ErrorCategory.PermissionDenied, null);
                    WriteError(er);
                }
            }
            finally
            {
                // Restore the name of the module being processed...
                Context.ModuleBeingProcessed = _origModuleBeingProcessed;
            }

            // If using AppDomain-level module path caching, add this module to the cache. This is only done for 
            // the modules loaded with without version info or other qualifiers.
            if (PSModuleInfo.UseAppDomainLevelModuleCache && module != null && moduleBase == null && this.AddToAppDomainLevelCache)
            {
                // Cache using the actual name specified by the user rather than the module basename
                PSModuleInfo.AddToAppDomainLevelModuleCache(module.Name, fileName, this.BaseForce);
            }

            return module;
        }

        private static bool ShouldProcessScriptModule(PSModuleInfo parentModule, ref bool found)
        {
            bool shouldProcessModule = true;

            // If we are in module analysis and the parent module declares non-wildcarded exports, then we don't need to
            // actually process the script module.

            if (parentModule != null)
            {
                if (shouldProcessModule && (parentModule.DeclaredFunctionExports != null) && (parentModule.DeclaredFunctionExports.Count > 0))
                {
                    shouldProcessModule = false;

                    foreach (string exportedFunction in parentModule.ExportedFunctions.Keys)
                    {
                        if (WildcardPattern.ContainsWildcardCharacters(exportedFunction))
                        {
                            shouldProcessModule = true;
                            break;
                        }
                    }

                    found = true;
                }
            }

            return shouldProcessModule;
        }

        private static object s_lockObject = new object();

        private void ClearAnalysisCaches()
        {
            lock (s_lockObject)
            {
                s_binaryAnalysisCache.Clear();
                s_scriptAnalysisCache.Clear();
            }
        }

        // Analyzes a binary module implementation for its cmdlets.
        private static Dictionary<string, Tuple<BinaryAnalysisResult, Version>> s_binaryAnalysisCache =
            new Dictionary<string, Tuple<BinaryAnalysisResult, Version>>();

#if CORECLR
        /// <summary>
        /// Analyze the module assembly to find out all cmdlets and aliases defined in that assembly
        /// </summary>
        /// <remarks>
        /// In CoreCLR, there is only one AppDomain, so we cannot spin up a new AppDomain to load the assembly and do analysis there.
        /// So we need to depend on System.Reflection.Metadata (Microsoft.Bcl.Metadata.dll) to analyze the metadata of the assembly.
        /// </remarks>
        private static BinaryAnalysisResult GetCmdletsFromBinaryModuleImplementation(string path, ManifestProcessingFlags manifestProcessingFlags, out Version assemblyVersion)
        {
            Tuple<BinaryAnalysisResult, Version> tuple;

            lock (s_lockObject)
            {
                s_binaryAnalysisCache.TryGetValue(path, out tuple);
            }

            if (tuple != null)
            {
                assemblyVersion = tuple.Item2;
                return tuple.Item1;
            }

            BinaryAnalysisResult analysisResult = PowerShellModuleAssemblyAnalyzer.AnalyzeModuleAssembly(path, out assemblyVersion);
            if (analysisResult == null && Path.IsPathRooted(path))
            {
                // If we couldn't load it from a file, try finding it in our probing path
                string assemblyFilename = Path.GetFileName(path);
                analysisResult = PowerShellModuleAssemblyAnalyzer.AnalyzeModuleAssembly(assemblyFilename, out assemblyVersion);
            }

            var resultToReturn = analysisResult ?? new BinaryAnalysisResult();

            lock (s_lockObject)
            {
                s_binaryAnalysisCache[path] = Tuple.Create(resultToReturn, assemblyVersion);
            }

            return resultToReturn;
        }
#else
        /// <summary>
        /// Analyze the module assembly to find out all cmdlets and aliases defined in that assembly
        /// </summary>
        private BinaryAnalysisResult GetCmdletsFromBinaryModuleImplementation(string path, ManifestProcessingFlags manifestProcessingFlags, out Version assemblyVersion)
        {
            Tuple<BinaryAnalysisResult, Version> tuple = null;

            lock (s_lockObject)
            {
                s_binaryAnalysisCache.TryGetValue(path, out tuple);
            }

            if (tuple != null)
            {
                assemblyVersion = tuple.Item2;
                return tuple.Item1;
            }

            assemblyVersion = new Version("0.0.0.0");

            bool cleanupModuleAnalysisAppDomain = false;
            AppDomain tempDomain = Context.AppDomainForModuleAnalysis;
            if (tempDomain == null)
            {
                cleanupModuleAnalysisAppDomain = Context.TakeResponsibilityForModuleAnalysisAppDomain();
                tempDomain = Context.AppDomainForModuleAnalysis = AppDomain.CreateDomain("ReflectionDomain");
            }

            try
            {
                // create temp appdomain if one is not passed in
                tempDomain.SetData("PathToProcess", path);
                tempDomain.SetData("IsModuleLoad", 0 != (manifestProcessingFlags & ManifestProcessingFlags.LoadElements));

                // reset DetectedCmdlets and AssemblyVersion from previous invocation
                tempDomain.SetData("DetectedCmdlets", null);
                tempDomain.SetData("DetectedAliases", null);
                tempDomain.SetData("AssemblyVersion", assemblyVersion);

                tempDomain.DoCallBack(AnalyzeSnapinDomainHelper);
                List<string> detectedCmdlets = (List<string>)tempDomain.GetData("DetectedCmdlets");
                List<Tuple<string, string>> detectedAliases = (List<Tuple<string, string>>)tempDomain.GetData("DetectedAliases");
                assemblyVersion = (Version)tempDomain.GetData("AssemblyVersion");

                if ((detectedCmdlets.Count == 0) && (System.IO.Path.IsPathRooted(path)))
                {
                    // If we couldn't load it from a file, try loading from the GAC
                    string assemblyname = Path.GetFileName(path);
                    BinaryAnalysisResult gacResult = GetCmdletsFromBinaryModuleImplementation(assemblyname, manifestProcessingFlags, out assemblyVersion);
                    detectedCmdlets = gacResult.DetectedCmdlets;
                    detectedAliases = gacResult.DetectedAliases;
                }

                BinaryAnalysisResult result = new BinaryAnalysisResult();
                result.DetectedCmdlets = detectedCmdlets;
                result.DetectedAliases = detectedAliases;

                lock (s_lockObject)
                {
                    s_binaryAnalysisCache[path] = Tuple.Create(result, assemblyVersion);
                }

                return result;
            }
            finally
            {
                if (cleanupModuleAnalysisAppDomain)
                {
                    Context.ReleaseResponsibilityForModuleAnalysisAppDomain();
                }
            }
        }


        private static void AnalyzeSnapinDomainHelper()
        {
            String path = (string)AppDomain.CurrentDomain.GetData("PathToProcess");
            bool isModuleLoad = (bool)AppDomain.CurrentDomain.GetData("IsModuleLoad");
            Dictionary<string, SessionStateCmdletEntry> cmdlets = null;
            Dictionary<string, List<SessionStateAliasEntry>> aliases = null;
            Dictionary<string, SessionStateProviderEntry> providers = null;
            string throwAwayHelpFile = null;
            Version assemblyVersion = new Version("0.0.0.0");

            try
            {
                Assembly assembly = null;

                try
                {
                    // If this is a fully-qualified search, load it from the file
                    if (Path.IsPathRooted(path))
                    {
                        assembly = InitialSessionState.LoadAssemblyFromFile(path);
                    }
                    else
                    {
                        // Otherwise, load it from the GAC
                        Exception ignored = null;
                        assembly = ExecutionContext.LoadAssembly(path, null, out ignored);
                    }
                    if (assembly != null)
                    {
                        assemblyVersion = GetAssemblyVersionNumber(assembly);
                    }
                }
                // Catch-all OK, analyzing user code.
                catch (Exception e)
                {
                    CommandProcessorBase.CheckForSevereException(e);
                }

                if (assembly != null)
                {
                    PSSnapInHelpers.AnalyzePSSnapInAssembly(assembly, assembly.Location, null, null, isModuleLoad, out cmdlets, out aliases, out providers, out throwAwayHelpFile);
                }
            }
            // Catch-all OK, analyzing user code.
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
            }

            List<string> detectedCmdlets = new List<string>();
            List<Tuple<string, string>> detectedAliases = new List<Tuple<string, string>>();

            if (cmdlets != null)
            {
                foreach (SessionStateCmdletEntry cmdlet in cmdlets.Values)
                {
                    detectedCmdlets.Add(cmdlet.Name);
                }
            }

            if (aliases != null)
            {
                foreach (List<SessionStateAliasEntry> aliasList in aliases.Values)
                {
                    foreach (SessionStateAliasEntry alias in aliasList)
                    {
                        detectedAliases.Add(new Tuple<string, string>(alias.Name, alias.Definition));
                    }
                }
            }

            AppDomain.CurrentDomain.SetData("DetectedCmdlets", detectedCmdlets);
            AppDomain.CurrentDomain.SetData("DetectedAliases", detectedAliases);
            AppDomain.CurrentDomain.SetData("AssemblyVersion", assemblyVersion);
        }
#endif

        // Analyzes a script module implementation for its exports.
        private static Dictionary<string, PSModuleInfo> s_scriptAnalysisCache = new Dictionary<string, PSModuleInfo>();

        private PSModuleInfo AnalyzeScriptFile(string filename, bool force, ExecutionContext context)
        {
            // We need to return a cloned version here.
            // This is because the Get-Module -List -All modifies the returned module info and we do not want the original one changed.
            PSModuleInfo module = null;

            lock (s_lockObject)
            {
                s_scriptAnalysisCache.TryGetValue(filename, out module);
            }

            if (module != null)
                return module.Clone();

            // fake/empty manifestInfo for processing in (!loadElements) mode
            module = new PSModuleInfo(filename, null, null);

            if (!force)
            {
                var exportedCommands = AnalysisCache.GetExportedCommands(filename, true, context);

                // If we have this info cached, return from the cache.
                if (exportedCommands != null)
                {
                    foreach (var pair in exportedCommands)
                    {
                        var commandName = pair.Key;
                        var commandType = pair.Value;

                        if ((commandType & CommandTypes.Alias) == CommandTypes.Alias)
                        {
                            module.AddDetectedAliasExport(commandName, null);
                        }
                        if ((commandType & CommandTypes.Workflow) == CommandTypes.Workflow)
                        {
                            module.AddDetectedWorkflowExport(commandName);
                        }
                        if ((commandType & CommandTypes.Function) == CommandTypes.Function)
                        {
                            module.AddDetectedFunctionExport(commandName);
                        }
                        if ((commandType & CommandTypes.Cmdlet) == CommandTypes.Cmdlet)
                        {
                            module.AddDetectedCmdletExport(commandName);
                        }
                        if ((commandType & CommandTypes.Configuration) == CommandTypes.Configuration)
                        {
                            module.AddDetectedFunctionExport(commandName);
                        }
                    }

                    lock (s_lockObject)
                    {
                        s_scriptAnalysisCache[filename] = module;
                    }

                    return module;
                }
            }

            // We don't have this cached, analyze the file.
            var scriptAnalysis = ScriptAnalysis.Analyze(filename, context);

            if (scriptAnalysis == null)
            {
                return module;
            }

            List<WildcardPattern> scriptAnalysisPatterns = new List<WildcardPattern>();
            foreach (string discoveredCommandFilter in scriptAnalysis.DiscoveredCommandFilters)
            {
                scriptAnalysisPatterns.Add(WildcardPattern.Get(discoveredCommandFilter, WildcardOptions.IgnoreCase));
            }

            // Add any directly discovered exports
            foreach (var command in scriptAnalysis.DiscoveredExports)
            {
                if (SessionStateUtilities.MatchesAnyWildcardPattern(command, scriptAnalysisPatterns, true))
                {
                    if (!HasInvalidCharacters(command.Replace("-", "")))
                    {
                        module.AddDetectedFunctionExport(command);
                    }
                }
            }

            // Add the discovered aliases
            foreach (var pair in scriptAnalysis.DiscoveredAliases)
            {
                var commandName = pair.Key;
                // These are already filtered
                if (!HasInvalidCharacters(commandName.Replace("-", "")))
                {
                    module.AddDetectedAliasExport(commandName, pair.Value);
                }
            }

            // Add the discovered exported types
            module.AddDetectedTypeExports(scriptAnalysis.DiscoveredClasses);

            // Add any files in PsScriptRoot if it added itself to the path
            if (scriptAnalysis.AddsSelfToPath)
            {
                string baseDirectory = System.IO.Path.GetDirectoryName(filename);

                try
                {
                    foreach (string item in System.IO.Directory.GetFiles(baseDirectory, "*.ps1"))
                    {
                        module.AddDetectedFunctionExport(Path.GetFileNameWithoutExtension(item));
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Consume this exception here
                }
            }

            // Process any referenced modules
            foreach (RequiredModuleInfo requiredModule in scriptAnalysis.DiscoveredModules)
            {
                string moduleToProcess = requiredModule.Name;
                List<PSModuleInfo> processedModules = new List<PSModuleInfo>();

                // If this has an extension, and it's a relative path,
                // then we need to ensure it's a fully-qualified path
                if ((moduleToProcess.IndexOfAny(Path.GetInvalidPathChars()) == -1) &&
                    Path.HasExtension(moduleToProcess) &&
                    (!Path.IsPathRooted(moduleToProcess)))
                {
                    string moduleDirectory = System.IO.Path.GetDirectoryName(filename);
                    moduleToProcess = Path.Combine(moduleDirectory, moduleToProcess);

                    PSModuleInfo fileBasedModule = CreateModuleInfoForGetModule(moduleToProcess, true);
                    if (fileBasedModule != null)
                    {
                        processedModules.Add(fileBasedModule);
                    }
                }
                else
                {
                    // This is a named module
                    processedModules.AddRange(GetModule(new string[] { moduleToProcess }, false, true));
                }

                if ((processedModules == null) || (processedModules.Count == 0))
                {
                    continue;
                }

                List<WildcardPattern> patterns = new List<WildcardPattern>();
                foreach (var discoveredCommandFilter in requiredModule.CommandsToPostFilter)
                {
                    patterns.Add(WildcardPattern.Get(discoveredCommandFilter, WildcardOptions.IgnoreCase));
                }

                foreach (PSModuleInfo processedModule in processedModules)
                {
                    foreach (string commandName in processedModule.ExportedFunctions.Keys)
                    {
                        if (SessionStateUtilities.MatchesAnyWildcardPattern(commandName, patterns, true) &&
                            SessionStateUtilities.MatchesAnyWildcardPattern(commandName, scriptAnalysisPatterns, true))
                        {
                            if (!HasInvalidCharacters(commandName.Replace("-", "")))
                            {
                                module.AddDetectedFunctionExport(commandName);
                            }
                        }
                    }

                    foreach (string commandName in processedModule.ExportedCmdlets.Keys)
                    {
                        if (SessionStateUtilities.MatchesAnyWildcardPattern(commandName, patterns, true) &&
                            SessionStateUtilities.MatchesAnyWildcardPattern(commandName, scriptAnalysisPatterns, true))
                        {
                            if (!HasInvalidCharacters(commandName.Replace("-", "")))
                            {
                                module.AddDetectedCmdletExport(commandName);
                            }
                        }
                    }

                    foreach (var pair in processedModule.ExportedAliases)
                    {
                        string commandName = pair.Key;
                        if (SessionStateUtilities.MatchesAnyWildcardPattern(commandName, patterns, true) &&
                            SessionStateUtilities.MatchesAnyWildcardPattern(commandName, scriptAnalysisPatterns, true))
                        {
                            module.AddDetectedAliasExport(commandName, pair.Value.Definition);
                        }
                    }
                }
            }

            // Cache the module's exported commands
            if (!module.HadErrorsLoading)
            {
                AnalysisCache.CacheModuleExports(module, context);
            }
            else
            {
                ModuleIntrinsics.Tracer.WriteLine("Caching skipped for {0} because it had errors while loading.", module.Name);
            }

            lock (s_lockObject)
            {
                s_scriptAnalysisCache[filename] = module;
            }

            return module;
        }

        /// <summary>
        /// Load a binary module. A binary module is an assembly that should contain cmdlets.
        /// </summary>
        /// <param name="trySnapInName">If true, then then the registered snapins will also be searched when loading.</param>
        /// <param name="moduleName">The name of the snapin or assembly to load.</param>
        /// <param name="fileName">The path to the assembly to load</param>
        /// <param name="assemblyToLoad">The assembly to load so no lookup need be done.</param>
        /// <param name="moduleBase">The module base to use for this module.</param>
        /// <param name="ss">
        /// The session state instance to use for this module. Normally binary modules don't have a session state
        /// instance, however when loaded through a module manifest with nested modules, it will have a session
        /// state instance to store the imported functions, aliases and variables.
        /// </param>
        /// <param name="options">The set of options that are used while importing a module</param>
        /// <param name="manifestProcessingFlags">The manifest processing flags to use when processing the module</param>
        /// <param name="loadTypes">load the types files mentioned in the snapin registration</param>
        /// <param name="loadFormats">Load the formst files mentioned in the snapin registration </param>
        /// <param name="prefix">Command name prefix</param>
        /// <param name="found">Sets this to true if an assembly was found.</param>
        /// <returns>THe module info object that was created...</returns>
        internal PSModuleInfo LoadBinaryModule(bool trySnapInName, string moduleName, string fileName,
            Assembly assemblyToLoad, string moduleBase, SessionState ss, ImportModuleOptions options,
            ManifestProcessingFlags manifestProcessingFlags, string prefix,
            bool loadTypes, bool loadFormats, out bool found)
        {
            return LoadBinaryModule(null, trySnapInName, moduleName, fileName, assemblyToLoad, moduleBase, ss, options,
                                    manifestProcessingFlags, prefix, loadTypes, loadFormats, out found, null, false);
        }

        /// <summary>
        /// Load a binary module. A binary module is an assembly that should contain cmdlets.
        /// </summary>
        /// <param name="parentModule">The parent module for which this module is a nested module</param>
        /// <param name="trySnapInName">If true, then then the registered snapins will also be searched when loading.</param>
        /// <param name="moduleName">The name of the snapin or assembly to load.</param>
        /// <param name="fileName">The path to the assembly to load</param>
        /// <param name="assemblyToLoad">The assembly to load so no lookup need be done.</param>
        /// <param name="moduleBase">The module base to use for this module.</param>
        /// <param name="ss">
        ///   The session state instance to use for this module. Normally binary modules don't have a session state
        ///   instance, however when loaded through a module manifest with nested modules, it will have a session
        ///   state instance to store the imported functions, aliases and variables.
        /// </param>
        /// <param name="options">The set of options that are used while importing a module</param>
        /// <param name="manifestProcessingFlags">The manifest processing flags to use when processing the module</param>
        /// <param name="prefix">Command name prefix</param>
        /// <param name="loadTypes">load the types files mentioned in the snapin registration</param>
        /// <param name="loadFormats">Load the formst files mentioned in the snapin registration </param>
        /// <param name="found">Sets this to true if an assembly was found.</param>
        /// <param name="shortModuleName">Short name for module.</param>
        /// <param name="disableFormatUpdates"></param>
        /// <returns>THe module info object that was created...</returns>
        internal PSModuleInfo LoadBinaryModule(PSModuleInfo parentModule, bool trySnapInName, string moduleName, string fileName, Assembly assemblyToLoad, string moduleBase, SessionState ss, ImportModuleOptions options, ManifestProcessingFlags manifestProcessingFlags, string prefix, bool loadTypes, bool loadFormats, out bool found, string shortModuleName, bool disableFormatUpdates)
        {
            PSModuleInfo module = null;

            if (String.IsNullOrEmpty(moduleName) && String.IsNullOrEmpty(fileName) && assemblyToLoad == null)
                throw PSTraceSource.NewArgumentNullException("moduleName,fileName,assemblyToLoad");

            // Load the dll and process any cmdlets it might contain...
            InitialSessionState iss = InitialSessionState.Create();
            List<string> detectedCmdlets = null;
            List<Tuple<string, string>> detectedAliases = null;
            Assembly assembly = null;
            Exception error = null;
            bool importSuccessful = false;
            string modulePath = string.Empty;
            Version assemblyVersion = new Version(0, 0, 0, 0);
            var importingModule = 0 != (manifestProcessingFlags & ManifestProcessingFlags.LoadElements);

            // See if we're loading a straight assembly...
            if (assemblyToLoad != null)
            {
                // Figure out what to use for a module path...
                if (!string.IsNullOrEmpty(fileName))
                {
                    modulePath = fileName;
                }
                else
                {
                    modulePath = assemblyToLoad.Location;
                }

                // And what to use for a module name...
                if (string.IsNullOrEmpty(moduleName))
                {
                    moduleName = "dynamic_code_module_" + assemblyToLoad.GetName();
                }

                if (importingModule)
                {
                    // Passing module as a parameter here so that the providers can have the module property populated.
                    // For engine providers, the module should point to top-level module name
                    // For FileSystem, the module is Microsoft.PowerShell.Core and not System.Management.Automation

                    if (parentModule != null && InitialSessionState.IsEngineModule(parentModule.Name))
                    {
                        iss.ImportCmdletsFromAssembly(assemblyToLoad, parentModule);
                    }
                    else
                    {
                        iss.ImportCmdletsFromAssembly(assemblyToLoad, null);
                    }
                }

                assemblyVersion = GetAssemblyVersionNumber(assemblyToLoad);
                assembly = assemblyToLoad;
            }
            else
            {
                // Avoid trying to import a PowerShell assembly as Snapin as it results in PSArgumentException
                if ((moduleName != null) && Utils.IsPowerShellAssembly(moduleName))
                {
                    trySnapInName = false;
                }

                if (trySnapInName && PSSnapInInfo.IsPSSnapinIdValid(moduleName))
                {
                    PSSnapInInfo snapin = null;

#if !CORECLR
                    // Avoid trying to load SnapIns with Import-Module
                    PSSnapInException warning;
                    try
                    {
                        if (importingModule)
                        {
                            snapin = iss.ImportPSSnapIn(moduleName, out warning);
                        }
                    }
                    catch (PSArgumentException)
                    {
                        //BUGBUG - brucepay - probably want to have a verbose message here...
                        ;
                    }
#endif

                    if (snapin != null)
                    {
                        importSuccessful = true;
                        if (string.IsNullOrEmpty(fileName))
                            modulePath = snapin.AbsoluteModulePath;
                        else
                            modulePath = fileName;
                        assemblyVersion = snapin.Version;
                        // If we're not supposed to load the types files from the snapin
                        // clear the iss member
                        if (!loadTypes)
                        {
                            iss.Types.Reset();
                        }
                        // If we're not supposed to load the format files from the snapin,
                        // clear the iss member
                        if (!loadFormats)
                        {
                            iss.Formats.Reset();
                        }
                        foreach (var a in ClrFacade.GetAssemblies())
                        {
                            if (a.GetName().FullName.Equals(snapin.AssemblyName, StringComparison.Ordinal))
                            {
                                assembly = a;
                                break;
                            }
                        }
                    }
                }

                if (importSuccessful == false)
                {
                    if (importingModule)
                    {
                        assembly = Context.AddAssembly(moduleName, fileName, out error);

                        if (assembly == null)
                        {
                            if (error != null)
                                throw error;

                            found = false;
                            return null;
                        }

                        assemblyVersion = GetAssemblyVersionNumber(assembly);

                        if (string.IsNullOrEmpty(fileName))
                            modulePath = assembly.Location;
                        else
                            modulePath = fileName;

                        // Passing module as a parameter here so that the providers can have the module property populated.
                        // For engine providers, the module should point to top-level module name
                        // For FileSystem, the module is Microsoft.PowerShell.Core and not System.Management.Automation

                        if (parentModule != null && InitialSessionState.IsEngineModule(parentModule.Name))
                        {
                            iss.ImportCmdletsFromAssembly(assembly, parentModule);
                        }
                        else
                        {
                            iss.ImportCmdletsFromAssembly(assembly, null);
                        }
                    }
                    else
                    {
                        string binaryPath = fileName;
                        modulePath = fileName;
                        if (binaryPath == null)
                        {
                            binaryPath = System.IO.Path.Combine(moduleBase, moduleName);
                        }

                        BinaryAnalysisResult analysisResult = GetCmdletsFromBinaryModuleImplementation(binaryPath, manifestProcessingFlags, out assemblyVersion);
                        detectedCmdlets = analysisResult.DetectedCmdlets;
                        detectedAliases = analysisResult.DetectedAliases;
                    }
                }
            }

            found = true;
            if (string.IsNullOrEmpty(shortModuleName))
                module = new PSModuleInfo(moduleName, modulePath, Context, ss);
            else
                module = new PSModuleInfo(shortModuleName, modulePath, Context, ss);

            module.SetModuleType(ModuleType.Binary);
            module.SetModuleBase(moduleBase);
            module.SetVersion(assemblyVersion);
            module.ImplementingAssembly = assemblyToLoad ?? assembly;

            if (importingModule)
            {
                SetModuleLoggingInformation(module);
            }

            // Add the types table entries

            List<string> typesFileNames = new List<string>();
            foreach (SessionStateTypeEntry sste in iss.Types)
            {
                typesFileNames.Add(sste.FileName);
            }
            if (typesFileNames.Count > 0)
            {
                module.SetExportedTypeFiles(new ReadOnlyCollection<string>(typesFileNames));
            }

            // Add the format file entries

            List<string> formatsFileNames = new List<string>();
            foreach (SessionStateFormatEntry ssfe in iss.Formats)
            {
                formatsFileNames.Add(ssfe.FileName);
            }
            if (formatsFileNames.Count > 0)
            {
                module.SetExportedFormatFiles(new ReadOnlyCollection<string>(formatsFileNames));
            }

            // Add the module info the providers...
            foreach (SessionStateProviderEntry sspe in iss.Providers)
            {
                // For engine providers, the module should point to top-level module name
                // For FileSystem, the module is Microsoft.PowerShell.Core and not System.Management.Automation
                if (parentModule != null && InitialSessionState.IsEngineModule(parentModule.Name))
                {
                    sspe.SetModule(parentModule);
                }
                else
                {
                    sspe.SetModule(module);
                }
            }

            // Add all of the exported cmdlets to the module object...
            if (iss.Commands != null)
            {
                foreach (SessionStateCommandEntry commandEntry in iss.Commands)
                {
                    commandEntry.SetModule(module);

                    // A binary module can only directly export cmdlets, so cmdletEntry should never be null.
                    // With nested modules in a manifest, there may be a session state attached to
                    // this module in which case we add the exported cmdlets to the existing list.
                    SessionStateCmdletEntry cmdletEntry = commandEntry as SessionStateCmdletEntry;
                    SessionStateAliasEntry aliasEntry = null;
                    if (cmdletEntry == null)
                    {
                        aliasEntry = commandEntry as SessionStateAliasEntry;
                    }
                    Dbg.Assert((cmdletEntry != null || aliasEntry != null), "When importing a binary module, the commands entry should only have cmdlets/aliases in it");
                    if (ss != null)
                    {
                        if (cmdletEntry != null)
                        {
                            ss.Internal.ExportedCmdlets.Add(CommandDiscovery.NewCmdletInfo(cmdletEntry, this.Context));
                        }
                        else if (aliasEntry != null)
                        {
                            ss.Internal.ExportedAliases.Add(CommandDiscovery.NewAliasInfo(aliasEntry, this.Context));
                        }
                    }
                    else
                    {
                        // If there is no session state, we need to attach the entry to the module instead
                        // of the session state.
                        if (cmdletEntry != null)
                        {
                            module.AddExportedCmdlet(CommandDiscovery.NewCmdletInfo(cmdletEntry, this.Context));
                        }
                        else if (aliasEntry != null)
                        {
                            module.AddExportedAlias(CommandDiscovery.NewAliasInfo(aliasEntry, this.Context));
                        }
                    }
                }
            }

            // If there were any cmdlets that were detected, add those.
            if (detectedCmdlets != null)
            {
                foreach (string detectedCmdlet in detectedCmdlets)
                {
                    module.AddDetectedCmdletExport(detectedCmdlet);
                }
            }

            // If there were any aliases that were detected, add those.
            if (detectedAliases != null)
            {
                foreach (Tuple<string, string> detectedAlias in detectedAliases)
                {
                    module.AddDetectedAliasExport(detectedAlias.Item1, detectedAlias.Item2);
                }
            }

            // If any command patterns where specified, then only import the
            // commands matching the patterns...
            if (BaseCmdletPatterns != null)
            {
                InitialSessionStateEntryCollection<SessionStateCommandEntry> commands = iss.Commands;
                // Remove all of the command entries that aren't matched by a pattern
                // in the pattern list...
                for (int i = commands.Count - 1; i >= 0; i--)
                {
                    SessionStateCommandEntry e = commands[i];
                    if (e == null)
                        continue;
                    string name = e.Name;
                    if (string.IsNullOrEmpty(name))
                        continue;
                    if (!SessionStateUtilities.MatchesAnyWildcardPattern(name, BaseCmdletPatterns, false))
                    {
                        commands.RemoveItem(i);
                    }
                }
            }

            // use the -Prefix parameter to change the names of imported commands
            foreach (SessionStateCommandEntry commandEntry in iss.Commands)
            {
                commandEntry.Name = AddPrefixToCommandName(commandEntry.Name, prefix);
            }

            SessionStateInternal oldSessionState = Context.EngineSessionState;

            if (importingModule)
            {
                try
                {
                    // If we have a session state instance, set it to be the engine sessionstate
                    // for the duration of the bind.
                    if (ss != null)
                    {
                        Context.EngineSessionState = ss.Internal;
                    }
                    if (disableFormatUpdates)
                        iss.DisableFormatUpdates = true;

                    // Load the cmdlets and providers, bound to the new module...
                    iss.Bind(Context, /*updateOnly*/ true, module, options.NoClobber, options.Local);

                    // Scan all of the types in the assembly to register JobSourceAdapters.
                    IEnumerable<Type> allTypes = new Type[] { };
                    if (assembly != null)
                    {
                        allTypes = assembly.ExportedTypes;
                    }
                    else if (assemblyToLoad != null)
                    {
                        allTypes = assemblyToLoad.ExportedTypes;
                    }
                    foreach (Type type in allTypes)
                    {
                        // If it derives from JobSourceAdapter and it's not already registered, register it...
                        // Add the second check since SMA could also be loaded via Import-Module
                        if (typeof(JobSourceAdapter).IsAssignableFrom(type) && typeof(JobSourceAdapter) != type)
                        {
                            if (!JobManager.IsRegistered(type.Name))
                            {
                                JobManager.RegisterJobSourceAdapter(type);
                            }
                        }
                    }
                }
                finally
                {
                    Context.EngineSessionState = oldSessionState;
                }
            }

            // WriteVerbose all of the imported cmdlets ...
            string snapInPrefix = module.Name + "\\";
            bool checkVerb = !BaseDisableNameChecking;
            bool checkNoun = !BaseDisableNameChecking;
            foreach (SessionStateCommandEntry ssce in iss.Commands)
            {
                if (ssce._isImported)
                {
                    try
                    {
                        if (ssce is SessionStateCmdletEntry || ssce is SessionStateFunctionEntry)
                        {
                            ValidateCommandName(this, ssce.Name, module.Name, ref checkVerb, ref checkNoun);
                        }

                        // Lookup using the module qualified name....
                        string name = snapInPrefix + ssce.Name;
                        CommandInvocationIntrinsics.GetCmdlet(name, Context);
                    }
                    catch (CommandNotFoundException cnfe)
                    {
                        // Handle the errors generated by duplicate commands...
                        WriteError(cnfe.ErrorRecord);
                    }

                    // WIN8: 561104 Importing PSWorkflow module tells wrongly that their is a cmdlet Import-PSWorkflow, when there is none
                    // Skipping the verbose message when cmdlet name is import-psworkflow
                    //
                    if (!String.Equals(ssce.Name, "import-psworkflow", StringComparison.OrdinalIgnoreCase))
                    {
                        string message = StringUtil.Format(ssce.CommandType == CommandTypes.Alias ? Modules.ImportingAlias : Modules.ImportingCmdlet, ssce.Name);

                        WriteVerbose(message);
                    }
                }
                else
                {
                    // The verbose output for Import-Module -NoClobber should state which members were skipped due to conflicts with existing names in the caller's environment.
                    String message = StringUtil.Format(Modules.ImportModuleNoClobberForCmdlet, ssce.Name);
                    WriteVerbose(message);
                }
            }

            // Add the module being imported to the list of child modules for the current module
            // if it hasn't already been added...
            if (importingModule)
            {
                AddModuleToModuleTables(this.Context, this.TargetSessionState.Internal, module);
            }

            return module;
        }

        private static Version GetAssemblyVersionNumber(Assembly assemblyToLoad)
        {
            Version assemblyVersion;

            // Get the assembly version...
            try
            {
                AssemblyName asn = assemblyToLoad.GetName();
                assemblyVersion = asn.Version;
            }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                assemblyVersion = new Version(0, 0);
            }
            return assemblyVersion;
        }

        internal static string AddPrefixToCommandName(string commandName, string prefix)
        {
            Dbg.Assert(commandName != null, "Caller should verify that commandName argument != null");

            if (string.IsNullOrEmpty(prefix))
                return commandName;

            string verb;
            string noun;
            if (CmdletInfo.SplitCmdletName(commandName, out verb, out noun))
            {
                commandName = verb + "-" + prefix + noun;
            }
            else
            {
                commandName = prefix + commandName;
            }
            return commandName;
        }

        /// <summary>
        /// Removes prefix from a command name and returns the command name
        /// </summary>
        /// <param name="commandName">The command name from which the prefix needs to be removed.</param>
        /// <param name="prefix">The string containing the prefix.</param>
        /// <returns>The command name without the prefix.</returns>
        internal static string RemovePrefixFromCommandName(string commandName, string prefix)
        {
            Dbg.Assert(commandName != null, "Caller should verify that commandName argument != null");
            Dbg.Assert(prefix != null, "Caller should verify that prefix argument != null");

            string result = commandName;

            if (string.IsNullOrEmpty(prefix))
                return result;

            string verb;
            string noun;
            if (CmdletInfo.SplitCmdletName(commandName, out verb, out noun))
            {
                if (noun.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    string originalNoun = noun.Substring(prefix.Length, noun.Length - prefix.Length);
                    result = verb + "-" + originalNoun;
                }
            }
            else
            {
                if (commandName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    result = commandName.Substring(prefix.Length, commandName.Length - prefix.Length);
                }
            }

            return result;
        }

        internal static bool IsPrefixedCommand(CommandInfo commandInfo)
        {
            Dbg.Assert(commandInfo != null, "Caller should verify that commandInfo is not null");
            Dbg.Assert(!String.IsNullOrEmpty(commandInfo.Prefix), "Caller should verify that the commandInfo has prefix");

            string verb, noun;
            bool isPrefixed = CmdletInfo.SplitCmdletName(commandInfo.Name, out verb, out noun)
                             ? noun.StartsWith(commandInfo.Prefix, StringComparison.OrdinalIgnoreCase)
                             : commandInfo.Name.StartsWith(commandInfo.Prefix, StringComparison.OrdinalIgnoreCase);

            return isPrefixed;
        }

        internal static void AddModuleToModuleTables(ExecutionContext context, SessionStateInternal targetSessionState, PSModuleInfo module)
        {
            Dbg.Assert(context != null, "Caller should verify that context != null");
            Dbg.Assert(targetSessionState != null, "Caller should verify that targetSessionState != null");
            Dbg.Assert(module != null, "Caller should verify that module != null");

            // if the module path is empty (assembly module in memory), we add the modulename as key
            string moduleTableKey;
            if (module.Path != "")
            {
                moduleTableKey = module.Path;
            }
            else
            {
                moduleTableKey = module.Name;
            }
            if (!context.Modules.ModuleTable.ContainsKey(moduleTableKey))
            {
                context.Modules.ModuleTable.Add(moduleTableKey, module);
            }
            if (context.previousModuleImported.ContainsKey(module.Name))
            {
                context.previousModuleImported.Remove(module.Name);
            }
            context.previousModuleImported.Add(module.Name, module.Path);

            if (!targetSessionState.ModuleTable.ContainsKey(moduleTableKey))
            {
                targetSessionState.ModuleTable.Add(moduleTableKey, module);
                targetSessionState.ModuleTableKeys.Add(moduleTableKey);
            }

            if (targetSessionState.Module != null)
            {
                targetSessionState.Module.AddNestedModule(module);
            }
        }

        /// <summary>
        /// Import the script-level functions from one session state to another, calling
        /// WriteVerbose for each imported member...
        /// </summary>
        /// <param name="sourceModule">The session state instance to use as the source of the functions</param>
        /// <param name="prefix">Command name prefix</param>
        protected internal void ImportModuleMembers(PSModuleInfo sourceModule, string prefix)
        {
            ImportModuleOptions importModuleOptions = new ImportModuleOptions();
            ImportModuleMembers(
                this,
                this.TargetSessionState.Internal,
                sourceModule,
                prefix,
                this.BaseFunctionPatterns,
                this.BaseCmdletPatterns,
                this.BaseVariablePatterns,
                this.BaseAliasPatterns,
                importModuleOptions);
        }

        /// <summary>
        /// Import the script-level functions from one session state to another, calling
        /// WriteVerbose for each imported member...
        /// </summary>
        /// <param name="sourceModule">The session state instance to use as the source of the functions</param>
        /// <param name="prefix">Command name prefix</param>
        /// <param name="options">The set of options that are used while importing a module</param>
        protected internal void ImportModuleMembers(PSModuleInfo sourceModule, string prefix, ImportModuleOptions options)
        {
            ImportModuleMembers(
                this,
                this.TargetSessionState.Internal,
                sourceModule,
                prefix,
                this.BaseFunctionPatterns,
                this.BaseCmdletPatterns,
                this.BaseVariablePatterns,
                this.BaseAliasPatterns,
                options);
        }

        internal static void ImportModuleMembers(
            ModuleCmdletBase cmdlet,
            SessionStateInternal targetSessionState,
            PSModuleInfo sourceModule,
            string prefix,
            List<WildcardPattern> functionPatterns,
            List<WildcardPattern> cmdletPatterns,
            List<WildcardPattern> variablePatterns,
            List<WildcardPattern> aliasPatterns,
            ImportModuleOptions options)
        {
            if (sourceModule == null)
                throw PSTraceSource.NewArgumentNullException("sourceModule");

            bool isImportModulePrivate = cmdlet.CommandInfo.Visibility == SessionStateEntryVisibility.Private ||
                targetSessionState.DefaultCommandVisibility == SessionStateEntryVisibility.Private;
            bool usePrefix = !string.IsNullOrEmpty(prefix);

            bool checkVerb = !cmdlet.BaseDisableNameChecking;
            bool checkNoun = !cmdlet.BaseDisableNameChecking;

            // Add the module being imported to the list of child modules for the current module
            // if it hasn't already been added...
            // TODO/FIXME: we should be calling AddModuleToModuleTables here but we can't
            //             because of the inconsistency with which dynamic modules should
            //             or should not be included in the global modules table
            if (targetSessionState.Module != null)
            {
                bool present = false;
                foreach (PSModuleInfo m in targetSessionState.Module.NestedModules)
                {
                    if (m.Path.Equals(sourceModule.Path, StringComparison.OrdinalIgnoreCase))
                        present = true;
                }
                if (!present)
                {
                    targetSessionState.Module.AddNestedModule(sourceModule);
                }
            }

            SessionStateInternal source = null;
            if (sourceModule.SessionState != null)
            {
                source = sourceModule.SessionState.Internal;
            }

            // True if none of -function, -cmdlet, -variable or -alias are specified
            bool noPatternsSpecified = functionPatterns == null &&
                variablePatterns == null && aliasPatterns == null && cmdletPatterns == null;

            // mapping from original to prefixed names (prefixed using -Prefix parameter of the cmdlet)
            // used to modify AliasInfo.Definition if necessary
            Dictionary<string, string> original2prefixedName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // We always load the cmdlets even if there is no session state which is
            // the case with a binary module.
            string message = null;
            foreach (CmdletInfo cmdletToImport in sourceModule.CompiledExports)
            {
                if (SessionStateUtilities.MatchesAnyWildcardPattern(cmdletToImport.Name, cmdletPatterns, noPatternsSpecified))
                {
                    if (options.NoClobber && CommandFound(cmdletToImport.Name, targetSessionState))
                    {
                        // The verbose output for Import-Module -NoClobber should state which members were skipped due to conflicts with existing names in the caller's environment.
                        message = StringUtil.Format(Modules.ImportModuleNoClobberForCmdlet, cmdletToImport.Name);
                        cmdlet.WriteVerbose(message);
                        continue;
                    }
                    CmdletInfo prefixedCmdlet = new CmdletInfo(
                        AddPrefixToCommandName(cmdletToImport.Name, prefix),
                        cmdletToImport.ImplementingType,
                        cmdletToImport.HelpFile,
                        cmdletToImport.PSSnapIn,
                        cmdlet.Context);
                    SetCommandVisibility(isImportModulePrivate, prefixedCmdlet);
                    prefixedCmdlet.Module = sourceModule;
                    if (usePrefix)
                    {
                        original2prefixedName.Add(cmdletToImport.Name, prefixedCmdlet.Name);
                        cmdletToImport.Prefix = prefix;
                        prefixedCmdlet.Prefix = prefix;
                    }

                    ValidateCommandName(cmdlet, prefixedCmdlet.Name, sourceModule.Name, ref checkVerb, ref checkNoun);

                    var scope = options.Local ? targetSessionState.CurrentScope : targetSessionState.ModuleScope;
                    scope.AddCmdletToCache(prefixedCmdlet.Name, prefixedCmdlet, CommandOrigin.Internal, targetSessionState.ExecutionContext);

                    cmdletToImport.IsImported = true;

                    // Write directly into the local cmdlet cache table...
                    message = StringUtil.Format(Modules.ImportingCmdlet, prefixedCmdlet.Name);
                    cmdlet.WriteVerbose(message);
                }
            }

            foreach (AliasInfo aliasToImport in sourceModule.CompiledAliasExports)
            {
                if (SessionStateUtilities.MatchesAnyWildcardPattern(aliasToImport.Name, aliasPatterns, noPatternsSpecified))
                {
                    string prefixedAliasName = AddPrefixToCommandName(aliasToImport.Name, prefix);
                    string prefixedAliasDefinition;
                    if (!usePrefix || !original2prefixedName.TryGetValue(aliasToImport.Definition, out prefixedAliasDefinition))
                    {
                        prefixedAliasDefinition = aliasToImport.Definition;
                    }

                    if (options.NoClobber && CommandFound(prefixedAliasName, targetSessionState))
                    {
                        // The verbose output for Import-Module -NoClobber should state which members were skipped due to conflicts with existing names in the caller's environment.
                        message = StringUtil.Format(Modules.ImportModuleNoClobberForAlias, prefixedAliasName);
                        cmdlet.WriteVerbose(message);
                        continue;
                    }

                    var prefixedAlias = new AliasInfo(
                        prefixedAliasName,
                        prefixedAliasDefinition,
                        cmdlet.Context);
                    SetCommandVisibility(isImportModulePrivate, prefixedAlias);
                    prefixedAlias.Module = sourceModule;
                    if (usePrefix)
                    {
                        if (!original2prefixedName.ContainsKey(aliasToImport.Name))
                        {
                            original2prefixedName.Add(aliasToImport.Name, prefixedAlias.Name);
                        }

                        aliasToImport.Prefix = prefix;
                        prefixedAlias.Prefix = prefix;
                    }

                    var scope = options.Local ? targetSessionState.CurrentScope : targetSessionState.ModuleScope;
                    scope.SetAliasItem(prefixedAlias, false);

                    aliasToImport.IsImported = true;

                    message = StringUtil.Format(Modules.ImportingAlias, prefixedAlias.Name);
                    cmdlet.WriteVerbose(message);
                }
            }

            // Only process functions, variables and aliases if there is a session state
            // associated with the source module.
            if (source != null)
            {
                foreach (FunctionInfo func in sourceModule.ExportedFunctions.Values)
                {
                    ImportFunctionsOrWorkflows(func, targetSessionState, sourceModule, functionPatterns, noPatternsSpecified, prefix, options, usePrefix, ref checkVerb, ref checkNoun, original2prefixedName, cmdlet, isImportModulePrivate, isFunction: true);
                }

                foreach (FunctionInfo func in sourceModule.ExportedWorkflows.Values)
                {
                    ImportFunctionsOrWorkflows(func, targetSessionState, sourceModule, functionPatterns, noPatternsSpecified, prefix, options, usePrefix, ref checkVerb, ref checkNoun, original2prefixedName, cmdlet, isImportModulePrivate, isFunction: false);
                }

                // Import any exported variables...
                foreach (PSVariable v in sourceModule.ExportedVariables.Values)
                {
                    if (SessionStateUtilities.MatchesAnyWildcardPattern(v.Name, variablePatterns, noPatternsSpecified))
                    {
                        if (options.NoClobber && (targetSessionState.ModuleScope.GetVariable(v.Name) != null))
                        {
                            // The verbose output for Import-Module -NoClobber should state which members were skipped due to conflicts with existing names in the caller's environment.
                            message = StringUtil.Format(Modules.ImportModuleNoClobberForVariable, v.Name);
                            cmdlet.WriteVerbose(message);
                            continue;
                        }
                        // Set the module on the variable...
                        v.SetModule(sourceModule);

                        var scope = options.Local ? targetSessionState.CurrentScope : targetSessionState.ModuleScope;
                        PSVariable newVariable = scope.NewVariable(v, true, source);

                        if (isImportModulePrivate)
                        {
                            newVariable.Visibility = SessionStateEntryVisibility.Private;
                        }

                        message = StringUtil.Format(Modules.ImportingVariable, v.Name);
                        cmdlet.WriteVerbose(message);
                    }
                }

                // Import any exported aliases...
                foreach (AliasInfo ai in sourceModule.ExportedAliases.Values)
                {
                    if (SessionStateUtilities.MatchesAnyWildcardPattern(ai.Name, aliasPatterns, noPatternsSpecified))
                    {
                        string prefixedAliasName = AddPrefixToCommandName(ai.Name, prefix);
                        string prefixedAliasDefinition;
                        if (!usePrefix || !original2prefixedName.TryGetValue(ai.Definition, out prefixedAliasDefinition))
                        {
                            prefixedAliasDefinition = ai.Definition;
                        }

                        if (options.NoClobber && CommandFound(prefixedAliasName, targetSessionState))
                        {
                            // The verbose output for Import-Module -NoClobber should state which members were skipped due to conflicts with existing names in the caller's environment.
                            message = StringUtil.Format(Modules.ImportModuleNoClobberForAlias, prefixedAliasName);
                            cmdlet.WriteVerbose(message);
                            continue;
                        }

                        AliasInfo prefixedAlias = new AliasInfo(
                            prefixedAliasName,
                            prefixedAliasDefinition,
                            cmdlet.Context);
                        SetCommandVisibility(isImportModulePrivate, prefixedAlias);
                        prefixedAlias.Module = sourceModule;

                        if (usePrefix)
                        {
                            if (!original2prefixedName.ContainsKey(ai.Name))
                            {
                                original2prefixedName.Add(ai.Name, prefixedAlias.Name);
                            }

                            ai.Prefix = prefix;
                            prefixedAlias.Prefix = prefix;
                        }

                        var scope = options.Local ? targetSessionState.CurrentScope : targetSessionState.ModuleScope;
                        scope.SetAliasItem(prefixedAlias, false, CommandOrigin.Internal);

                        ai.IsImported = true;

                        message = StringUtil.Format(Modules.ImportingAlias, prefixedAlias.Name);
                        cmdlet.WriteVerbose(message);
                    }
                }
            }
        }

        private static void ImportFunctionsOrWorkflows(FunctionInfo func, SessionStateInternal targetSessionState, PSModuleInfo sourceModule, List<WildcardPattern> functionPatterns, bool noPatternsSpecified,
            string prefix, ImportModuleOptions options, bool usePrefix, ref bool checkVerb, ref bool checkNoun, Dictionary<string, string> original2prefixedName, ModuleCmdletBase cmdlet, bool isImportModulePrivate, bool isFunction)
        {
            string message = null;
            if (SessionStateUtilities.MatchesAnyWildcardPattern(func.Name, functionPatterns, noPatternsSpecified))
            {
                string prefixedName = AddPrefixToCommandName(func.Name, prefix);

                if (options.NoClobber && CommandFound(prefixedName, targetSessionState))
                {
                    // The verbose output for Import-Module -NoClobber should state which members were skipped due to conflicts with existing names in the caller's environment.
                    message = StringUtil.Format(Modules.ImportModuleNoClobberForFunction, func.Name);
                    cmdlet.WriteVerbose(message);
                    return;
                }

                var scope = options.Local ? targetSessionState.CurrentScope : targetSessionState.ModuleScope;
                // Write directly into the function table...
                FunctionInfo functionInfo = scope.SetFunction(
                    prefixedName,
                    func.ScriptBlock,
                    func,
                    false,
                    CommandOrigin.Internal,
                    targetSessionState.ExecutionContext);

                SetCommandVisibility(isImportModulePrivate, functionInfo);
                functionInfo.Module = sourceModule;

                func.IsImported = true;
                if (usePrefix)
                {
                    original2prefixedName.Add(func.Name, prefixedName);
                    func.Prefix = prefix;
                    functionInfo.Prefix = prefix;
                }

                ValidateCommandName(cmdlet, functionInfo.Name, sourceModule.Name, ref checkNoun, ref checkVerb);

                if (func.CommandType == CommandTypes.Workflow)
                {
                    message = StringUtil.Format(Modules.ImportingWorkflow, prefixedName);
                }
                else
                {
                    message = StringUtil.Format(Modules.ImportingFunction, prefixedName);
                }
                cmdlet.WriteVerbose(message);
            }
        }


        private static void SetCommandVisibility(bool isImportModulePrivate, CommandInfo command)
        {
            if (isImportModulePrivate)
            {
                command.Visibility = SessionStateEntryVisibility.Private;
            }
        }

        internal static bool CommandFound(string commandName, SessionStateInternal sessionStateInternal)
        {
            EventHandler<CommandLookupEventArgs> oldCommandNotFoundAction =
                sessionStateInternal.ExecutionContext.EngineIntrinsics.InvokeCommand.CommandNotFoundAction;
            try
            {
                sessionStateInternal.ExecutionContext.EngineIntrinsics.InvokeCommand.CommandNotFoundAction = null;
                CommandSearcher searcher = new CommandSearcher(
                            commandName,
                            SearchResolutionOptions.CommandNameIsPattern | SearchResolutionOptions.ResolveAliasPatterns | SearchResolutionOptions.ResolveFunctionPatterns,
                            CommandTypes.Alias | CommandTypes.Function | CommandTypes.Cmdlet | CommandTypes.Configuration,
                            sessionStateInternal.ExecutionContext);

                if (!searcher.MoveNext())
                {
                    return false;
                }
                return true;
            }
            finally
            {
                sessionStateInternal.ExecutionContext.EngineIntrinsics.InvokeCommand.CommandNotFoundAction = oldCommandNotFoundAction;
            }
        }

        private static bool HasInvalidCharacters(string commandName)
        {
            foreach (char c in commandName)
            {
                switch (c)
                {
                    case '#':
                    case ',':
                    case '(':
                    case ')':
                    case '{':
                    case '}':
                    case '[':
                    case ']':
                    case '&':
                    case '-':
                    case '/':
                    case '\\':
                    case '$':
                    case '^':
                    case ';':
                    case ':':
                    case '"':
                    case '\'':
                    case '<':
                    case '>':
                    case '|':
                    case '?':
                    case '@':
                    case '`':
                    case '*':
                    case '%':
                    case '+':
                    case '=':
                    case '~':
                        return true;
                }
            }

            return false;
        }

        private static void ValidateCommandName(ModuleCmdletBase cmdlet,
                                                string commandName,
                                                string moduleName,
                                                ref bool checkVerb,
                                                ref bool checkNoun)
        {
            string verb;
            string noun;
            string message;

            if (!CmdletInfo.SplitCmdletName(commandName, out verb, out noun))
                return;

            if (!Verbs.IsStandard(verb))
            {
                // A hack for Sort-Object and Tee-Object as they have non-standard verbs
                // With engine dlls being loaded as modules, introducing the additional check to avoid warning message
                if (!commandName.Equals("Sort-Object", StringComparison.OrdinalIgnoreCase) && !commandName.Equals("Tee-Object", StringComparison.OrdinalIgnoreCase))
                {
                    if (checkVerb)
                    {
                        checkVerb = false;
                        message = StringUtil.Format(Modules.ImportingNonStandardVerb, moduleName);
                        cmdlet.WriteWarning(message);
                    }
                    string[] alternates = Verbs.SuggestedAlternates(verb);
                    if (alternates == null)
                    {
                        message = StringUtil.Format(Modules.ImportingNonStandardVerbVerbose, commandName, moduleName);
                        cmdlet.WriteVerbose(message);
                    }
                    else
                    {
                        var suggestions = string.Join(CultureInfo.CurrentUICulture.TextInfo.ListSeparator, alternates);
                        message = StringUtil.Format(Modules.ImportingNonStandardVerbVerboseSuggestion, commandName, suggestions, moduleName);
                        cmdlet.WriteVerbose(message);
                    }
                }
            }

            if (HasInvalidCharacters(noun))
            {
                if (checkNoun)
                {
                    message = Modules.ImportingNonStandardNoun;
                    cmdlet.WriteWarning(message);
                    checkNoun = false;
                }
                message = StringUtil.Format(Modules.ImportingNonStandardNounVerbose, commandName, moduleName);
                cmdlet.WriteVerbose(message);
                return;
            }
        }

        /// <summary>
        /// Search a PSSnapin with the specified name
        /// </summary>
        internal static PSSnapInInfo GetEngineSnapIn(ExecutionContext context, string name)
        {
            HashSet<PSSnapInInfo> snapinSet = new HashSet<PSSnapInInfo>();
            List<CmdletInfo> cmdlets = context.SessionState.InvokeCommand.GetCmdlets();
            foreach (CmdletInfo cmdlet in cmdlets)
            {
                PSSnapInInfo snapin = cmdlet.PSSnapIn;
                if (snapin != null && !snapinSet.Contains(snapin))
                    snapinSet.Add(snapin);
            }

            foreach (PSSnapInInfo snapin in snapinSet)
            {
                if (string.Equals(snapin.Name, name, StringComparison.OrdinalIgnoreCase))
                    return snapin;
            }

            return null;
        }
    } // end ModuleCmdletBase

    /// <summary>
    /// Holds the result of a binary module analysis
    /// </summary>
    internal class BinaryAnalysisResult
    {
        /// <summary>
        /// The list of cmdlets detected from the binary
        /// </summary>
        internal List<string> DetectedCmdlets { get; set; }

        // The list of aliases detected from the binary
        internal List<Tuple<string, string>> DetectedAliases { get; set; }
    }

    #endregion ModuleCmdletBase
} // Microsoft.PowerShell.Commands
