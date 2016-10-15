/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System.Globalization;
using System.Collections.ObjectModel;
using System.Net;
using System.ComponentModel;
using System.Management.Automation.Internal;
using System.Diagnostics;
using System.Threading;
using System.IO;
using System.Xml;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using Microsoft.PowerShell.Commands;
using Microsoft.Win32;
using System.Security;

#if CORECLR
using System.Net.Http;
using System.Threading.Tasks;
// Use stub for SerializableAttribute, SerializationInfo and ISerializable related types. WebClient.
using Microsoft.PowerShell.CoreClr.Stubs;
#else
using System.Xml.Schema;
#endif

namespace System.Management.Automation.Help
{
    /// <summary>
    /// Updatable help system exception
    /// </summary>
    [Serializable]
    internal class UpdatableHelpSystemException : Exception
    {
        /// <summary>
        /// Class constructor
        /// </summary>
        /// <param name="errorId">FullyQualifiedErrorId</param>
        /// <param name="message">exception message</param>
        /// <param name="cat">category</param>
        /// <param name="targetObject">target object</param>
        /// <param name="innerException">inner exception</param>
        internal UpdatableHelpSystemException(string errorId, string message, ErrorCategory cat, object targetObject, Exception innerException)
            : base(message, innerException)
        {
            FullyQualifiedErrorId = errorId;
            ErrorCategory = cat;
            TargetObject = targetObject;
        }

#if !CORECLR
        /// <summary>
        /// Class constructor
        /// </summary>
        /// <param name="serializationInfo">serialization info</param>
        /// <param name="streamingContext">streaming context</param>
        protected UpdatableHelpSystemException(SerializationInfo serializationInfo, StreamingContext streamingContext)
            : base(serializationInfo, streamingContext)
        {
        }
#endif

        /// <summary>
        /// Fully qualified error id
        /// </summary>
        internal string FullyQualifiedErrorId { get; }

        /// <summary>
        /// Error category
        /// </summary>
        internal ErrorCategory ErrorCategory { get; }

        /// <summary>
        /// Target object
        /// </summary>
        internal object TargetObject { get; }
    }

    /// <summary>
    /// Exception context
    /// </summary>
    internal class UpdatableHelpExceptionContext
    {
        /// <summary>
        /// Class constructor
        /// </summary>
        /// <param name="exception">exception to wrap</param>
        internal UpdatableHelpExceptionContext(UpdatableHelpSystemException exception)
        {
            Exception = exception;
            Modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Cultures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A list of modules
        /// </summary>
        internal HashSet<string> Modules { get; set; }

        /// <summary>
        /// A list of UI cultures
        /// </summary>
        internal HashSet<string> Cultures { get; set; }

        /// <summary>
        /// Gets the help system exception
        /// </summary>
        internal UpdatableHelpSystemException Exception { get; }

        /// <summary>
        /// Creates an error record from this context
        /// </summary>
        /// <param name="commandType">command type</param>
        /// <returns>error record</returns>
        internal ErrorRecord CreateErrorRecord(UpdatableHelpCommandType commandType)
        {
            Debug.Assert(Modules.Count != 0);

            return new ErrorRecord(new Exception(GetExceptionMessage(commandType)), Exception.FullyQualifiedErrorId, Exception.ErrorCategory,
                Exception.TargetObject);
        }

        /// <summary>
        /// Gets the exception message
        /// </summary>
        /// <param name="commandType"></param>
        /// <returns></returns>
        internal string GetExceptionMessage(UpdatableHelpCommandType commandType)
        {
            string message = "";
            SortedSet<string> sortedModules = new SortedSet<string>(Modules, StringComparer.CurrentCultureIgnoreCase);
            SortedSet<string> sortedCultures = new SortedSet<string>(Cultures, StringComparer.CurrentCultureIgnoreCase);
            string modules = String.Join(", ", sortedModules);
            string cultures = String.Join(", ", sortedCultures);

            if (commandType == UpdatableHelpCommandType.UpdateHelpCommand)
            {
                if (Cultures.Count == 0)
                {
                    message = StringUtil.Format(HelpDisplayStrings.FailedToUpdateHelpForModule, modules, Exception.Message);
                }
                else
                {
                    message = StringUtil.Format(HelpDisplayStrings.FailedToUpdateHelpForModuleWithCulture, modules, cultures, Exception.Message);
                }
            }
            else
            {
                if (Cultures.Count == 0)
                {
                    message = StringUtil.Format(HelpDisplayStrings.FailedToSaveHelpForModule, modules, Exception.Message);
                }
                else
                {
                    message = StringUtil.Format(HelpDisplayStrings.FailedToSaveHelpForModuleWithCulture, modules, cultures, Exception.Message);
                }
            }

            return message;
        }
    }

    /// <summary>
    /// Enumeration showing Update or Save help
    /// </summary>
    internal enum UpdatableHelpCommandType
    {
        UnknownCommand = 0,
        UpdateHelpCommand = 1,
        SaveHelpCommand = 2
    }

    /// <summary>
    /// Progress event arguments
    /// </summary>
    internal class UpdatableHelpProgressEventArgs : EventArgs
    {
        /// <summary>
        /// Class constructor
        /// </summary>
        /// <param name="moduleName">module name</param>
        /// <param name="status">progress status</param>
        /// <param name="percent">progress percentage</param>
        internal UpdatableHelpProgressEventArgs(string moduleName, string status, int percent)
        {
            Debug.Assert(!String.IsNullOrEmpty(status));

            CommandType = UpdatableHelpCommandType.UnknownCommand;
            ProgressStatus = status;
            ProgressPercent = percent;
            ModuleName = moduleName;
        }

        /// <summary>
        /// Class constructor
        /// </summary>
        /// <param name="moduleName">module name</param>
        /// <param name="type">command type</param>
        /// <param name="status">progress status</param>
        /// <param name="percent">progress percentage</param>
        internal UpdatableHelpProgressEventArgs(string moduleName, UpdatableHelpCommandType type, string status, int percent)
        {
            Debug.Assert(!String.IsNullOrEmpty(status));

            CommandType = type;
            ProgressStatus = status;
            ProgressPercent = percent;
            ModuleName = moduleName;
        }

        /// <summary>
        /// Progress status
        /// </summary>
        internal string ProgressStatus { get; }

        /// <summary>
        /// Progress percentage
        /// </summary>
        internal int ProgressPercent { get; }


        /// <summary>
        /// Module name
        /// </summary>
        internal string ModuleName { get; }

        /// <summary>
        /// Command type
        /// </summary>
        internal UpdatableHelpCommandType CommandType { get; set; }
    }

    /// <summary>
    /// This class implements the Updatable Help System common operations
    /// </summary>
    internal class UpdatableHelpSystem : IDisposable
    {
#if CORECLR
        private TimeSpan _defaultTimeout;
#else
        private AutoResetEvent _completionEvent;
        private bool _completed;
#endif
        private Collection<UpdatableHelpProgressEventArgs> _progressEvents;
        private bool _stopping;
        private object _syncObject;
        private UpdatableHelpCommandBase _cmdlet;
        private CancellationTokenSource _cancelTokenSource;

        internal WebClient WebClient { get; }

        internal string CurrentModule { get; set; }

