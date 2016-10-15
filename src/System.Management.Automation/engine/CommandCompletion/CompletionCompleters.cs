
/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System.Collections;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management.Automation.Internal;
using System.Management.Automation.Language;
using System.Collections.Generic;
using System.Management.Automation.Runspaces;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Options;
using Microsoft.PowerShell;
using Microsoft.PowerShell.Cim;
using Microsoft.PowerShell.Commands;

namespace System.Management.Automation
{
    /// <summary>
    /// 
    /// </summary>
    public static class CompletionCompleters
    {
        static CompletionCompleters()
        {
#if CORECLR
            ClrFacade.AddAssemblyLoadHandler(UpdateTypeCacheOnAssemblyLoad);
#else
            AppDomain.CurrentDomain.AssemblyLoad += UpdateTypeCacheOnAssemblyLoad;
#endif
        }

#if CORECLR
        static void UpdateTypeCacheOnAssemblyLoad(Assembly loadedAssembly)
#else
        private static void UpdateTypeCacheOnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
#endif
        {
            // Just null out the cache - we'll rebuild it the next time someone tries to complete a type.
            // We could rebuild it now, but we could be loading multiple assemblies (e.g. dependent assemblies)
            // and there is no sense in rebuilding anything until we're done loading all of the assemblies.
            Interlocked.Exchange(ref s_typeCache, null);
        }

        #region Command Names

        /// <summary>
        /// 
        /// </summary>
        /// <param name="commandName"></param>
        /// <returns></returns>
        public static IEnumerable<CompletionResult> CompleteCommand(string commandName)
        {
            return CompleteCommand(commandName, null);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="commandName"></param>
        /// <param name="moduleName"></param>
        /// <param name="commandTypes"></param>
        /// <returns></returns>
        [SuppressMessage("Microsoft.Design", "CA1026:DefaultParametersShouldNotBeUsed")]
        public static IEnumerable<CompletionResult> CompleteCommand(string commandName, string moduleName, CommandTypes commandTypes = CommandTypes.All)
        {
            var runspace = Runspace.DefaultRunspace;
            if (runspace == null)
            {
                // No runspace, just return no results.
                return CommandCompletion.EmptyCompletionResult;
            }

            var helper = new CompletionExecutionHelper(PowerShell.Create(RunspaceMode.CurrentRunspace));
            return CompleteCommand(new CompletionContext { WordToComplete = commandName, Helper = helper }, moduleName, commandTypes);
        }

        internal static List<CompletionResult> CompleteCommand(CompletionContext context)
        {
            return CompleteCommand(context, null);
        }

        private static List<CompletionResult> CompleteCommand(CompletionContext context, string moduleName, CommandTypes types = CommandTypes.All)
        {
            var addAmpersandIfNecessary = IsAmpersandNeeded(context, false);

            string commandName = context.WordToComplete;
            string quote = HandleDoubleAndSingleQuote(ref commandName);

            commandName += "*";
            List<CompletionResult> commandResults = null;

            if (commandName.IndexOfAny(Utils.Separators.DirectoryOrDrive) == -1)
            {
                // The name to complete is neither module qualified nor is it a relative/rooted file path.

                Ast lastAst = null;
                if (context.RelatedAsts != null && context.RelatedAsts.Count > 0)
                {
                    lastAst = context.RelatedAsts.Last();
                }

                var powershell = context.Helper.CurrentPowerShell;
                AddCommandWithPreferenceSetting(powershell, "Get-Command", typeof(GetCommandCommand))
                    .AddParameter("All")
                    .AddParameter("Name", commandName);

                if (moduleName != null)
                    powershell.AddParameter("Module", moduleName);
                if (!types.Equals(CommandTypes.All))
                    powershell.AddParameter("CommandType", types);

                Exception exceptionThrown;
                var commandInfos = context.Helper.ExecuteCurrentPowerShell(out exceptionThrown);

                // Complete against pseudo commands that work only in the script workflow.
                // It's for argument completion when RelatedAst is null, don't complete pseudo commands for arguments
                if (lastAst != null)
                {
                    commandInfos = CompleteWorkflowCommand(commandName, lastAst, commandInfos);
                }

                if (commandInfos != null && commandInfos.Count > 1)
                {
                    // OrderBy is using stable sorting
                    var sortedCommandInfos = commandInfos.OrderBy(a => a, new CommandNameComparer());
                    commandResults = MakeCommandsUnique(sortedCommandInfos, false, addAmpersandIfNecessary, quote, context);
                }
                else
                {
                    commandResults = MakeCommandsUnique(commandInfos, false, addAmpersandIfNecessary, quote, context);
                }

                if (lastAst != null)
                {
                    // Search the asts for function definitions that we might be calling
                    var findFunctionsVisitor = new FindFunctionsVisitor();
                    while (lastAst.Parent != null)
                    {
                        lastAst = lastAst.Parent;
                    }
                    lastAst.Visit(findFunctionsVisitor);

                    WildcardPattern commandNamePattern = WildcardPattern.Get(commandName, WildcardOptions.IgnoreCase);
                    foreach (var defn in findFunctionsVisitor.FunctionDefinitions)
                    {
                        if (commandNamePattern.IsMatch(defn.Name)
                            && !commandResults.Where(cr => cr.CompletionText.Equals(defn.Name, StringComparison.OrdinalIgnoreCase)).Any())
                        {
                            // Results found in the current script are prepended to show up at the top of the list.
                            commandResults.Insert(0, GetCommandNameCompletionResult(defn.Name, defn, addAmpersandIfNecessary, quote));
                        }
                    }
                }
            }
            else
            {
                // If there is a single \, we might be looking for a module/snapin qualified command
                var indexOfFirstColon = commandName.IndexOf(':');
                var indexOfFirstBackslash = commandName.IndexOf('\\');
                if (indexOfFirstBackslash > 0 && (indexOfFirstBackslash < indexOfFirstColon || indexOfFirstColon == -1))
                {
                    // First try the name before the backslash as a module name.
                    // Use the exact module name provided by the user
                    moduleName = commandName.Substring(0, indexOfFirstBackslash);
                    commandName = commandName.Substring(indexOfFirstBackslash + 1);

                    var powershell = context.Helper.CurrentPowerShell;
                    AddCommandWithPreferenceSetting(powershell, "Get-Command", typeof(GetCommandCommand))
                        .AddParameter("All")
                        .AddParameter("Name", commandName)
                        .AddParameter("Module", moduleName);

                    if (!types.Equals(CommandTypes.All))
                        powershell.AddParameter("CommandType", types);

                    Exception exceptionThrown;
                    var commandInfos = context.Helper.ExecuteCurrentPowerShell(out exceptionThrown);

                    if (commandInfos != null && commandInfos.Count > 1)
                    {
                        var sortedCommandInfos = commandInfos.OrderBy(a => a, new CommandNameComparer());
                        commandResults = MakeCommandsUnique(sortedCommandInfos, true, addAmpersandIfNecessary, quote, context);
                    }
                    else
                    {
                        commandResults = MakeCommandsUnique(commandInfos, true, addAmpersandIfNecessary, quote, context);
                    }
                }
            }

            return commandResults;
        }

        private static readonly HashSet<string> s_keywordsToExcludeFromAddingAmpersand
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { TokenKind.InlineScript.ToString(), TokenKind.Configuration.ToString() };
        internal static CompletionResult GetCommandNameCompletionResult(string name, object command, bool addAmpersandIfNecessary, string quote)
        {
            string syntax = name, listItem = name;

            var commandInfo = command as CommandInfo;
            if (commandInfo != null)
            {
                try
                {
                    listItem = commandInfo.Name;
                    // This may require parsing a script, which could fail in a number of different ways
                    // (syntax errors, security exceptions, etc.)  If so, the name is fine for the tooltip.
                    syntax = commandInfo.Syntax;
                }
                catch (Exception e)
                {
                    CommandProcessorBase.CheckForSevereException(e);
                }
            }

            syntax = string.IsNullOrEmpty(syntax) ? name : syntax;
            bool needAmpersand;

            if (CompletionRequiresQuotes(name, false))
            {
                needAmpersand = quote == string.Empty && addAmpersandIfNecessary;
                string quoteInUse = quote == string.Empty ? "'" : quote;
                if (quoteInUse == "'")
                {
                    name = name.Replace("'", "''");
                }
                else
                {
                    name = name.Replace("`", "``");
                    name = name.Replace("$", "`$");
                }
                name = quoteInUse + name + quoteInUse;
            }
            else
            {
                needAmpersand = quote == string.Empty && addAmpersandIfNecessary &&
                                Tokenizer.IsKeyword(name) && !s_keywordsToExcludeFromAddingAmpersand.Contains(name);
                name = quote + name + quote;
            }

            // It's useless to call ForEach-Object (foreach) as the first command of a pipeline. For example:
            //     PS C:\> fore<tab>  --->   PS C:\> foreach   (expected, use as the keyword)
            //     PS C:\> fore<tab>  --->   PS C:\> & foreach (unexpected, ForEach-Object is seldom used as the first command of a pipeline)
            if (needAmpersand && name != SpecialVariables.@foreach)
            {
                name = "& " + name;
            }
            return new CompletionResult(name, listItem, CompletionResultType.Command, syntax);
        }

        internal static List<CompletionResult> MakeCommandsUnique(IEnumerable<PSObject> commandInfoPsObjs, bool includeModulePrefix, bool addAmpersandIfNecessary, string quote, CompletionContext context)
        {
            List<CompletionResult> results = new List<CompletionResult>();
            if (commandInfoPsObjs == null || !commandInfoPsObjs.Any())
            {
                return results;
            }

            var commandTable = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var psobj in commandInfoPsObjs)
            {
                object baseObj = PSObject.Base(psobj);
                string name = null;

                var commandInfo = baseObj as CommandInfo;
                if (commandInfo != null)
                {
                    // Skip the private commands
                    if (commandInfo.Visibility == SessionStateEntryVisibility.Private) { continue; }
                    name = commandInfo.Name;
                    if (includeModulePrefix && !string.IsNullOrEmpty(commandInfo.ModuleName))
                    {
                        // The command might be a prefixed commandInfo that we get by importing a module with the -Prefix parameter, for example:
                        //    FooModule.psm1: Get-Foo
                        //    import-module FooModule -Prefix PowerShell
                        //    --> command 'Get-PowerShellFoo' in the global session state (prefixed commandInfo)
                        //        command 'Get-Foo' in the module session state (un-prefixed commandInfo)
                        // in that case, we should not add the module name qualification because it doesn't work
                        if (String.IsNullOrEmpty(commandInfo.Prefix) || !ModuleCmdletBase.IsPrefixedCommand(commandInfo))
                        {
                            name = commandInfo.ModuleName + "\\" + commandInfo.Name;
                        }
                    }
                }
                else
                {
                    name = baseObj as string;
                    if (name == null) { continue; }
                }

                object value;
                if (!commandTable.TryGetValue(name, out value))
                {
                    commandTable.Add(name, baseObj);
                }
                else
                {
                    var list = value as List<object>;
                    if (list != null)
                    {
                        list.Add(baseObj);
                    }
                    else
                    {
                        list = new List<object> { value, baseObj };
                        commandTable[name] = list;
                    }
                }
            }

            List<CompletionResult> endResults = null;
            foreach (var keyValuePair in commandTable)
            {
                var commandList = keyValuePair.Value as List<object>;
                if (commandList != null)
                {
                    if (endResults == null)
                    {
                        endResults = new List<CompletionResult>();
                    }

                    // The first command might be an un-prefixed commandInfo that we get by importing a module with the -Prefix parameter,
                    // in that case, we should add the module name qualification because if the module is not in the module path, calling 
                    // 'Get-Foo' directly doesn't work
                    string completionName = keyValuePair.Key;
                    if (!includeModulePrefix)
                    {
                        var commandInfo = commandList[0] as CommandInfo;
                        if (commandInfo != null && !String.IsNullOrEmpty(commandInfo.Prefix))
                        {
                            Diagnostics.Assert(!String.IsNullOrEmpty(commandInfo.ModuleName), "the module name should exist if commandInfo.Prefix is not an empty string");
                            if (!ModuleCmdletBase.IsPrefixedCommand(commandInfo))
                            {
                                completionName = commandInfo.ModuleName + "\\" + completionName;
                            }
                        }
                    }
                    results.Add(GetCommandNameCompletionResult(completionName, commandList[0], addAmpersandIfNecessary, quote));

                    // For the other commands that are hidden, we need to disambiguate,
                    // but put these at the end as it's less likely any of the hidden
                    // commands are desired.  If we can't add anything to disambiguate,
                    // then we'll skip adding a completion result.
                    for (int index = 1; index < commandList.Count; index++)
                    {
                        var commandInfo = commandList[index] as CommandInfo;
                        // If it's a pseudo command that only works in the script workflow, don't bother adding it to the result 
                        // list since it's a duplicate
                        if (commandInfo == null) { continue; }

                        if (commandInfo.CommandType == CommandTypes.Application)
                        {
                            endResults.Add(GetCommandNameCompletionResult(commandInfo.Definition, commandInfo, addAmpersandIfNecessary, quote));
                        }
                        else if (!string.IsNullOrEmpty(commandInfo.ModuleName))
                        {
                            var name = commandInfo.ModuleName + "\\" + commandInfo.Name;
                            endResults.Add(GetCommandNameCompletionResult(name, commandInfo, addAmpersandIfNecessary, quote));
                        }
                    }
                }
                else
                {
                    // The first command might be an un-prefixed commandInfo that we get by importing a module with the -Prefix parameter,
                    // in that case, we should add the module name qualification because if the module is not in the module path, calling 
                    // 'Get-Foo' directly doesn't work
                    string completionName = keyValuePair.Key;
                    if (!includeModulePrefix)
                    {
                        var commandInfo = keyValuePair.Value as CommandInfo;
                        if (commandInfo != null && !String.IsNullOrEmpty(commandInfo.Prefix))
                        {
                            Diagnostics.Assert(!String.IsNullOrEmpty(commandInfo.ModuleName), "the module name should exist if commandInfo.Prefix is not an empty string");
                            if (!ModuleCmdletBase.IsPrefixedCommand(commandInfo))
                            {
                                completionName = commandInfo.ModuleName + "\\" + completionName;
                            }
                        }
                    }
                    results.Add(GetCommandNameCompletionResult(completionName, keyValuePair.Value, addAmpersandIfNecessary, quote));
                }
            }

            if (endResults != null && endResults.Count > 0)
            {
                results.AddRange(endResults);
            }

            return results;
        }

        /// <summary>
        /// Contains the pseudo commands that only work in the script workflow.
        /// </summary>
        internal static readonly List<string> PseudoWorkflowCommands
            = new List<string> { "Checkpoint-Workflow", "Suspend-Workflow", "InlineScript" };
        private static Collection<PSObject> CompleteWorkflowCommand(string command, Ast lastAst, Collection<PSObject> commandInfos)
        {
            if (!lastAst.IsInWorkflow())
                return commandInfos;

            commandInfos = commandInfos ?? new Collection<PSObject>();
            var commandPattern = WildcardPattern.Get(command, WildcardOptions.IgnoreCase);

            foreach (string pseudoCommand in PseudoWorkflowCommands)
            {
                if (!commandPattern.IsMatch(pseudoCommand))
                    continue;

                commandInfos.Add(PSObject.AsPSObject(pseudoCommand));
            }

            return commandInfos;
        }

        private class FindFunctionsVisitor : AstVisitor
        {
            internal readonly List<FunctionDefinitionAst> FunctionDefinitions = new List<FunctionDefinitionAst>();

            public override AstVisitAction VisitFunctionDefinition(FunctionDefinitionAst functionDefinitionAst)
            {
                FunctionDefinitions.Add(functionDefinitionAst);
                return AstVisitAction.Continue;
            }
        }

        #endregion Command Names

        #region Module Names

        internal static List<CompletionResult> CompleteModuleName(CompletionContext context, bool loadedModulesOnly)
        {
            var moduleName = context.WordToComplete ?? string.Empty;
            var result = new List<CompletionResult>();
            var quote = HandleDoubleAndSingleQuote(ref moduleName);

            if (!moduleName.EndsWith("*", StringComparison.Ordinal))
            {
                moduleName += "*";
            }

            var powershell = context.Helper.CurrentPowerShell;
            AddCommandWithPreferenceSetting(powershell, "Get-Module", typeof(GetModuleCommand)).AddParameter("Name", moduleName);
            if (!loadedModulesOnly)
            {
                powershell.AddParameter("ListAvailable", true);
            }

            Exception exceptionThrown;
            var psObjects = context.Helper.ExecuteCurrentPowerShell(out exceptionThrown);

            if (psObjects != null)
            {
                foreach (dynamic moduleInfo in psObjects)
                {
                    var completionText = moduleInfo.Name.ToString();
                    var listItemText = completionText;
                    var toolTip = "Description: " + moduleInfo.Description.ToString() + "\r\nModuleType: "
                                  + moduleInfo.ModuleType.ToString() + "\r\nPath: "
                                  + moduleInfo.Path.ToString();

                    if (CompletionRequiresQuotes(completionText, false))
                    {
                        var quoteInUse = quote == string.Empty ? "'" : quote;
                        if (quoteInUse == "'")
                            completionText = completionText.Replace("'", "''");
                        completionText = quoteInUse + completionText + quoteInUse;
                    }
                    else
                    {
                        completionText = quote + completionText + quote;
                    }

                    result.Add(new CompletionResult(completionText, listItemText, CompletionResultType.ParameterValue, toolTip));
                }
            }

            return result;
        }

        #endregion Module Names

        #region Command Parameters
        private static string[] s_parameterNamesOfImportDSCResource = { "Name", "ModuleName", "ModuleVersion" };

        internal static List<CompletionResult> CompleteCommandParameter(CompletionContext context)
        {
            string partialName = null;
            bool withColon = false;
            CommandAst commandAst = null;
            List<CompletionResult> result = new List<CompletionResult>();

            // Find the parameter ast, it will be near or at the end
            CommandParameterAst parameterAst = null;
            DynamicKeywordStatementAst keywordAst = null;
            for (int i = context.RelatedAsts.Count - 1; i >= 0; i--)
            {
                if (keywordAst == null)
                    keywordAst = context.RelatedAsts[i] as DynamicKeywordStatementAst;
                parameterAst = (context.RelatedAsts[i] as CommandParameterAst);
                if (parameterAst != null) break;
            }

            if (parameterAst != null)
            {
                keywordAst = parameterAst.Parent as DynamicKeywordStatementAst;
            }

            // If parent is DynamicKeywordStatementAst - 'Import-DscResource',
            // then customize the auto completion results
            if (keywordAst != null && String.Equals(keywordAst.Keyword.Keyword, "Import-DscResource", StringComparison.OrdinalIgnoreCase)
                && !String.IsNullOrWhiteSpace(context.WordToComplete) && context.WordToComplete.StartsWith("-", StringComparison.OrdinalIgnoreCase))
            {
                var lastAst = context.RelatedAsts.Last();
                var wordToMatch = context.WordToComplete.Substring(1) + "*";
                var pattern = WildcardPattern.Get(wordToMatch, WildcardOptions.IgnoreCase);
                var parameterNames = keywordAst.CommandElements.Where(ast => ast is CommandParameterAst).Select(ast => (ast as CommandParameterAst).ParameterName);
                foreach (var parameterName in s_parameterNamesOfImportDSCResource)
                {
                    if (pattern.IsMatch(parameterName) && !parameterNames.Contains(parameterName, StringComparer.OrdinalIgnoreCase))
                    {
                        string tooltip = "[String] " + parameterName;
                        result.Add(new CompletionResult("-" + parameterName, parameterName, CompletionResultType.ParameterName, tooltip));
                    }
                }
                if (result.Count > 0)
                {
                    context.ReplacementLength = context.WordToComplete.Length;
                    context.ReplacementIndex = lastAst.Extent.StartOffset;
                }
                return result;
            }

            if (parameterAst != null)
            {
                // Parent must be a command
                commandAst = (CommandAst)parameterAst.Parent;
                partialName = parameterAst.ParameterName;
                withColon = context.WordToComplete.EndsWith(":", StringComparison.Ordinal);
            }
            else
            {
                // No CommandParameterAst is found. It could be a StringConstantExpressionAst "-"
                var dashAst = (context.RelatedAsts[context.RelatedAsts.Count - 1] as StringConstantExpressionAst);
                if (dashAst == null)
                    return result;
                if (!dashAst.Value.Trim().Equals("-", StringComparison.OrdinalIgnoreCase))
                    return result;

                // Parent must be a command
                commandAst = (CommandAst)dashAst.Parent;
                partialName = string.Empty;
            }

            PseudoBindingInfo pseudoBinding = new PseudoParameterBinder()
                                                .DoPseudoParameterBinding(commandAst, null, parameterAst, PseudoParameterBinder.BindingType.ParameterCompletion);
            // The command cannot be found or it's not a cmdlet, not a script cmdlet, not a function
            if (pseudoBinding == null)
            {
                return result;
            }

            switch (pseudoBinding.InfoType)
            {
                case PseudoBindingInfoType.PseudoBindingFail:
                    // The command is a cmdlet or script cmdlet. Binding failed
                    result = GetParameterCompletionResults(partialName, uint.MaxValue, pseudoBinding.UnboundParameters, withColon);
                    break;
                case PseudoBindingInfoType.PseudoBindingSucceed:
                    // The command is a cmdlet or script cmdlet. Binding succeeded.
                    result = GetParameterCompletionResults(partialName, pseudoBinding, parameterAst, withColon);
                    break;
            }

            if (result.Count == 0)
            {
                result = pseudoBinding.CommandName.Equals("Set-Location", StringComparison.OrdinalIgnoreCase)
                             ? new List<CompletionResult>(CompleteFilename(context, true, null))
                             : new List<CompletionResult>(CompleteFilename(context));
            }

            return result;
        }

        /// <summary>
        /// Get the parameter completion results when the pseudo binding was successful
        /// </summary>
        /// <param name="parameterName"></param>
        /// <param name="bindingInfo"></param>
        /// <param name="parameterAst"></param>
        /// <param name="withColon"></param>
        /// <returns></returns>
        private static List<CompletionResult> GetParameterCompletionResults(string parameterName, PseudoBindingInfo bindingInfo, CommandParameterAst parameterAst, bool withColon)
        {
            Diagnostics.Assert(bindingInfo.InfoType.Equals(PseudoBindingInfoType.PseudoBindingSucceed), "The pseudo binding should succeed");
            List<CompletionResult> result = new List<CompletionResult>();

            if (parameterName == string.Empty)
            {
                result = GetParameterCompletionResults(
                    parameterName,
                    bindingInfo.ValidParameterSetsFlags,
                    bindingInfo.UnboundParameters,
                    withColon);
                return result;
            }

            if (bindingInfo.ParametersNotFound.Count > 0)
            {
                // The parameter name cannot be matched to any parameter
                if (bindingInfo.ParametersNotFound.Any(pAst => parameterAst.GetHashCode() == pAst.GetHashCode()))
                {
                    return result;
                }
            }

            if (bindingInfo.AmbiguousParameters.Count > 0)
            {
                // The parameter name is ambiguous. It's ignored in the pseudo binding, and we should search in the UnboundParameters
                if (bindingInfo.AmbiguousParameters.Any(pAst => parameterAst.GetHashCode() == pAst.GetHashCode()))
                {
                    result = GetParameterCompletionResults(
                        parameterName,
                        bindingInfo.ValidParameterSetsFlags,
                        bindingInfo.UnboundParameters,
                        withColon);
                }
                return result;
            }

            if (bindingInfo.DuplicateParameters.Count > 0)
            {
                // The parameter name is resolved to a parameter that is already bound. We search it in the BoundParameters
                if (bindingInfo.DuplicateParameters.Any(pAst => parameterAst.GetHashCode() == pAst.Parameter.GetHashCode()))
                {
                    result = GetParameterCompletionResults(
                        parameterName,
                        bindingInfo.ValidParameterSetsFlags,
                        bindingInfo.BoundParameters.Values,
                        withColon);
                }
                return result;
            }

            // The parameter should be bound in the pseudo binding during the named binding
            string matchedParameterName = null;
            foreach (KeyValuePair<string, AstParameterArgumentPair> entry in bindingInfo.BoundArguments)
            {
                switch (entry.Value.ParameterArgumentType)
                {
                    case AstParameterArgumentType.AstPair:
                        {
                            AstPair pair = (AstPair)entry.Value;
                            if (pair.ParameterSpecified && pair.Parameter.GetHashCode() == parameterAst.GetHashCode())
                            {
                                matchedParameterName = entry.Key;
                            }
                            else if (pair.ArgumentIsCommandParameterAst && pair.Argument.GetHashCode() == parameterAst.GetHashCode())
                            {
                                // The parameter name cannot be resolved to a parameter
                                return result;
                            }
                        }
                        break;
                    case AstParameterArgumentType.Fake:
                        {
                            FakePair pair = (FakePair)entry.Value;
                            if (pair.ParameterSpecified && pair.Parameter.GetHashCode() == parameterAst.GetHashCode())
                            {
                                matchedParameterName = entry.Key;
                            }
                        }
                        break;
                    case AstParameterArgumentType.Switch:
                        {
                            SwitchPair pair = (SwitchPair)entry.Value;
                            if (pair.ParameterSpecified && pair.Parameter.GetHashCode() == parameterAst.GetHashCode())
                            {
                                matchedParameterName = entry.Key;
                            }
                        }
                        break;
                    case AstParameterArgumentType.AstArray:
                    case AstParameterArgumentType.PipeObject:
                        break;
                }

                if (matchedParameterName != null)
                    break;
            }

            Diagnostics.Assert(matchedParameterName != null, "we should find matchedParameterName from the BoundArguments");
            MergedCompiledCommandParameter param = bindingInfo.BoundParameters[matchedParameterName];

            WildcardPattern pattern = WildcardPattern.Get(parameterName + "*", WildcardOptions.IgnoreCase);
            string parameterType = "[" + ToStringCodeMethods.Type(param.Parameter.Type, dropNamespaces: true) + "] ";
            string colonSuffix = withColon ? ":" : string.Empty;
            if (pattern.IsMatch(matchedParameterName))
            {
                string completionText = "-" + matchedParameterName + colonSuffix;
                string tooltip = parameterType + matchedParameterName;
                result.Add(new CompletionResult(completionText, matchedParameterName, CompletionResultType.ParameterName, tooltip));
            }

            // Process alias when there is partial input
            result.AddRange(from alias in param.Parameter.Aliases
                            where pattern.IsMatch(alias)
                            select
                                new CompletionResult("-" + alias + colonSuffix, alias, CompletionResultType.ParameterName,
                                                     parameterType + alias));

            return result;
        }

        /// <summary>
        /// Get the parameter completion results by using the given valid parameter sets and available parameters
        /// </summary>
        /// <param name="parameterName"></param>
        /// <param name="validParameterSetFlags"></param>
        /// <param name="parameters"></param>
        /// <param name="withColon"></param>
        /// <returns></returns>
        private static List<CompletionResult> GetParameterCompletionResults(
            string parameterName,
            uint validParameterSetFlags,
            IEnumerable<MergedCompiledCommandParameter> parameters,
            bool withColon)
        {
            var result = new List<CompletionResult>();
            var commonParamResult = new List<CompletionResult>();
            var pattern = WildcardPattern.Get(parameterName + "*", WildcardOptions.IgnoreCase);
            var colonSuffix = withColon ? ":" : string.Empty;

            bool addCommonParameters = true;
            foreach (MergedCompiledCommandParameter param in parameters)
            {
                bool inParameterSet = (param.Parameter.ParameterSetFlags & validParameterSetFlags) != 0 || param.Parameter.IsInAllSets;
                if (!inParameterSet)
                    continue;

                string name = param.Parameter.Name;
                string type = "[" + ToStringCodeMethods.Type(param.Parameter.Type, dropNamespaces: true) + "] ";
                bool isCommonParameter = Cmdlet.CommonParameters.Contains(name, StringComparer.OrdinalIgnoreCase);
                List<CompletionResult> listInUse = isCommonParameter ? commonParamResult : result;

                if (pattern.IsMatch(name))
                {
                    // Then using functions to back dynamic keywords, we don't necessarily
                    // want all of the parameters to be shown to the user. Those that are marked
                    // DontShow will not be displayed. Also, if any of the parameters have
                    // don't show set, we won't show any of the common parameters either.
                    bool showToUser = true;
                    var compiledAttributes = param.Parameter.CompiledAttributes;
                    if (compiledAttributes != null && compiledAttributes.Count > 0)
                    {
                        foreach (var attr in compiledAttributes)
                        {
                            var pattr = attr as ParameterAttribute;
                            if (pattr != null && pattr.DontShow)
                            {
                                showToUser = false;
                                addCommonParameters = false;
                                break;
                            }
                        }
                    }
                    if (showToUser)
                    {
                        string completionText = "-" + name + colonSuffix;
                        string tooltip = type + name;
                        listInUse.Add(new CompletionResult(completionText, name, CompletionResultType.ParameterName,
                                                           tooltip));
                    }
                }

                if (parameterName != string.Empty)
                {
                    // Process alias when there is partial input
                    listInUse.AddRange(from alias in param.Parameter.Aliases
                                       where pattern.IsMatch(alias)
                                       select
                                         new CompletionResult("-" + alias + colonSuffix, alias, CompletionResultType.ParameterName,
                                                              type + alias));
                }
            }

            // Add the common parameters to the results if expected.
            if (addCommonParameters)
            {
                result.AddRange(commonParamResult);
            }
            return result;
        }

        /// <summary>
        /// Get completion results for operators that start with <paramref name="wordToComplete"/>
        /// </summary>
        /// <param name="wordToComplete">The starting text of the operator to complete</param>
        /// <returns>A list of completion results</returns>
        public static List<CompletionResult> CompleteOperator(string wordToComplete)
        {
            if (wordToComplete.StartsWith("-", StringComparison.Ordinal))
            {
                wordToComplete = wordToComplete.Substring(1);
            }

            return (from op in Tokenizer._operatorText
                    where op.StartsWith(wordToComplete, StringComparison.OrdinalIgnoreCase)
                    orderby op
                    select new CompletionResult("-" + op, op, CompletionResultType.ParameterName, GetOperatorDescription(op))).ToList();
        }

        private static string GetOperatorDescription(string op)
        {
            return ResourceManagerCache.GetResourceString(typeof(CompletionCompleters).GetTypeInfo().Assembly, "TabCompletionStrings", op + "OperatorDescription");
        }

        #endregion Command Parameters

        #region Command Arguments

