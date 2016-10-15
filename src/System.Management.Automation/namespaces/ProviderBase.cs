/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

#pragma warning disable 1634, 1691
#pragma warning disable 56506

using System.Collections.ObjectModel;
using System.Management.Automation.Runspaces;
using System.Management.Automation.Internal;
using System.Management.Automation.Host;
using System.Reflection;
using System.Resources;
using System.Diagnostics.CodeAnalysis; // for fxcop
using System.Security.AccessControl;

namespace System.Management.Automation.Provider
{
    /// <summary>
    /// This interface needs to be implemented by providers that want users to see
    /// provider-specific help.
    /// </summary>
    public interface ICmdletProviderSupportsHelp
    {
        /// <summary>
        /// Called by the help system to get provider-specific help from the provider.
        /// </summary>
        /// <param name="helpItemName">
        /// Name of command that the help is requested for.
        /// </param>
        /// <param name="path">
        /// Full path to the current location of the user or the full path to 
        /// the location of the property that the user needs help about.
        /// </param>
        /// <returns>
        /// The MAML help XML that should be presented to the user.
        /// </returns>
        [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Maml", Justification = "Maml is an acronym.")]
        string GetHelpMaml(string helpItemName, string path);
    }

    #region CmdletProvider

    /// <summary>
    /// The base class for Cmdlet provider.
    /// </summary>
    /// 
    /// <remarks>
    /// Although it is possible to derive from this base class to implement a Cmdlet Provider, in most
    /// cases one should derive from <see cref="System.Management.Automation.Provider.ItemCmdletProvider"/>,
    /// <see cref="System.Management.Automation.Provider.ContainerCmdletProvider"/>, or 
    /// <see cref ="System.Management.Automation.Provider.NavigationCmdletProvider"/>
    /// </remarks>
    public abstract partial class CmdletProvider : IResourceSupplier
    {
        #region private data

        /// <summary>
        /// The context under which the provider is running. This will change between each
        /// invocation of a method in this class or derived classes.
        /// </summary>
        private CmdletProviderContext _contextBase = null;

        /// <summary>
        /// The information that the Monad engine stores on behalf of the provider.
        /// </summary>
        private ProviderInfo _providerInformation = null;

        #endregion private data

        #region internal members

        #region Trace object

        /// <summary>
        /// An instance of the PSTraceSource class used for trace output
        /// using "CmdletProviderClasses" as the category.
        /// </summary>
        [TraceSourceAttribute(
             "CmdletProviderClasses",
             "The namespace provider base classes tracer")]
        internal static PSTraceSource providerBaseTracer = PSTraceSource.GetTracer(
                                                               "CmdletProviderClasses",
                                                               "The namespace provider base classes tracer");

        #endregion Trace object

        /// <summary>
        /// Sets the provider information that is stored in the Monad engine into the
        /// provider base class.
        /// </summary>
        ///
        /// <param name="providerInfoToSet">
        /// The provider information that is stored by the Monad engine.
        /// </param>
        ///
        /// <exception cref="ArgumentNullException">
        /// If <paramref name="providerInformation"/> is null.
        /// </exception>
        /// 
        internal void SetProviderInformation(ProviderInfo providerInfoToSet)
        {
            if (providerInfoToSet == null)
            {
                throw PSTraceSource.NewArgumentNullException("providerInfoToSet");
            }

            _providerInformation = providerInfoToSet;
        } // SetProviderInformation

        /// <summary>
        /// Checks whether the filter of the provider is set. 
        /// Can be overridden by derived class when additional filters are defined.
        /// </summary>
        /// <returns>
        /// Whether the filter of the provider is set. 
        /// </returns>
        internal virtual bool IsFilterSet()
        {
            bool filterSet = !String.IsNullOrEmpty(Filter);
            return filterSet;
        }

        #region CmdletProvider method wrappers

        /// <summary>
        /// Gets or sets the context for the running command.
        /// </summary>
        /// 
        /// <exception cref="NotSupportedException">
        /// On set, if the context contains credentials and the provider
        /// doesn't support credentials, or if the context contains a filter
        /// parameter and the provider does not support filters.
        /// </exception>
        /// 
        internal CmdletProviderContext Context
        {
            get
            {
                return _contextBase;
            }

            set
            {
                if (value == null)
                {
                    throw PSTraceSource.NewArgumentNullException("value");
                }

                // Check that the provider supports the use of credentials
                if (value.Credential != null &&
                    value.Credential != PSCredential.Empty &&
                    !CmdletProviderManagementIntrinsics.CheckProviderCapabilities(ProviderCapabilities.Credentials, _providerInformation))
                {
                    throw PSTraceSource.NewNotSupportedException(
                        SessionStateStrings.Credentials_NotSupported);
                }

                // Supplying Credentials for the FileSystemProvider is supported only for New-PSDrive Command.
                if (_providerInformation != null && !string.IsNullOrEmpty(_providerInformation.Name) && _providerInformation.Name.Equals("FileSystem") &&
                    value.Credential != null &&
                    value.Credential != PSCredential.Empty &&
                    !value.ExecutionContext.CurrentCommandProcessor.Command.GetType().Name.Equals("NewPSDriveCommand"))
                {
                    throw PSTraceSource.NewNotSupportedException(
                        SessionStateStrings.FileSystemProviderCredentials_NotSupported);
                }

                // Check that the provider supports the use of filters
                if ((!String.IsNullOrEmpty(value.Filter)) &&
                    (!CmdletProviderManagementIntrinsics.CheckProviderCapabilities(ProviderCapabilities.Filter, _providerInformation)))
                {
                    throw PSTraceSource.NewNotSupportedException(
                        SessionStateStrings.Filter_NotSupported);
                }

                // Check that the provider supports the use of transactions if the command
                // requested it
                if ((value.UseTransaction) &&
                   (!CmdletProviderManagementIntrinsics.CheckProviderCapabilities(ProviderCapabilities.Transactions, _providerInformation)))
                {
                    throw PSTraceSource.NewNotSupportedException(
                        SessionStateStrings.Transactions_NotSupported);
                }

                _contextBase = value;
                _contextBase.ProviderInstance = this;
            }
        } // Context

        /// <summary>
        /// Called when the provider is first initialized. It sets the context
        /// of the call and then calls the derived providers Start method.
        /// </summary>
        ///
        /// <param name="providerInfo">
        /// The information about the provider.
        /// </param>
        /// 
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        ///
        internal ProviderInfo Start(ProviderInfo providerInfo, CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;
            return Start(providerInfo);
        } // Start

        /// <summary>
        /// Gets an object that defines the additional parameters for the Start implementation
        /// for a provider.
        /// </summary>
        /// 
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        ///
        /// <returns>
        /// An object that has properties and fields decorated with
        /// parsing attributes similar to a cmdlet class.
        /// </returns>
        /// 
        internal object StartDynamicParameters(CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            return StartDynamicParameters();
        } // StartDynamicParameter

        /// <summary>
        /// Called when the provider is being removed. It sets the context
        /// of the call and then calls the derived providers Stop method.
        /// </summary>
        ///
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        ///</param>
        ///
        internal void Stop(CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;
            Stop();
        } // Stop

        /// <Content contentref="System.Management.Automation.Cmdlet.StopProcessing" />
        protected internal virtual void StopProcessing()
        {
        } // StopProcessing

        #endregion CmdletProvider method wrappers

        #region IPropertyCmdletProvider method wrappers

        /// <summary>
        /// Internal wrapper for the GetProperty protected method. This method will
        /// only be called if the provider implements the IPropertyCmdletProvider interface.
        /// </summary>
        /// 
        /// <param name="path">
        /// The path to the item to retrieve properties from.
        /// </param>
        /// 
        /// <param name="providerSpecificPickList">
        /// A list of properties that should be retrieved. If this parameter is null
        /// or empty, all properties should be retrieved.
        /// </param>
        /// 
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        internal void GetProperty(
            string path,
            Collection<string> providerSpecificPickList,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IPropertyCmdletProvider propertyProvider = this as IPropertyCmdletProvider;

            if (propertyProvider == null)
            {
                throw
                    PSTraceSource.NewNotSupportedException(
                        SessionStateStrings.IPropertyCmdletProvider_NotSupported);
            }

            // Call interface method

            propertyProvider.GetProperty(path, providerSpecificPickList);
        } // GetProperty

