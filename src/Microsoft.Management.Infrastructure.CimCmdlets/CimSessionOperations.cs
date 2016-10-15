/*============================================================================
 * Copyright (C) Microsoft Corporation, All rights reserved. 
 *============================================================================
 */

#region Using directives

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Globalization;
using Microsoft.Management.Infrastructure.Options;

#endregion

namespace Microsoft.Management.Infrastructure.CimCmdlets
{
    #region CimSessionWrapper

    internal class CimSessionWrapper
    {
        #region members

        /// <summary>
        /// id of the cimsession
        /// </summary>
        public uint SessionId
        {
            get
            {
                return this.sessionId;
            }
        }
        private uint sessionId;

        /// <summary>
        /// instanceId of the cimsession
        /// </summary>
        public Guid InstanceId
        {
            get
            {
                return this.instanceId;
            }
        }
        private Guid instanceId;

        /// <summary>
        /// name of the cimsession
        /// </summary>
        public string Name
        {
            get
            {
                return this.name;
            }
        }
        private string name;

        /// <summary>
        /// computer name of the cimsession
        /// </summary>
        public string ComputerName
        {
            get
            {
                return this.computerName;
            }
        }
        private string computerName;

        /// <summary>
        /// wrapped cimsession object
        /// </summary>
        public CimSession CimSession
        {
            get
            {
                return this.cimSession;
            }
        }
        private CimSession cimSession;

        /// <summary>
        /// computer name of the cimsession
        /// </summary>
        public string Protocol
        {
            get
            {
                switch (protocol)
                {
                    case ProtocolType.Dcom:
                        return "DCOM";
                    case ProtocolType.Default:
                    case ProtocolType.Wsman:                   
                    default:
                        return "WSMAN";
                }
            }
        }
        internal ProtocolType GetProtocolType()
        {
            return protocol;
        }
        private ProtocolType protocol;

        /// <summary>
        /// PSObject that wrapped the cimSession
        /// </summary>
        private PSObject psObject;

        #endregion

        internal CimSessionWrapper(
            uint theSessionId,
            Guid theInstanceId,
            string theName,
            string theComputerName,
            CimSession theCimSession,
            ProtocolType theProtocol)
        {
            this.sessionId = theSessionId;
            this.instanceId = theInstanceId;
            this.name = theName;
            this.computerName = theComputerName;
            this.cimSession = theCimSession;
            this.psObject = null;
            this.protocol = theProtocol;
        }

        internal PSObject GetPSObject()
        {
            if (psObject == null)
            {
                psObject = new PSObject(this.cimSession);
                psObject.Properties.Add(new PSNoteProperty(CimSessionState.idPropName, this.sessionId));
                psObject.Properties.Add(new PSNoteProperty(CimSessionState.namePropName, this.name));
                psObject.Properties.Add(new PSNoteProperty(CimSessionState.instanceidPropName, this.instanceId));
                psObject.Properties.Add(new PSNoteProperty(CimSessionState.computernamePropName, this.ComputerName));
                psObject.Properties.Add(new PSNoteProperty(CimSessionState.protocolPropName, this.Protocol));
            }
            else
            {
                psObject.Properties[CimSessionState.idPropName].Value = this.SessionId;
                psObject.Properties[CimSessionState.namePropName].Value = this.name;
                psObject.Properties[CimSessionState.instanceidPropName].Value = this.instanceId;
                psObject.Properties[CimSessionState.computernamePropName].Value = this.ComputerName;
                psObject.Properties[CimSessionState.protocolPropName].Value = this.Protocol;
            }
            return psObject;
        }
    }

    #endregion

    #region CimSessionState

    /// <summary>
    /// <para>
    /// Class used to hold all cimsession related status data related to a runspace.
    /// Including the CimSession cache, session counters for generating session name.
    /// </para>
    /// </summary>
    internal class CimSessionState : IDisposable
    {
        #region private members

        /// <summary>
        /// Default session name.
        /// If a name is not passed, then the session is given the name CimSession<int>,
        /// where <int> is the next available session number.
        /// For example, CimSession1, CimSession2, etc...
        /// </summary>
        internal static string CimSessionClassName = "CimSession";

        /// <summary>
        /// CimSession object name
        /// </summary>
        internal static string CimSessionObject = "{CimSession Object}";

        /// <summary>
        /// <para>
        /// CimSession object path, which is identifying a cimsession object
        /// </para>
        /// </summary>
        internal static string SessionObjectPath = @"CimSession id = {0}, name = {2}, ComputerName = {3}, instance id = {1}";

        /// <summary>
        /// Id property name of cimsession wrapper object
        /// </summary>
        internal static string idPropName = "Id";

