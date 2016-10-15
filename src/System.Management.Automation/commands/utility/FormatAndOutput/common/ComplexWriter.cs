/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using System.Globalization;

namespace Microsoft.PowerShell.Commands.Internal.Format
{
    /// <summary>
    /// writer class to handle Complex Object formatting
    /// </summary>
    internal sealed class ComplexWriter
    {
        /// <summary>
        /// initialization method to be called before any other operation
        /// </summary>
        /// <param name="lineOutput">LineOutput interfaces to write to</param>
        /// <param name="numberOfTextColumns">number of columns used to write out</param>
        internal void Initialize(LineOutput lineOutput, int numberOfTextColumns)
        {
            _lo = lineOutput;
            _textColumns = numberOfTextColumns;
        }

        /// <summary>
        /// Writes a string
        /// </summary>
        /// <param name="s"></param>
        internal void WriteString(string s)
        {
            _indentationManager.Clear();

            AddToBuffer(s);

            WriteToScreen();
        }

        /// <summary>
        /// it interprets a list of format value tokens and outputs it
        /// </summary>
        /// <param name="formatValueList">list of FormatValue tokens to interpret</param>
        internal void WriteObject(List<FormatValue> formatValueList)
        {
            // we always start with no indentation
            _indentationManager.Clear();

            foreach (FormatEntry fe in formatValueList)
            {
                // operate on each directive inside the list,
                // carrying the indentation from invocation to invocation
                GenerateFormatEntryDisplay(fe, 0);
            }
            // make sure that, if we have pending text in the buffer it gets flushed
            WriteToScreen();
        }

        /// <summary>
        /// operate on a single entry
        /// </summary>
        /// <param name="fe">entry to process</param>
        /// <param name="currentDepth">current depth of recursion</param>
        private void GenerateFormatEntryDisplay(FormatEntry fe, int currentDepth)
        {
            foreach (object obj in fe.formatValueList)
            {
                FormatEntry feChild = obj as FormatEntry;
                if (feChild != null)
                {
                    if (currentDepth < maxRecursionDepth)
                    {
                        if (feChild.frameInfo != null)
                        {
                            // if we have frame information, we need to push it on the
                            // indentation stack
                            using (_indentationManager.StackFrame(feChild.frameInfo))
                            {
                                GenerateFormatEntryDisplay(feChild, currentDepth + 1);
                            }
                        }
                        else
                        {
                            // no need here of activating an indentation stack frame
                            GenerateFormatEntryDisplay(feChild, currentDepth + 1);
                        }
                    }
                    continue;
                }
                if (obj is FormatNewLine)
                {
                    this.WriteToScreen();
                    continue;
                }
                FormatTextField ftf = obj as FormatTextField;
                if (ftf != null)
                {
                    this.AddToBuffer(ftf.text);
                    continue;
                }
                FormatPropertyField fpf = obj as FormatPropertyField;
                if (fpf != null)
                {
                    this.AddToBuffer(fpf.propertyValue);
                }
            }
        }

        /// <summary>
        /// add a string to the current buffer, waiting for a FlushBuffer()
        /// </summary>
        /// <param name="s">string to add to buffer</param>
        private void AddToBuffer(string s)
        {
            _stringBuffer.Append(s);
        }

        /// <summary>
        /// write to the output interface
        /// </summary>
        private void WriteToScreen()
        {
            int leftIndentation = _indentationManager.LeftIndentation;
            int rightIndentation = _indentationManager.RightIndentation;
            int firstLineIndentation = _indentationManager.FirstLineIndentation;

            // VALIDITY CHECKS:

            // check the useful ("active") width
            int usefulWidth = _textColumns - rightIndentation - leftIndentation;
            if (usefulWidth <= 0)
            {
                // fatal error, there is nothing to write to the device
                // just clear the buffer and return
                _stringBuffer = new StringBuilder();
            }

            // check indentation or hanging is not larger than the active width
            int indentationAbsoluteValue = (firstLineIndentation > 0) ? firstLineIndentation : -firstLineIndentation;
            if (indentationAbsoluteValue >= usefulWidth)
            {
                // valu too big, we reset it to zero
                firstLineIndentation = 0;
            }


            // compute the first line indentation or hanging
            int firstLineWidth = _textColumns - rightIndentation - leftIndentation;
            int followingLinesWidth = firstLineWidth;

            if (firstLineIndentation >= 0)
            {
                // the first line has an indentation
                firstLineWidth -= firstLineIndentation;
            }
            else
            {
                // the first line is hanging
                followingLinesWidth += firstLineIndentation;
            }

            //error checking on invalid values

            // generate the lines using the computed widths
            StringCollection sc = StringManipulationHelper.GenerateLines(_lo.DisplayCells, _stringBuffer.ToString(),
                                        firstLineWidth, followingLinesWidth);

            // compute padding
            int firstLinePadding = leftIndentation;
            int followingLinesPadding = leftIndentation;
            if (firstLineIndentation >= 0)
            {
                // the first line has an indentation
                firstLinePadding += firstLineIndentation;
            }
            else
            {
                // the first line is hanging
                followingLinesPadding -= firstLineIndentation;
            }

            // now write the lines on the screen
            bool firstLine = true;
            foreach (string s in sc)
            {
                if (firstLine)
                {
                    firstLine = false;
                    _lo.WriteLine(StringManipulationHelper.PadLeft(s, firstLinePadding));
                }
                else
                {
                    _lo.WriteLine(StringManipulationHelper.PadLeft(s, followingLinesPadding));
                }
            }

            _stringBuffer = new StringBuilder();
        }

