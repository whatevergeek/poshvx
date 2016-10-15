/*============================================================================
 * Copyright (C) Microsoft Corporation, All rights reserved. 
 *============================================================================
 */

#region Using directives

using System;
using System.ComponentModel;
using System.Management.Automation;

#endregion

namespace Microsoft.Management.Infrastructure.CimCmdlets
{
    /// <summary>
    /// <para>
    /// Abstract Cimindication event args, which containing all elements related to
    /// an Cimindication.
    /// </para>
    /// </summary>
    public abstract class CimIndicationEventArgs : EventArgs
    {
        /// <summary>
        /// <para>
        /// Returns an Object value for an operation context
        /// </para>
        /// </summary>
        public Object Context
        {
            get
            {
                return context;
            }
        }
        internal Object context;
    }

    /// <summary>
    /// Cimindication exception event args, which containing occurred exception
    /// </summary>
    public class CimIndicationEventExceptionEventArgs : CimIndicationEventArgs
    {
        /// <summary>
        /// <para>
        /// Returns an exception
        /// </para>
        /// </summary>
        public Exception Exception
        {
            get
            {
                return exception;
            }
        }
        private Exception exception;

        /// <summary>
        /// <para>
        /// Constructor
        /// </para>
        /// </summary>
        /// <param name="result"></param>
        public CimIndicationEventExceptionEventArgs(Exception theException)
        {
            context = null;
            this.exception = theException;
        }
    }

    /// <summary>
    /// Cimindication event args, which containing all elements related to
    /// an Cimindication.
    /// </summary>
    public class CimIndicationEventInstanceEventArgs : CimIndicationEventArgs
    {
        /// <summary>
        /// Get ciminstance of the indication object
        /// </summary>
        public CimInstance NewEvent
        {
            get
            {
                return (result == null) ? null : result.Instance;
            }
        }

        /// <summary>
        /// Get MachineId of the indication object
        /// </summary>
        public string MachineId
        {
            get
            {
                return (result == null) ? null : result.MachineId;
            }
        }

        /// <summary>
        /// Get BookMark of the indication object
        /// </summary>
        public string Bookmark
        {
            get
            {
                return (result == null) ? null : result.Bookmark;
            }
        }

        /// <summary>
        /// <para>
        /// Constructor
        /// </para>
        /// </summary>
        /// <param name="result"></param>
        public CimIndicationEventInstanceEventArgs(CimSubscriptionResult result)
        {
            context = null;
            this.result = result;
        }

        /// <summary>
        /// <para>
        /// subscription result
        /// </para>
        /// </summary>
        private CimSubscriptionResult result;
    }

    /// <summary>
    /// <para>
    /// A public class used to start/stop the subscription to specific indication source,
    /// and listen to the incoming indications, event <see cref="CimIndicationArrived"/>
    /// will be raised for each cimindication.
    /// </para>
    /// </summary>
    public class CimIndicationWatcher
    {
        /// <summary>
        /// status of <see cref="CimIndicationWatcher"/> object.
        /// </summary>
        internal enum Status
        {
            Default,
            Started,
            Stopped
        }

        /// <summary>
        /// <para>
        /// CimIndication arrived event
        /// </para>
        /// </summary>
        public event EventHandler<CimIndicationEventArgs> CimIndicationArrived;

        /// <summary>
        /// <para>
        /// Constructor with given computerName, namespace, queryExpression and timeout
        /// </para>
        /// </summary>
        /// <param name="computerName"></param>
        /// <param name="nameSpace"></param>
        /// <param name="queryExpression"></param>
        /// <param name="operationTimeout"></param>
        public CimIndicationWatcher(
            string computerName,
            string theNamespace,
            string queryDialect,
            string queryExpression,
            UInt32 operationTimeout)
        {
            ValidationHelper.ValidateNoNullorWhiteSpaceArgument(queryExpression, queryExpressionParameterName);
            computerName = ConstValue.GetComputerName(computerName);
            theNamespace = ConstValue.GetNamespace(theNamespace);
            Initialize(computerName, null, theNamespace, queryDialect, queryExpression, operationTimeout);
        }

        /// <summary>
        /// <para>
        /// Constructor with given cimsession, namespace, queryExpression and timeout
        /// </para>
        /// </summary>
        /// <param name="cimSession"></param>
        /// <param name="nameSpace"></param>
        /// <param name="queryExpression"></param>
        /// <param name="operationTimeout"></param>
        public CimIndicationWatcher(
            CimSession cimSession,
            string theNamespace,
            string queryDialect,
            string queryExpression,
            UInt32 operationTimeout)
        {
            ValidationHelper.ValidateNoNullorWhiteSpaceArgument(queryExpression, queryExpressionParameterName);
            ValidationHelper.ValidateNoNullArgument(cimSession, cimSessionParameterName);
            theNamespace = ConstValue.GetNamespace(theNamespace);
            Initialize(null, cimSession, theNamespace, queryDialect, queryExpression, operationTimeout);
        }

