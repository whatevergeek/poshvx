#pragma warning disable 1634, 1691

/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

#pragma warning disable 1634, 1691
#pragma warning disable 56506

using System;
using System.Management.Automation;
using Dbg = System.Management.Automation;
using System.Management.Automation.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Globalization;
using System.ComponentModel;

#if CORECLR
// Use stub for SystemException
using Microsoft.PowerShell.CoreClr.Stubs;
using System.Reflection;
#endif

namespace Microsoft.PowerShell.Commands
{
    /// <summary>
    /// Defines the base class from which all Security Descriptor commands
    /// are derived.
    /// </summary>
    public abstract class SecurityDescriptorCommandsBase : PSCmdlet
    {
        /// <summary>
        /// Gets or sets the filter property.  The filter
        /// property allows for provider-specific filtering of results.
        /// </summary>
        [Parameter]
        public string Filter
        {
            get
            {
                return _filter;
            }

            set
            {
                _filter = value;
            }
        }

        /// <summary>
        /// Gets or sets the include property.  The include property
        /// specifies the items on which the command will act.
        /// </summary>
        [Parameter]
        public string[] Include
        {
            get
            {
                return _include;
            }

            set
            {
                _include = value;
            }
        }

        /// <summary>
        /// Gets or sets the exclude property.  The exclude property
        /// specifies the items on which the command will not act.
        /// </summary>
        [Parameter]
        public string[] Exclude
        {
            get
            {
                return _exclude;
            }

            set
            {
                _exclude = value;
            }
        }

        /// <summary>
        /// The context for the command that is passed to the core command providers.
        /// </summary>
        internal CmdletProviderContext CmdletProviderContext
        {
            get
            {
                CmdletProviderContext coreCommandContext =
                    new CmdletProviderContext(this);

                Collection<string> includeFilter =
                    SessionStateUtilities.ConvertArrayToCollection<string>(Include);

                Collection<string> excludeFilter =
                    SessionStateUtilities.ConvertArrayToCollection<string>(Exclude);

                coreCommandContext.SetFilters(includeFilter,
                                              excludeFilter,
                                              Filter);

                return coreCommandContext;
            }
        } // CmdletProviderContext


        #region brokered properties

        /// <summary>
        /// Add brokered properties for easy access to important properties
        /// of security descriptor
        /// </summary>
        static internal void AddBrokeredProperties(
            Collection<PSObject> results,
            bool audit,
            bool allCentralAccessPolicies)
        {
            foreach (PSObject result in results)
            {
                if (audit)
                {
                    //Audit
                    result.Properties.Add
                    (
                        new PSCodeProperty
                            (
                                "Audit",
                                typeof(SecurityDescriptorCommandsBase).GetMethod("GetAudit")
                            )
                    );
                }
                //CentralAccessPolicyId retrieval does not require elevation, so we always add this property.
                result.Properties.Add
                (
                    new PSCodeProperty
                        (
                            "CentralAccessPolicyId",
                            typeof(SecurityDescriptorCommandsBase).GetMethod("GetCentralAccessPolicyId")
                        )
                );
#if !CORECLR    //GetAllCentralAccessPolicies and GetCentralAccessPolicyName are not supported in OneCore powershell
                //because function 'LsaQueryCAPs' is not available in OneCoreUAP and NanoServer.
                if (allCentralAccessPolicies)
                {
                    //AllCentralAccessPolicies
                    result.Properties.Add
                    (
                        new PSCodeProperty
                            (
                                "AllCentralAccessPolicies",
                                typeof(SecurityDescriptorCommandsBase).GetMethod("GetAllCentralAccessPolicies")
                            )
                    );
                }
                //CentralAccessPolicyName retrieval does not require elevation, so we always add this property.
                result.Properties.Add
                (
                    new PSCodeProperty
                        (
                            "CentralAccessPolicyName",
                            typeof(SecurityDescriptorCommandsBase).GetMethod("GetCentralAccessPolicyName")
                        )
                );
#endif
            }
        }

        /// <summary>
        /// Gets the Path of the provided PSObject.
        /// </summary>
        /// <param name="instance">
        /// The PSObject for which to obtain the path.
        /// </param>
        /// <returns>
        /// The path of the provided PSObject.
        /// </returns>
        public static string GetPath(PSObject instance)
        {
            if (instance == null)
            {
                throw PSTraceSource.NewArgumentNullException("instance");
            }
            else
            {
                // These are guaranteed to not be null, but even checking
                // them for null causes a presharp warning
#pragma warning disable 56506

                //Get path
                return instance.Properties["PSPath"].Value.ToString();
#pragma warning enable 56506
            }
        }

        /// <summary>
        /// Gets the Owner of the provided PSObject.
        /// </summary>
        /// <param name="instance">
        /// The PSObject for which to obtain the Owner.
        /// </param>
        /// <returns>
        /// The Owner of the provided PSObject.
        /// </returns>
        public static string GetOwner(PSObject instance)
        {
            if (instance == null)
            {
                throw PSTraceSource.NewArgumentNullException("instance");
            }

            ObjectSecurity sd = instance.BaseObject as ObjectSecurity;
            if (sd == null)
            {
                throw PSTraceSource.NewArgumentNullException("instance");
            }

            //Get owner
            try
            {
                IdentityReference ir = sd.GetOwner(typeof(NTAccount));
                return ir.ToString();
            }
            catch (IdentityNotMappedException)
            {
                // All Acl cmdlets returning SIDs will return a string
                // representation of the SID in all cases where the SID
                // cannot be mapped to a proper user or group name.                    
            }

            // We are here since we cannot get IdentityReference from sd..
            // So return sddl..
            return sd.GetSecurityDescriptorSddlForm(AccessControlSections.Owner);
        }

