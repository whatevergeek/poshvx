//-----------------------------------------------------------------------
// <copyright file="AutomationTextBlock.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace Microsoft.Management.UI.Internal
{
    #region Using Directives

    using System.ComponentModel;
    using System.Diagnostics.CodeAnalysis;
    using System.Windows.Automation.Peers;
    using System.Windows.Controls;

    #endregion

    /// <summary>
    /// Provides a <see cref="TextBlock"/> control that is always visible in the automation tree.
    /// </summary>
    [SuppressMessage("Microsoft.MSInternal", "CA903:InternalNamespaceShouldNotContainPublicTypes")]
    [Description("Provides a System.Windows.Controls.TextBlock control that is always visible in the automation tree.")]
    public class AutomationTextBlock : TextBlock
    {
        #region Structors

        /// <summary>
        /// Initializes a new instance of the <see cref="AutomationTextBlock" /> class.
        /// </summary>
        public AutomationTextBlock()
        {
            // This constructor intentionally left blank
        }

        #endregion

        #region Overrides

        /// <summary>
        /// Returns the <see cref="System.Windows.Automation.Peers.AutomationPeer"/> implementations for this control.
        /// </summary>
        /// <returns>The <see cref="System.Windows.Automation.Peers.AutomationPeer"/> implementations for this control.</returns>
        protected override AutomationPeer OnCreateAutomationPeer()
        {
            return new AutomationTextBlockAutomationPeer(this);
        }

        #endregion
    }
}
