//-----------------------------------------------------------------------
//     Copyright © Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace Microsoft.PowerShell.Commands.ShowCommandExtension
{
    using System;
    using System.Management.Automation;

    /// <summary>
    /// Implements a facade around PSModuleInfo and its deserialized counterpart
    /// </summary>
    public class ShowCommandModuleInfo
    {
        /// <summary>
        /// Creates an instance of the ShowCommandModuleInfo class based on a CommandInfo object
        /// </summary>
        /// 
        /// <param name="other">
        /// The object to wrap.
        /// </param>
        public ShowCommandModuleInfo(PSModuleInfo other)
        {
            if (null == other)
            {
                throw new ArgumentNullException("other");
            }

            this.Name = other.Name;
        }

        /// <summary>
        /// Creates an instance of the ShowCommandModuleInfo class based on a PSObject object
        /// </summary>
        /// 
        /// <param name="other">
        /// The object to wrap.
        /// </param>
        public ShowCommandModuleInfo(PSObject other)
        {
            if (null == other)
            {
                throw new ArgumentNullException("other");
            }

            this.Name = other.Members["Name"].Value as string;
        }

        /// <summary>
        /// Gets the name of this module
        /// </summary>
        public string Name { get; private set; }
    }
}