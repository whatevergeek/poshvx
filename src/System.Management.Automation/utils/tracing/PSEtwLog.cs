#if !UNIX
//
//    Copyright (C) Microsoft.  All rights reserved.
//
using System.Globalization;
using System.Management.Automation.Internal;
using System.Collections.Generic;

namespace System.Management.Automation.Tracing
{
    /// <summary>
    /// ETW logging API
    /// </summary>
    internal static class PSEtwLog
    {
        private static PSEtwLogProvider provider;

        /// <summary>
        /// Class constructor
        /// </summary>
        static PSEtwLog()
        {
            provider = new PSEtwLogProvider();
        }

        /// <summary>
        /// Provider interface function for logging health event
        /// </summary>
        /// <param name="logContext"></param>
        /// <param name="eventId"></param>
        /// <param name="exception"></param>
        /// <param name="additionalInfo"></param>
        /// 
        internal static void LogEngineHealthEvent(LogContext logContext, int eventId, Exception exception, Dictionary<String, String> additionalInfo)
        {
            provider.LogEngineHealthEvent(logContext, eventId, exception, additionalInfo);
        }

        /// <summary>
        /// Provider interface function for logging engine lifecycle event
        /// </summary>
        /// <param name="logContext"></param>
        /// <param name="newState"></param>
        /// <param name="previousState"></param>
        /// 
        internal static void LogEngineLifecycleEvent(LogContext logContext, EngineState newState, EngineState previousState)
        {
            provider.LogEngineLifecycleEvent(logContext, newState, previousState);
        }

        /// <summary>
        /// Provider interface function for logging command health event
        /// </summary>
        /// <param name="logContext"></param>
        /// <param name="exception"></param>
        internal static void LogCommandHealthEvent(LogContext logContext, Exception exception)
        {
            provider.LogCommandHealthEvent(logContext, exception);
        }

        /// <summary>
        /// Provider interface function for logging command lifecycle event
        /// </summary>
        /// <param name="logContext"></param>
        /// <param name="newState"></param>
        /// 
        internal static void LogCommandLifecycleEvent(LogContext logContext, CommandState newState)
        {
            provider.LogCommandLifecycleEvent(() => logContext, newState);
        }

        /// <summary>
        /// Provider interface function for logging pipeline execution detail.
        /// </summary>
        /// <param name="logContext"></param>
        /// <param name="pipelineExecutionDetail"></param>
        internal static void LogPipelineExecutionDetailEvent(LogContext logContext, List<String> pipelineExecutionDetail)
        {
            provider.LogPipelineExecutionDetailEvent(logContext, pipelineExecutionDetail);
        }

        /// <summary>
        /// Provider interface function for logging provider health event
        /// </summary>
        /// <param name="logContext"></param>
        /// <param name="providerName"></param>
        /// <param name="exception"></param>
        internal static void LogProviderHealthEvent(LogContext logContext, string providerName, Exception exception)
        {
            provider.LogProviderHealthEvent(logContext, providerName, exception);
        }

        /// <summary>
        /// Provider interface function for logging provider lifecycle event
        /// </summary>
        /// <param name="logContext"></param>
        /// <param name="providerName"></param>
        /// <param name="newState"></param>
        /// 
        internal static void LogProviderLifecycleEvent(LogContext logContext, string providerName, ProviderState newState)
        {
            provider.LogProviderLifecycleEvent(logContext, providerName, newState);
        }

        /// <summary>
        /// Provider interface function for logging settings event
        /// </summary>
        /// <param name="logContext"></param>
        /// <param name="variableName"></param>
        /// <param name="value"></param>
        /// <param name="previousValue"></param>
        /// 
        internal static void LogSettingsEvent(LogContext logContext, string variableName, string value, string previousValue)
        {
            provider.LogSettingsEvent(logContext, variableName, value, previousValue);
        }

