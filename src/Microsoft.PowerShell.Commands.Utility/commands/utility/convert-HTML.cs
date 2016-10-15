/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Management.Automation;
using System.Management.Automation.Internal;
using System.Net;
using System.Text;
using Microsoft.PowerShell.Commands.Internal.Format;

namespace Microsoft.PowerShell.Commands
{
    /// <summary>
    ///
    /// Class comment
    ///
    /// </summary>

    [Cmdlet(VerbsData.ConvertTo, "Html", DefaultParameterSetName = "Page",
        HelpUri = "http://go.microsoft.com/fwlink/?LinkID=113290", RemotingCapability = RemotingCapability.None)]
    public sealed
    class ConvertToHtmlCommand : PSCmdlet
    {
        /// <summary>The incoming object</summary>
        /// <value></value>
        [Parameter(ValueFromPipeline = true)]
        public PSObject InputObject
        {
            get
            {
                return _inputObject;
            }
            set
            {
                _inputObject = value;
            }
        }
        private PSObject _inputObject;

        /// <summary>
        /// The list of properties to display
        /// These take the form of an MshExpression
        /// </summary>
        /// <value></value>
        [Parameter(Position = 0)]
        public object[] Property
        {
            get
            {
                return _property;
            }
            set
            {
                _property = value;
            }
        }
        private object[] _property;

        /// <summary>
        /// Text to go after the opening body tag
        /// and before the table
        /// </summary>
        /// <value></value>
        [Parameter(ParameterSetName = "Page", Position = 3)]
        public string[] Body
        {
            get
            {
                return _body;
            }
            set
            {
                _body = value;
            }
        }
        private string[] _body;

        /// <summary>
        /// Text to go into the head section
        /// of the html doc
        /// </summary>
        /// <value></value>
        [Parameter(ParameterSetName = "Page", Position = 1)]
        public string[] Head
        {
            get
            {
                return _head;
            }
            set
            {
                _head = value;
            }
        }
        private string[] _head;

        /// <summary>
        /// The string for the title tag
        /// The title is also placed in the body of the document
        /// before the table between h3 tags
        /// If the -Head parameter is used, this parameter has no
        /// effect.
        /// </summary>
        /// <value></value>
        [Parameter(ParameterSetName = "Page", Position = 2)]
        [ValidateNotNullOrEmpty]
        public string Title
        {
            get
            {
                return _title;
            }
            set
            {
                _title = value;
            }
        }
        private string _title = "HTML TABLE";

        /// <summary>
        /// This specifies whether the objects should
        /// be rendered as an HTML TABLE or
        /// HTML LIST
        /// </summary>
        /// <value></value>
        [Parameter]
        [ValidateNotNullOrEmpty]
        [ValidateSet("Table", "List")]
        public string As
        {
            get
            {
                return _as;
            }
            set
            {
                _as = value;
            }
        }
        private string _as = "Table";

        /// <summary>
        /// This specifies a full or partial URI
        /// for the CSS information.
        /// The html should reference the css file specified
        /// </summary>
        [Parameter(ParameterSetName = "Page")]
        [Alias("cu", "uri")]
        [ValidateNotNullOrEmpty]
        public Uri CssUri
        {
            get
            {
                return _cssuri;
            }
            set
            {
                _cssuri = value;
                _cssuriSpecified = true;
            }
        }
        private Uri _cssuri;
        private bool _cssuriSpecified;

        /// <summary>
        /// When this switch is specified generate only the
        /// HTML representation of the incoming object
        /// without the HTML,HEAD,TITLE,BODY,etc tags.
        /// </summary>
        [Parameter(ParameterSetName = "Fragment")]
        [ValidateNotNullOrEmpty]
        public SwitchParameter Fragment
        {
            get
            {
                return _fragment;
            }
            set
            {
                _fragment = value;
            }
        }
        private SwitchParameter _fragment;

        /// <summary>
        /// Specifies the text to include prior the 
        /// closing body tag of the HTML output
        /// </summary>
        [Parameter]
        [ValidateNotNullOrEmpty]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public string[] PostContent
        {
            get
            {
                return _postContent;
            }
            set
            {
                _postContent = value;
            }
        }
        private string[] _postContent;

