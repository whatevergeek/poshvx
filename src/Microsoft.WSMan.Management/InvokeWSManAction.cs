/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/
using System;
using System.IO;
using System.Reflection;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Management.Automation;
using System.Management.Automation.Provider;
using System.Xml;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis; 



namespace Microsoft.WSMan.Management
{
    /// <summary>
    /// Executes action on a target object specified by RESOURCE_URI, where 
    /// parameters are specified by key value pairs.
    /// eg., Call StartService method on the spooler service
    /// Invoke-WSManAction -Action StartService -ResourceURI wmicimv2/Win32_Service  
    /// -SelectorSet {Name=Spooler}
    /// </summary>

    [Cmdlet(VerbsLifecycle.Invoke, "WSManAction", DefaultParameterSetName = "URI", HelpUri = "http://go.microsoft.com/fwlink/?LinkId=141446")]
    public class InvokeWSManActionCommand : AuthenticatingWSManCommand, IDisposable
    {
        /// <summary>
        /// The following is the definition of the input parameter "Action".
        /// Indicates the method which needs to be executed on the management object 
        /// specified by the ResourceURI and selectors
        /// </summary>
        [Parameter(Mandatory = true,
                  Position = 1)]
        [ValidateNotNullOrEmpty]
        public String Action
        {
            get { return action; }
            set { action = value; }
        }
        private String action;

        /// <summary>
        /// The following is the definition of the input parameter "ApplicationName".
        /// ApplicationName identifies the remote endpoint.
        /// </summary>
        [Parameter(ParameterSetName = "ComputerName")]
        [ValidateNotNullOrEmpty]
        public String ApplicationName
        {
            get { return applicationname; }
            set { applicationname = value; }
        }
        private String applicationname = null;

        /// <summary>
        /// The following is the definition of the input parameter "ComputerName".
        /// Executes the management operation on the specified computer(s). The default 
        /// is the local computer. Type the fully qualified domain name, NETBIOS name or 
        /// IP address to indicate the remote host(s)
        /// </summary>
        [Parameter(ParameterSetName = "ComputerName")]
        [Alias("cn")]
        public String ComputerName
        {
            get { return computername; }
            set
            {
                computername = value;
                if ((string.IsNullOrEmpty(computername)) || (computername.Equals(".", StringComparison.CurrentCultureIgnoreCase)))
                {
                    computername = "localhost";
                }
               
            }
        }
        private String computername = null;

        /// <summary>
        /// The following is the definition of the input parameter "ConnectionURI".
        /// Specifies the transport, server, port, and ApplicationName of the new 
        /// runspace. The format of this string is: 
        /// transport://server:port/ApplicationName.
        /// </summary>
        [Parameter(ParameterSetName = "URI")]
        [ValidateNotNullOrEmpty]
        [Alias("CURI", "CU")]
        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", MessageId = "URI")]
        public Uri ConnectionURI
        {
            get { return connectionuri; }
            set { connectionuri = value; }
        }
        private Uri connectionuri;

        /// <summary>
        /// The following is the definition of the input parameter "FilePath".
        /// Updates the management resource specified by the ResourceURI and SelectorSet 
        /// via this input file
        /// </summary>
        [Parameter]
        [ValidateNotNullOrEmpty]
        public String FilePath
        {
            get { return filepath; }
            set { filepath = value; }
        }
        private String filepath;

        /// <summary>
        /// The following is the definition of the input parameter "OptionSet".
        /// OptionSet is a hashtable  and is used to pass a set of switches to the 
        /// service to modify or refine the nature of the request.
        /// </summary>
        [Parameter(ValueFromPipeline = true,
                   ValueFromPipelineByPropertyName = true)]
        [ValidateNotNullOrEmpty]
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        [Alias("os")]
        public Hashtable OptionSet
        {
            get { return optionset; }
            set { optionset = value; }
        }
        private Hashtable optionset;

        /// <summary>
        /// The following is the definition of the input parameter "Port".
        /// Specifies the port to be used when connecting to the ws management service. 
        /// </summary>
        [Parameter(ParameterSetName = "ComputerName")]
        [ValidateNotNullOrEmpty]
        [ValidateRange(1, Int32.MaxValue)]
        public Int32 Port
        {
            get { return port; }
            set { port = value; }
        }
        private Int32 port = 0;

        /// <summary>
        /// The following is the definition of the input parameter "SelectorSet".
        /// SelectorSet is a hash table which helps in identify an instance of the 
        /// management resource if there are are more than 1 instance of the resource 
        /// class
        /// </summary>
        [Parameter(Position = 2,
                   ValueFromPipeline = true,
                   ValueFromPipelineByPropertyName = true)]
        [ValidateNotNullOrEmpty]
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public Hashtable SelectorSet
        {
            get { return selectorset; }
            set { selectorset = value; }
        }
        private Hashtable selectorset;