        /// <summary>
        /// Class constructor
        /// </summary>
        internal UpdatableHelpSystem(UpdatableHelpCommandBase cmdlet, bool useDefaultCredentials)
        {
            WebClient = new WebClient();
#if CORECLR
            _defaultTimeout = new TimeSpan(0, 0, 30);
#else
            _completionEvent = new AutoResetEvent(false);
            _completed = false;
#endif
            _progressEvents = new Collection<UpdatableHelpProgressEventArgs>();
            Errors = new Collection<Exception>();
            _stopping = false;
            _syncObject = new object();
            _cmdlet = cmdlet;
            _cancelTokenSource = new CancellationTokenSource();

            WebClient.UseDefaultCredentials = useDefaultCredentials;

#if !CORECLR
            WebClient.DownloadProgressChanged += new DownloadProgressChangedEventHandler(HandleDownloadProgressChanged);
            WebClient.DownloadFileCompleted += new AsyncCompletedEventHandler(HandleDownloadFileCompleted);
#endif
        }

        /// <summary>
        /// Disposes the help system
        /// </summary>
        public void Dispose()
        {
#if !CORECLR
            _completionEvent.Dispose();
#endif
            _cancelTokenSource.Dispose();
            WebClient.Dispose();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Help system errors
        /// </summary>
        internal Collection<Exception> Errors { get; }

        /// <summary>
        /// Gets the current UIculture (includes the fallback chain)
        /// </summary>
        /// <returns>a list of cultures</returns>
        internal IEnumerable<string> GetCurrentUICulture()
        {
            CultureInfo culture = CultureInfo.CurrentUICulture;

            while (culture != null)
            {
                if (string.IsNullOrEmpty(culture.Name))
                {
                    yield break;
                }

                yield return culture.Name;

                culture = culture.Parent;
            }

            yield break;
        }

        #region Help Metadata Retrieval

        /// <summary>
        /// Gets an internal help URI
        /// </summary>
        /// <param name="module">internal module information</param>
        /// <param name="culture">help content culture</param>
        /// <returns>internal help uri representation</returns>
        internal UpdatableHelpUri GetHelpInfoUri(UpdatableHelpModuleInfo module, CultureInfo culture)
        {
            return new UpdatableHelpUri(module.ModuleName, module.ModuleGuid, culture, ResolveUri(module.HelpInfoUri, false));
        }

        /// <summary>
        /// Gets the HelpInfo xml from the given URI
        /// </summary>
        /// <param name="commandType">command type</param>
        /// <param name="uri">HelpInfo URI</param>
        /// <param name="moduleName">module name</param>
        /// <param name="moduleGuid">module GUID</param>
        /// <param name="culture">current UI culture</param>
        /// <returns>HelpInfo object</returns>
        internal UpdatableHelpInfo GetHelpInfo(UpdatableHelpCommandType commandType, string uri, string moduleName, Guid moduleGuid, string culture)
        {
            try
            {
                OnProgressChanged(this, new UpdatableHelpProgressEventArgs(CurrentModule, commandType, StringUtil.Format(
                    HelpDisplayStrings.UpdateProgressLocating), 0));

#if CORECLR
                string xml;
                using (HttpClientHandler handler = new HttpClientHandler())
                {
                    handler.UseDefaultCredentials = WebClient.UseDefaultCredentials;
                    using (HttpClient client = new HttpClient(handler))
                    {
                        client.Timeout = _defaultTimeout;
                        Task<string> responseBody = client.GetStringAsync(uri);
                        xml = responseBody.Result;
                        if (responseBody.Exception != null)
                        {
                            return null;
                        }
                    }
                }
#else
                string xml = WebClient.DownloadString(uri);
#endif
                UpdatableHelpInfo helpInfo = CreateHelpInfo(xml, moduleName, moduleGuid,
                                                            currentCulture: culture, pathOverride: null, verbose: true,
                                                            shouldResolveUri: true, ignoreValidationException: false);

                return helpInfo;
            }
#if !CORECLR
            catch (WebException)
            {
                return null;
            }
#endif
            finally
            {
                OnProgressChanged(this, new UpdatableHelpProgressEventArgs(CurrentModule, commandType, StringUtil.Format(
                    HelpDisplayStrings.UpdateProgressLocating), 100));
            }
        }

        /// <summary>
        /// Sends a standard HTTP request to get the resolved URI (potential FwLinks)
        /// </summary>
        /// <param name="baseUri">base URI</param>
        /// <param name="verbose"></param>
        /// <returns>resolved URI</returns>
        private string ResolveUri(string baseUri, bool verbose)
        {
            Debug.Assert(!String.IsNullOrEmpty(baseUri));

            // Directory.Exists checks if baseUri is a network drive or
            // a local directory. If baseUri is local, we don't need to resolve it.
            // 
            // The / check works because all of our fwlinks must resolve
            // to a remote virtual directory. I think HTTP always appends /
            // in reference to a directory.
            // Like if you send a request to www.technet.com/powershell you will get
            // a 301/203 response with the response URI set to www.technet.com/powershell/
            //
            if (Directory.Exists(baseUri) || baseUri.EndsWith("/", StringComparison.OrdinalIgnoreCase))
            {
                if (verbose)
                {
                    _cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.URIRedirectWarningToHost, baseUri));
                }

                return baseUri;
            }

            if (verbose)
            {
                _cmdlet.WriteVerbose(StringUtil.Format(HelpDisplayStrings.UpdateHelpResolveUriVerbose, baseUri));
            }

            string uri = baseUri;

            try
            {
                // We only allow 10 redirections
                for (int i = 0; i < 10; i++)
                {
                    if (_stopping)
                    {
                        return uri;
                    }

#if CORECLR
                    using (HttpClientHandler handler = new HttpClientHandler())
                    {
                        handler.AllowAutoRedirect = false;
                        handler.UseDefaultCredentials = WebClient.UseDefaultCredentials;
                        using (HttpClient client = new HttpClient(handler))
                        {
                            client.Timeout = new TimeSpan(0, 0, 30); // Set 30 second timeout
                            Task<HttpResponseMessage> responseMessage = client.GetAsync(uri);
                            using (HttpResponseMessage response = responseMessage.Result)
                            {
                                if (response.StatusCode == HttpStatusCode.Found ||
                                    response.StatusCode == HttpStatusCode.Redirect ||
                                    response.StatusCode == HttpStatusCode.Moved ||
                                    response.StatusCode == HttpStatusCode.MovedPermanently)
                                {
                                    Uri responseUri = response.Headers.Location;

                                    if (responseUri.IsAbsoluteUri)
                                    {
                                        uri = responseUri.ToString();
                                    }
                                    else
                                    {
                                        Uri originalAbs = new Uri(uri);
                                        uri = uri.Replace(originalAbs.AbsolutePath, responseUri.ToString());
                                    }

                                    uri = uri.Trim();

                                    if (verbose)
                                    {
                                        _cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.URIRedirectWarningToHost, uri));
                                    }

                                    if (uri.EndsWith("/", StringComparison.OrdinalIgnoreCase))
                                    {
                                        return uri;
                                    }
                                }
                                else if (response.StatusCode == HttpStatusCode.OK)
                                {
                                    if (uri.EndsWith("/", StringComparison.OrdinalIgnoreCase))
                                    {
                                        return uri;
                                    }
                                    else
                                    {
                                        throw new UpdatableHelpSystemException("InvalidHelpInfoUri", StringUtil.Format(HelpDisplayStrings.InvalidHelpInfoUri, uri),
                                            ErrorCategory.InvalidOperation, null, null);
                                    }
                                }
                            }
                        }
                    }
#else
                    HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(uri);

