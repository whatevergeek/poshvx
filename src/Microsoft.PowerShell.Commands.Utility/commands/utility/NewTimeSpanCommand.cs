/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Management.Automation;
using Dbg = System.Management.Automation;

namespace Microsoft.PowerShell.Commands
{
    /// <summary> 
    /// implementation for the new-timespan command 
    /// </summary> 
    [Cmdlet(VerbsCommon.New, "TimeSpan", DefaultParameterSetName = "Date",
        HelpUri = "http://go.microsoft.com/fwlink/?LinkID=113360", RemotingCapability = RemotingCapability.None)]
    [OutputType(typeof(TimeSpan))]
    public sealed class NewTimeSpanCommand : PSCmdlet
    {
        #region parameters

        /// <summary>
        /// This parameter indicates the date the time span begins;
        /// it is used if two times are being compared
        /// </summary>
        [Parameter(Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true, ParameterSetName = "Date")]
        [Alias("LastWriteTime")]
        public DateTime Start
        {
            get
            {
                return _start;
            }
            set
            {
                _start = value;
                _startSpecified = true;
            }
        }
        private DateTime _start;
        private bool _startSpecified;


        /// <summary>
        /// This parameter indicates the end of a time span.  It is used if two
        /// times are being compared.  If one of the times is not specified,
        /// the current system time is used.  
        /// </summary>
        [Parameter(Position = 1, ValueFromPipelineByPropertyName = true, ParameterSetName = "Date")]
        public DateTime End
        {
            get
            {
                return _end;
            }
            set
            {
                _end = value;
                _endSpecified = true;
            }
        }
        private DateTime _end;
        private bool _endSpecified = false;


        /// <summary>
        /// Allows the user to override the day
        /// </summary>
        [Parameter(ParameterSetName = "Time")]
        public int Days { get; set; } = 0;


        /// <summary>
        /// Allows the user to override the hour
        /// </summary>
        [Parameter(ParameterSetName = "Time")]
        public int Hours { get; set; } = 0;


        /// <summary>
        /// Allows the user to override the minute
        /// </summary>
        [Parameter(ParameterSetName = "Time")]
        public int Minutes { get; set; } = 0;


        /// <summary>
        /// Allows the user to override the second
        /// </summary>
        [Parameter(ParameterSetName = "Time")]
        public int Seconds { get; set; } = 0;

        #endregion

        #region methods

        /// <summary>
        /// Calculate and write out the appropriate timespan
        /// </summary>
        protected override void ProcessRecord()
        {
            // initially set start and end time to be equal
            DateTime startTime = DateTime.Now;
            DateTime endTime = startTime;
            TimeSpan result;

            switch (ParameterSetName)
            {
                case "Date":
                    if (_startSpecified)
                    {
                        startTime = Start;
                    }
                    if (_endSpecified)
                    {
                        endTime = End;
                    }

                    result = endTime.Subtract(startTime);
                    break;

                case "Time":
                    result = new TimeSpan(Days, Hours, Minutes, Seconds);
                    break;

                default:
                    Dbg.Diagnostics.Assert(false, "Only one of the specified parameter sets should be called.");
                    return;
            }

            WriteObject(result);
        } // EndProcessing

        #endregion
    }  // NewTimeSpanCommand
} // namespace Microsoft.PowerShell.Commands