        /// <summary>
        /// Gives the provider a chance to attach additional parameters to
        /// the get-itemproperty cmdlet.
        /// </summary>
        /// 
        /// <param name="path">
        /// If the path was specified on the command line, this is the path
        /// to the item to get the dynamic parameters for.
        /// </param>
        /// 
        /// <param name="providerSpecificPickList">
        /// A list of properties that should be retrieved. If this parameter is null
        /// or empty, all properties should be retrieved.
        /// </param>
        /// 
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <returns>
        /// An object that has properties and fields decorated with
        /// parsing attributes similar to a cmdlet class.
        /// </returns>
        /// 
        internal object GetPropertyDynamicParameters(
            string path,
            Collection<string> providerSpecificPickList,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IPropertyCmdletProvider propertyProvider = this as IPropertyCmdletProvider;

            if (propertyProvider == null)
            {
                return null;
            }
            return propertyProvider.GetPropertyDynamicParameters(path, providerSpecificPickList);
        } // GetPropertyDynamicParameters

        /// <summary>
        /// Internal wrapper for the SetProperty protected method. This method will
        /// only be called if the provider implements the IPropertyCmdletProvider interface.
        /// </summary>
        /// 
        /// <param name="path">
        /// The path to the item to set the properties on.
        /// </param>
        /// 
        /// <param name="propertyValue">
        /// A PSObject which contains a collection of the name, type, value 
        /// of the properties to be set.
        /// </param>
        /// 
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        internal void SetProperty(
            string path,
            PSObject propertyValue,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IPropertyCmdletProvider propertyProvider = this as IPropertyCmdletProvider;

            if (propertyProvider == null)
            {
                throw
                    PSTraceSource.NewNotSupportedException(
                        SessionStateStrings.IPropertyCmdletProvider_NotSupported);
            }

            // Call interface method

            propertyProvider.SetProperty(path, propertyValue);
        } // SetProperty

        /// <summary>
        /// Gives the provider a chance to attach additional parameters to
        /// the set-itemproperty cmdlet.
        /// </summary>
        /// 
        /// <param name="path">
        /// If the path was specified on the command line, this is the path
        /// to the item to get the dynamic parameters for.
        /// </param>
        /// 
        /// <param name="propertyValue">
        /// A PSObject which contains a collection of the name, type, value 
        /// of the properties to be set.
        /// </param>
        /// 
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <returns>
        /// An object that has properties and fields decorated with
        /// parsing attributes similar to a cmdlet class.
        /// </returns>
        /// 
        internal object SetPropertyDynamicParameters(
            string path,
            PSObject propertyValue,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IPropertyCmdletProvider propertyProvider = this as IPropertyCmdletProvider;

            if (propertyProvider == null)
            {
                return null;
            }
            return propertyProvider.SetPropertyDynamicParameters(path, propertyValue);
        } // SetPropertyDynamicParameters

        /// <summary>
        /// Internal wrapper for the ClearProperty protected method. This method will
        /// only be called if the provider implements the IPropertyCmdletProvider interface.
        /// </summary>
        /// 
        /// <param name="path">
        /// The path to the item from which the property should be cleared.
        /// </param>
        /// 
        /// <param name="propertyName">
        /// The name of the property that should be cleared.
        /// </param>
        /// 
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <remarks>
        /// Implement this method when you are providing access to a data store
        /// that allows dynamic clearing of properties.
        /// </remarks>
        /// 
        internal void ClearProperty(
            string path,
            Collection<string> propertyName,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IPropertyCmdletProvider propertyProvider = this as IPropertyCmdletProvider;

            if (propertyProvider == null)
            {
                throw
                    PSTraceSource.NewNotSupportedException(
                        SessionStateStrings.IPropertyCmdletProvider_NotSupported);
            }

            // Call interface method

            propertyProvider.ClearProperty(path, propertyName);
        } // ClearProperty

        /// <summary>
        /// Gives the provider a chance to attach additional parameters to
        /// the clear-itemproperty cmdlet.
        /// </summary>
        /// 
        /// <param name="path">
        /// If the path was specified on the command line, this is the path
        /// to the item to get the dynamic parameters for.
        /// </param>
        /// 
        /// <param name="providerSpecificPickList">
        /// A list of properties that should be cleared. If this parameter is null
        /// or empty, all properties should be cleared.
        /// </param>
        /// 
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <returns>
        /// An object that has properties and fields decorated with
        /// parsing attributes similar to a cmdlet class.
        /// </returns>
        /// 
        internal object ClearPropertyDynamicParameters(
            string path,
            Collection<string> providerSpecificPickList,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IPropertyCmdletProvider propertyProvider = this as IPropertyCmdletProvider;

            if (propertyProvider == null)
            {
                return null;
            }
            return propertyProvider.ClearPropertyDynamicParameters(path, providerSpecificPickList);
        } // ClearPropertyDynamicParameters

        #endregion IPropertyCmdletProvider

#if SUPPORTS_IMULTIVALUEPROPERTYCMDLETPROVIDER
        #region IMultivaluePropertyCmdletProvider

        /// <summary>
        /// Internal wrapper for the GetPropertyAt protected method. This method will
        /// only be called if the provider implements the IPropertyCmdletProvider interface.
        /// </summary>
        /// 
        /// <param name="path">
        /// The path to the item to retrieve property from.
        /// </param>
        /// 
        /// <param name="propertyName">
        /// The property that should be retrieved.
        /// </param>
        /// 
        /// <param name="index">
        /// The index of the value to be retrieved from the property.
        /// </param>
        ///
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        internal void GetPropertyValueAt(
            string path,
            string propertyName,
            object index,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IMultivaluePropertyCmdletProvider propertyProvider = 
                this as IMultivaluePropertyCmdletProvider;

            if (propertyProvider == null)
            {
                throw
                    providerBaseTracer.NewNotSupportedException(
                        "SessionStateStrings",
                        "IMultivaluePropertyCmdletProvider_NotSupported");
            }

            // Call interface method

            propertyProvider.GetPropertyValueAt(path, propertyName, index);
        } // GetPropertyValueAt

        /// <summary>
        /// Gives the provider a chance to attach additional parameters to
        /// the get-propertyvalue -at cmdlet.
        /// </summary>
        /// 
        /// <param name="path">
        /// If the path was specified on the command line, this is the path
        /// to the item to get the dynamic parameters for.
        /// </param>
        /// 
        /// <param name="propertyName">
        /// The property that should be retrieved.
        /// </param>
        /// 
        /// <param name="at">
        /// The index of the value to be retrieved from the property.
        /// </param>
        ///
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <returns>
        /// An object that has properties and fields decorated with
        /// parsing attributes similar to a cmdlet class.
        /// </returns>
        /// 
        internal object GetPropertyValueAtDynamicParameters(
            string path, 
            string propertyName,
            object at,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IMultivaluePropertyCmdletProvider propertyProvider = this as IMultivaluePropertyCmdletProvider;

            if (propertyProvider == null)
            {
                return null;
            }
            return propertyProvider.GetPropertyValueAtDynamicParameters(path, propertyName, at);
        } // GetPropertyValueAtDynamicParameters
        
        /// <summary>
        /// Sets the specified properties of the item at the specified path at the specified
        /// position if the property is a multivalued property.
        /// </summary>
        /// 
        /// <param name="path">
        /// The path to the item to set the properties on.
        /// </param>
        /// 
        /// <param name="position">
        /// The position to place the new value of the property.
        /// </param>
        ///
        /// <param name="propertyValue">
        /// A PSObject which contains a collection of the name, type, value 
        /// of the properties to be set.
        /// </param>
        /// 
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <returns>
        /// Nothing. An instance of PSObject representing the properties that were set
        /// should be passed to the WriteObject() method.
        /// </returns>
        /// 
        internal void SetPropertyValueAt(
            string path,
            object position,
            PSObject propertyValue,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IMultivaluePropertyCmdletProvider propertyProvider = 
                this as IMultivaluePropertyCmdletProvider;

            if (propertyProvider == null)
            {
                throw
                    providerBaseTracer.NewNotSupportedException(
                        "SessionStateStrings",
                        "IMultivaluePropertyCmdletProvider_NotSupported");
            }

            // Call interface method

            propertyProvider.SetPropertyValueAt(path, position, propertyValue);
        } // SetPropertyValueAt