                    request.AllowAutoRedirect = false;
                    request.Timeout = 30000;

                    HttpWebResponse response = (HttpWebResponse)request.GetResponse();

                    WebHeaderCollection headers = response.Headers;

                    try
                    {
                        if (response.StatusCode == HttpStatusCode.Found ||
                            response.StatusCode == HttpStatusCode.Redirect ||
                            response.StatusCode == HttpStatusCode.Moved ||
                            response.StatusCode == HttpStatusCode.MovedPermanently)
                        {
                            Uri responseUri = new Uri(headers["Location"], UriKind.RelativeOrAbsolute);

                            if (responseUri.IsAbsoluteUri)
                            {
                                uri = responseUri.ToString();
                            }
                            else
                            {
                                uri = uri.Replace(request.Address.AbsolutePath, responseUri.ToString());
                            }

                            uri = uri.Trim();

                            if (verbose)
                            {
                                _cmdlet.WriteVerbose(StringUtil.Format(RemotingErrorIdStrings.URIRedirectWarningToHost, uri));
                            }

                            if (uri.EndsWith("/", StringComparison.OrdinalIgnoreCase))
                            {
                                return uri;
                            }
                        }
                        else if (response.StatusCode == HttpStatusCode.OK)
                        {
                            if (uri.EndsWith("/", StringComparison.OrdinalIgnoreCase))
                            {
                                return uri;
                            }
                            else
                            {
                                throw new UpdatableHelpSystemException("InvalidHelpInfoUri", StringUtil.Format(HelpDisplayStrings.InvalidHelpInfoUri, uri),
                                    ErrorCategory.InvalidOperation, null, null);
                            }
                        }
                    }
                    finally
                    {
                        response.Close();
                    }
#endif
                }
            }
            catch (UriFormatException e)
            {
                throw new UpdatableHelpSystemException("InvalidUriFormat", e.Message, ErrorCategory.InvalidData, null, e);
            }

            throw new UpdatableHelpSystemException("TooManyRedirections", StringUtil.Format(HelpDisplayStrings.TooManyRedirections),
                ErrorCategory.InvalidOperation, null, null);
        }

        /// <summary>
        /// HelpInfo.xml schema
        /// </summary>
        private const string HelpInfoXmlSchema = @"<?xml version=""1.0"" encoding=""utf-8""?>
            <xs:schema attributeFormDefault=""unqualified"" elementFormDefault=""qualified""
                targetNamespace=""http://schemas.microsoft.com/powershell/help/2010/05"" xmlns:xs=""http://www.w3.org/2001/XMLSchema"">
                <xs:element name=""HelpInfo"">
                    <xs:complexType>
                        <xs:sequence>
                            <xs:element name=""HelpContentURI"" type=""xs:anyURI"" minOccurs=""1"" maxOccurs=""1"" />
                            <xs:element name=""SupportedUICultures"" minOccurs=""1"" maxOccurs=""1"">
                                <xs:complexType>
                                    <xs:sequence>
                                        <xs:element name=""UICulture"" minOccurs=""1"" maxOccurs=""unbounded"">
                                            <xs:complexType>
                                                <xs:sequence>
                                                    <xs:element name=""UICultureName"" type=""xs:language"" minOccurs=""1"" maxOccurs=""1"" />
                                                    <xs:element name=""UICultureVersion"" type=""xs:string"" minOccurs=""1"" maxOccurs=""1"" />
                                                </xs:sequence>
                                            </xs:complexType>
                                        </xs:element>
                                    </xs:sequence>
                                </xs:complexType>
                            </xs:element>
                        </xs:sequence>
                    </xs:complexType>
                </xs:element>
            </xs:schema>";
        private const string HelpInfoXmlNamespace = "http://schemas.microsoft.com/powershell/help/2010/05";
        private const string HelpInfoXmlValidationFailure = "HelpInfoXmlValidationFailure";

        /// <summary>
        /// Creates a HelpInfo object
        /// </summary>
        /// <param name="xml">XML text</param>
        /// <param name="moduleName">module name</param>
        /// <param name="moduleGuid">module GUID</param>
        /// <param name="currentCulture">current UI cultures</param>
        /// <param name="pathOverride">overrides the path contained within HelpInfo.xml</param>
        /// <param name="verbose"></param>
        /// <param name="shouldResolveUri">
        /// Resolve the uri retrieved from the <paramref name="xml"/> content. The uri is resolved
        /// to handle redirections if any.
        /// </param>
        /// <param name="ignoreValidationException">ignore the xsd validation exception and return null in such case</param>
        /// <returns>HelpInfo object</returns>
        internal UpdatableHelpInfo CreateHelpInfo(string xml, string moduleName, Guid moduleGuid,
            string currentCulture, string pathOverride, bool verbose, bool shouldResolveUri, bool ignoreValidationException)
        {
            XmlDocument document = null;
            try
            {
                document = CreateValidXmlDocument(xml, HelpInfoXmlNamespace, HelpInfoXmlSchema,
#if !CORECLR
                    new ValidationEventHandler(HelpInfoValidationHandler),
#endif
                    true);
            }
            catch (UpdatableHelpSystemException e)
            {
                if (ignoreValidationException && HelpInfoXmlValidationFailure.Equals(e.FullyQualifiedErrorId, StringComparison.Ordinal))
                {
                    return null;
                }

                throw;
            }
            catch (XmlException e)
            {
                if (ignoreValidationException) { return null; }

                throw new UpdatableHelpSystemException(HelpInfoXmlValidationFailure,
                    e.Message, ErrorCategory.InvalidData, null, e);
            }

            string uri = pathOverride;
            string unresolvedUri = document["HelpInfo"]["HelpContentURI"].InnerText;

            if (String.IsNullOrEmpty(pathOverride))
            {
                if (shouldResolveUri)
                {
                    uri = ResolveUri(unresolvedUri, verbose);
                }
                else
                {
                    uri = unresolvedUri;
                }
            }

            XmlNodeList cultures = document["HelpInfo"]["SupportedUICultures"].ChildNodes;

            CultureSpecificUpdatableHelp[] updatableHelpItem = new CultureSpecificUpdatableHelp[cultures.Count];

            for (int i = 0; i < cultures.Count; i++)
            {
                updatableHelpItem[i] = new CultureSpecificUpdatableHelp(
                    new CultureInfo(cultures[i]["UICultureName"].InnerText),
                    new Version(cultures[i]["UICultureVersion"].InnerText));
            }

            UpdatableHelpInfo helpInfo = new UpdatableHelpInfo(unresolvedUri, updatableHelpItem);

            if (!String.IsNullOrEmpty(currentCulture))
            {
                WildcardOptions wildcardOptions = WildcardOptions.IgnoreCase | WildcardOptions.CultureInvariant;
                IEnumerable<WildcardPattern> patternList = SessionStateUtilities.CreateWildcardsFromStrings(new string[1] { currentCulture }, wildcardOptions);

                for (int i = 0; i < updatableHelpItem.Length; i++)
                {
                    if (SessionStateUtilities.MatchesAnyWildcardPattern(updatableHelpItem[i].Culture.Name, patternList, true))
                    {
                        helpInfo.HelpContentUriCollection.Add(new UpdatableHelpUri(moduleName, moduleGuid, updatableHelpItem[i].Culture, uri));
                    }
                }
            }


            if (!String.IsNullOrEmpty(currentCulture) && helpInfo.HelpContentUriCollection.Count == 0)
            {
                // throw exception
                throw new UpdatableHelpSystemException("HelpCultureNotSupported",
                    StringUtil.Format(HelpDisplayStrings.HelpCultureNotSupported,
                        currentCulture, helpInfo.GetSupportedCultures()), ErrorCategory.InvalidOperation, null, null);
            }

            return helpInfo;
        }

