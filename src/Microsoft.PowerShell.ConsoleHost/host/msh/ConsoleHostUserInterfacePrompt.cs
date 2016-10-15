/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/



using System;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Management.Automation;
using System.Management.Automation.Internal;
using System.Management.Automation.Host;
using System.Security;
using Dbg = System.Management.Automation.Diagnostics;
using InternalHostUserInterface = System.Management.Automation.Internal.Host.InternalHostUserInterface;



namespace Microsoft.PowerShell
{
    internal partial
    class ConsoleHostUserInterface : System.Management.Automation.Host.PSHostUserInterface
    {
        /// <summary>
        /// Used by Prompt to indicate any common errors when converting the user input string to
        ///  the type of the parameter
        /// </summary>
        private enum PromptCommonInputErrors
        {
            /// <summary>
            /// No error or not an error prompt handles
            /// </summary>
            None,
            /// <summary>
            /// Format error
            /// </summary>
            Format,
            /// <summary>
            /// Overflow error
            /// </summary>
            Overflow
        }



        private static
        bool
        AtLeastOneHelpMessageIsPresent(Collection<FieldDescription> descriptions)
        {
            foreach (FieldDescription fd in descriptions)
            {
                if (fd != null)
                {
                    if (!String.IsNullOrEmpty(fd.HelpMessage))
                    {
                        return true;
                    }
                }
            }

            return false;
        }



        /// <summary>
        /// 
        /// See base class
        /// 
        /// </summary>
        /// <param name="caption"></param>
        /// <param name="message"></param>
        /// <param name="descriptions"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">
        /// 
        /// If <paramref name="descriptions"/> is null
        ///    OR
        ///    at least one FieldDescription in <paramref name="descriptions"/> is null
        /// 
        /// </exception>
        /// <exception cref="ArgumentException">
        /// 
        /// If <paramref name="descriptions"/> count is less than 1
        ///    OR   
        ///    at least one FieldDescription.AssemblyFullName in <paramref name="descriptions"/> is
        ///     null or empty
        /// 
        /// </exception>
        /// <exception cref="PromptingException">
        /// 
        /// If a FieldDescription in <paramref name="descriptions"/> specifies one of SecureString or
        ///     PSCredential and the type can not be loaded.
        ///    OR
        ///    at least one FieldDescription in <paramref name="descriptions"/> specifies an array
        ///     whose rank is less than 1.
        ///    
        /// </exception>
        /// <exception cref="PSInvalidCastException">
        /// 
        /// If the converting the user input to the prompt field type fails unless it is caused by
        ///     OverflowException or FormatException
        /// 
        /// </exception> 

