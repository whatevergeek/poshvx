/*============================================================================
 * Copyright (C) Microsoft Corporation, All rights reserved. 
 *============================================================================
 */

#region Using directives

#endregion

namespace Microsoft.Management.Infrastructure.CimCmdlets
{
    /// <summary>
    /// <para>
    /// Write result object to ps pipeline
    /// </para>
    /// </summary>
    internal sealed class CimWriteResultObject : CimBaseAction
    {
        /// <summary>
        /// Constructor
        /// </summary>
        public CimWriteResultObject(object result, XOperationContextBase theContext)
        {
            this.result = result;
            this.Context = theContext;
        }

        /// <summary>
        /// <para>
        /// Write result object to ps pipeline
        /// </para>
        /// </summary>
        /// <param name="cmdlet"></param>
        public override void Execute(CmdletOperationBase cmdlet)
        {
            ValidationHelper.ValidateNoNullArgument(cmdlet, "cmdlet");
            cmdlet.WriteObject(result, this.Context);
        }

        #region members
        /// <summary>
        /// result object
        /// </summary>
        internal object Result
        {
            get
            {
                return result;
            }
        }
        private object result;
        #endregion
    }//End Class

}//End namespace
