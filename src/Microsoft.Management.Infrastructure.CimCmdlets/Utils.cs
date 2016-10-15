﻿/*============================================================================
 * Copyright (C) Microsoft Corporation, All rights reserved. 
 *============================================================================
 */

// #define LOGENABLE // uncomment this line to enable the log, 
                  // create c:\temp\cim.log before invoking cimcmdlets

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using System.Text.RegularExpressions;
using System.Threading;

namespace Microsoft.Management.Infrastructure.CimCmdlets
{
    /// <summary>
    /// <para>
    /// Global Non-localization strings
    /// </para>
    /// </summary>
    internal static class ConstValue
    {
        /// <summary>
        /// <para>
        /// Default computername
        /// </para>
        /// </summary>
        internal static string[] DefaultSessionName = {@"*"};

        /// <summary>
        /// <para>
        /// Empty computername, which will create DCOM session
        /// </para>
        /// </summary>
        internal static string NullComputerName = null;

        /// <summary>
        /// <para>
        /// Empty computername array, which will create DCOM session
        /// </para>
        /// </summary>
        internal static string[] NullComputerNames = { NullComputerName };

        /// <summary>
        /// <para>
        /// localhost computername, which will create WSMAN session
        /// </para>
        /// </summary>
        internal static string LocalhostComputerName = @"localhost";

        /// <summary>
        /// <para>
        /// Default namespace
        /// </para>
        /// </summary>
        internal static string DefaultNameSpace = @"root\cimv2";

        /// <summary>
        /// <para>
        /// Default namespace
        /// </para>
        /// </summary>
        internal static string DefaultQueryDialect = @"WQL";

        /// <summary>
        /// Name of the note property that controls if "PSComputerName" column is shown
        /// </summary>
        internal static string ShowComputerNameNoteProperty = "PSShowComputerName";

        /// <summary>
        /// <para>
        /// Whether given computername is either null or empty
        /// </para>
        /// </summary>
        /// <param name="computerName"></param>
        /// <returns></returns>
        internal static bool IsDefaultComputerName(string computerName)
        {
            return String.IsNullOrEmpty(computerName);
        }

        /// <summary>
        /// <para>
        /// Get computer names, if it is null then return DCOM one
        /// </para>
        /// </summary>
        /// <param name="computerNames"></param>
        /// <returns></returns>
        internal static IEnumerable<string> GetComputerNames(IEnumerable<string> computerNames)
        {
            return (computerNames == null) ? NullComputerNames : computerNames;
        }

        /// <summary>
        /// Get computer name, if it is null then return default one
        /// </summary>
        /// <param name="computerName"></param>
        /// <returns></returns>
        internal static string GetComputerName(string computerName)
        {
            return string.IsNullOrEmpty(computerName) ? NullComputerName : computerName;
        }

        /// <summary>
        /// <para>
        /// Get namespace, if it is null then return default one
        /// </para>
        /// </summary>
        /// <param name="nameSpace"></param>
        /// <returns></returns>
        internal static string GetNamespace(string nameSpace)
        {
            return (nameSpace == null) ? DefaultNameSpace : nameSpace;
        }

        /// <summary>
        /// <para>
        /// Get queryDialect, if it is null then return default query Dialect
        /// </para>
        /// </summary>
        /// <param name="queryDialect"></param>
        /// <returns></returns>
        internal static string GetQueryDialectWithDefault(string queryDialect)
        {
            return (queryDialect == null) ? DefaultQueryDialect : queryDialect;
        }
    }

    /// <summary>
    /// <para>
    /// Debug helper class used to dump debug message to log file
    /// </para>
    /// </summary>
    internal static class DebugHelper
    {
        #region private members

        /// <summary>
        /// Flag used to control generating log message into file.
        /// </summary>
        private static bool generateLog = true;
        internal static bool GenerateLog
        {
            get { return generateLog; }
            set { generateLog = value; }
        }

        /// <summary>
        /// Whether the log been initialized
        /// </summary>
        private static bool logInitialized = false;

        /// <summary>
        /// Flag used to control generating message into powershell
        /// </summary>
        private static bool genrateVerboseMessage = true;
        internal static bool GenrateVerboseMessage
        {
            get { return genrateVerboseMessage; }
            set { genrateVerboseMessage = value; }
        }

        /// <summary>
        /// Flag used to control generating message into powershell
        /// </summary>
        internal static string logFile = @"c:\temp\Cim.log";

