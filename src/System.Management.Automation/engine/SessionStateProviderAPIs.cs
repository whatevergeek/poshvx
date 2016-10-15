/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation.Provider;
using System.Management.Automation.Runspaces;
using System.Text;
using Dbg = System.Management.Automation;

#pragma warning disable 1634, 1691 // Stops compiler from warning about unknown warnings
#pragma warning disable 56500

namespace System.Management.Automation
{
    /// <summary>
    /// Holds the state of a PowerShell session 
    /// </summary>
    internal sealed partial class SessionStateInternal
    {
        /// <summary>
        /// A collection of the providers. Any provider in this collection can
        /// have drives in any scope in session state.
        /// </summary>
        internal Dictionary<string, List<ProviderInfo>> Providers
        {
            get
            {
                if (this == ExecutionContext.TopLevelSessionState)
                    return _providers;
                return ExecutionContext.TopLevelSessionState.Providers;
            }
        }
        private Dictionary<string, List<ProviderInfo>> _providers =
            new Dictionary<string, List<ProviderInfo>>(
                    SessionStateConstants.DefaultDictionaryCapacity, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Stores the current working drive for each provider. This
        /// allows for retrieving the current working directory for each 
        /// individual provider.
        /// </summary>
        internal Dictionary<ProviderInfo, PSDriveInfo> ProvidersCurrentWorkingDrive
        {
            get
            {
                if (this == ExecutionContext.TopLevelSessionState)
                    return _providersCurrentWorkingDrive;
                return ExecutionContext.TopLevelSessionState.ProvidersCurrentWorkingDrive;
            }
        }
        private Dictionary<ProviderInfo, PSDriveInfo> _providersCurrentWorkingDrive = new Dictionary<ProviderInfo, PSDriveInfo>();

        private bool _providersInitialized = false;

        /// <summary>
        /// Gets called by the RunspaceConfiguration when a PSSnapin gets added or removed.
        /// </summary>
        /// 
        internal void UpdateProviders()
        {
            // This should only be called from Update() on a runspace configuration q.e.d. runspace configuration
            // should never be null when this gets called...
            if (this.ExecutionContext.RunspaceConfiguration == null)
                throw PSTraceSource.NewInvalidOperationException();

            if (this == ExecutionContext.TopLevelSessionState && !_providersInitialized)
            {
                foreach (ProviderConfigurationEntry providerConfig in this.ExecutionContext.RunspaceConfiguration.Providers)
                {
                    AddProvider(providerConfig);
                }

                _providersInitialized = true;
                return;
            }

            foreach (ProviderConfigurationEntry providerConfig in this.ExecutionContext.RunspaceConfiguration.Providers.UpdateList)
            {
                switch (providerConfig.Action)
                {
                    case UpdateAction.Add:
                        AddProvider(providerConfig);
                        break;

                    case UpdateAction.Remove:
                        RemoveProvider(providerConfig);
                        break;

                    default:
                        break;
                }
            }
        }

        /// <summary>
        /// Entrypoint used by to add a provider to the current session state
        /// based on a SessionStateProviderEntry.
        /// </summary>
        /// <param name="providerEntry"></param>
        internal void AddSessionStateEntry(SessionStateProviderEntry providerEntry)
        {
            ProviderInfo provider = AddProvider(providerEntry.ImplementingType,
                            providerEntry.Name,
                            providerEntry.HelpFileName,
                            providerEntry.PSSnapIn,
                            providerEntry.Module
            );
        }

        /// <summary>
        /// Internal method used by RunspaceConfig for updating providers.
        /// </summary>
        /// <param name="providerConfig"></param>
        private ProviderInfo AddProvider(ProviderConfigurationEntry providerConfig)
        {
            return AddProvider(providerConfig.ImplementingType,
                            providerConfig.Name,
                            providerConfig.HelpFileName,
                            providerConfig.PSSnapIn,
                            null
            );
        }

        private ProviderInfo AddProvider(Type implementingType, string name, string helpFileName, PSSnapInInfo psSnapIn, PSModuleInfo module)
        {
            ProviderInfo provider = null;

            try
            {
                provider =
                    new ProviderInfo(
                        new SessionState(this),
                        implementingType,
                        name,
                        helpFileName,
                        psSnapIn);
                provider.SetModule(module);

                NewProvider(provider);

                // Log the provider start event

                MshLog.LogProviderLifecycleEvent(
                    this.ExecutionContext,
                    provider.Name,
                    ProviderState.Started);
            }
            catch (LoopFlowException)
            {
                throw;
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (ActionPreferenceStopException)
            {
                throw;
            }
            catch (SessionStateException sessionStateException)
            {
                if (sessionStateException.GetType() == typeof(SessionStateException))
                {
                    throw;
                }
                else
                {
                    // NTRAID#Windows OS Bugs-1009281-2004/02/11-JeffJon
                    this.ExecutionContext.ReportEngineStartupError(sessionStateException);
                }
            }
            catch (Exception e) // Catch-all OK, 3rd party callout
            {
                CommandProcessorBase.CheckForSevereException(e);

                // NTRAID#Windows OS Bugs-1009281-2004/02/11-JeffJon
                this.ExecutionContext.ReportEngineStartupError(e);
            }
            return provider;
        }


        /// <summary>
        /// Determines the appropriate provider for the drive and then calls the NewDrive
        /// method of that provider.
        /// </summary>
        /// 
        /// <param name="drive">
        /// The drive to have the provider verify.
        /// </param>
        /// 
        /// <param name="context">
        /// The command context under which the drive is being added.
        /// </param>
        /// 
        /// <param name="resolvePathIfPossible">
        /// If true, the drive root will be resolved as an MSH path before verifying with
        /// the provider. If false, the path is assumed to be a provider-internal path.
        /// </param>
        /// 
        /// <returns>
        /// The instance of the drive to be added as approved by the provider.
        /// </returns>
        /// 
        /// <exception cref="NotSupportedException">
        /// If the provider is not a DriveCmdletProvider.
        /// </exception>
        /// 
        /// <exception cref="ProviderNotFoundException">
        /// The provider for the <paramref name="drive"/> could not be found.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If the provider throws an exception while validating the drive.
        /// </exception>
        /// 
        private PSDriveInfo ValidateDriveWithProvider(PSDriveInfo drive, CmdletProviderContext context, bool resolvePathIfPossible)
        {
            Dbg.Diagnostics.Assert(
                drive != null,
                "drive should have been validated by the caller");

            DriveCmdletProvider namespaceProvider =
                GetDriveProviderInstance(drive.Provider);

            return ValidateDriveWithProvider(namespaceProvider, drive, context, resolvePathIfPossible);
        }

        private PSDriveInfo ValidateDriveWithProvider(
            DriveCmdletProvider driveProvider,
            PSDriveInfo drive,
            CmdletProviderContext context,
            bool resolvePathIfPossible)
        {
            Dbg.Diagnostics.Assert(
                drive != null,
                "drive should have been validated by the caller");

            Dbg.Diagnostics.Assert(
                driveProvider != null,
                "driveProvider should have been validated by the caller");

            // Mark the drive as being created so that the provider can modify the
            // root if necessary

            drive.DriveBeingCreated = true;

            // Only try to resolve the root as an MSH path if there is a current drive.

            if (CurrentDrive != null && resolvePathIfPossible)
            {
                string newRoot = GetProviderRootFromSpecifiedRoot(drive.Root, drive.Provider);

                if (newRoot != null)
                {
                    drive.SetRoot(newRoot);
                }
            }

            PSDriveInfo result = null;

            try
            {
                result = driveProvider.NewDrive(drive, context);
            }
            catch (LoopFlowException)
            {
                throw;
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (ActionPreferenceStopException)
            {
                throw;
            }
            catch (Exception e) // Catch-all OK, 3rd party callout
            {
                CommandProcessorBase.CheckForSevereException(e);
                ProviderInvocationException pie =
                    NewProviderInvocationException(
                        "NewDriveProviderException",
                        SessionStateStrings.NewDriveProviderException,
                        driveProvider.ProviderInfo,
                        drive.Root,
                        e);
                context.WriteError(
                    new ErrorRecord(
                        pie.ErrorRecord,
                        pie));
            }
            finally
            {
                drive.DriveBeingCreated = false;
            }
            return result;
        } // ValidateDriveWithProvider

        /// <summary>
        /// Gets an instance of a provider given the provider ID.
        /// </summary>
        /// 
        /// <param name="providerId">
        /// The identifier for the provider to return an instance of.
        /// </param>
        /// 
        /// <returns>
        /// An instance of the specified provider.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="providerId"/> is null.
        /// </exception>
        /// 
        /// <exception cref="ProviderNotFoundException">
        /// If the <paramref name="providerId"/> refers to a provider that doesn't exist or
        /// the name passed matched multiple providers.
        /// </exception>
        /// 
        internal Provider.CmdletProvider GetProviderInstance(string providerId)
        {
            if (providerId == null)
            {
                throw PSTraceSource.NewArgumentNullException("providerId");
            }

            ProviderInfo provider = GetSingleProvider(providerId);

            return GetProviderInstance(provider);
        } // GetProviderInstance

        /// <summary>
        /// Gets an instance of a provider given the provider information.
        /// </summary>
        /// 
        /// <param name="provider">
        /// The provider to return an instance of.
        /// </param>
        /// 
        /// <returns>
        /// An instance of the specified provider.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="provider"/> is null.
        /// </exception>
        /// 
        internal Provider.CmdletProvider GetProviderInstance(ProviderInfo provider)
        {
            if (provider == null)
            {
                throw PSTraceSource.NewArgumentNullException("provider");
            }

            return provider.CreateInstance();
        } // GetProviderInstance

        /// <summary>
        /// Creates an exception for the case where the provider name matched multiple providers.
        /// </summary>
        /// 
        /// <param name="name">
        /// The name of the provider.
        /// </param>
        /// 
        /// <param name="matchingProviders">
        /// The ProviderInfo of the possible matches.
        /// </param>
        /// 
        /// <returns>
        /// An exception representing the error with a message stating which providers are possible matches.
        /// </returns>
        /// 
        internal static ProviderNameAmbiguousException NewAmbiguousProviderName(string name, Collection<ProviderInfo> matchingProviders)
        {
            string possibleMatches = GetPossibleMatches(matchingProviders);

            ProviderNameAmbiguousException e =
                new ProviderNameAmbiguousException(
                    name,
                    "ProviderNameAmbiguous",
                    SessionStateStrings.ProviderNameAmbiguous,
                    matchingProviders,
                    possibleMatches);

            return e;
        }

        private static string GetPossibleMatches(Collection<ProviderInfo> matchingProviders)
        {
            StringBuilder possibleMatches = new StringBuilder();

            foreach (ProviderInfo matchingProvider in matchingProviders)
            {
                possibleMatches.Append(" ");
                possibleMatches.Append(matchingProvider.FullName);
            }

            return possibleMatches.ToString();
        }

        /// <summary>
        /// Gets an instance of an DriveCmdletProvider given the provider ID.
        /// </summary>
        /// 
        /// <param name="providerId">
        /// The provider ID of the provider to get an instance of.
        /// </param>
        /// 
        /// <returns>
        /// An instance of a DriveCmdletProvider for the specified provider ID.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// if <paramref name="providerId"/> is null.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// if the <paramref name="providerId"/> is not for a provider
        /// that is derived from NavigationCmdletProvider.
        /// </exception>
        /// 
        /// <exception cref="ProviderNotFoundException">
        /// If the <paramref name="providerId"/> refers to a provider that doesn't exist.
        /// </exception>
        /// 
        internal DriveCmdletProvider GetDriveProviderInstance(string providerId)
        {
            if (providerId == null)
            {
                throw PSTraceSource.NewArgumentNullException("providerId");
            }

            DriveCmdletProvider driveCmdletProvider =
                GetProviderInstance(providerId) as DriveCmdletProvider;

            if (driveCmdletProvider == null)
            {
                throw
                    PSTraceSource.NewNotSupportedException(SessionStateStrings.DriveCmdletProvider_NotSupported);
            }

            return driveCmdletProvider;
        } // GetDriveProviderInstance

        /// <summary>
        /// Gets an instance of an DriveCmdletProvider given the provider information.
        /// </summary>
        /// 
        /// <param name="provider">
        /// The provider to get an instance of.
        /// </param>
        /// 
        /// <returns>
        /// An instance of a DriveCmdletProvider for the specified provider.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// if <paramref name="provider"/> is null.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// if the <paramref name="provider"/> is not for a provider
        /// that is derived from NavigationCmdletProvider.
        /// </exception>
        /// 
        internal DriveCmdletProvider GetDriveProviderInstance(ProviderInfo provider)
        {
            if (provider == null)
            {
                throw PSTraceSource.NewArgumentNullException("provider");
            }

            DriveCmdletProvider driveCmdletProvider =
                GetProviderInstance(provider) as DriveCmdletProvider;

            if (driveCmdletProvider == null)
            {
                throw
                    PSTraceSource.NewNotSupportedException(SessionStateStrings.DriveCmdletProvider_NotSupported);
            }

            return driveCmdletProvider;
        } // GetDriveProviderInstance

        /// <summary>
        /// Gets an instance of an DriveCmdletProvider given the provider ID.
        /// </summary>
        /// 
        /// <param name="providerInstance">
        /// The instance of the provider to use.
        /// </param>
        /// 
        /// <returns>
        /// An instance of a DriveCmdletProvider for the specified provider ID.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// if <paramref name="providerInstance"/> is null.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// if the <paramref name="providerInstance"/> is not for a provider
        /// that is derived from DriveCmdletProvider.
        /// </exception>
        /// 
        private static DriveCmdletProvider GetDriveProviderInstance(CmdletProvider providerInstance)
        {
            if (providerInstance == null)
            {
                throw PSTraceSource.NewArgumentNullException("providerInstance");
            }

            DriveCmdletProvider driveCmdletProvider =
                providerInstance as DriveCmdletProvider;

            if (driveCmdletProvider == null)
            {
                throw
                    PSTraceSource.NewNotSupportedException(SessionStateStrings.DriveCmdletProvider_NotSupported);
            }

            return driveCmdletProvider;
        } // GetDriveProviderInstance

        /// <summary>
        /// Gets an instance of an ItemCmdletProvider given the provider ID.
        /// </summary>
        /// 
        /// <param name="providerId">
        /// The provider ID of the provider to get an instance of.
        /// </param>
        /// 
        /// <returns>
        /// An instance of a ItemCmdletProvider for the specified provider ID.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// if <paramref name="providerId"/> is null.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// if the <paramref name="providerId"/> is not for a provider
        /// that is derived from NavigationCmdletProvider.
        /// </exception>
        /// 
        /// <exception cref="ProviderNotFoundException">
        /// If the <paramref name="providerId"/> refers to a provider that doesn't exist.
        /// </exception>
        /// 
        internal ItemCmdletProvider GetItemProviderInstance(string providerId)
        {
            if (providerId == null)
            {
                throw PSTraceSource.NewArgumentNullException("providerId");
            }

            ItemCmdletProvider itemCmdletProvider =
                GetProviderInstance(providerId) as ItemCmdletProvider;

            if (itemCmdletProvider == null)
            {
                throw
                    PSTraceSource.NewNotSupportedException(SessionStateStrings.ItemCmdletProvider_NotSupported);
            }

            return itemCmdletProvider;
        } // GetItemProviderInstance

        /// <summary>
        /// Gets an instance of an ItemCmdletProvider given the provider.
        /// </summary>
        /// 
        /// <param name="provider">
        /// The provider to get an instance of.
        /// </param>
        /// 
        /// <returns>
        /// An instance of a ItemCmdletProvider for the specified provider.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// if <paramref name="provider"/> is null.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// if the <paramref name="provider"/> is not for a provider
        /// that is derived from NavigationCmdletProvider.
        /// </exception>
        /// 
        internal ItemCmdletProvider GetItemProviderInstance(ProviderInfo provider)
        {
            if (provider == null)
            {
                throw PSTraceSource.NewArgumentNullException("provider");
            }

            ItemCmdletProvider itemCmdletProvider =
                GetProviderInstance(provider) as ItemCmdletProvider;

            if (itemCmdletProvider == null)
            {
                throw
                    PSTraceSource.NewNotSupportedException(SessionStateStrings.ItemCmdletProvider_NotSupported);
            }

            return itemCmdletProvider;
        } // GetItemProviderInstance

        /// <summary>
        /// Gets an instance of an ItemCmdletProvider given the provider ID.
        /// </summary>
        /// 
        /// <param name="providerInstance">
        /// The instance of the provider to use.
        /// </param>
        /// 
        /// <returns>
        /// An instance of a ItemCmdletProvider for the specified provider ID.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// if <paramref name="providerInstance"/> is null.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// if the <paramref name="providerInstance"/> is not for a provider
        /// that is derived from ItemCmdletProvider.
        /// </exception>
        /// 
        private static ItemCmdletProvider GetItemProviderInstance(CmdletProvider providerInstance)
        {
            if (providerInstance == null)
            {
                throw PSTraceSource.NewArgumentNullException("providerInstance");
            }

            ItemCmdletProvider itemCmdletProvider =
                providerInstance as ItemCmdletProvider;

            if (itemCmdletProvider == null)
            {
                throw
                    PSTraceSource.NewNotSupportedException(SessionStateStrings.ItemCmdletProvider_NotSupported);
            }

            return itemCmdletProvider;
        } // GetItemProviderInstance

        /// <summary>
        /// Gets an instance of an ContainerCmdletProvider given the provider ID.
        /// </summary>
        /// 
        /// <param name="providerId">
        /// The provider ID of the provider to get an instance of.
        /// </param>
        /// 
        /// <returns>
        /// An instance of a ContainerCmdletProvider for the specified provider ID.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// if <paramref name="providerId"/> is null.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// if the <paramref name="providerId"/> is not for a provider
        /// that is derived from NavigationCmdletProvider.
        /// </exception>
        /// 
        /// <exception cref="ProviderNotFoundException">
        /// If the <paramref name="providerId"/> refers to a provider that doesn't exist.
        /// </exception>
        /// 
        internal ContainerCmdletProvider GetContainerProviderInstance(string providerId)
        {
            if (providerId == null)
            {
                throw PSTraceSource.NewArgumentNullException("providerId");
            }

            ContainerCmdletProvider containerCmdletProvider =
                GetProviderInstance(providerId) as ContainerCmdletProvider;

            if (containerCmdletProvider == null)
            {
                throw
                    PSTraceSource.NewNotSupportedException(SessionStateStrings.ContainerCmdletProvider_NotSupported);
            }

            return containerCmdletProvider;
        } // GetContainerProviderInstance

        /// <summary>
        /// Gets an instance of an ContainerCmdletProvider given the provider.
        /// </summary>
        /// 
        /// <param name="provider">
        /// The provider to get an instance of.
        /// </param>
        /// 
        /// <returns>
        /// An instance of a ContainerCmdletProvider for the specified provider.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// if <paramref name="provider"/> is null.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// if the <paramref name="provider"/> is not for a provider
        /// that is derived from NavigationCmdletProvider.
        /// </exception>
        /// 
        internal ContainerCmdletProvider GetContainerProviderInstance(ProviderInfo provider)
        {
            if (provider == null)
            {
                throw PSTraceSource.NewArgumentNullException("provider");
            }

            ContainerCmdletProvider containerCmdletProvider =
                GetProviderInstance(provider) as ContainerCmdletProvider;

            if (containerCmdletProvider == null)
            {
                throw
                    PSTraceSource.NewNotSupportedException(SessionStateStrings.ContainerCmdletProvider_NotSupported);
            }

            return containerCmdletProvider;
        } // GetContainerProviderInstance

        /// <summary>
        /// Gets an instance of an ContainerCmdletProvider given the provider ID.
        /// </summary>
        /// 
        /// <param name="providerInstance">
        /// The instance of the provider to use.
        /// </param>
        /// 
        /// <returns>
        /// An instance of a ContainerCmdletProvider for the specified provider ID.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// if <paramref name="providerInstance"/> is null.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// if the <paramref name="providerInstance"/> is not for a provider
        /// that is derived from ContainerCmdletProvider.
        /// </exception>
        /// 
        private static ContainerCmdletProvider GetContainerProviderInstance(CmdletProvider providerInstance)
        {
            if (providerInstance == null)
            {
                throw PSTraceSource.NewArgumentNullException("providerInstance");
            }

            ContainerCmdletProvider containerCmdletProvider =
                providerInstance as ContainerCmdletProvider;

            if (containerCmdletProvider == null)
            {
                throw
                    PSTraceSource.NewNotSupportedException(SessionStateStrings.ContainerCmdletProvider_NotSupported);
            }

            return containerCmdletProvider;
        } // GetContainerProviderInstance

        /// <summary>
        /// Gets an instance of an NavigationCmdletProvider given the provider.
        /// </summary>
        /// 
        /// <param name="provider">
        /// The provider to get an instance of.
        /// </param>
        /// 
        /// <returns>
        /// An instance of a NavigationCmdletProvider for the specified provider ID.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// if <paramref name="provider"/> is null.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// if the <paramref name="provider"/> is not for a provider
        /// that is derived from NavigationCmdletProvider.
        /// </exception>
        /// 
        internal NavigationCmdletProvider GetNavigationProviderInstance(ProviderInfo provider)
        {
            if (provider == null)
            {
                throw PSTraceSource.NewArgumentNullException("provider");
            }

            NavigationCmdletProvider navigationCmdletProvider =
                GetProviderInstance(provider) as NavigationCmdletProvider;

            if (navigationCmdletProvider == null)
            {
                throw
                    PSTraceSource.NewNotSupportedException(SessionStateStrings.NavigationCmdletProvider_NotSupported);
            }

            return navigationCmdletProvider;
        } // GetNavigationProviderInstance

        /// <summary>
        /// Gets an instance of an NavigationCmdletProvider given the provider ID.
        /// </summary>
        /// 
        /// <param name="providerInstance">
        /// The instance of the provider to use.
        /// </param>
        /// 
        /// <param name="acceptNonContainerProviders">
        /// Specify True if the method should just return the Path if the
        /// provider doesn't support container overloads.
        /// </param>
        /// 
        /// <returns>
        /// An instance of a NavigationCmdletProvider for the specified provider ID.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// if <paramref name="providerInstance"/> is null.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// if the <paramref name="providerInstance"/> is not for a provider
        /// that is derived from NavigationCmdletProvider.
        /// </exception>
        /// 
        private static NavigationCmdletProvider GetNavigationProviderInstance(CmdletProvider providerInstance, bool acceptNonContainerProviders)
        {
            if (providerInstance == null)
            {
                throw PSTraceSource.NewArgumentNullException("providerInstance");
            }

            NavigationCmdletProvider navigationCmdletProvider =
                providerInstance as NavigationCmdletProvider;

            if ((navigationCmdletProvider == null) && (!acceptNonContainerProviders))
            {
                throw
                    PSTraceSource.NewNotSupportedException(SessionStateStrings.NavigationCmdletProvider_NotSupported);
            }

            return navigationCmdletProvider;
        } // GetNavigationProviderInstance

        #region GetProvider

        /// <summary>
        /// Determines if the specified CmdletProvider is loaded.
        /// </summary>
        /// 
        /// <param name="name">
        /// The name of the CmdletProvider.
        /// </param>
        /// 
        /// <returns>
        /// true if the CmdletProvider is loaded, or false otherwise.
        /// </returns>
        /// 
        /// <exception cref="ArgumentException">
        /// If <paramref name="name"/> is null or empty.
        /// </exception>
        /// 
        internal bool IsProviderLoaded(string name)
        {
            bool result = false;

            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }

            // Get the provider from the providers container

            try
            {
                ProviderInfo providerInfo = GetSingleProvider(name);

                result = providerInfo != null;
            }
            catch (ProviderNotFoundException)
            {
            }

            return result;
        } // IsProviderLoaded

        /// <summary>
        /// Gets the provider of the specified name
        /// </summary>
        /// 
        /// <param name="name">
        /// The name of the provider to retrieve
        /// </param>
        /// 
        /// <returns>
        /// The provider of the given name
        /// </returns>
        /// 
        /// <exception cref="ArgumentException">
        /// If <paramref name="name"/> is null or empty.
        /// </exception>
        /// 
        /// <exception cref="ProviderNotFoundException">
        /// The provider with the specified <paramref name="name"/> 
        /// could not be found.
        /// </exception>
        /// 
        internal Collection<ProviderInfo> GetProvider(string name)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }

            PSSnapinQualifiedName providerName = PSSnapinQualifiedName.GetInstance(name);

            if (providerName == null)
            {
                ProviderNotFoundException e =
                 new ProviderNotFoundException(
                     name,
                     SessionStateCategory.CmdletProvider,
                     "ProviderNotFoundBadFormat",
                     SessionStateStrings.ProviderNotFoundBadFormat);

                throw e;
            }
            return GetProvider(providerName);
        }

