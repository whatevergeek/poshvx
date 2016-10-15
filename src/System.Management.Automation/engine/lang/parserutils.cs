/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Management.Automation.Internal;
using System.Management.Automation.Internal.Host;
using System.Management.Automation.Language;
using System.Management.Automation.Runspaces;
using Dbg = System.Management.Automation.Diagnostics;

#if CORECLR
// Use stubs for SystemException and SerializationInfo
using Microsoft.PowerShell.CoreClr.Stubs;
#endif

#pragma warning disable 1634, 1691 // Stops compiler from warning about unknown warnings

namespace System.Management.Automation
{
    #region SpecialCharacters
    /// <summary>
    /// Define the various unicode special characters that
    /// the parser has to deal with.
    /// </summary>
    internal static class SpecialCharacters
    {
        public const char enDash = (char)0x2013;
        public const char emDash = (char)0x2014;
        public const char horizontalBar = (char)0x2015;

        public const char quoteSingleLeft = (char)0x2018;   // left single quotation mark  
        public const char quoteSingleRight = (char)0x2019;  // right single quotation mark 
        public const char quoteSingleBase = (char)0x201a;   // single low-9 quotation mark
        public const char quoteReversed = (char)0x201b;     // single high-reversed-9 quotation mark    
        public const char quoteDoubleLeft = (char)0x201c;   // left double quotation mark
        public const char quoteDoubleRight = (char)0x201d;  // right double quotation mark
        public const char quoteLowDoubleLeft = (char)0x201E;// low double left quote used in german.

        public static bool IsDash(char c)
        {
            return (c == enDash || c == emDash || c == horizontalBar || c == '-');
        }
        public static bool IsSingleQuote(char c)
        {
            return (c == quoteSingleLeft || c == quoteSingleRight || c == quoteSingleBase ||
                c == quoteReversed || c == '\'');
        }
        public static bool IsDoubleQuote(char c)
        {
            return (c == '"' || c == quoteDoubleLeft || c == quoteDoubleRight || c == quoteLowDoubleLeft);
        }
        public static bool IsQuote(char c)
        {
            return (IsSingleQuote(c) || IsDoubleQuote(c));
        }
        public static bool IsDelimiter(char c, char delimiter)
        {
            if (delimiter == '"') return IsDoubleQuote(c);
            if (delimiter == '\'') return IsSingleQuote(c);
            return (c == delimiter);
        }
        public static bool IsCurlyBracket(char c)
        {
            return (c == '{' || c == '}');
        }
        /// <summary>
        /// Canonicalize the quote character - map all of the aliases for " or '
        /// into their ascii equivalent.
        /// </summary>
        /// <param name="c">The character to map</param>
        /// <returns>The mapped character.</returns>
        public static char AsQuote(char c)
        {
            if (IsSingleQuote(c)) return '\'';
            if (IsDoubleQuote(c)) return '"';
            return (c);
        }
    };

    #endregion SpecialChars

    #region Flow Control Exceptions

    /// <summary>
    /// FlowControlException, base class for flow control exceptions.
    /// </summary>
    public abstract class FlowControlException : SystemException
    {
        internal FlowControlException() { }

        internal FlowControlException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// LoopFlowException, base class for loop control exceptions.
    /// </summary>
    public abstract class LoopFlowException : FlowControlException
    {
        internal LoopFlowException(string label)
        {
            this.Label = label ?? "";
        }

        internal LoopFlowException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }

        internal LoopFlowException() { }

        /// <summary>
        /// Label, indicates nested loop level affected by exception.
        /// No label means most nested loop is affected.
        /// </summary>
        public string Label
        {
            get;
            internal set;
        }

        internal bool MatchLabel(string loopLabel)
        {
            return MatchLoopLabel(Label, loopLabel);
        }