        public override
        Dictionary<string, PSObject>
        Prompt(string caption, string message, Collection<FieldDescription> descriptions)
        {
            // Need to implement EchoOnPrompt
            HandleThrowOnReadAndPrompt();

            if (descriptions == null)
            {
                throw PSTraceSource.NewArgumentNullException("descriptions");
            }

            if (descriptions.Count < 1)
            {
                throw PSTraceSource.NewArgumentException("descriptions",
                    ConsoleHostUserInterfaceStrings.PromptEmptyDescriptionsErrorTemplate, "descriptions");
            }

            // we lock here so that multiple threads won't interleave the various reads and writes here.

            lock (_instanceLock)
            {
                Dictionary<string, PSObject> results = new Dictionary<string, PSObject>();

                Boolean cancelInput = false;

                if (!String.IsNullOrEmpty(caption))
                {
                    // Should be a skin lookup

                    WriteLineToConsole();
                    WriteToConsole(PromptColor, RawUI.BackgroundColor, WrapToCurrentWindowWidth(caption));
                    WriteLineToConsole();
                }
                if (!String.IsNullOrEmpty(message))
                {
                    WriteLineToConsole(WrapToCurrentWindowWidth(message));
                }

                if (AtLeastOneHelpMessageIsPresent(descriptions))
                {
                    WriteLineToConsole(WrapToCurrentWindowWidth(ConsoleHostUserInterfaceStrings.PromptHelp));
                }

                int descIndex = -1;

                foreach (FieldDescription desc in descriptions)
                {
                    descIndex++;
                    if (desc == null)
                    {
                        throw PSTraceSource.NewArgumentException("descriptions",
                            ConsoleHostUserInterfaceStrings.NullErrorTemplate,
                            string.Format(CultureInfo.InvariantCulture, "descriptions[{0}]", descIndex));
                    }
                    PSObject inputPSObject = null;
                    string fieldPrompt = null;
                    fieldPrompt = desc.Name;

                    bool fieldEchoOnPrompt = true;

                    // FieldDescription.ParameterAssemblyFullName never returns null. But this is
                    // defense in depth.
                    if (string.IsNullOrEmpty(desc.ParameterAssemblyFullName))
                    {
                        string paramName =
                            string.Format(CultureInfo.InvariantCulture, "descriptions[{0}].AssemblyFullName", descIndex);
                        throw PSTraceSource.NewArgumentException(paramName, ConsoleHostUserInterfaceStrings.NullOrEmptyErrorTemplate, paramName);
                    }

                    Type fieldType = InternalHostUserInterface.GetFieldType(desc);
                    if (fieldType == null)
                    {
                        if (InternalHostUserInterface.IsSecuritySensitiveType(desc.ParameterTypeName))
                        {
                            string errMsg =
                                StringUtil.Format(ConsoleHostUserInterfaceStrings.PromptTypeLoadErrorTemplate,
                                    desc.Name, desc.ParameterTypeFullName);
                            PromptingException e = new PromptingException(errMsg,
                                null, "BadTypeName", ErrorCategory.InvalidType);
                            throw e;
                        }

                        fieldType = typeof(string);
                    }

                    if (fieldType.GetInterface(typeof(IList).FullName) != null)
                    {
                        // field is a type implementing IList
                        ArrayList inputList = new ArrayList(); // stores all converted user input before
                        // assigned to an array

                        // if the field is an array, the element type can be found; else, use Object
                        Type elementType = typeof(Object);
                        if (fieldType.IsArray)
                        {
                            elementType = fieldType.GetElementType();
                            int rank = fieldType.GetArrayRank();
                            // This check may be redundant because it doesn't seem possible to create
                            // an array of zero dimension.
                            if (rank <= 0)
                            {
                                string msg = StringUtil.Format(ConsoleHostUserInterfaceStrings.RankZeroArrayErrorTemplate, desc.Name);
                                ArgumentException innerException = PSTraceSource.NewArgumentException(
                                    string.Format(CultureInfo.InvariantCulture,
                                    "descriptions[{0}].AssemblyFullName", descIndex));
                                PromptingException e = new PromptingException(msg, innerException, "ZeroRankArray", ErrorCategory.InvalidOperation);
                                throw e;
                            }
                        }

                        StringBuilder fieldPromptList = new StringBuilder(fieldPrompt);
                        // fieldPromptList = fieldPrompt + "[i] :"
                        fieldPromptList.Append("[");

                        while (true)
                        {
                            fieldPromptList.Append(
                                string.Format(CultureInfo.InvariantCulture, "{0}]: ", inputList.Count));
                            Boolean inputListEnd = false;
                            Object convertedObj = null;
                            string inputString = PromptForSingleItem(elementType, fieldPromptList.ToString(), fieldPrompt, caption, message,
                                desc, fieldEchoOnPrompt, true, out inputListEnd, out cancelInput, out convertedObj);

                            if (cancelInput || inputListEnd)
                            {
                                break;
                            }
                            else if (!cancelInput)
                            {
                                inputList.Add(convertedObj);
                                // Remove the indices from the prompt
                                fieldPromptList.Length = fieldPrompt.Length + 1;
                            }
                        }
                        // if cancelInput, should throw OperationCancelException?
                        if (!cancelInput)
                        {
                            object tryConvertResult = null;
                            if (LanguagePrimitives.TryConvertTo(inputList, fieldType, out tryConvertResult))
                            {
                                inputPSObject = PSObject.AsPSObject(tryConvertResult);
                            }
                            else
                            {
                                inputPSObject = PSObject.AsPSObject(inputList);
                            }
                        }
                    }
                    else
                    {
                        string printFieldPrompt = StringUtil.Format(ConsoleHostUserInterfaceStrings.PromptFieldPromptInputSeparatorTemplate,
                            fieldPrompt);
                        // field is not a list
                        Object convertedObj = null;
                        Boolean dummy = false;

                        PromptForSingleItem(fieldType, printFieldPrompt, fieldPrompt, caption, message, desc,
                                            fieldEchoOnPrompt, false, out dummy, out cancelInput, out convertedObj);
                        if (!cancelInput)
                        {
                            inputPSObject = PSObject.AsPSObject(convertedObj);
                        }
                    }
                    if (cancelInput)
                    {
                        s_tracer.WriteLine("Prompt canceled");
                        WriteLineToConsole();
                        results.Clear();
                        break;
                    }
                    results.Add(desc.Name, PSObject.AsPSObject(inputPSObject));
                }
                return results;
            }
        }

