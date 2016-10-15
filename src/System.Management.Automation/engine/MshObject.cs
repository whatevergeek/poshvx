/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Dynamic;
using System.Linq;
using System.Linq.Expressions;
using System.Management.Automation.Language;
using System.Reflection;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using System.Management.Automation;
using System.Management.Automation.Internal;
using System.Management.Automation.Runspaces;
using System.Runtime.Serialization;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Management.Infrastructure;
using Dbg = System.Management.Automation.Diagnostics;

#if !CORECLR
using System.Data;
using System.DirectoryServices;
#else
// Use stubs for Serializable attribute and ISerializable related types.
using Microsoft.PowerShell.CoreClr.Stubs;
#endif

#pragma warning disable 1634, 1691 // Stops compiler from warning about unknown warnings
#pragma warning disable 56500

namespace System.Management.Automation
{
    /// <summary>
    /// Wraps an object providing alternate views of the available members 
    /// and ways to extend them. Members can be methods, properties, 
    /// parameterized properties, etc.
    /// </summary>
    /// <remarks>
    /// It is permitted to subclass <see cref="PSObject"/>
    /// but there is no established scenario for doing this, nor has it been tested.
    /// </remarks>
#if !CORECLR // TypeDescriptionProvider is not supported in CoreCLR
    [TypeDescriptionProvider(typeof(PSObjectTypeDescriptionProvider))]
#endif
    [Serializable]
    public class PSObject : IFormattable, IComparable, ISerializable, IDynamicMetaObjectProvider
    {
        #region constructors

        #region private to the constructors

        internal TypeTable GetTypeTable()
        {
            TypeTable typeTable;
            if (_typeTable != null && _typeTable.TryGetTarget(out typeTable))
            {
                return typeTable;
            }

            ExecutionContext context = LocalPipeline.GetExecutionContextFromTLS();
            if (context != null)
            {
                return context.TypeTable;
            }

            return null;
        }

        internal static T TypeTableGetMemberDelegate<T>(PSObject msjObj, string name) where T : PSMemberInfo
        {
            TypeTable table = msjObj.GetTypeTable();
            return TypeTableGetMemberDelegate<T>(msjObj, table, name);
        }

        private static T TypeTableGetMemberDelegate<T>(PSObject msjObj, TypeTable typeTableToUse, string name) where T : PSMemberInfo
        {
            if (typeTableToUse == null)
            {
                return null;
            }

            PSMemberInfoInternalCollection<PSMemberInfo> allMembers = typeTableToUse.GetMembers<PSMemberInfo>(msjObj.InternalTypeNames);
            PSMemberInfo member = allMembers[name];
            if (member == null)
            {
                PSObject.memberResolution.WriteLine("\"{0}\" NOT present in type table.", name);
                return null;
            }
            T memberAsT = member as T;
            if (memberAsT != null)
            {
                PSObject.memberResolution.WriteLine("\"{0}\" present in type table.", name);
                return memberAsT;
            }
            PSObject.memberResolution.WriteLine("\"{0}\" from types table ignored because it has type {1} instead of {2}.",
                name, member.GetType(), typeof(T));
            return null;
        }

        internal static PSMemberInfoInternalCollection<T> TypeTableGetMembersDelegate<T>(PSObject msjObj) where T : PSMemberInfo
        {
            TypeTable table = msjObj.GetTypeTable();
            return TypeTableGetMembersDelegate<T>(msjObj, table);
        }

        internal static PSMemberInfoInternalCollection<T> TypeTableGetMembersDelegate<T>(PSObject msjObj, TypeTable typeTableToUse) where T : PSMemberInfo
        {
            if (typeTableToUse == null)
            {
                return new PSMemberInfoInternalCollection<T>();
            }
            PSMemberInfoInternalCollection<T> members = typeTableToUse.GetMembers<T>(msjObj.InternalTypeNames);
            PSObject.memberResolution.WriteLine("Type table members: {0}.", members.Count);
            return members;
        }

        private static T AdapterGetMemberDelegate<T>(PSObject msjObj, string name) where T : PSMemberInfo
        {
            if (msjObj.isDeserialized)
            {
                if (msjObj.adaptedMembers == null)
                {
                    return null;
                }
                T adaptedMember = msjObj.adaptedMembers[name] as T;
                PSObject.memberResolution.WriteLine("Serialized adapted member: {0}.", adaptedMember == null ? "not found" : adaptedMember.Name);
                return adaptedMember;
            }
            T retValue = msjObj.InternalAdapter.BaseGetMember<T>(msjObj._immediateBaseObject, name);
            PSObject.memberResolution.WriteLine("Adapted member: {0}.", retValue == null ? "not found" : retValue.Name);
            return retValue;
        }

        internal static PSMemberInfoInternalCollection<U> TransformMemberInfoCollection<T, U>(PSMemberInfoCollection<T> source) where T : PSMemberInfo where U : PSMemberInfo
        {
            if (typeof(T) == typeof(U))
            {
                // If the types are the same, don't make a copy, return the cached collection.
                return source as PSMemberInfoInternalCollection<U>;
            }

            PSMemberInfoInternalCollection<U> returnValue = new PSMemberInfoInternalCollection<U>();
            foreach (T member in source)
            {
                U tAsU = member as U;
                if (tAsU != null)
                {
                    returnValue.Add(tAsU);
                }
            }
            return returnValue;
        }

        private static PSMemberInfoInternalCollection<T> AdapterGetMembersDelegate<T>(PSObject msjObj) where T : PSMemberInfo
        {
            if (msjObj.isDeserialized)
            {
                if (msjObj.adaptedMembers == null)
                {
                    return new PSMemberInfoInternalCollection<T>();
                }
                PSObject.memberResolution.WriteLine("Serialized adapted members: {0}.", msjObj.adaptedMembers.Count);
                return TransformMemberInfoCollection<PSPropertyInfo, T>(msjObj.adaptedMembers);
            }
            PSMemberInfoInternalCollection<T> retValue = msjObj.InternalAdapter.BaseGetMembers<T>(msjObj._immediateBaseObject);
            PSObject.memberResolution.WriteLine("Adapted members: {0}.", retValue.VisibleCount);
            return retValue;
        }

        private static PSMemberInfoInternalCollection<T> DotNetGetMembersDelegate<T>(PSObject msjObj) where T : PSMemberInfo
        {
            // Don't lookup dotnet members if the object doesn't insist.
            if (null != msjObj.InternalAdapterSet.DotNetAdapter)
            {
                PSMemberInfoInternalCollection<T> retValue = msjObj.InternalAdapterSet.DotNetAdapter.BaseGetMembers<T>(msjObj._immediateBaseObject);
                PSObject.memberResolution.WriteLine("DotNet members: {0}.", retValue.VisibleCount);
                return retValue;
            }

            return new PSMemberInfoInternalCollection<T>();
        }

        private static T DotNetGetMemberDelegate<T>(PSObject msjObj, string name) where T : PSMemberInfo
        {
            // Don't lookup dotnet member if the object doesn't insist.
            if (null != msjObj.InternalAdapterSet.DotNetAdapter)
            {
                T retValue = msjObj.InternalAdapterSet.DotNetAdapter.BaseGetMember<T>(msjObj._immediateBaseObject, name);
                PSObject.memberResolution.WriteLine("DotNet member: {0}.", retValue == null ? "not found" : retValue.Name);
                return retValue;
            }

            return null;
        }

        private static Collection<CollectionEntry<PSMemberInfo>> s_memberCollection = GetMemberCollection(PSMemberViewTypes.All);
        private static Collection<CollectionEntry<PSMethodInfo>> s_methodCollection = GetMethodCollection();
        private static Collection<CollectionEntry<PSPropertyInfo>> s_propertyCollection = GetPropertyCollection(PSMemberViewTypes.All);

        /// <summary>
        /// A collection of delegates to get Extended/Adapted/Dotnet members based on the 
        /// <paramref name="viewType"/>
        /// </summary>
        /// <param name="viewType">
        /// A filter to select Extended/Adapted/Dotnet view of the object
        /// </param>
        /// <returns></returns>
        internal static Collection<CollectionEntry<PSMemberInfo>> GetMemberCollection(PSMemberViewTypes viewType)
        {
            return GetMemberCollection(viewType, null);
        }

        /// <summary>
        /// A collection of delegates to get Extended/Adapted/Dotnet members based on the 
        /// <paramref name="viewType"/>
        /// </summary>
        /// <param name="viewType">
        /// A filter to select Extended/Adapted/Dotnet view of the object
        /// </param>
        /// <param name="backupTypeTable">
        /// Backup type table to use if there is no execution context associated with the current thread
        /// </param>
        /// <returns></returns>
        internal static Collection<CollectionEntry<PSMemberInfo>> GetMemberCollection(
            PSMemberViewTypes viewType,
            TypeTable backupTypeTable)
        {
            Collection<CollectionEntry<PSMemberInfo>> returnValue = new Collection<CollectionEntry<PSMemberInfo>>();
            if ((viewType & PSMemberViewTypes.Extended) == PSMemberViewTypes.Extended)
            {
                if (backupTypeTable == null)
                {
                    returnValue.Add(new CollectionEntry<PSMemberInfo>(
                        PSObject.TypeTableGetMembersDelegate<PSMemberInfo>,
                        PSObject.TypeTableGetMemberDelegate<PSMemberInfo>,
                        true, true, "type table members"));
                }
                else
                {
                    returnValue.Add(new CollectionEntry<PSMemberInfo>(
                        delegate (PSObject msjObj)
                        {
                            return TypeTableGetMembersDelegate<PSMemberInfo>(msjObj, backupTypeTable);
                        },
                        delegate (PSObject msjObj, string name)
                        {
                            return TypeTableGetMemberDelegate<PSMemberInfo>(msjObj, backupTypeTable, name);
                        },
                        true, true, "type table members"));
                }
            }
            if ((viewType & PSMemberViewTypes.Adapted) == PSMemberViewTypes.Adapted)
            {
                returnValue.Add(new CollectionEntry<PSMemberInfo>(
                    PSObject.AdapterGetMembersDelegate<PSMemberInfo>,
                    PSObject.AdapterGetMemberDelegate<PSMemberInfo>,
                    false, false, "adapted members"));
            }
            if ((viewType & PSMemberViewTypes.Base) == PSMemberViewTypes.Base)
            {
                returnValue.Add(new CollectionEntry<PSMemberInfo>(
                    PSObject.DotNetGetMembersDelegate<PSMemberInfo>,
                    PSObject.DotNetGetMemberDelegate<PSMemberInfo>,
                    false, false, "clr members"));
            }
            return returnValue;
        }