        /// <summary>
        /// Logs information to the operational channel
        /// </summary>
        /// <param name="id"></param>
        /// <param name="opcode"></param>
        /// <param name="task"></param>
        /// <param name="keyword"></param>
        /// <param name="args"></param>
        internal static void LogOperationalInformation(PSEventId id, PSOpcode opcode, PSTask task, PSKeyword keyword, params object[] args)
        {
            provider.WriteEvent(id, PSChannel.Operational, opcode, PSLevel.Informational, task, keyword, args);
        }

        /// <summary>
        /// Logs information to the operational channel
        /// </summary>
        /// <param name="id"></param>
        /// <param name="opcode"></param>
        /// <param name="task"></param>
        /// <param name="keyword"></param>
        /// <param name="args"></param>
        internal static void LogOperationalWarning(PSEventId id, PSOpcode opcode, PSTask task, PSKeyword keyword, params object[] args)
        {
            provider.WriteEvent(id, PSChannel.Operational, opcode, PSLevel.Warning, task, keyword, args);
        }

        /// <summary>
        /// Logs Verbose to the operational channel
        /// </summary>
        /// <param name="id"></param>
        /// <param name="opcode"></param>
        /// <param name="task"></param>
        /// <param name="keyword"></param>
        /// <param name="args"></param>
        internal static void LogOperationalVerbose(PSEventId id, PSOpcode opcode, PSTask task, PSKeyword keyword, params object[] args)
        {
            provider.WriteEvent(id, PSChannel.Operational, opcode, PSLevel.Verbose, task, keyword, args);
        }

        /// <summary>
        /// Logs error message to the analytic channel
        /// </summary>
        /// <param name="id"></param>
        /// <param name="opcode"></param>
        /// <param name="task"></param>
        /// <param name="keyword"></param>
        /// <param name="args"></param>
        internal static void LogAnalyticError(PSEventId id, PSOpcode opcode, PSTask task, PSKeyword keyword, params object[] args)
        {
            provider.WriteEvent(id, PSChannel.Analytic, opcode, PSLevel.Error, task, keyword, args);
        }

        /// <summary>
        /// Logs warning message to the analytic channel
        /// </summary>
        /// <param name="id"></param>
        /// <param name="opcode"></param>
        /// <param name="task"></param>
        /// <param name="keyword"></param>
        /// <param name="args"></param>
        internal static void LogAnalyticWarning(PSEventId id, PSOpcode opcode, PSTask task, PSKeyword keyword, params object[] args)
        {
            provider.WriteEvent(id, PSChannel.Analytic, opcode, PSLevel.Warning, task, keyword, args);
        }

        /// <summary>
        /// Logs remoting fragment data to verbose channel.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="opcode"></param>
        /// <param name="task"></param>
        /// <param name="keyword"></param>
        /// <param name="objectId"></param>
        /// <param name="fragmentId"></param>
        /// <param name="isStartFragment"></param>
        /// <param name="isEndFragment"></param>
        /// <param name="fragmentLength"></param>
        /// <param name="fragmentData"></param>
        internal static void LogAnalyticVerbose(PSEventId id, PSOpcode opcode, PSTask task, PSKeyword keyword,
            Int64 objectId,
            Int64 fragmentId,
            int isStartFragment,
            int isEndFragment,
            UInt32 fragmentLength,
            PSETWBinaryBlob fragmentData)
        {
            if (provider.IsEnabled(PSLevel.Verbose, keyword))
            {
                string payLoadData = BitConverter.ToString(fragmentData.blob, fragmentData.offset, fragmentData.length);
                payLoadData = string.Format(CultureInfo.InvariantCulture, "0x{0}", payLoadData.Replace("-", ""));

                provider.WriteEvent(id, PSChannel.Analytic, opcode, PSLevel.Verbose, task, keyword,
                                    objectId, fragmentId, isStartFragment, isEndFragment, fragmentLength,
                                    payLoadData);
            }
        }

