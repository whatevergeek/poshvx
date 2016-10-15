﻿/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Management.Automation;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.PowerShell.Commands
{
    /// <summary>
    /// Cmdlet used to write a collection of formatting directives to
    /// an XML file
    /// </summary>
    [Cmdlet("Export", "FormatData", DefaultParameterSetName = "ByPath", HelpUri = "http://go.microsoft.com/fwlink/?LinkID=144302")]
    public class ExportFormatDataCommand : PSCmdlet
    {
        private ExtendedTypeDefinition[] _typeDefinition;

        /// <summary>
        /// type definition to include in export
        /// </summary>
        [Parameter(Mandatory = true, ValueFromPipeline = true)]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public ExtendedTypeDefinition[] InputObject
        {
            get
            {
                return _typeDefinition;
            }
            set
            {
                _typeDefinition = value;
            }
        }

        private string _filepath;

        /// <summary>
        /// Path of the XML file
        /// </summary>
        [Parameter(ParameterSetName = "ByPath", Mandatory = true)]
        [Alias("FilePath")]
        public String Path
        {
            get
            {
                return _filepath;
            }
            set
            {
                _filepath = value;
            }
        }

        /// <summary>
        /// Literal path of the XML file
        /// </summary>
        [Parameter(ParameterSetName = "ByLiteralPath", Mandatory = true)]
        [Alias("PSPath")]
        public String LiteralPath
        {
            get
            {
                return _filepath;
            }
            set
            {
                _filepath = value;
                _isLiteralPath = true;
            }
        }
        private bool _isLiteralPath = false;

        private List<ExtendedTypeDefinition> _typeDefinitions = new List<ExtendedTypeDefinition>();

        private bool _force;

        /// <summary>
        /// Force writing a file
        /// </summary>
        [Parameter()]
        public SwitchParameter Force
        {
            get
            {
                return _force;
            }
            set
            {
                _force = value;
            }
        }

        /// <summary>
        /// Do not overwrite file if exists
        /// </summary>
        [Parameter()]
        [Alias("NoOverwrite")]
        public SwitchParameter NoClobber
        {
            get
            {
                return _noclobber;
            }
            set
            {
                _noclobber = value;
            }
        }
        private bool _noclobber;

        /// <summary>
        /// Include scriptblocks for export
        /// </summary>
        [Parameter()]
        public SwitchParameter IncludeScriptBlock
        {
            get
            {
                return _includescriptblock;
            }
            set
            {
                _includescriptblock = value;
            }
        }
        private bool _includescriptblock;

        /// <summary>
        /// Adds the type to the collection
        /// </summary>
        protected override void ProcessRecord()
        {
            foreach (ExtendedTypeDefinition typedef in _typeDefinition)
            {
                _typeDefinitions.Add(typedef);
            }
        }

        /// <summary>
        /// writes out the formatting directives from the 
        /// collection to the specified XML file
        /// </summary>
        protected override void EndProcessing()
        {
            FormatXmlWriter.WriteToPs1Xml(this, _typeDefinitions, _filepath, _force, _noclobber, _includescriptblock, _isLiteralPath);
        }
    }
}