        /// <summary>
        /// Gives the provider a chance to attach additional parameters to
        /// the set-propertyvalue -at cmdlet.
        /// </summary>
        /// 
        /// <param name="path">
        /// If the path was specified on the command line, this is the path
        /// to the item to get the dynamic parameters for.
        /// </param>
        /// 
        /// <param name="at">
        /// The position to place the new value of the property.
        /// </param>
        ///
        /// <param name="propertyValue">
        /// A PSObject which contains a collection of the name, type, value 
        /// of the properties to be added.
        /// </param>
        /// 
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <returns>
        /// An object that has properties and fields decorated with
        /// parsing attributes similar to a cmdlet class.
        /// </returns>
        /// 
        internal object SetPropertyValueAtDynamicParameters(
            string path, 
            object at,
            PSObject propertyValue,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IMultivaluePropertyCmdletProvider propertyProvider = this as IMultivaluePropertyCmdletProvider;

            if (propertyProvider == null)
            {
                return null;
            }
            return propertyProvider.SetPropertyValueAtDynamicParameters(path, at, propertyValue);
        } // SetPropertyValueAtDynamicParameters

        /// <summary>
        /// Internal wrapper for the ClearPropertyAt protected method. This method will
        /// only be called if the provider implements the 
        /// IMultivaluePropertyCmdletProvider interface.
        /// </summary>
        /// 
        /// <param name="path">
        /// The path to the item from which the property should be cleared.
        /// </param>
        /// 
        /// <param name="propertyName">
        /// The name of the property that should be cleared.
        /// </param>
        /// 
        /// <param name="at">
        /// The position of the value of the property to be cleared.
        /// </param>
        ///
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <remarks>
        /// Implement this method when you are providing access to a data store
        /// that allows dynamic clearing of properties.
        /// </remarks>
        /// 
        internal void ClearPropertyValueAt(
            string path,
            string propertyName,
            object at,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IMultivaluePropertyCmdletProvider propertyProvider = 
                this as IMultivaluePropertyCmdletProvider;

            if (propertyProvider == null)
            {
                throw
                    providerBaseTracer.NewNotSupportedException(
                        "SessionStateStrings",
                        "IMultivaluePropertyCmdletProvider_NotSupported");
            }

            // Call interface method

            propertyProvider.ClearPropertyValueAt(path, propertyName, at);
        } // ClearPropertyValueAt

        /// <summary>
        /// Gives the provider a chance to attach additional parameters to
        /// the clear-propertyvalue -at cmdlet.
        /// </summary>
        /// 
        /// <param name="path">
        /// If the path was specified on the command line, this is the path
        /// to the item to get the dynamic parameters for.
        /// </param>
        /// 
        /// <param name="propertyName">
        /// The name of the property that should be cleared.
        /// </param>
        /// 
        /// <param name="at">
        /// The position of the value of the property to be cleared.
        /// </param>
        ///
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <returns>
        /// An object that has properties and fields decorated with
        /// parsing attributes similar to a cmdlet class.
        /// </returns>
        /// 
        internal object ClearPropertyValueAtDynamicParameters(
            string path, 
            string propertyName,
            object at,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IMultivaluePropertyCmdletProvider propertyProvider = this as IMultivaluePropertyCmdletProvider;

            if (propertyProvider == null)
            {
                return null;
            }
            return propertyProvider.ClearPropertyValueAtDynamicParameters(path, propertyName, at);
        } // ClearPropertyValueAtDynamicParameters
        
        /// <summary>
        /// Internal wrapper for the RemovePropertyValueAt protected method. This method will
        /// only be called if the provider implements the 
        /// IMultivaluePropertyCmdletProvider interface.
        /// </summary>
        /// 
        /// <param name="path">
        /// The path to the item from which the property value should be removed.
        /// </param>
        /// 
        /// <param name="propertyName">
        /// The name of the property that the value should be remove from.
        /// </param>
        /// 
        /// <param name="at">
        /// The position of the value of the property to be removed.
        /// </param>
        ///
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <remarks>
        /// Implement this method when you are providing access to a data store
        /// that allows dynamic removing of property values.
        /// </remarks>
        /// 
        internal void RemovePropertyValueAt(
            string path,
            string propertyName,
            object at,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IMultivaluePropertyCmdletProvider propertyProvider = 
                this as IMultivaluePropertyCmdletProvider;

            if (propertyProvider == null)
            {
                throw
                    providerBaseTracer.NewNotSupportedException(
                        "SessionStateStrings",
                        "IMultivaluePropertyCmdletProvider_NotSupported");
            }

            // Call interface method

            propertyProvider.RemovePropertyValueAt(path, propertyName, at);
        } // RemovePropertyValueAt
        
        /// <summary>
        /// Gives the provider a chance to attach additional parameters to
        /// the remove-propertyvalue -at cmdlet.
        /// </summary>
        /// 
        /// <param name="path">
        /// If the path was specified on the command line, this is the path
        /// to the item to get the dynamic parameters for.
        /// </param>
        /// 
        /// <param name="propertyName">
        /// The name of the property that the value should be remove from.
        /// </param>
        /// 
        /// <param name="at">
        /// The position of the value of the property to be removed.
        /// </param>
        ///
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <returns>
        /// An object that has properties and fields decorated with
        /// parsing attributes similar to a cmdlet class.
        /// </returns>
        /// 
        internal object RemovePropertyValueAtDynamicParameters(
            string path, 
            string propertyName,
            object at,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IMultivaluePropertyCmdletProvider propertyProvider = this as IMultivaluePropertyCmdletProvider;

            if (propertyProvider == null)
            {
                return null;
            }
            return propertyProvider.RemovePropertyValueAtDynamicParameters(path, propertyName, at);
        } // RemovePropertyValueAtDynamicParameters
        
        /// <summary>
        /// Adds the specified properties of the item at the specified path at the specified
        /// position if the property is a multivalued property.
        /// </summary>
        /// 
        /// <param name="path">
        /// The path to the item to add the property to.
        /// </param>
        /// 
        /// <param name="position">
        /// The position to place the new value of the property.
        /// </param>
        ///
        /// <param name="propertyValue">
        /// A PSObject which contains a collection of the name, type, value 
        /// of the properties to be added.
        /// </param>
        /// 
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <returns>
        /// Nothing. An instance of PSObject representing the properties that were set
        /// should be passed to the WriteObject() method.
        /// </returns>
        /// 
        internal void AddPropertyValueAt(
            string path,
            object position,
            PSObject propertyValue,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IMultivaluePropertyCmdletProvider propertyProvider = 
                this as IMultivaluePropertyCmdletProvider;

            if (propertyProvider == null)
            {
                throw
                    providerBaseTracer.NewNotSupportedException(
                        "SessionStateStrings",
                        "IMultivaluePropertyCmdletProvider_NotSupported");
            }

            // Call interface method

            propertyProvider.AddPropertyValueAt(path, position, propertyValue);
        } // AddPropertyValueAt
        
        /// <summary>
        /// Gives the provider a chance to attach additional parameters to
        /// the add-propertyvalue -at cmdlet.
        /// </summary>
        /// 
        /// <param name="path">
        /// If the path was specified on the command line, this is the path
        /// to the item to get the dynamic parameters for.
        /// </param>
        /// 
        /// <param name="at">
        /// The position to place the new value of the property.
        /// </param>
        ///
        /// <param name="propertyValue">
        /// A PSObject which contains a collection of the name, type, value 
        /// of the properties to be added.
        /// </param>
        /// 
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <returns>
        /// An object that has properties and fields decorated with
        /// parsing attributes similar to a cmdlet class.
        /// </returns>
        /// 
        internal object AddPropertyValueAtDynamicParameters(
            string path, 
            object at,
            PSObject propertyValue,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IMultivaluePropertyCmdletProvider propertyProvider = this as IMultivaluePropertyCmdletProvider;

            if (propertyProvider == null)
            {
                return null;
            }
            return propertyProvider.AddPropertyValueAtDynamicParameters(path, at, propertyValue);
        } // AddPropertyValueAtDynamicParameters
        
        #endregion IMultivaluePropertyCmdletProvider
#endif // SUPPORTS_IMULTIVALUEPROPERTYCMDLETPROVIDER

        #region IDynamicPropertyCmdletProvider

