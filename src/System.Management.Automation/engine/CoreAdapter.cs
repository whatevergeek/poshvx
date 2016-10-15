/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Management.Automation.Language;
using System.Management.Automation.Runspaces;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using System.Xml;

using System.Management.Automation.Internal;
using Microsoft.PowerShell;
using Dbg = System.Management.Automation.Diagnostics;

#pragma warning disable 1634, 1691 // Stops compiler from warning about unknown warnings

namespace System.Management.Automation
{
    /// <summary>
    /// Base class for all Adapters
    /// This is the place to look every time you create a new Adapter. Consider if you 
    /// should implement each of the virtual methods here.
    /// The base class deals with errors and performs additional operations before and after
    /// calling the derived virtual methods.
    /// </summary>
    internal abstract class Adapter
    {
        /// <summary>
        /// tracer for this and derivate classes
        /// </summary>
        [TraceSource("ETS", "Extended Type System")]
        protected static PSTraceSource tracer = PSTraceSource.GetTracer("ETS", "Extended Type System");
        #region virtual

        #region member

        internal virtual bool SiteBinderCanOptimize { get { return false; } }

        protected static IEnumerable<string> GetDotNetTypeNameHierarchy(Type type)
        {
            for (; type != null; type = type.GetTypeInfo().BaseType)
            {
                yield return type.FullName;
            }
        }

        protected static IEnumerable<string> GetDotNetTypeNameHierarchy(object obj)
        {
            return GetDotNetTypeNameHierarchy(obj.GetType());
        }

        /// <summary>
        /// Returns the TypeNameHierarchy out of an object
        /// </summary>
        /// <param name="obj">object to get the TypeNameHierarchy from</param>
        protected virtual IEnumerable<string> GetTypeNameHierarchy(object obj)
        {
            return GetDotNetTypeNameHierarchy(obj);
        }

        /// <summary>
        /// Returns the cached typename, if it can be cached, otherwise constructs a new typename.
        /// By default, we don't return interned values, adapters can override if they choose.
        /// </summary>
        /// <param name="obj">object to get the TypeNameHierarchy from</param>
        protected virtual ConsolidatedString GetInternedTypeNameHierarchy(object obj)
        {
            return new ConsolidatedString(GetTypeNameHierarchy(obj));
        }

        /// <summary>
        /// Returns null if memberName is not a member in the adapter or
        /// the corresponding PSMemberInfo
        /// </summary>
        /// <param name="obj">object to retrieve the PSMemberInfo from</param>
        /// <param name="memberName">name of the member to be retrieved</param>
        /// <returns>The PSMemberInfo corresponding to memberName from obj</returns>
        protected abstract T GetMember<T>(object obj, string memberName) where T : PSMemberInfo;

        /// <summary>
        /// Retrieves all the members available in the object.
        /// The adapter implementation is encouraged to cache all properties/methods available
        /// in the first call to GetMember and GetMembers so that subsequent
        /// calls can use the cache.
        /// In the case of the .NET adapter that would be a cache from the .NET type to
        /// the public properties and fields available in that type. 
        /// In the case of the DirectoryEntry adapter, this could be a cache of the objectClass
        /// to the properties available in it.
        /// </summary>
        /// <param name="obj">object to get all the member information from</param>
        /// <returns>all members in obj</returns>
        protected abstract PSMemberInfoInternalCollection<T> GetMembers<T>(object obj) where T : PSMemberInfo;

        #endregion member

        #region property

        /// <summary>
        /// Returns the value from a property coming from a previous call to GetMember
        /// </summary>
        /// <param name="property">PSProperty coming from a previous call to GetMember</param>
        /// <returns>The value of the property</returns>
        protected abstract object PropertyGet(PSProperty property);

        /// <summary>
        /// Sets the value of a property coming from a previous call to GetMember
        /// </summary>
        /// <param name="property">PSProperty coming from a previous call to GetMember</param>
        /// <param name="setValue">value to set the property with</param>
        /// <param name="convertIfPossible">instructs the adapter to convert before setting, if the adapter supports conversion</param>
        protected abstract void PropertySet(PSProperty property, object setValue, bool convertIfPossible);

        /// <summary>
        /// Returns true if the property is settable
        /// </summary>
        /// <param name="property">property to check</param>
        /// <returns>true if the property is settable</returns>
        protected abstract bool PropertyIsSettable(PSProperty property);

        /// <summary>
        /// Returns true if the property is gettable
        /// </summary>
        /// <param name="property">property to check</param>
        /// <returns>true if the property is gettable</returns>
        protected abstract bool PropertyIsGettable(PSProperty property);

        /// <summary>
        /// Returns the name of the type corresponding to the property's value
        /// </summary>
        /// <param name="property">PSProperty obtained in a previous GetMember</param>
        /// <param name="forDisplay">True if the result is for display purposes only</param>
        /// <returns>the name of the type corresponding to the member</returns>
        protected abstract string PropertyType(PSProperty property, bool forDisplay);

        /// <summary>
        /// Returns the string representation of the property in the object
        /// </summary>
        /// <param name="property">property obtained in a previous GetMember</param>
        /// <returns>the string representation of the property in the object</returns>
        protected abstract string PropertyToString(PSProperty property);

        /// <summary>
        /// Returns an array with the property attributes
        /// </summary>
        /// <param name="property">property we want the attributes from</param>
        /// <returns>an array with the property attributes</returns>
        protected abstract AttributeCollection PropertyAttributes(PSProperty property);

        #endregion property

        #region method

        /// <summary>
        /// Called after a non null return from GetMember to try to call
        /// the method with the arguments
        /// </summary>
        /// <param name="method">the non empty return from GetMethods</param>
        /// <param name="invocationConstraints">invocation constraints</param>
        /// <param name="arguments">the arguments to use</param>
        /// <returns>the return value for the method</returns>
        protected virtual object MethodInvoke(PSMethod method, PSMethodInvocationConstraints invocationConstraints, object[] arguments)
        {
            return this.MethodInvoke(method, arguments);
        }

        /// <summary>
        /// Called after a non null return from GetMember to try to call
        /// the method with the arguments
        /// </summary>
        /// <param name="method">the non empty return from GetMethods</param>
        /// <param name="arguments">the arguments to use</param>
        /// <returns>the return value for the method</returns>
        protected abstract object MethodInvoke(PSMethod method, object[] arguments);

        /// <summary>
        /// Called after a non null return from GetMember to return the overloads
        /// </summary>
        /// <param name="method">the return of GetMember</param>
        /// <returns></returns>
        protected abstract Collection<String> MethodDefinitions(PSMethod method);

        /// <summary>
        /// Returns the string representation of the method in the object
        /// </summary>
        /// <returns>the string representation of the method in the object</returns>
        protected virtual string MethodToString(PSMethod method)
        {
            StringBuilder returnValue = new StringBuilder();
            Collection<string> definitions = MethodDefinitions(method);
            for (int i = 0; i < definitions.Count; i++)
            {
                returnValue.Append(definitions[i]);
                returnValue.Append(", ");
            }

            returnValue.Remove(returnValue.Length - 2, 2);
            return returnValue.ToString();
        }

        #endregion method

        #region parameterized property
        /// <summary>
        /// Returns the name of the type corresponding to the property's value
        /// </summary>
        /// <param name="property">property obtained in a previous GetMember</param>
        /// <returns>the name of the type corresponding to the member</returns>
        /// <remarks>
        /// It is not necessary for derived methods to override this.
        /// This method is called only if ParameterizedProperties are present.
        /// </remarks>
        protected virtual string ParameterizedPropertyType(PSParameterizedProperty property)
        {
            Diagnostics.Assert(false, "adapter is not called for parameterized properties");
            throw PSTraceSource.NewNotSupportedException();
        }

        /// <summary>
        /// Returns true if the property is settable
        /// </summary>
        /// <param name="property">property to check</param>
        /// <returns>true if the property is settable</returns>
        /// <remarks>
        /// It is not necessary for derived methods to override this.
        /// This method is called only if ParameterizedProperties are present.
        /// </remarks>
        protected virtual bool ParameterizedPropertyIsSettable(PSParameterizedProperty property)
        {
            Diagnostics.Assert(false, "adapter is not called for parameterized properties");
            throw PSTraceSource.NewNotSupportedException();
        }

        /// <summary>
        /// Returns true if the property is gettable
        /// </summary>
        /// <param name="property">property to check</param>
        /// <returns>true if the property is gettable</returns>
        /// <remarks>
        /// It is not necessary for derived methods to override this.
        /// This method is called only if ParameterizedProperties are present.
        /// </remarks>
        protected virtual bool ParameterizedPropertyIsGettable(PSParameterizedProperty property)
        {
            Diagnostics.Assert(false, "adapter is not called for parameterized properties");
            throw PSTraceSource.NewNotSupportedException();
        }

        /// <summary>
        /// Called after a non null return from GetMember to return the overloads
        /// </summary>
        /// <param name="property">the return of GetMember</param>
        /// <remarks>
        /// It is not necessary for derived methods to override this.
        /// This method is called only if ParameterizedProperties are present.
        /// </remarks>
        protected virtual Collection<String> ParameterizedPropertyDefinitions(PSParameterizedProperty property)
        {
            Diagnostics.Assert(false, "adapter is not called for parameterized properties");
            throw PSTraceSource.NewNotSupportedException();
        }

        /// <summary>
        /// Called after a non null return from GetMember to get the property value
        /// </summary>
        /// <param name="property">the non empty return from GetMember</param>
        /// <param name="arguments">the arguments to use</param>
        /// <returns>the return value for the property</returns>
        /// <remarks>
        /// It is not necessary for derived methods to override this.
        /// This method is called only if ParameterizedProperties are present.
        /// </remarks>
        protected virtual object ParameterizedPropertyGet(PSParameterizedProperty property, object[] arguments)
        {
            Diagnostics.Assert(false, "adapter is not called for parameterized properties");
            throw PSTraceSource.NewNotSupportedException();
        }

        /// <summary>
        /// Called after a non null return from GetMember to set the property value
        /// </summary>
        /// <param name="property">the non empty return from GetMember</param>
        /// <param name="setValue">the value to set property with</param>
        /// <param name="arguments">the arguments to use</param>
        /// <remarks>
        /// It is not necessary for derived methods to override this.
        /// This method is called only if ParameterizedProperties are present.
        /// </remarks>
        protected virtual void ParameterizedPropertySet(PSParameterizedProperty property, object setValue, object[] arguments)
        {
            Diagnostics.Assert(false, "adapter is not called for parameterized properties");
            throw PSTraceSource.NewNotSupportedException();
        }

        /// <summary>
        /// Returns the string representation of the property in the object
        /// </summary>
        /// <param name="property">property obtained in a previous GetMember</param>
        /// <returns>the string representation of the property in the object</returns>
        /// <remarks>
        /// It is not necessary for derived methods to override this.
        /// This method is called only if ParameterizedProperties are present.
        /// </remarks>
        protected virtual string ParameterizedPropertyToString(PSParameterizedProperty property)
        {
            Diagnostics.Assert(false, "adapter is not called for parameterized properties");
            throw PSTraceSource.NewNotSupportedException();
        }

        #endregion parameterized property      

        #endregion virtual

        #region base

        #region private

        private static Exception NewException(Exception e, string errorId,
            string targetErrorId, string resourceString, params object[] parameters)
        {
            object[] newParameters = new object[parameters.Length + 1];
            for (int i = 0; i < parameters.Length; i++)
            {
                newParameters[i + 1] = parameters[i];
            }
            Exception ex = e as TargetInvocationException;
            if (ex != null)
            {
                Exception inner = ex.InnerException ?? ex;
                newParameters[0] = inner.Message;
                return new ExtendedTypeSystemException(targetErrorId,
                    inner,
                    resourceString,
                    newParameters);
            }
            newParameters[0] = e.Message;
            return new ExtendedTypeSystemException(errorId,
                e,
                resourceString,
                newParameters);
        }

        #endregion private

        #region member
        internal ConsolidatedString BaseGetTypeNameHierarchy(object obj)
        {
            try
            {
                return GetInternedTypeNameHierarchy(obj);
            }
            catch (ExtendedTypeSystemException) { throw; }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                throw NewException(e, "CatchFromBaseGetTypeNameHierarchy", "CatchFromBaseGetTypeNameHierarchyTI",
                                   ExtendedTypeSystem.ExceptionRetrievingTypeNameHierarchy);
            }
        }