        /// <summary>
        /// helper object to manage the frame-based indentation and margins
        /// </summary>
        private IndentationManager _indentationManager = new IndentationManager();

        /// <summary>
        /// buffer to accumulate partially constructed text
        /// </summary>
        private StringBuilder _stringBuffer = new StringBuilder();

        /// <summary>
        /// interface to write to
        /// </summary>
        private LineOutput _lo;

        /// <summary>
        /// number of columns for the output device
        /// </summary>
        private int _textColumns;

        private const int maxRecursionDepth = 50;
    }


    internal sealed class IndentationManager
    {
        private sealed class IndentationStackFrame : IDisposable
        {
            internal IndentationStackFrame(IndentationManager mgr)
            {
                _mgr = mgr;
            }

            public void Dispose()
            {
                if (_mgr != null)
                {
                    _mgr.RemoveStackFrame();
                }
            }

            private IndentationManager _mgr;
        }

        internal void Clear()
        {
            _frameInfoStack.Clear();
        }

        internal IDisposable StackFrame(FrameInfo frameInfo)
        {
            IndentationStackFrame frame = new IndentationStackFrame(this);
            _frameInfoStack.Push(frameInfo);
            return frame;
        }

        private void RemoveStackFrame()
        {
            _frameInfoStack.Pop();
        }

        internal int RightIndentation
        {
            get
            {
                return ComputeRightIndentation();
            }
        }

        internal int LeftIndentation
        {
            get
            {
                return ComputeLeftIndentation();
            }
        }

        internal int FirstLineIndentation
        {
            get
            {
                if (_frameInfoStack.Count == 0)
                    return 0;
                return _frameInfoStack.Peek().firstLine;
            }
        }


        private int ComputeRightIndentation()
        {
            int val = 0;
            foreach (FrameInfo fi in _frameInfoStack)
            {
                val += fi.rightIndentation;
            }
            return val;
        }

        private int ComputeLeftIndentation()
        {
            int val = 0;
            foreach (FrameInfo fi in _frameInfoStack)
            {
                val += fi.leftIndentation;
            }
            return val;
        }

        private Stack<FrameInfo> _frameInfoStack = new Stack<FrameInfo>();
    }

    /// <summary>
    /// Result of GetWords
    /// </summary>
    internal struct GetWordsResult
    {
        internal string Word;
        internal string Delim;
    }

    /// <summary>
    /// collection of helper functions for string formatting
    /// </summary>
    internal sealed class StringManipulationHelper
    {
        private static readonly char s_softHyphen = '\u00AD';
        private static readonly char s_hardHyphen = '\u2011';
        private static readonly char s_nonBreakingSpace = '\u00A0';
        private static Collection<string> s_cultureCollection = new Collection<string>();

        static StringManipulationHelper()
        {
            s_cultureCollection.Add("en");        // English
            s_cultureCollection.Add("fr");        // French
            s_cultureCollection.Add("de");        // German
            s_cultureCollection.Add("it");        // Italian
            s_cultureCollection.Add("pt");        // Portuguese
            s_cultureCollection.Add("es");        // Spanish
        }