        /// <summary>
        /// Internal wrapper for the NewProperty protected method. This method will
        /// only be called if the provider implements the IDynamicPropertyCmdletProvider interface.
        /// </summary>
        /// 
        /// <param name="path">
        /// The path to the item on which the new property should be created.
        /// </param>
        ///
        /// <param name="propertyName">
        /// The name of the property that should be created.
        /// </param>
        ///
        /// <param name="propertyTypeName">
        /// The type of the property that should be created.
        /// </param> 
        ///
        /// <param name="value">
        /// The new value of the property that should be created.
        /// </param>
        ///
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <remarks>
        /// Implement this method when you are providing access to a data store
        /// that allows dynamic creation of properties.
        /// </remarks>
        /// 
        internal void NewProperty(
            string path,
            string propertyName,
            string propertyTypeName,
            object value,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IDynamicPropertyCmdletProvider propertyProvider = this as IDynamicPropertyCmdletProvider;

            if (propertyProvider == null)
            {
                throw
                    PSTraceSource.NewNotSupportedException(
                        SessionStateStrings.IDynamicPropertyCmdletProvider_NotSupported);
            }

            // Call interface method

            propertyProvider.NewProperty(path, propertyName, propertyTypeName, value);
        } // NewProperty

        /// <summary>
        /// Gives the provider a chance to attach additional parameters to
        /// the new-itemproperty cmdlet.
        /// </summary>
        /// 
        /// <param name="path">
        /// If the path was specified on the command line, this is the path
        /// to the item to get the dynamic parameters for.
        /// </param>
        /// 
        /// <param name="propertyName">
        /// The name of the property that should be created.
        /// </param>
        ///
        /// <param name="propertyTypeName">
        /// The type of the property that should be created.
        /// </param> 
        ///
        /// <param name="value">
        /// The new value of the property that should be created.
        /// </param>
        ///
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <returns>
        /// An object that has properties and fields decorated with
        /// parsing attributes similar to a cmdlet class.
        /// </returns>
        /// 
        internal object NewPropertyDynamicParameters(
            string path,
            string propertyName,
            string propertyTypeName,
            object value,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IDynamicPropertyCmdletProvider propertyProvider = this as IDynamicPropertyCmdletProvider;

            if (propertyProvider == null)
            {
                return null;
            }
            return propertyProvider.NewPropertyDynamicParameters(path, propertyName, propertyTypeName, value);
        } // NewPropertyDynamicParameters

        /// <summary>
        /// Internal wrapper for the RemoveProperty protected method. This method will
        /// only be called if the provider implements the IDynamicPropertyCmdletProvider interface.
        /// </summary>
        /// 
        /// <param name="path">
        /// The path to the item on which the property should be removed.
        /// </param>
        /// 
        /// <param name="propertyName">
        /// The name of the property to be removed
        /// </param>
        /// 
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <remarks>
        /// Implement this method when you are providing access to a data store
        /// that allows dynamic removal of properties.
        /// </remarks>
        /// 
        internal void RemoveProperty(
            string path,
            string propertyName,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IDynamicPropertyCmdletProvider propertyProvider = this as IDynamicPropertyCmdletProvider;

            if (propertyProvider == null)
            {
                throw
                    PSTraceSource.NewNotSupportedException(
                        SessionStateStrings.IDynamicPropertyCmdletProvider_NotSupported);
            }

            // Call interface method

            propertyProvider.RemoveProperty(path, propertyName);
        } // RemoveProperty

        /// <summary>
        /// Gives the provider a chance to attach additional parameters to
        /// the remove-itemproperty cmdlet.
        /// </summary>
        /// 
        /// <param name="path">
        /// If the path was specified on the command line, this is the path
        /// to the item to get the dynamic parameters for.
        /// </param>
        /// 
        /// <param name="propertyName">
        /// The name of the property that should be removed.
        /// </param>
        ///
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <returns>
        /// An object that has properties and fields decorated with
        /// parsing attributes similar to a cmdlet class.
        /// </returns>
        /// 
        internal object RemovePropertyDynamicParameters(
            string path,
            string propertyName,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IDynamicPropertyCmdletProvider propertyProvider = this as IDynamicPropertyCmdletProvider;

            if (propertyProvider == null)
            {
                return null;
            }
            return propertyProvider.RemovePropertyDynamicParameters(path, propertyName);
        } // RemovePropertyDynamicParameters

        /// <summary>
        /// Internal wrapper for the RenameProperty protected method. This method will
        /// only be called if the provider implements the IDynamicPropertyCmdletProvider interface.
        /// </summary>
        /// 
        /// <param name="path">
        /// The path to the item on which the property should be renamed.
        /// </param>
        /// 
        /// <param name="propertyName">
        /// The name of the property that should be renamed.
        /// </param>
        /// 
        /// <param name="newPropertyName">
        /// The new name for the property.
        /// </param>
        ///
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <remarks>
        /// Implement this method when you are providing access to a data store
        /// that allows dynamic renaming of properties.
        /// </remarks>
        /// 
        internal void RenameProperty(
                    string path,
            string propertyName,
            string newPropertyName,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IDynamicPropertyCmdletProvider propertyProvider = this as IDynamicPropertyCmdletProvider;

            if (propertyProvider == null)
            {
                throw
                    PSTraceSource.NewNotSupportedException(
                        SessionStateStrings.IDynamicPropertyCmdletProvider_NotSupported);
            }

            // Call interface method

            propertyProvider.RenameProperty(path, propertyName, newPropertyName);
        } // RenameProperty

        /// <summary>
        /// Gives the provider a chance to attach additional parameters to
        /// the rename-itemproperty cmdlet.
        /// </summary>
        /// 
        /// <param name="path">
        /// If the path was specified on the command line, this is the path
        /// to the item to get the dynamic parameters for.
        /// </param>
        /// 
        /// <param name="sourceProperty">
        /// The name of the property that should be renamed.
        /// </param>
        ///
        /// <param name="destinationProperty">
        /// The name of the property to rename it to.
        /// </param>
        /// 
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <returns>
        /// An object that has properties and fields decorated with
        /// parsing attributes similar to a cmdlet class.
        /// </returns>
        /// 
        internal object RenamePropertyDynamicParameters(
            string path,
            string sourceProperty,
            string destinationProperty,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IDynamicPropertyCmdletProvider propertyProvider = this as IDynamicPropertyCmdletProvider;

            if (propertyProvider == null)
            {
                return null;
            }
            return propertyProvider.RenamePropertyDynamicParameters(path, sourceProperty, destinationProperty);
        } // RenamePropertyDynamicParameters

        /// <summary>
        /// Internal wrapper for the CopyProperty protected method. This method will
        /// only be called if the provider implements the IDynamicPropertyCmdletProvider interface.
        /// </summary>
        /// 
        /// <param name="sourcePath">
        /// The path to the item from which the property should be copied.
        /// </param>
        /// 
        /// <param name="sourceProperty">
        /// The name of the property that should be copied.
        /// </param>
        /// 
        /// <param name="destinationPath">
        /// The path to the item to which the property should be copied.
        /// </param>
        ///
        /// <param name="destinationProperty">
        /// The name of the property that should be copied to.
        /// </param>
        ///
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <remarks>
        /// Implement this method when you are providing access to a data store
        /// that allows dynamic copying of properties.
        /// </remarks>
        /// 
        internal void CopyProperty(
            string sourcePath,
            string sourceProperty,
            string destinationPath,
            string destinationProperty,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IDynamicPropertyCmdletProvider propertyProvider = this as IDynamicPropertyCmdletProvider;

            if (propertyProvider == null)
            {
                throw
                    PSTraceSource.NewNotSupportedException(
                        SessionStateStrings.IDynamicPropertyCmdletProvider_NotSupported);
            }

            // Call interface method

            propertyProvider.CopyProperty(sourcePath, sourceProperty, destinationPath, destinationProperty);
        } // CopyProperty

        /// <summary>
        /// Gives the provider a chance to attach additional parameters to
        /// the copy-itemproperty cmdlet.
        /// </summary>
        /// 
        /// <param name="path">
        /// If the path was specified on the command line, this is the path
        /// to the item to get the dynamic parameters for.
        /// </param>
        /// 
        /// <param name="sourceProperty">
        /// The name of the property that should be copied.
        /// </param>
        /// 
        /// <param name="destinationPath">
        /// The path to the item to which the property should be copied.
        /// </param>
        ///
        /// <param name="destinationProperty">
        /// The name of the property that should be copied to.
        /// </param>
        ///
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <returns>
        /// An object that has properties and fields decorated with
        /// parsing attributes similar to a cmdlet class.
        /// </returns>
        /// 
        internal object CopyPropertyDynamicParameters(
            string path,
            string sourceProperty,
            string destinationPath,
            string destinationProperty,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IDynamicPropertyCmdletProvider propertyProvider = this as IDynamicPropertyCmdletProvider;

            if (propertyProvider == null)
            {
                return null;
            }
            return propertyProvider.CopyPropertyDynamicParameters(path, sourceProperty, destinationPath, destinationProperty);
        } // CopyPropertyDynamicParameters