        /// <summary>
        /// Instanceid property name of cimsession wrapper object
        /// </summary>
        internal static string instanceidPropName = "InstanceId";

        /// <summary>
        /// Name property name of cimsession wrapper object
        /// </summary>
        internal static string namePropName = "Name";

        /// <summary>
        /// Computer name property name of cimsession object
        /// </summary>
        internal static string computernamePropName = "ComputerName";

        /// <summary>
        /// Protocol name property name of cimsession object
        /// </summary>
        internal static string protocolPropName = "Protocol";

        /// <summary>
        /// <para>
        /// session counter bound to current runspace.
        /// </para>
        /// </summary>
        private UInt32 sessionNameCounter;

        /// <summary>
        /// <para>
        /// Dictionary used to holds all CimSessions in current runspace by session name.
        /// </para>
        /// </summary>
        private Dictionary<string, HashSet<CimSessionWrapper>> curCimSessionsByName;

        /// <summary>
        /// <para>
        /// Dictionary used to holds all CimSessions in current runspace by computer name.
        /// </para>
        /// </summary>
        private Dictionary<string, HashSet<CimSessionWrapper>> curCimSessionsByComputerName;

        /// <summary>
        /// <para>
        /// Dictionary used to holds all CimSessions in current runspace by instance ID.
        /// </para>
        /// </summary>
        private Dictionary<Guid, CimSessionWrapper> curCimSessionsByInstanceId;

        /// <summary>
        /// <para>
        /// Dictionary used to holds all CimSessions in current runspace by session id.
        /// </para>
        /// </summary>
        private Dictionary<UInt32, CimSessionWrapper> curCimSessionsById;

        /// <summary>
        /// <para>
        /// Dictionary used to link CimSession object with PSObject.
        /// </para>
        /// </summary>
        private Dictionary<CimSession, CimSessionWrapper> curCimSessionWrapper;

        #endregion

        /// <summary>
        /// <para>
        /// constructor
        /// </para>
        /// </summary>
        internal CimSessionState()
        {
            sessionNameCounter = 1;
            curCimSessionsByName = new Dictionary<string, HashSet<CimSessionWrapper>>(
                StringComparer.OrdinalIgnoreCase);
            curCimSessionsByComputerName = new Dictionary<string, HashSet<CimSessionWrapper>>(
                StringComparer.OrdinalIgnoreCase);
            curCimSessionsByInstanceId = new Dictionary<Guid, CimSessionWrapper>();
            curCimSessionsById = new Dictionary<uint, CimSessionWrapper>();
            curCimSessionWrapper = new Dictionary<CimSession, CimSessionWrapper>();
        }

        /// <summary>
        /// <para>
        /// Get sessions count.
        /// </para>
        /// </summary>
        /// <returns>The count of session objects in current runspace.</returns>
        internal int GetSessionsCount()
        {
            return this.curCimSessionsById.Count;
        }

        /// <summary>
        /// <para>
        /// Generates an unique session id.
        /// </para>
        /// </summary>
        /// <returns>Unique session id under current runspace</returns>
        internal UInt32 GenerateSessionId()
        {
            return this.sessionNameCounter++;
        }
        #region IDisposable

        /// <summary>
        /// <para>
        /// Indicates whether this object was disposed or not
        /// </para>
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// <para>
        /// Dispose() calls Dispose(true).
        /// Implement IDisposable. Do not make this method virtual.
        /// A derived class should not be able to override this method.
        /// </para>
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            // This object will be cleaned up by the Dispose method.
            // Therefore, you should call GC.SuppressFinalize to
            // take this object off the finalization queue
            // and prevent finalization code for this object
            // from executing a second time.
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// <para>
        /// Dispose(bool disposing) executes in two distinct scenarios.
        /// If disposing equals true, the method has been called directly
        /// or indirectly by a user's code. Managed and unmanaged resources
        /// can be disposed.
        /// If disposing equals false, the method has been called by the
        /// runtime from inside the finalizer and you should not reference
        /// other objects. Only unmanaged resources can be disposed.
        /// </para>
        /// </summary>
        /// <param name="disposing">Whether it is directly called</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!this._disposed)
            {
                if (disposing)
                {
                    // free managed resources
                    Cleanup();
                    this._disposed = true;
                }
                // free native resources if there are any
            }
        }

        /// <summary>
        /// <para>
        /// Performs application-defined tasks associated with freeing, releasing, or
        /// resetting unmanaged resources.
        /// </para>
        /// </summary>
        public void Cleanup()
        {
            foreach (CimSession session in curCimSessionWrapper.Keys)
            {
                session.Dispose();
            }
            curCimSessionWrapper.Clear();
            curCimSessionsByName.Clear();
            curCimSessionsByComputerName.Clear();
            curCimSessionsByInstanceId.Clear();
            curCimSessionsById.Clear();
            sessionNameCounter = 1;
        }