        /// <summary>
        /// <para>
        /// Initialize
        /// </para>
        /// </summary>
        private void Initialize(
            string theComputerName,
            CimSession theCimSession,
            string theNameSpace,
            string theQueryDialect,
            string theQueryExpression,
            UInt32 theOpreationTimeout)
        {
            enableRaisingEvents = false;
            status = Status.Default;
            myLock = new object();
            cimRegisterCimIndication = new CimRegisterCimIndication();
            cimRegisterCimIndication.OnNewSubscriptionResult += NewSubscriptionResultHandler;

            this.cimSession = theCimSession;
            this.nameSpace = theNameSpace;
            this.queryDialect = ConstValue.GetQueryDialectWithDefault(theQueryDialect);
            this.queryExpression = theQueryExpression;
            this.opreationTimeout = theOpreationTimeout;
            this.computerName = theComputerName;
        }

        /// <summary>
        /// <para>
        /// Handler of new subscription result
        /// </para>
        /// </summary>
        /// <param name="src"></param>
        /// <param name="args"></param>
        private void NewSubscriptionResultHandler(object src, CimSubscriptionEventArgs args)
        {
            EventHandler<CimIndicationEventArgs> temp = this.CimIndicationArrived;
            if (temp != null)
            {
                // raise the event
                CimSubscriptionResultEventArgs resultArgs = args as CimSubscriptionResultEventArgs;
                if (resultArgs != null)
                    temp(this, new CimIndicationEventInstanceEventArgs(resultArgs.Result));
                else
                {
                    CimSubscriptionExceptionEventArgs exceptionArgs = args as CimSubscriptionExceptionEventArgs;
                    if (exceptionArgs != null)
                        temp(this, new CimIndicationEventExceptionEventArgs(exceptionArgs.Exception));
                }
            }
        }

        /// <summary>
        /// <para>
        /// Will be called by admin\monad\src\eengine\EventManager.cs:
        /// PSEventManager::ProcessNewSubscriber to start to listen to the Cim Indication.
        /// </para>
        /// <para>
        /// If set EnableRaisingEvents to false, which will be ignored
        /// </para>
        /// </summary>
#if !CORECLR
        [BrowsableAttribute(false)]
#endif
        public bool EnableRaisingEvents
        {
            get
            {
                return enableRaisingEvents;
            }
            set
            {
                DebugHelper.WriteLogEx();
                if (value && !enableRaisingEvents)
                {
                    enableRaisingEvents = value;
                    Start();
                }
            }
        }
        private bool enableRaisingEvents;

        /// <summary>
        /// <para>
        /// Start the subscription
        /// </para>
        /// </summary>
        public void Start()
        {
            DebugHelper.WriteLogEx();

            lock(myLock)
            {
                if (status == Status.Default)
                {
                    if (this.cimSession == null)
                    {
                        cimRegisterCimIndication.RegisterCimIndication(
                            this.computerName,
                            this.nameSpace,
                            this.queryDialect,
                            this.queryExpression,
                            this.opreationTimeout);
                    }
                    else
                    {
                        cimRegisterCimIndication.RegisterCimIndication(
                            this.cimSession,
                            this.nameSpace,
                            this.queryDialect,
                            this.queryExpression,
                            this.opreationTimeout);
                    }
                    status = Status.Started;
                }
            }
        }

        /// <summary>
        /// <para>
        /// Unsubscribe the subscription
        /// </para>
        /// </summary>
        public void Stop()
        {
            DebugHelper.WriteLogEx("Status = {0}", 0, this.status);

            lock (this.myLock)
            {
                if (status == Status.Started)
                {
                    if (this.cimRegisterCimIndication != null)
                    {
                        DebugHelper.WriteLog("Dispose CimRegisterCimIndication object", 4);
                        this.cimRegisterCimIndication.Dispose();
                    }
                    status = Status.Stopped;
                }
            }
        }

        #region internal method
        /// <summary>
        /// Set the cmdlet object to throw ThrowTerminatingError
        /// in case there is a subscription failure
        /// </summary>
        /// <param name="cmdlet"></param>
        internal void SetCmdlet(Cmdlet cmdlet)
        {
            if (this.cimRegisterCimIndication != null)
            {
                this.cimRegisterCimIndication.Cmdlet = cmdlet;
            }
        }
        #endregion

        #region private members
        /// <summary>
        /// <para>
        /// CimRegisterCimIndication object
        /// </para>
        /// </summary>
        private CimRegisterCimIndication cimRegisterCimIndication;

        /// <summary>
        /// the status of <see cref="CimIndicationWatcher"/> object
        /// </summary>
        private Status status;

        /// <summary>
        /// lock started field
        /// </summary>
        private object myLock;

        /// <summary>
        /// CimSession parameter name
        /// </summary>
        private const string cimSessionParameterName = "cimSession";

        /// <summary>
        /// QueryExpression parameter name
        /// </summary>
        private const string queryExpressionParameterName = "queryExpression";

        #region parameters
        /// <summary>
        /// <para>
        /// parameters used to start the subscription
        /// </para>
        /// </summary>
        private string computerName;
        private CimSession cimSession;
        private string nameSpace;
        private string queryDialect;
        private string queryExpression;
        private UInt32 opreationTimeout;
        #endregion
        #endregion
    }
}//End namespace