#if CORECLR

        /// <summary>
        /// Creates a valid xml document
        /// </summary>
        /// <param name="xml">input xml</param>
        /// <param name="ns">schema namespace</param>
        /// <param name="schema">xml schema</param>
        /// <param name="helpInfo">HelpInfo or HelpContent?</param>
        private XmlDocument CreateValidXmlDocument(string xml, string ns, string schema, bool helpInfo)
        {
            XmlReaderSettings settings = new XmlReaderSettings();

            XmlReader reader = XmlReader.Create(new StringReader(xml), settings);
            XmlDocument document = new XmlDocument();

            try
            {
                document.Load(reader);
            }
            catch (XmlException e)
            {
                if (helpInfo)
                {
                    throw new UpdatableHelpSystemException(HelpInfoXmlValidationFailure,
                        StringUtil.Format(HelpDisplayStrings.HelpInfoXmlValidationFailure, e.Message),
                        ErrorCategory.InvalidData, null, e);
                }
                else
                {
                    throw new UpdatableHelpSystemException("HelpContentXmlValidationFailure",
                        StringUtil.Format(HelpDisplayStrings.HelpContentXmlValidationFailure, e.Message),
                        ErrorCategory.InvalidData, null, e);
                }
            }
            return document;
        }

#else

        /// <summary>
        /// Creates a valid xml document
        /// </summary>
        /// <param name="xml">input xml</param>
        /// <param name="ns">schema namespace</param>
        /// <param name="schema">xml schema</param>
        /// <param name="handler">validation event handler</param>
        /// <param name="helpInfo">HelpInfo or HelpContent?</param>
        private XmlDocument CreateValidXmlDocument(string xml, string ns, string schema, ValidationEventHandler handler,
            bool helpInfo)
        {
            XmlReaderSettings settings = new XmlReaderSettings();

            settings.Schemas.Add(ns, new XmlTextReader(new StringReader(schema)));
            settings.ValidationType = ValidationType.Schema;

            XmlReader reader = XmlReader.Create(new StringReader(xml), settings);
            XmlDocument document = new XmlDocument();

            try
            {
                document.Load(reader);
                document.Validate(handler);
            }
            catch (XmlSchemaValidationException e)
            {
                if (helpInfo)
                {
                    throw new UpdatableHelpSystemException(HelpInfoXmlValidationFailure,
                        StringUtil.Format(HelpDisplayStrings.HelpInfoXmlValidationFailure, e.Message),
                        ErrorCategory.InvalidData, null, e);
                }
                else
                {
                    throw new UpdatableHelpSystemException("HelpContentXmlValidationFailure",
                        StringUtil.Format(HelpDisplayStrings.HelpContentXmlValidationFailure, e.Message),
                        ErrorCategory.InvalidData, null, e);
                }
            }
            return document;
        }

        /// <summary>
        /// Handles HelpInfo XML validation events
        /// </summary>
        /// <param name="sender">event sender</param>
        /// <param name="arg">event arguments</param>
        private void HelpInfoValidationHandler(object sender, ValidationEventArgs arg)
        {
            switch (arg.Severity)
            {
                case XmlSeverityType.Error:
                    {
                        throw new UpdatableHelpSystemException(HelpInfoXmlValidationFailure,
                            StringUtil.Format(HelpDisplayStrings.HelpInfoXmlValidationFailure),
                            ErrorCategory.InvalidData, null, arg.Exception);
                    }
                case XmlSeverityType.Warning:
                    break;
            }
        }

        /// <summary>
        /// Handles Help content MAML validation events
        /// </summary>
        /// <param name="sender">event sender</param>
        /// <param name="arg">event arguments</param>
        private void HelpContentValidationHandler(object sender, ValidationEventArgs arg)
        {
            switch (arg.Severity)
            {
                case XmlSeverityType.Error:
                    {
                        throw new UpdatableHelpSystemException("HelpContentXmlValidationFailure",
                            StringUtil.Format(HelpDisplayStrings.HelpContentXmlValidationFailure),
                            ErrorCategory.InvalidData, null, arg.Exception);
                    }
                case XmlSeverityType.Warning:
                    break;
            }
        }

#endif

        #endregion

        #region Help Content Retrieval

        /// <summary>
        /// Cancels all asynchronous download operations
        /// </summary>
        internal void CancelDownload()
        {
#if CORECLR
            _cancelTokenSource.Cancel();
#else
            if (WebClient.IsBusy)
            {
                WebClient.CancelAsync();
                _completed = true;
                _completionEvent.Set();
            }
#endif
            _stopping = true;
        }

        /// <summary>
        /// Downloads and installs help content
        /// </summary>
        /// <param name="commandType">command type</param>
        /// <param name="context">execution context</param>
        /// <param name="destPaths">destination paths</param>
        /// <param name="fileName">file names</param>
        /// <param name="culture">culture to update</param>
        /// <param name="helpContentUri">help content uri</param>
        /// <param name="xsdPath">path of the maml XSDs</param>
        /// <param name="installed">files installed</param>
        /// <returns>true if the operation succeeded, false if not</returns>
        internal bool DownloadAndInstallHelpContent(UpdatableHelpCommandType commandType, ExecutionContext context, Collection<string> destPaths,
            string fileName, CultureInfo culture, string helpContentUri, string xsdPath, out Collection<string> installed)
        {
            if (_stopping)
            {
                installed = new Collection<string>();
                return false;
            }

            string cache = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(Path.GetRandomFileName()));

            if (!DownloadHelpContent(commandType, cache, helpContentUri, fileName, culture.Name))
            {
                installed = new Collection<string>();
                return false;
            }

            InstallHelpContent(commandType, context, cache, destPaths, fileName, cache, culture, xsdPath, out installed);

            return true;
        }

        /// <summary>
        /// Downloads the help content
        /// </summary>
        /// <param name="commandType">command type</param>
        /// <param name="path">destination path</param>
        /// <param name="helpContentUri">help content uri</param>
        /// <param name="fileName">combined file name</param>
        /// <param name="culture">culture name</param>
        /// <returns>true if the operation succeeded, false if not</returns>
        internal bool DownloadHelpContent(UpdatableHelpCommandType commandType, string path, string helpContentUri, string fileName, string culture)
        {
            if (_stopping)
            {
                return false;
            }

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            OnProgressChanged(this, new UpdatableHelpProgressEventArgs(CurrentModule, commandType, StringUtil.Format(
                HelpDisplayStrings.UpdateProgressConnecting), 0));

            string uri = helpContentUri + fileName;

#if CORECLR
            return DownloadHelpContentHttpClient(uri, Path.Combine(path, fileName), commandType);
#else
            return DownloadHelpContentWebClient(uri, Path.Combine(path, fileName), culture, commandType);
#endif
        }