        #endregion

        #region Add CimSession to/remove CimSession from cache

        /// <summary>
        /// <para>
        /// Add new CimSession object to cache
        /// </para>
        /// </summary>
        /// <param name="session"></param>
        /// <param name="sessionId"></param>
        /// <param name="instanceId"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        internal PSObject AddObjectToCache(
            CimSession session,
            UInt32 sessionId,
            Guid instanceId,
            String name,
            String computerName,
            ProtocolType protocol)
        {
            CimSessionWrapper wrapper = new CimSessionWrapper(
                sessionId, instanceId, name, computerName, session, protocol);

            HashSet<CimSessionWrapper> objects;
            if (!this.curCimSessionsByComputerName.TryGetValue(computerName, out objects))
            {
                objects = new HashSet<CimSessionWrapper>();
                this.curCimSessionsByComputerName.Add(computerName, objects);
            }
            objects.Add(wrapper);

            if (!this.curCimSessionsByName.TryGetValue(name, out objects))
            {
                objects = new HashSet<CimSessionWrapper>();
                this.curCimSessionsByName.Add(name, objects);
            }
            objects.Add(wrapper);

            this.curCimSessionsByInstanceId.Add(instanceId, wrapper);
            this.curCimSessionsById.Add(sessionId, wrapper);
            this.curCimSessionWrapper.Add(session, wrapper);
            return wrapper.GetPSObject();
        }

        /// <summary>
        /// <para>
        /// Generates remove session message by given wrapper object.
        /// </para>
        /// </summary>
        /// <param name="psObject"></param>
        internal string GetRemoveSessionObjectTarget(PSObject psObject)
        {
            String message = String.Empty;
            if (psObject.BaseObject is CimSession)
            {
                UInt32 id = 0x0;
                Guid instanceId = Guid.Empty;
                String name = String.Empty;
                String computerName = string.Empty;
                if (psObject.Properties[idPropName].Value is UInt32)
                {
                    id = Convert.ToUInt32(psObject.Properties[idPropName].Value, null);
                }
                if (psObject.Properties[instanceidPropName].Value is Guid)
                {
                    instanceId = (Guid)psObject.Properties[instanceidPropName].Value;
                }
                if (psObject.Properties[namePropName].Value is String)
                {
                    name = (String)psObject.Properties[namePropName].Value;
                }
                if (psObject.Properties[computernamePropName].Value is String)
                {
                    computerName = (String)psObject.Properties[computernamePropName].Value;
                }
                message = String.Format(CultureInfo.CurrentUICulture, SessionObjectPath, id, instanceId, name, computerName);
            }
            return message;
        }

        /// <summary>
        /// <para>
        /// Remove given <see cref="PSObject"/> object from cache
        /// </para>
        /// </summary>
        /// <param name="psObject"></param>
        internal void RemoveOneSessionObjectFromCache(PSObject psObject)
        {
            DebugHelper.WriteLogEx();

            if (psObject.BaseObject is CimSession)
            {
                RemoveOneSessionObjectFromCache(psObject.BaseObject as CimSession);
            }
        }

        /// <summary>
        /// <para>
        /// Remove given <see cref="CimSession"/> object from cache
        /// </para>
        /// </summary>
        /// <param name="session"></param>
        internal void RemoveOneSessionObjectFromCache(CimSession session)
        {
            DebugHelper.WriteLogEx();

            if (!this.curCimSessionWrapper.ContainsKey(session))
            {
                return;
            }
            CimSessionWrapper wrapper = this.curCimSessionWrapper[session];
            String name = wrapper.Name;
            String computerName = wrapper.ComputerName;

            DebugHelper.WriteLog("name {0}, computername {1}, id {2}, instanceId {3}", 1, name, computerName, wrapper.SessionId, wrapper.InstanceId);

            HashSet<CimSessionWrapper> objects;
            if (this.curCimSessionsByComputerName.TryGetValue(computerName, out objects))
            {
                objects.Remove(wrapper);
            }
            if (this.curCimSessionsByName.TryGetValue(name, out objects))
            {
                objects.Remove(wrapper);
            }
            RemoveSessionInternal(session, wrapper);
        }

        /// <summary>
        /// <para>
        /// Remove given <see cref="CimSession"/> object from partial of the cache only.
        /// </para>
        /// </summary>
        /// <param name="session"></param>
        /// <param name="psObject"></param>
        private void RemoveSessionInternal(CimSession session, CimSessionWrapper wrapper)
        {
            DebugHelper.WriteLogEx();

            this.curCimSessionsByInstanceId.Remove(wrapper.InstanceId);
            this.curCimSessionsById.Remove(wrapper.SessionId);
            this.curCimSessionWrapper.Remove(session);
            session.Dispose();
        }

        #endregion

        #region Query CimSession from cache

        /// <summary>
        /// <para>
        /// Add ErrorRecord to list.
        /// </para>
        /// </summary>
        /// <param name="errRecords"></param>
        /// <param name="propertyName"></param>
        /// <param name="propertyValue"></param>
        private void AddErrorRecord(
            ref List<ErrorRecord> errRecords,
            string propertyName,
            object propertyValue)
        {
            errRecords.Add(
                new ErrorRecord(
                    new CimException(String.Format(CultureInfo.CurrentUICulture, Strings.CouldNotFindCimsessionObject, propertyName, propertyValue)),
                    string.Empty,
                    ErrorCategory.ObjectNotFound,
                    null));
        }

        /// <summary>
        /// Query session list by given id array
        /// </summary>
        /// <param name="ids"></param>
        /// <returns>List of session wrapper objects</returns>
        internal IEnumerable<PSObject> QuerySession(IEnumerable<UInt32> ids,
            out IEnumerable<ErrorRecord> errorRecords)
        {
            HashSet<PSObject> sessions = new HashSet<PSObject>();
            HashSet<uint> sessionIds = new HashSet<uint>();
            List<ErrorRecord> errRecords = new List<ErrorRecord>();
            errorRecords = errRecords;
            // NOTES: use template function to implement this will save duplicate code
            foreach (UInt32 id in ids)
            {
                if (this.curCimSessionsById.ContainsKey(id))
                {
                    if (!sessionIds.Contains(id))
                    {
                        sessionIds.Add(id);
                        sessions.Add(this.curCimSessionsById[id].GetPSObject());
                    }
                }
                else
                {
                    AddErrorRecord(ref errRecords, idPropName, id);
                }
            }
            return sessions;
        }

        /// <summary>
        /// Query session list by given instance id array
        /// </summary>
        /// <param name="instanceIds"></param>
        /// <returns>List of session wrapper objects</returns>
        internal IEnumerable<PSObject> QuerySession(IEnumerable<Guid> instanceIds,
            out IEnumerable<ErrorRecord> errorRecords)
        {
            HashSet<PSObject> sessions = new HashSet<PSObject>();
            HashSet<uint> sessionIds = new HashSet<uint>();
            List<ErrorRecord> errRecords = new List<ErrorRecord>();
            errorRecords = errRecords;
            foreach (Guid instanceid in instanceIds)
            {
                if (this.curCimSessionsByInstanceId.ContainsKey(instanceid))
                {
                    CimSessionWrapper wrapper = this.curCimSessionsByInstanceId[instanceid];
                    if (!sessionIds.Contains(wrapper.SessionId))
                    {
                        sessionIds.Add(wrapper.SessionId);
                        sessions.Add(wrapper.GetPSObject());
                    }
                }
                else
                {
                    AddErrorRecord(ref errRecords, instanceidPropName, instanceid);
                }
            }
            return sessions;
        }

        /// <summary>
        /// Query session list by given name array
        /// </summary>
        /// <param name="nameArray"></param>
        /// <returns>List of session wrapper objects</returns>
        internal IEnumerable<PSObject> QuerySession(IEnumerable<string> nameArray,
            out IEnumerable<ErrorRecord> errorRecords)
        {
            HashSet<PSObject> sessions = new HashSet<PSObject>();
            HashSet<uint> sessionIds = new HashSet<uint>();
            List<ErrorRecord> errRecords = new List<ErrorRecord>();
            errorRecords = errRecords;
            foreach (string name in nameArray)
            {
                bool foundSession = false;
                WildcardPattern pattern = new WildcardPattern(name, WildcardOptions.IgnoreCase);
                foreach (KeyValuePair<String, HashSet<CimSessionWrapper>> kvp in this.curCimSessionsByName)
                {
                    if (pattern.IsMatch(kvp.Key))
                    {
                        HashSet<CimSessionWrapper> wrappers = kvp.Value;
                        foundSession = (wrappers.Count > 0);
                        foreach (CimSessionWrapper wrapper in wrappers)
                        {
                            if (!sessionIds.Contains(wrapper.SessionId))
                            {
                                sessionIds.Add(wrapper.SessionId);
                                sessions.Add(wrapper.GetPSObject());
                            }
                        }
                    }
                }
                if (!foundSession && !WildcardPattern.ContainsWildcardCharacters(name))
                {
                    AddErrorRecord(ref errRecords, namePropName, name);
                }
            }
            return sessions;
        }

        /// <summary>
        /// Query session list by given computer name array
        /// </summary>
        /// <param name="computernameArray"></param>
        /// <returns>List of session wrapper objects</returns>
        internal IEnumerable<PSObject> QuerySessionByComputerName(
            IEnumerable<string> computernameArray,
            out IEnumerable<ErrorRecord> errorRecords)
        {
            HashSet<PSObject> sessions = new HashSet<PSObject>();
            HashSet<uint> sessionIds = new HashSet<uint>();
            List<ErrorRecord> errRecords = new List<ErrorRecord>();
            errorRecords = errRecords;
            foreach (string computername in computernameArray)
            {
                bool foundSession = false;
                if (this.curCimSessionsByComputerName.ContainsKey(computername))
                {
                    HashSet<CimSessionWrapper> wrappers = this.curCimSessionsByComputerName[computername];
                    foundSession = (wrappers.Count > 0);
                    foreach (CimSessionWrapper wrapper in wrappers)
                    {
                        if (!sessionIds.Contains(wrapper.SessionId))
                        {
                            sessionIds.Add(wrapper.SessionId);
                            sessions.Add(wrapper.GetPSObject());
                        }
                    }
                }
                if (!foundSession)
                {
                    AddErrorRecord(ref errRecords, computernamePropName, computername);
                }
            }
            return sessions;
        }

        /// <summary>
        /// Query session list by given session objects array
        /// </summary>
        /// <param name="cimsessions"></param>
        /// <returns>List of session wrapper objects</returns>
        internal IEnumerable<PSObject> QuerySession(IEnumerable<CimSession> cimsessions,
            out IEnumerable<ErrorRecord> errorRecords)
        {
            HashSet<PSObject> sessions = new HashSet<PSObject>();
            HashSet<uint> sessionIds = new HashSet<uint>();
            List<ErrorRecord> errRecords = new List<ErrorRecord>();
            errorRecords = errRecords;
            foreach (CimSession cimsession in cimsessions)
            {
                if (this.curCimSessionWrapper.ContainsKey(cimsession))
                {
                    CimSessionWrapper wrapper = this.curCimSessionWrapper[cimsession];
                    if (!sessionIds.Contains(wrapper.SessionId))
                    {
                        sessionIds.Add(wrapper.SessionId);
                        sessions.Add(wrapper.GetPSObject());
                    }
                }
                else
                {
                    AddErrorRecord(ref errRecords, CimSessionClassName, CimSessionObject);
                }
            }
            return sessions;
        }

        /// <summary>
        /// Query session wrapper object
        /// </summary>
        /// <param name="cimsessions"></param>
        /// <returns>session wrapper</returns>
        internal CimSessionWrapper QuerySession(CimSession cimsession)
        {
            CimSessionWrapper wrapper;
            this.curCimSessionWrapper.TryGetValue(cimsession, out wrapper);
            return wrapper;
        }

        /// <summary>
        /// Query session object with given CimSessionInstanceID
        /// </summary>
        /// <param name="cimSessionInstanceId"></param>
        /// <returns>CimSession object</returns>
        internal CimSession QuerySession(Guid cimSessionInstanceId)
        {
            if (this.curCimSessionsByInstanceId.ContainsKey(cimSessionInstanceId))
            {
                CimSessionWrapper wrapper = this.curCimSessionsByInstanceId[cimSessionInstanceId];
                return wrapper.CimSession;
            }
            return null;
        }
        #endregion
    }

    #endregion

    #region CimSessionBase

    /// <summary>
    /// <para>
    /// Base class of all session operation classes.
    /// All sessions created will be held in a ConcurrentDictionary:cimSessions.
    /// It manages the lifecycle of the sessions being created for each
    /// runspace according to the state of the runspace.
    /// </para>
    /// </summary>
    internal class CimSessionBase
    {
        #region constructor

        /// <summary>
        /// Constructor
        /// </summary>
        public CimSessionBase()
        {
            this.sessionState = cimSessions.GetOrAdd(
                CurrentRunspaceId,
                delegate(Guid instanceId)
                {
                    if (Runspace.DefaultRunspace != null)
                    {
                        Runspace.DefaultRunspace.StateChanged += DefaultRunspace_StateChanged;
                    }
                    return new CimSessionState();
                });
        }

        #endregion

        #region members

        /// <summary>
        /// <para>
        /// Thread safe static dictionary to store session objects associated
        /// with each runspace, which is identified by a GUID. NOTE: cmdlet
        /// can running parallelly under more than one runspace(s).
        /// </para>
        /// </summary>
        static internal ConcurrentDictionary<Guid, CimSessionState> cimSessions
            = new ConcurrentDictionary<Guid, CimSessionState>();

        /// <summary>
        /// <para>
        /// Default runspace id
        /// </para>
        /// </summary>
        static internal Guid defaultRunspaceId = Guid.Empty;

        /// <summary>
        /// <para>
        /// Object used to hold all CimSessions and status data bound
        /// to current runspace.
        /// </para>
        /// </summary>
        internal CimSessionState sessionState;

        /// <summary>
        /// Get current runspace id
        /// </summary>
        private static Guid CurrentRunspaceId
        {
            get
            {
                if (Runspace.DefaultRunspace != null)
                {
                    return Runspace.DefaultRunspace.InstanceId;
                }
                else
                {
                    return CimSessionBase.defaultRunspaceId;
                }
            }
        }
        #endregion

        public static CimSessionState GetCimSessionState()
        {
            CimSessionState state = null;
            cimSessions.TryGetValue(CurrentRunspaceId, out state);
            return state;
        }

        /// <summary>
        /// <para>
        /// clean up the dictionaries if the runspace is closed or broken.
        /// </para>
        /// </summary>
        /// <param name="sender">Runspace</param>
        /// <param name="e">Event args</param>
        private static void DefaultRunspace_StateChanged(object sender, RunspaceStateEventArgs e)
        {
            Runspace runspace = (Runspace)sender;
            switch (e.RunspaceStateInfo.State)
            {
                case RunspaceState.Broken:
                case RunspaceState.Closed:
                    CimSessionState state;
                    if (cimSessions.TryRemove(runspace.InstanceId, out state))
                    {
                        DebugHelper.WriteLog(String.Format(CultureInfo.CurrentUICulture, DebugHelper.runspaceStateChanged, runspace.InstanceId, e.RunspaceStateInfo.State));
                        state.Dispose();
                    }
                    runspace.StateChanged -= DefaultRunspace_StateChanged;
                    break;
                default:
                    break;
            }
        }
    }

    #endregion

    #region CimTestConnection

    #endregion

    #region CimNewSession

    /// <summary>
    /// <para>
    /// <c>CimNewSession</c> is the class to create cimSession
    /// based on given <c>NewCimSessionCommand</c>.
    /// </para>
    /// </summary>
    internal class CimNewSession : CimSessionBase, IDisposable
    {
        /// <summary>
        /// CimTestCimSessionContext
        /// </summary>
        internal class CimTestCimSessionContext : XOperationContextBase
        {
            /// <summary>
            /// <para>
            /// Constructor
            /// </para>
            /// </summary>
            /// <param name="theProxy"></param>
            /// <param name="wrapper"></param>
            internal CimTestCimSessionContext(
                CimSessionProxy theProxy,
                CimSessionWrapper wrapper)
            {
                this.proxy = theProxy;
                this.cimSessionWrapper = wrapper;
                this.nameSpace = null;
            }

            /// <summary>
            /// <para>namespace</para>
            /// </summary>
            internal CimSessionWrapper CimSessionWrapper
            {
                get
                {
                    return this.cimSessionWrapper;
                }
            }
            private CimSessionWrapper cimSessionWrapper;
        }

        /// <summary>
        /// <para>
        /// constructor
        /// </para>
        /// </summary>
        internal CimNewSession() : base()
        {
            this.cimTestSession = new CimTestSession();
            this._disposed = false;
        }

        /// <summary>
        /// Create a new <see cref="CimSession"/> base on given cmdlet
        /// and its parameter
        /// </summary>        
        /// <param name="cmdlet"></param>
        /// <param name="sessionOptions"></param>
        /// <param name="credential"></param>
        internal void NewCimSession(NewCimSessionCommand cmdlet,
            CimSessionOptions sessionOptions,
            CimCredential credential)
        {
            DebugHelper.WriteLogEx();

            IEnumerable<string> computerNames = ConstValue.GetComputerNames(cmdlet.ComputerName);
            foreach (string computerName in computerNames)
            {
                CimSessionProxy proxy;
                if (sessionOptions == null)
                {
                    DebugHelper.WriteLog("Create CimSessionOption due to NewCimSessionCommand has null sessionoption", 1);
                    sessionOptions = CimSessionProxy.CreateCimSessionOption(computerName,
                        cmdlet.OperationTimeoutSec, credential);
                }
                proxy = new CimSessionProxyTestConnection(computerName, sessionOptions);
                string computerNameValue = (computerName == ConstValue.NullComputerName) ? ConstValue.LocalhostComputerName : computerName;
                CimSessionWrapper wrapper = new CimSessionWrapper(0, Guid.Empty, cmdlet.Name, computerNameValue, proxy.CimSession, proxy.Protocol);
                CimTestCimSessionContext context = new CimTestCimSessionContext(proxy, wrapper);
                proxy.ContextObject = context;
                // Skip test the connection if user intend to
                if(cmdlet.SkipTestConnection.IsPresent)
                {
                    AddSessionToCache(proxy.CimSession, context, new CmdletOperationBase(cmdlet));
                }
                else
                {
                    //CimSession will be returned as part of TestConnection
                    this.cimTestSession.TestCimSession(computerName, proxy); 
                }
            }
        }

        /// <summary>
        /// <para>
        /// Add session to global cache
        /// </para>
        /// </summary>
        /// <param name="cimSession"></param>
        /// <param name="context"></param>
        /// <param name="cmdlet"></param>
        internal void AddSessionToCache(CimSession cimSession, XOperationContextBase context, CmdletOperationBase cmdlet)
        {
            DebugHelper.WriteLogEx();

            CimTestCimSessionContext testCimSessionContext = context as CimTestCimSessionContext;
            UInt32 sessionId = this.sessionState.GenerateSessionId();
            string orginalSessioName = testCimSessionContext.CimSessionWrapper.Name;
            string sessionName = (orginalSessioName != null) ? orginalSessioName : String.Format(CultureInfo.CurrentUICulture, @"{0}{1}", CimSessionState.CimSessionClassName, sessionId);

            // detach CimSession from the proxy object
            CimSession createdCimSession = testCimSessionContext.Proxy.Detach();
            PSObject psObject = this.sessionState.AddObjectToCache(
                createdCimSession,
                sessionId,
                createdCimSession.InstanceId,
                sessionName,
                testCimSessionContext.CimSessionWrapper.ComputerName,
                testCimSessionContext.Proxy.Protocol);
            cmdlet.WriteObject(psObject, null);
        }

        /// <summary>
        /// <para>
        /// process all actions in the action queue
        /// </para>
        /// </summary>
        /// <param name="cmdletOperation">
        /// wrapper of cmdlet, <seealso cref="CmdletOperationBase"/> for details
        /// </param>
        public void ProcessActions(CmdletOperationBase cmdletOperation)
        {
            this.cimTestSession.ProcessActions(cmdletOperation);
        }

        /// <summary>
        /// <para>
        /// process remaining actions until all operations are completed or
        /// current cmdlet is terminated by user
        /// </para>
        /// </summary>
        /// <param name="cmdletOperation">
        /// wrapper of cmdlet, <seealso cref="CmdletOperationBase"/> for details
        /// </param>
        public void ProcessRemainActions(CmdletOperationBase cmdletOperation)
        {
            this.cimTestSession.ProcessRemainActions(cmdletOperation);
        }

        #region private members
        /// <summary>
        /// <para>
        /// <see cref="CimTestSession"/> object.
        /// </para>
        /// </summary>
        private CimTestSession cimTestSession;
        #endregion //private members

        #region IDisposable

        /// <summary>
        /// <para>
        /// Indicates whether this object was disposed or not
        /// </para>
        /// </summary>
        protected bool Disposed
        {
            get
            {
                return _disposed;
            }
        }
        private bool _disposed;

        /// <summary>
        /// <para>
        /// Dispose() calls Dispose(true).
        /// Implement IDisposable. Do not make this method virtual.
        /// A derived class should not be able to override this method.
        /// </para>
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            // This object will be cleaned up by the Dispose method.
            // Therefore, you should call GC.SuppressFinalize to
            // take this object off the finalization queue
            // and prevent finalization code for this object
            // from executing a second time.
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// <para>
        /// Dispose(bool disposing) executes in two distinct scenarios.
        /// If disposing equals true, the method has been called directly
        /// or indirectly by a user's code. Managed and unmanaged resources
        /// can be disposed.
        /// If disposing equals false, the method has been called by the
        /// runtime from inside the finalizer and you should not reference
        /// other objects. Only unmanaged resources can be disposed.
        /// </para>
        /// </summary>
        /// <param name="disposing">Whether it is directly called</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!this._disposed)
            {
                if (disposing)
                {
                    // free managed resources
                    this.cimTestSession.Dispose();
                    this._disposed = true;
                }
                // free native resources if there are any
            }
        }
        #endregion
    }//End Class

    #endregion

    #region CimGetSession

    /// <summary>
    /// <para>
    /// Get CimSession based on given id/instanceid/computername/name
    /// </para>
    /// </summary>
    internal class CimGetSession : CimSessionBase
    {
        /// <summary>
        /// constructor
        /// </summary>
        public CimGetSession() : base()
        {
        }

        /// <summary>
        /// Get <see cref="CimSession"/> objects based on the given cmdlet
        /// and its parameter
        /// </summary>
        /// <param name="cmdlet"></param>
        public void GetCimSession(GetCimSessionCommand cmdlet)
        {
            DebugHelper.WriteLogEx();

            IEnumerable<PSObject> sessionToGet = null;
            IEnumerable<ErrorRecord> errorRecords = null;
            switch (cmdlet.ParameterSetName)
            {
                case CimBaseCommand.ComputerNameSet:
                    if (cmdlet.ComputerName == null)
                    {
                        sessionToGet = this.sessionState.QuerySession(ConstValue.DefaultSessionName, out errorRecords);
                    }
                    else
                    {
                        sessionToGet = this.sessionState.QuerySessionByComputerName(cmdlet.ComputerName, out errorRecords);
                    }
                    break;
                case CimBaseCommand.SessionIdSet:
                    sessionToGet = this.sessionState.QuerySession(cmdlet.Id, out errorRecords);
                    break;
                case CimBaseCommand.InstanceIdSet:
                    sessionToGet = this.sessionState.QuerySession(cmdlet.InstanceId, out errorRecords);
                    break;
                case CimBaseCommand.NameSet:
                    sessionToGet = this.sessionState.QuerySession(cmdlet.Name, out errorRecords);
                    break;
                default:
                    break;
            }
            if (sessionToGet != null)
            {
                foreach(PSObject psobject in sessionToGet)
                {
                    cmdlet.WriteObject(psobject);
                }
            }
            if (errorRecords != null)
            {
                foreach (ErrorRecord errRecord in errorRecords)
                {
                    cmdlet.WriteError(errRecord);
                }
            }
        }

        #region helper methods

        #endregion
    }//End Class

    #endregion

    #region CimRemoveSession

    /// <summary>
    /// <para>
    /// Get CimSession based on given id/instanceid/computername/name
    /// </para>
    /// </summary>
    internal class CimRemoveSession : CimSessionBase
    {
        /// <summary>
        /// Remove session action string
        /// </summary>
        internal static string RemoveCimSessionActionName = "Remove CimSession";

        /// <summary>
        /// constructor
        /// </summary>
        public CimRemoveSession() : base()
        {
        }

        /// <summary>
        /// Remove the <see cref="CimSession"/> objects based on given cmdlet
        /// and its parameter
        /// </summary>
        /// <param name="cmdlet"></param>
        public void RemoveCimSession(RemoveCimSessionCommand cmdlet)
        {
            DebugHelper.WriteLogEx();

            IEnumerable<PSObject> sessionToRemove = null;
            IEnumerable<ErrorRecord> errorRecords = null;
            switch (cmdlet.ParameterSetName)
            {
                case CimBaseCommand.CimSessionSet:
                    sessionToRemove = this.sessionState.QuerySession(cmdlet.CimSession, out errorRecords);
                    break;
                case CimBaseCommand.ComputerNameSet:
                    sessionToRemove = this.sessionState.QuerySessionByComputerName(cmdlet.ComputerName, out errorRecords);
                    break;
                case CimBaseCommand.SessionIdSet:
                    sessionToRemove = this.sessionState.QuerySession(cmdlet.Id, out errorRecords);
                    break;
                case CimBaseCommand.InstanceIdSet:
                    sessionToRemove = this.sessionState.QuerySession(cmdlet.InstanceId, out errorRecords);
                    break;
                case CimBaseCommand.NameSet:
                    sessionToRemove = this.sessionState.QuerySession(cmdlet.Name, out errorRecords);
                    break;
                default:
                    break;
            }
            if (sessionToRemove != null)
            {
                foreach (PSObject psobject in sessionToRemove)
                {
                    if (cmdlet.ShouldProcess(this.sessionState.GetRemoveSessionObjectTarget(psobject), RemoveCimSessionActionName))
                    {
                        this.sessionState.RemoveOneSessionObjectFromCache(psobject);
                    }
                }
            }
            if (errorRecords != null)
            {
                foreach (ErrorRecord errRecord in errorRecords)
                {
                    cmdlet.WriteError(errRecord);
                }
            }
        }
    }//End Class

    #endregion

    #region CimTestSession

    /// <summary>
    /// Class <see cref="CimTestSession"/>, which is used to
    /// test cimsession and execute async operations.
    /// </summary>
    internal class CimTestSession : CimAsyncOperation
    {
        /// <summary>
        /// Constructor
        /// </summary>
        internal CimTestSession()
            : base()
        {
        }

        /// <summary>
        /// Test the session connection with
        /// given <see cref="CimSessionProxy"/> object.
        /// </summary>
        /// <param name="computerName"></param>
        /// <param name="proxy"></param>
        internal void TestCimSession(
            string computerName,
            CimSessionProxy proxy)
        {
            DebugHelper.WriteLogEx();
            this.SubscribeEventAndAddProxytoCache(proxy);
            proxy.TestConnectionAsync();
        }
    }

    #endregion

}//End namespace
