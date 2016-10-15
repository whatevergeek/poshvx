//
//    Copyright (C) Microsoft.  All rights reserved.
//
using Microsoft.PowerShell.Activities;
using System.Management.Automation;
using System.Activities;
using System.Collections.Generic;
using System.ComponentModel;


namespace Microsoft.WSMan.Management.Activities
{
    /// <summary>
    /// Activity to invoke the Microsoft.WSMan.Management\Get-WSManInstance command in a Workflow.
    /// </summary>
    [System.CodeDom.Compiler.GeneratedCode("Microsoft.PowerShell.Activities.ActivityGenerator.GenerateFromName", "3.0")]
    public sealed class GetWSManInstance : PSRemotingActivity
    {
        /// <summary>
        /// Gets the display name of the command invoked by this activity.
        /// </summary>
        public GetWSManInstance()
        {
            this.DisplayName = "Get-WSManInstance";
        }

        /// <summary>
        /// Gets the fully qualified name of the command invoked by this activity.
        /// </summary>
        public override string PSCommandName { get { return "Microsoft.WSMan.Management\\Get-WSManInstance"; } }
        
        // Arguments
        
        /// <summary>
        /// Provides access to the ApplicationName parameter.
        /// </summary>
        [ParameterSpecificCategory]
        [DefaultValue(null)]
        public InArgument<System.String> ApplicationName { get; set; }

        /// <summary>
        /// Provides access to the BasePropertiesOnly parameter.
        /// </summary>
        [ParameterSpecificCategory]
        [DefaultValue(null)]
        public InArgument<System.Management.Automation.SwitchParameter> BasePropertiesOnly { get; set; }

        /// <summary>
        /// Provides access to the ComputerName parameter.
        /// </summary>        
        [ParameterSpecificCategory]
        [DefaultValue(null)]
        public InArgument<System.String> ComputerName { get; set; }

        /// <summary>
        /// Provides access to the ConnectionURI parameter.
        /// </summary>
        [ParameterSpecificCategory]
        [DefaultValue(null)]
        public InArgument<System.Uri> ConnectionURI { get; set; }

        /// <summary>
        /// Provides access to the Dialect parameter.
        /// </summary>
        [ParameterSpecificCategory]
        [DefaultValue(null)]
        public InArgument<System.Uri> Dialect { get; set; }

        /// <summary>
        /// Provides access to the Enumerate parameter.
        /// </summary>
        [ParameterSpecificCategory]
        [DefaultValue(null)]
        public InArgument<System.Management.Automation.SwitchParameter> Enumerate { get; set; }

        /// <summary>
        /// Provides access to the Filter parameter.
        /// </summary>
        [ParameterSpecificCategory]
        [DefaultValue(null)]
        public InArgument<System.String> Filter { get; set; }

        /// <summary>
        /// Provides access to the Fragment parameter.
        /// </summary>
        [ParameterSpecificCategory]
        [DefaultValue(null)]
        public InArgument<System.String> Fragment { get; set; }

        /// <summary>
        /// Provides access to the OptionSet parameter.
        /// </summary>
        [ParameterSpecificCategory]
        [DefaultValue(null)]
        public InArgument<System.Collections.Hashtable> OptionSet { get; set; }

        /// <summary>
        /// Provides access to the Port parameter.
        /// </summary>
        [ParameterSpecificCategory]
        [DefaultValue(null)]
        public InArgument<System.Int32> Port { get; set; }

        /// <summary>
        /// Provides access to the Associations parameter.
        /// </summary>
        [ParameterSpecificCategory]
        [DefaultValue(null)]
        public InArgument<System.Management.Automation.SwitchParameter> Associations { get; set; }

        /// <summary>
        /// Provides access to the ResourceURI parameter.
        /// </summary>
        [ParameterSpecificCategory]
        [DefaultValue(null)]
        public InArgument<System.Uri> ResourceURI { get; set; }

        /// <summary>
        /// Provides access to the ReturnType parameter.
        /// </summary>
        [ParameterSpecificCategory]
        [DefaultValue(null)]
        public InArgument<System.String> ReturnType { get; set; }

        /// <summary>
        /// Provides access to the SelectorSet parameter.
        /// </summary>
        [ParameterSpecificCategory]
        [DefaultValue(null)]
        public InArgument<System.Collections.Hashtable> SelectorSet { get; set; }

        /// <summary>
        /// Provides access to the SessionOption parameter.
        /// </summary>
        [ParameterSpecificCategory]
        [DefaultValue(null)]
        public InArgument<Microsoft.WSMan.Management.SessionOption> SessionOption { get; set; }

        /// <summary>
        /// Provides access to the Shallow parameter.
        /// </summary>
        [ParameterSpecificCategory]
        [DefaultValue(null)]
        public InArgument<System.Management.Automation.SwitchParameter> Shallow { get; set; }