        /// <summary>
        /// Gets the Group of the provided PSObject.
        /// </summary>
        /// <param name="instance">
        /// The PSObject for which to obtain the Group.
        /// </param>
        /// <returns>
        /// The Group of the provided PSObject.
        /// </returns>
        public static string GetGroup(PSObject instance)
        {
            if (instance == null)
            {
                throw PSTraceSource.NewArgumentNullException("instance");
            }

            ObjectSecurity sd = instance.BaseObject as ObjectSecurity;
            if (sd == null)
            {
                throw PSTraceSource.NewArgumentNullException("instance");
            }

            //Get Group
            try
            {
                IdentityReference ir = sd.GetGroup(typeof(NTAccount));
                return ir.ToString();
            }
            catch (IdentityNotMappedException)
            {
                // All Acl cmdlets returning SIDs will return a string
                // representation of the SID in all cases where the SID
                // cannot be mapped to a proper user or group name.
            }

            // We are here since we cannot get IdentityReference from sd..
            // So return sddl..
            return sd.GetSecurityDescriptorSddlForm(AccessControlSections.Group);
        }
        /// <summary>
        /// Gets the access rules of the provided PSObject.
        /// </summary>
        /// <param name="instance">
        /// The PSObject for which to obtain the access rules.
        /// </param>
        /// <returns>
        /// The access rules of the provided PSObject.
        /// </returns>
        public static AuthorizationRuleCollection GetAccess(PSObject instance)
        {
            if (instance == null)
            {
                throw PSTraceSource.NewArgumentNullException("instance");
            }

            ObjectSecurity sd = instance.BaseObject as ObjectSecurity;
            if (sd == null)
            {
                PSTraceSource.NewArgumentException("instance");
            }

            //Get DACL
            AuthorizationRuleCollection dacl;
            CommonObjectSecurity cos = sd as CommonObjectSecurity;
            if (cos != null)
            {
                dacl = cos.GetAccessRules(true, true, typeof(NTAccount));
            }
            else
            {
                DirectoryObjectSecurity dos = sd as DirectoryObjectSecurity;
                Dbg.Diagnostics.Assert(dos != null, "Acl should be of type CommonObjectSecurity or DirectoryObjectSecurity");
                dacl = dos.GetAccessRules(true, true, typeof(NTAccount));
            }
            return dacl;
        }

        /// <summary>
        /// Gets the audit rules of the provided PSObject.
        /// </summary>
        /// <param name="instance">
        /// The PSObject for which to obtain the audit rules.
        /// </param>
        /// <returns>
        /// The audit rules of the provided PSObject.
        /// </returns>
        public static AuthorizationRuleCollection GetAudit(PSObject instance)
        {
            if (instance == null)
            {
                throw PSTraceSource.NewArgumentNullException("instance");
            }

            ObjectSecurity sd = instance.BaseObject as ObjectSecurity;
            if (sd == null)
            {
                PSTraceSource.NewArgumentException("instance");
            }

            AuthorizationRuleCollection sacl;
            CommonObjectSecurity cos = sd as CommonObjectSecurity;
            if (cos != null)
            {
                sacl = cos.GetAuditRules(true, true, typeof(NTAccount));
            }
            else
            {
                DirectoryObjectSecurity dos = sd as DirectoryObjectSecurity;
                Dbg.Diagnostics.Assert(dos != null, "Acl should be of type CommonObjectSecurity or DirectoryObjectSecurity");
                sacl = dos.GetAuditRules(true, true, typeof(NTAccount));
            }
            return sacl;
        }

