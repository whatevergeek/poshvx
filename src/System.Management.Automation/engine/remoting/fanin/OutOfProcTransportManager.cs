/********************************************************************++
 * Copyright (c) Microsoft Corporation.  All rights reserved.
 * --********************************************************************/

/*
 * Common file that contains implementation for both server and client transport
 * managers for Out-Of-Process and Named Pipe (on the local machine) remoting implementation.
 * These interfaces are used by *-Job cmdlets to support background jobs and
 * attach-to-process feature without depending on WinRM (WinRM has complex requirements like 
 * elevation to support local machine remoting).
 */

using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management.Automation.Internal;
using System.Management.Automation.Runspaces;
using System.Management.Automation.Tracing;
using System.Net;
using System.Threading;
using System.Xml;

using PSRemotingCryptoHelper = System.Management.Automation.Internal.PSRemotingCryptoHelper;
using PSRemotingCryptoHelperServer = System.Management.Automation.Internal.PSRemotingCryptoHelperServer;
using RunspaceConnectionInfo = System.Management.Automation.Runspaces.RunspaceConnectionInfo;
using ClientRemotePowerShell = System.Management.Automation.Runspaces.Internal.ClientRemotePowerShell;
using NewProcessConnectionInfo = System.Management.Automation.Runspaces.NewProcessConnectionInfo;
using PSTask = System.Management.Automation.Internal.PSTask;
using PSOpcode = System.Management.Automation.Internal.PSOpcode;
using PSEventId = System.Management.Automation.Internal.PSEventId;
using TypeTable = System.Management.Automation.Runspaces.TypeTable;
using Dbg = System.Management.Automation.Diagnostics;

namespace System.Management.Automation.Remoting
{
    internal static class OutOfProcessUtils
    {
        #region Helper Fields

        internal const string PS_OUT_OF_PROC_DATA_TAG = "Data";
        internal const string PS_OUT_OF_PROC_DATA_ACK_TAG = "DataAck";
        internal const string PS_OUT_OF_PROC_STREAM_ATTRIBUTE = "Stream";
        internal const string PS_OUT_OF_PROC_PSGUID_ATTRIBUTE = "PSGuid";
        internal const string PS_OUT_OF_PROC_CLOSE_TAG = "Close";
        internal const string PS_OUT_OF_PROC_CLOSE_ACK_TAG = "CloseAck";
        internal const string PS_OUT_OF_PROC_COMMAND_TAG = "Command";
        internal const string PS_OUT_OF_PROC_COMMAND_ACK_TAG = "CommandAck";
        internal const string PS_OUT_OF_PROC_SIGNAL_TAG = "Signal";
        internal const string PS_OUT_OF_PROC_SIGNAL_ACK_TAG = "SignalAck";
        internal const int EXITCODE_UNHANDLED_EXCEPTION = 0x0FA0;

        internal static XmlReaderSettings XmlReaderSettings;

        #endregion

        #region Static Constructor

        static OutOfProcessUtils()
        {
            // data coming from inputs stream is in Xml format. create appropriate
            // xml reader settings only once and reuse the same settings for all
            // the reads from StdIn stream.
            XmlReaderSettings = new XmlReaderSettings();
            XmlReaderSettings.CheckCharacters = false;
            XmlReaderSettings.IgnoreComments = true;
            XmlReaderSettings.IgnoreProcessingInstructions = true;
#if !CORECLR  // No XmlReaderSettings.XmlResolver in CoreCLR
            XmlReaderSettings.XmlResolver = null;
#endif
            XmlReaderSettings.ConformanceLevel = ConformanceLevel.Fragment;
        }

        #endregion

        #region Packet Creation Helper Methods

        internal static string CreateDataPacket(byte[] data, DataPriorityType streamType, Guid psGuid)
        {
            string result = string.Format(CultureInfo.InvariantCulture,
                "<{0} {1}='{2}' {3}='{4}'>{5}</{0}>",
                PS_OUT_OF_PROC_DATA_TAG,
                PS_OUT_OF_PROC_STREAM_ATTRIBUTE,
                streamType.ToString(),
                PS_OUT_OF_PROC_PSGUID_ATTRIBUTE,
                psGuid.ToString(),
                Convert.ToBase64String(data));

            return result;
        }

        internal static string CreateDataAckPacket(Guid psGuid)
        {
            return CreatePSGuidPacket(PS_OUT_OF_PROC_DATA_ACK_TAG, psGuid);
        }

        internal static string CreateCommandPacket(Guid psGuid)
        {
            return CreatePSGuidPacket(PS_OUT_OF_PROC_COMMAND_TAG, psGuid);
        }

        internal static string CreateCommandAckPacket(Guid psGuid)
        {
            return CreatePSGuidPacket(PS_OUT_OF_PROC_COMMAND_ACK_TAG, psGuid);
        }

        internal static string CreateClosePacket(Guid psGuid)
        {
            return CreatePSGuidPacket(PS_OUT_OF_PROC_CLOSE_TAG, psGuid);
        }

        internal static string CreateCloseAckPacket(Guid psGuid)
        {
            return CreatePSGuidPacket(PS_OUT_OF_PROC_CLOSE_ACK_TAG, psGuid);
        }

        internal static string CreateSignalPacket(Guid psGuid)
        {
            return CreatePSGuidPacket(PS_OUT_OF_PROC_SIGNAL_TAG, psGuid);
        }

        internal static string CreateSignalAckPacket(Guid psGuid)
        {
            return CreatePSGuidPacket(PS_OUT_OF_PROC_SIGNAL_ACK_TAG, psGuid);
        }

        /// <summary>
        /// Common method to create a packet that contains only a PS Guid
        /// with element name changing
        /// </summary>
        /// <param name="element"></param>
        /// <param name="psGuid"></param>
        /// <returns></returns>
        private static string CreatePSGuidPacket(string element, Guid psGuid)
        {
            string result = string.Format(CultureInfo.InvariantCulture,
                "<{0} {1}='{2}' />",
                element,
                PS_OUT_OF_PROC_PSGUID_ATTRIBUTE,
                psGuid.ToString());

            return result;
        }
        #endregion

        #region Packet Processing Helper Methods / Delegates

        internal delegate void DataPacketReceived(byte[] rawData, string stream, Guid psGuid);
        internal delegate void DataAckPacketReceived(Guid psGuid);
        internal delegate void CommandCreationPacketReceived(Guid psGuid);
        internal delegate void CommandCreationAckReceived(Guid psGuid);
        internal delegate void ClosePacketReceived(Guid psGuid);
        internal delegate void CloseAckPacketReceived(Guid psGuid);
        internal delegate void SignalPacketReceived(Guid psGuid);
        internal delegate void SignalAckPacketReceived(Guid psGuid);

        internal struct DataProcessingDelegates
        {
            internal DataPacketReceived DataPacketReceived;
            internal DataAckPacketReceived DataAckPacketReceived;
            internal CommandCreationPacketReceived CommandCreationPacketReceived;
            internal CommandCreationAckReceived CommandCreationAckReceived;
            internal SignalPacketReceived SignalPacketReceived;
            internal SignalAckPacketReceived SignalAckPacketReceived;
            internal ClosePacketReceived ClosePacketReceived;
            internal CloseAckPacketReceived CloseAckPacketReceived;
        }

