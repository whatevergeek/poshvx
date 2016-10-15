/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Collections.Generic;
using System.Xml;
using System.Management.Automation;
using System.Management.Automation.Internal;
using System.Management.Automation.Host;
using System.Globalization;
using System.Text;

#if CORECLR
// Use stubs for SystemException
using Microsoft.PowerShell.CoreClr.Stubs;
#endif

/*
 SUMMARY: this file contains a general purpose, reusable framework for 
    loading XML files, and do data validation. 
    It provides the capability of:
    * logging errors, warnings and traces to a file or in memory
    * managing the XML dom traversal using an add hoc stack frame management scheme
    * validating common error conditions (e.g. missing node or unknown node)
*/

namespace Microsoft.PowerShell.Commands.Internal.Format
{
    /// <summary>
    /// base exception to be used for all the exceptions that this framework will generate
    /// </summary>
    internal abstract class TypeInfoDataBaseLoaderException : SystemException
    {
    }

    /// <summary>
    /// exception thrown by the loader when the maximum number of errors is exceeded
    /// </summary>
    internal class TooManyErrorsException : TypeInfoDataBaseLoaderException
    {
        /// <summary>
        /// error count that triggered the exception
        /// </summary>
        internal int errorCount;
    }


    /// <summary>
    /// entry logged by the loader and made available to external consumers
    /// </summary>
    internal class XmlLoaderLoggerEntry
    {
        internal enum EntryType { Error, Trace };

        /// <summary>
        /// type of information being logged
        /// </summary>
        internal EntryType entryType;

        /// <summary>
        /// path of the file the info refers to
        /// </summary>
        internal string filePath = null;

        /// <summary>
        /// XPath location inside the file
        /// </summary>
        internal string xPath = null;

        /// <summary>
        /// message to be displayed to the user
        /// </summary>
        internal string message = null;

        /// <summary>
        /// indicate whether we fail to load the file due to the security reason
        /// </summary>
        internal bool failToLoadFile = false;
    }


    /// <summary>
    /// logger object used by the loader (class XmlLoaderBase) to write log entries.
    /// It logs to a memory buffer and (optionally) to a text file
    /// </summary>
    internal class XmlLoaderLogger : IDisposable
    {
        #region tracer
        //PSS/end-user tracer 
        [TraceSource("FormatFileLoading", "Loading format files")]
        private static PSTraceSource s_formatFileLoadingtracer = PSTraceSource.GetTracer("FormatFileLoading", "Loading format files", false);

        #endregion tracer
        /// <summary>
        /// log an entry
        /// </summary>
        /// <param name="entry">entry to log</param>
        internal void LogEntry(XmlLoaderLoggerEntry entry)
        {
            if (entry.entryType == XmlLoaderLoggerEntry.EntryType.Error)
                _hasErrors = true;

            if (_saveInMemory)
                _entries.Add(entry);

            if ((s_formatFileLoadingtracer.Options | PSTraceSourceOptions.WriteLine) != 0)
                WriteToTracer(entry);
        }

        private void WriteToTracer(XmlLoaderLoggerEntry entry)
        {
            if (entry.entryType == XmlLoaderLoggerEntry.EntryType.Error)
            {
                s_formatFileLoadingtracer.WriteLine("ERROR:\r\n FilePath: {0}\r\n XPath: {1}\r\n Message = {2}", entry.filePath, entry.xPath, entry.message);
            }
            else if (entry.entryType == XmlLoaderLoggerEntry.EntryType.Trace)
            {
                s_formatFileLoadingtracer.WriteLine("TRACE:\r\n FilePath: {0}\r\n XPath: {1}\r\n Message = {2}", entry.filePath, entry.xPath, entry.message);
            }
        }

        /// <summary>
        /// IDisposable implementation
        /// </summary>
        /// <remarks>This method calls GC.SuppressFinalize</remarks>
        public void Dispose()
        {
            Dispose(true);

            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
            }
        }

