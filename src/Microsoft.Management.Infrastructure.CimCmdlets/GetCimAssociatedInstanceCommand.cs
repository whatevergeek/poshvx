/*============================================================================
 * Copyright (C) Microsoft Corporation, All rights reserved. 
 *============================================================================
 */

#region Using directives

using System;
using System.Diagnostics.CodeAnalysis;
using System.Management.Automation;
using System.Collections.Generic;

#endregion


namespace Microsoft.Management.Infrastructure.CimCmdlets
{
    /// <summary>
    /// <para>
    /// The Cmdlet retrieves instances connected to the given instance, which
    /// is called the source instance, via a given association. In an
    /// association each instance has a named role, and the same instance can
    /// participate in an association in different roles. Hence, the Cmdlet
    /// takes SourceRole and AssociatorRole parameters in addition to the
    /// Association parameter.
    /// </para>
    /// </summary>
    [Cmdlet(VerbsCommon.Get,
        GetCimAssociatedInstanceCommand.Noun,
        DefaultParameterSetName = CimBaseCommand.ComputerSetName,
        HelpUri = "http://go.microsoft.com/fwlink/?LinkId=227958")]
    [OutputType(typeof(CimInstance))]
    public class GetCimAssociatedInstanceCommand : CimBaseCommand
    {
        #region constructor
        
        /// <summary>
        /// constructor
        /// </summary>
        public GetCimAssociatedInstanceCommand()
            : base(parameters, parameterSets)
        {
            DebugHelper.WriteLogEx();
        }
        
        #endregion

        #region parameters

        /// <summary>
        /// The following is the definition of the input parameter "Association".
        /// Specifies the class name of the association to be traversed from the
        /// SourceRole to AssociatorRole.
        /// </summary>
        [Parameter(
            Position = 1,
            ValueFromPipelineByPropertyName = true)]
        public String Association
        {
            get { return association; }
            set { association = value; }
        }
        private String association;

        /// <summary>
        /// The following is the definition of the input parameter "ResultClassName".
        /// Specifies the class name of the result class name, which associated with
        /// the given instance.
        /// </summary>
        [Parameter]
        public String ResultClassName
        {
            get { return resultClassName; }
            set { resultClassName = value; }
        }
        private String resultClassName;

        /// <summary>
        /// The following is the definition of the input parameter "AssociatorRole".
        /// Specifies the name of the association role of the instances to be retrieved.
        /// </summary>
        //[Parameter(ValueFromPipelineByPropertyName = true)]
        //public String AssociatorRole
        //{
        //    get { return associatorRole; }
        //    set { associatorRole = value; }
        //}
        //private String associatorRole;

        /// <summary>
        /// The following is the definition of the input parameter "SourceRole".
        /// Specifies the name of the association role of the source instance where the
        /// association traversal should begin.
        /// </summary>
        //[Parameter(ValueFromPipelineByPropertyName = true)]
        //public String SourceRole
        //{
        //    get { return sourcerole; }
        //    set { sourcerole = value; }
        //}
        //private String sourcerole;

