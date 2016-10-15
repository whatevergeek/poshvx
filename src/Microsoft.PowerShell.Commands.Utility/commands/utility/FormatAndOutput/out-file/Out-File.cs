/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Management.Automation;
using System.Management.Automation.Internal;
using System.Management.Automation.Host;
using System.IO;
using Microsoft.PowerShell.Commands.Internal.Format;

namespace Microsoft.PowerShell.Commands
{
    internal static class InputFileOpenModeConversion
    {
        internal static FileMode Convert(OpenMode openMode)
        {
            return SessionStateUtilities.GetFileModeFromOpenMode(openMode);
        }
    }

    /// <summary>
    /// implementation for the out-file command
    /// </summary>
    [Cmdlet("Out", "File", SupportsShouldProcess = true, DefaultParameterSetName = "ByPath", HelpUri = "http://go.microsoft.com/fwlink/?LinkID=113363")]
    public class OutFileCommand : FrontEndCommandBase
    {
        /// <summary>
        /// set inner command
        /// </summary>
        public OutFileCommand()
        {
            this.implementation = new OutputManagerInner();
        }

        #region Command Line Parameters

        /// <summary>
        /// mandatory file name to write to
        /// </summary>
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "ByPath")]
        public string FilePath
        {
            get { return _fileName; }
            set { _fileName = value; }
        }

        private string _fileName;

        /// <summary>
        /// mandatory file name to write to
        /// </summary>
        [Parameter(Mandatory = true, ValueFromPipelineByPropertyName = true, ParameterSetName = "ByLiteralPath")]
        [Alias("PSPath")]
        public string LiteralPath
        {
            get
            {
                return _fileName;
            }
            set
            {
                _fileName = value;
                _isLiteralPath = true;
            }
        }
        private bool _isLiteralPath = false;

        /// <summary>
        /// Encoding optional flag
        /// </summary>
        /// 
        [Parameter(Position = 1)]
        [ValidateNotNullOrEmpty]
        [ValidateSetAttribute(new string[] {
            EncodingConversion.Unknown,
            EncodingConversion.String,
            EncodingConversion.Unicode,
            EncodingConversion.BigEndianUnicode,
            EncodingConversion.Utf8,
            EncodingConversion.Utf7,
            EncodingConversion.Utf32,
            EncodingConversion.Ascii,
            EncodingConversion.Default,
            EncodingConversion.OEM })]
        public string Encoding
        {
            get { return _encoding; }
            set { _encoding = value; }
        }

        private string _encoding;

        /// <summary>
        /// Property that sets append parameter.
        /// </summary>
        [Parameter()]
        public SwitchParameter Append
        {
            get { return _append; }
            set { _append = value; }
        }
        private bool _append;

        /// <summary>
        /// Property that sets force parameter.
        /// </summary>
        [Parameter()]
        public SwitchParameter Force
        {
            get { return _force; }
            set { _force = value; }
        }
        private bool _force;

        /// <summary>
        /// Property that prevents file overwrite.
        /// </summary>
        [Parameter()]
        [Alias("NoOverwrite")]
        public SwitchParameter NoClobber
        {
            get { return _noclobber; }
            set { _noclobber = value; }
        }
        private bool _noclobber;

        /// <summary>
        /// optional, number of columns to use when writing to device
        /// </summary>
        [ValidateRangeAttribute(2, int.MaxValue)]
        [Parameter]
        public int Width
        {
            get { return (_width != null) ? _width.Value : 0; }
            set { _width = value; }
        }

        private Nullable<int> _width = null;

        /// <summary>
        /// False to add a newline to the end of the output string, true if not.
        /// </summary>
        [Parameter]
        public SwitchParameter NoNewline
        {
            get
            {
                return _suppressNewline;
            }
            set
            {
                _suppressNewline = value;
            }
        }

        private bool _suppressNewline = false;

        #endregion

        /// <summary>
        /// read command line parameters
        /// </summary>
        protected override void BeginProcessing()
        {
            // set up the Scree Host interface
            OutputManagerInner outInner = (OutputManagerInner)this.implementation;

            // NOTICE: if any exception is thrown from here to the end of the method, the 
            // cleanup code will be called in IDisposable.Dispose()
            outInner.LineOutput = InstantiateLineOutputInterface();

            if (null == _sw)
            {
                return;
            }

            // finally call the base class for general hookup
            base.BeginProcessing();
        }


        /// <summary>
        /// one time initialization: acquire a screen host interface
        /// by creating one on top of a file
        /// NOTICE: we assume that at this time the file name is
        /// available in the CRO. JonN recommends: file name has to be
        /// a MANDATORY parameter on the command line
        /// </summary>
        private LineOutput InstantiateLineOutputInterface()
        {
            string action = StringUtil.Format(FormatAndOut_out_xxx.OutFile_Action);
            if (ShouldProcess(FilePath, action))
            {
                PathUtils.MasterStreamOpen(
                    this,
                    FilePath,
                    _encoding,
                    false, // defaultEncoding
                    Append,
                    Force,
                    NoClobber,
                    out _fs,
                    out _sw,
                    out _readOnlyFileInfo,
                    _isLiteralPath
                    );
            }
            else
                return null;

            // compute the # of columns available
            int computedWidth = 120;

            if (_width != null)
            {
                // use the value from the command line
                computedWidth = _width.Value;
            }
            else
            {
                // use the value we get from the console
                try
                {
                    // NOTE: we subtract 1 because we want to properly handle
                    // the following scenario:
                    // MSH>get-foo|out-file foo.txt
                    // MSH>get-content foo.txt
                    // in this case, if the computed width is (say) 80, get-content
                    // would cause a wrapping of the 80 column long raw strings.
                    // Hence we set the width to 79.
                    computedWidth = this.Host.UI.RawUI.BufferSize.Width - 1;
                }
                catch (HostException)
                {
                    // non interactive host
                }
            }

            // use the stream writer to create and initialize the Line Output writer
            TextWriterLineOutput twlo = new TextWriterLineOutput(_sw, computedWidth, _suppressNewline);

            // finally have the ILineOutput interface extracted
            return (LineOutput)twlo;
        }

        /// <summary>
        /// execution entry point
        /// </summary>
        protected override void ProcessRecord()
        {
            _processRecordExecuted = true;
            if (null == _sw)
            {
                return;
            }

            // NOTICE: if any exception is thrown, the 
            // cleanup code will be called in IDisposable.Dispose()
            base.ProcessRecord();
            _sw.Flush();
        }

        /// <summary>
        /// execution entry point
        /// </summary>
        protected override void EndProcessing()
        {
            // When the Out-File is used in a redirection pipelineProcessor,
            // its ProcessRecord method may not be called when nothing is written to the 
            // output pipe, for example:
            //     Write-Error error > test.txt
            // In this case, the EndProcess method should return immediately as if it's 
            // never been called. The cleanup work will be done in IDisposable.Dispose()
            if (!_processRecordExecuted)
            {
                return;
            }

            if (null == _sw)
            {
                return;
            }

            // NOTICE: if any exception is thrown, the 
            // cleanup code will be called in IDisposable.Dispose()
            base.EndProcessing();

            _sw.Flush();

            CleanUp();
        }

        /// <summary>
        /// 
        /// </summary>
        protected override void InternalDispose()
        {
            base.InternalDispose();
            CleanUp();
        }

        private void CleanUp()
        {
            if (_fs != null)
            {
                _fs.Dispose();
                _fs = null;
            }

            // reset the read-only attribute
            if (null != _readOnlyFileInfo)
            {
                _readOnlyFileInfo.Attributes |= FileAttributes.ReadOnly;
                _readOnlyFileInfo = null;
            }
        }

        /// <summary>
        /// handle to file stream
        /// </summary>
        private FileStream _fs;

        /// <summary>
        /// stream writer used to write to file
        /// </summary>
        private StreamWriter _sw = null;

        /// <summary>
        /// indicate whether the ProcessRecord method was executed.
        /// When the Out-File is used in a redirection pipelineProcessor,
        /// its ProcessRecord method may not be called when nothing is written to the 
        /// output pipe, for example:
        ///     Write-Error error > test.txt
        /// In this case, the EndProcess method should return immediately as if it's 
        /// never been called.
        /// </summary>
        private bool _processRecordExecuted = false;

        /// <summary>
        /// FileInfo of file to clear read-only flag when operation is complete
        /// </summary>
        private FileInfo _readOnlyFileInfo = null;
    }
}