        internal T BaseGetMember<T>(object obj, string memberName) where T : PSMemberInfo
        {
            try
            {
                return this.GetMember<T>(obj, memberName);
            }
            catch (ExtendedTypeSystemException) { throw; }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                throw NewException(e, "CatchFromBaseGetMember", "CatchFromBaseGetMemberTI",
                    ExtendedTypeSystem.ExceptionGettingMember, memberName);
            }
        }

        internal PSMemberInfoInternalCollection<T> BaseGetMembers<T>(object obj) where T : PSMemberInfo
        {
            try
            {
                return this.GetMembers<T>(obj);
            }
            catch (ExtendedTypeSystemException) { throw; }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                throw NewException(e, "CatchFromBaseGetMembers", "CatchFromBaseGetMembersTI",
                    ExtendedTypeSystem.ExceptionGettingMembers);
            }
        }

        #endregion member

        #region property

        internal object BasePropertyGet(PSProperty property)
        {
            try
            {
                return PropertyGet(property);
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                throw new GetValueInvocationException("CatchFromBaseAdapterGetValueTI",
                    inner,
                    ExtendedTypeSystem.ExceptionWhenGetting,
                    property.Name, inner.Message);
            }
            catch (GetValueException) { throw; }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                throw new GetValueInvocationException("CatchFromBaseAdapterGetValue",
                    e,
                    ExtendedTypeSystem.ExceptionWhenGetting,
                    property.Name, e.Message);
            }
        }

        internal void BasePropertySet(PSProperty property, object setValue, bool convert)
        {
            try
            {
                PropertySet(property, setValue, convert);
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                throw new SetValueInvocationException("CatchFromBaseAdapterSetValueTI",
                    inner,
                    ExtendedTypeSystem.ExceptionWhenSetting,
                    property.Name, inner.Message);
            }
            catch (SetValueException) { throw; }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                throw new SetValueInvocationException("CatchFromBaseAdapterSetValue",
                    e,
                    ExtendedTypeSystem.ExceptionWhenSetting,
                    property.Name, e.Message);
            }
        }

        internal bool BasePropertyIsSettable(PSProperty property)
        {
            try
            {
                return this.PropertyIsSettable(property);
            }
            catch (ExtendedTypeSystemException) { throw; }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                throw NewException(e, "CatchFromBasePropertyIsSettable", "CatchFromBasePropertyIsSettableTI",
                    ExtendedTypeSystem.ExceptionRetrievingPropertyWriteState, property.Name);
            }
        }

        internal bool BasePropertyIsGettable(PSProperty property)
        {
            try
            {
                return this.PropertyIsGettable(property);
            }
            catch (ExtendedTypeSystemException) { throw; }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                throw NewException(e, "CatchFromBasePropertyIsGettable", "CatchFromBasePropertyIsGettableTI",
                    ExtendedTypeSystem.ExceptionRetrievingPropertyReadState, property.Name);
            }
        }

        internal string BasePropertyType(PSProperty property)
        {
            try
            {
                return this.PropertyType(property, forDisplay: false);
            }
            catch (ExtendedTypeSystemException) { throw; }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                throw NewException(e, "CatchFromBasePropertyType", "CatchFromBasePropertyTypeTI",
                    ExtendedTypeSystem.ExceptionRetrievingPropertyType, property.Name);
            }
        }

        internal string BasePropertyToString(PSProperty property)
        {
            try
            {
                return this.PropertyToString(property);
            }
            catch (ExtendedTypeSystemException) { throw; }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                throw NewException(e, "CatchFromBasePropertyToString", "CatchFromBasePropertyToStringTI",
                    ExtendedTypeSystem.ExceptionRetrievingPropertyString, property.Name);
            }
        }

        internal AttributeCollection BasePropertyAttributes(PSProperty property)
        {
            try
            {
                return this.PropertyAttributes(property);
            }
            catch (ExtendedTypeSystemException) { throw; }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                throw NewException(e, "CatchFromBasePropertyAttributes", "CatchFromBasePropertyAttributesTI",
                    ExtendedTypeSystem.ExceptionRetrievingPropertyAttributes, property.Name);
            }
        }

        #endregion property

        #region method
        internal object BaseMethodInvoke(PSMethod method, PSMethodInvocationConstraints invocationConstraints, params object[] arguments)
        {
            try
            {
                return this.MethodInvoke(method, invocationConstraints, arguments);
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                throw new MethodInvocationException("CatchFromBaseAdapterMethodInvokeTI",
                    inner,
                    ExtendedTypeSystem.MethodInvocationException,
                    method.Name, arguments.Length, inner.Message);
            }
            catch (FlowControlException) { throw; }
            catch (ScriptCallDepthException) { throw; }
            catch (PipelineStoppedException) { throw; }
            catch (MethodException) { throw; }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);

                if (method.baseObject is SteppablePipeline
                    && (method.Name.Equals("Begin", StringComparison.OrdinalIgnoreCase) ||
                        method.Name.Equals("Process", StringComparison.OrdinalIgnoreCase) ||
                        method.Name.Equals("End", StringComparison.OrdinalIgnoreCase)))
                {
                    throw;
                }

                throw new MethodInvocationException("CatchFromBaseAdapterMethodInvoke",
                    e,
                    ExtendedTypeSystem.MethodInvocationException,
                    method.Name, arguments.Length, e.Message);
            }
        }

        internal Collection<String> BaseMethodDefinitions(PSMethod method)
        {
            try
            {
                return this.MethodDefinitions(method);
            }
            catch (ExtendedTypeSystemException) { throw; }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                throw NewException(e, "CatchFromBaseMethodDefinitions", "CatchFromBaseMethodDefinitionsTI",
                    ExtendedTypeSystem.ExceptionRetrievingMethodDefinitions, method.Name);
            }
        }

        internal string BaseMethodToString(PSMethod method)
        {
            try
            {
                return this.MethodToString(method);
            }
            catch (ExtendedTypeSystemException) { throw; }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                throw NewException(e, "CatchFromBaseMethodToString", "CatchFromBaseMethodToStringTI",
                    ExtendedTypeSystem.ExceptionRetrievingMethodString, method.Name);
            }
        }
        #endregion method


        #region parameterized property
        internal string BaseParameterizedPropertyType(PSParameterizedProperty property)
        {
            try
            {
                return this.ParameterizedPropertyType(property);
            }
            catch (ExtendedTypeSystemException) { throw; }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                throw NewException(e, "CatchFromBaseParameterizedPropertyType", "CatchFromBaseParameterizedPropertyTypeTI",
                    ExtendedTypeSystem.ExceptionRetrievingParameterizedPropertytype, property.Name);
            }
        }

        internal bool BaseParameterizedPropertyIsSettable(PSParameterizedProperty property)
        {
            try
            {
                return this.ParameterizedPropertyIsSettable(property);
            }
            catch (ExtendedTypeSystemException) { throw; }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                throw NewException(e, "CatchFromBaseParameterizedPropertyIsSettable", "CatchFromBaseParameterizedPropertyIsSettableTI",
                    ExtendedTypeSystem.ExceptionRetrievingParameterizedPropertyWriteState, property.Name);
            }
        }

        internal bool BaseParameterizedPropertyIsGettable(PSParameterizedProperty property)
        {
            try
            {
                return this.ParameterizedPropertyIsGettable(property);
            }
            catch (ExtendedTypeSystemException) { throw; }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                throw NewException(e, "CatchFromBaseParameterizedPropertyIsGettable", "CatchFromBaseParameterizedPropertyIsGettableTI",
                    ExtendedTypeSystem.ExceptionRetrievingParameterizedPropertyReadState, property.Name);
            }
        }

        internal Collection<String> BaseParameterizedPropertyDefinitions(PSParameterizedProperty property)
        {
            try
            {
                return this.ParameterizedPropertyDefinitions(property);
            }
            catch (ExtendedTypeSystemException) { throw; }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                throw NewException(e, "CatchFromBaseParameterizedPropertyDefinitions", "CatchFromBaseParameterizedPropertyDefinitionsTI",
                    ExtendedTypeSystem.ExceptionRetrievingParameterizedPropertyDefinitions, property.Name);
            }
        }

        internal object BaseParameterizedPropertyGet(PSParameterizedProperty property, params object[] arguments)
        {
            try
            {
                return this.ParameterizedPropertyGet(property, arguments);
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                throw new GetValueInvocationException("CatchFromBaseAdapterParameterizedPropertyGetValueTI",
                    inner,
                    ExtendedTypeSystem.ExceptionWhenGetting,
                    property.Name, inner.Message);
            }
            catch (GetValueException) { throw; }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                throw new GetValueInvocationException("CatchFromBaseParameterizedPropertyAdapterGetValue",
                    e,
                    ExtendedTypeSystem.ExceptionWhenGetting,
                    property.Name, e.Message);
            }
        }

        internal void BaseParameterizedPropertySet(PSParameterizedProperty property, object setValue, params object[] arguments)
        {
            try
            {
                this.ParameterizedPropertySet(property, setValue, arguments);
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                throw new SetValueInvocationException("CatchFromBaseAdapterParameterizedPropertySetValueTI",
                    inner,
                    ExtendedTypeSystem.ExceptionWhenSetting,
                    property.Name, inner.Message);
            }
            catch (SetValueException) { throw; }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                throw new SetValueInvocationException("CatchFromBaseAdapterParameterizedPropertySetValue",
                    e,
                    ExtendedTypeSystem.ExceptionWhenSetting,
                    property.Name, e.Message);
            }
        }




        internal string BaseParameterizedPropertyToString(PSParameterizedProperty property)
        {
            try
            {
                return this.ParameterizedPropertyToString(property);
            }
            catch (ExtendedTypeSystemException) { throw; }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                throw NewException(e, "CatchFromBaseParameterizedPropertyToString", "CatchFromBaseParameterizedPropertyToStringTI",
                    ExtendedTypeSystem.ExceptionRetrievingParameterizedPropertyString, property.Name);
            }
        }

        #endregion parameterized property

        #region Internal Helper Methods

        private static Type GetArgumentType(object argument)
        {
            if (argument == null)
            {
                return typeof(LanguagePrimitives.Null);
            }
            PSReference psref = argument as PSReference;
            if (psref != null)
            {
                return GetArgumentType(PSObject.Base(psref.Value));
            }
            return argument.GetType();
        }

        internal static ConversionRank GetArgumentConversionRank(object argument, Type parameterType)
        {
            Type fromType = GetArgumentType(argument);
            ConversionRank rank = LanguagePrimitives.GetConversionRank(fromType, parameterType);

            if (rank == ConversionRank.None)
            {
                fromType = GetArgumentType(PSObject.Base(argument));
                rank = LanguagePrimitives.GetConversionRank(fromType, parameterType);
            }

            return rank;
        }

        private static ParameterInformation[] ExpandParameters(int argCount, ParameterInformation[] parameters, Type elementType)
        {
            Diagnostics.Assert(parameters[parameters.Length - 1].isParamArray, "ExpandParameters shouldn't be called on non-param method");

            ParameterInformation[] newParameters = new ParameterInformation[argCount];
            Array.Copy(parameters, newParameters, parameters.Length - 1);
            for (int i = parameters.Length - 1; i < argCount; ++i)
            {
                newParameters[i] = new ParameterInformation(elementType, false, null, false);
            }

            return newParameters;
        }

        /// <summary>
        /// Compare the 2 methods, determining which method is better.
        /// </summary>
        /// <returns>1 if method1 is better, -1 if method2 is better, 0 otherwise.</returns>
        private static int CompareOverloadCandidates(OverloadCandidate candidate1, OverloadCandidate candidate2, object[] arguments)
        {
            Diagnostics.Assert(candidate1.conversionRanks.Length == candidate2.conversionRanks.Length,
                               "should have same number of conversions regardless of the number of parameters - default arguments are not included here");

            ParameterInformation[] params1 = candidate1.expandedParameters ?? candidate1.parameters;
            ParameterInformation[] params2 = candidate2.expandedParameters ?? candidate2.parameters;

            int betterCount = 0;
            int multiplier = candidate1.conversionRanks.Length;
            for (int i = 0; i < candidate1.conversionRanks.Length; ++i, --multiplier)
            {
                if (candidate1.conversionRanks[i] < candidate2.conversionRanks[i])
                {
                    betterCount -= multiplier;
                }
                else if (candidate1.conversionRanks[i] > candidate2.conversionRanks[i])
                {
                    betterCount += multiplier;
                }
                else if (candidate1.conversionRanks[i] == ConversionRank.UnrelatedArrays)
                {
                    // If both are unrelated arrays, then use the element type conversions instead.
                    Type argElemType = EffectiveArgumentType(arguments[i]).GetElementType();
                    ConversionRank rank1 = LanguagePrimitives.GetConversionRank(argElemType, params1[i].parameterType.GetElementType());
                    ConversionRank rank2 = LanguagePrimitives.GetConversionRank(argElemType, params2[i].parameterType.GetElementType());
                    if (rank1 < rank2)
                    {
                        betterCount -= multiplier;
                    }
                    else if (rank1 > rank2)
                    {
                        betterCount += multiplier;
                    }
                }
            }

            if (betterCount == 0)
            {
                multiplier = candidate1.conversionRanks.Length;
                for (int i = 0; i < candidate1.conversionRanks.Length; ++i, multiplier = Math.Abs(multiplier) - 1)
                {
                    // The following rather tricky logic tries to pick the best method in 2 very different cases -
                    //   - Pick the most specific method when conversions aren't losing information
                    //   - Pick the most general method when conversions will lose information.
                    // Consider:
                    //    f(uint32), f(decimal), call with f([byte]$i)
                    //        in this case, we want to call f(uint32) because it is more specific
                    //        while not losing information
                    //    f(byte), f(int16), call with f([int]$i)
                    //        in this case, we want to call f(int16) because it is more general,
                    //        we know we could lose information with either call, but we will lose
                    //        less information calling f(int16).
                    ConversionRank rank1 = candidate1.conversionRanks[i];
                    ConversionRank rank2 = candidate2.conversionRanks[i];
                    if (rank1 < ConversionRank.NullToValue || rank2 < ConversionRank.NullToValue)
                    {
                        // The tie breaking rules here do not apply to conversions that are not
                        // numeric or inheritance related.
                        continue;
                    }

                    if ((rank1 >= ConversionRank.NumericImplicit) != (rank2 >= ConversionRank.NumericImplicit))
                    {
                        // Skip trying to break ties when argument conversions are not both implicit or both
                        // explicit.  If we have that situation, there are multiple arguments and an
                        // ambiguity is probably the best choice.
                        continue;
                    }

                    // We will now compare the parameter types, ignoring the actual argument type.  To choose
                    // the right method, we need to know if we want the "most specific" or the "most general".
                    // If we have implicit argument conversions, we'll want the most specific, so invert the multiplier.
                    if (rank1 >= ConversionRank.NumericImplicit)
                    {
                        multiplier = -multiplier;
                    }

                    // With a positive multiplier, we'll choose the "most general" type, and a negative
                    // multiplier will choose the "most specific".
                    rank1 = LanguagePrimitives.GetConversionRank(params1[i].parameterType, params2[i].parameterType);
                    rank2 = LanguagePrimitives.GetConversionRank(params2[i].parameterType, params1[i].parameterType);
                    if (rank1 < rank2)
                    {
                        betterCount += multiplier;
                    }
                    else if (rank1 > rank2)
                    {
                        betterCount -= multiplier;
                    }
                }
            }

            if (betterCount == 0)
            {
                // Check if parameters are the same.  If so, we have a few tiebreakering rules down below.
                for (int i = 0; i < candidate1.conversionRanks.Length; ++i)
                {
                    if (params1[i].parameterType != params2[i].parameterType)
                    {
                        return 0;
                    }
                }
            }

            if (betterCount == 0)
            {
                // Apply tie breaking rules, related to expanded parameters
                if (candidate1.expandedParameters != null && candidate2.expandedParameters != null)
                {
                    // Both are using expanded parameters.  The one with more parameters is better
                    return (candidate1.parameters.Length > candidate2.parameters.Length) ? 1 : -1;
                }
                else if (candidate1.expandedParameters != null)
                {
                    return -1;
                }
                else if (candidate2.expandedParameters != null)
                {
                    return 1;
                }
            }

            if (betterCount == 0)
            {
                // Apply tie breaking rules, related to specificity of parameters
                betterCount = CompareTypeSpecificity(candidate1, candidate2);
            }

            // The methods with fewer parameter wins
            //Need to revisit this if we support named arguments
            if (betterCount == 0)
            {
                if (candidate1.parameters.Length < candidate2.parameters.Length)
                {
                    return 1;
                }
                else if (candidate1.parameters.Length > candidate2.parameters.Length)
                {
                    return -1;
                }
            }

            return betterCount;
        }

        private static OverloadCandidate FindBestCandidate(List<OverloadCandidate> candidates, object[] arguments)
        {
            Dbg.Assert(candidates != null, "Caller should verify candidates != null");

            OverloadCandidate bestCandidateSoFar = null;
            bool multipleBestCandidates = false;

            for (int i = 0; i < candidates.Count; i++)
            {
                OverloadCandidate currentCandidate = candidates[i];
                if (bestCandidateSoFar == null) // first iteration
                {
                    bestCandidateSoFar = currentCandidate;
                    continue;
                }

                int comparisonResult = CompareOverloadCandidates(bestCandidateSoFar, currentCandidate, arguments);
                if (comparisonResult == 0)
                {
                    multipleBestCandidates = true;
                }
                else if (comparisonResult < 0)
                {
                    bestCandidateSoFar = currentCandidate;
                    multipleBestCandidates = false;
                }
            }

            Dbg.Assert(
                !candidates.Any(otherCandidate => otherCandidate != bestCandidateSoFar && CompareOverloadCandidates(otherCandidate, bestCandidateSoFar, arguments) > 0),
                "No other candidates are better than bestCandidateSoFar");

            return multipleBestCandidates ? null : bestCandidateSoFar;
        }

        private static OverloadCandidate FindBestCandidate(List<OverloadCandidate> candidates, object[] arguments, PSMethodInvocationConstraints invocationConstraints)
        {
            List<OverloadCandidate> filteredCandidates = candidates.Where(candidate => IsInvocationConstraintSatisfied(candidate, invocationConstraints)).ToList();
            if (filteredCandidates.Count > 0)
            {
                candidates = filteredCandidates;
            }

            OverloadCandidate bestCandidate = FindBestCandidate(candidates, arguments);
            return bestCandidate;
        }

        private static int CompareTypeSpecificity(Type type1, Type type2)
        {
            if (type1.IsGenericParameter || type2.IsGenericParameter)
            {
                int result = 0;
                if (type1.IsGenericParameter)
                {
                    result -= 1;
                }
                if (type2.IsGenericParameter)
                {
                    result += 1;
                }
                return result;
            }

            if (type1.IsArray)
            {
                Dbg.Assert(type2.IsArray, "Caller should verify that both overload candidates have the same parameter types");
                Dbg.Assert(type1.GetArrayRank() == type2.GetArrayRank(), "Caller should verify that both overload candidates have the same parameter types");
                return CompareTypeSpecificity(type1.GetElementType(), type2.GetElementType());
            }

            if (type1.GetTypeInfo().IsGenericType)
            {
                Dbg.Assert(type2.GetTypeInfo().IsGenericType, "Caller should verify that both overload candidates have the same parameter types");
                Dbg.Assert(type1.GetGenericTypeDefinition() == type2.GetGenericTypeDefinition(), "Caller should verify that both overload candidates have the same parameter types");
                return CompareTypeSpecificity(type1.GetGenericArguments(), type2.GetGenericArguments());
            }

            return 0;
        }

        private static int CompareTypeSpecificity(Type[] params1, Type[] params2)
        {
            Dbg.Assert(params1.Length == params2.Length, "Caller should verify that both overload candidates have the same number of parameters");

            bool candidate1hasAtLeastOneMoreSpecificParameter = false;
            bool candidate2hasAtLeastOneMoreSpecificParameter = false;
            for (int i = 0; i < params1.Length; ++i)
            {
                int specificityComparison = CompareTypeSpecificity(params1[i], params2[i]);
                if (specificityComparison > 0)
                {
                    candidate1hasAtLeastOneMoreSpecificParameter = true;
                }
                else if (specificityComparison < 0)
                {
                    candidate2hasAtLeastOneMoreSpecificParameter = true;
                }

                if (candidate1hasAtLeastOneMoreSpecificParameter && candidate2hasAtLeastOneMoreSpecificParameter)
                {
                    break;
                }
            }

            if (candidate1hasAtLeastOneMoreSpecificParameter && !candidate2hasAtLeastOneMoreSpecificParameter)
            {
                return 1;
            }
            else if (candidate2hasAtLeastOneMoreSpecificParameter && !candidate1hasAtLeastOneMoreSpecificParameter)
            {
                return -1;
            }
            else
            {
                return 0;
            }
        }

        /// <summary>
        /// Returns -1 if <paramref name="candidate1"/> is less specific than <paramref name="candidate2"/>
        /// (1 otherwise, or 0 if both are equally specific or non-comparable)
        /// </summary>
        private static int CompareTypeSpecificity(OverloadCandidate candidate1, OverloadCandidate candidate2)
        {
            if (!(candidate1.method.isGeneric || candidate2.method.isGeneric))
            {
                return 0;
            }

            Type[] params1 = GetGenericMethodDefinitionIfPossible(candidate1.method.method).GetParameters().Select(p => p.ParameterType).ToArray();
            Type[] params2 = GetGenericMethodDefinitionIfPossible(candidate2.method.method).GetParameters().Select(p => p.ParameterType).ToArray();
            return CompareTypeSpecificity(params1, params2);
        }

        private static MethodBase GetGenericMethodDefinitionIfPossible(MethodBase method)
        {
            if (method.IsGenericMethod && !method.IsGenericMethodDefinition)
            {
                MethodInfo methodInfo = method as MethodInfo;
                if (methodInfo != null)
                {
                    return methodInfo.GetGenericMethodDefinition();
                }
            }

            return method;
        }

        [DebuggerDisplay("OverloadCandidate: {method.methodDefinition}")]
        private class OverloadCandidate
        {
            internal MethodInformation method;
            internal ParameterInformation[] parameters;
            internal ParameterInformation[] expandedParameters;
            internal ConversionRank[] conversionRanks;

            internal OverloadCandidate(MethodInformation method, int argCount)
            {
                this.method = method;
                this.parameters = method.parameters;
                conversionRanks = new ConversionRank[argCount];
            }
        }

        private static bool IsInvocationTargetConstraintSatisfied(MethodInformation method, PSMethodInvocationConstraints invocationConstraints)
        {
            Dbg.Assert(method != null, "Caller should verify method != null");

            if (method.method == null)
            {
                return true; // do not apply methodTargetType constraint to non-.NET types (i.e. to COM or WMI types)
            }

            // An invocation constraint is only specified when there is an explicit cast on the target expression, so:
            //
            //    [IFoo]$x.Bar()
            //
            // will have [IFoo] as the method target type, but
            //
            //    $hash = @{}; $hash.Add(1,2)
            //
            // will have no method target type.

            var methodDeclaringType = method.method.DeclaringType;
            var methodDeclaringTypeInfo = methodDeclaringType.GetTypeInfo();
            if (invocationConstraints == null || invocationConstraints.MethodTargetType == null)
            {
                // If no method target type is specified, we say the constraint is matched as long as the method is not an interface.
                // This behavior matches V2 - our candidate sets never included methods with declaring type as an interface in V2.

                return !methodDeclaringTypeInfo.IsInterface;
            }

            var targetType = invocationConstraints.MethodTargetType;
            var targetTypeInfo = targetType.GetTypeInfo();
            if (targetTypeInfo.IsInterface)
            {
                // If targetType is an interface, types must match exactly.  This is how we can call method impls.
                // We also allow the method declaring type to be in a base interface.
                return methodDeclaringType == targetType || (methodDeclaringTypeInfo.IsInterface && targetType.IsSubclassOf(methodDeclaringType));
            }

            if (methodDeclaringTypeInfo.IsInterface)
            {
                // targetType is a class.  We don't try comparing with targetType because we'll end up with
                // an ambiguous set because what is effectively the same method may appear in our set multiple
                // times (once with the declaring type as the interface, and once as the actual class type.)
                return false;
            }

            // Dual-purpose of ([type]<expression>).method() syntax makes this code a little bit tricky to understand.
            // First purpose of this syntax is cast. 
            // Second is a non-virtual super-class method call.
            //
            // Consider this code:
            //
            // ```
            // class B {
            //     [string]foo() {return 'B.foo'}
            //     [string]foo($a) {return 'B.foo'}
            // }
            // 
            // class Q : B {
            //     [string]$Name
            //     Q([string]$name) {$this.name = $name}
            // }
            // 
            // ([Q]'t').foo()
            // ```
            //
            // Here we are using [Q] just for the cast and we are expecting foo() to be resolved to a super-class method.
            // So methodDeclaringType is [B] and targetType is [Q]
            //
            // Now consider another code
            // 
            // ```
            // ([object]"abc").ToString()
            // ```
            //
            // Here we are using [object] to specify that we want a super-class implementation of ToString(), so it should
            // return "System.String"
            // Here methodDeclaringType is [string] and targetType is [object]
            //
            // Notice: in one case targetType is a subclass of methodDeclaringType,
            // in another case it's the reverse.
            // Both of them are valid.
            //
            // Array is a special case.
            return targetType.IsAssignableFrom(methodDeclaringType)
                || methodDeclaringType.IsAssignableFrom(targetType)
                || (targetTypeInfo.IsArray && methodDeclaringType == typeof(Array));
        }

        private static bool IsInvocationConstraintSatisfied(OverloadCandidate overloadCandidate, PSMethodInvocationConstraints invocationConstraints)
        {
            Dbg.Assert(overloadCandidate != null, "Caller should verify overloadCandidate != null");

            if (invocationConstraints == null)
            {
                return true;
            }

            if (invocationConstraints.ParameterTypes != null)
            {
                int parameterIndex = 0;
                foreach (Type parameterTypeConstraint in invocationConstraints.ParameterTypes)
                {
                    if (parameterTypeConstraint != null)
                    {
                        if (parameterIndex >= overloadCandidate.parameters.Length)
                        {
                            return false;
                        }

                        Type parameterType = overloadCandidate.parameters[parameterIndex].parameterType;
                        if (parameterType != parameterTypeConstraint)
                        {
                            return false;
                        }
                    }

                    parameterIndex++;
                }
            }

            return true;
        }

        /// <summary>
        /// Return the best method out of overloaded methods.
        /// The best has the smallest type distance between the method's parameters and the given arguments.
        /// </summary>
        /// <param name="methods">different overloads for a method</param>
        /// <param name="invocationConstraints">invocation constraints</param>
        /// <param name="arguments">arguments to check against the overloads</param>
        /// <param name="errorId">if no best method, the error id to use in the error message</param>
        /// <param name="errorMsg">if no best method, the error message (format string) to use in the error message</param>
        /// <param name="expandParamsOnBest">true if the best method's last parameter is a params method</param>
        /// <param name="callNonVirtually">true if best method should be called as non-virtual</param>
        internal static MethodInformation FindBestMethod(
            MethodInformation[] methods,
            PSMethodInvocationConstraints invocationConstraints,
            object[] arguments,
            ref string errorId,
            ref string errorMsg,
            out bool expandParamsOnBest,
            out bool callNonVirtually)
        {
            callNonVirtually = false;
            var methodInfo = FindBestMethodImpl(methods, invocationConstraints, arguments, ref errorId, ref errorMsg, out expandParamsOnBest);
            if (methodInfo == null)
            {
                return null;
            }

            // For PS classes we need to support base method call syntax:
            //
            // class BaseClass
            // {
            //    [int] foo() { return 1}
            // }
            // class DerivedClass : BaseClass
            // {
            //    [int] foo() { return 2 * ([BaseClass]$this).foo() }
            // }
            //
            // If we have such information in invocationConstraints then we should call method on the baseClass.
            if (invocationConstraints != null &&
                invocationConstraints.MethodTargetType != null &&
                methodInfo.method != null &&
                methodInfo.method.DeclaringType != null)
            {
                Type targetType = methodInfo.method.DeclaringType;
                if (targetType != invocationConstraints.MethodTargetType && targetType.IsSubclassOf(invocationConstraints.MethodTargetType))
                {
                    var parameterTypes = methodInfo.method.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
                    var targetTypeMethod = invocationConstraints.MethodTargetType.GetMethod(methodInfo.method.Name, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, parameterTypes, null);

                    if (targetTypeMethod != null && (targetTypeMethod.IsPublic || targetTypeMethod.IsFamily))
                    {
                        methodInfo = new MethodInformation(targetTypeMethod, 0);
                        callNonVirtually = true;
                    }
                }
            }
            return methodInfo;
        }

        private static MethodInformation FindBestMethodImpl(
            MethodInformation[] methods,
            PSMethodInvocationConstraints invocationConstraints,
            object[] arguments,
            ref string errorId,
            ref string errorMsg,
            out bool expandParamsOnBest)
        {
            expandParamsOnBest = false;

            // Small optimization so we don't calculate type distances when there is only one method
            // We skip the optimization, if the method hasVarArgs, since in the case where arguments
            // and parameters are of the same size, we want to know if the last argument should
            // be turned into an array.
            // We also skip the optimization if the number of arguments and parameters is different
            // so we let the loop deal with possible optional parameters.
            if ((methods.Length == 1) &&
                (methods[0].hasVarArgs == false) &&
                (methods[0].isGeneric == false) &&
                (methods[0].method == null || !(methods[0].method.DeclaringType.GetTypeInfo().IsGenericTypeDefinition)) &&
                // generic methods need to be double checked in a loop below - generic methods can be rejected if type inference fails
                (methods[0].parameters.Length == arguments.Length))
            {
                return methods[0];
            }

            Type[] argumentTypes = arguments.Select(EffectiveArgumentType).ToArray();
            List<OverloadCandidate> candidates = new List<OverloadCandidate>();
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInformation method = methods[i];

                if (method.method != null && method.method.DeclaringType.GetTypeInfo().IsGenericTypeDefinition)
                {
                    continue; // skip methods defined by an *open* generic type
                }

                if (method.isGeneric)
                {
                    Type[] argumentTypesForTypeInference = new Type[argumentTypes.Length];
                    Array.Copy(argumentTypes, argumentTypesForTypeInference, argumentTypes.Length);
                    if (invocationConstraints != null && invocationConstraints.ParameterTypes != null)
                    {
                        int parameterIndex = 0;
                        foreach (Type typeConstraintFromCallSite in invocationConstraints.ParameterTypes)
                        {
                            if (typeConstraintFromCallSite != null)
                            {
                                argumentTypesForTypeInference[parameterIndex] = typeConstraintFromCallSite;
                            }
                            parameterIndex++;
                        }
                    }

                    method = TypeInference.Infer(method, argumentTypesForTypeInference);
                    if (method == null)
                    {
                        // Skip generic methods for which we cannot infer type arguments
                        continue;
                    }
                }

                if (!IsInvocationTargetConstraintSatisfied(method, invocationConstraints))
                {
                    continue;
                }

                ParameterInformation[] parameters = method.parameters;
                if (arguments.Length != parameters.Length)
                {
                    // Skip methods w/ an incorrect # of arguments.

                    if (arguments.Length > parameters.Length)
                    {
                        // If too many args,it's only OK if the method is varargs.
                        if (!method.hasVarArgs)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        // Too few args, OK if there are optionals, or varargs with the param array omitted
                        if (!method.hasOptional && (!method.hasVarArgs || (arguments.Length + 1) != parameters.Length))
                        {
                            continue;
                        }

                        if (method.hasOptional)
                        {
                            // Count optionals.  This code is rarely hit, mainly when calling code in the
                            // assembly Microsoft.VisualBasic.  If it were more frequent, the optional count
                            // should be stored in MethodInformation.
                            int optionals = 0;
                            for (int j = 0; j < parameters.Length; j++)
                            {
                                if (parameters[j].isOptional)
                                {
                                    optionals += 1;
                                }
                            }

                            if (arguments.Length + optionals < parameters.Length)
                            {
                                // Even with optionals, there are too few.
                                continue;
                            }
                        }
                    }
                }

                OverloadCandidate candidate = new OverloadCandidate(method, arguments.Length);
                for (int j = 0; candidate != null && j < parameters.Length; j++)
                {
                    ParameterInformation parameter = parameters[j];
                    if (parameter.isOptional && arguments.Length <= j)
                    {
                        break; // All the other parameters are optional and it is ok not to have more arguments
                    }
                    else if (parameter.isParamArray)
                    {
                        Type elementType = parameter.parameterType.GetElementType();
                        if (parameters.Length == arguments.Length)
                        {
                            ConversionRank arrayConv = GetArgumentConversionRank(arguments[j], parameter.parameterType);
                            ConversionRank elemConv = GetArgumentConversionRank(arguments[j], elementType);
                            if (elemConv > arrayConv)
                            {
                                candidate.expandedParameters = ExpandParameters(arguments.Length, parameters, elementType);
                                candidate.conversionRanks[j] = elemConv;
                            }
                            else
                            {
                                candidate.conversionRanks[j] = arrayConv;
                            }
                            if (candidate.conversionRanks[j] == ConversionRank.None)
                            {
                                candidate = null;
                            }
                        }
                        else
                        {
                            // All remaining arguments will be added to one array to be passed as this params
                            // argument.
                            // Note that we go through here when the param array parameter has no argument.
                            for (int k = j; k < arguments.Length; k++)
                            {
                                candidate.conversionRanks[k] = GetArgumentConversionRank(arguments[k], elementType);
                                if (candidate.conversionRanks[k] == ConversionRank.None)
                                {
                                    // No longer a candidate
                                    candidate = null;
                                    break;
                                }
                            }

                            if (null != candidate)
                            {
                                candidate.expandedParameters = ExpandParameters(arguments.Length, parameters, elementType);
                            }
                        }
                    }
                    else
                    {
                        candidate.conversionRanks[j] = GetArgumentConversionRank(arguments[j], parameter.parameterType);

                        if (candidate.conversionRanks[j] == ConversionRank.None)
                        {
                            // No longer a candidate
                            candidate = null;
                        }
                    }
                } // parameter loop

                if (candidate != null)
                {
                    candidates.Add(candidate);
                }
            } // method loop

            if (candidates.Count == 0)
            {
                if ((methods.Length > 0) && (methods.All(m => m.method != null && m.method.DeclaringType.GetTypeInfo().IsGenericTypeDefinition && m.method.IsStatic)))
                {
                    errorId = "CannotInvokeStaticMethodOnUninstantiatedGenericType";
                    errorMsg = string.Format(
                        CultureInfo.InvariantCulture,
                        ExtendedTypeSystem.CannotInvokeStaticMethodOnUninstantiatedGenericType,
                        methods[0].method.DeclaringType.FullName);
                    return null;
                }
                else
                {
                    errorId = "MethodCountCouldNotFindBest";
                    errorMsg = ExtendedTypeSystem.MethodArgumentCountException;
                    return null;
                }
            }

            OverloadCandidate bestCandidate = candidates.Count == 1
                ? candidates[0]
                : FindBestCandidate(candidates, arguments, invocationConstraints);
            if (bestCandidate != null)
            {
                expandParamsOnBest = bestCandidate.expandedParameters != null;
                return bestCandidate.method;
            }

            errorId = "MethodCountCouldNotFindBest";
            errorMsg = ExtendedTypeSystem.MethodAmbiguousException;
            return null;
        }

        internal static Type EffectiveArgumentType(object arg)
        {
            if (arg != null)
            {
                arg = PSObject.Base(arg);
                object[] argAsArray = arg as object[];
                if (argAsArray != null && argAsArray.Length > 0 && PSObject.Base(argAsArray[0]) != null)
                {
                    Type firstType = PSObject.Base(argAsArray[0]).GetType();
                    bool allSameType = true;

                    for (int j = 1; j < argAsArray.Length; ++j)
                    {
                        if (argAsArray[j] == null || firstType != PSObject.Base(argAsArray[j]).GetType())
                        {
                            allSameType = false;
                            break;
                        }
                    }
                    if (allSameType)
                    {
                        return firstType.MakeArrayType();
                    }
                }
                return arg.GetType();
            }
            else
            {
                return typeof(LanguagePrimitives.Null);
            }
        }

        internal static void SetReferences(object[] arguments, MethodInformation methodInformation, object[] originalArguments)
        {
            using (PSObject.memberResolution.TraceScope("Checking for possible references."))
            {
                ParameterInformation[] parameters = methodInformation.parameters;
                for (int i = 0; (i < originalArguments.Length) && (i < parameters.Length) && (i < arguments.Length); i++)
                {
                    object originalArgument = originalArguments[i];
                    PSReference originalArgumentReference = originalArgument as PSReference;
                    // It still might be an PSObject wrapping an PSReference
                    if (originalArgumentReference == null)
                    {
                        PSObject originalArgumentObj = originalArgument as PSObject;
                        if (originalArgumentObj == null)
                        {
                            continue;
                        }
                        originalArgumentReference = originalArgumentObj.BaseObject as PSReference;
                        if (originalArgumentReference == null)
                        {
                            continue;
                        }
                    }
                    ParameterInformation parameter = parameters[i];
                    if (!parameter.isByRef)
                    {
                        continue;
                    }
                    object argument = arguments[i];
                    PSObject.memberResolution.WriteLine("Argument '{0}' was a reference so it will be set to \"{1}\".", i + 1, argument);
                    originalArgumentReference.Value = argument;
                }
            }
        }

        internal static MethodInformation GetBestMethodAndArguments(
            string methodName,
            MethodInformation[] methods,
            object[] arguments,
            out object[] newArguments)
        {
            return GetBestMethodAndArguments(methodName, methods, null, arguments, out newArguments);
        }

        internal static MethodInformation GetBestMethodAndArguments(
            string methodName,
            MethodInformation[] methods,
            PSMethodInvocationConstraints invocationConstraints,
            object[] arguments,
            out object[] newArguments)
        {
            bool expandParamsOnBest;
            bool callNonVirtually;
            string errorId = null;
            string errorMsg = null;
            MethodInformation bestMethod = FindBestMethod(methods, invocationConstraints, arguments, ref errorId, ref errorMsg, out expandParamsOnBest, out callNonVirtually);
            if (bestMethod == null)
            {
                throw new MethodException(errorId, null, errorMsg, methodName, arguments.Length);
            }
            newArguments = GetMethodArgumentsBase(methodName, bestMethod.parameters, arguments, expandParamsOnBest);
            return bestMethod;
        }

        /// <summary>
        /// Called in GetBestMethodAndArguments after a call to FindBestMethod to perform the
        /// type conversion, copying(varArg) and optional value setting of the final arguments.
        /// </summary>        
        internal static object[] GetMethodArgumentsBase(string methodName,
            ParameterInformation[] parameters, object[] arguments,
            bool expandParamsOnBest)
        {
            int parametersLength = parameters.Length;
            if (parametersLength == 0)
            {
                return Utils.EmptyArray<object>();
            }
            object[] retValue = new object[parametersLength];
            for (int i = 0; i < parametersLength - 1; i++)
            {
                ParameterInformation parameter = parameters[i];
                SetNewArgument(methodName, arguments, retValue, parameter, i);
            }
            ParameterInformation lastParameter = parameters[parametersLength - 1];
            if (!expandParamsOnBest)
            {
                SetNewArgument(methodName, arguments, retValue, lastParameter, parametersLength - 1);
                return retValue;
            }

            // From this point on, we are dealing with VarArgs (Params)

            // If we have no arguments left, we use an appropriate empty array for the last parameter
            if (arguments.Length < parametersLength)
            {
                retValue[parametersLength - 1] = Array.CreateInstance(lastParameter.parameterType.GetElementType(), new int[] { 0 });
                return retValue;
            }

            // We are going to put all the remaining arguments into an array
            // and convert them to the propper type, if necessary to be the
            // one argument for this last parameter
            int remainingArgumentCount = arguments.Length - parametersLength + 1;
            if (remainingArgumentCount == 1 && arguments[arguments.Length - 1] == null)
            {
                // Don't turn a single null argument into an array of 1 element, just pass null.
                retValue[parametersLength - 1] = null;
            }
            else
            {
                object[] remainingArguments = new object[remainingArgumentCount];
                Type paramsElementType = lastParameter.parameterType.GetElementType();
                for (int j = 0; j < remainingArgumentCount; j++)
                {
                    int argumentIndex = j + parametersLength - 1;
                    try
                    {
                        remainingArguments[j] = MethodArgumentConvertTo(arguments[argumentIndex], false, argumentIndex,
                            paramsElementType, CultureInfo.InvariantCulture);
                    }
                    catch (InvalidCastException e)
                    {
                        // NTRAID#Windows Out Of Band Releases-924162-2005/11/17-JonN
                        throw new MethodException(
                            "MethodArgumentConversionInvalidCastArgument",
                            e,
                            ExtendedTypeSystem.MethodArgumentConversionException,
                            argumentIndex, arguments[argumentIndex], methodName, paramsElementType, e.Message);
                    }
                }

                try
                {
                    retValue[parametersLength - 1] = MethodArgumentConvertTo(remainingArguments,
                        lastParameter.isByRef, parametersLength - 1, lastParameter.parameterType,
                        CultureInfo.InvariantCulture);
                }
                catch (InvalidCastException e)
                {
                    // NTRAID#Windows Out Of Band Releases-924162-2005/11/17-JonN
                    throw new MethodException(
                        "MethodArgumentConversionParamsConversion",
                        e,
                        ExtendedTypeSystem.MethodArgumentConversionException,
                        parametersLength - 1, remainingArguments, methodName, lastParameter.parameterType, e.Message);
                }
            }

            return retValue;
        }

        /// <summary>
        /// Auxiliary method in MethodInvoke to set newArguments[index] with the propper value
        /// </summary>
        /// <param name="methodName">used for the MethodException that might be thrown</param>
        /// <param name="arguments">the complete array of arguments</param>
        /// <param name="newArguments">the complete array of new arguments</param>
        /// <param name="parameter">the parameter to use</param>
        /// <param name="index">the index in newArguments to set</param>
        internal static void SetNewArgument(string methodName, object[] arguments,
            object[] newArguments, ParameterInformation parameter, int index)
        {
            if (arguments.Length > index)
            {
                try
                {
                    newArguments[index] = MethodArgumentConvertTo(arguments[index], parameter.isByRef, index,
                        parameter.parameterType, CultureInfo.InvariantCulture);
                }
                catch (InvalidCastException e)
                {
                    // NTRAID#Windows Out Of Band Releases-924162-2005/11/17-JonN
                    throw new MethodException(
                        "MethodArgumentConversionInvalidCastArgument",
                        e,
                        ExtendedTypeSystem.MethodArgumentConversionException,
                        index, arguments[index], methodName, parameter.parameterType, e.Message);
                }
            }
            else
            {
                Diagnostics.Assert(parameter.isOptional, "FindBestMethod would not return this method if there is no corresponding argument for a non optional parameter");
                newArguments[index] = parameter.defaultValue;
            }
        }

        internal static object MethodArgumentConvertTo(object valueToConvert,
            bool isParameterByRef, int parameterIndex, Type resultType,
            IFormatProvider formatProvider)
        {
            using (PSObject.memberResolution.TraceScope("Method argument conversion."))
            {
                if (resultType == null)
                {
                    throw PSTraceSource.NewArgumentNullException("resultType");
                }

                bool isArgumentByRef;
                valueToConvert = UnReference(valueToConvert, out isArgumentByRef);
                if (isParameterByRef && !isArgumentByRef)
                {
                    throw new MethodException("NonRefArgumentToRefParameterMsg", null,
                        ExtendedTypeSystem.NonRefArgumentToRefParameter, parameterIndex + 1, typeof(PSReference).FullName, "[ref]");
                }

                if (isArgumentByRef && !isParameterByRef)
                {
                    throw new MethodException("RefArgumentToNonRefParameterMsg", null,
                        ExtendedTypeSystem.RefArgumentToNonRefParameter, parameterIndex + 1, typeof(PSReference).FullName, "[ref]");
                }
                return PropertySetAndMethodArgumentConvertTo(valueToConvert, resultType, formatProvider);
            }
        }

        internal static object UnReference(object obj, out bool isArgumentByRef)
        {
            isArgumentByRef = false;
            PSReference reference = obj as PSReference;
            if (reference != null)
            {
                PSObject.memberResolution.WriteLine("Parameter was a reference.");
                isArgumentByRef = true;
                return reference.Value;
            }
            PSObject mshObj = obj as PSObject;
            if (mshObj != null)
            {
                reference = mshObj.BaseObject as PSReference;
            }
            if (reference != null)
            {
                PSObject.memberResolution.WriteLine("Parameter was an PSObject containing a reference.");
                isArgumentByRef = true;
                return reference.Value;
            }
            return obj;
        }

        internal static object PropertySetAndMethodArgumentConvertTo(object valueToConvert,
            Type resultType, IFormatProvider formatProvider)
        {
            using (PSObject.memberResolution.TraceScope("Converting parameter \"{0}\" to \"{1}\".", valueToConvert, resultType))
            {
                if (resultType == null)
                {
                    throw PSTraceSource.NewArgumentNullException("resultType");
                }
                PSObject mshObj = valueToConvert as PSObject;
                if (mshObj != null)
                {
                    if (resultType == typeof(object))
                    {
                        PSObject.memberResolution.WriteLine("Parameter was an PSObject and will be converted to System.Object.");
                        // we use PSObject.Base so we don't return 
                        // PSCustomObject
                        return PSObject.Base(mshObj);
                    }
                }

                return LanguagePrimitives.ConvertTo(valueToConvert, resultType, formatProvider);
            }
        }

        internal static void DoBoxingIfNecessary(ILGenerator generator, Type type)
        {
            TypeInfo typeInfo = null;
            if (type.IsByRef)
            {
                // We can't use a byref like we would use System.Object (the CLR will
                // crash if we attempt to do so.)  There isn't much anyone could do
                // with a byref in PowerShell anyway, so just load the object and
                // return that instead.
                type = type.GetElementType();
                typeInfo = type.GetTypeInfo();
                if (typeInfo.IsPrimitive)
                {
                    if (type == typeof(byte)) { generator.Emit(OpCodes.Ldind_U1); }
                    else if (type == typeof(ushort)) { generator.Emit(OpCodes.Ldind_U2); }
                    else if (type == typeof(uint)) { generator.Emit(OpCodes.Ldind_U4); }
                    else if (type == typeof(sbyte)) { generator.Emit(OpCodes.Ldind_I8); }
                    else if (type == typeof(short)) { generator.Emit(OpCodes.Ldind_I2); }
                    else if (type == typeof(int)) { generator.Emit(OpCodes.Ldind_I4); }
                    else if (type == typeof(long)) { generator.Emit(OpCodes.Ldind_I8); }
                    else if (type == typeof(float)) { generator.Emit(OpCodes.Ldind_R4); }
                    else if (type == typeof(double)) { generator.Emit(OpCodes.Ldind_R8); }
                }
                else if (typeInfo.IsValueType)
                {
                    generator.Emit(OpCodes.Ldobj, type);
                }
                else
                {
                    generator.Emit(OpCodes.Ldind_Ref);
                }
            }
            else if (type.IsPointer)
            {
                // Pointers are similar to a byref.  Here we mimic what C# would do
                // when assigning a pointer to an object.  This might not be useful
                // to PowerShell script, but if we did nothing, the CLR would crash
                // our process.
                MethodInfo boxMethod = typeof(Pointer).GetMethod("Box");
                MethodInfo typeFromHandle = typeof(Type).GetMethod("GetTypeFromHandle");
                generator.Emit(OpCodes.Ldtoken, type);
                generator.Emit(OpCodes.Call, typeFromHandle);
                generator.Emit(OpCodes.Call, boxMethod);
            }

            typeInfo = typeInfo ?? type.GetTypeInfo();
            if (typeInfo.IsValueType)
            {
                generator.Emit(OpCodes.Box, type);
            }
        }

        #endregion

        #endregion base
    }
    /// <summary>
    /// ordered and case insensitive hashtable
    /// </summary>
    internal class CacheTable
    {
        internal Collection<object> memberCollection;
        private Dictionary<string, int> _indexes;
        internal CacheTable()
        {
            memberCollection = new Collection<object>();
            _indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
        internal void Add(string name, object member)
        {
            _indexes[name] = memberCollection.Count;
            memberCollection.Add(member);
        }
        internal object this[string name]
        {
            get
            {
                int indexObj;
                if (!_indexes.TryGetValue(name, out indexObj))
                {
                    return null;
                }
                return this.memberCollection[indexObj];
            }
        }
    }

    /// <summary>
    /// Stores method related information.
    /// This structure should be used whenever a new type is adapted.
    /// For example, ManagementObjectAdapter uses this structure to store
    /// WMI method information.
    /// </summary>
    [DebuggerDisplay("MethodInformation: {methodDefinition}")]
    internal class MethodInformation
    {
        internal MethodBase method;
        private string _cachedMethodDefinition;
        internal string methodDefinition
        {
            get
            {
                if (_cachedMethodDefinition == null)
                {
                    var name = method is ConstructorInfo ? "new" : method.Name;
                    var methodDefn = DotNetAdapter.GetMethodInfoOverloadDefinition(name, method, method.GetParameters().Length - parameters.Length);
                    Interlocked.CompareExchange(ref _cachedMethodDefinition, methodDefn, null);
                }
                return _cachedMethodDefinition;
            }
        }

        internal ParameterInformation[] parameters;
        internal bool hasVarArgs;
        internal bool hasOptional;
        internal bool isGeneric;

        private bool _useReflection;
        private delegate object MethodInvoker(object target, object[] arguments);
        private MethodInvoker _methodInvoker;

        /// <summary>
        /// This constructor supports .net methods
        /// </summary>
        internal MethodInformation(MethodBase method, int parametersToIgnore)
        {
            this.method = method;
            this.isGeneric = method.IsGenericMethod;
            ParameterInfo[] methodParameters = method.GetParameters();
            int parametersLength = methodParameters.Length - parametersToIgnore;
            this.parameters = new ParameterInformation[parametersLength];

            for (int i = 0; i < parametersLength; i++)
            {
                this.parameters[i] = new ParameterInformation(methodParameters[i]);
                if (methodParameters[i].IsOptional)
                {
                    hasOptional = true;
                }
            }

            this.hasVarArgs = false;
            if (parametersLength > 0)
            {
                ParameterInfo lastParameter = methodParameters[parametersLength - 1];

                // Optional and params together are forbidden in VB and so we only check for params
                // if !hasOptional
                if (!hasOptional && lastParameter.ParameterType.IsArray)
                {
                    // The extension method 'CustomAttributeExtensions.GetCustomAttributes(ParameterInfo, Type, Boolean)' has inconsistent
                    // behavior on its return value in both FullCLR and CoreCLR. According to MSDN, if the attribute cannot be found, it
                    // should return an empty collection. However, it returns null in some rare cases [when the parameter isn't backed by
                    // actual metadata].
                    // This inconsistent behavior affects OneCore powershell because we are using the extension method here when compiling
                    // against CoreCLR. So we need to add a null check until this is fixed in CLR.
                    var paramArrayAttrs = lastParameter.GetCustomAttributes(typeof(ParamArrayAttribute), false);
                    if (paramArrayAttrs != null && paramArrayAttrs.Any())
                    {
                        this.hasVarArgs = true;
                        this.parameters[parametersLength - 1].isParamArray = true;
                    }
                }
            }
        }

        internal MethodInformation(bool hasvarargs, bool hasoptional, ParameterInformation[] arguments)
        {
            hasVarArgs = hasvarargs;
            hasOptional = hasoptional;
            parameters = arguments;
        }

        internal object Invoke(object target, object[] arguments)
        {
            if (target is PSObject)
            {
                if (!method.DeclaringType.IsAssignableFrom(target.GetType()))
                {
                    target = PSObject.Base(target);
                }
            }

            if (!_useReflection)
            {
                if (_methodInvoker == null)
                {
                    if (!(method is MethodInfo))
                    {
                        _useReflection = true;
                    }
                    else
                    {
                        _methodInvoker = GetMethodInvoker((MethodInfo)method);
                    }
                }
                if (_methodInvoker != null)
                {
                    return _methodInvoker(target, arguments);
                }
            }
            return method.Invoke(target, arguments);
        }

        private static OpCode[] s_ldc = new OpCode[] {
            OpCodes.Ldc_I4_0, OpCodes.Ldc_I4_1, OpCodes.Ldc_I4_2, OpCodes.Ldc_I4_3, OpCodes.Ldc_I4_4,
            OpCodes.Ldc_I4_5, OpCodes.Ldc_I4_6, OpCodes.Ldc_I4_7, OpCodes.Ldc_I4_8
        };

        private static void EmitLdc(ILGenerator emitter, int c)
        {
            if (c < s_ldc.Length)
            {
                emitter.Emit(s_ldc[c]);
            }
            else
            {
                emitter.Emit(OpCodes.Ldc_I4, c);
            }
        }

        private static bool CompareMethodParameters(MethodBase method1, MethodBase method2)
        {
            ParameterInfo[] params1 = method1.GetParameters();
            ParameterInfo[] params2 = method2.GetParameters();

            if (params1.Length != params2.Length)
            {
                return false;
            }

            for (int i = 0; i < params1.Length; ++i)
            {
                if (params1[i].ParameterType != params2[i].ParameterType)
                {
                    return false;
                }
            }

            return true;
        }

        private static Type FindInterfaceForMethod(MethodInfo method, out MethodInfo methodToCall)
        {
            methodToCall = null;

            Type valuetype = method.DeclaringType;

            Diagnostics.Assert(valuetype.GetTypeInfo().IsValueType, "This code only works with valuetypes");

            Type[] interfaces = valuetype.GetInterfaces();
            for (int i = 0; i < interfaces.Length; i++)
            {
                Type type = interfaces[i];
                MethodInfo methodInfo = type.GetMethod(method.Name, BindingFlags.Instance);
                if (methodInfo != null && CompareMethodParameters(methodInfo, method))
                {
                    methodToCall = methodInfo;
                    return type;
                }
            }

            // TODO: method impls (not especially important because I don't think they can be called in script.

            return null;
        }

        [SuppressMessage("NullPtr", "#pw26500", Justification = "This is a false positive. Original warning was on the deference of 'locals' on line 1863: emitter.Emit(OpCodes.Ldloca, locals[cLocal])")]
        private MethodInvoker GetMethodInvoker(MethodInfo method)
        {
            Type type;
            bool valueTypeInstanceMethod = false;
            bool anyOutOrRefParameters = false;
            bool mustStoreRetVal = false;
            MethodInfo methodToCall = method;
            int cLocal = 0;
            int c;

            DynamicMethod dynamicMethod = new DynamicMethod(method.Name, typeof(object),
                new Type[] { typeof(object), typeof(object[]) }, typeof(Adapter).GetTypeInfo().Module, true);

            ILGenerator emitter = dynamicMethod.GetILGenerator();
            ParameterInfo[] parameters = method.GetParameters();

            int localCount = 0;
            if (!method.IsStatic && method.DeclaringType.GetTypeInfo().IsValueType)
            {
                if (!method.IsVirtual)
                {
                    // We need a local to unbox the instance argument into
                    valueTypeInstanceMethod = true;
                    localCount += 1;
                }
            }

            for (int i = 0; i < parameters.Length; i++)
            {
                // We need locals for any out/ref parameters.  We could get
                // away with avoiding a local if the parameter was 'object',
                // but that optimization is not implemented.
                if (parameters[i].IsOut || parameters[i].ParameterType.IsByRef)
                {
                    anyOutOrRefParameters = true;
                    localCount += 1;
                }
            }

            LocalBuilder[] locals = null;
            Type returnType = method.ReturnType;
            if (localCount > 0)
            {
                if (anyOutOrRefParameters && returnType != typeof(void))
                {
                    // If there are any ref/out parameters, we set them after the
                    // call.  We can't leave the return value on the stack (it fails
                    // verification), so we must create a local to hold the return value.
                    localCount += 1;
                    mustStoreRetVal = true;
                }
                locals = new LocalBuilder[localCount];

                cLocal = 0;
                if (valueTypeInstanceMethod)
                {
                    // Unbox the instance parameter into a local.
                    type = method.DeclaringType;
                    locals[cLocal] = emitter.DeclareLocal(type);
                    emitter.Emit(OpCodes.Ldarg_0);
                    emitter.Emit(OpCodes.Unbox_Any, type);
                    emitter.Emit(OpCodes.Stloc, locals[cLocal]);
                    cLocal += 1;
                }

                // Copy all arguments that are being passed as out/ref parameters into
                // locals.
                for (c = 0; c < parameters.Length; ++c)
                {
                    type = parameters[c].ParameterType;
                    if (parameters[c].IsOut || type.IsByRef)
                    {
                        if (type.IsByRef)
                        {
                            type = type.GetElementType();
                        }
                        locals[cLocal] = emitter.DeclareLocal(type);

                        emitter.Emit(OpCodes.Ldarg_1);
                        EmitLdc(emitter, c);
                        emitter.Emit(OpCodes.Ldelem_Ref);
                        if (type.GetTypeInfo().IsValueType)
                        {
                            emitter.Emit(OpCodes.Unbox_Any, type);
                        }
                        else if (type != typeof(object))
                        {
                            emitter.Emit(OpCodes.Castclass, type);
                        }
                        emitter.Emit(OpCodes.Stloc, locals[cLocal]);

                        cLocal += 1;
                    }
                }

                if (mustStoreRetVal)
                {
                    locals[cLocal] = emitter.DeclareLocal(returnType);
                }
            }

            cLocal = 0;
            if (!method.IsStatic)
            {
                // Load the "instance" argument.
                if (method.DeclaringType.GetTypeInfo().IsValueType)
                {
                    if (method.IsVirtual)
                    {
                        type = FindInterfaceForMethod(method, out methodToCall);
                        if (type == null)
                        {
                            _useReflection = true;
                            return null;
                        }
                        emitter.Emit(OpCodes.Ldarg_0);
                        emitter.Emit(OpCodes.Castclass, type);
                    }
                    else
                    {
                        emitter.Emit(OpCodes.Ldloca, locals[cLocal]);
                        cLocal += 1;
                    }
                }
                else
                {
                    emitter.Emit(OpCodes.Ldarg_0);
                }
            }

            for (c = 0; c < parameters.Length; c++)
            {
                type = parameters[c].ParameterType;
                if (type.IsByRef)
                {
                    emitter.Emit(OpCodes.Ldloca, locals[cLocal]);
                    cLocal += 1;
                }
                else if (parameters[c].IsOut)
                {
                    emitter.Emit(OpCodes.Ldloc, locals[cLocal]);
                    cLocal += 1;
                }
                else
                {
                    emitter.Emit(OpCodes.Ldarg_1);
                    EmitLdc(emitter, c);
                    emitter.Emit(OpCodes.Ldelem_Ref);

                    // Unbox value types since our args array is full of objects
                    if (type.GetTypeInfo().IsValueType)
                    {
                        emitter.Emit(OpCodes.Unbox_Any, type);
                    }
                    // For reference types, cast from object
                    else if (type != typeof(object))
                    {
                        emitter.Emit(OpCodes.Castclass, type);
                    }
                }
            }

            emitter.Emit(method.IsStatic ? OpCodes.Call : OpCodes.Callvirt, methodToCall);

            if (mustStoreRetVal)
            {
                emitter.Emit(OpCodes.Stloc, locals[locals.Length - 1]);
            }

            // Handle the ref/out arguments by copying the locals
            // back into the original args array
            if (anyOutOrRefParameters)
            {
                cLocal = valueTypeInstanceMethod ? 1 : 0;
                for (c = 0; c < parameters.Length; c++)
                {
                    type = parameters[c].ParameterType;
                    if (!parameters[c].IsOut && !type.IsByRef)
                    {
                        continue;
                    }
                    if (type.IsByRef)
                    {
                        type = type.GetElementType();
                    }

                    emitter.Emit(OpCodes.Ldarg_1);
                    EmitLdc(emitter, c);
                    emitter.Emit(OpCodes.Ldloc, locals[cLocal]);

                    // Again, box value types since the args array holds objects
                    if (type.GetTypeInfo().IsValueType)
                    {
                        emitter.Emit(OpCodes.Box, type);
                    }

                    emitter.Emit(OpCodes.Stelem_Ref);
                    cLocal += 1;
                }
            }

            // We must return something, so return null for void methods
            if (returnType == typeof(void))
            {
                emitter.Emit(OpCodes.Ldnull);
            }
            else
            {
                if (mustStoreRetVal)
                {
                    // Return value was stored in a local, load it before return
                    emitter.Emit(OpCodes.Ldloc, locals[locals.Length - 1]);
                }

                Adapter.DoBoxingIfNecessary(emitter, returnType);
            }

            emitter.Emit(OpCodes.Ret);

            return (MethodInvoker)dynamicMethod.CreateDelegate(typeof(MethodInvoker));
        }
    }

    /// <summary>
    /// Stores parameter related information.
    /// This structure should be used whenever a new type is adapted.
    /// For example, ManagementObjectAdapter uses this structure to store
    /// method parameter information.
    /// </summary>
    internal class ParameterInformation
    {
        internal Type parameterType;
        internal object defaultValue;
        internal bool isOptional;
        internal bool isByRef;
        internal bool isParamArray;

        internal ParameterInformation(System.Reflection.ParameterInfo parameter)
        {
            this.isOptional = parameter.IsOptional;
            this.defaultValue = parameter.DefaultValue;
            this.parameterType = parameter.ParameterType;
            if (this.parameterType.IsByRef)
            {
                this.isByRef = true;
                this.parameterType = this.parameterType.GetElementType();
            }
            else
            {
                this.isByRef = false;
            }
        }

        internal ParameterInformation(Type parameterType, bool isOptional, object defaultValue, bool isByRef)
        {
            this.parameterType = parameterType;
            this.isOptional = isOptional;
            this.defaultValue = defaultValue;
            this.isByRef = isByRef;
        }
    }

    /// <summary>
    /// This is the adapter used for all objects that don't match the appropriate types for other adapters.
    /// It uses reflection to retrieve property information.
    /// </summary>
    internal class DotNetAdapter : Adapter
    {
        #region auxiliary methods and classes

        private const BindingFlags instanceBindingFlags = (BindingFlags.FlattenHierarchy | BindingFlags.Public |
                                                              BindingFlags.IgnoreCase | BindingFlags.Instance);
        private const BindingFlags staticBindingFlags = (BindingFlags.FlattenHierarchy | BindingFlags.Public |
                                                              BindingFlags.IgnoreCase | BindingFlags.Static);
        private bool _isStatic;

        internal DotNetAdapter() { }

        internal DotNetAdapter(bool isStatic)
        {
            _isStatic = isStatic;
        }

        // This static is thread safe based on the lock in GetInstancePropertyReflectionTable
        /// <summary>
        /// CLR reflection property cache for instance properties
        /// </summary>
        private static Dictionary<Type, CacheTable> s_instancePropertyCacheTable = new Dictionary<Type, CacheTable>();

        // This static is thread safe based on the lock in GetStaticPropertyReflectionTable
        /// <summary>
        /// CLR reflection property cache for static properties
        /// </summary>
        private static Dictionary<Type, CacheTable> s_staticPropertyCacheTable = new Dictionary<Type, CacheTable>();

        // This static is thread safe based on the lock in GetInstanceMethodReflectionTable
        /// <summary>
        /// CLR reflection method cache for instance methods
        /// </summary>
        private static Dictionary<Type, CacheTable> s_instanceMethodCacheTable = new Dictionary<Type, CacheTable>();

        // This static is thread safe based on the lock in GetStaticMethodReflectionTable
        /// <summary>
        /// CLR reflection method cache for static methods
        /// </summary>
        private static Dictionary<Type, CacheTable> s_staticMethodCacheTable = new Dictionary<Type, CacheTable>();

        // This static is thread safe based on the lock in GetInstanceMethodReflectionTable
        /// <summary>
        /// CLR reflection method cache for instance events
        /// </summary>
        private static readonly Dictionary<Type, Dictionary<string, EventCacheEntry>> s_instanceEventCacheTable
            = new Dictionary<Type, Dictionary<string, EventCacheEntry>>();

        // This static is thread safe based on the lock in GetStaticMethodReflectionTable
        /// <summary>
        /// CLR reflection method cache for static events
        /// </summary>
        private static readonly Dictionary<Type, Dictionary<string, EventCacheEntry>> s_staticEventCacheTable
            = new Dictionary<Type, Dictionary<string, EventCacheEntry>>();

        internal class MethodCacheEntry
        {
            internal MethodInformation[] methodInformationStructures;

            internal MethodCacheEntry(MethodBase[] methods)
            {
                methodInformationStructures = DotNetAdapter.GetMethodInformationArray(methods);
            }

            internal MethodInformation this[int i]
            {
                get
                {
                    return methodInformationStructures[i];
                }
            }
        }

        internal class EventCacheEntry
        {
            internal EventInfo[] events;

            internal EventCacheEntry(EventInfo[] events)
            {
                this.events = events;
            }
        }

        internal class ParameterizedPropertyCacheEntry
        {
            internal MethodInformation[] getterInformation;
            internal MethodInformation[] setterInformation;
            internal string propertyName;
            internal bool readOnly;
            internal bool writeOnly;
            internal Type propertyType;
            // propertyDefinition is used as a string representation of the property
            internal string[] propertyDefinition;

            internal ParameterizedPropertyCacheEntry(List<PropertyInfo> properties)
            {
                PropertyInfo firstProperty = properties[0];
                this.propertyName = firstProperty.Name;
                this.propertyType = firstProperty.PropertyType;
                var getterList = new List<MethodInfo>();
                var setterList = new List<MethodInfo>();
                var definitionArray = new List<string>();

                for (int i = 0; i < properties.Count; i++)
                {
                    PropertyInfo property = properties[i];
                    // Properties can have different return types. If they do
                    // we pretend it is System.Object
                    if (property.PropertyType != this.propertyType)
                    {
                        this.propertyType = typeof(object);
                    }

                    // Get the public getter
                    MethodInfo propertyGetter = property.GetGetMethod();
                    StringBuilder definition = new StringBuilder();
                    StringBuilder extraDefinition = new StringBuilder();
                    if (propertyGetter != null)
                    {
                        extraDefinition.Append("get;");
                        definition.Append(DotNetAdapter.GetMethodInfoOverloadDefinition(this.propertyName, propertyGetter, 0));
                        getterList.Add(propertyGetter);
                    }

                    // Get the public setter
                    MethodInfo propertySetter = property.GetSetMethod();
                    if (propertySetter != null)
                    {
                        extraDefinition.Append("set;");
                        if (definition.Length == 0)
                        {
                            definition.Append(DotNetAdapter.GetMethodInfoOverloadDefinition(this.propertyName, propertySetter, 1));
                        }
                        setterList.Add(propertySetter);
                    }
                    definition.Append(" {");
                    definition.Append(extraDefinition);
                    definition.Append("}");
                    definitionArray.Add(definition.ToString());
                }
                propertyDefinition = definitionArray.ToArray();

                this.writeOnly = getterList.Count == 0;
                this.readOnly = setterList.Count == 0;

                this.getterInformation = new MethodInformation[getterList.Count];
                for (int i = 0; i < getterList.Count; i++)
                {
                    this.getterInformation[i] = new MethodInformation(getterList[i], 0);
                }

                this.setterInformation = new MethodInformation[setterList.Count];
                for (int i = 0; i < setterList.Count; i++)
                {
                    this.setterInformation[i] = new MethodInformation(setterList[i], 1);
                }
            }
        }

        internal class PropertyCacheEntry
        {
            internal delegate object GetterDelegate(object instance);
            internal delegate void SetterDelegate(object instance, object setValue);

            internal PropertyCacheEntry(PropertyInfo property)
            {
                this.member = property;
                this.propertyType = property.PropertyType;
                // Generating code for fields/properties in ValueTypes is complex and will probably
                // require different delegates
                // The same is true for generics, COM Types.
                TypeInfo declaringTypeInfo = property.DeclaringType.GetTypeInfo();
                TypeInfo propertyTypeInfo = property.PropertyType.GetTypeInfo();

                if (declaringTypeInfo.IsValueType ||
                    propertyTypeInfo.IsGenericType ||
                    declaringTypeInfo.IsGenericType ||
                    property.DeclaringType.IsComObject() ||
                    property.PropertyType.IsComObject())
                {
                    this.readOnly = property.GetSetMethod() == null;
                    this.writeOnly = property.GetGetMethod() == null;
                    this.useReflection = true;
                    return;
                }

                // Get the public or protected getter
                MethodInfo propertyGetter = property.GetGetMethod(true);
                if (propertyGetter != null && (propertyGetter.IsPublic || propertyGetter.IsFamily))
                {
                    this.isStatic = propertyGetter.IsStatic;
                    // Delegate is initialized later to avoid jit if it's not called
                }
                else
                {
                    this.writeOnly = true;
                }

                // Get the public or protected setter
                MethodInfo propertySetter = property.GetSetMethod(true);
                if (propertySetter != null && (propertySetter.IsPublic || propertySetter.IsFamily))
                {
                    this.isStatic = propertySetter.IsStatic;
                }
                else
                {
                    this.readOnly = true;
                }
            }

            internal PropertyCacheEntry(FieldInfo field)
            {
                this.member = field;
                this.isStatic = field.IsStatic;
                this.propertyType = field.FieldType;

                // const fields have no setter and we are getting them with GetValue instead of
                // using generated code. Init fields are only settable during initialization
                // then cannot be set afterwards..
                if (field.IsLiteral || field.IsInitOnly)
                {
                    this.readOnly = true;
                }
            }

            private void InitGetter()
            {
                if (writeOnly || useReflection)
                    return;

                var parameter = Expression.Parameter(typeof(object));
                Expression instance = null;

                var field = member as FieldInfo;
                if (field != null)
                {
                    var declaringType = field.DeclaringType;
                    var declaringTypeInfo = declaringType.GetTypeInfo();
                    if (!field.IsStatic)
                    {
                        if (declaringTypeInfo.IsValueType)
                        {
                            // I'm not sure we can get here with a Nullable, but if so,
                            // we must use the Value property, see PSGetMemberBinder.GetTargetValue.
                            instance = Nullable.GetUnderlyingType(declaringType) != null
                                ? (Expression)Expression.Property(parameter, "Value")
                                : Expression.Unbox(parameter, declaringType);
                        }
                        else
                        {
                            instance = parameter.Cast(declaringType);
                        }
                    }
                    Expression getterExpr;

                    if (declaringTypeInfo.IsGenericTypeDefinition)
                    {
                        Expression innerException = Expression.New(CachedReflectionInfo.GetValueException_ctor,
                            Expression.Constant("PropertyGetException"),
                            Expression.Constant(null, typeof(Exception)),
                            Expression.Constant(ParserStrings.PropertyInGenericType),
                            Expression.NewArrayInit(typeof(object), Expression.Constant(field.Name)));
                        getterExpr = Compiler.ThrowRuntimeErrorWithInnerException("PropertyGetException",
                                                                          Expression.Constant(ParserStrings.PropertyInGenericType),
                                                                          innerException, typeof(object), Expression.Constant(field.Name));
                    }
                    else
                    {
                        getterExpr = Expression.Field(instance, field).Cast(typeof(object));
                    }

                    _getterDelegate = Expression.Lambda<GetterDelegate>(getterExpr, parameter).Compile();
                    return;
                }

                var property = (PropertyInfo)member;
                var propertyGetter = property.GetGetMethod(true);

                instance = this.isStatic ? null : parameter.Cast(propertyGetter.DeclaringType);
                _getterDelegate = Expression.Lambda<GetterDelegate>(
                    Expression.Property(instance, property).Cast(typeof(object)), parameter).Compile();
            }

            private void InitSetter()
            {
                if (readOnly || useReflection)
                    return;

                var parameter = Expression.Parameter(typeof(object));
                var value = Expression.Parameter(typeof(object));
                Expression instance = null;

                var field = member as FieldInfo;
                if (field != null)
                {
                    var declaringType = field.DeclaringType;
                    var declaringTypeInfo = declaringType.GetTypeInfo();
                    if (!field.IsStatic)
                    {
                        if (declaringTypeInfo.IsValueType)
                        {
                            // I'm not sure we can get here with a Nullable, but if so,
                            // we must use the Value property, see PSGetMemberBinder.GetTargetValue.
                            instance = Nullable.GetUnderlyingType(declaringType) != null
                                ? (Expression)Expression.Property(parameter, "Value")
                                : Expression.Unbox(parameter, declaringType);
                        }
                        else
                        {
                            instance = parameter.Cast(declaringType);
                        }
                    }

                    Expression setterExpr;
                    string errMessage = null;
                    Type errType = field.FieldType;
                    if (declaringTypeInfo.IsGenericTypeDefinition)
                    {
                        errMessage = ParserStrings.PropertyInGenericType;
                        if (errType.GetTypeInfo().ContainsGenericParameters)
                        {
                            errType = typeof(object);
                        }
                    }
                    else if (readOnly)
                    {
                        errMessage = ParserStrings.PropertyIsReadOnly;
                    }

                    if (errMessage != null)
                    {
                        Expression innerException = Expression.New(CachedReflectionInfo.SetValueException_ctor,
                            Expression.Constant("PropertyAssignmentException"),
                            Expression.Constant(null, typeof(Exception)),
                            Expression.Constant(errMessage),
                            Expression.NewArrayInit(typeof(object), Expression.Constant(field.Name)));
                        setterExpr = Compiler.ThrowRuntimeErrorWithInnerException("PropertyAssignmentException",
                                                                                  Expression.Constant(errMessage),
                                                                                  innerException, errType, Expression.Constant(field.Name));
                    }
                    else
                    {
                        setterExpr = Expression.Assign(Expression.Field(instance, field), Expression.Convert(value, field.FieldType));
                    }
                    _setterDelegate = Expression.Lambda<SetterDelegate>(setterExpr, parameter, value).Compile();
                    return;
                }

                var property = (PropertyInfo)member;
                MethodInfo propertySetter = property.GetSetMethod(true);

                instance = this.isStatic ? null : parameter.Cast(propertySetter.DeclaringType);
                _setterDelegate =
                    Expression.Lambda<SetterDelegate>(
                        Expression.Assign(Expression.Property(instance, property),
                            Expression.Convert(value, property.PropertyType)), parameter, value).Compile();
            }


            internal MemberInfo member;

            internal GetterDelegate getterDelegate
            {
                get
                {
                    if (_getterDelegate == null)
                    {
                        InitGetter();
                    }
                    return _getterDelegate;
                }
            }
            private GetterDelegate _getterDelegate;

            internal SetterDelegate setterDelegate
            {
                get
                {
                    if (_setterDelegate == null)
                    {
                        InitSetter();
                    }

                    return _setterDelegate;
                }
            }
            private SetterDelegate _setterDelegate;

            internal bool useReflection;
            internal bool readOnly;
            internal bool writeOnly;
            internal bool isStatic;
            internal Type propertyType;

            private AttributeCollection _attributes;
            internal AttributeCollection Attributes
            {
                get
                {
                    if (_attributes == null)
                    {
                        // Since AttributeCollection can only be constructed with an Attribute[], one is built.
                        var objAttributes = this.member.GetCustomAttributes(true);
                        _attributes = new AttributeCollection(objAttributes.Cast<Attribute>().ToArray());
                    }
                    return _attributes;
                }
            }
        }

        /// <summary>
        /// Compare the signatures of the methods, returning true if the methods have
        /// the same signature.
        /// </summary>
        private static bool SameSignature(MethodBase method1, MethodBase method2)
        {
            if (method1.GetGenericArguments().Length != method2.GetGenericArguments().Length)
            {
                return false;
            }
            ParameterInfo[] parameters1 = method1.GetParameters();
            ParameterInfo[] parameters2 = method2.GetParameters();
            if (parameters1.Length != parameters2.Length)
            {
                return false;
            }
            for (int i = 0; i < parameters1.Length; ++i)
            {
                if (parameters1[i].ParameterType != parameters2[i].ParameterType
                    || parameters1[i].IsOut != parameters2[i].IsOut
                    || parameters1[i].IsOptional != parameters2[i].IsOptional)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Adds an overload to a list of MethodInfo.  Before adding to the list, the
        /// list is searched to make sure we don't end up with 2 functions with the
        /// same signature.  This can happen when there is a newslot method.
        /// </summary>
        private static void AddOverload(List<MethodBase> previousMethodEntry, MethodInfo method)
        {
            bool add = true;

            for (int i = 0; i < previousMethodEntry.Count; i++)
            {
                if (SameSignature(previousMethodEntry[i], method))
                {
                    add = false;
                    break;
                }
            }

            if (add)
            {
                previousMethodEntry.Add(method);
            }
        }

        private static void PopulateMethodReflectionTable(Type type, MethodInfo[] methods, CacheTable typeMethods)
        {
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.DeclaringType == type)
                {
                    string methodName = method.Name;
                    var previousMethodEntry = (List<MethodBase>)typeMethods[methodName];
                    if (previousMethodEntry == null)
                    {
                        var methodEntry = new List<MethodBase> { method };
                        typeMethods.Add(methodName, methodEntry);
                    }
                    else
                    {
                        AddOverload(previousMethodEntry, method);
                    }
                }
            }

            var typeInfo = type.GetTypeInfo();
            if (typeInfo.BaseType != null)
            {
                PopulateMethodReflectionTable(typeInfo.BaseType, methods, typeMethods);
            }
        }

        private static void PopulateMethodReflectionTable(ConstructorInfo[] ctors, CacheTable typeMethods)
        {
            foreach (var ctor in ctors)
            {
                var previousMethodEntry = (List<MethodBase>)typeMethods["new"];
                if (previousMethodEntry == null)
                {
                    var methodEntry = new List<MethodBase>();
                    methodEntry.Add(ctor);
                    typeMethods.Add("new", methodEntry);
                }
                else
                {
                    previousMethodEntry.Add(ctor);
                }
            }
        }

        /// <summary>
        /// Called from GetMethodReflectionTable within a lock to fill the
        /// method cache table
        /// </summary>
        /// <param name="type">type to get methods from</param>
        /// <param name="typeMethods">table to be filled</param>
        /// <param name="bindingFlags">bindingFlags to use</param>
        private static void PopulateMethodReflectionTable(Type type, CacheTable typeMethods, BindingFlags bindingFlags)
        {
            var typeInfo = type.GetTypeInfo();
            Type typeToGetMethod = type;
#if CORECLR 
            // Assemblies in CoreCLR might not allow reflection execution on their internal types. In such case, we walk up 
            // the derivation chain to find the first public parent, and use reflection methods on the public parent.
            if (!TypeResolver.IsPublic(typeInfo) && DisallowPrivateReflection(typeInfo))
            {
                typeToGetMethod = GetFirstPublicParentType(typeInfo);
            }
#endif
            // In CoreCLR, "GetFirstPublicParentType" may return null if 'type' is an interface
            if (typeToGetMethod != null)
            {
                MethodInfo[] methods = typeToGetMethod.GetMethods(bindingFlags);
                PopulateMethodReflectionTable(typeToGetMethod, methods, typeMethods);
            }

            Type[] interfaces = type.GetInterfaces();
            for (int interfaceIndex = 0; interfaceIndex < interfaces.Length; interfaceIndex++)
            {
                var interfaceType = interfaces[interfaceIndex];
                var interfaceTypeInfo = interfaceType.GetTypeInfo();
                if (!TypeResolver.IsPublic(interfaceTypeInfo))
                {
                    continue;
                }

                if (interfaceTypeInfo.IsGenericType && type.IsArray)
                {
                    continue; // GetInterfaceMap is not supported in this scenario... not sure if we need to do something special here...
                }

                MethodInfo[] methods;
                if (typeInfo.IsInterface)
                {
                    methods = interfaceType.GetMethods(bindingFlags);
                }
                else
                {
                    InterfaceMapping interfaceMapping = typeInfo.GetRuntimeInterfaceMap(interfaceType);
                    methods = interfaceMapping.InterfaceMethods;
                }
                for (int methodIndex = 0; methodIndex < methods.Length; methodIndex++)
                {
                    MethodInfo interfaceMethodDefinition = methods[methodIndex];

                    if ((!interfaceMethodDefinition.IsPublic) ||
                        (interfaceMethodDefinition.IsStatic != ((BindingFlags.Static & bindingFlags) != 0)))
                    {
                        continue;
                    }

                    var previousMethodEntry = (List<MethodBase>)typeMethods[interfaceMethodDefinition.Name];
                    if (previousMethodEntry == null)
                    {
                        var methodEntry = new List<MethodBase> { interfaceMethodDefinition };
                        typeMethods.Add(interfaceMethodDefinition.Name, methodEntry);
                    }
                    else
                    {
                        if (!previousMethodEntry.Contains(interfaceMethodDefinition))
                        {
                            previousMethodEntry.Add(interfaceMethodDefinition);
                        }
                    }
                }
            }

            if ((bindingFlags & BindingFlags.Static) != 0 && TypeResolver.IsPublic(typeInfo))
            {
                // We don't add constructors if there was a static method named new.
                // We don't add constructors if the target type is not public, because it's useless to an internal type.
                var previousMethodEntry = (List<MethodBase>)typeMethods["new"];
                if (previousMethodEntry == null)
                {
                    var ctorBindingFlags = bindingFlags & ~(BindingFlags.FlattenHierarchy | BindingFlags.Static);
                    ctorBindingFlags |= BindingFlags.Instance;
                    var ctorInfos = type.GetConstructors(ctorBindingFlags);
                    PopulateMethodReflectionTable(ctorInfos, typeMethods);
                }
            }

            for (int i = 0; i < typeMethods.memberCollection.Count; i++)
            {
                typeMethods.memberCollection[i] =
                    new MethodCacheEntry(((List<MethodBase>)typeMethods.memberCollection[i]).ToArray());
            }
        }

        /// <summary>
        /// Called from GetEventReflectionTable within a lock to fill the
        /// event cache table
        /// </summary>
        /// <param name="type">type to get events from</param>
        /// <param name="typeEvents">table to be filled</param>
        /// <param name="bindingFlags">bindingFlags to use</param>
        private static void PopulateEventReflectionTable(Type type, Dictionary<string, EventCacheEntry> typeEvents, BindingFlags bindingFlags)
        {
#if CORECLR 
            // Assemblies in CoreCLR might not allow reflection execution on their internal types. In such case, we walk up 
            // the derivation chain to find the first public parent, and use reflection events on the public parent.
            TypeInfo typeInfo = type.GetTypeInfo();
            if (!TypeResolver.IsPublic(typeInfo) && DisallowPrivateReflection(typeInfo))
            {
                type = GetFirstPublicParentType(typeInfo);
            }
#endif
            // In CoreCLR, "GetFirstPublicParentType" may return null if 'type' is an interface
            if (type != null)
            {
                EventInfo[] events = type.GetEvents(bindingFlags);
                var tempTable = new Dictionary<string, List<EventInfo>>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < events.Length; i++)
                {
                    var typeEvent = events[i];
                    string eventName = typeEvent.Name;
                    List<EventInfo> previousEntry;
                    if (!tempTable.TryGetValue(eventName, out previousEntry))
                    {
                        var eventEntry = new List<EventInfo> { typeEvent };
                        tempTable.Add(eventName, eventEntry);
                    }
                    else
                    {
                        previousEntry.Add(typeEvent);
                    }
                }

                foreach (var entry in tempTable)
                {
                    typeEvents.Add(entry.Key, new EventCacheEntry(entry.Value.ToArray()));
                }
            }
        }

        /// <summary>
        /// This method is necessary becausean overridden property in a specific class derived from a generic one will
        /// appear twice. The second time, it should be ignored.
        /// </summary>
        private static bool PropertyAlreadyPresent(List<PropertyInfo> previousProperties, PropertyInfo property)
        {
            // The loop below 
            bool returnValue = false;
            ParameterInfo[] propertyParameters = property.GetIndexParameters();
            int propertyIndexLength = propertyParameters.Length;

            for (int propertyIndex = 0; propertyIndex < previousProperties.Count; propertyIndex++)
            {
                var previousProperty = previousProperties[propertyIndex];
                ParameterInfo[] previousParameters = previousProperty.GetIndexParameters();
                if (previousParameters.Length == propertyIndexLength)
                {
                    bool parametersAreSame = true;
                    for (int parameterIndex = 0; parameterIndex < previousParameters.Length; parameterIndex++)
                    {
                        ParameterInfo previousParameter = previousParameters[parameterIndex];
                        ParameterInfo propertyParameter = propertyParameters[parameterIndex];
                        if (previousParameter.ParameterType != propertyParameter.ParameterType)
                        {
                            parametersAreSame = false;
                            break;
                        }
                    }
                    if (parametersAreSame)
                    {
                        returnValue = true;
                        break;
                    }
                }
            }

            return returnValue;
        }

        /// <summary>
        /// Called from GetPropertyReflectionTable within a lock to fill the
        /// property cache table
        /// </summary>
        /// <param name="type">type to get properties from</param>
        /// <param name="typeProperties">table to be filled</param>
        /// <param name="bindingFlags">bindingFlags to use</param>
        private static void PopulatePropertyReflectionTable(Type type, CacheTable typeProperties, BindingFlags bindingFlags)
        {
            var tempTable = new Dictionary<string, List<PropertyInfo>>(StringComparer.OrdinalIgnoreCase);
            Type typeToGetPropertyAndField = type;
#if CORECLR 
            // Assemblies in CoreCLR might not allow reflection execution on their internal types. In such case, we walk up the
            // derivation chain to find the first public parent, and use reflection properties/fileds on the public parent.
            TypeInfo typeInfo = type.GetTypeInfo();
            if (!TypeResolver.IsPublic(typeInfo) && DisallowPrivateReflection(typeInfo))
            {
                typeToGetPropertyAndField = GetFirstPublicParentType(typeInfo);
            }
#endif
            // In CoreCLR, "GetFirstPublicParentType" may return null if 'type' is an interface
            PropertyInfo[] properties;
            if (typeToGetPropertyAndField != null)
            {
                properties = typeToGetPropertyAndField.GetProperties(bindingFlags);
                for (int i = 0; i < properties.Length; i++)
                {
                    PopulateSingleProperty(type, properties[i], tempTable, properties[i].Name);
                }
            }

            Type[] interfaces = type.GetInterfaces();
            for (int interfaceIndex = 0; interfaceIndex < interfaces.Length; interfaceIndex++)
            {
                Type interfaceType = interfaces[interfaceIndex];
                if (!TypeResolver.IsPublic(interfaceType))
                {
                    continue;
                }

                properties = interfaceType.GetProperties(bindingFlags);
                for (int propertyIndex = 0; propertyIndex < properties.Length; propertyIndex++)
                {
                    PopulateSingleProperty(type, properties[propertyIndex], tempTable, properties[propertyIndex].Name);
                }
            }

            foreach (var pairs in tempTable)
            {
                var propertiesList = pairs.Value;
                PropertyInfo firstProperty = propertiesList[0];
                if ((propertiesList.Count > 1) || (firstProperty.GetIndexParameters().Length != 0))
                {
                    typeProperties.Add(pairs.Key, new ParameterizedPropertyCacheEntry(propertiesList));
                }
                else
                {
                    typeProperties.Add(pairs.Key, new PropertyCacheEntry(firstProperty));
                }
            }

            // In CoreCLR, "GetFirstPublicParentType" may return null if 'type' is an interface
            if (typeToGetPropertyAndField != null)
            {
                FieldInfo[] fields = typeToGetPropertyAndField.GetFields(bindingFlags);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    string fieldName = field.Name;
                    var previousMember = (PropertyCacheEntry)typeProperties[fieldName];
                    if (previousMember == null)
                    {
                        typeProperties.Add(fieldName, new PropertyCacheEntry(field));
                    }
                    else
                    {
                        // A property/field declared with new in a derived class might appear twice
                        if (!String.Equals(previousMember.member.Name, fieldName))
                        {
                            throw new ExtendedTypeSystemException("NotACLSComplaintField", null,
                                ExtendedTypeSystem.NotAClsCompliantFieldProperty, fieldName, type.FullName, previousMember.member.Name);
                        }
                    }
                }
            }
        }

        private static void PopulateSingleProperty(Type type, PropertyInfo property, Dictionary<string, List<PropertyInfo>> tempTable, string propertyName)
        {
            List<PropertyInfo> previousPropertyEntry;
            if (!tempTable.TryGetValue(propertyName, out previousPropertyEntry))
            {
                previousPropertyEntry = new List<PropertyInfo> { property };
                tempTable.Add(propertyName, previousPropertyEntry);
            }
            else
            {
                var firstProperty = previousPropertyEntry[0];
                if (!String.Equals(property.Name, firstProperty.Name, StringComparison.Ordinal))
                {
                    throw new ExtendedTypeSystemException("NotACLSComplaintProperty", null,
                                                          ExtendedTypeSystem.NotAClsCompliantFieldProperty, property.Name, type.FullName, firstProperty.Name);
                }

                if (PropertyAlreadyPresent(previousPropertyEntry, property))
                {
                    return;
                }

                previousPropertyEntry.Add(property);
            }
        }

        #region Handle_Internal_Type_Reflection_In_CoreCLR