        private static Collection<CollectionEntry<PSMethodInfo>> GetMethodCollection()
        {
            Collection<CollectionEntry<PSMethodInfo>> returnValue = new Collection<CollectionEntry<PSMethodInfo>>();
            returnValue.Add(new CollectionEntry<PSMethodInfo>(
                PSObject.TypeTableGetMembersDelegate<PSMethodInfo>,
                PSObject.TypeTableGetMemberDelegate<PSMethodInfo>,
                true, true, "type table members"));
            returnValue.Add(new CollectionEntry<PSMethodInfo>(
                PSObject.AdapterGetMembersDelegate<PSMethodInfo>,
                PSObject.AdapterGetMemberDelegate<PSMethodInfo>,
                false, false, "adapted members"));
            returnValue.Add(new CollectionEntry<PSMethodInfo>(
                PSObject.DotNetGetMembersDelegate<PSMethodInfo>,
                PSObject.DotNetGetMemberDelegate<PSMethodInfo>,
                false, false, "clr members"));
            return returnValue;
        }

        /// <summary>
        /// A collection of delegates to get Extended/Adapted/Dotnet properties based on the 
        /// <paramref name="viewType"/>
        /// </summary>
        /// <param name="viewType">
        /// A filter to select Extended/Adapted/Dotnet view of the object
        /// </param>
        /// <returns></returns>
        internal static Collection<CollectionEntry<PSPropertyInfo>> GetPropertyCollection(
            PSMemberViewTypes viewType)
        {
            return GetPropertyCollection(viewType, null);
        }

        /// <summary>
        /// A collection of delegates to get Extended/Adapted/Dotnet properties based on the 
        /// <paramref name="viewType"/>
        /// </summary>
        /// <param name="viewType">
        /// A filter to select Extended/Adapted/Dotnet view of the object
        /// </param>
        /// <param name="backupTypeTable">
        /// Backup type table to use if there is no execution context associated with the current thread
        /// </param>
        /// <returns></returns>
        internal static Collection<CollectionEntry<PSPropertyInfo>> GetPropertyCollection(
            PSMemberViewTypes viewType,
            TypeTable backupTypeTable)
        {
            Collection<CollectionEntry<PSPropertyInfo>> returnValue = new Collection<CollectionEntry<PSPropertyInfo>>();
            if ((viewType & PSMemberViewTypes.Extended) == PSMemberViewTypes.Extended)
            {
                if (backupTypeTable == null)
                {
                    returnValue.Add(new CollectionEntry<PSPropertyInfo>(
                        PSObject.TypeTableGetMembersDelegate<PSPropertyInfo>,
                        PSObject.TypeTableGetMemberDelegate<PSPropertyInfo>,
                        true, true, "type table members"));
                }
                else
                {
                    returnValue.Add(new CollectionEntry<PSPropertyInfo>(
                        delegate (PSObject msjObj)
                        {
                            return TypeTableGetMembersDelegate<PSPropertyInfo>(msjObj, backupTypeTable);
                        },
                        delegate (PSObject msjObj, string name)
                        {
                            return TypeTableGetMemberDelegate<PSPropertyInfo>(msjObj, backupTypeTable, name);
                        },
                        true, true, "type table members"));
                }
            }
            if ((viewType & PSMemberViewTypes.Adapted) == PSMemberViewTypes.Adapted)
            {
                returnValue.Add(new CollectionEntry<PSPropertyInfo>(
                    PSObject.AdapterGetMembersDelegate<PSPropertyInfo>,
                    PSObject.AdapterGetMemberDelegate<PSPropertyInfo>,
                    false, false, "adapted members"));
            }
            if ((viewType & PSMemberViewTypes.Base) == PSMemberViewTypes.Base)
            {
                returnValue.Add(new CollectionEntry<PSPropertyInfo>(
                    PSObject.DotNetGetMembersDelegate<PSPropertyInfo>,
                    PSObject.DotNetGetMemberDelegate<PSPropertyInfo>,
                    false, false, "clr members"));
            }
            return returnValue;
        }

        private void CommonInitialization(object obj)
        {
            Diagnostics.Assert(obj != null, "checked by callers");
            if (obj is PSCustomObject)
            {
                this.immediateBaseObjectIsEmpty = true;
            }

            _immediateBaseObject = obj;
            var context = LocalPipeline.GetExecutionContextFromTLS();
            _typeTable = context != null ? context.TypeTableWeakReference : null;
        }

        internal static readonly DotNetAdapter dotNetInstanceAdapter = new DotNetAdapter();
        private static readonly DotNetAdapter s_baseAdapterForAdaptedObjects = new BaseDotNetAdapterForAdaptedObjects();
        internal static readonly DotNetAdapter dotNetStaticAdapter = new DotNetAdapter(true);

        private static readonly AdapterSet s_dotNetInstanceAdapterSet = new AdapterSet(dotNetInstanceAdapter, null);
        private static readonly AdapterSet s_mshMemberSetAdapter = new AdapterSet(new PSMemberSetAdapter(), null);
        private static readonly AdapterSet s_mshObjectAdapter = new AdapterSet(new PSObjectAdapter(), null);
        private static PSObject.AdapterSet s_cimInstanceAdapter =
            new PSObject.AdapterSet(new ThirdPartyAdapter(typeof(Microsoft.Management.Infrastructure.CimInstance),
                                                          new Microsoft.PowerShell.Cim.CimInstanceAdapter()),
                                    PSObject.dotNetInstanceAdapter);

#if !CORECLR // WMIv1/ADSI/DataRow/DataRowView Adapters Not Supported On CSS
        private static readonly AdapterSet managementObjectAdapter = new AdapterSet(new ManagementObjectAdapter(), dotNetInstanceAdapter);
        private static readonly AdapterSet managementClassAdapter = new AdapterSet(new ManagementClassApdapter(), dotNetInstanceAdapter);
        private static readonly AdapterSet directoryEntryAdapter = new AdapterSet(new DirectoryEntryAdapter(), dotNetInstanceAdapter);
        private static readonly AdapterSet dataRowViewAdapter = new AdapterSet(new DataRowViewAdapter(), s_baseAdapterForAdaptedObjects);
        private static readonly AdapterSet dataRowAdapter = new AdapterSet(new DataRowAdapter(), s_baseAdapterForAdaptedObjects);
#endif
        private static readonly AdapterSet s_xmlNodeAdapter = new AdapterSet(new XmlNodeAdapter(), s_baseAdapterForAdaptedObjects);

        #region Adapter Mappings

        private static readonly ConcurrentDictionary<Type, AdapterSet> s_adapterMapping = new ConcurrentDictionary<Type, AdapterSet>();
        private static readonly List<Func<object, AdapterSet>> s_adapterSetMappers = new List<Func<object, AdapterSet>>
                                                                                    {
                                                                                        MappedInternalAdapterSet
                                                                                    };

        internal static void RegisterAdapterMapping(Func<object, AdapterSet> mapper)
        {
            lock (s_adapterSetMappers)
            {
                s_adapterSetMappers.Add(mapper);
            }
        }

        private static AdapterSet MappedInternalAdapterSet(object obj)
        {
            if (obj is PSMemberSet) { return PSObject.s_mshMemberSetAdapter; }
            if (obj is PSObject) { return PSObject.s_mshObjectAdapter; }
            if (obj is CimInstance) { return PSObject.s_cimInstanceAdapter; }

#if !CORECLR // WMIv1/ADSI/DataRow/DataRowView Adapters Not Supported On CSS
            if (obj is ManagementClass) { return PSObject.managementClassAdapter; }
            if (obj is ManagementBaseObject) { return PSObject.managementObjectAdapter; }
            if (obj is DirectoryEntry) { return PSObject.directoryEntryAdapter; }
            if (obj is DataRowView) { return PSObject.dataRowViewAdapter; }
            if (obj is DataRow) { return PSObject.dataRowAdapter; }
#endif
            if (obj is XmlNode) { return PSObject.s_xmlNodeAdapter; }
            return null;
        }

        /// <summary>
        /// Returns the adapter corresponding to obj.GetType()
        /// </summary>
        /// <returns>the adapter set corresponding to obj.GetType()</returns>
        internal static AdapterSet GetMappedAdapter(object obj, TypeTable typeTable)
        {
            Type objectType = obj.GetType();

            if (typeTable != null)
            {
                PSObject.AdapterSet adapter = typeTable.GetTypeAdapter(objectType);

                if (adapter != null)
                {
                    // We don't cache results found via the type table because type tables may differ b/w runspaces,
                    // our cache is app domain wide, and the key is simply the type.
                    return adapter;
                }
            }

            AdapterSet result;
            if (s_adapterMapping.TryGetValue(objectType, out result))
            {
                return result;
            }

            lock (s_adapterSetMappers)
            {
                foreach (var mapper in s_adapterSetMappers)
                {
                    result = mapper(obj);
                    if (result != null)
                    {
                        break;
                    }
                }
            }

            if (result == null)
            {
                if (objectType.IsComObject())
                {
                    // All WinRT types are COM types.
                    // All WinRT types would contain the TypeAttributes flag being set to WindowsRunTime.
                    if (WinRTHelper.IsWinRTType(objectType))
                    {
                        result = PSObject.s_dotNetInstanceAdapterSet;
                    }

                    // We are not using IsAssignableFrom because we want to avoid
                    // using the COM adapters for Primary Interop Assembly types
                    // and they derive from System.__ComObject.
                    // We are not using Type.Equals because System.__ComObject is
                    // not public.
                    else if (objectType.FullName.Equals("System.__ComObject"))
                    {
                        // We don't cache the adapter set for COM objects because the adapter varies depending on the COM object, but often
                        // (typically), the runtime type is always the same.  That's why this if statement is here and returns.

                        ComTypeInfo info = ComTypeInfo.GetDispatchTypeInfo(obj);
                        return info != null
                                   ? new AdapterSet(new ComAdapter(info), dotNetInstanceAdapter)
                                   : PSObject.s_dotNetInstanceAdapterSet;
                    }
                    else
                    {
                        ComTypeInfo info = ComTypeInfo.GetDispatchTypeInfo(obj);
                        result = info != null
                                   ? new AdapterSet(new DotNetAdapterWithComTypeName(info), null)
                                   : PSObject.s_dotNetInstanceAdapterSet;
                    }
                }
                else
                {
                    result = PSObject.s_dotNetInstanceAdapterSet;
                }
            }

            var existingOrNew = s_adapterMapping.GetOrAdd(objectType, result);
            Diagnostics.Assert(existingOrNew == result, "There is a logic error in caching adapter sets.");

            return result;
        }

