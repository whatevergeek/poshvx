using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml;
using System.IO;
using System.Text;
using System.Reflection;
using System.Globalization;
using System.Threading;

using System.Management.Automation;
using Microsoft.Win32;

#if CORECLR
// Some APIs are missing from System.Environment. We use System.Management.Automation.Environment as a proxy type:
//  - for missing APIs, System.Management.Automation.Environment has extension implementation.
//  - for existing APIs, System.Management.Automation.Environment redirect the call to System.Environment.
using Environment = System.Management.Automation.Environment;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
#endif

namespace System.Management.Automation
{

    /// <summary>
    /// Leverages the strategy pattern to abstract away the details of gathering properties from outside sources.
    /// Note: This is a class so that it can be internal.
    /// </summary>
    internal abstract class ConfigPropertyAccessor
    {
        #region Statics
        /// <summary>
        /// Static constructor to instantiate an instance
        /// </summary>
        static ConfigPropertyAccessor()
        {
#if CORECLR
            Instance = Platform.IsInbox
                            ? (ConfigPropertyAccessor) new RegistryAccessor() 
                            : new JsonConfigFileAccessor();
#else
            Instance = new RegistryAccessor();
#endif
        }
        /// <summary>
        /// The instance of the ConfigPropertyAccessor to use to interact with properties.
        /// Derived classes should not be directly instantiated.
        /// </summary>
        internal static readonly ConfigPropertyAccessor Instance;

        #endregion // Statics

        #region Enums

        /// <summary>
        /// Describes the scope of the property query.
        /// SystemWide properties apply to all users.
        /// CurrentUser properties apply to the current user that is impersonated.
        /// </summary>
        internal enum PropertyScope
        {
            SystemWide = 0,
            CurrentUser = 1
        }

        #endregion // Enums

        #region Interface Methods

        /// <summary>
        /// Existing Key = HKLM:\System\CurrentControlSet\Control\Session Manager\Environment
        /// Proposed value = %ProgramFiles%\PowerShell\Modules by default
        /// 
        /// Note: There is no setter because this value is immutable.
        /// </summary>
        /// <returns>Module path values from the config file.</returns>
        internal abstract string GetModulePath(PropertyScope scope);

        /// <summary>
        /// Existing Key = HKCU and HKLM\SOFTWARE\Microsoft\PowerShell\1\ShellIds\Microsoft.PowerShell
        /// Proposed value = Existing default execution policy if not already specified
        /// </summary>
        /// <param name="scope">Where it should check for the value.</param>
        /// <param name="shellId">The shell associated with this policy. Typically, it is "Microsoft.PowerShell"</param>
        /// <returns></returns>
        internal abstract string GetExecutionPolicy(PropertyScope scope, string shellId);
        internal abstract void RemoveExecutionPolicy(PropertyScope scope, string shellId);
        internal abstract void SetExecutionPolicy(PropertyScope scope, string shellId, string executionPolicy);

        /// <summary>
        /// Existing Key = HKLM\SOFTWARE\Microsoft\PowerShell\1\ShellIds
        /// Proposed value = existing default. Probably "1"
        /// </summary>
        /// <returns>Whether console prompting should happen.</returns>
        internal abstract bool GetConsolePrompting();
        internal abstract void SetConsolePrompting(bool shouldPrompt);

        /// <summary>
        /// Existing Key = HKLM\SOFTWARE\Microsoft\PowerShell
        /// Proposed value = Existing default. Probably "0"
        /// </summary>
        /// <returns>Boolean indicating whether Update-Help should prompt</returns>
        internal abstract bool GetDisablePromptToUpdateHelp();
        internal abstract void SetDisablePromptToUpdateHelp(bool prompt);