#if CORECLR
        /// <summary>
        /// The dictionary cache about if an assembly supports reflection execution on its internal types.
        /// </summary>
        private static readonly ConcurrentDictionary<string, bool> s_disallowReflectionCache =
            new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Check if the type is defined in an assembly that disallows reflection execution on internal types.
        ///  - .NET Framework assemblies don't support reflection execution on their internal types.
        /// </summary>
        internal static bool DisallowPrivateReflection(TypeInfo typeInfo)
        {
            bool disallowReflection = false;
            Assembly assembly = typeInfo.Assembly;
            if (s_disallowReflectionCache.TryGetValue(assembly.FullName, out disallowReflection))
            {
                return disallowReflection;
            }

            var productAttribute = assembly.GetCustomAttribute<AssemblyProductAttribute>();
            if (productAttribute != null && string.Equals(productAttribute.Product, "Microsoft� .NET Framework", StringComparison.OrdinalIgnoreCase))
            {
                disallowReflection = true;
            }
            else
            {
                // Check for 'DisablePrivateReflectionAttribute'. It's applied at the assembly level, and allow an assembly to opt-out of private/internal reflection.
                var disablePrivateReflectionAttribute = assembly.GetCustomAttribute<System.Runtime.CompilerServices.DisablePrivateReflectionAttribute>();
                disallowReflection = disablePrivateReflectionAttribute != null;
            }

            s_disallowReflectionCache.TryAdd(assembly.FullName, disallowReflection);
            return disallowReflection;
        }

        /// <summary>
        /// Walk up the derivation chain to find the first public parent type.
        /// </summary>
        internal static Type GetFirstPublicParentType(TypeInfo typeInfo)
        {
            Dbg.Assert(!TypeResolver.IsPublic(typeInfo), "typeInfo should not be public.");
            Type parent = typeInfo.BaseType;
            while (parent != null)
            {
                TypeInfo parentTypeInfo = parent.GetTypeInfo();
                if (parentTypeInfo.IsPublic)
                {
                    return parent;
                }
                parent = parentTypeInfo.BaseType;
            }

            // Return null when typeInfo is an interface
            return null;
        }