        internal static List<CompletionResult> CompleteCommandArgument(CompletionContext context)
        {
            CommandAst commandAst = null;
            List<CompletionResult> result = new List<CompletionResult>();

            // Find the expression ast. It should be at the end if there is one
            ExpressionAst expressionAst = null;
            MemberExpressionAst secondToLastMemberAst = null;
            Ast lastAst = context.RelatedAsts.Last();

            expressionAst = lastAst as ExpressionAst;
            if (expressionAst != null)
            {
                if (expressionAst.Parent is CommandAst)
                {
                    commandAst = (CommandAst)expressionAst.Parent;

                    if (expressionAst is ErrorExpressionAst && expressionAst.Extent.Text.EndsWith(",", StringComparison.Ordinal))
                    {
                        context.WordToComplete = string.Empty;
                        //BUGBUG context.CursorPosition = expressionAst.Extent.StartScriptPosition;
                    }
                    else if (commandAst.CommandElements.Count == 1 || context.WordToComplete == string.Empty)
                    {
                        expressionAst = null;
                    }
                    else if (commandAst.CommandElements.Count > 2)
                    {
                        var length = commandAst.CommandElements.Count;
                        var index = 1;

                        for (; index < length; index++)
                        {
                            if (commandAst.CommandElements[index] == expressionAst)
                                break;
                        }

                        CommandElementAst secondToLastAst = null;
                        if (index > 1)
                        {
                            secondToLastAst = commandAst.CommandElements[index - 1];
                            secondToLastMemberAst = secondToLastAst as MemberExpressionAst;
                        }

                        var partialPathAst = expressionAst as StringConstantExpressionAst;
                        if (partialPathAst != null && secondToLastAst != null &&
                            partialPathAst.StringConstantType == StringConstantType.BareWord &&
                            secondToLastAst.Extent.EndLineNumber == partialPathAst.Extent.StartLineNumber &&
                            secondToLastAst.Extent.EndColumnNumber == partialPathAst.Extent.StartColumnNumber &&
                            partialPathAst.Value.IndexOfAny(Utils.Separators.Directory) == 0)
                        {
                            var secondToLastStringConstantAst = secondToLastAst as StringConstantExpressionAst;
                            var secondToLastExpandableStringAst = secondToLastAst as ExpandableStringExpressionAst;
                            var secondToLastArrayAst = secondToLastAst as ArrayLiteralAst;
                            var secondToLastParamAst = secondToLastAst as CommandParameterAst;

                            if (secondToLastStringConstantAst != null || secondToLastExpandableStringAst != null)
                            {
                                var fullPath = ConcatenateStringPathArguments(secondToLastAst, partialPathAst.Value, context);
                                expressionAst = secondToLastStringConstantAst != null
                                                    ? (ExpressionAst)secondToLastStringConstantAst
                                                    : (ExpressionAst)secondToLastExpandableStringAst;

                                context.ReplacementIndex = ((InternalScriptPosition)secondToLastAst.Extent.StartScriptPosition).Offset;
                                context.ReplacementLength += ((InternalScriptPosition)secondToLastAst.Extent.EndScriptPosition).Offset - context.ReplacementIndex;
                                context.WordToComplete = fullPath;
                                //context.CursorPosition = secondToLastAst.Extent.StartScriptPosition;
                            }
                            else if (secondToLastArrayAst != null)
                            {
                                // Handle cases like: dir -Path .\cd, 'a b'\new<tab>
                                var lastArrayElement = secondToLastArrayAst.Elements.LastOrDefault();
                                var fullPath = ConcatenateStringPathArguments(lastArrayElement, partialPathAst.Value, context);
                                if (fullPath != null)
                                {
                                    expressionAst = secondToLastArrayAst;

                                    context.ReplacementIndex = ((InternalScriptPosition)lastArrayElement.Extent.StartScriptPosition).Offset;
                                    context.ReplacementLength += ((InternalScriptPosition)lastArrayElement.Extent.EndScriptPosition).Offset - context.ReplacementIndex;
                                    context.WordToComplete = fullPath;
                                }
                            }
                            else if (secondToLastParamAst != null)
                            {
                                // Handle cases like: dir -Path: .\cd, 'a b'\new<tab> || dir -Path: 'a b'\new<tab>
                                var fullPath = ConcatenateStringPathArguments(secondToLastParamAst.Argument, partialPathAst.Value, context);
                                if (fullPath != null)
                                {
                                    expressionAst = secondToLastParamAst.Argument;

                                    context.ReplacementIndex = ((InternalScriptPosition)secondToLastParamAst.Argument.Extent.StartScriptPosition).Offset;
                                    context.ReplacementLength += ((InternalScriptPosition)secondToLastParamAst.Argument.Extent.EndScriptPosition).Offset - context.ReplacementIndex;
                                    context.WordToComplete = fullPath;
                                }
                                else
                                {
                                    var arrayArgAst = secondToLastParamAst.Argument as ArrayLiteralAst;
                                    if (arrayArgAst != null)
                                    {
                                        var lastArrayElement = arrayArgAst.Elements.LastOrDefault();
                                        fullPath = ConcatenateStringPathArguments(lastArrayElement, partialPathAst.Value, context);
                                        if (fullPath != null)
                                        {
                                            expressionAst = arrayArgAst;

                                            context.ReplacementIndex = ((InternalScriptPosition)lastArrayElement.Extent.StartScriptPosition).Offset;
                                            context.ReplacementLength += ((InternalScriptPosition)lastArrayElement.Extent.EndScriptPosition).Offset - context.ReplacementIndex;
                                            context.WordToComplete = fullPath;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                else if (expressionAst.Parent is ArrayLiteralAst && expressionAst.Parent.Parent is CommandAst)
                {
                    commandAst = (CommandAst)expressionAst.Parent.Parent;

                    if (commandAst.CommandElements.Count == 1 || context.WordToComplete == string.Empty)
                    {
                        // dir -Path a.txt, b.txt <tab>
                        expressionAst = null;
                    }
                    else
                    {
                        // dir -Path a.txt, b.txt c<tab>
                        expressionAst = (ExpressionAst)expressionAst.Parent;
                    }
                }
                else if (expressionAst.Parent is ArrayLiteralAst && expressionAst.Parent.Parent is CommandParameterAst)
                {
                    // Handle scenarios such as 
                    //      dir -Path: a.txt, <tab> || dir -Path: a.txt, b.txt <tab>
                    commandAst = (CommandAst)expressionAst.Parent.Parent.Parent;
                    if (context.WordToComplete == string.Empty)
                    {
                        // dir -Path: a.txt, b.txt <tab>
                        expressionAst = null;
                    }
                    else
                    {
                        // dir -Path: a.txt, b<tab>
                        expressionAst = (ExpressionAst)expressionAst.Parent;
                    }
                }
                else if (expressionAst.Parent is CommandParameterAst && expressionAst.Parent.Parent is CommandAst)
                {
                    commandAst = (CommandAst)expressionAst.Parent.Parent;
                    if (expressionAst is ErrorExpressionAst && expressionAst.Extent.Text.EndsWith(",", StringComparison.Ordinal))
                    {
                        // dir -Path: a.txt,<tab>
                        context.WordToComplete = string.Empty;
                        //context.CursorPosition = expressionAst.Extent.StartScriptPosition;
                    }
                    else if (context.WordToComplete == string.Empty)
                    {
                        // Handle scenario like this: Set-ExecutionPolicy -Scope:CurrentUser <tab>
                        expressionAst = null;
                    }
                }
            }
            else
            {
                var paramAst = lastAst as CommandParameterAst;
                if (paramAst != null)
                {
                    commandAst = paramAst.Parent as CommandAst;
                }
                else
                {
                    commandAst = lastAst as CommandAst;
                }
            }

            if (commandAst == null)
            {
                // We don't know if this could be expanded into anything interesting
                return result;
            }

            PseudoBindingInfo pseudoBinding = new PseudoParameterBinder()
                                                .DoPseudoParameterBinding(commandAst, null, null, PseudoParameterBinder.BindingType.ArgumentCompletion);

            do
            {
                // The command cannot be found, or it's NOT a cmdlet, NOT a script cmdlet and NOT a function
                if (pseudoBinding == null)
                    break;

                bool parsedArgumentsProvidesMatch = false;

                if (pseudoBinding.AllParsedArguments != null && pseudoBinding.AllParsedArguments.Count > 0)
                {
                    ArgumentLocation argLocation;
                    bool treatAsExpression = false;

                    if (expressionAst != null)
                    {
                        treatAsExpression = true;
                        var dashExp = expressionAst as StringConstantExpressionAst;
                        if (dashExp != null && dashExp.Value.Trim().Equals("-", StringComparison.OrdinalIgnoreCase))
                        {
                            // "-" is represented as StringConstantExpressionAst. Most likely the user is typing a <tab>
                            // after it, so in the pseudo binder, we ignore it to avoid treating it as an argument.
                            // for example:
                            //      Get-Content -Path "-<tab>  -->  Get-Content -Path ".\-patt.txt"
                            treatAsExpression = false;
                        }
                    }

                    if (treatAsExpression)
                    {
                        argLocation = FindTargetArgumentLocation(
                            pseudoBinding.AllParsedArguments, expressionAst);
                    }
                    else
                    {
                        argLocation = FindTargetArgumentLocation(
                            pseudoBinding.AllParsedArguments, context.TokenAtCursor ?? context.TokenBeforeCursor);
                    }

                    if (argLocation != null)
                    {
                        context.PseudoBindingInfo = pseudoBinding;
                        switch (pseudoBinding.InfoType)
                        {
                            case PseudoBindingInfoType.PseudoBindingSucceed:
                                result = GetArgumentCompletionResultsWithSuccessfulPseudoBinding(context, argLocation, commandAst);
                                break;
                            case PseudoBindingInfoType.PseudoBindingFail:
                                result = GetArgumentCompletionResultsWithFailedPseudoBinding(context, argLocation, commandAst);
                                break;
                        }
                        parsedArgumentsProvidesMatch = true;
                    }
                }

                if (!parsedArgumentsProvidesMatch)
                {
                    int index = 0;
                    CommandElementAst prevElem = null;
                    if (expressionAst != null)
                    {
                        foreach (CommandElementAst eleAst in commandAst.CommandElements)
                        {
                            if (eleAst.GetHashCode() == expressionAst.GetHashCode())
                                break;
                            prevElem = eleAst;
                            index++;
                        }
                    }
                    else
                    {
                        var token = context.TokenAtCursor ?? context.TokenBeforeCursor;
                        foreach (CommandElementAst eleAst in commandAst.CommandElements)
                        {
                            if (eleAst.Extent.StartOffset > token.Extent.EndOffset)
                                break;
                            prevElem = eleAst;
                            index++;
                        }
                    }

                    // positional argument with position 0
                    if (index == 1)
                    {
                        CompletePositionalArgument(
                            pseudoBinding.CommandName,
                            commandAst,
                            context,
                            result,
                            pseudoBinding.UnboundParameters,
                            pseudoBinding.DefaultParameterSetFlag,
                            uint.MaxValue,
                            0);
                    }
                    else
                    {
                        if (prevElem is CommandParameterAst && ((CommandParameterAst)prevElem).Argument == null)
                        {
                            var paramName = ((CommandParameterAst)prevElem).ParameterName;
                            var pattern = WildcardPattern.Get(paramName + "*", WildcardOptions.IgnoreCase);
                            foreach (MergedCompiledCommandParameter param in pseudoBinding.UnboundParameters)
                            {
                                if (pattern.IsMatch(param.Parameter.Name))
                                {
                                    ProcessParameter(pseudoBinding.CommandName, commandAst, context, result, param);
                                    break;
                                }

                                var isAliasMatch = false;
                                foreach (string alias in param.Parameter.Aliases)
                                {
                                    if (pattern.IsMatch(alias))
                                    {
                                        isAliasMatch = true;
                                        ProcessParameter(pseudoBinding.CommandName, commandAst, context, result, param);
                                        break;
                                    }
                                }

                                if (isAliasMatch)
                                    break;
                            }
                        }
                    }
                }
            } while (false);

            // Indicate if the current argument completion falls into those pre-defined cases and
            // has been processed already.
            bool hasBeenProcessed = false;
            if (result.Count > 0 && result[result.Count - 1].Equals(CompletionResult.Null))
            {
                result.RemoveAt(result.Count - 1);
                hasBeenProcessed = true;

                if (result.Count > 0)
                    return result;
            }

            // Handle some special cases such as:
            //    & "get-comm<tab> --> & "Get-Command"
            //    & "sa<tab>       --> & ".\sa[v].txt"
            if (expressionAst == null && !hasBeenProcessed &&
                commandAst.CommandElements.Count == 1 &&
                commandAst.InvocationOperator != TokenKind.Unknown &&
                context.WordToComplete != string.Empty)
            {
                // Use literal path after Ampersand
                var tryCmdletCompletion = false;
                var clearLiteralPathsKey = TurnOnLiteralPathOption(context);

                if (context.WordToComplete.IndexOf('-') != -1)
                {
                    tryCmdletCompletion = true;
                }

                try
                {
                    var fileCompletionResults = new List<CompletionResult>(CompleteFilename(context));
                    if (tryCmdletCompletion)
                    {
                        // It's actually command name completion, other than argument completion
                        var cmdletCompletionResults = CompleteCommand(context);
                        if (cmdletCompletionResults != null && cmdletCompletionResults.Count > 0)
                        {
                            fileCompletionResults.AddRange(cmdletCompletionResults);
                        }
                    }
                    return fileCompletionResults;
                }
                finally
                {
                    if (clearLiteralPathsKey)
                        context.Options.Remove("LiteralPaths");
                }
            }

            if (expressionAst is StringConstantExpressionAst)
            {
                var pathAst = (StringConstantExpressionAst)expressionAst;
                // Handle static member completion: echo [int]::<tab>
                var shareMatch = Regex.Match(pathAst.Value, @"^(\[[\w\d\.]+\]::[\w\d\*]*)$");
                if (shareMatch.Success)
                {
                    int fakeReplacementIndex, fakeReplacementLength;
                    var input = shareMatch.Groups[1].Value;
                    var completionParameters = CommandCompletion.MapStringInputToParsedInput(input, input.Length);
                    var completionAnalysis = new CompletionAnalysis(completionParameters.Item1, completionParameters.Item2, completionParameters.Item3, context.Options);
                    var ret = completionAnalysis.GetResults(
                        context.Helper.CurrentPowerShell,
                        out fakeReplacementIndex,
                        out fakeReplacementLength);

                    if (ret != null && ret.Count > 0)
                    {
                        var prefix = TokenKind.LParen.Text() + input.Substring(0, fakeReplacementIndex);
                        foreach (CompletionResult entry in ret)
                        {
                            string completionText = prefix + entry.CompletionText;
                            if (entry.ResultType.Equals(CompletionResultType.Property))
                                completionText += TokenKind.RParen.Text();
                            result.Add(new CompletionResult(completionText, entry.ListItemText, entry.ResultType,
                                                            entry.ToolTip));
                        }
                        return result;
                    }
                }

                // Handle member completion with wildcard: echo $a.*<tab>
                if (pathAst.Value.IndexOf('*') != -1 && secondToLastMemberAst != null &&
                    secondToLastMemberAst.Extent.EndLineNumber == pathAst.Extent.StartLineNumber &&
                    secondToLastMemberAst.Extent.EndColumnNumber == pathAst.Extent.StartColumnNumber)
                {
                    var memberName = pathAst.Value.EndsWith("*", StringComparison.Ordinal)
                                         ? pathAst.Value
                                         : pathAst.Value + "*";
                    var targetExpr = secondToLastMemberAst.Expression;
                    if (IsSplattedVariable(targetExpr))
                    {
                        // It's splatted variable, and the member completion is not useful
                        return result;
                    }

                    var memberAst = secondToLastMemberAst.Member as StringConstantExpressionAst;
                    if (memberAst != null)
                    {
                        memberName = memberAst.Value + memberName;
                    }

                    CompleteMemberHelper(false, memberName, targetExpr, context, result);
                    if (result.Count > 0)
                    {
                        context.ReplacementIndex =
                            ((InternalScriptPosition)secondToLastMemberAst.Expression.Extent.EndScriptPosition).Offset + 1;
                        if (memberAst != null)
                            context.ReplacementLength += memberAst.Value.Length;
                        return result;
                    }
                }

                // Treat it as the file name completion
                // Handle this scenario: & 'c:\a b'\<tab>
                string fileName = pathAst.Value;
                if (commandAst.InvocationOperator != TokenKind.Unknown && fileName.IndexOfAny(Utils.Separators.Directory) == 0 &&
                    commandAst.CommandElements.Count == 2 && commandAst.CommandElements[0] is StringConstantExpressionAst &&
                    commandAst.CommandElements[0].Extent.EndLineNumber == expressionAst.Extent.StartLineNumber &&
                    commandAst.CommandElements[0].Extent.EndColumnNumber == expressionAst.Extent.StartColumnNumber)
                {
                    if (pseudoBinding != null)
                    {
                        // CommandElements[0] is resolved to a command
                        return result;
                    }
                    else
                    {
                        var constantAst = (StringConstantExpressionAst)commandAst.CommandElements[0];
                        fileName = constantAst.Value + fileName;
                        context.ReplacementIndex = ((InternalScriptPosition)constantAst.Extent.StartScriptPosition).Offset;
                        context.ReplacementLength += ((InternalScriptPosition)constantAst.Extent.EndScriptPosition).Offset - context.ReplacementIndex;
                        context.WordToComplete = fileName;
                        // commandAst.InvocationOperator != TokenKind.Unknown, so we should use literal path
                        var clearLiteralPathKey = TurnOnLiteralPathOption(context);

                        try
                        {
                            return new List<CompletionResult>(CompleteFilename(context));
                        }
                        finally
                        {
                            if (clearLiteralPathKey)
                                context.Options.Remove("LiteralPaths");
                        }
                    }
                }
            }

            // The default argument completion: file path completion, command name completion('WordToComplete' is not empty and contains a dash).
            // If the current argument completion has been process already, we don't go through the default argument completion anymore.
            if (!hasBeenProcessed)
            {
                var commandName = commandAst.GetCommandName();
                var customCompleter = GetCustomArgumentCompleter(
                    "NativeArgumentCompleters",
                    new[] { commandName, Path.GetFileName(commandName), Path.GetFileNameWithoutExtension(commandName) },
                    context);
                if (customCompleter != null)
                {
                    if (InvokeScriptArgumentCompleter(
                        customCompleter,
                        new object[] { context.WordToComplete, commandAst, context.CursorPosition.Offset },
                        result))
                    {
                        return result;
                    }
                }

                var clearLiteralPathKey = false;
                if (pseudoBinding == null)
                {
                    // the command could be a native command such as notepad.exe, we use literal path in this case
                    clearLiteralPathKey = TurnOnLiteralPathOption(context);
                }

                try
                {
                    result = new List<CompletionResult>(CompleteFilename(context));
                }
                finally
                {
                    if (clearLiteralPathKey)
                        context.Options.Remove("LiteralPaths");
                }

                if (context.WordToComplete != string.Empty && context.WordToComplete.IndexOf('-') != -1)
                {
                    // For argument completion, we don't want to complete against pseudo commands that only work in the script workflow.
                    // The way to avoid that is to pass in a CompletionContext with RelatedAst = null
                    var commandResults = CompleteCommand(new CompletionContext { WordToComplete = context.WordToComplete, Helper = context.Helper });
                    if (commandResults != null)
                        result.AddRange(commandResults);
                }
            }

            return result;
        }

        internal static string ConcatenateStringPathArguments(CommandElementAst stringAst, string partialPath, CompletionContext completionContext)
        {
            var constantPathAst = stringAst as StringConstantExpressionAst;
            if (constantPathAst != null)
            {
                string quote = String.Empty;
                switch (constantPathAst.StringConstantType)
                {
                    case StringConstantType.SingleQuoted:
                        quote = "'";
                        break;
                    case StringConstantType.DoubleQuoted:
                        quote = "\"";
                        break;
                    default:
                        break;
                }
                return quote + constantPathAst.Value + partialPath + quote;
            }
            else
            {
                var expandablePathAst = stringAst as ExpandableStringExpressionAst;
                string fullPath = null;
                if (expandablePathAst != null &&
                    IsPathSafelyExpandable(expandableStringAst: expandablePathAst,
                                           extraText: partialPath,
                                           executionContext: completionContext.ExecutionContext,
                                           expandedString: out fullPath))
                {
                    return fullPath;
                }
            }

            return null;
        }

        /// <summary>
        /// Get the argument completion results when the pseudo binding was not successful
        /// </summary>
        private static List<CompletionResult> GetArgumentCompletionResultsWithFailedPseudoBinding(
            CompletionContext context,
            ArgumentLocation argLocation,
            CommandAst commandAst)
        {
            List<CompletionResult> result = new List<CompletionResult>();

            PseudoBindingInfo bindingInfo = context.PseudoBindingInfo;
            if (argLocation.IsPositional)
            {
                CompletePositionalArgument(
                    bindingInfo.CommandName,
                    commandAst,
                    context,
                    result,
                    bindingInfo.UnboundParameters,
                    bindingInfo.DefaultParameterSetFlag,
                    uint.MaxValue,
                    argLocation.Position);
            }
            else
            {
                string paramName = argLocation.Argument.ParameterName;
                WildcardPattern pattern = WildcardPattern.Get(paramName + "*", WildcardOptions.IgnoreCase);
                foreach (MergedCompiledCommandParameter param in bindingInfo.UnboundParameters)
                {
                    if (pattern.IsMatch(param.Parameter.Name))
                    {
                        ProcessParameter(bindingInfo.CommandName, commandAst, context, result, param);
                        break;
                    }

                    bool isAliasMatch = false;
                    foreach (string alias in param.Parameter.Aliases)
                    {
                        if (pattern.IsMatch(alias))
                        {
                            isAliasMatch = true;
                            ProcessParameter(bindingInfo.CommandName, commandAst, context, result, param);
                            break;
                        }
                    }

                    if (isAliasMatch)
                        break;
                }
            }

            return result;
        }

        /// <summary>
        /// Get the argument completion results when the pseudo binding was successful
        /// </summary>
        private static List<CompletionResult> GetArgumentCompletionResultsWithSuccessfulPseudoBinding(
            CompletionContext context,
            ArgumentLocation argLocation,
            CommandAst commandAst)
        {
            PseudoBindingInfo bindingInfo = context.PseudoBindingInfo;
            Diagnostics.Assert(bindingInfo.InfoType.Equals(PseudoBindingInfoType.PseudoBindingSucceed), "Caller needs to make sure the pseudo binding was successful");
            List<CompletionResult> result = new List<CompletionResult>();

            if (argLocation.IsPositional && argLocation.Argument == null)
            {
                AstPair lastPositionalArg;
                AstParameterArgumentPair targetPositionalArg =
                    FindTargetPositionalArgument(
                        bindingInfo.AllParsedArguments,
                        argLocation.Position,
                        out lastPositionalArg);

                if (targetPositionalArg != null)
                    argLocation.Argument = targetPositionalArg;
                else
                {
                    if (lastPositionalArg != null)
                    {
                        bool lastPositionalGetBound = false;
                        Collection<string> parameterNames = new Collection<string>();

                        foreach (KeyValuePair<string, AstParameterArgumentPair> entry in bindingInfo.BoundArguments)
                        {
                            // positional argument
                            if (!entry.Value.ParameterSpecified)
                            {
                                var arg = (AstPair)entry.Value;
                                if (arg.Argument.GetHashCode() == lastPositionalArg.Argument.GetHashCode())
                                {
                                    lastPositionalGetBound = true;
                                    break;
                                }
                            }
                            else if (entry.Value.ParameterArgumentType.Equals(AstParameterArgumentType.AstArray))
                            {
                                // check if the positional argument would be bound to a "ValueFromRemainingArgument" parameter
                                var arg = (AstArrayPair)entry.Value;
                                if (arg.Argument.Any(exp => exp.GetHashCode() == lastPositionalArg.Argument.GetHashCode()))
                                {
                                    parameterNames.Add(entry.Key);
                                }
                            }
                        }

                        if (parameterNames.Count > 0)
                        {
                            // parameter should be in BoundParameters
                            foreach (string param in parameterNames)
                            {
                                MergedCompiledCommandParameter parameter = bindingInfo.BoundParameters[param];
                                ProcessParameter(bindingInfo.CommandName, commandAst, context, result, parameter, bindingInfo.BoundArguments);
                            }
                            return result;
                        }
                        else if (!lastPositionalGetBound)
                        {
                            // last positional argument was not bound, then positional argument 'tab' wants to
                            // expand will not get bound either
                            return result;
                        }
                    }

                    CompletePositionalArgument(
                        bindingInfo.CommandName,
                        commandAst,
                        context,
                        result,
                        bindingInfo.UnboundParameters,
                        bindingInfo.DefaultParameterSetFlag,
                        bindingInfo.ValidParameterSetsFlags,
                        argLocation.Position,
                        bindingInfo.BoundArguments);

                    return result;
                }
            }

            if (argLocation.Argument != null)
            {
                Collection<string> parameterNames = new Collection<string>();
                foreach (KeyValuePair<string, AstParameterArgumentPair> entry in bindingInfo.BoundArguments)
                {
                    if (entry.Value.ParameterArgumentType.Equals(AstParameterArgumentType.PipeObject))
                        continue;

                    if (entry.Value.ParameterArgumentType.Equals(AstParameterArgumentType.AstArray) && !argLocation.Argument.ParameterSpecified)
                    {
                        var arrayArg = (AstArrayPair)entry.Value;
                        var target = (AstPair)argLocation.Argument;
                        if (arrayArg.Argument.Any(exp => exp.GetHashCode() == target.Argument.GetHashCode()))
                        {
                            parameterNames.Add(entry.Key);
                        }
                    }
                    else if (entry.Value.GetHashCode() == argLocation.Argument.GetHashCode())
                    {
                        parameterNames.Add(entry.Key);
                    }
                }

                if (parameterNames.Count > 0)
                {
                    // those parameters should be in BoundParameters
                    foreach (string param in parameterNames)
                    {
                        MergedCompiledCommandParameter parameter = bindingInfo.BoundParameters[param];
                        ProcessParameter(bindingInfo.CommandName, commandAst, context, result, parameter, bindingInfo.BoundArguments);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Get the positional argument completion results based on the position it's in the command line
        /// </summary>
        private static void CompletePositionalArgument(
            string commandName,
            CommandAst commandAst,
            CompletionContext context,
            List<CompletionResult> result,
            IEnumerable<MergedCompiledCommandParameter> parameters,
            uint defaultParameterSetFlag,
            uint validParameterSetFlags,
            int position,
            Dictionary<string, AstParameterArgumentPair> boundArguments = null)
        {
            bool isProcessedAsPositional = false;
            bool isDefaultParameterSetValid = defaultParameterSetFlag != 0 &&
                                              (defaultParameterSetFlag & validParameterSetFlags) != 0;
            MergedCompiledCommandParameter positionalParam = null;

            foreach (MergedCompiledCommandParameter param in parameters)
            {
                bool isInParameterSet = (param.Parameter.ParameterSetFlags & validParameterSetFlags) != 0 || param.Parameter.IsInAllSets;
                if (!isInParameterSet)
                    continue;

                var parameterSetDataCollection = param.Parameter.GetMatchingParameterSetData(validParameterSetFlags);
                foreach (ParameterSetSpecificMetadata parameterSetData in parameterSetDataCollection)
                {
                    // in the first pass, we skip the remaining argument ones
                    if (parameterSetData.ValueFromRemainingArguments)
                    {
                        continue;
                    }

                    // Check the position
                    int positionInParameterSet = parameterSetData.Position;

                    if (positionInParameterSet == int.MinValue || positionInParameterSet != position)
                    {
                        // The parameter is not positional, or its position is not what we want
                        continue;
                    }

                    if (isDefaultParameterSetValid)
                    {
                        if (parameterSetData.ParameterSetFlag == defaultParameterSetFlag)
                        {
                            ProcessParameter(commandName, commandAst, context, result, param, boundArguments);
                            isProcessedAsPositional = result.Any();
                            break;
                        }
                        else
                        {
                            if (positionalParam == null)
                                positionalParam = param;
                        }
                    }
                    else
                    {
                        isProcessedAsPositional = true;
                        ProcessParameter(commandName, commandAst, context, result, param, boundArguments);
                        break;
                    }
                }

                if (isProcessedAsPositional)
                    break;
            }

            if (!isProcessedAsPositional && positionalParam != null)
            {
                isProcessedAsPositional = true;
                ProcessParameter(commandName, commandAst, context, result, positionalParam, boundArguments);
            }

            if (!isProcessedAsPositional)
            {
                foreach (MergedCompiledCommandParameter param in parameters)
                {
                    bool isInParameterSet = (param.Parameter.ParameterSetFlags & validParameterSetFlags) != 0 || param.Parameter.IsInAllSets;
                    if (!isInParameterSet)
                        continue;

                    var parameterSetDataCollection = param.Parameter.GetMatchingParameterSetData(validParameterSetFlags);
                    foreach (ParameterSetSpecificMetadata parameterSetData in parameterSetDataCollection)
                    {
                        // in the second pass, we check the remaining argument ones
                        if (parameterSetData.ValueFromRemainingArguments)
                        {
                            ProcessParameter(commandName, commandAst, context, result, param, boundArguments);
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Process a parameter to get the argument completion results
        /// </summary>
        /// 
        /// <remarks>
        /// If the argument completion falls into these pre-defined cases:
        ///   1. The matching parameter is of type Enum
        ///   2. The matching parameter is of type SwitchParameter
        ///   3. The matching parameter is declared with ValidateSetAttribute
        ///   4. Falls into the native command argument completion
        /// a null instance of CompletionResult is added to the end of the
        /// "result" list, to indicate that this particular argument completion
        /// has been processed already. If the "result" list is still empty, we
        /// will not go through the default argument completion steps anymore.
        /// </remarks>
        private static void ProcessParameter(
            string commandName,
            CommandAst commandAst,
            CompletionContext context,
            List<CompletionResult> result,
            MergedCompiledCommandParameter parameter,
            Dictionary<string, AstParameterArgumentPair> boundArguments = null)
        {
            CompletionResult fullMatch = null;
            Type parameterType = GetEffectiveParameterType(parameter.Parameter.Type);

            if (parameterType.IsArray)
            {
                parameterType = parameterType.GetElementType();
            }

            if (parameterType.GetTypeInfo().IsEnum)
            {
                RemoveLastNullCompletionResult(result);

                string enumString = LanguagePrimitives.EnumSingleTypeConverter.EnumValues(parameterType);
                string separator = CultureInfo.CurrentUICulture.TextInfo.ListSeparator;
                string[] enumArray = enumString.Split(new string[] { separator }, StringSplitOptions.RemoveEmptyEntries);

                string wordToComplete = context.WordToComplete;
                string quote = HandleDoubleAndSingleQuote(ref wordToComplete);

                var pattern = WildcardPattern.Get(wordToComplete + "*", WildcardOptions.IgnoreCase);
                var enumList = new List<string>();

                foreach (string value in enumArray)
                {
                    if (wordToComplete.Equals(value, StringComparison.OrdinalIgnoreCase))
                    {
                        string completionText = quote == string.Empty ? value : quote + value + quote;
                        fullMatch = new CompletionResult(completionText, value, CompletionResultType.ParameterValue, value);
                        continue;
                    }

                    if (pattern.IsMatch(value))
                    {
                        enumList.Add(value);
                    }
                }

                if (fullMatch != null)
                {
                    result.Add(fullMatch);
                }

                enumList.Sort();
                result.AddRange(from entry in enumList
                                let completionText = quote == string.Empty ? entry : quote + entry + quote
                                select new CompletionResult(completionText, entry, CompletionResultType.ParameterValue, entry));

                result.Add(CompletionResult.Null);
                return;
            }

            if (parameterType.Equals(typeof(SwitchParameter)))
            {
                RemoveLastNullCompletionResult(result);

                if (context.WordToComplete == string.Empty || context.WordToComplete.Equals("$", StringComparison.Ordinal))
                {
                    result.Add(new CompletionResult("$true", "$true", CompletionResultType.ParameterValue, "$true"));
                    result.Add(new CompletionResult("$false", "$false", CompletionResultType.ParameterValue, "$false"));
                }

                result.Add(CompletionResult.Null);
                return;
            }

            foreach (ValidateArgumentsAttribute att in parameter.Parameter.ValidationAttributes)
            {
                if (att is ValidateSetAttribute)
                {
                    RemoveLastNullCompletionResult(result);

                    var setAtt = (ValidateSetAttribute)att;

                    string wordToComplete = context.WordToComplete;
                    string quote = HandleDoubleAndSingleQuote(ref wordToComplete);

                    var pattern = WildcardPattern.Get(wordToComplete + "*", WildcardOptions.IgnoreCase);
                    var setList = new List<string>();

                    foreach (string value in setAtt.ValidValues)
                    {
                        if (wordToComplete.Equals(value, StringComparison.OrdinalIgnoreCase))
                        {
                            string completionText = quote == string.Empty ? value : quote + value + quote;
                            fullMatch = new CompletionResult(completionText, value, CompletionResultType.ParameterValue, value);
                            continue;
                        }

                        if (pattern.IsMatch(value))
                        {
                            setList.Add(value);
                        }
                    }

                    if (fullMatch != null)
                    {
                        result.Add(fullMatch);
                    }

                    setList.Sort();
                    foreach (string entry in setList)
                    {
                        string realEntry = entry;
                        string completionText = entry;
                        if (quote == string.Empty)
                        {
                            if (CompletionRequiresQuotes(entry, false))
                            {
                                realEntry = CodeGeneration.EscapeSingleQuotedStringContent(entry);
                                completionText = "'" + realEntry + "'";
                            }
                        }
                        else
                        {
                            if (quote.Equals("'", StringComparison.OrdinalIgnoreCase))
                            {
                                realEntry = CodeGeneration.EscapeSingleQuotedStringContent(entry);
                            }
                            completionText = quote + realEntry + quote;
                        }

                        result.Add(new CompletionResult(completionText, entry, CompletionResultType.ParameterValue, entry));
                    }
                    result.Add(CompletionResult.Null);
                    return;
                }
            }

            NativeCommandArgumentCompletion(commandName, parameter.Parameter, result, commandAst, context, boundArguments);
        }

        private static IEnumerable<PSTypeName> NativeCommandArgumentCompletion_InferTypesOfArugment(
            Dictionary<string, AstParameterArgumentPair> boundArguments,
            CommandAst commandAst,
            CompletionContext context,
            string parameterName)
        {
            if (boundArguments == null)
            {
                yield break;
            }

            AstParameterArgumentPair astParameterArgumentPair;
            if (!boundArguments.TryGetValue(parameterName, out astParameterArgumentPair))
            {
                yield break;
            }

            Ast argumentAst = null;
            switch (astParameterArgumentPair.ParameterArgumentType)
            {
                case AstParameterArgumentType.AstPair:
                    {
                        AstPair astPair = (AstPair)astParameterArgumentPair;
                        argumentAst = astPair.Argument;
                    }
                    break;

                case AstParameterArgumentType.PipeObject:
                    {
                        var pipelineAst = commandAst.Parent as PipelineAst;
                        if (pipelineAst != null)
                        {
                            int i;
                            for (i = 0; i < pipelineAst.PipelineElements.Count; i++)
                            {
                                if (pipelineAst.PipelineElements[i] == commandAst)
                                    break;
                            }

                            if (i != 0)
                            {
                                argumentAst = pipelineAst.PipelineElements[i - 1];
                            }
                        }
                    }
                    break;

                default:
                    break;
            }

            if (argumentAst == null)
            {
                yield break;
            }

            ExpressionAst argumentExpressionAst = argumentAst as ExpressionAst;
            if (argumentExpressionAst == null)
            {
                CommandExpressionAst argumentCommandExpressionAst = argumentAst as CommandExpressionAst;
                if (argumentCommandExpressionAst != null)
                {
                    argumentExpressionAst = argumentCommandExpressionAst.Expression;
                }
            }

            object argumentValue;
            if (argumentExpressionAst != null && SafeExprEvaluator.TrySafeEval(argumentExpressionAst, context.ExecutionContext, out argumentValue))
            {
                if (argumentValue != null)
                {
                    IEnumerable enumerable = LanguagePrimitives.GetEnumerable(argumentValue) ??
                                             new object[] { argumentValue };
                    foreach (var element in enumerable)
                    {
                        if (element == null)
                        {
                            continue;
                        }

                        PSObject pso = PSObject.AsPSObject(element);
                        if ((pso.TypeNames.Count > 0) && (!(pso.TypeNames[0].Equals(pso.BaseObject.GetType().FullName, StringComparison.OrdinalIgnoreCase))))
                        {
                            yield return new PSTypeName(pso.TypeNames[0]);
                        }
                        if (!(pso.BaseObject is PSCustomObject))
                        {
                            yield return new PSTypeName(pso.BaseObject.GetType());
                        }
                    }
                    yield break;
                }
            }

            foreach (PSTypeName typeName in argumentAst.GetInferredType(context))
            {
                yield return typeName;
            }
        }

        internal static IList<string> NativeCommandArgumentCompletion_ExtractSecondaryArgument(
            Dictionary<string, AstParameterArgumentPair> boundArguments,
            string parameterName)
        {
            List<string> result = new List<string>();

            if (boundArguments == null)
            {
                return result;
            }

            AstParameterArgumentPair argumentValue;
            if (!boundArguments.TryGetValue(parameterName, out argumentValue))
            {
                return result;
            }

            switch (argumentValue.ParameterArgumentType)
            {
                case AstParameterArgumentType.AstPair:
                    {
                        var value = (AstPair)argumentValue;
                        if (value.Argument is StringConstantExpressionAst)
                        {
                            var argument = (StringConstantExpressionAst)value.Argument;
                            result.Add(argument.Value);
                        }
                        else if (value.Argument is ArrayLiteralAst)
                        {
                            var argument = (ArrayLiteralAst)value.Argument;
                            foreach (ExpressionAst entry in argument.Elements)
                            {
                                var entryAsString = entry as StringConstantExpressionAst;
                                if (entryAsString != null)
                                {
                                    result.Add(entryAsString.Value);
                                }
                                else
                                {
                                    result.Clear();
                                    break;
                                }
                            }
                        }

                        break;
                    }
                case AstParameterArgumentType.AstArray:
                    {
                        var value = (AstArrayPair)argumentValue;
                        var argument = value.Argument;

                        foreach (ExpressionAst entry in argument)
                        {
                            var entryAsString = entry as StringConstantExpressionAst;
                            if (entryAsString != null)
                            {
                                result.Add(entryAsString.Value);
                            }
                            else
                            {
                                result.Clear();
                                break;
                            }
                        }
                        break;
                    }
                default:
                    break;
            }

            return result;
        }

        private static void NativeCommandArgumentCompletion(
            string commandName,
            CompiledCommandParameter parameter,
            List<CompletionResult> result,
            CommandAst commandAst,
            CompletionContext context,
            Dictionary<string, AstParameterArgumentPair> boundArguments = null)
        {
            if (string.IsNullOrEmpty(commandName))
            {
                return;
            }

            var parameterName = parameter.Name;
            var customCompleter = GetCustomArgumentCompleter(
                    "CustomArgumentCompleters",
                    new[] { commandName + ":" + parameterName, parameterName },
                    context);
            if (customCompleter != null)
            {
                if (InvokeScriptArgumentCompleter(
                    customCompleter,
                    commandName, parameterName, context.WordToComplete, commandAst, context,
                    result))
                {
                    return;
                }
            }

            var argumentCompleterAttribute = parameter.CompiledAttributes.OfType<ArgumentCompleterAttribute>().FirstOrDefault();
            if (argumentCompleterAttribute != null)
            {
                try
                {
                    if (argumentCompleterAttribute.Type != null)
                    {
                        var completer = Activator.CreateInstance(argumentCompleterAttribute.Type) as IArgumentCompleter;
                        if (completer != null)
                        {
                            var customResults = completer.CompleteArgument(commandName, parameterName,
                                context.WordToComplete, commandAst, GetBoundArgumentsAsHashtable(context));
                            if (customResults != null)
                            {
                                result.AddRange(customResults);
                                result.Add(CompletionResult.Null);
                                return;
                            }
                        }
                    }
                    else
                    {
                        if (InvokeScriptArgumentCompleter(
                            argumentCompleterAttribute.ScriptBlock,
                            commandName, parameterName, context.WordToComplete, commandAst, context,
                            result))
                        {
                            return;
                        }
                    }
                }
                catch (Exception e)
                {
                    CommandProcessorBase.CheckForSevereException(e);
                }
            }
            switch (commandName)
            {
                case "Get-Command":
                    {
                        if (parameterName.Equals("Module", StringComparison.OrdinalIgnoreCase))
                        {
                            NativeCompletionGetCommand(context.WordToComplete, null, parameterName, result, context);
                            break;
                        }
                        if (parameterName.Equals("Name", StringComparison.OrdinalIgnoreCase))
                        {
                            var moduleNames = NativeCommandArgumentCompletion_ExtractSecondaryArgument(boundArguments, "Module");

                            if (moduleNames.Count > 0)
                            {
                                foreach (string module in moduleNames)
                                {
                                    NativeCompletionGetCommand(context.WordToComplete, module, parameterName, result, context);
                                }
                            }
                            else
                            {
                                NativeCompletionGetCommand(context.WordToComplete, null, parameterName, result, context);
                            }
                            break;
                        }

                        if (parameterName.Equals("ParameterType", StringComparison.OrdinalIgnoreCase))
                        {
                            NativeCompletionTypeName(context, result);
                            break;
                        }

                        break;
                    }
                case "Show-Command":
                    {
                        NativeCompletionGetHelpCommand(context.WordToComplete, parameterName, false, result, context);
                        break;
                    }
                case "help":
                case "Get-Help":
                    {
                        NativeCompletionGetHelpCommand(context.WordToComplete, parameterName, true, result, context);
                        break;
                    }
                case "Invoke-Expression":
                    {
                        if (parameterName.Equals("Command", StringComparison.OrdinalIgnoreCase))
                        {
                            // For argument completion, we don't want to complete against pseudo commands that only work in the script workflow.
                            // The way to avoid that is to pass in a CompletionContext with RelatedAst = null
                            var commandResults = CompleteCommand(new CompletionContext { WordToComplete = context.WordToComplete, Helper = context.Helper });
                            if (commandResults != null)
                                result.AddRange(commandResults);
                        }
                        break;
                    }
                case "Clear-EventLog":
                case "Get-EventLog":
                case "Limit-EventLog":
                case "Remove-EventLog":
                case "Write-EventLog":
                    {
                        NativeCompletionEventLogCommands(context.WordToComplete, parameterName, result, context);
                        break;
                    }
                case "Get-Job":
                case "Receive-Job":
                case "Remove-Job":
                case "Stop-Job":
                case "Wait-Job":
                case "Suspend-Job":
                case "Resume-Job":
                    {
                        NativeCompletionJobCommands(context.WordToComplete, parameterName, result, context);
                        break;
                    }
                case "Disable-ScheduledJob":
                case "Enable-ScheduledJob":
                case "Get-ScheduledJob":
                case "Unregister-ScheduledJob":
                    {
                        NativeCompletionScheduledJobCommands(context.WordToComplete, parameterName, result, context);
                        break;
                    }
                case "Get-Module":
                    {
                        bool loadedModulesOnly = boundArguments == null || !boundArguments.ContainsKey("ListAvailable");
                        NativeCompletionModuleCommands(context.WordToComplete, parameterName, loadedModulesOnly, false, result, context);
                        break;
                    }
                case "Remove-Module":
                    {
                        NativeCompletionModuleCommands(context.WordToComplete, parameterName, true, false, result, context);
                        break;
                    }
                case "Import-Module":
                    {
                        NativeCompletionModuleCommands(context.WordToComplete, parameterName, false, true, result, context);
                        break;
                    }
                case "Debug-Process":
                case "Get-Process":
                case "Stop-Process":
                case "Wait-Process":
                case "Enter-PSHostProcess":
                    {
                        NativeCompletionProcessCommands(context.WordToComplete, parameterName, result, context);
                        break;
                    }
                case "Get-PSDrive":
                case "Remove-PSDrive":
                    {
                        if (parameterName.Equals("PSProvider", StringComparison.OrdinalIgnoreCase))
                        {
                            NativeCompletionProviderCommands(context.WordToComplete, parameterName, result, context);
                        }
                        else if (parameterName.Equals("Name", StringComparison.OrdinalIgnoreCase))
                        {
                            var psProviders = NativeCommandArgumentCompletion_ExtractSecondaryArgument(boundArguments, "PSProvider");
                            if (psProviders.Count > 0)
                            {
                                foreach (string psProvider in psProviders)
                                {
                                    NativeCompletionDriveCommands(context.WordToComplete, psProvider, parameterName, result, context);
                                }
                            }
                            else
                            {
                                NativeCompletionDriveCommands(context.WordToComplete, null, parameterName, result, context);
                            }
                        }

                        break;
                    }
                case "New-PSDrive":
                    {
                        NativeCompletionProviderCommands(context.WordToComplete, parameterName, result, context);
                        break;
                    }
                case "Get-PSProvider":
                    {
                        NativeCompletionProviderCommands(context.WordToComplete, parameterName, result, context);
                        break;
                    }
                case "Get-Service":
                case "Start-Service":
                case "Restart-Service":
                case "Resume-Service":
                case "Set-Service":
                case "Stop-Service":
                case "Suspend-Service":
                    {
                        NativeCompletionServiceCommands(context.WordToComplete, parameterName, result, context);
                        break;
                    }
                case "Clear-Variable":
                case "Get-Variable":
                case "Remove-Variable":
                case "Set-Variable":
                    {
                        NativeCompletionVariableCommands(context.WordToComplete, parameterName, result, context);
                        break;
                    }
                case "Get-Alias":
                    {
                        NativeCompletionAliasCommands(context.WordToComplete, parameterName, result, context);
                        break;
                    }
                case "Get-TraceSource":
                case "Set-TraceSource":
                case "Trace-Command":
                    {
                        NativeCompletionTraceSourceCommands(context.WordToComplete, parameterName, result, context);
                        break;
                    }
                case "Push-Location":
                case "Set-Location":
                    {
                        NativeCompletionSetLocationCommand(context.WordToComplete, parameterName, result, context);
                        break;
                    }
                case "Move-Item":
                case "Copy-Item":
                    {
                        NativeCompletionCopyMoveItemCommand(context.WordToComplete, parameterName, result, context);
                        break;
                    }
                case "New-Item":
                    {
                        NativeCompletionNewItemCommand(context.WordToComplete, parameterName, result, context);
                        break;
                    }
                case "ForEach-Object":
                    {
                        if (parameterName.Equals("MemberName", StringComparison.OrdinalIgnoreCase))
                        {
                            NativeCompletionMemberName(context.WordToComplete, result, commandAst, context);
                        }
                        break;
                    }
                case "Group-Object":
                case "Measure-Object":
                case "Select-Object":
                case "Sort-Object":
                case "Where-Object":
                case "Format-Custom":
                case "Format-List":
                case "Format-Table":
                case "Format-Wide":
                    {
                        if (parameterName.Equals("Property", StringComparison.OrdinalIgnoreCase))
                        {
                            NativeCompletionMemberName(context.WordToComplete, result, commandAst, context);
                        }
                        break;
                    }

                case "New-Object":
                    {
                        if (parameterName.Equals("TypeName", StringComparison.OrdinalIgnoreCase))
                        {
                            NativeCompletionTypeName(context, result);
                        }
                        break;
                    }

                case "Get-CimClass":
                case "Get-CimInstance":
                case "Get-CimAssociatedInstance":
                case "Invoke-CimMethod":
                case "New-CimInstance":
                case "Register-CimIndicationEvent":
                    {
                        NativeCompletionCimCommands(parameterName, boundArguments, result, commandAst, context);
                        break;
                    }

                default:
                    {
                        NativeCompletionPathArgument(context.WordToComplete, parameterName, result, context);
                        break;
                    }
            }
        }

        private static Hashtable GetBoundArgumentsAsHashtable(CompletionContext context)
        {
            var result = new Hashtable(StringComparer.OrdinalIgnoreCase);
            if (context.PseudoBindingInfo != null)
            {
                var boundArguments = context.PseudoBindingInfo.BoundArguments;
                if (boundArguments != null)
                {
                    foreach (var boundArgument in boundArguments)
                    {
                        var astPair = boundArgument.Value as AstPair;
                        if (astPair != null)
                        {
                            var parameterAst = astPair.Argument as CommandParameterAst;
                            var exprAst = parameterAst != null
                                              ? parameterAst.Argument
                                              : astPair.Argument as ExpressionAst;
                            object value;
                            if (exprAst != null && SafeExprEvaluator.TrySafeEval(exprAst, context.ExecutionContext, out value))
                            {
                                result[boundArgument.Key] = value;
                            }
                            continue;
                        }
                        var switchPair = boundArgument.Value as SwitchPair;
                        if (switchPair != null)
                        {
                            result[boundArgument.Key] = switchPair.Argument;
                            continue;
                        }
                        // Ignored:
                        //     AstArrayPair - only used for ValueFromRemainingArguments, not that useful for tab completion
                        //     FakePair - missing argument, not that useful
                        //     PipeObjectPair - no actual argument, makes for a poor api
                    }
                }
            }
            return result;
        }

        private static ScriptBlock GetCustomArgumentCompleter(
            string optionKey,
            IEnumerable<string> keys,
            CompletionContext context)
        {
            ScriptBlock scriptBlock;
            var options = context.Options;
            if (options != null)
            {
                var customCompleters = options[optionKey] as Hashtable;
                if (customCompleters != null)
                {
                    foreach (var key in keys)
                    {
                        if (customCompleters.ContainsKey(key))
                        {
                            scriptBlock = customCompleters[key] as ScriptBlock;
                            if (scriptBlock != null)
                                return scriptBlock;
                        }
                    }
                }
            }

            var registeredCompleters = optionKey.Equals("NativeArgumentCompleters", StringComparison.OrdinalIgnoreCase)
                ? context.NativeArgumentCompleters
                : context.CustomArgumentCompleters;

            if (registeredCompleters != null)
            {
                foreach (var key in keys)
                {
                    if (registeredCompleters.TryGetValue(key, out scriptBlock))
                    {
                        return scriptBlock;
                    }
                }
            }

            return null;
        }


        private static bool InvokeScriptArgumentCompleter(
            ScriptBlock scriptBlock,
            string commandName,
            string parameterName,
            string wordToComplete,
            CommandAst commandAst,
            CompletionContext context,
            List<CompletionResult> resultList)
        {
            bool result = InvokeScriptArgumentCompleter(
                scriptBlock,
                new object[] { commandName, parameterName, wordToComplete, commandAst, GetBoundArgumentsAsHashtable(context) },
                resultList);
            if (result)
            {
                resultList.Add(CompletionResult.Null);
            }

            return result;
        }

        private static bool InvokeScriptArgumentCompleter(
            ScriptBlock scriptBlock,
            object[] argumentsToCompleter,
            List<CompletionResult> result)
        {
            Collection<PSObject> customResults = null;
            try
            {
                customResults = scriptBlock.Invoke(argumentsToCompleter);
            }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
            }

            if (customResults == null || !customResults.Any())
            {
                return false;
            }

            foreach (var customResult in customResults)
            {
                var resultAsCompletion = customResult.BaseObject as CompletionResult;
                if (resultAsCompletion != null)
                {
                    result.Add(resultAsCompletion);
                    continue;
                }

                var resultAsString = customResult.ToString();
                result.Add(new CompletionResult(resultAsString));
            }

            return true;
        }

        // All the methods for native command argument completion will add a null instance of the type CompletionResult to the end of the
        // "result" list, to indicate that this particular argument completion has fallen into one of the native command argument completion methods,
        // and has been processed already. So if the "result" list is still empty afterward, we will not go through the default argument completion anymore.
        #region Native Command Argument Completion

        private static void RemoveLastNullCompletionResult(List<CompletionResult> result)
        {
            if (result.Count > 0 && result[result.Count - 1].Equals(CompletionResult.Null))
            {
                result.RemoveAt(result.Count - 1);
            }
        }

        private static bool NativeCompletionCimCommands_ParseTypeName(PSTypeName typename, out string cimNamespace, out string className)
        {
            cimNamespace = null;
            className = null;
            if (typename == null)
            {
                return false;
            }
            if (typename.Type != null)
            {
                return false;
            }

            var match = Regex.Match(typename.Name, "(?<NetTypeName>.*)#(?<CimNamespace>.*)[/\\\\](?<CimClassName>.*)");
            if (!match.Success)
            {
                return false;
            }

            if (!match.Groups["NetTypeName"].Value.Equals(typeof(CimInstance).FullName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            cimNamespace = match.Groups["CimNamespace"].Value;
            className = match.Groups["CimClassName"].Value;
            return true;
        }

        private static void NativeCompletionCimCommands(
            string parameter,
            Dictionary<string, AstParameterArgumentPair> boundArguments,
            List<CompletionResult> result,
            CommandAst commandAst,
            CompletionContext context)
        {
            if (boundArguments != null)
            {
                AstParameterArgumentPair astParameterArgumentPair;
                if ((boundArguments.TryGetValue("ComputerName", out astParameterArgumentPair)
                     || boundArguments.TryGetValue("CimSession", out astParameterArgumentPair))
                    && astParameterArgumentPair != null)
                {
                    switch (astParameterArgumentPair.ParameterArgumentType)
                    {
                        case AstParameterArgumentType.PipeObject:
                        case AstParameterArgumentType.Fake:
                            break;

                        default:
                            return; // we won't tab-complete remote class names
                    }
                }
            }

            if (parameter.Equals("Namespace", StringComparison.OrdinalIgnoreCase))
            {
                NativeCompletionCimNamespace(result, context);
                result.Add(CompletionResult.Null);
                return;
            }

            string pseudoboundCimNamespace = NativeCommandArgumentCompletion_ExtractSecondaryArgument(boundArguments, "Namespace").FirstOrDefault();
            if (parameter.Equals("ClassName", StringComparison.OrdinalIgnoreCase))
            {
                NativeCompletionCimClassName(pseudoboundCimNamespace, result, context);
                result.Add(CompletionResult.Null);
                return;
            }

            bool gotInstance = false;
            IEnumerable<PSTypeName> cimClassTypeNames = null;
            string pseudoboundClassName = NativeCommandArgumentCompletion_ExtractSecondaryArgument(boundArguments, "ClassName").FirstOrDefault();
            if (pseudoboundClassName != null)
            {
                gotInstance = false;
                var tmp = new List<PSTypeName>();
                tmp.Add(new PSTypeName(typeof(CimInstance).FullName + "#" + (pseudoboundCimNamespace ?? "root/cimv2") + "/" + pseudoboundClassName));
                cimClassTypeNames = tmp;
            }
            else if (boundArguments != null && boundArguments.ContainsKey("InputObject"))
            {
                gotInstance = true;
                cimClassTypeNames = NativeCommandArgumentCompletion_InferTypesOfArugment(boundArguments, commandAst, context, "InputObject");
            }

            if (cimClassTypeNames != null)
            {
                foreach (PSTypeName typeName in cimClassTypeNames)
                {
                    if (NativeCompletionCimCommands_ParseTypeName(typeName, out pseudoboundCimNamespace, out pseudoboundClassName))
                    {
                        if (parameter.Equals("ResultClassName", StringComparison.OrdinalIgnoreCase))
                        {
                            NativeCompletionCimAssociationResultClassName(pseudoboundCimNamespace, pseudoboundClassName, result, context);
                        }
                        else if (parameter.Equals("MethodName", StringComparison.OrdinalIgnoreCase))
                        {
                            NativeCompletionCimMethodName(pseudoboundCimNamespace, pseudoboundClassName, !gotInstance, result, context);
                        }
                    }
                }
                result.Add(CompletionResult.Null);
            }
        }

        private static ConcurrentDictionary<string, IEnumerable<string>> s_cimNamespaceAndClassNameToAssociationResultClassNames =
            new ConcurrentDictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase);

        private static IEnumerable<string> NativeCompletionCimAssociationResultClassName_GetResultClassNames(
            string cimNamespaceOfSource,
            string cimClassNameOfSource)
        {
            StringBuilder safeClassName = new StringBuilder();
            foreach (char c in cimClassNameOfSource)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    safeClassName.Append(c);
                }
            }

            List<string> resultClassNames = new List<string>();
            using (var cimSession = CimSession.Create(null))
            {
                CimClass cimClass = cimSession.GetClass(cimNamespaceOfSource ?? "root/cimv2", cimClassNameOfSource);
                while (cimClass != null)
                {
                    string query = string.Format(
                        CultureInfo.InvariantCulture,
                        "associators of {{{0}}} WHERE SchemaOnly",
                        cimClass.CimSystemProperties.ClassName);

                    resultClassNames.AddRange(
                        cimSession.QueryInstances(cimNamespaceOfSource ?? "root/cimv2", "WQL", query)
                            .Select(associationInstance => associationInstance.CimSystemProperties.ClassName));

                    cimClass = cimClass.CimSuperClass;
                }
            }
            resultClassNames.Sort(StringComparer.OrdinalIgnoreCase);

            return resultClassNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void NativeCompletionCimAssociationResultClassName(
            string pseudoboundNamespace,
            string pseudoboundClassName,
            List<CompletionResult> result,
            CompletionContext context)
        {
            if (string.IsNullOrWhiteSpace(pseudoboundClassName))
            {
                return;
            }

            IEnumerable<string> resultClassNames = s_cimNamespaceAndClassNameToAssociationResultClassNames.GetOrAdd(
                (pseudoboundNamespace ?? "root/cimv2") + ":" + pseudoboundClassName,
                _ => NativeCompletionCimAssociationResultClassName_GetResultClassNames(pseudoboundNamespace, pseudoboundClassName));

            WildcardPattern resultClassNamePattern = WildcardPattern.Get(context.WordToComplete + "*", WildcardOptions.IgnoreCase | WildcardOptions.CultureInvariant);
            result.AddRange(resultClassNames
                .Where(resultClassNamePattern.IsMatch)
                .Select(x => new CompletionResult(x, x, CompletionResultType.Type, string.Format(CultureInfo.InvariantCulture, "{0} -> {1}", pseudoboundClassName, x))));
        }

        private static void NativeCompletionCimMethodName(
            string pseudoboundNamespace,
            string pseudoboundClassName,
            bool staticMethod,
            List<CompletionResult> result,
            CompletionContext context)
        {
            if (string.IsNullOrWhiteSpace(pseudoboundClassName))
            {
                return;
            }

            CimClass cimClass;
            using (var cimSession = CimSession.Create(null))
            {
                cimClass = cimSession.GetClass(pseudoboundNamespace ?? "root/cimv2", pseudoboundClassName);
            }

            WildcardPattern methodNamePattern = WildcardPattern.Get(context.WordToComplete + "*", WildcardOptions.CultureInvariant | WildcardOptions.IgnoreCase);
            List<CompletionResult> localResults = new List<CompletionResult>();
            foreach (CimMethodDeclaration methodDeclaration in cimClass.CimClassMethods)
            {
                string methodName = methodDeclaration.Name;
                if (!methodNamePattern.IsMatch(methodName))
                {
                    continue;
                }

                bool currentMethodIsStatic = methodDeclaration.Qualifiers.Any(q => q.Name.Equals("Static", StringComparison.OrdinalIgnoreCase));
                if ((currentMethodIsStatic && !staticMethod) || (!currentMethodIsStatic && staticMethod))
                {
                    continue;
                }

                StringBuilder tooltipText = new StringBuilder();
                tooltipText.Append(methodName);
                tooltipText.Append("(");
                bool gotFirstParameter = false;
                foreach (var methodParameter in methodDeclaration.Parameters)
                {
                    bool outParameter = methodParameter.Qualifiers.Any(q => q.Name.Equals("Out", StringComparison.OrdinalIgnoreCase));

                    if (!gotFirstParameter)
                    {
                        gotFirstParameter = true;
                    }
                    else
                    {
                        tooltipText.Append(", ");
                    }
                    if (outParameter)
                    {
                        tooltipText.Append("[out] ");
                    }
                    tooltipText.Append(CimInstanceAdapter.CimTypeToTypeNameDisplayString(methodParameter.CimType));
                    tooltipText.Append(" ");
                    tooltipText.Append(methodParameter.Name);

                    if (outParameter)
                    {
                        continue;
                    }
                }
                tooltipText.Append(")");

                localResults.Add(new CompletionResult(methodName, methodName, CompletionResultType.Method, tooltipText.ToString()));
            }

            result.AddRange(localResults.OrderBy(x => x.ListItemText, StringComparer.OrdinalIgnoreCase));
        }

        private static ConcurrentDictionary<string, IEnumerable<string>> s_cimNamespaceToClassNames =
            new ConcurrentDictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase);

        private static IEnumerable<string> NativeCompletionCimClassName_GetClassNames(string targetNamespace)
        {
            List<string> result = new List<string>();
            using (CimSession cimSession = CimSession.Create(null))
            {
                using (var operationOptions = new CimOperationOptions { ClassNamesOnly = true })
                    foreach (CimClass cimClass in cimSession.EnumerateClasses(targetNamespace, null, operationOptions))
                        using (cimClass)
                        {
                            string className = cimClass.CimSystemProperties.ClassName;
                            result.Add(className);
                        }
            }
            return result;
        }

        private static void NativeCompletionCimClassName(
            string pseudoBoundNamespace,
            List<CompletionResult> result,
            CompletionContext context)
        {
            string targetNamespace = pseudoBoundNamespace ?? "root/cimv2";

            List<string> regularClasses = new List<string>();
            List<string> systemClasses = new List<string>();

            IEnumerable<string> allClasses = s_cimNamespaceToClassNames.GetOrAdd(
                targetNamespace,
                NativeCompletionCimClassName_GetClassNames);
            WildcardPattern classNamePattern = WildcardPattern.Get(context.WordToComplete + "*", WildcardOptions.CultureInvariant | WildcardOptions.IgnoreCase);

            foreach (string className in allClasses)
            {
                if (context.Helper.CancelTabCompletion)
                {
                    break;
                }

                if (!classNamePattern.IsMatch(className))
                {
                    continue;
                }

                if (className.Length > 0 && className[0] == '_')
                {
                    systemClasses.Add(className);
                }
                else
                {
                    regularClasses.Add(className);
                }
            }

            regularClasses.Sort(StringComparer.OrdinalIgnoreCase);
            systemClasses.Sort(StringComparer.OrdinalIgnoreCase);
            result.AddRange(
                regularClasses.Concat(systemClasses)
                    .Select(className => new CompletionResult(className, className, CompletionResultType.Type, targetNamespace + ":" + className)));
        }

        private static void NativeCompletionCimNamespace(
            List<CompletionResult> result,
            CompletionContext context)
        {
            string containerNamespace = "root";
            string prefixOfChildNamespace = "";
            if (!string.IsNullOrEmpty(context.WordToComplete))
            {
                int lastSlashOrBackslash = context.WordToComplete.LastIndexOfAny(Utils.Separators.Directory);
                if (lastSlashOrBackslash != (-1))
                {
                    containerNamespace = context.WordToComplete.Substring(0, lastSlashOrBackslash);
                    prefixOfChildNamespace = context.WordToComplete.Substring(lastSlashOrBackslash + 1);
                }
            }

            List<CompletionResult> namespaceResults = new List<CompletionResult>();
            WildcardPattern childNamespacePattern = WildcardPattern.Get(prefixOfChildNamespace + "*", WildcardOptions.IgnoreCase | WildcardOptions.CultureInvariant);
            using (CimSession cimSession = CimSession.Create(null))
            {
                foreach (CimInstance namespaceInstance in cimSession.EnumerateInstances(containerNamespace, "__Namespace"))
                    using (namespaceInstance)
                    {
                        if (context.Helper.CancelTabCompletion)
                        {
                            break;
                        }

                        CimProperty namespaceNameProperty = namespaceInstance.CimInstanceProperties["Name"];
                        if (namespaceNameProperty == null)
                        {
                            continue;
                        }

                        string childNamespace = namespaceNameProperty.Value as string;
                        if (childNamespace == null)
                        {
                            continue;
                        }

                        if (!childNamespacePattern.IsMatch(childNamespace))
                        {
                            continue;
                        }

                        namespaceResults.Add(new CompletionResult(
                                                 containerNamespace + "/" + childNamespace,
                                                 childNamespace,
                                                 CompletionResultType.Namespace,
                                                 containerNamespace + "/" + childNamespace));
                    }
            }

            result.AddRange(namespaceResults.OrderBy(x => x.ListItemText, StringComparer.OrdinalIgnoreCase));
        }

        private static void NativeCompletionGetCommand(string commandName, string moduleName, string paramName, List<CompletionResult> result, CompletionContext context)
        {
            if (!string.IsNullOrEmpty(paramName) && paramName.Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                RemoveLastNullCompletionResult(result);

                // Available commands
                var commandResults = CompleteCommand(new CompletionContext { WordToComplete = commandName, Helper = context.Helper }, moduleName);
                if (commandResults != null)
                    result.AddRange(commandResults);

                // Consider files only if the -Module parameter is not present 
                if (moduleName == null)
                {
                    // ps1 files and directories. We only complete the files with .ps1 extension for Get-Command, because the -Syntax
                    // may only works on files with .ps1 extension
                    var ps1Extension = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { StringLiterals.PowerShellScriptFileExtension };
                    var moduleFilesResults = new List<CompletionResult>(CompleteFilename(new CompletionContext { WordToComplete = commandName, Helper = context.Helper }, false, ps1Extension));
                    if (moduleFilesResults.Count > 0)
                        result.AddRange(moduleFilesResults);
                }

                result.Add(CompletionResult.Null);
            }
            else if (!string.IsNullOrEmpty(paramName) && paramName.Equals("Module", StringComparison.OrdinalIgnoreCase))
            {
                RemoveLastNullCompletionResult(result);

                var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var moduleResults = CompleteModuleName(new CompletionContext { WordToComplete = commandName, Helper = context.Helper }, true);
                if (moduleResults != null)
                {
                    foreach (CompletionResult moduleResult in moduleResults)
                    {
                        if (!modules.Contains(moduleResult.ToolTip))
                        {
                            modules.Add(moduleResult.ToolTip);
                            result.Add(moduleResult);
                        }
                    }
                }

                moduleResults = CompleteModuleName(new CompletionContext { WordToComplete = commandName, Helper = context.Helper }, false);
                if (moduleResults != null)
                {
                    foreach (CompletionResult moduleResult in moduleResults)
                    {
                        if (!modules.Contains(moduleResult.ToolTip))
                        {
                            modules.Add(moduleResult.ToolTip);
                            result.Add(moduleResult);
                        }
                    }
                }

                result.Add(CompletionResult.Null);
            }
        }

        private static void NativeCompletionGetHelpCommand(string commandName, string paramName, bool isHelpRelated, List<CompletionResult> result, CompletionContext context)
        {
            if (!string.IsNullOrEmpty(paramName) && paramName.Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                RemoveLastNullCompletionResult(result);

                // Available commands
                const CommandTypes commandTypes = CommandTypes.Cmdlet | CommandTypes.Function | CommandTypes.Alias | CommandTypes.ExternalScript | CommandTypes.Workflow | CommandTypes.Configuration;
                var commandResults = CompleteCommand(new CompletionContext { WordToComplete = commandName, Helper = context.Helper }, null, commandTypes);
                if (commandResults != null)
                    result.AddRange(commandResults);

                // ps1 files and directories
                var ps1Extension = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { StringLiterals.PowerShellScriptFileExtension };
                var fileResults = new List<CompletionResult>(CompleteFilename(new CompletionContext { WordToComplete = commandName, Helper = context.Helper }, false, ps1Extension));
                if (fileResults.Count > 0)
                    result.AddRange(fileResults);

                if (isHelpRelated)
                {
                    // Available topics
                    var helpTopicResults = CompleteHelpTopics(new CompletionContext { WordToComplete = commandName, Helper = context.Helper });
                    if (helpTopicResults != null)
                        result.AddRange(helpTopicResults);
                }

                result.Add(CompletionResult.Null);
            }
        }

        private static void NativeCompletionEventLogCommands(string logName, string paramName, List<CompletionResult> result, CompletionContext context)
        {
            if (!string.IsNullOrEmpty(paramName) && paramName.Equals("LogName", StringComparison.OrdinalIgnoreCase))
            {
                RemoveLastNullCompletionResult(result);

                logName = logName ?? string.Empty;
                var quote = HandleDoubleAndSingleQuote(ref logName);

                if (!logName.EndsWith("*", StringComparison.Ordinal))
                {
                    logName += "*";
                }
                var pattern = WildcardPattern.Get(logName, WildcardOptions.IgnoreCase);

                var powershell = context.Helper.CurrentPowerShell;
                AddCommandWithPreferenceSetting(powershell, "Microsoft.PowerShell.Management\\Get-EventLog").AddParameter("LogName", "*");

                Exception exceptionThrown;
                var psObjects = context.Helper.ExecuteCurrentPowerShell(out exceptionThrown);

                if (psObjects != null)
                {
                    foreach (dynamic eventLog in psObjects)
                    {
                        var completionText = eventLog.Log.ToString();
                        var listItemText = completionText;

                        if (CompletionRequiresQuotes(completionText, false))
                        {
                            var quoteInUse = quote == string.Empty ? "'" : quote;
                            if (quoteInUse == "'")
                                completionText = completionText.Replace("'", "''");
                            completionText = quoteInUse + completionText + quoteInUse;
                        }
                        else
                        {
                            completionText = quote + completionText + quote;
                        }

                        if (pattern.IsMatch(listItemText))
                        {
                            result.Add(new CompletionResult(completionText, listItemText, CompletionResultType.ParameterValue, listItemText));
                        }
                    }
                }

                result.Add(CompletionResult.Null);
            }
        }

        private static void NativeCompletionJobCommands(string wordToComplete, string paramName, List<CompletionResult> result, CompletionContext context)
        {
            if (string.IsNullOrEmpty(paramName))
                return;

            wordToComplete = wordToComplete ?? string.Empty;
            var quote = HandleDoubleAndSingleQuote(ref wordToComplete);
            var powershell = context.Helper.CurrentPowerShell;

            if (!wordToComplete.EndsWith("*", StringComparison.Ordinal))
            {
                wordToComplete += "*";
            }
            var pattern = WildcardPattern.Get(wordToComplete, WildcardOptions.IgnoreCase);

            if (paramName.Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                AddCommandWithPreferenceSetting(powershell, "Get-Job", typeof(GetJobCommand)).AddParameter("Name", wordToComplete);
            }
            else
            {
                AddCommandWithPreferenceSetting(powershell, "Get-Job", typeof(GetJobCommand)).AddParameter("IncludeChildJob", true);
            }

            Exception exceptionThrown;
            var psObjects = context.Helper.ExecuteCurrentPowerShell(out exceptionThrown);
            if (psObjects == null)
                return;

            if (paramName.Equals("Id", StringComparison.OrdinalIgnoreCase))
            {
                RemoveLastNullCompletionResult(result);

                foreach (dynamic psJob in psObjects)
                {
                    var completionText = psJob.Id.ToString();
                    if (pattern.IsMatch(completionText))
                    {
                        var listItemText = completionText;
                        completionText = quote + completionText + quote;
                        result.Add(new CompletionResult(completionText, listItemText, CompletionResultType.ParameterValue, listItemText));
                    }
                }

                result.Add(CompletionResult.Null);
            }
            else if (paramName.Equals("InstanceId", StringComparison.OrdinalIgnoreCase))
            {
                RemoveLastNullCompletionResult(result);

                foreach (dynamic psJob in psObjects)
                {
                    var completionText = psJob.InstanceId.ToString();
                    if (pattern.IsMatch(completionText))
                    {
                        var listItemText = completionText;
                        completionText = quote + completionText + quote;
                        result.Add(new CompletionResult(completionText, listItemText, CompletionResultType.ParameterValue, listItemText));
                    }
                }

                result.Add(CompletionResult.Null);
            }
            else if (paramName.Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                RemoveLastNullCompletionResult(result);

                foreach (dynamic psJob in psObjects)
                {
                    var completionText = psJob.Name;
                    var listItemText = completionText;

                    if (CompletionRequiresQuotes(completionText, false))
                    {
                        var quoteInUse = quote == string.Empty ? "'" : quote;
                        if (quoteInUse == "'")
                            completionText = completionText.Replace("'", "''");
                        completionText = quoteInUse + completionText + quoteInUse;
                    }
                    else
                    {
                        completionText = quote + completionText + quote;
                    }

                    result.Add(new CompletionResult(completionText, listItemText, CompletionResultType.ParameterValue, listItemText));
                }

                result.Add(CompletionResult.Null);
            }
        }

        private static void NativeCompletionScheduledJobCommands(string wordToComplete, string paramName, List<CompletionResult> result, CompletionContext context)
        {
            if (string.IsNullOrEmpty(paramName))
                return;

            wordToComplete = wordToComplete ?? string.Empty;
            var quote = HandleDoubleAndSingleQuote(ref wordToComplete);
            var powershell = context.Helper.CurrentPowerShell;

            if (!wordToComplete.EndsWith("*", StringComparison.Ordinal))
            {
                wordToComplete += "*";
            }
            var pattern = WildcardPattern.Get(wordToComplete, WildcardOptions.IgnoreCase);

            if (paramName.Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                AddCommandWithPreferenceSetting(powershell, "PSScheduledJob\\Get-ScheduledJob").AddParameter("Name", wordToComplete);
            }
            else
            {
                AddCommandWithPreferenceSetting(powershell, "PSScheduledJob\\Get-ScheduledJob");
            }

            Exception exceptionThrown;
            var psObjects = context.Helper.ExecuteCurrentPowerShell(out exceptionThrown);
            if (psObjects == null)
                return;

            if (paramName.Equals("Id", StringComparison.OrdinalIgnoreCase))
            {
                RemoveLastNullCompletionResult(result);

                foreach (dynamic psJob in psObjects)
                {
                    var completionText = psJob.Id.ToString();
                    if (pattern.IsMatch(completionText))
                    {
                        var listItemText = completionText;
                        completionText = quote + completionText + quote;
                        result.Add(new CompletionResult(completionText, listItemText, CompletionResultType.ParameterValue, listItemText));
                    }
                }

                result.Add(CompletionResult.Null);
            }
            else if (paramName.Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                RemoveLastNullCompletionResult(result);

                foreach (dynamic psJob in psObjects)
                {
                    var completionText = psJob.Name;
                    var listItemText = completionText;

                    if (CompletionRequiresQuotes(completionText, false))
                    {
                        var quoteInUse = quote == string.Empty ? "'" : quote;
                        if (quoteInUse == "'")
                            completionText = completionText.Replace("'", "''");
                        completionText = quoteInUse + completionText + quoteInUse;
                    }
                    else
                    {
                        completionText = quote + completionText + quote;
                    }

                    result.Add(new CompletionResult(completionText, listItemText, CompletionResultType.ParameterValue, listItemText));
                }

                result.Add(CompletionResult.Null);
            }
        }

        private static void NativeCompletionModuleCommands(string assemblyOrModuleName, string paramName, bool loadedModulesOnly, bool isImportModule, List<CompletionResult> result, CompletionContext context)
        {
            if (string.IsNullOrEmpty(paramName))
            {
                return;
            }

            if (paramName.Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                RemoveLastNullCompletionResult(result);

                if (isImportModule)
                {
                    var moduleExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                            {   StringLiterals.PowerShellScriptFileExtension,
                                StringLiterals.PowerShellModuleFileExtension,
                                StringLiterals.PowerShellDataFileExtension,
                                StringLiterals.PowerShellNgenAssemblyExtension,
                                StringLiterals.DependentWorkflowAssemblyExtension,
                                StringLiterals.PowerShellCmdletizationFileExtension,
                                StringLiterals.WorkflowFileExtension
                            };
                    var moduleFilesResults = new List<CompletionResult>(CompleteFilename(new CompletionContext { WordToComplete = assemblyOrModuleName, Helper = context.Helper }, false, moduleExtensions));
                    if (moduleFilesResults.Count > 0)
                        result.AddRange(moduleFilesResults);

                    if (assemblyOrModuleName.IndexOfAny(Utils.Separators.DirectoryOrDrive) != -1)
                    {
                        // The partial input is a path, then we don't iterate modules under $ENV:PSModulePath
                        return;
                    }
                }

                var moduleResults = CompleteModuleName(new CompletionContext { WordToComplete = assemblyOrModuleName, Helper = context.Helper }, loadedModulesOnly);
                if (moduleResults != null && moduleResults.Count > 0)
                    result.AddRange(moduleResults);

                result.Add(CompletionResult.Null);
            }
            else if (paramName.Equals("Assembly", StringComparison.OrdinalIgnoreCase))
            {
                RemoveLastNullCompletionResult(result);

                var moduleExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".dll" };
                var moduleFilesResults = new List<CompletionResult>(CompleteFilename(new CompletionContext { WordToComplete = assemblyOrModuleName, Helper = context.Helper }, false, moduleExtensions));
                if (moduleFilesResults.Count > 0)
                    result.AddRange(moduleFilesResults);

                result.Add(CompletionResult.Null);
            }
        }

        private static void NativeCompletionProcessCommands(string wordToComplete, string paramName, List<CompletionResult> result, CompletionContext context)
        {
            if (string.IsNullOrEmpty(paramName))
                return;

            wordToComplete = wordToComplete ?? string.Empty;
            var quote = HandleDoubleAndSingleQuote(ref wordToComplete);
            var powershell = context.Helper.CurrentPowerShell;

            if (!wordToComplete.EndsWith("*", StringComparison.Ordinal))
            {
                wordToComplete += "*";
            }

            if (paramName.Equals("Id", StringComparison.OrdinalIgnoreCase))
            {
                AddCommandWithPreferenceSetting(powershell, "Microsoft.PowerShell.Management\\Get-Process");
            }
            else
            {
                AddCommandWithPreferenceSetting(powershell, "Microsoft.PowerShell.Management\\Get-Process").AddParameter("Name", wordToComplete);
            }

            Exception exceptionThrown;
            var psObjects = context.Helper.ExecuteCurrentPowerShell(out exceptionThrown);
            if (psObjects == null)
                return;

            if (paramName.Equals("Id", StringComparison.OrdinalIgnoreCase))
            {
                RemoveLastNullCompletionResult(result);

                var pattern = WildcardPattern.Get(wordToComplete, WildcardOptions.IgnoreCase);
                foreach (dynamic process in psObjects)
                {
                    var completionText = process.Id.ToString();
                    if (pattern.IsMatch(completionText))
                    {
                        var listItemText = completionText;
                        completionText = quote + completionText + quote;
                        result.Add(new CompletionResult(completionText, listItemText, CompletionResultType.ParameterValue, listItemText));
                    }
                }

                result.Add(CompletionResult.Null);
            }
            else if (paramName.Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                RemoveLastNullCompletionResult(result);

                var uniqueSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (dynamic process in psObjects)
                {
                    var completionText = process.Name;
                    var listItemText = completionText;

                    if (uniqueSet.Contains(completionText))
                        continue;

                    uniqueSet.Add(completionText);
                    if (CompletionRequiresQuotes(completionText, false))
                    {
                        var quoteInUse = quote == string.Empty ? "'" : quote;
                        if (quoteInUse == "'")
                            completionText = completionText.Replace("'", "''");
                        completionText = quoteInUse + completionText + quoteInUse;
                    }
                    else
                    {
                        completionText = quote + completionText + quote;
                    }

                    result.Add(new CompletionResult(completionText, listItemText, CompletionResultType.ParameterValue, listItemText));
                }

                result.Add(CompletionResult.Null);
            }
        }

        private static void NativeCompletionProviderCommands(string providerName, string paramName, List<CompletionResult> result, CompletionContext context)
        {
            if (string.IsNullOrEmpty(paramName) || !paramName.Equals("PSProvider", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            RemoveLastNullCompletionResult(result);

            providerName = providerName ?? string.Empty;
            var quote = HandleDoubleAndSingleQuote(ref providerName);
            var powershell = context.Helper.CurrentPowerShell;

            if (!providerName.EndsWith("*", StringComparison.Ordinal))
            {
                providerName += "*";
            }

            AddCommandWithPreferenceSetting(powershell, "Microsoft.PowerShell.Management\\Get-PSProvider").AddParameter("PSProvider", providerName);
            Exception exceptionThrown;
            var psObjects = context.Helper.ExecuteCurrentPowerShell(out exceptionThrown);
            if (psObjects == null)
                return;

            foreach (dynamic providerInfo in psObjects)
            {
                var completionText = providerInfo.Name;
                var listItemText = completionText;

                if (CompletionRequiresQuotes(completionText, false))
                {
                    var quoteInUse = quote == string.Empty ? "'" : quote;
                    if (quoteInUse == "'")
                        completionText = completionText.Replace("'", "''");
                    completionText = quoteInUse + completionText + quoteInUse;
                }
                else
                {
                    completionText = quote + completionText + quote;
                }

                result.Add(new CompletionResult(completionText, listItemText, CompletionResultType.ParameterValue, listItemText));
            }

            result.Add(CompletionResult.Null);
        }

        private static void NativeCompletionDriveCommands(string wordToComplete, string psProvider, string paramName, List<CompletionResult> result, CompletionContext context)
        {
            if (string.IsNullOrEmpty(paramName) || !paramName.Equals("Name", StringComparison.OrdinalIgnoreCase))
                return;

            RemoveLastNullCompletionResult(result);

            wordToComplete = wordToComplete ?? string.Empty;
            var quote = HandleDoubleAndSingleQuote(ref wordToComplete);
            var powershell = context.Helper.CurrentPowerShell;

            if (!wordToComplete.EndsWith("*", StringComparison.Ordinal))
            {
                wordToComplete += "*";
            }

            AddCommandWithPreferenceSetting(powershell, "Microsoft.PowerShell.Management\\Get-PSDrive").AddParameter("Name", wordToComplete);
            if (psProvider != null)
                powershell.AddParameter("PSProvider", psProvider);

            Exception exceptionThrown;
            var psObjects = context.Helper.ExecuteCurrentPowerShell(out exceptionThrown);
            if (psObjects != null)
            {
                foreach (dynamic driveInfo in psObjects)
                {
                    var completionText = driveInfo.Name;
                    var listItemText = completionText;

                    if (CompletionRequiresQuotes(completionText, false))
                    {
                        var quoteInUse = quote == string.Empty ? "'" : quote;
                        if (quoteInUse == "'")
                            completionText = completionText.Replace("'", "''");
                        completionText = quoteInUse + completionText + quoteInUse;
                    }
                    else
                    {
                        completionText = quote + completionText + quote;
                    }

                    result.Add(new CompletionResult(completionText, listItemText, CompletionResultType.ParameterValue, listItemText));
                }
            }

            result.Add(CompletionResult.Null);
        }

        private static void NativeCompletionServiceCommands(string wordToComplete, string paramName, List<CompletionResult> result, CompletionContext context)
        {
            if (string.IsNullOrEmpty(paramName))
                return;

            wordToComplete = wordToComplete ?? string.Empty;
            var quote = HandleDoubleAndSingleQuote(ref wordToComplete);
            var powershell = context.Helper.CurrentPowerShell;

            if (!wordToComplete.EndsWith("*", StringComparison.Ordinal))
            {
                wordToComplete += "*";
            }

            Exception exceptionThrown;
            if (paramName.Equals("DisplayName", StringComparison.OrdinalIgnoreCase))
            {
                RemoveLastNullCompletionResult(result);

                AddCommandWithPreferenceSetting(powershell, "Microsoft.PowerShell.Management\\Get-Service")
                    .AddParameter("DisplayName", wordToComplete);
                AddCommandWithPreferenceSetting(powershell, "Microsoft.PowerShell.Utility\\Sort-Object")
                    .AddParameter("Property", "DisplayName");
                var psObjects = context.Helper.ExecuteCurrentPowerShell(out exceptionThrown);
                if (psObjects != null)
                {
                    foreach (dynamic serviceInfo in psObjects)
                    {
                        var completionText = serviceInfo.DisplayName;
                        var listItemText = completionText;

                        if (CompletionRequiresQuotes(completionText, false))
                        {
                            var quoteInUse = quote == string.Empty ? "'" : quote;
                            if (quoteInUse == "'")
                                completionText = completionText.Replace("'", "''");
                            completionText = quoteInUse + completionText + quoteInUse;
                        }
                        else
                        {
                            completionText = quote + completionText + quote;
                        }

                        result.Add(new CompletionResult(completionText, listItemText, CompletionResultType.ParameterValue, listItemText));
                    }
                }

                result.Add(CompletionResult.Null);
            }
            else if (paramName.Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                RemoveLastNullCompletionResult(result);

                AddCommandWithPreferenceSetting(powershell, "Microsoft.PowerShell.Management\\Get-Service").AddParameter("Name", wordToComplete);
                var psObjects = context.Helper.ExecuteCurrentPowerShell(out exceptionThrown);
                if (psObjects != null)
                {
                    foreach (dynamic serviceInfo in psObjects)
                    {
                        var completionText = serviceInfo.Name;
                        var listItemText = completionText;

                        if (CompletionRequiresQuotes(completionText, false))
                        {
                            var quoteInUse = quote == string.Empty ? "'" : quote;
                            if (quoteInUse == "'")
                                completionText = completionText.Replace("'", "''");
                            completionText = quoteInUse + completionText + quoteInUse;
                        }
                        else
                        {
                            completionText = quote + completionText + quote;
                        }

                        result.Add(new CompletionResult(completionText, listItemText, CompletionResultType.ParameterValue, listItemText));
                    }
                }

                result.Add(CompletionResult.Null);
            }
        }

        private static void NativeCompletionVariableCommands(string variableName, string paramName, List<CompletionResult> result, CompletionContext context)
        {
            if (string.IsNullOrEmpty(paramName) || !paramName.Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            RemoveLastNullCompletionResult(result);

            variableName = variableName ?? string.Empty;
            var quote = HandleDoubleAndSingleQuote(ref variableName);
            var powershell = context.Helper.CurrentPowerShell;

            if (!variableName.EndsWith("*", StringComparison.Ordinal))
            {
                variableName += "*";
            }

            AddCommandWithPreferenceSetting(powershell, "Microsoft.PowerShell.Utility\\Get-Variable").AddParameter("Name", variableName);
            Exception exceptionThrown;
            var psObjects = context.Helper.ExecuteCurrentPowerShell(out exceptionThrown);
            if (psObjects == null)
                return;

            foreach (dynamic variable in psObjects)
            {
                var effectiveQuote = quote;
                var completionText = variable.Name;
                var listItemText = completionText;

                // Handle special characters ? and * in variable names
                if (completionText.IndexOfAny(Utils.Separators.StarOrQuestion) != -1)
                {
                    effectiveQuote = "'";
                    completionText = completionText.Replace("?", "`?");
                    completionText = completionText.Replace("*", "`*");
                }

                if (!completionText.Equals("$", StringComparison.Ordinal) && CompletionRequiresQuotes(completionText, false))
                {
                    var quoteInUse = effectiveQuote == string.Empty ? "'" : effectiveQuote;
                    if (quoteInUse == "'")
                        completionText = completionText.Replace("'", "''");
                    completionText = quoteInUse + completionText + quoteInUse;
                }
                else
                {
                    completionText = effectiveQuote + completionText + effectiveQuote;
                }

                result.Add(new CompletionResult(completionText, listItemText, CompletionResultType.ParameterValue, listItemText));
            }

            result.Add(CompletionResult.Null);
        }

        private static void NativeCompletionAliasCommands(string commandName, string paramName, List<CompletionResult> result, CompletionContext context)
        {
            if (string.IsNullOrEmpty(paramName) ||
                (!paramName.Equals("Definition", StringComparison.OrdinalIgnoreCase) &&
                 !paramName.Equals("Name", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            RemoveLastNullCompletionResult(result);

            if (paramName.Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                commandName = commandName ?? string.Empty;
                var quote = HandleDoubleAndSingleQuote(ref commandName);
                var powershell = context.Helper.CurrentPowerShell;

                if (!commandName.EndsWith("*", StringComparison.Ordinal))
                {
                    commandName += "*";
                }

                Exception exceptionThrown;
                AddCommandWithPreferenceSetting(powershell, "Microsoft.PowerShell.Utility\\Get-Alias").AddParameter("Name", commandName);
                var psObjects = context.Helper.ExecuteCurrentPowerShell(out exceptionThrown);
                if (psObjects != null)
                {
                    foreach (dynamic aliasInfo in psObjects)
                    {
                        var completionText = aliasInfo.Name;
                        var listItemText = completionText;

                        if (CompletionRequiresQuotes(completionText, false))
                        {
                            var quoteInUse = quote == string.Empty ? "'" : quote;
                            if (quoteInUse == "'")
                                completionText = completionText.Replace("'", "''");
                            completionText = quoteInUse + completionText + quoteInUse;
                        }
                        else
                        {
                            completionText = quote + completionText + quote;
                        }

                        result.Add(new CompletionResult(completionText, listItemText, CompletionResultType.ParameterValue, listItemText));
                    }
                }
            }
            else
            {
                // Complete for the parameter Definition
                // Available commands
                const CommandTypes commandTypes = CommandTypes.Cmdlet | CommandTypes.Function | CommandTypes.ExternalScript | CommandTypes.Workflow | CommandTypes.Configuration;
                var commandResults = CompleteCommand(new CompletionContext { WordToComplete = commandName, Helper = context.Helper }, null, commandTypes);
                if (commandResults != null && commandResults.Count > 0)
                    result.AddRange(commandResults);

                // The parameter Definition takes a file
                var fileResults = new List<CompletionResult>(CompleteFilename(new CompletionContext { WordToComplete = commandName, Helper = context.Helper }));
                if (fileResults.Count > 0)
                    result.AddRange(fileResults);
            }

            result.Add(CompletionResult.Null);
        }

        private static void NativeCompletionTraceSourceCommands(string traceSourceName, string paramName, List<CompletionResult> result, CompletionContext context)
        {
            if (string.IsNullOrEmpty(paramName) || !paramName.Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            RemoveLastNullCompletionResult(result);

            traceSourceName = traceSourceName ?? string.Empty;
            var quote = HandleDoubleAndSingleQuote(ref traceSourceName);
            var powershell = context.Helper.CurrentPowerShell;

            if (!traceSourceName.EndsWith("*", StringComparison.Ordinal))
            {
                traceSourceName += "*";
            }

            AddCommandWithPreferenceSetting(powershell, "Microsoft.PowerShell.Utility\\Get-TraceSource").AddParameter("Name", traceSourceName);
            Exception exceptionThrown;
            var psObjects = context.Helper.ExecuteCurrentPowerShell(out exceptionThrown);
            if (psObjects == null)
                return;

            foreach (dynamic trace in psObjects)
            {
                var completionText = trace.Name;
                var listItemText = completionText;

                if (CompletionRequiresQuotes(completionText, false))
                {
                    var quoteInUse = quote == string.Empty ? "'" : quote;
                    if (quoteInUse == "'")
                        completionText = completionText.Replace("'", "''");
                    completionText = quoteInUse + completionText + quoteInUse;
                }
                else
                {
                    completionText = quote + completionText + quote;
                }

                result.Add(new CompletionResult(completionText, listItemText, CompletionResultType.ParameterValue, listItemText));
            }

            result.Add(CompletionResult.Null);
        }

        private static void NativeCompletionSetLocationCommand(string dirName, string paramName, List<CompletionResult> result, CompletionContext context)
        {
            if (string.IsNullOrEmpty(paramName) ||
                (!paramName.Equals("Path", StringComparison.OrdinalIgnoreCase) &&
                 !paramName.Equals("LiteralPath", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            RemoveLastNullCompletionResult(result);

            context.WordToComplete = dirName ?? string.Empty;
            var clearLiteralPath = false;
            if (paramName.Equals("LiteralPath", StringComparison.OrdinalIgnoreCase))
            {
                clearLiteralPath = TurnOnLiteralPathOption(context);
            }

            try
            {
                var fileNameResults = CompleteFilename(context, true, null);
                if (fileNameResults != null)
                    result.AddRange(fileNameResults);
            }
            finally
            {
                if (clearLiteralPath)
                    context.Options.Remove("LiteralPaths");
            }

            result.Add(CompletionResult.Null);
        }

        /// <summary>
        /// Provides completion results for NewItemCommand
        /// </summary>
        /// <param name="itemTypeToComplete">The item provided by user for completion.</param>
        /// <param name="paramName">Name of the parameter whose value needs completion.</param>
        /// <param name="result">List of completion suggestions.</param>
        /// <param name="context">Completion context.</param>        
        private static void NativeCompletionNewItemCommand(string itemTypeToComplete, string paramName, List<CompletionResult> result, CompletionContext context)
        {
            if (string.IsNullOrEmpty(paramName))
            {
                return;
            }

            var powershell = context.Helper.CurrentPowerShell;
            var executionContext = powershell.GetContextFromTLS();

            var boundArgs = GetBoundArgumentsAsHashtable(context);
            var providedPath = boundArgs["Path"] as string ?? executionContext.SessionState.Path.CurrentLocation.Path;

            ProviderInfo provider;
            executionContext.LocationGlobber.GetProviderPath(providedPath, out provider);

            var isFileSystem = provider != null &&
                               provider.Name.Equals(FileSystemProvider.ProviderName, StringComparison.OrdinalIgnoreCase);

            //AutoComplete only if filesystem provider.
            if (isFileSystem)
            {
                if (paramName.Equals("ItemType", StringComparison.OrdinalIgnoreCase))
                {
                    if (!String.IsNullOrEmpty(itemTypeToComplete))
                    {
                        WildcardPattern patternEvaluator = WildcardPattern.Get(itemTypeToComplete + "*", WildcardOptions.IgnoreCase);

                        if (patternEvaluator.IsMatch("file"))
                        {
                            result.Add(new CompletionResult("File"));
                        }
                        else if (patternEvaluator.IsMatch("directory"))
                        {
                            result.Add(new CompletionResult("Directory"));
                        }
                        else if (patternEvaluator.IsMatch("symboliclink"))
                        {
                            result.Add(new CompletionResult("SymbolicLink"));
                        }
                        else if (patternEvaluator.IsMatch("junction"))
                        {
                            result.Add(new CompletionResult("Junction"));
                        }
                        else if (patternEvaluator.IsMatch("hardlink"))
                        {
                            result.Add(new CompletionResult("HardLink"));
                        }
                    }
                    else
                    {
                        result.Add(new CompletionResult("File"));
                        result.Add(new CompletionResult("Directory"));
                        result.Add(new CompletionResult("SymbolicLink"));
                        result.Add(new CompletionResult("Junction"));
                        result.Add(new CompletionResult("HardLink"));
                    }

                    result.Add(CompletionResult.Null);
                }
            }
        }

        private static void NativeCompletionCopyMoveItemCommand(string pathName, string paramName, List<CompletionResult> result, CompletionContext context)
        {
            if (string.IsNullOrEmpty(paramName))
            {
                return;
            }

            if (paramName.Equals("LiteralPath", StringComparison.OrdinalIgnoreCase) || paramName.Equals("Path", StringComparison.OrdinalIgnoreCase))
            {
                NativeCompletionPathArgument(pathName, paramName, result, context);
            }
            else if (paramName.Equals("Destination", StringComparison.OrdinalIgnoreCase))
            {
                // The parameter Destination for Move-Item and Copy-Item takes literal path
                RemoveLastNullCompletionResult(result);

                context.WordToComplete = pathName ?? string.Empty;
                var clearLiteralPath = TurnOnLiteralPathOption(context);

                try
                {
                    var fileNameResults = CompleteFilename(context);
                    if (fileNameResults != null)
                        result.AddRange(fileNameResults);
                }
                finally
                {
                    if (clearLiteralPath)
                        context.Options.Remove("LiteralPaths");
                }

                result.Add(CompletionResult.Null);
            }
        }

        private static void NativeCompletionPathArgument(string pathName, string paramName, List<CompletionResult> result, CompletionContext context)
        {
            if (string.IsNullOrEmpty(paramName) ||
                (!paramName.Equals("LiteralPath", StringComparison.OrdinalIgnoreCase) &&
                (!paramName.Equals("Path", StringComparison.OrdinalIgnoreCase)) &&
                (!paramName.Equals("FilePath", StringComparison.OrdinalIgnoreCase))))
            {
                return;
            }

            RemoveLastNullCompletionResult(result);

            context.WordToComplete = pathName ?? string.Empty;
            var clearLiteralPath = false;
            if (paramName.Equals("LiteralPath", StringComparison.OrdinalIgnoreCase))
            {
                clearLiteralPath = TurnOnLiteralPathOption(context);
            }

            try
            {
                var fileNameResults = CompleteFilename(context);
                if (fileNameResults != null)
                    result.AddRange(fileNameResults);
            }
            finally
            {
                if (clearLiteralPath)
                    context.Options.Remove("LiteralPaths");
            }

            result.Add(CompletionResult.Null);
        }

        private static void NativeCompletionMemberName(string wordToComplete, List<CompletionResult> result, CommandAst commandAst, CompletionContext context)
        {
            // Command is something like where-object/foreach-object/format-list/etc. where there is a parameter that is a property name
            // and we want member names based on the input object, which is either the parameter InputObject, or comes from the pipeline.
            var pipelineAst = commandAst.Parent as PipelineAst;
            if (pipelineAst == null)
                return;

            int i;
            for (i = 0; i < pipelineAst.PipelineElements.Count; i++)
            {
                if (pipelineAst.PipelineElements[i] == commandAst)
                    break;
            }

            IEnumerable<PSTypeName> prevType = null;
            if (i == 0)
            {
                AstParameterArgumentPair pair;
                if (!context.PseudoBindingInfo.BoundArguments.TryGetValue("InputObject", out pair)
                    || !pair.ArgumentSpecified)
                {
                    return;
                }
                var astPair = pair as AstPair;
                if (astPair == null || astPair.Argument == null)
                {
                    return;
                }
                prevType = astPair.Argument.GetInferredType(context);
            }
            else
            {
                prevType = pipelineAst.PipelineElements[i - 1].GetInferredType(context);
            }

            CompleteMemberByInferredType(context, prevType, result, wordToComplete + "*", filter: IsPropertyMember, isStatic: false);
            result.Add(CompletionResult.Null);
        }

        private static void NativeCompletionTypeName(CompletionContext context, List<CompletionResult> result)
        {
            var wordToComplete = context.WordToComplete;
            var isQuoted = wordToComplete.Length > 0 && (wordToComplete[0].IsSingleQuote() || wordToComplete[0].IsDoubleQuote());
            string prefix = "";
            string suffix = "";
            if (isQuoted)
            {
                prefix = suffix = wordToComplete.Substring(0, 1);

                var endQuoted = (wordToComplete.Length > 1) && wordToComplete[wordToComplete.Length - 1] == wordToComplete[0];
                wordToComplete = wordToComplete.Substring(1, wordToComplete.Length - (endQuoted ? 2 : 1));
            }
            if (wordToComplete.IndexOf('[') != -1)
            {
                var cursor = (InternalScriptPosition)context.CursorPosition;
                cursor = cursor.CloneWithNewOffset(cursor.Offset - context.TokenAtCursor.Extent.StartOffset - (isQuoted ? 1 : 0));
                var fullTypeName = Parser.ScanType(wordToComplete, ignoreErrors: true);
                var typeNameToComplete = CompletionAnalysis.FindTypeNameToComplete(fullTypeName, cursor);
                if (typeNameToComplete == null)
                    return;

                var openBrackets = 0;
                var closeBrackets = 0;
                foreach (char c in wordToComplete)
                {
                    if (c == '[') openBrackets += 1;
                    else if (c == ']') closeBrackets += 1;
                }
                wordToComplete = typeNameToComplete.FullName;
                var typeNameText = fullTypeName.Extent.Text;
                if (!isQuoted)
                {
                    // We need to add quotes - the square bracket messes up parsing the argument
                    prefix = suffix = "'";
                }
                if (closeBrackets < openBrackets)
                {
                    suffix = suffix.Insert(0, new string(']', (openBrackets - closeBrackets)));
                }

                if (isQuoted && closeBrackets == openBrackets)
                {
                    // Already quoted, and has matching [].  We can give a better Intellisense experience
                    // if we only replace the minimum.
                    context.ReplacementIndex = typeNameToComplete.Extent.StartOffset + context.TokenAtCursor.Extent.StartOffset + 1;
                    context.ReplacementLength = wordToComplete.Length;
                    prefix = suffix = "";
                }
                else
                {
                    prefix += typeNameText.Substring(0, typeNameToComplete.Extent.StartOffset);
                    suffix = suffix.Insert(0, typeNameText.Substring(typeNameToComplete.Extent.EndOffset));
                }
            }

            context.WordToComplete = wordToComplete;

            var typeResults = CompleteType(context, prefix, suffix);
            if (typeResults != null)
            {
                result.AddRange(typeResults);
            }
            result.Add(CompletionResult.Null);
        }

        #endregion Native Command Argument Completion


        /// <summary>
        /// Find the positional argument at the specific position from the parsed argument list
        /// </summary>
        /// <param name="parsedArguments"></param>
        /// <param name="position"></param>
        /// <param name="lastPositionalArgument"></param>
        /// <returns>
        /// If the command line after the [tab] will not be truncated, the return value could be non-null: Get-Cmdlet [tab] abc
        /// If the command line after the [tab] is truncated, the return value will always be null
        /// </returns>
        private static AstPair FindTargetPositionalArgument(Collection<AstParameterArgumentPair> parsedArguments, int position, out AstPair lastPositionalArgument)
        {
            int index = 0;
            lastPositionalArgument = null;
            foreach (AstParameterArgumentPair pair in parsedArguments)
            {
                if (!pair.ParameterSpecified && index == position)
                    return (AstPair)pair;
                else if (!pair.ParameterSpecified)
                {
                    index++;
                    lastPositionalArgument = (AstPair)pair;
                }
            }

            // Cannot find an existing positional argument at 'position'
            return null;
        }

        /// <summary>
        /// Find the location where 'tab' is typed based on the line and colum.
        /// </summary>
        private static ArgumentLocation FindTargetArgumentLocation(Collection<AstParameterArgumentPair> parsedArguments, Token token)
        {
            int position = 0;
            AstParameterArgumentPair prevArg = null;
            foreach (AstParameterArgumentPair pair in parsedArguments)
            {
                switch (pair.ParameterArgumentType)
                {
                    case AstParameterArgumentType.AstPair:
                        {
                            var arg = (AstPair)pair;
                            if (arg.ParameterSpecified)
                            {
                                // Named argument
                                if (arg.Parameter.Extent.StartOffset > token.Extent.StartOffset)
                                {
                                    // case: Get-Cmdlet <tab> -Param abc
                                    return GenerateArgumentLocation(prevArg, position);
                                }

                                if (!arg.ParameterContainsArgument && arg.Argument.Extent.StartOffset > token.Extent.StartOffset)
                                {
                                    // case: Get-Cmdlet -Param <tab> abc
                                    return new ArgumentLocation() { Argument = arg, IsPositional = false, Position = -1 };
                                }
                            }
                            else
                            {
                                // Positional argument
                                if (arg.Argument.Extent.StartOffset > token.Extent.StartOffset)
                                {
                                    // case: Get-Cmdlet <tab> abc
                                    return GenerateArgumentLocation(prevArg, position);
                                }
                                position++;
                            }
                            prevArg = arg;
                        }
                        break;
                    case AstParameterArgumentType.Fake:
                    case AstParameterArgumentType.Switch:
                        {
                            if (pair.Parameter.Extent.StartOffset > token.Extent.StartOffset)
                            {
                                return GenerateArgumentLocation(prevArg, position);
                            }
                            prevArg = pair;
                        }
                        break;
                    case AstParameterArgumentType.AstArray:
                    case AstParameterArgumentType.PipeObject:
                        Diagnostics.Assert(false, "parsed arguments should not contain AstArray and PipeObject");
                        break;
                }
            }

            // The 'tab' should be typed after the last argument
            return GenerateArgumentLocation(prevArg, position);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="prev">the argument that is right before the 'tab' location</param>
        /// <param name="position">the number of positional arguments before the 'tab' location</param>
        /// <returns></returns>
        private static ArgumentLocation GenerateArgumentLocation(AstParameterArgumentPair prev, int position)
        {
            // Tab is typed before the first argument
            if (prev == null)
            {
                return new ArgumentLocation() { Argument = null, IsPositional = true, Position = 0 };
            }

            switch (prev.ParameterArgumentType)
            {
                case AstParameterArgumentType.AstPair:
                case AstParameterArgumentType.Switch:
                    if (!prev.ParameterSpecified)
                        return new ArgumentLocation() { Argument = null, IsPositional = true, Position = position };

                    return prev.Parameter.Extent.Text.EndsWith(":", StringComparison.Ordinal)
                        ? new ArgumentLocation() { Argument = prev, IsPositional = false, Position = -1 }
                        : new ArgumentLocation() { Argument = null, IsPositional = true, Position = position };
                case AstParameterArgumentType.Fake:
                    return new ArgumentLocation() { Argument = prev, IsPositional = false, Position = -1 };
                default:
                    Diagnostics.Assert(false, "parsed arguments should not contain AstArray and PipeObject");
                    return null;
            }
        }

        /// <summary>
        /// Find the location where 'tab' is typed based on the expressionAst.
        /// </summary>
        /// <param name="parsedArguments"></param>
        /// <param name="expAst"></param>
        /// <returns></returns>
        private static ArgumentLocation FindTargetArgumentLocation(Collection<AstParameterArgumentPair> parsedArguments, ExpressionAst expAst)
        {
            Diagnostics.Assert(expAst != null, "Caller needs to make sure expAst is not null");
            int position = 0;
            foreach (AstParameterArgumentPair pair in parsedArguments)
            {
                switch (pair.ParameterArgumentType)
                {
                    case AstParameterArgumentType.AstPair:
                        {
                            AstPair arg = (AstPair)pair;
                            if (arg.ArgumentIsCommandParameterAst)
                                continue;

                            if (arg.ParameterContainsArgument && arg.Argument == expAst)
                            {
                                return new ArgumentLocation() { IsPositional = false, Position = -1, Argument = arg };
                            }

                            if (arg.Argument.GetHashCode() == expAst.GetHashCode())
                            {
                                return arg.ParameterSpecified ?
                                    new ArgumentLocation() { IsPositional = false, Position = -1, Argument = arg } :
                                    new ArgumentLocation() { IsPositional = true, Position = position, Argument = arg };
                            }

                            if (!arg.ParameterSpecified)
                                position++;
                        }
                        break;
                    case AstParameterArgumentType.Fake:
                    case AstParameterArgumentType.Switch:
                        // FakePair and SwitchPair contains no ExpressionAst
                        break;
                    case AstParameterArgumentType.AstArray:
                    case AstParameterArgumentType.PipeObject:
                        Diagnostics.Assert(false, "parsed arguments should not contain AstArray and PipeObject arguments");
                        break;
                }
            }

            // We should be able to find the ExpAst from the parsed argument list, if all parameters was specified correctly.
            // We may try to complete something incorrect
            // ls -Recurse -QQQ qwe<+tab>
            return null;
        }

        private sealed class ArgumentLocation
        {
            internal bool IsPositional { get; set; }
            internal int Position { get; set; }
            internal AstParameterArgumentPair Argument { get; set; }
        }

        #endregion Command Arguments

        #region Filenames

        /// <summary>
        /// 
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        [SuppressMessage("Microsoft.Naming", "CA1702:CompoundWordsShouldBeCasedCorrectly")]
        public static IEnumerable<CompletionResult> CompleteFilename(string fileName)
        {
            var runspace = Runspace.DefaultRunspace;
            if (runspace == null)
            {
                // No runspace, just return no results.
                return CommandCompletion.EmptyCompletionResult;
            }

            var helper = new CompletionExecutionHelper(PowerShell.Create(RunspaceMode.CurrentRunspace));
            return CompleteFilename(new CompletionContext { WordToComplete = fileName, Helper = helper });
        }

        internal static IEnumerable<CompletionResult> CompleteFilename(CompletionContext context)
        {
            return CompleteFilename(context, false, null);
        }

        [SuppressMessage("Microsoft.Naming", "CA1702:CompoundWordsShouldBeCasedCorrectly")]
        internal static IEnumerable<CompletionResult> CompleteFilename(CompletionContext context, bool containerOnly, HashSet<string> extension)
        {
            var wordToComplete = context.WordToComplete;
            var quote = HandleDoubleAndSingleQuote(ref wordToComplete);
            var results = new List<CompletionResult>();

            // First, try to match \\server\share
            var shareMatch = Regex.Match(wordToComplete, "^\\\\\\\\([^\\\\]+)\\\\([^\\\\]*)$");
            if (shareMatch.Success)
            {
                // Only match share names, no filenames.
                var server = shareMatch.Groups[1].Value;
                var sharePattern = WildcardPattern.Get(shareMatch.Groups[2].Value + "*", WildcardOptions.IgnoreCase);
                var ignoreHidden = context.GetOption("IgnoreHiddenShares", @default: false);
                var shares = GetFileShares(server, ignoreHidden);
                foreach (var share in shares)
                {
                    if (sharePattern.IsMatch(share))
                    {
                        string shareFullPath = "\\\\" + server + "\\" + share;
                        if (quote != string.Empty)
                        {
                            shareFullPath = quote + shareFullPath + quote;
                        }
                        results.Add(new CompletionResult(shareFullPath, shareFullPath, CompletionResultType.ProviderContainer, shareFullPath));
                    }
                }
            }
            else
            {
                var powershell = context.Helper.CurrentPowerShell;
                var executionContext = powershell.GetContextFromTLS();

                // We want to prefer relative paths in a completion result unless the user has already
                // specified a drive or portion of the path.
                string unused;
                var defaultRelative = string.IsNullOrWhiteSpace(wordToComplete)
                                      || (wordToComplete.IndexOfAny(Utils.Separators.Directory) != 0 &&
                                          !Regex.Match(wordToComplete, @"^~[\\/]+.*").Success &&
                                          !executionContext.LocationGlobber.IsAbsolutePath(wordToComplete, out unused));
                var relativePaths = context.GetOption("RelativePaths", @default: defaultRelative);
                var useLiteralPath = context.GetOption("LiteralPaths", @default: false);

                if (useLiteralPath && LocationGlobber.StringContainsGlobCharacters(wordToComplete))
                {
                    wordToComplete = WildcardPattern.Escape(wordToComplete, Utils.Separators.StarOrQuestion);
                }

                if (!defaultRelative && wordToComplete.Length >= 2 && wordToComplete[1] == ':' && char.IsLetter(wordToComplete[0]) && context.ExecutionContext != null)
                {
                    // We don't actually need the drive, but the drive must be "mounted" in PowerShell before completion
                    // can succeed.  This call will mount the drive if it wasn't already.
                    context.ExecutionContext.SessionState.Drive.GetAtScope(wordToComplete.Substring(0, 1), "global");
                }

                AddCommandWithPreferenceSetting(powershell, "Microsoft.PowerShell.Management\\Resolve-Path")
                    .AddParameter("Path", wordToComplete + "*");

                Exception exceptionThrown;
                var psobjs = context.Helper.ExecuteCurrentPowerShell(out exceptionThrown);

                if (psobjs != null)
                {
                    var isFileSystem = false;
                    var wordContainsProviderId = ProviderSpecified(wordToComplete);

                    if (psobjs.Count > 0)
                    {
                        dynamic firstObj = psobjs[0];
                        var provider = firstObj.Provider as ProviderInfo;
                        isFileSystem = provider != null &&
                                       provider.Name.Equals(FileSystemProvider.ProviderName,
                                                            StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        try
                        {
                            ProviderInfo provider;
                            if (defaultRelative)
                            {
                                provider = executionContext.EngineSessionState.CurrentDrive.Provider;
                            }
                            else
                            {
                                executionContext.LocationGlobber.GetProviderPath(wordToComplete, out provider);
                            }
                            isFileSystem = provider != null &&
                                           provider.Name.Equals(FileSystemProvider.ProviderName,
                                                                StringComparison.OrdinalIgnoreCase);
                        }
                        catch (Exception e)
                        {
                            CommandProcessorBase.CheckForSevereException(e);
                        }
                    }

                    if (isFileSystem)
                    {
                        bool hiddenFilesAreHandled = false;

                        if (psobjs.Count > 0 && !LocationGlobber.StringContainsGlobCharacters(wordToComplete))
                        {
                            string leaf = null;
                            string pathWithoutProvider = wordContainsProviderId
                                    ? wordToComplete.Substring(wordToComplete.IndexOf(':') + 2)
                                    : wordToComplete;

                            try
                            {
                                leaf = Path.GetFileName(pathWithoutProvider);
                            }
                            catch (Exception e)
                            {
                                CommandProcessorBase.CheckForSevereException(e);
                            }

                            var notHiddenEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            string providerPath = null;

                            foreach (dynamic entry in psobjs)
                            {
                                providerPath = entry.ProviderPath;
                                if (string.IsNullOrEmpty(providerPath))
                                {
                                    // This is unexpected. ProviderPath should never be null or an empty string
                                    leaf = null;
                                    break;
                                }

                                if (!notHiddenEntries.Contains(providerPath))
                                {
                                    notHiddenEntries.Add(providerPath);
                                }
                            }

                            if (leaf != null)
                            {
                                leaf = leaf + "*";
                                var parentPath = Path.GetDirectoryName(providerPath);

                                // ProviderPath should be absolute path for FileSystem entries
                                if (!string.IsNullOrEmpty(parentPath))
                                {
                                    string[] entries = null;
                                    try
                                    {
                                        entries = Directory.GetFileSystemEntries(parentPath, leaf);
                                    }
                                    catch (Exception e)
                                    {
                                        CommandProcessorBase.CheckForSevereException(e);
                                    }

                                    if (entries != null)
                                    {
                                        hiddenFilesAreHandled = true;

                                        if (entries.Length > notHiddenEntries.Count)
                                        {
                                            // Do the iteration only if there are hidden files
                                            foreach (var entry in entries)
                                            {
                                                if (notHiddenEntries.Contains(entry))
                                                    continue;

                                                var fileInfo = new FileInfo(entry);
                                                if ((fileInfo.Attributes & FileAttributes.Hidden) != 0)
                                                {
                                                    PSObject wrapper = PSObject.AsPSObject(entry);
                                                    psobjs.Add(wrapper);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        if (!hiddenFilesAreHandled)
                        {
                            AddCommandWithPreferenceSetting(powershell, "Microsoft.PowerShell.Management\\Get-ChildItem")
                                .AddParameter("Path", wordToComplete + "*")
                                .AddParameter("Hidden", true);

                            var hiddenItems = context.Helper.ExecuteCurrentPowerShell(out exceptionThrown);
                            if (hiddenItems != null && hiddenItems.Count > 0)
                            {
                                foreach (var hiddenItem in hiddenItems)
                                {
                                    psobjs.Add(hiddenItem);
                                }
                            }
                        }
                    }

                    // Sorting the results by the path
                    var sortedPsobjs = psobjs.OrderBy(a => a, new ItemPathComparer());

                    foreach (PSObject psobj in sortedPsobjs)
                    {
                        object baseObj = PSObject.Base(psobj);
                        string path = null, providerPath = null;

                        // Get the path, the PSObject could be:
                        // 1. a PathInfo object -- results of Resolve-Path
                        // 2. a FileSystemInfo Object -- results of Get-ChildItem
                        // 3. a string -- the path results return by the direct .NET API invocation
                        var baseObjAsPathInfo = baseObj as PathInfo;
                        if (baseObjAsPathInfo != null)
                        {
                            path = baseObjAsPathInfo.Path;
                            providerPath = baseObjAsPathInfo.ProviderPath;
                        }
                        else if (baseObj is FileSystemInfo)
                        {
                            // The target provider is the FileSystem
                            dynamic dirResult = psobj;
                            providerPath = dirResult.FullName;
                            path = wordContainsProviderId ? dirResult.PSPath : providerPath;
                        }
                        else
                        {
                            var baseObjAsString = baseObj as string;
                            if (baseObjAsString != null)
                            {
                                // The target provider is the FileSystem
                                providerPath = baseObjAsString;
                                path = wordContainsProviderId
                                    ? FileSystemProvider.ProviderName + "::" + baseObjAsString
                                    : providerPath;
                            }
                        }

                        if (path == null) continue;
                        if (isFileSystem && providerPath == null) continue;

                        string completionText;
                        if (relativePaths)
                        {
                            try
                            {
                                var sessionStateInternal = executionContext.EngineSessionState;
                                completionText = sessionStateInternal.NormalizeRelativePath(path, sessionStateInternal.CurrentLocation.ProviderPath);
                                string parentDirectory = ".." + Path.DirectorySeparatorChar;
                                if (!completionText.StartsWith(parentDirectory, StringComparison.Ordinal))
                                    completionText = Path.Combine(".", completionText);
                            }
                            catch (Exception e)
                            {
                                // The object at the specifie path is not accessable, such as c:\hiberfil.sys (for hibernation) or c:\pagefile.sys (for paging)
                                // We ignore those files
                                CommandProcessorBase.CheckForSevereException(e);
                                continue;
                            }
                        }
                        else
                        {
                            completionText = path;
                        }

                        if (ProviderSpecified(completionText) && !wordContainsProviderId)
                        {
                            // Remove the provider id from the path: cd \\scratch2\scratch\dongbw
                            var index = completionText.IndexOf(':');
                            completionText = completionText.Substring(index + 2);
                        }

                        if (CompletionRequiresQuotes(completionText, !useLiteralPath))
                        {
                            var quoteInUse = quote == string.Empty ? "'" : quote;
                            if (quoteInUse == "'")
                            {
                                completionText = completionText.Replace("'", "''");
                            }
                            else
                            {
                                // When double quote is in use, we have to escape the backtip and '$' even when using literal path
                                //   Get-Content -LiteralPath ".\a``g.txt"
                                completionText = completionText.Replace("`", "``");
                                completionText = completionText.Replace("$", "`$");
                            }

                            if (!useLiteralPath)
                            {
                                if (quoteInUse == "'")
                                {
                                    completionText = completionText.Replace("[", "`[");
                                    completionText = completionText.Replace("]", "`]");
                                }
                                else
                                {
                                    completionText = completionText.Replace("[", "``[");
                                    completionText = completionText.Replace("]", "``]");
                                }
                            }

                            completionText = quoteInUse + completionText + quoteInUse;
                        }
                        else if (quote != string.Empty)
                        {
                            completionText = quote + completionText + quote;
                        }

                        if (isFileSystem)
                        {
                            // Use .NET APIs directly to reduce the time overhead
                            var isContainer = Directory.Exists(providerPath);
                            if (containerOnly && !isContainer)
                                continue;

                            if (!containerOnly && !isContainer && !CheckFileExtension(providerPath, extension))
                                continue;

                            string tooltip = providerPath, listItemText = Path.GetFileName(providerPath);
                            results.Add(new CompletionResult(completionText, listItemText,
                                                             isContainer ? CompletionResultType.ProviderContainer : CompletionResultType.ProviderItem,
                                                             tooltip));
                        }
                        else
                        {
                            AddCommandWithPreferenceSetting(powershell, "Microsoft.PowerShell.Management\\Get-Item")
                                .AddParameter("LiteralPath", path);
                            var items = context.Helper.ExecuteCurrentPowerShell(out exceptionThrown);
                            if (items != null && items.Count == 1)
                            {
                                dynamic item = items[0];
                                var isContainer = LanguagePrimitives.ConvertTo<bool>(item.PSIsContainer);

                                if (containerOnly && !isContainer)
                                    continue;

                                AddCommandWithPreferenceSetting(powershell, "Microsoft.PowerShell.Management\\Convert-Path")
                                    .AddParameter("LiteralPath", item.PSPath);
                                var tooltips = context.Helper.ExecuteCurrentPowerShell(out exceptionThrown);
                                string tooltip = null, listItemText = item.PSChildName;
                                if (tooltips != null && tooltips.Count == 1)
                                {
                                    tooltip = PSObject.Base(tooltips[0]) as string;
                                }
                                if (string.IsNullOrEmpty(listItemText))
                                {
                                    // For provider items that don't have PSChildName values, such as variable::error
                                    listItemText = item.Name;
                                }

                                results.Add(new CompletionResult(completionText, listItemText,
                                                                 isContainer ? CompletionResultType.ProviderContainer : CompletionResultType.ProviderItem,
                                                                 tooltip ?? path));
                            }
                            else
                            {
                                // We can get here when get-item fails, perhaps due an acl or whatever.
                                results.Add(new CompletionResult(completionText));
                            }
                        } // End of not filesystem case
                    } // End of foreach
                } // End of 'if (psobjs != null)'
            }

            return results;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHARE_INFO_1
        {
            public string netname;
            public int type;
            public string remark;
        }

        private const int MAX_PREFERRED_LENGTH = -1;
        private const int NERR_Success = 0;
        private const int ERROR_MORE_DATA = 234;
        private const int STYPE_DISKTREE = 0;
        private const int STYPE_MASK = 0x000000FF;

        [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetShareEnum(string serverName, int level, out IntPtr bufptr, int prefMaxLen,
                                               out uint entriesRead, out uint totalEntries, ref uint resumeHandle);

        internal static List<string> GetFileShares(string machine, bool ignoreHidden)
        {
#if UNIX
            return new List<string>();
#else
            IntPtr shBuf;
            uint numEntries;
            uint totalEntries;
            uint resumeHandle = 0;
            int result = NetShareEnum(machine, 1, out shBuf,
                                        MAX_PREFERRED_LENGTH, out numEntries, out totalEntries,
                                        ref resumeHandle);

            var shares = new List<string>();
            if (result == NERR_Success || result == ERROR_MORE_DATA)
            {
                for (int i = 0; i < numEntries; ++i)
                {
                    IntPtr curInfoPtr = (IntPtr)((long)shBuf + (ClrFacade.SizeOf<SHARE_INFO_1>() * i));
                    SHARE_INFO_1 shareInfo = ClrFacade.PtrToStructure<SHARE_INFO_1>(curInfoPtr);


                    if ((shareInfo.type & STYPE_MASK) != STYPE_DISKTREE)
                        continue;
                    if (ignoreHidden && shareInfo.netname.EndsWith("$", StringComparison.Ordinal))
                        continue;
                    shares.Add(shareInfo.netname);
                }
            }
            return shares;
#endif
        }

        private static bool CheckFileExtension(string path, HashSet<string> extension)
        {
            if (extension == null || extension.Count == 0)
                return true;

            var ext = System.IO.Path.GetExtension(path);
            return ext == null || extension.Contains(ext);
        }

        #endregion Filenames

        #region Variable

        /// <summary>
        /// 
        /// </summary>
        /// <param name="variableName"></param>
        /// <returns></returns>
        public static IEnumerable<CompletionResult> CompleteVariable(string variableName)
        {
            var runspace = Runspace.DefaultRunspace;
            if (runspace == null)
            {
                // No runspace, just return no results.
                return CommandCompletion.EmptyCompletionResult;
            }

            var helper = new CompletionExecutionHelper(PowerShell.Create(RunspaceMode.CurrentRunspace));
            return CompleteVariable(new CompletionContext { WordToComplete = variableName, Helper = helper });
        }

        private static readonly string[] s_variableScopes = new string[] { "Global:", "Local:", "Script:", "Private:" };
        private static readonly char[] s_charactersRequiringQuotes = new char[] {
            '-', '`', '&', '@', '\'', '"', '#', '{', '}', '(', ')', '$', ',', ';', '|', '<', '>', ' ', '.', '\\', '/', '\t', '^',
        };

        internal static List<CompletionResult> CompleteVariable(CompletionContext context)
        {
            HashSet<string> hashedResults = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<CompletionResult> results = new List<CompletionResult>();

            var wordToComplete = context.WordToComplete;
            var colon = wordToComplete.IndexOf(':');

            var prefix = "$";
            var lastAst = context.RelatedAsts.Last();
            var variableAst = lastAst as VariableExpressionAst;
            if (variableAst != null && variableAst.Splatted)
            {
                prefix = "@";
            }

            // Look for variables in the input (e.g. parameters, etc.) before checking session state - these
            // variables might not exist in session state yet.
            var wildcardPattern = WildcardPattern.Get(wordToComplete + "*", WildcardOptions.IgnoreCase);
            if (lastAst != null)
            {
                Ast parent = lastAst.Parent;
                var findVariablesVisitor = new FindVariablesVisitor { CompletionVariableAst = lastAst };
                while (parent != null)
                {
                    if (parent is IParameterMetadataProvider)
                    {
                        findVariablesVisitor.Top = parent;
                        parent.Visit(findVariablesVisitor);
                    }
                    parent = parent.Parent;
                }

                foreach (Tuple<string, Ast> varAst in findVariablesVisitor.VariableSources)
                {
                    Ast astTarget = null;
                    string userPath = null;

                    VariableExpressionAst variableDefinitionAst = varAst.Item2 as VariableExpressionAst;
                    if (variableDefinitionAst != null)
                    {
                        userPath = varAst.Item1;
                        astTarget = varAst.Item2.Parent;
                    }
                    else
                    {
                        CommandAst commandParameterAst = varAst.Item2 as CommandAst;
                        if (commandParameterAst != null)
                        {
                            userPath = varAst.Item1;
                            astTarget = varAst.Item2;
                        }
                    }

                    if (String.IsNullOrEmpty(userPath))
                    {
                        Diagnostics.Assert(false, "Found a variable source but it was an unknown AST type.");
                    }

                    if (wildcardPattern.IsMatch(userPath))
                    {
                        var completedName = (userPath.IndexOfAny(s_charactersRequiringQuotes) == -1)
                                                ? prefix + userPath
                                                : prefix + "{" + userPath + "}";
                        var tooltip = userPath;
                        var ast = astTarget;

                        while (ast != null)
                        {
                            var parameterAst = ast as ParameterAst;
                            if (parameterAst != null)
                            {
                                var typeConstraint = parameterAst.Attributes.OfType<TypeConstraintAst>().FirstOrDefault();
                                if (typeConstraint != null)
                                {
                                    tooltip = StringUtil.Format("{0}${1}", typeConstraint.Extent.Text, userPath);
                                }

                                break;
                            }

                            var assignmentAst = ast.Parent as AssignmentStatementAst;
                            if (assignmentAst != null)
                            {
                                if (assignmentAst.Left == ast)
                                {
                                    tooltip = ast.Extent.Text;
                                }
                                break;
                            }

                            var commandAst = ast as CommandAst;
                            if (commandAst != null)
                            {
                                PSTypeName discoveredType = ast.GetInferredType(context).FirstOrDefault<PSTypeName>();
                                if (discoveredType != null)
                                {
                                    tooltip = StringUtil.Format("[{0}]${1}", discoveredType.Name, userPath);
                                }
                                break;
                            }

                            ast = ast.Parent;
                        }
                        AddUniqueVariable(hashedResults, results, completedName, userPath, tooltip);
                    }
                }
            }

            string pattern;
            string provider;
            if (colon == -1)
            {
                pattern = "variable:" + wordToComplete + "*";
                provider = "";
            }
            else
            {
                provider = wordToComplete.Substring(0, colon + 1);
                if (s_variableScopes.Contains(provider, StringComparer.OrdinalIgnoreCase))
                {
                    pattern = "variable:" + wordToComplete.Substring(colon + 1) + "*";
                }
                else
                {
                    pattern = wordToComplete + "*";
                }
            }

            var powershell = context.Helper.CurrentPowerShell;
            AddCommandWithPreferenceSetting(powershell, "Microsoft.PowerShell.Management\\Get-Item").AddParameter("Path", pattern);
            AddCommandWithPreferenceSetting(powershell, "Microsoft.PowerShell.Utility\\Sort-Object").AddParameter("Property", "Name");

            Exception exceptionThrown;
            var psobjs = context.Helper.ExecuteCurrentPowerShell(out exceptionThrown);
            if (psobjs != null)
            {
                foreach (dynamic psobj in psobjs)
                {
                    var name = psobj.Name as string;
                    if (!string.IsNullOrEmpty(name))
                    {
                        var tooltip = name;
                        var variable = PSObject.Base(psobj) as PSVariable;
                        if (variable != null)
                        {
                            var value = variable.Value;
                            if (value != null)
                            {
                                tooltip = StringUtil.Format("[{0}]${1}",
                                                            ToStringCodeMethods.Type(value.GetType(),
                                                                                     dropNamespaces: true), name);
                            }
                        }

                        var completedName = (name.IndexOfAny(s_charactersRequiringQuotes) == -1)
                                                ? prefix + provider + name
                                                : prefix + "{" + provider + name + "}";
                        AddUniqueVariable(hashedResults, results, completedName, name, tooltip);
                    }
                }
            }

            if (colon == -1 && "env".StartsWith(wordToComplete, StringComparison.OrdinalIgnoreCase))
            {
                powershell = context.Helper.CurrentPowerShell;
                AddCommandWithPreferenceSetting(powershell, "Microsoft.PowerShell.Management\\Get-Item").AddParameter("Path", "env:*");
                AddCommandWithPreferenceSetting(powershell, "Microsoft.PowerShell.Utility\\Sort-Object").AddParameter("Property", "Key");

                psobjs = context.Helper.ExecuteCurrentPowerShell(out exceptionThrown);
                if (psobjs != null)
                {
                    foreach (dynamic psobj in psobjs)
                    {
                        var name = psobj.Name as string;
                        if (!string.IsNullOrEmpty(name))
                        {
                            name = "env:" + name;
                            var completedName = (name.IndexOfAny(s_charactersRequiringQuotes) == -1)
                                                    ? prefix + name
                                                    : prefix + "{" + name + "}";
                            AddUniqueVariable(hashedResults, results, completedName, name, "[string]" + name);
                        }
                    }
                }
            }

            // Return variables already in session state first, because we can sometimes give better information,
            // like the variables type.
            foreach (var specialVariable in s_specialVariablesCache.Value)
            {
                if (wildcardPattern.IsMatch(specialVariable))
                {
                    var completedName = (specialVariable.IndexOfAny(s_charactersRequiringQuotes) == -1)
                                            ? prefix + specialVariable
                                            : prefix + "{" + specialVariable + "}";

                    AddUniqueVariable(hashedResults, results, completedName, specialVariable, specialVariable);
                }
            }

            if (colon == -1)
            {
                // If no drive was specified, then look for matching drives/scopes
                pattern = wordToComplete + "*";
                AddCommandWithPreferenceSetting(powershell, "Microsoft.PowerShell.Management\\Get-PSDrive").AddParameter("Name", pattern);
                AddCommandWithPreferenceSetting(powershell, "Microsoft.PowerShell.Utility\\Sort-Object").AddParameter("Property", "Name");
                psobjs = context.Helper.ExecuteCurrentPowerShell(out exceptionThrown);
                if (psobjs != null)
                {
                    foreach (var psobj in psobjs)
                    {
                        var driveInfo = PSObject.Base(psobj) as PSDriveInfo;
                        if (driveInfo != null)
                        {
                            var name = driveInfo.Name;
                            if (name != null && !string.IsNullOrWhiteSpace(name) && name.Length > 1)
                            {
                                var completedName = (name.IndexOfAny(s_charactersRequiringQuotes) == -1)
                                                        ? prefix + name + ":"
                                                        : prefix + "{" + name + ":}";

                                var tooltip = string.IsNullOrEmpty(driveInfo.Description) ? name : driveInfo.Description;
                                AddUniqueVariable(hashedResults, results, completedName, name, tooltip);
                            }
                        }
                    }
                }

                var scopePattern = WildcardPattern.Get(pattern, WildcardOptions.IgnoreCase);
                foreach (var scope in s_variableScopes)
                {
                    if (scopePattern.IsMatch(scope))
                    {
                        var completedName = (scope.IndexOfAny(s_charactersRequiringQuotes) == -1)
                                                ? prefix + scope
                                                : prefix + "{" + scope + "}";
                        AddUniqueVariable(hashedResults, results, completedName, scope, scope);
                    }
                }
            }

            return results;
        }

        private static void AddUniqueVariable(HashSet<string> hashedResults, List<CompletionResult> results, string completionText, string listItemText, string tooltip)
        {
            if (!hashedResults.Contains(completionText))
            {
                hashedResults.Add(completionText);
                results.Add(new CompletionResult(completionText, listItemText, CompletionResultType.Variable, tooltip));
            }
        }

        private class FindVariablesVisitor : AstVisitor
        {
            internal Ast Top;
            internal Ast CompletionVariableAst;
            internal readonly List<Tuple<string, Ast>> VariableSources = new List<Tuple<string, Ast>>();

            public override AstVisitAction VisitVariableExpression(VariableExpressionAst variableExpressionAst)
            {
                if (variableExpressionAst != CompletionVariableAst)
                {
                    VariableSources.Add(new Tuple<string, Ast>(variableExpressionAst.VariablePath.UserPath, variableExpressionAst));
                }
                return AstVisitAction.Continue;
            }

            public override AstVisitAction VisitCommand(CommandAst commandAst)
            {
                // MSFT: 784739 Stack overflow during tab completion of pipeline variable
                // $null | % -pv p { $p<TAB> -> In this case $p is pipelinevariable
                // and is used in the same command. PipelineVariables are not available
                // in the command they are assigned in. Hence the following code ignores
                // if the variable being completed is in the command extent.
                if ((commandAst != CompletionVariableAst) && (!CompletionVariableAst.Extent.IsWithin(commandAst.Extent)))
                {
                    string[] desiredParameters = new string[] { "PV", "PipelineVariable", "OV", "OutVariable" };

                    StaticBindingResult bindingResult = StaticParameterBinder.BindCommand(commandAst, false, desiredParameters);
                    if (bindingResult != null)
                    {
                        ParameterBindingResult parameterBindingResult;

                        foreach (string commandVariableParameter in desiredParameters)
                        {
                            if (bindingResult.BoundParameters.TryGetValue(commandVariableParameter, out parameterBindingResult))
                            {
                                VariableSources.Add(new Tuple<string, Ast>((string)parameterBindingResult.ConstantValue, commandAst));
                            }
                        }
                    }
                }

                return AstVisitAction.Continue;
            }

            public override AstVisitAction VisitFunctionDefinition(FunctionDefinitionAst functionDefinitionAst)
            {
                return functionDefinitionAst != Top ? AstVisitAction.SkipChildren : AstVisitAction.Continue;
            }

            public override AstVisitAction VisitScriptBlockExpression(ScriptBlockExpressionAst scriptBlockExpressionAst)
            {
                return scriptBlockExpressionAst != Top ? AstVisitAction.SkipChildren : AstVisitAction.Continue;
            }

            public override AstVisitAction VisitScriptBlock(ScriptBlockAst scriptBlockAst)
            {
                return scriptBlockAst != Top ? AstVisitAction.SkipChildren : AstVisitAction.Continue;
            }
        }

        private static readonly Lazy<SortedSet<string>> s_specialVariablesCache = new Lazy<SortedSet<string>>(BuildSpecialVariablesCache);

        private static SortedSet<string> BuildSpecialVariablesCache()
        {
            var result = new SortedSet<string>();
            foreach (var member in typeof(SpecialVariables).GetFields(BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (member.FieldType.Equals(typeof(string)))
                {
                    result.Add((string)member.GetValue(null));
                }
            }
            return result;
        }

        #endregion Variables

        #region Comments

        // Complete the history entries
        internal static List<CompletionResult> CompleteComment(CompletionContext context)
        {
            List<CompletionResult> results = new List<CompletionResult>();

            Match matchResult = Regex.Match(context.WordToComplete, @"^#([\w\-]*)$");
            if (!matchResult.Success) { return results; }

            string wordToComplete = matchResult.Groups[1].Value;
            PowerShell powershell = context.Helper.CurrentPowerShell;
            Collection<PSObject> psobjs;
            Exception exceptionThrown;

            int entryId;
            if (Regex.IsMatch(wordToComplete, @"^[0-9]+$") && LanguagePrimitives.TryConvertTo(wordToComplete, out entryId))
            {
                AddCommandWithPreferenceSetting(powershell, "Get-History", typeof(GetHistoryCommand)).AddParameter("Id", entryId);
                psobjs = context.Helper.ExecuteCurrentPowerShell(out exceptionThrown);

                if (psobjs != null && psobjs.Count == 1)
                {
                    var historyInfo = PSObject.Base(psobjs[0]) as HistoryInfo;
                    if (historyInfo != null)
                    {
                        var commandLine = historyInfo.CommandLine;
                        if (!string.IsNullOrEmpty(commandLine))
                        {
                            // var tooltip = "Id: " + historyInfo.Id + "\n" +
                            //               "ExecutionStatus: " + historyInfo.ExecutionStatus + "\n" +
                            //               "StartExecutionTime: " + historyInfo.StartExecutionTime + "\n" +
                            //               "EndExecutionTime: " + historyInfo.EndExecutionTime + "\n";
                            // Use the commandLine as the Tooltip in case the commandLine is multiple lines of scripts
                            results.Add(new CompletionResult(commandLine, commandLine, CompletionResultType.History, commandLine));
                        }
                    }
                }

                return results;
            }

            wordToComplete = "*" + wordToComplete + "*";
            AddCommandWithPreferenceSetting(powershell, "Get-History", typeof(GetHistoryCommand));

            psobjs = context.Helper.ExecuteCurrentPowerShell(out exceptionThrown);
            var pattern = WildcardPattern.Get(wordToComplete, WildcardOptions.IgnoreCase);

            if (psobjs != null)
            {
                for (int index = psobjs.Count - 1; index >= 0; index--)
                {
                    var psobj = psobjs[index];
                    var historyInfo = PSObject.Base(psobj) as HistoryInfo;
                    if (historyInfo == null) continue;

                    var commandLine = historyInfo.CommandLine;
                    if (!string.IsNullOrEmpty(commandLine) && pattern.IsMatch(commandLine))
                    {
                        // var tooltip = "Id: " + historyInfo.Id + "\n" +
                        //               "ExecutionStatus: " + historyInfo.ExecutionStatus + "\n" +
                        //               "StartExecutionTime: " + historyInfo.StartExecutionTime + "\n" +
                        //               "EndExecutionTime: " + historyInfo.EndExecutionTime + "\n";
                        // Use the commandLine as the Tooltip in case the commandLine is multiple lines of scripts
                        results.Add(new CompletionResult(commandLine, commandLine, CompletionResultType.History, commandLine));
                    }
                }
            }

            return results;
        }

        #endregion Comments

        #region Members

        // List of extension methods <MethodName, Signature>
        private static readonly List<Tuple<string, string>> s_extensionMethods =
            new List<Tuple<string, string>>
                {
                    new Tuple<string, string>("Where", "Where({ expression } [, mode [, numberToReturn]])"),
                    new Tuple<string, string>("ForEach", "ForEach(expression [, arguments...])")
                };
        // List of DSC collection-value variables
        private static readonly HashSet<string> s_dscCollectionVariables =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SelectedNodes", "AllNodes" };

        internal static List<CompletionResult> CompleteMember(CompletionContext context, bool @static)
        {
            // If we get here, we know that either:
            //   * the cursor appeared immediately after a member access token ('.' or '::').
            //   * the parent of the ast on the cursor was a member expression.
            //
            // In the first case, we have 2 possibilities:
            //   * the last ast is an error ast because no member name was entered and we were in expression context
            //   * the last ast is a string constant, with something like:   echo $foo.

            var results = new List<CompletionResult>();

            var lastAst = context.RelatedAsts.Last();
            var lastAstAsMemberExpr = lastAst as MemberExpressionAst;
            Ast memberNameCandidateAst = null;
            ExpressionAst targetExpr = null;
            if (lastAstAsMemberExpr != null)
            {
                // If the cursor is not inside the member name in the member expression, assume
                // that the user had incomplete input, but the parser got lucky and succeeded parsing anyway.
                if (context.TokenAtCursor.Extent.StartOffset >= lastAstAsMemberExpr.Member.Extent.StartOffset)
                {
                    memberNameCandidateAst = lastAstAsMemberExpr.Member;
                }
                targetExpr = lastAstAsMemberExpr.Expression;
            }
            else
            {
                memberNameCandidateAst = lastAst;
            }
            var memberNameAst = memberNameCandidateAst as StringConstantExpressionAst;

            var memberName = "*";
            if (memberNameAst != null)
            {
                // Make sure to correctly handle: echo $foo.
                if (!memberNameAst.Value.Equals(".", StringComparison.OrdinalIgnoreCase) && !memberNameAst.Value.Equals("::", StringComparison.OrdinalIgnoreCase))
                {
                    memberName = memberNameAst.Value + "*";
                }
            }
            else if (!(lastAst is ErrorExpressionAst) && targetExpr == null)
            {
                // I don't think we can complete anything interesting
                return results;
            }

            var commandAst = lastAst.Parent as CommandAst;
            if (commandAst != null)
            {
                int i;
                for (i = commandAst.CommandElements.Count - 1; i >= 0; --i)
                {
                    if (commandAst.CommandElements[i] == lastAst)
                    {
                        break;
                    }
                }
                var nextToLastAst = commandAst.CommandElements[i - 1];
                var nextToLastExtent = nextToLastAst.Extent;
                var lastExtent = lastAst.Extent;
                if (nextToLastExtent.EndLineNumber == lastExtent.StartLineNumber &&
                    nextToLastExtent.EndColumnNumber == lastExtent.StartColumnNumber)
                {
                    targetExpr = nextToLastAst as ExpressionAst;
                }
            }
            else if (lastAst.Parent is MemberExpressionAst)
            {
                // If 'targetExpr' has already been set, we should skip this step. This is for some member completion
                // cases in ISE. In ISE, we may add a new statement in the middle of existing statements as follows:
                //     $xml = New-Object Xml
                //     $xml.
                //     $xml.Save("C:\data.xml")
                // In this example, we add $xml. between two existing statements, and the 'lastAst' in this case is
                // a MemberExpressionAst '$xml.$xml', whose parent is still a MemberExpressionAst '$xml.$xml.Save'. 
                // But here we DO NOT want to re-assign 'targetExpr' to be '$xml.$xml'. 'targetExpr' in this case
                // should be '$xml'.
                if (targetExpr == null)
                {
                    var memberExprAst = (MemberExpressionAst)lastAst.Parent;
                    targetExpr = memberExprAst.Expression;
                }
            }
            else if (lastAst.Parent is BinaryExpressionAst && context.TokenAtCursor.Kind.Equals(TokenKind.Multiply))
            {
                var memberExprAst = ((BinaryExpressionAst)lastAst.Parent).Left as MemberExpressionAst;
                if (memberExprAst != null)
                {
                    targetExpr = memberExprAst.Expression;
                    if (memberExprAst.Member is StringConstantExpressionAst)
                    {
                        memberName = ((StringConstantExpressionAst)memberExprAst.Member).Value + "*";
                    }
                }
            }

            if (targetExpr == null)
            {
                // Not sure what we have, but we're not looking for members.
                return results;
            }

            if (IsSplattedVariable(targetExpr))
            {
                // It's splatted variable, member expansion is not useful
                return results;
            }

            CompleteMemberHelper(@static, memberName, targetExpr, context, results);

            if (results.Count == 0)
            {
                PSTypeName[] inferredTypes = null;

                if (@static)
                {
                    var typeExpr = targetExpr as TypeExpressionAst;
                    if (typeExpr != null)
                    {
                        inferredTypes = new[] { new PSTypeName(typeExpr.TypeName) };
                    }
                }
                else
                {
                    inferredTypes = targetExpr.GetInferredType(context).ToArray();
                }

                if (inferredTypes != null && inferredTypes.Length > 0)
                {
                    // Use inferred types if we have any
                    CompleteMemberByInferredType(context, inferredTypes, results, memberName, filter: null, isStatic: @static);
                }
                else
                {
                    // Handle special DSC collection variables to complete the extension methods 'Where' and 'ForEach'
                    // e.g. Configuration foo { node $AllNodes.<tab> --> $AllNodes.Where(
                    var variableAst = targetExpr as VariableExpressionAst;
                    var memberExprAst = targetExpr as MemberExpressionAst;
                    bool shouldAddExtensionMethods = false;

                    // We complete against extension methods 'Where' and 'ForEach' for the following DSC variables
                    // $SelectedNodes, $AllNodes, $ConfigurationData.AllNodes
                    if (variableAst != null)
                    {
                        // Handle $SelectedNodes and $AllNodes
                        var variablePath = variableAst.VariablePath;
                        if (variablePath.IsVariable && s_dscCollectionVariables.Contains(variablePath.UserPath) && IsInDscContext(variableAst))
                        {
                            shouldAddExtensionMethods = true;
                        }
                    }
                    else if (memberExprAst != null)
                    {
                        // Handle $ConfigurationData.AllNodes
                        var member = memberExprAst.Member as StringConstantExpressionAst;
                        if (IsConfigurationDataVariable(memberExprAst.Expression) && member != null &&
                            string.Equals("AllNodes", member.Value, StringComparison.OrdinalIgnoreCase) &&
                            IsInDscContext(memberExprAst))
                        {
                            shouldAddExtensionMethods = true;
                        }
                    }

                    if (shouldAddExtensionMethods)
                    {
                        CompleteExtensionMethods(memberName, results);
                    }
                }

                if (results.Count == 0)
                {
                    // Handle '$ConfigurationData' specially to complete 'AllNodes' for it
                    if (IsConfigurationDataVariable(targetExpr) && IsInDscContext(targetExpr))
                    {
                        var pattern = WildcardPattern.Get(memberName, WildcardOptions.IgnoreCase);
                        if (pattern.IsMatch("AllNodes"))
                        {
                            results.Add(new CompletionResult("AllNodes", "AllNodes", CompletionResultType.Property, "AllNodes"));
                        }
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Complete members against extension methods 'Where' and 'ForEach'
        /// </summary>
        private static void CompleteExtensionMethods(string memberName, List<CompletionResult> results)
        {
            var pattern = WildcardPattern.Get(memberName, WildcardOptions.IgnoreCase);
            CompleteExtensionMethods(pattern, results);
        }

        /// <summary>
        /// Complete members against extension methods 'Where' and 'ForEach' based on the given pattern
        /// </summary>
        private static void CompleteExtensionMethods(WildcardPattern pattern, List<CompletionResult> results)
        {
            results.AddRange(from member in s_extensionMethods
                             where pattern.IsMatch(member.Item1)
                             select
                                 new CompletionResult(member.Item1 + "(", member.Item1,
                                                      CompletionResultType.Method, member.Item2));
        }

        /// <summary>
        /// Verify if an expression Ast is representing the $ConfigurationData variable
        /// </summary>
        private static bool IsConfigurationDataVariable(ExpressionAst targetExpr)
        {
            var variableExpr = targetExpr as VariableExpressionAst;
            if (variableExpr != null)
            {
                var varPath = variableExpr.VariablePath;
                if (varPath.IsVariable &&
                    varPath.UserPath.Equals("ConfigurationData", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Verify if an expression Ast is within a configuration definition
        /// </summary>
        private static bool IsInDscContext(ExpressionAst expression)
        {
            return Ast.GetAncestorAst<ConfigurationDefinitionAst>(expression) != null;
        }

        private static void CompleteMemberByInferredType(CompletionContext context, IEnumerable<PSTypeName> inferredTypes, List<CompletionResult> results, string memberName, Func<object, bool> filter, bool isStatic)
        {
            bool extensionMethodsAdded = false;
            HashSet<string> typeNameUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            WildcardPattern memberNamePattern = WildcardPattern.Get(memberName, WildcardOptions.IgnoreCase);
            foreach (var psTypeName in inferredTypes)
            {
                if (typeNameUsed.Contains(psTypeName.Name))
                {
                    continue;
                }
                typeNameUsed.Add(psTypeName.Name);
                var members = GetMembersByInferredType(psTypeName, context, isStatic, filter);
                foreach (var member in members)
                {
                    AddInferredMember(member, memberNamePattern, results);
                }

                // Check if we need to complete against the extension methods 'Where' and 'ForEach'
                if (!extensionMethodsAdded && psTypeName.Type != null && IsStaticTypeEnumerable(psTypeName.Type))
                {
                    // Complete extension methods 'Where' and 'ForEach' for Enumerable types
                    extensionMethodsAdded = true;
                    CompleteExtensionMethods(memberNamePattern, results);
                }
            }

            if (results.Count > 0)
            {
                // Sort the results
                AddCommandWithPreferenceSetting(context.Helper.CurrentPowerShell, "Microsoft.PowerShell.Utility\\Sort-Object")
                    .AddParameter("Property", new[] { "ResultType", "ListItemText" })
                    .AddParameter("Unique");
                Exception unused;
                var sortedResults = context.Helper.ExecuteCurrentPowerShell(out unused, results);
                results.Clear();
                results.AddRange(sortedResults.Select(psobj => PSObject.Base(psobj) as CompletionResult));
            }
        }

        private static void AddInferredMember(object member, WildcardPattern memberNamePattern, List<CompletionResult> results)
        {
            string memberName = null;
            bool isMethod = false;
            Func<string> getToolTip = null;
            var propertyInfo = member as PropertyInfo;
            if (propertyInfo != null)
            {
                memberName = propertyInfo.Name;
                getToolTip = () => ToStringCodeMethods.Type(propertyInfo.PropertyType) + " " + memberName
                    + " { " + (propertyInfo.GetGetMethod() != null ? "get; " : "")
                    + (propertyInfo.GetSetMethod() != null ? "set; " : "") + "}";
            }
            var fieldInfo = member as FieldInfo;
            if (fieldInfo != null)
            {
                memberName = fieldInfo.Name;
                getToolTip = () => ToStringCodeMethods.Type(fieldInfo.FieldType) + " " + memberName;
            }

            var methodCacheEntry = member as DotNetAdapter.MethodCacheEntry;
            if (methodCacheEntry != null)
            {
                memberName = methodCacheEntry[0].method.Name;
                isMethod = true;
                getToolTip = () => string.Join("\n", methodCacheEntry.methodInformationStructures.Select(m => m.methodDefinition));
            }

            var psMemberInfo = member as PSMemberInfo;
            if (psMemberInfo != null)
            {
                memberName = psMemberInfo.Name;
                isMethod = member is PSMethodInfo;
                getToolTip = psMemberInfo.ToString;
            }

            var cimProperty = member as CimPropertyDeclaration;
            if (cimProperty != null)
            {
                memberName = cimProperty.Name;
                isMethod = false;
                getToolTip = () => GetCimPropertyToString(cimProperty);
            }

            var memberAst = member as MemberAst;
            if (memberAst != null)
            {
                memberName = memberAst is CompilerGeneratedMemberFunctionAst ? "new" : memberAst.Name;
                isMethod = memberAst is FunctionMemberAst || memberAst is CompilerGeneratedMemberFunctionAst;
                getToolTip = memberAst.GetTooltip;
            }

            if (memberName == null || !memberNamePattern.IsMatch(memberName))
            {
                return;
            }

            var completionResultType = isMethod ? CompletionResultType.Method : CompletionResultType.Property;
            var completionText = isMethod ? memberName + "(" : memberName;

            results.Add(new CompletionResult(completionText, memberName, completionResultType, getToolTip()));
        }

        private static string GetCimPropertyToString(CimPropertyDeclaration cimProperty)
        {
            string type;
            switch (cimProperty.CimType)
            {
                case Microsoft.Management.Infrastructure.CimType.DateTime:
                case Microsoft.Management.Infrastructure.CimType.Instance:
                case Microsoft.Management.Infrastructure.CimType.Reference:
                case Microsoft.Management.Infrastructure.CimType.DateTimeArray:
                case Microsoft.Management.Infrastructure.CimType.InstanceArray:
                case Microsoft.Management.Infrastructure.CimType.ReferenceArray:
                    type = "CimInstance#" + cimProperty.CimType.ToString();
                    break;

                default:
                    type = ToStringCodeMethods.Type(CimConverter.GetDotNetType(cimProperty.CimType));
                    break;
            }

            bool isReadOnly = (CimFlags.ReadOnly == (cimProperty.Flags & CimFlags.ReadOnly));
            return type + " " + cimProperty.Name + " { get; " + (isReadOnly ? "}" : "set; }");
        }

        private static bool IsWriteablePropertyMember(object member)
        {
            var propertyInfo = member as PropertyInfo;
            if (propertyInfo != null)
            {
                return propertyInfo.CanWrite;
            }

            var psPropertyInfo = member as PSPropertyInfo;
            if (psPropertyInfo != null)
            {
                return psPropertyInfo.IsSettable;
            }

            return false;
        }

        private static bool IsPropertyMember(object member)
        {
            return member is PropertyInfo
                   || member is FieldInfo
                   || member is PSPropertyInfo
                   || member is CimPropertyDeclaration
                   || member is PropertyMemberAst;
        }

        private static bool IsMemberHidden(object member)
        {
            var psMemberInfo = member as PSMemberInfo;
            if (psMemberInfo != null)
                return psMemberInfo.IsHidden;

            var memberInfo = member as MemberInfo;
            if (memberInfo != null)
                return memberInfo.GetCustomAttributes(typeof(HiddenAttribute), false).Any();

            var propertyMemberAst = member as PropertyMemberAst;
            if (propertyMemberAst != null)
                return propertyMemberAst.IsHidden;

            var functionMemberAst = member as FunctionMemberAst;
            if (functionMemberAst != null)
                return functionMemberAst.IsHidden;

            return false;
        }

        private static bool IsConstructor(object member)
        {
            var psMethod = member as PSMethod;
            if (psMethod != null)
            {
                var methodCacheEntry = psMethod.adapterData as DotNetAdapter.MethodCacheEntry;
                if (methodCacheEntry != null)
                {
                    return methodCacheEntry.methodInformationStructures[0].method.IsConstructor;
                }
            }

            return false;
        }

        internal static IEnumerable<object> GetMembersByInferredType(PSTypeName typename, CompletionContext context, bool @static, Func<object, bool> filter)
        {
            List<object> results = new List<object>();

            Func<object, bool> filterToCall = filter;
            if (typename.Type != null)
            {
                if (context.CurrentTypeDefinitionAst == null || context.CurrentTypeDefinitionAst.Type != typename.Type)
                {
                    if (filterToCall == null)
                        filterToCall = o => !IsMemberHidden(o);
                    else
                        filterToCall = o => !IsMemberHidden(o) && filter(o);
                }
                IEnumerable<Type> elementTypes;
                if (typename.Type.IsArray)
                {
                    elementTypes = new[] { typename.Type.GetElementType() };
                }
                else
                {
                    elementTypes = typename.Type.GetInterfaces().Where(
                        t => t.GetTypeInfo().IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>));
                }
                foreach (var type in elementTypes.Prepend(typename.Type))
                {
                    // Look in the type table first.
                    if (!@static)
                    {
                        var consolidatedString = DotNetAdapter.GetInternedTypeNameHierarchy(type);
                        results.AddRange(context.ExecutionContext.TypeTable.GetMembers<PSMemberInfo>(consolidatedString));
                    }

                    var members = @static
                        ? PSObject.dotNetStaticAdapter.BaseGetMembers<PSMemberInfo>(type)
                        : PSObject.dotNetInstanceAdapter.GetPropertiesAndMethods(type, false);
                    results.AddRange(filterToCall != null ? members.Where(filterToCall) : members);
                }
            }
            else if (typename.TypeDefinitionAst != null)
            {
                if (context.CurrentTypeDefinitionAst != typename.TypeDefinitionAst)
                {
                    if (filterToCall == null)
                        filterToCall = o => !IsMemberHidden(o);
                    else
                        filterToCall = o => !IsMemberHidden(o) && filter(o);
                }

                bool foundConstructor = false;
                foreach (var member in typename.TypeDefinitionAst.Members)
                {
                    bool add = false;
                    var propertyMember = member as PropertyMemberAst;
                    if (propertyMember != null)
                    {
                        if (propertyMember.IsStatic == @static)
                        {
                            add = true;
                        }
                    }
                    else
                    {
                        var functionMember = (FunctionMemberAst)member;
                        if (functionMember.IsStatic == @static)
                        {
                            add = true;
                        }
                        foundConstructor |= functionMember.IsConstructor;
                    }

                    if (filterToCall != null && add)
                    {
                        add = filterToCall(member);
                    }

                    if (add)
                    {
                        results.Add(member);
                    }
                }

                //iterate through bases/interfaces
                foreach (var baseType in typename.TypeDefinitionAst.BaseTypes)
                {
                    TypeName baseTypeName = baseType.TypeName as TypeName;
                    if (baseTypeName != null)
                    {
                        TypeDefinitionAst baseTypeDefinitionAst = baseTypeName._typeDefinitionAst;
                        results.AddRange(GetMembersByInferredType(new PSTypeName(baseTypeDefinitionAst), context, @static, filterToCall));
                    }
                }

                // Add stuff from our base class System.Object.  
                if (@static)
                {
                    // Don't add base class constructors
                    if (filter == null)
                    {
                        filterToCall = o => !IsConstructor(o);
                    }
                    else
                    {
                        filterToCall = o => !IsConstructor(o) && filter(o);
                    }

                    if (!foundConstructor)
                    {
                        results.Add(
                            new CompilerGeneratedMemberFunctionAst(PositionUtilities.EmptyExtent, typename.TypeDefinitionAst,
                                SpecialMemberFunctionType.DefaultConstructor));
                    }
                }
                else
                {
                    // Reset the filter because the recursive call will add IsHidden back if necessary.
                    filterToCall = filter;
                }
                results.AddRange(GetMembersByInferredType(new PSTypeName(typeof(object)), context, @static, filterToCall));
            }
            else
            {
                // Look in the type table first.
                if (!@static)
                {
                    var consolidatedString = new ConsolidatedString(new string[] { typename.Name });
                    results.AddRange(context.ExecutionContext.TypeTable.GetMembers<PSMemberInfo>(consolidatedString));
                }

                string cimNamespace;
                string className;
                if (NativeCompletionCimCommands_ParseTypeName(typename, out cimNamespace, out className))
                {
                    AddCommandWithPreferenceSetting(context.Helper.CurrentPowerShell, "CimCmdlets\\Get-CimClass")
                        .AddParameter("Namespace", cimNamespace)
                        .AddParameter("Class", className);
                    Exception unused;
                    var classes = context.Helper.ExecuteCurrentPowerShell(out unused);
                    foreach (var @class in classes.Select(PSObject.Base).OfType<CimClass>())
                    {
                        results.AddRange(filterToCall != null ? @class.CimClassProperties.Where(filterToCall) : @class.CimClassProperties);
                    }
                }
            }

            return results;
        }

        #endregion Members

        #region Types

        private abstract class TypeCompletionBase
        {
            internal abstract CompletionResult GetCompletionResult(string keyMatched, string prefix, string suffix);
            internal abstract CompletionResult GetCompletionResult(string keyMatched, string prefix, string suffix, string namespaceToRemove);

            internal static string RemoveBackTick(string typeName)
            {
                var backtick = typeName.LastIndexOf('`');
                return backtick == -1 ? typeName : typeName.Substring(0, backtick);
            }
        }

        /// <summary>
        /// In OneCore PS, there is no way to retrieve all loaded assemblies. But we have the type catalog dictionary
        /// which contains the full type names of all available CoreCLR .NET types. We can extract the necessary info
        /// from the full type names to make type name auto-completion work.
        /// This type represents a non-generic type for type name completion. It only contains information that can be
        /// inferred from the full type name.
        /// </summary>
        private class TypeCompletionInStringFormat : TypeCompletionBase
        {
            /// <summary>
            /// Get the full type name of the type represented by this instance.
            /// </summary>
            internal string FullTypeName;

            /// <summary>
            /// Get the short type name of the type represented by this instance.
            /// </summary>
            internal string ShortTypeName
            {
                get
                {
                    if (_shortTypeName == null)
                    {
                        int lastDotIndex = FullTypeName.LastIndexOf('.');
                        int lastPlusIndex = FullTypeName.LastIndexOf('+');
                        _shortTypeName = lastPlusIndex != -1
                                           ? FullTypeName.Substring(lastPlusIndex + 1)
                                           : FullTypeName.Substring(lastDotIndex + 1);
                    }
                    return _shortTypeName;
                }
            }
            private string _shortTypeName;

            /// <summary>
            /// Get the namespace of the type represented by this instance.
            /// </summary>
            internal string Namespace
            {
                get
                {
                    if (_namespace == null)
                    {
                        int lastDotIndex = FullTypeName.LastIndexOf('.');
                        _namespace = FullTypeName.Substring(0, lastDotIndex);
                    }
                    return _namespace;
                }
            }
            private string _namespace;

            /// <summary>
            /// Construct the CompletionResult based on the information of this instance
            /// </summary>
            internal override CompletionResult GetCompletionResult(string keyMatched, string prefix, string suffix)
            {
                return GetCompletionResult(keyMatched, prefix, suffix, null);
            }

            /// <summary>
            /// Construct the CompletionResult based on the information of this instance
            /// </summary>
            internal override CompletionResult GetCompletionResult(string keyMatched, string prefix, string suffix, string namespaceToRemove)
            {
                string completion = string.IsNullOrEmpty(namespaceToRemove)
                                        ? FullTypeName
                                        : FullTypeName.Substring(namespaceToRemove.Length + 1);

                string listItem = ShortTypeName;
                string tooltip = FullTypeName;

                return new CompletionResult(prefix + completion + suffix, listItem, CompletionResultType.Type, tooltip);
            }
        }

        /// <summary>
        /// In OneCore PS, there is no way to retrieve all loaded assemblies. But we have the type catalog dictionary
        /// which contains the full type names of all available CoreCLR .NET types. We can extract the necessary info
        /// from the full type names to make type name auto-completion work.
        /// This type represents a generic type for type name completion. It only contains information that can be 
        /// inferred from the full type name.
        /// </summary>
        private class GenericTypeCompletionInStringFormat : TypeCompletionInStringFormat
        {
            /// <summary>
            /// Get the number of generic type arguments required by the type represented by this instance.
            /// </summary>
            private int GenericArgumentCount
            {
                get
                {
                    if (_genericArgumentCount == 0)
                    {
                        var backtick = FullTypeName.LastIndexOf('`');
                        var argCount = FullTypeName.Substring(backtick + 1);
                        _genericArgumentCount = LanguagePrimitives.ConvertTo<int>(argCount);
                    }
                    return _genericArgumentCount;
                }
            }
            private int _genericArgumentCount = 0;

            /// <summary>
            /// Construct the CompletionResult based on the information of this instance
            /// </summary>
            internal override CompletionResult GetCompletionResult(string keyMatched, string prefix, string suffix)
            {
                return GetCompletionResult(keyMatched, prefix, suffix, null);
            }

            /// <summary>
            /// Construct the CompletionResult based on the information of this instance
            /// </summary>
            internal override CompletionResult GetCompletionResult(string keyMatched, string prefix, string suffix, string namespaceToRemove)
            {
                string fullNameWithoutBacktip = RemoveBackTick(FullTypeName);
                string completion = string.IsNullOrEmpty(namespaceToRemove)
                                        ? fullNameWithoutBacktip
                                        : fullNameWithoutBacktip.Substring(namespaceToRemove.Length + 1);

                string typeName = RemoveBackTick(ShortTypeName);
                var listItem = typeName + "<>";

                var tooltip = new StringBuilder();
                tooltip.Append(fullNameWithoutBacktip);
                tooltip.Append('[');

                for (int i = 0; i < GenericArgumentCount; i++)
                {
                    if (i != 0) tooltip.Append(", ");
                    tooltip.Append(GenericArgumentCount == 1
                                       ? "T"
                                       : string.Format(CultureInfo.InvariantCulture, "T{0}", i + 1));
                }
                tooltip.Append(']');

                return new CompletionResult(prefix + completion + suffix, listItem, CompletionResultType.Type, tooltip.ToString());
            }
        }

        /// <summary>
        /// This type represents a non-generic type for type name completion. It contains the actual type instance.
        /// </summary>
        private class TypeCompletion : TypeCompletionBase
        {
            internal Type Type;

            protected string GetTooltipPrefix()
            {
                TypeInfo typeInfo = Type.GetTypeInfo();

                if (typeof(Delegate).IsAssignableFrom(Type))
                    return "Delegate ";
                if (typeInfo.IsInterface)
                    return "Interface ";
                if (typeInfo.IsClass)
                    return "Class ";
                if (typeInfo.IsEnum)
                    return "Enum ";
                if (typeof(ValueType).IsAssignableFrom(Type))
                    return "Struct ";

                return ""; // what other interesting types are there?
            }

            internal override CompletionResult GetCompletionResult(string keyMatched, string prefix, string suffix)
            {
                return GetCompletionResult(keyMatched, prefix, suffix, null);
            }

            internal override CompletionResult GetCompletionResult(string keyMatched, string prefix, string suffix, string namespaceToRemove)
            {
                string completion = ToStringCodeMethods.Type(Type);

                // If the completion included a namespace and ToStringCodeMethods.Type found
                // an accelerator, then just use the type's FullName instead because the user
                // probably didn't want the accelerator.
                if (keyMatched.IndexOf('.') != -1 && completion.IndexOf('.') == -1)
                {
                    completion = Type.FullName;
                }

                if (!string.IsNullOrEmpty(namespaceToRemove) && completion.Equals(Type.FullName, StringComparison.OrdinalIgnoreCase))
                {
                    // Remove the namespace only if the completion text contains namespace
                    completion = completion.Substring(namespaceToRemove.Length + 1);
                }

                string listItem = Type.Name;
                string tooltip = GetTooltipPrefix() + Type.FullName;

                return new CompletionResult(prefix + completion + suffix, listItem, CompletionResultType.Type, tooltip);
            }
        }

        /// <summary>
        /// This type represents a generic type for type name completion. It contains the actual type instance.
        /// </summary>
        private class GenericTypeCompletion : TypeCompletion
        {
            internal override CompletionResult GetCompletionResult(string keyMatched, string prefix, string suffix)
            {
                return GetCompletionResult(keyMatched, prefix, suffix, null);
            }

            internal override CompletionResult GetCompletionResult(string keyMatched, string prefix, string suffix, string namespaceToRemove)
            {
                string fullNameWithoutBacktip = RemoveBackTick(Type.FullName);
                string completion = string.IsNullOrEmpty(namespaceToRemove)
                                        ? fullNameWithoutBacktip
                                        : fullNameWithoutBacktip.Substring(namespaceToRemove.Length + 1);

                string typeName = RemoveBackTick(Type.Name);
                var listItem = typeName + "<>";

                var tooltip = new StringBuilder();
                tooltip.Append(GetTooltipPrefix());
                tooltip.Append(fullNameWithoutBacktip);
                tooltip.Append('[');
                var genericParameters = Type.GetGenericArguments();
                for (int i = 0; i < genericParameters.Length; i++)
                {
                    if (i != 0) tooltip.Append(", ");
                    tooltip.Append(genericParameters[i].Name);
                }
                tooltip.Append(']');

                return new CompletionResult(prefix + completion + suffix, listItem, CompletionResultType.Type, tooltip.ToString());
            }
        }

        /// <summary>
        /// This type represents a namespace for namespace completion.
        /// </summary>
        private class NamespaceCompletion : TypeCompletionBase
        {
            internal string Namespace;

            internal override CompletionResult GetCompletionResult(string keyMatched, string prefix, string suffix)
            {
                var listItemText = Namespace;
                var dotIndex = listItemText.LastIndexOf('.');
                if (dotIndex != -1)
                {
                    listItemText = listItemText.Substring(dotIndex + 1);
                }
                return new CompletionResult(prefix + Namespace + suffix, listItemText, CompletionResultType.Namespace, "Namespace " + Namespace);
            }

            internal override CompletionResult GetCompletionResult(string keyMatched, string prefix, string suffix, string namespaceToRemove)
            {
                return GetCompletionResult(keyMatched, prefix, suffix);
            }
        }

        private class TypeCompletionMapping
        {
            // The Key is the string we'll be searching on.  It could complete to various things.
            internal string Key;
            internal List<TypeCompletionBase> Completions = new List<TypeCompletionBase>();
        }

        private static TypeCompletionMapping[][] s_typeCache;
        private static TypeCompletionMapping[][] InitializeTypeCache()
        {
            #region Process_TypeAccelerators

            var entries = new Dictionary<string, TypeCompletionMapping>(StringComparer.OrdinalIgnoreCase);
            foreach (var type in TypeAccelerators.Get)
            {
                TypeCompletionMapping entry;
                var typeCompletionInstance = new TypeCompletion { Type = type.Value };

                if (entries.TryGetValue(type.Key, out entry))
                {
                    // Check if this accelerator type is already included in the mapping entry referenced by the same key.
                    Type acceleratorType = type.Value;
                    bool typeAlreadyIncluded = entry.Completions.Any(
                        item =>
                            {
                                var typeCompletion = item as TypeCompletion;
                                return typeCompletion != null && typeCompletion.Type == acceleratorType;
                            });

                    // If it's already included, skip it.
                    // This may happen when an accelerator name is the same as the short name of the type it represents,
                    // and aslo that type has more than one accelerator names. For example: 
                    //    "float"  -> System.Single
                    //    "single" -> System.Single
                    if (typeAlreadyIncluded) { continue; }

                    // If this accelerator type is not included in the mapping entry, add it in.
                    // This may happen when an accelerator name happens to be the short name of a different type (rare case).
                    entry.Completions.Add(typeCompletionInstance);
                }
                else
                {
                    entries.Add(type.Key, new TypeCompletionMapping { Key = type.Key, Completions = { typeCompletionInstance } });
                }

                // If the full type name has already been included, then we know for sure that the short type name has also been included.
                string fullTypeName = type.Value.FullName;
                if (entries.ContainsKey(fullTypeName)) { continue; }

                // Otherwise, add the mapping from full type name to the type
                entries.Add(fullTypeName, new TypeCompletionMapping { Key = fullTypeName, Completions = { typeCompletionInstance } });

                // If the short type name is the same as the accelerator name, then skip it to avoid duplication.
                string shortTypeName = type.Value.Name;
                if (type.Key.Equals(shortTypeName, StringComparison.OrdinalIgnoreCase)) { continue; }

                // Otherwise, add a new mapping entry, or put the TypeCompletion instance in the existing mapping entry.
                // For example, this may happen if both System.TimeoutException and System.ServiceProcess.TimeoutException
                // are in the TypeAccelerator cache.
                if (!entries.TryGetValue(shortTypeName, out entry))
                {
                    entry = new TypeCompletionMapping { Key = shortTypeName };
                    entries.Add(shortTypeName, entry);
                }
                entry.Completions.Add(typeCompletionInstance);
            }

            #endregion Process_TypeAccelerators

            #region Process_LoadedAssemblies

            var assembliesExludingPSGenerated = ClrFacade.GetAssemblies();
            var allPublicTypes = assembliesExludingPSGenerated.SelectMany(assembly => assembly.GetTypes().Where(TypeResolver.IsPublic));

            foreach (var type in allPublicTypes)
            {
                HandleNamespace(entries, type.Namespace);
                HandleType(entries, type.FullName, type.Name, type);
            }

            #endregion Process_LoadedAssemblies

            #region Process_CoreCLR_TypeCatalog
#if CORECLR
            // In CoreCLR, we have namespace-qualified type names of all available .NET types stored in TypeCatalog of the AssemblyLoadContext.
            // Populate the type completion cache using the namespace-qualified type names.
            foreach (string fullTypeName in ClrFacade.GetAvailableCoreClrDotNetTypes())
            {
                var typeCompInString = new TypeCompletionInStringFormat { FullTypeName = fullTypeName };
                HandleNamespace(entries, typeCompInString.Namespace);
                HandleType(entries, fullTypeName, typeCompInString.ShortTypeName, null);
            }
#endif
            #endregion Process_CoreCLR_TypeCatalog

            var grouping = entries.Values.GroupBy(t => t.Key.Count(c => c == '.')).OrderBy(g => g.Key).ToArray();
            var localTypeCache = new TypeCompletionMapping[grouping.Last().Key + 1][];
            foreach (var group in grouping)
            {
                localTypeCache[group.Key] = group.ToArray();
            }

            Interlocked.Exchange(ref s_typeCache, localTypeCache);
            return localTypeCache;
        }

        /// <summary>
        /// Handle namespace when initializing the type cache
        /// </summary>
        /// <param name="entryCache">The TypeCompletionMapping dictionary</param>
        /// <param name="namespace">The namespace</param>
        private static void HandleNamespace(Dictionary<string, TypeCompletionMapping> entryCache, string @namespace)
        {
            if (string.IsNullOrEmpty(@namespace))
            {
                return;
            }

            int dotIndex = 0;
            while (dotIndex != -1)
            {
                dotIndex = @namespace.IndexOf('.', dotIndex + 1);
                string subNamespace = dotIndex != -1
                                        ? @namespace.Substring(0, dotIndex)
                                        : @namespace;

                TypeCompletionMapping entry;
                if (!entryCache.TryGetValue(subNamespace, out entry))
                {
                    entry = new TypeCompletionMapping
                    {
                        Key = subNamespace,
                        Completions = { new NamespaceCompletion { Namespace = subNamespace } }
                    };
                    entryCache.Add(subNamespace, entry);
                }
                else if (!entry.Completions.OfType<NamespaceCompletion>().Any())
                {
                    entry.Completions.Add(new NamespaceCompletion { Namespace = subNamespace });
                }
            }
        }

        /// <summary>
        /// Handle a type when initializing the type cache
        /// </summary>
        /// <param name="entryCache">The TypeCompletionMapping dictionary</param>
        /// <param name="fullTypeName">The full type name</param>
        /// <param name="shortTypeName">The short type name</param>
        /// <param name="actualType">The actual type object. It may be null if we are handling type information from the CoreCLR TypeCatalog</param>
        private static void HandleType(Dictionary<string, TypeCompletionMapping> entryCache, string fullTypeName, string shortTypeName, Type actualType)
        {
            if (string.IsNullOrEmpty(fullTypeName)) { return; }

            TypeCompletionBase typeCompletionBase = null;
            var backtick = fullTypeName.LastIndexOf('`');
            var plusChar = fullTypeName.LastIndexOf('+');

            bool isGenericTypeDefinition = backtick != -1;
            bool isNested = plusChar != -1;

            if (isGenericTypeDefinition)
            {
                // Nested generic types aren't useful for completion.
                if (isNested) { return; }

                typeCompletionBase = actualType != null
                                         ? (TypeCompletionBase)new GenericTypeCompletion { Type = actualType }
                                         : new GenericTypeCompletionInStringFormat { FullTypeName = fullTypeName };

                // Remove the backtick, we only want 1 generic in our results for types like Func or Action.
                fullTypeName = fullTypeName.Substring(0, backtick);
                shortTypeName = shortTypeName.Substring(0, shortTypeName.LastIndexOf('`'));
            }
            else
            {
                typeCompletionBase = actualType != null
                                         ? (TypeCompletionBase)new TypeCompletion { Type = actualType }
                                         : new TypeCompletionInStringFormat { FullTypeName = fullTypeName };
            }

            // If the full type name has already been included, then we know for sure that the short type
            // name and the accelerator type names (if there are any) have also been included.
            TypeCompletionMapping entry;
            if (!entryCache.TryGetValue(fullTypeName, out entry))
            {
                entry = new TypeCompletionMapping
                {
                    Key = fullTypeName,
                    Completions = { typeCompletionBase }
                };
                entryCache.Add(fullTypeName, entry);

                // Add a new mapping entry, or put the TypeCompletion instance in the existing mapping entry of the shortTypeName.
                // For example, this may happen to System.ServiceProcess.TimeoutException when System.TimeoutException is already in the cache.
                if (!entryCache.TryGetValue(shortTypeName, out entry))
                {
                    entry = new TypeCompletionMapping { Key = shortTypeName };
                    entryCache.Add(shortTypeName, entry);
                }
                entry.Completions.Add(typeCompletionBase);
            }
        }

        internal static List<CompletionResult> CompleteNamespace(CompletionContext context, string prefix = "", string suffix = "")
        {
            var localTypeCache = s_typeCache ?? InitializeTypeCache();
            var results = new List<CompletionResult>();
            var wordToComplete = context.WordToComplete;
            var dots = wordToComplete.Count(c => c == '.');
            if (dots >= localTypeCache.Length || localTypeCache[dots] == null)
            {
                return results;
            }

            var pattern = WildcardPattern.Get(wordToComplete + "*", WildcardOptions.IgnoreCase);

            foreach (var entry in localTypeCache[dots].Where(e => e.Completions.OfType<NamespaceCompletion>().Any() && pattern.IsMatch(e.Key)))
            {
                foreach (var completion in entry.Completions)
                {
                    results.Add(completion.GetCompletionResult(entry.Key, prefix, suffix));
                }
            }
            results.Sort((c1, c2) => string.Compare(c1.ListItemText, c2.ListItemText, StringComparison.OrdinalIgnoreCase));
            return results;
        }

        /// <summary>
        /// Complete a typename
        /// </summary>
        /// <param name="typeName"></param>
        /// <returns></returns>
        public static IEnumerable<CompletionResult> CompleteType(string typeName)
        {
            // When completing types, we don't care about the runspace, types are visible across the appdomain
            var powershell = (Runspace.DefaultRunspace == null)
                                 ? PowerShell.Create()
                                 : PowerShell.Create(RunspaceMode.CurrentRunspace);

            var helper = new CompletionExecutionHelper(powershell);
            return CompleteType(new CompletionContext { WordToComplete = typeName, Helper = helper });
        }

        internal static List<CompletionResult> CompleteType(CompletionContext context, string prefix = "", string suffix = "")
        {
            var localTypeCache = s_typeCache ?? InitializeTypeCache();

            var results = new List<CompletionResult>();
            var completionTextSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var wordToComplete = context.WordToComplete;
            var dots = wordToComplete.Count(c => c == '.');
            if (dots >= localTypeCache.Length || localTypeCache[dots] == null)
            {
                return results;
            }

            var pattern = WildcardPattern.Get(wordToComplete + "*", WildcardOptions.IgnoreCase);

            foreach (var entry in localTypeCache[dots].Where(e => pattern.IsMatch(e.Key)))
            {
                foreach (var completion in entry.Completions)
                {
                    string namespaceToRemove = GetNamespaceToRemove(context, completion);
                    var completionResult = completion.GetCompletionResult(entry.Key, prefix, suffix, namespaceToRemove);

                    // We might get the same completion result twice. For example, the type cache has:
                    //    DscResource->System.Management.Automation.DscResourceAttribute (from accelerator)
                    //    DscResourceAttribute->System.Management.Automation.DscResourceAttribute (from short type name)
                    // input '[DSCRes' can match both of them, but they actually resolves to the same completion text 'DscResource'.
                    if (!completionTextSet.Contains(completionResult.CompletionText))
                    {
                        results.Add(completionResult);
                        completionTextSet.Add(completionResult.CompletionText);
                    }
                }
            }

            //this is a temparary fix. Only the type defined in the same script get complete. Need to use using Module when that is available. 
            var scriptBlockAst = (ScriptBlockAst)context.RelatedAsts[0];
            var typeAsts = scriptBlockAst.FindAll(ast => ast is TypeDefinitionAst, false).Cast<TypeDefinitionAst>();
            foreach (var typeAst in typeAsts.Where(ast => pattern.IsMatch(ast.Name)))
            {
                string toolTipPrefix = String.Empty;
                if (typeAst.IsInterface)
                    toolTipPrefix = "Interface ";
                else if (typeAst.IsClass)
                    toolTipPrefix = "Class ";
                else if (typeAst.IsEnum)
                    toolTipPrefix = "Enum ";

                results.Add(new CompletionResult(prefix + typeAst.Name + suffix, typeAst.Name, CompletionResultType.Type, toolTipPrefix + typeAst.Name));
            }

            results.Sort((c1, c2) => string.Compare(c1.ListItemText, c2.ListItemText, StringComparison.OrdinalIgnoreCase));
            return results;
        }

        private static string GetNamespaceToRemove(CompletionContext context, TypeCompletionBase completion)
        {
            if (completion is NamespaceCompletion) { return null; }

            var typeCompletion = completion as TypeCompletion;
            string typeNameSpace = typeCompletion != null
                                       ? typeCompletion.Type.Namespace
                                       : ((TypeCompletionInStringFormat)completion).Namespace;

            var scriptBlockAst = (ScriptBlockAst)context.RelatedAsts[0];
            var matchingNsStates = scriptBlockAst.UsingStatements.Where(s =>
                 s.UsingStatementKind == UsingStatementKind.Namespace
                 && typeNameSpace != null
                 && typeNameSpace.StartsWith(s.Name.Value, StringComparison.OrdinalIgnoreCase));

            string ns = String.Empty;
            foreach (var nsState in matchingNsStates)
            {
                if (nsState.Name.Extent.Text.Length > ns.Length)
                {
                    ns = nsState.Name.Extent.Text;
                }
            }

            return ns;
        }

        #endregion Types

        #region Help Topics

        internal static List<CompletionResult> CompleteHelpTopics(CompletionContext context)
        {
            var results = new List<CompletionResult>();
            var dirPath = Utils.GetApplicationBase(Utils.DefaultPowerShellShellID) + "\\" + CultureInfo.CurrentCulture.Name;
            var wordToComplete = context.WordToComplete + "*";
            var topicPattern = WildcardPattern.Get("about_*.help.txt", WildcardOptions.IgnoreCase);
            string[] files = null;

            try
            {
                files = Directory.GetFiles(dirPath, wordToComplete);
            }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
            }

            if (files != null)
            {
                foreach (string file in files)
                {
                    if (file == null)
                    {
                        continue;
                    }

                    try
                    {
                        var fileName = Path.GetFileName(file);
                        if (fileName == null || !topicPattern.IsMatch(fileName))
                            continue;

                        // All topic files are ending with ".help.txt"
                        var completionText = fileName.Substring(0, fileName.Length - 9);
                        results.Add(new CompletionResult(completionText));
                    }
                    catch (Exception e)
                    {
                        CommandProcessorBase.CheckForSevereException(e);
                        continue;
                    }
                }
            }

            return results;
        }

        #endregion Help Topics

        #region Statement Parameters

        internal static List<CompletionResult> CompleteStatementFlags(TokenKind kind, string wordToComplete)
        {
            switch (kind)
            {
                case TokenKind.Switch:

                    Diagnostics.Assert(!String.IsNullOrEmpty(wordToComplete) && wordToComplete[0].IsDash(), "the word to complete should start with '-'");
                    wordToComplete = wordToComplete.Substring(1);
                    bool withColon = wordToComplete.EndsWith(":", StringComparison.Ordinal);
                    wordToComplete = withColon ? wordToComplete.Remove(wordToComplete.Length - 1) : wordToComplete;

                    string enumString = LanguagePrimitives.EnumSingleTypeConverter.EnumValues(typeof(SwitchFlags));
                    string separator = CultureInfo.CurrentUICulture.TextInfo.ListSeparator;
                    string[] enumArray = enumString.Split(new string[] { separator }, StringSplitOptions.RemoveEmptyEntries);

                    var pattern = WildcardPattern.Get(wordToComplete + "*", WildcardOptions.IgnoreCase);
                    var enumList = new List<string>();
                    var result = new List<CompletionResult>();
                    CompletionResult fullMatch = null;

                    foreach (string value in enumArray)
                    {
                        if (value.Equals(SwitchFlags.None.ToString(), StringComparison.OrdinalIgnoreCase)) { continue; }

                        if (wordToComplete.Equals(value, StringComparison.OrdinalIgnoreCase))
                        {
                            string completionText = withColon ? "-" + value + ":" : "-" + value;
                            fullMatch = new CompletionResult(completionText, value, CompletionResultType.ParameterName, value);
                            continue;
                        }

                        if (pattern.IsMatch(value))
                        {
                            enumList.Add(value);
                        }
                    }

                    if (fullMatch != null)
                    {
                        result.Add(fullMatch);
                    }

                    enumList.Sort();
                    result.AddRange(from entry in enumList
                                    let completionText = withColon ? "-" + entry + ":" : "-" + entry
                                    select new CompletionResult(completionText, entry, CompletionResultType.ParameterName, entry));

                    return result;

                default:
                    break;
            }

            return null;
        }

        #endregion Statement Parameters

        #region Hashtable Keys

        /// <summary>
        /// Generate auto complete results for hashtable key within a Dynamickeyword.
        /// Results are generated based on properties of a DynamicKeyword matches given identifier.
        /// For example, following "D" matches "DestinationPath"
        /// 
        ///     Configuration
        ///     {
        ///         File
        ///         {
        ///             D^
        ///         }
        ///     }
        /// 
        /// </summary>
        /// <param name="completionContext"></param>
        /// <param name="ast"></param>
        /// <param name="hashtableAst"></param>
        /// <returns></returns>
        internal static List<CompletionResult> CompleteHashtableKeyForDynamicKeyword(
            CompletionContext completionContext,
            DynamicKeywordStatementAst ast,
            HashtableAst hashtableAst)
        {
            Diagnostics.Assert(ast.Keyword != null, "DynamicKeywordStatementAst.Keyword can never be null");
            List<CompletionResult> results = null;
            var dynamicKeywordProperies = ast.Keyword.Properties;
            var memberPattern = completionContext.WordToComplete + "*";

            //
            // Capture all existing properties in hashtable
            //
            var propertiesName = new List<string>();
            int cursorOffset = completionContext.CursorPosition.Offset;
            foreach (var keyValueTuple in hashtableAst.KeyValuePairs)
            {
                var propName = keyValueTuple.Item1 as StringConstantExpressionAst;
                // Exclude the property name at cursor
                if (propName != null && propName.Extent.EndOffset != cursorOffset)
                {
                    propertiesName.Add(propName.Value);
                }
            }

            if (dynamicKeywordProperies.Count > 0)
            {
                // Excludes existing properties in the hashtable statement
                var tempProperties = dynamicKeywordProperies.Where(p => !propertiesName.Contains(p.Key, StringComparer.OrdinalIgnoreCase));
                if (tempProperties != null && tempProperties.Any())
                {
                    results = new List<CompletionResult>();
                    // Filter by name
                    var wildcardPattern = WildcardPattern.Get(memberPattern, WildcardOptions.IgnoreCase);
                    var matchedResults = tempProperties.Where(p => wildcardPattern.IsMatch(p.Key));
                    if (matchedResults == null || !matchedResults.Any())
                    {
                        // Fallback to all non-exist properties in the hashtable statement
                        matchedResults = tempProperties;
                    }

                    foreach (var p in matchedResults)
                    {
                        string psTypeName = LanguagePrimitives.ConvertTypeNameToPSTypeName(p.Value.TypeConstraint);
                        if (psTypeName == "[]" || string.IsNullOrEmpty(psTypeName))
                        {
                            psTypeName = "[" + p.Value.TypeConstraint + "]";
                        }

                        if (string.Equals(psTypeName, "[MSFT_Credential]", StringComparison.OrdinalIgnoreCase))
                        {
                            psTypeName = "[pscredential]";
                        }

                        results.Add(new CompletionResult(
                            p.Key + " = ",
                            p.Key,
                            CompletionResultType.Property,
                            psTypeName));
                    }
                }
            }
            return results;
        }

        internal static List<CompletionResult> CompleteHashtableKey(CompletionContext completionContext, HashtableAst hashtableAst)
        {
            var typeAst = hashtableAst.Parent as ConvertExpressionAst;
            if (typeAst != null)
            {
                var result = new List<CompletionResult>();
                CompleteMemberByInferredType(
                    completionContext, typeAst.GetInferredType(completionContext),
                    result, completionContext.WordToComplete + "*", IsWriteablePropertyMember, isStatic: false);
                return result;
            }

            // hashtable arguments sometimes have expected keys.  Examples:
            //   new-object System.Drawing.Point -prop @{ X=1; Y=1 }
            //   dir | sort-object -prop @{Expression=...   ; Ascending=... }
            // format-table -Property
            //     Expression
            //     FormatString
            //     Label
            //     Width
            //     Alignment
            // format-list -Property
            //     Expression
            //     FormatString
            //     Label
            // format-custom -Property
            //     Expression
            //     Depth
            // format-* -GroupBy
            //     Expression
            //     FormatString
            //     Label
            //

            // Find out if we are in a command argument.  Consider the following possibilities:
            //     cmd @{}
            //     cmd -foo @{}
            //     cmd -foo:@{}
            //     cmd @{},@{}
            //     cmd -foo @{},@{}
            //     cmd -foo:@{},@{}

            var ast = hashtableAst.Parent;

            // Handle completion for hashtable within DynamicKeyword statement
            var dynamicKeywordStatementAst = ast as DynamicKeywordStatementAst;
            if (dynamicKeywordStatementAst != null)
            {
                return CompleteHashtableKeyForDynamicKeyword(completionContext, dynamicKeywordStatementAst, hashtableAst);
            }

            if (ast is ArrayLiteralAst)
            {
                ast = ast.Parent;
            }
            if (ast is CommandParameterAst)
            {
                ast = ast.Parent;
            }

            var commandAst = ast as CommandAst;
            if (commandAst != null)
            {
                var binding = new PseudoParameterBinder().DoPseudoParameterBinding(commandAst, null, null, bindingType: PseudoParameterBinder.BindingType.ArgumentCompletion);
                string parameterName = null;
                foreach (var boundArg in binding.BoundArguments)
                {
                    var astPair = boundArg.Value as AstPair;
                    if (astPair != null)
                    {
                        if (astPair.Argument == hashtableAst)
                        {
                            parameterName = boundArg.Key;
                            break;
                        }
                        continue;
                    }
                    var astArrayPair = boundArg.Value as AstArrayPair;
                    if (astArrayPair != null)
                    {
                        if (astArrayPair.Argument.Contains(hashtableAst))
                        {
                            parameterName = boundArg.Key;
                            break;
                        }
                        continue;
                    }
                }

                if (parameterName != null)
                {
                    if (parameterName.Equals("GroupBy", StringComparison.OrdinalIgnoreCase))
                    {
                        switch (binding.CommandName)
                        {
                            case "Format-Table":
                            case "Format-List":
                            case "Format-Wide":
                            case "Format-Custom":
                                return GetSpecialHashTableKeyMembers("Expression", "FormatString", "Label");
                        }

                        return null;
                    }

                    if (parameterName.Equals("Property", StringComparison.OrdinalIgnoreCase))
                    {
                        switch (binding.CommandName)
                        {
                            case "New-Object":
                                var inferredType = commandAst.GetInferredType(completionContext);
                                var result = new List<CompletionResult>();
                                CompleteMemberByInferredType(
                                    completionContext, inferredType,
                                    result, completionContext.WordToComplete + "*", IsWriteablePropertyMember, isStatic: false);
                                return result;
                            case "Sort-Object":
                                return GetSpecialHashTableKeyMembers("Expression", "Ascending", "Descending");
                            case "Group-Object":
                                return GetSpecialHashTableKeyMembers("Expression");
                            case "Format-Table":
                                return GetSpecialHashTableKeyMembers("Expression", "FormatString", "Label", "Width", "Alignment");
                            case "Format-List":
                                return GetSpecialHashTableKeyMembers("Expression", "FormatString", "Label");
                            case "Format-Wide":
                                return GetSpecialHashTableKeyMembers("Expression", "FormatString");
                            case "Format-Custom":
                                return GetSpecialHashTableKeyMembers("Expression", "Depth");
                        }
                    }
                }
            }

            return null;
        }

        private static List<CompletionResult> GetSpecialHashTableKeyMembers(params string[] keys)
        {
            // Resources were removed because they missed the deadline for loc.
            //return keys.Select(key => new CompletionResult(key, key, CompletionResultType.Property,
            //    ResourceManagerCache.GetResourceString(typeof(CompletionCompleters).Assembly,
            //                                           "TabCompletionStrings", key + "HashKeyDescription"))).ToList();
            return keys.Select(key => new CompletionResult(key, key, CompletionResultType.Property, key)).ToList();
        }

        #endregion Hashtable Keys

        #region Helpers

        internal static PowerShell AddCommandWithPreferenceSetting(PowerShell powershell, string command, Type type = null)
        {
            Diagnostics.Assert(powershell != null, "the passed-in powershell cannot be null");
            Diagnostics.Assert(!String.IsNullOrWhiteSpace(command), "the passed-in command name should not be null or whitespaces");

            if (type != null)
            {
                var cmdletInfo = new CmdletInfo(command, type);
                powershell.AddCommand(cmdletInfo);
            }
            else
            {
                powershell.AddCommand(command);
            }
            powershell
                .AddParameter("ErrorAction", ActionPreference.Ignore)
                .AddParameter("WarningAction", ActionPreference.Ignore)
                .AddParameter("InformationAction", ActionPreference.Ignore)
                .AddParameter("Verbose", false)
                .AddParameter("Debug", false);

            return powershell;
        }

        internal static bool IsPathSafelyExpandable(ExpandableStringExpressionAst expandableStringAst, string extraText, ExecutionContext executionContext, out string expandedString)
        {
            expandedString = null;
            // Expand the string if its type is DoubleQuoted or BareWord
            var constType = expandableStringAst.StringConstantType;
            if (constType == StringConstantType.DoubleQuotedHereString) { return false; }
            Diagnostics.Assert(
                constType == StringConstantType.BareWord ||
                (constType == StringConstantType.DoubleQuoted && expandableStringAst.Extent.Text[0].IsDoubleQuote()),
                "the string to be expanded should be either BareWord or DoubleQuoted");

            var varValues = new List<string>();
            foreach (ExpressionAst nestedAst in expandableStringAst.NestedExpressions)
            {
                var variableAst = nestedAst as VariableExpressionAst;
                if (variableAst == null) { return false; }

                string strValue = CombineVariableWithPartialPath(variableAst, null, executionContext);
                if (strValue != null)
                {
                    varValues.Add(strValue);
                }
                else
                {
                    return false;
                }
            }

            var formattedString = String.Format(CultureInfo.InvariantCulture, expandableStringAst.FormatExpression, varValues.ToArray());
            string quote = (constType == StringConstantType.DoubleQuoted) ? "\"" : String.Empty;

            expandedString = quote + formattedString + extraText + quote;
            return true;
        }

        internal static string CombineVariableWithPartialPath(VariableExpressionAst variableAst, string extraText, ExecutionContext executionContext)
        {
            var varPath = variableAst.VariablePath;
            if (varPath.IsVariable || varPath.DriveName.Equals("env", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    // We check the strict mode inside GetVariableValue
                    object value = VariableOps.GetVariableValue(varPath, executionContext, variableAst);
                    var strValue = (value == null) ? String.Empty : value as string;

                    if (strValue == null)
                    {
                        object baseObj = PSObject.Base(value);
                        if (baseObj is string || baseObj.GetType().GetTypeInfo().IsPrimitive)
                        {
                            strValue = LanguagePrimitives.ConvertTo<string>(value);
                        }
                    }

                    if (strValue != null)
                    {
                        return strValue + extraText;
                    }
                }
                catch (Exception e)
                {
                    CommandProcessorBase.CheckForSevereException(e);
                }
            }
            return null;
        }

        internal static string HandleDoubleAndSingleQuote(ref string wordToComplete)
        {
            string quote = string.Empty;

            if (!string.IsNullOrEmpty(wordToComplete) && (wordToComplete[0].IsSingleQuote() || wordToComplete[0].IsDoubleQuote()))
            {
                char frontQuote = wordToComplete[0];
                int length = wordToComplete.Length;

                if (length == 1)
                {
                    wordToComplete = string.Empty;
                    quote = frontQuote.IsSingleQuote() ? "'" : "\"";
                }
                else if (length > 1)
                {
                    if ((wordToComplete[length - 1].IsDoubleQuote() && frontQuote.IsDoubleQuote()) || (wordToComplete[length - 1].IsSingleQuote() && frontQuote.IsSingleQuote()))
                    {
                        wordToComplete = wordToComplete.Substring(1, length - 2);
                        quote = frontQuote.IsSingleQuote() ? "'" : "\"";
                    }
                    else if (!wordToComplete[length - 1].IsDoubleQuote() && !wordToComplete[length - 1].IsSingleQuote())
                    {
                        wordToComplete = wordToComplete.Substring(1);
                        quote = frontQuote.IsSingleQuote() ? "'" : "\"";
                    }
                }
            }

            return quote;
        }

        internal static bool IsSplattedVariable(Ast targetExpr)
        {
            if (targetExpr is VariableExpressionAst && ((VariableExpressionAst)targetExpr).Splatted)
            {
                // It's splatted variable, member expansion is not useful
                return true;
            }
            return false;
        }

        internal static void CompleteMemberHelper(
            bool @static,
            string memberName,
            ExpressionAst targetExpr,
            CompletionContext context,
            List<CompletionResult> results)
        {
            object value;
            if (SafeExprEvaluator.TrySafeEval(targetExpr, context.ExecutionContext, out value) && value != null)
            {
                if (targetExpr is ArrayExpressionAst && !(value is object[]))
                {
                    // When the array contains only one element, the evaluation result would be that element. We wrap it into an array
                    value = new[] { value };
                }

                var powershell = context.Helper.CurrentPowerShell;

                // Instead of Get-Member, we access the members directly and send as input to the pipe.
                AddCommandWithPreferenceSetting(powershell, "Microsoft.PowerShell.Core\\Where-Object")
                    .AddParameter("Property", "Name")
                    .AddParameter("Like")
                    .AddParameter("Value", memberName);
                AddCommandWithPreferenceSetting(powershell, "Microsoft.PowerShell.Utility\\Sort-Object")
                    .AddParameter("Property", new object[] { "MemberType", "Name" });

                IEnumerable members;
                if (@static)
                {
                    var type = PSObject.Base(value) as Type;
                    if (type == null)
                    {
                        return;
                    }
                    members = PSObject.dotNetStaticAdapter.BaseGetMembers<PSMemberInfo>(type);
                }
                else
                {
                    members = PSObject.AsPSObject(value).Members;
                }
                Exception exceptionThrown;
                var sortedMembers = context.Helper.ExecuteCurrentPowerShell(out exceptionThrown, members);

                foreach (var member in sortedMembers)
                {
                    var memberInfo = (PSMemberInfo)PSObject.Base(member);
                    if (memberInfo.IsHidden)
                    {
                        continue;
                    }

                    var completionText = memberInfo.Name;

                    // Handle scenarios like this: $aa | add-member 'a b' 23; $aa.a<tab>
                    if (completionText.IndexOfAny(s_charactersRequiringQuotes) != -1)
                    {
                        completionText = completionText.Replace("'", "''");
                        completionText = "'" + completionText + "'";
                    }

                    var isMethod = memberInfo is PSMethodInfo;
                    if (isMethod)
                    {
                        var isSpecial = (memberInfo is PSMethod) && ((PSMethod)memberInfo).IsSpecial;
                        if (isSpecial)
                            continue;
                        completionText += '(';
                    }

                    string tooltip = memberInfo.ToString();
                    if (tooltip.IndexOf("),", StringComparison.OrdinalIgnoreCase) != -1)
                    {
                        var overloads = tooltip.Split(new[] { ")," }, StringSplitOptions.RemoveEmptyEntries);
                        var newTooltip = new StringBuilder();
                        foreach (var overload in overloads)
                        {
                            newTooltip.Append(overload.Trim() + ")\r\n");
                        }
                        newTooltip.Remove(newTooltip.Length - 3, 3);
                        tooltip = newTooltip.ToString();
                    }

                    results.Add(
                        new CompletionResult(completionText, memberInfo.Name,
                                             isMethod ? CompletionResultType.Method : CompletionResultType.Property,
                                             tooltip));
                }

                var dictionary = PSObject.Base(value) as IDictionary;
                if (dictionary != null)
                {
                    var pattern = WildcardPattern.Get(memberName, WildcardOptions.IgnoreCase);
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        var key = entry.Key as string;
                        if (key == null)
                            continue;

                        if (pattern.IsMatch(key))
                        {
                            // Handle scenarios like this: $hashtable["abc#d"] = 100; $hashtable.ab<tab>
                            if (key.IndexOfAny(s_charactersRequiringQuotes) != -1)
                            {
                                key = key.Replace("'", "''");
                                key = "'" + key + "'";
                            }

                            results.Add(new CompletionResult(key, key, CompletionResultType.Property, key));
                        }
                    }
                }

                if (!@static && IsValueEnumerable(PSObject.Base(value)))
                {
                    // Complete extension methods 'Where' and 'ForEach' for Enumerable values
                    CompleteExtensionMethods(memberName, results);
                }
            }
        }

        /// <summary>
        /// Check if a value is treated as Enumerable in powershell
        /// </summary>
        private static bool IsValueEnumerable(object value)
        {
            object baseValue = PSObject.Base(value);

            if (baseValue == null || baseValue is string || baseValue is PSObject ||
                baseValue is IDictionary || baseValue is System.Xml.XmlNode)
            {
                return false;
            }

            if (baseValue is IEnumerable || baseValue is IEnumerator || baseValue is DataTable)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Check if a strong type is treated as Enumerable in powershell
        /// </summary>
        private static bool IsStaticTypeEnumerable(Type type)
        {
            if (type.Equals(typeof(string)) || typeof(IDictionary).IsAssignableFrom(type) || typeof(System.Xml.XmlNode).IsAssignableFrom(type))
            {
                return false;
            }

            if (typeof(IEnumerable).IsAssignableFrom(type) || typeof(IEnumerator).IsAssignableFrom(type))
            {
                return true;
            }

            return false;
        }

        private static bool CompletionRequiresQuotes(string completion, bool escape)
        {
            // If the tokenizer sees the completion as more than two tokens, or if there is some error, then
            // some form of quoting is necessary (if it's a variable, we'd need ${}, filenames would need [], etc.)

            Language.Token[] tokens;
            ParseError[] errors;
            Language.Parser.ParseInput(completion, out tokens, out errors);

            char[] charToCheck = escape ? new[] { '$', '[', ']', '`' } : new[] { '$', '`' };

            // Expect no errors and 2 tokens (1 is for our completion, the other is eof)
            // Or if the completion is a keyword, we ignore the errors
            bool requireQuote = !(errors.Length == 0 && tokens.Length == 2);
            if ((!requireQuote && tokens[0] is StringToken) ||
                (tokens.Length == 2 && (tokens[0].TokenFlags & TokenFlags.Keyword) != 0))
            {
                requireQuote = false;
                var value = tokens[0].Text;
                if (value.IndexOfAny(charToCheck) != -1)
                    requireQuote = true;
            }

            return requireQuote;
        }

        private static bool ProviderSpecified(string path)
        {
            var index = path.IndexOf(':');
            return index != -1 && index + 1 < path.Length && path[index + 1] == ':';
        }

        private static Type GetEffectiveParameterType(Type type)
        {
            var underlying = Nullable.GetUnderlyingType(type);
            return underlying ?? type;
        }

        /// <summary>
        /// Turn on the "LiteralPaths" option.
        /// </summary>
        /// <param name="completionContext"></param>
        /// <returns>
        /// Indicate whether the "LiteralPaths" option needs to be removed after operation
        /// </returns>
        private static bool TurnOnLiteralPathOption(CompletionContext completionContext)
        {
            bool clearLiteralPathsKey = false;

            if (completionContext.Options == null)
            {
                completionContext.Options = new Hashtable { { "LiteralPaths", true } };
                clearLiteralPathsKey = true;
            }
            else if (!completionContext.Options.ContainsKey("LiteralPaths"))
            {
                // Dont escape '[',']','`' when the file name is treated as command name
                completionContext.Options.Add("LiteralPaths", true);
                clearLiteralPathsKey = true;
            }

            return clearLiteralPathsKey;
        }

        /// <summary>
        /// Return whether we need to add ampersand when it's necessary
        /// </summary>
        /// <param name="context"></param>
        /// <param name="defaultChoice"></param>
        /// <returns></returns>
        internal static bool IsAmpersandNeeded(CompletionContext context, bool defaultChoice)
        {
            if (context.RelatedAsts != null && !string.IsNullOrEmpty(context.WordToComplete))
            {
                var lastAst = context.RelatedAsts.Last();
                var parent = lastAst.Parent as CommandAst;

                if (parent != null && parent.CommandElements.Count == 1 &&
                    ((!defaultChoice && parent.InvocationOperator == TokenKind.Unknown) ||
                     (defaultChoice && parent.InvocationOperator != TokenKind.Unknown)))
                {
                    // - When the default choice is NOT to add ampersand, we only return true
                    //   when the invocation operator is NOT specified.
                    // - When the default choice is to add ampersand, we only return false
                    //   when the invocation operator is specified.
                    defaultChoice = !defaultChoice;
                }
            }
            return defaultChoice;
        }

        private class ItemPathComparer : IComparer<PSObject>
        {
            public int Compare(PSObject x, PSObject y)
            {
                var xPathInfo = PSObject.Base(x) as PathInfo;
                var xFileInfo = PSObject.Base(x) as IO.FileSystemInfo;
                var xPathStr = PSObject.Base(x) as string;

                var yPathInfo = PSObject.Base(y) as PathInfo;
                var yFileInfo = PSObject.Base(y) as IO.FileSystemInfo;
                var yPathStr = PSObject.Base(y) as string;

                string xPath = null, yPath = null;

                if (xPathInfo != null)
                    xPath = xPathInfo.ProviderPath;
                else if (xFileInfo != null)
                    xPath = xFileInfo.FullName;
                else if (xPathStr != null)
                    xPath = xPathStr;

                if (yPathInfo != null)
                    yPath = yPathInfo.ProviderPath;
                else if (yFileInfo != null)
                    yPath = yFileInfo.FullName;
                else if (yPathStr != null)
                    yPath = yPathStr;

                if (string.IsNullOrEmpty(xPath) || string.IsNullOrEmpty(yPath))
                    Diagnostics.Assert(false, "Base object of item PSObject should be either PathInfo or FileSystemInfo");

                return String.Compare(xPath, yPath, StringComparison.CurrentCultureIgnoreCase);
            }
        }

        private class CommandNameComparer : IComparer<PSObject>
        {
            public int Compare(PSObject x, PSObject y)
            {
                string xName = null;
                string yName = null;

                object xObj = PSObject.Base(x);
                object yObj = PSObject.Base(y);

                var xCommandInfo = xObj as CommandInfo;
                xName = xCommandInfo != null ? xCommandInfo.Name : xObj as string;

                var yCommandInfo = yObj as CommandInfo;
                yName = yCommandInfo != null ? yCommandInfo.Name : yObj as string;

                if (xName == null || yName == null)
                    Diagnostics.Assert(false, "Base object of Command PSObject should be either CommandInfo or string");

                return String.Compare(xName, yName, StringComparison.OrdinalIgnoreCase);
            }
        }

        #endregion Helpers
    }

    /// <summary>
    /// This class is very similar to the restricted langauge checker, but it is meant to allow more things, yet still
    /// be considered "safe", at least in the sense that tab completion can rely on it to not do bad things.  The primary
    /// use is for intellisense where you don't want to run arbitrary code, but you do want to know the values
    /// of various expressions so you can get the members.
    /// </summary>
    internal class SafeExprEvaluator : ICustomAstVisitor2
    {
        internal static bool TrySafeEval(ExpressionAst ast, ExecutionContext executionContext, out object value)
        {
            if (!(bool)ast.Accept(new SafeExprEvaluator()))
            {
                value = null;
                return false;
            }

            try
            {
                // ConstrainedLanguage has already been applied as necessary when we construct CompletionContext
                Diagnostics.Assert(!(ExecutionContext.HasEverUsedConstrainedLanguage && executionContext.LanguageMode != PSLanguageMode.ConstrainedLanguage),
                                   "If the runspace has ever used constrained language mode, then the current language mode should already be set to contrained language");

                // We're passing 'true' here for isTrustedInput, because SafeExprEvaluator ensures that the AST
                // has no dangerous side-effects such as arbitrary expression evaluation. It does require variable
                // access and a few other minor things, which staples of tab completion:
                //
                // $t = Get-Process
                // $t[0].MainModule.<TAB>
                //
                value = Compiler.GetExpressionValue(ast, true, executionContext);
                return true;
            }
            catch
            {
                value = null;
                return false;
            }
        }

        public object VisitErrorStatement(ErrorStatementAst errorStatementAst) { return false; }
        public object VisitErrorExpression(ErrorExpressionAst errorExpressionAst) { return false; }
        public object VisitScriptBlock(ScriptBlockAst scriptBlockAst) { return false; }
        public object VisitParamBlock(ParamBlockAst paramBlockAst) { return false; }
        public object VisitNamedBlock(NamedBlockAst namedBlockAst) { return false; }
        public object VisitTypeConstraint(TypeConstraintAst typeConstraintAst) { return false; }
        public object VisitAttribute(AttributeAst attributeAst) { return false; }
        public object VisitNamedAttributeArgument(NamedAttributeArgumentAst namedAttributeArgumentAst) { return false; }
        public object VisitParameter(ParameterAst parameterAst) { return false; }
        public object VisitFunctionDefinition(FunctionDefinitionAst functionDefinitionAst) { return false; }
        public object VisitIfStatement(IfStatementAst ifStmtAst) { return false; }
        public object VisitTrap(TrapStatementAst trapStatementAst) { return false; }
        public object VisitSwitchStatement(SwitchStatementAst switchStatementAst) { return false; }
        public object VisitDataStatement(DataStatementAst dataStatementAst) { return false; }
        public object VisitForEachStatement(ForEachStatementAst forEachStatementAst) { return false; }
        public object VisitDoWhileStatement(DoWhileStatementAst doWhileStatementAst) { return false; }
        public object VisitForStatement(ForStatementAst forStatementAst) { return false; }
        public object VisitWhileStatement(WhileStatementAst whileStatementAst) { return false; }
        public object VisitCatchClause(CatchClauseAst catchClauseAst) { return false; }
        public object VisitTryStatement(TryStatementAst tryStatementAst) { return false; }
        public object VisitBreakStatement(BreakStatementAst breakStatementAst) { return false; }
        public object VisitContinueStatement(ContinueStatementAst continueStatementAst) { return false; }
        public object VisitReturnStatement(ReturnStatementAst returnStatementAst) { return false; }
        public object VisitExitStatement(ExitStatementAst exitStatementAst) { return false; }
        public object VisitThrowStatement(ThrowStatementAst throwStatementAst) { return false; }
        public object VisitDoUntilStatement(DoUntilStatementAst doUntilStatementAst) { return false; }
        public object VisitAssignmentStatement(AssignmentStatementAst assignmentStatementAst) { return false; }
        // REVIEW: we could relax this to allow specific commands
        public object VisitCommand(CommandAst commandAst) { return false; }
        public object VisitCommandExpression(CommandExpressionAst commandExpressionAst) { return false; }
        public object VisitCommandParameter(CommandParameterAst commandParameterAst) { return false; }
        public object VisitFileRedirection(FileRedirectionAst fileRedirectionAst) { return false; }
        public object VisitMergingRedirection(MergingRedirectionAst mergingRedirectionAst) { return false; }
        public object VisitExpandableStringExpression(ExpandableStringExpressionAst expandableStringExpressionAst) { return false; }
        public object VisitAttributedExpression(AttributedExpressionAst attributedExpressionAst) { return false; }
        public object VisitBlockStatement(BlockStatementAst blockStatementAst) { return false; }
        public object VisitInvokeMemberExpression(InvokeMemberExpressionAst invokeMemberExpressionAst) { return false; }
        public object VisitUsingExpression(UsingExpressionAst usingExpressionAst) { return false; }
        public object VisitTypeDefinition(TypeDefinitionAst typeDefinitionAst) { return false; }
        public object VisitPropertyMember(PropertyMemberAst propertyMemberAst) { return false; }
        public object VisitFunctionMember(FunctionMemberAst functionMemberAst) { return false; }
        public object VisitBaseCtorInvokeMemberExpression(BaseCtorInvokeMemberExpressionAst baseCtorInvokeMemberExpressionAst) { return false; }
        public object VisitUsingStatement(UsingStatementAst usingStatementAst) { return false; }
        public object VisitConfigurationDefinition(ConfigurationDefinitionAst configurationDefinitionAst)
        {
            return configurationDefinitionAst.Body.Accept(this);
        }
        public object VisitDynamicKeywordStatement(DynamicKeywordStatementAst dynamicKeywordStatementAst)
        {
            return false;
        }

        public object VisitStatementBlock(StatementBlockAst statementBlockAst)
        {
            if (statementBlockAst.Traps != null) return false;
            // REVIEW: we could relax this to allow multiple statements
            if (statementBlockAst.Statements.Count > 1) return false;
            var pipeline = statementBlockAst.Statements.FirstOrDefault();
            return pipeline != null && (bool)pipeline.Accept(this);
        }

        public object VisitPipeline(PipelineAst pipelineAst)
        {
            var expr = pipelineAst.GetPureExpression();
            return expr != null && (bool)expr.Accept(this);
        }

        public object VisitBinaryExpression(BinaryExpressionAst binaryExpressionAst)
        {
            return (bool)binaryExpressionAst.Left.Accept(this) && (bool)binaryExpressionAst.Right.Accept(this);
        }

        public object VisitUnaryExpression(UnaryExpressionAst unaryExpressionAst)
        {
            return (bool)unaryExpressionAst.Child.Accept(this);
        }

        public object VisitConvertExpression(ConvertExpressionAst convertExpressionAst)
        {
            return (bool)convertExpressionAst.Child.Accept(this);
        }

        public object VisitConstantExpression(ConstantExpressionAst constantExpressionAst)
        {
            return true;
        }

        public object VisitStringConstantExpression(StringConstantExpressionAst stringConstantExpressionAst)
        {
            return true;
        }

        public object VisitSubExpression(SubExpressionAst subExpressionAst)
        {
            return subExpressionAst.SubExpression.Accept(this);
        }

        public object VisitVariableExpression(VariableExpressionAst variableExpressionAst)
        {
            return true;
        }

        public object VisitTypeExpression(TypeExpressionAst typeExpressionAst)
        {
            return true;
        }

        public object VisitMemberExpression(MemberExpressionAst memberExpressionAst)
        {
            return (bool)memberExpressionAst.Expression.Accept(this) && (bool)memberExpressionAst.Member.Accept(this);
        }

        public object VisitIndexExpression(IndexExpressionAst indexExpressionAst)
        {
            return (bool)indexExpressionAst.Target.Accept(this) && (bool)indexExpressionAst.Index.Accept(this);
        }

        public object VisitArrayExpression(ArrayExpressionAst arrayExpressionAst)
        {
            return arrayExpressionAst.SubExpression.Accept(this);
        }

        public object VisitArrayLiteral(ArrayLiteralAst arrayLiteralAst)
        {
            return arrayLiteralAst.Elements.All(e => (bool)e.Accept(this));
        }

        public object VisitHashtable(HashtableAst hashtableAst)
        {
            foreach (var keyValuePair in hashtableAst.KeyValuePairs)
            {
                if (!(bool)keyValuePair.Item1.Accept(this))
                    return false;
                if (!(bool)keyValuePair.Item2.Accept(this))
                    return false;
            }
            return true;
        }

        public object VisitScriptBlockExpression(ScriptBlockExpressionAst scriptBlockExpressionAst)
        {
            return true;
        }

        public object VisitParenExpression(ParenExpressionAst parenExpressionAst)
        {
            return parenExpressionAst.Pipeline.Accept(this);
        }
    }
}
