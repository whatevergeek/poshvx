/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System.Linq;
using System.Management.Automation.Language;
using System.Reflection;
using System.Globalization;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections;
using System.ComponentModel;
using System.Text;
using System.Management.Automation.Internal;
using Microsoft.PowerShell;
using TypeTable = System.Management.Automation.Runspaces.TypeTable;

#pragma warning disable 1634, 1691 // Stops compiler from warning about unknown warnings
#pragma warning disable 56503

namespace System.Management.Automation
{
    #region PSMemberInfo

    /// <summary>
    /// Enumerates all possible types of members
    /// </summary>
    [TypeConverterAttribute(typeof(LanguagePrimitives.EnumMultipleTypeConverter))]
    [FlagsAttribute()]
    public enum PSMemberTypes
    {
        /// <summary>
        /// An alias to another member
        /// </summary>
        AliasProperty = 1,
        /// <summary>
        /// A property defined as a reference to a method
        /// </summary>
        CodeProperty = 2,
        /// <summary>
        /// A property from the BaseObject
        /// </summary>
        Property = 4,
        /// <summary>
        /// A property defined by a Name-Value pair
        /// </summary>
        NoteProperty = 8,
        /// <summary>
        /// A property defined by script language
        /// </summary>
        ScriptProperty = 16,
        /// <summary>
        /// A set of properties
        /// </summary>
        PropertySet = 32,
        /// <summary>
        /// A method from the BaseObject
        /// </summary>
        Method = 64,
        /// <summary>
        /// A method defined as a reference to another method
        /// </summary>
        CodeMethod = 128,
        /// <summary>
        /// A method defined as a script
        /// </summary>
        ScriptMethod = 256,
        /// <summary>
        /// A member that acts like a Property that takes parameters. This is not consider to be a property or a method.
        /// </summary>
        ParameterizedProperty = 512,
        /// <summary>
        /// A set of members
        /// </summary>
        MemberSet = 1024,
        /// <summary>
        /// All events
        /// </summary>
        Event = 2048,
        /// <summary>
        /// All dynamic members (where PowerShell cannot know the type of the member)
        /// </summary>
        Dynamic = 4096,
        /// <summary>
        /// All property member types
        /// </summary>
        Properties = AliasProperty | CodeProperty | Property | NoteProperty | ScriptProperty,
        /// <summary>
        /// All method member types
        /// </summary>
        Methods = CodeMethod | Method | ScriptMethod,
        /// <summary>
        /// All member types
        /// </summary>
        All = Properties | Methods | Event | PropertySet | MemberSet | ParameterizedProperty | Dynamic
    }

    /// <summary>
    /// Enumerator for all possible views available on a PSObject.
    /// </summary>
    [TypeConverterAttribute(typeof(LanguagePrimitives.EnumMultipleTypeConverter))]
    [FlagsAttribute()]
    public enum PSMemberViewTypes
    {
        /// <summary>
        /// Extended methods / properties
        /// </summary>
        Extended = 1,
        /// <summary>
        /// Adapted methods / properties
        /// </summary>
        Adapted = 2,
        /// <summary>
        /// Base methods / properties
        /// </summary>
        Base = 4,
        /// <summary>
        /// All methods / properties
        /// </summary>
        All = Extended | Adapted | Base
    }

    /// <summary>
    /// Match options 
    /// </summary>
    [FlagsAttribute]
    internal enum MshMemberMatchOptions
    {
        /// <summary>
        /// No options
        /// </summary>
        None = 0,
        /// <summary>
        /// Hidden members should be displayed
        /// </summary>
        IncludeHidden = 1,
        /// <summary>
        /// Only include members with <see cref="PSMemberInfo.ShouldSerialize"/> property set to <c>true</c>
        /// </summary>
        OnlySerializable = 2
    }

    /// <summary>
    /// Serves as the base class for all members of an PSObject
    /// </summary>
    public abstract class PSMemberInfo
    {
        internal object instance;
        internal string name;
        internal bool ShouldSerialize { get; set; }

        internal virtual void ReplicateInstance(object particularInstance)
        {
            this.instance = particularInstance;
        }

        internal void SetValueNoConversion(object setValue)
        {
            PSProperty thisAsProperty = this as PSProperty;
            if (thisAsProperty == null)
            {
                this.Value = setValue;
                return;
            }
            thisAsProperty.SetAdaptedValue(setValue, false);
        }


        /// <summary>
        /// Initializes a new instance of an PSMemberInfo derived class
        /// </summary>
        protected PSMemberInfo()
        {
            ShouldSerialize = true;
            IsInstance = true;
        }

        internal void CloneBaseProperties(PSMemberInfo destiny)
        {
            destiny.name = name;
            destiny.IsHidden = IsHidden;
            destiny.IsReservedMember = IsReservedMember;
            destiny.IsInstance = IsInstance;
            destiny.instance = instance;
            destiny.ShouldSerialize = ShouldSerialize;
        }

        /// <summary>
        /// Gets the member type
        /// </summary>
        public abstract PSMemberTypes MemberType { get; }

        /// <summary>
        /// Gets the member name
        /// </summary>
        public string Name
        {
            get
            {
                return this.name;
            }
        }

        /// <summary>
        /// Allows a derived class to set the member name...
        /// </summary>
        /// <param name="name"></param>
        protected void SetMemberName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            this.name = name;
        }

        /// <summary>
        /// True if this is one of the reserved members
        /// </summary>
        internal bool IsReservedMember { get; set; }

        /// <summary>
        /// True if the member should be hidden when searching with PSMemberInfoInternalCollection's Match
        /// or enumerating a collection.
        /// This should not be settable as it would make the count of hidden properties in
        /// PSMemberInfoInternalCollection invalid.
        /// For now, we are carefully setting this.isHidden before adding 
        /// the members toPSObjectMembersetCollection. In the future, we might need overload for all
        /// PSMemberInfo constructors to take isHidden.
        /// </summary>
        internal bool IsHidden { get; set; }

        /// <summary>
        /// True if this member has been added to the instance as opposed to
        /// coming from the adapter or from type data
        /// </summary>
        public bool IsInstance { get; internal set; }

        /// <summary>
        /// Gets and Sets the value of this member
        /// </summary>
        /// <exception cref="GetValueException">When getting the value of a property throws an exception. 
        /// This exception is also thrown if the property is an <see cref="PSScriptProperty"/> and there 
        /// is no Runspace to run the script.</exception>
        /// <exception cref="SetValueException">When setting the value of a property throws an exception.
        /// This exception is also thrown if the property is an <see cref="PSScriptProperty"/> and there 
        /// is no Runspace to run the script.</exception>
        /// <exception cref="ExtendedTypeSystemException">When some problem other then getting/setting the value happened</exception>
        public abstract object Value { get; set; }

        /// <summary>
        /// Gets the type of the value for this member
        /// </summary>
        /// <exception cref="ExtendedTypeSystemException">When there was a problem getting the property</exception>
        public abstract string TypeNameOfValue { get; }

        /// <summary>
        /// returns a new PSMemberInfo that is a copy of this PSMemberInfo
        /// </summary>
        /// <returns>a new PSMemberInfo that is a copy of this PSMemberInfo</returns>
        public abstract PSMemberInfo Copy();

        internal bool MatchesOptions(MshMemberMatchOptions options)
        {
            if (this.IsHidden && (0 == (options & MshMemberMatchOptions.IncludeHidden)))
            {
                return false;
            }

            if (!this.ShouldSerialize && (0 != (options & MshMemberMatchOptions.OnlySerializable)))
            {
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Serves as a base class for all members that behave like properties.
    /// </summary>
    public abstract class PSPropertyInfo : PSMemberInfo
    {
        /// <summary>
        /// Initializes a new instance of an PSPropertyInfo derived class
        /// </summary>
        protected PSPropertyInfo() { }
        /// <summary>
        /// Gets true if this property can be set
        /// </summary>
        public abstract bool IsSettable { get; }

        /// <summary>
        /// Gets true if this property can be read
        /// </summary>
        public abstract bool IsGettable { get; }

        internal Exception NewSetValueException(Exception e, string errorId)
        {
            return new SetValueInvocationException(errorId,
                e,
                ExtendedTypeSystem.ExceptionWhenSetting,
                this.Name, e.Message);
        }

        internal Exception NewGetValueException(Exception e, string errorId)
        {
            return new GetValueInvocationException(errorId,
                e,
                ExtendedTypeSystem.ExceptionWhenGetting,
                this.Name, e.Message);
        }
    }

    /// <summary>
    /// Serves as an alias to another member
    /// </summary>
    /// <remarks>
    /// It is permitted to subclass <see cref="PSAliasProperty"/>
    /// but there is no established scenario for doing this, nor has it been tested.
    /// </remarks>
    public class PSAliasProperty : PSPropertyInfo
    {
        /// <summary>
        /// Returns the string representation of this property
        /// </summary>
        /// <returns>This property as a string</returns>
        public override string ToString()
        {
            StringBuilder returnValue = new StringBuilder();
            returnValue.Append(this.Name);
            returnValue.Append(" = ");
            if (ConversionType != null)
            {
                returnValue.Append("(");
                returnValue.Append(ConversionType);
                returnValue.Append(")");
            }
            returnValue.Append(ReferencedMemberName);
            return returnValue.ToString();
        }

        /// <summary>
        /// Initializes a new instance of PSAliasProperty setting the name of the alias
        /// and the name of the member this alias refers to.
        /// </summary>
        /// <param name="name">name of the alias</param>
        /// <param name="referencedMemberName">name of the member this alias refers to</param>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public PSAliasProperty(string name, string referencedMemberName)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            this.name = name;
            if (String.IsNullOrEmpty(referencedMemberName))
            {
                throw PSTraceSource.NewArgumentException("referencedMemberName");
            }
            ReferencedMemberName = referencedMemberName;
        }

        /// <summary>
        /// Initializes a new instance of PSAliasProperty setting the name of the alias, 
        /// the name of the member this alias refers to and the type to convert the referenced
        /// member's value.
        /// </summary>
        /// <param name="name">name of the alias</param>
        /// <param name="referencedMemberName">name of the member this alias refers to</param>
        /// <param name="conversionType">the type to convert the referenced member's value</param>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public PSAliasProperty(string name, string referencedMemberName, Type conversionType)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            this.name = name;
            if (String.IsNullOrEmpty(referencedMemberName))
            {
                throw PSTraceSource.NewArgumentException("referencedMemberName");
            }
            ReferencedMemberName = referencedMemberName;
            // conversionType is optional and can be null
            ConversionType = conversionType;
        }

        /// <summary>
        /// Gets the name of the member this alias refers to
        /// </summary>
        public string ReferencedMemberName { get; }

        /// <summary>
        /// Gets the member this alias refers to
        /// </summary>
        internal PSMemberInfo ReferencedMember
        {
            get
            {
                return this.LookupMember(ReferencedMemberName);
            }
        }

        /// <summary>
        /// Gets the the type to convert the referenced member's value. It might be 
        /// null when no conversion is done.
        /// </summary>
        public Type ConversionType { get; private set; }

        #region virtual implementation

        /// <summary>
        /// returns a new PSMemberInfo that is a copy of this PSMemberInfo
        /// </summary>
        /// <returns>a new PSMemberInfo that is a copy of this PSMemberInfo</returns>
        public override PSMemberInfo Copy()
        {
            PSAliasProperty alias = new PSAliasProperty(name, ReferencedMemberName);
            alias.ConversionType = ConversionType;
            CloneBaseProperties(alias);
            return alias;
        }

        /// <summary>
        /// Gets the member type
        /// </summary>
        public override PSMemberTypes MemberType
        {
            get
            {
                return PSMemberTypes.AliasProperty;
            }
        }

        /// <summary>
        /// Gets the type of the value for this member
        /// </summary>
        /// <exception cref="ExtendedTypeSystemException">
        /// When 
        ///     the alias has not been added to an PSObject or
        ///     the alias has a cycle or
        ///     an aliased member is not present
        /// </exception>
        public override string TypeNameOfValue
        {
            get
            {
                if (ConversionType != null)
                {
                    return ConversionType.FullName;
                }
                return this.ReferencedMember.TypeNameOfValue;
            }
        }

        /// <summary>
        /// Gets true if this property can be set
        /// </summary>
        /// <exception cref="ExtendedTypeSystemException">
        /// When 
        ///     the alias has not been added to an PSObject or
        ///     the alias has a cycle or
        ///     an aliased member is not present
        /// </exception>
        public override bool IsSettable
        {
            get
            {
                PSPropertyInfo memberProperty = this.ReferencedMember as PSPropertyInfo;
                if (memberProperty != null)
                {
                    return memberProperty.IsSettable;
                }
                return false;
            }
        }

        /// <summary>
        /// Gets true if this property can be read
        /// </summary>
        /// <exception cref="ExtendedTypeSystemException">
        ///     When 
        ///         the alias has not been added to an PSObject or
        ///         the alias has a cycle or
        ///         an aliased member is not present
        /// </exception>
        public override bool IsGettable
        {
            get
            {
                PSPropertyInfo memberProperty = this.ReferencedMember as PSPropertyInfo;
                if (memberProperty != null)
                {
                    return memberProperty.IsGettable;
                }
                return false;
            }
        }

        private PSMemberInfo LookupMember(string name)
        {
            bool hasCycle;
            PSMemberInfo returnValue;
            LookupMember(name, new HashSet<string>(StringComparer.OrdinalIgnoreCase), out returnValue, out hasCycle);
            if (hasCycle)
            {
                throw new
                    ExtendedTypeSystemException(
                    "CycleInAliasLookup",
                    null,
                    ExtendedTypeSystem.CycleInAlias,
                    this.Name);
            }
            return returnValue;
        }

        private void LookupMember(string name, HashSet<string> visitedAliases, out PSMemberInfo returnedMember, out bool hasCycle)
        {
            returnedMember = null;
            if (this.instance == null)
            {
                throw new ExtendedTypeSystemException("AliasLookupMemberOutsidePSObject",
                    null,
                    ExtendedTypeSystem.AccessMemberOutsidePSObject,
                    name);
            }

            PSMemberInfo member = PSObject.AsPSObject(this.instance).Properties[name];
            if (member == null)
            {
                throw new ExtendedTypeSystemException(
                    "AliasLookupMemberNotPresent",
                    null,
                    ExtendedTypeSystem.MemberNotPresent,
                    name);
            }

            PSAliasProperty aliasMember = member as PSAliasProperty;
            if (aliasMember == null)
            {
                hasCycle = false;
                returnedMember = member;
                return;
            }
            if (visitedAliases.Contains(name))
            {
                hasCycle = true;
                return;
            }
            visitedAliases.Add(name);
            LookupMember(aliasMember.ReferencedMemberName, visitedAliases, out returnedMember, out hasCycle);
        }

        /// <summary>
        /// Gets and Sets the value of this member
        /// </summary>
        /// <exception cref="ExtendedTypeSystemException">
        /// When 
        ///     the alias has not been added to an PSObject or
        ///     the alias has a cycle or
        ///     an aliased member is not present
        /// </exception>
        /// <exception cref="GetValueException">When getting the value of a property throws an exception</exception>
        /// <exception cref="SetValueException">When setting the value of a property throws an exception</exception>
        public override object Value
        {
            get
            {
                object returnValue = this.ReferencedMember.Value;
                if (ConversionType != null)
                {
                    returnValue = LanguagePrimitives.ConvertTo(returnValue, ConversionType, CultureInfo.InvariantCulture);
                }
                return returnValue;
            }
            set
            {
                this.ReferencedMember.Value = value;
            }
        }
        #endregion virtual implementation
    }

    /// <summary>
    /// Serves as a property implemented with references to methods for getter and setter.
    /// </summary>
    /// <remarks>
    /// It is permitted to subclass <see cref="PSCodeProperty"/>
    /// but there is no established scenario for doing this, nor has it been tested.
    /// </remarks>
    public class PSCodeProperty : PSPropertyInfo
    {
        /// <summary>
        /// Returns the string representation of this property
        /// </summary>
        /// <returns>This property as a string</returns>
        public override string ToString()
        {
            StringBuilder returnValue = new StringBuilder();
            returnValue.Append(this.TypeNameOfValue);
            returnValue.Append(" ");
            returnValue.Append(this.Name);
            returnValue.Append("{");
            if (this.IsGettable)
            {
                returnValue.Append("get=");
                returnValue.Append(GetterCodeReference.Name);
                returnValue.Append(";");
            }
            if (this.IsSettable)
            {
                returnValue.Append("set=");
                returnValue.Append(SetterCodeReference.Name);
                returnValue.Append(";");
            }
            returnValue.Append("}");
            return returnValue.ToString();
        }