#if CORECLR
        /// <summary>
        /// Downloads the help content and saves it to a directory. The goal is to achieve
        /// functional parity with WebClient.DownloadFileAsync() using CoreCLR-compatible APIs.
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="fileName"></param>
        /// <param name="commandType"></param>
        /// <returns></returns>
        private bool DownloadHelpContentHttpClient(string uri, string fileName, UpdatableHelpCommandType commandType)
        {
            // TODO: Was it intentional for them to remove IDisposable from Task?
            using (HttpClientHandler handler = new HttpClientHandler())
            {
                handler.AllowAutoRedirect = false;
                handler.UseDefaultCredentials = WebClient.UseDefaultCredentials;
                using (HttpClient client = new HttpClient(handler))
                {
                    client.Timeout = _defaultTimeout;
                    Task<HttpResponseMessage> responseMsg = client.GetAsync(new Uri(uri), _cancelTokenSource.Token);

                    // TODO: Should I use a continuation to write the stream to a file?
                    responseMsg.Wait();

                    if (_stopping)
                    {
                        return true;
                    }

                    if (!responseMsg.IsCanceled)
                    {
                        if (responseMsg.Exception != null)
                        {
                            Errors.Add(new UpdatableHelpSystemException("HelpContentNotFound",
                                StringUtil.Format(HelpDisplayStrings.HelpContentNotFound),
                                ErrorCategory.ResourceUnavailable, null, responseMsg.Exception));
                        }
                        else
                        {
                            lock (_syncObject)
                            {
                                _progressEvents.Add(new UpdatableHelpProgressEventArgs(CurrentModule, StringUtil.Format(
                                    HelpDisplayStrings.UpdateProgressDownloading), 100));
                            }

                            // Write the stream to the specified file to achieve functional parity with WebClient.DownloadFileAsync().
                            HttpResponseMessage response = responseMsg.Result;
                            if (response.IsSuccessStatusCode)
                            {
                                WriteResponseToFile(response, fileName);
                            }
                            else
                            {
                                Errors.Add(new UpdatableHelpSystemException("HelpContentNotFound",
                                    StringUtil.Format(HelpDisplayStrings.HelpContentNotFound),
                                    ErrorCategory.ResourceUnavailable, null, responseMsg.Exception));
                            }
                        }
                    }
                    SendProgressEvents(commandType);
                }
            }
            return (Errors.Count == 0);
        }

        /// <summary>
        /// Writes the content of an HTTP response to the specified file.
        /// </summary>
        /// <param name="response"></param>
        /// <param name="fileName"></param>
        private void WriteResponseToFile(HttpResponseMessage response, string fileName)
        {
            // TODO: Settings to use? FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite
            using (FileStream downloadedFileStream = new FileStream(fileName, FileMode.Create, FileAccess.Write))
            {
                Task copyStreamOp = response.Content.CopyToAsync(downloadedFileStream);
                copyStreamOp.Wait();
                if (copyStreamOp.Exception != null)
                {
                    Errors.Add(copyStreamOp.Exception);
                }
            }
        }

#else

        /// <summary>
        /// Downloads help content and saves it in the specified file.
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="fileName"></param>
        /// <param name="culture"></param>
        /// <param name="commandType"></param>
        /// <returns></returns>
        private bool DownloadHelpContentWebClient(string uri, string fileName, string culture, UpdatableHelpCommandType commandType)
        {
            WebClient.DownloadFileAsync(new Uri(uri), fileName, culture);

            OnProgressChanged(this, new UpdatableHelpProgressEventArgs(CurrentModule, commandType, StringUtil.Format(
                HelpDisplayStrings.UpdateProgressConnecting), 100));

            while (!_completed || WebClient.IsBusy)
            {
                _completionEvent.WaitOne();

                SendProgressEvents(commandType);
            }

            return (Errors.Count == 0);
        }