        /// <summary>
        /// The following is the definition of the input parameter "SessionOption".
        /// Defines a set of extended options for the WSMan session. This hashtable can 
        /// be created using New-WSManSessionOption
        /// </summary>
        [Parameter]
        [ValidateNotNullOrEmpty]
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        [Alias("so")]
        public SessionOption SessionOption
        {
            get { return sessionoption; }
            set { sessionoption = value; }
        }
        private SessionOption sessionoption;

        /// <summary>
        /// The following is the definition of the input parameter "UseSSL".
        /// Uses the Secure Sockets Layer (SSL) protocol to establish a connection to 
        /// the remote computer. If SSL is not available on the port specified by the 
        /// Port parameter, the command fails.
        /// </summary>
        [Parameter(ParameterSetName = "ComputerName")]
        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", MessageId = "SSL")]
        public SwitchParameter UseSSL
        {
            get { return usessl; }
            set { usessl = value; }
        }
        private SwitchParameter usessl;

        /// <summary>
        /// The following is the definition of the input parameter "ValueSet".
        /// ValueSet is a hahs table which helps to modify resource represented by the 
        /// ResourceURI and SelectorSet.
        /// </summary>
        [Parameter]
        [ValidateNotNullOrEmpty]
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public Hashtable ValueSet
        {
            get { return valueset; }
            set { valueset = value; }
        }
        private Hashtable valueset;


        /// <summary>
        /// The following is the definition of the input parameter "ResourceURI".
        /// URI of the resource class/instance representation
        /// </summary>
        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true,
                   ValueFromPipelineByPropertyName = true)]
        [ValidateNotNullOrEmpty]
        [Alias("ruri")]
        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", MessageId = "URI")]
        public Uri ResourceURI
        {
            get { return resourceuri; }
            set { resourceuri = value; }
        }
        private Uri resourceuri;


        private WSManHelper helper;
        IWSManEx m_wsmanObject = (IWSManEx)new WSManClass();
        IWSManSession m_session = null;
        string connectionStr = string.Empty;
        

        /// <summary>
        /// BeginProcessing method.
        /// </summary>
        protected override void BeginProcessing()
        {
            helper = new WSManHelper(this);

            helper.WSManOp = "invoke";

            //create the connection string
            connectionStr = helper.CreateConnectionString(connectionuri, port, computername, applicationname);


        }

        /// <summary>
        /// ProcessRecord method.
        /// </summary>
        protected override void ProcessRecord()
        {


            try
            {
                //create the resourcelocator object
                IWSManResourceLocator m_resource = helper.InitializeResourceLocator(optionset, selectorset, null, null, m_wsmanObject, resourceuri);

                //create the session object
                m_session = helper.CreateSessionObject(m_wsmanObject, Authentication, sessionoption, Credential, connectionStr, CertificateThumbprint, usessl.IsPresent);

                string rootNode = helper.GetRootNodeName(helper.WSManOp, m_resource.ResourceUri, action);
                string input = helper.ProcessInput(m_wsmanObject, filepath, helper.WSManOp, rootNode, valueset, m_resource, m_session);
                string resultXml = m_session.Invoke(action, m_resource, input, 0);

                XmlDocument xmldoc = new XmlDocument();
                xmldoc.LoadXml(resultXml);
                WriteObject(xmldoc.DocumentElement);

            }
            finally
            {
                if (!String.IsNullOrEmpty(m_wsmanObject.Error))
                {
                    helper.AssertError(m_wsmanObject.Error, true, resourceuri);
                }
                if (!String.IsNullOrEmpty(m_session.Error))
                {
                    helper.AssertError(m_session.Error, true, resourceuri);
                }
                if (m_session != null)
                    Dispose(m_session);

            }

        }//End ProcessRecord()

        #region IDisposable Members

        /// <summary>
        /// public dispose method
        /// </summary>
        public
        void
        Dispose()
        {
            //CleanUp();
            GC.SuppressFinalize(this);
        }
        /// <summary>
        /// public dispose method
        /// </summary>
        public
        void
        Dispose(IWSManSession sessionObject)
        {
            sessionObject = null;
            this.Dispose();
        }

        #endregion IDisposable Members

        /// <summary>
        /// BeginProcessing method.
        /// </summary>
        protected override void EndProcessing()
        {
            //  WSManHelper helper = new WSManHelper();
            helper.CleanUp();
        }






    }//End Class
}
