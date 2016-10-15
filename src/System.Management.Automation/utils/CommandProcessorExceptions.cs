/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System.Runtime.Serialization;

#if CORECLR
// Use stub for SerializableAttribute and ISerializable related types.
using Microsoft.PowerShell.CoreClr.Stubs;
#endif

namespace System.Management.Automation
{
    /// <summary>
    /// Defines the exception that is thrown if a native command fails.
    /// </summary>
    [Serializable]
    public class ApplicationFailedException : RuntimeException
    {
        #region private
        private const string errorIdString = "NativeCommandFailed";
        #endregion

        #region ctor

        #region Serialization
        /// <summary>
        /// Initializes a new instance of the ApplicationFailedException class and defines the serialization information,
        /// and streaming context.
        /// </summary>
        /// <param name="info">The serialization information to use when initializing this object</param>
        /// <param name="context">The streaming context to use when initializing this object</param>
        /// <returns> constructed object </returns>
        protected ApplicationFailedException(SerializationInfo info,
                           StreamingContext context)
                : base(info, context)
        {
        }
        #endregion Serialization

        /// <summary>
        /// Initializes a new instance of the class ApplicationFailedException.
        /// </summary>
        /// <returns> constructed object </returns>
        public ApplicationFailedException() : base()
        {
            base.SetErrorId(errorIdString);
            base.SetErrorCategory(ErrorCategory.ResourceUnavailable);
        }

        /// <summary>
        /// Initializes a new instance of the ApplicationFailedException class and defines the error message.
        /// </summary>
        /// <param name="message">The error message to use when initializing this object</param>
        /// <returns> constructed object </returns>
        public ApplicationFailedException(string message) : base(message)
        {
            base.SetErrorId(errorIdString);
            base.SetErrorCategory(ErrorCategory.ResourceUnavailable);
        }

        /// <summary>
        /// Initializes a new instance of the ApplicationFailedException class and defines the error message and
        /// errorID.
        /// </summary>
        /// <param name="message">The error message to use when initializing this object</param>
        /// <param name="errorId">The errorId to use when initializing this object</param>
        /// <returns> constructed object </returns>
        internal ApplicationFailedException(string message, string errorId) : base(message)
        {
            base.SetErrorId(errorId);
            base.SetErrorCategory(ErrorCategory.ResourceUnavailable);
        }

        /// <summary>
        /// Initializes a new instance of the ApplicationFailedException class and defines the error message,
        /// error ID and inner exception.
        /// </summary>
        /// <param name="message">The error message to use when initializing this object</param>
        /// <param name="errorId">The errorId to use when initializing this object</param>
        /// <param name="innerException">The inner exception to use when initializing this object</param>
        /// <returns> constructed object </returns>
        internal ApplicationFailedException(string message, string errorId, Exception innerException)
            : base(message, innerException)
        {
            base.SetErrorId(errorId);
            base.SetErrorCategory(ErrorCategory.ResourceUnavailable);
        }

        /// <summary>
        /// Initializes a new instance of the ApplicationFailedException class and defines the error message and
        /// inner exception.
        /// </summary>
        /// <param name="message">The error message to use when initializing this object</param>
        /// <param name="innerException">The inner exception to use when initializing this object</param>
        /// <returns> constructed object </returns>
        public ApplicationFailedException(string message,
                        Exception innerException)
                : base(message, innerException)
        {
            base.SetErrorId(errorIdString);
            base.SetErrorCategory(ErrorCategory.ResourceUnavailable);
        }
        #endregion ctor
    } // ApplicationFailedException
} // namespace System.Management.Automation