        /// <summary>
        /// Specifies the text to include after the 
        /// body tag of the HTML output
        /// </summary>
        [Parameter]
        [ValidateNotNullOrEmpty]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public string[] PreContent
        {
            get
            {
                return _preContent;
            }
            set
            {
                _preContent = value;
            }
        }
        private string[] _preContent;

        /// <summary>
        /// definitions for hash table keys
        /// </summary>
        internal static class ConvertHTMLParameterDefinitionKeys
        {
            internal const string LabelEntryKey = "label";
            internal const string AlignmentEntryKey = "alignment";
            internal const string WidthEntryKey = "width";
        }

        /// <summary>
        /// This allows for @{e='foo';label='bar';alignment='center';width='20'}
        /// </summary>
        internal class ConvertHTMLExpressionParameterDefinition : CommandParameterDefinition
        {
            protected override void SetEntries()
            {
                this.hashEntries.Add(new ExpressionEntryDefinition());
                this.hashEntries.Add(new HashtableEntryDefinition(ConvertHTMLParameterDefinitionKeys.LabelEntryKey, new Type[] { typeof(string) }));
                this.hashEntries.Add(new HashtableEntryDefinition(ConvertHTMLParameterDefinitionKeys.AlignmentEntryKey, new Type[] { typeof(string) }));
                this.hashEntries.Add(new HashtableEntryDefinition(ConvertHTMLParameterDefinitionKeys.WidthEntryKey, new Type[] { typeof(string) }));
            }
        }

        /// <summary>
        /// Create a list of MshParameter from properties
        /// </summary>
        /// <param name="properties">can be a string, ScriptBlock, or Hashtable</param>
        /// <returns></returns>
        private List<MshParameter> ProcessParameter(object[] properties)
        {
            TerminatingErrorContext invocationContext = new TerminatingErrorContext(this);
            ParameterProcessor processor =
                new ParameterProcessor(new ConvertHTMLExpressionParameterDefinition());
            if (properties == null)
            {
                properties = new object[] { "*" };
            }
            return processor.ProcessParameters(properties, invocationContext);
        }

        /// <summary>
        /// Resolve all wildcards in user input Property into resolvedNameMshParameters
        /// </summary>
        private void InitializeResolvedNameMshParameters()
        {
            // temp list of properties with wildcards resolved
            ArrayList resolvedNameProperty = new ArrayList();

            foreach (MshParameter p in _propertyMshParameterList)
            {
                string label = p.GetEntry(ConvertHTMLParameterDefinitionKeys.LabelEntryKey) as string;
                string alignment = p.GetEntry(ConvertHTMLParameterDefinitionKeys.AlignmentEntryKey) as string;
                string width = p.GetEntry(ConvertHTMLParameterDefinitionKeys.WidthEntryKey) as string;
                MshExpression ex = p.GetEntry(FormatParameterDefinitionKeys.ExpressionEntryKey) as MshExpression;
                List<MshExpression> resolvedNames = ex.ResolveNames(_inputObject);
                if (resolvedNames.Count == 1)
                {
                    Hashtable ht = CreateAuxPropertyHT(label, alignment, width);
                    if (ex.Script != null)
                        ht.Add(FormatParameterDefinitionKeys.ExpressionEntryKey, ex.Script);
                    else
                        ht.Add(FormatParameterDefinitionKeys.ExpressionEntryKey, ex.ToString());
                    resolvedNameProperty.Add(ht);
                }
                else
                {
                    foreach (MshExpression resolvedName in resolvedNames)
                    {
                        Hashtable ht = CreateAuxPropertyHT(label, alignment, width);
                        ht.Add(FormatParameterDefinitionKeys.ExpressionEntryKey, resolvedName.ToString());
                        resolvedNameProperty.Add(ht);
                    }
                }
            }
            _resolvedNameMshParameters = ProcessParameter(resolvedNameProperty.ToArray());
        }

        private static Hashtable CreateAuxPropertyHT(
            string label,
            string alignment,
            string width)
        {
            Hashtable ht = new Hashtable();
            if (label != null)
            {
                ht.Add(ConvertHTMLParameterDefinitionKeys.LabelEntryKey, label);
            }
            if (alignment != null)
            {
                ht.Add(ConvertHTMLParameterDefinitionKeys.AlignmentEntryKey, alignment);
            }
            if (width != null)
            {
                ht.Add(ConvertHTMLParameterDefinitionKeys.WidthEntryKey, width);
            }
            return ht;
        }

