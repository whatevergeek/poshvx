/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

#pragma warning disable 1634, 1691
#pragma warning disable 56523

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Microsoft.PowerShell;
using Microsoft.PowerShell.Commands;
using System.Management.Automation.Security;
using System.Management.Automation.Internal;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Globalization;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

using Microsoft.Win32;

using DWORD = System.UInt32;
using BOOL = System.UInt32;

namespace Microsoft.PowerShell
{
    /// <summary>
    /// Defines the different Execution Policies supported by the
    /// PSAuthorizationManager class.
    /// 
    /// </summary>
    public enum ExecutionPolicy
    {
        /// Unrestricted - No files must be signed.  If a file originates from the
        ///    internet, Monad provides a warning prompt to alert the user.  To
        ///    suppress this warning message, right-click on the file in File Explorer,
        ///    select "Properties," and then "Unblock."
        Unrestricted = 0,

        /// RemoteSigned - Only .msh and .mshxml files originating from the internet
        ///    must be digitally signed.  If remote, signed, and executed, Monad 
        ///    prompts to determine if files from the signing publisher should be 
        ///    run or not.  This is the default setting.
        RemoteSigned = 1,

        /// AllSigned - All .msh and .mshxml files must be digitally signed.  If
        ///    signed and executed, Monad prompts to determine if files from the 
        ///    signing publisher should be run or not.
        AllSigned = 2,

        /// Restricted - All .msh files are blocked.  Mshxml files must be digitally 
        ///    signed, and by a trusted publisher.  If you haven't made a trust decision
        ///    on the publisher yet, prompting is done as in AllSigned mode.
        Restricted = 3,

        /// Bypass - No files must be signed, and internet origin is not verified
        Bypass = 4,

        /// Undefined - Not specified at this scope
        Undefined = 5,

        /// <summary>
        /// Default - The most restrictive policy available.
        /// </summary>
        Default = Restricted
    };

    /// <summary>
    /// Defines the available configuration scopes for an execution
    /// policy. They are in the following priority, with successive
    /// elements overriding the items that precede them:
    /// LocalMachine -> CurrentUser -> Runspace
    /// 
    /// </summary>
    public enum ExecutionPolicyScope
    {
        /// Execution policy is retrieved from the
        /// PSExecutionPolicyPreference environment variable.
        Process = 0,

        /// Execution policy is retrieved from the HKEY_CURRENT_USER
        /// registry hive for the current ShellId.
        CurrentUser = 1,

        /// Execution policy is retrieved from the HKEY_LOCAL_MACHINE
        /// registry hive for the current ShellId.
        LocalMachine = 2,

        /// Execution policy is retrieved from the current user's
        /// group policy setting.
        UserPolicy = 3,

        /// Execution policy is retrieved from the machine-wide
        /// group policy setting.
        MachinePolicy = 4
    }
}

namespace System.Management.Automation.Internal
{
    /// <summary>
    /// The SAFER policy associated with this file
    /// 
    /// </summary>
    internal enum SaferPolicy
    {
        /// Explicitly allowed through an Allow rule
        ExplicitlyAllowed = 0,

        /// Allowed because it has not been explicitly disallowed
        Allowed = 1,

        /// Disallowed by a rule or policy.
        Disallowed = 2
    }

    /// <summary>
    /// Security Support APIs
    /// </summary>
    public static class SecuritySupport
    {
        #region execution policy

        internal static ExecutionPolicyScope[] ExecutionPolicyScopePreferences
        {
            get
            {
                return new ExecutionPolicyScope[] {
                        ExecutionPolicyScope.MachinePolicy,
                        ExecutionPolicyScope.UserPolicy,
                        ExecutionPolicyScope.Process,
                        ExecutionPolicyScope.CurrentUser,
                        ExecutionPolicyScope.LocalMachine
                    };
            }
        }

        internal static void SetExecutionPolicy(ExecutionPolicyScope scope, ExecutionPolicy policy, string shellId)
        {
#if UNIX
            throw new PlatformNotSupportedException();
#else
            string executionPolicy = "Restricted";
            string preferenceKey = Utils.GetRegistryConfigurationPath(shellId);

            switch (policy)
            {
                case ExecutionPolicy.Restricted:
                    executionPolicy = "Restricted"; break;
                case ExecutionPolicy.AllSigned:
                    executionPolicy = "AllSigned"; break;
                case ExecutionPolicy.RemoteSigned:
                    executionPolicy = "RemoteSigned"; break;
                case ExecutionPolicy.Unrestricted:
                    executionPolicy = "Unrestricted"; break;
                case ExecutionPolicy.Bypass:
                    executionPolicy = "Bypass"; break;
            }

            // Set the execution policy
            switch (scope)
            {
                case ExecutionPolicyScope.Process:
                {
                    if (policy == ExecutionPolicy.Undefined)
                        executionPolicy = null;

                    Environment.SetEnvironmentVariable("PSExecutionPolicyPreference", executionPolicy);
                    break;
                }

                case ExecutionPolicyScope.CurrentUser:
                {
                    // They want to remove it
                    if (policy == ExecutionPolicy.Undefined)
                    {
                        ConfigPropertyAccessor.Instance.RemoveExecutionPolicy(ConfigPropertyAccessor.PropertyScope.CurrentUser, shellId);
                        CleanKeyParents(Registry.CurrentUser, preferenceKey);
                    }
                    else
                    {
                        ConfigPropertyAccessor.Instance.SetExecutionPolicy(ConfigPropertyAccessor.PropertyScope.CurrentUser, shellId, executionPolicy);
                    }
                    break;
                }

                case ExecutionPolicyScope.LocalMachine:
                {
                    // They want to remove it
                    if (policy == ExecutionPolicy.Undefined)
                    {
                        ConfigPropertyAccessor.Instance.RemoveExecutionPolicy(ConfigPropertyAccessor.PropertyScope.SystemWide, shellId);
                        CleanKeyParents(Registry.LocalMachine, preferenceKey);
                    }
                    else
                    {
                        ConfigPropertyAccessor.Instance.SetExecutionPolicy(ConfigPropertyAccessor.PropertyScope.SystemWide, shellId, executionPolicy);
                    }
                    break;
                }
            }
#endif
        }

        // Clean up the parents of a registry key as long as they
        // contain at most a single child
        private static void CleanKeyParents(RegistryKey baseKey, string keyPath)
        {
#if CORECLR
            if (!Platform.IsInbox)
                return;
#endif
            using (RegistryKey key = baseKey.OpenSubKey(keyPath, true))
            {
                // Verify the child key has no children
                if ((key == null) || ((key.ValueCount == 0) && (key.SubKeyCount == 0)))
                {
                    // If so, split the key into its path elements
                    string[] parentKeys = keyPath.Split(Utils.Separators.Backslash);

                    // Verify we aren't traveling into SOFTWARE\Microsoft
                    if (parentKeys.Length <= 2)
                        return;

                    string currentItem = parentKeys[parentKeys.Length - 1];

                    // Figure out the parent key name
                    string parentKeyPath = keyPath.Remove(keyPath.Length - currentItem.Length - 1);

                    // Open the parent, and clear the child key
                    if (key != null)
                    {
                        using (RegistryKey parentKey = baseKey.OpenSubKey(parentKeyPath, true))
                        {
                            parentKey.DeleteSubKey(currentItem, true);
                        }
                    }

                    // Now, process the parent key
                    CleanKeyParents(baseKey, parentKeyPath);
                }
                else
                {
                    return;
                }
            }
        }