        private string PromptForSingleItem(Type fieldType,
            string printFieldPrompt,
            string fieldPrompt,
            string caption,
            string message,
            FieldDescription desc,
            bool fieldEchoOnPrompt,
            bool listInput,
            out bool endListInput,
            out bool cancelInput,
            out object convertedObj
            )
        {
            cancelInput = false;
            endListInput = false;
            convertedObj = null;

            if (fieldType.Equals(typeof(SecureString)))
            {
                WriteToConsole(printFieldPrompt, true);
                SecureString secureString = ReadLineAsSecureString();
                convertedObj = secureString;
                cancelInput = (convertedObj == null);
                if ((secureString != null) && (secureString.Length == 0) && listInput)
                {
                    endListInput = true;
                }
            }
            else if (fieldType.Equals(typeof(PSCredential)))
            {
                WriteLineToConsole(WrapToCurrentWindowWidth(fieldPrompt));
                PSCredential credential = null;
                // the earlier implementation contained null 
                // for caption and message in the call below
                // Passing null is a potential security risk
                // as any modifications made with security in
                // mind is lost. This can lead to a malicious
                // server prompting the user for a request 
                // which can appear to come from locally.
                if (!PromptUsingConsole() && desc.ModifiedByRemotingProtocol)
                {
                    credential =
                        PromptForCredential(
                            caption,
                            message,
                            null,
                            string.Empty);
                }
                else
                {
                    credential =
                        PromptForCredential(
                            null,   // caption already written
                            null,   // message already written
                            null,
                            string.Empty);
                }
                convertedObj = credential;
                cancelInput = (convertedObj == null);
                if ((credential != null) && (credential.Password.Length == 0) && listInput)
                {
                    endListInput = true;
                }
            }
            else
            {
                string inputString = null;
                do
                {
                    inputString = PromptReadInput(
                        printFieldPrompt, desc, fieldEchoOnPrompt,
                        listInput, out endListInput, out cancelInput);
                }
                while (!cancelInput && !endListInput && PromptTryConvertTo(fieldType, desc.IsFromRemoteHost, inputString, out convertedObj) !=
                    PromptCommonInputErrors.None);
                return inputString;
            }

            return null;
        }

        /// <summary>
        /// Called by Prompt. Reads user input and processes tilde commands.
        /// </summary>
        /// <param name="fieldPrompt">prompt written to host for the field</param>
        /// <param name="desc">the field to be read</param>
        /// <param name="fieldEchoOnPrompt">true to echo user input</param>
        /// <param name="listInput">true if the field is a list</param>
        /// <param name="endListInput">valid only if listInput is true. set to true if the input signals end of list input </param>
        /// <param name="cancelled">true iff the input is canceled, e.g., by Ctrl-C or Ctrl-Break</param>
        /// <returns>processed input string to be converted with LanguagePrimitives.ConvertTo </returns>
        private string PromptReadInput(string fieldPrompt, FieldDescription desc, bool fieldEchoOnPrompt,
                        bool listInput, out bool endListInput, out bool cancelled)
        {
            Dbg.Assert(fieldPrompt != null, "fieldPrompt should never be null when PromptReadInput is called");
            Dbg.Assert(desc != null, "desc should never be null when PromptReadInput is called");

            string processedInputString = null;
            endListInput = false;
            cancelled = false;
            bool inputDone = false;
            while (!inputDone)
            {
                WriteToConsole(fieldPrompt, true);
                string rawInputString = null;
                // Implement no echo here.
                if (fieldEchoOnPrompt)
                {
                    rawInputString = ReadLine();
                }
                else
                {
                    object userInput = ReadLineSafe(false, null);
                    string userInputString = userInput as string;
                    System.Management.Automation.Diagnostics.Assert(userInputString != null, "ReadLineSafe did not return a string");
                    rawInputString = userInputString;
                }
                if (rawInputString == null)
                {
                    // processedInputString is null as well. No need to assign null to it.
                    cancelled = true;
                    break;
                }
                else
                if (rawInputString.StartsWith(PromptCommandPrefix, StringComparison.CurrentCulture))
                {
                    processedInputString = PromptCommandMode(rawInputString, desc, out inputDone);
                }
                else
                {
                    if (rawInputString.Length == 0 && listInput)
                    {
                        endListInput = true;
                    }
                    processedInputString = rawInputString;
                    break;
                }
            }
            return processedInputString;
        }

