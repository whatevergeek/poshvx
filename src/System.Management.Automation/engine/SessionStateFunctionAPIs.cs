/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System.Collections;
using System.Collections.Generic;
using System.Management.Automation.Language;
using System.Management.Automation.Runspaces;
using Dbg = System.Management.Automation.Diagnostics;


namespace System.Management.Automation
{
    /// <summary>
    /// Holds the state of a Monad Shell session 
    /// </summary>
    internal sealed partial class SessionStateInternal
    {
        #region Functions

        /// <summary>
        /// Add an new SessionState function entry to this session state object...
        /// </summary>
        /// <param name="entry">The entry to add</param>
        internal void AddSessionStateEntry(SessionStateFunctionEntry entry)
        {
            ScriptBlock sb = entry.ScriptBlock.Clone();

            FunctionInfo fn = this.SetFunction(entry.Name, sb, null, entry.Options, false, CommandOrigin.Internal, this.ExecutionContext, entry.HelpFile, true);
            fn.Visibility = entry.Visibility;
            fn.Module = entry.Module;
            fn.ScriptBlock.LanguageMode = PSLanguageMode.FullLanguage;
        }

#if !CORECLR // Workflow Not Supported On CSS
        internal void AddSessionStateEntry(InitialSessionState initialSessionState, SessionStateWorkflowEntry entry)
        {
            var converterInstance = Utils.GetAstToWorkflowConverterAndEnsureWorkflowModuleLoaded(null);

            var workflowInfo = entry.WorkflowInfo ??
                               converterInstance.CompileWorkflow(entry.Name, entry.Definition, initialSessionState);

            WorkflowInfo wf = new WorkflowInfo(workflowInfo);

            wf = this.SetWorkflowRaw(wf, CommandOrigin.Internal);
            wf.Visibility = entry.Visibility;
            wf.Module = entry.Module;
        }
#endif

        /// <summary>
        /// Gets a flattened view of the functions that are visible using
        /// the current scope as a reference and filtering the functions in
        /// the other scopes based on the scoping rules.
        /// </summary>
        /// 
        /// <returns>
        /// An IDictionary representing the visible functions.
        /// </returns>
        /// 
        internal IDictionary GetFunctionTable()
        {
            SessionStateScopeEnumerator scopeEnumerator =
                new SessionStateScopeEnumerator(_currentScope);

            Dictionary<string, FunctionInfo> result =
                new Dictionary<string, FunctionInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (SessionStateScope scope in scopeEnumerator)
            {
                foreach (FunctionInfo entry in scope.FunctionTable.Values)
                {
                    if (!result.ContainsKey(entry.Name))
                    {
                        result.Add(entry.Name, entry);
                    }
                }
            }

            return result;
        } // GetFunctionTable

        /// <summary>
        /// Gets an IEnumerable for the function table for a given scope
        /// </summary>
        /// 
        /// <param name="scopeID">
        /// A scope identifier that is either one of the "special" scopes like
        /// "global", "script", "local", or "private, or a numeric ID of a relative scope
        /// to the current scope.
        /// </param>
        ///  
        /// <exception cref="ArgumentException">
        /// If <paramref name="scopeID"/> is less than zero, or not
        /// a number and not "script", "global", "local", or "private"
        /// </exception>
        /// 
        /// <exception cref="ArgumentOutOfRangeException">
        /// If <paramref name="scopeID"/> is less than zero or greater than the number of currently
        /// active scopes.
        /// </exception>
        /// 
        internal IDictionary<string, FunctionInfo> GetFunctionTableAtScope(string scopeID)
        {
            Dictionary<string, FunctionInfo> result =
                new Dictionary<string, FunctionInfo>(StringComparer.OrdinalIgnoreCase);

            SessionStateScope scope = GetScopeByID(scopeID);

            foreach (FunctionInfo entry in scope.FunctionTable.Values)
            {
                // Make sure the function/filter isn't private or if it is that the current
                // scope is the same scope the alias was retrieved from.

                if ((entry.Options & ScopedItemOptions.Private) == 0 ||
                    scope == _currentScope)
                {
                    result.Add(entry.Name, entry);
                }
            }

            return result;
        } // GetFunctionTableAtScope

        /// <summary>
        /// List of functions/filters to export from this session state object...
        /// </summary>
        internal List<FunctionInfo> ExportedFunctions { get; } = new List<FunctionInfo>();