        /// <summary>
        /// Gets the central access policy ID of the provided PSObject.
        /// </summary>
        /// <param name="instance">
        /// The PSObject for which to obtain the central access policy ID.
        /// </param>
        /// <returns>
        /// The central access policy ID of the provided PSObject.
        /// </returns>
        public static SecurityIdentifier GetCentralAccessPolicyId(PSObject instance)
        {
            SessionState sessionState = new SessionState();
            string path = sessionState.Path.GetUnresolvedProviderPathFromPSPath(
                GetPath(instance));
            IntPtr pOwner = IntPtr.Zero, pGroup = IntPtr.Zero;
            IntPtr pDacl = IntPtr.Zero, pSacl = IntPtr.Zero, pSd = IntPtr.Zero;

            try
            {
                // Get the file's SACL containing the CAPID ACE.
                uint rs = NativeMethods.GetNamedSecurityInfo(
                    path,
                    NativeMethods.SeObjectType.SE_FILE_OBJECT,
                    NativeMethods.SecurityInformation.SCOPE_SECURITY_INFORMATION,
                    out pOwner,
                    out pGroup,
                    out pDacl,
                    out pSacl,
                    out pSd);
                if (rs != NativeMethods.ERROR_SUCCESS)
                {
                    throw new Win32Exception((int)rs);
                }
                if (pSacl == IntPtr.Zero)
                {
                    return null;
                }
                NativeMethods.ACL sacl = new NativeMethods.ACL();
                sacl = ClrFacade.PtrToStructure<NativeMethods.ACL>(pSacl);
                if (sacl.AceCount == 0)
                {
                    return null;
                }

                // Extract the first CAPID from the SACL that does not have INHERIT_ONLY_ACE flag set.
                IntPtr pAce = pSacl + Marshal.SizeOf(new NativeMethods.ACL());
                for (uint aceIdx = 0; aceIdx < sacl.AceCount; aceIdx++)
                {
                    NativeMethods.ACE_HEADER ace = new NativeMethods.ACE_HEADER();
                    ace = ClrFacade.PtrToStructure<NativeMethods.ACE_HEADER>(pAce);
                    Dbg.Diagnostics.Assert(ace.AceType ==
                        NativeMethods.SYSTEM_SCOPED_POLICY_ID_ACE_TYPE,
                        "Unexpected ACE type: " + ace.AceType.ToString(CultureInfo.CurrentCulture));
                    if ((ace.AceFlags & NativeMethods.INHERIT_ONLY_ACE) == 0)
                    {
                        break;
                    }
                    pAce += ace.AceSize;
                }
                IntPtr pSid = pAce + Marshal.SizeOf(new NativeMethods.SYSTEM_AUDIT_ACE()) -
                    Marshal.SizeOf(new uint());
                bool ret = NativeMethods.IsValidSid(pSid);
                if (!ret)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                return new SecurityIdentifier(pSid);
            }
            finally
            {
                NativeMethods.LocalFree(pSd);
            }
        }

#if !CORECLR
        /// <summary>
        /// Gets the central access policy name of the provided PSObject.
        /// </summary>
        /// <remarks>
        /// Function 'LsaQueryCAPs' is not available in OneCoreUAP and NanoServer.
        /// </remarks>
        /// <param name="instance">
        /// The PSObject for which to obtain the central access policy name.
        /// </param>
        /// <returns>
        /// The central access policy name of the provided PSObject.
        /// </returns>
        public static string GetCentralAccessPolicyName(PSObject instance)
        {
            SecurityIdentifier capId = GetCentralAccessPolicyId(instance);
            if (capId == null)
            {
                return null; // file does not have the scope ace
            }
            int capIdSize = capId.BinaryLength;
            byte[] capIdArray = new byte[capIdSize];
            capId.GetBinaryForm(capIdArray, 0);
            IntPtr caps = IntPtr.Zero;
            IntPtr pCapId = Marshal.AllocHGlobal(capIdSize);

            try
            {
                // Retrieve the CAP by CAPID.
                Marshal.Copy(capIdArray, 0, pCapId, capIdSize);
                IntPtr[] ppCapId = new IntPtr[1];
                ppCapId[0] = pCapId;
                uint capCount = 0;
                uint rs = NativeMethods.LsaQueryCAPs(
                    ppCapId,
                    1,
                    out caps,
                    out capCount);
                if (rs != NativeMethods.STATUS_SUCCESS)
                {
                    throw new Win32Exception((int)rs);
                }
                if (capCount == 0 || caps == IntPtr.Zero)
                {
                    return null;
                }

                // Get the CAP name.
                NativeMethods.CENTRAL_ACCESS_POLICY cap = new NativeMethods.CENTRAL_ACCESS_POLICY();
                cap = ClrFacade.PtrToStructure<NativeMethods.CENTRAL_ACCESS_POLICY>(caps);
                // LSA_UNICODE_STRING is composed of WCHARs, but its length is given in bytes.
                return Marshal.PtrToStringUni(cap.Name.Buffer, cap.Name.Length / 2);
            }
            finally
            {
                Marshal.FreeHGlobal(pCapId);
                uint rs = NativeMethods.LsaFreeMemory(caps);
                Dbg.Diagnostics.Assert(rs == NativeMethods.STATUS_SUCCESS,
                    "LsaFreeMemory failed: " + rs.ToString(CultureInfo.CurrentCulture));
            }
        }

        /// <summary>
        /// Gets the names and IDs of all central access policies available on the machine.
        /// </summary>
        /// <remarks>
        /// Function 'LsaQueryCAPs' is not available in OneCoreUAP and NanoServer.
        /// </remarks>
        /// <param name="instance">
        /// The PSObject argument is ignored.
        /// </param>
        /// <returns>
        /// The names and IDs of all central access policies available on the machine.
        /// </returns>
        public static string[] GetAllCentralAccessPolicies(PSObject instance)
        {
            IntPtr caps = IntPtr.Zero;

            try
            {
                // Retrieve all CAPs.
                uint capCount = 0;
                uint rs = NativeMethods.LsaQueryCAPs(
                    null,
                    0,
                    out caps,
                    out capCount);
                if (rs != NativeMethods.STATUS_SUCCESS)
                {
                    throw new Win32Exception((int)rs);
                }
                Dbg.Diagnostics.Assert(capCount < 0xFFFF,
                    "Too many central access policies");
                if (capCount == 0 || caps == IntPtr.Zero)
                {
                    return null;
                }

                // Add CAP names and IDs to a string array.
                string[] policies = new string[capCount];
                NativeMethods.CENTRAL_ACCESS_POLICY cap = new NativeMethods.CENTRAL_ACCESS_POLICY();
                IntPtr capPtr = caps;
                for (uint capIdx = 0; capIdx < capCount; capIdx++)
                {
                    // Retrieve CAP name.
                    Dbg.Diagnostics.Assert(capPtr != IntPtr.Zero,
                        "Invalid central access policies array");
                    cap = ClrFacade.PtrToStructure<NativeMethods.CENTRAL_ACCESS_POLICY>(capPtr);
                    // LSA_UNICODE_STRING is composed of WCHARs, but its length is given in bytes.
                    policies[capIdx] = "\"" + Marshal.PtrToStringUni(
                        cap.Name.Buffer,
                        cap.Name.Length / 2) + "\"";

                    // Retrieve CAPID.
                    IntPtr pCapId = cap.CAPID;
                    Dbg.Diagnostics.Assert(pCapId != IntPtr.Zero,
                        "Invalid central access policies array");
                    bool ret = NativeMethods.IsValidSid(pCapId);
                    if (!ret)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                    SecurityIdentifier sid = new SecurityIdentifier(pCapId);
                    policies[capIdx] += " (" + sid.ToString() + ")";

                    capPtr += Marshal.SizeOf(cap);
                }
                return policies;
            }
            finally
            {
                uint rs = NativeMethods.LsaFreeMemory(caps);
                Dbg.Diagnostics.Assert(rs == NativeMethods.STATUS_SUCCESS,
                    "LsaFreeMemory failed: " + rs.ToString(CultureInfo.CurrentCulture));
            }
        }
#endif

