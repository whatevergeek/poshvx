/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System.Management.Automation.Internal;

namespace System.Management.Automation
{
    /// <summary>
    /// Derives InternalCommand for Native Commands
    /// </summary>
    internal sealed class NativeCommand : InternalCommand
    {
        private NativeCommandProcessor _myCommandProcessor;
        internal NativeCommandProcessor MyCommandProcessor
        {
            get { return _myCommandProcessor; }
            set { _myCommandProcessor = value; }
        }

        /// <summary>
        /// Implement the stop functionality for native commands...
        /// </summary>
        internal override void DoStopProcessing()
        {
            try
            {
                if (_myCommandProcessor != null)
                    _myCommandProcessor.StopProcessing();
            }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                // Ignore exceptions here...
                ;
            }
        }
    }
}