#endif
        #endregion Handle_Internal_Type_Reflection_In_CoreCLR

        /// <summary>
        /// Called from GetProperty and GetProperties to populate the
        /// typeTable with all public properties and fields
        /// of type.
        /// </summary>
        /// <param name="type">type to load properties for</param>
        private static CacheTable GetStaticPropertyReflectionTable(Type type)
        {
            lock (s_staticPropertyCacheTable)
            {
                CacheTable typeTable = null;
                if (s_staticPropertyCacheTable.TryGetValue(type, out typeTable))
                {
                    return typeTable;
                }

                typeTable = new CacheTable();
                PopulatePropertyReflectionTable(type, typeTable, staticBindingFlags);
                s_staticPropertyCacheTable[type] = typeTable;
                return typeTable;
            }
        }

        /// <summary>
        /// Retrieves the table for static methods
        /// </summary>
        /// <param name="type">type to load methods for</param>
        private static CacheTable GetStaticMethodReflectionTable(Type type)
        {
            lock (s_staticMethodCacheTable)
            {
                CacheTable typeTable = null;
                if (s_staticMethodCacheTable.TryGetValue(type, out typeTable))
                {
                    return typeTable;
                }

                typeTable = new CacheTable();
                PopulateMethodReflectionTable(type, typeTable, staticBindingFlags);
                s_staticMethodCacheTable[type] = typeTable;
                return typeTable;
            }
        }

        /// <summary>
        /// Retrieves the table for static events
        /// </summary>
        /// <param name="type">type containing properties to load in typeTable</param>
        private static Dictionary<string, EventCacheEntry> GetStaticEventReflectionTable(Type type)
        {
            lock (s_staticEventCacheTable)
            {
                Dictionary<string, EventCacheEntry> typeTable;
                if (s_staticEventCacheTable.TryGetValue(type, out typeTable))
                {
                    return typeTable;
                }
                typeTable = new Dictionary<string, EventCacheEntry>();
                PopulateEventReflectionTable(type, typeTable, staticBindingFlags);
                s_staticEventCacheTable[type] = typeTable;
                return typeTable;
            }
        }

        /// <summary>
        /// Called from GetProperty and GetProperties to populate the
        /// typeTable with all public properties and fields
        /// of type.
        /// </summary>
        /// <param name="type">type with properties to load in typeTable</param>
        private static CacheTable GetInstancePropertyReflectionTable(Type type)
        {
            lock (s_instancePropertyCacheTable)
            {
                CacheTable typeTable = null;
                if (s_instancePropertyCacheTable.TryGetValue(type, out typeTable))
                {
                    return typeTable;
                }
                typeTable = new CacheTable();
                PopulatePropertyReflectionTable(type, typeTable, instanceBindingFlags);
                s_instancePropertyCacheTable[type] = typeTable;
                return typeTable;
            }
        }

        /// <summary>
        /// Retrieves the table for instance methods
        /// </summary>
        /// <param name="type">type with methods to load in typeTable</param>
        private static CacheTable GetInstanceMethodReflectionTable(Type type)
        {
            lock (s_instanceMethodCacheTable)
            {
                CacheTable typeTable = null;
                if (s_instanceMethodCacheTable.TryGetValue(type, out typeTable))
                {
                    return typeTable;
                }
                typeTable = new CacheTable();
                PopulateMethodReflectionTable(type, typeTable, instanceBindingFlags);
                s_instanceMethodCacheTable[type] = typeTable;
                return typeTable;
            }
        }

        internal IEnumerable<object> GetPropertiesAndMethods(Type type, bool @static)
        {
            CacheTable propertyTable = @static
                ? GetStaticPropertyReflectionTable(type)
                : GetInstancePropertyReflectionTable(type);
            for (int i = 0; i < propertyTable.memberCollection.Count; i++)
            {
                var propertyCacheEntry = propertyTable.memberCollection[i] as PropertyCacheEntry;
                if (propertyCacheEntry != null)
                    yield return propertyCacheEntry.member;
            }

            CacheTable methodTable = @static
                ? GetStaticMethodReflectionTable(type)
                : GetInstanceMethodReflectionTable(type);
            for (int i = 0; i < methodTable.memberCollection.Count; i++)
            {
                var method = methodTable.memberCollection[i] as MethodCacheEntry;
                if (method != null && !method[0].method.IsSpecialName)
                {
                    yield return method;
                }
            }
        }

        /// <summary>
        /// Retrieves the table for instance events
        /// </summary>
        /// <param name="type">type containing methods to load in typeTable</param>
        private static Dictionary<string, EventCacheEntry> GetInstanceEventReflectionTable(Type type)
        {
            lock (s_instanceEventCacheTable)
            {
                Dictionary<string, EventCacheEntry> typeTable;
                if (s_instanceEventCacheTable.TryGetValue(type, out typeTable))
                {
                    return typeTable;
                }
                typeTable = new Dictionary<string, EventCacheEntry>(StringComparer.OrdinalIgnoreCase);
                PopulateEventReflectionTable(type, typeTable, instanceBindingFlags);
                s_instanceEventCacheTable[type] = typeTable;
                return typeTable;
            }
        }

        /// <summary>
        /// Returns true if a parameterized property should be in a PSMemberInfoCollection of type t
        /// </summary>
        /// <param name="t">Type of a PSMemberInfoCollection like the type of T in PSMemberInfoCollection of T</param>
        /// <returns>true if a parameterized property should be in a collection</returns>
        /// <remarks>
        /// Usually typeof(T).IsAssignableFrom(typeof(PSParameterizedProperty)) would work like it does
        /// for PSMethod and PSProperty, but since PSParameterizedProperty derives from PSMethodInfo and
        /// since we don't want to have ParameterizedProperties in PSMemberInfoCollection of PSMethodInfo
        /// we need this method.
        /// </remarks>
        internal static bool IsTypeParameterizedProperty(Type t)
        {
            return t == typeof(PSMemberInfo) || t == typeof(PSParameterizedProperty);
        }

        internal T GetDotNetProperty<T>(object obj, string propertyName) where T : PSMemberInfo
        {
            bool lookingForProperties = typeof(T).IsAssignableFrom(typeof(PSProperty));
            bool lookingForParameterizedProperties = IsTypeParameterizedProperty(typeof(T));
            if (!lookingForProperties && !lookingForParameterizedProperties)
            {
                return null;
            }

            CacheTable typeTable = _isStatic
                ? GetStaticPropertyReflectionTable((Type)obj)
                : GetInstancePropertyReflectionTable(obj.GetType());

            object entry = typeTable[propertyName];
            if (entry == null)
            {
                return null;
            }

            var propertyEntry = entry as PropertyCacheEntry;
            if (propertyEntry != null && lookingForProperties)
            {
                var isHidden = propertyEntry.member.GetCustomAttributes(typeof(HiddenAttribute), false).Any();
                return new PSProperty(propertyEntry.member.Name, this, obj, propertyEntry) { IsHidden = isHidden } as T;
            }

            var parameterizedPropertyEntry = entry as ParameterizedPropertyCacheEntry;
            if (parameterizedPropertyEntry != null && lookingForParameterizedProperties)
            {
                // TODO: check for HiddenAttribute
                // We can't currently write a parameterized property in a PowerShell class so this isn't too important,
                // but if someone added the attribute to their C#, it'd be good to set isHidden correctly here.
                return new PSParameterizedProperty(parameterizedPropertyEntry.propertyName,
                    this, obj, parameterizedPropertyEntry) as T;
            }
            return null;
        }

        internal T GetDotNetMethod<T>(object obj, string methodName) where T : PSMemberInfo
        {
            if (!typeof(T).IsAssignableFrom(typeof(PSMethod)))
            {
                return null;
            }

            CacheTable typeTable = _isStatic
                ? GetStaticMethodReflectionTable((Type)obj)
                : GetInstanceMethodReflectionTable(obj.GetType());

            var methods = (MethodCacheEntry)typeTable[methodName];
            if (methods == null)
            {
                return null;
            }
            var isCtor = methods[0].method is ConstructorInfo;
            bool isSpecial = !isCtor && methods[0].method.IsSpecialName;
            bool isHidden = false;
            foreach (var method in methods.methodInformationStructures)
            {
                if (method.method.GetCustomAttributes(typeof(HiddenAttribute), false).Any())
                {
                    isHidden = true;
                    break;
                }
            }
            return new PSMethod(methods[0].method.Name, this, obj, methods, isSpecial, isHidden) as T;
        }

        internal void AddAllProperties<T>(object obj, PSMemberInfoInternalCollection<T> members, bool ignoreDuplicates) where T : PSMemberInfo
        {
            bool lookingForProperties = typeof(T).IsAssignableFrom(typeof(PSProperty));
            bool lookingForParameterizedProperties = IsTypeParameterizedProperty(typeof(T));
            if (!lookingForProperties && !lookingForParameterizedProperties)
            {
                return;
            }

            CacheTable table = _isStatic
                ? GetStaticPropertyReflectionTable((Type)obj)
                : GetInstancePropertyReflectionTable(obj.GetType());

            for (int i = 0; i < table.memberCollection.Count; i++)
            {
                var propertyEntry = table.memberCollection[i] as PropertyCacheEntry;
                if (propertyEntry != null)
                {
                    if (lookingForProperties)
                    {
                        if (!ignoreDuplicates || (members[propertyEntry.member.Name] == null))
                        {
                            var isHidden = propertyEntry.member.GetCustomAttributes(typeof(HiddenAttribute), false).Any();
                            members.Add(new PSProperty(propertyEntry.member.Name, this,
                                obj, propertyEntry)
                            { IsHidden = isHidden } as T);
                        }
                    }
                }
                else if (lookingForParameterizedProperties)
                {
                    var parameterizedPropertyEntry = (ParameterizedPropertyCacheEntry)table.memberCollection[i];
                    if (!ignoreDuplicates || (members[parameterizedPropertyEntry.propertyName] == null))
                    {
                        // TODO: check for HiddenAttribute
                        // We can't currently write a parameterized property in a PowerShell class so this isn't too important,
                        // but if someone added the attribute to their C#, it'd be good to set isHidden correctly here.
                        members.Add(new PSParameterizedProperty(parameterizedPropertyEntry.propertyName,
                            this, obj, parameterizedPropertyEntry) as T);
                    }
                }
            }
        }

        internal void AddAllMethods<T>(object obj, PSMemberInfoInternalCollection<T> members, bool ignoreDuplicates) where T : PSMemberInfo
        {
            if (!typeof(T).IsAssignableFrom(typeof(PSMethod)))
            {
                return;
            }

            CacheTable table = _isStatic
                ? GetStaticMethodReflectionTable((Type)obj)
                : GetInstanceMethodReflectionTable(obj.GetType());

            for (int i = 0; i < table.memberCollection.Count; i++)
            {
                var method = (MethodCacheEntry)table.memberCollection[i];
                var isCtor = method[0].method is ConstructorInfo;
                var name = isCtor ? "new" : method[0].method.Name;

                if (!ignoreDuplicates || (members[name] == null))
                {
                    bool isSpecial = !isCtor && method[0].method.IsSpecialName;
                    bool isHidden = false;
                    foreach (var m in method.methodInformationStructures)
                    {
                        if (m.method.GetCustomAttributes(typeof(HiddenAttribute), false).Any())
                        {
                            isHidden = true;
                            break;
                        }
                    }
                    members.Add(new PSMethod(name, this, obj, method, isSpecial, isHidden) as T);
                }
            }
        }

        internal void AddAllEvents<T>(object obj, PSMemberInfoInternalCollection<T> members, bool ignoreDuplicates) where T : PSMemberInfo
        {
            if (!typeof(T).IsAssignableFrom(typeof(PSEvent)))
            {
                return;
            }

            var table = _isStatic
                ? GetStaticEventReflectionTable((Type)obj)
                : GetInstanceEventReflectionTable(obj.GetType());

            foreach (var psEvent in table.Values)
            {
                if (!ignoreDuplicates || (members[psEvent.events[0].Name] == null))
                {
                    members.Add(new PSEvent(psEvent.events[0]) as T);
                }
            }
        }

        internal void AddAllDynamicMembers<T>(object obj, PSMemberInfoInternalCollection<T> members, bool ignoreDuplicates) where T : PSMemberInfo
        {
            var idmop = obj as IDynamicMetaObjectProvider;
            if (idmop == null || obj is PSObject)
            {
                return;
            }
            if (!typeof(T).IsAssignableFrom(typeof(PSDynamicMember)))
            {
                return;
            }

            foreach (var name in idmop.GetMetaObject(Expression.Variable(idmop.GetType())).GetDynamicMemberNames())
            {
                members.Add(new PSDynamicMember(name) as T);
            }
        }

        private static bool PropertyIsStatic(PSProperty property)
        {
            PropertyCacheEntry entry = property.adapterData as PropertyCacheEntry;
            if (entry == null)
            {
                return false;
            }
            return entry.isStatic;
        }

        #endregion auxiliary methods and classes

        #region virtual

        #region member

        internal override bool SiteBinderCanOptimize { get { return true; } }

        private static ConcurrentDictionary<Type, ConsolidatedString> s_typeToTypeNameDictionary =
            new ConcurrentDictionary<Type, ConsolidatedString>();

        internal static ConsolidatedString GetInternedTypeNameHierarchy(Type type)
        {
            return s_typeToTypeNameDictionary.GetOrAdd(type,
                                                     t => new ConsolidatedString(GetDotNetTypeNameHierarchy(t), interned: true));
        }

        protected override ConsolidatedString GetInternedTypeNameHierarchy(object obj)
        {
            return GetInternedTypeNameHierarchy(obj.GetType());
        }

        /// <summary>
        /// Returns null if memberName is not a member in the adapter or
        /// the corresponding PSMemberInfo
        /// </summary>
        /// <param name="obj">object to retrieve the PSMemberInfo from</param>
        /// <param name="memberName">name of the member to be retrieved</param>
        /// <returns>The PSMemberInfo corresponding to memberName from obj</returns>
        protected override T GetMember<T>(object obj, string memberName)
        {
            T returnValue = GetDotNetProperty<T>(obj, memberName);
            if (returnValue != null) return returnValue;
            return GetDotNetMethod<T>(obj, memberName);
        }

        /// <summary>
        /// Retrieves all the members available in the object.
        /// The adapter implementation is encouraged to cache all properties/methods available
        /// in the first call to GetMember and GetMembers so that subsequent
        /// calls can use the cache.
        /// In the case of the .NET adapter that would be a cache from the .NET type to
        /// the public properties and fields available in that type. 
        /// In the case of the DirectoryEntry adapter, this could be a cache of the objectClass
        /// to the properties available in it.
        /// </summary>
        /// <param name="obj">object to get all the member information from</param>
        /// <returns>all members in obj</returns>
        protected override PSMemberInfoInternalCollection<T> GetMembers<T>(object obj)
        {
            PSMemberInfoInternalCollection<T> returnValue = new PSMemberInfoInternalCollection<T>();
            AddAllProperties<T>(obj, returnValue, false);
            AddAllMethods<T>(obj, returnValue, false);
            AddAllEvents<T>(obj, returnValue, false);
            AddAllDynamicMembers(obj, returnValue, false);

            return returnValue;
        }

        #endregion member

        #region property

        /// <summary>
        /// Returns an array with the property attributes
        /// </summary>
        /// <param name="property">property we want the attributes from</param>
        /// <returns>an array with the property attributes</returns>
        protected override AttributeCollection PropertyAttributes(PSProperty property)
        {
            PropertyCacheEntry adapterData = (PropertyCacheEntry)property.adapterData;
            return adapterData.Attributes;
        }

        /// <summary>
        /// Returns the string representation of the property in the object
        /// </summary>
        /// <param name="property">property obtained in a previous GetMember</param>
        /// <returns>the string representation of the property in the object</returns>
        protected override string PropertyToString(PSProperty property)
        {
            StringBuilder returnValue = new StringBuilder();
            if (PropertyIsStatic(property))
            {
                returnValue.Append("static ");
            }

            returnValue.Append(PropertyType(property, forDisplay: true));
            returnValue.Append(" ");
            returnValue.Append(property.Name);
            returnValue.Append(" {");
            if (PropertyIsGettable(property))
            {
                returnValue.Append("get;");
            }
            if (PropertyIsSettable(property))
            {
                returnValue.Append("set;");
            }
            returnValue.Append("}");
            return returnValue.ToString();
        }

        /// <summary>
        /// Returns the value from a property coming from a previous call to GetMember
        /// </summary>
        /// <param name="property">PSProperty coming from a previous call to GetMember</param>
        /// <returns>The value of the property</returns>
        protected override object PropertyGet(PSProperty property)
        {
            PropertyCacheEntry adapterData = (PropertyCacheEntry)property.adapterData;
            PropertyInfo propertyInfo = adapterData.member as PropertyInfo;
            if (propertyInfo != null)
            {
                if (adapterData.writeOnly)
                {
                    throw new GetValueException("WriteOnlyProperty",
                        null,
                        ExtendedTypeSystem.WriteOnlyProperty,
                        propertyInfo.Name);
                }
                if (adapterData.useReflection)
                {
                    return propertyInfo.GetValue(property.baseObject, null);
                }
                else
                {
                    return adapterData.getterDelegate(property.baseObject);
                }
            }

            FieldInfo field = adapterData.member as FieldInfo;
            if (adapterData.useReflection)
            {
                return field.GetValue(property.baseObject);
            }
            else
            {
                return adapterData.getterDelegate(property.baseObject);
            }
        }

        /// <summary>
        /// Sets the value of a property coming from a previous call to GetMember
        /// </summary>
        /// <param name="property">PSProperty coming from a previous call to GetMember</param>
        /// <param name="setValue">value to set the property with</param>
        /// <param name="convertIfPossible">instructs the adapter to convert before setting, if the adapter supports conversion</param>
        protected override void PropertySet(PSProperty property, object setValue, bool convertIfPossible)
        {
            PropertyCacheEntry adapterData = (PropertyCacheEntry)property.adapterData;

            if (adapterData.readOnly)
            {
                throw new SetValueException("ReadOnlyProperty",
                    null,
                    ExtendedTypeSystem.ReadOnlyProperty,
                    adapterData.member.Name);
            }

            PropertyInfo propertyInfo = adapterData.member as PropertyInfo;
            if (propertyInfo != null)
            {
                if (convertIfPossible)
                {
                    setValue = PropertySetAndMethodArgumentConvertTo(setValue, propertyInfo.PropertyType, CultureInfo.InvariantCulture);
                }
                if (adapterData.useReflection)
                {
                    propertyInfo.SetValue(property.baseObject, setValue, null);
                }
                else
                {
                    adapterData.setterDelegate(property.baseObject, setValue);
                }
                return;
            }

            FieldInfo field = adapterData.member as FieldInfo;
            if (convertIfPossible)
            {
                setValue = PropertySetAndMethodArgumentConvertTo(setValue, field.FieldType, CultureInfo.InvariantCulture);
            }
            if (adapterData.useReflection)
            {
                field.SetValue(property.baseObject, setValue);
            }
            else
            {
                adapterData.setterDelegate(property.baseObject, setValue);
            }
        }

        /// <summary>
        /// Returns true if the property is settable
        /// </summary>
        /// <param name="property">property to check</param>
        /// <returns>true if the property is settable</returns>
        protected override bool PropertyIsSettable(PSProperty property)
        {
            return !((PropertyCacheEntry)property.adapterData).readOnly;
        }

        /// <summary>
        /// Returns true if the property is gettable
        /// </summary>
        /// <param name="property">property to check</param>
        /// <returns>true if the property is gettable</returns>
        protected override bool PropertyIsGettable(PSProperty property)
        {
            return !((PropertyCacheEntry)property.adapterData).writeOnly;
        }

        /// <summary>
        /// Returns the name of the type corresponding to the property's value
        /// </summary>
        /// <param name="property">PSProperty obtained in a previous GetMember</param>
        /// <param name="forDisplay">True if the result is for display purposes only</param>
        /// <returns>the name of the type corresponding to the member</returns>
        protected override string PropertyType(PSProperty property, bool forDisplay)
        {
            var propertyType = ((PropertyCacheEntry)property.adapterData).propertyType;
            return forDisplay ? ToStringCodeMethods.Type(propertyType) : propertyType.FullName;
        }

        #endregion property

        #region method

        #region auxiliary to method calling

        /// <summary>
        /// Calls constructor using the arguments and catching the appropriate exception
        /// </summary>
        /// <param name="arguments">final arguments to the constructor</param>
        /// <returns>the return of the constructor</returns>
        /// <param name="methodInformation">Information about the method to call. Used for setting references.</param>
        /// <param name="originalArguments">Original arguments in the method call. Used for setting references.</param>
        /// <exception cref="MethodInvocationException">if the constructor throws an exception</exception>
        internal static object AuxiliaryConstructorInvoke(MethodInformation methodInformation, object[] arguments, object[] originalArguments)
        {
            object returnValue;
#pragma warning disable 56500
            try
            {
                // We cannot call MethodBase's Invoke on a constructor
                // because it requires a target we don't have.
                returnValue = ((ConstructorInfo)methodInformation.method).Invoke(arguments);
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                throw new MethodInvocationException(
                    "DotNetconstructorTargetInvocation",
                    inner,
                    ExtendedTypeSystem.MethodInvocationException,
                    ".ctor", arguments.Length, inner.Message);
            }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                throw new MethodInvocationException(
                    "DotNetconstructorException",
                    e,
                    ExtendedTypeSystem.MethodInvocationException,
                    ".ctor", arguments.Length, e.Message);
            }
            SetReferences(arguments, methodInformation, originalArguments);
            return returnValue;
