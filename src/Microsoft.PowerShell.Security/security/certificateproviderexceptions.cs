/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/


using System;
using Dbg = System.Management.Automation;
using System.Runtime.Serialization;
using System.Reflection;

#if CORECLR
// Use stub for SerializableAttribute, SystemException, ThreadAbortException and ISerializable related types.
using Microsoft.PowerShell.CoreClr.Stubs;
#endif

namespace Microsoft.PowerShell.Commands
{
    /// <summary>
    /// Defines the base class for exceptions thrown by the 
    /// certificate provider when the specified item cannot be located.
    /// </summary>
    [Serializable]
    public class CertificateProviderItemNotFoundException : SystemException
    {
        /// <summary>
        /// Initializes a new instance of the CertificateProviderItemNotFoundException
        /// class with the default message.
        /// </summary>
        public CertificateProviderItemNotFoundException() : base()
        {
        }

        /// <summary>
        /// Initializes a new instance of the CertificateProviderItemNotFoundException
        /// class with the specified message.
        /// </summary>
        /// <param name="message">
        /// The message to be included in the exception.
        /// </param>
        public CertificateProviderItemNotFoundException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the CertificateProviderItemNotFoundException
        /// class with the specified message, and inner exception.
        /// </summary>
        /// <param name="message">
        /// The message to be included in the exception.
        /// </param>
        /// <param name="innerException">
        /// The inner exception to be included in the exception.
        /// </param>
        public CertificateProviderItemNotFoundException(string message,
                                            Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the CertificateProviderItemNotFoundException
        /// class with the specified serialization information, and context.
        /// </summary>
        /// <param name="info">
        /// The serialization information.
        /// </param>
        /// 
        /// <param name="context">
        /// The streaming context.
        /// </param>
        protected CertificateProviderItemNotFoundException(SerializationInfo info,
                                            StreamingContext context)
            : base(info, context)
        {
        }

        /// <summary>
        /// Initializes a new instance of the CertificateProviderItemNotFoundException
        /// class with the specified inner exception.
        /// </summary>
        /// <param name="innerException">
        /// The inner exception to be included in the exception.
        /// </param>
        internal CertificateProviderItemNotFoundException(Exception innerException)
            : base(innerException.Message, innerException)
        {
        }
    }

    /// <summary>
    /// Defines the exception thrown by the certificate provider
    /// when the specified X509 certificate cannot be located.
    /// </summary>
    [Serializable]
    public class CertificateNotFoundException
              : CertificateProviderItemNotFoundException
    {
        /// <summary>
        /// Initializes a new instance of the CertificateNotFoundException
        /// class with the default message.
        /// </summary>
        public CertificateNotFoundException()
            : base()
        {
        }

        /// <summary>
        /// Initializes a new instance of the CertificateNotFoundException
        /// class with the specified message.
        /// </summary>
        /// <param name="message">
        /// The message to be included in the exception.
        /// </param>
        public CertificateNotFoundException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the CertificateNotFoundException
        /// class with the specified message, and inner exception.
        /// </summary>
        /// <param name="message">
        /// The message to be included in the exception.
        /// </param>
        /// <param name="innerException">
        /// The inner exception to be included in the exception.
        /// </param>
        public CertificateNotFoundException(string message,
                                            Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the CertificateNotFoundException
        /// class with the specified serialization information, and context.
        /// </summary>
        /// <param name="info">
        /// The serialization information.
        /// </param>
        /// 
        /// <param name="context">
        /// The streaming context.
        /// </param>
        protected CertificateNotFoundException(SerializationInfo info,
                                            StreamingContext context)
            : base(info, context)
        {
        }

        /// <summary>
        /// Initializes a new instance of the CertificateNotFoundException
        /// class with the specified inner exception.
        /// </summary>
        /// <param name="innerException">
        /// The inner exception to be included in the exception.
        /// </param>
        internal CertificateNotFoundException(Exception innerException)
            : base(innerException.Message, innerException)
        {
        }
    }

    /// <summary>
    /// Defines the exception thrown by the certificate provider
    /// when the specified X509 store cannot be located.
    /// </summary>
    [Serializable]
    public class CertificateStoreNotFoundException
              : CertificateProviderItemNotFoundException
    {
        /// <summary>
        /// Initializes a new instance of the CertificateStoreNotFoundException
        /// class with the default message.
        /// </summary>
        public CertificateStoreNotFoundException()
            : base()
        {
        }

        /// <summary>
        /// Initializes a new instance of the CertificateStoreNotFoundException
        /// class with the specified message.
        /// </summary>
        /// <param name="message">
        /// The message to be included in the exception.
        /// </param>
        public CertificateStoreNotFoundException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the CertificateStoreNotFoundException
        /// class with the specified message, and inner exception.
        /// </summary>
        /// <param name="message">
        /// The message to be included in the exception.
        /// </param>
        /// <param name="innerException">
        /// The inner exception to be included in the exception.
        /// </param>
        public CertificateStoreNotFoundException(string message,
                                            Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the CertificateStoreNotFoundException
        /// class with the specified serialization information, and context.
        /// </summary>
        /// <param name="info">
        /// The serialization information.
        /// </param>
        /// 
        /// <param name="context">
        /// The streaming context.
        /// </param>
        protected CertificateStoreNotFoundException(SerializationInfo info,
                                            StreamingContext context)
            : base(info, context)
        {
        }

        /// <summary>
        /// Initializes a new instance of the CertificateStoreNotFoundException
        /// class with the specified inner exception.
        /// </summary>
        /// <param name="innerException">
        /// The inner exception to be included in the exception.
        /// </param>
        internal CertificateStoreNotFoundException(Exception innerException)
            : base(innerException.Message, innerException)
        {
        }
    }

    /// <summary>
    /// Defines the exception thrown by the certificate provider
    /// when the specified X509 store location cannot be located.
    /// </summary>
    [Serializable]
    public class CertificateStoreLocationNotFoundException
              : CertificateProviderItemNotFoundException
    {
        /// <summary>
        /// Initializes a new instance of the CertificateStoreLocationNotFoundException
        /// class with the default message.
        /// </summary>
        public CertificateStoreLocationNotFoundException()
            : base()
        {
        }

        /// <summary>
        /// Initializes a new instance of the CertificateStoreLocationNotFoundException
        /// class with the specified message.
        /// </summary>
        /// <param name="message">
        /// The message to be included in the exception.
        /// </param>
        public CertificateStoreLocationNotFoundException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the CertificateStoreLocationNotFoundException
        /// class with the specified message, and inner exception.
        /// </summary>
        /// <param name="message">
        /// The message to be included in the exception.
        /// </param>
        /// <param name="innerException">
        /// The inner exception to be included in the exception.
        /// </param>
        public CertificateStoreLocationNotFoundException(string message,
                                            Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the CertificateStoreLocationNotFoundException
        /// class with the specified serialization information, and context.
        /// </summary>
        /// <param name="info">
        /// The serialization information.
        /// </param>
        /// 
        /// <param name="context">
        /// The streaming context.
        /// </param>
        protected CertificateStoreLocationNotFoundException(SerializationInfo info,
                                            StreamingContext context)
            : base(info, context)
        {
        }

        /// <summary>
        /// Initializes a new instance of the CertificateStoreLocationNotFoundException
        /// class with the specified inner exception.
        /// </summary>
        /// <param name="innerException">
        /// The inner exception to be included in the exception.
        /// </param>
        internal CertificateStoreLocationNotFoundException(Exception innerException)
            : base(innerException.Message, innerException)
        {
        }
    }
}

