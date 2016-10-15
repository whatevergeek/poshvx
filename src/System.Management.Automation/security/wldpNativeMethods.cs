using System.Management.Automation.Internal;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;

namespace System.Management.Automation.Security
{
    /// <summary>
    /// How the policy is being enforced.
    /// 
    /// </summary>
    // Internal Note: Current code that consumes this enum assumes that anything but 'Enforce' means
    // that the script is allowed, and that a system lockdown policy that is anything but 'None' means
    // that the API should be called again for individual files. If any elements are added to this enum,
    // callers of the GetLockdownPolicy() should be reviewed.
    public enum SystemEnforcementMode
    {
        /// Not enforced at all
        None = 0,

        /// Enabled - allow, but audit
        Audit = 1,

        /// Enabled, enforce restrictions
        Enforce = 2
    }

    /// <summary>
    /// Support class for dealing with the Windows Lockdown Policy,
    /// Device Guard, and Constrained PowerShell
    /// </summary>
    public sealed class SystemPolicy
    {
        private SystemPolicy()
        {
        }

        /// <summary>
        /// Gets the system lockdown policy
        /// </summary>
        /// <returns>An EnforcementMode that describes the system policy</returns>
        public static SystemEnforcementMode GetSystemLockdownPolicy()
        {
            if (s_wasSystemPolicyDebugPolicy || (s_systemLockdownPolicy == null))
            {
                lock (s_systemLockdownPolicyLock)
                {
                    if (s_wasSystemPolicyDebugPolicy || (s_systemLockdownPolicy == null))
                    {
                        s_systemLockdownPolicy = GetLockdownPolicy(null, null);
                    }
                }
            }

            return s_systemLockdownPolicy.Value;
        }

        private static object s_systemLockdownPolicyLock = new Object();
        private static Nullable<SystemEnforcementMode> s_systemLockdownPolicy = null;
        private static bool s_wasSystemPolicyDebugPolicy = false;

        /// <summary>
        /// Gets lockdown policy as applied to a file
        /// </summary>
        /// <returns>An EnforcementMode that describes policy</returns>
        public static SystemEnforcementMode GetLockdownPolicy(string path, SafeHandle handle)
        {
            // Check the WLDP API
            SystemEnforcementMode lockdownPolicy = GetWldpPolicy(path, handle);
            if (lockdownPolicy == SystemEnforcementMode.Enforce)
            {
                return lockdownPolicy;
            }

            // At this point, LockdownPolicy = Audit or Allowed.
            // If there was a WLDP policy, but WLDP didn't block it,
            // then it was explicitly allowed. Therefore, return the result for the file.
            SystemEnforcementMode systemWldpPolicy = s_cachedWldpSystemPolicy.GetValueOrDefault(SystemEnforcementMode.None);
            if ((systemWldpPolicy == SystemEnforcementMode.Enforce) ||
                (systemWldpPolicy == SystemEnforcementMode.Audit))
            {
                return lockdownPolicy;
            }

            // Check the AppLocker API
            lockdownPolicy = GetAppLockerPolicy(path, handle);
            if (lockdownPolicy == SystemEnforcementMode.Enforce)
            {
                return lockdownPolicy;
            }

            // If there was a system-wide AppLocker policy, but AppLocker didn't block it,
            // then return AppLocker's status.
            if (s_cachedSaferSystemPolicy.GetValueOrDefault(SaferPolicy.Allowed) ==
                SaferPolicy.Disallowed)
            {
                return lockdownPolicy;
            }

            // If it's not set to 'Enforce' by the platform, allow debug overrides
            return GetDebugLockdownPolicy(path);
        }