        /// <summary>
        /// Gets the provider of the specified name
        /// </summary>
        /// 
        /// <param name="name">
        /// The name of the provider to retrieve
        /// </param>
        /// 
        /// <returns>
        /// The provider of the given name
        /// </returns>
        /// 
        /// <exception cref="ArgumentException">
        /// If <paramref name="name"/> is null or empty.
        /// </exception>
        /// 
        /// <exception cref="ProviderNotFoundException">
        /// The provider with the specified <paramref name="name"/> 
        /// could not be found or the name was ambiguous.
        /// If the name is ambiguous then the PSSnapin qualified name must
        /// be specified.
        /// </exception>
        /// 
        internal ProviderInfo GetSingleProvider(string name)
        {
            Collection<ProviderInfo> matchingProviders = GetProvider(name);

            if (matchingProviders.Count != 1)
            {
                if (matchingProviders.Count == 0)
                {
                    ProviderNotFoundException e =
                        new ProviderNotFoundException(
                            name,
                            SessionStateCategory.CmdletProvider,
                            "ProviderNotFound",
                            SessionStateStrings.ProviderNotFound);

                    throw e;
                }
                else
                {
                    throw NewAmbiguousProviderName(name, matchingProviders);
                }
            }
            return matchingProviders[0];
        }

