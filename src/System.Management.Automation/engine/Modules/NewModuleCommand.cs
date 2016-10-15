/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Diagnostics.CodeAnalysis;

//
// Now define the set of commands for manipulating modules.
//

namespace Microsoft.PowerShell.Commands
{
    #region New-Module

    /// <summary>
    /// Implements a cmdlet that creates a dynamic module from a scriptblock..
    /// </summary>
    [Cmdlet("New", "Module", DefaultParameterSetName = "ScriptBlock", HelpUri = "http://go.microsoft.com/fwlink/?LinkID=141554")]
    [OutputType(typeof(PSModuleInfo))]
    public sealed class NewModuleCommand : ModuleCmdletBase
    {
        /// <summary>
        /// This parameter specifies the name to assign to the dynamic module.
        /// </summary>
        [Parameter(ParameterSetName = "Name", Mandatory = true, ValueFromPipeline = true, Position = 0)]
        public string Name
        {
            set { _name = value; }
            get { return _name; }
        }
        private string _name;

        /// <summary>
        /// Specify a scriptblock to use for the module body...
        /// </summary>
        [Parameter(ParameterSetName = "Name", Mandatory = true, Position = 1)]
        [Parameter(ParameterSetName = "ScriptBlock", Mandatory = true, Position = 0)]
        [ValidateNotNull]
        public ScriptBlock ScriptBlock
        {
            get { return _scriptBlock; }
            set
            {
                _scriptBlock = value;
            }
        }
        private ScriptBlock _scriptBlock;

        /// <summary>
        /// This parameter specifies the patterns matching the functions to import from the module...
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
        /// This parameter specifies the patterns matching the cmdlets to import from the module...
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
        /// This parameter causes the session state instance to be written...
        /// </summary>
        [Parameter]
        public SwitchParameter ReturnResult
        {
            get { return (SwitchParameter)_returnResult; }
            set { _returnResult = value; }
        }
        private bool _returnResult;

        /// <summary>
        /// This parameter causes the session state instance to be written...
        /// </summary>
        [Parameter]
        public SwitchParameter AsCustomObject
        {
            get { return (SwitchParameter)_asCustomObject; }
            set { _asCustomObject = value; }
        }
        private bool _asCustomObject;

        /// <summary>
        /// The arguments to pass to the scriptblock used to create the module
        /// </summary>
        [Parameter(ValueFromRemainingArguments = true)]
        [Alias("Args")]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays", Justification = "Cmdlets use arrays for parameters.")]
        public object[] ArgumentList
        {
            get { return _arguments; }
            set { _arguments = value; }
        }
        private object[] _arguments;

        /// <summary>
        /// Create the new module...
        /// </summary>
        protected override void EndProcessing()
        {
            // Create a module from a scriptblock...
            if (_scriptBlock != null)
            {
                string gs = System.Guid.NewGuid().ToString();
                if (String.IsNullOrEmpty(_name))
                {
                    _name = PSModuleInfo.DynamicModulePrefixString + gs;
                }

                try
                {
                    Context.Modules.IncrementModuleNestingDepth(this, _name);

                    List<object> results = null;
                    PSModuleInfo localModule = null;
                    try
                    {
                        // The path for a "dynamic" module will be a GUID so it's unique.
                        localModule = Context.Modules.CreateModule(_name, gs, _scriptBlock, null, out results, _arguments);

                        // Export all functions and variables if no exports were specified...
                        if (!localModule.SessionState.Internal.UseExportList)
                        {
                            List<WildcardPattern> cmdletPatterns = BaseCmdletPatterns ?? MatchAll;
                            List<WildcardPattern> functionPatterns = BaseFunctionPatterns ?? MatchAll;

                            ModuleIntrinsics.ExportModuleMembers(this,
                                localModule.SessionState.Internal,
                                functionPatterns, cmdletPatterns, BaseAliasPatterns, BaseVariablePatterns, null);
                        }
                    }
                    catch (RuntimeException e)
                    {
                        // Preserve the inner module invocation info...
                        e.ErrorRecord.PreserveInvocationInfoOnce = true;
                        WriteError(e.ErrorRecord);
                    }

                    // If the module was created successfully, then process the result...
                    if (localModule != null)
                    {
                        if (_returnResult)
                        {
                            // import the specified members...
                            ImportModuleMembers(localModule, string.Empty /* no -Prefix for New-Module cmdlet */);
                            WriteObject(results, true);
                        }
                        else if (_asCustomObject)
                        {
                            WriteObject(localModule.AsCustomObject());
                        }
                        else
                        {
                            // import the specified members...
                            ImportModuleMembers(localModule, string.Empty /* no -Prefix for New-Module cmdlet */);
                            WriteObject(localModule);
                        }
                    }
                }
                finally
                {
                    Context.Modules.DecrementModuleNestingCount();
                }
                return;
            }
        }
    }

    #endregion
} // Microsoft.PowerShell.Commands