        /// <summary>
        /// List of workflows to export from this session state object...
        /// </summary>
        internal List<WorkflowInfo> ExportedWorkflows { get; } = new List<WorkflowInfo>();

        internal bool UseExportList { get; set; } = false;

        /// <summary>
        /// Get a functions out of session state.
        /// </summary>
        /// 
        /// <param name="name">
        /// name of function to look up
        /// </param>
        /// 
        /// <param name="origin">
        /// Origin of the command that called this API...
        /// </param>
        /// 
        /// <returns>
        /// The value of the specified function.
        /// </returns>
        /// 
        /// <exception cref="ArgumentException">
        /// If <paramref name="name"/> is null or empty.
        /// </exception>
        /// 
        internal FunctionInfo GetFunction(string name, CommandOrigin origin)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }

            FunctionInfo result = null;

            FunctionLookupPath lookupPath = new FunctionLookupPath(name);

            FunctionScopeItemSearcher searcher =
                new FunctionScopeItemSearcher(this, lookupPath, origin);

            if (searcher.MoveNext())
            {
                result = ((IEnumerator<FunctionInfo>)searcher).Current;
            }
            return result;
        } // GetFunction

        /// <summary>
        /// Get a functions out of session state.
        /// </summary>
        /// 
        /// <param name="name">
        /// name of function to look up
        /// </param>
        /// 
        /// <returns>
        /// The value of the specified function.
        /// </returns>
        /// 
        /// <exception cref="ArgumentException">
        /// If <paramref name="name"/> is null or empty.
        /// </exception>
        /// 
        internal FunctionInfo GetFunction(string name)
        {
            return GetFunction(name, CommandOrigin.Internal);
        } // GetFunction 

        private IEnumerable<string> GetFunctionAliases(IParameterMetadataProvider ipmp)
        {
            if (ipmp == null || ipmp.Body.ParamBlock == null)
                yield break;

            var attributes = ipmp.Body.ParamBlock.Attributes;
            foreach (var attributeAst in attributes)
            {
                var attributeType = attributeAst.TypeName.GetReflectionAttributeType();
                if (attributeType == typeof(AliasAttribute))
                {
                    var cvv = new ConstantValueVisitor { AttributeArgument = true };
                    for (int i = 0; i < attributeAst.PositionalArguments.Count; i++)
                    {
                        yield return Compiler._attrArgToStringConverter.Target(Compiler._attrArgToStringConverter,
                            attributeAst.PositionalArguments[i].Accept(cvv));
                    }
                }
            }
        }

        /// <summary>
        /// Set a function in the current scope of session state.
        /// </summary>
        /// 
        /// <param name="name">
        /// The name of the function to set.
        /// </param>
        /// 
        /// <param name="function">
        /// The new value of the function being set.
        /// </param>
        /// 
        /// <param name="origin">
        /// Origin of the caller of this API
        /// </param>
        /// 
        /// <exception cref="ArgumentException">
        /// If <paramref name="name"/> is null or empty.
        /// </exception>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="function"/> is null.
        /// </exception>
        /// 
        /// <exception cref="SessionStateUnauthorizedAccessException">
        /// If the function is read-only or constant.
        /// </exception> 
        /// 
        /// <exception cref="SessionStateOverflowException">
        /// If the maximum number of functions have been reached for this scope.
        /// </exception>
        /// 
        internal FunctionInfo SetFunctionRaw(
            string name,
            ScriptBlock function,
            CommandOrigin origin)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }

            if (function == null)
            {
                throw PSTraceSource.NewArgumentNullException("function");
            }

            string originalName = name;

            FunctionLookupPath path = new FunctionLookupPath(name);
            name = path.UnqualifiedPath;

            if (String.IsNullOrEmpty(name))
            {
                SessionStateException exception =
                    new SessionStateException(
                        originalName,
                        SessionStateCategory.Function,
                        "ScopedFunctionMustHaveName",
                        SessionStateStrings.ScopedFunctionMustHaveName,
                        ErrorCategory.InvalidArgument);

                throw exception;
            }

            ScopedItemOptions options = ScopedItemOptions.None;
            if (path.IsPrivate)
            {
                options |= ScopedItemOptions.Private;
            }

            FunctionScopeItemSearcher searcher =
                new FunctionScopeItemSearcher(
                    this,
                    path,
                    origin);

            var functionInfo = searcher.InitialScope.SetFunction(name, function, null, options, false, origin, ExecutionContext);

            foreach (var aliasName in GetFunctionAliases(function.Ast as IParameterMetadataProvider))
            {
                searcher.InitialScope.SetAliasValue(aliasName, name, ExecutionContext, false, origin);
            }

            return functionInfo;
        } // SetFunctionRaw

        internal WorkflowInfo SetWorkflowRaw(
            WorkflowInfo workflowInfo,
            CommandOrigin origin)
        {
            string originalName = workflowInfo.Name;
            string name = originalName;

            FunctionLookupPath path = new FunctionLookupPath(name);
            name = path.UnqualifiedPath;

            if (String.IsNullOrEmpty(name))
            {
                SessionStateException exception =
                    new SessionStateException(
                        originalName,
                        SessionStateCategory.Function,
                        "ScopedFunctionMustHaveName",
                        SessionStateStrings.ScopedFunctionMustHaveName,
                        ErrorCategory.InvalidArgument);

                throw exception;
            }

            ScopedItemOptions options = ScopedItemOptions.None;
            if (path.IsPrivate)
            {
                options |= ScopedItemOptions.Private;
            }

            FunctionScopeItemSearcher searcher =
                new FunctionScopeItemSearcher(
                    this,
                    path,
                    origin);

            // The script that defines a workflowInfo wrapper is fully trusted
            workflowInfo.ScriptBlock.LanguageMode = PSLanguageMode.FullLanguage;

            if (workflowInfo.Module == null && this.Module != null)
            {
                workflowInfo.Module = this.Module;
            }

            var wfInfo = (WorkflowInfo)
                searcher.InitialScope.SetFunction(name, workflowInfo.ScriptBlock, null, options, false, origin, ExecutionContext, null,
                                                     (arg1, arg2, arg3, arg4, arg5, arg6) => workflowInfo);
            foreach (var aliasName in GetFunctionAliases(workflowInfo.ScriptBlock.Ast as IParameterMetadataProvider))
            {
                searcher.InitialScope.SetAliasValue(aliasName, name, ExecutionContext, false, origin);
            }

            return wfInfo;
        } // SetWorkflowRaw

        /// <summary>
        /// Set a function in the current scope of session state.
        /// </summary>
        /// 
        /// <param name="name">
        /// The name of the function to set.
        /// </param>
        /// 
        /// <param name="function">
        /// The new value of the function being set.
        /// </param>
        /// 
        /// <param name="originalFunction">
        /// The original function (if any) from which the ScriptBlock is derived.
        /// </param>
        /// 
        /// <param name="options">
        /// The options to set on the function.
        /// </param>
        /// 
        /// <param name="force">
        /// If true, the function will be set even if its ReadOnly.
        /// </param>
        /// 
        /// <param name="origin">
        /// Origin of the caller of this API
        /// </param>
        /// <exception cref="ArgumentException">
        /// If <paramref name="name"/> is null or empty.
        /// </exception>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="function"/> is null.
        /// </exception>
        /// 
        /// <exception cref="SessionStateUnauthorizedAccessException">
        /// If the function is read-only or constant.
        /// </exception> 
        /// 
        /// <exception cref="SessionStateOverflowException">
        /// If the maximum number of functions have been reached for this scope.
        /// </exception>
        /// 
        internal FunctionInfo SetFunction(
            string name,
            ScriptBlock function,
            FunctionInfo originalFunction,
            ScopedItemOptions options,
            bool force,
            CommandOrigin origin)
        {
            return SetFunction(name, function, originalFunction, options, force, origin, ExecutionContext, null);
        } // SetFunction

        /// <summary>
        /// Set a function in the current scope of session state.
        /// </summary>
        /// 
        /// <param name="name">
        /// The name of the function to set.
        /// </param>
        /// 
        /// <param name="function">
        /// The new value of the function being set.
        /// </param>
        /// 
        /// <param name="originalFunction">
        /// The original function (if any) from which the ScriptBlock is derived.
        /// </param>
        /// 
        /// <param name="options">
        /// The options to set on the function.
        /// </param>
        /// 
        /// <param name="force">
        /// If true, the function will be set even if its ReadOnly.
        /// </param>
        /// 
        /// <param name="origin">
        /// Origin of the caller of this API
        /// </param>
        /// 
        /// <param name="helpFile">
        /// The name of the help file associated with the function.
        /// </param>
        /// 
        /// <exception cref="ArgumentException">
        /// If <paramref name="name"/> is null or empty.
        /// </exception>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="function"/> is null.
        /// </exception>
        /// 
        /// <exception cref="SessionStateUnauthorizedAccessException">
        /// If the function is read-only or constant.
        /// </exception> 
        /// 
        /// <exception cref="SessionStateOverflowException">
        /// If the maximum number of functions have been reached for this scope.
        /// </exception>
        /// 
        internal FunctionInfo SetFunction(
            string name,
            ScriptBlock function,
            FunctionInfo originalFunction,
            ScopedItemOptions options,
            bool force,
            CommandOrigin origin,
            string helpFile)
        {
            return SetFunction(name, function, originalFunction, options, force, origin, ExecutionContext, helpFile, false);
        } // SetFunction

        /// <summary>
        /// Set a function in the current scope of session state.
        /// </summary>
        /// 
        /// <param name="name">
        /// The name of the function to set.
        /// </param>
        /// 
        /// <param name="function">
        /// The new value of the function being set.
        /// </param>
        /// 
        /// <param name="originalFunction">
        /// The original function (if any) from which the ScriptBlock is derived.
        /// </param>
        /// 
        /// <param name="options">
        /// The options to set on the function.
        /// </param>
        /// 
        /// <param name="force">
        /// If true, the function will be set even if its ReadOnly.
        /// </param>
        /// 
        /// <param name="origin">
        /// Origin of the caller of this API
        /// </param>
        /// 
        /// <param name="context">
        /// The execution context for the function.
        /// </param>
        /// 
        /// <param name="helpFile">
        /// The name of the help file associated with the function.
        /// </param> 
        /// <exception cref="ArgumentException">
        /// If <paramref name="name"/> is null or empty.
        /// </exception>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="function"/> is null.
        /// </exception>
        /// 
        /// <exception cref="SessionStateUnauthorizedAccessException">
        /// If the function is read-only or constant.
        /// </exception> 
        /// 
        /// <exception cref="SessionStateOverflowException">
        /// If the maximum number of functions have been reached for this scope.
        /// </exception>
        /// 
        internal FunctionInfo SetFunction(
            string name,
            ScriptBlock function,
            FunctionInfo originalFunction,
            ScopedItemOptions options,
            bool force,
            CommandOrigin origin,
            ExecutionContext context,
            string helpFile)
        {
            return SetFunction(name, function, originalFunction, options, force, origin, context, helpFile, false);
        } // SetFunction

        /// <summary>
        /// Set a function in the current scope of session state.
        /// </summary>
        /// 
        /// <param name="name">
        /// The name of the function to set.
        /// </param>
        /// 
        /// <param name="function">
        /// The new value of the function being set.
        /// </param>
        /// 
        /// <param name="originalFunction">
        /// The original function (if any) from which the ScriptBlock is derived.
        /// </param>
        /// 
        /// <param name="options">
        /// The options to set on the function.
        /// </param>
        /// 
        /// <param name="force">
        /// If true, the function will be set even if its ReadOnly.
        /// </param>
        /// 
        /// <param name="origin">
        /// Origin of the caller of this API
        /// </param>
        /// 
        /// <param name="context">
        /// The execution context for the function.
        /// </param>
        /// 
        /// <param name="helpFile">
        /// The name of the help file associated with the function.
        /// </param>
        /// 
        /// <param name="isPreValidated">
        /// Set to true if it is a regular function (meaning, we do not need to check this is a workflow or if the script contains JobDefinition Attribute and then process it)
        /// </param>
        /// 
        /// <exception cref="ArgumentException">
        /// If <paramref name="name"/> is null or empty.
        /// </exception>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="function"/> is null.
        /// </exception>
        /// 
        /// <exception cref="SessionStateUnauthorizedAccessException">
        /// If the function is read-only or constant.
        /// </exception> 
        /// 
        /// <exception cref="SessionStateOverflowException">
        /// If the maximum number of functions have been reached for this scope.
        /// </exception>
        /// 
        internal FunctionInfo SetFunction(
            string name,
            ScriptBlock function,
            FunctionInfo originalFunction,
            ScopedItemOptions options,
            bool force,
            CommandOrigin origin,
            ExecutionContext context,
            string helpFile,
            bool isPreValidated)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }

            if (function == null)
            {
                throw PSTraceSource.NewArgumentNullException("function");
            }

            string originalName = name;

            FunctionLookupPath path = new FunctionLookupPath(name);
            name = path.UnqualifiedPath;

            if (String.IsNullOrEmpty(name))
            {
                SessionStateException exception =
                    new SessionStateException(
                        originalName,
                        SessionStateCategory.Function,
                        "ScopedFunctionMustHaveName",
                        SessionStateStrings.ScopedFunctionMustHaveName,
                        ErrorCategory.InvalidArgument);

                throw exception;
            }

            if (path.IsPrivate)
            {
                options |= ScopedItemOptions.Private;
            }


            FunctionScopeItemSearcher searcher =
                new FunctionScopeItemSearcher(
                    this,
                    path,
                    origin);

            return searcher.InitialScope.SetFunction(name, function, originalFunction, options, force, origin, context, helpFile);
        } // SetFunction

        /// <summary>
        /// Set a function in the current scope of session state.
        /// </summary>
        /// 
        /// <param name="name">
        /// The name of the function to set.
        /// </param>
        /// 
        /// <param name="function">
        /// The new value of the function being set.
        /// </param>
        /// 
        /// <param name="originalFunction">
        /// The original function (if any) from which the ScriptBlock is derived.
        /// </param>
        /// 
        /// <param name="force">
        /// If true, the function will be set even if its ReadOnly.
        /// </param>
        /// 
        /// <param name="origin">
        /// The origin of the caller
        /// </param>
        /// 
        /// <exception cref="ArgumentException">
        /// If <paramref name="name"/> is null or empty.
        /// or
        /// If <paramref name="function"/> is not a <see cref="FilterInfo">FilterInfo</see>
        /// or <see cref="FunctionInfo">FunctionInfo</see>
        /// </exception>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="function"/> is null.
        /// </exception>
        /// 
        /// <exception cref="SessionStateUnauthorizedAccessException">
        /// If the function is read-only or constant.
        /// </exception> 
        /// 
        /// <exception cref="SessionStateOverflowException">
        /// If the maximum number of functions have been reached for this scope.
        /// </exception>
        /// 
        internal FunctionInfo SetFunction(
            string name,
            ScriptBlock function,
            FunctionInfo originalFunction,
            bool force,
            CommandOrigin origin)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }

            if (function == null)
            {
                throw PSTraceSource.NewArgumentNullException("function");
            }

            string originalName = name;

            FunctionLookupPath path = new FunctionLookupPath(name);
            name = path.UnqualifiedPath;

            if (String.IsNullOrEmpty(name))
            {
                SessionStateException exception =
                    new SessionStateException(
                        originalName,
                        SessionStateCategory.Function,
                        "ScopedFunctionMustHaveName",
                        SessionStateStrings.ScopedFunctionMustHaveName,
                        ErrorCategory.InvalidArgument);

                throw exception;
            }

            ScopedItemOptions options = ScopedItemOptions.None;
            if (path.IsPrivate)
            {
                options |= ScopedItemOptions.Private;
            }


            FunctionScopeItemSearcher searcher =
                new FunctionScopeItemSearcher(
                    this,
                    path,
                    origin);

            FunctionInfo result = null;

            SessionStateScope scope = searcher.InitialScope;

            if (searcher.MoveNext())
            {
                scope = searcher.CurrentLookupScope;
                name = searcher.Name;

                if (path.IsPrivate)
                {
                    // Need to add the Private flag
                    FunctionInfo existingFunction = scope.GetFunction(name);
                    options |= existingFunction.Options;
                    result = scope.SetFunction(name, function, originalFunction, options, force, origin, ExecutionContext);
                }
                else
                {
                    result = scope.SetFunction(name, function, force, origin, ExecutionContext);
                }
            }
            else
            {
                if (path.IsPrivate)
                {
                    result = scope.SetFunction(name, function, originalFunction, options, force, origin, ExecutionContext);
                }
                else
                {
                    result = scope.SetFunction(name, function, force, origin, ExecutionContext);
                }
            }
            return result;
        }

        /// <summary>
        /// Set a function in the current scope of session state.
        /// 
        /// BUGBUG: this overload is preserved because a lot of tests use reflection to
        /// call it. The tests should be fixed and this API eventually removed.
        /// </summary>
        /// 
        /// <param name="name">
        /// The name of the function to set.
        /// </param>
        /// 
        /// <param name="function">
        /// The new value of the function being set.
        /// </param>
        /// 
        /// <param name="force">
        /// If true, the function will be set even if its ReadOnly.
        /// </param>
        /// 
        /// <exception cref="ArgumentException">
        /// If <paramref name="name"/> is null or empty.
        /// or
        /// If <paramref name="function"/> is not a <see cref="FilterInfo">FilterInfo</see>
        /// or <see cref="FunctionInfo">FunctionInfo</see>
        /// </exception>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="function"/> is null.
        /// </exception>
        /// 
        /// <exception cref="SessionStateUnauthorizedAccessException">
        /// If the function is read-only or constant.
        /// </exception> 
        /// 
        /// <exception cref="SessionStateOverflowException">
        /// If the maximum number of functions have been reached for this scope.
        /// </exception>
        /// 
        internal FunctionInfo SetFunction(string name, ScriptBlock function, bool force)
        {
            return SetFunction(name, function, null, force, CommandOrigin.Internal);
        }

        /// <summary>
        /// Removes a function from the function table.
        /// </summary>
        /// 
        /// <param name="name">
        /// The name of the function to remove.
        /// </param>
        /// 
        /// <param name="origin">
        /// THe origin of the caller of this API
        /// </param>
        /// 
        /// <param name="force">
        /// If true, the function is removed even if it is ReadOnly.
        /// </param>
        /// 
        /// <exception cref="ArgumentException">
        /// If <paramref name="name"/> is null or empty.
        /// </exception>
        /// 
        /// <exception cref="SessionStateUnauthorizedAccessException">
        /// If the function is constant.
        /// </exception> 
        /// 
        internal void RemoveFunction(string name, bool force, CommandOrigin origin)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }

            // Use the scope enumerator to find an existing function

            SessionStateScope scope = _currentScope;

            FunctionLookupPath path = new FunctionLookupPath(name);

            FunctionScopeItemSearcher searcher =
                new FunctionScopeItemSearcher(
                    this,
                    path,
                    origin);

            if (searcher.MoveNext())
            {
                scope = searcher.CurrentLookupScope;
            }
            scope.RemoveFunction(name, force);
        } // RemoveFunction

        /// <summary>
        /// Removes a function from the function table.
        /// </summary>
        /// 
        /// <param name="name">
        /// The name of the function to remove.
        /// </param>
        /// 
        /// <param name="force">
        /// If true, the function is removed even if it is ReadOnly.
        /// </param>
        /// 
        /// <exception cref="ArgumentException">
        /// If <paramref name="name"/> is null or empty.
        /// </exception>
        /// 
        /// <exception cref="SessionStateUnauthorizedAccessException">
        /// If the function is constant.
        /// </exception> 
        /// 
        internal void RemoveFunction(string name, bool force)
        {
            RemoveFunction(name, force, CommandOrigin.Internal);
        }

        /// <summary>
        /// Removes a function from the function table 
        /// if the function was imported from the given module.
        /// 
        /// BUGBUG: This is only used by the implicit remoting functions...
        /// </summary>
        /// 
        /// <param name="name">
        /// The name of the function to remove.
        /// </param>
        /// 
        /// <param name="module">
        /// Module the function might be imported from.
        /// </param>
        /// 
        /// <exception cref="SessionStateUnauthorizedAccessException">
        /// If the function is constant.
        /// </exception> 
        /// 
        internal void RemoveFunction(string name, PSModuleInfo module)
        {
            Dbg.Assert(module != null, "Caller should verify that module parameter is not null");

            FunctionInfo func = GetFunction(name) as FunctionInfo;
            if (func != null && func.ScriptBlock != null
                && func.ScriptBlock.File != null
                && func.ScriptBlock.File.Equals(module.Path, StringComparison.OrdinalIgnoreCase))
            {
                RemoveFunction(name, true);
            }
        }

        #endregion Functions
    } // SessionStateInternal class
}