        /// <summary>
        /// Internal wrapper for the MoveProperty protected method. This method will
        /// only be called if the provider implements the IDynamicPropertyCmdletProvider  interface.
        /// </summary>
        /// 
        /// <param name="sourcePath">
        /// The path to the item from which the property should be moved.
        /// </param>
        /// 
        /// <param name="sourceProperty">
        /// The name of the property that should be moved.
        /// </param>
        /// 
        /// <param name="destinationPath">
        /// The path to the item to which the property should be moved.
        /// </param>
        ///
        /// <param name="destinationProperty">
        /// The name of the property that should be moved to.
        /// </param>
        ///
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <remarks>
        /// Implement this method when you are providing access to a data store
        /// that allows dynamic moving of properties.
        /// </remarks>
        /// 
        internal void MoveProperty(
            string sourcePath,
            string sourceProperty,
            string destinationPath,
            string destinationProperty,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IDynamicPropertyCmdletProvider propertyProvider = this as IDynamicPropertyCmdletProvider;

            if (propertyProvider == null)
            {
                throw
                    PSTraceSource.NewNotSupportedException(
                        SessionStateStrings.IDynamicPropertyCmdletProvider_NotSupported);
            }

            // Call interface method

            propertyProvider.MoveProperty(sourcePath, sourceProperty, destinationPath, destinationProperty);
        } // MoveProperty

        /// <summary>
        /// Gives the provider a chance to attach additional parameters to
        /// the move-itemproperty cmdlet.
        /// </summary>
        /// 
        /// <param name="path">
        /// If the path was specified on the command line, this is the path
        /// to the item to get the dynamic parameters for.
        /// </param>
        /// 
        /// <param name="sourceProperty">
        /// The name of the property that should be copied.
        /// </param>
        /// 
        /// <param name="destinationPath">
        /// The path to the item to which the property should be copied.
        /// </param>
        ///
        /// <param name="destinationProperty">
        /// The name of the property that should be copied to.
        /// </param>
        ///
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <returns>
        /// An object that has properties and fields decorated with
        /// parsing attributes similar to a cmdlet class.
        /// </returns>
        /// 
        internal object MovePropertyDynamicParameters(
            string path,
            string sourceProperty,
            string destinationPath,
            string destinationProperty,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IDynamicPropertyCmdletProvider propertyProvider = this as IDynamicPropertyCmdletProvider;

            if (propertyProvider == null)
            {
                return null;
            }
            return propertyProvider.MovePropertyDynamicParameters(path, sourceProperty, destinationPath, destinationProperty);
        } // MovePropertyDynamicParameters

        #endregion IDynamicPropertyCmdletProvider method wrappers

        #region IContentCmdletProvider method wrappers

        /// <summary>
        /// Internal wrapper for the GetContentReader protected method. This method will
        /// only be called if the provider implements the IContentCmdletProvider interface.
        /// </summary>
        /// 
        /// <param name="path">
        /// The path to the item to retrieve content from.
        /// </param>
        /// 
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <returns>
        /// An instance of the IContentReader for the specified path.
        /// </returns>
        /// 
        internal IContentReader GetContentReader(
            string path,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IContentCmdletProvider contentProvider = this as IContentCmdletProvider;

            if (contentProvider == null)
            {
                throw
                    PSTraceSource.NewNotSupportedException(
                        SessionStateStrings.IContentCmdletProvider_NotSupported);
            }

            // Call interface method

            return contentProvider.GetContentReader(path);
        } // GetContentReader

        /// <summary>
        /// Gives the provider a chance to attach additional parameters to
        /// the get-content cmdlet.
        /// </summary>
        /// 
        /// <param name="path">
        /// If the path was specified on the command line, this is the path
        /// to the item to get the dynamic parameters for.
        /// </param>
        /// 
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <returns>
        /// An object that has properties and fields decorated with
        /// parsing attributes similar to a cmdlet class.
        /// </returns>
        /// 
        internal object GetContentReaderDynamicParameters(
            string path,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IContentCmdletProvider contentProvider = this as IContentCmdletProvider;

            if (contentProvider == null)
            {
                return null;
            }
            return contentProvider.GetContentReaderDynamicParameters(path);
        } // GetContentReaderDynamicParameters


        /// <summary>
        /// Internal wrapper for the GetContentWriter protected method. This method will
        /// only be called if the provider implements the IContentCmdletProvider interface.
        /// </summary>
        /// 
        /// <param name="path">
        /// The path to the item to set content on.
        /// </param>
        /// 
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <returns>
        /// An instance of the IContentWriter for the specified path.
        /// </returns>
        /// 
        internal IContentWriter GetContentWriter(
            string path,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IContentCmdletProvider contentProvider = this as IContentCmdletProvider;

            if (contentProvider == null)
            {
                throw
                    PSTraceSource.NewNotSupportedException(
                        SessionStateStrings.IContentCmdletProvider_NotSupported);
            }

            // Call interface method

            return contentProvider.GetContentWriter(path);
        } // GetContentWriter

        /// <summary>
        /// Gives the provider a chance to attach additional parameters to
        /// the add-content and set-content cmdlet.
        /// </summary>
        /// 
        /// <param name="path">
        /// If the path was specified on the command line, this is the path
        /// to the item to get the dynamic parameters for.
        /// </param>
        /// 
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <returns>
        /// An object that has properties and fields decorated with
        /// parsing attributes similar to a cmdlet class.
        /// </returns>
        /// 
        internal object GetContentWriterDynamicParameters(
            string path,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IContentCmdletProvider contentProvider = this as IContentCmdletProvider;

            if (contentProvider == null)
            {
                return null;
            }
            return contentProvider.GetContentWriterDynamicParameters(path);
        } // GetContentWriterDynamicParameters


        /// <summary>
        /// Internal wrapper for the ClearContent protected method. This method will
        /// only be called if the provider implements the IContentCmdletProvider interface.
        /// </summary>
        /// 
        /// <param name="path">
        /// The path to the item to clear the content from.
        /// </param>
        /// 
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        internal void ClearContent(
            string path,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IContentCmdletProvider contentProvider = this as IContentCmdletProvider;

            if (contentProvider == null)
            {
                throw
                    PSTraceSource.NewNotSupportedException(
                        SessionStateStrings.IContentCmdletProvider_NotSupported);
            }

            // Call interface method

            contentProvider.ClearContent(path);
        } // ClearContent

        /// <summary>
        /// Gives the provider a chance to attach additional parameters to
        /// the clear-content cmdlet.
        /// </summary>
        /// 
        /// <param name="path">
        /// If the path was specified on the command line, this is the path
        /// to the item to get the dynamic parameters for.
        /// </param>
        /// 
        /// <param name="cmdletProviderContext">
        /// The context under which this method is being called.
        /// </param>
        /// 
        /// <returns>
        /// An object that has properties and fields decorated with
        /// parsing attributes similar to a cmdlet class.
        /// </returns>
        /// 
        internal object ClearContentDynamicParameters(
            string path,
            CmdletProviderContext cmdletProviderContext)
        {
            Context = cmdletProviderContext;

            IContentCmdletProvider contentProvider = this as IContentCmdletProvider;

            if (contentProvider == null)
            {
                return null;
            }
            return contentProvider.ClearContentDynamicParameters(path);
        } // ClearContentDynamicParameters


        #endregion IContentCmdletProvider method wrappers

        #endregion internal members

        #region protected members

        /// <summary>
        /// Gives the provider the opportunity to initialize itself.
        /// </summary>
        /// 
        /// <param name="providerInfo">
        /// The information about the provider that is being started.
        /// </param>
        /// 
        /// <remarks>
        /// The default implementation returns the ProviderInfo instance that
        /// was passed.
        /// 
        /// To have session state maintain persisted data on behalf of the provider,
        /// the provider should derive from <see cref="System.Management.Automation.ProviderInfo"/>
        /// and add any properties or
        /// methods for the data it wishes to persist.  When Start gets called the
        /// provider should construct an instance of its derived ProviderInfo using the
        /// providerInfo that is passed in and return that new instance.
        /// </remarks>
        protected virtual ProviderInfo Start(ProviderInfo providerInfo)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                return providerInfo;
            }
        }