#pragma warning restore 56500
        }

        /// <summary>
        /// Calls method on target using the arguments and catching the appropriate exception
        /// </summary>
        /// <param name="target">object we want to call the method on</param>
        /// <param name="arguments">final arguments to the method</param>
        /// <param name="methodInformation">Information about the method to call. Used for setting references.</param>
        /// <param name="originalArguments">Original arguments in the method call. Used for setting references.</param>
        /// <returns>the return of the method</returns>
        /// <exception cref="MethodInvocationException">if the method throws an exception</exception>
        internal static object AuxiliaryMethodInvoke(object target, object[] arguments, MethodInformation methodInformation, object[] originalArguments)
        {
            object result;

#pragma warning disable 56500
            try
            {
                // call the method and return the result unless the return type is void in which
                // case we'll return AutomationNull.Value
                result = methodInformation.Invoke(target, arguments);
            }
            catch (TargetInvocationException ex)
            {
                // Special handling to allow methods to throw flowcontrol exceptions
                // Needed for ExitNestedPrompt exception.
                if (ex.InnerException is FlowControlException || ex.InnerException is ScriptCallDepthException)
                    throw ex.InnerException;
                // Win7:138054 - When wrapping cmdlets, we want the original exception to be raised,
                // not the wrapped exception that occurs from invoking a steppable pipeline method.
                if (ex.InnerException is ParameterBindingException)
                    throw ex.InnerException;

                Exception inner = ex.InnerException ?? ex;

                throw new MethodInvocationException(
                    "DotNetMethodTargetInvocation",
                    inner,
                    ExtendedTypeSystem.MethodInvocationException,
                    methodInformation.method.Name, arguments.Length, inner.Message);
            }
            //
            // Note that FlowControlException, ScriptCallDepthException and ParameterBindingException will be wrapped in 
            // a TargetInvocationException only when the invocation uses reflection so we need to bubble them up here as well.
            //
            catch (ParameterBindingException) { throw; }
            catch (FlowControlException) { throw; }
            catch (ScriptCallDepthException) { throw; }
            catch (PipelineStoppedException) { throw; }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);

                if (methodInformation.method.DeclaringType == typeof(SteppablePipeline) &&
                    (methodInformation.method.Name.Equals("Begin") ||
                     methodInformation.method.Name.Equals("Process") ||
                     methodInformation.method.Name.Equals("End")))
                {
                    // Don't wrap exceptions that happen when calling methods on SteppablePipeline
                    // that are only used for proxy commands.
                    throw;
                }

                throw new MethodInvocationException(
                    "DotNetMethodException",
                    e,
                    ExtendedTypeSystem.MethodInvocationException,
                    methodInformation.method.Name, arguments.Length, e.Message);
            }
