//-----------------------------------------------------------------------
// <copyright file="DefaultStringConverter.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
// <summary>
// Implements the DefaultStringConverter control.
//</summary>
//-----------------------------------------------------------------------

namespace Microsoft.Management.UI.Internal
{
    #region Using
    using System;
    using System.Windows.Data;
    #endregion Using

    /// <summary>
    /// Converts the value of the single <see cref="Binding"/> in a
    /// <see cref="MultiBinding"/> to a string,
    /// and returns that string if not null/empty,
    /// otherwise returns DefaultValue.
    /// The <see cref="MultiBinding"/> must have exactly one
    /// <see cref="Binding"/>.
    /// </summary>
    /// <remarks>
    /// The problem solved by this <see cref="IMultiValueConverter"/>
    /// is that for an ordinary <see cref="Binding"/> which is bound to
    /// "Path=PropertyA.PropertyB", the Converter is not called if the value
    /// of PropertyA was null (and therefore PropertyB could not be accessed).
    /// By contrast, the converter for an <see cref="IMultiValueConverter"/>
    /// will be called even if any or all of the bindings fail to evaluate
    /// down to the last property.
    /// 
    /// Note that the <see cref="MultiBinding"/> which uses this
    /// <see cref="IMultiValueConverter"/> must have exactly one
    /// <see cref="Binding"/>.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.MSInternal", "CA903:InternalNamespaceShouldNotContainPublicTypes")]
    public class DefaultStringConverter : IMultiValueConverter
    {
        /// <summary>
        /// Gets or sets default string returned by the converter
        /// if the value is null/empty.
        /// </summary>
        public string DefaultValue
        {
            get;
            set;
        }

        /// <summary>
        /// Converts the value of the single <see cref="Binding"/> in the
        /// <see cref="IMultiValueConverter"/> to a string,
        /// and returns that string if not null/empty,
        /// otherwise returns DefaultValue.
        /// </summary>
        /// <param name="values">
        /// Must contain exactly one value, of any type.
        /// </param>
        /// <param name="targetType">The parameter is not used.</param>
        /// <param name="parameter">The parameter is not used.</param>
        /// <param name="culture">The parameter is not used.</param>
        /// <returns>
        /// A string, either the value of the first <see cref="Binding"/>
        /// converted to string, or DefaultValue.
        /// </returns>
        /// <remarks>
        /// Note that the <see cref="MultiBinding"/> which uses this
        /// <see cref="IMultiValueConverter"/> must have exactly one
        /// <see cref="Binding"/>.
        /// </remarks>
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (values == null || 1 != values.Length)
            {
                throw new ArgumentNullException("values");
            }

            string val = values[0] as string;
            if (!String.IsNullOrEmpty(val))
            {
                return val;
            }

            return this.DefaultValue;
        }

        /// <summary>
        /// Skip ConvertBack binding.
        /// </summary>
        /// <param name="value">The parameter is not used.</param>
        /// <param name="targetTypes">The parameter is not used.</param>
        /// <param name="parameter">The parameter is not used.</param>
        /// <param name="culture">The parameter is not used.</param>
        /// <returns>Binding.DoNothing blocks ConvertBack binding.</returns>
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        {
            return new object[1] { Binding.DoNothing };
        }
    }
}