        /// <summary>
        /// Process's data. Data should be a valid XML.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="callbacks"></param>
        /// <exception cref="PSRemotingTransportException">
        /// 1. Expected only two attributes with names "{0}" and "{1}" in "{2}" element.
        /// 2. Not enough data available to process "{0}" element.
        /// 3. Unknown node "{0}" in "{1}" element. Only "{2}" is expected in "{1}" element.
        /// </exception>
        internal static void ProcessData(string data, DataProcessingDelegates callbacks)
        {
            if (string.IsNullOrEmpty(data))
            {
                return;
            }

            XmlReader reader = XmlReader.Create(new StringReader(data), XmlReaderSettings);
            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        ProcessElement(reader, callbacks);
                        break;
                    case XmlNodeType.EndElement:
                        break;
                    default:
                        throw new PSRemotingTransportException(PSRemotingErrorId.IPCUnknownNodeType, RemotingErrorIdStrings.IPCUnknownNodeType,
                            reader.NodeType.ToString(),
                            XmlNodeType.Element.ToString(),
                            XmlNodeType.EndElement.ToString());
                }
            }
        }

        /// <summary>
        /// Process an XmlElement. The element name must be one of the following:
        ///         "Data"
        /// </summary>
        /// <param name="xmlReader"></param>
        /// <param name="callbacks"></param>
        /// <exception cref="PSRemotingTransportException">
        /// 1. Expected only two attributes with names "{0}" and "{1}" in "{2}" element.
        /// 2. Not enough data available to process "{0}" element.
        /// 3. Unknown node "{0}" in "{1}" element. Only "{2}" is expected in "{1}" element.
        /// </exception>
        private static void ProcessElement(XmlReader xmlReader, DataProcessingDelegates callbacks)
        {
            Dbg.Assert(null != xmlReader, "xmlReader cannot be null.");
            Dbg.Assert(xmlReader.NodeType == XmlNodeType.Element, "xmlReader's NodeType should be of type Element");

            PowerShellTraceSource tracer = PowerShellTraceSourceFactory.GetTraceSource();

            switch (xmlReader.LocalName)
            {
                case OutOfProcessUtils.PS_OUT_OF_PROC_DATA_TAG:
                    {
                        // A <Data> should have 1 attribute identifying the stream
                        if (xmlReader.AttributeCount != 2)
                        {
                            throw new PSRemotingTransportException(PSRemotingErrorId.IPCWrongAttributeCountForDataElement,
                                RemotingErrorIdStrings.IPCWrongAttributeCountForDataElement,
                                OutOfProcessUtils.PS_OUT_OF_PROC_STREAM_ATTRIBUTE,
                                OutOfProcessUtils.PS_OUT_OF_PROC_PSGUID_ATTRIBUTE,
                                OutOfProcessUtils.PS_OUT_OF_PROC_DATA_TAG);
                        }
                        string stream = xmlReader.GetAttribute(OutOfProcessUtils.PS_OUT_OF_PROC_STREAM_ATTRIBUTE);
                        string psGuidString = xmlReader.GetAttribute(OutOfProcessUtils.PS_OUT_OF_PROC_PSGUID_ATTRIBUTE);
                        Guid psGuid = new Guid(psGuidString);

                        // Now move the reader to the data portion
                        if (!xmlReader.Read())
                        {
                            throw new PSRemotingTransportException(PSRemotingErrorId.IPCInsufficientDataforElement,
                                RemotingErrorIdStrings.IPCInsufficientDataforElement,
                                OutOfProcessUtils.PS_OUT_OF_PROC_DATA_TAG);
                        }

                        if (xmlReader.NodeType != XmlNodeType.Text)
                        {
                            throw new PSRemotingTransportException(PSRemotingErrorId.IPCOnlyTextExpectedInDataElement,
                                RemotingErrorIdStrings.IPCOnlyTextExpectedInDataElement,
                                xmlReader.NodeType, OutOfProcessUtils.PS_OUT_OF_PROC_DATA_TAG, XmlNodeType.Text);
                        }

                        string data = xmlReader.Value;
                        tracer.WriteMessage("OutOfProcessUtils.ProcessElement : PS_OUT_OF_PROC_DATA received, psGuid : " + psGuid.ToString());
                        byte[] rawData = Convert.FromBase64String(data);
                        callbacks.DataPacketReceived(rawData, stream, psGuid);
                    }

                    break;
                case OutOfProcessUtils.PS_OUT_OF_PROC_DATA_ACK_TAG:
                    {
                        if (xmlReader.AttributeCount != 1)
                        {
                            throw new PSRemotingTransportException(PSRemotingErrorId.IPCWrongAttributeCountForElement,
                                RemotingErrorIdStrings.IPCWrongAttributeCountForElement,
                                OutOfProcessUtils.PS_OUT_OF_PROC_PSGUID_ATTRIBUTE,
                                OutOfProcessUtils.PS_OUT_OF_PROC_DATA_ACK_TAG);
                        }
                        string psGuidString = xmlReader.GetAttribute(OutOfProcessUtils.PS_OUT_OF_PROC_PSGUID_ATTRIBUTE);
                        Guid psGuid = new Guid(psGuidString);

                        tracer.WriteMessage("OutOfProcessUtils.ProcessElement : PS_OUT_OF_PROC_DATA_ACK received, psGuid : " + psGuid.ToString());
                        callbacks.DataAckPacketReceived(psGuid);
                    }
                    break;
                case OutOfProcessUtils.PS_OUT_OF_PROC_COMMAND_TAG:
                    {
                        if (xmlReader.AttributeCount != 1)
                        {
                            throw new PSRemotingTransportException(PSRemotingErrorId.IPCWrongAttributeCountForElement,
                                RemotingErrorIdStrings.IPCWrongAttributeCountForElement,
                                OutOfProcessUtils.PS_OUT_OF_PROC_PSGUID_ATTRIBUTE,
                                OutOfProcessUtils.PS_OUT_OF_PROC_COMMAND_TAG);
                        }
                        string psGuidString = xmlReader.GetAttribute(OutOfProcessUtils.PS_OUT_OF_PROC_PSGUID_ATTRIBUTE);
                        Guid psGuid = new Guid(psGuidString);

                        tracer.WriteMessage("OutOfProcessUtils.ProcessElement : PS_OUT_OF_PROC_COMMAND received, psGuid : " + psGuid.ToString());
                        callbacks.CommandCreationPacketReceived(psGuid);
                    }
                    break;
                case OutOfProcessUtils.PS_OUT_OF_PROC_COMMAND_ACK_TAG:
                    {
                        if (xmlReader.AttributeCount != 1)
                        {
                            throw new PSRemotingTransportException(PSRemotingErrorId.IPCWrongAttributeCountForElement,
                                RemotingErrorIdStrings.IPCWrongAttributeCountForElement,
                                OutOfProcessUtils.PS_OUT_OF_PROC_PSGUID_ATTRIBUTE,
                                OutOfProcessUtils.PS_OUT_OF_PROC_COMMAND_ACK_TAG);
                        }
                        string psGuidString = xmlReader.GetAttribute(OutOfProcessUtils.PS_OUT_OF_PROC_PSGUID_ATTRIBUTE);
                        Guid psGuid = new Guid(psGuidString);
                        tracer.WriteMessage("OutOfProcessUtils.ProcessElement : PS_OUT_OF_PROC_COMMAND_ACK received, psGuid : " + psGuid.ToString());
                        callbacks.CommandCreationAckReceived(psGuid);
                    }
                    break;
                case OutOfProcessUtils.PS_OUT_OF_PROC_CLOSE_TAG:
                    {
                        if (xmlReader.AttributeCount != 1)
                        {
                            throw new PSRemotingTransportException(PSRemotingErrorId.IPCWrongAttributeCountForElement,
                                RemotingErrorIdStrings.IPCWrongAttributeCountForElement,
                                OutOfProcessUtils.PS_OUT_OF_PROC_PSGUID_ATTRIBUTE,
                                OutOfProcessUtils.PS_OUT_OF_PROC_CLOSE_TAG);
                        }
                        string psGuidString = xmlReader.GetAttribute(OutOfProcessUtils.PS_OUT_OF_PROC_PSGUID_ATTRIBUTE);
                        Guid psGuid = new Guid(psGuidString);

                        tracer.WriteMessage("OutOfProcessUtils.ProcessElement : PS_OUT_OF_PROC_CLOSE received, psGuid : " + psGuid.ToString());
                        callbacks.ClosePacketReceived(psGuid);
                    }
                    break;
                case OutOfProcessUtils.PS_OUT_OF_PROC_CLOSE_ACK_TAG:
                    {
                        if (xmlReader.AttributeCount != 1)
                        {
                            throw new PSRemotingTransportException(PSRemotingErrorId.IPCWrongAttributeCountForElement,
                            RemotingErrorIdStrings.IPCWrongAttributeCountForElement,
                                OutOfProcessUtils.PS_OUT_OF_PROC_PSGUID_ATTRIBUTE,
                                OutOfProcessUtils.PS_OUT_OF_PROC_CLOSE_ACK_TAG);
                        }
                        string psGuidString = xmlReader.GetAttribute(OutOfProcessUtils.PS_OUT_OF_PROC_PSGUID_ATTRIBUTE);
                        Guid psGuid = new Guid(psGuidString);
                        tracer.WriteMessage("OutOfProcessUtils.ProcessElement : PS_OUT_OF_PROC_CLOSE_ACK received, psGuid : " + psGuid.ToString());
                        callbacks.CloseAckPacketReceived(psGuid);
                    }
                    break;
                case OutOfProcessUtils.PS_OUT_OF_PROC_SIGNAL_TAG:
                    {
                        if (xmlReader.AttributeCount != 1)
                        {
                            throw new PSRemotingTransportException(PSRemotingErrorId.IPCWrongAttributeCountForElement,
                            RemotingErrorIdStrings.IPCWrongAttributeCountForElement,
                                OutOfProcessUtils.PS_OUT_OF_PROC_PSGUID_ATTRIBUTE,
                                OutOfProcessUtils.PS_OUT_OF_PROC_SIGNAL_TAG);
                        }
                        string psGuidString = xmlReader.GetAttribute(OutOfProcessUtils.PS_OUT_OF_PROC_PSGUID_ATTRIBUTE);
                        Guid psGuid = new Guid(psGuidString);

                        tracer.WriteMessage("OutOfProcessUtils.ProcessElement : PS_OUT_OF_PROC_SIGNAL received, psGuid : " + psGuid.ToString());
                        callbacks.SignalPacketReceived(psGuid);
                    }
                    break;
                case OutOfProcessUtils.PS_OUT_OF_PROC_SIGNAL_ACK_TAG:
                    {
                        if (xmlReader.AttributeCount != 1)
                        {
                            throw new PSRemotingTransportException(PSRemotingErrorId.IPCWrongAttributeCountForElement,
                            RemotingErrorIdStrings.IPCWrongAttributeCountForElement,
                                OutOfProcessUtils.PS_OUT_OF_PROC_PSGUID_ATTRIBUTE,
                                OutOfProcessUtils.PS_OUT_OF_PROC_SIGNAL_ACK_TAG);
                        }
                        string psGuidString = xmlReader.GetAttribute(OutOfProcessUtils.PS_OUT_OF_PROC_PSGUID_ATTRIBUTE);
                        Guid psGuid = new Guid(psGuidString);
                        tracer.WriteMessage("OutOfProcessUtils.ProcessElement : PS_OUT_OF_PROC_SIGNAL_ACK received, psGuid : " + psGuid.ToString());
                        callbacks.SignalAckPacketReceived(psGuid);
                    }
                    break;
                default:
                    throw new PSRemotingTransportException(PSRemotingErrorId.IPCUnknownElementReceived,
                    RemotingErrorIdStrings.IPCUnknownElementReceived,
                        xmlReader.LocalName);
            }
        }

        #endregion
    }


    /// <summary>
    /// A wrapper around TextWriter to allow for synchronized writing to a stream.
    /// Synchronization is required to avoid collision when multiple TransportManager's
    /// write data at the same time to the same writer
    /// </summary>
    internal class OutOfProcessTextWriter
    {
        #region Private Data

        private TextWriter _writer;
        private bool _isStopped;
        private object _syncObject = new object();

        #endregion

        #region Constructors

        /// <summary>
        /// Constructs the wrapper
        /// </summary>
        /// <param name="writerToWrap"></param>
        internal OutOfProcessTextWriter(TextWriter writerToWrap)
        {
            Dbg.Assert(null != writerToWrap, "Cannot wrap a null writer.");
            _writer = writerToWrap;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Calls writer.WriteLine() with data.
        /// </summary>
        /// <param name="data"></param>
        internal virtual void WriteLine(string data)
        {
            if (_isStopped)
            {
                return;
            }

            lock (_syncObject)
            {
                _writer.WriteLine(data);
                _writer.Flush();
            }
        }

        /// <summary>
        /// Stops the writer from writing anything to the stream.
        /// This is used by session transport manager when the server
        /// process is exited but the session data structure handlers
        /// are not notified yet. So while the data structure handler
        /// is disposing we should not let anyone use the stream.
        /// </summary>
        internal void StopWriting()
        {
            _isStopped = true;
        }

        #endregion
    }
}

