/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management.Automation.Internal;
using System.Management.Automation.Runspaces;
using System.Security;
using Dbg = System.Management.Automation.Diagnostics;
using System.Management.Automation.Host;
using Microsoft.PowerShell.Commands;
using Microsoft.Win32;

namespace System.Management.Automation
{
    /// <summary>
    /// EventArgs for the ScriptCmdletVariableUpdate event
    /// </summary>
    public class CommandLookupEventArgs : EventArgs
    {
        /// <summary>
        /// Constructor for event args object
        /// </summary>
        /// <param name="commandName">The name of the command we're searching for</param>
        /// <param name="commandOrigin">The origin of the command internal or runspace (external)</param>
        /// <param name="context">The execution context for this command</param>
        internal CommandLookupEventArgs(string commandName, CommandOrigin commandOrigin, ExecutionContext context)
        {
            CommandName = commandName;
            CommandOrigin = commandOrigin;
            _context = context;
        }

        private ExecutionContext _context;

        /// <summary>
        /// The name of the command we're looking for
        /// </summary>
        public string CommandName { get; }

        /// <summary>
        /// The origin of the command internal or runspace (external)
        /// </summary>
        public CommandOrigin CommandOrigin { get; }

        /// <summary>
        /// If true on return from event handler, the search is stopped.
        /// </summary>
        public bool StopSearch { get; set; }

        /// <summary>
        /// The CommandInfo obejct for the command that was found.
        /// </summary>
        public CommandInfo Command { get; set; }

        /// <summary>
        /// Scriptblock to be returned as the found command. If it is set to
        /// null, then the command to return and the StopSearch flag will be reset.
        /// </summary>
        public ScriptBlock CommandScriptBlock
        {
            get { return _scriptBlock; }

            set
            {
                _scriptBlock = value;
                if (_scriptBlock != null)
                {
                    string dynamicName = "LookupHandlerReplacementFor<<" + CommandName + ">>";
                    Command = new FunctionInfo(dynamicName, _scriptBlock, _context);
                    StopSearch = true;
                }
                else
                {
                    Command = null;
                    StopSearch = false;
                }
            }
        }
        private ScriptBlock _scriptBlock;
    }

    /// <summary>
    /// Defines the preference options for the Module Auto-loading feature
    /// </summary>
    public enum PSModuleAutoLoadingPreference
    {
        /// <summary>
        /// Do not auto-load modules when a command is not found.
        /// </summary>
        None = 0,

        /// <summary>
        /// Only auto-load modules when a command is not found, and the command
        /// is module-qualified.
        /// </summary>
        ModuleQualified = 1,

        /// <summary>
        /// Auto-load modules when a command is not found.
        /// </summary>
        All = 2
    }

    /// <summary>
    /// CommandDiscovery...
    /// </summary>
    internal class CommandDiscovery
    {
        [TraceSource("CommandDiscovery", "Traces the discovery of cmdlets, scripts, functions, applications, etc.")]
        internal static PSTraceSource discoveryTracer =
            PSTraceSource.GetTracer(
                "CommandDiscovery",
                "Traces the discovery of cmdlets, scripts, functions, applications, etc.",
                false);

        #region ctor

        /// <summary>
        /// Default constructor...
        /// </summary>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="context"/> is null.
        /// </exception>
        /// 
        internal CommandDiscovery(ExecutionContext context)
        {
            if (context == null)
            {
                throw PSTraceSource.NewArgumentNullException("context");
            }

            Context = context;
            discoveryTracer.ShowHeaders = false;

            // Cache the ScriptInfo for the scripts defined in the RunspaceConfiguration

            _cachedScriptInfo =
                new Dictionary<string, ScriptInfo>(StringComparer.OrdinalIgnoreCase);

            LoadScriptInfo();
        }

        private void AddCmdletToCache(CmdletConfigurationEntry entry)
        {
            if (!IsSpecialCmdlet(entry.ImplementingType))
            {
                CmdletInfo newCmdletInfo = NewCmdletInfo(entry, SessionStateEntryVisibility.Public);
                AddCmdletInfoToCache(newCmdletInfo.Name, newCmdletInfo, isGlobal: true);
            }
        }

        /// <summary>
        /// Determines if the cmdlet is a cmdlet that shouldn't be in the discovery list.
        /// </summary>
        /// 
        /// <param name="implementingType">
        /// Type implementing the cmdlet
        /// </param>
        /// 
        /// <returns>
        /// True if the cmdlet is a special cmdlet that shouldn't be part of the discovery list. Or false otherwise.
        /// </returns>
        /// 
        private bool IsSpecialCmdlet(Type implementingType)
        {
            // These commands should never be put in the discovery list.  They are an internal implementation
            // detail of the formatting and output component. That component uses these cmdlets by creating
            // an instance of the CommandProcessor class directly.
            return implementingType == typeof(OutLineOutputCommand) || implementingType == typeof(FormatDefaultCommand);
        }

        private CmdletInfo NewCmdletInfo(CmdletConfigurationEntry entry, SessionStateEntryVisibility visibility)
        {
            CmdletInfo ci = new CmdletInfo(entry.Name, entry.ImplementingType, entry.HelpFileName, entry.PSSnapIn, Context);
            ci.Visibility = visibility;
            return ci;
        }

        private CmdletInfo NewCmdletInfo(SessionStateCmdletEntry entry)
        {
            return NewCmdletInfo(entry, Context);
        }

        internal static CmdletInfo NewCmdletInfo(SessionStateCmdletEntry entry, ExecutionContext context)
        {
            CmdletInfo ci = new CmdletInfo(entry.Name, entry.ImplementingType, entry.HelpFileName, entry.PSSnapIn, context)
            {
                Visibility = entry.Visibility,
                Module = entry.Module
            };
            return ci;
        }

        internal static AliasInfo NewAliasInfo(SessionStateAliasEntry entry, ExecutionContext context)
        {
            AliasInfo ci = new AliasInfo(entry.Name, entry.Definition, context, entry.Options)
            {
                Visibility = entry.Visibility,
                Module = entry.Module
            };
            return ci;
        }

        /// <summary>
        /// Adds the CmdletInfo to the cmdlet cache in the current scope object.
        /// </summary>
        /// 
        /// <param name="name">
        /// The name of the cmdlet to add.
        /// </param>
        /// 
        /// <param name="newCmdletInfo">
        /// The CmdletInfo to add.
        /// </param>
        /// 
        /// <param name="isGlobal">
        /// If true, the cmdlet is added to the Module Scope of the session state.
        /// </param>
        /// 
        /// <exception cref="PSNotSupportedException">
        /// If a cmdlet with the same module and cmdlet name already exists
        /// but has a different implementing type.
        /// </exception>
        ///
        internal CmdletInfo AddCmdletInfoToCache(string name, CmdletInfo newCmdletInfo, bool isGlobal)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }

            if (newCmdletInfo == null)
            {
                throw PSTraceSource.NewArgumentNullException("cmdlet");
            }

            if (isGlobal)
            {
                // When cmdlet cache was not scope-based, we used to import cmdlets to the module scope. 
                // We need to do the same as the default action (setting "isGlobal" is done as a default action in the caller)
                return Context.EngineSessionState.ModuleScope.AddCmdletToCache(newCmdletInfo.Name, newCmdletInfo, CommandOrigin.Internal, Context);
            }

