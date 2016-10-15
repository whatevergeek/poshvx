/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System.Globalization;
using System.Diagnostics;

namespace System.Management.Automation.Help
{
    /// <summary>
    /// Updatable help system internal representation of the PSModuleInfo class
    /// </summary>
    internal class UpdatableHelpModuleInfo
    {
        internal static readonly string HelpContentZipName = "HelpContent.cab";
        internal static readonly string HelpIntoXmlName = "HelpInfo.xml";

        /// <summary>
        /// Class constructor
        /// </summary>
        /// <param name="name">module name</param>
        /// <param name="guid">module GUID</param>
        /// <param name="path">module path</param>
        /// <param name="uri">HelpInfo URI</param>
        internal UpdatableHelpModuleInfo(string name, Guid guid, string path, string uri)
        {
            Debug.Assert(!String.IsNullOrEmpty(name));
            Debug.Assert(guid != null);
            Debug.Assert(!String.IsNullOrEmpty(path));
            Debug.Assert(!String.IsNullOrEmpty(uri));

            ModuleName = name;
            _moduleGuid = guid;
            ModuleBase = path;
            HelpInfoUri = uri;
        }

        /// <summary>
        /// Module name
        /// </summary>
        internal string ModuleName { get; }

        /// <summary>
        /// Module GUID
        /// </summary>
        internal Guid ModuleGuid
        {
            get
            {
                return _moduleGuid;
            }
        }
        private Guid _moduleGuid;

        /// <summary>
        /// Module path
        /// </summary>
        internal string ModuleBase { get; }

        /// <summary>
        /// HelpInfo URI
        /// </summary>
        internal string HelpInfoUri { get; }

        /// <summary>
        /// Gets the combined HelpContent.zip name
        /// </summary>
        /// <param name="culture">current culture</param>
        /// <returns>HelpContent name</returns>
        internal string GetHelpContentName(CultureInfo culture)
        {
            Debug.Assert(culture != null);

            return ModuleName + "_" + _moduleGuid.ToString() + "_" + culture.Name + "_" + HelpContentZipName;
        }

        /// <summary>
        /// Gets the combined HelpInfo.xml name
        /// </summary>
        /// <returns>HelpInfo name</returns>
        internal string GetHelpInfoName()
        {
            return ModuleName + "_" + _moduleGuid.ToString() + "_" + HelpIntoXmlName;
        }
    }
}