        /// <summary>
        /// Existing Key = HKCU and HKLM\Software\Policies\Microsoft\Windows\PowerShell\UpdatableHelp
        /// Proposed value = blank.This should be supported though
        /// </summary>
        /// <returns></returns>
        internal abstract string GetDefaultSourcePath();
        internal abstract void SetDefaultSourcePath(string defaultPath);

        #endregion // Interface Methods
    }

#if CORECLR
    /// <summary>
    /// JSON configuration file accessor
    ///
    /// Reads from and writes to configuration files. The values stored were 
    /// originally stored in the Windows registry.
    /// </summary>
    internal class JsonConfigFileAccessor : ConfigPropertyAccessor
    {
        private string psHomeConfigDirectory;
        private string appDataConfigDirectory;
        private const string configFileName = "PowerShellProperties.json";

        /// <summary>
        /// Lock used to enable multiple concurrent readers and singular write locks within a
        /// single process.
        /// TODO: This solution only works for IO from a single process. A more robust solution is needed to enable ReaderWriterLockSlim behavior between processes.
        /// </summary>
        private ReaderWriterLockSlim fileLock = new ReaderWriterLockSlim();

        internal JsonConfigFileAccessor()
        {
            //
            // Sets the system-wide configuration directory
            //
            Assembly assembly = typeof(PSObject).GetTypeInfo().Assembly;
            psHomeConfigDirectory = Path.GetDirectoryName(assembly.Location);

            //
            // Sets the per-user configuration directory
            //
            appDataConfigDirectory = Utils.GetUserConfigurationDirectory();
            if (!Directory.Exists(appDataConfigDirectory))
            {
                try
                {
                    Directory.CreateDirectory(appDataConfigDirectory);
                }
                catch (UnauthorizedAccessException)
                {
                    // Do nothing now. This failure shouldn't block initialization
                    appDataConfigDirectory = null;
                }
            }
        }

        /// <summary>
        /// Enables delayed creation of the user settings directory so it does 
        /// not interfere with PowerShell initialization
        /// </summary>
        /// <returns>Returns the directory if present or creatable. Throws otherwise.</returns>
        private string GetCurrentUserConfigDirectory()
        {
            if (null == appDataConfigDirectory)
            {
                string tempAppDataConfigDir = Utils.GetUserConfigurationDirectory();
                if (!Directory.Exists(tempAppDataConfigDir))
                {
                    Directory.CreateDirectory(tempAppDataConfigDir);
                    // Only assign it if creation succeeds. It will throw if it fails.
                    appDataConfigDirectory = tempAppDataConfigDir;
                }
                // Do not catch exceptions here. Let them flow up.
            }
            return appDataConfigDirectory;
        }

        /// <summary>
        /// This value is not writable via the API and must be set using a text editor.
        /// </summary>
        /// <param name="scope"></param>
        /// <returns>Value if found, null otherwise. The behavior matches ModuleIntrinsics.GetExpandedEnvironmentVariable().</returns>
        internal override string GetModulePath(PropertyScope scope)
        {
            string scopeDirectory = psHomeConfigDirectory;
            
            // Defaults to system wide.
            if (PropertyScope.CurrentUser == scope)
            {
                scopeDirectory = GetCurrentUserConfigDirectory();
            }

            string fileName = Path.Combine(scopeDirectory, configFileName);

            string modulePath = ReadValueFromFile<string>(fileName, "PsModulePath");
            if (!string.IsNullOrEmpty(modulePath))
            {
                modulePath = Environment.ExpandEnvironmentVariables(modulePath);
            }
            return modulePath;
        }

