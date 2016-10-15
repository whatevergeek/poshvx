//-----------------------------------------------------------------------
// <copyright file="PropertyValueGetter.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace Microsoft.Management.UI.Internal
{
    using System;
    using System.ComponentModel;
    using System.Data;
    using System.Globalization;

    /// <summary>
    /// Provides methods for retrieving the property values of objects.
    /// </summary>
    public class PropertyValueGetter : IPropertyValueGetter
    {
        private const string PropertyDescriptorColumnId = "PropertyDescriptor";
        private DataTable cachedProperties;

        /// <summary>
        /// Initializes a new instance of the <see cref="PropertyValueGetter"/> class.
        /// </summary>
        public PropertyValueGetter()
        {
            // Create the table locally first so that FxCop detects the setting of Locale \\
            var cachedProperties = new DataTable();
            cachedProperties.Locale = CultureInfo.InvariantCulture;
            var dataTypeColumn = cachedProperties.Columns.Add("Type", typeof(Type));
            var propertyNameColumn = cachedProperties.Columns.Add("PropertyName", typeof(string));
            cachedProperties.Columns.Add(PropertyDescriptorColumnId, typeof(PropertyDescriptor));

            cachedProperties.PrimaryKey = new DataColumn[] { dataTypeColumn, propertyNameColumn };

            this.cachedProperties = cachedProperties;
        }

        /// <summary>Gets the value of the specified property on the specified object.</summary>
        /// <param name="propertyName">The name of the property to get the value for.</param>
        /// <param name="value">The object to get value from.</param>
        /// <param name="propertyValue">The value of the property.</param>
        /// <returns><c>true</c> if the property value could be retrieved; otherwise, <c>false</c>.</returns>
        public virtual bool TryGetPropertyValue(string propertyName, object value, out object propertyValue)
        {
            propertyValue = null;

            if (String.IsNullOrEmpty(propertyName))
            {
                throw new ArgumentException("propertyName is empty", "propertyName");
            }

            if (null == value)
            {
                throw new ArgumentNullException("value");
            }

            PropertyDescriptor descriptor = this.GetPropertyDescriptor(propertyName, value);
            if (null == descriptor)
            {
                return false;
            }

            return this.TryGetPropertyValueInternal(descriptor, value, out propertyValue);
        }

        /// <summary>Gets the value of the specified property on the specified object.</summary>
        /// <typeparam name="T">The type of the property value.</typeparam>
        /// <param name="propertyName">The name of the property to get the value for.</param>
        /// <param name="value">The object to get value from.</param>
        /// <param name="propertyValue">The value of the property if it exists; otherwise, <c>default(T)</c>.</param>
        /// <returns><c>true</c> if the property value of the specified type could be retrieved; otherwise, <c>false</c>.</returns>
        public virtual bool TryGetPropertyValue<T>(string propertyName, object value, out T propertyValue)
        {
            propertyValue = default(T);

            object uncastPropertyValue;
            if (!this.TryGetPropertyValue(propertyName, value, out uncastPropertyValue))
            {
                return false;
            }

            return FilterUtilities.TryCastItem<T>(uncastPropertyValue, out propertyValue);
        }

        private PropertyDescriptor GetPropertyDescriptor(string propertyName, object value)
        {
            var dataType = value.GetType();
            var propertyRow = this.cachedProperties.Rows.Find(new object[]
            {
                dataType,
                propertyName
            });

            PropertyDescriptor descriptor;

            if (null == propertyRow)
            {
                PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(value);

                descriptor = properties[propertyName];
                if (null != descriptor)
                {
                    this.cachedProperties.Rows.Add(dataType, propertyName, descriptor);
                }
            }
            else
            {
                descriptor = (PropertyDescriptor)propertyRow[PropertyDescriptorColumnId];
            }

            return descriptor;
        }

        private bool TryGetPropertyValueInternal(PropertyDescriptor descriptor, object value, out object propertyValue)
        {
            propertyValue = null;
            try
            {
                propertyValue = descriptor.GetValue(value);
                return true;
            }
            catch (Exception e)
            {
                if (e is AccessViolationException || e is StackOverflowException)
                {
                    throw;
                }
            }

            return false;
        }
    }
}
