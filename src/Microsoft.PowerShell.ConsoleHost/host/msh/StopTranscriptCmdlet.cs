/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/



using System;
using System.Management.Automation;
using System.Management.Automation.Internal;


namespace Microsoft.PowerShell.Commands
{
    /// <summary>
    /// 
    /// Implements the stop-transcript cmdlet
    /// 
    /// </summary>

    [Cmdlet(VerbsLifecycle.Stop, "Transcript", HelpUri = "http://go.microsoft.com/fwlink/?LinkID=113415")]
    [OutputType(typeof(String))]
    public sealed class StopTranscriptCommand : PSCmdlet
    {
        /// <summary>
        /// 
        /// Starts the transcription
        /// </summary>

        protected override
        void
        BeginProcessing()
        {
            try
            {
                string outFilename = Host.UI.StopTranscribing();
                if (outFilename != null)
                {
                    PSObject outputObject = new PSObject(
                        StringUtil.Format(TranscriptStrings.TranscriptionStopped, outFilename));
                    outputObject.Properties.Add(new PSNoteProperty("Path", outFilename));
                    WriteObject(outputObject);
                }
            }
            catch (Exception e)
            {
                ConsoleHost.CheckForSevereException(e);
                throw PSTraceSource.NewInvalidOperationException(
                        e, TranscriptStrings.ErrorStoppingTranscript, e.Message);
            }
        }
    }
}