        /// <summary>
        /// Existing Key = HKCU and HKLM\SOFTWARE\Microsoft\PowerShell\1\ShellIds\Microsoft.PowerShell
        /// Proposed value = Existing default execution policy if not already specified
        /// 
        /// Schema:
        /// {
        ///     "shell ID string","ExecutionPolicy" : "execution policy string"
        /// }
        /// 
        /// TODO: In a single config file, it might be better to nest this. It is unnecessary complexity until a need arises for more nested values.
        /// </summary>
        /// <param name="scope">Whether this is a system-wide or per-user setting.</param>
        /// <param name="shellId">The shell associated with this policy. Typically, it is "Microsoft.PowerShell"</param>
        /// <returns>The execution policy if found. Null otherwise.</returns>
        internal override string GetExecutionPolicy(PropertyScope scope, string shellId)
        {
            string execPolicy = null;
            string scopeDirectory = psHomeConfigDirectory;

            // Defaults to system wide.
            if(PropertyScope.CurrentUser == scope)
            {
                scopeDirectory = GetCurrentUserConfigDirectory();
            }

            string fileName = Path.Combine(scopeDirectory, configFileName);
            string valueName = string.Concat(shellId, ":", "ExecutionPolicy");
            string rawExecPolicy = ReadValueFromFile<string>(fileName, valueName);

            if (!String.IsNullOrEmpty(rawExecPolicy))
            {
                execPolicy = rawExecPolicy;
            }
            return execPolicy;
        }

        internal override void RemoveExecutionPolicy(PropertyScope scope, string shellId)
        {
            string scopeDirectory = psHomeConfigDirectory;

            // Defaults to system wide.
            if (PropertyScope.CurrentUser == scope)
            {
                scopeDirectory = GetCurrentUserConfigDirectory();
            }

            string fileName = Path.Combine(scopeDirectory, configFileName);
            string valueName = string.Concat(shellId, ":", "ExecutionPolicy");
            RemoveValueFromFile<string>(fileName, valueName);
        }

        internal override void SetExecutionPolicy(PropertyScope scope, string shellId, string executionPolicy)
        {
            string scopeDirectory = psHomeConfigDirectory;

            // Defaults to system wide.
            if (PropertyScope.CurrentUser == scope)
            {
                scopeDirectory = GetCurrentUserConfigDirectory();
            }

            string fileName = Path.Combine(scopeDirectory, configFileName);
            string valueName = string.Concat(shellId, ":", "ExecutionPolicy");
            WriteValueToFile<string>(fileName, valueName, executionPolicy);
        }

        /// <summary>
        /// Existing Key = HKLM\SOFTWARE\Microsoft\PowerShell\1\ShellIds
        /// Proposed value = existing default. Probably "1"
        /// 
        /// Schema:
        /// {
        ///     "ConsolePrompting" : bool
        /// }
        /// </summary>
        /// <returns>Whether console prompting should happen. If the value cannot be read it defaults to false.</returns>
        internal override bool GetConsolePrompting()
        {
            string fileName = Path.Combine(psHomeConfigDirectory, configFileName);
            return ReadValueFromFile<bool>(fileName, "ConsolePrompting");
        }

        internal override void SetConsolePrompting(bool shouldPrompt)
        {
            string fileName = Path.Combine(psHomeConfigDirectory, configFileName);
            WriteValueToFile<bool>(fileName, "ConsolePrompting", shouldPrompt);
        }

        /// <summary>
        /// Existing Key = HKLM\SOFTWARE\Microsoft\PowerShell
        /// Proposed value = Existing default. Probably "0"
        /// 
        /// Schema:
        /// {
        ///     "DisablePromptToUpdateHelp" : bool
        /// }
        /// </summary>
        /// <returns>Boolean indicating whether Update-Help should prompt. If the value cannot be read, it defaults to false.</returns>
        internal override bool GetDisablePromptToUpdateHelp()
        {
            string fileName = Path.Combine(psHomeConfigDirectory, configFileName);
            return ReadValueFromFile<bool>(fileName, "DisablePromptToUpdateHelp");
        }

        internal override void SetDisablePromptToUpdateHelp(bool prompt)
        {
            string fileName = Path.Combine(psHomeConfigDirectory, configFileName);
            WriteValueToFile<bool>(fileName, "DisablePromptToUpdateHelp", prompt);
        }