        #endregion

        internal static AdapterSet CreateThirdPartyAdapterSet(Type adaptedType, PSPropertyAdapter adapter)
        {
            return new AdapterSet(new ThirdPartyAdapter(adaptedType, adapter), s_baseAdapterForAdaptedObjects);
        }

        #endregion private to the constructors

        /// <summary>
        /// Initializes a new instance of PSObject with an PSCustomObject BaseObject
        /// </summary>
        public PSObject()
        {
            CommonInitialization(PSCustomObject.SelfInstance);
        }

        /// <summary>
        /// Initializes a new instance of PSObject wrapping obj (accessible through BaseObject).
        /// </summary>
        /// <param name="obj">object we are wrapping</param>
        /// <exception cref="ArgumentNullException">if <paramref name="obj"/> is null</exception>
        [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames", MessageId = "obj", Justification = "This is shipped as part of V1. Retaining this for backward compatibility.")]
        public PSObject(object obj)
        {
            if (obj == null)
            {
                throw PSTraceSource.NewArgumentNullException("obj");
            }
            CommonInitialization(obj);
        }

        /// <summary>
        /// Creates a PSObject from an ISerializable context
        /// </summary>
        /// <param name="info">Serialization information for this instance</param>
        /// <param name="context">The streaming context for this instance</param>
        protected PSObject(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw PSTraceSource.NewArgumentNullException("info");
            }

            string serializedData = info.GetValue("CliXml", typeof(string)) as string;

            if (serializedData == null)
            {
                throw PSTraceSource.NewArgumentNullException("info");
            }

            PSObject result = PSObject.AsPSObject(PSSerializer.Deserialize(serializedData));
            CommonInitialization(result.ImmediateBaseObject);

            CopyDeserializerFields(source: result, target: this);
        }

        internal static PSObject ConstructPSObjectFromSerializationInfo(SerializationInfo info, StreamingContext context)
        {
            return new PSObject(info, context);
        }

        #endregion constructors

        #region fields

        #region instance fields
        private object _lockObject = new Object();

        /// <summary>
        /// If this is non-null return this string as the ToString() for this wrapped object.
        /// </summary>
        internal string TokenText;

        /// <summary>
        /// This is the main field in the class representing
        /// the System.Object we are encapsulating
        /// </summary>
        private object _immediateBaseObject;


        private WeakReference<TypeTable> _typeTable;

        /// <summary>
        /// This is the adapter that will depend on the type of baseObject.
        /// </summary>
        internal Adapter InternalAdapter
        {
            get
            {
                return InternalAdapterSet.OriginalAdapter;
            }
            set
            {
                InternalAdapterSet.OriginalAdapter = value;
            }
        }

        /// <summary>
        /// This is the adapter that is used to resolve the base dotnet members for an
        /// adapted object. If an object is not adapted, this will be null.
        /// </summary>
        /// <remarks>
        /// If an object is not adapted, InternalAdapter will use the dotnet adapter.
        /// So there is no point falling back to the same dotnet adapter.
        /// 
        /// If an object is adapted, this adapter will be used to resolve the dotnet
        /// members.
        /// </remarks>
        internal Adapter InternalBaseDotNetAdapter
        {
            get
            {
                return InternalAdapterSet.DotNetAdapter;
            }
        }

        /// <summary>
        /// This is the adapter set that will contain the adapter of the baseObject 
        /// and the ultimate .net member lookup adapter.
        /// See <see cref="PSObject.AdapterSet"/> for explanation.
        /// </summary>
        private AdapterSet InternalAdapterSet
        {
            get
            {
                if (null == _adapterSet)
                {
                    lock (_lockObject)
                    {
                        if (null == _adapterSet)
                        {
                            _adapterSet = GetMappedAdapter(_immediateBaseObject, GetTypeTable());
                        }
                    }
                }

                return _adapterSet;
            }
        }
        private AdapterSet _adapterSet;

        internal bool hasGeneratedReservedMembers;

        internal PSMemberInfoInternalCollection<PSMemberInfo> InstanceMembers
        {
            get
            {
                if (_instanceMembers == null)
                {
                    lock (_lockObject)
                    {
                        if (_instanceMembers == null)
                        {
                            _instanceMembers =
                                s_instanceMembersResurrectionTable.GetValue(
                                    GetKeyForResurrectionTables(this),
                                    _ => new PSMemberInfoInternalCollection<PSMemberInfo>());
                        }
                    }
                }

                return _instanceMembers;
            }
            set
            {
                _instanceMembers = value;
            }
        }
        private PSMemberInfoInternalCollection<PSMemberInfo> _instanceMembers;

        internal static bool HasInstanceMembers(object obj, out PSMemberInfoInternalCollection<PSMemberInfo> instanceMembers)
        {
            var psobj = obj as PSObject;
            if (psobj != null)
            {
                lock (psobj)
                {
                    if (psobj._instanceMembers == null)
                    {
                        s_instanceMembersResurrectionTable.TryGetValue(GetKeyForResurrectionTables(psobj),
                                                                        out psobj._instanceMembers);
                    }
                }
                instanceMembers = psobj._instanceMembers;
            }
            else if (obj != null)
            {
                s_instanceMembersResurrectionTable.TryGetValue(GetKeyForResurrectionTables(obj), out instanceMembers);
            }
            else
            {
                instanceMembers = null;
            }

            return instanceMembers != null && instanceMembers.Count > 0;
        }

        private static readonly ConditionalWeakTable<object, PSMemberInfoInternalCollection<PSMemberInfo>> s_instanceMembersResurrectionTable =
            new ConditionalWeakTable<object, PSMemberInfoInternalCollection<PSMemberInfo>>();

        /// <summary>
        /// Indicate whether we store the instance members and type names locally
        /// for this PSObject instance.
        /// </summary>
        private bool _storeTypeNameAndInstanceMembersLocally = false;

        /// <summary>
        /// Members from the adapter of the object before it was serialized
        /// Null for live objects but not null for deserialized objects
        /// </summary>
        internal PSMemberInfoInternalCollection<PSPropertyInfo> adaptedMembers;

        /// <summary>
        /// Members from the adapter of the object before it was serialized
        /// Null for live objects but not null for deserialized objects
        /// </summary>
        internal PSMemberInfoInternalCollection<PSPropertyInfo> clrMembers;

        /// <summary>
        /// Set to true when the BaseObject is PSCustomObject
        /// </summary>
        internal bool immediateBaseObjectIsEmpty;

        internal static PSTraceSource memberResolution = PSTraceSource.GetTracer("MemberResolution", "Traces the resolution from member name to the member. A member can be a property, method, etc.", false);

        #endregion instance fields


        #endregion fields

        #region properties

        /// <summary>
        /// Gets the member collection
        /// </summary>
        public PSMemberInfoCollection<PSMemberInfo> Members
        {
            get
            {
                if (_members == null)
                {
                    lock (_lockObject)
                    {
                        if (_members == null)
                        {
                            _members = new PSMemberInfoIntegratingCollection<PSMemberInfo>(this, s_memberCollection);
                        }
                    }
                }

                return _members;
            }
        }
        private PSMemberInfoIntegratingCollection<PSMemberInfo> _members;

        /// <summary>
        /// Gets the Property collection, or the members that are actually properties.
        /// </summary>
        public PSMemberInfoCollection<PSPropertyInfo> Properties
        {
            get
            {
                if (_properties == null)
                {
                    lock (_lockObject)
                    {
                        if (_properties == null)
                        {
                            _properties = new PSMemberInfoIntegratingCollection<PSPropertyInfo>(this, s_propertyCollection);
                        }
                    }
                }

                return _properties;
            }
        }
        private PSMemberInfoIntegratingCollection<PSPropertyInfo> _properties;

        /// <summary>
        /// Gets the Method collection, or the members that are actually methods.
        /// </summary>
        public PSMemberInfoCollection<PSMethodInfo> Methods
        {
            get
            {
                if (_methods == null)
                {
                    lock (_lockObject)
                    {
                        if (_methods == null)
                        {
                            _methods = new PSMemberInfoIntegratingCollection<PSMethodInfo>(this, s_methodCollection);
                        }
                    }
                }

                return _methods;
            }
        }
        private PSMemberInfoIntegratingCollection<PSMethodInfo> _methods;


        /// <summary>
        /// Gets the object we are directly wrapping.
        /// </summary>
        /// <remarks>If the ImmediateBaseObject is another PSObject, 
        /// that PSObject will be returned.</remarks>
        public object ImmediateBaseObject
        {
            get
            {
                return _immediateBaseObject;
            }
        }

        /// <summary>
        /// Gets the object we are wrapping.
        /// </summary>
        /// <remarks>If the ImmediateBaseObject is another PSObject, this property
        /// will return its BaseObject.</remarks>
        public object BaseObject
        {
            get
            {
                object returnValue = null;
                PSObject mshObj = this;
                do
                {
                    returnValue = mshObj._immediateBaseObject;
                    mshObj = returnValue as PSObject;
                } while (mshObj != null);

                return returnValue;
            }
        }

        /// <summary>
        /// Gets the type names collection initially containing the object type hierarchy.
        /// </summary>
        public Collection<string> TypeNames
        {
            get
            {
                var result = InternalTypeNames;
                if (result.IsReadOnly)
                {
                    lock (_lockObject)
                    {
                        // Check again after acquiring the lock to ensure some other thread didn't make the copy.
                        if (result.IsReadOnly)
                        {
                            _typeNames = s_typeNamesResurrectionTable.GetValue(
                                            GetKeyForResurrectionTables(this),
                                            _ => new ConsolidatedString(_typeNames));

                            object baseObj = BaseObject;
                            // In most cases, the TypeNames will be modified after it's returned
                            if (baseObj != null) { PSVariableAssignmentBinder.NoteTypeHasInstanceMemberOrTypeName(baseObj.GetType()); }
                            return _typeNames;
                        }
                    }
                }

                return result;
            }
        }

