﻿//-----------------------------------------------------------------------
// <copyright file="TextFilterRule.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace Microsoft.Management.UI.Internal
{
    using System;
    using System.Globalization;
    using System.Text.RegularExpressions;
    using System.Diagnostics;
    using System.ComponentModel;

    /// <summary>
    /// The TextFilterRule class supports derived rules by offering services for
    /// evaluating string operations.
    /// </summary>
    [Serializable]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.MSInternal", "CA903:InternalNamespaceShouldNotContainPublicTypes")]
    public abstract class TextFilterRule : SingleValueComparableValueFilterRule<string>
    {
        /// <summary>
        /// Gets a regex pattern that describes a word boundary that can include symbols.
        /// </summary>
        protected static readonly string WordBoundaryRegexPattern = @"(^|$|\W|\b)";

        private bool ignoreCase;
        private bool cultureInvariant;

        /// <summary>
        /// Gets or sets whether to ignore case when evaluating.
        /// </summary>
        public bool IgnoreCase
        {
            get
            {
                return this.ignoreCase;
            }
            set
            {
                this.ignoreCase = value;

                this.NotifyEvaluationResultInvalidated();
            }
        }

        /// <summary>
        /// Gets or sets whether culture differences in language are ignored when evaluating.
        /// </summary>
        public bool CultureInvariant
        {
            get
            {
                return this.cultureInvariant;
            }
            set
            {
                this.cultureInvariant = value;

                this.NotifyEvaluationResultInvalidated();
            }
        }

        /// <summary>
        /// Initializes a new instance of the TextFilterRule class.
        /// </summary>
        protected TextFilterRule()
        {
            this.IgnoreCase = true;
            this.CultureInvariant = false;
        }

        /// <summary>
        /// Gets the current value and determines whether it should be evaluated as an exact match.
        /// </summary>
        /// <param name="evaluateAsExactMatch">Whether the current value should be evaluated as an exact match.</param>
        /// <returns>The current value.</returns>
        protected internal string GetParsedValue(out bool evaluateAsExactMatch)
        {
            var parsedValue = this.Value.GetCastValue();

            // Consider it an exact-match value if it starts with a quote; trailing quotes and other requirements can be added later if need be \\
            evaluateAsExactMatch = parsedValue.StartsWith("\"", StringComparison.Ordinal);

            // If it's an exact-match value, remove quotes and use the exact-match pattern \\
            if (evaluateAsExactMatch)
            {
                parsedValue = parsedValue.Replace("\"", String.Empty);
            }

            return parsedValue;
        }

        /// <summary>
        /// Gets a regular expression pattern based on the current value and the specified patterns.
        /// If the current value is an exact-match string, <paramref name="exactMatchPattern"/> will be used; otherwise, <paramref name="pattern"/> will be used.
        /// </summary>
        /// <param name="pattern">The pattern to use if the current value is not an exact-match string. The pattern must contain a <c>{0}</c> token.</param>
        /// <param name="exactMatchPattern">The pattern to use if the current value is an exact-match string. The pattern must contain a <c>{0}</c> token.</param>
        /// <returns>A regular expression pattern based on the current value and the specified patterns.</returns>
        /// <exception cref="ArgumentNullException">The specified value is a null reference.</exception>
        protected internal string GetRegexPattern(string pattern, string exactMatchPattern)
        {
            if (pattern == null)
            {
                throw new ArgumentNullException("pattern");
            }

            if (exactMatchPattern == null)
            {
                throw new ArgumentNullException("exactMatchPattern");
            }

            Debug.Assert(this.IsValid);

            bool evaluateAsExactMatch;
            string value = this.GetParsedValue(out evaluateAsExactMatch);

            if (evaluateAsExactMatch)
            {
                pattern = exactMatchPattern;
            }

            value = Regex.Escape(value);

            // Format the pattern using the specified data \\
            return String.Format(CultureInfo.InvariantCulture, pattern, value);
        }

        /// <summary>
        /// Gets a <see cref="RegexOptions"/> object that matches the values of <see cref="IgnoreCase"/> and <see cref="CultureInvariant"/>.
        /// </summary>
        /// <returns>A <see cref="RegexOptions"/> object that matches the values of <see cref="IgnoreCase"/> and <see cref="CultureInvariant"/>.</returns>
        protected internal RegexOptions GetRegexOptions()
        {
            RegexOptions options = RegexOptions.None;

            if (this.IgnoreCase)
            {
                options |= RegexOptions.IgnoreCase;
            }

            if (this.CultureInvariant)
            {
                options |= RegexOptions.CultureInvariant;
            }

            return options;
        }

        /// <summary>
        /// Gets a value indicating whether the specified data matches one of the specified patterns.
        /// If the current value is an exact-match string, <paramref name="exactMatchPattern"/> will be used; otherwise, <paramref name="pattern"/> will be used.
        /// </summary>
        /// <param name="data">The data to evaluate.</param>
        /// <param name="pattern">The pattern to use if the current value is not an exact-match string. The pattern must contain a <c>{0}</c> token.</param>
        /// <param name="exactMatchPattern">The pattern to use if the current value is an exact-match string. The pattern must contain a <c>{0}</c> token.</param>
        /// <returns><c>true</c> if the specified data matches one of the specified patterns; otherwise, <c>false</c>.</returns>
        protected internal bool ExactMatchEvaluate(string data, string pattern, string exactMatchPattern)
        {
            Debug.Assert(this.IsValid);

            var parsedPattern = this.GetRegexPattern(pattern, exactMatchPattern);
            var options = this.GetRegexOptions();

            return Regex.IsMatch(data, parsedPattern, options);
        }
    }
}