        /// <summary>
        /// Provides access to the UseSSL parameter.
        /// </summary>
        [ParameterSpecificCategory]
        [DefaultValue(null)]
        public InArgument<System.Management.Automation.SwitchParameter> UseSSL { get; set; }

        /// <summary>
        /// Provides access to the Credential parameter.
        /// </summary>
        [ParameterSpecificCategory]
        [DefaultValue(null)]
        public InArgument<System.Management.Automation.PSCredential> Credential { get; set; }

        /// <summary>
        /// Provides access to the Authentication parameter.
        /// </summary>
        [ParameterSpecificCategory]
        [DefaultValue(null)]
        public InArgument<Microsoft.WSMan.Management.AuthenticationMechanism> Authentication { get; set; }

        /// <summary>
        /// Provides access to the CertificateThumbprint parameter.
        /// </summary>
        [ParameterSpecificCategory]
        [DefaultValue(null)]
        public InArgument<System.String> CertificateThumbprint { get; set; }

        /// <summary>
        /// Declares that this activity supports its own remoting.
        /// </summary>        
        protected override bool SupportsCustomRemoting { get { return true; } }


        // Module defining this command
        

        // Optional custom code for this activity
        

        /// <summary>
        /// Returns a configured instance of System.Management.Automation.PowerShell, pre-populated with the command to run.
        /// </summary>
        /// <param name="context">The NativeActivityContext for the currently running activity.</param>
        /// <returns>A populated instance of System.Management.Automation.PowerShell</returns>
        /// <remarks>The infrastructure takes responsibility for closing and disposing the PowerShell instance returned.</remarks>
        protected override ActivityImplementationContext GetPowerShell(NativeActivityContext context)
        {
            System.Management.Automation.PowerShell invoker = global::System.Management.Automation.PowerShell.Create();
            System.Management.Automation.PowerShell targetCommand = invoker.AddCommand(PSCommandName);

            // Initialize the arguments
            
            if(ApplicationName.Expression != null)
            {
                targetCommand.AddParameter("ApplicationName", ApplicationName.Get(context));
            }

            if(BasePropertiesOnly.Expression != null)
            {
                targetCommand.AddParameter("BasePropertiesOnly", BasePropertiesOnly.Get(context));
            }

            if ((ComputerName.Expression != null) && (PSRemotingBehavior.Get(context) != RemotingBehavior.Custom))
            {
                targetCommand.AddParameter("ComputerName", ComputerName.Get(context));
            }

            if(ConnectionURI.Expression != null)
            {
                targetCommand.AddParameter("ConnectionURI", ConnectionURI.Get(context));
            }

            if(Dialect.Expression != null)
            {
                targetCommand.AddParameter("Dialect", Dialect.Get(context));
            }

            if(Enumerate.Expression != null)
            {
                targetCommand.AddParameter("Enumerate", Enumerate.Get(context));
            }

            if(Filter.Expression != null)
            {
                targetCommand.AddParameter("Filter", Filter.Get(context));
            }

            if(Fragment.Expression != null)
            {
                targetCommand.AddParameter("Fragment", Fragment.Get(context));
            }

            if(OptionSet.Expression != null)
            {
                targetCommand.AddParameter("OptionSet", OptionSet.Get(context));
            }

            if(Port.Expression != null)
            {
                targetCommand.AddParameter("Port", Port.Get(context));
            }

            if(Associations.Expression != null)
            {
                targetCommand.AddParameter("Associations", Associations.Get(context));
            }

            if(ResourceURI.Expression != null)
            {
                targetCommand.AddParameter("ResourceURI", ResourceURI.Get(context));
            }

            if(ReturnType.Expression != null)
            {
                targetCommand.AddParameter("ReturnType", ReturnType.Get(context));
            }

            if(SelectorSet.Expression != null)
            {
                targetCommand.AddParameter("SelectorSet", SelectorSet.Get(context));
            }

            if(SessionOption.Expression != null)
            {
                targetCommand.AddParameter("SessionOption", SessionOption.Get(context));
            }

            if(Shallow.Expression != null)
            {
                targetCommand.AddParameter("Shallow", Shallow.Get(context));
            }

            if(UseSSL.Expression != null)
            {
                targetCommand.AddParameter("UseSSL", UseSSL.Get(context));
            }

            if(Credential.Expression != null)
            {
                targetCommand.AddParameter("Credential", Credential.Get(context));
            }

            if(Authentication.Expression != null)
            {
                targetCommand.AddParameter("Authentication", Authentication.Get(context));
            }

            if(CertificateThumbprint.Expression != null)
            {
                targetCommand.AddParameter("CertificateThumbprint", CertificateThumbprint.Get(context));
            }

            if(GetIsComputerNameSpecified(context) && (PSRemotingBehavior.Get(context) == RemotingBehavior.Custom))
            {
                targetCommand.AddParameter("ComputerName", PSComputerName.Get(context));
            }

            return new ActivityImplementationContext() { PowerShellInstance = invoker };
        }
    }
}