        /// <summary>
        /// Called from TypeTableUpdate before SetSetterFromTypeTable is called
        /// </summary>
        internal void SetGetterFromTypeTable(Type type, string methodName)
        {
            MethodInfo methodAsMember = null;

            try
            {
                methodAsMember = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.IgnoreCase);
            }
            catch (AmbiguousMatchException)
            {
                // Ignore the AmbiguousMatchException. 
                // We will generate error below if we cannot find exactly one match method.
            }

            if (methodAsMember == null)
            {
                throw new ExtendedTypeSystemException(
                    "GetterFormatFromTypeTable",
                    null,
                    ExtendedTypeSystem.CodePropertyGetterFormat);
            }
            SetGetter(methodAsMember);
        }

        /// <summary>
        /// Called from TypeTableUpdate after SetGetterFromTypeTable is called
        /// </summary>
        internal void SetSetterFromTypeTable(Type type, string methodName)
        {
            MethodInfo methodAsMember = null;

            try
            {
                methodAsMember = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.IgnoreCase);
            }
            catch (AmbiguousMatchException)
            {
                // Ignore the AmbiguousMatchException. 
                // We will generate error below if we cannot find exactly one match method.
            }

            if (methodAsMember == null)
            {
                throw new ExtendedTypeSystemException(
                    "SetterFormatFromTypeTable",
                    null,
                    ExtendedTypeSystem.CodePropertySetterFormat);
            }
            SetSetter(methodAsMember, GetterCodeReference);
        }

        /// <summary>
        /// Used from TypeTable with the internal constructor
        /// </summary>
        internal void SetGetter(MethodInfo methodForGet)
        {
            if (methodForGet == null)
            {
                GetterCodeReference = null;
                return;
            }

            if (!CheckGetterMethodInfo(methodForGet))
            {
                throw new ExtendedTypeSystemException(
                    "GetterFormat",
                    null,
                    ExtendedTypeSystem.CodePropertyGetterFormat);
            }
            GetterCodeReference = methodForGet;
        }

        internal static bool CheckGetterMethodInfo(MethodInfo methodForGet)
        {
            ParameterInfo[] parameters = methodForGet.GetParameters();
            return methodForGet.IsPublic
                && methodForGet.IsStatic
                && methodForGet.ReturnType != typeof(void)
                && parameters.Length == 1
                && parameters[0].ParameterType == typeof(PSObject);
        }

        /// <summary>
        /// Used from TypeTable with the internal constructor
        /// </summary>
        private void SetSetter(MethodInfo methodForSet, MethodInfo methodForGet)
        {
            if (methodForSet == null)
            {
                if (methodForGet == null)
                {
                    throw new ExtendedTypeSystemException(
                        "SetterAndGetterNullFormat",
                        null,
                        ExtendedTypeSystem.CodePropertyGetterAndSetterNull);
                }
                SetterCodeReference = null;
                return;
            }

            if (!CheckSetterMethodInfo(methodForSet, methodForGet))
            {
                throw new ExtendedTypeSystemException(
                    "SetterFormat",
                    null,
                    ExtendedTypeSystem.CodePropertySetterFormat);
            }
            SetterCodeReference = methodForSet;
        }

        internal static bool CheckSetterMethodInfo(MethodInfo methodForSet, MethodInfo methodForGet)
        {
            ParameterInfo[] parameters = methodForSet.GetParameters();
            return methodForSet.IsPublic
                && methodForSet.IsStatic
                && methodForSet.ReturnType == typeof(void)
                && parameters.Length == 2
                && parameters[0].ParameterType == typeof(PSObject)
                && (methodForGet == null || methodForGet.ReturnType == parameters[1].ParameterType);
        }

        /// <summary>
        /// Used from TypeTable to delay setting getter and setter
        /// </summary>
        internal PSCodeProperty(string name)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            this.name = name;
        }

        /// <summary>
        /// Initializes a new instance of the PSCodeProperty class as a read only property.
        /// </summary>
        /// <param name="name">name of the property</param>
        /// <param name="getterCodeReference">This should be a public static non void method taking one PSObject parameter.</param>
        /// <exception cref="ArgumentException">if name is null or empty or getterCodeReference is null</exception>
        /// <exception cref="ExtendedTypeSystemException">if getterCodeReference doesn't have the right format.</exception>
        public PSCodeProperty(string name, MethodInfo getterCodeReference)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            this.name = name;
            if (getterCodeReference == null)
            {
                throw PSTraceSource.NewArgumentNullException("getterCodeReference");
            }
            SetGetter(getterCodeReference);
        }

        /// <summary>
        /// Initializes a new instance of the PSCodeProperty class. Setter or getter can be null, but both cannot be null.
        /// </summary>
        /// <param name="name">name of the property</param>
        /// <param name="getterCodeReference">This should be a public static non void method taking one PSObject parameter.</param>
        /// <param name="setterCodeReference">This should be a public static void method taking 2 parameters, where the first is an PSObject.</param>
        /// <exception cref="ArgumentException">when methodForGet and methodForSet are null</exception>
        /// <exception cref="ExtendedTypeSystemException">
        /// if:
        ///     - getterCodeReference doesn't have the right format, 
        ///     - setterCodeReference doesn't have the right format,
        ///     - both getterCodeReference and setterCodeReference are null.
        /// </exception>
        public PSCodeProperty(string name, MethodInfo getterCodeReference, MethodInfo setterCodeReference)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            this.name = name;
            if (getterCodeReference == null && setterCodeReference == null)
            {
                throw PSTraceSource.NewArgumentNullException("getterCodeReference setterCodeReference");
            }
            SetGetter(getterCodeReference);
            SetSetter(setterCodeReference, getterCodeReference);
        }

        /// <summary>
        /// Gets the method used for the properties' getter. It might be null.
        /// </summary>
        public MethodInfo GetterCodeReference { get; private set; }

        /// <summary>
        /// Gets the method used for the properties' setter. It might be null.
        /// </summary>
        public MethodInfo SetterCodeReference { get; private set; }

        #region virtual implementation
        /// <summary>
        /// returns a new PSMemberInfo that is a copy of this PSMemberInfo
        /// </summary>
        /// <returns>a new PSMemberInfo that is a copy of this PSMemberInfo</returns>
        public override PSMemberInfo Copy()
        {
            PSCodeProperty property = new PSCodeProperty(name, GetterCodeReference, SetterCodeReference);
            CloneBaseProperties(property);
            return property;
        }

        /// <summary>
        /// Gets the member type
        /// </summary>
        public override PSMemberTypes MemberType
        {
            get
            {
                return PSMemberTypes.CodeProperty;
            }
        }

        /// <summary>
        /// Gets true if this property can be set
        /// </summary>
        public override bool IsSettable
        {
            get
            {
                return this.SetterCodeReference != null;
            }
        }

        /// <summary>
        /// Gets true if this property can be read
        /// </summary>
        public override bool IsGettable
        {
            get
            {
                return GetterCodeReference != null;
            }
        }


        /// <summary>
        /// Gets and Sets the value of this member
        /// </summary>
        /// <exception cref="GetValueException">When getting and there is no getter or when the getter throws an exception</exception>
        /// <exception cref="SetValueException">When setting and there is no setter or when the setter throws an exception</exception>
        public override object Value
        {
            get
            {
                if (GetterCodeReference == null)
                {
                    throw new GetValueException("GetWithoutGetterFromCodePropertyValue",
                        null,
                        ExtendedTypeSystem.GetWithoutGetterException,
                        this.Name);
                }
                try
                {
                    return GetterCodeReference.Invoke(null, new object[1] { this.instance });
                }
                catch (TargetInvocationException ex)
                {
                    Exception inner = ex.InnerException ?? ex;
                    throw new GetValueInvocationException("CatchFromCodePropertyGetTI",
                        inner,
                        ExtendedTypeSystem.ExceptionWhenGetting,
                        this.name, inner.Message);
                }
                catch (Exception e)
                {
                    if (e is GetValueException)
                    {
                        throw;
                    }
                    CommandProcessorBase.CheckForSevereException(e);
                    throw new GetValueInvocationException("CatchFromCodePropertyGet",
                        e,
                        ExtendedTypeSystem.ExceptionWhenGetting,
                        this.name, e.Message);
                }
            }
            set
            {
                if (SetterCodeReference == null)
                {
                    throw new SetValueException("SetWithoutSetterFromCodeProperty",
                        null,
                        ExtendedTypeSystem.SetWithoutSetterException,
                        this.Name);
                }
                try
                {
                    SetterCodeReference.Invoke(null, new object[2] { this.instance, value });
                }
                catch (TargetInvocationException ex)
                {
                    Exception inner = ex.InnerException ?? ex;
                    throw new SetValueInvocationException("CatchFromCodePropertySetTI",
                        inner,
                        ExtendedTypeSystem.ExceptionWhenSetting,
                        this.name, inner.Message);
                }
                catch (Exception e)
                {
                    if (e is SetValueException)
                    {
                        throw;
                    }
                    CommandProcessorBase.CheckForSevereException(e);
                    throw new SetValueInvocationException("CatchFromCodePropertySet",
                        e,
                        ExtendedTypeSystem.ExceptionWhenSetting,
                        this.name, e.Message);
                }
            }
        }
        /// <summary>
        /// Gets the type of the value for this member
        /// </summary>
        /// <exception cref="GetValueException">If there is no property getter</exception>
        public override string TypeNameOfValue
        {
            get
            {
                if (GetterCodeReference == null)
                {
                    throw new GetValueException("GetWithoutGetterFromCodePropertyTypeOfValue",
                        null,
                        ExtendedTypeSystem.GetWithoutGetterException,
                        this.Name);
                }
                return GetterCodeReference.ReturnType.FullName;
            }
        }
        #endregion virtual implementation

    }

    /// <summary>
    /// Used to access the adapted or base properties from the BaseObject
    /// </summary>
    public class PSProperty : PSPropertyInfo
    {
        /// <summary>
        /// Returns the string representation of this property
        /// </summary>
        /// <returns>This property as a string</returns>
        public override string ToString()
        {
            if (this.isDeserialized)
            {
                StringBuilder returnValue = new StringBuilder();
                returnValue.Append(this.TypeNameOfValue);
                returnValue.Append(" {get;set;}");
                return returnValue.ToString();
            }
            Diagnostics.Assert((this.baseObject != null) && (this.adapter != null), "if it is deserialized, it should have all these properties set");
            return adapter.BasePropertyToString(this);
        }

        /// <summary>
        /// used by the adapters to keep intermediate data used between DoGetProperty and
        /// DoGetValue or DoSetValue
        /// </summary>

        internal string typeOfValue;
        internal object serializedValue;
        internal bool isDeserialized;

        /// <summary>
        /// This will be either instance.adapter or instance.clrAdapter
        /// </summary>
        internal Adapter adapter;
        internal object adapterData;
        internal object baseObject;

        /// <summary>
        /// Constructs a property from a serialized value
        /// </summary>
        /// <param name="name">name of the property</param>
        /// <param name="serializedValue">value of the property</param>
        internal PSProperty(string name, object serializedValue)
        {
            this.isDeserialized = true;
            this.serializedValue = serializedValue;
            this.name = name;
        }

        /// <summary>
        /// Constructs this property
        /// </summary>
        /// <param name="name">name of the property</param>
        /// <param name="adapter">adapter used in DoGetProperty</param>
        /// <param name="baseObject">object passed to DoGetProperty</param>
        /// <param name="adapterData">adapter specific data</param>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        internal PSProperty(string name, Adapter adapter, object baseObject, object adapterData)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            this.name = name;
            this.adapter = adapter;
            this.adapterData = adapterData;
            this.baseObject = baseObject;
        }

        #region virtual implementation
        /// <summary>
        /// returns a new PSMemberInfo that is a copy of this PSMemberInfo
        /// </summary>
        /// <returns>a new PSMemberInfo that is a copy of this PSMemberInfo</returns>
        public override PSMemberInfo Copy()
        {
            PSProperty property = new PSProperty(this.name, this.adapter, this.baseObject, this.adapterData);
            CloneBaseProperties(property);
            property.typeOfValue = this.typeOfValue;
            property.serializedValue = this.serializedValue;
            property.isDeserialized = this.isDeserialized;
            return property;
        }

        /// <summary>
        /// Gets the member type
        /// </summary>
        public override PSMemberTypes MemberType
        {
            get
            {
                return PSMemberTypes.Property;
            }
        }

        private object GetAdaptedValue()
        {
            if (this.isDeserialized)
            {
                return serializedValue;
            }
            Diagnostics.Assert((this.baseObject != null) && (this.adapter != null), "if it is deserialized, it should have all these properties set");

            object o = adapter.BasePropertyGet(this);
            return o;
        }

        internal void SetAdaptedValue(object setValue, bool shouldConvert)
        {
            if (this.isDeserialized)
            {
                serializedValue = setValue;
                return;
            }
            Diagnostics.Assert((this.baseObject != null) && (this.adapter != null), "if it is deserialized, it should have all these properties set");
            adapter.BasePropertySet(this, setValue, shouldConvert);
        }

        /// <summary>
        /// Gets or sets the value of this property
        /// </summary>
        /// <exception cref="GetValueException">When getting the value of a property throws an exception</exception>
        /// <exception cref="SetValueException">When setting the value of a property throws an exception</exception>
        public override object Value
        {
            get
            {
                return GetAdaptedValue();
            }
            set
            {
                SetAdaptedValue(value, true);
            }
        }

        /// <summary>
        /// Gets true if this property can be set
        /// </summary>
        public override bool IsSettable
        {
            get
            {
                if (this.isDeserialized)
                {
                    return true;
                }
                Diagnostics.Assert((this.baseObject != null) && (this.adapter != null), "if it is deserialized, it should have all these properties set");
                return adapter.BasePropertyIsSettable(this);
            }
        }

        /// <summary>
        /// Gets true if this property can be read
        /// </summary>
        public override bool IsGettable
        {
            get
            {
                if (this.isDeserialized)
                {
                    return true;
                }
                Diagnostics.Assert((this.baseObject != null) && (this.adapter != null), "if it is deserialized, it should have all these properties set");
                return adapter.BasePropertyIsGettable(this);
            }
        }
        /// <summary>
        /// Gets the type of the value for this member
        /// </summary>
        public override string TypeNameOfValue
        {
            get
            {
                if (this.isDeserialized)
                {
                    if (serializedValue == null)
                    {
                        return String.Empty;
                    }

                    PSObject serializedValueAsPSObject = serializedValue as PSObject;
                    if (serializedValueAsPSObject != null)
                    {
                        var typeNames = serializedValueAsPSObject.InternalTypeNames;
                        if ((typeNames != null) && (typeNames.Count >= 1))
                        {
                            // type name at 0-th index is the most specific type (i.e. deserialized.system.io.directoryinfo)
                            // type names at other indices are less specific (i.e. deserialized.system.object)
                            return typeNames[0];
                        }
                    }

                    return serializedValue.GetType().FullName;
                }
                Diagnostics.Assert((this.baseObject != null) && (this.adapter != null), "if it is deserialized, it should have all these properties set");
                return adapter.BasePropertyType(this);
            }
        }
        #endregion virtual implementation
    }

    /// <summary>
    /// A property created by a user-defined PSPropertyAdapter
    /// </summary>
    public class PSAdaptedProperty : PSProperty
    {
        /// <summary>
        /// Creates a property for the given base object
        /// </summary>
        /// <param name="name">name of the property</param>
        /// <param name="tag">an adapter can use this object to keep any arbitrary data it needs</param>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public PSAdaptedProperty(string name, object tag)
            : base(name, null, null, tag)
        {
            //
            // Note that the constructor sets the adapter and base object to null; the ThirdPartyAdapter managing this property must set these values 
            //
        }

        internal PSAdaptedProperty(string name, Adapter adapter, object baseObject, object adapterData)
            : base(name, adapter, baseObject, adapterData)
        {
        }

        /// <summary>
        /// Copy an adapted property.
        /// </summary>
        public override PSMemberInfo Copy()
        {
            PSAdaptedProperty property = new PSAdaptedProperty(this.name, this.adapter, this.baseObject, this.adapterData);
            CloneBaseProperties(property);
            property.typeOfValue = this.typeOfValue;
            property.serializedValue = this.serializedValue;
            property.isDeserialized = this.isDeserialized;
            return property;
        }

        /// <summary>
        /// Gets the object the property belongs to
        /// </summary>
        public object BaseObject
        {
            get
            {
                return this.baseObject;
            }
        }

        /// <summary>
        /// Gets the data attached to this property
        /// </summary>
        public object Tag
        {
            get
            {
                return this.adapterData;
            }
        }
    }

    /// <summary>
    /// Serves as a property that is a simple name-value pair.
    /// </summary>
    public class PSNoteProperty : PSPropertyInfo
    {
        /// <summary>
        /// Returns the string representation of this property
        /// </summary>
        /// <returns>This property as a string</returns>
        public override string ToString()
        {
            StringBuilder returnValue = new StringBuilder();

            returnValue.Append(GetDisplayTypeNameOfValue(this.Value));
            returnValue.Append(" ");
            returnValue.Append(this.Name);
            returnValue.Append("=");
            returnValue.Append(this.noteValue == null ? "null" : this.noteValue.ToString());
            return returnValue.ToString();
        }


        internal object noteValue;

        /// <summary>
        /// Initializes a new instance of the PSNoteProperty class.
        /// </summary>
        /// <param name="name">name of the property</param>
        /// <param name="value">value of the property</param>
        /// <exception cref="ArgumentException">for an empty or null name</exception>
        public PSNoteProperty(string name, object value)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            this.name = name;
            // value can be null
            this.noteValue = value;
        }

        #region virtual implementation
        /// <summary>
        /// returns a new PSMemberInfo that is a copy of this PSMemberInfo
        /// </summary>
        /// <returns>a new PSMemberInfo that is a copy of this PSMemberInfo</returns>
        public override PSMemberInfo Copy()
        {
            PSNoteProperty property = new PSNoteProperty(this.name, this.noteValue);
            CloneBaseProperties(property);
            return property;
        }

        /// <summary>
        /// Gets PSMemberTypes.NoteProperty
        /// </summary>
        public override PSMemberTypes MemberType
        {
            get
            {
                return PSMemberTypes.NoteProperty;
            }
        }

        /// <summary>
        /// Gets true since the value of an PSNoteProperty can always be set
        /// </summary>
        public override bool IsSettable
        {
            get
            {
                return this.IsInstance;
            }
        }

        /// <summary>
        /// Gets true since the value of an PSNoteProperty can always be obtained
        /// </summary>
        public override bool IsGettable
        {
            get
            {
                return true;
            }
        }

        /// <summary>
        /// Gets or sets the value of this property
        /// </summary>
        public override object Value
        {
            get
            {
                return this.noteValue;
            }
            set
            {
                if (!this.IsInstance)
                {
                    throw new SetValueException("ChangeValueOfStaticNote",
                        null,
                        ExtendedTypeSystem.ChangeStaticMember,
                        this.Name);
                }
                this.noteValue = value;
            }
        }

        /// <summary>
        /// Gets the type of the value for this member
        /// </summary>
        public override string TypeNameOfValue
        {
            get
            {
                object val = this.Value;

                if (val == null)
                {
                    return typeof(object).FullName;
                }

                PSObject valAsPSObject = val as PSObject;
                if (valAsPSObject != null)
                {
                    var typeNames = valAsPSObject.InternalTypeNames;
                    if ((typeNames != null) && (typeNames.Count >= 1))
                    {
                        // type name at 0-th index is the most specific type (i.e. system.string)
                        // type names at other indices are less specific (i.e. system.object)
                        return typeNames[0];
                    }
                }

                return val.GetType().FullName;
            }
        }

        #endregion virtual implementation

        internal static string GetDisplayTypeNameOfValue(object val)
        {
            string displayTypeName = null;

            PSObject valAsPSObject = val as PSObject;
            if (valAsPSObject != null)
            {
                var typeNames = valAsPSObject.InternalTypeNames;
                if ((typeNames != null) && (typeNames.Count >= 1))
                {
                    displayTypeName = typeNames[0];
                }
            }
            if (string.IsNullOrEmpty(displayTypeName))
            {
                displayTypeName = val == null
                    ? "object"
                    : ToStringCodeMethods.Type(val.GetType(), dropNamespaces: true);
            }

            return displayTypeName;
        }
    }

    /// <summary>
    /// Serves as a property that is a simple name-value pair.
    /// </summary>
    /// <remarks>
    /// It is permitted to subclass <see cref="PSNoteProperty"/>
    /// but there is no established scenario for doing this, nor has it been tested.
    /// </remarks>
    public class PSVariableProperty : PSNoteProperty
    {
        /// <summary>
        /// Returns the string representation of this property
        /// </summary>
        /// <returns>This property as a string</returns>
        public override string ToString()
        {
            StringBuilder returnValue = new StringBuilder();
            returnValue.Append(GetDisplayTypeNameOfValue(_variable.Value));
            returnValue.Append(" ");
            returnValue.Append(_variable.Name);
            returnValue.Append("=");
            returnValue.Append(_variable.Value ?? "null");
            return returnValue.ToString();
        }


        internal PSVariable _variable;

        /// <summary>
        /// Initializes a new instance of the PSVariableProperty class. This is
        /// a subclass of the NoteProperty that wraps a variable instead of a simple value.
        /// </summary>
        /// <param name="variable">The variable to wrap</param>
        /// <exception cref="ArgumentException">for an empty or null name</exception>
        public PSVariableProperty(PSVariable variable)
            : base(variable != null ? variable.Name : null, null)
        {
            if (variable == null)
            {
                throw PSTraceSource.NewArgumentException("variable");
            }
            _variable = variable;
        }

        #region virtual implementation
        /// <summary>
        /// returns a new PSMemberInfo that is a copy of this PSMemberInfo,
        /// Note that it returns another reference to the variable, not a reference
        /// to a new variable...
        /// </summary>
        /// <returns>a new PSMemberInfo that is a copy of this PSMemberInfo</returns>
        public override PSMemberInfo Copy()
        {
            PSNoteProperty property = new PSVariableProperty(_variable);
            CloneBaseProperties(property);
            return property;
        }

        /// <summary>
        /// Gets PSMemberTypes.NoteProperty
        /// </summary>
        public override PSMemberTypes MemberType
        {
            get
            {
                return PSMemberTypes.NoteProperty;
            }
        }

        /// <summary>
        /// True if the underlying variable is settable...
        /// </summary>
        public override bool IsSettable
        {
            get
            {
                return (_variable.Options & (ScopedItemOptions.Constant | ScopedItemOptions.ReadOnly)) == ScopedItemOptions.None;
            }
        }

        /// <summary>
        /// Gets true since the value of an PSNoteProperty can always be obtained
        /// </summary>
        public override bool IsGettable
        {
            get
            {
                return true;
            }
        }

        /// <summary>
        /// Gets or sets the value of this property
        /// </summary>
        public override object Value
        {
            get
            {
                return _variable.Value;
            }
            set
            {
                if (!this.IsInstance)
                {
                    throw new SetValueException("ChangeValueOfStaticNote",
                        null,
                        ExtendedTypeSystem.ChangeStaticMember,
                        this.Name);
                }
                _variable.Value = value;
            }
        }

        /// <summary>
        /// Gets the type of the value for this member
        /// </summary>
        public override string TypeNameOfValue
        {
            get
            {
                object val = _variable.Value;

                if (val == null)
                {
                    return typeof(object).FullName;
                }

                PSObject valAsPSObject = val as PSObject;
                if (valAsPSObject != null)
                {
                    var typeNames = valAsPSObject.InternalTypeNames;
                    if ((typeNames != null) && (typeNames.Count >= 1))
                    {
                        // type name at 0-th index is the most specific type (i.e. system.string)
                        // type names at other indices are less specific (i.e. system.object)
                        return typeNames[0];
                    }
                }

                return val.GetType().FullName;
            }
        }

        #endregion virtual implementation
    }

    /// <summary>
    /// Serves as a property implemented with getter and setter scripts.
    /// </summary>
    /// <remarks>
    /// It is permitted to subclass <see cref="PSScriptProperty"/>
    /// but there is no established scenario for doing this, nor has it been tested.
    /// </remarks>
    public class PSScriptProperty : PSPropertyInfo
    {
        /// <summary>
        /// Returns the string representation of this property
        /// </summary>
        /// <returns>This property as a string</returns>
        public override string ToString()
        {
            StringBuilder returnValue = new StringBuilder();
            returnValue.Append(this.TypeNameOfValue);
            returnValue.Append(" ");
            returnValue.Append(this.Name);
            returnValue.Append(" {");
            if (this.IsGettable)
            {
                returnValue.Append("get=");
                returnValue.Append(this.GetterScript.ToString());
                returnValue.Append(";");
            }
            if (this.IsSettable)
            {
                returnValue.Append("set=");
                returnValue.Append(this.SetterScript.ToString());
                returnValue.Append(";");
            }
            returnValue.Append("}");
            return returnValue.ToString();
        }

        private Nullable<PSLanguageMode> _languageMode;
        private string _getterScriptText;
        private ScriptBlock _getterScript;

        private string _setterScriptText;
        private ScriptBlock _setterScript;
        private bool _shouldCloneOnAccess;

        /// <summary>
        /// Gets the script used for the property getter. It might be null.
        /// </summary>
        public ScriptBlock GetterScript
        {
            get
            {
                // If we don't have a script block for the getter, see if we
                // have the text for it (to support delayed script compilation).
                if ((_getterScript == null) && (_getterScriptText != null))
                {
                    _getterScript = ScriptBlock.Create(_getterScriptText);

                    if (_languageMode.HasValue)
                    {
                        _getterScript.LanguageMode = _languageMode;
                    }

                    _getterScript.DebuggerStepThrough = true;
                }

                if (_getterScript == null)
                {
                    return null;
                }

                if (_shouldCloneOnAccess)
                {
                    // returning a clone as TypeTable might be shared between multiple
                    // runspaces and ScriptBlock is not shareable. We decided to
                    // Clone as needed instead of Cloning whenever a shared TypeTable is
                    // attached to a Runspace to save on Memory.
                    ScriptBlock newGetterScript = _getterScript.Clone();
                    newGetterScript.LanguageMode = _getterScript.LanguageMode;
                    return newGetterScript;
                }
                else
                {
                    return _getterScript;
                }
            }
        }

        /// <summary>
        /// Gets the script used for the property setter. It might be null.
        /// </summary>
        public ScriptBlock SetterScript
        {
            get
            {
                // If we don't have a script block for the setter, see if we
                // have the text for it (to support delayed script compilation).
                if ((_setterScript == null) && (_setterScriptText != null))
                {
                    _setterScript = ScriptBlock.Create(_setterScriptText);

                    if (_languageMode.HasValue)
                    {
                        _setterScript.LanguageMode = _languageMode;
                    }

                    _setterScript.DebuggerStepThrough = true;
                }

                if (_setterScript == null)
                {
                    return null;
                }

                if (_shouldCloneOnAccess)
                {
                    // returning a clone as TypeTable might be shared between multiple
                    // runspaces and ScriptBlock is not shareable. We decided to
                    // Clone as needed instead of Cloning whenever a shared TypeTable is
                    // attached to a Runspace to save on Memory.
                    ScriptBlock newSetterScript = _setterScript.Clone();
                    newSetterScript.LanguageMode = _setterScript.LanguageMode;
                    return newSetterScript;
                }
                else
                {
                    return _setterScript;
                }
            }
        }

        /// <summary>
        /// Initializes an instance of the PSScriptProperty class as a read only property.
        /// </summary>
        /// <param name="name">name of the property</param>
        /// <param name="getterScript">script to be used for the property getter. $this will be this PSObject.</param>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public PSScriptProperty(string name, ScriptBlock getterScript)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            this.name = name;
            if (getterScript == null)
            {
                throw PSTraceSource.NewArgumentNullException("getterScript");
            }
            _getterScript = getterScript;
        }

        /// <summary>
        /// Initializes an instance of the PSScriptProperty class as a read only 
        /// property. getterScript or setterScript can be null, but not both.
        /// </summary>
        /// <param name="name">Name of this property</param>
        /// <param name="getterScript">script to be used for the property getter. $this will be this PSObject.</param>
        /// <param name="setterScript">script to be used for the property setter. $this will be this PSObject and $args(1) will be the value to set.</param>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public PSScriptProperty(string name, ScriptBlock getterScript, ScriptBlock setterScript)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            this.name = name;
            if (getterScript == null && setterScript == null)
            {
                // we only do not allow both getterScript and setterScript to be null
                throw PSTraceSource.NewArgumentException("getterScript setterScript");
            }

            if (getterScript != null)
            {
                getterScript.DebuggerStepThrough = true;
            }
            if (setterScript != null)
            {
                setterScript.DebuggerStepThrough = true;
            }

            _getterScript = getterScript;
            _setterScript = setterScript;
        }

        /// <summary>
        /// Initializes an instance of the PSScriptProperty class as a read only 
        /// property, using the text of the properties to support lazy initialization.
        /// </summary>
        /// <param name="name">Name of this property</param>
        /// <param name="getterScript">script to be used for the property getter. $this will be this PSObject.</param>
        /// <param name="setterScript">script to be used for the property setter. $this will be this PSObject and $args(1) will be the value to set.</param>
        /// <param name="languageMode">Language mode to be used during script block evaluation.</param>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        internal PSScriptProperty(string name, string getterScript, string setterScript, Nullable<PSLanguageMode> languageMode)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            this.name = name;
            if (getterScript == null && setterScript == null)
            {
                // we only do not allow both getterScript and setterScript to be null
                throw PSTraceSource.NewArgumentException("getterScript setterScript");
            }

            _getterScriptText = getterScript;
            _setterScriptText = setterScript;
            _languageMode = languageMode;
        }

        internal PSScriptProperty(string name, ScriptBlock getterScript, ScriptBlock setterScript, bool shouldCloneOnAccess)
            : this(name, getterScript, setterScript)
        {
            _shouldCloneOnAccess = shouldCloneOnAccess;
        }

        internal PSScriptProperty(string name, string getterScript, string setterScript, Nullable<PSLanguageMode> languageMode, bool shouldCloneOnAccess)
            : this(name, getterScript, setterScript, languageMode)
        {
            _shouldCloneOnAccess = shouldCloneOnAccess;
        }

        #region virtual implementation
        /// <summary>
        /// returns a new PSMemberInfo that is a copy of this PSMemberInfo
        /// </summary>
        /// <returns>a new PSMemberInfo that is a copy of this PSMemberInfo</returns>
        public override PSMemberInfo Copy()
        {
            PSScriptProperty property;
            property = new PSScriptProperty(name, this.GetterScript, this.SetterScript);
            property._shouldCloneOnAccess = _shouldCloneOnAccess;
            CloneBaseProperties(property);
            return property;
        }
        /// <summary>
        /// Gets the member type
        /// </summary>
        public override PSMemberTypes MemberType
        {
            get
            {
                return PSMemberTypes.ScriptProperty;
            }
        }

        /// <summary>
        /// Gets true if this property can be set
        /// </summary>
        public override bool IsSettable
        {
            get
            {
                return this.SetterScript != null;
            }
        }

        /// <summary>
        /// Gets true if this property can be read
        /// </summary>
        public override bool IsGettable
        {
            get
            {
                return this.GetterScript != null;
            }
        }

        /// <summary>
        /// Gets and Sets the value of this property
        /// </summary>
        /// <exception cref="GetValueException">When getting and there is no getter, 
        /// when the getter throws an exception or when there is no Runspace to run the script.
        /// </exception>
        /// <exception cref="SetValueException">When setting and there is no setter,
        /// when the setter throws an exception or when there is no Runspace to run the script.</exception>
        public override object Value
        {
            get
            {
                if (this.GetterScript == null)
                {
                    throw new GetValueException("GetWithoutGetterFromScriptPropertyValue",
                        null,
                        ExtendedTypeSystem.GetWithoutGetterException,
                        this.Name);
                }
                return InvokeGetter(this.instance);
            }
            set
            {
                if (this.SetterScript == null)
                {
                    throw new SetValueException("SetWithoutSetterFromScriptProperty",
                        null,
                        ExtendedTypeSystem.SetWithoutSetterException,
                        this.Name);
                }
                InvokeSetter(this.instance, value);
            }
        }

        internal object InvokeSetter(object scriptThis, object value)
        {
            try
            {
                SetterScript.DoInvokeReturnAsIs(
                    useLocalScope: true,
                    errorHandlingBehavior: ScriptBlock.ErrorHandlingBehavior.WriteToExternalErrorPipe,
                    dollarUnder: AutomationNull.Value,
                    input: AutomationNull.Value,
                    scriptThis: scriptThis,
                    args: new object[] { value });
                return value;
            }
            catch (SessionStateOverflowException e)
            {
                throw NewSetValueException(e, "ScriptSetValueSessionStateOverflowException");
            }
            catch (RuntimeException e)
            {
                throw NewSetValueException(e, "ScriptSetValueRuntimeException");
            }
            catch (TerminateException)
            {
                // The debugger is terminating the execution; let the exception bubble up
                throw;
            }
            catch (FlowControlException e)
            {
                throw NewSetValueException(e, "ScriptSetValueFlowControlException");
            }
            catch (PSInvalidOperationException e)
            {
                throw NewSetValueException(e, "ScriptSetValueInvalidOperationException");
            }
        }

        internal object InvokeGetter(object scriptThis)
        {
            try
            {
                return GetterScript.DoInvokeReturnAsIs(
                    useLocalScope: true,
                    errorHandlingBehavior: ScriptBlock.ErrorHandlingBehavior.SwallowErrors,
                    dollarUnder: AutomationNull.Value,
                    input: AutomationNull.Value,
                    scriptThis: scriptThis,
                    args: Utils.EmptyArray<object>());
            }
            catch (SessionStateOverflowException e)
            {
                throw NewGetValueException(e, "ScriptGetValueSessionStateOverflowException");
            }
            catch (RuntimeException e)
            {
                throw NewGetValueException(e, "ScriptGetValueRuntimeException");
            }
            catch (TerminateException)
            {
                // The debugger is terminating the execution; let the exception bubble up
                throw;
            }
            catch (FlowControlException e)
            {
                throw NewGetValueException(e, "ScriptGetValueFlowControlException");
            }
            catch (PSInvalidOperationException e)
            {
                throw NewGetValueException(e, "ScriptgetValueInvalidOperationException");
            }
        }

        /// <summary>
        /// Gets the type of the value for this member. Currently this always returns typeof(object).FullName.
        /// </summary>
        public override string TypeNameOfValue
        {
            get
            {
                if ((this.GetterScript != null) &&
                    (this.GetterScript.OutputType.Count > 0))
                {
                    return this.GetterScript.OutputType[0].Name;
                }
                else
                {
                    return typeof(object).FullName;
                }
            }
        }

        #endregion virtual implementation
    }

    internal class PSMethodInvocationConstraints
    {
        internal PSMethodInvocationConstraints(
            Type methodTargetType,
            Type[] parameterTypes)
        {
            this.MethodTargetType = methodTargetType;
            _parameterTypes = parameterTypes;
        }

        /// <remarks>
        /// If <c>null</c> then there are no constraints
        /// </remarks>
        public Type MethodTargetType { get; private set; }

        /// <remarks>
        /// If <c>null</c> then there are no constraints
        /// </remarks>
        public IEnumerable<Type> ParameterTypes
        {
            get
            {
                return _parameterTypes;
            }
        }

        private readonly Type[] _parameterTypes;

        internal static bool EqualsForCollection<T>(ICollection<T> xs, ICollection<T> ys)
        {
            if (xs == null)
            {
                return ys == null;
            }
            if (ys == null)
            {
                return false;
            }
            if (xs.Count != ys.Count)
            {
                return false;
            }
            return xs.SequenceEqual(ys);
        }

        // TODO: IEnumerable<Type> genericTypeParameters { get; private set; }

        public bool Equals(PSMethodInvocationConstraints other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            if (other.MethodTargetType != this.MethodTargetType)
            {
                return false;
            }
            if (!EqualsForCollection(_parameterTypes, other._parameterTypes))
            {
                return false;
            }
            return true;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }
            if (ReferenceEquals(this, obj))
            {
                return true;
            }
            if (obj.GetType() != typeof(PSMethodInvocationConstraints))
            {
                return false;
            }
            return Equals((PSMethodInvocationConstraints)obj);
        }

        public override int GetHashCode()
        {
            // algorithm based on http://stackoverflow.com/questions/263400/what-is-the-best-algorithm-for-an-overridden-system-object-gethashcode
            unchecked
            {
                int result = 61;

                result = result * 397 + (MethodTargetType != null ? MethodTargetType.GetHashCode() : 0);
                result = result * 397 + ParameterTypes.SequenceGetHashCode();

                return result;
            }
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            string separator = "";
            if (MethodTargetType != null)
            {
                sb.Append("this: ");
                sb.Append(ToStringCodeMethods.Type(MethodTargetType, dropNamespaces: true));
                separator = " ";
            }

            if (_parameterTypes != null)
            {
                sb.Append(separator);
                sb.Append("args: ");
                separator = "";
                foreach (var p in _parameterTypes)
                {
                    sb.Append(separator);
                    sb.Append(ToStringCodeMethods.Type(p, dropNamespaces: true));
                    separator = ", ";
                }
            }

            if (sb.Length == 0)
            {
                sb.Append("<empty>");
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Serves as a base class for all members that behave like methods.
    /// </summary>
    public abstract class PSMethodInfo : PSMemberInfo
    {
        /// <summary>
        /// Initializes a new instance of a class derived from PSMethodInfo.
        /// </summary>
        protected PSMethodInfo() { }

        /// <summary>
        /// Invokes the appropriate method overload for the given arguments and returns its result.
        /// </summary>
        /// <param name="arguments">arguments to the method</param>
        /// <returns>return value from the method</returns>
        /// <exception cref="ArgumentException">if arguments is null</exception>
        /// <exception cref="MethodException">For problems finding an appropriate method for the arguments</exception>
        /// <exception cref="MethodInvocationException">For exceptions invoking the method.
        /// This exception is also thrown for an <see cref="PSScriptMethod"/> when there is no Runspace to run the script.</exception>
        public abstract object Invoke(params object[] arguments);

        /// <summary>
        /// Gets a list of all the overloads for this method
        /// </summary>
        public abstract Collection<string> OverloadDefinitions { get; }

        #region virtual implementation

        /// <summary>
        /// Gets the value of this member. The getter returns the PSMethodInfo itself.
        /// </summary>
        /// <exception cref="ExtendedTypeSystemException">When setting the member</exception>
        /// <remarks>
        /// This is not the returned value of the method even for Methods with no arguments.
        /// The getter returns this (the PSMethodInfo itself). The setter is not supported.
        /// </remarks>
        public sealed override object Value
        {
            get
            {
                return this;
            }
            set
            {
                throw new ExtendedTypeSystemException("CannotChangePSMethodInfoValue",
                    null,
                    ExtendedTypeSystem.CannotSetValueForMemberType,
                    this.GetType().FullName);
            }
        }

        #endregion virtual implementation
    }

    /// <summary>
    /// Serves as a method implemented with a reference to another method.
    /// </summary>
    /// <remarks>
    /// It is permitted to subclass <see cref="PSCodeMethod"/>
    /// but there is no established scenario for doing this, nor has it been tested.
    /// </remarks>
    public class PSCodeMethod : PSMethodInfo
    {
        /// <summary>
        /// Returns the string representation of this member
        /// </summary>
        /// <returns>This property as a string</returns>
        public override string ToString()
        {
            StringBuilder returnValue = new StringBuilder();
            foreach (string overload in OverloadDefinitions)
            {
                returnValue.Append(overload);
                returnValue.Append(", ");
            }
            returnValue.Remove(returnValue.Length - 2, 2);
            return returnValue.ToString();
        }

        private MethodInformation[] _codeReferenceMethodInformation;

        internal static bool CheckMethodInfo(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return method.IsStatic
                && method.IsPublic
                && parameters.Length != 0
                && parameters[0].ParameterType == typeof(PSObject);
        }

        internal void SetCodeReference(Type type, string methodName)
        {
            MethodInfo methodAsMember = null;

            try
            {
                methodAsMember = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.IgnoreCase);
            }
            catch (AmbiguousMatchException)
            {
                // Ignore the AmbiguousMatchException. 
                // We will generate error below if we cannot find exactly one match method.
            }

            if (methodAsMember == null)
            {
                throw new ExtendedTypeSystemException("WrongMethodFormatFromTypeTable", null,
                        ExtendedTypeSystem.CodeMethodMethodFormat);
            }
            CodeReference = methodAsMember;
            if (!CheckMethodInfo(CodeReference))
            {
                throw new ExtendedTypeSystemException("WrongMethodFormat", null, ExtendedTypeSystem.CodeMethodMethodFormat);
            }
        }


        /// <summary>
        /// Used from TypeTable
        /// </summary>
        internal PSCodeMethod(string name)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            this.name = name;
        }

        /// <summary>
        /// Initializes a new instance of the PSCodeMethod class.
        /// </summary>
        /// <param name="name">name of the property</param>
        /// <param name="codeReference">this should be a public static method where the first parameter is an PSObject.</param>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        /// <exception cref="ExtendedTypeSystemException">if the codeReference does not have the right format</exception>
        public PSCodeMethod(string name, MethodInfo codeReference)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            if (codeReference == null)
            {
                throw PSTraceSource.NewArgumentNullException("codeReference");
            }
            if (!CheckMethodInfo(codeReference))
            {
                throw new ExtendedTypeSystemException("WrongMethodFormat", null, ExtendedTypeSystem.CodeMethodMethodFormat);
            }

            this.name = name;
            CodeReference = codeReference;
        }

        /// <summary>
        /// Gets the method referenced by this PSCodeMethod
        /// </summary>
        public MethodInfo CodeReference { get; private set; }

        #region virtual implementation
        /// <summary>
        /// returns a new PSMemberInfo that is a copy of this PSMemberInfo
        /// </summary>
        /// <returns>a new PSMemberInfo that is a copy of this PSMemberInfo</returns>
        public override PSMemberInfo Copy()
        {
            PSCodeMethod member = new PSCodeMethod(name, CodeReference);
            CloneBaseProperties(member);
            return member;
        }

        /// <summary>
        /// Gets the member type
        /// </summary>
        public override PSMemberTypes MemberType
        {
            get
            {
                return PSMemberTypes.CodeMethod;
            }
        }


        /// <summary>
        /// Invokes CodeReference method and returns its results.
        /// </summary>
        /// <param name="arguments">arguments to the method</param>
        /// <returns>return value from the method</returns>
        /// <exception cref="ArgumentException">if arguments is null</exception>
        /// <exception cref="MethodException">
        ///     When 
        ///         could CodeReference cannot match the given argument count or
        ///         could not convert an argument to the type required
        /// </exception>
        /// <exception cref="MethodInvocationException">For exceptions invoking the CodeReference</exception>
        public override object Invoke(params object[] arguments)
        {
            if (arguments == null)
            {
                throw PSTraceSource.NewArgumentNullException("arguments");
            }
            object[] newArguments = new object[arguments.Length + 1];
            newArguments[0] = this.instance;
            for (int i = 0; i < arguments.Length; i++)
            {
                newArguments[i + 1] = arguments[i];
            }

            if (_codeReferenceMethodInformation == null)
            {
                _codeReferenceMethodInformation = DotNetAdapter.GetMethodInformationArray(new[] { CodeReference });
            }
            object[] convertedArguments;
            Adapter.GetBestMethodAndArguments(CodeReference.Name, _codeReferenceMethodInformation, newArguments, out convertedArguments);

            return DotNetAdapter.AuxiliaryMethodInvoke(null, convertedArguments, _codeReferenceMethodInformation[0], newArguments);
        }

        /// <summary>
        /// Gets the definition for CodeReference
        /// </summary>
        public override Collection<string> OverloadDefinitions
        {
            get
            {
                return new Collection<string>
                {
                    DotNetAdapter.GetMethodInfoOverloadDefinition(null, CodeReference, 0)
                };
            }
        }

        /// <summary>
        /// Gets the type of the value for this member. Currently this always returns typeof(PSCodeMethod).FullName.
        /// </summary>
        public override string TypeNameOfValue
        {
            get
            {
                return typeof(PSCodeMethod).FullName;
            }
        }

        #endregion virtual implementation
    }

    /// <summary>
    /// Serves as a method implemented with a script
    /// </summary>
    /// <remarks>
    /// It is permitted to subclass <see cref="PSScriptMethod"/>
    /// but there is no established scenario for doing this, nor has it been tested.
    /// </remarks>
    public class PSScriptMethod : PSMethodInfo
    {
        /// <summary>
        /// Returns the string representation of this member
        /// </summary>
        /// <returns>This property as a string</returns>
        public override string ToString()
        {
            StringBuilder returnValue = new StringBuilder();
            returnValue.Append(this.TypeNameOfValue);
            returnValue.Append(" ");
            returnValue.Append(this.Name);
            returnValue.Append("();");
            return returnValue.ToString();
        }

        private ScriptBlock _script;
        private bool _shouldCloneOnAccess;

        /// <summary>
        /// Gets the script implementing this PSScriptMethod
        /// </summary>
        public ScriptBlock Script
        {
            get
            {
                if (_shouldCloneOnAccess)
                {
                    // returning a clone as TypeTable might be shared between multiple
                    // runspaces and ScriptBlock is not shareable. We decided to
                    // Clone as needed instead of Cloning whenever a shared TypeTable is
                    // attached to a Runspace to save on Memory.
                    ScriptBlock newScript = _script.Clone();
                    newScript.LanguageMode = _script.LanguageMode;

                    return newScript;
                }
                else
                {
                    return _script;
                }
            }
        }


        /// <summary>
        /// Initializes a new instance of PSScriptMethod
        /// </summary>
        /// <param name="name">name of the method</param>
        /// <param name="script">script to be used when calling the method.</param>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public PSScriptMethod(string name, ScriptBlock script)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            this.name = name;
            if (script == null)
            {
                throw PSTraceSource.NewArgumentNullException("script");
            }
            _script = script;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <param name="script"></param>
        /// <param name="shouldCloneOnAccess">
        /// Used by TypeTable.
        /// TypeTable might be shared between multiple runspaces and 
        /// ScriptBlock is not shareable. We decided to Clone as needed 
        /// instead of Cloning whenever a shared TypeTable is attached 
        /// to a Runspace to save on Memory.
        /// </param>
        internal PSScriptMethod(string name, ScriptBlock script, bool shouldCloneOnAccess)
            : this(name, script)
        {
            _shouldCloneOnAccess = shouldCloneOnAccess;
        }

        #region virtual implementation

        /// <summary>
        /// Invokes Script method and returns its results.
        /// </summary>
        /// <param name="arguments">arguments to the method</param>
        /// <returns>return value from the method</returns>
        /// <exception cref="ArgumentException">if arguments is null</exception>
        /// <exception cref="MethodInvocationException">For exceptions invoking the Script or if there is no Runspace to run the script.</exception>
        public override object Invoke(params object[] arguments)
        {
            if (arguments == null)
            {
                throw PSTraceSource.NewArgumentNullException("arguments");
            }
            return InvokeScript(Name, _script, this.instance, arguments);
        }

        internal static object InvokeScript(string methodName, ScriptBlock script, object @this, object[] arguments)
        {
            try
            {
                return script.DoInvokeReturnAsIs(
                    useLocalScope: true,
                    errorHandlingBehavior: ScriptBlock.ErrorHandlingBehavior.WriteToExternalErrorPipe,
                    dollarUnder: AutomationNull.Value,
                    input: AutomationNull.Value,
                    scriptThis: @this,
                    args: arguments);
            }
            catch (SessionStateOverflowException e)
            {
                throw new MethodInvocationException(
                    "ScriptMethodSessionStateOverflowException",
                    e,
                    ExtendedTypeSystem.MethodInvocationException,
                    methodName, arguments.Length, e.Message);
            }
            catch (RuntimeException e)
            {
                throw new MethodInvocationException(
                    "ScriptMethodRuntimeException",
                    e,
                    ExtendedTypeSystem.MethodInvocationException,
                    methodName, arguments.Length, e.Message);
            }
            catch (TerminateException)
            {
                // The debugger is terminating the execution; let the exception bubble up
                throw;
            }
            catch (FlowControlException e)
            {
                throw new MethodInvocationException(
                    "ScriptMethodFlowControlException",
                    e,
                    ExtendedTypeSystem.MethodInvocationException,
                    methodName, arguments.Length, e.Message);
            }
            catch (PSInvalidOperationException e)
            {
                throw new MethodInvocationException(
                    "ScriptMethodInvalidOperationException",
                    e,
                    ExtendedTypeSystem.MethodInvocationException,
                    methodName, arguments.Length, e.Message);
            }
        }

        /// <summary>
        /// Gets a list of all the overloads for this method
        /// </summary>
        public override Collection<string> OverloadDefinitions
        {
            get
            {
                Collection<string> retValue = new Collection<string>();
                retValue.Add(this.ToString());
                return retValue;
            }
        }

        /// <summary>
        /// returns a new PSMemberInfo that is a copy of this PSMemberInfo
        /// </summary>
        /// <returns>a new PSMemberInfo that is a copy of this PSMemberInfo</returns>
        public override PSMemberInfo Copy()
        {
            PSScriptMethod method;
            method = new PSScriptMethod(this.name, _script);
            method._shouldCloneOnAccess = _shouldCloneOnAccess;
            CloneBaseProperties(method);
            return method;
        }
        /// <summary>
        /// Gets the member type
        /// </summary>
        public override PSMemberTypes MemberType
        {
            get
            {
                return PSMemberTypes.ScriptMethod;
            }
        }

        /// <summary>
        /// Gets the type of the value for this member. Currently this always returns typeof(object).FullName.
        /// </summary>
        public override string TypeNameOfValue
        {
            get
            {
                return typeof(object).FullName;
            }
        }

        #endregion virtual implementation
    }

    /// <summary>
    /// Used to access the adapted or base methods from the BaseObject
    /// </summary>
    /// <remarks>
    /// It is permitted to subclass <see cref="PSMethod"/>
    /// but there is no established scenario for doing this, nor has it been tested.
    /// </remarks>
    public class PSMethod : PSMethodInfo
    {
        internal override void ReplicateInstance(object particularInstance)
        {
            base.ReplicateInstance(particularInstance);
            baseObject = particularInstance;
        }

        /// <summary>
        /// Returns the string representation of this member
        /// </summary>
        /// <returns>This property as a string</returns>
        public override string ToString()
        {
            return _adapter.BaseMethodToString(this);
        }

        internal object adapterData;
        private Adapter _adapter;
        internal object baseObject;

        /// <summary>
        /// Constructs this method
        /// </summary>
        /// <param name="name">name</param>
        /// <param name="adapter">adapter to be used invoking</param>
        /// <param name="baseObject">baseObject for the methods</param>
        /// <param name="adapterData">adapterData from adapter.GetMethodData</param>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        internal PSMethod(string name, Adapter adapter, object baseObject, object adapterData)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            this.name = name;
            this.adapterData = adapterData;
            _adapter = adapter;
            this.baseObject = baseObject;
        }

        /// <summary>
        /// Constructs a PSMethod
        /// </summary>
        /// <param name="name">name</param>
        /// <param name="adapter">adapter to be used invoking</param>
        /// <param name="baseObject">baseObject for the methods</param>
        /// <param name="adapterData">adapterData from adapter.GetMethodData</param>
        /// <param name="isSpecial">true if this member is a special member, false otherwise.</param>
        /// <param name="isHidden">true if this member is hidden, false otherwise.</param>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        internal PSMethod(string name, Adapter adapter, object baseObject, object adapterData, bool isSpecial, bool isHidden)
            : this(name, adapter, baseObject, adapterData)
        {
            this.IsSpecial = isSpecial;
            this.IsHidden = isHidden;
        }

        #region virtual implementation
        /// <summary>
        /// returns a new PSMemberInfo that is a copy of this PSMemberInfo
        /// </summary>
        /// <returns>a new PSMemberInfo that is a copy of this PSMemberInfo</returns>
        public override PSMemberInfo Copy()
        {
            PSMethod member = new PSMethod(this.name, _adapter, this.baseObject, this.adapterData, this.IsSpecial, this.IsHidden);
            CloneBaseProperties(member);
            return member;
        }

        /// <summary>
        /// Gets the member type
        /// </summary>
        public override PSMemberTypes MemberType
        {
            get
            {
                return PSMemberTypes.Method;
            }
        }

        /// <summary>
        /// Invokes the appropriate method overload for the given arguments and returns its result.
        /// </summary>
        /// <param name="arguments">arguments to the method</param>
        /// <returns>return value from the method</returns>
        /// <exception cref="ArgumentException">if arguments is null</exception>
        /// <exception cref="MethodException">For problems finding an appropriate method for the arguments</exception>
        /// <exception cref="MethodInvocationException">For exceptions invoking the method</exception>
        public override object Invoke(params object[] arguments)
        {
            return this.Invoke(null, arguments);
        }

        /// <summary>
        /// Invokes the appropriate method overload for the given arguments and returns its result.
        /// </summary>
        /// <param name="invocationConstraints">constraints </param>
        /// <param name="arguments">arguments to the method</param>
        /// <returns>return value from the method</returns>
        /// <exception cref="ArgumentException">if arguments is null</exception>
        /// <exception cref="MethodException">For problems finding an appropriate method for the arguments</exception>
        /// <exception cref="MethodInvocationException">For exceptions invoking the method</exception>
        internal object Invoke(PSMethodInvocationConstraints invocationConstraints, params object[] arguments)
        {
            if (arguments == null)
            {
                throw PSTraceSource.NewArgumentNullException("arguments");
            }
            return _adapter.BaseMethodInvoke(this, invocationConstraints, arguments);
        }

        /// <summary>
        /// Gets a list of all the overloads for this method
        /// </summary>
        public override Collection<string> OverloadDefinitions
        {
            get
            {
                return _adapter.BaseMethodDefinitions(this);
            }
        }

        /// <summary>
        /// Gets the type of the value for this member. This always returns typeof(PSMethod).FullName.
        /// </summary>
        public override string TypeNameOfValue
        {
            get
            {
                return typeof(PSMethod).FullName;
            }
        }

        #endregion virtual implementation

        /// <summary>
        /// True if the method is a special method like GET/SET property accessor methods.
        /// </summary>
        internal bool IsSpecial { get; private set; }
    }

    /// <summary>
    /// Used to access parameterized properties from the BaseObject
    /// </summary>
    /// <remarks>
    /// It is permitted to subclass <see cref="PSParameterizedProperty"/>
    /// but there is no established scenario for doing this, nor has it been tested.
    /// </remarks>
    public class PSParameterizedProperty : PSMethodInfo
    {
        /// <summary>
        /// Returns the string representation of this member
        /// </summary>
        /// <returns>This property as a string</returns>
        public override string ToString()
        {
            Diagnostics.Assert((this.baseObject != null) && (this.adapter != null) && (this.adapterData != null), "it should have all these properties set");
            return this.adapter.BaseParameterizedPropertyToString(this);
        }


        internal Adapter adapter;
        internal object adapterData;
        internal object baseObject;

        /// <summary>
        /// Constructs this parameterized property
        /// </summary>
        /// <param name="name">name of the property</param>
        /// <param name="adapter">adapter used in DoGetMethod</param>
        /// <param name="baseObject">object passed to DoGetMethod</param>
        /// <param name="adapterData">adapter specific data</param>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        internal PSParameterizedProperty(string name, Adapter adapter, object baseObject, object adapterData)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            this.name = name;
            this.adapter = adapter;
            this.adapterData = adapterData;
            this.baseObject = baseObject;
        }
        internal PSParameterizedProperty(string name)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            this.name = name;
        }

        /// <summary>
        /// Gets true if this property can be set
        /// </summary>
        public bool IsSettable
        {
            get
            {
                return adapter.BaseParameterizedPropertyIsSettable(this);
            }
        }

        /// <summary>
        /// Gets true if this property can be read
        /// </summary>
        public bool IsGettable
        {
            get
            {
                return adapter.BaseParameterizedPropertyIsGettable(this);
            }
        }

        #region virtual implementation
        /// <summary>
        /// Invokes the getter method and returns its result
        /// </summary>
        /// <param name="arguments">arguments to the method</param>
        /// <returns>return value from the method</returns>
        /// <exception cref="ArgumentException">if arguments is null</exception>
        /// <exception cref="GetValueException">When getting the value of a property throws an exception</exception>
        public override object Invoke(params object[] arguments)
        {
            if (arguments == null)
            {
                throw PSTraceSource.NewArgumentNullException("arguments");
            }
            return this.adapter.BaseParameterizedPropertyGet(this, arguments);
        }

        /// <summary>
        /// Invokes the setter method
        /// </summary>
        /// <param name="valueToSet">value to set this property with</param>
        /// <param name="arguments">arguments to the method</param>
        /// <exception cref="ArgumentException">if arguments is null</exception>
        /// <exception cref="SetValueException">When setting the value of a property throws an exception</exception>
        public void InvokeSet(object valueToSet, params object[] arguments)
        {
            if (arguments == null)
            {
                throw PSTraceSource.NewArgumentNullException("arguments");
            }
            this.adapter.BaseParameterizedPropertySet(this, valueToSet, arguments);
        }


        /// <summary>
        /// Returns a collection of the definitions for this property
        /// </summary>
        public override Collection<string> OverloadDefinitions
        {
            get
            {
                return adapter.BaseParameterizedPropertyDefinitions(this);
            }
        }

        /// <summary>
        /// Gets the type of the value for this member.
        /// </summary>
        public override string TypeNameOfValue
        {
            get
            {
                return adapter.BaseParameterizedPropertyType(this);
            }
        }

        /// <summary>
        /// returns a new PSMemberInfo that is a copy of this PSMemberInfo
        /// </summary>
        /// <returns>a new PSMemberInfo that is a copy of this PSMemberInfo</returns>
        public override PSMemberInfo Copy()
        {
            PSParameterizedProperty property = new PSParameterizedProperty(this.name, this.adapter, this.baseObject, this.adapterData);
            CloneBaseProperties(property);
            return property;
        }

        /// <summary>
        /// Gets the member type
        /// </summary>
        public override PSMemberTypes MemberType
        {
            get
            {
                return PSMemberTypes.ParameterizedProperty;
            }
        }
        #endregion virtual implementation

    }

    /// <summary>
    /// Serves as a set of members
    /// </summary>
    public class PSMemberSet : PSMemberInfo
    {
        internal override void ReplicateInstance(object particularInstance)
        {
            base.ReplicateInstance(particularInstance);
            foreach (var member in Members)
            {
                member.ReplicateInstance(particularInstance);
            }
        }

        /// <summary>
        /// Returns the string representation of this member
        /// </summary>
        /// <returns>This property as a string</returns>
        public override string ToString()
        {
            StringBuilder returnValue = new StringBuilder();
            returnValue.Append(" {");

            foreach (PSMemberInfo member in this.Members)
            {
                returnValue.Append(member.Name);
                returnValue.Append(", ");
            }
            if (returnValue.Length > 2)
            {
                returnValue.Remove(returnValue.Length - 2, 2);
            }
            returnValue.Insert(0, this.Name);
            returnValue.Append("}");
            return returnValue.ToString();
        }

        private PSMemberInfoIntegratingCollection<PSMemberInfo> _members;
        private PSMemberInfoIntegratingCollection<PSPropertyInfo> _properties;
        private PSMemberInfoIntegratingCollection<PSMethodInfo> _methods;
        internal PSMemberInfoInternalCollection<PSMemberInfo> internalMembers;
        private PSObject _constructorPSObject;

        private static Collection<CollectionEntry<PSMemberInfo>> s_emptyMemberCollection = new Collection<CollectionEntry<PSMemberInfo>>();
        private static Collection<CollectionEntry<PSMethodInfo>> s_emptyMethodCollection = new Collection<CollectionEntry<PSMethodInfo>>();
        private static Collection<CollectionEntry<PSPropertyInfo>> s_emptyPropertyCollection = new Collection<CollectionEntry<PSPropertyInfo>>();

        /// <summary>
        /// Initializes a new instance of PSMemberSet with no initial members
        /// </summary>
        /// <param name="name">name for the member set</param>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public PSMemberSet(string name)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            this.name = name;
            this.internalMembers = new PSMemberInfoInternalCollection<PSMemberInfo>();
            _members = new PSMemberInfoIntegratingCollection<PSMemberInfo>(this, s_emptyMemberCollection);
            _properties = new PSMemberInfoIntegratingCollection<PSPropertyInfo>(this, s_emptyPropertyCollection);
            _methods = new PSMemberInfoIntegratingCollection<PSMethodInfo>(this, s_emptyMethodCollection);
        }

        /// <summary>
        /// Initializes a new instance of PSMemberSet with all the initial members in <paramref name="members"/>
        /// </summary>
        /// <param name="name">name for the member set</param>
        /// <param name="members">members in the member set</param>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public PSMemberSet(string name, IEnumerable<PSMemberInfo> members)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            this.name = name;
            if (members == null)
            {
                throw PSTraceSource.NewArgumentNullException("members");
            }
            this.internalMembers = new PSMemberInfoInternalCollection<PSMemberInfo>();
            foreach (PSMemberInfo member in members)
            {
                if (member == null)
                {
                    throw PSTraceSource.NewArgumentNullException("members");
                }
                this.internalMembers.Add(member.Copy());
            }
            _members = new PSMemberInfoIntegratingCollection<PSMemberInfo>(this, s_emptyMemberCollection);
            _properties = new PSMemberInfoIntegratingCollection<PSPropertyInfo>(this, s_emptyPropertyCollection);
            _methods = new PSMemberInfoIntegratingCollection<PSMethodInfo>(this, s_emptyMethodCollection);
        }

        private static Collection<CollectionEntry<PSMemberInfo>> s_typeMemberCollection = GetTypeMemberCollection();
        private static Collection<CollectionEntry<PSMethodInfo>> s_typeMethodCollection = GetTypeMethodCollection();
        private static Collection<CollectionEntry<PSPropertyInfo>> s_typePropertyCollection = GetTypePropertyCollection();

        private static Collection<CollectionEntry<PSMemberInfo>> GetTypeMemberCollection()
        {
            Collection<CollectionEntry<PSMemberInfo>> returnValue = new Collection<CollectionEntry<PSMemberInfo>>();
            returnValue.Add(new CollectionEntry<PSMemberInfo>(
                PSObject.TypeTableGetMembersDelegate<PSMemberInfo>,
                PSObject.TypeTableGetMemberDelegate<PSMemberInfo>,
                true, true, "type table members"));
            return returnValue;
        }

        private static Collection<CollectionEntry<PSMethodInfo>> GetTypeMethodCollection()
        {
            Collection<CollectionEntry<PSMethodInfo>> returnValue = new Collection<CollectionEntry<PSMethodInfo>>();
            returnValue.Add(new CollectionEntry<PSMethodInfo>(
                PSObject.TypeTableGetMembersDelegate<PSMethodInfo>,
                PSObject.TypeTableGetMemberDelegate<PSMethodInfo>,
                true, true, "type table members"));
            return returnValue;
        }

        private static Collection<CollectionEntry<PSPropertyInfo>> GetTypePropertyCollection()
        {
            Collection<CollectionEntry<PSPropertyInfo>> returnValue = new Collection<CollectionEntry<PSPropertyInfo>>();
            returnValue.Add(new CollectionEntry<PSPropertyInfo>(
                PSObject.TypeTableGetMembersDelegate<PSPropertyInfo>,
                PSObject.TypeTableGetMemberDelegate<PSPropertyInfo>,
                true, true, "type table members"));
            return returnValue;
        }

        /// <summary>
        /// Used to create the Extended MemberSet
        /// </summary>
        /// <param name="name">name of the memberSet</param>
        /// <param name="mshObject">object associated with this memberset</param>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        internal PSMemberSet(string name, PSObject mshObject)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            this.name = name;
            if (mshObject == null)
            {
                throw PSTraceSource.NewArgumentNullException("mshObject");
            }
            _constructorPSObject = mshObject;
            this.internalMembers = mshObject.InstanceMembers;
            _members = new PSMemberInfoIntegratingCollection<PSMemberInfo>(this, s_typeMemberCollection);
            _properties = new PSMemberInfoIntegratingCollection<PSPropertyInfo>(this, s_typePropertyCollection);
            _methods = new PSMemberInfoIntegratingCollection<PSMethodInfo>(this, s_typeMethodCollection);
        }

        internal bool inheritMembers = true;

        /// <summary>
        /// Gets a flag indicating whether the memberset will inherit members of the memberset 
        /// of the same name in the "parent" class.
        /// </summary>
        public bool InheritMembers
        {
            get
            {
                return this.inheritMembers;
            }
        }


        /// <summary>
        /// Gets the internal member collection
        /// </summary>
        internal virtual PSMemberInfoInternalCollection<PSMemberInfo> InternalMembers
        {
            get { return this.internalMembers; }
        }

        /// <summary>
        /// Gets the member collection
        /// </summary>
        public PSMemberInfoCollection<PSMemberInfo> Members
        {
            get
            {
                return _members;
            }
        }

        /// <summary>
        /// Gets the Property collection, or the members that are actually properties.
        /// </summary>
        public PSMemberInfoCollection<PSPropertyInfo> Properties
        {
            get
            {
                return _properties;
            }
        }

        /// <summary>
        /// Gets the Method collection, or the members that are actually methods.
        /// </summary>
        public PSMemberInfoCollection<PSMethodInfo> Methods
        {
            get
            {
                return _methods;
            }
        }

        #region virtual implementation
        /// <summary>
        /// returns a new PSMemberInfo that is a copy of this PSMemberInfo
        /// </summary>
        /// <returns>a new PSMemberInfo that is a copy of this PSMemberInfo</returns>
        public override PSMemberInfo Copy()
        {
            if (_constructorPSObject == null)
            {
                PSMemberSet memberSet = new PSMemberSet(name);
                foreach (PSMemberInfo member in this.Members)
                {
                    memberSet.Members.Add(member);
                }
                CloneBaseProperties(memberSet);
                return memberSet;
            }
            else
            {
                return new PSMemberSet(name, _constructorPSObject);
            }
        }

        /// <summary>
        /// Gets the member type. For PSMemberSet the member type is PSMemberTypes.MemberSet.
        /// </summary>
        public override PSMemberTypes MemberType
        {
            get
            {
                return PSMemberTypes.MemberSet;
            }
        }

        /// <summary>
        /// Gets the value of this member. The getter returns the PSMemberSet itself.
        /// </summary>
        /// <exception cref="ExtendedTypeSystemException">When trying to set the property</exception>
        public override object Value
        {
            get
            {
                return this;
            }
            set
            {
                throw new ExtendedTypeSystemException("CannotChangePSMemberSetValue", null,
                    ExtendedTypeSystem.CannotSetValueForMemberType, this.GetType().FullName);
            }
        }

        /// <summary>
        /// Gets the type of the value for this member. This returns typeof(PSMemberSet).FullName.
        /// </summary>
        public override string TypeNameOfValue
        {
            get
            {
                return typeof(PSMemberSet).FullName;
            }
        }
        #endregion virtual implementation

    }

    /// <summary>
    /// This MemberSet is used internally to represent the memberset for properties
    /// PSObject, PSBase, PSAdapted members of a PSObject. Having a specialized
    /// memberset enables delay loading the members for these members. This saves 
    /// time loading the members of a PSObject.
    /// </summary>
    /// <remarks>
    /// This is added to improve hosting PowerShell's PSObjects in a ASP.Net GridView
    /// Control
    /// </remarks>
    internal class PSInternalMemberSet : PSMemberSet
    {
        private object _syncObject = new Object();
        private PSObject _psObject;

        #region Constructor

        /// <summary>
        /// Constructs the specialized member set.
        /// </summary>
        /// <param name="propertyName">
        /// Should be one of PSObject, PSBase, PSAdapted
        /// </param>
        /// <param name="psObject">
        /// original PSObject to use to generate members
        /// </param>
        internal PSInternalMemberSet(string propertyName, PSObject psObject)
            : base(propertyName)
        {
            this.internalMembers = null;
            _psObject = psObject;
        }

        #endregion

        #region virtual overrides

        /// <summary>
        /// Generates the members when needed.
        /// </summary>
        internal override PSMemberInfoInternalCollection<PSMemberInfo> InternalMembers
        {
            get
            {
                // do not cache "psadapted"
                if (name.Equals(PSObject.AdaptedMemberSetName, StringComparison.OrdinalIgnoreCase))
                {
                    return GetInternalMembersFromAdapted();
                }

                // cache "psbase" and "psobject"
                if (null == internalMembers)
                {
                    lock (_syncObject)
                    {
                        if (null == internalMembers)
                        {
                            internalMembers = new PSMemberInfoInternalCollection<PSMemberInfo>();

                            switch (name.ToLowerInvariant())
                            {
                                case PSObject.BaseObjectMemberSetName:
                                    GenerateInternalMembersFromBase();
                                    break;
                                case PSObject.PSObjectMemberSetName:
                                    GenerateInternalMembersFromPSObject();
                                    break;
                                default:
                                    Diagnostics.Assert(false,
                                        string.Format(CultureInfo.InvariantCulture,
                                        "PSInternalMemberSet cannot process {0}", name));
                                    break;
                            }
                        }
                    }
                }

                return internalMembers;
            }
        }

        #endregion

        #region  Private Methods

        private void GenerateInternalMembersFromBase()
        {
            if (_psObject.isDeserialized)
            {
                if (_psObject.clrMembers != null)
                {
                    foreach (PSMemberInfo member in _psObject.clrMembers)
                    {
                        internalMembers.Add(member.Copy());
                    }
                }
            }
            else
            {
                foreach (PSMemberInfo member in
                    PSObject.dotNetInstanceAdapter.BaseGetMembers<PSMemberInfo>(_psObject.ImmediateBaseObject))
                {
                    internalMembers.Add(member.Copy());
                }
            }
        }

        private PSMemberInfoInternalCollection<PSMemberInfo> GetInternalMembersFromAdapted()
        {
            PSMemberInfoInternalCollection<PSMemberInfo> retVal = new PSMemberInfoInternalCollection<PSMemberInfo>();

            if (_psObject.isDeserialized)
            {
                if (_psObject.adaptedMembers != null)
                {
                    foreach (PSMemberInfo member in _psObject.adaptedMembers)
                    {
                        retVal.Add(member.Copy());
                    }
                }
            }
            else
            {
                foreach (PSMemberInfo member in _psObject.InternalAdapter.BaseGetMembers<PSMemberInfo>(
                    _psObject.ImmediateBaseObject))
                {
                    retVal.Add(member.Copy());
                }
            }

            return retVal;
        }

        private void GenerateInternalMembersFromPSObject()
        {
            PSMemberInfoCollection<PSMemberInfo> members = PSObject.dotNetInstanceAdapter.BaseGetMembers<PSMemberInfo>(
               _psObject);
            foreach (PSMemberInfo member in members)
            {
                internalMembers.Add(member.Copy());
            }
        }

        #endregion
    }

    /// <summary>
    /// Serves as a list of property names
    /// </summary>
    /// <remarks>
    /// It is permitted to subclass <see cref="PSPropertySet"/>
    /// but there is no established scenario for doing this, nor has it been tested.
    /// </remarks>
    public class PSPropertySet : PSMemberInfo
    {
        /// <summary>
        /// Returns the string representation of this member
        /// </summary>
        /// <returns>This property as a string</returns>
        public override string ToString()
        {
            StringBuilder returnValue = new StringBuilder();
            returnValue.Append(this.Name);
            returnValue.Append(" {");
            if (ReferencedPropertyNames.Count != 0)
            {
                foreach (string property in ReferencedPropertyNames)
                {
                    returnValue.Append(property);
                    returnValue.Append(", ");
                }
                returnValue.Remove(returnValue.Length - 2, 2);
            }
            returnValue.Append("}");
            return returnValue.ToString();
        }

        /// <summary>
        /// Initializes a new instance of PSPropertySet with a name and list of property names
        /// </summary>
        /// <param name="name">name of the set</param>
        /// <param name="referencedPropertyNames">name of the properties in the set</param>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public PSPropertySet(string name, IEnumerable<string> referencedPropertyNames)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            this.name = name;
            if (referencedPropertyNames == null)
            {
                throw PSTraceSource.NewArgumentNullException("referencedPropertyNames");
            }
            ReferencedPropertyNames = new Collection<string>();
            foreach (string referencedPropertyName in referencedPropertyNames)
            {
                if (String.IsNullOrEmpty(referencedPropertyName))
                {
                    throw PSTraceSource.NewArgumentException("referencedPropertyNames");
                }
                ReferencedPropertyNames.Add(referencedPropertyName);
            }
        }

        /// <summary>
        /// Gets the property names in this property set
        /// </summary>
        public Collection<string> ReferencedPropertyNames { get; }

        #region virtual implementation
        /// <summary>
        /// returns a new PSMemberInfo that is a copy of this PSMemberInfo
        /// </summary>
        /// <returns>a new PSMemberInfo that is a copy of this PSMemberInfo</returns>
        public override PSMemberInfo Copy()
        {
            PSPropertySet member = new PSPropertySet(name, ReferencedPropertyNames);
            CloneBaseProperties(member);
            return member;
        }
        /// <summary>
        /// Gets the member type
        /// </summary>
        public override PSMemberTypes MemberType
        {
            get
            {
                return PSMemberTypes.PropertySet;
            }
        }

        /// <summary>
        /// Gets the PSPropertySet itself.
        /// </summary>
        /// <exception cref="ExtendedTypeSystemException">When setting the member</exception>
        public override object Value
        {
            get
            {
                return this;
            }
            set
            {
                throw new ExtendedTypeSystemException("CannotChangePSPropertySetValue", null,
                    ExtendedTypeSystem.CannotSetValueForMemberType, this.GetType().FullName);
            }
        }

        /// <summary>
        /// Gets the type of the value for this member. This returns typeof(PSPropertySet).FullName.
        /// </summary>
        public override string TypeNameOfValue
        {
            get
            {
                return typeof(PSPropertySet).FullName;
            }
        }
        #endregion virtual implementation
    }

    /// <summary>
    /// Used to access the adapted or base events from the BaseObject
    /// </summary>
    /// <remarks>
    /// It is permitted to subclass <see cref="PSMethod"/>
    /// but there is no established scenario for doing this, nor has it been tested.
    /// </remarks>
    public class PSEvent : PSMemberInfo
    {
        /// <summary>
        /// Returns the string representation of this member
        /// </summary>
        /// <returns>This property as a string</returns>
        public override string ToString()
        {
            StringBuilder eventDefinition = new StringBuilder();
            eventDefinition.Append(this.baseEvent.ToString());

            eventDefinition.Append("(");

            int loopCounter = 0;
            foreach (ParameterInfo parameter in baseEvent.EventHandlerType.GetMethod("Invoke").GetParameters())
            {
                if (loopCounter > 0)
                    eventDefinition.Append(", ");

                eventDefinition.Append(parameter.ParameterType.ToString());

                loopCounter++;
            }

            eventDefinition.Append(")");

            return eventDefinition.ToString();
        }
        internal EventInfo baseEvent;

        /// <summary>
        /// Constructs this event
        /// </summary>
        /// <param name="baseEvent">The actual event</param>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        internal PSEvent(EventInfo baseEvent)
        {
            this.baseEvent = baseEvent;
            this.name = baseEvent.Name;
        }

        #region virtual implementation
        /// <summary>
        /// returns a new PSMemberInfo that is a copy of this PSMemberInfo
        /// </summary>
        /// <returns>a new PSMemberInfo that is a copy of this PSMemberInfo</returns>
        public override PSMemberInfo Copy()
        {
            PSEvent member = new PSEvent(this.baseEvent);
            CloneBaseProperties(member);
            return member;
        }

        /// <summary>
        /// Gets the member type
        /// </summary>
        public override PSMemberTypes MemberType
        {
            get
            {
                return PSMemberTypes.Event;
            }
        }

        /// <summary>
        /// Gets the value of this member. The getter returns the
        /// actual .NET event that this type wraps.
        /// </summary>
        /// <exception cref="ExtendedTypeSystemException">When setting the member</exception>
        public sealed override object Value
        {
            get
            {
                return baseEvent;
            }
            set
            {
                throw new ExtendedTypeSystemException("CannotChangePSEventInfoValue", null,
                    ExtendedTypeSystem.CannotSetValueForMemberType, this.GetType().FullName);
            }
        }

        /// <summary>
        /// Gets the type of the value for this member. This always returns typeof(PSMethod).FullName.
        /// </summary>
        public override string TypeNameOfValue
        {
            get
            {
                return typeof(PSEvent).FullName;
            }
        }

        #endregion virtual implementation
    }

    /// <summary>
    /// A dynamic member
    /// </summary>
    public class PSDynamicMember : PSMemberInfo
    {
        internal PSDynamicMember(string name)
        {
            this.name = name;
        }

        /// <summary/>
        public override string ToString()
        {
            return "dynamic " + Name;
        }

        /// <summary/>
        public override PSMemberTypes MemberType
        {
            get { return PSMemberTypes.Dynamic; }
        }

        /// <summary/>
        public override object Value
        {
            get { throw PSTraceSource.NewInvalidOperationException(); }
            set { throw PSTraceSource.NewInvalidOperationException(); }
        }

        /// <summary/>
        public override string TypeNameOfValue
        {
            get { return "dynamic"; }
        }

        /// <summary/>
        public override PSMemberInfo Copy()
        {
            return new PSDynamicMember(Name);
        }
    }

    #endregion PSMemberInfo

    #region Member collection classes and its auxiliary classes

    /// <summary>
    /// /// This class is used in PSMemberInfoInternalCollection and ReadOnlyPSMemberInfoCollection
    /// </summary>
    internal class MemberMatch
    {
        internal static WildcardPattern GetNamePattern(string name)
        {
            if (name != null && WildcardPattern.ContainsWildcardCharacters(name))
            {
                return WildcardPattern.Get(name, WildcardOptions.IgnoreCase);
            }
            return null;
        }


        /// <summary>
        /// Returns all members in memberList matching name and memberTypes
        /// </summary>
        /// <param name="memberList">Members to look for member with the correct types and name.</param>
        /// <param name="name">Name of the members to look for. The name might contain globbing characters</param>
        /// <param name="nameMatch">WildcardPattern out of name</param>
        /// <param name="memberTypes">type of members we want to retrieve</param>
        /// <returns>A collection of members of the right types and name extracted from memberList.</returns>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        internal static PSMemberInfoInternalCollection<T> Match<T>(PSMemberInfoInternalCollection<T> memberList, string name, WildcardPattern nameMatch, PSMemberTypes memberTypes) where T : PSMemberInfo
        {
            PSMemberInfoInternalCollection<T> returnValue = new PSMemberInfoInternalCollection<T>();
            if (memberList == null)
            {
                throw PSTraceSource.NewArgumentNullException("memberList");
            }

            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            if (nameMatch == null)
            {
                T member = memberList[name];
                if (member != null && (member.MemberType & memberTypes) != 0)
                {
                    returnValue.Add(member);
                }
                return returnValue;
            }

            foreach (T member in memberList)
            {
                if (nameMatch.IsMatch(member.Name) && ((member.MemberType & memberTypes) != 0))
                {
                    returnValue.Add(member);
                }
            }
            return returnValue;
        }
    }

    /// <summary>
    /// Serves as the collection of members in an PSObject or MemberSet
    /// </summary>
    public abstract class PSMemberInfoCollection<T> : IEnumerable<T> where T : PSMemberInfo
    {
        #region ctor
        /// <summary>
        /// Initializes a new instance of an PSMemberInfoCollection derived class
        /// </summary>
        protected PSMemberInfoCollection()
        {
        }
        #endregion ctor

        #region abstract
        /// <summary>
        /// Adds a member to this collection
        /// </summary>
        /// <param name="member">member to be added</param>
        /// <exception cref="ExtendedTypeSystemException">
        ///     When:
        ///         adding a member to an PSMemberSet from the type configuration file or
        ///         adding a member with a reserved member name or
        ///         trying to add a member with a type not compatible with this collection or
        ///         a member by this name is already present
        /// </exception>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public abstract void Add(T member);

        /// <summary>
        /// Adds a member to this collection
        /// </summary>
        /// <param name="member">member to be added</param>
        /// <param name="preValidated">flag to indicate that validation has already been done
        ///     on this new member.  Use only when you can guarantee that the input will not 
        ///     cause any of the errors normally caught by this method.</param>
        /// <exception cref="ExtendedTypeSystemException">
        ///     When:
        ///         adding a member to an PSMemberSet from the type configuration file or
        ///         adding a member with a reserved member name or
        ///         trying to add a member with a type not compatible with this collection or
        ///         a member by this name is already present
        /// </exception>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public abstract void Add(T member, bool preValidated);

        /// <summary>
        /// Removes a member from this collection
        /// </summary>
        /// <param name="name">name of the member to be removed</param>
        /// <exception cref="ExtendedTypeSystemException">
        ///     When:
        ///         removing a member from an PSMemberSet from the type configuration file
        ///         removing a member with a reserved member name or
        ///         trying to remove a member with a type not compatible with this collection
        /// </exception>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public abstract void Remove(string name);

        /// <summary>
        /// Gets the member in this collection matching name. If the member does not exist, null is returned.
        /// </summary>
        /// <param name="name">name of the member to look for</param>
        /// <returns>the member matching name</returns>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public abstract T this[string name]
        {
            get;
        }
        #endregion abstract

        #region Match
        /// <summary>
        /// Returns all members in the collection matching name
        /// </summary>
        /// <param name="name">name of the members to be return. May contain wildcard characters.</param>
        /// <returns>all members in the collection matching name</returns>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public abstract ReadOnlyPSMemberInfoCollection<T> Match(string name);

        /// <summary>
        /// Returns all members in the collection matching name and types
        /// </summary>
        /// <param name="name">name of the members to be return. May contain wildcard characters.</param>
        /// <param name="memberTypes">type of the members to be searched.</param>
        /// <returns>all members in the collection matching name and types</returns>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public abstract ReadOnlyPSMemberInfoCollection<T> Match(string name, PSMemberTypes memberTypes);

        /// <summary>
        /// Returns all members in the collection matching name and types
        /// </summary>
        /// <param name="name">name of the members to be return. May contain wildcard characters.</param>
        /// <param name="memberTypes">type of the members to be searched.</param>
        /// <param name="matchOptions">match options</param>
        /// <returns>all members in the collection matching name and types</returns>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        internal abstract ReadOnlyPSMemberInfoCollection<T> Match(string name, PSMemberTypes memberTypes, MshMemberMatchOptions matchOptions);


        #endregion Match

        internal static bool IsReservedName(string name)
        {
            return (String.Equals(name, PSObject.BaseObjectMemberSetName, StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(name, PSObject.AdaptedMemberSetName, StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(name, PSObject.ExtendedMemberSetName, StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(name, PSObject.PSObjectMemberSetName, StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(name, PSObject.PSTypeNames, StringComparison.OrdinalIgnoreCase));
        }

        #region IEnumerable


        /// <summary>
        /// Gets the general enumerator for this collection
        /// </summary>
        /// <returns>the enumerator for this collection</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Gets the specific enumerator for this collection. 
        /// </summary>
        /// <returns>the enumerator for this collection</returns>
        public abstract IEnumerator<T> GetEnumerator();
        #endregion IEnumerable
    }

    /// <summary>
    /// Serves as a read only collection of members
    /// </summary>
    /// <remarks>
    /// It is permitted to subclass <see cref="ReadOnlyPSMemberInfoCollection&lt;T&gt;"/>
    /// but there is no established scenario for doing this, nor has it been tested.
    /// </remarks>
    public class ReadOnlyPSMemberInfoCollection<T> : IEnumerable<T> where T : PSMemberInfo
    {
        private PSMemberInfoInternalCollection<T> _members;

        /// <summary>
        /// Initializes a new instance of ReadOnlyPSMemberInfoCollection with the given members
        /// </summary>
        /// <param name="members"></param>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        internal ReadOnlyPSMemberInfoCollection(PSMemberInfoInternalCollection<T> members)
        {
            if (members == null)
            {
                throw PSTraceSource.NewArgumentNullException("members");
            }
            _members = members;
        }

        /// <summary>
        /// Return the member in this collection matching name. If the member does not exist, null is returned.
        /// </summary>
        /// <param name="name">name of the member to look for</param>
        /// <returns>the member matching name</returns>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public T this[string name]
        {
            get
            {
                if (String.IsNullOrEmpty(name))
                {
                    throw PSTraceSource.NewArgumentException("name");
                }
                return _members[name];
            }
        }

        /// <summary>
        /// Returns all members in the collection matching name
        /// </summary>
        /// <param name="name">name of the members to be return. May contain wildcard characters.</param>
        /// <returns>all members in the collection matching name</returns>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public ReadOnlyPSMemberInfoCollection<T> Match(string name)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            return _members.Match(name);
        }

        /// <summary>
        /// Returns all members in the collection matching name and types
        /// </summary>
        /// <param name="name">name of the members to be return. May contain wildcard characters.</param>
        /// <param name="memberTypes">type of the members to be searched.</param>
        /// <returns>all members in the collection matching name and types</returns>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public ReadOnlyPSMemberInfoCollection<T> Match(string name, PSMemberTypes memberTypes)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            return _members.Match(name, memberTypes);
        }

        /// <summary>
        /// Gets the general enumerator for this collection
        /// </summary>
        /// <returns>the enumerator for this collection</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Gets the specific enumerator for this collection. 
        /// </summary>
        /// <returns>the enumerator for this collection</returns>
        public virtual IEnumerator<T> GetEnumerator()
        {
            return _members.GetEnumerator();
        }

        /// <summary>
        /// Gets the number of elements in this collection
        /// </summary>
        public int Count { get { return _members.Count; } }

        /// <summary>
        /// Returns the 0 based member identified by index
        /// </summary>
        /// <param name="index">index of the member to retrieve</param>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public T this[int index] { get { return _members[index]; } }
    }

    /// <summary>
    /// Collection of members
    /// </summary>
    internal class PSMemberInfoInternalCollection<T> : PSMemberInfoCollection<T>, IEnumerable<T> where T : PSMemberInfo
    {
        private readonly OrderedDictionary _members;
        private int _countHidden;

        /// <summary>
        /// Constructs this collection
        /// </summary>
        internal PSMemberInfoInternalCollection()
        {
            _members = new OrderedDictionary(StringComparer.OrdinalIgnoreCase);
        }

        private void Replace(T oldMember, T newMember)
        {
            _members[newMember.Name] = newMember;
            if (oldMember.IsHidden)
            {
                _countHidden--;
            }
            if (newMember.IsHidden)
            {
                _countHidden++;
            }
        }

        /// <summary>
        /// Adds a member to the collection by replacing the one with the same name
        /// </summary>
        /// <param name="newMember"></param>
        internal void Replace(T newMember)
        {
            Diagnostics.Assert(newMember != null, "called from internal code that checks for new member not null");

            lock (_members)
            {
                var oldMember = _members[newMember.Name] as T;
                Diagnostics.Assert(oldMember != null, "internal code checks member already exists");
                Replace(oldMember, newMember);
            }
        }

        /// <summary>
        /// Adds a member to this collection
        /// </summary>
        /// <param name="member">member to be added</param>
        /// <exception cref="ExtendedTypeSystemException">when a member by this name is already present</exception>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public override void Add(T member)
        {
            Add(member, false);
        }

        /// <summary>
        /// Adds a member to this collection
        /// </summary>
        /// <param name="member">member to be added</param>
        /// <param name="preValidated">flag to indicate that validation has already been done
        ///     on this new member.  Use only when you can guarantee that the input will not 
        ///     cause any of the errors normally caught by this method.</param>
        /// <exception cref="ExtendedTypeSystemException">when a member by this name is already present</exception>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public override void Add(T member, bool preValidated)
        {
            if (member == null)
            {
                throw PSTraceSource.NewArgumentNullException("member");
            }

            lock (_members)
            {
                var existingMember = _members[member.Name] as T;
                if (existingMember != null)
                {
                    Replace(existingMember, member);
                }
                else
                {
                    _members[member.Name] = member;
                    if (member.IsHidden)
                    {
                        _countHidden++;
                    }
                }
            }
        }

        /// <summary>
        /// Removes a member from this collection
        /// </summary>
        /// <param name="name">name of the member to be removed</param>
        /// <exception cref="ExtendedTypeSystemException">When removing a member with a reserved member name</exception>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public override void Remove(string name)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }

            if (IsReservedName(name))
            {
                throw new ExtendedTypeSystemException("PSMemberInfoInternalCollectionRemoveReservedName",
                    null,
                    ExtendedTypeSystem.ReservedMemberName,
                    name);
            }

            lock (_members)
            {
                var member = _members[name] as PSMemberInfo;
                if (member != null)
                {
                    if (member.IsHidden)
                    {
                        _countHidden--;
                    }
                    _members.Remove(name);
                }
            }
        }

        /// <summary>
        /// Returns the member in this collection matching name
        /// </summary>
        /// <param name="name">name of the member to look for</param>
        /// <returns>the member matching name</returns>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public override T this[string name]
        {
            get
            {
                if (String.IsNullOrEmpty(name))
                {
                    throw PSTraceSource.NewArgumentException("name");
                }

                lock (_members)
                {
                    return _members[name] as T;
                }
            }
        }

        /// <summary>
        /// Returns all members in the collection matching name
        /// </summary>
        /// <param name="name">name of the members to be return. May contain wildcard characters.</param>
        /// <returns>all members in the collection matching name</returns>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public override ReadOnlyPSMemberInfoCollection<T> Match(string name)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            return Match(name, PSMemberTypes.All, MshMemberMatchOptions.None);
        }

        /// <summary>
        /// Returns all members in the collection matching name and types
        /// </summary>
        /// <param name="name">name of the members to be return. May contain wildcard characters.</param>
        /// <param name="memberTypes">type of the members to be searched.</param>
        /// <returns>all members in the collection matching name and types</returns>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public override ReadOnlyPSMemberInfoCollection<T> Match(string name, PSMemberTypes memberTypes)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            return Match(name, memberTypes, MshMemberMatchOptions.None);
        }

        /// <summary>
        /// Returns all members in the collection matching name and types
        /// </summary>
        /// <param name="name">name of the members to be return. May contain wildcard characters.</param>
        /// <param name="memberTypes">type of the members to be searched.</param>
        /// <param name="matchOptions">match options</param>
        /// <returns>all members in the collection matching name and types</returns>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        internal override ReadOnlyPSMemberInfoCollection<T> Match(string name, PSMemberTypes memberTypes, MshMemberMatchOptions matchOptions)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }

            PSMemberInfoInternalCollection<T> internalMembers = GetInternalMembers(matchOptions);
            return new ReadOnlyPSMemberInfoCollection<T>(MemberMatch.Match(internalMembers, name, MemberMatch.GetNamePattern(name), memberTypes));
        }

        private PSMemberInfoInternalCollection<T> GetInternalMembers(MshMemberMatchOptions matchOptions)
        {
            PSMemberInfoInternalCollection<T> returnValue = new PSMemberInfoInternalCollection<T>();
            lock (_members)
            {
                foreach (T member in _members.Values.OfType<T>())
                {
                    if (member.MatchesOptions(matchOptions))
                    {
                        returnValue.Add(member);
                    }
                }
            }

            return returnValue;
        }

        /// <summary>
        /// The number of elements in this collection
        /// </summary>
        internal int Count
        {
            get
            {
                lock (_members)
                {
                    return _members.Count;
                }
            }
        }

        /// <summary>
        /// The number of elements in this collection not marked as Hidden
        /// </summary>
        internal int VisibleCount
        {
            get
            {
                lock (_members)
                {
                    return _members.Count - _countHidden;
                }
            }
        }

        /// <summary>
        /// Returns the 0 based member identified by index
        /// </summary>
        /// <param name="index">index of the member to retrieve</param>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        internal T this[int index]
        {
            get
            {
                lock (_members)
                {
                    return _members[index] as T;
                }
            }
        }

        /// <summary>
        /// Gets the specific enumerator for this collection.
        /// This virtual works around the difficulty of implementing
        /// interfaces virtually.
        /// </summary>
        /// <returns>the enumerator for this collection</returns>
        public override IEnumerator<T> GetEnumerator()
        {
            lock (_members)
            {
                // Copy the members to a list so that iteration can be performed without holding a lock.
                return _members.Values.OfType<T>().ToList().GetEnumerator();
            }
        }
    }

    #region CollectionEntry


    internal class CollectionEntry<T> where T : PSMemberInfo
    {
        internal delegate PSMemberInfoInternalCollection<T> GetMembersDelegate(PSObject obj);
        internal delegate T GetMemberDelegate(PSObject obj, string name);

        internal CollectionEntry(GetMembersDelegate getMembers, GetMemberDelegate getMember,
                                    bool shouldReplicateWhenReturning, bool shouldCloneWhenReturning, string collectionNameForTracing)
        {
            GetMembers = getMembers;
            GetMember = getMember;
            ShouldReplicateWhenReturning = shouldReplicateWhenReturning;
            ShouldCloneWhenReturning = shouldCloneWhenReturning;
            CollectionNameForTracing = collectionNameForTracing;
        }

        internal GetMembersDelegate GetMembers { get; }

        internal GetMemberDelegate GetMember { get; }

        internal bool ShouldReplicateWhenReturning { get; }

        internal bool ShouldCloneWhenReturning { get; }

        internal string CollectionNameForTracing { get; }
    }
    #endregion CollectionEntry

    internal static class ReservedNameMembers
    {
        private static object GenerateMemberSet(string name, object obj)
        {
            PSObject mshOwner = PSObject.AsPSObject(obj);
            var memberSet = mshOwner.InstanceMembers[name];
            if (memberSet == null)
            {
                memberSet = new PSInternalMemberSet(name, mshOwner)
                {
                    ShouldSerialize = false,
                    IsHidden = true,
                    IsReservedMember = true
                };
                mshOwner.InstanceMembers.Add(memberSet);
                memberSet.instance = mshOwner;
            }
            return memberSet;
        }

        internal static object GeneratePSBaseMemberSet(object obj)
        {
            return GenerateMemberSet(PSObject.BaseObjectMemberSetName, obj);
        }

        internal static object GeneratePSAdaptedMemberSet(object obj)
        {
            return GenerateMemberSet(PSObject.AdaptedMemberSetName, obj);
        }

        internal static object GeneratePSObjectMemberSet(object obj)
        {
            return GenerateMemberSet(PSObject.PSObjectMemberSetName, obj);
        }

        internal static object GeneratePSExtendedMemberSet(object obj)
        {
            PSObject mshOwner = PSObject.AsPSObject(obj);
            var memberSet = mshOwner.InstanceMembers[PSObject.ExtendedMemberSetName];
            if (memberSet == null)
            {
                memberSet = new PSMemberSet(PSObject.ExtendedMemberSetName, mshOwner)
                {
                    ShouldSerialize = false,
                    IsHidden = true,
                    IsReservedMember = true
                };
                memberSet.ReplicateInstance(mshOwner);
                memberSet.instance = mshOwner;
                mshOwner.InstanceMembers.Add(memberSet);
            }

            return memberSet;
        }

        // This is the implementation of the PSTypeNames CodeProperty.
        public static Collection<string> PSTypeNames(PSObject o)
        {
            return o.TypeNames;
        }

        internal static void GeneratePSTypeNames(object obj)
        {
            PSObject mshOwner = PSObject.AsPSObject(obj);
            if (null != mshOwner.InstanceMembers[PSObject.PSTypeNames])
            {
                // PSTypeNames member set is already generated..just return.
                return;
            }

            PSCodeProperty codeProperty = new PSCodeProperty(PSObject.PSTypeNames, CachedReflectionInfo.ReservedNameMembers_PSTypeNames)
            {
                ShouldSerialize = false,
                instance = mshOwner,
                IsHidden = true,
                IsReservedMember = true
            };
            mshOwner.InstanceMembers.Add(codeProperty);
        }
    }

    internal class PSMemberInfoIntegratingCollection<T> : PSMemberInfoCollection<T>, IEnumerable<T> where T : PSMemberInfo
    {
        #region reserved names

        private void GenerateAllReservedMembers()
        {
            if (!_mshOwner.hasGeneratedReservedMembers)
            {
                _mshOwner.hasGeneratedReservedMembers = true;
                ReservedNameMembers.GeneratePSExtendedMemberSet(_mshOwner);
                ReservedNameMembers.GeneratePSBaseMemberSet(_mshOwner);
                ReservedNameMembers.GeneratePSObjectMemberSet(_mshOwner);
                ReservedNameMembers.GeneratePSAdaptedMemberSet(_mshOwner);
                ReservedNameMembers.GeneratePSTypeNames(_mshOwner);
            }
        }

        #endregion reserved names

        #region Constructor, fields and properties

        internal Collection<CollectionEntry<T>> Collections { get; }


        private PSObject _mshOwner;
        private PSMemberSet _memberSetOwner;

        internal PSMemberInfoIntegratingCollection(object owner, Collection<CollectionEntry<T>> collections)
        {
            if (owner == null)
            {
                throw PSTraceSource.NewArgumentNullException("owner");
            }

            _mshOwner = owner as PSObject;
            _memberSetOwner = owner as PSMemberSet;
            if (_mshOwner == null && _memberSetOwner == null)
            {
                throw PSTraceSource.NewArgumentException("owner");
            }

            if (collections == null)
            {
                throw PSTraceSource.NewArgumentNullException("collections");
            }

            Collections = collections;
        }

        #endregion Constructor, fields and properties

        #region overrides

        /// <summary>
        /// Adds member to the collection
        /// </summary>
        /// <param name="member">member to be added</param>
        /// <exception cref="ExtendedTypeSystemException">
        ///     When 
        ///         member is an PSProperty or PSMethod
        ///         adding a member to a MemberSet with a reserved name
        ///         adding a member with a reserved member name or
        ///         adding a member with a type not compatible with this collection
        ///         a member with this name already exists
        ///         trying to add a member to a static memberset
        /// </exception>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public override void Add(T member)
        {
            Add(member, false);
        }

        /// <summary>
        /// Adds member to the collection
        /// </summary>
        /// <param name="member">member to be added</param>
        /// <param name="preValidated">flag to indicate that validation has already been done
        ///     on this new member.  Use only when you can guarantee that the input will not 
        ///     cause any of the errors normally caught by this method.</param>
        /// <exception cref="ExtendedTypeSystemException">
        ///     When 
        ///         member is an PSProperty or PSMethod
        ///         adding a member to a MemberSet with a reserved name
        ///         adding a member with a reserved member name or
        ///         adding a member with a type not compatible with this collection
        ///         a member with this name already exists
        ///         trying to add a member to a static memberset
        /// </exception>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public override void Add(T member, bool preValidated)
        {
            if (member == null)
            {
                throw PSTraceSource.NewArgumentNullException("member");
            }

            if (!preValidated)
            {
                if (member.MemberType == PSMemberTypes.Property || member.MemberType == PSMemberTypes.Method)
                {
                    throw new ExtendedTypeSystemException(
                        "CannotAddMethodOrProperty",
                        null,
                        ExtendedTypeSystem.CannotAddPropertyOrMethod);
                }


                if (_memberSetOwner != null && _memberSetOwner.IsReservedMember)
                {
                    throw new ExtendedTypeSystemException("CannotAddToReservedNameMemberset",
                        null,
                        ExtendedTypeSystem.CannotChangeReservedMember,
                        _memberSetOwner.Name);
                }
            }

            AddToReservedMemberSet(member, preValidated);
        }

        /// <summary>
        /// Auxiliary to add members from types.xml
        /// </summary>
        /// <param name="member"></param>
        /// <param name="preValidated"></param>
        internal void AddToReservedMemberSet(T member, bool preValidated)
        {
            if (!preValidated)
            {
                if (_memberSetOwner != null && !_memberSetOwner.IsInstance)
                {
                    throw new ExtendedTypeSystemException("RemoveMemberFromStaticMemberSet",
                        null,
                        ExtendedTypeSystem.ChangeStaticMember,
                        member.Name);
                }
            }

            AddToTypesXmlCache(member, preValidated);
        }

        /// <summary>
        /// Adds member to the collection
        /// </summary>
        /// <param name="member">member to be added</param>
        /// <param name="preValidated">flag to indicate that validation has already been done
        ///    on this new member.  Use only when you can guarantee that the input will not 
        ///    cause any of the errors normally caught by this method.</param>
        /// <exception cref="ExtendedTypeSystemException">
        ///     When 
        ///         adding a member with a reserved member name or
        ///         adding a member with a type not compatible with this collection
        ///         a member with this name already exists
        ///         trying to add a member to a static memberset
        /// </exception>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        internal void AddToTypesXmlCache(T member, bool preValidated)
        {
            if (member == null)
            {
                throw PSTraceSource.NewArgumentNullException("member");
            }

            if (!preValidated)
            {
                if (IsReservedName(member.Name))
                {
                    throw new ExtendedTypeSystemException("PSObjectMembersMembersAddReservedName",
                        null,
                        ExtendedTypeSystem.ReservedMemberName,
                        member.Name);
                }
            }

            PSMemberInfo memberToBeAdded = member.Copy();

            if (_mshOwner != null)
            {
                if (!preValidated)
                {
                    TypeTable typeTable = _mshOwner.GetTypeTable();
                    if (typeTable != null)
                    {
                        PSMemberInfoInternalCollection<T> typesXmlMembers = typeTable.GetMembers<T>(_mshOwner.InternalTypeNames);
                        if (typesXmlMembers[member.Name] != null)
                        {
                            throw new ExtendedTypeSystemException(
                                "AlreadyPresentInTypesXml",
                                null,
                                ExtendedTypeSystem.MemberAlreadyPresentFromTypesXml,
                                member.Name);
                        }
                    }
                }
                memberToBeAdded.ReplicateInstance(_mshOwner);
                _mshOwner.InstanceMembers.Add(memberToBeAdded, preValidated);

                // All member binders may need to invalidate dynamic sites, and now must generate
                // different binding restrictions (specifically, must check for an instance member
                // before considering the type table or adapted members.)
                PSGetMemberBinder.SetHasInstanceMember(memberToBeAdded.Name);
                PSVariableAssignmentBinder.NoteTypeHasInstanceMemberOrTypeName(PSObject.Base(_mshOwner).GetType());

                return;
            }

            _memberSetOwner.InternalMembers.Add(memberToBeAdded, preValidated);
        }

        /// <summary>
        /// Removes the member named name from the collection
        /// </summary>
        /// <param name="name">Name of the member to be removed</param>
        /// <exception cref="ExtendedTypeSystemException">
        /// When trying to remove a member with a type not compatible with this collection
        /// When trying to remove a member from a static memberset
        /// When trying to remove a member from a MemberSet with a reserved name
        /// </exception>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public override void Remove(string name)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }

            if (_mshOwner != null)
            {
                _mshOwner.InstanceMembers.Remove(name);
                return;
            }

            if (!_memberSetOwner.IsInstance)
            {
                throw new ExtendedTypeSystemException("AddMemberToStaticMemberSet",
                    null,
                    ExtendedTypeSystem.ChangeStaticMember,
                    name);
            }

            if (IsReservedName(_memberSetOwner.Name))
            {
                throw new ExtendedTypeSystemException("CannotRemoveFromReservedNameMemberset",
                    null,
                    ExtendedTypeSystem.CannotChangeReservedMember,
                    _memberSetOwner.Name);
            }

            _memberSetOwner.InternalMembers.Remove(name);
        }


        /// <summary>
        /// Method which checks if the <paramref name="name"/> is reserved and if so
        /// it will ensure that the particular reserved member is loaded into the
        /// objects member collection.
        /// 
        /// Caller should ensure that name is not null or empty.
        /// </summary>
        /// <param name="name">
        /// Name of the member to check and load (if needed).
        /// </param>
        private void EnsureReservedMemberIsLoaded(string name)
        {
            Diagnostics.Assert(!String.IsNullOrEmpty(name),
                "Name cannot be null or empty");

            // Length >= psbase (shortest special member)
            if (name.Length >= 6 && (name[0] == 'p' || name[0] == 'P') && (name[1] == 's' || name[1] == 'S'))
            {
                switch (name.ToLowerInvariant())
                {
                    case PSObject.BaseObjectMemberSetName:
                        ReservedNameMembers.GeneratePSBaseMemberSet(_mshOwner);
                        break;
                    case PSObject.AdaptedMemberSetName:
                        ReservedNameMembers.GeneratePSAdaptedMemberSet(_mshOwner);
                        break;
                    case PSObject.ExtendedMemberSetName:
                        ReservedNameMembers.GeneratePSExtendedMemberSet(_mshOwner);
                        break;
                    case PSObject.PSObjectMemberSetName:
                        ReservedNameMembers.GeneratePSObjectMemberSet(_mshOwner);
                        break;
                    case PSObject.PSTypeNames:
                        ReservedNameMembers.GeneratePSTypeNames(_mshOwner);
                        break;
                    default:
                        break;
                }
            }
        }


        /// <summary>
        /// Returns the name corresponding to name or null if it is not present
        /// </summary>
        /// <param name="name">name of the member to return</param>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public override T this[string name]
        {
            get
            {
                using (PSObject.memberResolution.TraceScope("Lookup"))
                {
                    if (String.IsNullOrEmpty(name))
                    {
                        throw PSTraceSource.NewArgumentException("name");
                    }

                    PSMemberInfo member;
                    object delegateOwner;
                    if (_mshOwner != null)
                    {
                        // this will check if name is a reserved name like PSBase, PSTypeNames
                        // if it is a reserved name, ensures the value is loaded.
                        EnsureReservedMemberIsLoaded(name);
                        delegateOwner = _mshOwner;
                        PSMemberInfoInternalCollection<PSMemberInfo> instanceMembers;
                        if (PSObject.HasInstanceMembers(_mshOwner, out instanceMembers))
                        {
                            member = instanceMembers[name];
                            T memberAsT = member as T;
                            if (memberAsT != null)
                            {
                                PSObject.memberResolution.WriteLine("Found PSObject instance member: {0}.", name);
                                return memberAsT;
                            }
                        }
                    }
                    else
                    {
                        member = _memberSetOwner.InternalMembers[name];
                        delegateOwner = _memberSetOwner.instance;
                        T memberAsT = member as T;
                        if (memberAsT != null)
                        {
                            // In membersets we cannot replicate the instance when adding
                            // since the memberset might not yet have an associated PSObject.
                            // We replicate the instance when returning the members of the memberset.
                            PSObject.memberResolution.WriteLine("Found PSMemberSet member: {0}.", name);
                            member.ReplicateInstance(delegateOwner);
                            return memberAsT;
                        }
                    }

                    if (delegateOwner == null)
                        return null;

                    delegateOwner = PSObject.AsPSObject(delegateOwner);
                    foreach (CollectionEntry<T> collection in Collections)
                    {
                        Diagnostics.Assert(delegateOwner != null, "all integrating collections with non emtpty collections have an associated PSObject");
                        T memberAsT = collection.GetMember((PSObject)delegateOwner, name);
                        if (memberAsT != null)
                        {
                            if (collection.ShouldCloneWhenReturning)
                            {
                                memberAsT = (T)memberAsT.Copy();
                            }
                            if (collection.ShouldReplicateWhenReturning)
                            {
                                memberAsT.ReplicateInstance(delegateOwner);
                            }
                            return memberAsT;
                        }
                    }

                    return null;
                }
            }
        }


        private PSMemberInfoInternalCollection<T> GetIntegratedMembers(MshMemberMatchOptions matchOptions)
        {
            using (PSObject.memberResolution.TraceScope("Generating the total list of members"))
            {
                PSMemberInfoInternalCollection<T> returnValue = new PSMemberInfoInternalCollection<T>();
                object delegateOwner;
                if (_mshOwner != null)
                {
                    delegateOwner = _mshOwner;
                    foreach (PSMemberInfo member in _mshOwner.InstanceMembers)
                    {
                        if (member.MatchesOptions(matchOptions))
                        {
                            T memberAsT = member as T;
                            if (memberAsT != null)
                            {
                                returnValue.Add(memberAsT);
                            }
                        }
                    }
                }
                else
                {
                    delegateOwner = _memberSetOwner.instance;
                    foreach (PSMemberInfo member in _memberSetOwner.InternalMembers)
                    {
                        if (member.MatchesOptions(matchOptions))
                        {
                            T memberAsT = member as T;
                            if (memberAsT != null)
                            {
                                member.ReplicateInstance(delegateOwner);
                                returnValue.Add(memberAsT);
                            }
                        }
                    }
                }

                if (delegateOwner == null)
                    return returnValue;

                delegateOwner = PSObject.AsPSObject(delegateOwner);
                foreach (CollectionEntry<T> collection in Collections)
                {
                    PSMemberInfoInternalCollection<T> members = collection.GetMembers((PSObject)delegateOwner);
                    foreach (T member in members)
                    {
                        PSMemberInfo previousMember = returnValue[member.Name];
                        if (previousMember != null)
                        {
                            PSObject.memberResolution.WriteLine("Member \"{0}\" of type \"{1}\" has been ignored because a member with the same name and type \"{2}\" is already present.",
                                member.Name, member.MemberType, previousMember.MemberType);
                            continue;
                        }
                        if (!member.MatchesOptions(matchOptions))
                        {
                            PSObject.memberResolution.WriteLine("Skipping hidden member \"{0}\".", member.Name);
                            continue;
                        }
                        T memberToAdd;
                        if (collection.ShouldCloneWhenReturning)
                        {
                            memberToAdd = (T)member.Copy();
                        }
                        else
                        {
                            memberToAdd = member;
                        }
                        if (collection.ShouldReplicateWhenReturning)
                        {
                            memberToAdd.ReplicateInstance(delegateOwner);
                        }
                        returnValue.Add(memberToAdd);
                    }
                }
                return returnValue;
            }
        }


        /// <summary>
        /// Returns all members in the collection matching name
        /// </summary>
        /// <param name="name">name of the members to be return. May contain wildcard characters.</param>
        /// <returns>all members in the collection matching name</returns>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public override ReadOnlyPSMemberInfoCollection<T> Match(string name)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            return Match(name, PSMemberTypes.All, MshMemberMatchOptions.None);
        }

        /// <summary>
        /// Returns all members in the collection matching name and types
        /// </summary>
        /// <param name="name">name of the members to be return. May contain wildcard characters.</param>
        /// <param name="memberTypes">type of the members to be searched.</param>
        /// <returns>all members in the collection matching name and types</returns>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        public override ReadOnlyPSMemberInfoCollection<T> Match(string name, PSMemberTypes memberTypes)
        {
            if (String.IsNullOrEmpty(name))
            {
                throw PSTraceSource.NewArgumentException("name");
            }
            return Match(name, memberTypes, MshMemberMatchOptions.None);
        }

        /// <summary>
        /// Returns all members in the collection matching name and types
        /// </summary>
        /// <param name="name">name of the members to be return. May contain wildcard characters.</param>
        /// <param name="memberTypes">type of the members to be searched.</param>
        /// <param name="matchOptions">search options</param>
        /// <returns>all members in the collection matching name and types</returns>
        /// <exception cref="ArgumentException">for invalid arguments</exception>
        internal override ReadOnlyPSMemberInfoCollection<T> Match(string name, PSMemberTypes memberTypes, MshMemberMatchOptions matchOptions)
        {
            using (PSObject.memberResolution.TraceScope("Matching \"{0}\"", name))
            {
                if (String.IsNullOrEmpty(name))
                {
                    throw PSTraceSource.NewArgumentException("name");
                }

                if (_mshOwner != null)
                {
                    GenerateAllReservedMembers();
                }

                WildcardPattern nameMatch = MemberMatch.GetNamePattern(name);
                PSMemberInfoInternalCollection<T> allMembers = GetIntegratedMembers(matchOptions);
                ReadOnlyPSMemberInfoCollection<T> returnValue = new ReadOnlyPSMemberInfoCollection<T>(MemberMatch.Match(allMembers, name, nameMatch, memberTypes));
                PSObject.memberResolution.WriteLine("{0} total matches.", returnValue.Count);
                return returnValue;
            }
        }

        /// <summary>
        /// Gets the specific enumerator for this collection.
        /// This virtual works around the difficulty of implementing
        /// interfaces virtually.
        /// </summary>
        /// <returns>the enumerator for this collection</returns>
        public override IEnumerator<T> GetEnumerator()
        {
            return new Enumerator<T>(this);
        }

        #endregion overrides

        /// <summary>
        /// Enumerable for this class
        /// </summary>
        internal struct Enumerator<S> : IEnumerator<S> where S : PSMemberInfo
        {
            private S _current;
            private int _currentIndex;
            private PSMemberInfoInternalCollection<S> _allMembers;

            /// <summary>
            /// Constructs this instance to enumerate over  members
            /// </summary>
            /// <param name="integratingCollection">members we are enumerating</param>
            internal Enumerator(PSMemberInfoIntegratingCollection<S> integratingCollection)
            {
                using (PSObject.memberResolution.TraceScope("Enumeration Start"))
                {
                    _currentIndex = -1;
                    _current = null;
                    _allMembers = integratingCollection.GetIntegratedMembers(MshMemberMatchOptions.None);
                    if (integratingCollection._mshOwner != null)
                    {
                        integratingCollection.GenerateAllReservedMembers();
                        PSObject.memberResolution.WriteLine("Enumerating PSObject with type \"{0}\".", integratingCollection._mshOwner.ImmediateBaseObject.GetType().FullName);
                        PSObject.memberResolution.WriteLine("PSObject instance members: {0}", _allMembers.VisibleCount);
                    }
                    else
                    {
                        PSObject.memberResolution.WriteLine("Enumerating PSMemberSet \"{0}\".", integratingCollection._memberSetOwner.Name);
                        PSObject.memberResolution.WriteLine("MemberSet instance members: {0}", _allMembers.VisibleCount);
                    }
                }
            }

            /// <summary>
            /// Moves to the next element in the enumeration
            /// </summary>
            /// <returns>
            /// false if there are no more elements to enumerate
            /// true otherwise
            /// </returns>
            public bool MoveNext()
            {
                _currentIndex++;

                S member = null;
                while (_currentIndex < _allMembers.Count)
                {
                    member = _allMembers[_currentIndex];
                    if (!member.IsHidden)
                    {
                        break;
                    }
                    _currentIndex++;
                }

                if (_currentIndex < _allMembers.Count)
                {
                    _current = member;
                    return true;
                }

                _current = null;
                return false;
            }

            /// <summary>
            /// Current PSMemberInfo in the enumeration
            /// </summary>
            /// <exception cref="ArgumentException">for invalid arguments</exception>
            S IEnumerator<S>.Current
            {
                get
                {
                    if (_currentIndex == -1)
                    {
                        throw PSTraceSource.NewInvalidOperationException();
                    }
                    return _current;
                }
            }

            object IEnumerator.Current
            {
                get
                {
                    return ((IEnumerator<S>)this).Current;
                }
            }

            void IEnumerator.Reset()
            {
                _currentIndex = -1;
                _current = null;
            }


            /// <summary>
            /// Not supported
            /// </summary>
            public void Dispose() { }
        }
    }

    #endregion Member collection classes and its auxiliary classes
}

#pragma warning restore 56503