        internal ConsolidatedString InternalTypeNames
        {
            get
            {
                if (_typeNames == null)
                {
                    lock (_lockObject)
                    {
                        if (_typeNames == null)
                        {
                            if (!s_typeNamesResurrectionTable.TryGetValue(GetKeyForResurrectionTables(this), out _typeNames))
                            {
                                // We don't cache this typename in the resurrection table because it's cached in the psobject,
                                // and the assumption is we'll usually get the value directly from the PSObject, so it's not
                                // needed in the resurrection table.
                                //
                                // If we hand out the typename to a client that could change it (through the public property
                                // TypeNames), then we'll need to store the copy in the resurrection table.
                                _typeNames = InternalAdapter.BaseGetTypeNameHierarchy(_immediateBaseObject);
                            }
                        }
                    }
                }

                return _typeNames;
            }
            set
            {
                _typeNames = value;
            }
        }

        internal static ConsolidatedString GetTypeNames(object obj)
        {
            var psobj = obj as PSObject;
            if (psobj != null)
            {
                return psobj.InternalTypeNames;
            }

            ConsolidatedString result;
            if (HasInstanceTypeName(obj, out result))
            {
                return result;
            }
            return PSObject.GetMappedAdapter(obj, null).OriginalAdapter.BaseGetTypeNameHierarchy(obj);
        }

        internal static bool HasInstanceTypeName(object obj, out ConsolidatedString result)
        {
            return s_typeNamesResurrectionTable.TryGetValue(GetKeyForResurrectionTables(obj), out result);
        }

        private ConsolidatedString _typeNames;
        private static readonly ConditionalWeakTable<object, ConsolidatedString> s_typeNamesResurrectionTable = new ConditionalWeakTable<object, ConsolidatedString>();

        #endregion properties

        #region static methods