        /// <summary>
        /// calls ToString. If an exception occurs, eats it and return string.Empty
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        private static string SafeToString(object obj)
        {
            if (obj == null)
            {
                return "";
            }
            try
            {
                return obj.ToString();
            }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                // eats exception if safe
            }
            return "";
        }


        /// <summary>
        /// 
        /// </summary>
        protected override void BeginProcessing()
        {
            //ValidateNotNullOrEmpty attribute is not working for System.Uri datatype, so handling it here
            if ((_cssuriSpecified) && (string.IsNullOrEmpty(_cssuri.OriginalString.Trim())))
            {
                ArgumentException ex = new ArgumentException(StringUtil.Format(UtilityCommonStrings.EmptyCSSUri, "CSSUri"));
                ErrorRecord er = new ErrorRecord(ex, "ArgumentException", ErrorCategory.InvalidArgument, "CSSUri");
                ThrowTerminatingError(er);
            }

            _propertyMshParameterList = ProcessParameter(_property);

            if (!String.IsNullOrEmpty(_title))
            {
                WebUtility.HtmlEncode(_title);
            }


            // This first line ensures w3c validation will succeed. However we are not specifying
            // an encoding in the HTML because we don't know where the text will be written and
            // if a particular encoding will be used.

            if (!_fragment)
            {
                WriteObject("<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Strict//EN\"  \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd\">");
                WriteObject("<html xmlns=\"http://www.w3.org/1999/xhtml\">");
                WriteObject("<head>");
                WriteObject(_head ?? new string[] { "<title>" + _title + "</title>" }, true);
                if (_cssuriSpecified)
                {
                    WriteObject("<link rel=\"stylesheet\" type=\"text/css\" href=\"" + _cssuri + "\" />");
                }
                WriteObject("</head><body>");
                if (_body != null)
                {
                    WriteObject(_body, true);
                }
            }
            if (_preContent != null)
            {
                WriteObject(_preContent, true);
            }
            WriteObject("<table>");
            _isTHWritten = false;
            _propertyCollector = new StringCollection();
        }

        /// <summary>
        /// Reads Width and Alignment from Property and write Col tags
        /// </summary>
        /// <param name="mshParams"></param>
        private void WriteColumns(List<MshParameter> mshParams)
        {
            StringBuilder COLTag = new StringBuilder();

            COLTag.Append("<colgroup>");
            foreach (MshParameter p in mshParams)
            {
                COLTag.Append("<col");
                string width = p.GetEntry(ConvertHTMLParameterDefinitionKeys.WidthEntryKey) as string;
                if (width != null)
                {
                    COLTag.Append(" width = \"");
                    COLTag.Append(width);
                    COLTag.Append("\"");
                }
                string alignment = p.GetEntry(ConvertHTMLParameterDefinitionKeys.AlignmentEntryKey) as string;
                if (alignment != null)
                {
                    COLTag.Append(" align = \"");
                    COLTag.Append(alignment);
                    COLTag.Append("\"");
                }
                COLTag.Append("/>");
            }

            COLTag.Append("</colgroup>");

            // The columngroup and col nodes will be printed in a single line.
            WriteObject(COLTag.ToString());
        }

        /// <summary>
        /// Writes the list entries when the As parameter has value List
        /// </summary>
        private void WriteListEntry()
        {
            foreach (MshParameter p in _resolvedNameMshParameters)
            {
                StringBuilder Listtag = new StringBuilder();
                Listtag.Append("<tr><td>");

                //for writing the property name
                WritePropertyName(Listtag, p);
                Listtag.Append(":");
                Listtag.Append("</td>");

                //for writing the property value
                Listtag.Append("<td>");
                WritePropertyValue(Listtag, p);
                Listtag.Append("</td></tr>");

                WriteObject(Listtag.ToString());
            }
        }

        /// <summary>
        /// To write the Property name
        /// </summary>
        private void WritePropertyName(StringBuilder Listtag, MshParameter p)
        {
            //for writing the property name
            string label = p.GetEntry(ConvertHTMLParameterDefinitionKeys.LabelEntryKey) as string;
            if (label != null)
            {
                Listtag.Append(label);
            }
            else
            {
                MshExpression ex = p.GetEntry(FormatParameterDefinitionKeys.ExpressionEntryKey) as MshExpression;
                Listtag.Append(ex.ToString());
            }
        }

