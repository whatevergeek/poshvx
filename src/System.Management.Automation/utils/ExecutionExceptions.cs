/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

#pragma warning disable 1634, 1691
#pragma warning disable 56506


using System.Runtime.Serialization;
using System.Diagnostics.CodeAnalysis;
using System.Management.Automation.Internal;

#if !CORECLR
using System.Security.Permissions;
#else
// Use stub for SerializableAttribute, SecurityPermissionAttribute and ISerializable related types.
using Microsoft.PowerShell.CoreClr.Stubs;
#endif

#pragma warning disable 1634, 1691 // Stops compiler from warning about unknown warnings

namespace System.Management.Automation
{
    #region CmdletInvocationException
    /// <summary>
    /// Indicates that a cmdlet hit a terminating error.
    /// </summary>
    /// <remarks>
    /// InnerException is the error which the cmdlet hit.
    /// </remarks>
    [Serializable]
    public class CmdletInvocationException : RuntimeException
    {
        #region ctor
        /// <summary>
        /// Instantiates a new instance of the CmdletInvocationException class
        /// </summary>
        /// <param name="errorRecord"></param>
        internal CmdletInvocationException(ErrorRecord errorRecord)
            : base(RetrieveMessage(errorRecord), RetrieveException(errorRecord))
        {
            if (null == errorRecord)
            {
                throw new ArgumentNullException("errorRecord");
            }
            _errorRecord = errorRecord;
            if (null != errorRecord.Exception)
            {
                // 2005/04/13-JonN Can't do this in an unsealed class: HelpLink = errorRecord.Exception.HelpLink;
                // Exception.Source is set by Throw
                // Source = errorRecord.Exception.Source;
            }
        }

        /// <summary>
        /// Instantiates a new instance of the CmdletInvocationException class
        /// </summary>
        /// <param name="innerException">wrapped exception</param>
        /// <param name="invocationInfo">
        /// identity of cmdlet, null is unknown
        /// </param>
        internal CmdletInvocationException(Exception innerException,
                                           InvocationInfo invocationInfo)
            : base(RetrieveMessage(innerException), innerException)
        {
            if (null == innerException)
            {
                throw new ArgumentNullException("innerException");
            }
            // invocationInfo may be null

            IContainsErrorRecord icer = innerException as IContainsErrorRecord;
            if (null != icer && null != icer.ErrorRecord)
            {
                _errorRecord = new ErrorRecord(icer.ErrorRecord, innerException);
            }
            else
            {
                // When no ErrorId is specified by a thrown exception,
                //  we use innerException.GetType().FullName.
                _errorRecord = new ErrorRecord(
                    innerException,
                    innerException.GetType().FullName,
                    ErrorCategory.NotSpecified,
                    null);
            }
            _errorRecord.SetInvocationInfo(invocationInfo);
            // 2005/04/13-JonN Can't do this in an unsealed class: HelpLink = innerException.HelpLink;
            // Exception.Source is set by Throw
            // Source = innerException.Source;
        }

        /// <summary>
        /// Instantiates a new instance of the CmdletInvocationException class
        /// </summary>
        public CmdletInvocationException()
            : base()
        {
        }

        /// <summary>
        /// Instantiates a new instance of the CmdletInvocationException class
        /// </summary>
        /// <param name="message">  </param>
        /// <returns> constructed object </returns>
        public CmdletInvocationException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Instantiates a new instance of the CmdletInvocationException class
        /// </summary>
        /// <param name="message">  </param>
        /// <param name="innerException">  </param>
        /// <returns> constructed object </returns>
        public CmdletInvocationException(string message,
                                         Exception innerException)
            : base(message, innerException)
        {
        }

        #region Serialization
        /// <summary>
        /// Initializes a new instance of the CmdletInvocationException class
        /// using data serialized via
        /// <see cref="ISerializable"/>
        /// </summary>
        /// <param name="info"> serialization information </param>
        /// <param name="context"> streaming context </param>
        /// <returns> constructed object </returns>
        protected CmdletInvocationException(SerializationInfo info,
                                            StreamingContext context)
                    : base(info, context)
        {
            bool hasErrorRecord = info.GetBoolean("HasErrorRecord");
            if (hasErrorRecord)
                _errorRecord = (ErrorRecord)info.GetValue("ErrorRecord", typeof(ErrorRecord));
        }