        internal static bool MatchLoopLabel(string flowLabel, string loopLabel)
        {
            // If the flow statement has no label, it always matches (because it just means, break or continue from
            // the most nested loop.)  Otherwise, compare the labels.

            return String.IsNullOrEmpty(flowLabel) || flowLabel.Equals(loopLabel, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Flow control BreakException.
    /// </summary>
    public sealed class BreakException : LoopFlowException
    {
        [SuppressMessage("Microsoft.Design", "CA1032:ImplementStandardExceptionConstructors", Justification = "This exception should only be thrown from SMA.dll")]
        internal BreakException(string label)
            : base(label)
        {
        }

        [SuppressMessage("Microsoft.Design", "CA1032:ImplementStandardExceptionConstructors", Justification = "This exception should only be thrown from SMA.dll")]
        internal BreakException() { }

        [SuppressMessage("Microsoft.Design", "CA1032:ImplementStandardExceptionConstructors", Justification = "This exception should only be thrown from SMA.dll")]
        internal BreakException(String label, Exception innerException)
            : base(label)
        {
        }

        private BreakException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// Flow control ContinueException
    /// </summary>
    public sealed class ContinueException : LoopFlowException
    {
        [SuppressMessage("Microsoft.Design", "CA1032:ImplementStandardExceptionConstructors", Justification = "This exception should only be thrown from SMA.dll")]
        internal ContinueException(string label)
            : base(label)
        {
        }

        [SuppressMessage("Microsoft.Design", "CA1032:ImplementStandardExceptionConstructors", Justification = "This exception should only be thrown from SMA.dll")]
        internal ContinueException() { }

        [SuppressMessage("Microsoft.Design", "CA1032:ImplementStandardExceptionConstructors", Justification = "This exception should only be thrown from SMA.dll")]
        internal ContinueException(String label, Exception innerException)
            : base(label)
        {
        }

        private ContinueException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    internal class ReturnException : FlowControlException
    {
        internal ReturnException(object argument)
        {
            this.Argument = argument;
        }

        internal object Argument { get; set; }
    }

    /// <summary>
    /// Implements the exit keyword.
    /// </summary>
    public class ExitException : FlowControlException
    {
        [SuppressMessage("Microsoft.Design", "CA1032:ImplementStandardExceptionConstructors", Justification = "This exception should only be thrown from SMA.dll")]
        internal ExitException(object argument)
        {
            this.Argument = argument;
        }

        /// <summary>
        /// Argument
        /// </summary>
        public object Argument { get; internal set; }

        [SuppressMessage("Microsoft.Design", "CA1032:ImplementStandardExceptionConstructors", Justification = "This exception should only be thrown from SMA.dll")]
        internal ExitException() { }

        [SuppressMessage("Microsoft.Design", "CA1032:ImplementStandardExceptionConstructors", Justification = "This exception should only be thrown from SMA.dll")]
        private ExitException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// Used by InternalHost.ExitNestedPrompt() to pop out of an interpreter level...
    /// </summary>
    internal class ExitNestedPromptException : FlowControlException
    {
    }

    /// <summary>
    /// Used by the debugger to terminate the execution of the current command.
    /// </summary>
    public sealed class TerminateException : FlowControlException
    {
    }

    /// <summary>
    /// Used by Select-Object cmdlet to stop all the upstream cmdlets, but continue
    /// executing downstream cmdlets.  The semantics of stopping is intended to mimic
    /// a user pressing Ctrl-C [but which only affects upstream cmdlets].
    /// </summary>
    internal class StopUpstreamCommandsException : FlowControlException
    {
        public StopUpstreamCommandsException(InternalCommand requestingCommand)
        {
            this.RequestingCommandProcessor = requestingCommand.Context.CurrentCommandProcessor;
        }

        public CommandProcessorBase RequestingCommandProcessor { get; private set; }
    }

    #endregion Flow Control Exceptions

    /// <summary>
    /// A enum corresponding to the options on the -split operator.
    /// </summary>
    [Flags]
    public enum SplitOptions
    {
        /// <summary>
        /// Use simple string comparison when evaluating the delimiter.
        /// Cannot be used with RegexMatch.
        /// </summary>
        SimpleMatch = 0x01,
        /// <summary>
        /// Use regular expression matching to evaluate the delimiter.
        /// This is the default behavior. Cannot be used with SimpleMatch.
        /// </summary>
        RegexMatch = 0x02,
        /// <summary>
        /// CultureInvariant: Ignores cultural differences in language when evaluating the delimiter.
        /// Valid only with RegexMatch.
        /// </summary>
        CultureInvariant = 0x04,
        /// <summary>
        /// Ignores unescaped whitespace and comments marked with #.
        /// Valid only with RegexMatch.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1702:CompoundWordsShouldBeCasedCorrectly", MessageId = "Whitespace")]
        IgnorePatternWhitespace = 0x08,
        /// <summary>
        /// Regex multiline mode, which recognizes the start and end of lines,
        /// as well as the start and end of strings.
        /// Valid only with RegexMatch.
        /// Singleline is the default.
        /// </summary>
        Multiline = 0x10,
        /// <summary>
        /// Regex Singleline mode, which recognizes only the start and end of strings.
        /// Valid only with RegexMatch.
        /// Singleline is the default.
        /// </summary>
        Singleline = 0x20,
        /// <summary>
        /// Forces case-insensitive matching, even if -cSplit is specified.
        /// </summary>
        IgnoreCase = 0x40,
        /// <summary>
        /// Ignores non-named match groups, so that only explicit capture groups
        /// are returned in the result list.
        /// </summary>
        ExplicitCapture = 0x80,
    }

    #region ParserOps

    internal delegate object PowerShellBinaryOperator(ExecutionContext context, IScriptExtent errorPosition, object lval, object rval);

    /// <summary>
    /// A static class holding various operations specific to the msh interpreter such as
    /// various math operations, ToString() and a routine to extract the base object from an
    /// PSObject in a canonical fashion.
    /// </summary>
    internal static class ParserOps
    {
        internal const string MethodNotFoundErrorId = "MethodNotFound";

        /// <summary>
        /// Construct the various caching structures used by the runtime routines...
        /// </summary>
        static ParserOps()
        {
            // Cache for ints and chars to avoid overhead of boxing every time...
            for (int i = 0; i < (_MaxCache - _MinCache); i++)
            {
                s_integerCache[i] = (object)(i + _MinCache);
            }

            for (char ch = (char)0; ch < 255; ch++)
            {
                s_chars[ch] = new string(ch, 1);
            }
        }

        private const int _MinCache = -100;
        private const int _MaxCache = 1000;
        private static readonly object[] s_integerCache = new object[_MaxCache - _MinCache];
        private static readonly string[] s_chars = new string[255];
        internal static readonly object _TrueObject = (object)true;
        internal static readonly object _FalseObject = (object)false;

        internal static string CharToString(char ch)
        {
            if (ch < 255) return s_chars[ch];
            return new string(ch, 1);
        }

        internal static object BoolToObject(bool value)
        {
            return value ? _TrueObject : _FalseObject;
        }

        /// <summary>
        /// Convert an object into an int, avoiding boxing small integers...
        /// </summary>
        /// <param name="value">The int to convert</param>
        /// <returns>The reference equivalent</returns>
        internal static object IntToObject(int value)
        {
            if (value < _MaxCache && value >= _MinCache)
            {
                return s_integerCache[value - _MinCache];
            }
            return (object)value;
        }

        internal static PSObject WrappedNumber(object data, string text)
        {
            PSObject wrapped = new PSObject(data);
            wrapped.TokenText = text;
            return wrapped;
        }

        /// <summary>
        /// A helper routine that turns the argument object into an
        /// integer. It handles PSObject and conversions.
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="errorPosition">The position to use for error reporting.</param>
        /// <returns></returns>
        /// <exception cref="RuntimeException">The result could not be represented as an integer</exception>
        internal static int FixNum(object obj, IScriptExtent errorPosition)
        {
            obj = PSObject.Base(obj);

            if (obj == null)
                return 0;
            if (obj is int)
                return (int)obj;
            int result = ConvertTo<int>(obj, errorPosition);
            return result;
        }

        /// <summary>
        /// This is a helper function for converting an object to a particular type. 
        /// 
        /// It will throw exception with information about token representing the object. 
        /// </summary>
        internal static T ConvertTo<T>(object obj, IScriptExtent errorPosition)
        {
            T result;

            try
            {
                result = (T)LanguagePrimitives.ConvertTo(obj, typeof(T), CultureInfo.InvariantCulture);
            }
            catch (PSInvalidCastException mice)
            {
                RuntimeException re = new RuntimeException(mice.Message, mice);
                re.ErrorRecord.SetInvocationInfo(new InvocationInfo(null, errorPosition));
                throw re;
            }
            return result;
        }

        /// <summary>
        /// private method used to call the op_* operations for the math operators
        /// </summary>
        /// <param name="lval">left operand</param>
        /// <param name="rval">right operand</param>
        /// <param name="op">name of the operation method to perform</param>
        /// <param name="errorPosition">The position to use for error reporting.</param>
        /// <param name="errorOp">the string to use in error messages representing the op</param>
        /// <returns>The result of the operation</returns>
        /// <exception cref="RuntimeException">An error occurred performing the operation, see inner exception</exception>
        internal static object ImplicitOp(object lval, object rval, string op, IScriptExtent errorPosition, string errorOp)
        {
            // Get the base object. At somepoint, we may allow users to dynamically extend
            // the implicit operators at which point, we'll need to change this to find the
            // extension method...
            lval = PSObject.Base(lval);
            rval = PSObject.Base(rval);

            Type lvalType = lval != null ? lval.GetType() : null;
            Type rvalType = rval != null ? rval.GetType() : null;
            Type opType;
            if (lvalType == null || (lvalType.GetTypeInfo().IsPrimitive))
            {
                // Prefer the LHS type when looking for the operator, but attempt the right
                // the lhs can't have an operator.
                //
                // This logic is overly simplified and doesn't match other languages which
                // would look for overloads in both types, but this logic covers the most common
                // cases.

                opType = (rvalType == null || rvalType.GetTypeInfo().IsPrimitive) ? null : rvalType;
            }
            else
            {
                opType = lvalType;
            }

            if (opType == null)
            {
                throw InterpreterError.NewInterpreterException(lval, typeof(RuntimeException), errorPosition,
                    "NotADefinedOperationForType", ParserStrings.NotADefinedOperationForType,
                    lvalType == null ? "$null" : lvalType.FullName,
                    errorOp,
                    rvalType == null ? "$null" : rvalType.FullName);
            }

            // None of the explicit conversions worked so try and invoke a method instead...
            object[] parms = new object[2];
            parms[0] = lval;
            parms[1] = rval;
            return CallMethod(
                errorPosition,
                opType,
                op, /* methodName */
                null, /* invocationConstraints */
                parms,
                true,
                AutomationNull.Value);
        }

        [Flags]
        private enum SplitImplOptions
        {
            None = 0x00,
            TrimContent = 0x01,
        }

        private static object[] unfoldTuple(ExecutionContext context, IScriptExtent errorPosition, object tuple)
        {
            List<object> result = new List<object>();

            IEnumerator enumerator = LanguagePrimitives.GetEnumerator(tuple);
            if (enumerator != null)
            {
                while (ParserOps.MoveNext(context, errorPosition, enumerator))
                {
                    object element = ParserOps.Current(errorPosition, enumerator);
                    result.Add(element);
                }
            }
            else
            {
                // Not a tuple at all, just a single item. Treat it
                // as a 1-tuple.
                result.Add(tuple);
            }

            return result.ToArray();
        }

        // uses "yield" from C# 2.0, which automatically creates
        // an enumerable out of the loop code. See
        // http://msdn.microsoft.com/msdnmag/issues/04/05/C20/ for
        // more details.
        private static IEnumerable<string> enumerateContent(ExecutionContext context, IScriptExtent errorPosition, SplitImplOptions implOptions, object tuple)
        {
            IEnumerator enumerator = LanguagePrimitives.GetEnumerator(tuple) ?? new object[] { tuple }.GetEnumerator();

            while (ParserOps.MoveNext(context, errorPosition, enumerator))
            {
                string strValue = PSObject.ToStringParser(context, enumerator.Current);
                if ((implOptions & SplitImplOptions.TrimContent) != 0)
                    strValue = strValue.Trim();

                yield return strValue;
            }
        }

        private static RegexOptions parseRegexOptions(SplitOptions options)
        {
            int[][] map = {
                new int[] { (int)SplitOptions.CultureInvariant, (int)RegexOptions.CultureInvariant },
                new int[] { (int)SplitOptions.IgnorePatternWhitespace, (int)RegexOptions.IgnorePatternWhitespace },
                new int[] { (int)SplitOptions.Multiline, (int)RegexOptions.Multiline },
                new int[] { (int)SplitOptions.Singleline, (int)RegexOptions.Singleline },
                new int[] { (int)SplitOptions.IgnoreCase, (int)RegexOptions.IgnoreCase },
                new int[] { (int)SplitOptions.ExplicitCapture, (int)RegexOptions.ExplicitCapture },
            };

            RegexOptions result = RegexOptions.None;
            foreach (int[] entry in map)
            {
                if (((int)options & entry[0]) != 0)
                {
                    result |= (RegexOptions)entry[1];
                }
            }

            return result;
        }

        internal static object UnarySplitOperator(ExecutionContext context, IScriptExtent errorPosition, object lval)
        {
            // unary split does a little extra processing to make
            // whitespace processing more convenient. Specifically,
            // it will ignore leading/trailing whitespace.
            return SplitOperatorImpl(context, errorPosition, lval, new object[] { @"\s+" }, SplitImplOptions.TrimContent, false);
        }

        internal static object SplitOperator(ExecutionContext context, IScriptExtent errorPosition, object lval, object rval, bool ignoreCase)
        {
            return SplitOperatorImpl(context, errorPosition, lval, rval, SplitImplOptions.None, ignoreCase);
        }

        private static void ExtendList<T>(IList<T> list, IList<T> items)
        {
            foreach (T item in items)
            {
                list.Add(item);
            }
        }

        private static object SplitOperatorImpl(ExecutionContext context, IScriptExtent errorPosition, object lval, object rval, SplitImplOptions implOptions, bool ignoreCase)
        {
            IEnumerable<string> content = enumerateContent(context, errorPosition, implOptions, lval);

            ScriptBlock predicate = null;
            string separatorPattern = null;
            int limit = 0;
            SplitOptions options = 0;

            object[] args = unfoldTuple(context, errorPosition, rval);
            if (args.Length >= 1)
            {
                predicate = args[0] as ScriptBlock;
                if (predicate == null)
                {
                    separatorPattern = PSObject.ToStringParser(context, args[0]);
                }
            }
            else
            {
                // The first argument to split is always required.
                throw InterpreterError.NewInterpreterException(rval, typeof(RuntimeException), errorPosition,
                    "BadOperatorArgument", ParserStrings.BadOperatorArgument, "-split", rval);
            }
            if (args.Length >= 2)
                limit = FixNum(args[1], errorPosition);
            if (args.Length >= 3 && args[2] != null)
            {
                string args2asString = args[2] as string;
                if (args2asString == null || !string.IsNullOrEmpty(args2asString))
                {
                    options = ConvertTo<SplitOptions>(args[2], errorPosition);
                    if (predicate != null)
                    {
                        throw InterpreterError.NewInterpreterException(null, typeof(ParseException),
                            errorPosition, "InvalidSplitOptionWithPredicate", ParserStrings.InvalidSplitOptionWithPredicate);
                    }
                    if (ignoreCase && (options & SplitOptions.IgnoreCase) == 0)
                    {
                        options |= SplitOptions.IgnoreCase;
                    }
                }
            }
            else if (ignoreCase)
            {
                options |= SplitOptions.IgnoreCase;
            }

            if (predicate != null)
            {
                return SplitWithPredicate(context, errorPosition, content, predicate, limit);
            }
            else
            {
                return SplitWithPattern(context, errorPosition, content, separatorPattern, limit, options);
            }
        }

        private static object SplitWithPredicate(ExecutionContext context, IScriptExtent errorPosition, IEnumerable<string> content, ScriptBlock predicate, int limit)
        {
            List<string> results = new List<string>();
            foreach (string item in content)
            {
                List<string> split = new List<String>();

                if (limit == 1)
                {
                    // Don't bother with looking for any delimiters,
                    // just return the original string.
                    results.Add(item);
                    continue;
                }

                StringBuilder buf = new StringBuilder();
                for (int strIndex = 0; strIndex < item.Length; strIndex++)
                {
                    object isDelimChar = predicate.DoInvokeReturnAsIs(
                        useLocalScope: true,
                        errorHandlingBehavior: ScriptBlock.ErrorHandlingBehavior.WriteToExternalErrorPipe,
                        dollarUnder: CharToString(item[strIndex]),
                        input: AutomationNull.Value,
                        scriptThis: AutomationNull.Value,
                        args: new object[] { item, strIndex });
                    if (LanguagePrimitives.IsTrue(isDelimChar))
                    {
                        split.Add(buf.ToString());
                        buf = new StringBuilder();

                        if (limit > 0 && split.Count >= (limit - 1))
                        {
                            // We're one item below the limit. If
                            // we have any string left, go ahead
                            // and add it as the last item, otherwise
                            // add an empty string if there was
                            // a delimiter at the end.
                            if ((strIndex + 1) < item.Length)
                            {
                                split.Add(item.Substring(strIndex + 1));
                            }
                            else
                            {
                                split.Add("");
                            }
                            break;
                        }

                        // If this delimiter is at the end of the string,
                        // add an empty string to denote the item "after"
                        // it.
                        if (strIndex == (item.Length - 1))
                        {
                            split.Add("");
                        }
                    }
                    else
                    {
                        buf.Append(item[strIndex]);
                    }
                }

                // Add any remainder, if we're under the limit.
                if (buf.Length > 0 &&
                    (limit <= 0 || split.Count < limit))
                {
                    split.Add(buf.ToString());
                }

                ExtendList(results, split);
            }

            return results.ToArray();
        }

        private static object SplitWithPattern(ExecutionContext context, IScriptExtent errorPosition, IEnumerable<string> content, string separatorPattern, int limit, SplitOptions options)
        {
            // Default to Regex matching if no match specified.
            if ((options & SplitOptions.SimpleMatch) == 0 &&
                (options & SplitOptions.RegexMatch) == 0)
            {
                options |= SplitOptions.RegexMatch;
            }

            if ((options & SplitOptions.SimpleMatch) != 0)
            {
                if ((options & ~(SplitOptions.SimpleMatch | SplitOptions.IgnoreCase)) != 0)
                {
                    throw InterpreterError.NewInterpreterException(null, typeof(ParseException),
                        errorPosition, "InvalidSplitOptionCombination", ParserStrings.InvalidSplitOptionCombination);
                }
            }

            if ((options & (SplitOptions.Multiline | SplitOptions.Singleline)) ==
                  (SplitOptions.Multiline | SplitOptions.Singleline))
            {
                throw InterpreterError.NewInterpreterException(null, typeof(ParseException),
                    errorPosition, "InvalidSplitOptionCombination", ParserStrings.InvalidSplitOptionCombination);
            }

            if ((options & SplitOptions.SimpleMatch) != 0)
            {
                separatorPattern = Regex.Escape(separatorPattern);
            }

            if (limit < 0)
            {
                // Regex only allows 0 to signify "no limit", whereas
                // we allow any integer <= 0.
                limit = 0;
            }

            RegexOptions regexOptions = parseRegexOptions(options);
            Regex regex = NewRegex(separatorPattern, regexOptions);

            List<string> results = new List<string>();
            foreach (string item in content)
            {
                string[] split = regex.Split(item, limit, 0);
                ExtendList(results, split);
            }

            return results.ToArray();
        }

        /// <summary>
        /// Implementation of the PowerShell unary -join operator...
        /// </summary>
        /// <param name="context">The execution context to use</param>
        /// <param name="errorPosition">The position to use for error reporting.</param>
        /// <param name="lval">left operand</param>
        /// <returns>The result of the operator</returns>
        internal static object UnaryJoinOperator(ExecutionContext context, IScriptExtent errorPosition, object lval)
        {
            return JoinOperator(context, errorPosition, lval, "");
        }

        /// <summary>
        /// Implementation of the PowerShell binary -join operator
        /// </summary>
        /// <param name="context">The execution context to use</param>
        /// <param name="errorPosition">The position to use for error reporting.</param>
        /// <param name="lval">left operand</param>
        /// <param name="rval">right operand</param>
        /// <returns>The result of the operator</returns>
        internal static object JoinOperator(ExecutionContext context, IScriptExtent errorPosition, object lval, object rval)
        {
            string separator = PSObject.ToStringParser(context, rval);

            // PSObject already has join functionality; just expose it
            // as an operator.
            IEnumerable enumerable = LanguagePrimitives.GetEnumerable(lval);
            if (enumerable != null)
            {
                return PSObject.ToStringEnumerable(context, enumerable, separator, null, null);
            }
            else
            {
                return PSObject.ToStringParser(context, lval);
            }
        }

        /// <summary>
        /// The implementation of the PowerShell -replace operator....
        /// </summary>
        /// <param name="context">The execution context in which to evaluate the expression</param>
        /// <param name="errorPosition">The position to use for error reporting.</param>
        /// <param name="lval">The object on which to replace the values</param>
        /// <param name="rval">The replacement description.</param>
        /// <param name="ignoreCase">True for -ireplace/-replace, false for -creplace</param>
        /// <returns>The result of the operator</returns>
        internal static object ReplaceOperator(ExecutionContext context, IScriptExtent errorPosition, object lval, object rval, bool ignoreCase)
        {
            string replacement = "";
            object pattern = "";

            rval = PSObject.Base(rval);
            IList rList = rval as IList;
            if (rList != null)
            {
                if (rList.Count > 2)
                {
                    // only allow 1 or 2 arguments to -replace
                    throw InterpreterError.NewInterpreterException(rval, typeof(RuntimeException), errorPosition,
                        "BadReplaceArgument", ParserStrings.BadReplaceArgument, ignoreCase ? "-ireplace" : "-replace", rList.Count);
                }

                if (rList.Count > 0)
                {
                    pattern = rList[0];
                    if (rList.Count > 1)
                    {
                        replacement = PSObject.ToStringParser(context, rList[1]);
                    }
                }
            }
            else
            {
                pattern = rval;
            }

            RegexOptions rreOptions = RegexOptions.None;

            if (ignoreCase)
            {
                rreOptions = RegexOptions.IgnoreCase;
            }

            Regex rr = pattern as Regex;
            if (rr == null)
            {
                try
                {
                    rr = NewRegex((string)PSObject.ToStringParser(context, pattern), rreOptions);
                }
                catch (ArgumentException ae)
                {
                    throw InterpreterError.NewInterpreterExceptionWithInnerException(pattern, typeof(RuntimeException),
                        null, "InvalidRegularExpression", ParserStrings.InvalidRegularExpression, ae, pattern);
                }
            }

            IEnumerator list = LanguagePrimitives.GetEnumerator(lval);
            if (list == null)
            {
                string lvalString = lval?.ToString() ?? String.Empty;

                // Find a single match in the string.
                return rr.Replace(lvalString, replacement);
            }
            else
            {
                List<object> resultList = new List<object>();
                while (ParserOps.MoveNext(context, errorPosition, list))
                {
                    string lvalString = PSObject.ToStringParser(context, ParserOps.Current(errorPosition, list));

                    resultList.Add(rr.Replace(lvalString, replacement));
                }

                return resultList.ToArray();
            }
        }

        /// <summary>
        /// Implementation of the PowerShell type operators...
        /// </summary>
        /// <param name="context">The execution context to use</param>
        /// <param name="errorPosition">The position to use for error reporting.</param>
        /// <param name="left">left operand</param>
        /// <param name="right">right operand</param>
        /// <returns>The result of the operator</returns>
        internal static object IsOperator(ExecutionContext context, IScriptExtent errorPosition, object left, object right)
        {
            object lval = PSObject.Base(left);
            object rval = PSObject.Base(right);

            Type rType = rval as Type;

            if (rType == null)
            {
                rType = ConvertTo<Type>(rval, errorPosition);

                if (rType == null)
                {
                    // "the right operand of '-is' must be a type"
                    throw InterpreterError.NewInterpreterException(rval, typeof(RuntimeException),
                        errorPosition, "IsOperatorRequiresType", ParserStrings.IsOperatorRequiresType);
                }
            }

            if (rType == typeof(PSCustomObject) && lval is PSObject)
            {
                Diagnostics.Assert(rType.IsInstanceOfType(((PSObject)lval).ImmediateBaseObject), "Unexpect PSObject");
                return _TrueObject;
            }

            if (rType.Equals(typeof(PSObject)) && left is PSObject)
            {
                return _TrueObject;
            }

            return BoolToObject(rType.IsInstanceOfType(lval));
        }

        /// <summary>
        /// Implementation of the PowerShell type operators...
        /// </summary>
        /// <param name="context">The execution context to use</param>
        /// <param name="errorPosition">The position to use for error reporting.</param>
        /// <param name="left">left operand</param>
        /// <param name="right">right operand</param>
        /// <returns>The result of the operator</returns>
        internal static object IsNotOperator(ExecutionContext context, IScriptExtent errorPosition, object left, object right)
        {
            object lval = PSObject.Base(left);
            object rval = PSObject.Base(right);

            Type rType = rval as Type;

            if (rType == null)
            {
                rType = ConvertTo<Type>(rval, errorPosition);

                if (rType == null)
                {
                    // "the right operand of '-is' must be a type"
                    throw InterpreterError.NewInterpreterException(rval, typeof(RuntimeException),
                        errorPosition, "IsOperatorRequiresType", ParserStrings.IsOperatorRequiresType);
                }
            }

            if (rType == typeof(PSCustomObject) && lval is PSObject)
            {
                Diagnostics.Assert(rType.IsInstanceOfType(((PSObject)lval).ImmediateBaseObject), "Unexpect PSObject");
                return _FalseObject;
            }

            if (rType.Equals(typeof(PSObject)) && left is PSObject)
            {
                return _FalseObject;
            }

            return BoolToObject(!rType.IsInstanceOfType(lval));
        }


        /// <summary>
        /// Implementation of the PowerShell -like operator
        /// </summary>
        /// <param name="context">The execution context to use</param>
        /// <param name="errorPosition">The position to use for error reporting.</param>
        /// <param name="lval">left operand</param>
        /// <param name="rval">right operand</param>
        /// <param name="operator">the operator</param>
        /// <returns>The result of the operator</returns>
        internal static object LikeOperator(ExecutionContext context, IScriptExtent errorPosition, object lval, object rval, TokenKind @operator)
        {
            var wcp = rval as WildcardPattern;
            if (wcp == null)
            {
                var ignoreCase = @operator == TokenKind.Ilike || @operator == TokenKind.Inotlike;
                wcp = WildcardPattern.Get(PSObject.ToStringParser(context, rval),
                    ignoreCase ? WildcardOptions.IgnoreCase : WildcardOptions.None);
            }

            bool notLike = @operator == TokenKind.Inotlike || @operator == TokenKind.Cnotlike;
            IEnumerator list = LanguagePrimitives.GetEnumerator(lval);
            if (list == null)
            {
                string lvalString = lval == null ? String.Empty : PSObject.ToStringParser(context, lval);

                return BoolToObject(wcp.IsMatch(lvalString) ^ notLike);
            }

            List<object> resultList = new List<object>();

            while (ParserOps.MoveNext(context, errorPosition, list))
            {
                object val = ParserOps.Current(errorPosition, list);

                string lvalString = val == null ? String.Empty : PSObject.ToStringParser(context, val);

                if (wcp.IsMatch(lvalString) ^ notLike)
                {
                    resultList.Add(lvalString);
                }
            }

            return resultList.ToArray();
        }

        /// <summary>
        /// Implementation of the PowerShell -match operator
        /// </summary>
        /// <param name="context">The execution context to use</param>
        /// <param name="errorPosition">The position to use for error reporting.</param>
        /// <param name="lval">left operand</param>
        /// <param name="rval">right operand</param>
        /// <param name="ignoreCase">ignore case?</param>
        /// <param name="notMatch">true for -notmatch, false for -match</param>
        /// <returns>The result of the operator</returns>
        internal static object MatchOperator(ExecutionContext context, IScriptExtent errorPosition, object lval, object rval, bool notMatch, bool ignoreCase)
        {
            RegexOptions reOptions = ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;

            // if passed an explicit regex, just use it
            // otherwise compile the expression.
            Regex r = PSObject.Base(rval) as Regex;
            if (r == null)
            {
                // In this situation, creation of Regex should not fail. We are not 
                // processing ArgumentException in this case. 
                r = NewRegex(PSObject.ToStringParser(context, rval), reOptions);
            }

            IEnumerator list = LanguagePrimitives.GetEnumerator(lval);
            if (list == null)
            {
                string lvalString = lval == null ? String.Empty : PSObject.ToStringParser(context, lval);

                // Find a match in the string.
                Match m = r.Match(lvalString);

                if (m.Success)
                {
                    GroupCollection groups = m.Groups;
                    if (groups.Count > 0)
                    {
                        Hashtable h = new Hashtable(StringComparer.CurrentCultureIgnoreCase);

                        foreach (string groupName in r.GetGroupNames())
                        {
                            Group g = groups[groupName];
                            if (g.Success)
                            {
                                int keyInt;

                                if (Int32.TryParse(groupName, out keyInt))
                                    h.Add(keyInt, g.ToString());
                                else
                                    h.Add(groupName, g.ToString());
                            }
                        }

                        context.SetVariable(SpecialVariables.MatchesVarPath, h);
                    }
                }

                return BoolToObject(m.Success ^ notMatch);
            }
            else
            {
                List<object> resultList = new List<object>();
                int check = 0;

                try
                {
                    while (list.MoveNext())
                    {
                        object val = list.Current;

                        string lvalString = val == null ? String.Empty : PSObject.ToStringParser(context, val);

                        // Find a single match in the string.
                        Match m = r.Match(lvalString);

                        if (m.Success ^ notMatch)
                        {
                            resultList.Add(val);
                        }
                        if (check++ > 1000)
                        {
                            // Check to see if we're stopping every one in a while...
                            if (context != null && context.CurrentPipelineStopping)
                                throw new PipelineStoppedException();
                            check = 0;
                        }
                    }

                    return resultList.ToArray();
                }
                catch (RuntimeException)
                {
                    throw;
                }
                catch (FlowControlException)
                {
                    throw;
                }
                catch (ScriptCallDepthException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    CommandProcessorBase.CheckForSevereException(e);
                    throw InterpreterError.NewInterpreterExceptionWithInnerException(list, typeof(RuntimeException),
                        errorPosition, "BadEnumeration", ParserStrings.BadEnumeration, e, e.Message);
                }
            }
        }

        // Implementation of the -contains/-in operators and case insensitive variants.
        internal static bool ContainsOperatorCompiled(ExecutionContext context,
                                                      CallSite<Func<CallSite, object, IEnumerator>> getEnumeratorSite,
                                                      CallSite<Func<CallSite, object, object, object>> comparerSite,
                                                      object left,
                                                      object right)
        {
            IEnumerator list = getEnumeratorSite.Target.Invoke(getEnumeratorSite, left);
            if (list == null || list is EnumerableOps.NonEnumerableObjectEnumerator)
            {
                return (bool)comparerSite.Target.Invoke(comparerSite, left, right);
            }

            while (EnumerableOps.MoveNext(context, list))
            {
                object val = EnumerableOps.Current(list);
                if ((bool)comparerSite.Target.Invoke(comparerSite, val, right))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Implementation of the PowerShell -contains/-notcontains operators (and case sensitive variants)
        /// </summary>
        /// <param name="context">The execution context to use</param>
        /// <param name="errorPosition">The position to use for error reporting.</param>
        /// <param name="left">left operand</param>
        /// <param name="right">right operand</param>
        /// <param name="ignoreCase">ignore case?</param>
        /// <param name="contains">true for -contains, false for -notcontains</param>
        /// <returns>The result of the operator</returns>
        internal static object ContainsOperator(ExecutionContext context, IScriptExtent errorPosition, object left, object right, bool contains, bool ignoreCase)
        {
            IEnumerator list = LanguagePrimitives.GetEnumerator(left);
            if (list == null)
            {
                return
                    BoolToObject(contains ==
                                 LanguagePrimitives.Equals(left, right, ignoreCase, CultureInfo.InvariantCulture));
            }

            while (ParserOps.MoveNext(context, errorPosition, list))
            {
                object val = ParserOps.Current(errorPosition, list);

                if (LanguagePrimitives.Equals(val, right, ignoreCase, CultureInfo.InvariantCulture))
                {
                    return BoolToObject(contains);
                }
            }

            return BoolToObject(!contains);
        }

        internal delegate bool CompareDelegate(object lhs, object rhs, bool ignoreCase);

        internal static object CompareOperators(ExecutionContext context, IScriptExtent errorPosition, object left, object right, CompareDelegate compareDelegate, bool ignoreCase)
        {
            IEnumerator list = LanguagePrimitives.GetEnumerator(left);
            if (list == null)
            {
                return BoolToObject(compareDelegate(left, right, ignoreCase));
            }

            List<object> resultList = new List<object>();
            while (ParserOps.MoveNext(context, errorPosition, list))
            {
                object val = ParserOps.Current(errorPosition, list);

                if (compareDelegate(val, right, ignoreCase))
                {
                    resultList.Add(val);
                }
            }

            return resultList.ToArray();
        }

        /// <summary>
        /// Cache regular expressions...
        /// </summary>
        /// <param name="patternString">The string to find the pattern for</param>
        /// <param name="options">The options used to create the regex...</param>
        /// <returns>A case-insensitive Regex...</returns>
        internal static Regex NewRegex(string patternString, RegexOptions options)
        {
            if (options != RegexOptions.IgnoreCase)
                return new Regex(patternString, options);

            lock (s_regexCache)
            {
                Regex result;
                if (s_regexCache.TryGetValue(patternString, out result))
                {
                    return result;
                }
                else
                {
                    if (s_regexCache.Count > MaxRegexCache)
                        s_regexCache.Clear();
                    Regex re = new Regex(patternString, RegexOptions.IgnoreCase);
                    s_regexCache.Add(patternString, re);
                    return re;
                }
            }
        }
        private static Dictionary<string, Regex> s_regexCache = new Dictionary<string, Regex>();
        private const int MaxRegexCache = 1000;

        /// <summary>
        /// A routine used to advance an enumerator and catch errors that might occur
        /// performing the operation
        /// </summary>
        /// <param name="context">The execution context used to see if the pipeline is stopping</param>
        /// <param name="errorPosition">The position to use for error reporting.</param>
        /// <param name="enumerator">THe enumerator to advance.</param>
        /// <exception cref="RuntimeException">An error occurred moving to the next element in the enumeration</exception>
        /// <returns>True if the move succeeded</returns>
        internal static bool MoveNext(ExecutionContext context, IScriptExtent errorPosition, IEnumerator enumerator)
        {
            try
            {
                // Check to see if we're stopping...
                if (context != null && context.CurrentPipelineStopping)
                    throw new PipelineStoppedException();

                return enumerator.MoveNext();
            }
            catch (RuntimeException)
            {
                throw;
            }
            catch (FlowControlException)
            {
                throw;
            }
            catch (ScriptCallDepthException)
            {
                throw;
            }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                throw InterpreterError.NewInterpreterExceptionWithInnerException(enumerator, typeof(RuntimeException),
                    errorPosition, "BadEnumeration", ParserStrings.BadEnumeration, e, e.Message);
            }
        }

        /// <summary>
        /// Wrapper caller for enumerator.MoveNext - handles and republishes errors...
        /// </summary>
        /// <param name="errorPosition">The position to use for error reporting.</param>
        /// <param name="enumerator">The enumerator to read from</param>
        /// <returns></returns>
        internal static object Current(IScriptExtent errorPosition, IEnumerator enumerator)
        {
            try
            {
                return enumerator.Current;
            }
            catch (RuntimeException)
            {
                throw;
            }
            catch (ScriptCallDepthException)
            {
                throw;
            }
            catch (FlowControlException)
            {
                throw;
            }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                throw InterpreterError.NewInterpreterExceptionWithInnerException(enumerator, typeof(RuntimeException),
                    errorPosition, "BadEnumeration", ParserStrings.BadEnumeration, e, e.Message);
            }
        }

        /// <summary>
        /// Retrieves the obj's type full name
        /// </summary>
        /// <param name="obj">the object we want to retrieve the type's full name from</param>
        /// <returns>The obj's type full name</returns>
        internal static string GetTypeFullName(object obj)
        {
            if (obj == null)
            {
                return String.Empty;
            }
            PSObject mshObj = obj as PSObject;
            if (mshObj == null)
            {
                return obj.GetType().FullName;
            }
            if (mshObj.InternalTypeNames.Count == 0)
            {
                return typeof(PSObject).FullName;
            }
            return mshObj.InternalTypeNames[0];
        }




        /// <summary>
        /// Launch a method on an object. This will handle .NET native methods, COM 
        /// methods and ScriptBlock notes. Native methods currently take precedence over notes...
        /// </summary>
        /// <param name="errorPosition">The position to use for error reporting.</param>
        /// <param name="target">The object to call the method on. It shouldn't be an msh object</param>
        /// <param name="methodName">The name of the method to call</param>
        /// <param name="invocationConstraints">Invocation constraints</param>
        /// <param name="paramArray">The arguments to pass to the method.</param>
        /// <param name="callStatic">Set to true if you want to call a static method.</param>
        /// <param name="valueToSet">If not automation null, then this must be a settable property</param>
        /// <exception cref="RuntimeException">Wraps the exception returned from the method call</exception>
        /// <exception cref="SessionStateOverflowException">The maximum scope depth would be exceeded</exception>
        /// <exception cref="FlowControlException">Internal exception from a flow control statement</exception>        
        /// <returns></returns>
        internal static object CallMethod(
            IScriptExtent errorPosition,
            object target,
            string methodName,
            PSMethodInvocationConstraints invocationConstraints,
            object[] paramArray,
            bool callStatic,
            object valueToSet)
        {
            Dbg.Assert(methodName != null, "methodName was null");

            PSMethodInfo targetMethod = null;
            object targetBase = null;
            PSObject targetAsPSObject = null;

            do
            {
                if (LanguagePrimitives.IsNull(target))
                {
                    // "you can't call a method on null"
                    throw InterpreterError.NewInterpreterException(methodName, typeof(RuntimeException), errorPosition, "InvokeMethodOnNull", ParserStrings.InvokeMethodOnNull);
                }
                targetBase = PSObject.Base(target);
                targetAsPSObject = PSObject.AsPSObject(target);

                Type targetType;
                if (callStatic)
                {
                    targetType = (Type)targetBase;
                }
                else
                {
                    targetType = targetBase.GetType();
                }

                if (callStatic)
                {
                    targetMethod = PSObject.GetStaticCLRMember(target, methodName) as PSMethod;
                }
                else
                {
                    targetMethod = targetAsPSObject.Members[methodName] as PSMethodInfo;
                }

                if (targetMethod == null)
                {
                    string typeFullName = null;
                    if (callStatic)
                    {
                        typeFullName = targetType.FullName;
                    }
                    else
                    {
                        typeFullName = GetTypeFullName(target);
                    }

                    if (valueToSet == AutomationNull.Value)
                    {
                        // "[{0}] doesn't contain a method named '{1}'"
                        throw InterpreterError.NewInterpreterException(methodName, typeof(RuntimeException), errorPosition,
                            MethodNotFoundErrorId, ParserStrings.MethodNotFound, typeFullName, methodName);
                    }
                    else
                    {
                        throw InterpreterError.NewInterpreterException(methodName, typeof(RuntimeException), errorPosition,
                            "ParameterizedPropertyAssignmentFailed", ParserStrings.ParameterizedPropertyAssignmentFailed, typeFullName, methodName);
                    }
                }
            } while (false);

            try
            {
                // If there is a property to set, then this is a multi-parameter property assignment
                // not really a method call.
                if (valueToSet != AutomationNull.Value)
                {
                    PSParameterizedProperty propertyToSet = targetMethod as PSParameterizedProperty;

                    if (propertyToSet == null)
                    {
                        throw InterpreterError.NewInterpreterException(methodName, typeof(RuntimeException), errorPosition,
                                                                       "ParameterizedPropertyAssignmentFailed", ParserStrings.ParameterizedPropertyAssignmentFailed, GetTypeFullName(target), methodName);
                    }
                    propertyToSet.InvokeSet(valueToSet, paramArray);
                    return valueToSet;
                }
                else
                {
                    PSMethod adaptedMethod = targetMethod as PSMethod;
                    if (adaptedMethod != null)
                    {
                        return adaptedMethod.Invoke(invocationConstraints, paramArray);
                    }
                    else
                    {
                        return targetMethod.Invoke(paramArray);
                    }
                }
            }
            catch (MethodInvocationException mie)
            {
                if (mie.ErrorRecord.InvocationInfo == null)
                    mie.ErrorRecord.SetInvocationInfo(new InvocationInfo(null, errorPosition));
                throw;
            }
            catch (RuntimeException rte)
            {
                if (rte.ErrorRecord.InvocationInfo == null)
                    rte.ErrorRecord.SetInvocationInfo(new InvocationInfo(null, errorPosition));
                throw;
            }
            catch (FlowControlException)
            {
                throw;
            }
            catch (ScriptCallDepthException)
            {
                throw;
            }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                // Note - we are catching all methods thrown from a method call and wrap them
                // unless they are already RuntimeException. This is ok.

                throw InterpreterError.NewInterpreterExceptionByMessage(typeof(RuntimeException), errorPosition,
                    e.Message, "MethodInvocationException", e);
            }
        } // CallMethod
    }

    #endregion ParserOps

    #region RangeEnumerator
    /// <summary>
    /// This is a simple enumerator class that just enumerates of a range of numbers. It's used in enumerating
    /// elements when the range operator .. is used.
    /// </summary>
    internal class RangeEnumerator : IEnumerator
    {
        private int _lowerBound;
        internal int LowerBound
        {
            get { return _lowerBound; }
        }
        private int _upperBound;
        internal int UpperBound
        {
            get { return _upperBound; }
        }

        private int _current;
        public object Current
        {
            get { return _current; }
        }

        internal int CurrentValue
        {
            get { return _current; }
        }

        private int _increment = 1;

        private bool _firstElement = true;

        public RangeEnumerator(int lowerBound, int upperBound)
        {
            _lowerBound = lowerBound;
            _current = _lowerBound;
            _upperBound = upperBound;
            if (lowerBound > upperBound)
                _increment = -1;
        }

        public void Reset()
        {
            _current = _lowerBound;
            _firstElement = true;
        }

        public bool MoveNext()
        {
            if (_firstElement)
            {
                _firstElement = false;
                return true;
            }
            if (_current == _upperBound)
                return false;

            _current += _increment;
            return true;
        }
    }
    #endregion RangeEnumerator

    #region InterpreterError
    internal static class InterpreterError
    {
        /// <summary>
        /// Create a new instance of an interpreter exception
        /// </summary>
        /// <param name="targetObject">The target object for this exception.</param>
        /// <param name="exceptionType">Type of exception to build</param>
        /// <param name="errorPosition">The position to use for error reporting.</param>
        /// <param name="resourceIdAndErrorId">
        /// ResourceID to look up template message, and also ErrorID
        /// </param>
        /// <param name="resourceString">
        /// Resource string that holds the error message
        /// </param>
        /// <param name="args">Insertion parameters to message</param>
        /// <returns>A new instance of the specified exception type</returns>
        internal static RuntimeException NewInterpreterException(object targetObject,
            Type exceptionType, IScriptExtent errorPosition, string resourceIdAndErrorId, string resourceString, params object[] args)
        {
            return NewInterpreterExceptionWithInnerException(targetObject, exceptionType, errorPosition, resourceIdAndErrorId, resourceString, null, args);
        }

        /// <summary>
        /// Create a new instance of an interpreter exception
        /// </summary>
        /// <param name="targetObject">The object associated with the problem</param>
        /// <param name="exceptionType">Type of exception to build</param>
        /// <param name="errorPosition">The position to use for error reporting.</param>
        /// <param name="resourceIdAndErrorId">
        /// ResourceID to look up template message, and also ErrorID
        /// </param>
        /// <param name="resourceString">
        /// Resource string which holds the error message
        /// </param>
        /// <param name="innerException">inner exception</param>
        /// <param name="args">Insertion parameters to message</param>
        /// <returns>New instance of an interpreter exception</returns>
        internal static RuntimeException NewInterpreterExceptionWithInnerException(object targetObject,
            Type exceptionType, IScriptExtent errorPosition, string resourceIdAndErrorId, string resourceString, Exception innerException, params object[] args)
        {
            // errToken may be null
            if (String.IsNullOrEmpty(resourceIdAndErrorId))
                throw PSTraceSource.NewArgumentException("resourceIdAndErrorId");
            // innerException may be null
            // args may be null or empty

            RuntimeException rte = null;

            try
            {
                string message;
                if (null == args || 0 == args.Length)
                {
                    // Don't format in case the string contains literal curly braces
                    message = resourceString;
                }
                else
                {
                    message = StringUtil.Format(resourceString, args);
                }
                if (String.IsNullOrEmpty(message))
                {
                    Dbg.Assert(false,
                        "Could not load text for parser exception '"
                        + resourceIdAndErrorId + "'");
                    rte = NewBackupInterpreterException(exceptionType, errorPosition, resourceIdAndErrorId, null);
                }
                else
                {
                    rte = NewInterpreterExceptionByMessage(exceptionType, errorPosition, message, resourceIdAndErrorId, innerException);
                }
            }
            catch (InvalidOperationException e)
            {
                Dbg.Assert(false,
                    "Could not load text for parser exception '"
                    + resourceIdAndErrorId
                    + "' due to InvalidOperationException " + e.Message);
                rte = NewBackupInterpreterException(exceptionType, errorPosition, resourceIdAndErrorId, e);
            }
            catch (System.Resources.MissingManifestResourceException e)
            {
                Dbg.Assert(false,
                    "Could not load text for parser exception '"
                    + resourceIdAndErrorId
                    + "' due to MissingManifestResourceException " + e.Message);
                rte = NewBackupInterpreterException(exceptionType, errorPosition, resourceIdAndErrorId, e);
            }
            catch (FormatException e)
            {
                Dbg.Assert(false,
                    "Could not load text for parser exception '"
                    + resourceIdAndErrorId
                    + "' due to FormatException " + e.Message);
                rte = NewBackupInterpreterException(exceptionType, errorPosition, resourceIdAndErrorId, e);
            }
            rte.SetTargetObject(targetObject);

            return rte;
        }

        /// <summary>
        /// Create a new instance of an interpreter exception
        /// </summary>
        /// <param name="exceptionType">Type of exception to build</param>
        /// <param name="errorPosition">The position to use for error reporting.</param>
        /// <param name="message">Message</param>
        /// <param name="errorId">ErrorID</param>
        /// <param name="innerException">inner exception</param>
        /// <returns>New instance of ParseException</returns>
        internal static RuntimeException NewInterpreterExceptionByMessage(
            Type exceptionType, IScriptExtent errorPosition, string message, string errorId, Exception innerException)
        {
            // errToken may be null
            // only assert -- be permissive at runtime
            Dbg.Assert(!String.IsNullOrEmpty(message), "message was null or empty");
            Dbg.Assert(!String.IsNullOrEmpty(errorId), "errorId was null or empty");
            // innerException may be null

            RuntimeException e;

            // Create an instance of the right exception type...
            if (exceptionType == typeof(ParseException))
            {
                e = new ParseException(message, errorId, innerException);
            }
            else if (exceptionType == typeof(IncompleteParseException))
            {
                e = new IncompleteParseException(message, errorId, innerException);
            }
            else
            {
                e = new RuntimeException(message, innerException);
                e.SetErrorId(errorId);
                e.SetErrorCategory(ErrorCategory.InvalidOperation);
            }

            // Don't trash the existing InvocationInfo.
            if (errorPosition != null)
                e.ErrorRecord.SetInvocationInfo(new InvocationInfo(null, errorPosition));
            return e;
        }

        private static RuntimeException NewBackupInterpreterException(
            Type exceptionType,
            IScriptExtent errorPosition,
            string errorId,
            Exception innerException)
        {
            string message;
            if (null == innerException)
            {
                // there is no reason this string lookup should fail
                message = StringUtil.Format(ParserStrings.BackupParserMessage, errorId);
            }
            else
            {
                // there is no reason this string lookup should fail
                message = StringUtil.Format(ParserStrings.BackupParserMessageWithException, errorId, innerException.Message);
            }

            return NewInterpreterExceptionByMessage(exceptionType, errorPosition, message, errorId, innerException);
        }

        internal static void UpdateExceptionErrorRecordPosition(Exception exception, IScriptExtent extent)
        {
            if (extent == null || extent == PositionUtilities.EmptyExtent)
            {
                return;
            }

            var icer = exception as IContainsErrorRecord;
            if (icer != null)
            {
                var errorRecord = icer.ErrorRecord;
                var invocationInfo = errorRecord.InvocationInfo;
                if (invocationInfo == null)
                {
                    errorRecord.SetInvocationInfo(new InvocationInfo(null, extent));
                }
                else if (invocationInfo.ScriptPosition == null || invocationInfo.ScriptPosition == PositionUtilities.EmptyExtent)
                {
                    invocationInfo.ScriptPosition = extent;
                    errorRecord.LockScriptStackTrace();
                }
            }
        }
    }
    #endregion InterpreterError

    #region ScriptTrace
    internal static class ScriptTrace
    {
        internal static void Trace(int level, string messageId, string resourceString, params object[] args)
        {
            // Need access to the execution context to see if we should trace. If we
            // can't get it, then just return...
            ExecutionContext context = LocalPipeline.GetExecutionContextFromTLS();
            if (context == null)
                return;
            Trace(context, level, messageId, resourceString, args);
        }

        internal static void Trace(ExecutionContext context, int level, string messageId, string resourceString, params object[] args)
        {
            ActionPreference pref = ActionPreference.Continue;

            if (context.PSDebugTraceLevel > level)
            {
                string message;
                if (null == args || 0 == args.Length)
                {
                    // Don't format in case the string contains literal curly braces
                    message = resourceString;
                }
                else
                {
                    message = StringUtil.Format(resourceString, args);
                }
                if (String.IsNullOrEmpty(message))
                {
                    message = "Could not load text for msh script tracing message id '" + messageId + "'";
                    Dbg.Assert(false, message);
                }

                ((InternalHostUserInterface)context.EngineHostInterface.UI).WriteDebugLine(message, ref pref);
            }
        }
    }
    #endregion ScriptTrace
}