        /// <summary>
        /// <para>
        /// The following is the definition of the input parameter "InputObject".
        /// Provides the instance from which the association traversal is to begin.
        /// </para>
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 0,
            ValueFromPipeline = true)]
        [Alias(CimBaseCommand.AliasCimInstance)]
        public CimInstance InputObject
        {
            get { return cimInstance; }
            set
            {
                cimInstance = value;
                base.SetParameter(value, nameCimInstance);
            }
        }

        /// <summary>
        /// Property for internal usage purpose
        /// </summary>
        internal CimInstance CimInstance
        {
            get { return cimInstance; }
        }
        private CimInstance cimInstance;

        /// <summary>
        /// The following is the definition of the input parameter "Namespace".
        /// Identifies the Namespace in which the source class, indicated by ClassName,
        /// is registered.
        /// </summary>
        [Parameter(ValueFromPipelineByPropertyName = true)]
        public String Namespace
        {
            get { return nameSpace; }
            set { nameSpace = value; }
        }
        private String nameSpace;

        /// <summary>
        /// The following is the definition of the input parameter "OperationTimeoutSec".
        /// Specifies the operation timeout after which the client operation should be
        /// canceled. The default is the CimSession operation timeout. If this parameter
        /// is specified, then this value takes precedence over the CimSession
        /// OperationTimeout.
        /// </summary>
        [Alias(AliasOT)]
        [Parameter(ValueFromPipelineByPropertyName = true)]
        public UInt32 OperationTimeoutSec
        {
            get { return operationTimeout; }
            set { operationTimeout = value; }
        }
        private UInt32 operationTimeout;

        /// <summary>
        /// <para>
        /// The following is the definition of the input parameter "ResourceUri".
        /// Define the Resource Uri for which the instances are retrieved.
        /// </para>
        /// </summary>
        [Parameter]
        public Uri ResourceUri
        {
            get { return resourceUri; }
            set
            {
                this.resourceUri = value;
                base.SetParameter(value, nameResourceUri);
            }
        }
        private Uri resourceUri;        

        /// <summary>
        /// <para>
        /// The following is the definition of the input parameter "ComputerName".
        /// Specifies the name of the computer where the source instance is stored and
        /// where the association traversal should begin.
        /// </para>
        /// <para>
        /// This is an optional parameter and if it is not provided, the default value
        /// will be "localhost".
        /// </para>
        /// </summary>
        [Alias(AliasCN, AliasServerName)]
        [Parameter(
            ParameterSetName = ComputerSetName)]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public String[] ComputerName
        {
            get { return computerName; }
            set
            {
                computerName = value;
                base.SetParameter(value, nameComputerName);
            }
        }
        private String[] computerName;

        /// <summary>
        /// The following is the definition of the input parameter "CimSession".
        /// Identifies the CimSession which is to be used to retrieve the instances.
        /// </summary>
        [Parameter(
            Mandatory = true,
            ValueFromPipeline = true,
            ParameterSetName = SessionSetName)]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public Microsoft.Management.Infrastructure.CimSession[] CimSession
        {
            get { return cimSession; }
            set
            {
                cimSession = value;
                base.SetParameter(value, nameCimSession);
            }
        }
        private Microsoft.Management.Infrastructure.CimSession[] cimSession;

        /// <summary>
        /// <para>
        /// The following is the definition of the input parameter "KeyOnly".
        /// Indicates that only key properties of the retrieved instances should be
        /// returned to the client.
        /// </para>
        /// </summary>
        [Parameter]
        public SwitchParameter KeyOnly
        {
            get { return keyOnly; }
            set { keyOnly = value; }
        }
        private SwitchParameter keyOnly;

        #endregion

        #region cmdlet methods

        /// <summary>
        /// BeginProcessing method.
        /// </summary>
        protected override void BeginProcessing()
        {
            this.CmdletOperation = new CmdletOperationBase(this);
            this.AtBeginProcess = false;
        }//End BeginProcessing()

        /// <summary>
        /// ProcessRecord method.
        /// </summary>
        protected override void ProcessRecord()
        {
            base.CheckParameterSet();
            CimGetAssociatedInstance operation = this.GetOperationAgent();
            if (operation == null)
            {
                operation = this.CreateOperationAgent();
            }
            operation.GetCimAssociatedInstance(this);
            operation.ProcessActions(this.CmdletOperation);
        }//End ProcessRecord()

        /// <summary>
        /// EndProcessing method.
        /// </summary>
        protected override void EndProcessing()
        {
            CimGetAssociatedInstance operation = this.GetOperationAgent();
            if (operation != null)
                operation.ProcessRemainActions(this.CmdletOperation);
        }//End EndProcessing()

        #endregion

        #region helper methods

        /// <summary>
        /// <para>
        /// Get <see cref="CimGetAssociatedInstance"/> object, which is
        /// used to delegate all Get-CimAssociatedInstance operations.
        /// </para>
        /// </summary>
        CimGetAssociatedInstance GetOperationAgent()
        {
            return this.AsyncOperation as CimGetAssociatedInstance;
        }

        /// <summary>
        /// <para>
        /// Create <see cref="CimGetAssociatedInstance"/> object, which is
        /// used to delegate all Get-CimAssociatedInstance operations.
        /// </para>
        /// </summary>
        /// <returns></returns>
        CimGetAssociatedInstance CreateOperationAgent()
        {
            this.AsyncOperation = new CimGetAssociatedInstance();
            return GetOperationAgent();
        }

        #endregion

        #region internal const strings

        /// <summary>
        /// Noun of current cmdlet
        /// </summary>
        internal const string Noun = @"CimAssociatedInstance";        

        #endregion

        #region private members

        #region const string of parameter names
        // internal const string nameAssociation = "Association";
        internal const string nameCimInstance = "InputObject";
        // internal const string nameNamespace = "Namespace";
        // internal const string nameOperationTimeoutSec = "OperationTimeoutSec";
        internal const string nameComputerName = "ComputerName";
        internal const string nameCimSession = "CimSession";
        internal const string nameResourceUri = "ResourceUri";        
        // internal const string nameKeyOnly = "KeyOnly";
        #endregion

        /// <summary>
        /// static parameter definition entries
        /// </summary>
        static Dictionary<string, HashSet<ParameterDefinitionEntry>> parameters = new Dictionary<string, HashSet<ParameterDefinitionEntry>>
        {
            {
                nameComputerName, new HashSet<ParameterDefinitionEntry> {
                                    new ParameterDefinitionEntry(CimBaseCommand.ComputerSetName, false),
                                 }
            },
            {
                nameCimSession, new HashSet<ParameterDefinitionEntry> {
                                    new ParameterDefinitionEntry(CimBaseCommand.SessionSetName, true),                                  
                                 }
            },
            {
                nameCimInstance, new HashSet<ParameterDefinitionEntry> {
                                    new ParameterDefinitionEntry(CimBaseCommand.ComputerSetName, true),
                                    new ParameterDefinitionEntry(CimBaseCommand.SessionSetName, true),                                  
                                 }
            },
            {
                nameResourceUri, new HashSet<ParameterDefinitionEntry> {
                                    new ParameterDefinitionEntry(CimBaseCommand.ComputerSetName, false),
                                    new ParameterDefinitionEntry(CimBaseCommand.SessionSetName, false),                                  
                                 }
            },
        };

        /// <summary>
        /// static parameter set entries
        /// </summary>
        static Dictionary<string, ParameterSetEntry> parameterSets = new Dictionary<string, ParameterSetEntry>
        {
            {   CimBaseCommand.SessionSetName, new ParameterSetEntry(2, false)     },
            {   CimBaseCommand.ComputerSetName, new ParameterSetEntry(1, true)     },
        };
        #endregion
    }//End Class
}//End namespace