        /// <summary>
        /// Gets an object that defines the additional parameters for the Start implementation
        /// for a provider.
        /// </summary>
        /// 
        /// <returns>
        /// Overrides of this method should return an object that has properties and fields decorated with
        /// parsing attributes similar to a cmdlet class or a 
        /// <see cref="System.Management.Automation.RuntimeDefinedParameterDictionary"/>.
        /// 
        /// The default implementation returns null. (no additional parameters)
        /// </returns>
        protected virtual object StartDynamicParameters()
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                return null;
            }
        }

        /// <summary>
        /// Called by session state when the provider is being removed.
        /// </summary>
        /// 
        /// <remarks>
        /// A provider should override this method to free up any resources that the provider
        /// was using.
        /// 
        /// The default implementation does nothing.
        /// </remarks>
        protected virtual void Stop()
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
            }
        }

        /// <summary>
        /// Indicates whether stop has been requested on this provider.
        /// </summary>
        public bool Stopping
        {
            get
            {
                using (PSTransactionManager.GetEngineProtectionScope())
                {
                    Diagnostics.Assert(
                        Context != null,
                        "The context should always be set");

                    return Context.Stopping;
                } // TraceProperty
            } // get
        } // Stopping

        /// <summary>
        /// Gets the instance of session state for the current runspace.
        /// </summary>
        public SessionState SessionState
        {
            get
            {
                using (PSTransactionManager.GetEngineProtectionScope())
                {
                    Diagnostics.Assert(
                        Context != null,
                        "The context should always be set");

                    return new SessionState(Context.ExecutionContext.EngineSessionState);
                } // TraceProperty
            } // get
        } // SessionState

        /// <summary>
        /// Gets the instance of the provider interface APIs for the current runspace.
        /// </summary>
        public ProviderIntrinsics InvokeProvider
        {
            get
            {
                using (PSTransactionManager.GetEngineProtectionScope())
                {
                    Diagnostics.Assert(
                        Context != null,
                        "The context should always be set");

                    return new ProviderIntrinsics(Context.ExecutionContext.EngineSessionState);
                } // TraceProperty
            } // get
        } // InvokeProvider

        /// <summary>
        /// Gets the instance of the command invocation APIs for the current runspace.
        /// </summary>
        public CommandInvocationIntrinsics InvokeCommand
        {
            get
            {
                using (PSTransactionManager.GetEngineProtectionScope())
                {
                    Diagnostics.Assert(
                        Context != null,
                        "The context should always be set");


                    return new CommandInvocationIntrinsics(Context.ExecutionContext);
                } // TraceProperty
            } // get
        } // InvokeCommand

        /// <summary>
        /// Gets the credentials under which the operation should run.
        /// </summary>
        public PSCredential Credential
        {
            get
            {
                using (PSTransactionManager.GetEngineProtectionScope())
                {
                    Diagnostics.Assert(
                        Context != null,
                        "The context should always be set");

                    return Context.Credential;
                }
            }
        }

        /// <summary>
        /// The information about the provider that is stored in the runspace
        /// on behalf of the provider.
        /// </summary>
        /// 
        /// <remarks>
        /// If a derived type of ProviderInfo was returned from the Start method, it
        /// will be set here in all subsequent calls to the provider.
        /// </remarks>
        protected internal ProviderInfo ProviderInfo
        {
            get
            {
                using (PSTransactionManager.GetEngineProtectionScope())
                {
                    return _providerInformation;
                } // TraceProperty
            } // get
        }

        /// <summary>
        /// The drive information associated with the context of the current operation.
        /// </summary>
        protected PSDriveInfo PSDriveInfo
        {
            get
            {
                using (PSTransactionManager.GetEngineProtectionScope())
                {
                    Diagnostics.Assert(
                        Context != null,
                        "The context should always be set");

                    return Context.Drive;
                } // TraceProperty
            } // get
        } // PSDriveInfo

        /// <summary>
        /// The dynamic parameters object populated with the values as specified
        /// by the user.
        /// </summary>
        protected object DynamicParameters
        {
            get
            {
                using (PSTransactionManager.GetEngineProtectionScope())
                {
                    Diagnostics.Assert(
                        Context != null,
                        "The context should always be set");

                    return Context.DynamicParameters;
                } // TraceProperty
            } // get
        } // DynamicParameters

        /// <summary>
        /// Gets the force property.
        /// </summary>
        /// 
        /// <remarks>
        /// Gives the provider guidance on how vigorous it should be about performing
        /// the operation. If true, the provider should do everything possible to perform
        /// the operation. If false, the provider should attempt the operation but allow
        /// even simple errors to terminate the operation.
        /// For example, if the user tries to copy a file to a path that already exists and
        /// the destination is read-only, if force is true, the provider should copy over
        /// the existing read-only file. If force is false, the provider should write an error.
        /// </remarks>
        public SwitchParameter Force
        {
            get
            {
                using (PSTransactionManager.GetEngineProtectionScope())
                {
                    Diagnostics.Assert(
                        Context != null,
                        "The context should always be set");

                    return Context.Force;
                } // TraceProperty
            } // get
        } // Force

        /// <summary>
        /// Gets the provider specific filter that was supplied by the caller.
        /// </summary>
        public string Filter
        {
            get
            {
                using (PSTransactionManager.GetEngineProtectionScope())
                {
                    Diagnostics.Assert(
                        Context != null,
                        "The context should always be set");

                    return Context.Filter;
                } // TraceProperty
            } // get 
        } // Filter

        /// <summary>
        /// Gets the include wildcard patterns which is used to determine which items
        /// will be included when taking an action.
        /// </summary>
        public Collection<string> Include
        {
            get
            {
                using (PSTransactionManager.GetEngineProtectionScope())
                {
                    Diagnostics.Assert(
                        Context != null,
                        "The context should always be set");

                    return Context.Include;
                } // TraceProperty
            } // get 
        } // Include

        /// <summary>
        /// Gets the exclude wildcard patterns which is used to determine which items
        /// will be excluded when taking an action.
        /// </summary>
        public Collection<string> Exclude
        {
            get
            {
                using (PSTransactionManager.GetEngineProtectionScope())
                {
                    Diagnostics.Assert(
                        Context != null,
                        "The context should always be set");

                    return Context.Exclude;
                } // TraceProperty
            } // get 
        } // Exclude

        /// <summary>
        /// Gets the host interaction APIs.
        /// </summary>
        public PSHost Host
        {
            get
            {
                using (PSTransactionManager.GetEngineProtectionScope())
                {
                    Diagnostics.Assert(
                        Context != null,
                        "The context should always be set");

                    return Context.ExecutionContext.EngineHostInterface;
                }
            }
        } // Host

        #region IResourceSupplier
        /// <summary>
        /// Gets the resource string corresponding to baseName and 
        /// resourceId from the current assembly. You should override 
        /// this if you require a different behavior.
        /// </summary>
        /// 
        /// <param name="baseName">
        /// the base resource name
        /// </param>
        /// 
        /// <param name="resourceId">
        /// the resource id
        /// </param>
        /// 
        /// <returns>
        /// the resource string corresponding to baseName and resourceId
        /// </returns>
        /// 
        /// <remarks>
        /// When overriding this method, the resource string for the specified
        /// resource should be retrieved from a localized resource assembly.
        /// </remarks>
        public virtual string GetResourceString(string baseName, string resourceId)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                if (String.IsNullOrEmpty(baseName))
                {
                    throw PSTraceSource.NewArgumentException("baseName");
                }

                if (String.IsNullOrEmpty(resourceId))
                {
                    throw PSTraceSource.NewArgumentException("resourceId");
                }

                ResourceManager manager =
                    ResourceManagerCache.GetResourceManager(
                        this.GetType().GetTypeInfo().Assembly,
                        baseName);

                string retValue = null;

                try
                {
                    retValue = manager.GetString(resourceId,
                                                  System.Globalization.CultureInfo.CurrentUICulture);
                }
                catch (MissingManifestResourceException)
                {
                    throw PSTraceSource.NewArgumentException("baseName", GetErrorText.ResourceBaseNameFailure, baseName);
                }
                if (retValue == null)
                {
                    throw PSTraceSource.NewArgumentException("resourceId", GetErrorText.ResourceIdFailure, resourceId);
                }

                return retValue;
            }
        } // GetResourceString
        #endregion IResourceSupplier

        #region ThrowTerminatingError
        /// <Content contentref="System.Management.Automation.Cmdlet.ThrowTerminatingError" />
        public void ThrowTerminatingError(ErrorRecord errorRecord)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                if (null == errorRecord)
                {
                    throw PSTraceSource.NewArgumentNullException("errorRecord");
                }

                if (null != errorRecord.ErrorDetails
                    && null != errorRecord.ErrorDetails.TextLookupError)
                {
                    Exception textLookupError = errorRecord.ErrorDetails.TextLookupError;
                    errorRecord.ErrorDetails.TextLookupError = null;
                    MshLog.LogProviderHealthEvent(
                        this.Context.ExecutionContext,
                        ProviderInfo.Name,
                        textLookupError,
                        Severity.Warning);
                }

                // We can't play the same game as Cmdlet.ThrowTerminatingError
                //  and save the exception in the "pipeline".  We need to pass
                //  the actual exception as a thrown exception.  So, we wrap
                //  it in ProviderInvocationException.

                ProviderInvocationException providerInvocationException =
                    new ProviderInvocationException(ProviderInfo, errorRecord);

                // Log a provider health event

                MshLog.LogProviderHealthEvent(
                    this.Context.ExecutionContext,
                    ProviderInfo.Name,
                    providerInvocationException,
                    Severity.Warning);

                throw providerInvocationException;
            }
        }
        #endregion ThrowTerminatingError

        #region User feedback mechanisms

        /// <Content contentref="System.Management.Automation.Cmdlet.ShouldProcess" />
        public bool ShouldProcess(
            string target)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                Diagnostics.Assert(
                    Context != null,
                    "The context should always be set");

                return Context.ShouldProcess(target);
            }
        } // ShouldProcess

        /// <Content contentref="System.Management.Automation.Cmdlet.ShouldProcess" />
        public bool ShouldProcess(
            string target,
            string action)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                Diagnostics.Assert(
                    Context != null,
                    "The context should always be set");

                return Context.ShouldProcess(target, action);
            }
        } // ShouldProcess

        /// <Content contentref="System.Management.Automation.Cmdlet.ShouldProcess" />
        public bool ShouldProcess(
            string verboseDescription,
            string verboseWarning,
            string caption)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                Diagnostics.Assert(
                    Context != null,
                    "The context should always be set");

                return Context.ShouldProcess(
                    verboseDescription,
                    verboseWarning,
                    caption);
            }
        } // ShouldProcess

        /// <Content contentref="System.Management.Automation.Cmdlet.ShouldProcess" />
        public bool ShouldProcess(
            string verboseDescription,
            string verboseWarning,
            string caption,
            out ShouldProcessReason shouldProcessReason)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                Diagnostics.Assert(
                    Context != null,
                    "The context should always be set");

                return Context.ShouldProcess(
                    verboseDescription,
                    verboseWarning,
                    caption,
                    out shouldProcessReason);
            }
        } // ShouldProcess

        /// <Content contentref="System.Management.Automation.Cmdlet.ShouldContinue" />
        public bool ShouldContinue(
            string query,
            string caption)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                Diagnostics.Assert(
                    Context != null,
                    "The context should always be set");

                return Context.ShouldContinue(query, caption);
            }
        } // ShouldContinue

        /// <Content contentref="System.Management.Automation.Cmdlet.ShouldContinue" />
        public bool ShouldContinue(
            string query,
            string caption,
            ref bool yesToAll,
            ref bool noToAll)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                Diagnostics.Assert(
                    Context != null,
                    "The context should always be set");

                return Context.ShouldContinue(
                    query, caption, ref yesToAll, ref noToAll);
            }
        } // ShouldContinue

        #region Transaction Support

        /// <summary>
        /// Returns true if a transaction is available and active.
        /// </summary>
        public bool TransactionAvailable()
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                if (Context == null)
                    return false;
                else
                    return Context.TransactionAvailable();
            }
        }

        /// <summary>
        /// Gets an object that surfaces the current PowerShell transaction.
        /// When this object is disposed, PowerShell resets the active transaction
        /// </summary>
        public PSTransactionContext CurrentPSTransaction
        {
            get
            {
                if (Context == null)
                    return null;
                else
                    return Context.CurrentPSTransaction;
            }
        }
        #endregion Transaction Support

        /// <Content contentref="System.Management.Automation.Cmdlet.WriteVerbose" />
        public void WriteVerbose(string text)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                Diagnostics.Assert(
                    Context != null,
                    "The context should always be set");

                Context.WriteVerbose(text);
            }
        } // WriteVerbose

        /// <Content contentref="System.Management.Automation.Cmdlet.WriteWarning" />
        public void WriteWarning(string text)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                Diagnostics.Assert(
                    Context != null,
                    "The context should always be set");

                Context.WriteWarning(text);
            }
        } // WriteVerbose


        /// <Content contentref="System.Management.Automation.Cmdlet.WriteProgress" />
        public void WriteProgress(ProgressRecord progressRecord)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                Diagnostics.Assert(
                    Context != null,
                    "The context should always be set");

                if (progressRecord == null)
                {
                    throw PSTraceSource.NewArgumentNullException("progressRecord");
                }

                Context.WriteProgress(progressRecord);
            }
        } // WriteProgress


        /// <Content contentref="System.Management.Automation.Cmdlet.WriteDebug" />
        public void WriteDebug(string text)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                Diagnostics.Assert(
                    Context != null,
                    "The context should always be set");

                Context.WriteDebug(text);
            }
        } // WriteDebug

        /// <Content contentref="System.Management.Automation.Cmdlet.WriteInformation" />
        public void WriteInformation(InformationRecord record)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                Diagnostics.Assert(
                    Context != null,
                    "The context should always be set");

                Context.WriteInformation(record);
            }
        } // WriteInformation

        /// <Content contentref="System.Management.Automation.Cmdlet.WriteInformation" />
        public void WriteInformation(Object messageData, string[] tags)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                Diagnostics.Assert(
                    Context != null,
                    "The context should always be set");

                Context.WriteInformation(messageData, tags);
            }
        } // WriteInformation

        /// <summary>
        /// Converts the incoming object to a PSObject and then adds extra
        /// data as notes. Then it writes the shell object to the context.
        /// </summary>
        ///
        /// <param name="item">
        /// The item being written out.
        /// </param>
        ///
        /// <param name="path">
        /// The path of the item being written out.
        /// </param>
        ///
        /// <param name="isContainer">
        /// True if the item is a container, false otherwise.
        /// </param>
        ///
        private void WriteObject(
            object item,
            string path,
            bool isContainer)
        {
            PSObject result = WrapOutputInPSObject(item, path);

            // Now add the IsContainer

            result.AddOrSetProperty("PSIsContainer", isContainer ? Boxed.True : Boxed.False);
            providerBaseTracer.WriteLine("Attaching {0} = {1}", "PSIsContainer", isContainer);

            Diagnostics.Assert(
                Context != null,
                "The context should always be set");

            Context.WriteObject(result);
        } // WriteObject