#pragma warning restore 56500

            SetReferences(arguments, methodInformation, originalArguments);
            MethodInfo methodInfo = methodInformation.method as MethodInfo;
            if (methodInfo != null && methodInfo.ReturnType != typeof(void))
                return result;
            return AutomationNull.Value;
        }

        /// <summary>
        /// Converts a MethodBase[] into a MethodInformation[]
        /// </summary>
        /// <param name="methods">the methods to be converted</param>
        /// <returns>the MethodInformation[] corresponding to methods</returns>
        internal static MethodInformation[] GetMethodInformationArray(MethodBase[] methods)
        {
            int methodCount = methods.Length;
            MethodInformation[] returnValue = new MethodInformation[methodCount];
            for (int i = 0; i < methods.Length; i++)
            {
                returnValue[i] = new MethodInformation(methods[i], 0);
            }
            return returnValue;
        }

        /// <summary>
        /// Calls the method best suited to the arguments on target.
        /// </summary>
        /// <param name="methodName">used for error messages</param>
        /// <param name="target">object to call the method on</param>
        /// <param name="methodInformation">method information corresponding to methods</param>
        /// <param name="invocationConstraints">invocation constraints</param>
        /// <param name="arguments">arguments of the call </param>
        /// <returns>the return of the method</returns>
        /// <exception cref="MethodInvocationException">if the method throws an exception</exception>
        /// <exception cref="MethodException">if we could not find a method for the given arguments</exception>
        internal static object MethodInvokeDotNet(
            string methodName,
            object target,
            MethodInformation[] methodInformation,
            PSMethodInvocationConstraints invocationConstraints,
            object[] arguments)
        {
            object[] newArguments;
            MethodInformation bestMethod = GetBestMethodAndArguments(methodName, methodInformation, invocationConstraints, arguments, out newArguments);
            if (bestMethod.method is ConstructorInfo)
            {
                return InvokeResolvedConstructor(bestMethod, newArguments, arguments);
            }

            string methodDefinition = bestMethod.methodDefinition;
            ScriptTrace.Trace(1, "TraceMethodCall", ParserStrings.TraceMethodCall, methodDefinition);
            PSObject.memberResolution.WriteLine("Calling Method: {0}", methodDefinition);
            return AuxiliaryMethodInvoke(target, newArguments, bestMethod, arguments);
        }

        /// <summary>
        /// Calls the method best suited to the arguments on target.
        /// </summary>
        /// <param name="type">the type being constructed, used for diagnostics and caching</param>
        /// <param name="constructors">all overloads for the constructors</param>
        /// <param name="arguments">arguments of the call </param>
        /// <returns>the return of the method</returns>
        /// <exception cref="MethodInvocationException">if the method throws an exception</exception>
        /// <exception cref="MethodException">if we could not find a method for the given arguments</exception>
        internal static object ConstructorInvokeDotNet(Type type, ConstructorInfo[] constructors, object[] arguments)
        {
            var newConstructors = GetMethodInformationArray(constructors);
            object[] newArguments;
            MethodInformation bestMethod = GetBestMethodAndArguments(type.Name, newConstructors, arguments, out newArguments);
            return InvokeResolvedConstructor(bestMethod, newArguments, arguments);
        }

        private static object InvokeResolvedConstructor(MethodInformation bestMethod, object[] newArguments, object[] arguments)
        {
            if ((PSObject.memberResolution.Options & PSTraceSourceOptions.WriteLine) != 0)
            {
                PSObject.memberResolution.WriteLine("Calling Constructor: {0}", DotNetAdapter.GetMethodInfoOverloadDefinition(null,
                    bestMethod.method, 0));
            }
            return AuxiliaryConstructorInvoke(bestMethod, newArguments, arguments);
        }

        /// <summary>
        /// this is a flavor of MethodInvokeDotNet to deal with a peculiarity of property setters:
        /// Tthe setValue is always the last parameter. This enables a parameter after a varargs or optional 
        /// parameters and GetBestMethodAndArguments is not prepared for that.
        /// This method disregards the last parameter in its call to GetBestMethodAndArguments used in this case
        /// more for its "Arguments" side than for its "BestMethod" side, since there is only one method.
        /// </summary>
        internal static void ParameterizedPropertyInvokeSet(string propertyName, object target, object valuetoSet, MethodInformation[] methodInformation, object[] arguments)
        {
            // bestMethodIndex is ignored since we know we have only 1 method. GetBestMethodAndArguments
            // is still useful to deal with optional and varargs parameters and to perform the type conversions
            // of all parameters but the last one
            object[] newArguments;
            MethodInformation bestMethod = GetBestMethodAndArguments(propertyName, methodInformation, arguments, out newArguments);
            PSObject.memberResolution.WriteLine("Calling Set Method: {0}", bestMethod.methodDefinition);
            ParameterInfo[] bestMethodParameters = bestMethod.method.GetParameters();
            Type propertyType = bestMethodParameters[bestMethodParameters.Length - 1].ParameterType;

            // we have to convert the last parameter (valuetoSet) manually since it has been
            // disregarded in GetBestMethodAndArguments.
            object lastArgument;
            try
            {
                lastArgument = PropertySetAndMethodArgumentConvertTo(valuetoSet, propertyType, CultureInfo.InvariantCulture);
            }
            catch (InvalidCastException e)
            {
                // NTRAID#Windows Out Of Band Releases-924162-2005/11/17-JonN
                throw new MethodException(
                    "PropertySetterConversionInvalidCastArgument",
                    e,
                    ExtendedTypeSystem.MethodArgumentConversionException,
                    arguments.Length - 1, valuetoSet, propertyName, propertyType, e.Message);
            }

            // and we also have to rebuild the argument array to include the last parameter
            object[] finalArguments = new object[newArguments.Length + 1];
            for (int i = 0; i < newArguments.Length; i++)
            {
                finalArguments[i] = newArguments[i];
            }
            finalArguments[newArguments.Length] = lastArgument;

            AuxiliaryMethodInvoke(target, finalArguments, bestMethod, arguments);
        }

        internal static string GetMethodInfoOverloadDefinition(string memberName, MethodBase methodEntry, int parametersToIgnore)
        {
            StringBuilder builder = new StringBuilder();
            if (methodEntry.IsStatic)
            {
                builder.Append("static ");
            }
            MethodInfo method = methodEntry as MethodInfo;
            if (method != null)
            {
                builder.Append(ToStringCodeMethods.Type(method.ReturnType));
                builder.Append(" ");
            }
            else
            {
                ConstructorInfo ctorInfo = methodEntry as ConstructorInfo;
                if (ctorInfo != null)
                {
                    builder.Append(ToStringCodeMethods.Type(ctorInfo.DeclaringType));
                    builder.Append(" ");
                }
            }
            if (methodEntry.DeclaringType.GetTypeInfo().IsInterface)
            {
                builder.Append(ToStringCodeMethods.Type(methodEntry.DeclaringType, dropNamespaces: true));
                builder.Append(".");
            }
            builder.Append(memberName ?? methodEntry.Name);
            if (methodEntry.IsGenericMethodDefinition)
            {
                builder.Append("[");

                Type[] genericArgs = methodEntry.GetGenericArguments();
                for (int i = 0; i < genericArgs.Length; i++)
                {
                    if (i > 0) { builder.Append(", "); }
                    builder.Append(ToStringCodeMethods.Type(genericArgs[i]));
                }

                builder.Append("]");
            }
            builder.Append("(");
            System.Reflection.ParameterInfo[] parameters = methodEntry.GetParameters();
            int parametersLength = parameters.Length - parametersToIgnore;
            if (parametersLength > 0)
            {
                for (int i = 0; i < parametersLength; i++)
                {
                    System.Reflection.ParameterInfo parameter = parameters[i];
                    var parameterType = parameter.ParameterType;
                    if (parameterType.IsByRef)
                    {
                        builder.Append("[ref] ");
                        parameterType = parameterType.GetElementType();
                    }
                    if (parameterType.IsArray && (i == parametersLength - 1))
                    {
                        // The extension method 'CustomAttributeExtensions.GetCustomAttributes(ParameterInfo, Type, Boolean)' has inconsistent
                        // behavior on its return value in both FullCLR and CoreCLR. According to MSDN, if the attribute cannot be found, it
                        // should return an empty collection. However, it returns null in some rare cases [when the parameter isn't backed by
                        // actual metadata].
                        // This inconsistent behavior affects OneCore powershell because we are using the extension method here when compiling
                        // against CoreCLR. So we need to add a null check until this is fixed in CLR.
                        var paramArrayAttrs = parameter.GetCustomAttributes(typeof(ParamArrayAttribute), false);
                        if (paramArrayAttrs != null && paramArrayAttrs.Any())
                            builder.Append("Params ");
                    }
                    builder.Append(ToStringCodeMethods.Type(parameterType));
                    builder.Append(" ");
                    builder.Append(parameter.Name);
                    builder.Append(", ");
                }
                builder.Remove(builder.Length - 2, 2);
            }
            builder.Append(")");

            return builder.ToString();
        }

        #endregion auxiliary to method calling

        /// <summary>
        /// Called after a non null return from GetMember to try to call
        /// the method with the arguments
        /// </summary>
        /// <param name="method">the non empty return from GetMethods</param>
        /// <param name="arguments">the arguments to use</param>
        /// <returns>the return value for the method</returns>
        protected override object MethodInvoke(PSMethod method, object[] arguments)
        {
            return this.MethodInvoke(method, null, arguments);
        }

        /// <summary>
        /// Called after a non null return from GetMember to try to call
        /// the method with the arguments
        /// </summary>
        /// <param name="method">the non empty return from GetMethods</param>
        /// <param name="invocationConstraints">invocation constraints</param>
        /// <param name="arguments">the arguments to use</param>
        /// <returns>the return value for the method</returns>
        protected override object MethodInvoke(PSMethod method, PSMethodInvocationConstraints invocationConstraints, object[] arguments)
        {
            MethodCacheEntry methodEntry = (MethodCacheEntry)method.adapterData;
            return DotNetAdapter.MethodInvokeDotNet(
                method.Name,
                method.baseObject,
                methodEntry.methodInformationStructures,
                invocationConstraints,
                arguments);
        }

        /// <summary>
        /// Called after a non null return from GetMember to return the overloads
        /// </summary>
        /// <param name="method">the return of GetMember</param>
        /// <returns></returns>
        protected override Collection<String> MethodDefinitions(PSMethod method)
        {
            MethodCacheEntry methodEntry = (MethodCacheEntry)method.adapterData;
            IList<string> uniqueValues = methodEntry
                .methodInformationStructures
                .Select(m => m.methodDefinition)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return new Collection<string>(uniqueValues);
        }

        #endregion method

        #region parameterized property

        /// <summary>
        /// Returns the name of the type corresponding to the property's value
        /// </summary>
        /// <param name="property">property obtained in a previous GetMember</param>
        /// <returns>the name of the type corresponding to the member</returns>
        protected override string ParameterizedPropertyType(PSParameterizedProperty property)
        {
            var adapterData = (ParameterizedPropertyCacheEntry)property.adapterData;
            return adapterData.propertyType.FullName;
        }

        /// <summary>
        /// Returns true if the property is settable
        /// </summary>
        /// <param name="property">property to check</param>
        /// <returns>true if the property is settable</returns>
        protected override bool ParameterizedPropertyIsSettable(PSParameterizedProperty property)
        {
            return !((ParameterizedPropertyCacheEntry)property.adapterData).readOnly;
        }

        /// <summary>
        /// Returns true if the property is gettable
        /// </summary>
        /// <param name="property">property to check</param>
        /// <returns>true if the property is gettable</returns>
        protected override bool ParameterizedPropertyIsGettable(PSParameterizedProperty property)
        {
            return !((ParameterizedPropertyCacheEntry)property.adapterData).writeOnly;
        }

        /// <summary>
        /// Called after a non null return from GetMember to get the property value
        /// </summary>
        /// <param name="property">the non empty return from GetMember</param>
        /// <param name="arguments">the arguments to use</param>
        /// <returns>the return value for the property</returns>
        protected override object ParameterizedPropertyGet(PSParameterizedProperty property, object[] arguments)
        {
            var adapterData = (ParameterizedPropertyCacheEntry)property.adapterData;
            return DotNetAdapter.MethodInvokeDotNet(property.Name, property.baseObject,
                adapterData.getterInformation, null, arguments);
        }

        /// <summary>
        /// Called after a non null return from GetMember to set the property value
        /// </summary>
        /// <param name="property">the non empty return from GetMember</param>
        /// <param name="setValue">the value to set property with</param>
        /// <param name="arguments">the arguments to use</param>
        protected override void ParameterizedPropertySet(PSParameterizedProperty property, object setValue, object[] arguments)
        {
            var adapterData = (ParameterizedPropertyCacheEntry)property.adapterData;
            ParameterizedPropertyInvokeSet(adapterData.propertyName, property.baseObject, setValue,
                adapterData.setterInformation, arguments);
        }

        /// <summary>
        /// Called after a non null return from GetMember to return the overloads
        /// </summary>
        protected override Collection<String> ParameterizedPropertyDefinitions(PSParameterizedProperty property)
        {
            var adapterData = (ParameterizedPropertyCacheEntry)property.adapterData;
            var returnValue = new Collection<string>();
            for (int i = 0; i < adapterData.propertyDefinition.Length; i++)
            {
                returnValue.Add(adapterData.propertyDefinition[i]);
            }
            return returnValue;
        }

        /// <summary>
        /// Returns the string representation of the property in the object
        /// </summary>
        /// <param name="property">property obtained in a previous GetMember</param>
        /// <returns>the string representation of the property in the object</returns>
        protected override string ParameterizedPropertyToString(PSParameterizedProperty property)
        {
            StringBuilder returnValue = new StringBuilder();
            Collection<string> definitions = ParameterizedPropertyDefinitions(property);
            for (int i = 0; i < definitions.Count; i++)
            {
                returnValue.Append(definitions[i]);
                returnValue.Append(", ");
            }
            returnValue.Remove(returnValue.Length - 2, 2);
            return returnValue.ToString();
        }

        #endregion parameterized property

        #endregion virtual
    }

    #region DotNetAdapterWithOnlyPropertyLookup
    /// <summary>
    /// This is used by PSObject to support dotnet member lookup for the adapted
    /// objects.
    /// </summary>
    /// <remarks>
    /// This class is created to avoid cluttering DotNetAdapter with if () { } blocks .
    /// </remarks>
    internal class BaseDotNetAdapterForAdaptedObjects : DotNetAdapter
    {
        /// <summary>
        /// Return a collection representing the <paramref name="obj"/> object's 
        /// members as returned by CLR reflection.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <returns></returns>
        protected override PSMemberInfoInternalCollection<T> GetMembers<T>(object obj)
        {
            PSMemberInfoInternalCollection<T> returnValue = new PSMemberInfoInternalCollection<T>();
            AddAllProperties<T>(obj, returnValue, true);
            AddAllMethods<T>(obj, returnValue, true);
            AddAllEvents<T>(obj, returnValue, true);

            return returnValue;
        }

        /// <summary>
        /// Returns a member representing the <paramref name="obj"/> as given by CLR reflection.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <param name="memberName"></param>
        /// <returns></returns>
        protected override T GetMember<T>(object obj, string memberName)
        {
            PSProperty property = base.GetDotNetProperty<PSProperty>(obj, memberName);
            if (typeof(T).IsAssignableFrom(typeof(PSProperty)) && (null != property))
            {
                return property as T;
            }

            // In order to not break v1..base dotnet adapter should not return methods
            // when accessed with T as PSMethod.. accessing method with PSMemberInfo
            // is ok as property always gets precedence over methods and duplicates
            // are ignored.
            if (typeof(T) == typeof(PSMemberInfo))
            {
                T returnValue = PSObject.dotNetInstanceAdapter.GetDotNetMethod<T>(obj, memberName);
                // We only return a method if there is no property by the same name
                // to match the behavior we have in GetMembers
                if (returnValue != null && property == null)
                {
                    return returnValue;
                }
            }

            if (IsTypeParameterizedProperty(typeof(T)))
            {
                PSParameterizedProperty parameterizedProperty = PSObject.dotNetInstanceAdapter.GetDotNetProperty<PSParameterizedProperty>(obj, memberName);
                // We only return a parameterized property if there is no property by the same name
                // to match the behavior we have in GetMembers
                if (parameterizedProperty != null && property == null)
                {
                    return parameterizedProperty as T;
                }
            }
            return null;
        }
    }

    #endregion

    /// <summary>
    /// Used only to add a COM style type name to a COM interop .NET type
    /// </summary>
    internal class DotNetAdapterWithComTypeName : DotNetAdapter
    {
        private ComTypeInfo _comTypeInfo;
        internal DotNetAdapterWithComTypeName(ComTypeInfo comTypeInfo)
        {
            _comTypeInfo = comTypeInfo;
        }

        protected override IEnumerable<string> GetTypeNameHierarchy(object obj)
        {
            for (Type type = obj.GetType(); type != null; type = type.GetTypeInfo().BaseType)
            {
                if (type.FullName.Equals("System.__ComObject"))
                {
                    yield return ComAdapter.GetComTypeName(_comTypeInfo.Clsid);
                }
                yield return type.FullName;
            }
        }

        protected override ConsolidatedString GetInternedTypeNameHierarchy(object obj)
        {
            return new ConsolidatedString(GetTypeNameHierarchy(obj), interned: true);
        }
    }
    /// <summary>
    /// Adapter used for GetMember and GetMembers only.
    /// All other methods will not be called.
    /// </summary>
    internal abstract class MemberRedirectionAdapter : Adapter
    {
        #region virtual

        #region property specific

        /// <summary>
        /// Returns an array with the property attributes
        /// </summary>
        /// <param name="property">property we want the attributes from</param>
        /// <returns>an array with the property attributes</returns>
        protected override AttributeCollection PropertyAttributes(PSProperty property)
        {
            return new AttributeCollection();
        }

        /// <summary>
        /// Returns the value from a property coming from a previous call to GetMember
        /// </summary>
        /// <param name="property">PSProperty coming from a previous call to GetMember</param>
        /// <returns>The value of the property</returns>
        protected override object PropertyGet(PSProperty property)
        {
            Diagnostics.Assert(false, "redirection adapter is not called for properties");
            throw PSTraceSource.NewNotSupportedException();
        }

        /// <summary>
        /// Sets the value of a property coming from a previous call to GetMember
        /// </summary>
        /// <param name="property">PSProperty coming from a previous call to GetMember</param>
        /// <param name="setValue">value to set the property with</param>
        /// <param name="convertIfPossible">instructs the adapter to convert before setting, if the adapter supports conversion</param>
        protected override void PropertySet(PSProperty property, object setValue, bool convertIfPossible)
        {
            Diagnostics.Assert(false, "redirection adapter is not called for properties");
            throw PSTraceSource.NewNotSupportedException();
        }


        /// <summary>
        /// Returns true if the property is settable
        /// </summary>
        /// <param name="property">property to check</param>
        /// <returns>true if the property is settable</returns>
        protected override bool PropertyIsSettable(PSProperty property)
        {
            Diagnostics.Assert(false, "redirection adapter is not called for properties");
            throw PSTraceSource.NewNotSupportedException();
        }


        /// <summary>
        /// Returns true if the property is gettable
        /// </summary>
        /// <param name="property">property to check</param>
        /// <returns>true if the property is gettable</returns>
        protected override bool PropertyIsGettable(PSProperty property)
        {
            Diagnostics.Assert(false, "redirection adapter is not called for properties");
            throw PSTraceSource.NewNotSupportedException();
        }


        /// <summary>
        /// Returns the name of the type corresponding to the property's value
        /// </summary>
        /// <param name="property">PSProperty obtained in a previous GetMember</param>
        /// <param name="forDisplay">True if the result is for display purposes only</param>
        /// <returns>the name of the type corresponding to the member</returns>
        protected override string PropertyType(PSProperty property, bool forDisplay)
        {
            Diagnostics.Assert(false, "redirection adapter is not called for properties");
            throw PSTraceSource.NewNotSupportedException();
        }

        /// <summary>
        /// Returns the string representation of the property in the object
        /// </summary>
        /// <param name="property">property obtained in a previous GetMember</param>
        /// <returns>the string representation of the property in the object</returns>
        protected override string PropertyToString(PSProperty property)
        {
            Diagnostics.Assert(false, "redirection adapter is not called for properties");
            throw PSTraceSource.NewNotSupportedException();
        }

        #endregion property specific

        #region method specific

        /// <summary>
        /// Called after a non null return from GetMember to try to call
        /// the method with the arguments
        /// </summary>
        /// <param name="method">the non empty return from GetMethods</param>
        /// <param name="arguments">the arguments to use</param>
        /// <returns>the return value for the method</returns>
        protected override object MethodInvoke(PSMethod method, object[] arguments)
        {
            Diagnostics.Assert(false, "redirection adapter is not called for methods");
            throw PSTraceSource.NewNotSupportedException();
        }

        /// <summary>
        /// Called after a non null return from GetMember to return the overloads
        /// </summary>
        /// <param name="method">the return of GetMember</param>
        /// <returns></returns>
        protected override Collection<String> MethodDefinitions(PSMethod method)
        {
            Diagnostics.Assert(false, "redirection adapter is not called for methods");
            throw PSTraceSource.NewNotSupportedException();
        }

        #endregion method specific

        #endregion virtual
    }
    /// <summary>
    /// adapter for properties in the inside PSObject if it has a null BaseObject
    /// </summary>
    internal class PSObjectAdapter : MemberRedirectionAdapter
    {
        #region virtual

        /// <summary>
        /// Returns the TypeNameHierarchy out of an object
        /// </summary>
        /// <param name="obj">object to get the TypeNameHierarchy from</param>
        protected override IEnumerable<string> GetTypeNameHierarchy(object obj)
        {
            return ((PSObject)obj).InternalTypeNames;
        }

        /// <summary>
        /// Returns null if memberName is not a member in the adapter or
        /// the corresponding PSMemberInfo
        /// </summary>
        /// <param name="obj">object to retrieve the PSMemberInfo from</param>
        /// <param name="memberName">name of the member to be retrieved</param>
        /// <returns>The PSMemberInfo corresponding to memberName from obj</returns>
        protected override T GetMember<T>(object obj, string memberName)
        {
            return ((PSObject)obj).Members[memberName] as T;
        }

        /// <summary>
        /// Retrieves all the members available in the object.
        /// The adapter implementation is encouraged to cache all properties/methods available
        /// in the first call to GetMember and GetMembers so that subsequent
        /// calls can use the cache.
        /// In the case of the .NET adapter that would be a cache from the .NET type to
        /// the public properties and fields available in that type. 
        /// In the case of the DirectoryEntry adapter, this could be a cache of the objectClass
        /// to the properties available in it.
        /// </summary>
        /// <param name="obj">object to get all the member information from</param>
        /// <returns>all members in obj</returns>
        protected override PSMemberInfoInternalCollection<T> GetMembers<T>(object obj)
        {
            var returnValue = new PSMemberInfoInternalCollection<T>();
            PSObject mshObj = (PSObject)obj;
            foreach (PSMemberInfo member in mshObj.Members)
            {
                T memberAsT = member as T;
                if (memberAsT != null)
                {
                    returnValue.Add(memberAsT);
                }
            }
            return returnValue;
        }

        #endregion virtual
    }
    /// <summary>
    /// adapter for properties inside a member set
    /// </summary>
    internal class PSMemberSetAdapter : MemberRedirectionAdapter
    {
        #region virtual

        protected override IEnumerable<string> GetTypeNameHierarchy(object obj)
        {
            // Make sure PSMemberSet adapter shows PSMemberSet as the typename.
            // This is because PSInternalMemberSet internal class derives from
            // PSMemberSet to support delay loading PSBase, PSObject etc. We
            // should not show internal type members to the users. Also this
            // might break type files shipped in v1.
            yield return typeof(PSMemberSet).FullName;
        }

        /// <summary>
        /// Returns null if memberName is not a member in the adapter or
        /// the corresponding PSMemberInfo
        /// </summary>
        /// <param name="obj">object to retrieve the PSMemberInfo from</param>
        /// <param name="memberName">name of the member to be retrieved</param>
        /// <returns>The PSMemberInfo corresponding to memberName from obj</returns>
        protected override T GetMember<T>(object obj, string memberName)
        {
            return ((PSMemberSet)obj).Members[memberName] as T;
        }

        /// <summary>
        /// Retrieves all the members available in the object.
        /// The adapter implementation is encouraged to cache all properties/methods available
        /// in the first call to GetMember and GetMembers so that subsequent
        /// calls can use the cache.
        /// In the case of the .NET adapter that would be a cache from the .NET type to
        /// the public properties and fields available in that type. 
        /// In the case of the DirectoryEntry adapter, this could be a cache of the objectClass
        /// to the properties available in it.
        /// </summary>
        /// <param name="obj">object to get all the member information from</param>
        /// <returns>all members in obj</returns>
        protected override PSMemberInfoInternalCollection<T> GetMembers<T>(object obj)
        {
            var returnValue = new PSMemberInfoInternalCollection<T>();
            foreach (PSMemberInfo member in ((PSMemberSet)obj).Members)
            {
                T memberAsT = member as T;
                if (memberAsT != null)
                {
                    returnValue.Add(memberAsT);
                }
            }
            return returnValue;
        }

        #endregion  virtual
    }
    /// <summary>
    /// Base class for all adapters that adapt only properties and retain 
    /// .NET methods
    /// </summary>
    internal abstract class PropertyOnlyAdapter : DotNetAdapter
    {
        internal override bool SiteBinderCanOptimize { get { return false; } }

        protected override ConsolidatedString GetInternedTypeNameHierarchy(object obj)
        {
            return new ConsolidatedString(GetTypeNameHierarchy(obj), interned: true);
        }

        /// <summary>
        /// Returns null if propertyName is not a property in the adapter or
        /// the corresponding PSProperty with its adapterData set to information
        /// to be used when retrieving the property.
        /// </summary>
        /// <param name="obj">object to retrieve the PSProperty from</param>
        /// <param name="propertyName">name of the property to be retrieved</param>
        /// <returns>The PSProperty corresponding to propertyName from obj</returns>
        protected abstract PSProperty DoGetProperty(object obj, string propertyName);

        /// <summary>
        /// Retrieves all the properties available in the object.
        /// </summary>
        /// <param name="obj">object to get all the property information from</param>
        /// <param name="members">collection where the properties will be added</param>
        protected abstract void DoAddAllProperties<T>(object obj, PSMemberInfoInternalCollection<T> members) where T : PSMemberInfo;


        /// <summary>
        /// Returns null if memberName is not a member in the adapter or
        /// the corresponding PSMemberInfo
        /// </summary>
        /// <param name="obj">object to retrieve the PSMemberInfo from</param>
        /// <param name="memberName">name of the member to be retrieved</param>
        /// <returns>The PSMemberInfo corresponding to memberName from obj</returns>
        protected override T GetMember<T>(object obj, string memberName)
        {
            PSProperty property = DoGetProperty(obj, memberName);

            if (typeof(T).IsAssignableFrom(typeof(PSProperty)) && property != null)
            {
                return property as T;
            }

            if (typeof(T).IsAssignableFrom(typeof(PSMethod)))
            {
                T returnValue = PSObject.dotNetInstanceAdapter.GetDotNetMethod<T>(obj, memberName);
                // We only return a method if there is no property by the same name
                // to match the behavior we have in GetMembers
                if (returnValue != null && property == null)
                {
                    return returnValue;
                }
            }
            if (IsTypeParameterizedProperty(typeof(T)))
            {
                PSParameterizedProperty parameterizedProperty = PSObject.dotNetInstanceAdapter.GetDotNetProperty<PSParameterizedProperty>(obj, memberName);
                // We only return a parameterized property if there is no property by the same name
                // to match the behavior we have in GetMembers
                if (parameterizedProperty != null && property == null)
                {
                    return parameterizedProperty as T;
                }
            }
            return null;
        }


        /// <summary>
        /// Retrieves all the members available in the object.
        /// The adapter implementation is encouraged to cache all properties/methods available
        /// in the first call to GetMember and GetMembers so that subsequent
        /// calls can use the cache.
        /// In the case of the .NET adapter that would be a cache from the .NET type to
        /// the public properties and fields available in that type. 
        /// In the case of the DirectoryEntry adapter, this could be a cache of the objectClass
        /// to the properties available in it.
        /// </summary>
        /// <param name="obj">object to get all the member information from</param>
        /// <returns>all members in obj</returns>
        protected override PSMemberInfoInternalCollection<T> GetMembers<T>(object obj)
        {
            var returnValue = new PSMemberInfoInternalCollection<T>();
            if (typeof(T).IsAssignableFrom(typeof(PSProperty)))
            {
                DoAddAllProperties<T>(obj, returnValue);
            }
            PSObject.dotNetInstanceAdapter.AddAllMethods(obj, returnValue, true);
            if (IsTypeParameterizedProperty(typeof(T)))
            {
                var parameterizedProperties = new PSMemberInfoInternalCollection<PSParameterizedProperty>();
                PSObject.dotNetInstanceAdapter.AddAllProperties(obj, parameterizedProperties, true);
                foreach (PSParameterizedProperty parameterizedProperty in parameterizedProperties)
                {
                    try
                    {
                        returnValue.Add(parameterizedProperty as T);
                    }
                    catch (ExtendedTypeSystemException)
                    {
                        // ignore duplicates: the adapted properties will take precedence
                    }
                }
            }
            return returnValue;
        }
    }

    /// <summary>
    /// Deals with XmlNode objects
    /// </summary>
    internal class XmlNodeAdapter : PropertyOnlyAdapter
    {
        #region virtual
        /// <summary>
        /// Returns the TypeNameHierarchy out of an object
        /// </summary>
        /// <param name="obj">object to get the TypeNameHierarchy from</param>
        protected override IEnumerable<string> GetTypeNameHierarchy(object obj)
        {
            XmlNode node = (XmlNode)obj;
            string nodeNamespace = node.NamespaceURI;
            IEnumerable<string> baseTypeNames = GetDotNetTypeNameHierarchy(obj);
            if (String.IsNullOrEmpty(nodeNamespace))
            {
                foreach (string baseType in baseTypeNames)
                {
                    yield return baseType;
                }
            }
            else
            {
                StringBuilder firstType = null;
                foreach (string baseType in baseTypeNames)
                {
                    if (firstType == null)
                    {
                        firstType = new StringBuilder(baseType);
                        firstType.Append("#");
                        firstType.Append(node.NamespaceURI);
                        firstType.Append("#");
                        firstType.Append(node.LocalName);
                        yield return firstType.ToString();
                    }
                    yield return baseType;
                }
            }
        }

        /// <summary>
        /// Retrieves all the properties available in the object.
        /// </summary>
        /// <param name="obj">object to get all the property information from</param>
        /// <param name="members">collection where the members will be added</param>
        protected override void DoAddAllProperties<T>(object obj, PSMemberInfoInternalCollection<T> members)
        {
            XmlNode node = (XmlNode)obj;

            Dictionary<string, List<XmlNode>> nodeArrays = new Dictionary<string, List<XmlNode>>(StringComparer.OrdinalIgnoreCase);

            if (node.Attributes != null)
            {
                foreach (XmlNode attribute in node.Attributes)
                {
                    List<XmlNode> nodeList;
                    if (!nodeArrays.TryGetValue(attribute.LocalName, out nodeList))
                    {
                        nodeList = new List<XmlNode>();
                        nodeArrays[attribute.LocalName] = nodeList;
                    }
                    nodeList.Add(attribute);
                }
            }

            if (node.ChildNodes != null)
            {
                foreach (XmlNode childNode in node.ChildNodes)
                {
                    // Win8: 437544 ignore whitespace
                    if (childNode is XmlWhitespace)
                    {
                        continue;
                    }

                    List<XmlNode> nodeList;
                    if (!nodeArrays.TryGetValue(childNode.LocalName, out nodeList))
                    {
                        nodeList = new List<XmlNode>();
                        nodeArrays[childNode.LocalName] = nodeList;
                    }
                    nodeList.Add(childNode);
                }
            }

            foreach (KeyValuePair<string, List<XmlNode>> nodeArrayEntry in nodeArrays)
            {
                members.Add(new PSProperty(nodeArrayEntry.Key, this, obj, nodeArrayEntry.Value.ToArray()) as T);
            }
        }
        /// <summary>
        /// Returns null if propertyName is not a property in the adapter or
        /// the corresponding PSProperty with its adapterData set to information
        /// to be used when retrieving the property.
        /// </summary>
        /// <param name="obj">object to retrieve the PSProperty from</param>
        /// <param name="propertyName">name of the property to be retrieved</param>
        /// <returns>The PSProperty corresponding to propertyName from obj</returns>
        protected override PSProperty DoGetProperty(object obj, string propertyName)
        {
            XmlNode[] nodes = FindNodes(obj, propertyName, StringComparison.OrdinalIgnoreCase);
            if (nodes.Length == 0)
            {
                return null;
            }
            return new PSProperty(nodes[0].LocalName, this, obj, nodes);
        }

        /// <summary>
        /// Returns true if the property is settable
        /// </summary>
        /// <param name="property">property to check</param>
        /// <returns>true if the property is settable</returns>
        protected override bool PropertyIsSettable(PSProperty property)
        {
            XmlNode[] nodes = (XmlNode[])property.adapterData;
            Diagnostics.Assert(nodes.Length != 0, "DoGetProperty would not return an empty array, it would return null instead");
            if (nodes.Length != 1)
            {
                return false;
            }
            XmlNode node = nodes[0];
            if (node is XmlText)
            {
                return true;
            }
            if (node is XmlAttribute)
            {
                return true;
            }
            XmlAttributeCollection nodeAttributes = node.Attributes;
            if ((nodeAttributes != null) && (nodeAttributes.Count != 0))
            {
                return false;
            }
            XmlNodeList nodeChildren = node.ChildNodes;
            if ((nodeChildren == null) || (nodeChildren.Count == 0))
            {
                return true;
            }

            if ((nodeChildren.Count == 1) && (nodeChildren[0].NodeType == XmlNodeType.Text))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true if the property is gettable
        /// </summary>
        /// <param name="property">property to check</param>
        /// <returns>true if the property is gettable</returns>
        protected override bool PropertyIsGettable(PSProperty property)
        {
            return true;
        }

        private static object GetNodeObject(XmlNode node)
        {
            XmlText text = node as XmlText;
            if (text != null)
            {
                return text.InnerText;
            }

            XmlAttributeCollection nodeAttributes = node.Attributes;

            // A node with attributes should not be simplified
            if ((nodeAttributes != null) && (nodeAttributes.Count != 0))
            {
                return node;
            }

            // If node does not have any children, return the innertext of the node
            if (!node.HasChildNodes)
            {
                return node.InnerText;
            }

            XmlNodeList nodeChildren = node.ChildNodes;
            // nodeChildren will not be null as we already verified iff the node has children.
            if ((nodeChildren.Count == 1) && (nodeChildren[0].NodeType == XmlNodeType.Text))
            {
                return node.InnerText;
            }

            XmlAttribute attribute = node as XmlAttribute;
            if (attribute != null)
            {
                return attribute.Value;
            }

            return node;
        }

        /// <summary>
        /// Returns the value from a property coming from a previous call to DoGetProperty
        /// </summary>
        /// <param name="property">PSProperty coming from a previous call to DoGetProperty</param>
        /// <returns>The value of the property</returns>
        protected override object PropertyGet(PSProperty property)
        {
            XmlNode[] nodes = (XmlNode[])property.adapterData;

            if (nodes.Length == 1)
            {
                return GetNodeObject(nodes[0]);
            }

            object[] returnValue = new object[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                returnValue[i] = GetNodeObject(nodes[i]);
            }

            return returnValue;
        }
        /// <summary>
        /// Sets the value of a property coming from a previous call to DoGetProperty
        /// </summary>
        /// <param name="property">PSProperty coming from a previous call to DoGetProperty</param>
        /// <param name="setValue">value to set the property with</param>
        /// <param name="convertIfPossible">instructs the adapter to convert before setting, if the adapter supports conversion</param>
        protected override void PropertySet(PSProperty property, object setValue, bool convertIfPossible)
        {
            string valueString = setValue as string;
            if (valueString == null)
            {
                throw new SetValueException("XmlNodeSetShouldBeAString",
                    null,
                    ExtendedTypeSystem.XmlNodeSetShouldBeAString,
                    property.Name);
            }
            XmlNode[] nodes = (XmlNode[])property.adapterData;
            Diagnostics.Assert(nodes.Length != 0, "DoGetProperty would not return an empty array, it would return null instead");
            if (nodes.Length > 1)
            {
                throw new SetValueException("XmlNodeSetRestrictionsMoreThanOneNode",
                    null,
                    ExtendedTypeSystem.XmlNodeSetShouldBeAString,
                    property.Name);
            }

            XmlNode node = nodes[0];
            XmlText text = node as XmlText;
            if (text != null)
            {
                text.InnerText = valueString;
                return;
            }

            XmlAttributeCollection nodeAttributes = node.Attributes;
            // A node with attributes cannot be set
            if ((nodeAttributes != null) && (nodeAttributes.Count != 0))
            {
                throw new SetValueException("XmlNodeSetRestrictionsNodeWithAttributes",
                    null,
                    ExtendedTypeSystem.XmlNodeSetShouldBeAString,
                    property.Name);
            }

            XmlNodeList nodeChildren = node.ChildNodes;
            if (nodeChildren == null || nodeChildren.Count == 0)
            {
                node.InnerText = valueString;
                return;
            }

            if ((nodeChildren.Count == 1) && (nodeChildren[0].NodeType == XmlNodeType.Text))
            {
                node.InnerText = valueString;
                return;
            }

            XmlAttribute attribute = node as XmlAttribute;
            if (attribute != null)
            {
                attribute.Value = valueString;
                return;
            }

            throw new SetValueException("XmlNodeSetRestrictionsUnknownNodeType",
                null,
                ExtendedTypeSystem.XmlNodeSetShouldBeAString,
                property.Name);
        }

        /// <summary>
        /// Returns the name of the type corresponding to the property
        /// </summary>
        /// <param name="property">PSProperty obtained in a previous DoGetProperty</param>
        /// <param name="forDisplay">True if the result is for display purposes only</param>
        /// <returns>the name of the type corresponding to the property</returns>
        protected override string PropertyType(PSProperty property, bool forDisplay)
        {
            object value = null;
            try
            {
                value = BasePropertyGet(property);
            }
            catch (GetValueException)
            {
            }
            var type = value == null ? typeof(object) : value.GetType();
            return forDisplay ? ToStringCodeMethods.Type(type) : type.FullName;
        }
        #endregion virtual


        /// <summary>
        /// Auxiliary in GetProperty to perform case sensitive and case insensitive searches
        /// in the child nodes
        /// </summary>
        /// <param name="obj">XmlNode to extract property from</param>
        /// <param name="propertyName">property to look for</param>
        /// <param name="comparisonType">type pf comparison to perform</param>
        /// <returns>the corresponding XmlNode or null if not present</returns>
        private static XmlNode[] FindNodes(object obj, string propertyName, StringComparison comparisonType)
        {
            List<XmlNode> retValue = new List<XmlNode>();
            XmlNode node = (XmlNode)obj;

            if (node.Attributes != null)
            {
                foreach (XmlNode attribute in node.Attributes)
                {
                    if (attribute.LocalName.Equals(propertyName, comparisonType))
                    {
                        retValue.Add(attribute);
                    }
                }
            }

            foreach (XmlNode childNode in node.ChildNodes)
            {
                if (childNode is XmlWhitespace)
                {
                    // Win8: 437544 ignore whitespace
                    continue;
                }

                if (childNode.LocalName.Equals(propertyName, comparisonType))
                {
                    retValue.Add(childNode);
                }
            }

            return retValue.ToArray();
        }
    }

    internal class TypeInference
    {
        [TraceSource("ETS", "Extended Type System")]
        private static readonly PSTraceSource s_tracer = PSTraceSource.GetTracer("ETS", "Extended Type System");

        internal static MethodInformation Infer(MethodInformation genericMethod, Type[] argumentTypes)
        {
            Dbg.Assert(genericMethod != null, "Caller should verify that genericMethod != null");
            Dbg.Assert(argumentTypes != null, "Caller should verify that arguments != null");

            // the cast is safe, because
            // 1) only ConstructorInfo and MethodInfo derive from MethodBase
            // 2) ConstructorInfo.IsGenericMethod is always false
            MethodInfo originalMethod = (MethodInfo)genericMethod.method;
            MethodInfo inferredMethod = TypeInference.Infer(originalMethod, argumentTypes, genericMethod.hasVarArgs);

            if (inferredMethod != null)
            {
                return new MethodInformation(inferredMethod, 0);
            }
            else
            {
                return null;
            }
        }

        private static MethodInfo Infer(MethodInfo genericMethod, Type[] typesOfMethodArguments, bool hasVarArgs)
        {
            Dbg.Assert(genericMethod != null, "Caller should verify that genericMethod != null");
            Dbg.Assert(typesOfMethodArguments != null, "Caller should verify that arguments != null");

            if (!genericMethod.ContainsGenericParameters)
            {
                return genericMethod;
            }

            Type[] typeParameters = genericMethod.GetGenericArguments();
            Type[] typesOfMethodParameters = genericMethod.GetParameters().Select(p => p.ParameterType).ToArray();

            MethodInfo inferredMethod = Infer(genericMethod, typeParameters, typesOfMethodParameters, typesOfMethodArguments);

            // normal inference failed, perhaps instead of inferring for 
            //   M<T1,T2,T3>(T1, T2, ..., params T3 [])
            // we can try to infer for this signature instead
            //   M<T1,T2,T3>)(T1, T2, ..., T3, T3, T3, T3)
            // where T3 is repeated appropriate number of times depending on the number of actual method arguments.
            if (inferredMethod == null &&
                hasVarArgs &&
                typesOfMethodArguments.Length >= (typesOfMethodParameters.Length - 1))
            {
                IEnumerable<Type> typeOfRegularParameters = typesOfMethodParameters.Take(typesOfMethodParameters.Length - 1);
                IEnumerable<Type> multipliedVarArgsElementType = Enumerable.Repeat(
                    typesOfMethodParameters[typesOfMethodParameters.Length - 1].GetElementType(),
                    typesOfMethodArguments.Length - typesOfMethodParameters.Length + 1);

                inferredMethod = Infer(
                    genericMethod,
                    typeParameters,
                    typeOfRegularParameters.Concat(multipliedVarArgsElementType),
                    typesOfMethodArguments);
            }

            return inferredMethod;
        }

        private static MethodInfo Infer(MethodInfo genericMethod, ICollection<Type> typeParameters, IEnumerable<Type> typesOfMethodParameters, IEnumerable<Type> typesOfMethodArguments)
        {
            Dbg.Assert(genericMethod != null, "Caller should verify that genericMethod != null");
            Dbg.Assert(typeParameters != null, "Caller should verify that typeParameters != null");
            Dbg.Assert(typesOfMethodParameters != null, "Caller should verify that typesOfMethodParameters != null");
            Dbg.Assert(typesOfMethodArguments != null, "Caller should verify that typesOfMethodArguments != null");

            using (s_tracer.TraceScope("Inferring type parameters for the following method: {0}", genericMethod))
            {
                if (PSTraceSourceOptions.WriteLine == (s_tracer.Options & PSTraceSourceOptions.WriteLine))
                {
                    s_tracer.WriteLine(
                        "Types of method arguments: {0}",
                        string.Join(", ", typesOfMethodArguments.Select(t => t.ToString()).ToArray()));
                }

                var typeInference = new TypeInference(typeParameters);
                if (!typeInference.UnifyMultipleTerms(typesOfMethodParameters, typesOfMethodArguments))
                {
                    return null;
                }

                IEnumerable<Type> inferredTypeParameters = typeParameters.Select(typeInference.GetInferredType);
                if (inferredTypeParameters.Any(inferredType => inferredType == null))
                {
                    return null;
                }

                try
                {
                    MethodInfo instantiatedMethod = genericMethod.MakeGenericMethod(inferredTypeParameters.ToArray());
                    s_tracer.WriteLine("Inference succesful: {0}", instantiatedMethod);
                    return instantiatedMethod;
                }
                catch (ArgumentException e)
                {
                    // Inference failure - most likely due to generic constraints being violated (i.e. where T: IEnumerable)
                    s_tracer.WriteLine("Inference failure: {0}", e.Message);
                    return null;
                }
            }
        }

        private readonly HashSet<Type>[] _typeParameterIndexToSetOfInferenceCandidates;

#if DEBUG
        private readonly HashSet<Type> _typeParametersOfTheMethod;
#endif

        internal TypeInference(ICollection<Type> typeParameters)
        {
#if DEBUG
            Dbg.Assert(typeParameters != null, "Caller should verify that typeParameters != null");
            Dbg.Assert(
                typeParameters.All(t => t.IsGenericParameter),
                "Caller should verify that typeParameters are really generic type parameters");
#endif
            _typeParameterIndexToSetOfInferenceCandidates = new HashSet<Type>[typeParameters.Count];
#if DEBUG
            List<int> listOfTypeParameterPositions = typeParameters.Select(t => t.GenericParameterPosition).ToList();
            listOfTypeParameterPositions.Sort();
            Dbg.Assert(
                listOfTypeParameterPositions.Count == listOfTypeParameterPositions.Distinct().Count(),
                "No type parameters should occupy the same position");
            Dbg.Assert(
                listOfTypeParameterPositions.All(p => p >= 0),
                "Type parameter positions should be between 0 and #ofParams");
            Dbg.Assert(
                listOfTypeParameterPositions.All(p => p < _typeParameterIndexToSetOfInferenceCandidates.Length),
                "Type parameter positions should be between 0 and #ofParams");

            _typeParametersOfTheMethod = new HashSet<Type>();
            foreach (Type t in typeParameters)
            {
                _typeParametersOfTheMethod.Add(t);
            }
#endif
        }

        internal Type GetInferredType(Type typeParameter)
        {
#if DEBUG
            Dbg.Assert(typeParameter != null, "Caller should verify typeParameter != null");
            Dbg.Assert(
                _typeParametersOfTheMethod.Contains(typeParameter),
                "Caller should verify that typeParameter is actuall a generic type parameter of the method");
#endif

            ICollection<Type> inferenceCandidates =
                _typeParameterIndexToSetOfInferenceCandidates[typeParameter.GenericParameterPosition];

            if ((inferenceCandidates != null) && (inferenceCandidates.Any(t => t == typeof(LanguagePrimitives.Null))))
            {
                Type firstValueType = inferenceCandidates.FirstOrDefault(t => t.GetTypeInfo().IsValueType);
                if (firstValueType != null)
                {
                    s_tracer.WriteLine("Cannot reconcile null and {0} (a value type)", firstValueType);
                    inferenceCandidates = null;
                    _typeParameterIndexToSetOfInferenceCandidates[typeParameter.GenericParameterPosition] = null;
                }
                else
                {
                    inferenceCandidates = inferenceCandidates.Where(t => t != typeof(LanguagePrimitives.Null)).ToList();
                    if (inferenceCandidates.Count == 0)
                    {
                        inferenceCandidates = null;
                        _typeParameterIndexToSetOfInferenceCandidates[typeParameter.GenericParameterPosition] = null;
                    }
                }
            }

            if ((inferenceCandidates != null) && (inferenceCandidates.Count > 1))
            {
                // "base class" assignability-wise (to account for interfaces)
                Type commonBaseClass = inferenceCandidates.FirstOrDefault(
                    potentiallyCommonBaseClass =>
                        inferenceCandidates.All(
                            otherCandidate =>
                                otherCandidate == potentiallyCommonBaseClass ||
                                potentiallyCommonBaseClass.IsAssignableFrom(otherCandidate)));

                if (commonBaseClass != null)
                {
                    inferenceCandidates.Clear();
                    inferenceCandidates.Add(commonBaseClass);
                }
                else
                {
                    s_tracer.WriteLine("Multiple unreconcilable inferences for type parameter {0}", typeParameter);
                    inferenceCandidates = null;
                    _typeParameterIndexToSetOfInferenceCandidates[typeParameter.GenericParameterPosition] = null;
                }
            }

            if (inferenceCandidates == null)
            {
                s_tracer.WriteLine("Couldn't infer type parameter {0}", typeParameter);
                return null;
            }
            else
            {
                Dbg.Assert(inferenceCandidates.Count == 1, "inferenceCandidates should contain exactly 1 element at this point");
                return inferenceCandidates.Single();
            }
        }

        internal bool UnifyMultipleTerms(IEnumerable<Type> parameterTypes, IEnumerable<Type> argumentTypes)
        {
            List<Type> leftList = parameterTypes.ToList();
            List<Type> rightList = argumentTypes.ToList();

            if (leftList.Count != rightList.Count)
            {
                s_tracer.WriteLine("Mismatch in number of parameters and arguments");
                return false;
            }

            for (int i = 0; i < leftList.Count; i++)
            {
                if (!this.Unify(leftList[i], rightList[i]))
                {
                    s_tracer.WriteLine("Couldn't unify {0} with {1}", leftList[i], rightList[i]);
                    return false;
                }
            }

            return true;
        }

        private bool Unify(Type parameterType, Type argumentType)
        {
            var parameterTypeInfo = parameterType.GetTypeInfo();
            if (!parameterTypeInfo.ContainsGenericParameters)
            {
                return true;
            }

            if (parameterType.IsGenericParameter)
            {
#if DEBUG
                Dbg.Assert(
                    _typeParametersOfTheMethod.Contains(parameterType),
                    "Only uninstantinated generic type parameters encountered in real life, should be the ones coming from the method");
#endif

                HashSet<Type> inferenceCandidates = _typeParameterIndexToSetOfInferenceCandidates[parameterType.GenericParameterPosition];
                if (inferenceCandidates == null)
                {
                    inferenceCandidates = new HashSet<Type>();
                    _typeParameterIndexToSetOfInferenceCandidates[parameterType.GenericParameterPosition] = inferenceCandidates;
                }
                inferenceCandidates.Add(argumentType);
                s_tracer.WriteLine("Inferred {0} => {1}", parameterType, argumentType);
                return true;
            }

            if (parameterType.IsArray)
            {
                if (argumentType == typeof(LanguagePrimitives.Null))
                {
                    return true;
                }

                if (argumentType.IsArray && parameterType.GetArrayRank() == argumentType.GetArrayRank())
                {
                    return this.Unify(parameterType.GetElementType(), argumentType.GetElementType());
                }

                s_tracer.WriteLine("Couldn't unify array {0} with {1}", parameterType, argumentType);
                return false;
            }

            if (parameterType.IsByRef)
            {
                if (argumentType.GetTypeInfo().IsGenericType && argumentType.GetGenericTypeDefinition() == typeof(PSReference<>))
                {
                    Type referencedType = argumentType.GetGenericArguments()[0];
                    if (referencedType == typeof(LanguagePrimitives.Null))
                    {
                        return true;
                    }
                    else
                    {
                        return this.Unify(
                            parameterType.GetElementType(),
                            referencedType);
                    }
                }
                else
                {
                    s_tracer.WriteLine("Couldn't unify reference type {0} with {1}", parameterType, argumentType);
                    return false;
                }
            }

            if (parameterTypeInfo.IsGenericType && parameterType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                if (argumentType == typeof(LanguagePrimitives.Null))
                {
                    return true;
                }

                return this.Unify(parameterType.GetGenericArguments()[0], argumentType);
            }

            if (parameterTypeInfo.IsGenericType)
            {
                if (argumentType == typeof(LanguagePrimitives.Null))
                {
                    return true;
                }

                return this.UnifyConstructedType(parameterType, argumentType);
            }

            Dbg.Assert(false, "Unrecognized kind of type");
            s_tracer.WriteLine("Unrecognized kind of type: {0}", parameterType);
            return false;
        }

        private bool UnifyConstructedType(Type parameterType, Type argumentType)
        {
            Dbg.Assert(parameterType.GetTypeInfo().IsGenericType, "Caller should verify parameterType.IsGenericType before calling this method");

            if (IsEqualGenericTypeDefinition(parameterType, argumentType))
            {
                IEnumerable<Type> typeParametersOfParameterType = parameterType.GetGenericArguments();
                IEnumerable<Type> typeArgumentsOfArgumentType = argumentType.GetGenericArguments();
                return this.UnifyMultipleTerms(typeParametersOfParameterType, typeArgumentsOfArgumentType);
            }

            Type[] interfaces = argumentType.GetInterfaces();
            for (int i = 0; i < interfaces.Length; i++)
            {
                if (IsEqualGenericTypeDefinition(parameterType, interfaces[i]))
                {
                    return UnifyConstructedType(parameterType, interfaces[i]);
                }
            }

            Type baseType = argumentType.GetTypeInfo().BaseType;
            while (baseType != null)
            {
                if (IsEqualGenericTypeDefinition(parameterType, baseType))
                {
                    return UnifyConstructedType(parameterType, baseType);
                }
                baseType = baseType.GetTypeInfo().BaseType;
            }

            s_tracer.WriteLine("Attempt to unify different constructed types: {0} and {1}", parameterType, argumentType);
            return false;
        }

        private static bool IsEqualGenericTypeDefinition(Type parameterType, Type argumentType)
        {
            Dbg.Assert(parameterType.GetTypeInfo().IsGenericType, "Caller should verify parameterType.IsGenericType before calling this method");

            if (!argumentType.GetTypeInfo().IsGenericType)
            {
                return false;
            }

            return parameterType.GetGenericTypeDefinition() == argumentType.GetGenericTypeDefinition();
        }
    }
}