        internal static ExecutionPolicy GetExecutionPolicy(string shellId)
        {
            foreach (ExecutionPolicyScope scope in ExecutionPolicyScopePreferences)
            {
                ExecutionPolicy policy = GetExecutionPolicy(shellId, scope);
                if (policy != ExecutionPolicy.Undefined)
                    return policy;
            }

            return ExecutionPolicy.Restricted;
        }

        internal static ExecutionPolicy GetExecutionPolicy(string shellId, ExecutionPolicyScope scope)
        {
#if UNIX
            return ExecutionPolicy.Unrestricted;
#else
            switch (scope)
            {
                case ExecutionPolicyScope.Process:
                    {
                        string policy = Environment.GetEnvironmentVariable("PSExecutionPolicyPreference");

                        if (!String.IsNullOrEmpty(policy))
                            return ParseExecutionPolicy(policy);
                        else
                            return ExecutionPolicy.Undefined;
                    }

                case ExecutionPolicyScope.CurrentUser:
                case ExecutionPolicyScope.LocalMachine:
                    {
                        string policy = GetLocalPreferenceValue(shellId, scope);

                        if (!String.IsNullOrEmpty(policy))
                            return ParseExecutionPolicy(policy);
                        else
                            return ExecutionPolicy.Undefined;
                    }

                // TODO: Group Policy is only supported on Full systems, but !LINUX && CORECLR 
                // will run there as well, so I don't think we should remove it.
                case ExecutionPolicyScope.UserPolicy:
                case ExecutionPolicyScope.MachinePolicy:
                    {
                        string groupPolicyPreference = GetGroupPolicyValue(shellId, scope);
                        if (!String.IsNullOrEmpty(groupPolicyPreference))
                        {
                            // Be sure we aren't being called by Group Policy
                            // itself. A group policy should never block a logon /
                            // logoff script.
                            Process currentProcess = Process.GetCurrentProcess();
                            string gpScriptPath = IO.Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.System),
                                "gpscript.exe");
                            bool foundGpScriptParent = false;

                            try
                            {
                                while (currentProcess != null)
                                {
                                    if (String.Equals(gpScriptPath,
                                            PsUtils.GetMainModule(currentProcess).FileName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        foundGpScriptParent = true;
                                        break;
                                    }
                                    else
                                    {
                                        currentProcess = PsUtils.GetParentProcess(currentProcess);
                                    }
                                }
                            }
                            catch (System.ComponentModel.Win32Exception)
                            {
                                // If you attempt to retrieve the MainModule of a 64-bit process
                                // from a WOW64 (32-bit) process, the Win32 API has a fatal
                                // flaw that causes this to return the error:
                                //   "Only part of a ReadProcessMemory or WriteProcessMemory
                                //   request was completed."
                                // In this case, we just catch the exception and eat it.
                                // The implication is that logon / logoff scripts that somehow
                                // launch the Wow64 version of PowerShell will be subject
                                // to the execution policy deployed by Group Policy (where
                                // our goal here is to not have the Group Policy execution policy
                                // affect logon / logoff scripts.
                            }

                            if (!foundGpScriptParent)
                            {
                                return ParseExecutionPolicy(groupPolicyPreference);
                            }
                        }

                        return ExecutionPolicy.Undefined;
                    }
            }

            return ExecutionPolicy.Restricted;
#endif
        }

        internal static ExecutionPolicy ParseExecutionPolicy(string policy)
        {
            if (String.Equals(policy, "Bypass",
                                   StringComparison.OrdinalIgnoreCase))
            {
                return ExecutionPolicy.Bypass;
            }
            else if (String.Equals(policy, "Unrestricted",
                                   StringComparison.OrdinalIgnoreCase))
            {
                return ExecutionPolicy.Unrestricted;
            }
            else if (String.Equals(policy, "RemoteSigned",
                                   StringComparison.OrdinalIgnoreCase))
            {
                return ExecutionPolicy.RemoteSigned;
            }
            else if (String.Equals(policy, "AllSigned",
                              StringComparison.OrdinalIgnoreCase))
            {
                return ExecutionPolicy.AllSigned;
            }
            else if (String.Equals(policy, "Restricted",
                         StringComparison.OrdinalIgnoreCase))
            {
                return ExecutionPolicy.Restricted;
            }
            else
            {
                return ExecutionPolicy.Default;
            }
        }

        internal static string GetExecutionPolicy(ExecutionPolicy policy)
        {
            switch (policy)
            {
                case ExecutionPolicy.Bypass: return "Bypass";
                case ExecutionPolicy.Unrestricted: return "Unrestricted";
                case ExecutionPolicy.RemoteSigned: return "RemoteSigned";
                case ExecutionPolicy.AllSigned: return "AllSigned";
                case ExecutionPolicy.Restricted: return "Restricted";
                default: return "Restricted";
            }
        }