        internal List<XmlLoaderLoggerEntry> LogEntries
        {
            get
            {
                return _entries;
            }
        }

        internal bool HasErrors
        {
            get
            {
                return _hasErrors;
            }
        }

        /// <summary>
        /// if true, log entries to memory
        /// </summary>
        private bool _saveInMemory = true;

        /// <summary>
        /// list of entries logged if saveInMemory is true
        /// </summary>
        private List<XmlLoaderLoggerEntry> _entries = new List<XmlLoaderLoggerEntry>();

        /// <summary>
        /// true if we ever logged an error
        /// </summary>
        private bool _hasErrors = false;
    }


    /// <summary>
    /// base class providing XML loading basic functionality (stack management and logging facilities)
    /// NOTE: you need to implement to load an actual XML document and traverse it as see fit
    /// </summary>
    internal abstract class XmlLoaderBase : IDisposable
    {
        #region tracer
        [TraceSource("XmlLoaderBase", "XmlLoaderBase")]
        private static PSTraceSource s_tracer = PSTraceSource.GetTracer("XmlLoaderBase", "XmlLoaderBase");
        #endregion tracer

        /// <summary>
        /// class representing a stack frame for the XML document tree traversal
        /// </summary>
        private sealed class XmlLoaderStackFrame : IDisposable
        {
            internal XmlLoaderStackFrame(XmlLoaderBase loader, XmlNode n, int index)
            {
                _loader = loader;
                this.node = n;
                this.index = index;
            }

            /// <summary>
            /// IDisposable implementation
            /// </summary>
            public void Dispose()
            {
                if (_loader != null)
                {
                    _loader.RemoveStackFrame();
                    _loader = null;
                }
            }

            /// <summary>
            ///  back pointer to the loader, used to pop a stack frame
            /// </summary>
            private XmlLoaderBase _loader;

            /// <summary>
            /// node the stack frame refers to
            /// </summary>
            internal XmlNode node;

            /// <summary>
            /// node index for enumerations, valid only if != -1
            /// NOTE: this allows to express the XPath construct "foo[0]"
            /// </summary>
            internal int index = -1;
        }

        /// <summary>
        /// IDisposable implementation
        /// </summary>
        /// <remarks>This method calls GC.SuppressFinalize</remarks>
        public void Dispose()
        {
            Dispose(true);

            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_logger != null)
                {
                    _logger.Dispose();
                    _logger = null;
                }
            }
        }

        /// <summary>
        /// get the list of log entries
        /// </summary>
        /// <value>list of entries logged during a load</value>
        internal List<XmlLoaderLoggerEntry> LogEntries
        {
            get
            {
                return _logger.LogEntries;
            }
        }

        /// <summary>
        /// check if there were errors
        /// </summary>
        /// <value>true of the log entry list has errors</value>
        internal bool HasErrors
        {
            get
            {
                return _logger.HasErrors;
            }
        }

        /// <summary>
        /// to be called when starting a stack frame.
        /// The returned IDisposable should be used in a using(){...} block
        /// </summary>
        /// <param name="n">node to push on the stack</param>
        /// <returns>object to dispose when exiting the frame</returns>
        protected IDisposable StackFrame(XmlNode n)
        {
            return StackFrame(n, -1);
        }

        /// <summary>
        /// to be called when starting a stack frame.
        /// The returned IDisposable should be used in a using(){...} block
        /// </summary>
        /// <param name="n">node to push on the stack</param>
        /// <param name="index">index of the node of the same name in a collection</param>
        /// <returns>object to dispose when exiting the frame</returns>
        protected IDisposable StackFrame(XmlNode n, int index)
        {
            XmlLoaderStackFrame sf = new XmlLoaderStackFrame(this, n, index);

            _executionStack.Push(sf);
            if (_logStackActivity)
                WriteStackLocation("Enter");
            return sf;
        }

        /// <summary>
        /// called by the Dispose code of the XmlLoaderStackFrame object
        /// to pop a frame off the stack
        /// </summary>
        private void RemoveStackFrame()
        {
            if (_logStackActivity)
                WriteStackLocation("Exit");
            _executionStack.Pop();
        }

        protected void ProcessUnknownNode(XmlNode n)
        {
            if (IsFilteredOutNode(n))
                return;

            ReportIllegalXmlNode(n);
        }

        protected void ProcessUnknownAttribute(XmlAttribute a)
        {
            ReportIllegalXmlAttribute(a);
        }

        protected static bool IsFilteredOutNode(XmlNode n)
        {
            return (n is XmlComment || n is XmlWhitespace);
        }

        protected bool VerifyNodeHasNoChildren(XmlNode n)
        {
            if (n.ChildNodes.Count == 0)
                return true;

            if (n.ChildNodes.Count == 1)
            {
                if (n.ChildNodes[0] is XmlText)
                    return true;
            }
            //Error at XPath {0} in file {1}: Node {2} cannot have children.
            this.ReportError(StringUtil.Format(FormatAndOutXmlLoadingStrings.NoChildrenAllowed, ComputeCurrentXPath(), FilePath, n.Name));
            return false;
        }

        internal string GetMandatoryInnerText(XmlNode n)
        {
            if (String.IsNullOrEmpty(n.InnerText))
            {
                this.ReportEmptyNode(n);
                return null;
            }

            return n.InnerText;
        }

        internal string GetMandatoryAttributeValue(XmlAttribute a)
        {
            if (String.IsNullOrEmpty(a.Value))
            {
                this.ReportEmptyAttribute(a);
                return null;
            }

            return a.Value;
        }

        /// <summary>
        /// helper to compare node names, e.g. "foo" in <foo/>
        /// it uses case sensitive, culture invariant compare.
        /// This is because XML tags are case sensitive.
        /// </summary>
        /// <param name="n">XmlNode whose name is to compare</param>
        /// <param name="s">string to compare the node name to</param>
        /// <param name="allowAttributes">if true, accept the presence of attributes on the node</param>
        /// <returns>true if there is a match</returns>
        private bool MatchNodeNameHelper(XmlNode n, string s, bool allowAttributes)
        {
            bool match = false;
            if (String.Equals(n.Name, s, StringComparison.Ordinal))
            {
                // we have a case sensitive match
                match = true;
            }
            else if (String.Equals(n.Name, s, StringComparison.OrdinalIgnoreCase))
            {
                // try a case insensitive match
                // we differ only in case: flag this as an ERROR for the time being
                // and accept the comparison

                string fmtString = "XML tag differ in case only {0} {1}";
                ReportTrace(string.Format(CultureInfo.InvariantCulture, fmtString, n.Name, s));

                match = true;
            }

            if (match && !allowAttributes)
            {
                XmlElement e = n as XmlElement;
                if (e != null && e.Attributes.Count > 0)
                {
                    //Error at XPath {0} in file {1}: The XML Element {2} does not allow attributes.
                    ReportError(StringUtil.Format(FormatAndOutXmlLoadingStrings.AttributesNotAllowed, ComputeCurrentXPath(), FilePath, n.Name));
                }
            }

            return match;
        }

        internal bool MatchNodeNameWithAttributes(XmlNode n, string s)
        {
            return MatchNodeNameHelper(n, s, true);
        }

        internal bool MatchNodeName(XmlNode n, string s)
        {
            return MatchNodeNameHelper(n, s, false);
        }

        internal bool MatchAttributeName(XmlAttribute a, string s)
        {
            if (String.Equals(a.Name, s, StringComparison.Ordinal))
            {
                // we have a case sensitive match
                return true;
            }
            else if (String.Equals(a.Name, s, StringComparison.OrdinalIgnoreCase))
            {
                // try a case insensitive match
                // we differ only in case: flag this as an ERROR for the time being
                // and accept the comparison

                string fmtString = "XML attribute differ in case only {0} {1}";
                ReportTrace(string.Format(CultureInfo.InvariantCulture, fmtString, a.Name, s));
                return true;
            }
            return false;
        }

        internal void ProcessDuplicateNode(XmlNode n)
        {
            //Error at XPath {0} in file {1}: Duplicated node.
            ReportLogEntryHelper(StringUtil.Format(FormatAndOutXmlLoadingStrings.DuplicatedNode, ComputeCurrentXPath(), FilePath), XmlLoaderLoggerEntry.EntryType.Error);
        }

        internal void ProcessDuplicateAlternateNode(string node1, string node2)
        {
            //Error at XPath {0} in file {1}: {2} and {3} are mutually exclusive.
            ReportLogEntryHelper(StringUtil.Format(FormatAndOutXmlLoadingStrings.MutuallyExclusiveNode, ComputeCurrentXPath(), FilePath, node1, node2), XmlLoaderLoggerEntry.EntryType.Error);
        }

        internal void ProcessDuplicateAlternateNode(XmlNode n, string node1, string node2)
        {
            //Error at XPath {0} in file {1}: {2}, {3} and {4} are mutually exclusive.
            ReportLogEntryHelper(StringUtil.Format(FormatAndOutXmlLoadingStrings.ThreeMutuallyExclusiveNode, ComputeCurrentXPath(), FilePath, n.Name, node1, node2), XmlLoaderLoggerEntry.EntryType.Error);
        }

        private void ReportIllegalXmlNode(XmlNode n)
        {
            //UnknownNode=Error at XPath {0} in file {1}: {2} is an unknown node.
            ReportLogEntryHelper(StringUtil.Format(FormatAndOutXmlLoadingStrings.UnknownNode, ComputeCurrentXPath(), FilePath, n.Name), XmlLoaderLoggerEntry.EntryType.Error);
        }

        private void ReportIllegalXmlAttribute(XmlAttribute a)
        {
            //Error at XPath {0} in file {1}: {2} is an unknown attribute.
            ReportLogEntryHelper(StringUtil.Format(FormatAndOutXmlLoadingStrings.UnknownAttribute, ComputeCurrentXPath(), FilePath, a.Name), XmlLoaderLoggerEntry.EntryType.Error);
        }

        protected void ReportMissingAttribute(string name)
        {
            //Error at XPath {0} in file {1}: {2} is a missing attribute.
            ReportLogEntryHelper(StringUtil.Format(FormatAndOutXmlLoadingStrings.MissingAttribute, ComputeCurrentXPath(), FilePath, name), XmlLoaderLoggerEntry.EntryType.Error);
        }

        protected void ReportMissingNode(string name)
        {
            //Error at XPath {0} in file {1}: Missing Node {2}.
            ReportLogEntryHelper(StringUtil.Format(FormatAndOutXmlLoadingStrings.MissingNode, ComputeCurrentXPath(), FilePath, name), XmlLoaderLoggerEntry.EntryType.Error);
        }

        protected void ReportMissingNodes(string[] names)
        {
            //Error at XPath {0} in file {1}: Missing Node from {2}.
            string namesString = string.Join(", ", names);
            ReportLogEntryHelper(StringUtil.Format(FormatAndOutXmlLoadingStrings.MissingNodeFromList, ComputeCurrentXPath(), FilePath, namesString), XmlLoaderLoggerEntry.EntryType.Error);
        }

        protected void ReportEmptyNode(XmlNode n)
        {
            //Error at XPath {0} in file {1}: {2} is an empty node.
            ReportLogEntryHelper(StringUtil.Format(FormatAndOutXmlLoadingStrings.EmptyNode, ComputeCurrentXPath(), FilePath, n.Name), XmlLoaderLoggerEntry.EntryType.Error);
        }

        protected void ReportEmptyAttribute(XmlAttribute a)
        {
            //EmptyAttribute=Error at XPath {0} in file {1}: {2} is an empty attribute.
            ReportLogEntryHelper(StringUtil.Format(FormatAndOutXmlLoadingStrings.EmptyAttribute, ComputeCurrentXPath(), FilePath, a.Name), XmlLoaderLoggerEntry.EntryType.Error);
        }

        /// <summary>
        /// For tracing purposes only, don't add to log
        /// </summary>
        /// <param name="message">
        /// trace message, non-localized string is OK.
        /// </param>
        protected void ReportTrace(string message)
        {
            ReportLogEntryHelper(message, XmlLoaderLoggerEntry.EntryType.Trace);
        }

        protected void ReportError(string message)
        {
            ReportLogEntryHelper(message, XmlLoaderLoggerEntry.EntryType.Error);
        }

        private void ReportLogEntryHelper(string message, XmlLoaderLoggerEntry.EntryType entryType, bool failToLoadFile = false)
        {
            string currentPath = ComputeCurrentXPath();
            XmlLoaderLoggerEntry entry = new XmlLoaderLoggerEntry();

            entry.entryType = entryType;
            entry.filePath = this.FilePath;
            entry.xPath = currentPath;
            entry.message = message;

            if (failToLoadFile)
            {
                System.Management.Automation.Diagnostics.Assert(entryType == XmlLoaderLoggerEntry.EntryType.Error, "the entry type should be 'error' when a file cannot be loaded");
                entry.failToLoadFile = true;
            }

            _logger.LogEntry(entry);

            if (entryType == XmlLoaderLoggerEntry.EntryType.Error)
            {
                _currentErrorCount++;
                if (_currentErrorCount >= _maxNumberOfErrors)
                {
                    // we have to log a last error and then bail
                    if (_maxNumberOfErrors > 1)
                    {
                        XmlLoaderLoggerEntry lastEntry = new XmlLoaderLoggerEntry();

                        lastEntry.entryType = XmlLoaderLoggerEntry.EntryType.Error;
                        lastEntry.filePath = this.FilePath;
                        lastEntry.xPath = currentPath;
                        lastEntry.message = StringUtil.Format(FormatAndOutXmlLoadingStrings.TooManyErrors, FilePath);
                        _logger.LogEntry(lastEntry);
                        _currentErrorCount++;
                    }

                    // NOTE: this exception is an internal one, and it is caught
                    // internally by the calling code.
                    TooManyErrorsException e = new TooManyErrorsException();

                    e.errorCount = _currentErrorCount;
                    throw e;
                }
            }
        }

        /// <summary>
        /// Report error when loading formatting data from object model
        /// </summary>
        /// <param name="message"></param>
        /// <param name="typeName"></param>
        protected void ReportErrorForLoadingFromObjectModel(string message, string typeName)
        {
            XmlLoaderLoggerEntry entry = new XmlLoaderLoggerEntry();

            entry.entryType = XmlLoaderLoggerEntry.EntryType.Error;
            entry.message = message;
            _logger.LogEntry(entry);

            _currentErrorCount++;
            if (_currentErrorCount >= _maxNumberOfErrors)
            {
                // we have to log a last error and then bail
                if (_maxNumberOfErrors > 1)
                {
                    XmlLoaderLoggerEntry lastEntry = new XmlLoaderLoggerEntry();

                    lastEntry.entryType = XmlLoaderLoggerEntry.EntryType.Error;
                    lastEntry.message = StringUtil.Format(FormatAndOutXmlLoadingStrings.TooManyErrorsInFormattingData, typeName);
                    _logger.LogEntry(lastEntry);
                    _currentErrorCount++;
                }

                // NOTE: this exception is an internal one, and it is caught
                // internally by the calling code.
                TooManyErrorsException e = new TooManyErrorsException();

                e.errorCount = _currentErrorCount;
                throw e;
            }
        }

        private void WriteStackLocation(string label)
        {
            ReportTrace(label);
        }

        protected string ComputeCurrentXPath()
        {
            StringBuilder path = new StringBuilder();
            foreach (XmlLoaderStackFrame sf in _executionStack)
            {
                path.Insert(0, "/");
                if (sf.index != -1)
                {
                    path.Insert(1, string.Format(CultureInfo.InvariantCulture,
                        "{0}[{1}]", sf.node.Name, sf.index + 1));
                }
                else
                {
                    path.Insert(1, sf.node.Name);
                }
            }
            return path.Length > 0 ? path.ToString() : null;
        }

        #region helpers for loading XML documents

        protected XmlDocument LoadXmlDocumentFromFileLoadingInfo(AuthorizationManager authorizationManager, PSHost host, out bool isFullyTrusted)
        {
            // get file contents
            ExternalScriptInfo ps1xmlInfo = new ExternalScriptInfo(FilePath, FilePath);
            string fileContents = ps1xmlInfo.ScriptContents;

            isFullyTrusted = false;
            if (ps1xmlInfo.DefiningLanguageMode == PSLanguageMode.FullLanguage)
            {
                isFullyTrusted = true;
            }

            if (authorizationManager != null)
            {
                try
                {
                    authorizationManager.ShouldRunInternal(ps1xmlInfo, CommandOrigin.Internal, host);
                }
                catch (PSSecurityException reason)
                {
                    string errorMessage = StringUtil.Format(TypesXmlStrings.ValidationException,
                        string.Empty /* TODO/FIXME snapin */,
                        FilePath,
                        reason.Message);
                    ReportLogEntryHelper(errorMessage, XmlLoaderLoggerEntry.EntryType.Error, failToLoadFile: true);
                    return null;
                }
            }

            // load file into XML document
            try
            {
                XmlDocument doc = InternalDeserializer.LoadUnsafeXmlDocument(
                    fileContents,
                    true, /* preserve whitespace, comments, etc. */
                    null); /* default maxCharacters */
                this.ReportTrace("XmlDocument loaded OK");
                return doc;
            }
            catch (XmlException e)
            {
                this.ReportError(StringUtil.Format(FormatAndOutXmlLoadingStrings.ErrorInFile, FilePath, e.Message));
                this.ReportTrace("XmlDocument discarded");
                return null;
            }
            catch (Exception e) // will rethrow
            {
                CommandProcessor.CheckForSevereException(e);
                throw;
            }
        }

        #endregion

        /// <summary>
        /// file system path for the file we are loading from
        /// </summary>
        /// 
        protected string FilePath
        {
            get
            {
                return _loadingInfo.filePath;
            }
        }


        protected void SetDatabaseLoadingInfo(XmlFileLoadInfo info)
        {
            _loadingInfo.fileDirectory = info.fileDirectory;
            _loadingInfo.filePath = info.filePath;
        }
        protected void SetLoadingInfoIsFullyTrusted(bool isFullyTrusted)
        {
            _loadingInfo.isFullyTrusted = isFullyTrusted;
        }
        protected void SetLoadingInfoIsProductCode(bool isProductCode)
        {
            _loadingInfo.isProductCode = isProductCode;
        }

        private DatabaseLoadingInfo _loadingInfo = new DatabaseLoadingInfo();

        protected DatabaseLoadingInfo LoadingInfo
        {
            get
            {
                DatabaseLoadingInfo info = new DatabaseLoadingInfo();
                info.filePath = _loadingInfo.filePath;
                info.fileDirectory = _loadingInfo.fileDirectory;
                info.isFullyTrusted = _loadingInfo.isFullyTrusted;
                info.isProductCode = _loadingInfo.isProductCode;
                return info;
            }
        }

        protected MshExpressionFactory expressionFactory;

        protected DisplayResourceManagerCache displayResourceManagerCache;


        internal bool VerifyStringResources { get; } = true;

        private int _maxNumberOfErrors = 30;

        private int _currentErrorCount = 0;

        private bool _logStackActivity = false;

        private Stack<XmlLoaderStackFrame> _executionStack = new Stack<XmlLoaderStackFrame>();

        private XmlLoaderLogger _logger = new XmlLoaderLogger();
    }
}
