/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Linq;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Internal;
using System.Diagnostics.CodeAnalysis;
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
    #region New-ModuleManifest
    /// <summary>
    /// Cmdlet to create a new module manifest file.
    /// </summary>
    [Cmdlet(VerbsCommon.New, "ModuleManifest", SupportsShouldProcess = true, HelpUri = "http://go.microsoft.com/fwlink/?LinkID=141555")]
    [OutputType(typeof(string))]
    public sealed class NewModuleManifestCommand : PSCmdlet
    {
        /// <summary>
        /// The output path for the generated file...
        /// </summary>
        [Parameter(Mandatory = true, Position = 0)]
        public string Path
        {
            get { return _path; }
            set { _path = value; }
        }
        private string _path;

        /// <summary>
        /// Sets the list of files to load by default...
        /// </summary>
        [Parameter]
        [AllowEmptyCollection]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        public object[] NestedModules
        {
            get { return _nestedModules; }
            set { _nestedModules = value; }
        }
        private object[] _nestedModules;

        /// <summary>
        /// Set the GUID in the manifest file
        /// </summary>
        [Parameter]
        public Guid Guid
        {
            get { return _guid; }
            set { _guid = value; }
        }
        private Guid _guid = Guid.NewGuid();

        /// <summary>
        /// Set the author string in the manifest
        /// </summary>
        [Parameter]
        [AllowEmptyString]
        public string Author
        {
            get { return _author; }
            set { _author = value; }
        }
        private string _author;

        /// <summary>
        /// Set the company name in the manifest
        /// </summary>
        [Parameter]
        [AllowEmptyString]
        public string CompanyName
        {
            get { return _companyName; }
            set { _companyName = value; }
        }
        private string _companyName = "";

        /// <summary>
        /// Set the copyright string in the module manifest
        /// </summary>
        [Parameter]
        [AllowEmptyString]
        public string Copyright
        {
            get { return _copyright; }
            set { _copyright = value; }
        }
        private string _copyright;

        /// <summary>
        /// Set the module version...
        /// </summary>
        [Parameter]
        [AllowEmptyString]
        [Alias("ModuleToProcess")]
        public string RootModule
        {
            get { return _rootModule; }
            set { _rootModule = value; }
        }
        private string _rootModule = null;

        /// <summary>
        /// Set the module version...
        /// </summary>
        [Parameter]
        [ValidateNotNull]
        public Version ModuleVersion
        {
            get { return _moduleVersion; }
            set { _moduleVersion = value; }
        }
        private Version _moduleVersion = new Version(1, 0);

        /// <summary>
        /// Set the module description
        /// </summary>
        [Parameter]
        [AllowEmptyString]
        public string Description
        {
            get { return _description; }
            set { _description = value; }
        }
        private string _description;

        /// <summary>
        /// Set the ProcessorArchitecture required by this module
        /// </summary>
        [Parameter]
        public ProcessorArchitecture ProcessorArchitecture
        {
            get { return _processorArchitecture.HasValue ? _processorArchitecture.Value : ProcessorArchitecture.None; }
            set { _processorArchitecture = value; }
        }
        private ProcessorArchitecture? _processorArchitecture = null;

        /// <summary>
        /// Set the PowerShell version required by this module
        /// </summary>
        [Parameter]
        public Version PowerShellVersion
        {
            get { return _powerShellVersion; }
            set { _powerShellVersion = value; }
        }
        private Version _powerShellVersion = null;

        /// <summary>
        /// Set the CLR version required by the module.
        /// </summary>
        [Parameter]
        public Version ClrVersion
        {
            get { return _ClrVersion; }
            set { _ClrVersion = value; }
        }
        private Version _ClrVersion = null;

        /// <summary>
        /// Set the version of .NET Framework required by the module.
        /// </summary>
        [Parameter]
        public Version DotNetFrameworkVersion
        {
            get { return _DotNetFrameworkVersion; }
            set { _DotNetFrameworkVersion = value; }
        }
        private Version _DotNetFrameworkVersion = null;

        /// <summary>
        /// Set the name of PowerShell host required by the module.
        /// </summary>
        [Parameter]
        public string PowerShellHostName
        {
            get { return _PowerShellHostName; }
            set { _PowerShellHostName = value; }
        }
        private string _PowerShellHostName = null;

        /// <summary>
        /// Set the version of PowerShell host required by the module.
        /// </summary>
        [Parameter]
        public Version PowerShellHostVersion
        {
            get { return _PowerShellHostVersion; }
            set { _PowerShellHostVersion = value; }
        }
        private Version _PowerShellHostVersion = null;

        /// <summary>
        /// Sets the list of Dependencies for the module
        /// </summary>
        [Parameter]
        [ArgumentTypeConverter(typeof(ModuleSpecification[]))]
        [SuppressMessage("Microsoft.Performance",
            "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        public object[] RequiredModules
        {
            get { return _requiredModules; }
            set { _requiredModules = value; }
        }
        private object[] _requiredModules;

        /// <summary>
        /// Sets the list of types files for the module
        /// </summary>
        [Parameter]
        [AllowEmptyCollection]
        [SuppressMessage("Microsoft.Performance",
            "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        public string[] TypesToProcess
        {
            get { return _types; }
            set { _types = value; }
        }
        private string[] _types;

        /// <summary>
        /// Sets the list of formats files for the module
        /// </summary>
        [Parameter]
        [AllowEmptyCollection]
        [SuppressMessage("Microsoft.Performance",
            "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        public string[] FormatsToProcess
        {
            get { return _formats; }
            set { _formats = value; }
        }
        private string[] _formats;

        /// <summary>
        /// Sets the list of ps1 scripts to run in the session state of the import-module invocation.
        /// </summary>
        [Parameter]
        [AllowEmptyCollection]
        [SuppressMessage("Microsoft.Performance",
            "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        public string[] ScriptsToProcess
        {
            get { return _scripts; }
            set { _scripts = value; }
        }
        private string[] _scripts;

        /// <summary>
        /// Set the list of assemblies to load for this module.
        /// </summary>
        [Parameter]
        [AllowEmptyCollection]
        [SuppressMessage("Microsoft.Performance",
            "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        public string[] RequiredAssemblies
        {
            get { return _requiredAssemblies; }
            set { _requiredAssemblies = value; }
        }
        private string[] _requiredAssemblies;

        /// <summary>
        /// Specify any additional files used by this module.
        /// </summary>
        [Parameter]
        [AllowEmptyCollection]
        [SuppressMessage("Microsoft.Performance",
            "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        public string[] FileList
        {
            get { return _miscFiles; }
            set { _miscFiles = value; }
        }
        private string[] _miscFiles;

        /// <summary>
        /// List of other modules included with this module. 
        /// Like the RequiredModules key, this list can be a simple list of module names or a complex list of module hashtables.
        /// </summary>
        [Parameter]
        [AllowEmptyCollection]
        [ArgumentTypeConverter(typeof(ModuleSpecification[]))]
        [SuppressMessage("Microsoft.Performance",
            "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        public object[] ModuleList
        {
            get { return _moduleList; }
            set { _moduleList = value; }
        }
        private object[] _moduleList;

        /// <summary>
        /// Specify any functions to export from this manifest.
        /// </summary>
        [Parameter]
        [AllowEmptyCollection]
        [SuppressMessage("Microsoft.Performance",
            "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        public string[] FunctionsToExport
        {
            get { return _exportedFunctions; }
            set { _exportedFunctions = value; }
        }
        private string[] _exportedFunctions;

        /// <summary>
        /// Specify any aliases to export from this manifest.
        /// </summary>
        [Parameter]
        [AllowEmptyCollection]
        [SuppressMessage("Microsoft.Performance",
            "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        public string[] AliasesToExport
        {
            get { return _exportedAliases; }
            set { _exportedAliases = value; }
        }
        private string[] _exportedAliases;

        /// <summary>
        /// Specify any variables to export from this manifest.
        /// </summary>
        [Parameter]
        [AllowEmptyCollection]
        [SuppressMessage("Microsoft.Performance",
            "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        public string[] VariablesToExport
        {
            get { return _exportedVariables; }
            set { _exportedVariables = value; }
        }
        private string[] _exportedVariables = new string[] { "*" };

        /// <summary>
        /// Specify any cmdlets to export from this manifest.
        /// </summary>
        [Parameter]
        [AllowEmptyCollection]
        [SuppressMessage("Microsoft.Performance",
            "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        public string[] CmdletsToExport
        {
            get { return _exportedCmdlets; }
            set { _exportedCmdlets = value; }
        }
        private string[] _exportedCmdlets;

        /// <summary>
        /// Specify any dsc resources to export from this manifest.
        /// </summary>
        [Parameter]
        [AllowEmptyCollection]
        [SuppressMessage("Microsoft.Performance",
            "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        public string[] DscResourcesToExport
        {
            get { return _dscResourcesToExport; }
            set { _dscResourcesToExport = value; }
        }
        private string[] _dscResourcesToExport;

        /// <summary>
        /// Specify compatible PSEditions of this module.
        /// </summary>
        [Parameter]
        [AllowEmptyCollection]
        [ValidateSet("Desktop", "Core")]
        [SuppressMessage("Microsoft.Performance",
            "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        public string[] CompatiblePSEditions
        {
            get { return _compatiblePSEditions; }
            set { _compatiblePSEditions = value; }
        }
        private string[] _compatiblePSEditions;

        /// <summary>
        /// Specify any module-specific private data here.
        /// </summary>
        [Parameter(Mandatory = false)]
        [AllowNull]
        public object PrivateData
        {
            get { return _privateData; }
            set { _privateData = value; }
        }
        private object _privateData;

        /// <summary>
        /// Specify any Tags.
        /// </summary>
        [Parameter(Mandatory = false)]
        [ValidateNotNullOrEmpty]
        [SuppressMessage("Microsoft.Performance",
            "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        public string[] Tags { get; set; }

        /// <summary>
        /// Specify the ProjectUri.
        /// </summary>
        [Parameter(Mandatory = false)]
        [ValidateNotNullOrEmpty]
        public Uri ProjectUri { get; set; }

        /// <summary>
        /// Specify the LicenseUri.
        /// </summary>
        [Parameter(Mandatory = false)]
        [ValidateNotNullOrEmpty]
        public Uri LicenseUri { get; set; }

        /// <summary>
        /// Specify the IconUri.
        /// </summary>
        [Parameter(Mandatory = false)]
        [ValidateNotNullOrEmpty]
        public Uri IconUri { get; set; }

        /// <summary>
        /// Specify the ReleaseNotes.
        /// </summary>
        [Parameter(Mandatory = false)]
        [ValidateNotNullOrEmpty]
        public string ReleaseNotes { get; set; }

        /// <summary>
        /// Specify the HelpInfo URI
        /// </summary>
        [Parameter]
        [AllowNull]
        [SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings")]
        public string HelpInfoUri
        {
            get { return _helpInfoUri; }
            set { _helpInfoUri = value; }
        }
        private string _helpInfoUri;

        /// <summary>
        /// This parameter causes the module manifest string to be to the output stream...
        /// </summary>
        [Parameter]
        public SwitchParameter PassThru
        {
            get { return (SwitchParameter)_passThru; }
            set { _passThru = value; }
        }
        private bool _passThru;

        /// <summary>
        /// Specify the Default Command Prefix 
        /// </summary>
        [Parameter]
        [AllowNull]
        public string DefaultCommandPrefix
        {
            get { return _defaultCommandPrefix; }
            set { _defaultCommandPrefix = value; }
        }
        private string _defaultCommandPrefix;

        private string _indent = "";

        /// <summary>
        /// Return a single-quoted string. Any embedded single quotes will be doubled.
        /// </summary>
        /// <param name="name">The string to quote</param>
        /// <returns>The quoted string</returns>
        private string QuoteName(object name)
        {
            if (name == null)
                return "''";
            return "'" + name.ToString().Replace("'", "''") + "'";
        }

        /// <summary>
        /// Takes a collection of strings and returns the collection
        /// quoted.
        /// </summary>
        /// <param name="names">The list to quote</param>
        /// <param name="streamWriter">Streamwriter to get end of line character from</param>
        /// <returns>The quoted list</returns>
        private string QuoteNames(IEnumerable names, StreamWriter streamWriter)
        {
            if (names == null)
                return "@()";

            StringBuilder result = new StringBuilder();

            int offset = 15;
            bool first = true;
            foreach (string name in names)
            {
                if (!string.IsNullOrEmpty(name))
                {
                    if (first)
                    {
                        first = false;
                    }
                    else
                    {
                        result.Append(", ");
                    }

                    string quotedString = QuoteName(name);
                    offset += quotedString.Length;
                    if (offset > 80)
                    {
                        result.Append(streamWriter.NewLine);
                        result.Append("               ");
                        offset = 15 + quotedString.Length;
                    }
                    result.Append(quotedString);
                }
            }
            if (result.Length == 0)
                return "@()";

            return result.ToString();
        }

        /// <summary>
        /// This function is created to PreProcess -NestedModules in Win8.
        /// In Win7, -NestedModules is of type string[]. In Win8, we changed
        /// this to object[] to support module specification using hashtable.
        /// To be backward compatible, this function calls ToString() on any
        /// object that is not of type hashtable or string.
        /// </summary>
        /// <param name="moduleSpecs"></param>
        /// <returns></returns>
        private IEnumerable PreProcessModuleSpec(IEnumerable moduleSpecs)
        {
            if (null != moduleSpecs)
            {
                foreach (object spec in moduleSpecs)
                {
                    if (!(spec is Hashtable))
                    {
                        yield return spec.ToString();
                    }
                    else
                    {
                        yield return spec;
                    }
                }
            }
        }

        /// <summary>
        /// Takes a collection of "module specifications" (string or hashtable)
        /// and returns the collection as a string that can be inserted into a module manifest
        /// </summary>
        /// <param name="moduleSpecs">The list to quote</param>
        /// <param name="streamWriter">Streamwriter to get end of line character from</param>
        /// <returns>The quoted list</returns>
        private string QuoteModules(IEnumerable moduleSpecs, StreamWriter streamWriter)
        {
            StringBuilder result = new StringBuilder();
            result.Append("@(");

            if (moduleSpecs != null)
            {
                bool firstModule = true;
                foreach (object spec in moduleSpecs)
                {
                    if (spec == null)
                    {
                        continue;
                    }

                    ModuleSpecification moduleSpecification = (ModuleSpecification)
                        LanguagePrimitives.ConvertTo(
                            spec,
                            typeof(ModuleSpecification),
                            CultureInfo.InvariantCulture);

                    if (!firstModule)
                    {
                        result.Append(", ");
                        result.Append(streamWriter.NewLine);
                        result.Append("               ");
                    }
                    firstModule = false;

                    if ((moduleSpecification.Guid == null) && (moduleSpecification.Version == null) && (moduleSpecification.MaximumVersion == null) && (moduleSpecification.RequiredVersion == null))
                    {
                        result.Append(QuoteName(moduleSpecification.Name));
                    }
                    else
                    {
                        result.Append("@{");

                        result.Append("ModuleName = ");
                        result.Append(QuoteName(moduleSpecification.Name));
                        result.Append("; ");

                        if (moduleSpecification.Guid != null)
                        {
                            result.Append("GUID = ");
                            result.Append(QuoteName(moduleSpecification.Guid.ToString()));
                            result.Append("; ");
                        }

                        if (moduleSpecification.Version != null)
                        {
                            result.Append("ModuleVersion = ");
                            result.Append(QuoteName(moduleSpecification.Version.ToString()));
                            result.Append("; ");
                        }

                        if (moduleSpecification.MaximumVersion != null)
                        {
                            result.Append("MaximumVersion = ");
                            result.Append(QuoteName(moduleSpecification.MaximumVersion));
                            result.Append("; ");
                        }

                        if (moduleSpecification.RequiredVersion != null)
                        {
                            result.Append("RequiredVersion = ");
                            result.Append(QuoteName(moduleSpecification.RequiredVersion.ToString()));
                            result.Append("; ");
                        }

                        result.Append("}");
                    }
                }
            }

            result.Append(")");
            return result.ToString();
        }

        /// <summary>
        /// Takes a collection of file names and returns the collection
        /// quoted.
        /// </summary>
        /// <param name="names">The list to quote</param>
        /// <param name="streamWriter">Streamwriter to get end of line character from</param>
        /// <returns>The quoted list</returns>
        private string QuoteFiles(IEnumerable names, StreamWriter streamWriter)
        {
            List<string> resolvedPaths = new List<string>();

            if (names != null)
            {
                foreach (string name in names)
                {
                    if (!string.IsNullOrEmpty(name))
                    {
                        foreach (string path in TryResolveFilePath(name))
                        {
                            resolvedPaths.Add(path);
                        }
                    }
                }
            }

            return QuoteNames(resolvedPaths, streamWriter);
        }

        ///// <summary>
        ///// Takes a collection of file names and returns the collection
        ///// quoted.  It does not expand wildcard to actual files (as QuoteFiles does).
        ///// It throws an error when the entered filename is different than the allowedExtension.
        ///// If any file name falls outside the directory tree basPath a warning is issued.
        ///// </summary>
        ///// <param name="basePath">This is the path which will be used to determine whether a warning is to be displayed.</param>
        ///// <param name="names">The list to quote</param>
        ///// <param name="allowedExtension">This is the allowed file extension, any other extension will give an error.</param>
        ///// <param name="streamWriter">Streamwriter to get end of line character from</param>
        ///// <param name="item">The item of the manifest file for which names are being resolved.</param>
        ///// <returns>The quoted list</returns>
        //private string QuoteFilesWithWildcard(string basePath, IEnumerable names, string allowedExtension, StreamWriter streamWriter, string item)
        //{
        //    if (names != null)
        //    {
        //        foreach (string name in names)
        //        {
        //            if (string.IsNullOrEmpty(name))
        //                continue;

        //            string fileName = name;

        //            string extension = System.IO.Path.GetExtension(fileName);
        //            if (string.Equals(extension, allowedExtension, StringComparison.OrdinalIgnoreCase))
        //            {
        //                string drive = string.Empty;
        //                if (!SessionState.Path.IsPSAbsolute(fileName, out drive) && !System.IO.Path.IsPathRooted(fileName))
        //                {
        //                    fileName = SessionState.Path.Combine(SessionState.Path.CurrentLocation.ProviderPath, fileName);
        //                }

        //                string basePathDir = System.IO.Path.GetDirectoryName(SessionState.Path.GetUnresolvedProviderPathFromPSPath(basePath));
        //                if (basePathDir[basePathDir.Length - 1] != StringLiterals.DefaultPathSeparator)
        //                {
        //                    basePathDir += StringLiterals.DefaultPathSeparator;
        //                }
        //                string fileDir = null;

        //                // Call to SessionState.Path.GetUnresolvedProviderPathFromPSPath throws an exception
        //                // when the drive in the path does not exist.
        //                // Based on the exception it is obvious that the path is outside the basePath, because
        //                // basePath must always exist.
        //                try
        //                {
        //                    fileDir = System.IO.Path.GetDirectoryName(SessionState.Path.GetUnresolvedProviderPathFromPSPath(fileName));
        //                    if (fileDir[fileDir.Length - 1] != StringLiterals.DefaultPathSeparator)
        //                    {
        //                        fileDir += StringLiterals.DefaultPathSeparator;
        //                    }
        //                }
        //                catch
        //                {
        //                }

        //                if (fileDir == null
        //                    || !fileDir.StartsWith(basePathDir, StringComparison.OrdinalIgnoreCase))
        //                {
        //                    WriteWarning(StringUtil.Format(Modules.IncludedItemPathFallsOutsideSaveTree, name,
        //                        fileDir ?? name, item));
        //                }
        //            }
        //            else
        //            {
        //                string message = StringUtil.Format(Modules.InvalidWorkflowExtension);
        //                InvalidOperationException invalidOp = new InvalidOperationException(message);
        //                ErrorRecord er = new ErrorRecord(invalidOp, "Modules_InvalidWorkflowExtension",
        //                    ErrorCategory.InvalidOperation, null);
        //                ThrowTerminatingError(er);
        //            }
        //        }
        //    }

        //    return QuoteNames(names, streamWriter);
        //}

        /// <summary>
        /// Glob a set of files then resolve them to relative paths.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        private List<string> TryResolveFilePath(string filePath)
        {
            List<string> result = new List<string>();
            ProviderInfo provider = null;
            SessionState sessionState = Context.SessionState;
            try
            {
                Collection<string> filePaths =
                    sessionState.Path.GetResolvedProviderPathFromPSPath(filePath, out provider);

                // If the name doesn't resolve to something we can use, just return the unresolved name...
                if (!provider.NameEquals(this.Context.ProviderNames.FileSystem) || filePaths == null || filePaths.Count < 1)
                {
                    result.Add(filePath);
                    return result;
                }

                // Otherwise get the relative resolved path and trim the .\ or ./ because
                // modules are always loaded relative to the manifest base directory.
                foreach (string path in filePaths)
                {
                    string adjustedPath = SessionState.Path.NormalizeRelativePath(path,
                        SessionState.Path.CurrentLocation.ProviderPath);
                    if (adjustedPath.StartsWith(".\\", StringComparison.OrdinalIgnoreCase) ||
                        adjustedPath.StartsWith("./", StringComparison.OrdinalIgnoreCase))
                    {
                        adjustedPath = adjustedPath.Substring(2);
                    }
                    result.Add(adjustedPath);
                }
            }
            catch (ItemNotFoundException)
            {
                result.Add(filePath);
            }

            return result;
        }

        /// <summary>
        /// This routine builds a fragment of the module manifest file
        /// for a particular key. It returns a formatted string that includes
        /// a comment describing the key as well as the key and its value.
        /// </summary>
        /// <param name="key">The manifest key to use</param>
        /// <param name="resourceString">resourceString that holds the message</param>
        /// <param name="value">The formatted manifest fragment</param>
        /// <param name="streamWriter">Streamwriter to get end of line character from</param>
        /// <returns></returns>
        private string ManifestFragment(string key, string resourceString, string value, StreamWriter streamWriter)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}# {1}{2}{0}{3:19} = {4}{2}{2}",
                _indent, resourceString, streamWriter.NewLine, key, value);
        }

        private string ManifestFragmentForNonSpecifiedManifestMember(string key, string resourceString, string value, StreamWriter streamWriter)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}# {1}{2}{0}# {3:19} = {4}{2}{2}",
                _indent, resourceString, streamWriter.NewLine, key, value);
        }

        private string ManifestComment(string insert, StreamWriter streamWriter)
        {
            // Prefix a non-empty string with a space for formatting reasons...
            if (!string.IsNullOrEmpty(insert))
            {
                insert = " " + insert;
            }
            return String.Format(CultureInfo.InvariantCulture, "#{0}{1}", insert, streamWriter.NewLine);
        }

        /// <summary>
        /// Generate the module manifest...
        /// </summary>
        protected override void EndProcessing()
        {
            // Win8: 264471 - Error message for New-ModuleManifest �ProcessorArchitecture is obsolete.
            // If an undefined value is passed for the ProcessorArchitecture parameter, the error message from parameter binder includes all the values from the enum. 
            // The value 'IA64' for ProcessorArchitecture is not supported. But since we do not own the enum System.Reflection.ProcessorArchitecture, we cannot control the values in it.
            // So, we add a separate check in our code to give an error if user specifies IA64
            if (ProcessorArchitecture == ProcessorArchitecture.IA64)
            {
                string message = StringUtil.Format(Modules.InvalidProcessorArchitectureInManifest, ProcessorArchitecture);
                InvalidOperationException ioe = new InvalidOperationException(message);
                ErrorRecord er = new ErrorRecord(ioe, "Modules_InvalidProcessorArchitectureInManifest",
                    ErrorCategory.InvalidArgument, ProcessorArchitecture);
                ThrowTerminatingError(er);
            }

            ProviderInfo provider = null;
            PSDriveInfo drive;
            string filePath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(_path, out provider, out drive);

            if (!provider.NameEquals(Context.ProviderNames.FileSystem) || !filePath.EndsWith(StringLiterals.PowerShellDataFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                string message = StringUtil.Format(Modules.InvalidModuleManifestPath, _path);
                InvalidOperationException ioe = new InvalidOperationException(message);
                ErrorRecord er = new ErrorRecord(ioe, "Modules_InvalidModuleManifestPath",
                    ErrorCategory.InvalidArgument, _path);
                ThrowTerminatingError(er);
            }

            // By default, we want to generate a module manifest the encourages the best practice of explicitly specifying
            // the commands exported (even if it's an empty array.) Unfortunately, changing the default breaks automation
            // (however unlikely, this cmdlet isn't really meant for automation). Instead of trying to detect interactive
            // use (which is quite hard), we infer interactive use if none of RootModule/NestedModules/RequiredModules is
            // specified - because the manifest needs to be edited to actually be of use in those cases.
            //
            // If one of these parameters has been specified, default back to the old behavior by specifying
            // wildcards for exported commands that weren't specified on the command line.
            if (_rootModule != null || _nestedModules != null || _requiredModules != null)
            {
                if (_exportedFunctions == null)
                    _exportedFunctions = new string[] { "*" };
                if (_exportedAliases == null)
                    _exportedAliases = new string[] { "*" };
                if (_exportedCmdlets == null)
                    _exportedCmdlets = new string[] { "*" };
            }

            ValidateUriParamterValue(ProjectUri, "ProjectUri");
            ValidateUriParamterValue(LicenseUri, "LicenseUri");
            ValidateUriParamterValue(IconUri, "IconUri");

            if (CompatiblePSEditions != null && (CompatiblePSEditions.Distinct(StringComparer.OrdinalIgnoreCase).Count() != CompatiblePSEditions.Count()))
            {
                string message = StringUtil.Format(Modules.DuplicateEntriesInCompatiblePSEditions, String.Join(",", CompatiblePSEditions));
                var ioe = new InvalidOperationException(message);
                var er = new ErrorRecord(ioe, "Modules_DuplicateEntriesInCompatiblePSEditions", ErrorCategory.InvalidArgument, CompatiblePSEditions);
                ThrowTerminatingError(er);
            }

            string action = StringUtil.Format(Modules.CreatingModuleManifestFile, filePath);

            if (ShouldProcess(filePath, action))
            {
                if (string.IsNullOrEmpty(_author))
                {
                    _author = Environment.UserName;
                }

                if (string.IsNullOrEmpty(_companyName))
                {
                    _companyName = Modules.DefaultCompanyName;
                }

                if (string.IsNullOrEmpty(_copyright))
                {
                    _copyright = StringUtil.Format(Modules.DefaultCopyrightMessage, DateTime.Now.Year, _author);
                }

                FileStream fileStream;
                StreamWriter streamWriter;
                FileInfo readOnlyFileInfo;

                // Now open the output file...
                PathUtils.MasterStreamOpen(
                    this,
                    filePath,
                    EncodingConversion.Unicode,
                    /* defaultEncoding */ false,
                    /* Append */ false,
                    /* Force */ false,
                    /* NoClobber */ false,
                    out fileStream,
                    out streamWriter,
                    out readOnlyFileInfo,
                    false
                );

                try
                {
                    StringBuilder result = new StringBuilder();

                    // Insert the formatted manifest header...
                    result.Append(ManifestComment("", streamWriter));
                    result.Append(ManifestComment(StringUtil.Format(Modules.ManifestHeaderLine1, System.IO.Path.GetFileNameWithoutExtension(filePath)),
                            streamWriter));
                    result.Append(ManifestComment("", streamWriter));
                    result.Append(ManifestComment(StringUtil.Format(Modules.ManifestHeaderLine2, _author),
                            streamWriter));
                    result.Append(ManifestComment("", streamWriter));
                    result.Append(ManifestComment(StringUtil.Format(Modules.ManifestHeaderLine3, DateTime.Now.ToString("d", CultureInfo.CurrentCulture)),
                            streamWriter));
                    result.Append(ManifestComment("", streamWriter));
                    result.Append(streamWriter.NewLine);
                    result.Append("@{");
                    result.Append(streamWriter.NewLine);
                    result.Append(streamWriter.NewLine);

                    if (_rootModule == null)
                        _rootModule = String.Empty;

                    BuildModuleManifest(result, "RootModule", Modules.RootModule, !string.IsNullOrEmpty(_rootModule), () => QuoteName(_rootModule), streamWriter);

                    BuildModuleManifest(result, "ModuleVersion", Modules.ModuleVersion, _moduleVersion != null && !string.IsNullOrEmpty(_moduleVersion.ToString()), () => QuoteName(_moduleVersion.ToString()), streamWriter);

                    BuildModuleManifest(result, "CompatiblePSEditions", Modules.CompatiblePSEditions, _compatiblePSEditions != null && _compatiblePSEditions.Length > 0, () => QuoteNames(_compatiblePSEditions, streamWriter), streamWriter);

                    BuildModuleManifest(result, "GUID", Modules.GUID, !string.IsNullOrEmpty(_guid.ToString()), () => QuoteName(_guid.ToString()), streamWriter);

                    BuildModuleManifest(result, "Author", Modules.Author, !string.IsNullOrEmpty(_author), () => QuoteName(Author), streamWriter);

                    BuildModuleManifest(result, "CompanyName", Modules.CompanyName, !string.IsNullOrEmpty(_companyName), () => QuoteName(_companyName), streamWriter);

                    BuildModuleManifest(result, "Copyright", Modules.Copyright, !string.IsNullOrEmpty(_copyright), () => QuoteName(_copyright), streamWriter);

                    BuildModuleManifest(result, "Description", Modules.Description, !string.IsNullOrEmpty(_description), () => QuoteName(_description), streamWriter);

                    BuildModuleManifest(result, "PowerShellVersion", Modules.PowerShellVersion, _powerShellVersion != null && !string.IsNullOrEmpty(_powerShellVersion.ToString()), () => QuoteName(_powerShellVersion), streamWriter);

                    BuildModuleManifest(result, "PowerShellHostName", Modules.PowerShellHostName, !string.IsNullOrEmpty(_PowerShellHostName), () => QuoteName(_PowerShellHostName), streamWriter);

                    BuildModuleManifest(result, "PowerShellHostVersion", Modules.PowerShellHostVersion, _PowerShellHostVersion != null && !string.IsNullOrEmpty(_PowerShellHostVersion.ToString()), () => QuoteName(_PowerShellHostVersion), streamWriter);

                    BuildModuleManifest(result, "DotNetFrameworkVersion", StringUtil.Format(Modules.DotNetFrameworkVersion, Modules.PrerequisiteForDesktopEditionOnly), _DotNetFrameworkVersion != null && !string.IsNullOrEmpty(_DotNetFrameworkVersion.ToString()), () => QuoteName(_DotNetFrameworkVersion), streamWriter);

                    BuildModuleManifest(result, "CLRVersion", StringUtil.Format(Modules.CLRVersion, Modules.PrerequisiteForDesktopEditionOnly), _ClrVersion != null && !string.IsNullOrEmpty(_ClrVersion.ToString()), () => QuoteName(_ClrVersion), streamWriter);

                    BuildModuleManifest(result, "ProcessorArchitecture", Modules.ProcessorArchitecture, _processorArchitecture.HasValue, () => QuoteName(_processorArchitecture), streamWriter);

                    BuildModuleManifest(result, "RequiredModules", Modules.RequiredModules, _requiredModules != null && _requiredModules.Length > 0, () => QuoteModules(_requiredModules, streamWriter), streamWriter);

                    BuildModuleManifest(result, "RequiredAssemblies", Modules.RequiredAssemblies, _requiredAssemblies != null, () => QuoteFiles(_requiredAssemblies, streamWriter), streamWriter);

                    BuildModuleManifest(result, "ScriptsToProcess", Modules.ScriptsToProcess, _scripts != null, () => QuoteFiles(_scripts, streamWriter), streamWriter);

                    BuildModuleManifest(result, "TypesToProcess", Modules.TypesToProcess, _types != null, () => QuoteFiles(_types, streamWriter), streamWriter);

                    BuildModuleManifest(result, "FormatsToProcess", Modules.FormatsToProcess, _formats != null, () => QuoteFiles(_formats, streamWriter), streamWriter);

                    BuildModuleManifest(result, "NestedModules", Modules.NestedModules, _nestedModules != null, () => QuoteModules(PreProcessModuleSpec(_nestedModules), streamWriter), streamWriter);

                    BuildModuleManifest(result, "FunctionsToExport", Modules.FunctionsToExport, true, () => QuoteNames(_exportedFunctions, streamWriter), streamWriter);

                    BuildModuleManifest(result, "CmdletsToExport", Modules.CmdletsToExport, true, () => QuoteNames(_exportedCmdlets, streamWriter), streamWriter);

                    BuildModuleManifest(result, "VariablesToExport", Modules.VariablesToExport, _exportedVariables != null && _exportedVariables.Length > 0, () => QuoteNames(_exportedVariables, streamWriter), streamWriter);

                    BuildModuleManifest(result, "AliasesToExport", Modules.AliasesToExport, true, () => QuoteNames(_exportedAliases, streamWriter), streamWriter);

                    BuildModuleManifest(result, "DscResourcesToExport", Modules.DscResourcesToExport, _dscResourcesToExport != null && _dscResourcesToExport.Length > 0, () => QuoteNames(_dscResourcesToExport, streamWriter), streamWriter);

                    BuildModuleManifest(result, "ModuleList", Modules.ModuleList, _moduleList != null, () => QuoteModules(_moduleList, streamWriter), streamWriter);

                    BuildModuleManifest(result, "FileList", Modules.FileList, _miscFiles != null, () => QuoteFiles(_miscFiles, streamWriter), streamWriter);

                    BuildPrivateDataInModuleManifest(result, streamWriter);

                    BuildModuleManifest(result, "HelpInfoURI", Modules.HelpInfoURI, !string.IsNullOrEmpty(_helpInfoUri), () => QuoteName(_helpInfoUri), streamWriter);

                    BuildModuleManifest(result, "DefaultCommandPrefix", Modules.DefaultCommandPrefix, !string.IsNullOrEmpty(_defaultCommandPrefix), () => QuoteName(_defaultCommandPrefix), streamWriter);

                    result.Append("}");
                    result.Append(streamWriter.NewLine);
                    result.Append(streamWriter.NewLine);
                    string strResult = result.ToString();

                    if (_passThru)
                    {
                        WriteObject(strResult);
                    }
                    streamWriter.Write(strResult);
                }
                finally
                {
                    streamWriter.Dispose();
                }
            }
        }

        private void BuildModuleManifest(StringBuilder result, string key, string keyDescription, bool hasValue, Func<string> action, StreamWriter streamWriter)
        {
            if (hasValue)
            {
                result.Append(ManifestFragment(key, keyDescription, action(), streamWriter));
            }
            else
            {
                result.Append(ManifestFragmentForNonSpecifiedManifestMember(key, keyDescription, action(), streamWriter));
            }
        }

        // PrivateData format in manifest file when PrivateData value is a HashTable or not specified.
        // <#
        // # Private data to pass to the module specified in RootModule/ModuleToProcess
        // PrivateData = @{
        //
        // PSData = @{
        // # Tags of this module
        // Tags = @()
        // # LicenseUri of this module
        // LicenseUri = ''
        // # ProjectUri of this module
        // ProjectUri = ''
        // # IconUri of this module
        // IconUri = ''
        // # ReleaseNotes of this module
        // ReleaseNotes = ''
        // }# end of PSData hashtable
        // 
        // # User's private data keys
        // 
        // }# end of PrivateData hashtable
        // #>
        private void BuildPrivateDataInModuleManifest(StringBuilder result, StreamWriter streamWriter)
        {
            var privateDataHashTable = PrivateData as Hashtable;
            bool specifiedPSDataProperties = !(Tags == null && ReleaseNotes == null && ProjectUri == null && IconUri == null && LicenseUri == null);

            if (_privateData != null && privateDataHashTable == null)
            {
                if (specifiedPSDataProperties)
                {
                    var ioe = new InvalidOperationException(Modules.PrivateDataValueTypeShouldBeHashTableError);
                    var er = new ErrorRecord(ioe, "PrivateDataValueTypeShouldBeHashTable", ErrorCategory.InvalidArgument, _privateData);
                    ThrowTerminatingError(er);
                }
                else
                {
                    WriteWarning(Modules.PrivateDataValueTypeShouldBeHashTableWarning);

                    BuildModuleManifest(result, "PrivateData", Modules.PrivateData, _privateData != null,
                        () => QuoteName((string)LanguagePrimitives.ConvertTo(_privateData, typeof(string), CultureInfo.InvariantCulture)),
                        streamWriter);
                }
            }
            else
            {
                result.Append(ManifestComment(Modules.PrivateData, streamWriter));
                result.Append("PrivateData = @{");
                result.Append(streamWriter.NewLine);

                result.Append(streamWriter.NewLine);
                result.Append("    PSData = @{");
                result.Append(streamWriter.NewLine);
                result.Append(streamWriter.NewLine);

                _indent = "        ";

                BuildModuleManifest(result, "Tags", Modules.Tags, Tags != null && Tags.Length > 0, () => QuoteNames(Tags, streamWriter), streamWriter);
                BuildModuleManifest(result, "LicenseUri", Modules.LicenseUri, LicenseUri != null, () => QuoteName(LicenseUri), streamWriter);
                BuildModuleManifest(result, "ProjectUri", Modules.ProjectUri, ProjectUri != null, () => QuoteName(ProjectUri), streamWriter);
                BuildModuleManifest(result, "IconUri", Modules.IconUri, IconUri != null, () => QuoteName(IconUri), streamWriter);
                BuildModuleManifest(result, "ReleaseNotes", Modules.ReleaseNotes, !string.IsNullOrEmpty(ReleaseNotes), () => QuoteName(ReleaseNotes), streamWriter);

                result.Append("    } ");
                result.Append(ManifestComment(StringUtil.Format(Modules.EndOfManifestHashTable, "PSData"), streamWriter));
                result.Append(streamWriter.NewLine);

                _indent = "    ";
                if (privateDataHashTable != null)
                {
                    result.Append(streamWriter.NewLine);

                    foreach (DictionaryEntry entry in privateDataHashTable)
                    {
                        result.Append(ManifestFragment(entry.Key.ToString(), entry.Key.ToString(), QuoteName((string)LanguagePrimitives.ConvertTo(entry.Value, typeof(string), CultureInfo.InvariantCulture)), streamWriter));
                    }
                }

                result.Append("} ");
                result.Append(ManifestComment(StringUtil.Format(Modules.EndOfManifestHashTable, "PrivateData"), streamWriter));

                _indent = "";

                result.Append(streamWriter.NewLine);
            }
        }

        private void ValidateUriParamterValue(Uri uri, string parameterName)
        {
            Dbg.Assert(!String.IsNullOrWhiteSpace(parameterName), "parameterName should not be null or whitespace");

            if (uri != null && !Uri.IsWellFormedUriString(uri.ToString(), UriKind.Absolute))
            {
                var message = StringUtil.Format(Modules.InvalidParameterValue, uri);
                var ioe = new InvalidOperationException(message);
                var er = new ErrorRecord(ioe, "Modules_InvalidUri",
                    ErrorCategory.InvalidArgument, parameterName);
                ThrowTerminatingError(er);
            }
        }
    }

    #endregion
} // Microsoft.PowerShell.Commands