        /// <summary>
        /// Existing Key = HKCU and HKLM\Software\Policies\Microsoft\Windows\PowerShell\UpdatableHelp
        /// Proposed value = blank.This should be supported though
        /// 
        /// Schema:
        /// {
        ///     "DefaultSourcePath" : "path to local updatable help location"
        /// }
        /// </summary>
        /// <returns>The source path if found, null otherwise.</returns>
        internal override string GetDefaultSourcePath()
        {
            string fileName = Path.Combine(psHomeConfigDirectory, configFileName);

            string rawExecPolicy = ReadValueFromFile<string>(fileName, "DefaultSourcePath");

            if (!String.IsNullOrEmpty(rawExecPolicy))
            {
                return rawExecPolicy;
            }
            return null;
        }

        internal override void SetDefaultSourcePath(string defaultPath)
        {
            string fileName = Path.Combine(psHomeConfigDirectory, configFileName);

            WriteValueToFile<string>(fileName, "DefaultSourcePath", defaultPath);
        }

        private T ReadValueFromFile<T>(string fileName, string key)
        {
            fileLock.EnterReadLock();
            try
            {
                // Open file for reading, but allow multiple readers
                using (FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (StreamReader streamRdr = new StreamReader(fs))
                using (JsonTextReader jsonReader = new JsonTextReader(streamRdr))
                {
                    JObject jsonObject = (JObject) JToken.ReadFrom(jsonReader);
                    JToken value = jsonObject.GetValue(key);
                    if (null != value)
                    {
                        return value.ToObject<T>();
                    }
                }
            }
            catch (FileNotFoundException)
            {
                // The file doesn't exist. Treat this the same way as if the 
                // key was not present in the file.
            }
            finally
            {
                fileLock.ExitReadLock();
            }

            return default(T);
        }

        /// <summary>
        /// TODO: Should this return success fail or throw?
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="fileName"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="addValue">Whether the key-value pair should be added to or removed from the file</param>
        private void UpdateValueInFile<T>(string fileName, string key, T value, bool addValue)
        {
            fileLock.EnterWriteLock();
            try
            {
                // Since multiple properties can be in a single file, replacement
                // is required instead of overwrite if a file already exists.
                // Handling the read and write operations within a single FileStream
                // prevents other processes from reading or writing the file while
                // the update is in progress. It also locks out readers during write
                // operations.
                using (FileStream fs = new FileStream(fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    JObject jsonObject = null;

                    // UTF8, BOM detection, and bufferSize are the same as the basic stream constructor.
                    // The most important parameter here is the last one, which keeps the StreamReader 
                    // (and FileStream) open during Dispose so that it can be reused for the write
                    // operation.
                    using (StreamReader streamRdr = new StreamReader(fs, Encoding.UTF8, true, 1024, true))
                    using (JsonTextReader jsonReader = new JsonTextReader(streamRdr))
                    {
                        // Safely determines whether there is content to read from the file
                        bool isReadSuccess = jsonReader.Read();
                        if (isReadSuccess)
                        {
                            // Read the stream into a root JObject for manipulation
                            jsonObject = (JObject) JToken.ReadFrom(jsonReader);
                            JProperty propertyToModify = jsonObject.Property(key);

                            if (null == propertyToModify)
                            {
                                // The property doesn't exist, so add it
                                if (addValue)
                                {
                                    jsonObject.Add(new JProperty(key, value));
                                }
                                // else the property doesn't exist so there is nothing to remove
                            }
                            // The property exists
                            else
                            {
                                if (addValue)
                                {
                                    propertyToModify.Replace(new JProperty(key, value));
                                }
                                else
                                {
                                    propertyToModify.Remove();
                                }
                            }
                        }
                        else
                        {
                            // The file doesn't already exist and we want to write to it 
                            // or it exists with no content.
                            // A new file will be created that contains only this value.
                            // If the file doesn't exist and a we don't want to write to it, no
                            // action is necessary.
                            if (addValue)
                            {
                                jsonObject = new JObject(new JProperty(key, value));
                            }
                            else
                            {
                                return;
                            }
                        }
                    }

                    // Reset the stream position to the beginning so that the 
                    // changes to the file can be written to disk
                    fs.Seek(0, SeekOrigin.Begin);

                    // Update the file with new content
                    using (StreamWriter streamWriter = new StreamWriter(fs))
                    using (JsonTextWriter jsonWriter = new JsonTextWriter(streamWriter))
                    {
                        // The entire document exists within the root JObject.
                        // I just need to write that object to produce the document.
                        jsonObject.WriteTo(jsonWriter);

                        // This trims the file if the file shrank. If the file grew,
                        // it is a no-op. The purpose is to trim extraneous characters 
                        // from the file stream when the resultant JObject is smaller
                        // than the input JObject.
                        fs.SetLength(fs.Position);
                    }
                }
            }
            finally
            {
                fileLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// TODO: Should this return success, fail, or throw?
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="fileName"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        private void WriteValueToFile<T>(string fileName, string key, T value)
        {
            UpdateValueInFile<T>(fileName, key, value, true);
        }

        /// <summary>
        /// TODO: Should this return success, fail, or throw?
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="fileName"></param>
        /// <param name="key"></param>
        private void RemoveValueFromFile<T>(string fileName, string key)
        {
            UpdateValueInFile<T>(fileName, key, default(T), false);
        }
    }

#endif // CORECLR

    internal class RegistryAccessor : ConfigPropertyAccessor
    {
        private const string DisablePromptToUpdateHelpRegPath = "Software\\Microsoft\\PowerShell";
        private const string DisablePromptToUpdateHelpRegPath32 = "Software\\Wow6432Node\\Microsoft\\PowerShell";
        private const string DisablePromptToUpdateHelpRegKey = "DisablePromptToUpdateHelp";
        private const string DefaultSourcePathRegPath = "Software\\Policies\\Microsoft\\Windows\\PowerShell\\UpdatableHelp";
        private const string DefaultSourcePathRegKey = "DefaultSourcePath";

        internal RegistryAccessor()
        {
        }

        /// <summary>
        /// Gets the specified module path from the appropriate Environment entry in the registry.
        /// </summary>
        /// <param name="scope"></param>
        /// <returns>The specified module path. Null if not present.</returns>
        internal override string GetModulePath(PropertyScope scope)
        {
            if (PropertyScope.CurrentUser == scope)
            {
                return ModuleIntrinsics.GetExpandedEnvironmentVariable("PSMODULEPATH", EnvironmentVariableTarget.User);
            }
            else
            {
                return ModuleIntrinsics.GetExpandedEnvironmentVariable("PSMODULEPATH", EnvironmentVariableTarget.Machine);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="scope"></param>
        /// <param name="shellId"></param>
        /// <returns>The execution policy string if found, otherwise null.</returns>
        internal override string GetExecutionPolicy(PropertyScope scope, string shellId)
        {
            string regKeyName = Utils.GetRegistryConfigurationPath(shellId);
            RegistryKey scopedKey = Registry.LocalMachine;

            // Override if set to another value;
            if (PropertyScope.CurrentUser == scope)
            {
                scopedKey = Registry.CurrentUser;
            }

            return GetRegistryString(scopedKey, regKeyName, "ExecutionPolicy");
        }

        internal override void SetExecutionPolicy(PropertyScope scope, string shellId, string executionPolicy)
        {
            string regKeyName = Utils.GetRegistryConfigurationPath(shellId);
            RegistryKey scopedKey = Registry.LocalMachine;

            // Override if set to another value;
            if (PropertyScope.CurrentUser == scope)
            {
                scopedKey = Registry.CurrentUser;
            }

            using (RegistryKey key = scopedKey.CreateSubKey(regKeyName))
            {
                if (null != key)
                {
                    key.SetValue("ExecutionPolicy", executionPolicy, RegistryValueKind.String);
                }
            }
        }

        internal override void RemoveExecutionPolicy(PropertyScope scope, string shellId)
        {
            string regKeyName = Utils.GetRegistryConfigurationPath(shellId);
            RegistryKey scopedKey = Registry.LocalMachine;

            // Override if set to another value;
            if (PropertyScope.CurrentUser == scope)
            {
                scopedKey = Registry.CurrentUser;
            }

            using (RegistryKey key = scopedKey.OpenSubKey(regKeyName, true))
            {
                if (key != null)
                {
                    if (key.GetValue("ExecutionPolicy") != null)
                        key.DeleteValue("ExecutionPolicy");
                }
            }
        }

        /// <summary>
        /// Existing Key = HKLM\SOFTWARE\Microsoft\PowerShell\1\ShellIds
        /// Proposed value = existing default. Probably "1"
        /// </summary>
        /// <returns>Whether console prompting should happen.</returns>
        internal override bool GetConsolePrompting()
        {
            string policyKeyName = Utils.GetRegistryConfigurationPrefix();
            string tempPrompt = GetRegistryString(Registry.LocalMachine, policyKeyName, "ConsolePrompting");

            if (null != tempPrompt)
            {
                return Convert.ToBoolean(tempPrompt, CultureInfo.InvariantCulture); 
            }
            else
            {
                return false;
            }
        }

        internal override void SetConsolePrompting(bool shouldPrompt)
        {
            string policyKeyName = Utils.GetRegistryConfigurationPrefix();
            SetRegistryString(Registry.LocalMachine, policyKeyName, "ConsolePrompting", shouldPrompt.ToString());
        }

        /// <summary>
        /// Existing Key = HKLM\SOFTWARE\Microsoft\PowerShell
        /// Proposed value = Existing default. Probably "0"
        /// </summary>
        /// <returns>Boolean indicating whether Update-Help should prompt</returns>
        internal override bool GetDisablePromptToUpdateHelp()
        {
            using (RegistryKey hklm = Registry.LocalMachine.OpenSubKey(DisablePromptToUpdateHelpRegPath))
            {
                if (hklm != null)
                {
                    object disablePromptToUpdateHelp = hklm.GetValue(DisablePromptToUpdateHelpRegKey, null, RegistryValueOptions.None);

                    if (disablePromptToUpdateHelp == null)
                    {
                        return true;
                    }
                    else
                    {
                        int result;

                        if (LanguagePrimitives.TryConvertTo<int>(disablePromptToUpdateHelp, out result))
                        {
                            return (result != 1);
                        }

                        return true;
                    }
                }
                else
                {
                    return true;
                }
            }
        }

        internal override void SetDisablePromptToUpdateHelp(bool prompt)
        {
            int valueToSet = prompt ? 1 : 0;
            using (RegistryKey hklm = Registry.LocalMachine.OpenSubKey(DisablePromptToUpdateHelpRegPath, true))
            {
                if (hklm != null)
                {
                    hklm.SetValue(DisablePromptToUpdateHelpRegKey, valueToSet, RegistryValueKind.DWord);
                }
            }

            using (RegistryKey hklm = Registry.LocalMachine.OpenSubKey(DisablePromptToUpdateHelpRegPath32, true))
            {
                if (hklm != null)
                {
                    hklm.SetValue(DisablePromptToUpdateHelpRegKey, valueToSet, RegistryValueKind.DWord);
                }
            }
        }

        /// <summary>
        /// Existing Key = HKCU and HKLM\Software\Policies\Microsoft\Windows\PowerShell\UpdatableHelp
        /// Proposed value = blank.This should be supported though
        /// </summary>
        /// <returns></returns>
        internal override string GetDefaultSourcePath()
        {
            return GetRegistryString(Registry.LocalMachine, DefaultSourcePathRegPath, DefaultSourcePathRegKey);
        }

        internal override void SetDefaultSourcePath(string defaultPath)
        {
            SetRegistryString(Registry.LocalMachine, DefaultSourcePathRegPath, DefaultSourcePathRegKey, defaultPath);
        }

        /// <summary>
        /// Reads a DWORD from the Registry. Exceptions are intentionally allowed to pass through to 
        /// the caller because different classes and methods within the code base handle Registry 
        /// exceptions differently. Some suppress exceptions and others pass them to the user.
        /// </summary>
        /// <param name="rootKey"></param>
        /// <param name="pathToKey"></param>
        /// <param name="valueName"></param>
        /// <returns></returns>
        private int? GetRegistryDword(RegistryKey rootKey, string pathToKey, string valueName)
        {
            using (RegistryKey regKey = rootKey.OpenSubKey(pathToKey))
            {
                if (null == regKey)
                {
                    // Key not found
                    return null;
                }

                // verify the value kind as a string
                RegistryValueKind kind = regKey.GetValueKind(valueName);

                if (kind == RegistryValueKind.DWord)
                {
                    return regKey.GetValue(valueName) as int?;
                }
                else
                {
                    // The function expected a DWORD, but got another type. This is a coding error or a registry key typing error.
                    return null;
                }
            }
        }

        /// <summary>
        /// Exceptions are intentionally allowed to pass through to 
        /// the caller because different classes and methods within the code base handle Registry 
        /// exceptions differently. Some suppress exceptions and others pass them to the user.
        /// </summary>
        /// <param name="rootKey"></param>
        /// <param name="pathToKey"></param>
        /// <param name="valueName"></param>
        /// <param name="value"></param>
        private void SetRegistryDword(RegistryKey rootKey, string pathToKey, string valueName, int value)
        {
            using (RegistryKey regKey = rootKey.OpenSubKey(pathToKey))
            {
                if (null != regKey)
                {
                    regKey.SetValue(valueName, value, RegistryValueKind.DWord);
                }
            }
        }

        /// <summary>
        /// Exceptions are intentionally allowed to pass through to 
        /// the caller because different classes and methods within the code base handle Registry 
        /// exceptions differently. Some suppress exceptions and others pass them to the user.
        /// </summary>
        /// <param name="rootKey"></param>
        /// <param name="pathToKey"></param>
        /// <param name="valueName"></param>
        /// <returns></returns>
        private string GetRegistryString(RegistryKey rootKey, string pathToKey, string valueName)
        {
            using (RegistryKey regKey = rootKey.OpenSubKey(pathToKey))
            {
                if (null == regKey)
                {
                    // Key not found
                    return null;
                }

                object regValue = regKey.GetValue(valueName);
                if (null != regValue)
                {
                    // verify the value kind as a string
                    RegistryValueKind kind = regKey.GetValueKind(valueName);

                    if (kind == RegistryValueKind.ExpandString ||
                        kind == RegistryValueKind.String)
                    {
                        return regValue as string;
                    }
                }

                // The function expected a string, but got another type or the value doesn't exist.
                return null;
            }
        }

        /// <summary>
        /// Exceptions are intentionally allowed to pass through to 
        /// the caller because different classes and methods within the code base handle Registry 
        /// exceptions differently. Some suppress exceptions and others pass them to the user.
        /// </summary>
        /// <param name="rootKey"></param>
        /// <param name="pathToKey"></param>
        /// <param name="valueName"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        private void SetRegistryString(RegistryKey rootKey, string pathToKey, string valueName, string value)
        {
            using (RegistryKey key = rootKey.CreateSubKey(pathToKey))
            {
                if (null != key)
                {
                    key.SetValue(valueName, value, RegistryValueKind.String);
                }
            }
        }
    }
} // Namespace System.Management.Automation