        /// <summary>
        /// To write the Property value
        /// </summary>
        private void WritePropertyValue(StringBuilder Listtag, MshParameter p)
        {
            MshExpression exValue = p.GetEntry(FormatParameterDefinitionKeys.ExpressionEntryKey) as MshExpression;

            // get the value of the property
            List<MshExpressionResult> resultList = exValue.GetValues(_inputObject);
            foreach (MshExpressionResult result in resultList)
            {
                // create comma sep list for multiple results
                if (result.Result != null)
                {
                    string htmlEncodedResult = WebUtility.HtmlEncode(SafeToString(result.Result));
                    Listtag.Append(htmlEncodedResult);
                }
                Listtag.Append(", ");
            }
            if (Listtag.ToString().EndsWith(", ", StringComparison.Ordinal))
            {
                Listtag.Remove(Listtag.Length - 2, 2);
            }
        }

        /// <summary>
        /// To write the Table header for the object property names
        /// </summary>
        private void WriteTableHeader(StringBuilder THtag, List<MshParameter> resolvedNameMshParameters)
        {
            //write the property names 
            foreach (MshParameter p in resolvedNameMshParameters)
            {
                THtag.Append("<th>");
                WritePropertyName(THtag, p);
                THtag.Append("</th>");
            }
        }

        /// <summary>
        /// To write the Table row for the object property values
        /// </summary>
        private void WriteTableRow(StringBuilder TRtag, List<MshParameter> resolvedNameMshParameters)
        {
            //write the property values
            foreach (MshParameter p in resolvedNameMshParameters)
            {
                TRtag.Append("<td>");
                WritePropertyValue(TRtag, p);
                TRtag.Append("</td>");
            }
        }

        //count of the objects
        private int _numberObjects = 0;

        /// <summary>
        /// 
        /// 
        /// </summary>
        protected override void ProcessRecord()
        {
            // writes the table headers
            // it is not in BeginProcessing because the first inputObject is needed for
            // the number of columns and column name
            if (_inputObject == null || _inputObject == AutomationNull.Value)
            {
                return;
            }
            _numberObjects++;
            if (!_isTHWritten)
            {
                InitializeResolvedNameMshParameters();
                if (_resolvedNameMshParameters == null || _resolvedNameMshParameters.Count == 0)
                {
                    return;
                }

                //if the As parameter is given as List
                if (_as.Equals("List", StringComparison.OrdinalIgnoreCase))
                {
                    //if more than one object,write the horizontal rule to put visual separator
                    if (_numberObjects > 1)
                        WriteObject("<tr><td><hr></td></tr>");
                    WriteListEntry();
                }
                else //if the As parameter is Table, first we have to write the property names
                {
                    WriteColumns(_resolvedNameMshParameters);

                    StringBuilder THtag = new StringBuilder("<tr>");

                    //write the table header
                    WriteTableHeader(THtag, _resolvedNameMshParameters);

                    THtag.Append("</tr>");
                    WriteObject(THtag.ToString());
                    _isTHWritten = true;
                }
            }
            //if the As parameter is Table, write the property values
            if (_as.Equals("Table", StringComparison.OrdinalIgnoreCase))
            {
                StringBuilder TRtag = new StringBuilder("<tr>");

                //write the table row
                WriteTableRow(TRtag, _resolvedNameMshParameters);

                TRtag.Append("</tr>");
                WriteObject(TRtag.ToString());
            }
        }

        /// <summary>
        /// 
        /// </summary>
        protected override void EndProcessing()
        {
            //if fragment,end with table
            WriteObject("</table>");
            if (_postContent != null)
                WriteObject(_postContent, true);

            //if not fragment end with body and html also
            if (!_fragment)
            {
                WriteObject("</body></html>");
            }
        }

        #region private

        /// <summary>
        /// list of incoming objects to compare
        /// </summary>
        private bool _isTHWritten;
        private StringCollection _propertyCollector;
        private List<MshParameter> _propertyMshParameterList;
        private List<MshParameter> _resolvedNameMshParameters;
        //private string ResourcesBaseName = "ConvertHTMLStrings";

        #endregion private
    }
}

