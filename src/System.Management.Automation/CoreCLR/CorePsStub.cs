#if CORECLR
/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Globalization;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Win32;
using System.Management.Automation.Remoting;

#pragma warning disable 1591, 1572, 1571, 1573, 1587, 1570, 0067

#region CLR_STUBS

// This namespace contains stubs for some .NET types that are not in CoreCLR, such as ISerializable and SerializableAttribute.
// We use the stubs in this namespace to reduce #if/def in the code as much as possible.
namespace Microsoft.PowerShell.CoreClr.Stubs
{
    using System.Runtime.InteropServices;
    // We create some stub attribute types to make some attribute markers work in CoreCLR.
    // The purpose of this is to avoid #if/def in powershell code as much as possible.

    #region Attribute_Related

    /// <summary>
    /// Stub for SpecialNameAttribute
    /// </summary>
    public sealed class SpecialNameAttribute : Attribute
    {
        /// <summary>
        /// 
        /// </summary>
        public SpecialNameAttribute() { }
    }

    /// <summary>
    /// Stub for SerializableAttribute
    /// </summary>
    public sealed class SerializableAttribute : Attribute
    {
        /// <summary>
        /// 
        /// </summary>
        public SerializableAttribute() { }
    }

    /// <summary>
    /// Stub for NonSerializedAttribute
    /// </summary>
    public sealed class NonSerializedAttribute : Attribute
    {
        /// <summary>
        /// 
        /// </summary>
        public NonSerializedAttribute() { }
    }

    /// <summary>
    /// Stub for SecurityAction
    /// </summary>
    public enum SecurityAction
    {
        Assert = 3,
        Demand = 2,
        InheritanceDemand = 7,
        LinkDemand = 6,
        PermitOnly = 5
    }

    /// <summary>
    /// Stub for SecurityPermissionAttribute
    /// </summary>
    public sealed class SecurityPermissionAttribute : Attribute
    {
        public SecurityPermissionAttribute(SecurityAction action) { }
        public bool SerializationFormatter { get; set; }

        public bool UnmanagedCode { get; set; }
    }