            return Context.EngineSessionState.CurrentScope.AddCmdletToCache(newCmdletInfo.Name, newCmdletInfo, CommandOrigin.Internal, Context);
        }

        /// <summary>
        /// Add a SessionStateCmdletEntry to the cmdlet cache...
        /// </summary>
        /// <param name="entry"></param>
        internal void AddSessionStateCmdletEntryToCache(SessionStateCmdletEntry entry)
        {
            AddSessionStateCmdletEntryToCache(entry, /*local*/false);
        }

        /// <summary>
        /// Add a SessionStateCmdletEntry to the cmdlet cache...
        /// </summary>
        /// <param name="entry"></param>
        /// <param name="local"></param>
        internal void AddSessionStateCmdletEntryToCache(SessionStateCmdletEntry entry, bool local)
        {
            if (!IsSpecialCmdlet(entry.ImplementingType))
            {
                CmdletInfo nci = NewCmdletInfo(entry);
                AddCmdletInfoToCache(nci.Name, nci, !local);
            }
        }

        private void LoadScriptInfo()
        {
            if (Context.RunspaceConfiguration != null)
            {
                foreach (ScriptConfigurationEntry entry in Context.RunspaceConfiguration.Scripts)
                {
                    try
                    {
                        _cachedScriptInfo.Add(entry.Name, new ScriptInfo(entry.Name, ScriptBlock.Create(Context, entry.Definition), Context));
                    }
                    catch (ArgumentException)
                    {
                        PSNotSupportedException notSupported =
                            PSTraceSource.NewNotSupportedException(
                                DiscoveryExceptions.DuplicateScriptName,
                                entry.Name);

                        throw notSupported;
                    }
                }
            }
        }
        #endregion ctor

        #region internal methods

        /// <summary>
        /// Look up a command named by the argument string and return its CommandProcessorBase.
        /// </summary>
        /// 
        /// <param name="commandName">
        /// The command name to lookup.
        /// </param>
        /// 
        /// <param name="commandOrigin"> Location where the command was dispatched from. </param>
        /// 
        /// <param name="useLocalScope"> 
        /// True if command processor should use local scope to execute the command,
        /// False if not.  Null if command discovery should default to something reasonable
        /// for the command discovered.
        /// </param>
        /// <returns>
        /// 
        /// </returns>
        /// 
        /// <exception cref="CommandNotFoundException">
        /// If the command, <paramref name="commandName"/>, could not be found.
        /// </exception>
        /// 
        /// <exception cref="System.Security.SecurityException">
        /// If the security manager is preventing the command from running.
        /// </exception>
        /// 
        internal CommandProcessorBase LookupCommandProcessor(string commandName,
            CommandOrigin commandOrigin, bool? useLocalScope)
        {
            CommandInfo commandInfo = null;
#if false                   
                if (tokenCache.ContainsKey (commandName))
                {
                    commandInfo = tokenCache[commandName];
                }
                else
                {
                    commandInfo = LookupCommandInfo (commandName);

                    if (commandInfo.CommandType == CommandTypes.Alias)
                    {
                        commandInfo = ((AliasInfo)commandInfo).ResolvedCommand;
                    }

                    tokenCache[commandName] = commandInfo;
                }
#else
            commandInfo = LookupCommandInfo(commandName, commandOrigin);
#endif
            CommandProcessorBase processor = LookupCommandProcessor(commandInfo, commandOrigin, useLocalScope, null);

            // commandInfo.Name might be different than commandName - restore the original invocation name
            processor.Command.MyInvocation.InvocationName = commandName;

            return processor;
        }

        //Minishell ExternalScriptInfo scriptInfo

        internal CommandProcessorBase CreateScriptProcessorForMiniShell(ExternalScriptInfo scriptInfo, bool useLocalScope, SessionStateInternal sessionState)
        {
            VerifyScriptRequirements(scriptInfo, Context);

            if (String.IsNullOrEmpty(scriptInfo.RequiresApplicationID))
            {
                if (scriptInfo.RequiresPSSnapIns != null && scriptInfo.RequiresPSSnapIns.Any())
                {
                    Collection<string> requiresMissingPSSnapIns = GetPSSnapinNames(scriptInfo.RequiresPSSnapIns);

                    ScriptRequiresException scriptRequiresException =
                        new ScriptRequiresException(
                            scriptInfo.Name,
                            requiresMissingPSSnapIns,
                            "ScriptRequiresMissingPSSnapIns",
                            true);
                    throw scriptRequiresException;
                }

                return CreateCommandProcessorForScript(scriptInfo, Context, useLocalScope, sessionState);
            }
            else
            {
                if (String.Equals(
                       Context.ShellID,
                       scriptInfo.RequiresApplicationID,
                       StringComparison.OrdinalIgnoreCase))
                {
                    return CreateCommandProcessorForScript(scriptInfo, Context, useLocalScope, sessionState);
                }
                else
                {
                    // Throw a runtime exception

                    string shellPath = GetShellPathFromRegistry(scriptInfo.RequiresApplicationID);

                    ScriptRequiresException sre =
                        new ScriptRequiresException(
                            scriptInfo.Name,
                            scriptInfo.RequiresApplicationID,
                            shellPath,
                            "ScriptRequiresUnmatchedShellId");

                    throw sre;
                }
            }
        }

        internal static void VerifyRequiredModules(ExternalScriptInfo scriptInfo, ExecutionContext context)
        {
            // Check Required Modules
            if (scriptInfo.RequiresModules != null)
            {
                foreach (var requiredModule in scriptInfo.RequiresModules)
                {
                    ErrorRecord error = null;
                    ModuleCmdletBase.LoadRequiredModule(
                        context: context,
                        currentModule: null,
                        requiredModuleSpecification: requiredModule,
                        moduleManifestPath: null,
                        manifestProcessingFlags: ModuleCmdletBase.ManifestProcessingFlags.LoadElements | ModuleCmdletBase.ManifestProcessingFlags.WriteErrors,
                        error: out error);
                    if (error != null)
                    {
                        ScriptRequiresException scriptRequiresException =
                            new ScriptRequiresException(
                                scriptInfo.Name,
                                new Collection<string> { requiredModule.Name },
                                "ScriptRequiresMissingModules",
                                false,
                                error);
                        throw scriptRequiresException;
                    }
                }
            }
        }

        private static Collection<String> GetPSSnapinNames(IEnumerable<PSSnapInSpecification> PSSnapins)
        {
            Collection<string> result = new Collection<string>();

            foreach (var PSSnapin in PSSnapins)
            {
                result.Add(BuildPSSnapInDisplayName(PSSnapin));
            }
            return result;
        }

        private CommandProcessorBase CreateScriptProcessorForSingleShell(ExternalScriptInfo scriptInfo, ExecutionContext context, bool useLocalScope, SessionStateInternal sessionState)
        {
            VerifyScriptRequirements(scriptInfo, Context);

            IEnumerable<PSSnapInSpecification> requiresPSSnapIns = scriptInfo.RequiresPSSnapIns;
            if (requiresPSSnapIns != null && requiresPSSnapIns.Any())
            {
                Collection<string> requiresMissingPSSnapIns = null;
                VerifyRequiredSnapins(requiresPSSnapIns, context, out requiresMissingPSSnapIns);
                if (requiresMissingPSSnapIns != null)
                {
                    ScriptRequiresException scriptRequiresException =
                        new ScriptRequiresException(
                            scriptInfo.Name,
                            requiresMissingPSSnapIns,
                            "ScriptRequiresMissingPSSnapIns",
                            true);
                    throw scriptRequiresException;
                }
            }
            else
            {
                // If there were no PSSnapins required but there is a shellID required, then we need
                // to error

                if (!String.IsNullOrEmpty(scriptInfo.RequiresApplicationID))
                {
                    ScriptRequiresException sre =
                      new ScriptRequiresException(
                          scriptInfo.Name,
                          String.Empty,
                          String.Empty,
                          "RequiresShellIDInvalidForSingleShell");

                    throw sre;
                }
            }
            return CreateCommandProcessorForScript(scriptInfo, Context, useLocalScope, sessionState);
        }

        private static void VerifyRequiredSnapins(IEnumerable<PSSnapInSpecification> requiresPSSnapIns, ExecutionContext context, out Collection<string> requiresMissingPSSnapIns)
        {
            requiresMissingPSSnapIns = null;
            bool isHostedWithInitialSessionState = false;
            RunspaceConfigForSingleShell rs = null;
            if (context.InitialSessionState != null)
            {
                isHostedWithInitialSessionState = true;
            }
            else if (context.RunspaceConfiguration != null)
            {
                rs = context.RunspaceConfiguration as RunspaceConfigForSingleShell;
                Dbg.Assert(rs != null, "RunspaceConfiguration should not be null");
            }
            else
            {
                Dbg.Assert(false, "PowerShell should be hosted with either InitialSessionState or RunspaceConfiguration");
            }

            foreach (var requiresPSSnapIn in requiresPSSnapIns)
            {
                IEnumerable<PSSnapInInfo> loadedPSSnapIns = null;
                if (isHostedWithInitialSessionState)
                {
                    loadedPSSnapIns = context.InitialSessionState.GetPSSnapIn(requiresPSSnapIn.Name);
                }
                else
                {
                    loadedPSSnapIns = rs.ConsoleInfo.GetPSSnapIn(requiresPSSnapIn.Name, false);
                }
                if (loadedPSSnapIns == null || loadedPSSnapIns.Count() == 0)
                {
                    if (requiresMissingPSSnapIns == null)
                    {
                        requiresMissingPSSnapIns = new Collection<string>();
                    }
                    requiresMissingPSSnapIns.Add(BuildPSSnapInDisplayName(requiresPSSnapIn));
                }
                else
                {
                    // the requires PSSnapin is loaded. now check the PSSnapin version
                    PSSnapInInfo loadedPSSnapIn = loadedPSSnapIns.First();
                    Diagnostics.Assert(loadedPSSnapIn.Version != null,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Version is null for loaded PSSnapin {0}.", loadedPSSnapIn));
                    if (requiresPSSnapIn.Version != null)
                    {
                        if (!AreInstalledRequiresVersionsCompatible(
                            requiresPSSnapIn.Version, loadedPSSnapIn.Version))
                        {
                            if (requiresMissingPSSnapIns == null)
                            {
                                requiresMissingPSSnapIns = new Collection<string>();
                            }
                            requiresMissingPSSnapIns.Add(BuildPSSnapInDisplayName(requiresPSSnapIn));
                        }
                    }
                }
            }
        }

        // This method verifies the following 3 elements of #Requires statement
        // #Requires -RunAsAdministrator
        // #Requires -PSVersion
        // #Requires -PSEdition
        // #Requires -Module
        internal static void VerifyScriptRequirements(ExternalScriptInfo scriptInfo, ExecutionContext context)
        {
            VerifyElevatedPriveleges(scriptInfo);
            VerifyPSVersion(scriptInfo);
            VerifyPSEdition(scriptInfo);
            VerifyRequiredModules(scriptInfo, context);
        }

        internal static void VerifyPSVersion(ExternalScriptInfo scriptInfo)
        {
            Version requiresPSVersion = scriptInfo.RequiresPSVersion;
            // in single shell mode
            if (requiresPSVersion != null)
            {
                if (!Utils.IsPSVersionSupported(requiresPSVersion))
                {
                    ScriptRequiresException scriptRequiresException =
                        new ScriptRequiresException(
                            scriptInfo.Name,
                            requiresPSVersion,
                            PSVersionInfo.PSVersion.ToString(),
                            "ScriptRequiresUnmatchedPSVersion");
                    throw scriptRequiresException;
                }
            }
        }

        internal static void VerifyPSEdition(ExternalScriptInfo scriptInfo)
        {
            if (scriptInfo.RequiresPSEditions != null)
            {
                var isCurrentEditionListed = false;
                var isRequiresPSEditionSpecified = false;
                foreach (var edition in scriptInfo.RequiresPSEditions)
                {
                    isRequiresPSEditionSpecified = true;
                    isCurrentEditionListed = Utils.IsPSEditionSupported(edition);
                    if (isCurrentEditionListed)
                    {
                        break;
                    }
                }

                // Throw an error if required PowerShell editions are specified and without the current PowerShell Edition.
                //
                if (isRequiresPSEditionSpecified && !isCurrentEditionListed)
                {
                    var specifiedEditionsString = string.Join(",", scriptInfo.RequiresPSEditions);
                    var message = StringUtil.Format(DiscoveryExceptions.RequiresPSEditionNotCompatible,
                        scriptInfo.Name,
                        specifiedEditionsString,
                        PSVersionInfo.PSEdition);
                    var ex = new RuntimeException(message);
                    ex.SetErrorId("ScriptRequiresUnmatchedPSEdition");
                    ex.SetTargetObject(scriptInfo.Name);
                    throw ex;
                }
            }
        }

        internal static void VerifyElevatedPriveleges(ExternalScriptInfo scriptInfo)
        {
            bool requiresElevation = scriptInfo.RequiresElevation;
            bool isAdministrator = Utils.IsAdministrator();
            if (requiresElevation && !isAdministrator)
            {
                ScriptRequiresException scriptRequiresException =
                        new ScriptRequiresException(
                            scriptInfo.Name,
                            "ScriptRequiresElevation");
                throw scriptRequiresException;
            }
        }


        #region comment out RequiresNetFrameworkVersion feature 8/10/2010
        /*
         * The "#requires -NetFrameworkVersion" feature is CUT OFF.
         * This method will be reenabled will be CUT OFF too
        /*
        internal static void VerifyNetFrameworkVersion(ExternalScriptInfo scriptInfo)
        {
            Version requiresNetFrameworkVersion = scriptInfo.RequiresNetFrameworkVersion;
            
            if (requiresNetFrameworkVersion != null)
            {
                if (!Utils.IsNetFrameworkVersionSupported(requiresNetFrameworkVersion))
                {
                    ScriptRequiresException scriptRequiresException =
                        new ScriptRequiresException(
                            scriptInfo.Name,
                            scriptInfo.NetFrameworkVersionLineNumber,
                            requiresNetFrameworkVersion,
                            "ScriptRequiresUnmatchedNetFrameworkVersion");
                    throw scriptRequiresException;
                }
            }
        }
        */
        #endregion


        /// <summary>
        /// used to determine compatibility between the versions in the requires statement and
        /// the installed version. The version can be PSSnapin or msh
        /// </summary>
        /// <param name="requires">versions in the requires statement</param>
        /// <param name="installed">version installed</param>
        /// <returns>
        /// true if requires and installed's major version match and requires' minor version
        /// is smaller than or equal to installed's
        /// </returns>
        /// <remarks>
        /// In PowerShell V2, script requiring PowerShell 1.0 will fail.
        /// </remarks>
        private static bool AreInstalledRequiresVersionsCompatible(Version requires, Version installed)
        {
            return requires.Major == installed.Major && requires.Minor <= installed.Minor;
        }

        private static string BuildPSSnapInDisplayName(PSSnapInSpecification PSSnapin)
        {
            return PSSnapin.Version == null ?
                PSSnapin.Name :
                StringUtil.Format(DiscoveryExceptions.PSSnapInNameVersion,
                        PSSnapin.Name, PSSnapin.Version);
        }

        /// <summary>
        /// Look up a command using a CommandInfo object and return its CommandProcessorBase.
        /// </summary>
        /// 
        /// <param name="commandInfo">
        /// The commandInfo for the command to lookup.
        /// </param>
        /// 
        /// <param name="commandOrigin"> Location where the command was dispatched from. </param>
        /// <param name="useLocalScope"> 
        /// True if command processor should use local scope to execute the command,
        /// False if not.  Null if command discovery should default to something reasonable
        /// for the command discovered.
        /// </param>
        /// <param name="sessionState">The session state the commandInfo should be run in.</param>
        /// <returns>
        /// 
        /// </returns>
        /// 
        /// <exception cref="CommandNotFoundException">
        /// If the command, <paramref name="commandName"/>, could not be found.
        /// </exception>
        /// 
        /// <exception cref="System.Management.Automation.PSSecurityException">
        /// If the security manager is preventing the command from running.
        /// </exception>
        /// 
        internal CommandProcessorBase LookupCommandProcessor(CommandInfo commandInfo,
            CommandOrigin commandOrigin, bool? useLocalScope, SessionStateInternal sessionState)
        {
            CommandProcessorBase processor = null;

            HashSet<string> processedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (commandInfo.CommandType == CommandTypes.Alias &&
                (!processedAliases.Contains(commandInfo.Name)) &&
                (commandOrigin == CommandOrigin.Internal || commandInfo.Visibility == SessionStateEntryVisibility.Public))
            {
                processedAliases.Add(commandInfo.Name);

                AliasInfo aliasCommandInfo = (AliasInfo)commandInfo;
                commandInfo = aliasCommandInfo.ResolvedCommand ??
                              LookupCommandInfo(aliasCommandInfo.Definition, commandOrigin, Context);

                // If we didn't have the alias target already resolved, see if it can be loaded.

                if (commandInfo == null)
                {
                    CommandNotFoundException e =
                        new CommandNotFoundException(
                            aliasCommandInfo.Name,
                            null,
                            "AliasNotResolvedException",
                            DiscoveryExceptions.AliasNotResolvedException,
                            aliasCommandInfo.UnresolvedCommandName);
                    throw e;
                }
            }

            ShouldRun(Context, Context.EngineHostInterface, commandInfo, commandOrigin);

            switch (commandInfo.CommandType)
            {
                case CommandTypes.Application:
                    processor = new NativeCommandProcessor((ApplicationInfo)commandInfo, Context);
                    break;
                case CommandTypes.Cmdlet:
                    processor = new CommandProcessor((CmdletInfo)commandInfo, Context);
                    break;
                case CommandTypes.ExternalScript:
                    ExternalScriptInfo scriptInfo = (ExternalScriptInfo)commandInfo;
                    scriptInfo.SignatureChecked = true;
                    try
                    {
                        if (!Context.IsSingleShell)
                        {
                            // in minishell mode
                            processor = CreateScriptProcessorForMiniShell(scriptInfo, useLocalScope ?? true, sessionState);
                        }
                        else
                        {
                            // single shell mode
                            processor = CreateScriptProcessorForSingleShell(scriptInfo, Context, useLocalScope ?? true, sessionState);
                        }
                    }
                    catch (ScriptRequiresSyntaxException reqSyntaxException)
                    {
                        CommandNotFoundException e =
                            new CommandNotFoundException(reqSyntaxException.Message, reqSyntaxException);
                        throw e;
                    }
                    catch (PSArgumentException argException)
                    {
                        CommandNotFoundException e =
                            new CommandNotFoundException(
                                commandInfo.Name,
                                argException,
                                "ScriptRequiresInvalidFormat",
                                DiscoveryExceptions.ScriptRequiresInvalidFormat);
                        throw e;
                    }
                    break;
                case CommandTypes.Filter:
                case CommandTypes.Function:
                case CommandTypes.Workflow:
                case CommandTypes.Configuration:
                    FunctionInfo functionInfo = (FunctionInfo)commandInfo;
                    processor = CreateCommandProcessorForScript(functionInfo, Context, useLocalScope ?? true, sessionState);
                    break;
                case CommandTypes.Script:
                    processor = CreateCommandProcessorForScript((ScriptInfo)commandInfo, Context, useLocalScope ?? true, sessionState);
                    break;
                case CommandTypes.Alias:
                default:
                    {
                        CommandNotFoundException e =
                            new CommandNotFoundException(
                                commandInfo.Name,
                                null,
                                "CommandNotFoundException",
                                DiscoveryExceptions.CommandNotFoundException);
                        throw e;
                    }
            }

            // Set the internal command origin member on the command object at this point...
            processor.Command.CommandOriginInternal = commandOrigin;

            processor.Command.MyInvocation.InvocationName = commandInfo.Name;

            return processor;
        } // LookupCommandProcessor

        internal static void ShouldRun(ExecutionContext context, PSHost host, CommandInfo commandInfo, CommandOrigin commandOrigin)
        {
            //ShouldRunInternal throws PSSecurityException if run is not allowed
            try
            {
                if (commandOrigin == CommandOrigin.Runspace && commandInfo.Visibility != SessionStateEntryVisibility.Public)
                {
                    CommandNotFoundException e = new CommandNotFoundException(
                        commandInfo.Name, null, "CommandNotFoundException", DiscoveryExceptions.CommandNotFoundException);
                    throw e;
                }

                context.AuthorizationManager.ShouldRunInternal(commandInfo, commandOrigin, host);
            }
            catch (PSSecurityException reason)
            {
                MshLog.LogCommandHealthEvent(context,
                                reason,
                                Severity.Warning);

                MshLog.LogCommandLifecycleEvent(context,
                                CommandState.Terminated,
                                commandInfo.Name);

                throw;
            }
        }

        private static CommandProcessorBase CreateCommandProcessorForScript(ScriptInfo scriptInfo, ExecutionContext context, bool useNewScope, SessionStateInternal sessionState)
        {
            sessionState = sessionState ?? scriptInfo.ScriptBlock.SessionStateInternal ?? context.EngineSessionState;
            CommandProcessorBase scriptAsCmdletProcessor = GetScriptAsCmdletProcessor(scriptInfo, context, useNewScope, true, sessionState);
            if (scriptAsCmdletProcessor != null)
            {
                return scriptAsCmdletProcessor;
            }

            return new DlrScriptCommandProcessor(scriptInfo, context, useNewScope, sessionState);
        }

        private static CommandProcessorBase CreateCommandProcessorForScript(ExternalScriptInfo scriptInfo, ExecutionContext context, bool useNewScope, SessionStateInternal sessionState)
        {
            sessionState = sessionState ?? scriptInfo.ScriptBlock.SessionStateInternal ?? context.EngineSessionState;
            CommandProcessorBase scriptAsCmdletProcessor = GetScriptAsCmdletProcessor(scriptInfo, context, useNewScope, true, sessionState);
            if (scriptAsCmdletProcessor != null)
            {
                return scriptAsCmdletProcessor;
            }

            return new DlrScriptCommandProcessor(scriptInfo, context, useNewScope, sessionState);
        }

        internal static CommandProcessorBase CreateCommandProcessorForScript(FunctionInfo functionInfo, ExecutionContext context, bool useNewScope, SessionStateInternal sessionState)
        {
            sessionState = sessionState ?? functionInfo.ScriptBlock.SessionStateInternal ?? context.EngineSessionState;
            CommandProcessorBase scriptAsCmdletProcessor = GetScriptAsCmdletProcessor(functionInfo, context, useNewScope, false, sessionState);
            if (scriptAsCmdletProcessor != null)
            {
                return scriptAsCmdletProcessor;
            }

            return new DlrScriptCommandProcessor(functionInfo, context, useNewScope, sessionState);
        }

        internal static CommandProcessorBase CreateCommandProcessorForScript(ScriptBlock scriptblock, ExecutionContext context, bool useNewScope, SessionStateInternal sessionState)
        {
            sessionState = sessionState ?? scriptblock.SessionStateInternal ?? context.EngineSessionState;

            if (scriptblock.UsesCmdletBinding)
            {
                FunctionInfo fi = new FunctionInfo("", scriptblock, context);
                return GetScriptAsCmdletProcessor(fi, context, useNewScope, false, sessionState);
            }

            return new DlrScriptCommandProcessor(scriptblock, context, useNewScope, CommandOrigin.Internal, sessionState);
        }

        private static CommandProcessorBase GetScriptAsCmdletProcessor(IScriptCommandInfo scriptCommandInfo, ExecutionContext context, bool useNewScope, bool fromScriptFile, SessionStateInternal sessionState)
        {
            if (scriptCommandInfo.ScriptBlock == null || !scriptCommandInfo.ScriptBlock.UsesCmdletBinding)
            {
                return null;
            }

            sessionState = sessionState ?? scriptCommandInfo.ScriptBlock.SessionStateInternal ?? context.EngineSessionState;

            return new CommandProcessor(scriptCommandInfo, context, useNewScope, fromScriptFile, sessionState);
        }

        /// <summary>
        /// Look up a command and return its CommandInfo.
        /// </summary>
        /// 
        /// <param name="commandName">
        /// The command name to lookup.
        /// </param>
        /// 
        /// <returns>
        /// An instance of a CommandInfo object that represents the
        /// command. If the command is resolved as an alias, an AliasInfo
        /// is returned with the ReferencedCommand info intact.
        /// </returns>
        /// 
        /// <exception cref="CommandNotFoundException">
        /// If the command, <paramref name="commandName"/>, could not be found.
        /// </exception>
        /// 
        internal CommandInfo LookupCommandInfo(string commandName)
        {
            return LookupCommandInfo(commandName, CommandOrigin.Internal);
        }

        internal CommandInfo LookupCommandInfo(string commandName, CommandOrigin commandOrigin)
        {
            return LookupCommandInfo(commandName, commandOrigin, Context);
        }

        internal static CommandInfo LookupCommandInfo(string commandName, CommandOrigin commandOrigin, ExecutionContext context)
        {
            return LookupCommandInfo(commandName, CommandTypes.All, SearchResolutionOptions.None, commandOrigin, context);
        }

        internal static CommandInfo LookupCommandInfo(
            string commandName,
            CommandTypes commandTypes,
            SearchResolutionOptions searchResolutionOptions,
            CommandOrigin commandOrigin,
            ExecutionContext context)
        {
            if (String.IsNullOrEmpty(commandName))
            {
                return null;
            }

            bool etwEnabled = CommandDiscoveryEventSource.Log.IsEnabled();
            if (etwEnabled) CommandDiscoveryEventSource.Log.CommandLookupStart(commandName);

            CommandInfo result = null;
            string originalCommandName = commandName;

            Exception lastError = null;

            // Check to see if there is a pre-search look-up event handler...
            CommandLookupEventArgs eventArgs = null;
            EventHandler<CommandLookupEventArgs> preCommandLookupEvent = context.EngineIntrinsics.InvokeCommand.PreCommandLookupAction;

            if (preCommandLookupEvent != null)
            {
                discoveryTracer.WriteLine("Executing PreCommandLookupAction: {0}", commandName);
                try
                {
                    context.CommandDiscovery.RegisterLookupCommandInfoAction("ActivePreLookup", originalCommandName);
                    eventArgs = new CommandLookupEventArgs(originalCommandName, commandOrigin, context);
                    preCommandLookupEvent.Invoke(originalCommandName, eventArgs);

                    discoveryTracer.WriteLine("PreCommandLookupAction returned: {0}", eventArgs.Command);
                }
                catch (Exception e)
                {
                    CommandProcessorBase.CheckForSevereException(e);
                }
                finally { context.CommandDiscovery.UnregisterLookupCommandInfoAction("ActivePreLookup", commandName); }
            }

            // Check the module auto-loading preference 
            PSModuleAutoLoadingPreference moduleAutoLoadingPreference = GetCommandDiscoveryPreference(context, SpecialVariables.PSModuleAutoLoadingPreferenceVarPath, "PSModuleAutoLoadingPreference");

            if (eventArgs == null || eventArgs.StopSearch != true)
            {
                do
                {
                    discoveryTracer.WriteLine("Looking up command: {0}", commandName);

                    // Use the CommandSearcher to find the first command. If there are duplicate
                    // command names, then take the first one...

                    result = TryNormalSearch(commandName, context, commandOrigin, searchResolutionOptions, commandTypes, ref lastError);

                    if (result != null)
                        break;

                    // Try the module-qualified auto-loading (unless module auto-loading has been entirely disabled)
                    if (moduleAutoLoadingPreference != PSModuleAutoLoadingPreference.None)
                    {
                        result = TryModuleAutoLoading(commandName, context, originalCommandName, commandOrigin, ref lastError);
                    }

                    if (result != null)
                        break;

                    // See if the this was not module-qualified. In that case, we should look for the first module
                    // that contains the command and load that.
                    if (moduleAutoLoadingPreference == PSModuleAutoLoadingPreference.All)
                    {
                        result = TryModuleAutoDiscovery(commandName, context, originalCommandName, commandOrigin,
                                                        searchResolutionOptions, commandTypes, ref lastError);
                    }

                    // Otherwise, invoke the CommandNotFound handler
                    if (result == null)
                    {
                        result = InvokeCommandNotFoundHandler(commandName, context, originalCommandName, commandOrigin);
                    }
                } while (false);
            }
            else
            {
                if (eventArgs.Command != null)
                {
                    result = eventArgs.Command;
                }
            }

            // If we resolved a command, give the PostCommandLookup a chance to change it
            if (result != null)
            {
                System.EventHandler<CommandLookupEventArgs> postAction = context.EngineIntrinsics.InvokeCommand.PostCommandLookupAction;
                if (postAction != null)
                {
                    discoveryTracer.WriteLine("Executing PostCommandLookupAction: {0}", originalCommandName);
                    try
                    {
                        context.CommandDiscovery.RegisterLookupCommandInfoAction("ActivePostCommand", originalCommandName);

                        eventArgs = new CommandLookupEventArgs(originalCommandName, commandOrigin, context);
                        eventArgs.Command = result;
                        postAction.Invoke(originalCommandName, eventArgs);

                        if (eventArgs != null)
                        {
                            result = eventArgs.Command;
                            discoveryTracer.WriteLine("PreCommandLookupAction returned: {0}", eventArgs.Command);
                        }
                    }
                    catch (Exception e)
                    {
                        CommandProcessorBase.CheckForSevereException(e);
                    }
                    finally
                    {
                        context.CommandDiscovery.UnregisterLookupCommandInfoAction("ActivePostCommand", originalCommandName);
                    }
                }
            }

            // After all command lookup is done, throw a CommandNotFound if we have no result.
            if (result == null)
            {
                discoveryTracer.TraceError(
                    "'{0}' is not recognized as a cmdlet, function, operable program or script file.",
                    commandName);

                CommandNotFoundException e =
                    new CommandNotFoundException(
                        originalCommandName,
                        lastError,
                        "CommandNotFoundException",
                        DiscoveryExceptions.CommandNotFoundException);
                throw e;
            }

            if (etwEnabled) CommandDiscoveryEventSource.Log.CommandLookupStop(commandName);

            return result;
        }

        internal static void AutoloadModulesWithJobSourceAdapters(System.Management.Automation.ExecutionContext context, CommandOrigin commandOrigin)
        {
            if (!context.IsModuleWithJobSourceAdapterLoaded)
            {
                PSModuleAutoLoadingPreference moduleAutoLoadingPreference = GetCommandDiscoveryPreference(context, SpecialVariables.PSModuleAutoLoadingPreferenceVarPath, "PSModuleAutoLoadingPreference");
                if (moduleAutoLoadingPreference != PSModuleAutoLoadingPreference.None)
                {
                    CmdletInfo cmdletInfo = context.SessionState.InvokeCommand.GetCmdlet("Microsoft.PowerShell.Core\\Import-Module");
                    if ((commandOrigin == CommandOrigin.Internal) ||
                        ((cmdletInfo != null) && (cmdletInfo.Visibility == SessionStateEntryVisibility.Public)))
                    {
                        foreach (var module in System.Management.Automation.ExecutionContext.ModulesWithJobSourceAdapters)
                        {
                            List<PSModuleInfo> existingModule = context.Modules.GetModules(new string[] { module }, false);
                            if (existingModule == null || existingModule.Count == 0)
                            {
                                Exception unUsedException = null;
                                AutoloadSpecifiedModule(module, context, cmdletInfo.Visibility, out unUsedException);
                            }
                        }
                        context.IsModuleWithJobSourceAdapterLoaded = true;
                    }
                }
            }
        }

        internal static Collection<PSModuleInfo> AutoloadSpecifiedModule(string moduleName, ExecutionContext context, SessionStateEntryVisibility visibility, out Exception exception)
        {
            exception = null;
            Collection<PSModuleInfo> matchingModules = null;
            CommandInfo commandInfo = new CmdletInfo("Import-Module", typeof(ImportModuleCommand), null, null, context);
            commandInfo.Visibility = visibility;
            Command importModuleCommand = new Command(commandInfo);

            discoveryTracer.WriteLine("Attempting to load module: {0}", moduleName);

            PowerShell ps = null;
            try
            {
                ps = PowerShell.Create(RunspaceMode.CurrentRunspace)
                    .AddCommand(importModuleCommand)
                    .AddParameter("Name", moduleName)
                     .AddParameter("Scope", StringLiterals.Global)
                     .AddParameter("PassThru")
                     .AddParameter("ErrorAction", ActionPreference.Ignore)
                     .AddParameter("WarningAction", ActionPreference.Ignore)
                     .AddParameter("InformationAction", ActionPreference.Ignore)
                     .AddParameter("Verbose", false)
                     .AddParameter("Debug", false);
                matchingModules = (Collection<PSModuleInfo>)ps.Invoke<PSModuleInfo>();
            }
            catch (Exception e)
            {
                exception = e;
                discoveryTracer.WriteLine("Encountered error importing module: {0}", e.Message);
                //Call-out to user code, catch-all OK
                CommandProcessorBase.CheckForSevereException(e);
            }

            return matchingModules;
        }


        private static CommandInfo InvokeCommandNotFoundHandler(string commandName, ExecutionContext context, string originalCommandName, CommandOrigin commandOrigin)
        {
            CommandInfo result = null;
            CommandLookupEventArgs eventArgs;
            System.EventHandler<CommandLookupEventArgs> cmdNotFoundHandler = context.EngineIntrinsics.InvokeCommand.CommandNotFoundAction;
            if (cmdNotFoundHandler != null)
            {
                discoveryTracer.WriteLine("Executing CommandNotFoundAction: {0}", commandName);
                try
                {
                    context.CommandDiscovery.RegisterLookupCommandInfoAction("ActiveCommandNotFound", originalCommandName);
                    eventArgs = new CommandLookupEventArgs(originalCommandName, commandOrigin, context);
                    cmdNotFoundHandler.Invoke(originalCommandName, eventArgs);
                    result = eventArgs.Command;
                }
                catch (Exception e)
                {
                    CommandProcessorBase.CheckForSevereException(e);
                }
                finally { context.CommandDiscovery.UnregisterLookupCommandInfoAction("ActiveCommandNotFound", originalCommandName); }
            }
            return result;
        }

        private static CommandInfo TryNormalSearch(string commandName,
                                                   ExecutionContext context,
                                                   CommandOrigin commandOrigin,
                                                   SearchResolutionOptions searchResolutionOptions,
                                                   CommandTypes commandTypes,
                                                   ref Exception lastError)
        {
            CommandInfo result = null;

            CommandSearcher searcher =
                new CommandSearcher(
                    commandName,
                    searchResolutionOptions,
                    commandTypes,
                    context);
            searcher.CommandOrigin = commandOrigin;

            try
            {
                if (!searcher.MoveNext())
                {
                    if (!commandName.Contains("-") && !commandName.Contains("\\"))
                    {
                        discoveryTracer.WriteLine(
                            "The command [{0}] was not found, trying again with get- prepended",
                            commandName);

                        commandName = StringLiterals.DefaultCommandVerb + StringLiterals.CommandVerbNounSeparator + commandName;

                        try
                        {
                            result = LookupCommandInfo(commandName, commandTypes, searchResolutionOptions, commandOrigin, context);
                        }
                        catch (CommandNotFoundException) { }
                    }
                }
                else
                {
                    result = ((IEnumerator<CommandInfo>)searcher).Current;
                }
            }
            catch (ArgumentException argException)
            {
                lastError = argException;
            }
            catch (PathTooLongException pathTooLong)
            {
                lastError = pathTooLong;
            }
            catch (FileLoadException fileLoadException)
            {
                lastError = fileLoadException;
            }
            catch (FormatException formatException)
            {
                lastError = formatException;
            }
            catch (MetadataException metadataException)
            {
                lastError = metadataException;
            }
            return result;
        }

        private static CommandInfo TryModuleAutoDiscovery(string commandName,
                                                          ExecutionContext context,
                                                          string originalCommandName,
                                                          CommandOrigin commandOrigin,
                                                          SearchResolutionOptions searchResolutionOptions,
                                                          CommandTypes commandTypes,
                                                          ref Exception lastError)
        {
            bool etwEnabled = CommandDiscoveryEventSource.Log.IsEnabled();
            if (etwEnabled) CommandDiscoveryEventSource.Log.ModuleAutoDiscoveryStart(commandName);

            CommandInfo result = null;
            bool cleanupModuleAnalysisAppDomain = false;
            try
            {
                // If commandName had a slash, it was module-qualified or path-qualified.
                // In that case, we should not return anything (module-qualified is handled
                // by the previous call to TryModuleAutoLoading().
                int colonOrBackslash = commandName.IndexOfAny(Utils.Separators.ColonOrBackslash);
                if (colonOrBackslash != -1)
                    return null;

                CmdletInfo cmdletInfo = context.SessionState.InvokeCommand.GetCmdlet("Microsoft.PowerShell.Core\\Get-Module");
                if ((commandOrigin == CommandOrigin.Internal) ||
                    ((cmdletInfo != null) && (cmdletInfo.Visibility == SessionStateEntryVisibility.Public)))
                {
                    // Search for a module with a matching command, as long as the user would have the ability to
                    // import the module.
                    cmdletInfo = context.SessionState.InvokeCommand.GetCmdlet("Microsoft.PowerShell.Core\\Import-Module");
                    if (((commandOrigin == CommandOrigin.Internal) ||
                         ((cmdletInfo != null) && (cmdletInfo.Visibility == SessionStateEntryVisibility.Public))))
                    {
                        discoveryTracer.WriteLine("Executing non module-qualified search: {0}", commandName);
                        context.CommandDiscovery.RegisterLookupCommandInfoAction("ActiveModuleSearch", commandName);

                        cleanupModuleAnalysisAppDomain = context.TakeResponsibilityForModuleAnalysisAppDomain();

                        // Get the available module files, preferring modules from $PSHOME so that user modules don't
                        // override system modules during auto-loading
                        if (etwEnabled) CommandDiscoveryEventSource.Log.SearchingForModuleFilesStart();
                        var defaultAvailableModuleFiles = ModuleUtils.GetDefaultAvailableModuleFiles(true, true, context);
                        if (etwEnabled) CommandDiscoveryEventSource.Log.SearchingForModuleFilesStop();

                        foreach (string modulePath in defaultAvailableModuleFiles)
                        {
                            // WinBlue:69141 - We need to get the full path here because the module path might be C:\Users\User1\DOCUME~1
                            // While the exportedCommands are cached, they are cached with the full path
                            string expandedModulePath = IO.Path.GetFullPath(modulePath);
                            string moduleShortName = System.IO.Path.GetFileNameWithoutExtension(expandedModulePath);
                            var exportedCommands = AnalysisCache.GetExportedCommands(expandedModulePath, false, context);

                            if (exportedCommands == null) { continue; }

                            CommandTypes exportedCommandTypes;
                            //Skip if module only has class or other types and no commands.
                            if (exportedCommands.TryGetValue(commandName, out exportedCommandTypes))
                            {
                                Exception exception;
                                discoveryTracer.WriteLine("Found in module: {0}", expandedModulePath);
                                Collection<PSModuleInfo> matchingModule = AutoloadSpecifiedModule(expandedModulePath, context,
                                    cmdletInfo != null ? cmdletInfo.Visibility : SessionStateEntryVisibility.Private,
                                        out exception);
                                lastError = exception;
                                if ((matchingModule == null) || (matchingModule.Count == 0))
                                {
                                    string error = StringUtil.Format(DiscoveryExceptions.CouldNotAutoImportMatchingModule, commandName, moduleShortName);
                                    CommandNotFoundException commandNotFound = new CommandNotFoundException(
                                        originalCommandName,
                                        lastError,
                                        "CouldNotAutoloadMatchingModule", error);
                                    throw commandNotFound;
                                }

                                result = LookupCommandInfo(commandName, commandTypes, searchResolutionOptions, commandOrigin, context);
                            }

                            if (result != null)
                            {
                                break;
                            }
                        }

                        // TODO: this causes AppVeyor builds to fail due to invalid XML being output
#if !CORECLR
                        // Close the progress pane that may have popped up from analyzing UNC paths.
                        if (context.CurrentCommandProcessor != null)
                        {
                            ProgressRecord analysisProgress = new ProgressRecord(0, Modules.ScriptAnalysisPreparing, " ");
                            analysisProgress.RecordType = ProgressRecordType.Completed;
                            context.CurrentCommandProcessor.CommandRuntime.WriteProgress(analysisProgress);
                        }
#endif
                    }
                }
            }
            catch (CommandNotFoundException) { throw; }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
            }
            finally
            {
                context.CommandDiscovery.UnregisterLookupCommandInfoAction("ActiveModuleSearch", commandName);
                if (cleanupModuleAnalysisAppDomain)
                {
                    context.ReleaseResponsibilityForModuleAnalysisAppDomain();
                }
            }

            if (etwEnabled) CommandDiscoveryEventSource.Log.ModuleAutoDiscoveryStop(commandName);

            return result;
        }

        private static CommandInfo TryModuleAutoLoading(string commandName, ExecutionContext context, string originalCommandName, CommandOrigin commandOrigin, ref Exception lastError)
        {
            CommandInfo result = null;

            // If commandName was module-qualified. In that case, we should load the module.
            var colonOrBackslash = commandName.IndexOfAny(Utils.Separators.ColonOrBackslash);

            // If we don't see '\', there is no module specified, so no module to load.
            // If we see ':' before '\', then we probably have a drive qualified path, not a module name
            if (colonOrBackslash == -1 || commandName[colonOrBackslash] == ':')
                return null;

            string moduleCommandName = commandName.Substring(colonOrBackslash + 1, commandName.Length - colonOrBackslash - 1);
            string moduleName;

            // Now we check if there exists the second '\'
            var secondBackslash = moduleCommandName.IndexOfAny(Utils.Separators.Backslash);
            if (secondBackslash == -1)
            {
                moduleName = commandName.Substring(0, colonOrBackslash);
            }
            else
            {
                string versionString = moduleCommandName.Substring(0, secondBackslash);
                // The second '\' could be version specified. eg: "Microsoft.PowerShell.Archive\1.0.0.0\Compress-Archive", we need to support this scenario
                Version version;
                if (Version.TryParse(versionString, out version))
                {
                    moduleCommandName = moduleCommandName.Substring(secondBackslash + 1, moduleCommandName.Length - secondBackslash - 1);
                    moduleName = commandName.Substring(0, colonOrBackslash) + "\\" + versionString + "\\" + commandName.Substring(0, colonOrBackslash) + ".psd1";
                }
                else
                {
                    moduleName = commandName.Substring(0, colonOrBackslash);
                }
            }

            if (String.IsNullOrEmpty(moduleName) || String.IsNullOrEmpty(moduleCommandName) || moduleName.EndsWith(".", StringComparison.Ordinal))
                return null;

            bool etwEnabled = CommandDiscoveryEventSource.Log.IsEnabled();
            if (etwEnabled) CommandDiscoveryEventSource.Log.ModuleAutoLoadingStart(commandName);

            try
            {
                discoveryTracer.WriteLine("Executing module-qualified search: {0}", commandName);
                context.CommandDiscovery.RegisterLookupCommandInfoAction("ActiveModuleSearch", commandName);

                // Verify that auto-loading is only done on for internal commands if it's not public 
                CmdletInfo cmdletInfo = context.SessionState.InvokeCommand.GetCmdlet("Microsoft.PowerShell.Core\\Import-Module");
                if ((commandOrigin == CommandOrigin.Internal) ||
                    ((cmdletInfo != null) && (cmdletInfo.Visibility == SessionStateEntryVisibility.Public)))
                {
                    List<PSModuleInfo> existingModule = context.Modules.GetModules(new string[] { moduleName }, false);
                    PSModuleInfo discoveredModule = null;

                    if (existingModule == null || existingModule.Count == 0)
                    {
                        discoveryTracer.WriteLine("Attempting to load module: {0}", moduleName);
                        Exception exception;
                        Collection<PSModuleInfo> importedModule = AutoloadSpecifiedModule(moduleName, context, cmdletInfo.Visibility, out exception);
                        lastError = exception;

                        if ((importedModule == null) || (importedModule.Count == 0))
                        {
                            string error = StringUtil.Format(DiscoveryExceptions.CouldNotAutoImportModule, moduleName);
                            CommandNotFoundException commandNotFound = new CommandNotFoundException(
                                originalCommandName,
                                lastError,
                                "CouldNotAutoLoadModule",
                                error);
                            throw commandNotFound;
                        }

                        discoveredModule = importedModule[0];
                    }
                    else
                    {
                        discoveredModule = existingModule[0];
                    }

                    CommandInfo exportedResult;
                    if (discoveredModule.ExportedCommands.TryGetValue(moduleCommandName, out exportedResult))
                    {
                        // Return the command if we found a module
                        result = exportedResult;
                    }
                }
            }
            catch (CommandNotFoundException) { throw; }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
            }
            finally { context.CommandDiscovery.UnregisterLookupCommandInfoAction("ActiveModuleSearch", commandName); }

            if (etwEnabled) CommandDiscoveryEventSource.Log.ModuleAutoLoadingStop(commandName);
            return result;
        }

        internal void RegisterLookupCommandInfoAction(string currentAction, string command)
        {
            HashSet<string> currentActionSet = null;
            switch (currentAction)
            {
                case "ActivePreLookup": currentActionSet = _activePreLookup; break;
                case "ActiveModuleSearch": currentActionSet = _activeModuleSearch; break;
                case "ActiveCommandNotFound": currentActionSet = _activeCommandNotFound; break;
                case "ActivePostCommand": currentActionSet = _activePostCommand; break;
            }

            if (currentActionSet.Contains(command))
                throw new InvalidOperationException();
            else
                currentActionSet.Add(command);
        }

        internal void UnregisterLookupCommandInfoAction(string currentAction, string command)
        {
            HashSet<string> currentActionSet = null;
            switch (currentAction)
            {
                case "ActivePreLookup": currentActionSet = _activePreLookup; break;
                case "ActiveModuleSearch": currentActionSet = _activeModuleSearch; break;
                case "ActiveCommandNotFound": currentActionSet = _activeCommandNotFound; break;
                case "ActivePostCommand": currentActionSet = _activePostCommand; break;
            }

            if (currentActionSet.Contains(command))
                currentActionSet.Remove(command);
        }

        private HashSet<string> _activePreLookup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _activeModuleSearch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _activeCommandNotFound = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _activePostCommand = new HashSet<string>(StringComparer.OrdinalIgnoreCase);


        /// <summary>
        /// Gets a CommandPathSearch constructed with the specified patterns and
        /// using the PATH as the lookup directories
        /// </summary>
        /// 
        /// <param name="patterns">
        /// The patterns to search for. These patterns must be in the form taken
        /// by DirectoryInfo.GetFiles().
        /// </param>
        /// 
        /// <returns>
        /// An instance of CommandPathSearch that is initialized with the specified
        /// patterns and using the PATH as the lookup directories.
        /// </returns>  
        internal IEnumerable<string> GetCommandPathSearcher(IEnumerable<string> patterns)
        {
            // Get the PATH environment variable
            IEnumerable<string> lookupPathArray = GetLookupDirectoryPaths();

            // Construct the CommandPathSearch object and return it.
            return new CommandPathSearch(patterns, lookupPathArray, Context);
        } // GetCommandPathSearcher

        /// <summary>
        /// Gets the resolved paths contained in the PATH environment
        /// variable.
        /// </summary>
        /// 
        /// <returns>
        /// The contents of the PATH environment variable split on System.IO.Path.PathSeparator.
        /// </returns>
        /// 
        /// <remarks>
        /// The result is an ordered list of paths with paths starting with "." unresolved until lookup time.
        /// </remarks>
        /// 
        internal IEnumerable<string> GetLookupDirectoryPaths()
        {
            LookupPathCollection result = new LookupPathCollection();

            string path = Environment.GetEnvironmentVariable("PATH");

            discoveryTracer.WriteLine(
                "PATH: {0}",
                path);

            bool isPathCacheValid =
                path != null &&
                String.Equals(_pathCacheKey, path, StringComparison.OrdinalIgnoreCase) &&
                _cachedPath != null;

            if (!isPathCacheValid)
            {
                // Reset the cached lookup paths
                _cachedLookupPaths = null;

                // Tokenize the path and cache it

                _pathCacheKey = path;

                if (_pathCacheKey != null)
                {
                    string[] tokenizedPath = _pathCacheKey.Split(Utils.Separators.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
                    _cachedPath = new Collection<string>();

                    foreach (string directory in tokenizedPath)
                    {
                        string tempDir = directory.TrimStart();
                        _cachedPath.Add(tempDir);
                        result.Add(tempDir);
                    }
                }
            }
            else
            {
                result.AddRange(_cachedPath);
            }

            // Cache the new lookup paths
            return _cachedLookupPaths ?? (_cachedLookupPaths = result);
        } // GetLookupDirectoryPaths

        /// <summary>
        /// The cached list of lookup paths. It can be invalidated by
        /// the PATH changing.
        /// </summary>
        private LookupPathCollection _cachedLookupPaths;

        /// <summary>
        /// The key that determines if the cached PATH can be used.
        /// </summary>
        private string _pathCacheKey;

        /// <summary>
        /// The cache of the tokenized PATH directories
        /// </summary>
        private Collection<string> _cachedPath;

        #endregion internal members

        #region environment variable helpers

        /// <summary>
        /// Gets the PATHEXT environment variable extensions and tokenizes them.
        /// </summary>
        /// 
        internal static string[] PathExtensionsWithPs1Prepended
        {
            get
            {
                var pathExt = Environment.GetEnvironmentVariable("PATHEXT");

                if (!string.Equals(pathExt, s_pathExtCacheKey, StringComparison.OrdinalIgnoreCase) ||
                    s_cachedPathExtCollection == null)
                {
                    InitPathExtCache(pathExt);
                }

                return s_cachedPathExtCollectionWithPs1;
            } // get
        } // PathExtensions

        /// <summary>
        /// Gets the PATHEXT environment variable extensions and tokenizes them.
        /// </summary>
        /// 
        internal static string[] PathExtensions
        {
            get
            {
                var pathExt = Environment.GetEnvironmentVariable("PATHEXT");

                if (!string.Equals(pathExt, s_pathExtCacheKey, StringComparison.OrdinalIgnoreCase) ||
                    s_cachedPathExtCollection == null)
                {
                    InitPathExtCache(pathExt);
                }

                return s_cachedPathExtCollection;
            } // get
        } // PathExtensions

        private static void InitPathExtCache(string pathExt)
        {
            lock (s_lockObject)
            {
                s_cachedPathExtCollection = pathExt != null
                    ? pathExt.Split(Utils.Separators.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                    : Utils.EmptyArray<string>();
                s_cachedPathExtCollectionWithPs1 = new string[s_cachedPathExtCollection.Length + 1];
                s_cachedPathExtCollectionWithPs1[0] = StringLiterals.PowerShellScriptFileExtension;
                Array.Copy(s_cachedPathExtCollection, 0, s_cachedPathExtCollectionWithPs1, 1, s_cachedPathExtCollection.Length);

                s_pathExtCacheKey = pathExt;
            }
        }

        #endregion environment variable helpers

        #region private members

        private static object s_lockObject = new object();
        private static string s_pathExtCacheKey;
        private static string[] s_cachedPathExtCollection;
        private static string[] s_cachedPathExtCollectionWithPs1;


        /// <summary>
        /// Gets the cmdlet information for the specified name.
        /// </summary>
        /// 
        /// <param name="cmdletName">
        /// The name of the cmdlet to return the information for.
        /// </param>
        /// <param name="searchAllScopes">
        /// True if we should search all scopes, false if we should stop after finding the first.
        /// </param>
        /// 
        /// <returns>
        /// The CmdletInfo for the cmdlet for all the cmdlets with the specified name.
        /// </returns>
        /// 
        /// <exception cref="ArgumentException">
        /// If <paramref name="cmdletName"/> is null or empty.
        /// </exception>
        /// 
        internal IEnumerator<CmdletInfo> GetCmdletInfo(string cmdletName, bool searchAllScopes)
        {
            Dbg.Assert(!String.IsNullOrEmpty(cmdletName), "Caller should verify the cmdletName");

            PSSnapinQualifiedName commandName = PSSnapinQualifiedName.GetInstance(cmdletName);

            if (commandName == null)
            {
                yield break;
            }

            // Check the current cmdlet cache then check the top level
            // if we aren't already at the top level.

            SessionStateScopeEnumerator scopeEnumerator =
                new SessionStateScopeEnumerator(Context.EngineSessionState.CurrentScope);

            foreach (SessionStateScope scope in scopeEnumerator)
            {
                List<CmdletInfo> cmdlets;
                if (!scope.CmdletTable.TryGetValue(commandName.ShortName, out cmdlets))
                {
                    continue;
                }

                foreach (var cmdletInfo in cmdlets)
                {
                    if (!String.IsNullOrEmpty(commandName.PSSnapInName))
                    {
                        if (String.Equals(cmdletInfo.ModuleName, commandName.PSSnapInName, StringComparison.OrdinalIgnoreCase))
                        {
                            yield return cmdletInfo;
                            if (!searchAllScopes)
                            {
                                yield break;
                            }
                        }
                        // The engine cmdlets get imported (via Import-Module) once when PowerShell starts and the cmdletInfo is added to PSSnapinHelpers._cmdletcache(static) with ModuleName 
                        // as "System.Management.Automation.dll" instead of the actual snapin name. The next time we load something in an InitialSessionState, we look at this _cmdletcache and
                        // if the the assembly is already loaded, we just return the cmdlets back. So, the CmdletInfo has moduleName has "System.Management.Automation.dll". So, when M3P Activity
                        // tries to access Microsoft.PowerShell.Core\\Get-Command, it cannot. So, adding an additional check to return the correct cmdletInfo for cmdlets from core modules.
                        else if (InitialSessionState.IsEngineModule(cmdletInfo.ModuleName))
                        {
                            if (String.Equals(
                                cmdletInfo.ModuleName,
                                InitialSessionState.GetNestedModuleDllName(commandName.PSSnapInName),
                                StringComparison.OrdinalIgnoreCase))
                            {
                                yield return cmdletInfo;
                                if (!searchAllScopes)
                                {
                                    yield break;
                                }
                            }
                        }
                    }
                    else
                    {
                        yield return cmdletInfo;
                        if (!searchAllScopes)
                        {
                            yield break;
                        }
                    }
                }
            }
        } // GetCmdletInfo

        private bool _cmdletCacheInitialized = false;

        /// <summary>
        /// Called by the RunspaceConfiguration when a PSSnapIn gets added to the
        /// console to update the list of available cmdlets.
        /// </summary>
        /// 
        internal void UpdateCmdletCache()
        {
            if (!_cmdletCacheInitialized)
            {
                foreach (CmdletConfigurationEntry entry in Context.RunspaceConfiguration.Cmdlets)
                {
                    AddCmdletToCache(entry);
                }

                _cmdletCacheInitialized = true;

                return;
            }

            foreach (CmdletConfigurationEntry entry in Context.RunspaceConfiguration.Cmdlets.UpdateList)
            {
                if (entry == null)
                {
                    continue;
                }

                switch (entry.Action)
                {
                    case UpdateAction.Add:
                        AddCmdletToCache(entry);
                        break;

                    case UpdateAction.Remove:
                        RemoveCmdletFromCache(entry);
                        break;

                    default:
                        break;
                }
            }
        } // UpdateCmdletCache


        /// <summary>
        /// Removes a cmdlet from the cmdlet cache.
        /// </summary>
        /// 
        /// <param name="entry">
        /// The configuration entry for the cmdlet which is being removed.
        /// </param>
        /// 
        private void RemoveCmdletFromCache(CmdletConfigurationEntry entry)
        {
            IDictionary<string, List<CmdletInfo>> cmdletTable = Context.EngineSessionState.GetCmdletTable();
            List<CmdletInfo> cacheEntry;
            if (cmdletTable.TryGetValue(entry.Name, out cacheEntry))
            {
                int removalIndex = GetCmdletRemovalIndex(cacheEntry, entry.PSSnapIn == null ? String.Empty : entry.PSSnapIn.Name);

                if (removalIndex >= 0)
                {
                    string name = cacheEntry[removalIndex].Name;
                    cacheEntry.RemoveAt(removalIndex);
                    Context.EngineSessionState.RemoveCmdlet(name, removalIndex, true);
                }

                // Remove the entry from the cache if there are no more cmdlets
                if (cacheEntry.Count == 0)
                {
                    Context.EngineSessionState.RemoveCmdletEntry(entry.Name, true);
                }
            }
        }

        private int GetCmdletRemovalIndex(List<CmdletInfo> cacheEntry, string PSSnapin)
        {
            int removalIndex = -1;
            for (int index = 0; index < cacheEntry.Count; ++index)
            {
                if (String.Equals(cacheEntry[index].ModuleName, PSSnapin, StringComparison.OrdinalIgnoreCase))
                {
                    removalIndex = index;
                    break;
                }
            }
            return removalIndex;
        }


        /// <summary>
        /// Gets the cached ScriptInfo for a command using the script name.
        /// </summary>
        /// 
        /// <param name="name">
        /// The name of the script.
        /// </param>
        /// 
        /// <returns>
        /// A reference to the ScriptInfo for the command if its in the cache, 
        /// or null otherwise.
        /// </returns>
        /// 
        internal ScriptInfo GetScriptInfo(string name)
        {
            Dbg.Assert(
                !String.IsNullOrEmpty(name),
                "The caller should verify the name");

            ScriptInfo result;
            _cachedScriptInfo.TryGetValue(name, out result);
            return result;
        } // GetScriptInfo

        /// <summary>
        /// Gets the script cache
        /// </summary>
        /// 
        internal Dictionary<string, ScriptInfo> ScriptCache
        {
            get { return _cachedScriptInfo; }
        }

        /// <summary>
        /// The cache for the ScriptInfo.
        /// </summary>
        /// 
        private Dictionary<string, ScriptInfo> _cachedScriptInfo;

        internal ExecutionContext Context { get; }

        /// <summary>
        /// Reads the path for the appropriate shellID from the registry.
        /// </summary>
        /// 
        /// <param name="shellID">
        /// The ID of the shell to retrieve the path for.
        /// </param>
        /// 
        /// <returns>
        /// The path to the shell represented by the shellID.
        /// </returns>
        /// 
        /// <remarks>
        /// The shellID must be registered in the Windows Registry in either
        /// the HKEY_CURRENT_USER or HKEY_LOCAL_MACHINE hive under 
        /// Software/Microsoft/MSH/&lt;ShellID&gt; and are searched in that order.
        /// </remarks>
        /// 
        internal static string GetShellPathFromRegistry(string shellID)
        {
            string result = null;

#if !UNIX
            try
            {
                RegistryKey shellKey = Registry.LocalMachine.OpenSubKey(Utils.GetRegistryConfigurationPath(shellID));
                if (shellKey != null)
                {
                    // verify the value kind as a string
                    RegistryValueKind kind = shellKey.GetValueKind("path");

                    if (kind == RegistryValueKind.ExpandString ||
                        kind == RegistryValueKind.String)
                    {
                        result = shellKey.GetValue("path") as string;
                    }
                }
            }
            // Ignore these exceptions and return an empty or null result
            catch (SecurityException)
            {
            }
            catch (IOException)
            {
            }
            catch (ArgumentException)
            {
            }
#endif

            return result;
        }

        internal static PSModuleAutoLoadingPreference GetCommandDiscoveryPreference(ExecutionContext context, VariablePath variablePath, string environmentVariable)
        {
            Dbg.Assert(context != null, "context cannot be Null");
            Dbg.Assert(variablePath != null, "variablePath must be non empty");
            Dbg.Assert(!string.IsNullOrEmpty(environmentVariable), "environmentVariable must be non empty");

            if (context == null)
            {
                throw PSTraceSource.NewArgumentNullException("context");
            }

            // check the PSVariable 
            object result = context.GetVariableValue(variablePath);

            try
            {
                if (result != null)
                {
                    return LanguagePrimitives.ConvertTo<PSModuleAutoLoadingPreference>(result);
                }

                // check the environment variable
                String psEnvironmentVariable = Environment.GetEnvironmentVariable(environmentVariable);
                if (!String.IsNullOrEmpty(psEnvironmentVariable))
                {
                    return LanguagePrimitives.ConvertTo<PSModuleAutoLoadingPreference>(psEnvironmentVariable);
                }
            }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);

                return PSModuleAutoLoadingPreference.All;
            }

            return PSModuleAutoLoadingPreference.All;
        }

        #endregion
    }

    /// <summary>
    /// A helper collection of strings that doesn't allow duplicate strings. Comparison
    /// is case-insensitive and done in the invariant culture.
    /// </summary>
    internal class LookupPathCollection : Collection<string>
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        internal LookupPathCollection() : base() { }

        /// <summary>
        /// Constructs a LookupPathCollection object and adds all the items
        /// in the supplied collection to it.
        /// </summary>
        /// 
        /// <param name="collection">
        /// A set of items to be added to the collection.
        /// </param>
        /// 
        internal LookupPathCollection(IEnumerable<string> collection) : base()
        {
            foreach (string item in collection)
            {
                Add(item);
            }
        }

        /// <summary>
        /// Adds the specified string to the collection if its not already
        /// a member of the collection.
        /// </summary>
        /// 
        /// <param name="item">
        /// The string to add to the collection.
        /// </param>
        /// 
        /// <returns>
        /// The index at which the string was added or -1 if it was not added.
        /// </returns>
        /// 
        public new int Add(string item)
        {
            int result = -1;
            if (!Contains(item))
            {
                base.Add(item);
                result = base.IndexOf(item);
            }
            return result;
        }

        /// <summary>
        /// Adds all the strings in the specified collection to this collection
        /// </summary>
        /// 
        /// <param name="collection">
        /// The collection of strings to add.
        /// </param>
        /// 
        /// <remarks>
        /// Only the strings that are not already in the collection will be added.
        /// </remarks>
        /// 
        internal void AddRange(ICollection<string> collection)
        {
            foreach (string name in collection)
            {
                Add(name);
            }
        }

        /// <summary>
        /// Determines if the string already exists in the collection
        /// using a invariant culture case insensitive comparison.
        /// </summary>
        /// 
        /// <param name="item">
        /// The string to check for existence.
        ///  </param>
        /// 
        /// <returns>
        /// True if the string already exists in the collection.
        /// </returns>
        /// 
        public new bool Contains(string item)
        {
            bool result = false;

            foreach (string name in this)
            {
                if (String.Equals(item, name, StringComparison.OrdinalIgnoreCase))
                {
                    result = true;
                    break;
                }
            }
            return result;
        }

        /// <summary>
        /// Returns a collection of all the indexes that are relative paths.
        /// </summary>
        /// 
        /// <returns>
        /// A collection of all the indexes that are relative paths.
        /// </returns>
        /// 
        internal Collection<int> IndexOfRelativePath()
        {
            Collection<int> result = new Collection<int>();

            for (int index = 0; index < this.Count; ++index)
            {
                string path = this[index];
                if (!String.IsNullOrEmpty(path) &&
                    path.StartsWith(".", StringComparison.CurrentCulture))
                {
                    result.Add(index);
                }
            }
            return result;
        } // IndexOfRelativePath

        /// <summary>
        /// Finds the first index of the specified string. The string
        /// is compared in the invariant culture using a case-insensitive comparison.
        /// </summary>
        /// 
        /// <param name="item">
        /// The string to look for.
        /// </param>
        /// 
        /// <returns>
        /// The index of the string in the collection or -1 if it was not found.
        /// </returns>
        /// 
        /// <exception cref="ArgumentException">
        /// If <paramref name="item"/> is null or empty.
        /// </exception>
        /// 
        public new int IndexOf(string item)
        {
            if (String.IsNullOrEmpty(item))
            {
                throw PSTraceSource.NewArgumentException("item");
            }

            int result = -1;

            for (int index = 0; index < this.Count; ++index)
            {
                if (String.Equals(this[index], item, StringComparison.OrdinalIgnoreCase))
                {
                    result = index;
                    break;
                }
            }
            return result;
        }
    } // LookupPathCollection

    // Guid is {ea9e8155-5042-5537-0b73-8c0e6b53f398}
    [EventSource(Name = "Microsoft-PowerShell-CommandDiscovery")]
    internal class CommandDiscoveryEventSource : EventSource
    {
        internal static CommandDiscoveryEventSource Log = new CommandDiscoveryEventSource();

        public void CommandLookupStart(string CommandName) { WriteEvent(1, CommandName); }
        public void CommandLookupStop(string CommandName) { WriteEvent(2, CommandName); }
        public void ModuleAutoLoadingStart(string CommandName) { WriteEvent(3, CommandName); }
        public void ModuleAutoLoadingStop(string CommandName) { WriteEvent(4, CommandName); }
        public void ModuleAutoDiscoveryStart(string CommandName) { WriteEvent(5, CommandName); }
        public void ModuleAutoDiscoveryStop(string CommandName) { WriteEvent(6, CommandName); }
        public void SearchingForModuleFilesStart() { WriteEvent(7); }
        public void SearchingForModuleFilesStop() { WriteEvent(8); }
        public void GetModuleExportedCommandsStart(string ModulePath) { WriteEvent(9, ModulePath); }
        public void GetModuleExportedCommandsStop(string ModulePath) { WriteEvent(10, ModulePath); }
        public void ModuleManifestAnalysisResult(string ModulePath, bool Success) { WriteEvent(11, ModulePath, Success); }
        public void ModuleManifestAnalysisException(string ModulePath, string Exception) { WriteEvent(12, ModulePath, Exception); }
    }
}