        [SuppressMessage("Microsoft.Reliability", "CA2001:AvoidCallingProblematicMethods",
            MessageId = "System.Runtime.InteropServices.SafeHandle.DangerousGetHandle")]
        private static SystemEnforcementMode GetWldpPolicy(string path, SafeHandle handle)
        {
            // If the WLDP assembly is missing (such as windows 7 or down OS), return default/None to skip WLDP validation
            if (s_hadMissingWldpAssembly || !IO.File.Exists(IO.Path.Combine(Environment.SystemDirectory, "wldp.dll")))
            {
                s_hadMissingWldpAssembly = true;
                return s_cachedWldpSystemPolicy.GetValueOrDefault(SystemEnforcementMode.None);
            }

            // If path is NULL, see if we have the cached system-wide lockdown policy.
            if (String.IsNullOrEmpty(path))
            {
                if ((s_cachedWldpSystemPolicy != null) && (!InternalTestHooks.BypassAppLockerPolicyCaching))
                {
                    return s_cachedWldpSystemPolicy.Value;
                }
            }

            try
            {
                WLDP_HOST_INFORMATION hostInformation = new WLDP_HOST_INFORMATION();
                hostInformation.dwRevision = WldpNativeConstants.WLDP_HOST_INFORMATION_REVISION;
                hostInformation.dwHostId = WLDP_HOST_ID.WLDP_HOST_ID_POWERSHELL;

                if (!String.IsNullOrEmpty(path))
                {
                    hostInformation.szSource = path;

                    if (handle != null)
                    {
                        IntPtr fileHandle = IntPtr.Zero;
                        fileHandle = handle.DangerousGetHandle();
                        hostInformation.hSource = fileHandle;
                    }
                }

                uint pdwLockdownState = 0;
                int result = WldpNativeMethods.WldpGetLockdownPolicy(ref hostInformation, ref pdwLockdownState, 0);
                if (result >= 0)
                {
                    SystemEnforcementMode resultingLockdownPolicy = GetLockdownPolicyForResult(pdwLockdownState);

                    // If this is a query for the system-wide lockdown policy, cache it.
                    if (String.IsNullOrEmpty(path))
                    {
                        s_cachedWldpSystemPolicy = resultingLockdownPolicy;
                    }

                    return resultingLockdownPolicy;
                }
                else
                {
                    // API failure?
                    return SystemEnforcementMode.Enforce;
                }
            }
            catch (DllNotFoundException)
            {
                s_hadMissingWldpAssembly = true;
                return s_cachedWldpSystemPolicy.GetValueOrDefault(SystemEnforcementMode.None);
            }
        }
        private static SystemEnforcementMode? s_cachedWldpSystemPolicy = null;


        private static SystemEnforcementMode GetAppLockerPolicy(string path, SafeHandle handle)
        {
            SaferPolicy result = SaferPolicy.Disallowed;

            // If path is NULL, we're looking for the system-wide lockdown policy.
            // Since there is no way to get that from AppLocker, we will test the policy
            // against a random non-existent script and module. If that is allowed, then there is
            // no AppLocker script policy.
            if (String.IsNullOrEmpty(path))
            {
                if ((s_cachedSaferSystemPolicy != null) && (!InternalTestHooks.BypassAppLockerPolicyCaching))
                {
                    result = s_cachedSaferSystemPolicy.Value;
                }
                else
                {
                    //
                    // Temp path can sometimes be deleted. While many places in PowerShell depend on its existence,
                    // this one can crash PowerShell.
                    // A less sensitive implementation will be possible once AppLocker allows validation of files that
                    // don't exist.
                    //

                    string testPathScript = null;
                    string testPathModule = null;
                    try
                    {
                        // Start with the current profile temp path.
                        string tempPath = IO.Path.GetTempPath();

                        int iteration = 0;
                        while (iteration++ < 2)
                        {
                            bool error = false;

                            try
                            {
                                if (!IO.Directory.Exists(tempPath))
                                {
                                    IO.Directory.CreateDirectory(tempPath);
                                }

                                testPathScript = IO.Path.Combine(tempPath, IO.Path.GetRandomFileName() + ".ps1");
                                testPathModule = IO.Path.Combine(tempPath, IO.Path.GetRandomFileName() + ".psm1");

                                // AppLocker fails when you try to check a policy on a file
                                // with no content. So create a scratch file and test on that.
                                IO.File.WriteAllText(testPathScript, "1");
                                IO.File.WriteAllText(testPathModule, "1");
                            }
                            catch (System.IO.IOException)
                            {
                                if (iteration == 2) throw;
                                error = true;
                            }
                            catch (System.UnauthorizedAccessException)
                            {
                                if (iteration == 2) throw;
                                error = true;
                            }
                            catch (System.Security.SecurityException)
                            {
                                if (iteration == 2) throw;
                                error = true;
                            }

                            if (!error) { break; }

                            // Try again with the AppData\LocalLow\Temp path using known folder id:
                            // https://msdn.microsoft.com/en-us/library/dd378457.aspx
                            Guid AppDatalocalLowFolderId = new Guid("A520A1A4-1780-4FF6-BD18-167343C5AF16");
                            tempPath = GetKnownFolderPath(AppDatalocalLowFolderId) + @"\Temp";
                        } // end while loop

                        // Test policy.
                        result = TestSaferPolicy(testPathScript, testPathModule);
                    }
                    catch (System.IO.IOException)
                    {
                        // If we fail to test the policy, assume the default.
                        result = SaferPolicy.Disallowed;
                    }
                    catch (System.UnauthorizedAccessException)
                    {
                        // This can happen during thread impersonation if the profile temp paths are not accessible.
                        // Allow policy if impersonated, otherwise disallow.
                        result =
                            (System.Security.Principal.WindowsIdentity.GetCurrent().ImpersonationLevel == System.Security.Principal.TokenImpersonationLevel.Impersonation) ?
                            SaferPolicy.Allowed : SaferPolicy.Disallowed;
                    }
                    catch (ArgumentException)
                    {
                        // This is for IO.Path.GetTempPath() call when temp paths are not accessible.
                        result =
                           (System.Security.Principal.WindowsIdentity.GetCurrent().ImpersonationLevel == System.Security.Principal.TokenImpersonationLevel.Impersonation) ?
                           SaferPolicy.Allowed : SaferPolicy.Disallowed;
                    }
                    finally
                    {
                        if (IO.File.Exists(testPathScript)) { IO.File.Delete(testPathScript); }
                        if (IO.File.Exists(testPathModule)) { IO.File.Delete(testPathModule); }
                    }

                    s_cachedSaferSystemPolicy = result;
                }

                if (result == SaferPolicy.Disallowed)
                {
                    return SystemEnforcementMode.Enforce;
                }
                else
                {
                    return SystemEnforcementMode.None;
                }
            }
            else
            {
                // We got a path. Return the result for that path.
                result = SecuritySupport.GetSaferPolicy(path, handle);
                if (result == SaferPolicy.Disallowed)
                {
                    return SystemEnforcementMode.Enforce;
                }

                return SystemEnforcementMode.None;
            }
        }
        private static SaferPolicy? s_cachedSaferSystemPolicy = null;