namespace System.Management.Automation.Remoting.Client
{
    internal abstract class OutOfProcessClientSessionTransportManagerBase : BaseClientSessionTransportManager
    {
        #region Data

        private PrioritySendDataCollection.OnDataAvailableCallback _onDataAvailableToSendCallback;
        private OutOfProcessUtils.DataProcessingDelegates _dataProcessingCallbacks;
        private Dictionary<Guid, OutOfProcessClientCommandTransportManager> _cmdTransportManagers;
        private Timer _closeTimeOutTimer;

        protected OutOfProcessTextWriter stdInWriter;
        protected PowerShellTraceSource _tracer;

        #endregion

        #region Constructor

        internal OutOfProcessClientSessionTransportManagerBase(
            Guid runspaceId,
            PSRemotingCryptoHelper cryptoHelper)
            : base(runspaceId, cryptoHelper)
        {
            _onDataAvailableToSendCallback =
                new PrioritySendDataCollection.OnDataAvailableCallback(OnDataAvailableCallback);

            _cmdTransportManagers = new Dictionary<Guid, OutOfProcessClientCommandTransportManager>();

            _dataProcessingCallbacks = new OutOfProcessUtils.DataProcessingDelegates();
            _dataProcessingCallbacks.DataPacketReceived += new OutOfProcessUtils.DataPacketReceived(OnDataPacketReceived);
            _dataProcessingCallbacks.DataAckPacketReceived += new OutOfProcessUtils.DataAckPacketReceived(OnDataAckPacketReceived);
            _dataProcessingCallbacks.CommandCreationPacketReceived += new OutOfProcessUtils.CommandCreationPacketReceived(OnCommandCreationPacketReceived);
            _dataProcessingCallbacks.CommandCreationAckReceived += new OutOfProcessUtils.CommandCreationAckReceived(OnCommandCreationAckReceived);
            _dataProcessingCallbacks.SignalPacketReceived += new OutOfProcessUtils.SignalPacketReceived(OnSignalPacketReceived);
            _dataProcessingCallbacks.SignalAckPacketReceived += new OutOfProcessUtils.SignalAckPacketReceived(OnSiganlAckPacketReceived);
            _dataProcessingCallbacks.ClosePacketReceived += new OutOfProcessUtils.ClosePacketReceived(OnClosePacketReceived);
            _dataProcessingCallbacks.CloseAckPacketReceived += new OutOfProcessUtils.CloseAckPacketReceived(OnCloseAckReceived);

            dataToBeSent.Fragmentor = base.Fragmentor;
            // session transport manager can receive unlimited data..however each object is limited
            // by maxRecvdObjectSize. this is to allow clients to use a session for an unlimited time..
            // also the messages that can be sent to a session are limited and very controlled.
            // However a command transport manager can be restricted to receive only a fixed amount of data
            // controlled by maxRecvdDataSizeCommand..This is because commands can accept any number of input
            // objects.
            ReceivedDataCollection.MaximumReceivedDataSize = null;
            ReceivedDataCollection.MaximumReceivedObjectSize = BaseTransportManager.MaximumReceivedObjectSize;
            // timers initialization
            _closeTimeOutTimer = new Timer(OnCloseTimeOutTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);

            _tracer = PowerShellTraceSourceFactory.GetTraceSource();
        }

        #endregion

        #region Overrides

        internal override void ConnectAsync()
        {
            throw new NotImplementedException(RemotingErrorIdStrings.IPCTransportConnectError);
        }

        /// <summary>
        /// Closes the server process.
        /// </summary>
        internal override void CloseAsync()
        {
            bool shouldRaiseCloseCompleted = false;
            lock (syncObject)
            {
                if (isClosed == true)
                {
                    return;
                }

                // first change the state..so other threads
                // will know that we are closing.
                isClosed = true;

                if (null == stdInWriter)
                {
                    // this will happen if CloseAsync() is called
                    // before ConnectAsync()..in which case we 
                    // just need to raise close completed event.
                    shouldRaiseCloseCompleted = true;
                }
            }

            base.CloseAsync();

            if (shouldRaiseCloseCompleted)
            {
                RaiseCloseCompleted();
                return;
            }

            PSEtwLog.LogAnalyticInformational(PSEventId.WSManCloseShell,
                PSOpcode.Disconnect, PSTask.None, PSKeyword.Transport | PSKeyword.UseAlwaysAnalytic,
                RunspacePoolInstanceId.ToString());

            _tracer.WriteMessage("OutOfProcessClientSessionTransportManager.CloseAsync, when sending close session packet, progress command count should be zero, current cmd count: " + _cmdTransportManagers.Count + ", RunSpacePool Id : " + this.RunspacePoolInstanceId);

            try
            {
                // send Close signal to the server and let it die gracefully.
                stdInWriter.WriteLine(OutOfProcessUtils.CreateClosePacket(Guid.Empty));

                // start the timer..so client can fail deterministically
                _closeTimeOutTimer.Change(60 * 1000, Timeout.Infinite);
            }
            catch (IOException)
            {
                // Cannot communicate with server.  Allow client to complete close operation.
                shouldRaiseCloseCompleted = true;
            }

            if (shouldRaiseCloseCompleted)
            {
                RaiseCloseCompleted();
            }
        }

        /// <summary>
        /// Create a transport manager for command
        /// </summary>
        /// <param name="connectionInfo"></param>
        /// <param name="cmd"></param>
        /// <param name="noInput"></param>
        /// <returns></returns>
        internal override BaseClientCommandTransportManager CreateClientCommandTransportManager(
            RunspaceConnectionInfo connectionInfo,
            ClientRemotePowerShell cmd,
            bool noInput)
        {
            Dbg.Assert(null != cmd, "Cmd cannot be null");

            OutOfProcessClientCommandTransportManager result = new
                OutOfProcessClientCommandTransportManager(cmd, noInput, this, stdInWriter);
            AddCommandTransportManager(cmd.InstanceId, result);

            return result;
        }

        /// <summary>
        /// Kills the server process and disposes other resources
        /// </summary>
        /// <param name="isDisposing"></param>
        internal override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            if (isDisposing)
            {
                _cmdTransportManagers.Clear();
                _closeTimeOutTimer.Dispose();
            }
        }

        #endregion

        #region Helper Methods

        private void AddCommandTransportManager(Guid key, OutOfProcessClientCommandTransportManager cmdTM)
        {
            lock (syncObject)
            {
                if (isClosed)
                {
                    // It is possible for this add command to occur after/during session close via
                    // asynchronous stop pipeline or Stop-Job.  In this case ignore the command.
                    _tracer.WriteMessage("OutOfProcessClientSessionTransportManager.AddCommandTransportManager, Adding command transport on closed session, RunSpacePool Id : " + this.RunspacePoolInstanceId);
                    return;
                }

                Dbg.Assert(!_cmdTransportManagers.ContainsKey(key), "key already exists");
                _cmdTransportManagers.Add(key, cmdTM);
            }
        }

        internal override void RemoveCommandTransportManager(Guid key)
        {
            lock (syncObject)
            {
                // We always need to remove commands from collection, even if isClosed is true.
                // If we don't then we hang because CloseAsync() will not complete until all 
                // commands are closed.
                if (!_cmdTransportManagers.Remove(key))
                {
                    _tracer.WriteMessage("key does not exist to remove from cmdTransportManagers");
                }
            }
        }

        private OutOfProcessClientCommandTransportManager GetCommandTransportManager(Guid key)
        {
            lock (syncObject)
            {
                OutOfProcessClientCommandTransportManager result = null;
                _cmdTransportManagers.TryGetValue(key, out result);
                return result;
            }
        }

        private void OnCloseSessionCompleted()
        {
            //stop timer
            _closeTimeOutTimer.Change(Timeout.Infinite, Timeout.Infinite);
            RaiseCloseCompleted();
            CleanupConnection();
        }

        protected abstract void CleanupConnection();

        #endregion

        #region Event Handlers

        protected void HandleOutputDataReceived(string data)
        {
            try
            {
                OutOfProcessUtils.ProcessData(data, _dataProcessingCallbacks);
            }
            catch (Exception exception)
            {
                CommandProcessorBase.CheckForSevereException(exception);

                PSRemotingTransportException psrte =
                    new PSRemotingTransportException(PSRemotingErrorId.IPCErrorProcessingServerData,
                        RemotingErrorIdStrings.IPCErrorProcessingServerData,
                        exception.Message);
                RaiseErrorHandler(new TransportErrorOccuredEventArgs(psrte, TransportMethodEnum.ReceiveShellOutputEx));
            }
        }

        protected void HandleErrorDataReceived(string data)
        {
            lock (syncObject)
            {
                if (isClosed)
                {
                    return;
                }
            }

            PSRemotingTransportException psrte = new PSRemotingTransportException(PSRemotingErrorId.IPCServerProcessReportedError,
                RemotingErrorIdStrings.IPCServerProcessReportedError,
                data);
            RaiseErrorHandler(new TransportErrorOccuredEventArgs(psrte, TransportMethodEnum.Unknown));
        }

        protected void OnExited(object sender, EventArgs e)
        {
            TransportMethodEnum transportMethod = TransportMethodEnum.Unknown;
            lock (syncObject)
            {
                // There is no need to return when IsClosed==true here as in a legitimate case process exits
                // after Close is called..In that legitimate case, Exit handler is removed before
                // calling Exit..So, this Exit must have been called abnormally.
                if (isClosed)
                {
                    transportMethod = TransportMethodEnum.CloseShellOperationEx;
                }

                // dont let the writer write new data as the process is exited.
                // Not assigning null to stdInWriter to fix the race condition between OnExited() and CloseAsync() methods.
                // 
                stdInWriter.StopWriting();
            }
            PSRemotingTransportException psrte = new PSRemotingTransportException(PSRemotingErrorId.IPCServerProcessExited,
                                RemotingErrorIdStrings.IPCServerProcessExited);
            RaiseErrorHandler(new TransportErrorOccuredEventArgs(psrte, transportMethod));
        }