        internal Collection<ProviderInfo> GetProvider(PSSnapinQualifiedName providerName)
        {
            Collection<ProviderInfo> result = new Collection<ProviderInfo>();

            if (providerName == null)
            {
                ProviderNotFoundException e =
                    new ProviderNotFoundException(
                        providerName.ToString(),
                        SessionStateCategory.CmdletProvider,
                        "ProviderNotFound",
                        SessionStateStrings.ProviderNotFound);

                throw e;
            }

            // Get the provider from the providers container

            List<ProviderInfo> matchingProviders = null;

            if (!Providers.TryGetValue(providerName.ShortName, out matchingProviders))
            {
                // If the provider was not found, we may need to auto-mount it.
                SessionStateInternal.MountDefaultDrive(providerName.ShortName, ExecutionContext);

                if (!Providers.TryGetValue(providerName.ShortName, out matchingProviders))
                {
                    ProviderNotFoundException e =
                        new ProviderNotFoundException(
                            providerName.ToString(),
                            SessionStateCategory.CmdletProvider,
                            "ProviderNotFound",
                            SessionStateStrings.ProviderNotFound);

                    throw e;
                }
            }

            if (ExecutionContext.IsSingleShell && !String.IsNullOrEmpty(providerName.PSSnapInName))
            {
                // Be sure the PSSnapin/Module name matches

                foreach (ProviderInfo provider in matchingProviders)
                {
                    if (String.Equals(
                            provider.PSSnapInName,
                            providerName.PSSnapInName,
                           StringComparison.OrdinalIgnoreCase) ||
                        String.Equals(
                            provider.ModuleName,
                            providerName.PSSnapInName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(provider);
                    }
                }
            }
            else
            {
                foreach (ProviderInfo provider in matchingProviders)
                {
                    result.Add(provider);
                }
            }
            return result;
        } // GetProvider

        /// <summary>
        /// Gets all the CoreCommandProviders
        /// </summary>
        ///
        internal IEnumerable<ProviderInfo> ProviderList
        {
            get
            {
                Collection<ProviderInfo> result = new Collection<ProviderInfo>();

                foreach (List<ProviderInfo> providerValues in Providers.Values)
                {
                    foreach (ProviderInfo provider in providerValues)
                    {
                        result.Add(provider);
                    }
                }
                return result;
            } // get
        } // Providers

        /// <summary>
        /// Copy the Providers from another session state instance...
        /// </summary>
        /// <param name="ss">the session state instance to copy from...</param>
        internal void CopyProviders(SessionStateInternal ss)
        {
            if (ss == null || ss.Providers == null)
                return;

            // private Dictionary<string, List<ProviderInfo>> providers;
            _providers = new Dictionary<string, List<ProviderInfo>>();

            foreach (KeyValuePair<string, List<ProviderInfo>> e in ss._providers)
            {
                _providers.Add(e.Key, e.Value);
            }
        }

        #endregion GetProvider

        #region NewProvider

        /// <summary>
        /// Initializes a provider by loading the assembly, creating an instance of the
        /// provider, calling its start method followed by the InitializeDefaultDrives method. The
        /// Drives that are returned from the InitializeDefaultDrives method are then mounted.
        /// </summary>
        /// 
        /// <param name="providerInstance">
        /// An instance of the provider to use for the initialization.
        /// </param>
        /// 
        /// <param name="provider">
        /// The provider to be initialized.
        /// </param>
        /// 
        /// <param name="context">
        /// The context under which the initialization is occurring. If this parameter is not
        /// null, errors will be written to the WriteError method of the context.
        /// </param>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="provider"/> or <paramref name="context"/> is null.
        /// </exception>
        /// 
        /// <exception cref="NotSupportedException">
        /// If the provider is not a DriveCmdletProvider.
        /// </exception>
        /// 
        /// <exception cref="SessionStateException">
        /// If a drive already exists for the name of one of the drives the
        /// provider tries to add.
        /// </exception>
        /// 
        /// <exception cref="SessionStateOverflowException">
        /// If the provider tries to add default drives which exceed the maximum
        /// limit for the number of drives in the current scope.
        /// </exception>
        /// 
        internal void InitializeProvider(
            Provider.CmdletProvider providerInstance,
            ProviderInfo provider,
            CmdletProviderContext context)
        {
            if (provider == null)
            {
                throw PSTraceSource.NewArgumentNullException("provider");
            }

            if (context == null)
            {
                context = new CmdletProviderContext(this.ExecutionContext);
            }

            // Initialize the provider so that it can add any drives
            // that it needs.

            List<PSDriveInfo> newDrives = new List<PSDriveInfo>();
            DriveCmdletProvider driveProvider =
                GetDriveProviderInstance(providerInstance);

            if (driveProvider != null)
            {
                try
                {
                    Collection<PSDriveInfo> drives = driveProvider.InitializeDefaultDrives(context);
                    if (drives != null && drives.Count > 0)
                    {
                        newDrives.AddRange(drives);
                        ProvidersCurrentWorkingDrive[provider] = drives[0];
                    }
                }
                catch (LoopFlowException)
                {
                    throw;
                }
                catch (PipelineStoppedException)
                {
                    throw;
                }
                catch (ActionPreferenceStopException)
                {
                    throw;
                }
                catch (Exception e) // Catch-all OK, 3rd party callout
                {
                    CommandProcessorBase.CheckForSevereException(e);
                    ProviderInvocationException providerException =
                        NewProviderInvocationException(
                            "InitializeDefaultDrivesException",
                            SessionStateStrings.InitializeDefaultDrivesException,
                            provider,
                            String.Empty,
                            e);

                    context.WriteError(
                        new ErrorRecord(
                            providerException,
                            "InitializeDefaultDrivesException",
                            ErrorCategory.InvalidOperation,
                            provider));
                }
            }

            if (newDrives != null && newDrives.Count > 0)
            {
                // Add the drives. 

                foreach (PSDriveInfo newDrive in newDrives)
                {
                    if (newDrive == null)
                    {
                        continue;
                    }

                    // Only mount drives for the current provider

                    if (!provider.NameEquals(newDrive.Provider.FullName))
                    {
                        continue;
                    }

                    try
                    {
                        PSDriveInfo validatedNewDrive = ValidateDriveWithProvider(driveProvider, newDrive, context, false);

                        if (validatedNewDrive != null)
                        {
                            // Since providers are global then the drives created
                            // through InitializeDefaultDrives should also be global.

                            GlobalScope.NewDrive(validatedNewDrive);
                        }
                    }
                    catch (SessionStateException exception)
                    {
                        context.WriteError(exception.ErrorRecord);
                    }
                } // foreach (drive in newDrives)
            }
        } // InitializeProvider



        /// <summary>
        /// Creates and adds a provider to the provider container 
        /// </summary>
        /// 
        /// <param name="provider">
        /// The provider to add.
        /// </param>
        /// 
        /// <returns>
        /// The provider that was added or null if the provider failed to be added.
        /// </returns>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="provider"/> is null.
        /// </exception>
        /// 
        /// <exception cref="SessionStateException">
        /// If the provider already exists.
        /// </exception>
        /// 
        /// <exception cref="ProviderInvocationException">
        /// If there was a failure to load the provider or the provider
        /// threw an exception.
        /// </exception>
        /// 
        internal ProviderInfo NewProvider(ProviderInfo provider)
        {
            if (provider == null)
            {
                throw PSTraceSource.NewArgumentNullException("provider");
            }

            // Check to see if the provider already exists.
            // We do the check instead of allowing the hashtable to 
            // throw the exception so that we give a better error
            // message.
            ProviderInfo existingProvider = ProviderExists(provider);
            if (existingProvider != null)
            {
                // If it's an already loaded provider, don't return an error...
                if (existingProvider.ImplementingType == provider.ImplementingType)
                    return existingProvider;

                SessionStateException sessionStateException =
                    new SessionStateException(
                        provider.Name,
                        SessionStateCategory.CmdletProvider,
                        "CmdletProviderAlreadyExists",
                        SessionStateStrings.CmdletProviderAlreadyExists,
                        ErrorCategory.ResourceExists);

                throw sessionStateException;
            }


            // Make sure we are able to create an instance of the provider.
            // Note, this will also set the friendly name if the user didn't
            // specify one.

            Provider.CmdletProvider providerInstance = provider.CreateInstance();

            // Now call start to let the provider initialize itself
            CmdletProviderContext context = new CmdletProviderContext(this.ExecutionContext);
            ProviderInfo newProviderInfo = null;

            try
            {
                newProviderInfo = providerInstance.Start(provider, context);

                // Set the new provider info in the instance in case the provider
                // derived a new one

                providerInstance.SetProviderInformation(newProviderInfo);
            }
            catch (LoopFlowException)
            {
                throw;
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (ActionPreferenceStopException)
            {
                throw;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception e) // Catch-call OK, 3rd party callout
            {
                CommandProcessorBase.CheckForSevereException(e);
                throw
                    NewProviderInvocationException(
                        "ProviderStartException",
                        SessionStateStrings.ProviderStartException,
                        provider,
                        null,
                        e);
            }

            context.ThrowFirstErrorOrDoNothing(true);

            if (newProviderInfo == null)
            {
                throw
                    PSTraceSource.NewInvalidOperationException(
                        SessionStateStrings.InvalidProviderInfoNull);
            }

            if (newProviderInfo != provider)
            {
                // Since the references are not the same, ensure that the provider
                // name is the same.

                if (!string.Equals(newProviderInfo.Name, provider.Name, StringComparison.OrdinalIgnoreCase))
                {
                    throw
                        PSTraceSource.NewInvalidOperationException(
                            SessionStateStrings.InvalidProviderInfo);
                }

                // Use the new provider info instead
                provider = newProviderInfo;
            }

            // Add the newly create provider to the providers container

            try
            {
                NewProviderEntry(provider);
            }
            catch (ArgumentException)
            {
                SessionStateException sessionStateException =
                    new SessionStateException(
                        provider.Name,
                        SessionStateCategory.CmdletProvider,
                        "CmdletProviderAlreadyExists",
                        SessionStateStrings.CmdletProviderAlreadyExists,
                        ErrorCategory.ResourceExists);

                throw sessionStateException;
            }

            // Add the provider to the provider current working 
            // drive hashtable so that we can associate a current working
            // drive with it.

            ProvidersCurrentWorkingDrive.Add(provider, null);

            bool initializeProviderError = false;
            try
            {
                // Initialize the provider and give it a chance to 
                // mount some drives.

                InitializeProvider(providerInstance, provider, context);
                context.ThrowFirstErrorOrDoNothing(true);
            }
            catch (LoopFlowException)
            {
                throw;
            }
            catch (PipelineStoppedException)
            {
                initializeProviderError = true;
                throw;
            }
            catch (ActionPreferenceStopException)
            {
                initializeProviderError = true;
                throw;
            }
            catch (NotSupportedException)
            {
                // We can safely ignore NotSupportedExceptions because
                // it just means that the provider doesn't support
                // drives.

                initializeProviderError = false;
            }
            catch (SessionStateException)
            {
                initializeProviderError = true;
                throw;
            }
            finally
            {
                if (initializeProviderError)
                {
                    // An exception during initialization should remove the provider from 
                    // session state.

                    Providers.Remove(provider.Name.ToString());
                    ProvidersCurrentWorkingDrive.Remove(provider);
                    provider = null;
                }
            }

#if RELATIONSHIP_SUPPORTED
    // 2004/11/24-JeffJon - Relationships have been removed from the Exchange release

            // Make sure the delay-load relationships get updated for the new provider

            relationships.ProcessDelayLoadRelationships (provider.Name);
#endif

            // Now write out the result

            return provider;
        } // NewProvider

        private ProviderInfo ProviderExists(ProviderInfo provider)
        {
            List<ProviderInfo> matchingProviders = null;

            if (Providers.TryGetValue(provider.Name, out matchingProviders))
            {
                foreach (ProviderInfo possibleMatch in matchingProviders)
                {
                    if (provider.NameEquals(possibleMatch.FullName))
                    {
                        return possibleMatch;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Creates an entry in the providers hashtable for the new provider.
        /// </summary>
        /// 
        /// <param name="provider">
        /// The provider being added.
        /// </param>
        /// 
        /// <exception cref="SessionStateException">
        /// If a provider with the same name and PSSnapIn name already exists.
        /// </exception>
        /// 
        private void NewProviderEntry(ProviderInfo provider)
        {
            bool isDuplicateProvider = false;

            // Add the entry to the list of providers with that name
            if (!Providers.ContainsKey(provider.Name))
            {
                Providers.Add(provider.Name, new List<ProviderInfo>());
            }
            else
            {
                // be sure the same provider from the same PSSnapin doesn't already exist

                List<ProviderInfo> existingProviders = Providers[provider.Name];

                foreach (ProviderInfo existingProvider in existingProviders)
                {
                    //making sure that we are not trying to add the same provider by checking the provider name & type of the new and existing providers.
                    if (string.IsNullOrEmpty(provider.PSSnapInName) && (string.Equals(existingProvider.Name, provider.Name, StringComparison.OrdinalIgnoreCase) &&
                        (existingProvider.GetType().Equals(provider.GetType()))))
                    {
                        isDuplicateProvider = true;
                    }

                    //making sure that we are not trying to add the same provider by checking the PSSnapinName of the new and existing providers.
                    else if (string.Equals(existingProvider.PSSnapInName, provider.PSSnapInName, StringComparison.OrdinalIgnoreCase))
                    {
                        isDuplicateProvider = true;
                    }
                }
            }

            if (!isDuplicateProvider)
            {
                Providers[provider.Name].Add(provider);
            }
        }

        #endregion NewProvider

        #region Remove Provider

        private void RemoveProvider(ProviderConfigurationEntry entry)
        {
            try
            {
                CmdletProviderContext context = new CmdletProviderContext(this.ExecutionContext);

                string providerName = GetProviderName(entry);
                RemoveProvider(providerName, true, context);

                context.ThrowFirstErrorOrDoNothing();
            }
            catch (LoopFlowException)
            {
                throw;
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (ActionPreferenceStopException)
            {
                throw;
            }
            catch (Exception e) // Catch-all OK, 3rd party callout
            {
                CommandProcessorBase.CheckForSevereException(e);

                // NTRAID#Windows OS Bugs-1009281-2004/02/11-JeffJon
                this.ExecutionContext.ReportEngineStartupError(e);
            }
        }

        private string GetProviderName(ProviderConfigurationEntry entry)
        {
            string name = entry.Name;
            if (entry.PSSnapIn != null)
            {
                name =
                    string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0}\\{1}",
                        entry.PSSnapIn.Name,
                        entry.Name);
            }
            return name;
        }

        /// <summary>
        /// Removes the provider of the given name.
        /// </summary>
        /// 
        /// <param name="providerName">
        /// The name of the provider to remove.
        /// </param>
        /// 
        /// <param name="force">
        /// Determines if the provider should be removed forcefully even if there were
        /// drives present or errors.
        /// </param>
        ///
        /// <param name="context">
        /// The context under which the command is being run.
        /// </param>
        /// 
        /// <error cref="ArgumentNullException">
        /// If <paramref name="providerName"/> is null.
        /// </error>
        /// 
        /// <error cref="SessionStateException">
        /// There are still drives associated with this provider,
        /// and the "force" option was not specified.
        /// </error>
        /// 
        /// <error cref="ProviderNotFoundException">
        /// A provider with name <paramref name="providerName"/> could not be found.
        /// </error>
        /// 
        /// <error>
        /// If a provider throws an exception it gets written to the <paramref name="context"/>.
        /// </error>
        /// 
        /// <exception cref="ArgumentException">
        /// If <paramref name="providerName"/> is null or empty.
        /// </exception>
        /// 
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="context"/> is null.
        /// </exception>
        /// 
        /// <remarks>
        /// All drives associated with the provider must be removed before the provider
        /// can be removed. Call SessionState.GetDrivesForProvider() to determine if there
        /// are any drives associated with the provider. A SessionStateException
        /// will be written to the context if any such drives do exist.
        /// </remarks>
        /// 
        internal void RemoveProvider(
            string providerName,
            bool force,
            CmdletProviderContext context)
        {
            if (context == null)
            {
                throw PSTraceSource.NewArgumentNullException("context");
            }

            if (String.IsNullOrEmpty(providerName))
            {
                throw PSTraceSource.NewArgumentException("providerName");
            }

            bool errors = false;

            ProviderInfo provider = null;

            try
            {
                provider = GetSingleProvider(providerName);
            }
            catch (ProviderNotFoundException)
            {
                return;
            }

            try
            {
                // First get an instance of the provider to make sure it exists
                Provider.CmdletProvider providerBase = GetProviderInstance(provider);

                if (providerBase == null)
                {
                    ProviderNotFoundException e = new ProviderNotFoundException(
                        providerName,
                        SessionStateCategory.CmdletProvider,
                        "ProviderNotFound",
                        SessionStateStrings.ProviderNotFound);
                    context.WriteError(new ErrorRecord(e.ErrorRecord, e));

                    errors = true;
                }
                else
                {
                    // See if there are any drives present for the provider

                    int driveCount = 0;
                    foreach (PSDriveInfo drive in GetDrivesForProvider(providerName))
                    {
                        if (drive != null)
                        {
                            ++driveCount;
                            break;
                        }
                    }

                    if (driveCount > 0)
                    {
                        if (force)
                        {
                            // Forcefully remove all the drives

                            foreach (PSDriveInfo drive in GetDrivesForProvider(providerName))
                            {
                                if (drive != null)
                                {
                                    RemoveDrive(drive, true, null);
                                }
                            }
                        }
                        else
                        {
                            errors = true;

                            // Since there are still drives associated with the provider
                            // the provider cannot be removed

                            SessionStateException e = new SessionStateException(
                                providerName,
                                SessionStateCategory.CmdletProvider,
                                "RemoveDrivesBeforeRemovingProvider",
                                SessionStateStrings.RemoveDrivesBeforeRemovingProvider,
                                ErrorCategory.InvalidOperation);
                            context.WriteError(new ErrorRecord(e.ErrorRecord, e));

                            return;
                        }
                    }

                    // Now tell the provider that they are going to be removed by
                    // calling the Stop method

                    try
                    {
                        providerBase.Stop(context);
                    }
                    catch (LoopFlowException)
                    {
                        throw;
                    }
                    catch (PipelineStoppedException)
                    {
                        throw;
                    }
                    catch (ActionPreferenceStopException)
                    {
                        throw;
                    }
                }
            }
            catch (LoopFlowException)
            {
                throw;
            }
            catch (PipelineStoppedException)
            {
                throw;
            }
            catch (ActionPreferenceStopException)
            {
                throw;
            }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                errors = true;
                context.WriteError(
                    new ErrorRecord(
                        e,
                        "RemoveProviderUnexpectedException",
                        ErrorCategory.InvalidArgument,
                        providerName));
            }
            finally
            {
                if (force || !errors)
                {
                    // Log the provider stopped event

                    MshLog.LogProviderLifecycleEvent(
                        this.ExecutionContext,
                        providerName,
                        ProviderState.Stopped);

                    RemoveProviderFromCollection(provider);
                    ProvidersCurrentWorkingDrive.Remove(provider);

#if RELATIONSHIP_SUPPORTED
    // 2004/11/24-JeffJon - Relationships have been removed from the Exchange release

                   // Now make sure no relationship reference this provider
                    relationships.ProcessRelationshipsOnCmdletProviderRemoval (providerName);
#endif
                }
            }
        } // RemoveProvider

        /// <summary>
        /// Removes the provider from the providers dictionary.
        /// </summary>
        /// 
        /// <param name="provider">
        /// The provider to be removed.
        /// </param>
        /// 
        /// <remarks>
        /// If there are multiple providers with the same name, then only the provider
        /// from the matching PSSnapin is removed.
        /// If the last provider of that name is removed the entry is removed from the dictionary.
        /// </remarks>
        /// 
        private void RemoveProviderFromCollection(ProviderInfo provider)
        {
            List<ProviderInfo> matchingProviders;
            if (Providers.TryGetValue(provider.Name, out matchingProviders))
            {
                if (matchingProviders.Count == 1 &&
                    matchingProviders[0].NameEquals(provider.FullName))
                {
                    Providers.Remove(provider.Name);
                }
                else
                {
                    matchingProviders.Remove(provider);
                }
            }
        }
        #endregion RemoveProvider

        /// <summary>
        /// Gets the count of the number of providers that are loaded
        /// </summary>
        /// 
        internal int ProviderCount
        {
            get
            {
                int count = 0;
                foreach (List<ProviderInfo> matchingProviders in Providers.Values)
                {
                    count += matchingProviders.Count;
                }
                return count;
            }
        } // ProviderCount
    } // SessionStateInternal class
}

#pragma warning restore 56500
