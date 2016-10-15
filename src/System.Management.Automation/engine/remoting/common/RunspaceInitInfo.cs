/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using Dbg = System.Management.Automation.Diagnostics;

namespace System.Management.Automation.Remoting
{
    /// <summary>
    /// Class that encapsulates the information carried by the RunspaceInitInfo PSRP message
    /// </summary>
    internal class RunspacePoolInitInfo
    {
        /// <summary>
        /// Min Runspaces setting on the server runspace pool
        /// </summary>
        internal int MinRunspaces { get; }

        /// <summary>
        /// Max Runspaces setting on the server runspace pool
        /// </summary>
        internal int MaxRunspaces { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="minRS"></param>
        /// <param name="maxRS"></param>
        internal RunspacePoolInitInfo(int minRS, int maxRS)
        {
            MinRunspaces = minRS;
            MaxRunspaces = maxRS;
        }
    }
}