        #endregion

        #region Sending Data related Methods

        protected void SendOneItem()
        {
            DataPriorityType priorityType;
            // This will either return data or register callback but doesn't do both.
            byte[] data = dataToBeSent.ReadOrRegisterCallback(_onDataAvailableToSendCallback,
                out priorityType);
            if (null != data)
            {
                SendData(data, priorityType);
            }
        }

        private void OnDataAvailableCallback(byte[] data, DataPriorityType priorityType)
        {
            Dbg.Assert(null != data, "data cannot be null in the data available callback");

            tracer.WriteLine("Received data to be sent from the callback.");
            SendData(data, priorityType);
        }

        private void SendData(byte[] data, DataPriorityType priorityType)
        {
            PSEtwLog.LogAnalyticInformational(
                       PSEventId.WSManSendShellInputEx, PSOpcode.Send, PSTask.None,
                       PSKeyword.Transport | PSKeyword.UseAlwaysAnalytic,
                       RunspacePoolInstanceId.ToString(),
                       Guid.Empty.ToString(),
                       data.Length.ToString(CultureInfo.InvariantCulture));

            lock (syncObject)
            {
                if (isClosed)
                {
                    return;
                }

                stdInWriter.WriteLine(OutOfProcessUtils.CreateDataPacket(data,
                    priorityType,
                    Guid.Empty));
            }
        }

        private void OnRemoteSessionSendCompleted()
        {
            PSEtwLog.LogAnalyticInformational(PSEventId.WSManSendShellInputExCallbackReceived,
                PSOpcode.Connect, PSTask.None, PSKeyword.Transport | PSKeyword.UseAlwaysAnalytic,
                RunspacePoolInstanceId.ToString(), Guid.Empty.ToString());

            SendOneItem();
        }

        #endregion

        #region Data Processing handlers

        private void OnDataPacketReceived(byte[] rawData, string stream, Guid psGuid)
        {
            string streamTemp = System.Management.Automation.Remoting.Client.WSManNativeApi.WSMAN_STREAM_ID_STDOUT;
            if (stream.Equals(DataPriorityType.PromptResponse.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                streamTemp = System.Management.Automation.Remoting.Client.WSManNativeApi.WSMAN_STREAM_ID_PROMPTRESPONSE;
            }

            if (psGuid == Guid.Empty)
            {
                PSEtwLog.LogAnalyticInformational(
                    PSEventId.WSManReceiveShellOutputExCallbackReceived, PSOpcode.Receive, PSTask.None,
                    PSKeyword.Transport | PSKeyword.UseAlwaysAnalytic,
                    RunspacePoolInstanceId.ToString(),
                    Guid.Empty.ToString(),
                    rawData.Length.ToString(CultureInfo.InvariantCulture));

                // this data is meant for session.
                base.ProcessRawData(rawData, streamTemp);
            }
            else
            {
                // this is for a command
                OutOfProcessClientCommandTransportManager cmdTM = GetCommandTransportManager(psGuid);
                if (null != cmdTM)
                {
                    // not throwing the exception in null case as the command might have already
                    // closed. The RS data structure handler does not wait for the close ack before
                    // it clears the command transport manager..so this might happen.
                    cmdTM.OnRemoteCmdDataReceived(rawData, streamTemp);
                }
            }
        }

        private void OnDataAckPacketReceived(Guid psGuid)
        {
            if (psGuid == Guid.Empty)
            {
                // this data is meant for session.
                OnRemoteSessionSendCompleted();
            }
            else
            {
                // this is for a command
                OutOfProcessClientCommandTransportManager cmdTM = GetCommandTransportManager(psGuid);
                if (null != cmdTM)
                {
                    // not throwing the exception in null case as the command might have already
                    // closed. The RS data structure handler does not wait for the close ack before
                    // it clears the command transport manager..so this might happen.
                    cmdTM.OnRemoteCmdSendCompleted();
                }
            }
        }

        private void OnCommandCreationPacketReceived(Guid psGuid)
        {
            throw new PSRemotingTransportException(PSRemotingErrorId.IPCUnknownElementReceived,
                RemotingErrorIdStrings.IPCUnknownElementReceived,
                   OutOfProcessUtils.PS_OUT_OF_PROC_COMMAND_TAG);
        }

        private void OnCommandCreationAckReceived(Guid psGuid)
        {
            OutOfProcessClientCommandTransportManager cmdTM = GetCommandTransportManager(psGuid);
            if (null == cmdTM)
            {
                throw new PSRemotingTransportException(PSRemotingErrorId.IPCUnknownCommandGuid,
                    RemotingErrorIdStrings.IPCUnknownCommandGuid,
                    psGuid.ToString(), OutOfProcessUtils.PS_OUT_OF_PROC_COMMAND_ACK_TAG);
            }

            cmdTM.OnCreateCmdCompleted();

            _tracer.WriteMessage("OutOfProcessClientSessionTransportManager.OnCommandCreationAckReceived, in progress command count after cmd creation ACK : " + _cmdTransportManagers.Count + ", psGuid : " + psGuid.ToString());
        }

        private void OnSignalPacketReceived(Guid psGuid)
        {
            throw new PSRemotingTransportException(PSRemotingErrorId.IPCUnknownElementReceived,
                RemotingErrorIdStrings.IPCUnknownElementReceived,
                   OutOfProcessUtils.PS_OUT_OF_PROC_SIGNAL_TAG);
        }

        private void OnSiganlAckPacketReceived(Guid psGuid)
        {
            if (psGuid == Guid.Empty)
            {
                throw new PSRemotingTransportException(PSRemotingErrorId.IPCNoSignalForSession,
                    RemotingErrorIdStrings.IPCNoSignalForSession,
                    OutOfProcessUtils.PS_OUT_OF_PROC_SIGNAL_ACK_TAG);
            }
            else
            {
                OutOfProcessClientCommandTransportManager cmdTM = GetCommandTransportManager(psGuid);
                if (null != cmdTM)
                {
                    cmdTM.OnRemoteCmdSignalCompleted();
                }
            }
        }

        private void OnClosePacketReceived(Guid psGuid)
        {
            throw new PSRemotingTransportException(PSRemotingErrorId.IPCUnknownElementReceived,
                RemotingErrorIdStrings.IPCUnknownElementReceived,
                   OutOfProcessUtils.PS_OUT_OF_PROC_CLOSE_TAG);
        }

        private void OnCloseAckReceived(Guid psGuid)
        {
            int commandCount;
            lock (syncObject)
            {
                commandCount = _cmdTransportManagers.Count;
            }

            if (psGuid == Guid.Empty)
            {
                _tracer.WriteMessage("OutOfProcessClientSessionTransportManager.OnCloseAckReceived, progress command count after CLOSE ACK should be zero = " + commandCount + " psGuid : " + psGuid.ToString());

                this.OnCloseSessionCompleted();
            }
            else
            {
                _tracer.WriteMessage("OutOfProcessClientSessionTransportManager.OnCloseAckReceived, in progress command count should be greater than zero: " + commandCount + ", RunSpacePool Id : " + this.RunspacePoolInstanceId + ", psGuid : " + psGuid.ToString());

                OutOfProcessClientCommandTransportManager cmdTM = GetCommandTransportManager(psGuid);
                if (null != cmdTM)
                {
                    // this might legitimately happen if cmd is already closed before we get an
                    // ACK back from server.
                    cmdTM.OnCloseCmdCompleted();
                }
            }
        }

        #endregion

        #region Private Timeout handlers

        internal void OnCloseTimeOutTimerElapsed(object source)
        {
            PSRemotingTransportException psrte = new PSRemotingTransportException(PSRemotingErrorId.IPCCloseTimedOut, RemotingErrorIdStrings.IPCCloseTimedOut);
            RaiseErrorHandler(new TransportErrorOccuredEventArgs(psrte, TransportMethodEnum.CloseShellOperationEx));
        }

        #endregion
    }

    internal class OutOfProcessClientSessionTransportManager : OutOfProcessClientSessionTransportManagerBase
    {
        #region Private Data

        private Process _serverProcess;
        private NewProcessConnectionInfo _connectionInfo;
        private bool _processCreated = true;
        private PowerShellProcessInstance _processInstance;

        #endregion

        #region Constructor

        internal OutOfProcessClientSessionTransportManager(Guid runspaceId,
            NewProcessConnectionInfo connectionInfo,
            PSRemotingCryptoHelper cryptoHelper)
            : base(runspaceId, cryptoHelper)
        {
            _connectionInfo = connectionInfo;
        }

        #endregion

        #region Overrides

