﻿//-----------------------------------------------------------------------
// <copyright file="ShowCommandParameterInfo.cs" company="Microsoft">
//     Copyright © Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace Microsoft.PowerShell.Commands.ShowCommandExtension
{
    using System;
    using System.Collections;
    using System.Management.Automation;


    /// <summary>
    /// Implements a facade around ShowCommandParameterInfo and its deserialized counterpart
    /// </summary>
    public class ShowCommandParameterType
    {
        /// <summary>
        /// Creates an instance of the ShowCommandParameterType class based on a Type object
        /// </summary>
        /// 
        /// <param name="other">
        /// The object to wrap.
        /// </param>
        public ShowCommandParameterType(Type other)
        {
            if (null == other)
            {
                throw new ArgumentNullException("other");
            }

            this.FullName = other.FullName;
            if (other.IsEnum)
            {
                this.EnumValues = new ArrayList(Enum.GetValues(other));
            }

            if (other.IsArray)
            {
                this.ElementType = new ShowCommandParameterType(other.GetElementType());
            }

            object[] attributes = other.GetCustomAttributes(typeof(FlagsAttribute), true);
            this.HasFlagAttribute = attributes.Length != 0;
            this.ImplementsDictionary = typeof(IDictionary).IsAssignableFrom(other);
        }

        /// <summary>
        /// Creates an instance of the ShowCommandParameterType class based on a Type object
        /// </summary>
        /// 
        /// <param name="other">
        /// The object to wrap.
        /// </param>
        public ShowCommandParameterType(PSObject other)
        {
            if (null == other)
            {
                throw new ArgumentNullException("other");
            }

            this.IsEnum = (bool)(other.Members["IsEnum"].Value);
            this.FullName = other.Members["FullName"].Value as string;
            this.IsArray = (bool)(other.Members["IsArray"].Value);
            this.HasFlagAttribute = (bool)(other.Members["HasFlagAttribute"].Value);
            this.ImplementsDictionary = (bool)(other.Members["ImplementsDictionary"].Value);

            if (this.IsArray)
            {
                this.ElementType = new ShowCommandParameterType(other.Members["ElementType"].Value as PSObject);
            }

            if (this.IsEnum)
            {
                this.EnumValues = (other.Members["EnumValues"].Value as PSObject).BaseObject as ArrayList;
            }
        }

        /// <summary>
        /// The full name of the outermost type
        /// </summary>
        public string FullName { get; private set; }

        /// <summary>
        /// Whether or not this type is an enum
        /// </summary>
        public bool IsEnum { get; private set; }

        /// <summary>
        /// Whether or not this type is an dictionary
        /// </summary>
        public bool ImplementsDictionary { get; private set; }

        /// <summary>
        /// Whether or not this enum has a flag attribute
        /// </summary>
        public bool HasFlagAttribute { get; private set; }

        /// <summary>
        /// Whether or not this type is an array type
        /// </summary>
        public bool IsArray { get; private set; }

        /// <summary>
        /// Gets the inner type, if this corresponds to an array type
        /// </summary>
        public ShowCommandParameterType ElementType { get; private set; }

        /// <summary>
        /// Whether or not this type is a string
        /// </summary>
        public bool IsString
        {
            get
            {
                return String.Equals(this.FullName, "System.String", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Whether or not this type is an script block
        /// </summary>
        public bool IsScriptBlock
        {
            get
            {
                return String.Equals(this.FullName, "System.Management.Automation.ScriptBlock", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Whether or not this type is a bool
        /// </summary>
        public bool IsBoolean
        {
            get
            {
                return String.Equals(this.FullName, "System.Management.Automation.ScriptBlock", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Whether or not this type is a switch parameter
        /// </summary>
        public bool IsSwitch
        {
            get
            {
                return String.Equals(this.FullName, "System.Management.Automation.SwitchParameter", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// If this is an enum value, return the list of potential values
        /// </summary>
        public ArrayList EnumValues { get; private set; }
    }
}