        /// <summary>
        /// Gets the security descriptor (in SDDL form) of the 
        /// provided PSObject.  SDDL form is the Security Descriptor
        /// Definition Language.
        /// </summary>
        /// <param name="instance">
        /// The PSObject for which to obtain the security descriptor.
        /// </param>
        /// <returns>
        /// The security descriptor of the provided PSObject, in SDDL form.
        /// </returns>
        public static string GetSddl(PSObject instance)
        {
            if (instance == null)
            {
                throw PSTraceSource.NewArgumentNullException("instance");
            }

            ObjectSecurity sd = instance.BaseObject as ObjectSecurity;
            if (sd == null)
            {
                throw PSTraceSource.NewArgumentNullException("instance");
            }

            string sddl = sd.GetSecurityDescriptorSddlForm(AccessControlSections.All);
            return sddl;
        }

        #endregion brokered properties

        /// <summary>
        /// The filter to be used to when globbing to get the item.
        /// </summary>
        private string _filter;

        /// <summary>
        /// The glob string used to determine which items are included.
        /// </summary>
        private string[] _include = new string[0];

        /// <summary>
        /// The glob string used to determine which items are excluded.
        /// </summary>
        private string[] _exclude = new string[0];
    }

    /// <summary>
    /// Defines the implementation of the 'get-acl' cmdlet.
    /// This cmdlet gets the security descriptor of an item at the specified path.  
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "Acl", SupportsTransactions = true, DefaultParameterSetName = "ByPath", HelpUri = "http://go.microsoft.com/fwlink/?LinkID=113305")]
    public sealed class GetAclCommand : SecurityDescriptorCommandsBase
    {
        /// <summary>
        /// Initializes a new instance of the GetAclCommand
        /// class.  Sets the default path to the current location.
        /// </summary>
        public GetAclCommand()
        {
            //Default for path is the current location
            _path = new string[] { "." };
        }
        #region parameters

        private string[] _path;

        /// <summary>
        /// Gets or sets the path of the item for which to obtain the
        /// security descriptor.  Default is the current location.
        /// </summary>
        [Parameter(Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true, ParameterSetName = "ByPath")]
        [ValidateNotNullOrEmpty()]
        public string[] Path
        {
            get
            {
                return _path;
            }

            set
            {
                _path = value;
            }
        }

        private PSObject _inputObject = null;

        /// <summary>
        /// InputObject Parameter
        /// Gets or sets the inputObject for which to obtain the security descriptor
        /// </summary>
        [Parameter(Mandatory = true, ParameterSetName = "ByInputObject")]
        public PSObject InputObject
        {
            get
            {
                return _inputObject;
            }

            set
            {
                _inputObject = value;
            }
        }

        /// <summary>
        /// Gets or sets the literal path of the item for which to obtain the
        /// security descriptor.  Default is the current location.
        /// </summary>
        [Parameter(ValueFromPipeline = true, ValueFromPipelineByPropertyName = true, ParameterSetName = "ByLiteralPath")]
        [Alias("PSPath")]
        [ValidateNotNullOrEmpty()]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public string[] LiteralPath
        {
            get
            {
                return _path;
            }

            set
            {
                _path = value;
                _isLiteralPath = true;
            }
        }
        private bool _isLiteralPath = false;

        /// <summary>
        /// Gets or sets the audit flag of the command.  This flag 
        /// determines if audit rules should also be retrieved.
        /// </summary>
        [Parameter()]
        public SwitchParameter Audit
        {
            get
            {
                return _audit;
            }

            set
            {
                _audit = value;
            }
        }
        private SwitchParameter _audit;

#if CORECLR
        /// <summary>
        /// Parameter '-AllCentralAccessPolicies' is not supported in OneCore powershell, 
        /// because function 'LsaQueryCAPs' is not available in OneCoreUAP and NanoServer.
        /// </summary>
        private SwitchParameter AllCentralAccessPolicies
        {
            get; set;
        }
#else
        /// <summary>
        /// Gets or sets the AllCentralAccessPolicies flag of the command. This flag 
        /// determines whether the information about all central access policies 
        /// available on the machine should be displayed.
        /// </summary>
        [Parameter()]
        public SwitchParameter AllCentralAccessPolicies
        {
            get
            {
                return allCentralAccessPolicies;
            }

            set
            {
                allCentralAccessPolicies = value;
            }
        }
        private SwitchParameter allCentralAccessPolicies;
#endif

        #endregion

        /// <summary>
        /// Processes records from the input pipeline.
        /// For each input file, the command retrieves its
        /// corresponding security descriptor.
        /// </summary>
        protected override void ProcessRecord()
        {
            Collection<PSObject> sd = null;
            AccessControlSections sections =
                AccessControlSections.Owner |
                AccessControlSections.Group |
                AccessControlSections.Access;
            if (_audit)
            {
                sections |= AccessControlSections.Audit;
            }

            if (_inputObject != null)
            {
                PSMethodInfo methodInfo = _inputObject.Methods["GetSecurityDescriptor"];

                if (methodInfo != null)
                {
                    object customDescriptor = null;

                    try
                    {
                        customDescriptor = PSObject.Base(methodInfo.Invoke());

                        if (!(customDescriptor is FileSystemSecurity))
                        {
                            customDescriptor = new CommonSecurityDescriptor(false, false, customDescriptor.ToString());
                        }
                    }
                    catch (Exception e)
                    {
                        // Calling user code, Catch-all OK
                        CommandProcessorBase.CheckForSevereException(e);

                        ErrorRecord er =
                        SecurityUtils.CreateNotSupportedErrorRecord(
                            UtilsStrings.MethodInvokeFail,
                            "GetAcl_OperationNotSupported"
                            );

                        WriteError(er);
                        return;
                    }

                    WriteObject(customDescriptor, true);
                }
                else
                {
                    ErrorRecord er =
                        SecurityUtils.CreateNotSupportedErrorRecord(
                            UtilsStrings.GetMethodNotFound,
                            "GetAcl_OperationNotSupported"
                            );

                    WriteError(er);
                }
            }

            else
            {
                foreach (string p in Path)
                {
                    List<string> pathsToProcess = new List<string>();

                    string currentPath = null;
                    try
                    {
                        if (_isLiteralPath)
                        {
                            pathsToProcess.Add(SessionState.Path.GetUnresolvedProviderPathFromPSPath(p));
                        }
                        else
                        {
                            Collection<PathInfo> resolvedPaths =
                                SessionState.Path.GetResolvedPSPathFromPSPath(p, CmdletProviderContext);
                            foreach (PathInfo pi in resolvedPaths)
                            {
                                pathsToProcess.Add(pi.Path);
                            }
                        }

                        foreach (string rp in pathsToProcess)
                        {
                            currentPath = rp;

                            CmdletProviderContext context = new CmdletProviderContext(this.Context);
                            context.SuppressWildcardExpansion = true;

                            if (!InvokeProvider.Item.Exists(rp, false, _isLiteralPath))
                            {
                                ErrorRecord er =
                                    SecurityUtils.CreatePathNotFoundErrorRecord(
                                               rp,
                                               "GetAcl_PathNotFound"
                                    );

                                WriteError(er);
                                continue;
                            }

                            InvokeProvider.SecurityDescriptor.Get(rp, sections, context);

                            sd = context.GetAccumulatedObjects();
                            if (sd != null)
                            {
                                AddBrokeredProperties(
                                    sd,
                                    _audit,
                                    AllCentralAccessPolicies);
                                WriteObject(sd, true);
                            }
                        }
                    }
                    catch (NotSupportedException)
                    {
                        ErrorRecord er =
                            SecurityUtils.CreateNotSupportedErrorRecord(
                                UtilsStrings.OperationNotSupportedOnPath,
                                "GetAcl_OperationNotSupported",
                                currentPath
                            );

                        WriteError(er);
                    }
                    catch (ItemNotFoundException)
                    {
                        ErrorRecord er =
                            SecurityUtils.CreatePathNotFoundErrorRecord(
                                p,
                                "GetAcl_PathNotFound_Exception"
                            );

                        WriteError(er);
                        continue;
                    }
                }
            }
        }
    } // class GetAclCommand : PSCmdlet

    /// <summary>
    /// Defines the implementation of the 'set-acl' cmdlet.
    /// This cmdlet sets the security descriptor of an item at the specified path.  
    /// </summary>
    [Cmdlet(VerbsCommon.Set, "Acl", SupportsShouldProcess = true, SupportsTransactions = true, DefaultParameterSetName = "ByPath",
            HelpUri = "http://go.microsoft.com/fwlink/?LinkID=113389")]
    public sealed class SetAclCommand : SecurityDescriptorCommandsBase
    {
        private string[] _path;

        /// <summary>
        /// Gets or sets the path of the item for which to set the
        /// security descriptor.
        /// </summary>
        [Parameter(Position = 0, Mandatory = true, ValueFromPipelineByPropertyName = true, ParameterSetName = "ByPath")]
        public string[] Path
        {
            get
            {
                return _path;
            }

            set
            {
                _path = value;
            }
        }

        private PSObject _inputObject = null;

        /// <summary>
        /// InputObject Parameter
        /// Gets or sets the inputObject for which to set the security descriptor
        /// </summary>
        [Parameter(Position = 0, Mandatory = true, ValueFromPipelineByPropertyName = true, ParameterSetName = "ByInputObject")]
        public PSObject InputObject
        {
            get
            {
                return _inputObject;
            }

            set
            {
                _inputObject = value;
            }
        }

        /// <summary>
        /// Gets or sets the literal path of the item for which to set the
        /// security descriptor.
        /// </summary>
        [Parameter(Mandatory = true, ValueFromPipelineByPropertyName = true, ParameterSetName = "ByLiteralPath")]
        [Alias("PSPath")]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public string[] LiteralPath
        {
            get
            {
                return _path;
            }

            set
            {
                _path = value;
                _isLiteralPath = true;
            }
        }
        private bool _isLiteralPath = false;

        private object _securityDescriptor;

        /// <summary>
        /// Gets or sets the security descriptor object to be
        /// set on the target item(s).
        /// </summary>
        [Parameter(Position = 1, Mandatory = true, ValueFromPipeline = true, ParameterSetName = "ByPath")]
        [Parameter(Position = 1, Mandatory = true, ValueFromPipeline = true, ParameterSetName = "ByLiteralPath")]
        [Parameter(Position = 1, Mandatory = true, ValueFromPipeline = true, ParameterSetName = "ByInputObject")]
        public object AclObject
        {
            get
            {
                return _securityDescriptor;
            }

            set
            {
                _securityDescriptor = PSObject.Base(value);
            }
        }

#if CORECLR
        /// <summary>
        /// Parameter '-CentralAccessPolicy' is not supported in OneCore powershell, 
        /// because function 'LsaQueryCAPs' is not available in OneCoreUAP and NanoServer.
        /// </summary>
        private string CentralAccessPolicy
        {
            get; set;
        }
#else
        private string centralAccessPolicy;

        /// <summary>
        /// Gets or sets the central access policy to be
        /// set on the target item(s).
        /// </summary>
        [Parameter(Position = 2, Mandatory = false, ValueFromPipelineByPropertyName = true, ParameterSetName = "ByPath")]
        [Parameter(Position = 2, Mandatory = false, ValueFromPipelineByPropertyName = true, ParameterSetName = "ByLiteralPath")]
        public string CentralAccessPolicy
        {
            get
            {
                return centralAccessPolicy;
            }

            set
            {
                centralAccessPolicy = value;
            }
        }
#endif

        private SwitchParameter _clearCentralAccessPolicy;

        /// <summary>
        /// Clears the central access policy applied on the target item(s).
        /// </summary>
        [Parameter(Mandatory = false, ParameterSetName = "ByPath")]
        [Parameter(Mandatory = false, ParameterSetName = "ByLiteralPath")]
        public SwitchParameter ClearCentralAccessPolicy
        {
            get
            {
                return _clearCentralAccessPolicy;
            }

            set
            {
                _clearCentralAccessPolicy = value;
            }
        }

        private SwitchParameter _passthru;

        /// <summary>
        /// Gets or sets the Passthru flag for the operation.
        /// If true, the security descriptor is also passed
        /// down the output pipeline.
        /// </summary>
        [Parameter()]
        public SwitchParameter Passthru
        {
            get
            {
                return _passthru;
            }

            set
            {
                _passthru = value;
            }
        }

        /// <summary>
        /// Returns a newly allocated SACL with no ACEs in it.
        /// Free the returned SACL by calling Marshal.FreeHGlobal.
        /// </summary>
        private IntPtr GetEmptySacl()
        {
            IntPtr pSacl = IntPtr.Zero;
            bool ret = true;

            try
            {
                // Calculate the size of the empty SACL, align to DWORD.
                uint saclSize = (uint)(Marshal.SizeOf(new NativeMethods.ACL()) +
                    Marshal.SizeOf(new uint()) - 1) & 0xFFFFFFFC;
                Dbg.Diagnostics.Assert(saclSize < 0xFFFF,
                    "Acl size must be less than max SD size of 0xFFFF");

                // Allocate and initialize the SACL.
                pSacl = Marshal.AllocHGlobal((int)saclSize);
                ret = NativeMethods.InitializeAcl(
                    pSacl,
                    saclSize,
                    NativeMethods.ACL_REVISION);
                if (!ret)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                if (!ret)
                {
                    Marshal.FreeHGlobal(pSacl);
                    pSacl = IntPtr.Zero;
                }
            }

            return pSacl;
        }

        /// <summary>
        /// Returns a newly allocated SACL with the supplied CAPID in it.
        /// Free the returned SACL by calling Marshal.FreeHGlobal.
        /// </summary>
        /// <remarks>
        /// Function 'LsaQueryCAPs' is not available in OneCoreUAP and NanoServer.
        /// So the parameter "-CentralAccessPolicy" is not supported on OneCore powershell, 
        /// and thus this method won't be hit in OneCore powershell.
        /// </remarks>
        private IntPtr GetSaclWithCapId(string capStr)
        {
            IntPtr pCapId = IntPtr.Zero, pSacl = IntPtr.Zero;
            IntPtr caps = IntPtr.Zero;
            bool ret = true, freeCapId = true;
            uint rs = NativeMethods.STATUS_SUCCESS;

            try
            {
                // Convert the supplied SID from string to binary form.
                ret = NativeMethods.ConvertStringSidToSid(capStr, out pCapId);
                if (!ret)
                {
                    // We may have got a CAP friendly name instead of CAPID.
                    // Enumerate all CAPs on the system and try to find one with 
                    // a matching friendly name.
                    // If we retrieve the CAPID from the LSA, the CAPID need not 
                    // be deallocated separately (but with the entire buffer 
                    // returned by LsaQueryCAPs).
                    freeCapId = false;
                    uint capCount = 0;
                    rs = NativeMethods.LsaQueryCAPs(
                        null,
                        0,
                        out caps,
                        out capCount);
                    if (rs != NativeMethods.STATUS_SUCCESS)
                    {
                        throw new Win32Exception((int)rs);
                    }
                    Dbg.Diagnostics.Assert(capCount < 0xFFFF,
                        "Too many central access policies");
                    if (capCount == 0 || caps == IntPtr.Zero)
                    {
                        return IntPtr.Zero;
                    }

                    // Find the supplied string among available CAP names, use the corresponding CAPID.
                    NativeMethods.CENTRAL_ACCESS_POLICY cap = new NativeMethods.CENTRAL_ACCESS_POLICY();
                    IntPtr capPtr = caps;
                    for (uint capIdx = 0; capIdx < capCount; capIdx++)
                    {
                        Dbg.Diagnostics.Assert(capPtr != IntPtr.Zero,
                            "Invalid central access policies array");
                        cap = ClrFacade.PtrToStructure<NativeMethods.CENTRAL_ACCESS_POLICY>(capPtr);
                        // LSA_UNICODE_STRING is composed of WCHARs, but its length is given in bytes.
                        string capName = Marshal.PtrToStringUni(
                            cap.Name.Buffer,
                            cap.Name.Length / 2);
                        if (capName.Equals(capStr, StringComparison.OrdinalIgnoreCase))
                        {
                            pCapId = cap.CAPID;
                            break;
                        }
                        capPtr += Marshal.SizeOf(cap);
                    }
                }
                if (pCapId == IntPtr.Zero)
                {
                    Exception e = new ArgumentException(UtilsStrings.InvalidCentralAccessPolicyIdentifier);
                    WriteError(new ErrorRecord(
                        e,
                        "SetAcl_CentralAccessPolicy",
                        ErrorCategory.InvalidArgument,
                        AclObject));
                    return IntPtr.Zero;
                }
                ret = NativeMethods.IsValidSid(pCapId);
                if (!ret)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                uint sidSize = NativeMethods.GetLengthSid(pCapId);

                // Calculate the size of the SACL with one CAPID ACE, align to DWORD.
                uint saclSize = (uint)(Marshal.SizeOf(new NativeMethods.ACL()) +
                    Marshal.SizeOf(new NativeMethods.SYSTEM_AUDIT_ACE()) +
                    sidSize - 1) & 0xFFFFFFFC;
                Dbg.Diagnostics.Assert(saclSize < 0xFFFF,
                    "Acl size must be less than max SD size of 0xFFFF");

                // Allocate and initialize the SACL.
                pSacl = Marshal.AllocHGlobal((int)saclSize);
                ret = NativeMethods.InitializeAcl(
                    pSacl,
                    saclSize,
                    NativeMethods.ACL_REVISION);
                if (!ret)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                // Add CAPID to the SACL.
                rs = NativeMethods.RtlAddScopedPolicyIDAce(
                    pSacl,
                    NativeMethods.ACL_REVISION,
                    NativeMethods.SUB_CONTAINERS_AND_OBJECTS_INHERIT,
                    0,
                    pCapId);
                if (rs != NativeMethods.STATUS_SUCCESS)
                {
                    if (rs == NativeMethods.STATUS_INVALID_PARAMETER)
                    {
                        throw new ArgumentException(UtilsStrings.InvalidCentralAccessPolicyIdentifier);
                    }
                    else
                    {
                        throw new Win32Exception((int)rs);
                    }
                }
            }
            finally
            {
                if (!ret || rs != NativeMethods.STATUS_SUCCESS)
                {
                    Marshal.FreeHGlobal(pSacl);
                    pSacl = IntPtr.Zero;
                }
                rs = NativeMethods.LsaFreeMemory(caps);
                Dbg.Diagnostics.Assert(rs == NativeMethods.STATUS_SUCCESS,
                    "LsaFreeMemory failed: " + rs.ToString(CultureInfo.CurrentCulture));
                if (freeCapId)
                {
                    NativeMethods.LocalFree(pCapId);
                }
            }

            return pSacl;
        }

        /// <summary>
        /// Returns the current thread or process token with the specified privilege enabled 
        /// and the previous state of this privilege. Free the returned token 
        /// by calling NativeMethods.CloseHandle.
        /// </summary>
        private IntPtr GetTokenWithEnabledPrivilege(
            string privilege,
            NativeMethods.TOKEN_PRIVILEGE previousState)
        {
            IntPtr pToken = IntPtr.Zero;
            bool ret = true;

            try
            {
                // First try to open the thread token for privilege adjustment.
                ret = NativeMethods.OpenThreadToken(
                    NativeMethods.GetCurrentThread(),
                    NativeMethods.TOKEN_QUERY | NativeMethods.TOKEN_ADJUST_PRIVILEGES,
                    true,
                    out pToken);

                if (!ret)
                {
                    if (Marshal.GetLastWin32Error() == NativeMethods.ERROR_NO_TOKEN)
                    {
                        // Client is not impersonating. Open the process token.
                        ret = NativeMethods.OpenProcessToken(
                            NativeMethods.GetCurrentProcess(),
                            NativeMethods.TOKEN_QUERY | NativeMethods.TOKEN_ADJUST_PRIVILEGES,
                            out pToken);
                    }

                    if (!ret)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }

                // Get the LUID of the specified privilege.
                NativeMethods.LUID luid = new NativeMethods.LUID();
                ret = NativeMethods.LookupPrivilegeValue(
                    null,
                    privilege,
                    ref luid);
                if (!ret)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                // Enable the privilege.
                NativeMethods.TOKEN_PRIVILEGE newState = new NativeMethods.TOKEN_PRIVILEGE();
                newState.PrivilegeCount = 1;
                newState.Privilege.Attributes = NativeMethods.SE_PRIVILEGE_ENABLED;
                newState.Privilege.Luid = luid;
                uint previousSize = 0;
                ret = NativeMethods.AdjustTokenPrivileges(
                    pToken,
                    false,
                    ref newState,
                    (uint)Marshal.SizeOf(previousState),
                    ref previousState,
                    ref previousSize);
                if (!ret)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                if (!ret)
                {
                    NativeMethods.CloseHandle(pToken);
                    pToken = IntPtr.Zero;
                }
            }

            return pToken;
        }

        /// Processes records from the input pipeline.
        /// For each input file, the command sets its
        /// security descriptor to the specified 
        /// Access Control List (ACL).
        protected override void ProcessRecord()
        {
            ObjectSecurity aclObjectSecurity = _securityDescriptor as ObjectSecurity;

            if (_inputObject != null)
            {
                PSMethodInfo methodInfo = _inputObject.Methods["SetSecurityDescriptor"];

                if (methodInfo != null)
                {
                    CommonSecurityDescriptor aclCommonSD = _securityDescriptor as CommonSecurityDescriptor;
                    string sddl;

                    if (aclObjectSecurity != null)
                    {
                        sddl = aclObjectSecurity.GetSecurityDescriptorSddlForm(AccessControlSections.All);
                    }
                    else if (aclCommonSD != null)
                    {
                        sddl = aclCommonSD.GetSddlForm(AccessControlSections.All);
                    }
                    else
                    {
                        Exception e = new ArgumentException("AclObject");
                        WriteError(new ErrorRecord(
                            e,
                            "SetAcl_AclObject",
                            ErrorCategory.InvalidArgument,
                            AclObject));
                        return;
                    }

                    try
                    {
                        methodInfo.Invoke(sddl);
                    }
                    catch (Exception e)
                    {
                        // Calling user code, Catch-all OK
                        CommandProcessorBase.CheckForSevereException(e);

                        ErrorRecord er =
                        SecurityUtils.CreateNotSupportedErrorRecord(
                            UtilsStrings.MethodInvokeFail,
                            "SetAcl_OperationNotSupported"
                            );

                        WriteError(er);
                        return;
                    }
                }
                else
                {
                    ErrorRecord er =
                        SecurityUtils.CreateNotSupportedErrorRecord(
                            UtilsStrings.SetMethodNotFound,
                            "SetAcl_OperationNotSupported"
                            );

                    WriteError(er);
                }
            }
            else
            {
                if (Path == null)
                {
                    Exception e = new ArgumentException("Path");
                    WriteError(new ErrorRecord(
                        e,
                        "SetAcl_Path",
                        ErrorCategory.InvalidArgument,
                        AclObject));
                    return;
                }

                if (aclObjectSecurity == null)
                {
                    Exception e = new ArgumentException("AclObject");
                    WriteError(new ErrorRecord(
                        e,
                        "SetAcl_AclObject",
                        ErrorCategory.InvalidArgument,
                        AclObject));
                    return;
                }

                if (CentralAccessPolicy != null || ClearCentralAccessPolicy)
                {
                    if (!DownLevelHelper.IsWin8AndAbove())
                    {
                        Exception e = new ParameterBindingException();
                        WriteError(new ErrorRecord(
                            e,
                            "SetAcl_OperationNotSupported",
                            ErrorCategory.InvalidArgument,
                            null));
                        return;
                    }
                }

                if (CentralAccessPolicy != null && ClearCentralAccessPolicy)
                {
                    Exception e = new ArgumentException(UtilsStrings.InvalidCentralAccessPolicyParameters);
                    ErrorRecord er =
                    SecurityUtils.CreateInvalidArgumentErrorRecord(
                        e,
                        "SetAcl_OperationNotSupported"
                        );

                    WriteError(er);
                    return;
                }

                IntPtr pSacl = IntPtr.Zero;
                NativeMethods.TOKEN_PRIVILEGE previousState = new NativeMethods.TOKEN_PRIVILEGE();
                try
                {
                    if (CentralAccessPolicy != null)
                    {
                        pSacl = GetSaclWithCapId(CentralAccessPolicy);
                        if (pSacl == IntPtr.Zero)
                        {
                            SystemException e = new SystemException(
                                UtilsStrings.GetSaclWithCapIdFail);
                            WriteError(new ErrorRecord(e,
                                                        "SetAcl_CentralAccessPolicy",
                                                        ErrorCategory.InvalidResult,
                                                        null));
                            return;
                        }
                    }
                    else if (ClearCentralAccessPolicy)
                    {
                        pSacl = GetEmptySacl();
                        if (pSacl == IntPtr.Zero)
                        {
                            SystemException e = new SystemException(
                                UtilsStrings.GetEmptySaclFail);
                            WriteError(new ErrorRecord(e,
                                                        "SetAcl_ClearCentralAccessPolicy",
                                                        ErrorCategory.InvalidResult,
                                                        null));
                            return;
                        }
                    }

                    foreach (string p in Path)
                    {
                        Collection<PathInfo> pathsToProcess = new Collection<PathInfo>();

                        CmdletProviderContext context = this.CmdletProviderContext;
                        context.PassThru = Passthru;
                        if (_isLiteralPath)
                        {
                            ProviderInfo Provider = null;
                            PSDriveInfo Drive = null;
                            string pathStr = SessionState.Path.GetUnresolvedProviderPathFromPSPath(p, out Provider, out Drive);
                            pathsToProcess.Add(new PathInfo(Drive, Provider, pathStr, SessionState));
                            context.SuppressWildcardExpansion = true;
                        }
                        else
                        {
                            pathsToProcess = SessionState.Path.GetResolvedPSPathFromPSPath(p, CmdletProviderContext);
                        }

                        foreach (PathInfo pathInfo in pathsToProcess)
                        {
                            if (ShouldProcess(pathInfo.Path))
                            {
                                try
                                {
                                    InvokeProvider.SecurityDescriptor.Set(pathInfo.Path,
                                                                          aclObjectSecurity,
                                                                          context);

                                    if (CentralAccessPolicy != null || ClearCentralAccessPolicy)
                                    {
                                        if (!pathInfo.Provider.NameEquals(Context.ProviderNames.FileSystem))
                                        {
                                            Exception e = new ArgumentException("Path");
                                            WriteError(new ErrorRecord(
                                                e,
                                                "SetAcl_Path",
                                                ErrorCategory.InvalidArgument,
                                                AclObject));
                                            continue;
                                        }

                                        // Enable the security privilege required to set SCOPE_SECURITY_INFORMATION.
                                        IntPtr pToken = GetTokenWithEnabledPrivilege("SeSecurityPrivilege", previousState);
                                        if (pToken == IntPtr.Zero)
                                        {
                                            SystemException e = new SystemException(
                                                UtilsStrings.GetTokenWithEnabledPrivilegeFail);
                                            WriteError(new ErrorRecord(e,
                                                                        "SetAcl_AdjustTokenPrivileges",
                                                                        ErrorCategory.InvalidResult,
                                                                        null));
                                            return;
                                        }

                                        // Set the file's CAPID.
                                        uint rs = NativeMethods.SetNamedSecurityInfo(
                                            pathInfo.ProviderPath,
                                            NativeMethods.SeObjectType.SE_FILE_OBJECT,
                                            NativeMethods.SecurityInformation.SCOPE_SECURITY_INFORMATION,
                                            IntPtr.Zero,
                                            IntPtr.Zero,
                                            IntPtr.Zero,
                                            pSacl);

                                        // Restore privileges to the previous state.
                                        if (pToken != IntPtr.Zero)
                                        {
                                            NativeMethods.TOKEN_PRIVILEGE newState = new NativeMethods.TOKEN_PRIVILEGE();
                                            uint newSize = 0;
                                            NativeMethods.AdjustTokenPrivileges(
                                                pToken,
                                                false,
                                                ref previousState,
                                                (uint)Marshal.SizeOf(newState),
                                                ref newState,
                                                ref newSize);

                                            NativeMethods.CloseHandle(pToken);
                                            pToken = IntPtr.Zero;
                                        }

                                        if (rs != NativeMethods.ERROR_SUCCESS)
                                        {
                                            Exception e = new Win32Exception(
                                                (int)rs,
                                                UtilsStrings.SetCentralAccessPolicyFail);
                                            WriteError(new ErrorRecord(e,
                                                                       "SetAcl_SetNamedSecurityInfo",
                                                                       ErrorCategory.InvalidResult,
                                                                       null));
                                        }
                                    }
                                }
                                catch (NotSupportedException)
                                {
                                    ErrorRecord er =
                                        SecurityUtils.CreateNotSupportedErrorRecord(
                                            UtilsStrings.OperationNotSupportedOnPath,
                                            "SetAcl_OperationNotSupported",
                                            pathInfo.Path
                                        );

                                    WriteError(er);
                                }
                            }
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pSacl);
                }
            }
        }
    } // class SetAclCommand
}// namespace Microsoft.PowerShell.Commands

#pragma warning restore 56506