#if SUPPORTS_IMULTIVALUEPROPERTYCMDLETPROVIDER

        /// <summary>
        /// Converts the incoming object to a PSObject and then adds extra
        /// data as notes. Then it writes the shell object to the context.
        /// </summary>
        ///
        /// <param name="item">
        /// The value of the property being written out.
        /// </param>
        ///
        /// <param name="path">
        /// The path of the item being written out.
        /// </param>
        ///
        /// <param name="propertyName">
        /// The name of the property the value came from.
        /// </param>
        ///
        /// <param name="position">
        /// The position in the property they value came from.
        /// </param>
        /// 
        private void WriteObject(
            object item,
            string path,
            string propertyName,
            object position)
        {

            PSObject result = WrapOutputInPSObject(item, path);

            // Now add the property

            PSNoteProperty propertyToAdd = new PSNoteProperty("PSProperty", propertyName);
            result.Properties.Add(propertyToAdd);
            providerBaseTracer.WriteLine("Attaching {0} = {1}", "PSProperty", propertyName);

            // Now add the position

            propertyToAdd = new PSNoteProperty("MshAt", position);
            result.Properties.Add(propertyToAdd);
            providerBaseTracer.WriteLine("Attaching {0} = {1}", "MshAt", position);

            Diagnostics.Assert (
                Context != null,
                "The context should always be set");

            Context.WriteObject(result);
        } // WriteObject
