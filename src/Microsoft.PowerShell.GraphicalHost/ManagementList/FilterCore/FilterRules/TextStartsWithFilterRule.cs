﻿//-----------------------------------------------------------------------
// <copyright file="TextStartsWithFilterRule.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace Microsoft.Management.UI.Internal
{
    using System;
    using System.Diagnostics;

    /// <summary>
    /// The TextStartsWithFilterRule class evaluates a string item to 
    /// check if it starts with the rule's value.
    /// </summary>
    [Serializable]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.MSInternal", "CA903:InternalNamespaceShouldNotContainPublicTypes")]
    public class TextStartsWithFilterRule : TextFilterRule
    {
        private static readonly string TextStartsWithCharactersRegexPattern = "^{0}";
        private static readonly string TextStartsWithWordsRegexPattern = TextStartsWithCharactersRegexPattern + WordBoundaryRegexPattern;

        /// <summary>
        /// Initializes a new instance of the TextStartsWithFilterRule class.
        /// </summary>
        public TextStartsWithFilterRule()
        {
            this.DisplayName = UICultureResources.FilterRule_TextStartsWith;
        }

        /// <summary>
        /// Determines if data starts with Value.
        /// </summary>
        /// <param name="data">
        /// The value to compare with.
        /// </param>
        /// <returns>
        /// Returns true is data starts with Value, false otherwise.
        /// </returns>
        protected override bool Evaluate(string data)
        {
            Debug.Assert(this.IsValid);

            return this.ExactMatchEvaluate(data, TextStartsWithCharactersRegexPattern, TextStartsWithWordsRegexPattern);
        }
    }

}