        /// <summary>
        /// Logs verbose message to the analytic channel
        /// </summary>
        /// <param name="id"></param>
        /// <param name="opcode"></param>
        /// <param name="task"></param>
        /// <param name="keyword"></param>
        /// <param name="args"></param>
        internal static void LogAnalyticVerbose(PSEventId id, PSOpcode opcode, PSTask task, PSKeyword keyword, params object[] args)
        {
            provider.WriteEvent(id, PSChannel.Analytic, opcode, PSLevel.Verbose, task, keyword, args);
        }

        /// <summary>
        /// Logs informational message to the analytic channel
        /// </summary>
        /// <param name="id"></param>
        /// <param name="opcode"></param>
        /// <param name="task"></param>
        /// <param name="keyword"></param>
        /// <param name="args"></param>
        internal static void LogAnalyticInformational(PSEventId id, PSOpcode opcode, PSTask task, PSKeyword keyword, params object[] args)
        {
            provider.WriteEvent(id, PSChannel.Analytic, opcode, PSLevel.Informational, task, keyword, args);
        }

        /// <summary>
        /// Logs error message to operation channel.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="opcode"></param>
        /// <param name="task"></param>
        /// <param name="keyword"></param>
        /// <param name="args"></param>
        internal static void LogOperationalError(PSEventId id, PSOpcode opcode, PSTask task, PSKeyword keyword, params object[] args)
        {
            provider.WriteEvent(id, PSChannel.Operational, opcode, PSLevel.Error, task, keyword, args);
        }

        /// <summary>
        /// Logs error message to the operational channel
        /// </summary>
        /// <param name="id"></param>
        /// <param name="opcode"></param>
        /// <param name="task"></param>
        /// <param name="logContext"></param>
        /// <param name="payLoad"></param>
        internal static void LogOperationalError(PSEventId id, PSOpcode opcode, PSTask task, LogContext logContext, string payLoad)
        {
            provider.WriteEvent(id, PSChannel.Operational, opcode, task, logContext, payLoad);
        }


        internal static void SetActivityIdForCurrentThread(Guid newActivityId)
        {
            provider.SetActivityIdForCurrentThread(newActivityId);
        }

        internal static void ReplaceActivityIdForCurrentThread(Guid newActivityId,
            PSEventId eventForOperationalChannel, PSEventId eventForAnalyticChannel, PSKeyword keyword, PSTask task)
        {
            // set the new activity id
            provider.SetActivityIdForCurrentThread(newActivityId);

            // Once the activity id is set, write the transfer event
            WriteTransferEvent(newActivityId, eventForOperationalChannel, eventForAnalyticChannel, keyword, task);
        }

        /// <summary>
        /// Writes a transfer event mapping current activity id
        /// with a related activity id
        /// This function writes a transfer event for both the
        /// operational and analytic channels
        /// </summary>
        /// <param name="relatedActivityId"></param>
        /// <param name="eventForOperationalChannel"></param>
        /// <param name="eventForAnalyticChannel"></param>
        /// <param name="keyword"></param>
        /// <param name="task"></param>
        internal static void WriteTransferEvent(Guid relatedActivityId, PSEventId eventForOperationalChannel,
                            PSEventId eventForAnalyticChannel, PSKeyword keyword, PSTask task)
        {
            provider.WriteEvent(eventForOperationalChannel, PSChannel.Operational, PSOpcode.Method, PSLevel.Informational, task,
                PSKeyword.UseAlwaysOperational);

            provider.WriteEvent(eventForAnalyticChannel, PSChannel.Analytic, PSOpcode.Method, PSLevel.Informational, task,
                PSKeyword.UseAlwaysAnalytic);
        }

        /// <summary>
        /// Writes a transfer event
        /// </summary>
        /// <param name="parentActivityId"></param>
        internal static void WriteTransferEvent(Guid parentActivityId)
        {
            provider.WriteTransferEvent(parentActivityId);
        }
    }
}


#endif