        /// <summary>
        /// Launch a new Process (PowerShell.exe -s) to perform remoting. This is used by *-Job cmdlets
        /// to support background jobs without depending on WinRM (WinRM has complex requirements like
        /// elevation to support local machine remoting)
        /// </summary>
        /// <exception cref="System.InvalidOperationException">
        /// </exception>
        /// <exception cref="System.ComponentModel.Win32Exception">
        /// 1. There was an error in opening the associated file. 
        /// </exception>
        internal override void CreateAsync()
        {
            if (null != _connectionInfo)
            {
                _processInstance = _connectionInfo.Process ?? new PowerShellProcessInstance(_connectionInfo.PSVersion,
                                                                                           _connectionInfo.Credential,
                                                                                           _connectionInfo.InitializationScript,
                                                                                           _connectionInfo.RunAs32);
                if (_connectionInfo.Process != null)
                {
                    _processCreated = false;
                }
                // _processInstance.Start();
            }

            PSEtwLog.LogAnalyticInformational(PSEventId.WSManCreateShell, PSOpcode.Connect,
                            PSTask.CreateRunspace, PSKeyword.Transport | PSKeyword.UseAlwaysAnalytic,
                            RunspacePoolInstanceId.ToString());

            try
            {
                lock (syncObject)
                {
                    if (isClosed)
                    {
                        return;
                    }

                    // Attach handlers and start the process
                    _serverProcess = _processInstance.Process;

                    if (_processInstance.RunspacePool != null)
                    {
                        _processInstance.RunspacePool.Close();
                        _processInstance.RunspacePool.Dispose();
                    }

                    stdInWriter = _processInstance.StdInWriter;
                    //if (stdInWriter == null)
                    {
                        _serverProcess.OutputDataReceived += new DataReceivedEventHandler(OnOutputDataReceived);
                        _serverProcess.ErrorDataReceived += new DataReceivedEventHandler(OnErrorDataReceived);
                    }
                    _serverProcess.Exited += new EventHandler(OnExited);

                    //serverProcess.Start();
                    _processInstance.Start();

                    if (stdInWriter != null)
                    {
                        _serverProcess.CancelErrorRead();
                        _serverProcess.CancelOutputRead();
                    }

                    // Start asynchronous reading of output/errors
                    _serverProcess.BeginOutputReadLine();
                    _serverProcess.BeginErrorReadLine();

                    stdInWriter = new OutOfProcessTextWriter(_serverProcess.StandardInput);
                    _processInstance.StdInWriter = stdInWriter;
                }
            }
            catch (System.ComponentModel.Win32Exception w32e)
            {
                PSRemotingTransportException psrte = new PSRemotingTransportException(w32e, RemotingErrorIdStrings.IPCExceptionLaunchingProcess,
                    w32e.Message);
                psrte.ErrorCode = w32e.HResult;
                TransportErrorOccuredEventArgs eventargs = new TransportErrorOccuredEventArgs(psrte, TransportMethodEnum.CreateShellEx);
                RaiseErrorHandler(eventargs);
                return;
            }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);

                PSRemotingTransportException psrte = new PSRemotingTransportException(PSRemotingErrorId.IPCExceptionLaunchingProcess,
                RemotingErrorIdStrings.IPCExceptionLaunchingProcess,
                    e.Message);
                TransportErrorOccuredEventArgs eventargs = new TransportErrorOccuredEventArgs(psrte, TransportMethodEnum.CreateShellEx);
                RaiseErrorHandler(eventargs);
                return;
            }

