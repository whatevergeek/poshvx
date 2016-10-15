/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

namespace System.Management.Automation
{
    /// <summary>
    /// A variable that represents $PSCulture
    /// </summary>
    internal class PSCultureVariable : PSVariable
    {
        /// <summary>
        /// Constructs an instance of the variable.
        /// </summary>
        internal PSCultureVariable()
            : base(SpecialVariables.PSCulture, true, ScopedItemOptions.ReadOnly | ScopedItemOptions.AllScope,
                   RunspaceInit.DollarPSCultureDescription)
        {
        }

        /// <summary>
        /// Gets or sets the value of the variable. 
        /// </summary>
        /// 
        public override object Value
        {
            get
            {
                DebuggerCheckVariableRead();
                return System.Globalization.CultureInfo.CurrentCulture.Name;
            }
        }
    }

    /// <summary>
    /// A variable that represents $PSUICulture
    /// </summary>
    internal class PSUICultureVariable : PSVariable
    {
        /// <summary>
        /// Constructs an instance of the variable.
        /// </summary>
        internal PSUICultureVariable()
            : base(SpecialVariables.PSUICulture, true, ScopedItemOptions.ReadOnly | ScopedItemOptions.AllScope,
                   RunspaceInit.DollarPSUICultureDescription)
        {
        }

        /// <summary>
        /// Gets or sets the value of the variable. 
        /// </summary>
        /// 
        public override object Value
        {
            get
            {
                DebuggerCheckVariableRead();
                return System.Globalization.CultureInfo.CurrentUICulture.Name;
            }
        }
    }
}