    /// <summary>
    /// Stub for TypeLibTypeAttribute
    /// </summary>
    public sealed class TypeLibTypeAttribute : Attribute
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="flags"></param>
        public TypeLibTypeAttribute(short flags) { }
    }

    /// <summary>
    /// Stub for SuppressUnmanagedCodeSecurityAttribute
    /// </summary>
    public class SuppressUnmanagedCodeSecurityAttribute : Attribute
    { }

    /// <summary>
    /// Stub for HostProtectionAttribute
    /// </summary>
    public sealed class HostProtectionAttribute : Attribute
    {
        public HostProtectionAttribute(SecurityAction action) { }
        public bool MayLeakOnAbort { get; set; }
    }

    /// <summary>
    /// Stub for ResourceExposureAttribute
    /// </summary>
    public sealed class ResourceExposureAttribute : Attribute
    {
        private ResourceScope _resourceExposureLevel;

        public ResourceExposureAttribute(ResourceScope exposureLevel)
        {
            _resourceExposureLevel = exposureLevel;
        }

        public ResourceScope ResourceExposureLevel
        {
            get { return _resourceExposureLevel; }
        }
    }

    /// <summary>
    /// Stub for ResourceScope
    /// </summary>
    public enum ResourceScope
    {
        None = 0,
        // Resource type
        Machine = 0x1,
        Process = 0x2,
        AppDomain = 0x4,
        Library = 0x8,
        // Visibility
        Private = 0x10,  // Private to this one class.
        Assembly = 0x20,  // Assembly-level, like C#'s "internal"
    }

    /// <summary>
    /// Stub for ReliabilityContractAttribute
    /// </summary>
    public sealed class ReliabilityContractAttribute : Attribute
    {
        /// <summary>
        /// 
        /// </summary>
        public ReliabilityContractAttribute(Consistency consistencyGuarantee, Cer cer)
        {
        }
    }

    /// <summary>
    /// Stub for Cer
    /// </summary>
    public enum Cer
    {
        /// <summary>
        /// None
        /// </summary>
        None,

        /// <summary>
        /// MayFail
        /// </summary>
        MayFail,

        /// <summary>
        /// Success
        /// </summary>
        Success
    }

    /// <summary>
    /// Stub for Consistency
    /// </summary>
    public enum Consistency
    {
        /// <summary>
        /// MayCorruptProcess
        /// </summary>
        MayCorruptProcess,

        /// <summary>
        /// MayCorruptAppDomain
        /// </summary>
        MayCorruptAppDomain,

        /// <summary>
        /// MayCorruptInstance
        /// </summary>
        MayCorruptInstance,

        /// <summary>
        /// WillNotCorruptState
        /// </summary>
        WillNotCorruptState
    }

    #endregion Attribute_Related

    #region Serialization_Related

    /// <summary>
    /// Stub for SerializationInfo
    /// </summary>
    public sealed class SerializationInfo
    {
        /// <summary>
        /// 
        /// </summary>
        public SerializationInfo() { }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <param name="value"></param>
        public void AddValue(string name, object value)
        {
            throw new NotImplementedException("AddValue(string name, object value)");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <param name="value"></param>
        public void AddValue(string name, bool value)
        {
            throw new NotImplementedException("AddValue(string name, bool value)");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <param name="value"></param>
        /// <param name="type"></param>
        public void AddValue(string name, Object value, Type type)
        {
            throw new NotImplementedException("AddValue(string name, Object value,    Type type)");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <param name="value"></param>
        public void AddValue(string name, int value)
        {
            throw new NotImplementedException("AddValue(string name, int value)");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public object GetValue(string name, Type type)
        {
            throw new NotImplementedException("GetValue(string name, Type type)");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public string GetString(string name)
        {
            throw new NotImplementedException("GetString(string name)");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public bool GetBoolean(string name)
        {
            throw new NotImplementedException("GetBoolean(string name)");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public int GetInt32(string name)
        {
            throw new NotImplementedException("GetInt32(string name)");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="type"></param>
        public void SetType(System.Type type)
        {
            throw new NotImplementedException("SetType(System.Type type)");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public long GetInt64(string name)
        {
            throw new NotImplementedException("GetInt64(string name)");
        }
    }

    #endregion Serialization_Related

    #region Interface_Related

    /// <summary>
    /// Stub for ISerializable
    /// </summary>
    public interface ISerializable
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        void GetObjectData(SerializationInfo info, System.Runtime.Serialization.StreamingContext context);
    }

    /// <summary>
    /// Stub for ICloneable
    /// </summary>
    public interface ICloneable
    {
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        object Clone();
    }

    /// <summary>
    /// Stub for IObjectReference
    /// </summary>
    public interface IObjectReference
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        Object GetRealObject(System.Runtime.Serialization.StreamingContext context);
    }

    /// <summary>
    /// Stub for IRuntimeVariables
    /// </summary>
    public interface IRuntimeVariables
    {
        /// <summary>
        /// 
        /// </summary>
        int Count { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        object this[int index] { get; set; }
    }

    #endregion Interface_Related

    #region Exception_Related

    /// <summary>
    /// Stub for SystemException
    /// </summary>
    public class SystemException : Exception
    {
        /// <summary>
        /// SystemException constructor
        /// </summary>
        public SystemException() : base() { }

        /// <summary>
        /// SystemException constructor
        /// </summary>
        /// <param name="message"></param>
        public SystemException(string message) : base(message) { }

        /// <summary>
        /// SystemException constructor
        /// </summary>
        /// <param name="message"></param>
        /// <param name="innerException"></param>
        public SystemException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        /// SystemException constructor
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        protected SystemException(SerializationInfo info, System.Runtime.Serialization.StreamingContext context) { }

        /// <summary>
        /// SystemException constructor
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        public virtual void GetObjectData(SerializationInfo info, System.Runtime.Serialization.StreamingContext context) { }
    }

    /// <summary>
    /// Stub for AccessViolationException
    /// </summary>
    public class AccessViolationException : Exception
    {
        /// <summary>
        /// AccessViolationException constructor
        /// </summary>
        public AccessViolationException() : base() { }

        /// <summary>
        /// AccessViolationException constructor
        /// </summary>
        /// <param name="message"></param>
        public AccessViolationException(string message) : base(message) { }

        /// <summary>
        /// AccessViolationException constructor
        /// </summary>
        /// <param name="message"></param>
        /// <param name="innerException"></param>
        public AccessViolationException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        /// AccessViolationException constructor
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        protected AccessViolationException(SerializationInfo info, System.Runtime.Serialization.StreamingContext context) { }

        /// <summary>
        /// AccessViolationException constructor
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        public virtual void GetObjectData(SerializationInfo info, System.Runtime.Serialization.StreamingContext context) { }
    }

    /// <summary>
    /// Stub for ThreadAbortException
    /// </summary>
    public sealed class ThreadAbortException : Exception
    {
    }

    /// <summary>
    /// Stub for AppDomainUnloadedException
    /// </summary>
    public sealed class AppDomainUnloadedException : Exception
    {
    }

    #endregion Exception_Related

    #region SafeHandle_Related

    /// <summary>
    /// Stub for SafeHandleZeroOrMinusOneIsInvalid
    /// </summary>
    public abstract class SafeHandleZeroOrMinusOneIsInvalid : SafeHandle
    {
        /// <summary>
        /// Constructor
        /// </summary>
        protected SafeHandleZeroOrMinusOneIsInvalid(bool ownsHandle)
            : base(IntPtr.Zero, ownsHandle)
        {
        }

        /// <summary>
        /// IsInvalid
        /// </summary>
        public override bool IsInvalid
        {
            get
            {
                return handle == IntPtr.Zero || handle == new IntPtr(-1);
            }
        }
    }

    #endregion SafeHandle_Related

    #region Misc_Types

    /// <summary>
    /// Stub for SecurityZone
    /// </summary>
    public enum SecurityZone
    {
        MyComputer = 0,
        Intranet = 1,
        Trusted = 2,
        Internet = 3,
        Untrusted = 4,

        NoZone = -1,
    }

    /// <summary>
    /// Stub for MailAddress
    /// </summary>
    public class MailAddress
    {
        public MailAddress(string address) { }
    }

    #endregion Misc_Types

    #region SystemManagementStubs

    // Summary:
    //     Describes the authentication level to be used to connect to WMI. This is
    //     used for the COM connection to WMI.
    public enum AuthenticationLevel
    {
        // Summary:
        //     Authentication level should remain as it was before.
        Unchanged = -1,
        //
        // Summary:
        //     The default COM authentication level. WMI uses the default Windows Authentication
        //     setting.
        Default = 0,
        //
        // Summary:
        //     No COM authentication.
        None = 1,
        //
        // Summary:
        //     Connect-level COM authentication.
        Connect = 2,
        //
        // Summary:
        //     Call-level COM authentication.
        Call = 3,
        //
        // Summary:
        //     Packet-level COM authentication.
        Packet = 4,
        //
        // Summary:
        //     Packet Integrity-level COM authentication.
        PacketIntegrity = 5,
        //
        // Summary:
        //     Packet Privacy-level COM authentication.
        PacketPrivacy = 6,
    }

    // Summary:
    //     Describes the impersonation level to be used to connect to WMI.
    public enum ImpersonationLevel
    {
        // Summary:
        //     Default impersonation.
        Default = 0,
        //
        // Summary:
        //     Anonymous COM impersonation level that hides the identity of the caller.
        //     Calls to WMI may fail with this impersonation level.
        Anonymous = 1,
        //
        // Summary:
        //     Identify-level COM impersonation level that allows objects to query the credentials
        //     of the caller. Calls to WMI may fail with this impersonation level.
        Identify = 2,
        //
        // Summary:
        //     Impersonate-level COM impersonation level that allows objects to use the
        //     credentials of the caller. This is the recommended impersonation level for
        //     WMI calls.
        Impersonate = 3,
        //
        // Summary:
        //     Delegate-level COM impersonation level that allows objects to permit other
        //     objects to use the credentials of the caller. This level, which will work
        //     with WMI calls but may constitute an unnecessary security risk, is supported
        //     only under Windows 2000.
        Delegate = 4,
    }

    #endregion
}


//TODO:CORECLR Put Stubs to System.Management.Core.dll
namespace System
{
    /// <summary>
    /// TODO:CORECLR Inspection of the binary module needs to be re-write without using AppDomain
    /// </summary>
    internal sealed class AppDomain
    {
        /// <summary>
        /// 
        /// </summary>
        public static System.AppDomain CreateDomain(string friendlyName)
        {
            return null;
        }

        /// <summary>
        /// 
        /// </summary>
        public static void Unload(System.AppDomain domain)
        {
        }
    }
}

namespace System.Net
{
    internal class WebClient //: Component 
    {
        public WebClient() { }

        public void Dispose() { }

        public bool UseDefaultCredentials { get; set; }
    }
}

namespace System.Security
{
    using System.Text;

    /// <summary>
    /// 
    /// </summary>  
    sealed public class SecurityElement
    {
        private static readonly string[] s_escapeStringPairs = new string[]
            {
                // these must be all once character escape sequences or a new escaping algorithm is needed
                "<", "&lt;",
                ">", "&gt;",
                "\"", "&quot;",
                "\'", "&apos;",
                "&", "&amp;"
            };
        private static readonly char[] s_escapeChars = new char[] { '<', '>', '\"', '\'', '&' };

        /// <summary>
        /// Replaces invalid XML characters in a string with their valid XML equivalent.
        /// </summary>  
        public static string Escape(string str)
        {
            if (str == null)
                return null;

            StringBuilder sb = null;

            int strLen = str.Length;
            int index; // Pointer into the string that indicates the location of the current '&' character
            int newIndex = 0; // Pointer into the string that indicates the start index of the "remaining" string (that still needs to be processed).

            do
            {
                index = str.IndexOfAny(s_escapeChars, newIndex);

                if (index == -1)
                {
                    if (sb == null)
                        return str;
                    else
                    {
                        sb.Append(str, newIndex, strLen - newIndex);
                        return sb.ToString();
                    }
                }
                else
                {
                    if (sb == null)
                        sb = new StringBuilder();

                    sb.Append(str, newIndex, index - newIndex);
                    sb.Append(GetEscapeSequence(str[index]));

                    newIndex = (index + 1);
                }
            }
            while (true);
        }

        private static string GetEscapeSequence(char c)
        {
            int iMax = s_escapeStringPairs.Length;

            for (int i = 0; i < iMax; i += 2)
            {
                String strEscSeq = s_escapeStringPairs[i];
                String strEscValue = s_escapeStringPairs[i + 1];

                if (strEscSeq[0] == c)
                    return strEscValue;
            }

            Diagnostics.Debug.Assert(false, "Unable to find escape sequence for this character");
            return c.ToString();
        }
    }
}

#endregion CLR_STUBS

#region PS_STUBS
// Include PS types that are not needed for PowerShell on CSS

namespace System.Management.Automation
{
    #region PSTransaction

    /// <summary>
    /// We don't need PSTransaction related types on CSS because System.Transactions 
    /// namespace is not available in CoreCLR
    /// </summary>
    public sealed class PSTransactionContext : IDisposable
    {
        internal PSTransactionContext(Internal.PSTransactionManager transactionManager) { }
        public void Dispose() { }
    }

    /// <summary>
    /// The severity of error that causes PowerShell to automatically
    /// rollback the transaction.
    /// </summary>
    public enum RollbackSeverity
    {
        /// <summary>
        /// Non-terminating errors or worse
        /// </summary>
        Error,

        /// <summary>
        /// Terminating errors or worse
        /// </summary>
        TerminatingError,

        /// <summary>
        /// Do not rollback the transaction on error
        /// </summary>
        Never
    }

    #endregion PSTransaction

    #region CMS

    internal static class CmsUtils
    {
        internal static string BEGIN_CERTIFICATE_SIGIL = "-----BEGIN CERTIFICATE-----";
        internal static string END_CERTIFICATE_SIGIL = "-----END CERTIFICATE-----";

        internal static string Encrypt(byte[] contentBytes, CmsMessageRecipient[] recipients, SessionState sessionState, out ErrorRecord error)
        {
            throw new NotImplementedException("CmsUtils.Encrypt(...) is not implemented in CoreCLR powershell.");
        }

        internal static string GetAsciiArmor(byte[] bytes)
        {
            throw new NotImplementedException("CmsUtils.GetAsciiArmor(...) is not implemented in CoreCLR powershell.");
        }

        internal static byte[] RemoveAsciiArmor(string actualContent, string beginMarker, string endMarker, out int startIndex, out int endIndex)
        {
            throw new NotImplementedException("CmsUtils.RemoveAsciiArmor(...) is not implemented in CoreCLR powershell.");
        }
    }

    #endregion CMS

    #region ApartmentState

    internal enum ApartmentState
    {
        //
        // Summary:
        //     The System.Threading.Thread will create and enter a single-threaded apartment.
        STA = 0,
        //
        // Summary:
        //     The System.Threading.Thread will create and enter a multithreaded apartment.
        MTA = 1,
        //
        // Summary:
        //     The System.Threading.Thread.ApartmentState property has not been set.
        Unknown = 2
    }

    #endregion ApartmentState
}

namespace System.Management.Automation.Internal
{
    /// <summary>
    /// We don't need PSTransaction related types on CSS because System.Transactions 
    /// namespace is not available in CoreCLR
    /// </summary>
    internal sealed class PSTransactionManager : IDisposable
    {
        /// <summary>
        /// Determines if you have a transaction that you can set active and work on.
        /// </summary>
        /// <remarks>
        /// Always return false in CoreCLR
        /// </remarks>
        internal bool HasTransaction
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Determines if the last transaction has been committed.
        /// </summary>
        internal bool IsLastTransactionCommitted
        {
            get
            {
                throw new NotImplementedException("IsLastTransactionCommitted");
            }
        }

        /// <summary>
        /// Determines if the last transaction has been rolled back.
        /// </summary>
        internal bool IsLastTransactionRolledBack
        {
            get
            {
                throw new NotImplementedException("IsLastTransactionRolledBack");
            }
        }

        /// <summary>
        /// Gets the rollback preference for the active transaction
        /// </summary>
        internal RollbackSeverity RollbackPreference
        {
            get
            {
                throw new NotImplementedException("RollbackPreference");
            }
        }

        /// <summary>
        /// Called by engine APIs to ensure they are protected from
        /// ambient transactions.
        /// </summary>
        /// <remarks>
        /// Always return null in CoreCLR
        /// </remarks>
        internal static IDisposable GetEngineProtectionScope()
        {
            return null;
        }

        /// <summary>
        /// Aborts the current transaction, no matter how many subscribers are part of it.
        /// </summary>
        internal void Rollback(bool suppressErrors)
        {
            throw new NotImplementedException("Rollback");
        }

        public void Dispose() { }
    }
}

namespace System.Management.Automation.ComInterop
{
    using System.Dynamic;
    using System.Diagnostics;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Provides helper methods to bind COM objects dynamically.
    /// </summary>
    /// <remarks>
    /// COM is not supported in core powershell. So this is a stub type.
    /// </remarks>
    internal static class ComBinder
    {
        /// <summary>
        /// Tries to perform binding of the dynamic get index operation.
        /// </summary>
        /// <remarks>
        /// Always return false in CoreCLR.
        /// </remarks>
        public static bool TryBindGetIndex(GetIndexBinder binder, DynamicMetaObject instance, DynamicMetaObject[] args, out DynamicMetaObject result)
        {
            result = null;
            return false;
        }

        /// <summary>
        /// Tries to perform binding of the dynamic set index operation.
        /// </summary>
        /// <remarks>
        /// Always return false in CoreCLR.
        /// </remarks>
        public static bool TryBindSetIndex(SetIndexBinder binder, DynamicMetaObject instance, DynamicMetaObject[] args, DynamicMetaObject value, out DynamicMetaObject result)
        {
            result = null;
            return false;
        }

        /// <summary>
        /// Tries to perform binding of the dynamic get member operation.
        /// </summary>
        /// <remarks>
        /// Always return false in CoreCLR.
        /// </remarks>
        public static bool TryBindGetMember(GetMemberBinder binder, DynamicMetaObject instance, out DynamicMetaObject result)
        {
            result = null;
            return false;
        }

        /// <summary>
        /// Tries to perform binding of the dynamic set member operation.
        /// </summary>
        /// <remarks>
        /// Always return false in CoreCLR.
        /// </remarks>
        public static bool TryBindSetMember(SetMemberBinder binder, DynamicMetaObject instance, DynamicMetaObject value, out DynamicMetaObject result)
        {
            result = null;
            return false;
        }

        /// <summary>
        /// Tries to perform binding of the dynamic invoke member operation.
        /// </summary>
        /// <remarks>
        /// Always return false in CoreCLR.
        /// </remarks>
        public static bool TryBindInvokeMember(InvokeMemberBinder binder, bool isSetProperty, DynamicMetaObject instance, DynamicMetaObject[] args, out DynamicMetaObject result)
        {
            result = null;
            return false;
        }
    }

#pragma warning disable 618 // Disable obsolete warning about VarEnum in CoreCLR
    internal class VarEnumSelector
    {
        private static readonly Dictionary<VarEnum, Type> _ComToManagedPrimitiveTypes = CreateComToManagedPrimitiveTypes();
        
        internal static Type GetTypeForVarEnum(VarEnum vt)
        {
            Type type;

            switch (vt)
            {
                // VarEnums which can be used in VARIANTs, but which cannot occur in a TYPEDESC
                case VarEnum.VT_EMPTY:
                case VarEnum.VT_NULL:
                case VarEnum.VT_RECORD:
                    type = typeof(void);
                    break;

                // VarEnums which are not used in VARIANTs, but which can occur in a TYPEDESC
                case VarEnum.VT_VOID:
                    type = typeof(void);
                    break;

                case VarEnum.VT_HRESULT:
                    type = typeof(int);
                    break;

                case ((VarEnum)37): // VT_INT_PTR:
                    type = typeof(IntPtr);
                    break;

                case ((VarEnum)38): // VT_UINT_PTR:
                    type = typeof(UIntPtr);
                    break;

                case VarEnum.VT_SAFEARRAY:
                case VarEnum.VT_CARRAY:
                    type = typeof(Array);
                    break;

                case VarEnum.VT_LPSTR:
                case VarEnum.VT_LPWSTR:
                    type = typeof(string);
                    break;

                case VarEnum.VT_PTR:
                case VarEnum.VT_USERDEFINED:
                    type = typeof(object);
                    break;

                // For VarEnums that can be used in VARIANTs and well as TYPEDESCs, just use VarEnumSelector
                default:
                    type = VarEnumSelector.GetManagedMarshalType(vt);
                    break;
            }

            return type;
        }

        /// <summary>
        /// Gets the managed type that an object needs to be coverted to in order for it to be able
        /// to be represented as a Variant.
        /// 
        /// In general, there is a many-to-many mapping between Type and VarEnum. However, this method
        /// returns a simple mapping that is needed for the current implementation. The reason for the 
        /// many-to-many relation is:
        /// 1. Int32 maps to VT_I4 as well as VT_ERROR, and Decimal maps to VT_DECIMAL and VT_CY. However,
        ///    this changes if you throw the wrapper types into the mix.
        /// 2. There is no Type to represent COM types. __ComObject is a private type, and Object is too
        ///    general.
        /// </summary>
        internal static Type GetManagedMarshalType(VarEnum varEnum) {
            Debug.Assert((varEnum & VarEnum.VT_BYREF) == 0);

            if (varEnum == VarEnum.VT_CY) {
                return typeof(CurrencyWrapper);
            }

            if (IsPrimitiveType(varEnum)) {
                return _ComToManagedPrimitiveTypes[varEnum];
            }

            switch (varEnum) {
                case VarEnum.VT_EMPTY:
                case VarEnum.VT_NULL:
                case VarEnum.VT_UNKNOWN:
                case VarEnum.VT_DISPATCH:
                case VarEnum.VT_VARIANT:
                    return typeof(Object);

                case VarEnum.VT_ERROR:
                    return typeof(ErrorWrapper);

                default:
                    throw new InvalidOperationException(string.Format(System.Globalization.CultureInfo.CurrentCulture, ParserStrings.UnexpectedVarEnum, varEnum));;
            }
        }

        private static Dictionary<VarEnum, Type> CreateComToManagedPrimitiveTypes()
        {
            Dictionary<VarEnum, Type> dict = new Dictionary<VarEnum, Type>();

            // *** BEGIN GENERATED CODE ***
            // generated by function: gen_ComToManagedPrimitiveTypes from: generate_comdispatch.py

            dict[VarEnum.VT_I1] = typeof(SByte);
            dict[VarEnum.VT_I2] = typeof(Int16);
            dict[VarEnum.VT_I4] = typeof(Int32);
            dict[VarEnum.VT_I8] = typeof(Int64);
            dict[VarEnum.VT_UI1] = typeof(Byte);
            dict[VarEnum.VT_UI2] = typeof(UInt16);
            dict[VarEnum.VT_UI4] = typeof(UInt32);
            dict[VarEnum.VT_UI8] = typeof(UInt64);
            dict[VarEnum.VT_INT] = typeof(Int32);
            dict[VarEnum.VT_UINT] = typeof(UInt32);
            dict[VarEnum.VT_PTR] = typeof(IntPtr);
            dict[VarEnum.VT_BOOL] = typeof(Boolean);
            dict[VarEnum.VT_R4] = typeof(Single);
            dict[VarEnum.VT_R8] = typeof(Double);
            dict[VarEnum.VT_DECIMAL] = typeof(Decimal);
            dict[VarEnum.VT_DATE] = typeof(DateTime);
            dict[VarEnum.VT_BSTR] = typeof(String);
            dict[VarEnum.VT_CLSID] = typeof(Guid);

            // *** END GENERATED CODE ***

            dict[VarEnum.VT_CY] = typeof(CurrencyWrapper);
            dict[VarEnum.VT_ERROR] = typeof(ErrorWrapper);

            return dict;
        }

        /// <summary>
        /// Primitive types are the basic COM types. It includes valuetypes like ints, but also reference types
        /// like BStrs. It does not include composite types like arrays and user-defined COM types (IUnknown/IDispatch).
        /// </summary>
        internal static bool IsPrimitiveType(VarEnum varEnum) {
            switch (varEnum) {

                // *** BEGIN GENERATED CODE ***
                // generated by function: gen_IsPrimitiveType from: generate_comdispatch.py

                case VarEnum.VT_I1:
                case VarEnum.VT_I2:
                case VarEnum.VT_I4:
                case VarEnum.VT_I8:
                case VarEnum.VT_UI1:
                case VarEnum.VT_UI2:
                case VarEnum.VT_UI4:
                case VarEnum.VT_UI8:
                case VarEnum.VT_INT:
                case VarEnum.VT_UINT:
                case VarEnum.VT_BOOL:
                case VarEnum.VT_ERROR:
                case VarEnum.VT_R4:
                case VarEnum.VT_R8:
                case VarEnum.VT_DECIMAL:
                case VarEnum.VT_CY:
                case VarEnum.VT_DATE:
                case VarEnum.VT_BSTR:

                // *** END GENERATED CODE ***
                    
                    return true;
            }

            return false;
        }
    }
#pragma warning restore 618
}

namespace Microsoft.PowerShell.Commands.Internal
{
    using System.Security.AccessControl;
    using System.Security.Principal;

    #region TransactedRegistryKey

    internal abstract class TransactedRegistryKey : IDisposable
    {
        public void Dispose() { }

        public void SetValue(string name, object value)
        {
            throw new NotImplementedException("SetValue(string name, obj value) is not implemented. TransactedRegistry related APIs should not be hitten.");
        }

        public void SetValue(string name, object value, RegistryValueKind valueKind)
        {
            throw new NotImplementedException("SetValue(string name, obj value, RegistryValueKind valueKind) is not implemented. TransactedRegistry related APIs should not be hitten.");
        }
        public string[] GetValueNames()
        {
            throw new NotImplementedException("GetValueNames() is not implemented. TransactedRegistry related APIs should not be hitten.");
        }

        public void DeleteValue(string name)
        {
            throw new NotImplementedException("DeleteValue(string name) is not implemented. TransactedRegistry related APIs should not be hitten.");
        }

        public string[] GetSubKeyNames()
        {
            throw new NotImplementedException("GetSubKeyNames() is not implemented. TransactedRegistry related APIs should not be hitten.");
        }

        public TransactedRegistryKey CreateSubKey(string subkey)
        {
            throw new NotImplementedException("CreateSubKey(string subkey) is not implemented. TransactedRegistry related APIs should not be hitten.");
        }

        public TransactedRegistryKey OpenSubKey(string name, bool writable)
        {
            throw new NotImplementedException("OpenSubKey(string name, bool writeable) is not implemented. TransactedRegistry related APIs should not be hitten.");
        }

        public void DeleteSubKeyTree(string subkey)
        {
            throw new NotImplementedException("DeleteSubKeyTree(string subkey) is not implemented. TransactedRegistry related APIs should not be hitten.");
        }

        public object GetValue(string name)
        {
            throw new NotImplementedException("GetValue(string name) is not implemented. TransactedRegistry related APIs should not be hitten.");
        }

        public object GetValue(string name, object defaultValue, RegistryValueOptions options)
        {
            throw new NotImplementedException("GetValue(string name, object defaultValue, RegistryValueOptions options) is not implemented. TransactedRegistry related APIs should not be hitten.");
        }

        public RegistryValueKind GetValueKind(string name)
        {
            throw new NotImplementedException("GetValueKind(string name) is not implemented. TransactedRegistry related APIs should not be hitten.");
        }

        public void Close()
        {
            throw new NotImplementedException("Close() is not implemented. TransactedRegistry related APIs should not be hitten.");
        }

        public abstract string Name { get; }
        public abstract int SubKeyCount { get; }

        public void SetAccessControl(ObjectSecurity securityDescriptor)
        {
            throw new NotImplementedException("SetAccessControl(ObjectSecurity securityDescriptor) is not implemented. TransactedRegistry related APIs should not be hitten.");
        }

        public ObjectSecurity GetAccessControl(AccessControlSections includeSections)
        {
            throw new NotImplementedException("GetAccessControl(AccessControlSections includeSections) is not implemented. TransactedRegistry related APIs should not be hitten.");
        }
    }

    internal sealed class TransactedRegistry
    {
        internal static readonly TransactedRegistryKey LocalMachine;
        internal static readonly TransactedRegistryKey ClassesRoot;
        internal static readonly TransactedRegistryKey Users;
        internal static readonly TransactedRegistryKey CurrentConfig;
        internal static readonly TransactedRegistryKey CurrentUser;
    }

    internal sealed class TransactedRegistrySecurity : ObjectSecurity
    {
        public override Type AccessRightType
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public override Type AccessRuleType
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public override Type AuditRuleType
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public override AccessRule AccessRuleFactory(IdentityReference identityReference, int accessMask, bool isInherited, InheritanceFlags inheritanceFlags, PropagationFlags propagationFlags, AccessControlType type)
        {
            throw new NotImplementedException();
        }

        public override AuditRule AuditRuleFactory(IdentityReference identityReference, int accessMask, bool isInherited, InheritanceFlags inheritanceFlags, PropagationFlags propagationFlags, AuditFlags flags)
        {
            throw new NotImplementedException();
        }

        protected override bool ModifyAccess(AccessControlModification modification, AccessRule rule, out bool modified)
        {
            throw new NotImplementedException();
        }

        protected override bool ModifyAudit(AccessControlModification modification, AuditRule rule, out bool modified)
        {
            throw new NotImplementedException();
        }
    }

    #endregion TransactedRegistryKey
}

#endregion PS_STUBS

// -- Will port the actual PS component [update: Not necessarily porting all PS components listed here]
#region TEMPORARY

namespace System.Management.Automation.Internal
{
    using Microsoft.PowerShell.Commands;

    /// <summary>
    /// TODO:CORECLR - The actual PowerShellModuleAssemblyAnalyzer cannot be enabled because we don't have 'System.Reflection.Metadata.dll' in our branch yet.
    /// This stub will be removed once we enable the actual 'PowerShellModuleAssemblyAnalyzer'.
    /// </summary>
    internal static class PowerShellModuleAssemblyAnalyzer
    {
        internal static BinaryAnalysisResult AnalyzeModuleAssembly(string path, out Version assemblyVersion)
        {
            assemblyVersion = new Version("0.0.0.0");
            return null;
        }
    }
}

namespace System.Management.Automation
{
    using Microsoft.Win32;

    #region RegistryStringResourceIndirect

    internal sealed class RegistryStringResourceIndirect : IDisposable
    {
        internal static RegistryStringResourceIndirect GetResourceIndirectReader()
        {
            return new RegistryStringResourceIndirect();
        }

        /// <summary>
        /// Dispose method unloads the app domain that was
        /// created in the constructor.
        /// </summary>
        public void Dispose()
        {
        }

        internal string GetResourceStringIndirect(
            string assemblyName,
            string modulePath,
            string baseNameRIDPair)
        {
            throw new NotImplementedException("RegistryStringResourceIndirect.GetResourceStringIndirect - 3 params");
        }

        internal string GetResourceStringIndirect(
            RegistryKey key,
            string valueName,
            string assemblyName,
            string modulePath)
        {
            throw new NotImplementedException("RegistryStringResourceIndirect.GetResourceStringIndirect - 4 params");
        }
    }

    #endregion

    #region Environment 

    internal static partial class Environment
    {
        //TODO:CORECLR Need to be removed once we decide what will be the replacement 
        internal static void Exit(int exitCode)
        {
        }
    }

    #endregion     Environment
}

namespace System.Management.Automation.Security
{
    /// <summary>
    /// We don't need Windows Lockdown Policy related types on CSS because CSS is
    /// amd64 only and is used internally.
    /// </summary>
    internal sealed class SystemPolicy
    {
        private SystemPolicy() { }

        /// <summary>
        /// Gets the system lockdown policy
        /// </summary>
        /// <remarks>Always return SystemEnforcementMode.None in CSS (trusted)</remarks>
        public static SystemEnforcementMode GetSystemLockdownPolicy()
        {
            return SystemEnforcementMode.None;
        }

        /// <summary>
        /// Gets lockdown policy as applied to a file
        /// </summary>
        /// <remarks>Always return SystemEnforcementMode.None in CSS (trusted)</remarks>
        public static SystemEnforcementMode GetLockdownPolicy(string path, System.Runtime.InteropServices.SafeHandle handle)
        {
            return SystemEnforcementMode.None;
        }

        internal static bool IsClassInApprovedList(Guid clsid)
        {
            throw new NotImplementedException("SystemPolicy.IsClassInApprovedList not implemented");
        }
    }

    /// <summary>
    /// How the policy is being enforced
    /// </summary>
    internal enum SystemEnforcementMode
    {
        /// Not enforced at all
        None = 0,

        /// Enabled - allow, but audit
        Audit = 1,

        /// Enabled, enforce restrictions
        Enforce = 2
    }
}


#if UNIX

// Porting note: Tracing is absolutely not available on Linux
namespace System.Management.Automation.Tracing
{
    using System.Management.Automation.Internal;

    /// <summary>
    /// 
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly")]
    public abstract class EtwActivity
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="activityId"></param>
        /// <returns></returns>
        public static bool SetActivityId(Guid activityId)
        {
            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static Guid CreateActivityId()
        {
            return Guid.Empty;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static Guid GetActivityId()
        {
            return Guid.Empty;
        }
    }

    internal static class PSEtwLog
    {
        static internal void LogAnalyticError(PSEventId id, PSOpcode opcode, PSTask task, PSKeyword keyword, params object[] args) { }
        static internal void LogAnalyticWarning(PSEventId id, PSOpcode opcode, PSTask task, PSKeyword keyword, params object[] args) { }
        static internal void LogAnalyticVerbose(PSEventId id, PSOpcode opcode, PSTask task, PSKeyword keyword,
            Int64 objectId,
            Int64 fragmentId,
            int isStartFragment,
            int isEndFragment,
            UInt32 fragmentLength,
            PSETWBinaryBlob fragmentData)
        { }
        static internal void LogAnalyticVerbose(PSEventId id, PSOpcode opcode, PSTask task, PSKeyword keyword, params object[] args) { }
        static internal void SetActivityIdForCurrentThread(Guid newActivityId) { }
        static internal void LogOperationalVerbose(PSEventId id, PSOpcode opcode, PSTask task, PSKeyword keyword, params object[] args) { }
        static internal void LogOperationalWarning(PSEventId id, PSOpcode opcode, PSTask task, PSKeyword keyword, params object[] args) { }
        static internal void ReplaceActivityIdForCurrentThread(Guid newActivityId, PSEventId eventForOperationalChannel, PSEventId eventForAnalyticChannel, PSKeyword keyword, PSTask task) { }
        static internal void LogOperationalError(PSEventId id, PSOpcode opcode, PSTask task, PSKeyword keyword, params object[] args) { }
        static internal void LogOperationalError(PSEventId id, PSOpcode opcode, PSTask task, LogContext logContext, string payLoad) { }
        static internal void LogAnalyticInformational(PSEventId id, PSOpcode opcode, PSTask task, PSKeyword keyword, params object[] args) { }
        static internal void LogOperationalInformation(PSEventId id, PSOpcode opcode, PSTask task, PSKeyword keyword, params object[] args) { }
    }

    public enum PowerShellTraceTask
    {
        /// <summary>
        /// None
        /// </summary>
        None = 0,

        /// <summary>
        /// CreateRunspace
        /// </summary>
        CreateRunspace = 1,

        /// <summary>
        /// ExecuteCommand
        /// </summary>
        ExecuteCommand = 2,

        /// <summary>
        /// Serialization
        /// </summary>
        Serialization = 3,

        /// <summary>
        /// PowerShellConsoleStartup
        /// </summary>
        PowerShellConsoleStartup = 4,
    }

    /// <summary>
    /// Defines Keywords.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1028")]
    [Flags]
    public enum PowerShellTraceKeywords : ulong
    {
        /// <summary>
        /// None
        /// </summary>
        None = 0,

        /// <summary>
        /// Runspace
        /// </summary>
        Runspace = 0x1,

        /// <summary>
        /// Pipeline
        /// </summary>
        Pipeline = 0x2,

        /// <summary>
        /// Protocol
        /// </summary>
        Protocol = 0x4,

        /// <summary>
        /// Transport
        /// </summary>
        Transport = 0x8,

        /// <summary>
        /// Host
        /// </summary>
        Host = 0x10,

        /// <summary>
        /// Cmdlets
        /// </summary>
        Cmdlets = 0x20,

        /// <summary>
        /// Serializer
        /// </summary>
        Serializer = 0x40,

        /// <summary>
        /// Session
        /// </summary>
        Session = 0x80,

        /// <summary>
        /// ManagedPlugIn
        /// </summary>
        ManagedPlugIn = 0x100,

        /// <summary>
        /// 
        /// </summary>
        UseAlwaysDebug = 0x2000000000000000,

        /// <summary>
        /// 
        /// </summary>
        UseAlwaysOperational = 0x8000000000000000,

        /// <summary>
        /// 
        /// </summary>
        UseAlwaysAnalytic = 0x4000000000000000,
    }

    public sealed partial class Tracer : System.Management.Automation.Tracing.EtwActivity
    {
        static Tracer() { }

        public void EndpointRegistered(string endpointName, string endpointType, string registeredBy)
        {
        }

        public void EndpointUnregistered(string endpointName, string unregisteredBy)
        {
        }

        public void EndpointDisabled(string endpointName, string disabledBy)
        {
        }

        public void EndpointEnabled(string endpointName, string enabledBy)
        {
        }

        public void EndpointModified(string endpointName, string modifiedBy)
        {
        }

        public void BeginContainerParentJobExecution(Guid containerParentJobInstanceId)
        {
        }

        public void BeginProxyJobExecution(Guid proxyJobInstanceId)
        {
        }

        public void ProxyJobRemoteJobAssociation(Guid proxyJobInstanceId, Guid containerParentJobInstanceId)
        {
        }

        public void EndProxyJobExecution(Guid proxyJobInstanceId)
        {
        }

        public void BeginProxyJobEventHandler(Guid proxyJobInstanceId)
        {
        }

        public void EndProxyJobEventHandler(Guid proxyJobInstanceId)
        {
        }

        public void BeginProxyChildJobEventHandler(Guid proxyChildJobInstanceId)
        {
        }

        public void EndContainerParentJobExecution(Guid containerParentJobInstanceId)
        {
        }
    }

    public sealed class PowerShellTraceSource : IDisposable
    {
        internal PowerShellTraceSource(PowerShellTraceTask task, PowerShellTraceKeywords keywords)
        {
        }

        public void Dispose()
        {
        }

        public bool WriteMessage(String message)
        {
            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message1"></param>
        /// <param name="message2"></param>
        /// <returns></returns>
        public bool WriteMessage(string message1, string message2)
        {
            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        /// <param name="instanceId"></param>
        /// <returns></returns>
        public bool WriteMessage(string message, Guid instanceId)
        {
            return false;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="className"></param>
        /// <param name="methodName"></param>
        /// <param name="workflowId"></param>
        /// <param name="message"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public void WriteMessage(string className, string methodName, Guid workflowId, string message, params string[] parameters)
        {
            return;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="className"></param>
        /// <param name="methodName"></param>
        /// <param name="workflowId"></param>
        /// <param name="job"></param>
        /// <param name="message"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public void WriteMessage(string className, string methodName, Guid workflowId, Job job, string message, params string[] parameters)
        {
            return;
        }

        public bool TraceException(Exception exception)
        {
            return false;
        }
    }

    /// <summary>
    /// TraceSourceFactory will return an instance of TraceSource every time GetTraceSource method is called.
    /// </summary>
    public static class PowerShellTraceSourceFactory
    {
        /// <summary>
        /// Returns an instance of BaseChannelWriter. 
        /// If the Etw is not supported by the platform it will return NullWriter.Instance
        /// 
        /// A Task and a set of Keywords can be specified in the GetTraceSource method (See overloads).  
        ///    The supplied task and keywords are used to pass to the Etw provider in case they are
        /// not defined in the manifest file.
        /// </summary>
        public static PowerShellTraceSource GetTraceSource()
        {
            return new PowerShellTraceSource(PowerShellTraceTask.None, PowerShellTraceKeywords.None);
        }

        /// <summary>
        /// Returns an instance of BaseChannelWriter. 
        /// If the Etw is not supported by the platform it will return NullWriter.Instance
        /// 
        /// A Task and a set of Keywords can be specified in the GetTraceSource method (See overloads).  
        ///    The supplied task and keywords are used to pass to the Etw provider in case they are
        /// not defined in the manifest file.
        /// </summary>
        public static PowerShellTraceSource GetTraceSource(PowerShellTraceTask task)
        {
            return new PowerShellTraceSource(task, PowerShellTraceKeywords.None);
        }

        /// <summary>
        /// Returns an instance of BaseChannelWriter. 
        /// If the Etw is not supported by the platform it will return NullWriter.Instance
        /// 
        /// A Task and a set of Keywords can be specified in the GetTraceSource method (See overloads).  
        ///    The supplied task and keywords are used to pass to the Etw provider in case they are
        /// not defined in the manifest file.
        /// </summary>
        public static PowerShellTraceSource GetTraceSource(PowerShellTraceTask task, PowerShellTraceKeywords keywords)
        {
            return new PowerShellTraceSource(task, keywords);
        }
    }
}

#endif

namespace Microsoft.PowerShell
{
    internal static class NativeCultureResolver
    {
        internal static void SetThreadUILanguage(Int16 langId) { }

        internal static CultureInfo UICulture
        {
            get
            {
                return CultureInfo.CurrentUICulture; // this is actually wrong, but until we port "hostifaces\NativeCultureResolver.cs" to Nano, this will do and will help avoid build break.
            }
        }

        internal static CultureInfo Culture
        {
            get
            {
                return CultureInfo.CurrentCulture; // this is actually wrong, but until we port "hostifaces\NativeCultureResolver.cs" to Nano, this will do and will help avoid build break.
            }
        }
    }
}

#endregion TEMPORARY



#region Timer
namespace System
{
    // Summary:
    //     Provides data for the event that is raised when there is an exception that
    //     is not handled in any application domain.
    //[Serializable]
    //[ComVisible(true)]
    internal class UnhandledExceptionEventArgs : EventArgs
    {
        // Summary:
        //     Initializes a new instance of the System.UnhandledExceptionEventArgs class
        //     with the exception object and a common language runtime termination flag.
        //PS_STUBS_FOR_REMOTE
        // Parameters:
        //   exception:
        //     The exception that is not handled.
        //
        //   isTerminating:
        //     true if the runtime is terminating; otherwise, false.
        public UnhandledExceptionEventArgs(object exception, bool isTerminating) { }

        // Summary:
        //     Gets the unhandled exception object.
        //
        // Returns:
        //     The unhandled exception object.
        public object ExceptionObject
        {
            get
            {
                return null;
            }
        }

        //
        // Summary:
        //     Indicates whether the common language runtime is terminating.
        //
        // Returns:
        //     true if the runtime is terminating; otherwise, false.
        public bool IsTerminating
        {
            get
            {
                return false;
            }
        }
    }
}

#endregion Timer

#pragma warning restore 1591, 1572, 1571, 1573, 1587, 1570, 0067

#endif