        private static string GetKnownFolderPath(Guid knownFolderId)
        {
            IntPtr pszPath = IntPtr.Zero;
            try
            {
                int hr = WldpNativeMethods.SHGetKnownFolderPath(knownFolderId, 0, IntPtr.Zero, out pszPath);
                if (hr >= 0)
                {
                    return Marshal.PtrToStringAuto(pszPath);
                }

                throw new System.IO.IOException();
            }
            finally
            {
                if (pszPath != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pszPath);
                }
            }
        }

        private static SaferPolicy TestSaferPolicy(string testPathScript, string testPathModule)
        {
            SaferPolicy result = SecuritySupport.GetSaferPolicy(testPathScript, null);
            if (result == SaferPolicy.Disallowed)
            {
                result = SecuritySupport.GetSaferPolicy(testPathModule, null);
            }
            return result;
        }

        private static SystemEnforcementMode GetDebugLockdownPolicy(string path)
        {
            if (PsUtils.IsRunningOnProcessorArchitectureARM())
            {
                return SystemEnforcementMode.Enforce;
            }

            s_wasSystemPolicyDebugPolicy = true;

            // Support fall-back debug hook for path exclusions on non-WOA platforms
            if (path != null)
            {
                // Assume everything under SYSTEM32 is trusted, with a purposefully sloppy
                // check so that we can actually put it in the filename during testing.
                if (path.IndexOf("System32", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return SystemEnforcementMode.None;
                }

                using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default))
                {
                    using (RegistryKey wldpPolicy = hklm.OpenSubKey("SYSTEM\\CurrentControlSet\\Control\\CI\\TRSData"))
                    {
                        if (wldpPolicy != null)
                        {
                            object exclusionPathsKey = wldpPolicy.GetValue("TestPath");

                            wldpPolicy.Close();
                            hklm.Close();

                            if (exclusionPathsKey != null)
                            {
                                string[] exclusionPaths = (string[])exclusionPathsKey;
                                foreach (string exclusionPath in exclusionPaths)
                                {
                                    if (path.IndexOf(exclusionPath, StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        return SystemEnforcementMode.None;
                                    }
                                }
                            }
                        }
                    }
                }

                // No explicit debug allowance for the file, so return the system policy if there
                // is one.
                return s_systemLockdownPolicy.GetValueOrDefault(SystemEnforcementMode.None);
            }

            // Support fall-back debug hook for system-wide policy on non-WOA platforms
            uint pdwLockdownState = 0;
            Object result = Environment.GetEnvironmentVariable("__PSLockdownPolicy", EnvironmentVariableTarget.Machine);
            if (result != null)
            {
                pdwLockdownState = LanguagePrimitives.ConvertTo<uint>(result);
                return GetLockdownPolicyForResult(pdwLockdownState);
            }

            // If the system-wide debug policy had no preference, then there is no enforcement.
            return SystemEnforcementMode.None;
        }
        private static bool s_hadMissingWldpAssembly = false;



