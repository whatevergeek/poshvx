/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System.Management.Automation;
using Dbg = System.Management.Automation;

namespace Microsoft.PowerShell.Commands
{
    /// <summary>
    /// The base class for the */property commands that also take
    /// a passthrough parameter
    /// </summary>
    public class PassThroughItemPropertyCommandBase : ItemPropertyCommandBase
    {
        #region Parameters

        /// <summary>
        /// Gets or sets the passthrough parameter to the command
        /// </summary>
        [Parameter]
        public SwitchParameter PassThru
        {
            get
            {
                return _passThrough;
            } // get

            set
            {
                _passThrough = value;
            } // set
        } // PassThru

        /// <summary>
        /// Gets or sets the force property
        /// </summary>
        ///
        /// <remarks>
        /// Gives the provider guidance on how vigorous it should be about performing
        /// the operation. If true, the provider should do everything possible to perform
        /// the operation. If false, the provider should attempt the operation but allow
        /// even simple errors to terminate the operation.
        /// For example, if the user tries to copy a file to a path that already exists and
        /// the destination is read-only, if force is true, the provider should copy over
        /// the existing read-only file. If force is false, the provider should write an error.
        /// </remarks>
        ///
        [Parameter]
        public override SwitchParameter Force
        {
            get
            {
                return base.Force;
            }
            set
            {
                base.Force = value;
            }
        } // Force

        #endregion Parameters

        #region parameter data

        /// <summary>
        /// Determines if the property returned from the provider should
        /// be passed through to the pipeline.
        /// </summary>
        private bool _passThrough;

        #endregion parameter data

        #region protected members

        /// <summary>
        /// Determines if the provider for the specified path supports ShouldProcess
        /// </summary>
        /// <value></value>
        protected override bool ProviderSupportsShouldProcess
        {
            get
            {
                return base.DoesProviderSupportShouldProcess(base.paths);
            }
        }

        /// <summary>
        /// Initializes a CmdletProviderContext instance to the current context of
        /// the command.
        /// </summary>
        /// 
        /// <returns>
        /// A CmdletProviderContext instance initialized to the context of the current
        /// command.
        /// </returns>
        /// 
        internal CmdletProviderContext GetCurrentContext()
        {
            CmdletProviderContext currentCommandContext = CmdletProviderContext;
            currentCommandContext.PassThru = PassThru;
            return currentCommandContext;
        } // GetCurrentContext

        #endregion protected members
    } // PassThroughItemPropertyCommandBase
} // namespace Microsoft.PowerShell.Commands