        /// <summary>
        /// 
        /// </summary>
        /// <param name="valueToConvert"></param>
        /// <returns></returns>
        public static implicit operator PSObject(int valueToConvert)
        {
            return PSObject.AsPSObject(valueToConvert);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="valueToConvert"></param>
        /// <returns></returns>
        public static implicit operator PSObject(string valueToConvert)
        {
            return PSObject.AsPSObject(valueToConvert);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="valueToConvert"></param>
        /// <returns></returns>
        public static implicit operator PSObject(Hashtable valueToConvert)
        {
            return PSObject.AsPSObject(valueToConvert);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="valueToConvert"></param>
        /// <returns></returns>
        public static implicit operator PSObject(double valueToConvert)
        {
            return PSObject.AsPSObject(valueToConvert);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="valueToConvert"></param>
        /// <returns></returns>
        public static implicit operator PSObject(bool valueToConvert)
        {
            return PSObject.AsPSObject(valueToConvert);
        }

        /// <summary>
        /// If obj is not an PSObject, it is returned. Otherwise, retrieves 
        /// the first non PSObject or PSObject with CustomBaseObject
        /// in the PSObject - BaseObject chain.
        /// </summary>
        internal static object Base(object obj)
        {
            PSObject mshObj = obj as PSObject;
            if (mshObj == null)
            {
                return obj;
            }
            if (mshObj == AutomationNull.Value)
                return null;
            if (mshObj.immediateBaseObjectIsEmpty)
            {
                return obj;
            }
            object returnValue = null;
            do
            {
                returnValue = mshObj._immediateBaseObject;
                mshObj = returnValue as PSObject;
            } while ((mshObj != null) && (!mshObj.immediateBaseObjectIsEmpty));

            return returnValue;
        }

        internal static PSMemberInfo GetStaticCLRMember(object obj, string methodName)
        {
            obj = PSObject.Base(obj);
            if (obj == null || methodName == null || methodName.Length == 0)
            {
                return null;
            }
            var objType = obj as Type ?? obj.GetType();
            return dotNetStaticAdapter.BaseGetMember<PSMemberInfo>(objType, methodName);
        }

        /// <summary>
        /// If obj is an PSObject it will be returned as is, otherwise
        /// a new PSObject will be created based on obj.
        /// </summary>
        /// <param name="obj">object to be wrapped</param>
        /// <returns>
        /// obj or a new PSObject whose BaseObject is obj
        /// </returns>
        /// <exception cref="ArgumentNullException">if <paramref name="obj"/> is null</exception>
        [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames", MessageId = "obj", Justification = "This is shipped as part of V1. Retaining this for backward compatibility.")]
        public static PSObject AsPSObject(object obj)
        {
            return AsPSObject(obj, false);
        }

        /// <summary>
        /// If obj is a PSObject, it will be returned as is, otherwise a new
        /// PSObject will be created on obj. Its InstanceMembers and TypeNames
        /// will be initialized if we are not going to use the ResurrectionTables
        /// for this PSObject instance.
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="storeTypeNameAndInstanceMembersLocally"></param>
        /// <returns></returns>
        [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames", MessageId = "obj", Justification = "AsPSObject is shipped as part of V1. This is a new overload method.")]
        internal static PSObject AsPSObject(object obj, bool storeTypeNameAndInstanceMembersLocally)
        {
            if (obj == null)
            {
                throw PSTraceSource.NewArgumentNullException("obj");
            }

            PSObject so = obj as PSObject;

            if (so != null)
            {
                return so;
            }

            return new PSObject(obj) { _storeTypeNameAndInstanceMembersLocally = storeTypeNameAndInstanceMembersLocally };
        }

        /// <summary>
        /// Returns an object that should be used as a key for getting 1) instance members and 2) type names
        /// - If base object is a PSCustomObject or a string or a type
        ///   then the most nested wrapping PSObject is returned (the PSObject where immediateBaseObject=PSCustomObject/string/type)
        /// - Otherwise the base object is returned
        /// This is a temporary fix for Win8 : 254345 - Job Failed By Throwing ExtendedTypeSystemException.
        /// </summary>
        /// <returns></returns>
        internal static object GetKeyForResurrectionTables(object obj)
        {
            var pso = obj as PSObject;
            if (pso == null)
            {
                return obj;
            }

            PSObject psObjectAboveBase = pso;
            while (psObjectAboveBase.ImmediateBaseObject is PSObject)
            {
                psObjectAboveBase = (PSObject)psObjectAboveBase.ImmediateBaseObject;
            }

            if (psObjectAboveBase.ImmediateBaseObject is PSCustomObject
                || psObjectAboveBase.ImmediateBaseObject is string
                || pso._storeTypeNameAndInstanceMembersLocally)
            {
                return psObjectAboveBase;
            }

            return psObjectAboveBase.ImmediateBaseObject;
        }

        #endregion static methods

        #region instance methods
        #region ToString

        private static string GetSeparator(ExecutionContext context, string separator)
        {
            if (separator != null)
            {
                return separator;
            }
            if (context != null)
            {
                object obj = context.GetVariableValue(SpecialVariables.OFSVarPath);
                if (obj != null)
                {
                    return obj.ToString();
                }
            }
            return " ";
        }

        internal static string ToStringEnumerator(ExecutionContext context, IEnumerator enumerator, string separator, string format, IFormatProvider formatProvider)
        {
            StringBuilder returnValue = new StringBuilder();
            string separatorToUse = GetSeparator(context, separator);
            while (enumerator.MoveNext())
            {
                object obj = enumerator.Current;
                returnValue.Append(PSObject.ToString(context, obj, separator, format, formatProvider, false, false));
                returnValue.Append(separatorToUse);
            }
            if (returnValue.Length == 0)
            {
                return String.Empty;
            }
            int separatorLength = separatorToUse.Length;
            returnValue.Remove(returnValue.Length - separatorLength, separatorLength);
            return returnValue.ToString();
        }

        internal static string ToStringEnumerable(ExecutionContext context, IEnumerable enumerable, string separator, string format, IFormatProvider formatProvider)
        {
            StringBuilder returnValue = new StringBuilder();
            string separatorToUse = GetSeparator(context, separator);
            foreach (object obj in enumerable)
            {
                if (obj != null)
                {
                    PSObject mshObj = PSObject.AsPSObject(obj);
                    returnValue.Append(PSObject.ToString(context, mshObj, separator, format, formatProvider, false, false));
                }
                returnValue.Append(separatorToUse);
            }
            if (returnValue.Length == 0)
            {
                return String.Empty;
            }
            int separatorLength = separatorToUse.Length;
            returnValue.Remove(returnValue.Length - separatorLength, separatorLength);
            return returnValue.ToString();
        }

        private static string ToStringEmptyBaseObject(ExecutionContext context, PSObject mshObj, string separator, string format, IFormatProvider formatProvider)
        {
            StringBuilder returnValue = new StringBuilder("@{");
            bool isFirst = true;
            foreach (PSPropertyInfo property in mshObj.Properties)
            {
                if (!isFirst)
                {
                    returnValue.Append("; ");
                }
                isFirst = false;
                returnValue.Append(property.Name);
                returnValue.Append("=");

                // Don't evaluate script properties during a ToString() operation.
                Object propertyValue = null;
                if (property is PSScriptProperty)
                {
                    propertyValue = property.GetType().FullName;
                }
                else
                {
                    propertyValue = property.Value;
                }

                returnValue.Append(PSObject.ToString(context, propertyValue, separator, format, formatProvider, false, false));
            }
            if (isFirst)
            {
                return string.Empty;
            }
            returnValue.Append("}");
            return returnValue.ToString();
        }

        /// <summary>
        /// Returns the string representation of obj.
        /// </summary>
        /// <param name="context">ExecutionContext used to fetch the separator. </param>
        /// <param name="obj">
        /// object we are trying to call ToString on. If this is not an PSObject we try
        /// enumerating and if that fails we call obj.ToString.
        /// If this is an PSObject, we look for a brokered ToString. 
        /// If it is not present, and the BaseObject is null we try listing the properties.
        /// If the BaseObject is not null we try enumerating. If that fails we try the BaseObject's ToString.
        /// </param>
        /// <returns>A string representation of the object</returns>
        /// <exception cref="ExtendedTypeSystemException">
        /// When there is a brokered ToString but it failed, or when the ToString on obj throws an exception.
        /// </exception>
        internal static string ToStringParser(ExecutionContext context, object obj)
        {
            try
            {
                return ToString(context, obj, null, null, CultureInfo.InvariantCulture, true, true);
            }
            catch (ExtendedTypeSystemException etse)
            {
                throw new PSInvalidCastException("InvalidCastFromAnyTypeToString", etse.InnerException,
                    ExtendedTypeSystem.InvalidCastCannotRetrieveString);
            }
        }

        /// <summary>
        /// Called from an PSObject instance ToString to provide a string representation for an object
        /// </summary>
        /// <param name="context">
        /// ExecutionContext used to fetch the separator. 
        /// Typically either this or separator will be null.
        /// If both are null, " " is used.
        /// </param>
        /// <param name="obj">
        /// object we are trying to call ToString on. If this is not an PSObject we try
        /// enumerating and if that fails we call obj.ToString.
        /// If this is an PSObject, we look for a brokered ToString. 
        /// If it is not present, and the BaseObject is null we try listing the properties.
        /// If the BaseObject is not null we try enumerating. If that fails we try the BaseObject's ToString.
        /// </param>
        /// <param name="separator">The separator between elements, if this is an enumeration</param>
        /// <param name="format">the format to be passed to ToString</param>
        /// <param name="formatProvider">the formatProvider to be passed to ToString</param>
        /// <param name="recurse">true if we should enumerate values or properties which would cause recursive
        /// calls to this method. Such recursive calls will have recurse set to false, limiting the depth.</param>
        /// <param name="unravelEnumeratorOnRecurse">If recurse is false, this parameter is not considered. If it is true
        /// this parameter will determine how enumerators are going to be treated.
        /// </param>
        /// <returns>A string representation of the object</returns>
        /// <exception cref="ExtendedTypeSystemException">
        /// When there is a brokered ToString but it failed, or when the ToString on obj throws an exception.
        /// </exception>
        internal static string ToString(ExecutionContext context, object obj, string separator, string format, IFormatProvider formatProvider, bool recurse, bool unravelEnumeratorOnRecurse)
        {
            PSObject mshObj = obj as PSObject;

            #region plain object
            if (mshObj == null)
            {
                if (obj == null)
                {
                    return String.Empty;
                }

                // Fast-track the primitive types...
                Type objType = obj.GetType();
                TypeCode code = objType.GetTypeCode();
                switch (code)
                {
                    case TypeCode.String:
                        return (string)obj;
                    case TypeCode.Byte:
                    case TypeCode.SByte:
                    case TypeCode.Int16:
                    case TypeCode.UInt16:
                    case TypeCode.Int32:
                    case TypeCode.UInt32:
                    case TypeCode.Int64:
                    case TypeCode.UInt64:
                        return obj.ToString();
                    case TypeCode.DateTime:
                        DateTime dt = (DateTime)obj;
                        return dt.ToString(formatProvider);
                    case TypeCode.Decimal:
                        Decimal dec = (Decimal)obj;
                        return dec.ToString(formatProvider);
                    case TypeCode.Double:
                        double dbl = (double)obj;
                        return dbl.ToString(formatProvider);

                    case TypeCode.Single:
                        float sgl = (float)obj;
                        return sgl.ToString(formatProvider);
                }

                #region recurse
                if (recurse)
                {
                    IEnumerable enumerable = LanguagePrimitives.GetEnumerable(obj);
                    if (enumerable != null)
                    {
                        try
                        {
                            return ToStringEnumerable(context, enumerable, separator, format, formatProvider);
                        }
                        catch (Exception e)
                        {
                            CommandProcessorBase.CheckForSevereException(e);
                            // We do want to ignore exceptions here to try the regular ToString below.
                        }
                    }
                    if (unravelEnumeratorOnRecurse)
                    {
                        IEnumerator enumerator = LanguagePrimitives.GetEnumerator(obj);
                        if (enumerator != null)
                        {
                            try
                            {
                                return ToStringEnumerator(context, enumerator, separator, format, formatProvider);
                            }
                            catch (Exception e)
                            {
                                CommandProcessorBase.CheckForSevereException(e);
                                // We do want to ignore exceptions here to try the regular ToString below.
                            }
                        }
                    }
                }
                #endregion recurse

                #region object ToString

                IFormattable objFormattable = obj as IFormattable;
                try
                {
                    if (objFormattable == null)
                    {
                        Type type = obj as Type;
                        if (type != null)
                        {
                            return Microsoft.PowerShell.ToStringCodeMethods.Type(type);
                        }
                        return obj.ToString();
                    }
                    return objFormattable.ToString(format, formatProvider);
                }
                catch (Exception e)
                {
                    CommandProcessorBase.CheckForSevereException(e);
                    throw new ExtendedTypeSystemException("ToStringObjectBasicException", e,
                        ExtendedTypeSystem.ToStringException, e.Message);
                }
                #endregion object ToString
            }
            #endregion plain object

            #region PSObject

            #region brokered ToString
            // A brokered ToString has precedence over any other attempts.
            // If it fails we let the exception go because the caller must be notified.
            PSMethodInfo method = null;
            PSMemberInfoInternalCollection<PSMemberInfo> instanceMembers;
            if (PSObject.HasInstanceMembers(mshObj, out instanceMembers))
            {
                method = instanceMembers["ToString"] as PSMethodInfo;
            }

            if (method == null)
            {
                if (mshObj.InternalTypeNames.Count != 0)
                {
                    TypeTable table = mshObj.GetTypeTable();
                    if (table != null)
                    {
                        method = table.GetMembers<PSMethodInfo>(mshObj.InternalTypeNames)["ToString"];
                        if (method != null)
                        {
                            method = (PSMethodInfo)method.Copy();
                            method.instance = mshObj;
                        }
                    }
                }
            }

            if (method != null)
            {
                object retObj;
                try
                {
                    // Even if a format specifier has been provided, if there is only one overload
                    // then it can't take a format specified...
                    if (formatProvider != null && method.OverloadDefinitions.Count > 1)
                    {
                        retObj = method.Invoke(format, formatProvider);
                        return retObj != null ? retObj.ToString() : string.Empty;
                    }

                    retObj = method.Invoke();
                    return retObj != null ? retObj.ToString() : string.Empty;
                }
                catch (MethodException e)
                {
                    throw new ExtendedTypeSystemException("MethodExceptionNullFormatProvider", e,
                        ExtendedTypeSystem.ToStringException, e.Message);
                }
            }
            #endregion brokered ToString

            #region recurse
            // Since we don't have a brokered ToString, we check for the need to enumerate the object or its properties
            if (recurse)
            {
                if (mshObj.immediateBaseObjectIsEmpty)
                {
                    try
                    {
                        return PSObject.ToStringEmptyBaseObject(context, mshObj, separator, format, formatProvider);
                    }
                    catch (Exception e)
                    {
                        CommandProcessorBase.CheckForSevereException(e);
                        // We do want to ignore exceptions here to try the regular ToString below.
                    }
                }

                IEnumerable enumerable = LanguagePrimitives.GetEnumerable(mshObj);
                if (enumerable != null)
                {
                    try
                    {
                        return ToStringEnumerable(context, enumerable, separator, format, formatProvider);
                    }
                    catch (Exception e)
                    {
                        CommandProcessorBase.CheckForSevereException(e);
                        // We do want to ignore exceptions here to try the regular ToString below.
                    }
                }
                if (unravelEnumeratorOnRecurse)
                {
                    IEnumerator enumerator = LanguagePrimitives.GetEnumerator(mshObj);
                    if (enumerator != null)
                    {
                        try
                        {
                            return ToStringEnumerator(context, enumerator, separator, format, formatProvider);
                        }
                        catch (Exception e)
                        {
                            CommandProcessorBase.CheckForSevereException(e);
                            // We do want to ignore exceptions here to try the regular ToString below.
                        }
                    }
                }
            }
            #endregion recurse

            #region mshObject's BaseObject ToString

            // If we've cached a string representation for this object, use that. This
            // is used to preserve the original string for numeric literals.
            if (mshObj.TokenText != null)
            {
                return mshObj.TokenText;
            }

            // Since we don't have a brokered ToString and the enumerations were not necessary or failed
            // we try the BaseObject's ToString
            object baseObject = mshObj._immediateBaseObject;
            IFormattable msjObjFormattable = baseObject as IFormattable;
            try
            {
                string result;

                if (msjObjFormattable == null)
                {
                    result = baseObject.ToString();
                }
                else
                {
                    result = msjObjFormattable.ToString(format, formatProvider);
                }

                return result ?? String.Empty;
            }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                throw new ExtendedTypeSystemException("ToStringPSObjectBasicException", e,
                    ExtendedTypeSystem.ToStringException, e.Message);
            }
            #endregion mshObject's BaseObject ToString

            #endregion PSObject
        }

        /// <summary>
        /// Returns the string representation for this object. A ToString
        /// CodeMethod or ScriptMethod will be used, if available. Enumerations items are
        /// concatenated using $ofs.
        /// </summary>
        /// <returns>the string representation for baseObject</returns>
        /// <exception cref="ExtendedTypeSystemException">if an exception was thrown by the BaseObject's ToString</exception>
        public override string ToString()
        {
            //If ToString value from deserialization is available,
            //simply return it.
            if (_toStringFromDeserialization != null)
            {
                return _toStringFromDeserialization;
            }
            return PSObject.ToString(null, this, null, null, null, true, false);
        }

        /// <summary>
        /// Returns the string representation for this object. A ToString
        /// CodeMethod or ScriptMethod will be used, if available. Enumerations items are
        /// concatenated using $ofs.
        /// </summary>
        /// <param name="format">repassed to baseObject's IFormattable if present</param>
        /// <param name="formatProvider">repassed to baseObject's IFormattable if present</param>
        /// <returns>the string representation for baseObject</returns>
        /// <exception cref="ExtendedTypeSystemException">if an exception was thrown by the BaseObject's ToString</exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            //If ToString value from deserialization is available,
            //simply return it.
            if (_toStringFromDeserialization != null)
            {
                return _toStringFromDeserialization;
            }
            return PSObject.ToString(null, this, null, format, formatProvider, true, false);
        }

        private string PrivateToString()
        {
            string result;
            try
            {
                result = this.ToString();
            }
            catch (ExtendedTypeSystemException)
            {
                result = this.BaseObject.GetType().FullName;
            }
            return result;
        }

        #endregion ToString
        #region Clone

        /// <summary>
        /// Returns a copy of this PSObject. This will copy the BaseObject if
        /// it is a value type, and use BaseObject.Clone() for the new PSObject, 
        /// if the BaseObject is ICloneable.
        /// </summary>
        /// <returns>a copy of this object</returns>
        public virtual PSObject Copy()
        {
            PSObject returnValue = (PSObject)this.MemberwiseClone();

            if (this.BaseObject is PSCustomObject)
            {
                returnValue._immediateBaseObject = PSCustomObject.SelfInstance;
                returnValue.immediateBaseObjectIsEmpty = true;
            }
            else
            {
                returnValue._immediateBaseObject = _immediateBaseObject;
                returnValue.immediateBaseObjectIsEmpty = false;
            }

            // Instance members will be recovered as necessary through the resurrection table.
            returnValue._instanceMembers = null;
            // The typename is not resurrected.  A common reason to copy a PSObject is to change the TypeName,
            // especially on a PSCustomObject - e.g. to a help object where we want to force a different view.
            returnValue._typeNames = null;

            returnValue._members = new PSMemberInfoIntegratingCollection<PSMemberInfo>(returnValue, s_memberCollection);
            returnValue._properties = new PSMemberInfoIntegratingCollection<PSPropertyInfo>(returnValue, s_propertyCollection);
            returnValue._methods = new PSMemberInfoIntegratingCollection<PSMethodInfo>(returnValue, s_methodCollection);

            returnValue._adapterSet = GetMappedAdapter(returnValue._immediateBaseObject, returnValue.GetTypeTable());

            ICloneable cloneableBase = returnValue._immediateBaseObject as ICloneable;
            if (cloneableBase != null)
            {
                returnValue._immediateBaseObject = cloneableBase.Clone();
            }

            if (returnValue._immediateBaseObject is ValueType)
            {
                returnValue._immediateBaseObject = CopyValueType(returnValue._immediateBaseObject);
            }


            // needToReAddInstanceMembersAndTypeNames = returnValue will have a different key (different from "this") returned from GetKeyForResurrectionTables
            bool needToReAddInstanceMembersAndTypeNames = !object.ReferenceEquals(GetKeyForResurrectionTables(this), GetKeyForResurrectionTables(returnValue));
            if (needToReAddInstanceMembersAndTypeNames)
            {
                Diagnostics.Assert(!returnValue.InstanceMembers.Any(), "needToReAddInstanceMembersAndTypeNames should mean that the new object has a fresh/empty list of instance members");
                foreach (PSMemberInfo member in this.InstanceMembers)
                {
                    if (member.IsHidden)
                    {
                        continue;
                    }
                    // Add will clone the member
                    returnValue.Members.Add(member);
                }

                returnValue.TypeNames.Clear();
                foreach (string typeName in this.InternalTypeNames)
                {
                    returnValue.TypeNames.Add(typeName);
                }
            }

            returnValue.hasGeneratedReservedMembers = false;

            return returnValue;
        }

        internal static object CopyValueType(object obj)
        {
            // this will force boxing and unboxing in a new object that will cause
            // the copy of the value type
            var newBaseArray = Array.CreateInstance(obj.GetType(), 1);
            newBaseArray.SetValue(obj, 0);
            return newBaseArray.GetValue(0);
        }

        #endregion Clone
        #region IComparable
        /// <summary>
        /// Compares the current instance with another object of the same type.
        /// </summary>
        /// <param name="obj">An object to compare with this instance.</param>
        /// <returns>A 32-bit signed integer that indicates the relative order of the comparands.
        /// The return value has these meanings:
        ///     Value             Meaning
        ///     Less than zero    This instance is less than obj.
        ///     Zero              This instance is equal to obj.
        ///     Greater than zero This instance is greater than obj.
        /// </returns>
        /// <exception cref="ExtendedTypeSystemException"> If <paramref name="obj"/> has a different type
        /// than this instance's BaseObject or if the BaseObject does not implement IComparable.
        /// </exception>
        public int CompareTo(object obj)
        {
            // This ReferenceEquals is not just an optimization. 
            // It is necessary so that mshObject.Equals(mshObject) returns 0. 
            // Please see the comments inside the Equals implementation.
            if (Object.ReferenceEquals(this, obj))
            {
                return 0;
            }
            try
            {
                // PSObject.Base instead of BaseObject could cause an infinite
                // loop with LP.Compare calling this Compare.
                return LanguagePrimitives.Compare(this.BaseObject, obj);
            }
            catch (ArgumentException e)
            {
                throw new ExtendedTypeSystemException("PSObjectCompareTo", e,
                    ExtendedTypeSystem.NotTheSameTypeOrNotIcomparable, this.PrivateToString(), PSObject.AsPSObject(obj).ToString(), "IComparable");
            }
        }
        #endregion IComparable
        #region Equals and GetHashCode
        /// <summary>
        /// Determines whether the specified Object is equal to the current Object.
        /// </summary>
        /// <param name="obj">The Object to compare with the current Object.</param>
        /// <returns>true if the specified Object is equal to the current Object; otherwise, false.</returns>
        public override bool Equals(object obj)
        {
            // There is a slight difference between BaseObject and PSObject.Base. 
            // PSObject.Base returns the containing PSObject that wraps an MshCustomBaseObject.
            // BaseObject returns the MshCustomBaseObject.
            // Because we have to call BaseObject here, and LP.Compare uses PSObject.Base
            // we need the reference equals below so that mshObject.Equals(mshObject) returns true.
            if (Object.ReferenceEquals(this, obj))
            {
                return true;
            }

            // The above check validates if we are comparing with the same object references
            // This check "shortcuts" the comparison if the first object is a CustomObject
            // since 2 custom objects are not equal.
            if (Object.ReferenceEquals(this.BaseObject, PSCustomObject.SelfInstance))
            {
                return false;
            }

            // PSObject.Base instead of BaseObject could cause an infinite
            // loop with LP.Equals calling this Equals.
            return LanguagePrimitives.Equals(this.BaseObject, obj);
        }

        /// <summary>
        /// Serves as a hash function for a particular type, suitable for use in hashing algorithms and data structures like a hash table.
        /// </summary>
        /// <returns>A hash code for the current Object</returns>
        public override int GetHashCode()
        {
            return this.BaseObject.GetHashCode();
        }

        #endregion Equals and GetHashCode

        internal void AddOrSetProperty(string memberName, object value)
        {
            PSMemberInfo memberInfo;
            if (PSGetMemberBinder.TryGetInstanceMember(this, memberName, out memberInfo) && memberInfo is PSPropertyInfo)
            {
                memberInfo.Value = value;
            }
            else
            {
                Properties.Add(new PSNoteProperty(memberName, value));
            }
        }

        internal void AddOrSetProperty(PSNoteProperty property)
        {
            PSMemberInfo memberInfo;
            if (PSGetMemberBinder.TryGetInstanceMember(this, property.Name, out memberInfo) && memberInfo is PSPropertyInfo)
            {
                memberInfo.Value = property.Value;
            }
            else
            {
                Properties.Add(property);
            }
        }

        #endregion instance methods

        #region public const strings

        /// <summary>
        /// The name of the member set for adapted members
        /// </summary>
        /// <remarks>
        /// This needs to be Lower cased as it saves some comparison time elsewhere.
        /// </remarks>
        public const string AdaptedMemberSetName = "psadapted";

        /// <summary>
        /// The name of the member set for extended members
        /// </summary>
        /// <remarks>
        /// This needs to be Lower cased as it saves some comparison time elsewhere.
        /// </remarks>
        public const string ExtendedMemberSetName = "psextended";

        /// <summary>
        /// The name of the member set for the BaseObject's members
        /// </summary>
        /// <remarks>
        /// This needs to be Lower cased as it saves some comparison time elsewhere.
        /// </remarks>
        public const string BaseObjectMemberSetName = "psbase";


        /// <summary>
        /// The PSObject's properties
        /// </summary>
        /// <remarks>
        /// This needs to be Lower cased as it saves some comparison time elsewhere.
        /// </remarks>
        internal const string PSObjectMemberSetName = "psobject";

        /// <summary>
        /// a shortcut to .PSObject.TypeNames
        /// </summary>
        /// <remarks>
        /// This needs to be Lower cased as it saves some comparison time elsewhere.
        /// </remarks>
        internal const string PSTypeNames = "pstypenames";

        #endregion public const strings

        #region serialization

        /// <summary>
        /// Implements the ISerializable contract for serializing a PSObject
        /// </summary>
        /// <param name="info">Serialization information for this instance</param>
        /// <param name="context">The streaming context for this instance</param>
        public virtual void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw PSTraceSource.NewArgumentNullException("info");
            }

            // We create a wrapper PSObject, so that we can successfully deserialize it
            string serializedContent = null;
            if (this.immediateBaseObjectIsEmpty)
            {
                PSObject serializeTarget = new PSObject(this);
                serializedContent = PSSerializer.Serialize(serializeTarget);
            }
            else
            {
                serializedContent = PSSerializer.Serialize(this);
            }

            info.AddValue("CliXml", serializedContent);
        }

        /// <summary>
        /// Used in the serialization duplicate entry hashtable to detect when an PSObject has been serialized
        /// </summary>
        /// <returns>The System.Object implementation of GetHashCode</returns>
        internal int GetReferenceHashCode()
        {
            return base.GetHashCode();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="noteName"></param>
        /// <param name="defaultValue"></param>
        /// <param name="expectedType"></param>
        /// <param name="shouldReplicateInstance">
        /// true to make this PSObject as the owner of the memberset.
        /// </param>
        /// <param name="ownerObject">
        /// PSObject to be used while replicating the owner for PSMemberSet
        /// </param>
        /// <returns></returns>
        internal static object GetNoteSettingValue(PSMemberSet settings, string noteName,
            object defaultValue, Type expectedType,
            bool shouldReplicateInstance, PSObject ownerObject)
        {
            if (settings == null)
            {
                return defaultValue;
            }

            if (shouldReplicateInstance)
            {
                settings.ReplicateInstance(ownerObject);
            }
            PSNoteProperty note = settings.Members[noteName] as PSNoteProperty;
            if (note == null)
            {
                return defaultValue;
            }
            object noteValue = note.Value;
            if (noteValue == null || noteValue.GetType() != expectedType)
            {
                return defaultValue;
            }
            return note.Value;
        }



        internal int GetSerializationDepth(TypeTable backupTypeTable)
        {
            int result = 0;

            TypeTable typeTable = backupTypeTable ?? this.GetTypeTable();
            if (null != typeTable)
            {
                PSMemberSet standardMemberSet = TypeTableGetMemberDelegate<PSMemberSet>(this,
                    typeTable, TypeTable.PSStandardMembers);
                result = (int)GetNoteSettingValue(standardMemberSet, TypeTable.SerializationDepth, 0, typeof(int), true, this);
            }

            return result;
        }

        /// <summary>
        /// </summary>
        /// <param name="backupTypeTable">
        /// TypeTable to use if this PSObject.GetTypeTable() returns null. This will happen
        /// in the remoting scenario on the client/server side (where a LocalRunspace may not be
        /// present).
        /// </param>
        /// <returns></returns>
        internal PSPropertyInfo GetStringSerializationSource(TypeTable backupTypeTable)
        {
            PSMemberInfo result = this.GetPSStandardMember(backupTypeTable, TypeTable.StringSerializationSource);
            return result as PSPropertyInfo;
        }

        /// <summary>
        /// </summary>
        /// <param name="backupTypeTable">
        /// TypeTable to use if this PSObject.GetTypeTable() returns null. This will happen
        /// in the remoting scenario on the client/server side (where a LocalRunspace may not be
        /// present).
        /// </param>
        /// <returns></returns>
        internal SerializationMethod GetSerializationMethod(TypeTable backupTypeTable)
        {
            SerializationMethod result = TypeTable.defaultSerializationMethod;

            TypeTable typeTable = backupTypeTable ?? this.GetTypeTable();
            if (null != typeTable)
            {
                PSMemberSet standardMemberSet = TypeTableGetMemberDelegate<PSMemberSet>(this,
                    typeTable, TypeTable.PSStandardMembers);
                result = (SerializationMethod)GetNoteSettingValue(standardMemberSet,
                        TypeTable.SerializationMethodNode, TypeTable.defaultSerializationMethod, typeof(SerializationMethod), true, this);
            }

            return result;
        }

        internal PSMemberSet PSStandardMembers
        {
            get
            {
                PSMemberSet retVal = null;

                retVal = TypeTableGetMemberDelegate<PSMemberSet>(this, TypeTable.PSStandardMembers);
                if (retVal != null)
                {
                    retVal = (PSMemberSet)retVal.Copy();
                    retVal.ReplicateInstance(this);
                    return retVal;
                }

                retVal = this.InstanceMembers[TypeTable.PSStandardMembers] as PSMemberSet;
                return retVal;
            }
        }

        internal PSMemberInfo GetPSStandardMember(TypeTable backupTypeTable, string memberName)
        {
            PSMemberInfo result = null;

            TypeTable typeTable = backupTypeTable ?? this.GetTypeTable();
            if (typeTable != null)
            {
                PSMemberSet standardMemberSet = TypeTableGetMemberDelegate<PSMemberSet>(
                    this, typeTable, TypeTable.PSStandardMembers);
                if (null != standardMemberSet)
                {
                    standardMemberSet.ReplicateInstance(this);
                    PSMemberInfoIntegratingCollection<PSMemberInfo> members =
                        new PSMemberInfoIntegratingCollection<PSMemberInfo>(
                            standardMemberSet,
                            GetMemberCollection(PSMemberViewTypes.All, backupTypeTable));
                    result = members[memberName];
                }
            }

            return result ?? InstanceMembers[TypeTable.PSStandardMembers] as PSMemberSet;
        }

        /// <summary>
        /// Used by Deserializer to deserialize a serialized object to a given type
        /// (as specified in the a types.ps1xml file)
        /// </summary>
        /// <param name="backupTypeTable">
        /// TypeTable to use if this PSObject.GetTypeTable() returns null. This will happen
        /// in the remoting scenario on the client/server side (where a LocalRunspace may not be
        /// present).
        /// </param>
        internal Type GetTargetTypeForDeserialization(TypeTable backupTypeTable)
        {
            PSMemberInfo targetType = this.GetPSStandardMember(backupTypeTable, TypeTable.TargetTypeForDeserialization);
            if (null != targetType)
            {
                return (targetType.Value as Type);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// This is only going to be called if SerializationMethod is SpecificProperties
        /// </summary>
        /// <param name="backupTypeTable">
        /// TypeTable to use if this PSObject.GetTypeTable() returns null. This will happen
        /// in the remoting scenario on the client side (where a LocalRunspace may not be
        /// present).
        /// </param>
        /// <returns>A collection with only the specific properties to serialize</returns>
        internal Collection<string> GetSpecificPropertiesToSerialize(TypeTable backupTypeTable)
        {
            TypeTable typeTable = backupTypeTable ?? this.GetTypeTable();
            if (null != typeTable)
            {
                Collection<string> tmp = typeTable.GetSpecificProperties(this.InternalTypeNames);
                return tmp;
            }

            return new Collection<string>(new List<string>());
        }

        internal bool ShouldSerializeAdapter()
        {
            if (this.isDeserialized)
            {
                return this.adaptedMembers != null;
            }
            return !this.immediateBaseObjectIsEmpty;
        }
        internal bool ShouldSerializeBase()
        {
            if (this.isDeserialized)
            {
                return this.adaptedMembers != this.clrMembers;
            }
            if (this.immediateBaseObjectIsEmpty)
            {
                return false;
            }
            return (this.InternalAdapter.GetType() != typeof(DotNetAdapter));
        }

        internal PSMemberInfoInternalCollection<PSPropertyInfo> GetAdaptedProperties()
        {
            return GetProperties(this.adaptedMembers, this.InternalAdapter);
        }

        internal PSMemberInfoInternalCollection<PSPropertyInfo> GetBaseProperties()
        {
            return GetProperties(this.clrMembers, dotNetInstanceAdapter);
        }

        private PSMemberInfoInternalCollection<PSPropertyInfo> GetProperties(PSMemberInfoInternalCollection<PSPropertyInfo> serializedMembers, Adapter particularAdapter)
        {
            if (this.isDeserialized)
            {
                return serializedMembers;
            }
            PSMemberInfoInternalCollection<PSPropertyInfo> returnValue = new PSMemberInfoInternalCollection<PSPropertyInfo>();

            foreach (PSPropertyInfo member in particularAdapter.BaseGetMembers<PSPropertyInfo>(_immediateBaseObject))
            {
                returnValue.Add(member);
            }

            return returnValue;
        }

        /// <summary>
        /// This flag is set to true in deserialized shellobject
        /// </summary>
        internal bool isDeserialized = false;

        internal static void CopyDeserializerFields(PSObject source, PSObject target)
        {
            if (!target.isDeserialized)
            {
                target.isDeserialized = source.isDeserialized;
                target.adaptedMembers = source.adaptedMembers;
                target.clrMembers = source.clrMembers;
            }

            if (target._toStringFromDeserialization == null)
            {
                target._toStringFromDeserialization = source._toStringFromDeserialization;
                target.TokenText = source.TokenText;
            }
        }

        /// <summary>
        /// Set base object
        /// </summary>
        /// <param name="value">object which is set as core</param>
        /// <param name="overrideTypeInfo">If true, overwrite the type information</param>
        ///<remarks>This method is to be used only by Serialization code</remarks>
        internal void SetCoreOnDeserialization(object value, bool overrideTypeInfo)
        {
            Diagnostics.Assert(this.immediateBaseObjectIsEmpty, "BaseObject should be PSCustomObject for deserialized objects");
            Diagnostics.Assert(value != null, "known objects are never null");
            this.immediateBaseObjectIsEmpty = false;
            _immediateBaseObject = value;
            _adapterSet = GetMappedAdapter(_immediateBaseObject, GetTypeTable());
            if (overrideTypeInfo)
            {
                this.InternalTypeNames = this.InternalAdapter.BaseGetTypeNameHierarchy(value);
            }
        }

        //This is toString value set on deserialization
        private string _toStringFromDeserialization = null;

        internal bool preserveToString = false;
        internal bool preserveToStringSet = false;

        internal bool PreserveToString
        {
            get
            {
                if (preserveToStringSet)
                {
                    return preserveToString;
                }
                preserveToStringSet = true;
                if (InternalTypeNames.Count == 0)
                {
                    return false;
                }

                preserveToString = false;

                return preserveToString;
            }
        }

        /// <summary>
        /// Sets the to string value on deserialization
        /// </summary>
        internal string ToStringFromDeserialization
        {
            get
            {
                return _toStringFromDeserialization;
            }
            set
            {
                _toStringFromDeserialization = value;
            }
        }

        #endregion serialization

        /// <summary>
        /// This class is solely used by PSObject to support .net member lookup for all the
        /// adapters except for dotNetInstanceAdapter, mshMemberSetAdapter and mshObjectAdapter.
        /// If the original adapter is not one of those, then .net members are also exposed
        /// on the PSObject. This will have the following effect:
        /// 
        /// 1. Every adapted object like xml, wmi, adsi will show adapted members as well as
        ///    .net members.
        /// 2. Users will not need to access PSBase to access original .net members.
        /// 3. This will fix v1.0 ADSI adapter where most of the complaints were about
        ///    discovering original .net members.
        /// 
        /// Use of this class will allow us to customize the ultimate .net member lookup.
        /// For example, XML adapter already exposes .net methods. 
        /// Using this class you can choose exact .net adapter to support .net
        /// member lookup and avoid lookup duplication.
        /// </summary>
        /// <remarks>
        /// This class is intended for internal use only.
        /// </remarks>
        internal class AdapterSet
        {
            #region Private Data

            // original adapter like Xml, ManagementClass, DirectoryEntry etc.
            // .net adapter

            #endregion

            #region Properties

            /// <summary>
            /// This property can be accessed only internally and hence
            /// no checks are performed on input.
            /// </summary>
            internal Adapter OriginalAdapter { get; set; }

            internal DotNetAdapter DotNetAdapter { get; }

            #endregion

            #region Constructors

            internal AdapterSet(Adapter adapter, DotNetAdapter dotnetAdapter)
            {
                OriginalAdapter = adapter;
                DotNetAdapter = dotnetAdapter;
            }

            #endregion
        }

        #region Dynamic metaobject implementation

        internal class PSDynamicMetaObject : DynamicMetaObject
        {
            internal PSDynamicMetaObject(Expression expression, PSObject value)
                : base(expression, BindingRestrictions.Empty, value)
            {
            }

            private new PSObject Value { get { return (PSObject)base.Value; } }

            private DynamicMetaObject GetUnwrappedObject()
            {
                return new DynamicMetaObject(Expression.Call(CachedReflectionInfo.PSObject_Base, this.Expression), this.Restrictions, PSObject.Base(Value));
            }

            public override IEnumerable<string> GetDynamicMemberNames()
            {
                return (from member in Value.Members select member.Name);
            }

            private bool MustDeferIDMOP()
            {
                var baseObject = PSObject.Base(Value);
                return baseObject is IDynamicMetaObjectProvider && !(baseObject is PSObject);
            }

            private DynamicMetaObject DeferForIDMOP(DynamicMetaObjectBinder binder, params DynamicMetaObject[] args)
            {
                Diagnostics.Assert(MustDeferIDMOP(), "Defer only works for idmop wrapped PSObjects");

                Expression[] exprs = new Expression[args.Length + 1];
                BindingRestrictions restrictions = this.Restrictions == BindingRestrictions.Empty ? this.PSGetTypeRestriction() : this.Restrictions;

                exprs[0] = Expression.Call(CachedReflectionInfo.PSObject_Base, this.Expression.Cast(typeof(object)));
                for (int i = 0; i < args.Length; i++)
                {
                    exprs[i + 1] = args[i].Expression;
                    restrictions = restrictions.Merge(args[i].Restrictions == BindingRestrictions.Empty ? args[i].PSGetTypeRestriction() : args[i].Restrictions);
                }

                return new DynamicMetaObject(DynamicExpression.Dynamic(binder, binder.ReturnType, exprs), restrictions);
            }

            public override DynamicMetaObject BindBinaryOperation(BinaryOperationBinder binder, DynamicMetaObject arg)
            {
                if (MustDeferIDMOP())
                {
                    return DeferForIDMOP(binder, arg);
                }
                return binder.FallbackBinaryOperation(GetUnwrappedObject(), arg);
            }

            public override DynamicMetaObject BindConvert(ConvertBinder binder)
            {
                if (MustDeferIDMOP())
                {
                    return DeferForIDMOP(binder);
                }

                // This will invoke the language binder, meaning we might not invoke PowerShell conversions.  This
                // is an interesting design choice, which, if revisited, needs some care because there are multiple
                // binders that PowerShell uses for conversion, the normal one, and the one used when we want to
                // attempt enumeration of some object that might or might not implement IEnumerable.
                return binder.FallbackConvert(this);
            }

            public override DynamicMetaObject BindDeleteIndex(DeleteIndexBinder binder, DynamicMetaObject[] indexes)
            {
                if (MustDeferIDMOP())
                {
                    return DeferForIDMOP(binder, indexes);
                }

                return binder.FallbackDeleteIndex(GetUnwrappedObject(), indexes);
            }

            public override DynamicMetaObject BindDeleteMember(DeleteMemberBinder binder)
            {
                if (MustDeferIDMOP())
                {
                    return DeferForIDMOP(binder);
                }

                return binder.FallbackDeleteMember(GetUnwrappedObject());
            }

            public override DynamicMetaObject BindGetIndex(GetIndexBinder binder, DynamicMetaObject[] indexes)
            {
                if (MustDeferIDMOP())
                {
                    return DeferForIDMOP(binder, indexes);
                }

                return binder.FallbackGetIndex(GetUnwrappedObject(), indexes);
            }

            public override DynamicMetaObject BindInvoke(InvokeBinder binder, DynamicMetaObject[] args)
            {
                if (MustDeferIDMOP())
                {
                    return DeferForIDMOP(binder, args);
                }

                return binder.FallbackInvoke(GetUnwrappedObject(), args);
            }

            public override DynamicMetaObject BindSetIndex(SetIndexBinder binder, DynamicMetaObject[] indexes, DynamicMetaObject value)
            {
                if (MustDeferIDMOP())
                {
                    return DeferForIDMOP(binder, indexes.Append(value).ToArray());
                }

                return binder.FallbackSetIndex(GetUnwrappedObject(), indexes, value);
            }

            public override DynamicMetaObject BindUnaryOperation(UnaryOperationBinder binder)
            {
                if (MustDeferIDMOP())
                {
                    return DeferForIDMOP(binder);
                }

                return binder.FallbackUnaryOperation(GetUnwrappedObject());
            }

            public override DynamicMetaObject BindInvokeMember(InvokeMemberBinder binder, DynamicMetaObject[] args)
            {
                if (MustDeferIDMOP())
                {
                    return DeferForIDMOP(binder, args);
                }

                return (binder as PSInvokeMemberBinder ??
                        (InvokeMemberBinder)(binder as PSInvokeBaseCtorBinder) ??
                        PSInvokeMemberBinder.Get(binder.Name, binder.CallInfo, false, false, null, null)).FallbackInvokeMember(this, args);
            }

            public override DynamicMetaObject BindGetMember(GetMemberBinder binder)
            {
                if (MustDeferIDMOP())
                {
                    return DeferForIDMOP(binder);
                }

                return (binder as PSGetMemberBinder ?? PSGetMemberBinder.Get(binder.Name, (Type)null, false)).FallbackGetMember(this);
            }

            public override DynamicMetaObject BindSetMember(SetMemberBinder binder, DynamicMetaObject value)
            {
                if (MustDeferIDMOP())
                {
                    return DeferForIDMOP(binder, value);
                }

                return (binder as PSSetMemberBinder ?? PSSetMemberBinder.Get(binder.Name, (Type)null, false)).FallbackSetMember(this, value);
            }
        }

        DynamicMetaObject IDynamicMetaObjectProvider.GetMetaObject(Expression parameter)
        {
            return new PSDynamicMetaObject(parameter, this);
        }

        #endregion

        #region Help formatting

        internal bool IsHelpObject
        {
            get { return _isHelpObject; }
            set { _isHelpObject = value; }
        }
        private bool _isHelpObject = false;

        #endregion
    }

    /// <summary>
    /// Serves as a placeholder BaseObject when PSObject's 
    /// constructor with no parameters is used.
    /// </summary>
    public class PSCustomObject
    {
        /// <summary>
        /// To prevent other instances than SelfInstance
        /// </summary>
        private PSCustomObject() { }

        internal static PSCustomObject SelfInstance = new PSCustomObject();
        /// <summary>
        /// Returns an empty string
        /// </summary>
        public override string ToString()
        {
            return "";
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <remarks>
    /// Please keep in sync with SerializationMethod from
    /// C:\e\win7_powershell\admin\monad\nttargets\assemblies\logging\ETW\Manifests\Microsoft-Windows-PowerShell-Instrumentation.man
    /// </remarks>
    internal enum SerializationMethod
    {
        AllPublicProperties = 0,
        String = 1,
        SpecificProperties = 2
    };
}

#pragma warning restore 56500

namespace Microsoft.PowerShell
{
    /// <summary>
    /// Contains auxiliary ToString CodeMethod implementations for some types
    /// </summary>
    public static partial class ToStringCodeMethods
    {
        private static void AddGenericArguments(StringBuilder sb, Type[] genericArguments, bool dropNamespaces)
        {
            sb.Append('[');
            for (int i = 0; i < genericArguments.Length; i++)
            {
                if (i > 0) { sb.Append(','); }
                sb.Append(Type(genericArguments[i], dropNamespaces));
            }
            sb.Append(']');
        }

        internal static string Type(Type type, bool dropNamespaces = false)
        {
            if (type == null)
                return String.Empty;

            string result;
            TypeInfo typeinfo = type.GetTypeInfo();
            if (typeinfo.IsGenericType && !typeinfo.IsGenericTypeDefinition)
            {
                string genericDefinition = Type(type.GetGenericTypeDefinition(), dropNamespaces);
                // For regular generic types, we find the backtick character, for example:
                //      System.Collections.Generic.List`1[T] -> 
                //      System.Collections.Generic.List[string]
                // For nested generic types, we find the left bracket character, for example:
                //      System.Collections.Generic.Dictionary`2+Enumerator[TKey, TValue] ->
                //      System.Collections.Generic.Dictionary`2+Enumerator[string,string]
                int backtickOrLeftBracketIndex = genericDefinition.LastIndexOf(typeinfo.IsNested ? '[' : '`');
                var sb = new StringBuilder(genericDefinition, 0, backtickOrLeftBracketIndex, 512);
                AddGenericArguments(sb, type.GetGenericArguments(), dropNamespaces);
                result = sb.ToString();
            }
            else if (typeinfo.IsArray)
            {
                string elementDefinition = Type(type.GetElementType(), dropNamespaces);
                var sb = new StringBuilder(elementDefinition, elementDefinition.Length + 10);
                sb.Append("[");
                for (int i = 0; i < type.GetArrayRank() - 1; ++i)
                {
                    sb.Append(",");
                }
                sb.Append("]");
                result = sb.ToString();
            }
            else
            {
                result = TypeAccelerators.FindBuiltinAccelerator(type);
                if (result == null)
                {
                    if (dropNamespaces)
                    {
                        if (typeinfo.IsNested)
                        {
                            // For nested types, we should return OuterType+InnerType. For example,
                            //  System.Environment+SpecialFolder ->  Environment+SpecialFolder
                            string fullName = type.ToString();
                            result = type.Namespace == null
                                        ? fullName
                                        : fullName.Substring(type.Namespace.Length + 1);
                        }
                        else
                        {
                            result = type.Name;
                        }
                    }
                    else
                    {
                        result = type.ToString();
                    }
                }
            }

            // We can't round trip anything with a generic parameter.
            // We also can't round trip if we're dropping the namespace.
            if (!typeinfo.IsGenericParameter
                && !typeinfo.ContainsGenericParameters
                && !dropNamespaces
                && !typeinfo.Assembly.GetCustomAttributes(typeof(DynamicClassImplementationAssemblyAttribute)).Any())
            {
                Type roundTripType;
                TypeResolver.TryResolveType(result, out roundTripType);
                if (roundTripType != type)
                {
                    result = type.AssemblyQualifiedName;
                }
            }
            return result;
        }

        /// <summary>
        /// ToString implementation for Type
        /// </summary>
        /// <param name="instance">instance of PSObject wrapping a Type</param>
        public static string Type(PSObject instance)
        {
            if (instance == null)
                return String.Empty;
            return Type((Type)instance.BaseObject);
        }

        /// <summary>
        /// ToString implementation for XmlNode
        /// </summary>
        /// <param name="instance">instance of PSObject wrapping an XmlNode</param>
        public static string XmlNode(PSObject instance)
        {
            if (instance == null)
                return String.Empty;
            XmlNode node = (XmlNode)instance.BaseObject;
            if (node == null)
                return String.Empty;
            return node.LocalName;
        }

        /// <summary>
        /// ToString implementation for XmlNodeList
        /// </summary>
        /// <param name="instance">instance of PSObject wrapping an XmlNodeList</param>
        public static string XmlNodeList(PSObject instance)
        {
            if (instance == null)
                return String.Empty;
            XmlNodeList nodes = (XmlNodeList)instance.BaseObject;
            if (nodes == null)
                return String.Empty;
            if (nodes.Count == 1)
            {
                if (nodes[0] == null)
                {
                    return String.Empty;
                }
                return PSObject.AsPSObject(nodes[0]).ToString();
            }

            return PSObject.ToStringEnumerable(null, (IEnumerable)nodes, null, null, null);
        }
    }
}