            // Send one fragment
            SendOneItem();
        }

        /// <summary>
        /// Kills the server process and disposes other resources
        /// </summary>
        /// <param name="isDisposing"></param>
        internal override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            if (isDisposing)
            {
                KillServerProcess();
                if (null != _serverProcess && _processCreated)
                {
                    // null can happen if Dispose is called before ConnectAsync()
                    _serverProcess.Dispose();
                }
            }
        }

        protected override void CleanupConnection()
        {
            // Clean up the child process
            KillServerProcess();
        }

        #endregion

        #region Event Handlers

        private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            HandleOutputDataReceived(e.Data);
        }

        private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            HandleErrorDataReceived(e.Data);
        }

        #endregion

        #region Helper Methods

        private void KillServerProcess()
        {
            if (null == _serverProcess)
            {
                // this can happen if Dispose is called before ConnectAsync()
                return;
            }

            try
            {
                if (!_serverProcess.HasExited)
                {
                    _serverProcess.Exited -= OnExited;

                    if (_processCreated)
                    {
                        _serverProcess.CancelOutputRead();
                        _serverProcess.CancelErrorRead();
                        _serverProcess.Kill();
                    }

                    _serverProcess.OutputDataReceived -= new DataReceivedEventHandler(OnOutputDataReceived);
                    _serverProcess.ErrorDataReceived -= new DataReceivedEventHandler(OnErrorDataReceived);
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                try
                {
                    // For processes running in an NTVDM, trying to kill with
                    // the original handle fails with a Win32 error, so we'll 
                    // use the ID and try to get a new handle...
                    Process newHandle = Process.GetProcessById(_serverProcess.Id);
                    // If the process was not found, we won't get here...
                    if (_processCreated) newHandle.Kill();
                }
                catch (Exception e) // ignore non-severe exceptions
                {
                    CommandProcessorBase.CheckForSevereException(e);
                }
            }
            catch (Exception e) // ignore non-severe exceptions
            {
                CommandProcessorBase.CheckForSevereException(e);
            }
        }

        #endregion
    }

    internal abstract class HyperVSocketClientSessionTransportManagerBase : OutOfProcessClientSessionTransportManagerBase
    {
        #region Data

        protected RemoteSessionHyperVSocketClient _client;
        private const string _threadName = "HyperVSocketTransport Reader Thread";

        #endregion

        #region Constructors

        internal HyperVSocketClientSessionTransportManagerBase(
            Guid runspaceId,
            PSRemotingCryptoHelper cryptoHelper)
            : base(runspaceId, cryptoHelper)
        { }

        #endregion

        #region Overrides

        internal override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (isDisposing)
            {
                if (_client != null)
                {
                    _client.Dispose();
                }
            }
        }

        protected override void CleanupConnection()
        {
            _client.Close();
        }

        #endregion

        #region Protected Methods

        protected void StartReaderThread(
            StreamReader reader)
        {
            Thread readerThread = new Thread(ProcessReaderThread);
            readerThread.Name = _threadName;
            readerThread.IsBackground = true;
            readerThread.Start(reader);
        }

        protected void ProcessReaderThread(object state)
        {
            try
            {
                StreamReader reader = state as StreamReader;
                Dbg.Assert(reader != null, "Reader cannot be null.");

                // Send one fragment.
                SendOneItem();

                // Start reader loop.
                while (true)
                {
                    string data = reader.ReadLine();
                    if (data == null)
                    {
                        // End of stream indicates the target process was lost.
                        // Raise transport exception to invalidate the client remote runspace.
                        PSRemotingTransportException psrte = new PSRemotingTransportException(
                            PSRemotingErrorId.IPCServerProcessReportedError,
                            RemotingErrorIdStrings.IPCServerProcessReportedError,
                            RemotingErrorIdStrings.HyperVSocketTransportProcessEnded);
                        RaiseErrorHandler(new TransportErrorOccuredEventArgs(psrte, TransportMethodEnum.ReceiveShellOutputEx));
                        break;
                    }

                    if (data.StartsWith(System.Management.Automation.Remoting.Server.HyperVSocketErrorTextWriter.ErrorPrepend, StringComparison.OrdinalIgnoreCase))
                    {
                        // Error message from the server.
                        string errorData = data.Substring(System.Management.Automation.Remoting.Server.HyperVSocketErrorTextWriter.ErrorPrepend.Length);
                        HandleErrorDataReceived(errorData);
                    }
                    else
                    {
                        // Normal output data.
                        HandleOutputDataReceived(data);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // Normal reader thread end.
            }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);

                if (e is ArgumentOutOfRangeException)
                {
                    Dbg.Assert(false, "Need to adjust transport fragmentor to accomodate read buffer size.");
                }

                string errorMsg = (e.Message != null) ? e.Message : string.Empty;
                _tracer.WriteMessage("HyperVSocketClientSessionTransportManager", "StartReaderThread", Guid.Empty,
                    "Transport manager reader thread ended with error: {0}", errorMsg);

                PSRemotingTransportException psrte = new PSRemotingTransportException(
                    PSRemotingErrorId.IPCServerProcessReportedError,
                    RemotingErrorIdStrings.IPCServerProcessReportedError,
                    RemotingErrorIdStrings.HyperVSocketTransportProcessEnded);
                RaiseErrorHandler(new TransportErrorOccuredEventArgs(psrte, TransportMethodEnum.ReceiveShellOutputEx));
            }
        }

        #endregion
    }

    internal sealed class VMHyperVSocketClientSessionTransportManager : HyperVSocketClientSessionTransportManagerBase
    {
        #region Private Data

        private Guid _vmGuid;
        private string _configurationName;
        private VMConnectionInfo _connectionInfo;
        private NetworkCredential _networkCredential;

        #endregion

        #region Constructors

        internal VMHyperVSocketClientSessionTransportManager(
            VMConnectionInfo connectionInfo,
            Guid runspaceId,
            PSRemotingCryptoHelper cryptoHelper,
            Guid vmGuid,
            string configurationName)
            : base(runspaceId, cryptoHelper)
        {
            if (connectionInfo == null)
            {
                throw new PSArgumentNullException("connectionInfo");
            }

            _connectionInfo = connectionInfo;
            _vmGuid = vmGuid;
            _configurationName = configurationName;

            if (connectionInfo.Credential == null)
            {
                _networkCredential = CredentialCache.DefaultNetworkCredentials;
            }
            else
            {
                _networkCredential = connectionInfo.Credential.GetNetworkCredential();
            }
        }

        #endregion

        #region Overrides

        /// <summary>
        /// Create a Hyper-V socket connection to the target process and set up
        /// transport reader/writer.
        /// </summary>
        internal override void CreateAsync()
        {
            _client = new RemoteSessionHyperVSocketClient(_vmGuid, true);
            if (!_client.Connect(_networkCredential, _configurationName, true))
            {
                _client.Dispose();
                throw new PSInvalidOperationException(
                    PSRemotingErrorInvariants.FormatResourceString(RemotingErrorIdStrings.VMSessionConnectFailed),
                    null,
                    PSRemotingErrorId.VMSessionConnectFailed.ToString(),
                    ErrorCategory.InvalidOperation,
                    null);
            }

            // TODO: remove below 3 lines when Hyper-V socket duplication is supported in .NET framework.
            _client.Dispose();
            _client = new RemoteSessionHyperVSocketClient(_vmGuid, false);
            if (!_client.Connect(_networkCredential, _configurationName, false))
            {
                _client.Dispose();
                throw new PSInvalidOperationException(
                    PSRemotingErrorInvariants.FormatResourceString(RemotingErrorIdStrings.VMSessionConnectFailed),
                    null,
                    PSRemotingErrorId.VMSessionConnectFailed.ToString(),
                    ErrorCategory.InvalidOperation,
                    null);
            }

            // Create writer for Hyper-V socket.
            stdInWriter = new OutOfProcessTextWriter(_client.TextWriter);

            // Create reader thread for Hyper-V socket.
            StartReaderThread(_client.TextReader);
        }

        #endregion
    }

    internal sealed class ContainerHyperVSocketClientSessionTransportManager : HyperVSocketClientSessionTransportManagerBase
    {
        #region Private Data

        private Guid _targetGuid; // currently this is the utility vm guid in HyperV container scenario
        private ContainerConnectionInfo _connectionInfo;

        #endregion

        #region Constructors

        internal ContainerHyperVSocketClientSessionTransportManager(
            ContainerConnectionInfo connectionInfo,
            Guid runspaceId,
            PSRemotingCryptoHelper cryptoHelper,
            Guid targetGuid)
            : base(runspaceId, cryptoHelper)
        {
            if (connectionInfo == null)
            {
                throw new PSArgumentNullException("connectionInfo");
            }

            _connectionInfo = connectionInfo;
            _targetGuid = targetGuid;
        }

        #endregion

        #region Overrides

        /// <summary>
        /// Create a Hyper-V socket connection to the target process and set up
        /// transport reader/writer.
        /// </summary>
        internal override void CreateAsync()
        {
            _client = new RemoteSessionHyperVSocketClient(_targetGuid, false, true);
            if (!_client.Connect(null, String.Empty, false))
            {
                _client.Dispose();
                throw new PSInvalidOperationException(
                    PSRemotingErrorInvariants.FormatResourceString(RemotingErrorIdStrings.ContainerSessionConnectFailed),
                    null,
                    PSRemotingErrorId.ContainerSessionConnectFailed.ToString(),
                    ErrorCategory.InvalidOperation,
                    null);
            }

            // Create writer for Hyper-V socket.
            stdInWriter = new OutOfProcessTextWriter(_client.TextWriter);

            // Create reader thread for Hyper-V socket.
            StartReaderThread(_client.TextReader);
        }

        #endregion
    }

    internal sealed class SSHClientSessionTransportManager : OutOfProcessClientSessionTransportManagerBase
    {
        #region Data

        private SSHConnectionInfo _connectionInfo;
        private Process _sshProcess;
        private StreamWriter _stdInWriter;
        private StreamReader _stdOutReader;
        private StreamReader _stdErrReader;
        private const string _threadName = "SSHTransport Reader Thread";

        #endregion

        #region Constructors

        internal SSHClientSessionTransportManager(
            SSHConnectionInfo connectionInfo,
            Guid runspaceId,
            PSRemotingCryptoHelper cryptoHelper)
            : base(runspaceId, cryptoHelper)
        {
            if (connectionInfo == null) { throw new PSArgumentException("connectionInfo"); }

            _connectionInfo = connectionInfo;
        }

        #endregion

        #region Overrides

        internal override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (isDisposing)
            {
                CloseConnection();
            }
        }

        protected override void CleanupConnection()
        {
            CloseConnection();
        }

        /// <summary>
        /// Create an SSH connection to the target host and set up
        /// transport reader/writer.
        /// </summary>
        internal override void CreateAsync()
        {
            // Create the ssh client process with connection to host target.
            _sshProcess = _connectionInfo.StartSSHProcess(
                out _stdInWriter,
                out _stdOutReader,
                out _stdErrReader);

            _sshProcess.Exited += (sender, args) =>
            {
                CloseConnection();
            };

            // Start error reader thread.
            StartErrorThread(_stdErrReader);

            // Create writer for named pipe.
            stdInWriter = new OutOfProcessTextWriter(_stdInWriter);

            // Create reader thread and send first PSRP message.
            StartReaderThread(_stdOutReader);
        }

        #endregion

        #region Private Methods

        private void CloseConnection()
        {
            var stdInWriter = _stdInWriter;
            if (stdInWriter != null) { stdInWriter.Dispose(); }

            var stdOutReader = _stdOutReader;
            if (stdOutReader != null) { stdOutReader.Dispose(); }

            var stdErrReader = _stdErrReader;
            if (stdErrReader != null) { stdErrReader.Dispose(); }

            var sshProcess = _sshProcess;
            if ((sshProcess != null) && !sshProcess.HasExited)
            {
                _sshProcess = null;
                try
                {
                    sshProcess.Kill();
                }
                catch (InvalidOperationException) { }
                catch (NotSupportedException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }

        private void StartErrorThread(
            StreamReader stdErrReader)
        {
            Thread errorThread = new Thread(ProcessErrorThread);
            errorThread.Name = "SSH Transport Error Thread";
            errorThread.IsBackground = true;
            errorThread.Start(stdErrReader);
        }

        private void ProcessErrorThread(object state)
        {
            try
            {
                StreamReader reader = state as StreamReader;
                Dbg.Assert(reader != null, "Reader cannot be null.");

                while (true)
                {
                    string error = reader.ReadLine();
                    if (!string.IsNullOrEmpty(error) && (error.IndexOf("WARNING:", StringComparison.OrdinalIgnoreCase) < 0))
                    {
                        // Any SSH client error results in a broken session.
                        PSRemotingTransportException psrte = new PSRemotingTransportException(
                            PSRemotingErrorId.IPCServerProcessReportedError,
                            RemotingErrorIdStrings.IPCServerProcessReportedError,
                            error);
                        RaiseErrorHandler(new TransportErrorOccuredEventArgs(psrte, TransportMethodEnum.CloseShellOperationEx));
                        CloseConnection();
                    }
                }
            }
            catch (ObjectDisposedException) { }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);

                string errorMsg = (e.Message != null) ? e.Message : string.Empty;
                _tracer.WriteMessage("SSHClientSessionTransportManager", "ProcessErrorThread", Guid.Empty,
                    "Transport manager error thread ended with error: {0}", errorMsg);
            }
        }

        private void StartReaderThread(
            StreamReader reader)
        {
            Thread readerThread = new Thread(ProcessReaderThread);
            readerThread.Name = _threadName;
            readerThread.IsBackground = true;
            readerThread.Start(reader);
        }

        private void ProcessReaderThread(object state)
        {
            try
            {
                StreamReader reader = state as StreamReader;
                Dbg.Assert(reader != null, "Reader cannot be null.");

                // Send one fragment.
                SendOneItem();

                // Start reader loop.
                while (true)
                {
                    string data = reader.ReadLine();
                    if (data == null)
                    {
                        // End of stream indicates the target process was lost.
                        // Raise transport exception to invalidate the client remote runspace.
                        PSRemotingTransportException psrte = new PSRemotingTransportException(
                            PSRemotingErrorId.IPCServerProcessReportedError,
                            RemotingErrorIdStrings.IPCServerProcessReportedError,
                            RemotingErrorIdStrings.NamedPipeTransportProcessEnded);
                        RaiseErrorHandler(new TransportErrorOccuredEventArgs(psrte, TransportMethodEnum.ReceiveShellOutputEx));
                        break;
                    }

                    if (data.StartsWith(System.Management.Automation.Remoting.Server.NamedPipeErrorTextWriter.ErrorPrepend, StringComparison.OrdinalIgnoreCase))
                    {
                        // Error message from the server.
                        string errorData = data.Substring(System.Management.Automation.Remoting.Server.NamedPipeErrorTextWriter.ErrorPrepend.Length);
                        HandleErrorDataReceived(errorData);
                    }
                    else
                    {
                        // Normal output data.
                        HandleOutputDataReceived(data);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // Normal reader thread end.
            }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);

                if (e is ArgumentOutOfRangeException)
                {
                    Dbg.Assert(false, "Need to adjust transport fragmentor to accomodate read buffer size.");
                }

                string errorMsg = (e.Message != null) ? e.Message : string.Empty;
                _tracer.WriteMessage("SSHClientSessionTransportManager", "ProcessReaderThread", Guid.Empty,
                    "Transport manager reader thread ended with error: {0}", errorMsg);
            }
        }

        #endregion
    }

    internal abstract class NamedPipeClientSessionTransportManagerBase : OutOfProcessClientSessionTransportManagerBase
    {
        #region Data

        private RunspaceConnectionInfo _connectionInfo;
        protected NamedPipeClientBase _clientPipe = new NamedPipeClientBase();
        private string _threadName;

        #endregion

        #region Constructors

        internal NamedPipeClientSessionTransportManagerBase(
            RunspaceConnectionInfo connectionInfo,
            Guid runspaceId,
            PSRemotingCryptoHelper cryptoHelper,
            string threadName)
            : base(runspaceId, cryptoHelper)
        {
            if (connectionInfo == null)
            {
                throw new PSArgumentNullException("connectionInfo");
            }

            _connectionInfo = connectionInfo;
            _threadName = threadName;
            Fragmentor.FragmentSize = RemoteSessionNamedPipeServer.NamedPipeBufferSizeForRemoting;
        }

        #endregion

        #region Overrides

        internal override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (isDisposing)
            {
                if (_clientPipe != null)
                {
                    _clientPipe.Dispose();
                }
            }
        }

        protected override void CleanupConnection()
        {
            _clientPipe.Close();
        }

        #endregion

        #region Protected Methods

        protected void StartReaderThread(
            StreamReader reader)
        {
            Thread readerThread = new Thread(ProcessReaderThread);
            readerThread.Name = _threadName;
            readerThread.IsBackground = true;
            readerThread.Start(reader);
        }

        #endregion

        #region Private Methods

        private void ProcessReaderThread(object state)
        {
            try
            {
                StreamReader reader = state as StreamReader;
                Dbg.Assert(reader != null, "Reader cannot be null.");

                // Send one fragment.
                SendOneItem();

                // Start reader loop.
                while (true)
                {
                    string data = reader.ReadLine();
                    if (data == null)
                    {
                        // End of stream indicates the target process was lost.
                        // Raise transport exception to invalidate the client remote runspace.
                        PSRemotingTransportException psrte = new PSRemotingTransportException(
                            PSRemotingErrorId.IPCServerProcessReportedError,
                            RemotingErrorIdStrings.IPCServerProcessReportedError,
                            RemotingErrorIdStrings.NamedPipeTransportProcessEnded);
                        RaiseErrorHandler(new TransportErrorOccuredEventArgs(psrte, TransportMethodEnum.ReceiveShellOutputEx));
                        break;
                    }

                    if (data.StartsWith(System.Management.Automation.Remoting.Server.NamedPipeErrorTextWriter.ErrorPrepend, StringComparison.OrdinalIgnoreCase))
                    {
                        // Error message from the server.
                        string errorData = data.Substring(System.Management.Automation.Remoting.Server.NamedPipeErrorTextWriter.ErrorPrepend.Length);
                        HandleErrorDataReceived(errorData);
                    }
                    else
                    {
                        // Normal output data.
                        HandleOutputDataReceived(data);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // Normal reader thread end.
            }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);

                if (e is ArgumentOutOfRangeException)
                {
                    Dbg.Assert(false, "Need to adjust transport fragmentor to accomodate read buffer size.");
                }

                string errorMsg = (e.Message != null) ? e.Message : string.Empty;
                _tracer.WriteMessage("NamedPipeClientSessionTransportManager", "StartReaderThread", Guid.Empty,
                    "Transport manager reader thread ended with error: {0}", errorMsg);
            }
        }

        #endregion
    }

    internal sealed class NamedPipeClientSessionTransportManager : NamedPipeClientSessionTransportManagerBase
    {
        #region Private Data

        private NamedPipeConnectionInfo _connectionInfo;
        private const string _threadName = "NamedPipeTransport Reader Thread";

        #endregion

        #region Constructors

        internal NamedPipeClientSessionTransportManager(
            NamedPipeConnectionInfo connectionInfo,
            Guid runspaceId,
            PSRemotingCryptoHelper cryptoHelper)
            : base(connectionInfo, runspaceId, cryptoHelper, _threadName)
        {
            if (connectionInfo == null)
            {
                throw new PSArgumentNullException("connectionInfo");
            }

            _connectionInfo = connectionInfo;
        }

        #endregion

        #region Overrides

        /// <summary>
        /// Create a named pipe connection to the target process and set up
        /// transport reader/writer.
        /// </summary>
        internal override void CreateAsync()
        {
            _clientPipe = new RemoteSessionNamedPipeClient(_connectionInfo.ProcessId, _connectionInfo.AppDomainName);

            // Wait for named pipe to connect.
            _clientPipe.Connect(_connectionInfo.OpenTimeout);

            // Create writer for named pipe.
            stdInWriter = new OutOfProcessTextWriter(_clientPipe.TextWriter);

            // Create reader thread for named pipe.
            StartReaderThread(_clientPipe.TextReader);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Aborts an existing connection attempt.
        /// </summary>
        public void AbortConnect()
        {
            if (_clientPipe != null)
            {
                _clientPipe.AbortConnect();
            }
        }

        #endregion
    }

    internal sealed class ContainerNamedPipeClientSessionTransportManager : NamedPipeClientSessionTransportManagerBase
    {
        #region Private Data

        private ContainerConnectionInfo _connectionInfo;
        private const string _threadName = "ContainerNamedPipeTransport Reader Thread";

        #endregion

        #region Constructors

        internal ContainerNamedPipeClientSessionTransportManager(
            ContainerConnectionInfo connectionInfo,
            Guid runspaceId,
            PSRemotingCryptoHelper cryptoHelper)
            : base(connectionInfo, runspaceId, cryptoHelper, _threadName)
        {
            if (connectionInfo == null)
            {
                throw new PSArgumentNullException("connectionInfo");
            }

            _connectionInfo = connectionInfo;
        }

        #endregion

        #region Overrides

        /// <summary>
        /// Create a named pipe connection to the target process in target container and set up
        /// transport reader/writer.
        /// </summary>
        internal override void CreateAsync()
        {
            _clientPipe = new ContainerSessionNamedPipeClient(
                _connectionInfo.ContainerProc.ProcessId,
                string.Empty, // AppDomainName
                _connectionInfo.ContainerProc.ContainerObRoot);

            // Wait for named pipe to connect.
            _clientPipe.Connect(_connectionInfo.OpenTimeout);

            // Create writer for named pipe.
            stdInWriter = new OutOfProcessTextWriter(_clientPipe.TextWriter);

            // Create reader thread for named pipe.
            StartReaderThread(_clientPipe.TextReader);
        }

        protected override void CleanupConnection()
        {
            _clientPipe.Close();

            // 
            // We should terminate the PowerShell process inside container that
            // is created for PowerShell Direct.
            //
            if (!_connectionInfo.TerminateContainerProcess())
            {
                _tracer.WriteMessage("ContainerNamedPipeClientSessionTransportManager", "CleanupConnection", Guid.Empty,
                    "Failed to terminate PowerShell process inside container");
            }
        }

        #endregion
    }

    internal class OutOfProcessClientCommandTransportManager : BaseClientCommandTransportManager
    {
        #region Private Data

        private OutOfProcessTextWriter _stdInWriter;
        private PrioritySendDataCollection.OnDataAvailableCallback _onDataAvailableToSendCallback;
        private Timer _signalTimeOutTimer;

        #endregion

        #region Constructors

        internal OutOfProcessClientCommandTransportManager(
            ClientRemotePowerShell cmd,
            bool noInput,
            OutOfProcessClientSessionTransportManagerBase sessnTM,
            OutOfProcessTextWriter stdInWriter) : base(cmd, sessnTM.CryptoHelper, sessnTM)
        {
            _stdInWriter = stdInWriter;
            _onDataAvailableToSendCallback =
                new PrioritySendDataCollection.OnDataAvailableCallback(OnDataAvailableCallback);
            _signalTimeOutTimer = new Timer(OnSignalTimeOutTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
        }

        #endregion

        #region Overrides

        internal override void ConnectAsync()
        {
            throw new NotImplementedException(RemotingErrorIdStrings.IPCTransportConnectError);
        }

        internal override void CreateAsync()
        {
            PSEtwLog.LogAnalyticInformational(PSEventId.WSManCreateCommand, PSOpcode.Connect,
                                PSTask.CreateRunspace,
                                PSKeyword.Transport | PSKeyword.UseAlwaysAnalytic,
                                RunspacePoolInstanceId.ToString(), powershellInstanceId.ToString());

            _stdInWriter.WriteLine(OutOfProcessUtils.CreateCommandPacket(powershellInstanceId));
        }

        internal override void CloseAsync()
        {
            lock (syncObject)
            {
                if (isClosed == true)
                {
                    return;
                }

                // first change the state..so other threads
                // will know that we are closing.
                isClosed = true;
            }

            base.CloseAsync();

            PSEtwLog.LogAnalyticInformational(PSEventId.WSManCloseCommand,
                PSOpcode.Disconnect, PSTask.None,
                PSKeyword.Transport | PSKeyword.UseAlwaysAnalytic,
                RunspacePoolInstanceId.ToString(), powershellInstanceId.ToString());

            // Send Close information to the server
            if (_stdInWriter != null)
            {
                try
                {
                    _stdInWriter.WriteLine(OutOfProcessUtils.CreateClosePacket(powershellInstanceId));
                }
                catch (IOException e)
                {
                    RaiseErrorHandler(
                        new TransportErrorOccuredEventArgs(
                            new PSRemotingTransportException(RemotingErrorIdStrings.NamedPipeTransportProcessEnded, e), TransportMethodEnum.CloseShellOperationEx));
                }
            }
        }

        internal override void SendStopSignal()
        {
            PSEtwLog.LogAnalyticInformational(PSEventId.WSManSignal,
                PSOpcode.Disconnect, PSTask.None,
                PSKeyword.Transport | PSKeyword.UseAlwaysAnalytic,
                RunspacePoolInstanceId.ToString(), powershellInstanceId.ToString(), "stopsignal");
            // make sure we dont send anymore data from now onwards.
            base.CloseAsync();

            // Stop is equivalent to closing on the server..
            _stdInWriter.WriteLine(OutOfProcessUtils.CreateSignalPacket(powershellInstanceId));

            // start the timer..so client can fail deterministically
            // set the interval to 60 seconds.
            _signalTimeOutTimer.Change(60 * 1000, Timeout.Infinite);
        }

        internal override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            if (isDisposing)
            {
                StopSignalTimerAndDecrementOperations();
                _signalTimeOutTimer.Dispose();
            }
        }

        #endregion

        #region Helper Methods

        internal void OnCreateCmdCompleted()
        {
            PSEtwLog.LogAnalyticInformational(PSEventId.WSManCreateCommandCallbackReceived,
                PSOpcode.Connect, PSTask.None,
                PSKeyword.Transport | PSKeyword.UseAlwaysAnalytic,
                RunspacePoolInstanceId.ToString(), powershellInstanceId.ToString());

            lock (syncObject)
            {
                // make sure the transport is not closed yet.
                if (isClosed)
                {
                    tracer.WriteLine("Client Session TM: Transport manager is closed. So returning");
                    return;
                }

                // Start sending data if any..and see if we can initiate a receive.
                SendOneItem();
            }
        }

        internal void OnRemoteCmdSendCompleted()
        {
            PSEtwLog.LogAnalyticInformational(PSEventId.WSManSendShellInputExCallbackReceived,
                PSOpcode.Connect, PSTask.None,
                PSKeyword.Transport | PSKeyword.UseAlwaysAnalytic,
                RunspacePoolInstanceId.ToString(), powershellInstanceId.ToString());

            lock (syncObject)
            {
                // if the transport manager is already closed..return immediately           
                if (isClosed)
                {
                    tracer.WriteLine("Client Command TM: Transport manager is closed. So returning");
                    return;
                }
            }

            SendOneItem();
        }

        internal void OnRemoteCmdDataReceived(byte[] rawData, string stream)
        {
            PSEtwLog.LogAnalyticInformational(
                    PSEventId.WSManReceiveShellOutputExCallbackReceived, PSOpcode.Receive, PSTask.None,
                    PSKeyword.Transport | PSKeyword.UseAlwaysAnalytic,
                    RunspacePoolInstanceId.ToString(),
                    powershellInstanceId.ToString(),
                    rawData.Length.ToString(CultureInfo.InvariantCulture));

            // if the transport manager is already closed..return immediately
            if (isClosed)
            {
                tracer.WriteLine("Client Command TM: Transport manager is closed. So returning");
                return;
            }

            ProcessRawData(rawData, stream);
        }

        internal void OnRemoteCmdSignalCompleted()
        {
            // log the callback received event.
            PSEtwLog.LogAnalyticInformational(PSEventId.WSManSignalCallbackReceived,
                PSOpcode.Disconnect, PSTask.None,
                PSKeyword.Transport | PSKeyword.UseAlwaysAnalytic,
                RunspacePoolInstanceId.ToString(), powershellInstanceId.ToString());

            StopSignalTimerAndDecrementOperations();

            if (isClosed)
            {
                return;
            }

            // Call Signal completed from callback thread.
            EnqueueAndStartProcessingThread(null, null, true);
        }

        internal void OnSignalTimeOutTimerElapsed(object source)
        {
            //Signal timer is triggered only once

            if (isClosed)
            {
                return;
            }

            PSRemotingTransportException psrte = new PSRemotingTransportException(RemotingErrorIdStrings.IPCSignalTimedOut);
            RaiseErrorHandler(new TransportErrorOccuredEventArgs(psrte, TransportMethodEnum.ReceiveShellOutputEx));
        }

        private void StopSignalTimerAndDecrementOperations()
        {
            lock (syncObject)
            {
                _signalTimeOutTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        /// <summary>
        /// Used by ServicePendingCallbacks to give the control to derived classes for
        /// processing data that the base class does not understand.
        /// </summary>
        /// <param name="privateData">
        /// Derived class specific data to process. For command transport manager this
        /// should be a boolean.
        /// </param>
        internal override void ProcessPrivateData(object privateData)
        {
            Dbg.Assert(null != privateData, "privateData cannot be null.");

            // For this version...only a boolean can be used for privateData.
            bool shouldRaiseSignalCompleted = (bool)privateData;
            if (shouldRaiseSignalCompleted)
            {
                base.RaiseSignalCompleted();
            }
        }

        internal void OnCloseCmdCompleted()
        {
            PSEtwLog.LogAnalyticInformational(PSEventId.WSManCloseCommandCallbackReceived,
                PSOpcode.Disconnect, PSTask.None,
                PSKeyword.Transport | PSKeyword.UseAlwaysAnalytic,
                RunspacePoolInstanceId.ToString(), powershellInstanceId.ToString());

            // Raising close completed only after receiving ACK from the server
            // otherwise may introduce race conditions on the server side
            RaiseCloseCompleted();
        }

        private void SendOneItem()
        {
            byte[] data = null;
            DataPriorityType priorityType = DataPriorityType.Default;
            // serializedPipeline is static ie., data is added to this collection at construction time only
            // and data is accessed by only one thread at any given time..so we can depend on this count
            if (serializedPipeline.Length > 0)
            {
                data = serializedPipeline.ReadOrRegisterCallback(null);
            }
            else
            {
                // This will either return data or register callback but doesn't do both.
                data = dataToBeSent.ReadOrRegisterCallback(_onDataAvailableToSendCallback, out priorityType);
            }

            if (null != data)
            {
                SendData(data, priorityType);
            }
        }

        private void SendData(byte[] data, DataPriorityType priorityType)
        {
            PSEtwLog.LogAnalyticInformational(
                    PSEventId.WSManSendShellInputEx, PSOpcode.Send, PSTask.None,
                    PSKeyword.Transport | PSKeyword.UseAlwaysAnalytic,
                    RunspacePoolInstanceId.ToString(),
                    powershellInstanceId.ToString(),
                    data.Length.ToString(CultureInfo.InvariantCulture));

            lock (syncObject)
            {
                if (isClosed)
                {
                    return;
                }

                _stdInWriter.WriteLine(OutOfProcessUtils.CreateDataPacket(data,
                    priorityType,
                    powershellInstanceId));
            }
        }

        private void OnDataAvailableCallback(byte[] data, DataPriorityType priorityType)
        {
            Dbg.Assert(null != data, "data cannot be null in the data available callback");

            tracer.WriteLine("Received data from dataToBeSent store.");
            SendData(data, priorityType);
        }

        #endregion
    }
}

namespace System.Management.Automation.Remoting.Server
{
    internal class OutOfProcessServerSessionTransportManager : AbstractServerSessionTransportManager
    {
        #region Private Data

        private OutOfProcessTextWriter _stdOutWriter;
        private OutOfProcessTextWriter _stdErrWriter;
        private Dictionary<Guid, OutOfProcessServerTransportManager> _cmdTransportManagers;
        private object _syncObject = new object();

        #endregion

        #region Constructors

        internal OutOfProcessServerSessionTransportManager(OutOfProcessTextWriter outWriter, OutOfProcessTextWriter errWriter, PSRemotingCryptoHelperServer cryptoHelper)
            : base(BaseTransportManager.DefaultFragmentSize, cryptoHelper)
        {
            Dbg.Assert(null != outWriter, "outWriter cannot be null.");
            Dbg.Assert(null != errWriter, "errWriter cannot be null.");
            _stdOutWriter = outWriter;
            _stdErrWriter = errWriter;
            _cmdTransportManagers = new Dictionary<Guid, OutOfProcessServerTransportManager>();
        }

        #endregion

        #region Overrides

        internal override void ProcessRawData(byte[] data, string stream)
        {
            base.ProcessRawData(data, stream);

            // Send ACK back to the client as we have processed data.
            _stdOutWriter.WriteLine(OutOfProcessUtils.CreateDataAckPacket(Guid.Empty));
        }

        internal override void Prepare()
        {
            throw new NotSupportedException();
        }

        protected override void SendDataToClient(byte[] data, bool flush, bool reportAsPending, bool reportAsDataBoundary)
        {
            _stdOutWriter.WriteLine(OutOfProcessUtils.CreateDataPacket(data,
                DataPriorityType.Default, Guid.Empty));
        }

        internal override void ReportExecutionStatusAsRunning()
        {
            //No-OP for outofProc TMs
        }

        internal void CreateCommandTransportManager(Guid powerShellCmdId)
        {
            OutOfProcessServerTransportManager cmdTM = new OutOfProcessServerTransportManager(_stdOutWriter, _stdErrWriter,
                powerShellCmdId, this.TypeTable, this.Fragmentor.FragmentSize, this.CryptoHelper);
            // this will make the Session's DataReady event handler handle 
            // the commands data as well. This is because the state machine
            // is per session.
            cmdTM.MigrateDataReadyEventHandlers(this);

            lock (_syncObject)
            {
                // the dictionary is cleared by ServerPowershellDataStructure handler
                // once the clean up is complete for the transport manager
                Dbg.Assert(!_cmdTransportManagers.ContainsKey(powerShellCmdId), "key already exists");
                _cmdTransportManagers.Add(powerShellCmdId, cmdTM);
            }

            // send command ack..so that client can start sending data
            _stdOutWriter.WriteLine(OutOfProcessUtils.CreateCommandAckPacket(powerShellCmdId));
        }

        internal override AbstractServerTransportManager GetCommandTransportManager(Guid powerShellCmdId)
        {
            lock (_syncObject)
            {
                OutOfProcessServerTransportManager result = null;
                _cmdTransportManagers.TryGetValue(powerShellCmdId, out result);
                return result;
            }
        }

        internal override void RemoveCommandTransportManager(Guid powerShellCmdId)
        {
            lock (_syncObject)
            {
                _cmdTransportManagers.Remove(powerShellCmdId);
            }
        }

        internal override void Close(Exception reasonForClose)
        {
            RaiseClosingEvent();
        }

        #endregion
    }

    internal class OutOfProcessServerTransportManager : AbstractServerTransportManager
    {
        #region Private Data

        private OutOfProcessTextWriter _stdOutWriter;
        private OutOfProcessTextWriter _stdErrWriter;
        private Guid _powershellInstanceId;
        private bool _isDataAckSendPending;

        #endregion

        #region Constructors

        internal OutOfProcessServerTransportManager(OutOfProcessTextWriter stdOutWriter, OutOfProcessTextWriter stdErrWriter,
            Guid powershellInstanceId,
            TypeTable typeTableToUse,
            int fragmentSize,
            PSRemotingCryptoHelper cryptoHelper)
            : base(fragmentSize, cryptoHelper)
        {
            _stdOutWriter = stdOutWriter;
            _stdErrWriter = stdErrWriter;
            _powershellInstanceId = powershellInstanceId;
            this.TypeTable = typeTableToUse;

            this.WSManTransportErrorOccured += HandleWSManTransportError;
        }

        #endregion

        #region Private Methods

        private void HandleWSManTransportError(object sender, TransportErrorOccuredEventArgs e)
        {
            _stdErrWriter.WriteLine(StringUtil.Format(RemotingErrorIdStrings.RemoteTransportError, e.Exception.TransportMessage));
        }

        #endregion

        #region Overrides

        internal override void ProcessRawData(byte[] data, string stream)
        {
            _isDataAckSendPending = true;
            base.ProcessRawData(data, stream);

            if (_isDataAckSendPending)
            {
                _isDataAckSendPending = false;
                // Send ACK back to the client as we have processed data.
                _stdOutWriter.WriteLine(OutOfProcessUtils.CreateDataAckPacket(_powershellInstanceId));
            }
        }

        internal override void ReportExecutionStatusAsRunning()
        {
            //No-OP for outofProc TMs
        }

        protected override void SendDataToClient(byte[] data, bool flush, bool reportAsPending, bool reportAsDataBoundary)
        {
            _stdOutWriter.WriteLine(OutOfProcessUtils.CreateDataPacket(data,
                DataPriorityType.Default, _powershellInstanceId));
        }

        internal override void Prepare()
        {
            if (_isDataAckSendPending)
            {
                _isDataAckSendPending = false;
                // let the base class prepare itself.
                base.Prepare();
                // Send ACK back to the client as we have processed data.
                _stdOutWriter.WriteLine(OutOfProcessUtils.CreateDataAckPacket(_powershellInstanceId));
            }
        }

        internal override void Close(Exception reasonForClose)
        {
            RaiseClosingEvent();
        }

        #endregion
    }
}