        /// <summary>
        /// Serializer for <see cref="ISerializable"/>
        /// </summary>
        /// <param name="info"> serialization information </param>
        /// <param name="context"> streaming context </param>
        [SecurityPermissionAttribute(SecurityAction.Demand, SerializationFormatter = true)]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new PSArgumentNullException("info");
            }

            base.GetObjectData(info, context);
            bool hasErrorRecord = (null != _errorRecord);
            info.AddValue("HasErrorRecord", hasErrorRecord);
            if (hasErrorRecord)
                info.AddValue("ErrorRecord", _errorRecord);
        }
        #endregion Serialization
        #endregion ctor

        #region Properties
        /// <summary>
        /// The error reported by the cmdlet
        /// </summary>
        /// <value>never null</value>
        public override ErrorRecord ErrorRecord
        {
            get
            {
                if (null == _errorRecord)
                {
                    _errorRecord = new ErrorRecord(
                        new ParentContainsErrorRecordException(this),
                        "CmdletInvocationException",
                        ErrorCategory.NotSpecified,
                        null);
                }
                return _errorRecord;
            }
        }
        private ErrorRecord _errorRecord = null;

        #endregion Properties
    } // class CmdletInvocationException
    #endregion CmdletInvocationException

    #region CmdletProviderInvocationException
    /// <summary>
    /// Indicates that a cmdlet hit a terminating error of type
    /// <see cref="System.Management.Automation.ProviderInvocationException"/>.
    /// This is generally reported from the standard provider navigation cmdlets
    /// such as get-childitem.
    /// </summary>
    [Serializable]
    public class CmdletProviderInvocationException : CmdletInvocationException
    {
        #region ctor
        /// <summary>
        /// Instantiates a new instance of the CmdletProviderInvocationException class
        /// </summary>
        /// <param name="innerException">wrapped exception</param>
        /// <param name="myInvocation">
        /// identity of cmdlet, null is unknown
        /// </param>
        /// <returns>constructed object</returns>
        internal CmdletProviderInvocationException(
                    ProviderInvocationException innerException,
                    InvocationInfo myInvocation)
            : base(GetInnerException(innerException), myInvocation)
        {
            if (null == innerException)
            {
                throw new ArgumentNullException("innerException");
            }
            _providerInvocationException = innerException;
        }

        /// <summary>
        /// Instantiates a new instance of the CmdletProviderInvocationException class
        /// </summary>
        /// <returns> constructed object </returns>
        public CmdletProviderInvocationException()
            : base()
        {
        }

        /// <summary>
        /// Initializes a new instance of the CmdletProviderInvocationException class
        /// using data serialized via
        /// <see cref="ISerializable"/>
        /// </summary>
        /// <param name="info"> serialization information </param>
        /// <param name="context"> streaming context </param>
        /// <returns> constructed object </returns>
        protected CmdletProviderInvocationException(SerializationInfo info,
                                                    StreamingContext context)
            : base(info, context)
        {
            _providerInvocationException = InnerException as ProviderInvocationException;
        }

        /// <summary>
        /// Instantiates a new instance of the CmdletProviderInvocationException class
        /// </summary>
        /// <param name="message">  </param>
        /// <returns> constructed object </returns>
        public CmdletProviderInvocationException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Instantiates a new instance of the CmdletProviderInvocationException class
        /// </summary>
        /// <param name="message">  </param>
        /// <param name="innerException">  </param>
        /// <returns> constructed object </returns>
        public CmdletProviderInvocationException(string message,
                                                 Exception innerException)
            : base(message, innerException)
        {
            _providerInvocationException = innerException as ProviderInvocationException;
        }
        #endregion ctor

        #region Properties
        /// <summary>
        /// InnerException as ProviderInvocationException
        /// </summary>
        /// <value>ProviderInvocationException</value>
        public ProviderInvocationException ProviderInvocationException
        {
            get
            {
                return _providerInvocationException;
            }
        }
        [NonSerialized]
        private ProviderInvocationException _providerInvocationException;

        /// <summary>
        /// This is the ProviderInfo associated with the provider which
        /// generated the error.
        /// </summary>
        /// <value>may be null</value>
        public ProviderInfo ProviderInfo
        {
            get
            {
                return (null == _providerInvocationException)
                    ? null
                    : _providerInvocationException.ProviderInfo;
            }
        }

        #endregion Properties

        #region Internal
        private static Exception GetInnerException(Exception e)
        {
            return (e == null) ? null : e.InnerException;
        }
        #endregion Internal
    } // CmdletProviderInvocationException
    #endregion CmdletProviderInvocationException

    #region PipelineStoppedException
    /// <summary>
    /// Indicates that the pipeline has already been stopped.
    /// </summary>
    /// <remarks>
    /// When reported as the result of a command, PipelineStoppedException
    /// indicates that the command was stopped asynchronously, either by the
    /// user hitting CTRL-C, or by a call to
    /// <see cref="System.Management.Automation.Runspaces.Pipeline.Stop"/>.
    /// 
    /// When a cmdlet or provider sees this exception thrown from a Monad API such as
    ///     WriteObject(object)
    /// this means that the command was already stopped.  The cmdlet or provider
    /// should clean up and return.
    /// Catching this exception is optional; if the cmdlet or providers chooses not to
    /// handle PipelineStoppedException and instead allow it to propagate to the
    /// Monad Engine's call to ProcessRecord, the Monad Engine will handle it properly.
    /// </remarks>
    [Serializable]
    public class PipelineStoppedException : RuntimeException
    {
        #region ctor
        /// <summary>
        /// Instantiates a new instance of the PipelineStoppedException class
        /// </summary>
        /// <returns> constructed object </returns>
        public PipelineStoppedException()
            : base(GetErrorText.PipelineStoppedException)
        {
            SetErrorId("PipelineStopped");
            SetErrorCategory(ErrorCategory.OperationStopped);
        }

        /// <summary>
        /// Initializes a new instance of the PipelineStoppedException class
        /// using data serialized via
        /// <see cref="ISerializable"/>
        /// </summary>
        /// <param name="info"> serialization information </param>
        /// <param name="context"> streaming context </param>
        /// <returns> constructed object </returns>
        protected PipelineStoppedException(SerializationInfo info,
                                           StreamingContext context)
                    : base(info, context)
        {
            // no properties, nothing more to serialize
            // no need for a GetObjectData implementation
        }

        /// <summary>
        /// Instantiates a new instance of the PipelineStoppedException class
        /// </summary>
        /// <param name="message">  </param>
        /// <returns> constructed object </returns>
        public PipelineStoppedException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Instantiates a new instance of the PipelineStoppedException class
        /// </summary>
        /// <param name="message">  </param>
        /// <param name="innerException">  </param>
        /// <returns> constructed object </returns>
        public PipelineStoppedException(string message,
                                        Exception innerException)
            : base(message, innerException)
        {
        }
        #endregion ctor
    } // PipelineStoppedException
    #endregion PipelineStoppedException

    #region PipelineClosedException
    /// <summary>
    /// PipelineClosedException occurs when someone tries to write
    /// to an asynchronous pipeline source and the pipeline has already
    /// been stopped.
    /// </summary>
    /// <seealso cref="System.Management.Automation.Runspaces.Pipeline.Input"/>
    [Serializable]
    public class PipelineClosedException : RuntimeException
    {
        #region ctor
        /// <summary>
        /// Instantiates a new instance of the PipelineClosedException class
        /// </summary>
        /// <returns> constructed object </returns>
        public PipelineClosedException()
            : base()
        {
        }

        /// <summary>
        /// Instantiates a new instance of the PipelineClosedException class
        /// </summary>
        /// <param name="message">  </param>
        /// <returns> constructed object </returns>
        public PipelineClosedException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Instantiates a new instance of the PipelineClosedException class
        /// </summary>
        /// <param name="message">  </param>
        /// <param name="innerException">  </param>
        /// <returns> constructed object </returns>
        public PipelineClosedException(string message,
                                       Exception innerException)
            : base(message, innerException)
        {
        }
        #endregion ctor

        #region Serialization
        /// <summary>
        /// Initializes a new instance of the PipelineClosedException class
        /// using data serialized via
        /// <see cref="ISerializable"/>
        /// </summary>
        /// <param name="info"> serialization information </param>
        /// <param name="context"> streaming context </param>
        /// <returns> constructed object </returns>
        protected PipelineClosedException(SerializationInfo info,
                                          StreamingContext context)
            : base(info, context)
        {
        }
        #endregion Serialization
    } // PipelineClosedException
    #endregion PipelineClosedException

    #region ActionPreferenceStopException
    /// <summary>
    /// ActionPreferenceStopException indicates that the command stopped due
    /// to the ActionPreference.Stop or Inquire policy.
    /// </summary>
    /// <remarks>
    /// For example, if $WarningPreference is "Stop", the command will fail with
    /// this error if a cmdlet calls WriteWarning.
    /// </remarks>
    [Serializable]
    public class ActionPreferenceStopException : RuntimeException
    {
        #region ctor
        /// <summary>
        /// Instantiates a new instance of the ActionPreferenceStopException class
        /// </summary>
        /// <returns> constructed object </returns>
        public ActionPreferenceStopException()
            : this(GetErrorText.ActionPreferenceStop)
        {
        }

        /// <summary>
        /// Instantiates a new instance of the ActionPreferenceStopException class
        /// </summary>
        /// <param name="error">
        /// Non-terminating error which triggered the Stop
        /// </param>
        /// <returns> constructed object </returns>
        internal ActionPreferenceStopException(ErrorRecord error)
            : this(RetrieveMessage(error))
        {
            if (null == error)
            {
                throw new ArgumentNullException("error");
            }
            _errorRecord = error;
        }

        /// <summary>
        /// Instantiates a new instance of the ActionPreferenceStopException class
        /// </summary>
        /// <param name="invocationInfo">  </param>
        /// <param name="message">  </param>
        /// <returns> constructed object </returns>
        internal ActionPreferenceStopException(InvocationInfo invocationInfo, string message)
            : this(message)
        {
            base.ErrorRecord.SetInvocationInfo(invocationInfo);
        }

        /// <summary>
        /// Instantiates a new instance of the ActionPreferenceStopException class
        /// </summary>
        internal ActionPreferenceStopException(InvocationInfo invocationInfo,
                                               ErrorRecord errorRecord,
                                               string message)
            : this(invocationInfo, message)
        {
            if (errorRecord == null)
            {
                throw new ArgumentNullException("errorRecord");
            }
            _errorRecord = errorRecord;
        }

        #region Serialization
        /// <summary>
        /// Initializes a new instance of the ActionPreferenceStopException class
        /// using data serialized via
        /// <see cref="ISerializable"/>
        /// </summary>
        /// <param name="info"> serialization information </param>
        /// <param name="context"> streaming context </param>
        /// <returns> constructed object </returns>
        protected ActionPreferenceStopException(SerializationInfo info,
                                                StreamingContext context)
                    : base(info, context)
        {
            bool hasErrorRecord = info.GetBoolean("HasErrorRecord");
            if (hasErrorRecord)
                _errorRecord = (ErrorRecord)info.GetValue("ErrorRecord", typeof(ErrorRecord));

            // fix for BUG: Windows Out Of Band Releases: 906263 and 906264
            // The interpreter prompt CommandBaseStrings:InquireHalt
            // should be suppressed when this flag is set.  This will be set
            // when this prompt has already occurred and Break was chosen,
            // or for ActionPreferenceStopException in all cases.
            this.SuppressPromptInInterpreter = true;
        }

        /// <summary>
        /// Serializer for <see cref="ISerializable"/>
        /// </summary>
        /// <param name="info"> serialization information </param>
        /// <param name="context"> streaming context </param>
        [SecurityPermissionAttribute(SecurityAction.Demand, SerializationFormatter = true)]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            if (null != info)
            {
                bool hasErrorRecord = (null != _errorRecord);
                info.AddValue("HasErrorRecord", hasErrorRecord);
                if (hasErrorRecord)
                {
                    info.AddValue("ErrorRecord", _errorRecord);
                }
            }

            // fix for BUG: Windows Out Of Band Releases: 906263 and 906264
            // The interpreter prompt CommandBaseStrings:InquireHalt
            // should be suppressed when this flag is set.  This will be set
            // when this prompt has already occurred and Break was chosen,
            // or for ActionPreferenceStopException in all cases.
            this.SuppressPromptInInterpreter = true;
        }
        #endregion Serialization

        /// <summary>
        /// Instantiates a new instance of the ActionPreferenceStopException class
        /// </summary>
        /// <param name="message">  </param>
        /// <returns> constructed object </returns>
        public ActionPreferenceStopException(string message)
            : base(message)
        {
            SetErrorCategory(ErrorCategory.OperationStopped);
            SetErrorId("ActionPreferenceStop");

            // fix for BUG: Windows Out Of Band Releases: 906263 and 906264
            // The interpreter prompt CommandBaseStrings:InquireHalt
            // should be suppressed when this flag is set.  This will be set
            // when this prompt has already occurred and Break was chosen,
            // or for ActionPreferenceStopException in all cases.
            this.SuppressPromptInInterpreter = true;
        }

        /// <summary>
        /// Instantiates a new instance of the ActionPreferenceStopException class
        /// </summary>
        /// <param name="message">  </param>
        /// <param name="innerException">  </param>
        /// <returns> constructed object </returns>
        public ActionPreferenceStopException(string message,
                                             Exception innerException)
            : base(message, innerException)
        {
            SetErrorCategory(ErrorCategory.OperationStopped);
            SetErrorId("ActionPreferenceStop");

            // fix for BUG: Windows Out Of Band Releases: 906263 and 906264
            // The interpreter prompt CommandBaseStrings:InquireHalt
            // should be suppressed when this flag is set.  This will be set
            // when this prompt has already occurred and Break was chosen,
            // or for ActionPreferenceStopException in all cases.
            this.SuppressPromptInInterpreter = true;
        }
        #endregion ctor

        #region Properties
        /// <summary>
        /// see <see cref="System.Management.Automation.IContainsErrorRecord"/>
        /// </summary>
        /// <value>ErrorRecord</value>
        /// <remarks>
        /// If this error results from a non-terminating error being promoted to
        /// terminating due to -ErrorAction or $ErrorActionPreference, this is
        /// the non-terminating error.
        /// </remarks>
        public override ErrorRecord ErrorRecord
        {
            get { return _errorRecord ?? base.ErrorRecord; }
        }
        private readonly ErrorRecord _errorRecord = null;
        #endregion Properties
    } // ActionPreferenceStopException
    #endregion ActionPreferenceStopException

    #region ParentContainsErrorRecordException
    /// <summary>
    /// ParentContainsErrorRecordException is the exception contained by the ErrorRecord
    /// which is associated with a Monad engine custom exception through
    /// the IContainsErrorRecord interface.
    /// </summary>
    /// <remarks>
    /// We use this exception class
    /// so that there is not a recursive "containment" relationship
    /// between the Monad engine exception and its ErrorRecord.
    /// </remarks>
    [Serializable]
    public class ParentContainsErrorRecordException : SystemException
    {
        #region Constructors
        /// <summary>
        /// Instantiates a new instance of the ParentContainsErrorRecordException class.
        /// Note that this sets the Message and not the InnerException.
        /// </summary>
        /// <returns> constructed object </returns>
        /// <remarks>
        /// I leave this non-standard constructor form public.
        /// </remarks>
#pragma warning disable 56506

        // BUGBUG : We should check whether wrapperException is not null.
        // Please remove the #pragma warning when this is fixed.
        public ParentContainsErrorRecordException(Exception wrapperException)
        {
            _wrapperException = wrapperException;
        }

#pragma warning restore 56506

        /// <summary>
        /// Instantiates a new instance of the ParentContainsErrorRecordException class
        /// </summary>
        /// <param name="message">  </param>
        /// <returns> constructed object </returns>
        public ParentContainsErrorRecordException(string message)
        {
            _message = message;
        }

        /// <summary>
        /// Instantiates a new instance of the ParentContainsErrorRecordException class
        /// </summary>
        /// <returns> constructed object </returns>
        public ParentContainsErrorRecordException()
            : base()
        {
        }

        /// <summary>
        /// Instantiates a new instance of the ParentContainsErrorRecordException class
        /// </summary>
        /// <param name="message">  </param>
        /// <param name="innerException">  </param>
        /// <returns> constructed object </returns>
        public ParentContainsErrorRecordException(string message,
                                                  Exception innerException)
            : base(message, innerException)
        {
            _message = message;
        }
        #endregion Constructors

        #region Serialization
        /// <summary>
        /// Initializes a new instance of the ParentContainsErrorRecordException class
        /// using data serialized via
        /// <see cref="ISerializable"/>
        /// </summary>
        /// <param name="info"> serialization information </param>
        /// <param name="context"> streaming context </param>
        /// <returns> doesn't return </returns>
        /// <exception cref="NotImplementedException">always</exception>
        protected ParentContainsErrorRecordException(
            SerializationInfo info, StreamingContext context)
                    : base(info, context)
        {
            _message = info.GetString("ParentContainsErrorRecordException_Message");
        }
        #endregion Serialization
        /// <summary>
        /// Gets the message for the exception
        /// </summary>
        public override string Message
        {
            get {
                return _message ?? (_message = (null != _wrapperException) ? _wrapperException.Message : String.Empty);
            }
        }

        /// <summary>
        /// Serializer for <see cref="ISerializable"/>
        /// </summary>
        /// <param name="info">serialization information</param>
        /// <param name="context">context</param>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new PSArgumentNullException("info");
            }

            base.GetObjectData(info, context);
            info.AddValue("ParentContainsErrorRecordException_Message", this.Message);
        }

        #region Private Data

        private readonly Exception _wrapperException;
        private string _message;

        #endregion
    }
    #endregion ParentContainsErrorRecordException

    #region RedirectedException
    /// <summary>
    /// Indicates that a success object was written and success-to-error ("1>&amp;2")
    /// has been specified.
    /// </summary>
    /// <remarks>
    /// The redirected object is available as
    /// <see cref="System.Management.Automation.ErrorRecord.TargetObject"/>
    /// in the ErrorRecord which contains this exception.
    /// </remarks>
    [Serializable]
    public class RedirectedException : RuntimeException
    {
        #region constructors
        /// <summary>
        /// Instantiates a new instance of the RedirectedException class
        /// </summary>
        /// <returns> constructed object </returns>
        public RedirectedException()
            : base()
        {
            SetErrorId("RedirectedException");
            SetErrorCategory(ErrorCategory.NotSpecified);
        }

        /// <summary>
        /// Instantiates a new instance of the RedirectedException class
        /// </summary>
        /// <param name="message">  </param>
        /// <returns> constructed object </returns>
        public RedirectedException(string message)
            : base(message)
        {
            SetErrorId("RedirectedException");
            SetErrorCategory(ErrorCategory.NotSpecified);
        }

        /// <summary>
        /// Instantiates a new instance of the RedirectedException class
        /// </summary>
        /// <param name="message">  </param>
        /// <param name="innerException">  </param>
        /// <returns> constructed object </returns>
        public RedirectedException(string message,
                                   Exception innerException)
            : base(message, innerException)
        {
            SetErrorId("RedirectedException");
            SetErrorCategory(ErrorCategory.NotSpecified);
        }

        /// <summary>
        /// Initializes a new instance of the RedirectedException class
        /// using data serialized via
        /// <see cref="ISerializable"/>
        /// </summary>
        /// <param name="info"> serialization information </param>
        /// <param name="context"> streaming context </param>
        /// <returns> constructed object </returns>
        protected RedirectedException(SerializationInfo info,
                                      StreamingContext context)
                    : base(info, context)
        {
        }
        #endregion constructors
    } // class RedirectedException
    #endregion RedirectedException

    #region ScriptCallDepthException
    /// <summary>
    /// ScriptCallDepthException occurs when the number of
    /// session state objects of this type in this scope
    /// exceeds the configured maximum.
    /// </summary>
    /// <remarks>
    /// When one Monad command or script calls another, this creates an additional
    /// scope.  Some script expressions also create a scope.  Monad imposes a maximum
    /// call depth to prevent stack overflows.  The maximum call depth is configurable
    /// but generally high enough that scripts which are not deeply recursive
    /// should not have a problem.
    /// </remarks>
    [Serializable]
    public class ScriptCallDepthException : SystemException, IContainsErrorRecord
    {
        #region ctor

        /// <summary>
        /// Instantiates a new instance of the ScriptCallDepthException class
        /// </summary>
        /// <returns> constructed object </returns>
        public ScriptCallDepthException()
            : base(GetErrorText.ScriptCallDepthException)
        {
        }

        /// <summary>
        /// Instantiates a new instance of the ScriptCallDepthException class
        /// </summary>
        /// <param name="message">  </param>
        /// <returns> constructed object </returns>
        public ScriptCallDepthException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Instantiates a new instance of the ScriptCallDepthException class
        /// </summary>
        /// <param name="message">  </param>
        /// <param name="innerException">  </param>
        /// <returns> constructed object </returns>
        public ScriptCallDepthException(string message,
                                        Exception innerException)
                : base(message, innerException)
        {
        }
        #endregion ctor

        #region Serialization
        /// <summary>
        /// Initializes a new instance of the ScriptCallDepthException class
        /// using data serialized via
        /// <see cref="ISerializable"/>
        /// </summary>
        /// <param name="info"> serialization information </param>
        /// <param name="context"> streaming context </param>
        /// <returns> constructed object </returns>
        protected ScriptCallDepthException(SerializationInfo info,
                                           StreamingContext context)
            : base(info, context)
        {
        }
        /// <summary>
        /// Serializer for <see cref="ISerializable"/>
        /// </summary>
        /// <param name="info">serialization information</param>
        /// <param name="context">context</param>
        [SecurityPermission(SecurityAction.Demand, SerializationFormatter = true)]
        public override
        void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
        }
        #endregion Serialization

        #region properties
        /// <summary>
        /// see <see cref="System.Management.Automation.IContainsErrorRecord"/>
        /// </summary>
        /// <value></value>
        /// <remarks>
        /// TargetObject is the offending call depth
        /// </remarks>
        public ErrorRecord ErrorRecord
        {
            get
            {
                if (null == _errorRecord)
                {
                    _errorRecord = new ErrorRecord(
                        new ParentContainsErrorRecordException(this),
                        "CallDepthOverflow",
                        ErrorCategory.InvalidOperation,
                        CallDepth);
                }
                return _errorRecord;
            }
        }
        private ErrorRecord _errorRecord = null;

        /// <summary>
        /// Always 0 - depth is not tracked as there is no hard coded maximum.
        /// </summary>
        public int CallDepth
        {
            get { return 0; }
        }
        #endregion properties
    } // ScriptCallDepthException
    #endregion ScriptCallDepthException

    #region PipelineDepthException
    /// <summary>
    /// PipelineDepthException occurs when the number of
    /// commands participating in a pipeline (object streaming)
    /// exceeds the configured maximum.
    /// </summary>
    /// <remarks>
    /// </remarks>
    [Serializable]
    public class PipelineDepthException : SystemException, IContainsErrorRecord
    {
        #region ctor
        /// <summary>
        /// Instantiates a new instance of the PipelineDepthException class
        /// </summary>
        /// <returns> constructed object </returns>
        public PipelineDepthException()
            : base(GetErrorText.PipelineDepthException)
        {
        }

        /// <summary>
        /// Instantiates a new instance of the PipelineDepthException class
        /// </summary>
        /// <param name="message">  </param>
        /// <returns> constructed object </returns>
        public PipelineDepthException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Instantiates a new instance of the PipelineDepthException class
        /// </summary>
        /// <param name="message">  </param>
        /// <param name="innerException">  </param>
        /// <returns> constructed object </returns>
        public PipelineDepthException(string message,
                                        Exception innerException)
            : base(message, innerException)
        {
        }
        #endregion ctor

        #region Serialization
        /// <summary>
        /// Initializes a new instance of the PipelineDepthException class
        /// using data serialized via
        /// <see cref="ISerializable"/>
        /// </summary>
        /// <param name="info"> serialization information </param>
        /// <param name="context"> streaming context </param>
        /// <returns> constructed object </returns>
        protected PipelineDepthException(SerializationInfo info,
                                           StreamingContext context)
            : base(info, context)
        {
        }
        /// <summary>
        /// Serializer for <see cref="ISerializable"/>
        /// </summary>
        /// <param name="info">serialization information</param>
        /// <param name="context">context</param>
        [SecurityPermission(SecurityAction.Demand, SerializationFormatter = true)]
        public override
        void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
        }
        #endregion Serialization

        #region properties
        /// <summary>
        /// see <see cref="System.Management.Automation.IContainsErrorRecord"/>
        /// </summary>
        /// <value></value>
        /// <remarks>
        /// TargetObject is the offending call depth
        /// </remarks>
        [SuppressMessage("Microsoft.Design", "CA1065:DoNotRaiseExceptionsInUnexpectedLocations")]
        public ErrorRecord ErrorRecord
        {
            get
            {
                if (null == _errorRecord)
                {
                    _errorRecord = new ErrorRecord(
                        new ParentContainsErrorRecordException(this),
                        "CallDepthOverflow",
                        ErrorCategory.InvalidOperation,
                        CallDepth);
                }
                return _errorRecord;
            }
        }
        private ErrorRecord _errorRecord = null;

        /// <summary>
        /// Always 0 - depth is not tracked as there is no hard coded maximum.
        /// </summary>
        /// <value></value>
        public int CallDepth
        {
            get { return 0; }
        }
        #endregion properties
    } // PipelineDepthException
    #endregion

    #region HaltCommandException
    /// <summary>
    /// A cmdlet/provider should throw HaltCommandException
    /// when it wants to terminate the running command without
    /// this being considered an error.
    /// </summary>
    /// <remarks>
    /// For example, "more" will throw HaltCommandException if the user hits "q".
    /// 
    /// Only throw HaltCommandException from your implementation of ProcessRecord etc.
    /// 
    /// Note that HaltCommandException does not define IContainsErrorRecord.
    /// This is because it is not reported to the user.
    /// </remarks>
    [Serializable]
    public class HaltCommandException : SystemException
    {
        #region ctor
        /// <summary>
        /// Instantiates a new instance of the HaltCommandException class
        /// </summary>
        /// <returns> constructed object </returns>
        public HaltCommandException()
            : base(StringUtil.Format(AutomationExceptions.HaltCommandException))
        {
        }

        /// <summary>
        /// Instantiates a new instance of the HaltCommandException class
        /// </summary>
        /// <param name="message">  </param>
        /// <returns> constructed object </returns>
        public HaltCommandException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Instantiates a new instance of the HaltCommandException class
        /// </summary>
        /// <param name="message">  </param>
        /// <param name="innerException">  </param>
        /// <returns> constructed object </returns>
        public HaltCommandException(string message,
                                    Exception innerException)
            : base(message, innerException)
        {
        }
        #endregion ctor

        #region Serialization
        /// <summary>
        /// Initializes a new instance of the HaltCommandException class
        /// using data serialized via
        /// <see cref="ISerializable"/>
        /// </summary>
        /// <param name="info"> serialization information </param>
        /// <param name="context"> streaming context </param>
        /// <returns> constructed object </returns>
        protected HaltCommandException(SerializationInfo info,
                                       StreamingContext context)
            : base(info, context)
        {
        }
        #endregion Serialization
    } // HaltCommandException
    #endregion HaltCommandException
} // namespace System.Management.Automation

#pragma warning restore 56506