        /// <summary>
        /// Indent space string
        /// </summary>
        internal static string space = @"    ";

        /// <summary>
        /// Indent space strings array
        /// </summary>
        internal static string[] spaces = {
                                              string.Empty,
                                              space,
                                              space + space,
                                              space + space + space,
                                              space + space + space + space,
                                              space + space + space + space + space,
                                          };

        /// <summary>
        /// Lock the log file
        /// </summary>
        internal static object logLock = new object();

        #endregion

        #region internal strings
        internal static string runspaceStateChanged = "Runspace {0} state changed to {1}";
        internal static string classDumpInfo = @"Class type is {0}";
        internal static string propertyDumpInfo = @"Property name {0} of type {1}, its value is {2}";
        internal static string defaultPropertyType = @"It is a default property, default value is {0}";
        internal static string propertyValueSet = @"This property value is set by user {0}";
        internal static string addParameterSetName = @"Add parameter set {0} name to cache";
        internal static string removeParameterSetName = @"Remove parameter set {0} name from cache";
        internal static string currentParameterSetNameCount = @"Cache have {0} parameter set names";
        internal static string currentParameterSetNameInCache = @"Cache have parameter set {0} valid {1}";
        internal static string currentnonMadatoryParameterSetInCache = @"Cache have optional parameter set {0} valid {1}";
        internal static string optionalParameterSetNameCount = @"Cache have {0} optional parameter set names";
        internal static string finalParameterSetName = @"------Final parameter set name of the cmdlet is {0}";
        internal static string addToOptionalParameterSet = @"Add to optional ParameterSetNames {0}";
        internal static string startToResolveParameterSet = @"------Resolve ParameterSet Name";
        internal static string reservedString = @"------";
        #endregion

        #region runtime methods
        internal static string GetSourceCodeInformation(bool withFileName, int depth)
        {
#if CORECLR
            //return a dummy string as StackFrame won't be available on CoreCLR
            return string.Format(CultureInfo.CurrentUICulture, "{0}::{1}        ", "Type", "Method");
#else
            StackTrace trace = new StackTrace();
            StackFrame frame = trace.GetFrame(depth);
            //if (withFileName)
            //{
            //    return string.Format(CultureInfo.CurrentUICulture, "{0}#{1}:{2}:", frame.GetFileName()., frame.GetFileLineNumber(), frame.GetMethod().Name);
            //}
            //else
            //{
            //    return string.Format(CultureInfo.CurrentUICulture, "{0}:", frame.GetMethod());
            //}
            return string.Format(CultureInfo.CurrentUICulture, "{0}::{1}        ",
                frame.GetMethod().DeclaringType.Name,
                frame.GetMethod().Name);
            
#endif
        }
        #endregion

        /// <summary>
        /// Write message to log file named @logFile
        /// </summary>
        /// <param name="message"></param>
        internal static void WriteLog(string message)
        {
            WriteLog(message, 0);
        }

        /// <summary>
        /// Write blank line to log file named @logFile
        /// </summary>
        /// <param name="message"></param>
        internal static void WriteEmptyLine()
        {
            WriteLog(string.Empty, 0);
        }

        /// <summary>
        /// Write message to log file named @logFile with args
        /// </summary>
        /// <param name="message"></param>
        internal static void WriteLog(string message, int indent, params object[] args)
        {
            String outMessage = String.Empty;
            FormatLogMessage(ref outMessage, message, args);
            WriteLog(outMessage, indent);
        }

        /// <summary>
        /// Write message to log file w/o arguments
        /// </summary>
        /// <param name="message"></param>
        /// <param name="indent"></param>
        internal static void WriteLog(string message, int indent)
        {
            WriteLogInternal(message, indent, -1);
        }

        /// <summary>
        /// Write message to log file named @logFile with args
        /// </summary>
        /// <param name="message"></param>
        internal static void WriteLogEx(string message, int indent, params object[] args)
        {
            String outMessage = String.Empty;
            WriteLogInternal(string.Empty, 0, -1);
            FormatLogMessage(ref outMessage, message, args);
            WriteLogInternal(outMessage, indent, 3);
        }

        /// <summary>
        /// Write message to log file w/o arguments
        /// </summary>
        /// <param name="message"></param>
        /// <param name="indent"></param>
        internal static void WriteLogEx(string message, int indent)
        {
            WriteLogInternal(string.Empty, 0, -1);
            WriteLogInternal(message, indent, 3);
        }