#endif // SUPPORTS_IMULTIVALUEPROPERTYCMDLETPROVIDER

        /// <summary>
        /// Converts the incoming object to a PSObject and then adds extra
        /// data as notes. Then it writes the shell object to the context.
        /// </summary>
        ///
        /// <param name="item">
        /// The item being written out.
        /// </param>
        ///
        /// <param name="path">
        /// The path of the item being written out.
        /// </param>
        ///
        private void WriteObject(
            object item,
            string path)
        {
            PSObject result = WrapOutputInPSObject(item, path);

            Diagnostics.Assert(
                Context != null,
                "The context should always be set");

            Context.WriteObject(result);
        } // WriteObject

        /// <summary>
        /// Wraps the item in a PSObject and attaches some notes to the
        /// object that deal with path information.
        /// </summary>
        ///
        /// <param name="item">
        /// The item to be wrapped.
        /// </param>
        ///
        /// <param name="path">
        /// The path to the item.
        /// </param>
        ///
        /// <returns>
        /// A PSObject that wraps the item and has path information attached
        /// as notes.
        /// </returns>
        ///
        /// <exception cref="ArgumentNullException">
        /// if <paramref name="item"/> is null.
        /// </exception>
        /// 
        private PSObject WrapOutputInPSObject(
            object item,
            string path)
        {
            if (item == null)
            {
                throw PSTraceSource.NewArgumentNullException("item");
            }
            PSObject result = new PSObject(item);

            Diagnostics.Assert(
                ProviderInfo != null,
                "The ProviderInfo should always be set");

            // Move the TypeNames to the wrapping object if the wrapped object
            // was an PSObject

            PSObject mshObj = item as PSObject;
            if (mshObj != null)
            {
                result.InternalTypeNames = new ConsolidatedString(mshObj.InternalTypeNames);
            }

            // Construct a provider qualified path as the Path note

            String providerQualifiedPath =
                LocationGlobber.GetProviderQualifiedPath(path, ProviderInfo);

            result.AddOrSetProperty("PSPath", providerQualifiedPath);
            providerBaseTracer.WriteLine("Attaching {0} = {1}", "PSPath", providerQualifiedPath);

            // Now get the parent path and child name

            NavigationCmdletProvider navProvider = this as NavigationCmdletProvider;
            if (navProvider != null && path != null)
            {
                // Get the parent path

                string parentPath = null;

                if (PSDriveInfo != null)
                {
                    parentPath = navProvider.GetParentPath(path, PSDriveInfo.Root, Context);
                }
                else
                {
                    parentPath = navProvider.GetParentPath(path, String.Empty, Context);
                }

                string providerQualifiedParentPath = String.Empty;

                if (!String.IsNullOrEmpty(parentPath))
                {
                    providerQualifiedParentPath =
                        LocationGlobber.GetProviderQualifiedPath(parentPath, ProviderInfo);
                }
                result.AddOrSetProperty("PSParentPath", providerQualifiedParentPath);
                providerBaseTracer.WriteLine("Attaching {0} = {1}", "PSParentPath", providerQualifiedParentPath);

                // Get the child name

                string childName = navProvider.GetChildName(path, Context);


                result.AddOrSetProperty("PSChildName", childName);
                providerBaseTracer.WriteLine("Attaching {0} = {1}", "PSChildName", childName);
            }

            // PSDriveInfo

            if (PSDriveInfo != null)
            {
                result.AddOrSetProperty(this.PSDriveInfo.GetNotePropertyForProviderCmdlets("PSDrive"));
                providerBaseTracer.WriteLine("Attaching {0} = {1}", "PSDrive", this.PSDriveInfo);
            }

            // ProviderInfo

            result.AddOrSetProperty(this.ProviderInfo.GetNotePropertyForProviderCmdlets("PSProvider"));
            providerBaseTracer.WriteLine("Attaching {0} = {1}", "PSProvider", this.ProviderInfo);

            return result;
        } // WrapOutputInPSObject

        /// <summary>
        /// Writes an item to the output as a PSObject with extra data attached
        /// as notes.
        /// </summary>
        /// 
        /// <param name="item">
        /// The item to be written.
        /// </param>
        ///
        /// <param name="path">
        /// The path of the item being written.
        /// </param> 
        ///
        /// <param name="isContainer">
        /// True if the item is a container, false otherwise.
        /// </param>
        ///
        /// <!--
        /// If streaming is on and the writeObjectHandler was specified then the object
        /// gets written to the writeObjectHandler. If streaming is on and the writeObjectHandler
        /// was not specified and the command object was specified, the object gets written to
        /// the WriteObject method of the command object. 
        /// If streaming is off the object gets written to an accumulator collection. The collection
        /// of written object can be retrieved using the AccumulatedObjects method.
        /// -->
        public void WriteItemObject(
            object item,
            string path,
            bool isContainer)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                WriteObject(item, path, isContainer);
            }
        } // WriteItemObject

        /// <summary>
        /// Writes a property object to the output as a PSObject with extra data attached
        /// as notes.
        /// </summary>
        /// 
        /// <param name="propertyValue">
        /// The properties to be written.
        /// </param>
        ///
        /// <param name="path">
        /// The path of the item being written.
        /// </param> 
        ///
        /// <!--
        /// If streaming is on and the writeObjectHandler was specified then the object
        /// gets written to the writeObjectHandler. If streaming is on and the writeObjectHandler
        /// was not specified and the command object was specified, the object gets written to
        /// the WriteObject method of the command object. 
        /// If streaming is off the object gets written to an accumulator collection. The collection
        /// of written object can be retrieved using the AccumulatedObjects method.
        /// -->
        public void WritePropertyObject(
            object propertyValue,
            string path)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                WriteObject(propertyValue, path);
            }
        } // WritePropertyObject

        /// <summary>
        /// Writes a Security Descriptor object to the output as a PSObject with extra data attached
        /// as notes.
        /// </summary>
        /// 
        /// <param name="securityDescriptor">
        /// The Security Descriptor to be written.
        /// </param>
        ///
        /// <param name="path">
        /// The path of the item from which the Security Descriptor was retrieved.
        /// </param> 
        ///
        /// <!--
        /// If streaming is on and the writeObjectHandler was specified then the object
        /// gets written to the writeObjectHandler. If streaming is on and the writeObjectHandler
        /// was not specified and the command object was specified, the object gets written to
        /// the WriteObject method of the command object. 
        /// If streaming is off the object gets written to an accumulator collection. The collection
        /// of written object can be retrieved using the AccumulatedObjects method.
        /// -->
        public void WriteSecurityDescriptorObject(
            ObjectSecurity securityDescriptor,
            string path)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                WriteObject(securityDescriptor, path);
            }
        } // WriteSecurityDescriptorObject

#if SUPPORTS_IMULTIVALUEPROPERTYCMDLETPROVIDER
        /// <summary>
        /// Writes a property value object to the output as a PSObject with extra data attached
        /// as notes.
        /// </summary>
        /// 
        /// <param name="item">
        /// The value of the property to be written.
        /// </param>
        /// 
        /// <param name="path">
        /// The path of the item being written.
        /// </param> 
        ///
        /// <param name="propertyName">
        /// The properties to be written.
        /// </param>
        ///
        /// <param name="position">
        /// The position at which the property value was retrieved or written.
        /// </param>
        /// 
        /// <remarks>
        /// If streaming is on and the writeObjectHandler was specified then the object
        /// gets written to the writeObjectHandler. If streaming is on and the writeObjectHandler
        /// was not specified and the command object was specified, the object gets written to
        /// the WriteObject method of the command object. 
        /// If streaming is off the object gets written to an accumulator collection. The collection
        /// of written object can be retrieved using the AccumulatedObjects method.
        /// </remarks>
        /// 
        public void WritePropertyValueObject(
            object item,
            string path,
            string propertyName,
            object position)
        {
            using(PSTransactionManager.GetEngineProtectionScope())
            {
                WriteObject(item, path, propertyName, position);
            }
        } // WritePropertyValueObject
#endif

        /// <Content contentref="System.Management.Automation.Cmdlet.WriteError" />
        public void WriteError(ErrorRecord errorRecord)
        {
            using (PSTransactionManager.GetEngineProtectionScope())
            {
                Diagnostics.Assert(
                    Context != null,
                    "The context should always be set");

                if (errorRecord == null)
                {
                    throw PSTraceSource.NewArgumentNullException("errorRecord");
                }

                if (null != errorRecord.ErrorDetails
                    && null != errorRecord.ErrorDetails.TextLookupError)
                {
                    MshLog.LogProviderHealthEvent(
                        this.Context.ExecutionContext,
                        ProviderInfo.Name,
                        errorRecord.ErrorDetails.TextLookupError,
                        Severity.Warning);
                }

                Context.WriteError(errorRecord);
            }
        } // WriteError

        #endregion User feedback mechanisms


        #endregion protected members
    } // CmdletProvider

    #endregion CmdletProvider
} // namespace System.Management.Automation

#pragma warning restore 56506