        /// <summary>
        /// Breaks a string into a collection of words
        /// TODO: we might be able to improve this function in the future
        /// so that we do not break paths etc.
        /// </summary>
        /// <param name="s">input string</param>
        /// <returns>a collection of words</returns>
        private static IEnumerable<GetWordsResult> GetWords(string s)
        {
            StringBuilder sb = new StringBuilder();
            GetWordsResult result = new GetWordsResult();

            for (int i = 0; i < s.Length; i++)
            {
                // Soft hyphen = \u00AD - Should break, and add a hyphen if needed. If not needed for a break, hyphen should be absent
                if (s[i] == ' ' || s[i] == '\t' || s[i] == s_softHyphen)
                {
                    result.Word = sb.ToString();
                    sb.Clear();
                    result.Delim = new String(s[i], 1);

                    yield return result;
                }
                // Non-breaking space = \u00A0 - ideally shouldn't wrap
                // Hard hyphen = \u2011 - Should not break
                else if (s[i] == s_hardHyphen || s[i] == s_nonBreakingSpace)
                {
                    result.Word = sb.ToString();
                    sb.Clear();
                    result.Delim = String.Empty;

                    yield return result;
                }
                else
                {
                    sb.Append(s[i]);
                }
            }

            result.Word = sb.ToString();
            result.Delim = String.Empty;

            yield return result;
        }

        internal static StringCollection GenerateLines(DisplayCells displayCells, string val, int firstLineLen, int followingLinesLen)
        {
            if (s_cultureCollection.Contains(CultureInfo.CurrentCulture.TwoLetterISOLanguageName))
            {
                return GenerateLinesWithWordWrap(displayCells, val, firstLineLen, followingLinesLen);
            }
            else
            {
                return GenerateLinesWithoutWordWrap(displayCells, val, firstLineLen, followingLinesLen);
            }
        }

        private static StringCollection GenerateLinesWithoutWordWrap(DisplayCells displayCells, string val, int firstLineLen, int followingLinesLen)
        {
            StringCollection retVal = new StringCollection();

            if (string.IsNullOrEmpty(val))
            {
                // if null or empty, just add and we are done
                retVal.Add(val);
                return retVal;
            }

            // break string on newlines and process each line separately
            string[] lines = SplitLines(val);

            for (int k = 0; k < lines.Length; k++)
            {
                if (lines[k] == null || displayCells.Length(lines[k]) <= firstLineLen)
                {
                    // we do not need to split further, just add
                    retVal.Add(lines[k]);
                    continue;
                }

                // the string does not fit, so we have to wrap around on multiple lines
                // for each of these lines in the string, the first line will have
                // a (potentially) different length (indentation or hanging)

                // for each line, start a new state
                SplitLinesAccumulator accumulator = new SplitLinesAccumulator(retVal, firstLineLen, followingLinesLen);

                int offset = 0; // offset into the line we are splitting

                while (true)
                {
                    // acquire the current active display line length (it can very from call to call)
                    int currentDisplayLen = accumulator.ActiveLen;

                    // determine if the current tail would fit or not

                    // for the remaining part of the string, determine its display cell count
                    int currentCellsToFit = displayCells.Length(lines[k], offset);

                    // determine if we fit into the line
                    int excessCells = currentCellsToFit - currentDisplayLen;

                    if (excessCells > 0)
                    {
                        // we are not at the end of the string, select a sub string
                        // that would fit in the remaining display length
                        int charactersToAdd = displayCells.GetHeadSplitLength(lines[k], offset, currentDisplayLen);

                        if (charactersToAdd <= 0)
                        {
                            // corner case: we have a two cell character and the current
                            // display length is one.
                            // add a single cell arbitrary character instead of the original
                            // one and keep going
                            charactersToAdd = 1;
                            accumulator.AddLine("?");
                        }
                        else
                        {
                            // of the given length, add it to the accumulator
                            accumulator.AddLine(lines[k].Substring(offset, charactersToAdd));
                        }

                        // increase the offset by the # of characters added
                        offset += charactersToAdd;
                    }
                    else
                    {
                        // we reached the last (partial) line, we add it all
                        accumulator.AddLine(lines[k].Substring(offset));
                        break;
                    }
                }
            }

            return retVal;
        }

        private sealed class SplitLinesAccumulator
        {
            internal SplitLinesAccumulator(StringCollection retVal, int firstLineLen, int followingLinesLen)
            {
                _retVal = retVal;
                _firstLineLen = firstLineLen;
                _followingLinesLen = followingLinesLen;
            }

            internal void AddLine(string s)
            {
                if (!_addedFirstLine)
                {
                    _addedFirstLine = true;
                }
                _retVal.Add(s);
            }

            internal int ActiveLen
            {
                get
                {
                    if (_addedFirstLine)
                        return _followingLinesLen;
                    return _firstLineLen;
                }
            }

            private StringCollection _retVal;
            private bool _addedFirstLine;
            private int _firstLineLen;
            private int _followingLinesLen;
        }