        /// <summary>
        /// Write message to log file w/o arguments
        /// </summary>
        /// <param name="message"></param>
        /// <param name="indent"></param>
        internal static void WriteLogEx()
        {
            WriteLogInternal(string.Empty, 0, -1);
            WriteLogInternal(string.Empty, 0, 3);
        }

        /// <summary>
        ///  Format the message
        /// </summary>
        /// <param name="message"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        [Conditional("LOGENABLE")]
        private static void FormatLogMessage(ref String outMessage, string message, params object[] args)
        {
            outMessage = String.Format(CultureInfo.CurrentCulture, message, args);
        }

        /// <summary>
        /// Write message to log file named @logFile
        /// with indent space ahead of the message.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="nIndent"></param>
        [Conditional("LOGENABLE")]
        private static void WriteLogInternal(string message, int indent, int depth)
        {
            if (!logInitialized)
            {
                lock (logLock)
                {
                    if (!logInitialized)
                    {
                        DebugHelper.GenerateLog = File.Exists(logFile);
                        logInitialized = true;
                    }
                }
            }

            if (generateLog)
            {
                if (indent < 0)
                {
                    indent = 0;
                }
                if (indent > 5)
                {
                    indent = 5;
                }
                string sourceInformation = string.Empty;
                if (depth != -1)
                {
                    sourceInformation = string.Format(
                        CultureInfo.InvariantCulture,
                        "Thread {0}#{1}:{2}:{3} {4}",
                        Thread.CurrentThread.ManagedThreadId,
                        DateTime.Now.Hour,
                        DateTime.Now.Minute,
                        DateTime.Now.Second,
                        GetSourceCodeInformation(true, depth));
                }
                lock (logLock)
                {
                    using (FileStream fs = new FileStream(logFile,FileMode.OpenOrCreate))
                    using (StreamWriter writer = new StreamWriter(fs))
                    {
                        writer.WriteLineAsync(spaces[indent] + sourceInformation + @"        " + message);
                    }
                    
                }
            }
        }
    }

    /// <summary>
    /// <para>
    /// Helper class used to validate given parameter
    /// </para>
    /// </summary>
    internal static class ValidationHelper
    {
        /// <summary>
        /// Validate the argument is not null
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="argumentName"></param>
        public static void ValidateNoNullArgument(object obj, string argumentName)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(argumentName);
            }
        }

        /// <summary>
        /// Validate the argument is not null and not whitespace
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="argumentName"></param>
        public static void ValidateNoNullorWhiteSpaceArgument(string obj, string argumentName)
        {
            if (String.IsNullOrWhiteSpace(obj))
            {
                throw new ArgumentException(argumentName);
            }
        }

        /// <summary>
        /// Validate that given classname/propertyname is a valid name compliance with DMTF standard.
        /// Only for verifying ClassName and PropertyName argument
        /// </summary>
        /// <param name="parameterName"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">Throw if the given value is not a valid name (class name or property name)</exception>
        public static string ValidateArgumentIsValidName(string parameterName, string value)
        {
            DebugHelper.WriteLogEx();
            if (value != null)
            {
                string trimed = value.Trim();
                // The first character should be contained in set: [A-Za-z_]
                // Inner characters should be contained in set: [A-Za-z0-9_]
                Regex regex = new Regex(@"^[a-zA-Z_][a-zA-Z0-9_]*\z");
                if (regex.IsMatch(trimed))
                {
                    DebugHelper.WriteLogEx("A valid name: {0}={1}", 0, parameterName, value);
                    return trimed;
                }
            }
            DebugHelper.WriteLogEx("An invalid name: {0}={1}", 0, parameterName, value);
            throw new ArgumentException(String.Format(CultureInfo.CurrentUICulture, Strings.InvalidParameterValue, value, parameterName));
        }

        /// <summary>
        /// Validate given arry argument contains all valid name (for -SelectProperties).
        /// * is valid for this case.
        /// </summary>
        /// <param name="parameterName"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">Throw if the given value contains any invalid name (class name or property name)</exception>
        public static String[] ValidateArgumentIsValidName(string parameterName, String[] value)
        {
            if (value != null)
            {
                foreach (string propertyName in value)
                {
                    // * is wild char supported in select properties
                    if ((propertyName != null) && (String.Compare(propertyName.Trim(), "*", StringComparison.OrdinalIgnoreCase) == 0))
                    {
                        continue;
                    }
                    ValidationHelper.ValidateArgumentIsValidName(parameterName, propertyName);
                }
            }
            return value;
        }
    }
}
