﻿//-----------------------------------------------------------------------
// <copyright file="TextDoesNotContainFilterRule.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace Microsoft.Management.UI.Internal
{
    using System;

    /// <summary>
    /// The TextDoesNotContainFilterRule class evaluates a string item to 
    /// check if it is does not contain the rule's value within it.
    /// </summary>
    [Serializable]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.MSInternal", "CA903:InternalNamespaceShouldNotContainPublicTypes")]
    public class TextDoesNotContainFilterRule : TextContainsFilterRule
    {
        /// <summary>
        /// Initializes a new instance of the TextDoesNotContainFilterRule class.
        /// </summary>
        public TextDoesNotContainFilterRule()
        {
            this.DisplayName = UICultureResources.FilterRule_DoesNotContain;
            this.DefaultNullValueEvaluation = true;
        }

        /// <summary>
        /// Determines if Value is not contained within data.
        /// </summary>
        /// <param name="data">
        /// The data to compare with.
        /// </param>
        /// <returns>
        /// Returns true if data does not contain Value, false otherwise.
        /// </returns>
        protected override bool Evaluate(string data)
        {
            return !base.Evaluate(data);
        }
    }
}