        /// <summary>
        /// Uses LanguagePrimitives.ConvertTo to parse inputString for fieldType. Handles two most common parse
        ///  exceptions: OverflowException and FormatException.
        /// </summary>
        /// <param name="fieldType">the type that inputString is to be interpreted</param>
        /// <param name="isFromRemoteHost">is the call coming from a remote host</param>
        /// <param name="inputString">the string to be converted</param>
        /// <param name="convertedObj">if there's no error in the conversion, the converted object will be assigned here;
        /// otherwise, this will be the same as the inputString</param>
        /// <returns>an object of type fieldType that inputString represents</returns>
        private PromptCommonInputErrors PromptTryConvertTo(Type fieldType, bool isFromRemoteHost, string inputString, out object convertedObj)
        {
            Dbg.Assert(fieldType != null, "fieldType should never be null when PromptTryConvertTo is called");
            convertedObj = inputString;

            // do not do any type conversion if the prompt request is coming from a remote host
            // (bug Windows 7: #381643) + its bad to have potential side effects from casting on the client (think casting to a FileStream)
            if (isFromRemoteHost)
            {
                return PromptCommonInputErrors.None;
            }

            try
            {
                convertedObj = LanguagePrimitives.ConvertTo(inputString, fieldType, CultureInfo.InvariantCulture);
            }
            catch (PSInvalidCastException e)
            {
                Exception innerE = e.InnerException;
                if (innerE != null)
                {
                    if (innerE is OverflowException)
                    {
                        string errMsgTemplate =
                            ConsoleHostUserInterfaceStrings.PromptParseOverflowErrorTemplate;
                        WriteLineToConsole(
                            WrapToCurrentWindowWidth(
                                string.Format(CultureInfo.CurrentCulture, errMsgTemplate, fieldType, inputString)));
                        return PromptCommonInputErrors.Overflow;
                    }
                    else if (innerE is FormatException)
                    {
                        // Don't output error message if the inputString is empty
                        if (inputString.Length > 0)
                        {
                            string errMsgTemplate =
                                ConsoleHostUserInterfaceStrings.PromptParseFormatErrorTemplate;
                            WriteLineToConsole(
                                WrapToCurrentWindowWidth(
                                    string.Format(CultureInfo.CurrentCulture, errMsgTemplate, fieldType, inputString)));
                        }
                        return PromptCommonInputErrors.Format;
                    }
                    else
                    {
                    }
                }
                else
                {
                }
            }
            return PromptCommonInputErrors.None;
        }

        /// <summary>
        /// Handles Tilde Commands in Prompt
        /// If input does not start with PromptCommandPrefix (= "!"), returns input
        /// Tilde commands -
        /// !   end of list, only valid for input field types that implement IList, returns string.Empty
        /// !!*  input !* literally, returns !* where * is any string
        /// !h  prints out field's Quick Help, returns null
        /// All others tilde comments are invalid and return null
        /// 
        /// returns null iff there's nothing the caller can process
        /// </summary>
        /// <param name="input"></param>
        /// <param name="desc"></param>
        /// <param name="inputDone"></param>
        /// <returns></returns>

        private string PromptCommandMode(string input, FieldDescription desc, out bool inputDone)
        {
            Dbg.Assert(input != null && input.StartsWith(PromptCommandPrefix, StringComparison.OrdinalIgnoreCase),
                string.Format(CultureInfo.InvariantCulture, "input should start with {0}", PromptCommandPrefix));
            Dbg.Assert(desc != null, "desc should never be null when PromptCommandMode is called");
            string command = input.Substring(1);

            inputDone = true;
            if (command.StartsWith(PromptCommandPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return command;
            }
            if (command.Length == 1)
            {
                if (command[0] == '?')
                {
                    if (string.IsNullOrEmpty(desc.HelpMessage))
                    {
                        string noHelpErrMsg =
                            StringUtil.Format(ConsoleHostUserInterfaceStrings.PromptNoHelpAvailableErrorTemplate, desc.Name);
                        s_tracer.TraceWarning(noHelpErrMsg);
                        WriteLineToConsole(WrapToCurrentWindowWidth(noHelpErrMsg));
                    }
                    else
                    {
                        WriteLineToConsole(WrapToCurrentWindowWidth(desc.HelpMessage));
                    }
                }
                else
                {
                    ReportUnrecognizedPromptCommand(input);
                }
                inputDone = false;
                return null;
            }
            if (command.Length == 2)
            {
                if (0 == string.Compare(command, "\"\"", StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }
            }
            if (0 == string.Compare(command, "$null", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            else
            {
                ReportUnrecognizedPromptCommand(input);
                inputDone = false;
                return null;
            }
        }

        private void ReportUnrecognizedPromptCommand(string command)
        {
            string msg = StringUtil.Format(ConsoleHostUserInterfaceStrings.PromptUnrecognizedCommandErrorTemplate, command);
            WriteLineToConsole(WrapToCurrentWindowWidth(msg));
        }

        // Prefix for command mode in Prompt
        private const string PromptCommandPrefix = "!";
    }
}   // namespace 