        /// <summary>
        /// Gets lockdown policy as applied to a COM object
        /// </summary>
        /// <returns>True if the COM object is allowed, False otherwise.</returns>
        internal static bool IsClassInApprovedList(Guid clsid)
        {
            try
            {
                WLDP_HOST_INFORMATION hostInformation = new WLDP_HOST_INFORMATION();
                hostInformation.dwRevision = WldpNativeConstants.WLDP_HOST_INFORMATION_REVISION;
                hostInformation.dwHostId = WLDP_HOST_ID.WLDP_HOST_ID_POWERSHELL;

                int pIsApproved = 0;
                int result = WldpNativeMethods.WldpIsClassInApprovedList(ref clsid, ref hostInformation, ref pIsApproved, 0);

                if (result >= 0)
                {
                    if (pIsApproved == 1)
                    {
                        // Hook for testability. If we've got an environmental override, say that ADODB.Parameter
                        // is not allowed.
                        // 0000050b-0000-0010-8000-00aa006d2ea4 = ADODB.Parameter
                        if (s_wasSystemPolicyDebugPolicy)
                        {
                            if (String.Equals(clsid.ToString(), "0000050b-0000-0010-8000-00aa006d2ea4", StringComparison.OrdinalIgnoreCase))
                            {
                                return false;
                            }
                        }

                        return true;
                    }
                }

                return false;
            }
            catch (DllNotFoundException)
            {
                // Hook for testability. IsClassInApprovedList is only called when the system is in global lockdown mode,
                // so this wouldn't be allowed in regular ConstrainedLanguage mode.
                // f6d90f11-9c73-11d3-b32e-00c04f990bb4 = MSXML2.DOMDocument
                if (String.Equals(clsid.ToString(), "f6d90f11-9c73-11d3-b32e-00c04f990bb4", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return false;
            }
        }

        private static SystemEnforcementMode GetLockdownPolicyForResult(uint pdwLockdownState)
        {
            if ((pdwLockdownState & WldpNativeConstants.WLDP_LOCKDOWN_UMCIAUDIT_FLAG) ==
                SystemPolicy.WldpNativeConstants.WLDP_LOCKDOWN_UMCIAUDIT_FLAG)
            {
                return SystemEnforcementMode.Audit;
            }
            else if ((pdwLockdownState & WldpNativeConstants.WLDP_LOCKDOWN_UMCIENFORCE_FLAG) ==
                WldpNativeConstants.WLDP_LOCKDOWN_UMCIENFORCE_FLAG)
            {
                return SystemEnforcementMode.Enforce;
            }
            else
            {
                return SystemEnforcementMode.None;
            }
        }

        internal static string DumpLockdownState(uint pdwLockdownState)
        {
            string returnValue = "";

            if ((pdwLockdownState & WldpNativeConstants.WLDP_LOCKDOWN_DEFINED_FLAG) == WldpNativeConstants.WLDP_LOCKDOWN_DEFINED_FLAG)
            {
                returnValue += "WLDP_LOCKDOWN_DEFINED_FLAG\r\n";
            }

            if ((pdwLockdownState & WldpNativeConstants.WLDP_LOCKDOWN_SECUREBOOT_FLAG) == WldpNativeConstants.WLDP_LOCKDOWN_SECUREBOOT_FLAG)
            {
                returnValue += "WLDP_LOCKDOWN_SECUREBOOT_FLAG\r\n";
            }

            if ((pdwLockdownState & WldpNativeConstants.WLDP_LOCKDOWN_DEBUGPOLICY_FLAG) == WldpNativeConstants.WLDP_LOCKDOWN_DEBUGPOLICY_FLAG)
            {
                returnValue += "WLDP_LOCKDOWN_DEBUGPOLICY_FLAG\r\n";
            }

            if ((pdwLockdownState & WldpNativeConstants.WLDP_LOCKDOWN_UMCIENFORCE_FLAG) == WldpNativeConstants.WLDP_LOCKDOWN_UMCIENFORCE_FLAG)
            {
                returnValue += "WLDP_LOCKDOWN_UMCIENFORCE_FLAG\r\n";
            }

            if ((pdwLockdownState & WldpNativeConstants.WLDP_LOCKDOWN_UMCIAUDIT_FLAG) == WldpNativeConstants.WLDP_LOCKDOWN_UMCIAUDIT_FLAG)
            {
                returnValue += "WLDP_LOCKDOWN_UMCIAUDIT_FLAG\r\n";
            }

            return returnValue;
        }

        // Overrides for features that should only be enabled in debug mode
        internal static bool XamlWorkflowSupported { get; set; }

        /// <summary>
        /// Native constants for dealing with the lockdown policy
        /// </summary>
        internal class WldpNativeConstants
        {
            internal const uint WLDP_HOST_INFORMATION_REVISION = 0x00000001;

            internal const uint WLDP_LOCKDOWN_UNDEFINED = 0;
            internal const uint WLDP_LOCKDOWN_DEFINED_FLAG = 0x80000000;
            internal const uint WLDP_LOCKDOWN_SECUREBOOT_FLAG = 1;
            internal const uint WLDP_LOCKDOWN_DEBUGPOLICY_FLAG = 2;
            internal const uint WLDP_LOCKDOWN_UMCIENFORCE_FLAG = 4;
            internal const uint WLDP_LOCKDOWN_UMCIAUDIT_FLAG = 8;
        }

        /// <summary>
        /// The different host IDs understood by the lockdown policy
        /// </summary>
        internal enum WLDP_HOST_ID
        {
            WLDP_HOST_ID_UNKNOWN = 0,
            WLDP_HOST_ID_GLOBAL = 1,
            WLDP_HOST_ID_VBA = 2,
            WLDP_HOST_ID_WSH = 3,
            WLDP_HOST_ID_POWERSHELL = 4,
            WLDP_HOST_ID_IE = 5,
            WLDP_HOST_ID_MSI = 6,
            WLDP_HOST_ID_MAX = 7,
        }

        /// <summary>
        /// Host information structure to contain the lockdown policy request
        /// </summary>
        [StructLayoutAttribute(LayoutKind.Sequential)]
        internal struct WLDP_HOST_INFORMATION
        {
            /// DWORD->unsigned int
            internal uint dwRevision;

            /// WLDP_HOST_ID->_WLDP_HOST_ID
            internal WLDP_HOST_ID dwHostId;

            /// PCWSTR->WCHAR*
            [MarshalAsAttribute(UnmanagedType.LPWStr)]
            internal string szSource;

            // HANDLE->IntPtr
            internal IntPtr hSource;
        }

        /// <summary>
        /// Native methods for dealing with the lockdown policy
        /// </summary>
        internal class WldpNativeMethods
        {
            /// Return Type: HRESULT->LONG->int
            /// pHostInformation: PWLDP_HOST_INFORMATION->_WLDP_HOST_INFORMATION*
            /// pdwLockdownState: PDWORD->DWORD*
            /// dwFlags: DWORD->unsigned int
            [DllImportAttribute("wldp.dll", EntryPoint = "WldpGetLockdownPolicy")]
            internal static extern int WldpGetLockdownPolicy(ref WLDP_HOST_INFORMATION pHostInformation, ref uint pdwLockdownState, uint dwFlags);


            /// Return Type: HRESULT->LONG->int
            /// rclsid: IID*
            /// pHostInformation: PWLDP_HOST_INFORMATION->_WLDP_HOST_INFORMATION*
            /// ptIsApproved: PBOOL->BOOL*
            /// dwFlags: DWORD->unsigned int
            [DllImportAttribute("wldp.dll", EntryPoint = "WldpIsClassInApprovedList")]
            internal static extern int WldpIsClassInApprovedList(ref Guid rclsid, ref WLDP_HOST_INFORMATION pHostInformation, ref int ptIsApproved, uint dwFlags);

            [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern int SHGetKnownFolderPath(
                [MarshalAs(UnmanagedType.LPStruct)]
                Guid rfid,
                int dwFlags,
                IntPtr hToken,
                out IntPtr pszPath);
        }
    }
}