        /// <summary>
        /// Returns true if file has product binary signature
        /// </summary>
        /// <param name="file">Name of file to check</param>
        /// <returns>True when file has product binary signature</returns>
        public static bool IsProductBinary(string file)
        {
            if (String.IsNullOrEmpty(file) || (!IO.File.Exists(file)))
            {
                return false;
            }

            // Check if it is in the product folder, if not, skip checking the catalog
            // and any other checks.
            var isUnderProductFolder = Utils.IsUnderProductFolder(file);
            if (!isUnderProductFolder)
            {
                return false;
            }

#if UNIX
            // There is no signature support on non-Windows platforms (yet), when
            // execution reaches here, we are sure the file is under product folder
            return true;
#else
            // Check the file signature
            Signature fileSignature = SignatureHelper.GetSignature(file, null);
            if ((fileSignature != null) && (fileSignature.IsOSBinary))
            {
                return true;
            }

            // WTGetSignatureInfo is used to verify catalog signature.
            // On Win7, catalog API is not available.
            // On OneCore SKUs like NanoServer/IoT, the API has a bug that makes it not able to find the
            // corresponding catalog file for a given product file, so it doesn't work properly.
            // In these cases, we just trust the 'isUnderProductFolder' check.
            if (Signature.CatalogApiAvailable.HasValue && !Signature.CatalogApiAvailable.Value)
            {
                // When execution reaches here, we are sure the file is under product folder
                return true;
            }

            return false;
#endif
        }

#if !CORECLR
        /// <summary>
        /// Get the pass / fail result of calling the SAFER API
        /// </summary>
        ///
        /// <param name="path">The path to the file in question</param>
        /// <param name="handle">A file handle to the file in question, if available.</param>
        [ArchitectureSensitive]
        [SuppressMessage("Microsoft.Reliability", "CA2001:AvoidCallingProblematicMethods")]
        internal static SaferPolicy GetSaferPolicy(string path, SafeHandle handle)
        {
            SaferPolicy status = SaferPolicy.Allowed;

            SAFER_CODE_PROPERTIES codeProperties = new SAFER_CODE_PROPERTIES();
            IntPtr hAuthzLevel;

            // Prepare the code properties struct.
            codeProperties.cbSize = (uint)Marshal.SizeOf(typeof(SAFER_CODE_PROPERTIES));
            codeProperties.dwCheckFlags = (
                NativeConstants.SAFER_CRITERIA_IMAGEPATH |
                NativeConstants.SAFER_CRITERIA_IMAGEHASH |
                NativeConstants.SAFER_CRITERIA_AUTHENTICODE);
            codeProperties.ImagePath = path;

            if (handle != null)
            {
                codeProperties.hImageFileHandle = handle.DangerousGetHandle();
            }

            // turn off WinVerifyTrust UI
            codeProperties.dwWVTUIChoice = NativeConstants.WTD_UI_NONE;

            // Identify the level associated with the code
            if (NativeMethods.SaferIdentifyLevel(1, ref codeProperties, out hAuthzLevel, NativeConstants.SRP_POLICY_SCRIPT))
            {
                // We found an Authorization Level applicable to this application.
                IntPtr hRestrictedToken = IntPtr.Zero;
                try
                {
                    if (!NativeMethods.SaferComputeTokenFromLevel(
                                               hAuthzLevel,                    // Safer Level
                                               IntPtr.Zero,                    // Test current process' token
                                               ref hRestrictedToken,           // target token
                                               NativeConstants.SAFER_TOKEN_NULL_IF_EQUAL,
                                               IntPtr.Zero))
                    {
                        int lastError = Marshal.GetLastWin32Error();
                        if ((lastError == NativeConstants.ERROR_ACCESS_DISABLED_BY_POLICY) ||
                            (lastError == NativeConstants.ERROR_ACCESS_DISABLED_NO_SAFER_UI_BY_POLICY))
                        {
                            status = SaferPolicy.Disallowed;
                        }
                        else
                        {
                            throw new System.ComponentModel.Win32Exception();
                        }
                    }
                    else
                    {
                        if (hRestrictedToken == IntPtr.Zero)
                        {
                            // This is not necessarily the "fully trusted" level, 
                            // it means that the thread token is complies with the requested level
                            status = SaferPolicy.Allowed;
                        }
                        else
                        {
                            status = SaferPolicy.Disallowed;
                            NativeMethods.CloseHandle(hRestrictedToken);
                        }
                    }
                }
                finally
                {
                    NativeMethods.SaferCloseLevel(hAuthzLevel);
                }
            }
            else
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            return status;
        }
#endif
        /// <summary>
        /// Returns the value of the Execution Policy as retrieved
        /// from group policy.
        /// </summary>
        /// <returns>NULL if it is not defined at this level</returns>
        private static string GetGroupPolicyValue(string shellId, ExecutionPolicyScope scope)
        {
            RegistryKey[] scopeKey = null;

            switch (scope)
            {
                case ExecutionPolicyScope.MachinePolicy:
                    {
                        scopeKey = Utils.RegLocalMachine;
                    }; break;

                case ExecutionPolicyScope.UserPolicy:
                    {
                        scopeKey = Utils.RegCurrentUser;
                    }; break;
            }

            Dictionary<string, object> groupPolicySettings = Utils.GetGroupPolicySetting(".", scopeKey);
            if (groupPolicySettings == null)
            {
                return null;
            }

            Object enableScriptsValue = null;
            if (groupPolicySettings.TryGetValue("EnableScripts", out enableScriptsValue))
            {
                if (String.Equals(enableScriptsValue.ToString(), "0", StringComparison.OrdinalIgnoreCase))
                {
                    return "Restricted";
                }
                else if (String.Equals(enableScriptsValue.ToString(), "1", StringComparison.OrdinalIgnoreCase))
                {
                    Object executionPolicyValue = null;
                    if (groupPolicySettings.TryGetValue("ExecutionPolicy", out executionPolicyValue))
                    {
                        return executionPolicyValue.ToString();
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the value of the Execution Policy as retrieved
        /// from the local preference.
        /// </summary>
        /// <returns>NULL if it is not defined at this level</returns>
        private static string GetLocalPreferenceValue(string shellId, ExecutionPolicyScope scope)
        {
            switch (scope)
            {
                // 1: Look up the current-user preference
                case ExecutionPolicyScope.CurrentUser:
                    return ConfigPropertyAccessor.Instance.GetExecutionPolicy(ConfigPropertyAccessor.PropertyScope.CurrentUser, shellId);

                // 2: Look up the system-wide preference
                case ExecutionPolicyScope.LocalMachine:
                    return ConfigPropertyAccessor.Instance.GetExecutionPolicy(ConfigPropertyAccessor.PropertyScope.SystemWide, shellId);
            }

            return null;
        }

#endregion execution policy

        /// <summary>
        /// throw if file does not exist
        /// </summary>
        ///
        /// <param name="filePath"> path to file </param>
        ///
        /// <returns> Does not return a value </returns>
        ///
        /// <remarks>  </remarks>
        ///
        internal static void CheckIfFileExists(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(filePath);
            }
        }

        /// <summary>
        /// check to see if the specified cert is suitable to be
        /// used as a code signing cert
        /// </summary>
        ///
        /// <param name="c"> certificate object </param>
        ///
        /// <returns> true on success, false otherwise </returns>
        ///
        /// <remarks>  </remarks>
        ///
        internal static bool CertIsGoodForSigning(X509Certificate2 c)
        {
            if (!CertHasPrivatekey(c))
            {
                return false;
            }

            return CertHasOid(c, CertificateFilterInfo.CodeSigningOid);
        }

        /// <summary>
        /// check to see if the specified cert is suitable to be
        /// used as an encryption cert for PKI encryption. Note
        /// that this cert doesn't require the private key.
        /// </summary>
        ///
        /// <param name="c"> certificate object </param>
        ///
        /// <returns> true on success, false otherwise </returns>
        ///
        /// <remarks>  </remarks>
        ///
        internal static bool CertIsGoodForEncryption(X509Certificate2 c)
        {
            return (
                CertHasOid(c, CertificateFilterInfo.DocumentEncryptionOid) &&
                (CertHasKeyUsage(c, X509KeyUsageFlags.DataEncipherment) ||
                 CertHasKeyUsage(c, X509KeyUsageFlags.KeyEncipherment)));
        }

        private static bool CertHasOid(X509Certificate2 c, string oid)
        {
            Collection<string> ekus = GetCertEKU(c);

            foreach (string testOid in ekus)
            {
                if (testOid == oid)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CertHasKeyUsage(X509Certificate2 c, X509KeyUsageFlags keyUsage)
        {
            foreach (X509Extension extension in c.Extensions)
            {
                X509KeyUsageExtension keyUsageExtension = extension as X509KeyUsageExtension;
                if (keyUsageExtension != null)
                {
                    if ((keyUsageExtension.KeyUsages & keyUsage) == keyUsage)
                    {
                        return true;
                    }
                    break;
                }
            }

            return false;
        }

        /// <summary>
        /// check if the specified cert has a private key in it
        /// </summary>
        ///
        /// <param name="cert"> certificate object </param>
        ///
        /// <returns> true on success, false otherwise </returns>
        ///
        /// <remarks>  </remarks>
        ///
        internal static bool CertHasPrivatekey(X509Certificate2 cert)
        {
            return cert.HasPrivateKey;
        }

        /// <summary>
        /// Get the EKUs of a cert
        /// </summary>
        ///
        /// <param name="cert"> certificate object </param>
        ///
        /// <returns> a collection of cert eku strings </returns>
        ///
        /// <remarks>  </remarks>
        ///
        [ArchitectureSensitive]
        internal static Collection<string> GetCertEKU(X509Certificate2 cert)
        {
            Collection<string> ekus = new Collection<string>();
            IntPtr pCert = cert.Handle;
            int structSize = 0;
            IntPtr dummy = IntPtr.Zero;

            if (Security.NativeMethods.CertGetEnhancedKeyUsage(pCert, 0, dummy,
                                                      out structSize))
            {
                if (structSize > 0)
                {
                    IntPtr ekuBuffer = Marshal.AllocHGlobal(structSize);

                    try
                    {
                        if (Security.NativeMethods.CertGetEnhancedKeyUsage(pCert, 0,
                                                                  ekuBuffer,
                                                                  out structSize))
                        {
                            Security.NativeMethods.CERT_ENHKEY_USAGE ekuStruct =
                                (Security.NativeMethods.CERT_ENHKEY_USAGE)
                                ClrFacade.PtrToStructure<Security.NativeMethods.CERT_ENHKEY_USAGE>(ekuBuffer);
                            IntPtr ep = ekuStruct.rgpszUsageIdentifier;
                            IntPtr ekuptr;

                            for (int i = 0; i < ekuStruct.cUsageIdentifier; i++)
                            {
                                ekuptr = Marshal.ReadIntPtr(ep, i * Marshal.SizeOf(ep));
                                string eku = Marshal.PtrToStringAnsi(ekuptr);
                                ekus.Add(eku);
                            }
                        }
                        else
                        {
                            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(ekuBuffer);
                    }
                }
            }
            else
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            return ekus;
        }

        /// <summary>
        /// convert an int to a DWORD
        /// </summary>
        ///
        /// <param name="n"> signed int number  </param>
        ///
        /// <returns> DWORD </returns>
        ///
        /// <remarks>  </remarks>
        ///
        internal static DWORD GetDWORDFromInt(int n)
        {
            UInt32 result = BitConverter.ToUInt32(BitConverter.GetBytes(n), 0);
            return (DWORD)result;
        }

        /// <summary>
        /// convert a DWORD to int
        /// </summary>
        ///
        /// <param name="n"> number </param>
        ///
        /// <returns> int </returns>
        ///
        /// <remarks>  </remarks>
        ///
        internal static int GetIntFromDWORD(DWORD n)
        {
            Int64 n64 = n - 0x100000000L;
            return (int)n64;
        }
    }

    /// <summary>
    /// information used for filtering a set of certs
    /// </summary>
    internal sealed class CertificateFilterInfo
    {
        internal CertificateFilterInfo()
        {
        }

        /// <summary>
        /// purpose of a certificate
        /// </summary>
        internal CertificatePurpose Purpose
        {
            get { return _purpose; }
            set { _purpose = value; }
        }

        /// <summary>
        /// SSL Server Authentication
        /// </summary>
        internal bool SSLServerAuthentication
        {
            get { return _sslServerAuthentication; }
            set { _sslServerAuthentication = value; }
        }

        /// <summary>
        /// DNS name of a certificate
        /// </summary>
        internal string DnsName
        {
            set { _dnsName = value; }
        }

        /// <summary>
        /// EKU OID list of a certificate
        /// </summary>
        internal string[] Eku
        {
            set { _eku = value; }
        }

        /// <summary>
        /// remaining validity period in days for a certificate
        /// </summary>
        internal int ExpiringInDays
        {
            set { _expiringInDays = value; }
        }

        /// <summary>
        /// combine properties into a filter string
        /// </summary>
        internal string FilterString
        {
            get
            {
                string filterString = "";

                if (_dnsName != null)
                {
                    filterString = AppendFilter(filterString, "dns", _dnsName);
                }

                string ekuT = "";
                if (_eku != null)
                {
                    for (int i = 0; i < _eku.Length; i++)
                    {
                        if (ekuT.Length != 0)
                        {
                            ekuT = ekuT + ",";
                        }
                        ekuT = ekuT + _eku[i];
                    }
                }
                if (_purpose == CertificatePurpose.CodeSigning)
                {
                    if (ekuT.Length != 0)
                    {
                        ekuT = ekuT + ",";
                    }
                    ekuT = ekuT + CodeSigningOid;
                }
                if (_purpose == CertificatePurpose.DocumentEncryption)
                {
                    if (ekuT.Length != 0)
                    {
                        ekuT = ekuT + ",";
                    }
                    ekuT = ekuT + DocumentEncryptionOid;
                }
                if (_sslServerAuthentication)
                {
                    if (ekuT.Length != 0)
                    {
                        ekuT = ekuT + ",";
                    }
                    ekuT = ekuT + szOID_PKIX_KP_SERVER_AUTH;
                }
                if (ekuT.Length != 0)
                {
                    filterString = AppendFilter(filterString, "eku", ekuT);
                    if (_purpose == CertificatePurpose.CodeSigning ||
                        _sslServerAuthentication)
                    {
                        filterString = AppendFilter(filterString, "key", "*");
                    }
                }
                if (_expiringInDays >= 0)
                {
                    filterString = AppendFilter(
                                    filterString,
                                    "ExpiringInDays",
                                    _expiringInDays.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                if (filterString.Length == 0)
                {
                    filterString = null;
                }
                return filterString;
            }
        }

        private string AppendFilter(
                            string filterString,
                            string name,
                            string value)
        {
            string newfilter = value;

            // append a "name=value" filter to the existing filter string.
            // insert a separating "&" if existing filter string is not empty.

            // if the value is empty, do nothing.

            if (newfilter.Length != 0)
            {
                // if the value contains an equal sign or an ampersand, throw
                // an exception to avoid compromising the native code parser.

                if (newfilter.Contains("=") || newfilter.Contains("&"))
                {
                    throw Marshal.GetExceptionForHR(
                                    Security.NativeMethods.E_INVALID_DATA);
                }
                newfilter = name + "=" + newfilter;
                if (filterString.Length != 0)
                {
                    newfilter = "&" + newfilter;
                }
            }
            return filterString + newfilter;
        }

        private CertificatePurpose _purpose = 0;
        private bool _sslServerAuthentication = false;
        private string _dnsName = null;
        private string[] _eku = null;
        private int _expiringInDays = -1;

        internal const string CodeSigningOid = "1.3.6.1.5.5.7.3.3";
        internal const string szOID_PKIX_KP_SERVER_AUTH = "1.3.6.1.5.5.7.3.1";

        // The OID arc 1.3.6.1.4.1.311.80 is assigned to PowerShell. If we need
        // new OIDs, we can assign them under this branch.
        internal const string DocumentEncryptionOid = "1.3.6.1.4.1.311.80.1";
    }
}

namespace Microsoft.PowerShell.Commands
{
    /// <summary>
    /// Defines the valid purposes by which
    /// we can filter certificates.
    /// </summary>    
    internal enum CertificatePurpose
    {
        /// <summary>
        /// Certificates where a purpose has not been specified.
        /// </summary>
        NotSpecified = 0,

        /// <summary>
        /// Certificates that can be used to sign
        /// code and scripts.
        /// </summary>    
        CodeSigning = 0x1,

        /// <summary>
        /// Certificates that can be used to encrypt
        /// data.
        /// </summary>    
        DocumentEncryption = 0x2,

        /// <summary>
        /// Certificates that can be used for any
        /// purpose.
        /// </summary>    
        All = 0xffff
    }
}


namespace System.Management.Automation
{
#if !CORECLR

    using System.Security.Cryptography.Pkcs;

    /// <summary>
    /// Utility class for CMS (Cryptographic Message Syntax) related operations
    /// </summary>
    /// <remarks>
    /// The namespace 'System.Security.Cryptography.Pkcs' is not available in CoreCLR,
    /// so the Cryptographic Message Syntax (CMS) will not be supported on OneCore PS.
    /// </remarks>
    internal static class CmsUtils
    {
        internal static string Encrypt(byte[] contentBytes, CmsMessageRecipient[] recipients, SessionState sessionState, out ErrorRecord error)
        {
            error = null;

            if ((contentBytes == null) || (contentBytes.Length == 0))
            {
                return String.Empty;
            }

            // After review with the crypto board, NIST_AES256_CBC is more appropriate
            // than .NET's default 3DES. Also, when specified, uses szOID_RSAES_OAEP for key
            // encryption to prevent padding attacks.
            const string szOID_NIST_AES256_CBC = "2.16.840.1.101.3.4.1.42";

            ContentInfo content = new ContentInfo(contentBytes);
            EnvelopedCms cms = new EnvelopedCms(content,
                new AlgorithmIdentifier(
                    Oid.FromOidValue(szOID_NIST_AES256_CBC, OidGroup.EncryptionAlgorithm)));

            CmsRecipientCollection recipientCollection = new CmsRecipientCollection();
            foreach (CmsMessageRecipient recipient in recipients)
            {
                // Resolve the recipient, if it hasn't been done yet.
                if ((recipient.Certificates != null) && (recipient.Certificates.Count == 0))
                {
                    recipient.Resolve(sessionState, ResolutionPurpose.Encryption, out error);
                }

                if (error != null)
                {
                    return null;
                }

                foreach (X509Certificate2 certificate in recipient.Certificates)
                {
                    recipientCollection.Add(new CmsRecipient(certificate));
                }
            }

            cms.Encrypt(recipientCollection);

            byte[] encodedBytes = cms.Encode();
            string encodedContent = CmsUtils.GetAsciiArmor(encodedBytes);
            return encodedContent;
        }

        internal static string BEGIN_CMS_SIGIL = "-----BEGIN CMS-----";
        internal static string END_CMS_SIGIL = "-----END CMS-----";

        internal static string BEGIN_CERTIFICATE_SIGIL = "-----BEGIN CERTIFICATE-----";
        internal static string END_CERTIFICATE_SIGIL = "-----END CERTIFICATE-----";

        /// <summary>
        /// Adds Ascii armour to a byte stream in Base64 format
        /// </summary>
        /// <param name="bytes">The bytes to encode</param>
        internal static string GetAsciiArmor(byte[] bytes)
        {
            StringBuilder output = new StringBuilder();
            output.AppendLine(BEGIN_CMS_SIGIL);

            string encodedString = Convert.ToBase64String(
                bytes, Base64FormattingOptions.InsertLineBreaks);
            output.AppendLine(encodedString);
            output.Append(END_CMS_SIGIL);

            return output.ToString();
        }

        /// <summary>
        /// Removes Ascii armour from a byte stream
        /// </summary>
        /// <param name="actualContent">The Ascii armored content</param>
        /// <param name="beginMarker">The marker of the start of the Base64 content</param>
        /// <param name="endMarker">The marker of the end of the Base64 content</param>
        /// <param name="startIndex">The beginning of where the Ascii armor was detected</param>
        /// <param name="endIndex">The end of where the Ascii armor was detected</param>
        internal static byte[] RemoveAsciiArmor(string actualContent, string beginMarker, string endMarker, out int startIndex, out int endIndex)
        {
            byte[] messageBytes = null;
            startIndex = -1;
            endIndex = -1;

            startIndex = actualContent.IndexOf(beginMarker, StringComparison.OrdinalIgnoreCase);
            if (startIndex < 0)
            {
                return null;
            }

            endIndex = actualContent.IndexOf(endMarker, startIndex, StringComparison.OrdinalIgnoreCase) +
                 endMarker.Length;
            if (endIndex < endMarker.Length)
            {
                return null;
            }

            int startContent = startIndex + beginMarker.Length;
            int endContent = endIndex - endMarker.Length;
            string encodedContent = actualContent.Substring(startContent, endContent - startContent);
            encodedContent = System.Text.RegularExpressions.Regex.Replace(encodedContent, "\\s", "");
            messageBytes = Convert.FromBase64String(encodedContent);

            return messageBytes;
        }
    }

#endif

    /// <summary>
    /// Represents a message recipient for the Cms cmdlets.
    /// </summary>
    public class CmsMessageRecipient
    {
        /// <summary>
        /// Creates an instance of the CmsMessageRecipient class
        /// </summary>
        internal CmsMessageRecipient() { }

        /// <summary>
        /// Creates an instance of the CmsMessageRecipient class
        /// </summary>
        /// <param name="identifier">
        ///     The identifier of the CmsMessageRecipient.
        ///     Can be either:
        ///         - The path to a file containing the certificate
        ///         - The path to a directory containing the certificate
        ///         - The thumbprint of the certificate, used to find the certificate in the certificate store
        ///         - The Subject name of the recipient, used to find the certificate in the certificate store
        /// </param>
        public CmsMessageRecipient(string identifier)
        {
            _identifier = identifier;
            this.Certificates = new X509Certificate2Collection();
        }
        private string _identifier = null;

        /// <summary>
        /// Creates an instance of the CmsMessageRecipient class
        /// </summary>
        /// <param name="certificate">The certificate to use.</param>
        public CmsMessageRecipient(X509Certificate2 certificate)
        {
            _pendingCertificate = certificate;
            this.Certificates = new X509Certificate2Collection();
        }
        private X509Certificate2 _pendingCertificate = null;

        /// <summary>
        /// Gets the certificate associated with this recipient
        /// </summary>
        public X509Certificate2Collection Certificates
        {
            get;
            internal set;
        }

        /// <summary>
        /// Resolves the provided identifier into a collection of certificates.
        /// </summary>
        /// <param name="sessionState">A reference to an instance of Powershell's SessionState class.</param>
        /// <param name="purpose">The purpose for which this identifier is being resolved (Encryption / Decryption.</param>
        /// <param name="error">The error generated (if any) for this resolution.</param>
        public void Resolve(SessionState sessionState, ResolutionPurpose purpose, out ErrorRecord error)
        {
            error = null;

            // Process the certificate if that was supplied exactly
            if (_pendingCertificate != null)
            {
                ProcessResolvedCertificates(purpose,
                    new List<X509Certificate2> { _pendingCertificate }, out error);
                if ((error != null) || (Certificates.Count != 0))
                {
                    return;
                }
            }

            if (_identifier != null)
            {
                // First try to resolve assuming that the cert was Base64 encoded.
                ResolveFromBase64Encoding(purpose, out error);
                if ((error != null) || (Certificates.Count != 0))
                {
                    return;
                }

                // Then try to resolve by path.
                ResolveFromPath(sessionState, purpose, out error);
                if ((error != null) || (Certificates.Count != 0))
                {
                    return;
                }

                // Then by thumbprint
                ResolveFromThumbprint(sessionState, purpose, out error);
                if ((error != null) || (Certificates.Count != 0))
                {
                    return;
                }

                // Then by Subject Name
                ResolveFromSubjectName(sessionState, purpose, out error);
                if ((error != null) || (Certificates.Count != 0))
                {
                    return;
                }
            }

            // Generate an error if no cert was found (and this is an encryption attempt).
            // If it is only decryption, then the system will always look in the 'My' store anyways, so
            // don't generate an error if they used wildcards. If they did not use wildcards,
            // then generate an error because they were expecting something specific.
            if ((purpose == ResolutionPurpose.Encryption) ||
                (!WildcardPattern.ContainsWildcardCharacters(_identifier)))
            {
                error = new ErrorRecord(
                    new ArgumentException(
                        String.Format(CultureInfo.InvariantCulture,
                            SecuritySupportStrings.NoCertificateFound, _identifier)),
                    "NoCertificateFound", ErrorCategory.ObjectNotFound, _identifier);
            }

            return;
        }

        private void ResolveFromBase64Encoding(ResolutionPurpose purpose, out ErrorRecord error)
        {
            error = null;
            int startIndex, endIndex;
            byte[] messageBytes = null;
            try
            {
                messageBytes = CmsUtils.RemoveAsciiArmor(_identifier, CmsUtils.BEGIN_CERTIFICATE_SIGIL, CmsUtils.END_CERTIFICATE_SIGIL, out startIndex, out endIndex);
            }
            catch (FormatException)
            {
                // Not Base-64 encoded
                return;
            }

            // Didn't have the sigil
            if (messageBytes == null)
            {
                return;
            }

            List<X509Certificate2> certificatesToProcess = new List<X509Certificate2>(); ;
            try
            {
                X509Certificate2 newCertificate = new X509Certificate2(messageBytes);
                certificatesToProcess.Add(newCertificate);
            }
            catch (Exception e)
            {
                // User call-out, catch-all OK
                CommandProcessorBase.CheckForSevereException(e);

                // Wasn't certificate data
                return;
            }

            // Now validate the certificate
            ProcessResolvedCertificates(purpose, certificatesToProcess, out error);
        }

        private void ResolveFromPath(SessionState sessionState, ResolutionPurpose purpose, out ErrorRecord error)
        {
            error = null;
            ProviderInfo pathProvider = null;
            Collection<string> resolvedPaths = null;

            try
            {
                resolvedPaths = sessionState.Path.GetResolvedProviderPathFromPSPath(_identifier, out pathProvider);
            }
            catch (SessionStateException)
            {
                // If we got an ItemNotFound / etc., then this didn't represent a valid path.
            }

            // If we got a resolved path, try to load certs from that path.
            if ((resolvedPaths != null) && (resolvedPaths.Count != 0))
            {
                // Ensure the path is from the file system provider
                if (!String.Equals(pathProvider.Name, "FileSystem", StringComparison.OrdinalIgnoreCase))
                {
                    error = new ErrorRecord(
                        new ArgumentException(
                            String.Format(CultureInfo.InvariantCulture,
                                SecuritySupportStrings.CertificatePathMustBeFileSystemPath, _identifier)),
                        "CertificatePathMustBeFileSystemPath", ErrorCategory.ObjectNotFound, pathProvider);
                    return;
                }

                // If this is a directory, add all certificates in it. This will be the primary
                // scenario for decryption via Group Protected PFX files
                // (http://social.technet.microsoft.com/wiki/contents/articles/13922.certificate-pfx-export-and-import-using-ad-ds-account-protection.aspx)
                List<string> pathsToAdd = new List<string>();
                List<string> pathsToRemove = new List<string>();
                foreach (string resolvedPath in resolvedPaths)
                {
                    if (System.IO.Directory.Exists(resolvedPath))
                    {
                        // It would be nice to limit this to *.pfx, *.cer, etc., but
                        // the crypto APIs support extracting certificates from arbitrary file types.
                        pathsToAdd.AddRange(System.IO.Directory.GetFiles(resolvedPath));
                        pathsToRemove.Add(resolvedPath);
                    }
                }

                // Update resolved paths
                foreach (string path in pathsToAdd)
                {
                    resolvedPaths.Add(path);
                }

                foreach (string path in pathsToRemove)
                {
                    resolvedPaths.Remove(path);
                }

                List<X509Certificate2> certificatesToProcess = new List<X509Certificate2>();
                foreach (string path in resolvedPaths)
                {
                    X509Certificate2 certificate = null;

                    try
                    {
                        certificate = new X509Certificate2(path);
                    }
                    catch (Exception e)
                    {
                        // User call-out, catch-all OK
                        CommandProcessorBase.CheckForSevereException(e);
                        continue;
                    }

                    certificatesToProcess.Add(certificate);
                }

                ProcessResolvedCertificates(purpose, certificatesToProcess, out error);
            }
        }

        private void ResolveFromThumbprint(SessionState sessionState, ResolutionPurpose purpose, out ErrorRecord error)
        {
            // Quickly check that this is a thumbprint-like pattern (just hex)
            if (!System.Text.RegularExpressions.Regex.IsMatch(_identifier, "^[a-f0-9]+$", Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                error = null;
                return;
            }

            Collection<PSObject> certificates = new Collection<PSObject>();

            try
            {
                // Get first from 'My' store
                string certificatePath = sessionState.Path.Combine("Microsoft.PowerShell.Security\\Certificate::CurrentUser\\My", _identifier);
                if (sessionState.InvokeProvider.Item.Exists(certificatePath))
                {
                    foreach (PSObject certificateObject in sessionState.InvokeProvider.Item.Get(certificatePath))
                    {
                        certificates.Add(certificateObject);
                    }
                }

                // Second from 'LocalMachine' store
                certificatePath = sessionState.Path.Combine("Microsoft.PowerShell.Security\\Certificate::LocalMachine\\My", _identifier);
                if (sessionState.InvokeProvider.Item.Exists(certificatePath))
                {
                    foreach (PSObject certificateObject in sessionState.InvokeProvider.Item.Get(certificatePath))
                    {
                        certificates.Add(certificateObject);
                    }
                }
            }
            catch (SessionStateException)
            {
                // If we got an ItemNotFound / etc., then this didn't represent a valid path.
            }

            List<X509Certificate2> certificatesToProcess = new List<X509Certificate2>();
            foreach (PSObject certificateObject in certificates)
            {
                X509Certificate2 certificate = certificateObject.BaseObject as X509Certificate2;
                if (certificate != null)
                {
                    certificatesToProcess.Add(certificate);
                }
            }

            ProcessResolvedCertificates(purpose, certificatesToProcess, out error);
        }

        private void ResolveFromSubjectName(SessionState sessionState, ResolutionPurpose purpose, out ErrorRecord error)
        {
            Collection<PSObject> certificates = new Collection<PSObject>();
            WildcardPattern subjectNamePattern = WildcardPattern.Get(_identifier, WildcardOptions.IgnoreCase);

            try
            {
                // Get first from 'My' store, then 'LocalMachine'
                string[] certificatePaths = new string[] {
                        "Microsoft.PowerShell.Security\\Certificate::CurrentUser\\My",
                        "Microsoft.PowerShell.Security\\Certificate::LocalMachine\\My" };

                foreach (string certificatePath in certificatePaths)
                {
                    foreach (PSObject certificateObject in sessionState.InvokeProvider.ChildItem.Get(certificatePath, false))
                    {
                        if (subjectNamePattern.IsMatch(certificateObject.Properties["Subject"].Value.ToString()))
                        {
                            certificates.Add(certificateObject);
                        }
                    }
                }
            }
            catch (SessionStateException)
            {
                // If we got an ItemNotFound / etc., then this didn't represent a valid path.
            }

            List<X509Certificate2> certificatesToProcess = new List<X509Certificate2>();
            foreach (PSObject certificateObject in certificates)
            {
                X509Certificate2 certificate = certificateObject.BaseObject as X509Certificate2;
                if (certificate != null)
                {
                    certificatesToProcess.Add(certificate);
                }
            }

            ProcessResolvedCertificates(purpose, certificatesToProcess, out error);
        }

        private void ProcessResolvedCertificates(ResolutionPurpose purpose, List<X509Certificate2> certificatesToProcess, out ErrorRecord error)
        {
            error = null;
            HashSet<String> processedThumbprints = new HashSet<string>();

            foreach (X509Certificate2 certificate in certificatesToProcess)
            {
                if (!SecuritySupport.CertIsGoodForEncryption(certificate))
                {
                    // If they specified a specific cert, generate an error if it isn't good
                    // for encryption.
                    if (!WildcardPattern.ContainsWildcardCharacters(_identifier))
                    {
                        error = new ErrorRecord(
                            new ArgumentException(
                                String.Format(CultureInfo.InvariantCulture,
                                    SecuritySupportStrings.CertificateCannotBeUsedForEncryption, certificate.Thumbprint, CertificateFilterInfo.DocumentEncryptionOid)),
                            "CertificateCannotBeUsedForEncryption", ErrorCategory.InvalidData, certificate);
                        return;
                    }
                    else
                    {
                        continue;
                    }
                }

                // When decrypting, only look for certs that have the private key
                if (purpose == ResolutionPurpose.Decryption)
                {
                    if (!certificate.HasPrivateKey)
                    {
                        continue;
                    }
                }

                if (processedThumbprints.Contains(certificate.Thumbprint))
                {
                    continue;
                }
                else
                {
                    processedThumbprints.Add(certificate.Thumbprint);
                }


                if (purpose == ResolutionPurpose.Encryption)
                {
                    // Only let wildcards expand to one recipient. Otherwise, data
                    // may be encrypted to the wrong person on accident.
                    if (Certificates.Count > 0)
                    {
                        error = new ErrorRecord(
                            new ArgumentException(
                                String.Format(CultureInfo.InvariantCulture,
                                    SecuritySupportStrings.IdentifierMustReferenceSingleCertificate, _identifier, "To")),
                            "IdentifierMustReferenceSingleCertificate", ErrorCategory.LimitsExceeded, certificatesToProcess);
                        Certificates.Clear();
                        return;
                    }
                }

                Certificates.Add(certificate);
            }
        }
    }

    /// <summary>
    /// Defines the purpose for resolution of a CmsMessageRecipient
    /// </summary>
    public enum ResolutionPurpose
    {
        /// <summary>
        /// This message recipient is intended to be used for message encryption
        /// </summary>
        Encryption,

        /// <summary>
        /// This message recipient is intended to be used for message decryption
        /// </summary>
        Decryption
    }

    internal class AmsiUtils
    {
        internal static int Init()
        {
            Diagnostics.Assert(s_amsiContext == IntPtr.Zero, "Init should be called just once");

            lock (s_amsiLockObject)
            {
                Process currentProcess = Process.GetCurrentProcess();
                string hostname;
                try
                {
                    var processModule = PsUtils.GetMainModule(currentProcess);
                    hostname = string.Concat("PowerShell_", processModule.FileName, "_",
                        ClrFacade.GetProcessModuleFileVersionInfo(processModule).ProductVersion);
                }
                catch (ComponentModel.Win32Exception)
                {
                    // This exception can be thrown during thread impersonation (Access Denied for process module access).
                    // Use command line arguments or process name.
                    string[] cmdLineArgs = Environment.GetCommandLineArgs();
                    string processPath = (cmdLineArgs.Length > 0) ? cmdLineArgs[0] : currentProcess.ProcessName;
                    hostname = string.Concat("PowerShell_", processPath, ".exe_0.0.0.0");
                }

#if !CORECLR
                AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
#endif

                var hr = AmsiNativeMethods.AmsiInitialize(hostname, ref s_amsiContext);
                if (!Utils.Succeeded(hr))
                {
                    s_amsiInitFailed = true;
                }

                return hr;
            }
        }

        /// <summary>
        /// Scans a string buffer for malware using the Antimalware Scan Interface (AMSI).
        /// Caller is responsible for calling AmsiCloseSession when a "session" (script)
        /// is complete, and for calling AmsiUninitialize when the runspace is being torn down.
        /// </summary>
        /// <param name="content">The string to be scanned</param>
        /// <param name="sourceMetadata">Information about the source (filename, etc.)</param>
        /// <returns>AMSI_RESULT_DETECTED if malware was detected in the sample.</returns>
        internal static AmsiNativeMethods.AMSI_RESULT ScanContent(string content, string sourceMetadata)
        {
#if UNIX
            return AmsiNativeMethods.AMSI_RESULT.AMSI_RESULT_NOT_DETECTED;
#else
            return WinScanContent(content, sourceMetadata);
#endif
        }

        internal static AmsiNativeMethods.AMSI_RESULT WinScanContent(string content, string sourceMetadata)
        {
            if (String.IsNullOrEmpty(sourceMetadata))
            {
                sourceMetadata = String.Empty;
            }

            const string EICAR_STRING = "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";
            if (InternalTestHooks.UseDebugAmsiImplementation)
            {
                if (content.IndexOf(EICAR_STRING, StringComparison.Ordinal) >= 0)
                {
                    return AmsiNativeMethods.AMSI_RESULT.AMSI_RESULT_DETECTED;
                }
            }

            // If we had a previous initialization failure, just return the neutral result.
            if (s_amsiInitFailed)
            {
                return AmsiNativeMethods.AMSI_RESULT.AMSI_RESULT_NOT_DETECTED;
            }

            lock (s_amsiLockObject)
            {
                if (s_amsiInitFailed)
                {
                    return AmsiNativeMethods.AMSI_RESULT.AMSI_RESULT_NOT_DETECTED;
                }

                try
                {
                    int hr = 0;

                    // Initialize AntiMalware Scan Interface, if not already initialized.
                    // If we failed to initialize previously, just return the neutral result ("AMSI_RESULT_NOT_DETECTED")
                    if (s_amsiContext == IntPtr.Zero)
                    {
                        hr = Init();

                        if (!Utils.Succeeded(hr))
                        {
                            s_amsiInitFailed = true;
                            return AmsiNativeMethods.AMSI_RESULT.AMSI_RESULT_NOT_DETECTED;
                        }
                    }

                    // Initialize the session, if one isn't already started.
                    // If we failed to initialize previously, just return the neutral result ("AMSI_RESULT_NOT_DETECTED")
                    if (s_amsiSession == IntPtr.Zero)
                    {
                        hr = AmsiNativeMethods.AmsiOpenSession(s_amsiContext, ref s_amsiSession);
                        AmsiInitialized = true;

                        if (!Utils.Succeeded(hr))
                        {
                            s_amsiInitFailed = true;
                            return AmsiNativeMethods.AMSI_RESULT.AMSI_RESULT_NOT_DETECTED;
                        }
                    }

                    AmsiNativeMethods.AMSI_RESULT result = AmsiNativeMethods.AMSI_RESULT.AMSI_RESULT_CLEAN;

                    hr = AmsiNativeMethods.AmsiScanString(
                        s_amsiContext,
                        content,
                        sourceMetadata,
                        s_amsiSession,
                        ref result);

                    if (!Utils.Succeeded(hr))
                    {
                        // If we got a failure, just return the neutral result ("AMSI_RESULT_NOT_DETECTED")
                        return AmsiNativeMethods.AMSI_RESULT.AMSI_RESULT_NOT_DETECTED;
                    }

                    return result;
                }
                catch (DllNotFoundException)
                {
                    s_amsiInitFailed = true;
                    return AmsiNativeMethods.AMSI_RESULT.AMSI_RESULT_NOT_DETECTED;
                }
            }
        }

        internal static void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
#if !UNIX
            VerifyAmsiUninitializeCalled();
#endif
        }

        [SuppressMessage("Microsoft.Reliability", "CA2006:UseSafeHandleToEncapsulateNativeResources")]
        private static IntPtr s_amsiContext = IntPtr.Zero;
        [SuppressMessage("Microsoft.Reliability", "CA2006:UseSafeHandleToEncapsulateNativeResources")]
        private static IntPtr s_amsiSession = IntPtr.Zero;

        private static bool s_amsiInitFailed = false;
        private static object s_amsiLockObject = new Object();

        /// <summary>
        /// Reset the AMSI session (used to track related script invocations)
        /// </summary>
        internal static void CloseSession()
        {
#if !UNIX
            WinCloseSession();
#endif
        }

        internal static void WinCloseSession()
        {
            if (!s_amsiInitFailed)
            {
                if ((s_amsiContext != IntPtr.Zero) && (s_amsiSession != IntPtr.Zero))
                {
                    lock (s_amsiLockObject)
                    {
                        // Clean up the session if one was open.
                        if ((s_amsiContext != IntPtr.Zero) && (s_amsiSession != IntPtr.Zero))
                        {
                            AmsiNativeMethods.AmsiCloseSession(s_amsiContext, s_amsiSession);
                            s_amsiSession = IntPtr.Zero;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Uninitialize the AMSI interface
        /// </summary>
        internal static void Uninitialize()
        {
#if !UNIX
            WinUninitialize();
#endif
        }

        internal static void WinUninitialize()
        {
            AmsiUninitializeCalled = true;
            if (!s_amsiInitFailed)
            {
                lock (s_amsiLockObject)
                {
                    if (s_amsiContext != IntPtr.Zero)
                    {
                        CloseSession();

                        // Uninitialize the AMSI interface.
                        AmsiCleanedUp = true;
                        AmsiNativeMethods.AmsiUninitialize(s_amsiContext);
                        s_amsiContext = IntPtr.Zero;
                    }
                }
            }
        }
        public static bool AmsiUninitializeCalled = false;
        public static bool AmsiInitialized = false;
        public static bool AmsiCleanedUp = false;

        private static void VerifyAmsiUninitializeCalled()
        {
            Debug.Assert((!AmsiInitialized) || AmsiUninitializeCalled, "AMSI should have been uninitialized.");
        }

        internal class AmsiNativeMethods
        {
            internal enum AMSI_RESULT
            {
                /// AMSI_RESULT_CLEAN -> 0
                AMSI_RESULT_CLEAN = 0,

                /// AMSI_RESULT_NOT_DETECTED -> 1
                AMSI_RESULT_NOT_DETECTED = 1,

                /// AMSI_RESULT_DETECTED -> 32768
                AMSI_RESULT_DETECTED = 32768,
            }

            /// Return Type: HRESULT->LONG->int
            ///appName: LPCWSTR->WCHAR*
            ///amsiContext: HAMSICONTEXT*
            [DllImportAttribute("amsi.dll", EntryPoint = "AmsiInitialize", CallingConvention = CallingConvention.StdCall)]
            internal static extern int AmsiInitialize(
                [InAttribute()] [MarshalAsAttribute(UnmanagedType.LPWStr)] string appName, ref System.IntPtr amsiContext);


            /// Return Type: void
            ///amsiContext: HAMSICONTEXT->HAMSICONTEXT__*
            [DllImportAttribute("amsi.dll", EntryPoint = "AmsiUninitialize", CallingConvention = CallingConvention.StdCall)]
            internal static extern void AmsiUninitialize(System.IntPtr amsiContext);


            /// Return Type: HRESULT->LONG->int
            ///amsiContext: HAMSICONTEXT->HAMSICONTEXT__*
            ///amsiSession: HAMSISESSION*
            [DllImportAttribute("amsi.dll", EntryPoint = "AmsiOpenSession", CallingConvention = CallingConvention.StdCall)]
            internal static extern int AmsiOpenSession(System.IntPtr amsiContext, ref System.IntPtr amsiSession);


            /// Return Type: void
            ///amsiContext: HAMSICONTEXT->HAMSICONTEXT__*
            ///amsiSession: HAMSISESSION->HAMSISESSION__*
            [DllImportAttribute("amsi.dll", EntryPoint = "AmsiCloseSession", CallingConvention = CallingConvention.StdCall)]
            internal static extern void AmsiCloseSession(System.IntPtr amsiContext, System.IntPtr amsiSession);


            /// Return Type: HRESULT->LONG->int
            ///amsiContext: HAMSICONTEXT->HAMSICONTEXT__*
            ///buffer: PVOID->void*
            ///length: ULONG->unsigned int
            ///contentName: LPCWSTR->WCHAR*
            ///amsiSession: HAMSISESSION->HAMSISESSION__*
            ///result: AMSI_RESULT*
            [DllImportAttribute("amsi.dll", EntryPoint = "AmsiScanBuffer", CallingConvention = CallingConvention.StdCall)]
            internal static extern int AmsiScanBuffer(
                System.IntPtr amsiContext, System.IntPtr buffer, uint length,
                [InAttribute()] [MarshalAsAttribute(UnmanagedType.LPWStr)] string contentName, System.IntPtr amsiSession, ref AMSI_RESULT result);


            /// Return Type: HRESULT->LONG->int
            ///amsiContext: HAMSICONTEXT->HAMSICONTEXT__*
            ///string: LPCWSTR->WCHAR*
            ///contentName: LPCWSTR->WCHAR*
            ///amsiSession: HAMSISESSION->HAMSISESSION__*
            ///result: AMSI_RESULT*
            [DllImportAttribute("amsi.dll", EntryPoint = "AmsiScanString", CallingConvention = CallingConvention.StdCall)]
            internal static extern int AmsiScanString(
                System.IntPtr amsiContext, [InAttribute()] [MarshalAsAttribute(UnmanagedType.LPWStr)] string @string,
                [InAttribute()] [MarshalAsAttribute(UnmanagedType.LPWStr)] string contentName, System.IntPtr amsiSession, ref AMSI_RESULT result);
        }
    }
}
#pragma warning restore 56523