        private static StringCollection GenerateLinesWithWordWrap(DisplayCells displayCells, string val, int firstLineLen, int followingLinesLen)
        {
            StringCollection retVal = new StringCollection();

            if (string.IsNullOrEmpty(val))
            {
                // if null or empty, just add and we are done
                retVal.Add(val);
                return retVal;
            }

            // break string on newlines and process each line separately
            string[] lines = SplitLines(val);

            for (int k = 0; k < lines.Length; k++)
            {
                if (lines[k] == null || displayCells.Length(lines[k]) <= firstLineLen)
                {
                    // we do not need to split further, just add
                    retVal.Add(lines[k]);
                    continue;
                }

                int spacesLeft = firstLineLen;
                int lineWidth = firstLineLen;
                bool firstLine = true;
                StringBuilder singleLine = new StringBuilder();

                foreach (GetWordsResult word in GetWords(lines[k]))
                {
                    string wordToAdd = word.Word;

                    // Handle soft hyphen
                    if (word.Delim == s_softHyphen.ToString())
                    {
                        int wordWidthWithHyphen = displayCells.Length(wordToAdd) + displayCells.Length(s_softHyphen.ToString());

                        // Add hyphen only if necessary
                        if (wordWidthWithHyphen == spacesLeft)
                        {
                            wordToAdd += "-";
                        }
                    }
                    else
                    {
                        if (!String.IsNullOrEmpty(word.Delim))
                        {
                            wordToAdd += word.Delim;
                        }
                    }

                    int wordWidth = displayCells.Length(wordToAdd);

                    // Handle zero width
                    if (lineWidth == 0)
                    {
                        if (firstLine)
                        {
                            firstLine = false;
                            lineWidth = followingLinesLen;
                        }

                        if (lineWidth == 0)
                        {
                            break;
                        }

                        spacesLeft = lineWidth;
                    }

                    // Word is wider than a single line
                    if (wordWidth > lineWidth)
                    {
                        foreach (char c in wordToAdd)
                        {
                            char charToAdd = c;
                            int charWidth = displayCells.Length(c);

                            // corner case: we have a two cell character and the current
                            // display length is one.
                            // add a single cell arbitrary character instead of the original
                            // one and keep going
                            if (charWidth > lineWidth)
                            {
                                charToAdd = '?';
                                charWidth = 1;
                            }

                            if (charWidth > spacesLeft)
                            {
                                retVal.Add(singleLine.ToString());
                                singleLine.Clear();
                                singleLine.Append(charToAdd);

                                if (firstLine)
                                {
                                    firstLine = false;
                                    lineWidth = followingLinesLen;
                                }

                                spacesLeft = lineWidth - charWidth;
                            }
                            else
                            {
                                singleLine.Append(charToAdd);
                                spacesLeft -= charWidth;
                            }
                        }
                    }
                    else
                    {
                        if (wordWidth > spacesLeft)
                        {
                            retVal.Add(singleLine.ToString());
                            singleLine.Clear();
                            singleLine.Append(wordToAdd);

                            if (firstLine)
                            {
                                firstLine = false;
                                lineWidth = followingLinesLen;
                            }

                            spacesLeft = lineWidth - wordWidth;
                        }
                        else
                        {
                            singleLine.Append(wordToAdd);
                            spacesLeft -= wordWidth;
                        }
                    }
                }

                retVal.Add(singleLine.ToString());
            }

            return retVal;
        }

        /// <summary>
        /// split a multiline string into an array of strings
        /// by honoring both \n and \r\n
        /// </summary>
        /// <param name="s">string to split</param>
        /// <returns>string array with the values</returns>
        internal static string[] SplitLines(string s)
        {
            if (string.IsNullOrEmpty(s))
                return new string[1] { s };

            StringBuilder sb = new StringBuilder();

            foreach (char c in s)
            {
                if (c != '\r')
                    sb.Append(c);
            }

            return sb.ToString().Split(s_newLineChar);
        }

#if false
        internal static string StripNewLines (string s)
        {
            if (string.IsNullOrEmpty (s))
                return s;

            string[] lines = SplitLines (s);

            if (lines.Length == 0)
                return null;

            if (lines.Length == 1)
                return lines[0];

            StringBuilder sb = new StringBuilder ();

            for (int k = 0; k < lines.Length; k++)
            {
                if (k == 0)
                    sb.Append (lines[k]);
                else
                    sb.Append (" " + lines[k]);
            }

            return sb.ToString ();
        }
#endif
        internal static string TruncateAtNewLine(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;

            int lineBreak = s.IndexOfAny(s_lineBreakChars);

            if (lineBreak < 0)
                return s;

            return s.Substring(0, lineBreak) + PSObjectHelper.ellipses;
        }

        internal static string PadLeft(string val, int count)
        {
            return new string(' ', count) + val;
        }

        private static readonly char[] s_newLineChar = new char[] { '\n' };
        private static readonly char[] s_lineBreakChars = new char[] { '\n', '\r' };
    }
}