#endif

        private void SendProgressEvents(UpdatableHelpCommandType commandType)
        {
            // Send progress events
            lock (_syncObject)
            {
                if (_progressEvents.Count > 0)
                {
                    foreach (UpdatableHelpProgressEventArgs evnt in _progressEvents)
                    {
                        evnt.CommandType = commandType;

                        OnProgressChanged(this, evnt);
                    }

                    _progressEvents.Clear();
                }
            }
        }

        /// <summary>
        /// Installs HelpInfo.xml   
        /// </summary>
        /// <param name="moduleName"></param>
        /// <param name="moduleGuid"></param>
        /// <param name="culture">culture updated</param>
        /// <param name="version">version updated</param>
        /// <param name="contentUri">help content uri</param>
        /// <param name="destPath">destination name</param>
        /// <param name="fileName">combined file name</param>
        /// <param name="force">forces the file to copy</param>
        internal void GenerateHelpInfo(string moduleName, Guid moduleGuid, string contentUri, string culture, Version version, string destPath, string fileName, bool force)
        {
            Debug.Assert(Directory.Exists(destPath));

            if (_stopping)
            {
                return;
            }

            string destHelpInfo = Path.Combine(destPath, fileName);

            if (force)
            {
                RemoveReadOnly(destHelpInfo);
            }

            UpdatableHelpInfo oldHelpInfo = null;
            string xml = UpdatableHelpSystem.LoadStringFromPath(_cmdlet, destHelpInfo, null);

            if (xml != null)
            {
                // constructing the helpinfo object from previous update help log xml..
                // no need to resolve the uri's in this case.
                oldHelpInfo = CreateHelpInfo(xml, moduleName, moduleGuid, currentCulture: null, pathOverride: null,
                                             verbose: false, shouldResolveUri: false, ignoreValidationException: force);
            }

            using (FileStream file = new FileStream(destHelpInfo, FileMode.Create, FileAccess.Write))
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = Encoding.UTF8;
                settings.Indent = true; // Default indentation is two spaces
                using (XmlWriter writer = XmlWriter.Create(file, settings))
                {
                    writer.WriteStartDocument();
                    writer.WriteStartElement("HelpInfo", "http://schemas.microsoft.com/powershell/help/2010/05");

                    writer.WriteStartElement("HelpContentURI");
                    writer.WriteValue(contentUri);
                    writer.WriteEndElement();

                    writer.WriteStartElement("SupportedUICultures");

                    bool found = false;

                    if (oldHelpInfo != null)
                    {
                        foreach (CultureSpecificUpdatableHelp oldInfo in oldHelpInfo.UpdatableHelpItems)
                        {
                            if (oldInfo.Culture.Name.Equals(culture, StringComparison.OrdinalIgnoreCase))
                            {
                                if (oldInfo.Version.Equals(version))
                                {
                                    writer.WriteStartElement("UICulture");
                                    writer.WriteStartElement("UICultureName");
                                    writer.WriteValue(oldInfo.Culture.Name);
                                    writer.WriteEndElement();
                                    writer.WriteStartElement("UICultureVersion");
                                    writer.WriteValue(oldInfo.Version.ToString());
                                    writer.WriteEndElement();
                                    writer.WriteEndElement();
                                }
                                else
                                {
                                    writer.WriteStartElement("UICulture");
                                    writer.WriteStartElement("UICultureName");
                                    writer.WriteValue(culture);
                                    writer.WriteEndElement();
                                    writer.WriteStartElement("UICultureVersion");
                                    writer.WriteValue(version.ToString());
                                    writer.WriteEndElement();
                                    writer.WriteEndElement();
                                }

                                found = true;
                            }
                            else
                            {
                                writer.WriteStartElement("UICulture");
                                writer.WriteStartElement("UICultureName");
                                writer.WriteValue(oldInfo.Culture.Name);
                                writer.WriteEndElement();
                                writer.WriteStartElement("UICultureVersion");
                                writer.WriteValue(oldInfo.Version.ToString());
                                writer.WriteEndElement();
                                writer.WriteEndElement();
                            }
                        }
                    }

                    if (!found)
                    {
                        writer.WriteStartElement("UICulture");
                        writer.WriteStartElement("UICultureName");
                        writer.WriteValue(culture);
                        writer.WriteEndElement();
                        writer.WriteStartElement("UICultureVersion");
                        writer.WriteValue(version.ToString());
                        writer.WriteEndElement();
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }
            }
        }

        /// <summary>
        /// Removes the read only attribute
        /// </summary>
        /// <param name="path"></param>
        private void RemoveReadOnly(string path)
        {
            if (File.Exists(path))
            {
                FileAttributes attributes = File.GetAttributes(path);

                if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    attributes = (attributes & ~FileAttributes.ReadOnly);
                    File.SetAttributes(path, attributes);
                }
            }
        }

        /// <summary>
        /// Installs (unzips) the help content
        /// </summary>
        /// <param name="commandType">command type</param>
        /// <param name="context">execution context</param>
        /// <param name="sourcePath">source directory</param>
        /// <param name="destPaths">destination paths</param>
        /// <param name="fileName">help content file name</param>
        /// <param name="tempPath">temporary path</param>
        /// <param name="culture">current culture</param>
        /// <param name="xsdPath">path of the maml XSDs</param>
        /// <param name="installed">files installed</param>
        /// <remarks>
        /// Directory pointed by <paramref name="tempPath"/> (if any) will be deleted.
        /// </remarks>
        internal void InstallHelpContent(UpdatableHelpCommandType commandType, ExecutionContext context, string sourcePath,
            Collection<string> destPaths, string fileName, string tempPath, CultureInfo culture, string xsdPath,
            out Collection<string> installed)
        {
            installed = new Collection<string>();

            if (_stopping)
            {
                installed = new Collection<string>();
                return;
            }

            // These paths must exist
            if (!Directory.Exists(tempPath))
            {
                Directory.CreateDirectory(tempPath);
            }

            Debug.Assert(destPaths.Count > 0);

            try
            {
                OnProgressChanged(this, new UpdatableHelpProgressEventArgs(CurrentModule, commandType, StringUtil.Format(
                    HelpDisplayStrings.UpdateProgressInstalling), 0));

                string combinedSourcePath = Path.Combine(sourcePath, fileName);

                if (!File.Exists(combinedSourcePath))
                {
                    throw new UpdatableHelpSystemException("HelpContentNotFound", StringUtil.Format(HelpDisplayStrings.HelpContentNotFound),
                        ErrorCategory.ResourceUnavailable, null, null);
                }

                string combinedTempPath = Path.Combine(tempPath, Path.GetFileNameWithoutExtension(fileName));

                if (Directory.Exists(combinedTempPath))
                {
                    Directory.Delete(combinedTempPath, true);
                }

                bool needToCopy = true;
                UnzipHelpContent(context, combinedSourcePath, combinedTempPath, out needToCopy);
                if (needToCopy)
                {
                    ValidateAndCopyHelpContent(combinedTempPath, destPaths, culture.Name, xsdPath, out installed);
                }
            }
            finally
            {
                OnProgressChanged(this, new UpdatableHelpProgressEventArgs(CurrentModule, commandType, StringUtil.Format(
                    HelpDisplayStrings.UpdateProgressInstalling), 100));

                try
                {
                    if (Directory.Exists(tempPath))
                    {
                        Directory.Delete(tempPath);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (ArgumentException) { }
            }
        }

        /// <summary>
        /// Unzips to help content to a given location
        /// </summary>
        /// <param name="context">execution context</param>
        /// <param name="srcPath">source path</param>
        /// <param name="destPath">destination path</param>
        /// <param name="needToCopy">Is set to false if we find a single file placeholder.txt in cab. This means we no longer need to install help files</param>
        private void UnzipHelpContent(ExecutionContext context, string srcPath, string destPath, out bool needToCopy)
        {
            needToCopy = true;

            if (!Directory.Exists(destPath))
            {
                Directory.CreateDirectory(destPath);
            }

            string cabDir = Path.GetDirectoryName(srcPath);

            // Cabinet API doesn't handle the trailing back slash
            if (!cabDir.EndsWith("\\", StringComparison.Ordinal))
            {
                cabDir += "\\";
            }

            if (!destPath.EndsWith("\\", StringComparison.Ordinal))
            {
                destPath += "\\";
            }

            if (!CabinetExtractorFactory.GetCabinetExtractor().Extract(Path.GetFileName(srcPath), cabDir, destPath))
            {
                throw new UpdatableHelpSystemException("UnableToExtract", StringUtil.Format(HelpDisplayStrings.UnzipFailure),
                    ErrorCategory.InvalidOperation, null, null);
            }

            string[] files = Directory.GetFiles(destPath);
            if (files.Length == 1)
            {
                // If there is a single file
                string file = Path.GetFileName(files[0]);
                if (!string.IsNullOrEmpty(file) && file.Equals("placeholder.txt", StringComparison.OrdinalIgnoreCase))
                {
                    // And that single file is named "placeholder.txt"
                    var fileInfo = new FileInfo(files[0]);
                    if (fileInfo.Length == 0)
                    {
                        // And if that single file has length 0, then we delete that file and no longer install help
                        needToCopy = false;
                        try
                        {
                            File.Delete(files[0]);
                            string directory = Path.GetDirectoryName(files[0]);
                            if (!string.IsNullOrEmpty(directory))
                            {
                                Directory.Delete(directory);
                            }
                        }
                        catch (FileNotFoundException)
                        { }
                        catch (DirectoryNotFoundException)
                        { }
                        catch (UnauthorizedAccessException)
                        { }
                        catch (System.Security.SecurityException)
                        { }
                        catch (ArgumentNullException)
                        { }
                        catch (ArgumentException)
                        { }
                        catch (PathTooLongException)
                        { }
                        catch (NotSupportedException)
                        { }
                        catch (IOException)
                        { }
                    }
                }
            }
            else
            {
                foreach (string file in files)
                {
                    if (File.Exists(file))
                    {
                        FileInfo fInfo = new FileInfo(file);
                        if ((fInfo.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                        {
                            // Clear the read-only attribute
                            fInfo.Attributes &= ~(FileAttributes.ReadOnly);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Validates all XML files within a given path
        /// </summary>
        /// <param name="sourcePath">path containing files to validate</param>
        /// <param name="destPaths">destination paths</param>
        /// <param name="culture">culture name</param>
        /// <param name="xsdPath">path of the maml XSDs</param>
        /// <param name="installed">installed files</param>
        private void ValidateAndCopyHelpContent(string sourcePath, Collection<string> destPaths, string culture, string xsdPath,
            out Collection<string> installed)
        {
            installed = new Collection<string>();

#if CORECLR // TODO:CORECLR Disabling this because XML Schemas are not supported for CoreCLR
            string xsd = "Remove this when adding schema support";
#else
            string xsd = LoadStringFromPath(_cmdlet, xsdPath, null);
#endif

            // We only accept txt files and xml files
            foreach (string file in Directory.GetFiles(sourcePath))
            {
                if (!String.Equals(Path.GetExtension(file), ".xml", StringComparison.OrdinalIgnoreCase)
                    && !String.Equals(Path.GetExtension(file), ".txt", StringComparison.OrdinalIgnoreCase))
                {
                    throw new UpdatableHelpSystemException("HelpContentContainsInvalidFiles",
                        StringUtil.Format(HelpDisplayStrings.HelpContentContainsInvalidFiles), ErrorCategory.InvalidData,
                        null, null);
                }
            }

            // xml validation
            foreach (string file in Directory.GetFiles(sourcePath))
            {
                if (String.Equals(Path.GetExtension(file), ".xml", StringComparison.OrdinalIgnoreCase))
                {
                    if (xsd == null)
                    {
                        throw new ItemNotFoundException(StringUtil.Format(HelpDisplayStrings.HelpContentXsdNotFound, xsdPath));
                    }
                    else
                    {
                        string xml = LoadStringFromPath(_cmdlet, file, null);

                        XmlReader documentReader = XmlReader.Create(new StringReader(xml));
                        XmlDocument contentDocument = new XmlDocument();

                        contentDocument.Load(documentReader);

                        if (contentDocument.ChildNodes.Count != 1 && contentDocument.ChildNodes.Count != 2)
                        {
                            throw new UpdatableHelpSystemException("HelpContentXmlValidationFailure",
                                StringUtil.Format(HelpDisplayStrings.HelpContentXmlValidationFailure, HelpDisplayStrings.RootElementMustBeHelpItems),
                                ErrorCategory.InvalidData, null, null);
                        }

                        XmlNode helpItemsNode = null;

                        if (contentDocument.DocumentElement != null &&
                            contentDocument.DocumentElement.LocalName.Equals("providerHelp", StringComparison.OrdinalIgnoreCase))
                        {
                            helpItemsNode = contentDocument;
                        }
                        else
                        {
                            if (contentDocument.ChildNodes.Count == 1)
                            {
                                if (!contentDocument.ChildNodes[0].LocalName.Equals("helpItems", StringComparison.OrdinalIgnoreCase))
                                {
                                    throw new UpdatableHelpSystemException("HelpContentXmlValidationFailure",
                                        StringUtil.Format(HelpDisplayStrings.HelpContentXmlValidationFailure, HelpDisplayStrings.RootElementMustBeHelpItems),
                                        ErrorCategory.InvalidData, null, null);
                                }
                                else
                                {
                                    helpItemsNode = contentDocument.ChildNodes[0];
                                }
                            }
                            else if (contentDocument.ChildNodes.Count == 2)
                            {
                                if (!contentDocument.ChildNodes[1].LocalName.Equals("helpItems", StringComparison.OrdinalIgnoreCase))
                                {
                                    throw new UpdatableHelpSystemException("HelpContentXmlValidationFailure",
                                        StringUtil.Format(HelpDisplayStrings.HelpContentXmlValidationFailure, HelpDisplayStrings.RootElementMustBeHelpItems),
                                        ErrorCategory.InvalidData, null, null);
                                }
                                else
                                {
                                    helpItemsNode = contentDocument.ChildNodes[1];
                                }
                            }
                        }

                        Debug.Assert(helpItemsNode != null, "helpItemsNode must not be null");

                        string targetNamespace = "http://schemas.microsoft.com/maml/2004/10";

                        foreach (XmlNode node in helpItemsNode.ChildNodes)
                        {
                            if (node.NodeType == XmlNodeType.Element)
                            {
                                if (!node.LocalName.Equals("providerHelp", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (node.LocalName.Equals("para", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (!node.NamespaceURI.Equals("http://schemas.microsoft.com/maml/2004/10", StringComparison.OrdinalIgnoreCase))
                                        {
                                            throw new UpdatableHelpSystemException("HelpContentXmlValidationFailure",
                                                StringUtil.Format(HelpDisplayStrings.HelpContentXmlValidationFailure,
                                                StringUtil.Format(HelpDisplayStrings.HelpContentMustBeInTargetNamespace, targetNamespace)), ErrorCategory.InvalidData, null, null);
                                        }
                                        else
                                        {
                                            continue;
                                        }
                                    }

                                    if (!node.NamespaceURI.Equals("http://schemas.microsoft.com/maml/dev/command/2004/10", StringComparison.OrdinalIgnoreCase) &&
                                        !node.NamespaceURI.Equals("http://schemas.microsoft.com/maml/dev/dscResource/2004/10", StringComparison.OrdinalIgnoreCase))
                                    {
                                        throw new UpdatableHelpSystemException("HelpContentXmlValidationFailure",
                                            StringUtil.Format(HelpDisplayStrings.HelpContentXmlValidationFailure,
                                            StringUtil.Format(HelpDisplayStrings.HelpContentMustBeInTargetNamespace, targetNamespace)), ErrorCategory.InvalidData, null, null);
                                    }
                                }

                                CreateValidXmlDocument(node.OuterXml, targetNamespace, xsd,
#if !CORECLR
                                    new ValidationEventHandler(HelpContentValidationHandler),
#endif
                                    false);
                            }
                        }
                    }
                }
                else if (String.Equals(Path.GetExtension(file), ".txt", StringComparison.OrdinalIgnoreCase))
                {
                    FileStream fileStream = new FileStream(file, FileMode.Open, FileAccess.Read);

                    if (fileStream.Length > 2)
                    {
                        byte[] firstTwoBytes = new byte[2];

                        fileStream.Read(firstTwoBytes, 0, 2);

                        // Check for Mark Zbikowski's magic initials
                        if (firstTwoBytes[0] == 'M' && firstTwoBytes[1] == 'Z')
                        {
                            throw new UpdatableHelpSystemException("HelpContentContainsInvalidFiles",
                                StringUtil.Format(HelpDisplayStrings.HelpContentContainsInvalidFiles), ErrorCategory.InvalidData,
                                null, null);
                        }
                    }
                }

                foreach (string path in destPaths)
                {
                    Debug.Assert(Directory.Exists(path));

                    string combinedPath = Path.Combine(path, culture);

                    if (!Directory.Exists(combinedPath))
                    {
                        Directory.CreateDirectory(combinedPath);
                    }

                    string destPath = Path.Combine(combinedPath, Path.GetFileName(file));

                    // Make the destpath writeable if force is used
                    FileAttributes? originalFileAttributes = null;
                    try
                    {
                        if (File.Exists(destPath) && (_cmdlet.Force))
                        {
                            FileInfo fInfo = new FileInfo(destPath);
                            if ((fInfo.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                            {
                                // remember to reset the read-only attribute later
                                originalFileAttributes = fInfo.Attributes;
                                // Clear the read-only attribute
                                fInfo.Attributes &= ~(FileAttributes.ReadOnly);
                            }
                        }

                        File.Copy(file, destPath, true);
                    }
                    finally
                    {
                        if (originalFileAttributes.HasValue)
                        {
                            File.SetAttributes(destPath, originalFileAttributes.Value);
                        }
                    }

                    installed.Add(destPath);
                }
            }
        }

        /// <summary>
        /// Loads string from the given path
        /// </summary>
        /// <param name="cmdlet">cmdlet instance</param>
        /// <param name="path">path to load</param>
        /// <param name="credential">credential</param>
        /// <returns>string loaded</returns>
        internal static string LoadStringFromPath(PSCmdlet cmdlet, string path, PSCredential credential)
        {
            Debug.Assert(path != null);

            if (credential != null)
            {
                // New PSDrive

                using (UpdatableHelpSystemDrive drive = new UpdatableHelpSystemDrive(cmdlet, Path.GetDirectoryName(path), credential))
                {
                    string tempPath = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(Path.GetTempFileName()));

                    if (!cmdlet.InvokeProvider.Item.Exists(Path.Combine(drive.DriveName, Path.GetFileName(path))))
                    {
                        return null;
                    }

                    cmdlet.InvokeProvider.Item.Copy(new string[1] { Path.Combine(drive.DriveName, Path.GetFileName(path)) }, tempPath,
                        false, CopyContainers.CopyTargetContainer, true, true);

                    path = tempPath;
                }
            }

            if (File.Exists(path))
            {
                using (FileStream currentHelpInfoFile = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    StreamReader reader = new StreamReader(currentHelpInfoFile);

                    return reader.ReadToEnd();
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the default source path from GP
        /// </summary>
        /// <returns></returns>
        internal string GetDefaultSourcePath()
        {
            try
            {
                return ConfigPropertyAccessor.Instance.GetDefaultSourcePath();
            }
            catch (SecurityException)
            {
                return null;
            }
        }

        /// <summary>
        /// Sets the DisablePromptToUpdatableHelp regkey
        /// </summary>
        internal static void SetDisablePromptToUpdateHelp()
        {
            try
            {
                ConfigPropertyAccessor.Instance.SetDisablePromptToUpdateHelp(true);
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore AccessDenied related exceptions
            }
            catch (SecurityException)
            {
                // Ignore AccessDenied related exceptions
            }
        }

        /// <summary>
        /// Checks if it is necessary to prompt to update help
        /// </summary>
        /// <returns></returns>
        internal static bool ShouldPromptToUpdateHelp()
        {
#if UNIX
            // TODO: This workaround needs to be removed once updatable help
            //       works on Linux.
            return false;
#else
            try
            {
                if (!Utils.IsAdministrator())
                {
                    return false;
                }

                return ConfigPropertyAccessor.Instance.GetDisablePromptToUpdateHelp();
            }
            catch (SecurityException)
            {
                return false;
            }
#endif
        }

        #endregion

        #region Events

        internal event EventHandler<UpdatableHelpProgressEventArgs> OnProgressChanged;

#if !CORECLR

        /// <summary>
        /// Handles the download completion event
        /// </summary>
        /// <param name="sender">event sender</param>
        /// <param name="e">event arguments</param>
        private void HandleDownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        {
            if (_stopping)
            {
                return;
            }

            if (!e.Cancelled)
            {
                if (e.Error != null)
                {
                    if (e.Error is WebException)
                    {
                        Errors.Add(new UpdatableHelpSystemException("HelpContentNotFound", StringUtil.Format(HelpDisplayStrings.HelpContentNotFound, e.UserState.ToString()),
                            ErrorCategory.ResourceUnavailable, null, null));
                    }
                    else
                    {
                        Errors.Add(e.Error);
                    }
                }
                else
                {
                    lock (_syncObject)
                    {
                        _progressEvents.Add(new UpdatableHelpProgressEventArgs(CurrentModule, StringUtil.Format(
                            HelpDisplayStrings.UpdateProgressDownloading), 100));
                    }
                }

                _completed = true;
                _completionEvent.Set();
            }
        }

        /// <summary>
        /// Handles the download progress changed event
        /// </summary>
        /// <param name="sender">event sender</param>
        /// <param name="e">event arguments</param>
        private void HandleDownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            if (_stopping)
            {
                return;
            }

            lock (_syncObject)
            {
                _progressEvents.Add(new UpdatableHelpProgressEventArgs(CurrentModule, StringUtil.Format(
                        HelpDisplayStrings.UpdateProgressDownloading), e.ProgressPercentage));
            }

            _completionEvent.Set();
        }
#endif

        #endregion
    }

    /// <summary>
    /// Controls the updatable help system drive
    /// </summary>
    internal class UpdatableHelpSystemDrive : IDisposable
    {
        private string _driveName;
        private PSCmdlet _cmdlet;

        /// <summary>
        /// Gets the drive name
        /// </summary>
        internal string DriveName
        {
            get
            {
                return _driveName + ":\\";
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="cmdlet"></param>
        /// <param name="path"></param>
        /// <param name="credential"></param>
        internal UpdatableHelpSystemDrive(PSCmdlet cmdlet, string path, PSCredential credential)
        {
            for (int i = 0; i < 6; i++)
            {
                _driveName = Path.GetFileNameWithoutExtension(Path.GetRandomFileName());
                _cmdlet = cmdlet;

                // Need to get rid of the trailing \, otherwise New-PSDrive will not work...
                if (path.EndsWith("\\", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/", StringComparison.OrdinalIgnoreCase))
                {
                    path = path.Remove(path.Length - 1);
                }

                PSDriveInfo mappedDrive = cmdlet.SessionState.Drive.GetAtScope(_driveName, "local");

                if (mappedDrive != null)
                {
                    if (mappedDrive.Root.Equals(path))
                    {
                        return;
                    }

                    // Remove the drive after 5 tries
                    if (i < 5)
                    {
                        continue;
                    }

                    cmdlet.SessionState.Drive.Remove(_driveName, true, "local");
                }

                mappedDrive = new PSDriveInfo(_driveName, cmdlet.SessionState.Internal.GetSingleProvider("FileSystem"),
                    path, String.Empty, credential);

                cmdlet.SessionState.Drive.New(mappedDrive, "local");

                break;
            }
        }

        /// <summary>
        /// Disposes the class
        /// </summary>
        public void Dispose()
        {
            PSDriveInfo mappedDrive = _cmdlet.SessionState.Drive.GetAtScope(_driveName, "local");

            if (mappedDrive != null)
            {
                _cmdlet.SessionState.Drive.Remove(_driveName, true, "local");
            }

            GC.SuppressFinalize(this);
        }
    }
}
