﻿//-----------------------------------------------------------------------
// <copyright file="IsNotEmptyFilterRule.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace Microsoft.Management.UI.Internal
{
    using System;

    /// <summary>
    /// The IsNotEmptyFilterRule evaluates an item to determine whether it
    /// is empty or not. 
    /// </summary>
    [Serializable]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.MSInternal", "CA903:InternalNamespaceShouldNotContainPublicTypes")]
    public class IsNotEmptyFilterRule : IsEmptyFilterRule
    {
        /// <summary>
        /// Initializes a new instance of the IsNotEmptyFilterRule class.
        /// </summary>
        public IsNotEmptyFilterRule()
        {
            this.DisplayName = UICultureResources.FilterRule_IsNotEmpty;
        }

        /// <summary>
        /// Gets a values indicating whether the supplied item is not empty. 
        /// </summary>
        /// <param name="item">The item to evaluate.</param>
        /// <returns>
        /// Returns false if the item is null or if the item is a string 
        /// composed of whitespace. True otherwise.
        /// </returns>
        public override bool Evaluate(object item)
        {
            return !base.Evaluate(item);
        }
    }
}
