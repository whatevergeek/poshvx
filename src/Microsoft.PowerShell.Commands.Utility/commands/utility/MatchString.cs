/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Text.RegularExpressions;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Internal;
using System.Globalization;
using System.Diagnostics.CodeAnalysis;

#if CORECLR
// Use stub for ICloneable
using Microsoft.PowerShell.CoreClr.Stubs;
#endif

namespace Microsoft.PowerShell.Commands
{
    /// <summary>
    /// Context information about a match.
    /// </summary>
    public sealed class MatchInfoContext : ICloneable
    {
        internal MatchInfoContext()
        {
        }

        /// <summary>
        /// Lines found before a match.
        /// </summary>

        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public string[] PreContext { get; set; }

        /// <summary>
        /// Lines found after a match.
        /// </summary>

        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public string[] PostContext { get; set; }

        /// <summary>
        /// Lines found before a match. Does not include
        /// overlapping context and thus can be used to
        /// display contiguous match regions.
        /// </summary>

        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public string[] DisplayPreContext { get; set; }

        /// <summary>
        /// Lines found after a match. Does not include
        /// overlapping context and thus can be used to
        /// display contiguous match regions.
        /// </summary>

        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public string[] DisplayPostContext { get; set; }

        /// <summary>
        /// Produce a deep copy of this object.
        /// </summary>
        public object Clone()
        {
            MatchInfoContext clone = new MatchInfoContext();
            clone.PreContext = (clone.PreContext != null) ? (string[])PreContext.Clone() : null;
            clone.PostContext = (clone.PostContext != null) ? (string[])PostContext.Clone() : null;
            clone.DisplayPreContext = (clone.DisplayPreContext != null) ? (string[])DisplayPreContext.Clone() : null;
            clone.DisplayPostContext = (clone.DisplayPostContext != null) ? (string[])DisplayPostContext.Clone() : null;
            return clone;
        }
    }

    /// <summary>
    /// The object returned by select-string representing the result of a match.
    /// </summary>
    public class MatchInfo
    {
        private static string s_inputStream = "InputStream";

        /// <summary>
        /// Indicates if the match was done ignoring case.
        /// </summary>
        /// <value>True if case was ignored.</value>
        public bool IgnoreCase { get; set; }

        /// <summary>
        /// Returns the number of the matching line.
        /// </summary>
        /// <value>The number of the matching line.</value>
        public int LineNumber { get; set; }

        /// <summary>
        /// Returns the text of the matching line.
        /// </summary>
        /// <value>The text of the matching line.</value>
        public string Line { get; set; } = "";

        /// <summary>
        /// Returns the base name of the file containing the matching line.
        /// <remarks>
        /// It will be the string "InputStream" if the object came from the input stream.
        /// This is a readonly property calculated from the path. <see cref="Path"/>
        /// </remarks>
        /// </summary>
        /// <value>The file name</value>
        public string Filename
        {
            get
            {
                if (!_pathSet)
                    return s_inputStream;
                return _filename ?? (_filename = System.IO.Path.GetFileName(_path));
            }
        }
        private string _filename;


        /// <summary>
        /// The full path of the file containing the matching line.
        /// <remarks>
        /// It will be "InputStream" if the object came from the input stream.
        /// </remarks>
        /// </summary>
        /// <value>The path name</value>
        public string Path
        {
            get
            {
                if (!_pathSet)
                    return s_inputStream;
                return _path;
            }
            set
            {
                _path = value;
                _pathSet = true;
            }
        }
        private string _path = s_inputStream;
        private bool _pathSet;

        /// <summary>
        /// Returns the pattern that was used in the match.
        /// </summary>
        /// <value>The pattern string</value>
        public string Pattern { get; set; }

        /// <summary>
        /// The context for the match, or null if -context was not
        /// specified.
        /// </summary>
        public MatchInfoContext Context { get; set; }

        /// <summary>
        /// Returns the path of the matching file truncated relative to the <paramref name="directory"/> parameter.
        /// <remarks>
        /// For example, if the matching path was c:\foo\bar\baz.c and the directory argument was c:\foo
        /// the routine would return bar\baz.c
        /// </remarks>
        /// </summary>
        /// <param name="directory">The directory base the truncation on.</param>
        /// <returns>The relative path that was produced.</returns>
        public string RelativePath(string directory)
        {
            if (!_pathSet)
                return this.Path;

            string relPath = _path;
            if (!String.IsNullOrEmpty(directory))
            {
                if (relPath.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
                {
                    int offset = directory.Length;
                    if (offset < relPath.Length)
                    {
                        if (directory[offset - 1] == '\\' || directory[offset - 1] == '/')
                            relPath = relPath.Substring(offset);
                        else if (relPath[offset] == '\\' || relPath[offset] == '/')
                            relPath = relPath.Substring(offset + 1);
                    }
                }
            }
            return relPath;
        }

        private const string MatchFormat = "{0}{1}:{2}:{3}";
        private const string SimpleFormat = "{0}{1}";

        // Prefixes used by formatting: Match and Context prefixes
        // are used when context-tracking is enabled, otherwise
        // the empty prefix is used.
        private const string MatchPrefix = "> ";
        private const string ContextPrefix = "  ";
        private const string EmptyPrefix = "";

        /// <summary>
        /// Returns the string representation of this object. The format
        /// depends on whether a path has been set for this object or not.
        /// <remarks>
        /// If the path component is set, as would be the case when matching
        /// in a file, ToString() would return the path, line number and line text.
        /// If path is not set, then just the line text is presented.
        /// </remarks>
        /// </summary>
        /// <returns>The string representation of the match object</returns>
        public override string ToString()
        {
            return ToString(null);
        }

        /// <summary>
        /// Returns the string representation of the match object same format as ToString()
        /// but trims the path to be relative to the <paramref name="directory"/> argument.
        /// </summary>
        /// <param name="directory">Directory to use as the root when calculating the relative path</param>
        /// <returns>The string representation of the match object</returns>
        public string ToString(string directory)
        {
            string displayPath = (directory != null) ? RelativePath(directory) : _path;

            // Just return a single line if the user didn't
            // enable context-tracking.
            if (Context == null)
            {
                return FormatLine(Line, this.LineNumber, displayPath, EmptyPrefix);
            }

            // Otherwise, render the full context.
            List<string> lines = new List<string>(Context.DisplayPreContext.Length + Context.DisplayPostContext.Length + 1);

            int displayLineNumber = this.LineNumber - Context.DisplayPreContext.Length;
            foreach (string contextLine in Context.DisplayPreContext)
            {
                lines.Add(FormatLine(contextLine, displayLineNumber++, displayPath, ContextPrefix));
            }

            lines.Add(FormatLine(Line, displayLineNumber++, displayPath, MatchPrefix));

            foreach (string contextLine in Context.DisplayPostContext)
            {
                lines.Add(FormatLine(contextLine, displayLineNumber++, displayPath, ContextPrefix));
            }

            return String.Join(System.Environment.NewLine, lines.ToArray());
        }

        /// <summary>
        /// Formats a line for use in ToString.
        /// </summary>
        /// <param name="lineStr">The line to format.</param>
        /// <param name="displayLineNumber">The line number to display.</param>
        /// <param name="displayPath">The file path, formatted for display.</param>
        /// <param name="prefix">The match prefix.</param>
        /// <returns>The formatted line as a string.</returns>
        private string FormatLine(string lineStr, int displayLineNumber, string displayPath, string prefix)
        {
            if (_pathSet)
                return StringUtil.Format(MatchFormat, prefix, displayPath, displayLineNumber, lineStr);
            else
                return StringUtil.Format(SimpleFormat, prefix, lineStr);
        }

        /// <summary>
        /// A list of all Regex matches on the matching line.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public Match[] Matches { get; set; } = new Match[] { };

        /// <summary>
        /// Create a deep copy of this MatchInfo instance.
        /// </summary>
        internal MatchInfo Clone()
        {
            // Just do a shallow copy and then deep-copy the
            // fields that need it.
            MatchInfo clone = (MatchInfo)this.MemberwiseClone();

            if (clone.Context != null)
            {
                clone.Context = (MatchInfoContext)clone.Context.Clone();
            }

            // Regex match objects are immutable, so we can get away
            // with just copying the array.
            clone.Matches = (Match[])clone.Matches.Clone();

            return clone;
        }
    }

    /// <summary>
    /// A cmdlet to search through strings and files for particular patterns.
    /// </summary>
    [Cmdlet("Select", "String", DefaultParameterSetName = "File", HelpUri = "http://go.microsoft.com/fwlink/?LinkID=113388")]
    [OutputType(typeof(MatchInfo), typeof(bool))]
    public sealed class SelectStringCommand : PSCmdlet
    {
        /// <summary>
        /// A generic circular buffer.
        /// </summary>
        private class CircularBuffer<T> : ICollection<T>
        {
            // Ring of items
            private T[] _items;
            // Current length, as opposed to the total capacity
            // Current start of the list. Starts at 0, but may
            // move forwards or wrap around back to 0 due to
            // rotation.
            private int _firstIndex;

            /// <summary>
            /// Construct a new buffer of the specified capacity.
            /// </summary>
            /// <param name="capacity">The maximum capacity of the buffer.</param>
            /// <exception cref="ArgumentOutOfRangeException">If <paramref name="capacity" /> is negative.</exception>
            public CircularBuffer(int capacity)
            {
                if (capacity < 0)
                    throw new ArgumentOutOfRangeException("capacity");

                _items = new T[capacity];
                Clear();
            }

            /// <summary>
            /// The maximum capacity of the buffer. If more items
            /// are added than the buffer has capacity for, then
            /// older items will be removed from the buffer with
            /// a first-in, first-out policy.
            /// </summary>
            public int Capacity
            {
                get
                {
                    return _items.Length;
                }
            }

            /// <summary>
            /// Whether or not the buffer is at capacity.
            /// </summary>
            public bool IsFull
            {
                get
                {
                    return Count == Capacity;
                }
            }

            /// <summary>
            /// Convert from a 0-based index to a buffer index which
            /// has been properly offset and wrapped.
            /// </summary>
            /// <param name="zeroBasedIndex">The index to wrap.</param>
            /// <exception cref="ArgumentOutOfRangeException">If <paramref name="zeroBasedIndex" /> is out of range.</exception>
            /// <returns>
            /// The actual index that <param ref="zeroBasedIndex" />
            /// maps to.
            /// </returns>
            private int WrapIndex(int zeroBasedIndex)
            {
                if (Capacity == 0 || zeroBasedIndex < 0)
                {
                    throw new ArgumentOutOfRangeException("zeroBasedIndex");
                }

                return (zeroBasedIndex + _firstIndex) % Capacity;
            }

            #region IEnumerable<T> implementation.
            public IEnumerator<T> GetEnumerator()
            {
                for (int i = 0; i < Count; i++)
                {
                    yield return _items[WrapIndex(i)];
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return (IEnumerator)GetEnumerator();
            }
            #endregion

            #region ICollection<T> implementation
            public int Count { get; private set; }

            public bool IsReadOnly
            {
                get
                {
                    return false;
                }
            }

            /// <summary>
            /// Adds an item to the buffer. If the buffer is already
            /// full, the oldest item in the list will be removed,
            /// and the new item added at the logical end of the list.
            /// </summary>
            /// <param name="item">The item to add.</param>
            public void Add(T item)
            {
                if (Capacity == 0)
                {
                    return;
                }

                int itemIndex;

                if (IsFull)
                {
                    itemIndex = _firstIndex;
                    _firstIndex = (_firstIndex + 1) % Capacity;
                }
                else
                {
                    itemIndex = _firstIndex + Count;
                    Count++;
                }

                _items[itemIndex] = item;
            }

            public void Clear()
            {
                _firstIndex = 0;
                Count = 0;
            }

            public bool Contains(T item)
            {
                throw new NotImplementedException();
            }


            [SuppressMessage("Microsoft.Usage", "CA2208:InstantiateArgumentExceptionsCorrectly")]
            public void CopyTo(T[] array, int arrayIndex)
            {
                if (array == null)
                    throw new ArgumentNullException("array");

                if (arrayIndex < 0)
                    throw new ArgumentOutOfRangeException("arrayIndex");

                if (Count > (array.Length - arrayIndex))
                    throw new ArgumentException("arrayIndex");

                // Iterate through the buffer in correct order.
                foreach (T item in this)
                {
                    array[arrayIndex++] = item;
                }
            }

            public bool Remove(T item)
            {
                throw new NotImplementedException();
            }
            #endregion

            /// <summary>
            /// Create an array of the items in the buffer. Items
            /// will be in the same order they were added.
            /// </summary>
            /// <returns>The new array.</returns>
            public T[] ToArray()
            {
                T[] result = new T[Count];
                CopyTo(result, 0);
                return result;
            }

            /// <summary>
            /// Access an item in the buffer. Indexing is based off
            /// of the order items were added, rather than any
            /// internal ordering the buffer may be maintaining.
            /// </summary>
            /// <param name="index">The index of the item to access.</param>
            public T this[int index]
            {
                get
                {
                    if (!(index >= 0 && index < Count))
                    {
                        throw new ArgumentOutOfRangeException("index");
                    }

                    return _items[WrapIndex(index)];
                }
            }
        }

        /// <summary>
        /// An interface to a context tracking algorithm.
        /// </summary>
        private interface IContextTracker
        {
            /// <summary>
            /// Matches with completed context information
            /// that are ready to be emitted into the pipeline.
            /// </summary>
            IList<MatchInfo> EmitQueue { get; }

            /// <summary>
            /// Track a non-matching line for context.
            /// </summary>
            /// <param name="line">The line to track.</param>
            void TrackLine(string line);

            /// <summary>
            /// Track a matching line.
            /// </summary>
            /// <param name="match">The line to track.</param>
            void TrackMatch(MatchInfo match);

            /// <summary>
            /// Track having reached the end of the file,
            /// giving the tracker a chance to process matches with
            /// incomplete context information.
            /// </summary>
            void TrackEOF();
        }

        /// <summary>
        /// A state machine to track display context for each match.
        /// </summary>
        private class DisplayContextTracker : IContextTracker
        {
            private enum ContextState
            {
                InitialState,
                CollectPre,
                CollectPost,
            }

            private ContextState _contextState = ContextState.InitialState;
            private int _preContext = 0;
            private int _postContext = 0;

            // The context leading up to the match.
            private CircularBuffer<string> _collectedPreContext = null;

            // The context after the match.
            private List<string> _collectedPostContext = null;

            // Current match info we are tracking postcontext for.
            // At any given time, if set, this value will not be
            // in the emitQueue but will be the next to be added.
            private MatchInfo _matchInfo = null;

            /// <summary>
            /// Constructor for DisplayContextTracker.
            /// </summary>
            /// <param name="preContext">How much precontext to collect at most.</param>
            /// <param name="postContext">How much precontext to collect at most.</param>
            public DisplayContextTracker(int preContext, int postContext)
            {
                _preContext = preContext;
                _postContext = postContext;

                _collectedPreContext = new CircularBuffer<string>(preContext);
                _collectedPostContext = new List<string>(postContext);
                _emitQueue = new List<MatchInfo>();
                Reset();
            }

            #region IContextTracker implementation
            public IList<MatchInfo> EmitQueue
            {
                get
                {
                    return _emitQueue;
                }
            }
            private List<MatchInfo> _emitQueue = null;

            // Track non-matching line
            public void TrackLine(string line)
            {
                switch (_contextState)
                {
                    case ContextState.InitialState:
                        break;
                    case ContextState.CollectPre:
                        _collectedPreContext.Add(line);
                        break;
                    case ContextState.CollectPost:
                        // We're not done collecting post-context.
                        _collectedPostContext.Add(line);

                        if (_collectedPostContext.Count >= _postContext)
                        {
                            // Now we're done.
                            UpdateQueue();
                        }
                        break;
                }
            }

            // Track matching line
            public void TrackMatch(MatchInfo match)
            {
                // Update the queue in case we were in the middle
                // of collecting postcontext for an older match...
                if (_contextState == ContextState.CollectPost)
                    UpdateQueue();

                // Update the current matchInfo.
                _matchInfo = match;

                // If postContext is set, then we need to hold
                // onto the match for a while and gather context.
                // Otherwise, immediately move the match onto the queue
                // and let UpdateQueue update our state instead.
                if (_postContext > 0)
                    _contextState = ContextState.CollectPost;
                else
                    UpdateQueue();
            }

            // Track having reached the end of the file.
            public void TrackEOF()
            {
                // If we're in the middle of collecting postcontext, we
                // already have a match and it's okay to queue it up
                // early since there are no more lines to track context
                // for.
                if (_contextState == ContextState.CollectPost)
                    UpdateQueue();
            }
            #endregion

            /// <summary>
            /// Moves matchInfo, if set, to the emitQueue and
            /// resets the tracking state.
            /// </summary>
            private void UpdateQueue()
            {
                if (_matchInfo != null)
                {
                    _emitQueue.Add(_matchInfo);

                    if (_matchInfo.Context != null)
                    {
                        _matchInfo.Context.DisplayPreContext = _collectedPreContext.ToArray();
                        _matchInfo.Context.DisplayPostContext = _collectedPostContext.ToArray();
                    }
                    Reset();
                }
            }

            // Reset tracking state. Does not reset the emit queue.
            private void Reset()
            {
                _contextState = (_preContext > 0)
                               ? ContextState.CollectPre
                               : ContextState.InitialState;
                _collectedPreContext.Clear();
                _collectedPostContext.Clear();
                _matchInfo = null;
            }
        }

        /// <summary>
        /// A class to track logical context for each match.
        /// </summary>
        /// <remarks>
        /// The difference between logical and display context is
        /// that logical context includes as many context lines
        /// as possible for a given match, up to the specified
        /// limit, including context lines which overlap between
        /// matches and other matching lines themselves. Display
        /// context, on the other hand, is designed to display
        /// a possibly-continuous set of matches by excluding
        /// overlapping context (lines will only appear once)
        /// and other matching lines (since they will appear
        /// as their own match entries.)
        /// </remarks>
        private class LogicalContextTracker : IContextTracker
        {
            // A union: string | MatchInfo. Needed since
            // context lines could be either proper matches
            // or non-matching lines.
            private class ContextEntry
            {
                public string Line = null;
                public MatchInfo Match = null;

                public ContextEntry(string line)
                {
                    Line = line;
                }

                public ContextEntry(MatchInfo match)
                {
                    Match = match;
                }

                public override string ToString()
                {
                    return (Match != null) ? Match.Line : Line;
                }
            }

            // Whether or not early entries found
            // while still filling up the context buffer
            // have been added to the emit queue.
            // Used by UpdateQueue.
            private bool _hasProcessedPreEntries = false;

            private int _preContext;
            private int _postContext;
            // A circular buffer tracking both precontext and postcontext.
            //
            // Essentially, the buffer is separated into regions:
            // | prectxt region  (older entries, length = precontext)  |
            // | match region    (length = 1)                          |
            // | postctxt region (newer entries, length = postcontext) |
            //
            // When context entries containing a match reach the "middle"
            // (the position between the pre/post context regions)
            // of this buffer, and the buffer is full, we will know
            // enough context to populate the Context properties of the
            // match. At that point, we will add the match object 
            // to the emit queue.
            private CircularBuffer<ContextEntry> _collectedContext = null;

            /// <summary>
            /// Constructor for LogicalContextTracker.
            /// </summary>
            /// <param name="preContext">How much precontext to collect at most.</param>
            /// <param name="postContext">How much postcontext to collect at most.</param>
            public LogicalContextTracker(int preContext, int postContext)
            {
                _preContext = preContext;
                _postContext = postContext;
                _collectedContext = new CircularBuffer<ContextEntry>(preContext + postContext + 1);
                _emitQueue = new List<MatchInfo>();
            }

            #region IContextTracker implementation
            public IList<MatchInfo> EmitQueue
            {
                get
                {
                    return _emitQueue;
                }
            }
            private List<MatchInfo> _emitQueue = null;

            public void TrackLine(string line)
            {
                ContextEntry entry = new ContextEntry(line);
                _collectedContext.Add(entry);
                UpdateQueue();
            }

            public void TrackMatch(MatchInfo match)
            {
                ContextEntry entry = new ContextEntry(match);
                _collectedContext.Add(entry);
                UpdateQueue();
            }

            public void TrackEOF()
            {
                // If the buffer is already full,
                // check for any matches with incomplete
                // postcontext and add them to the emit queue.
                // These matches can be identified by being past
                // the "middle" of the context buffer (still in
                // the postcontext region.
                //
                // If the buffer isn't full, then nothing will have
                // ever been emitted and everything is still waiting
                // on postcontext. So process the whole buffer.

                int startIndex = (_collectedContext.IsFull) ? _preContext + 1 : 0;
                EmitAllInRange(startIndex, _collectedContext.Count - 1);
            }
            #endregion

            /// <summary>
            /// Add all matches found in the specified range
            /// to the emit queue, collecting as much context
            /// as possible up to the limits specified in the ctor.
            /// </summary>
            /// <remarks>
            /// The range is inclusive; the entries at
            /// startIndex and endIndex will both be checked.
            /// </remarks>
            /// <param name="startIndex">The beginning of the match range.</param>
            /// <param name="endIndex">The ending of the match range.</param>
            private void EmitAllInRange(int startIndex, int endIndex)
            {
                for (int i = startIndex; i <= endIndex; i++)
                {
                    MatchInfo match = _collectedContext[i].Match;
                    if (match != null)
                    {
                        int preStart = Math.Max(i - _preContext, 0);
                        int postLength = Math.Min(_postContext, _collectedContext.Count - i - 1);
                        Emit(match, preStart, i - preStart, i + 1, postLength);
                    }
                }
            }

            /// <summary>
            /// Add match(es) found in the match region to the
            /// emit queue. Should be called every time an entry
            /// is added to the context buffer.
            /// </summary>
            private void UpdateQueue()
            {
                // Are we at capacity and thus have enough postcontext?
                // Is there a match in the "middle" of the buffer
                // that we know the pre/post context for?
                //
                // If this is the first time we've reached full capacity,
                // hasProcessedPreEntries will not be set, and we
                // should go through the entire context, because it might
                // have entries that never collected enough
                // precontext. Otherwise, we should just look at the
                // middle region.
                if (_collectedContext.IsFull)
                {
                    if (_hasProcessedPreEntries)
                    {
                        // Only process a potential match with exactly
                        // enough pre and post-context.
                        EmitAllInRange(_preContext, _preContext);
                    }
                    else
                    {
                        // Some of our early entries may not
                        // have enough precontext. Process them too.
                        EmitAllInRange(0, _preContext);
                        _hasProcessedPreEntries = true;
                    }
                }
            }

            /// <summary>
            /// Collects context from the specified ranges. Populates
            /// the specified match with the collected context
            /// and adds it to the emit queue.
            /// </summary>
            /// <remarks>
            /// Context ranges must be within the bounds of the context
            /// buffer.
            /// </remarks>
            /// <param name="match">The match to operate on.</param>
            /// <param name="preStartIndex">The start index of the precontext range.</param>
            /// <param name="preLength">The length of the precontext range.</param>
            /// <param name="postStartIndex">The start index of the postcontext range.</param>
            /// <param name="postLength">The length of the precontext range.</param>
            private void Emit(MatchInfo match, int preStartIndex, int preLength, int postStartIndex, int postLength)
            {
                if (match.Context != null)
                {
                    match.Context.PreContext = CopyContext(preStartIndex, preLength);
                    match.Context.PostContext = CopyContext(postStartIndex, postLength);
                }

                _emitQueue.Add(match);
            }

            /// <summary>
            /// Collects context from the specified ranges.
            /// </summary>
            /// <remarks>
            /// The range must be within the bounds of the context buffer.
            /// </remarks>
            /// <param name="startIndex">The index to start at.</param>
            /// <param name="length">The length of the range.</param>
            private string[] CopyContext(int startIndex, int length)
            {
                string[] result = new string[length];

                for (int i = 0; i < length; i++)
                {
                    result[i] = _collectedContext[startIndex + i].ToString();
                }

                return result;
            }
        }

        /// <summary>
        /// A class to track both logical and display contexts.
        /// </summary>
        private class ContextTracker : IContextTracker
        {
            private IContextTracker _displayTracker;
            private IContextTracker _logicalTracker;

            /// <summary>
            /// Constructor for LogicalContextTracker.
            /// </summary>
            /// <param name="preContext">How much precontext to collect at most.</param>
            /// <param name="postContext">How much postcontext to collect at most.</param>
            public ContextTracker(int preContext, int postContext)
            {
                _displayTracker = new DisplayContextTracker(preContext, postContext);
                _logicalTracker = new LogicalContextTracker(preContext, postContext);
                EmitQueue = new List<MatchInfo>();
            }

            #region IContextTracker implementation
            public IList<MatchInfo> EmitQueue { get; }

            public void TrackLine(string line)
            {
                _displayTracker.TrackLine(line);
                _logicalTracker.TrackLine(line);
                UpdateQueue();
            }

            public void TrackMatch(MatchInfo match)
            {
                _displayTracker.TrackMatch(match);
                _logicalTracker.TrackMatch(match);
                UpdateQueue();
            }

            public void TrackEOF()
            {
                _displayTracker.TrackEOF();
                _logicalTracker.TrackEOF();
                UpdateQueue();
            }
            #endregion

            /// <summary>
            /// Update the emit queue based on the wrapped trackers.
            /// </summary>
            private void UpdateQueue()
            {
                // Look for completed matches in the logical
                // tracker's queue. Since the logical tracker
                // will try to collect as much context as
                // possible, the display tracker will have either
                // already finished collecting its context for the
                // match or will have completed it at the same
                // time as the logical tracker, so we can
                // be sure the matches will have both logical
                // and display context already populated.

                foreach (MatchInfo match in _logicalTracker.EmitQueue)
                {
                    EmitQueue.Add(match);
                }

                _logicalTracker.EmitQueue.Clear();
                _displayTracker.EmitQueue.Clear();
            }
        }

        /// <summary>
        /// This parameter specifies the current pipeline object 
        /// </summary>
        [Parameter(ValueFromPipeline = true, Mandatory = true, ParameterSetName = "Object")]
        [AllowNull]
        [AllowEmptyString]
        public PSObject InputObject
        {
            get
            {
                return _inputObject;
            }
            set
            {
                _inputObject = LanguagePrimitives.IsNull(value) ? PSObject.AsPSObject("") : value;
            }
        }
        private PSObject _inputObject = AutomationNull.Value;

        /// <summary>
        /// String index to start from the beginning.
        ///
        /// If the value is negative, the length is counted from the
        /// end of the string.
        /// </summary>
        ///
        [Parameter(Mandatory = true, Position = 0)]
        public string[] Pattern { get; set; }

        private Regex[] _regexPattern;

        /// <summary>
        /// file to read from 
        /// Globbing is done on these
        /// </summary>
        [Parameter(Position = 1, Mandatory = true, ValueFromPipelineByPropertyName = true, ParameterSetName = "File")]
        [FileinfoToString]
        public string[] Path { get; set; }

        /// <summary>
        /// Literal file to read from 
        /// Globbing is not done on these
        /// </summary>
        [Parameter(Mandatory = true, ValueFromPipelineByPropertyName = true, ParameterSetName = "LiteralFile")]
        [FileinfoToString]
        [Alias("PSPath")]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public string[] LiteralPath
        {
            get
            {
                return Path;
            }
            set
            {
                Path = value;
                _isLiteralPath = true;
            }
        }
        private bool _isLiteralPath = false;

        /// <summary> If set, match pattern string literally. 
        /// If not (default) search using pattern as a Regular Expression
        /// </summary>
        [Parameter]
        public SwitchParameter SimpleMatch
        {
            get
            {
                return _simpleMatch;
            }
            set
            {
                _simpleMatch = value;
            }
        }
        private bool _simpleMatch;

        ///<summary> 
        /// If true, then do case-sensitive searches...
        /// </summary>
        [Parameter]
        public SwitchParameter CaseSensitive
        {
            get
            {
                return _caseSensitive;
            }
            set
            {
                _caseSensitive = value;
            }
        }
        private bool _caseSensitive;

        /// <summary>
        /// If true the cmdlet will stop processing at the first successful match and
        /// return true.  If both List and Quiet parameters are given, an exception is thrown.
        /// </summary>
        [Parameter]
        public SwitchParameter Quiet
        {
            get
            {
                return _quiet;
            }
            set
            {
                _quiet = value;
            }
        }
        private bool _quiet;

        /// <summary> 
        /// list files where a match is found
        /// This is the Unix functionality this switch is intended to mimic; 
        /// the actual action of this option is to stop after the first match 
        /// is found and returned from any particular file. 
        /// </summary>
        [Parameter]
        public SwitchParameter List
        {
            get
            {
                return _list;
            }
            set
            {
                _list = value;
            }
        }
        private bool _list;

        /// <summary>
        /// Lets you include particular files.  Files not matching
        /// one of these (if specified) are excluded.
        /// </summary>
        /// <exception cref="WildcardPatternException">Invalid wildcard pattern was specified.</exception>
        [Parameter]
        [ValidateNotNullOrEmpty]
        public string[] Include
        {
            get
            {
                return includeStrings;
            }
            set
            {
                // null check is not needed (because of ValidateNotNullOrEmpty),
                // but we have to include it to silence OACR
                if (value == null)
                {
                    throw PSTraceSource.NewArgumentNullException("value");
                }

                includeStrings = value;

                this.include = new WildcardPattern[includeStrings.Length];
                for (int i = 0; i < includeStrings.Length; i++)
                {
                    this.include[i] = WildcardPattern.Get(includeStrings[i], WildcardOptions.IgnoreCase);
                }
            }
        }
        internal string[] includeStrings = null;
        internal WildcardPattern[] include = null;

        /// <summary>
        /// Lets you exclude particular files.  Files matching
        /// one of these (if specified) are excluded.
        /// </summary>
        [Parameter]
        [ValidateNotNullOrEmpty]
        public string[] Exclude
        {
            get
            {
                return excludeStrings;
            }
            set
            {
                // null check is not needed (because of ValidateNotNullOrEmpty),
                // but we have to include it to silence OACR
                if (value == null)
                {
                    throw PSTraceSource.NewArgumentNullException("value");
                }

                excludeStrings = value;

                this.exclude = new WildcardPattern[excludeStrings.Length];
                for (int i = 0; i < excludeStrings.Length; i++)
                {
                    this.exclude[i] = WildcardPattern.Get(excludeStrings[i], WildcardOptions.IgnoreCase);
                }
            }
        }
        internal string[] excludeStrings;
        internal WildcardPattern[] exclude;

        /// <summary>
        /// Only show lines which do not match.
        /// Equivalent to grep -v/findstr -v.
        /// </summary>
        [Parameter]
        public SwitchParameter NotMatch
        {
            get
            {
                return _notMatch;
            }
            set
            {
                _notMatch = value;
            }
        }
        private bool _notMatch;

        /// <summary>
        /// If set, sets the Matches property of MatchInfo to the result
        /// of calling System.Text.RegularExpressions.Regex.Matches() on
        /// the corresponding line.
        ///
        /// Has no effect if -SimpleMatch is also specified.
        /// </summary>
        [Parameter]
        public SwitchParameter AllMatches
        {
            get
            {
                return _allMatches;
            }
            set
            {
                _allMatches = value;
            }
        }
        private bool _allMatches;

        /// <summary>
        /// The text encoding to process each file as.
        /// </summary>
        [Parameter]
        [ValidateNotNullOrEmpty]
        [ValidateSetAttribute(new string[] {
            EncodingConversion.Unicode,
            EncodingConversion.Utf7,
            EncodingConversion.Utf8,
            EncodingConversion.Utf32,
            EncodingConversion.Ascii,
            EncodingConversion.BigEndianUnicode,
            EncodingConversion.Default,
            EncodingConversion.OEM })]
        public string Encoding { get; set; }

        private System.Text.Encoding _textEncoding;

        /// <summary>
        /// The number of context lines to collect. If set to a
        /// single integer value N, collects N lines each of pre-
        /// and post- context. If set to a 2-tuple B,A, collects B
        /// lines of pre- and A lines of post- context.
        /// If set to a list with more than 2 elements, the
        /// excess elements are ignored.
        /// </summary>
        [Parameter]
        [ValidateNotNullOrEmpty]
        [ValidateCount(1, 2)]
        [ValidateRange(0, Int32.MaxValue)]
        public new int[] Context
        {
            get
            {
                return _context;
            }
            set
            {
                // null check is not needed (because of ValidateNotNullOrEmpty),
                // but we have to include it to silence OACR
                if (value == null)
                {
                    throw PSTraceSource.NewArgumentNullException("value");
                }

                _context = value;

                if (_context.Length == 1)
                {
                    _preContext = _context[0];
                    _postContext = _context[0];
                }
                else if (_context.Length >= 2)
                {
                    _preContext = _context[0];
                    _postContext = _context[1];
                }
            }
        }
        private int[] _context;
        private int _preContext = 0;
        private int _postContext = 0;

        // This context tracker is only used for strings which are piped
        // directly into the cmdlet. File processing doesn't need
        // to track state between calls to ProcessRecord, and so
        // allocates its own tracker. The reason we can't
        // use a single global tracker for both is that in the case of
        // a mixed list of strings and FileInfo, the context tracker
        // would get reset after each file.
        private ContextTracker _globalContextTracker = null;

        /// <summary>
        /// This is used to handle the case were we're done processing input objects.
        /// If true, process record will just return.
        /// </summary>
        private bool _doneProcessing;

        private int _inputRecordNumber;

        /// <summary>
        /// Read command line parameters.
        /// </summary>
        protected override void BeginProcessing()
        {
            // Process encoding switch.
            if (Encoding != null)
            {
                _textEncoding = EncodingConversion.Convert(this, Encoding);
            }
            else
            {
                _textEncoding = new System.Text.UTF8Encoding();
            }

            if (!_simpleMatch)
            {
                RegexOptions regexOptions = (_caseSensitive) ? RegexOptions.None : RegexOptions.IgnoreCase;
                _regexPattern = new Regex[Pattern.Length];
                for (int i = 0; i < Pattern.Length; i++)
                {
                    try
                    {
                        _regexPattern[i] = new Regex(Pattern[i], regexOptions);
                    }
                    catch (Exception e)
                    {
                        this.ThrowTerminatingError(BuildErrorRecord(MatchStringStrings.InvalidRegex, Pattern[i], e.Message, "InvalidRegex", e));
                        throw;
                    }
                }
            }

            _globalContextTracker = new ContextTracker(_preContext, _postContext);
        }

        /// <summary>
        /// process the input
        /// </summary>
        ///
        /// <returns> Does not return a value </returns>
        ///
        /// <exception cref="ArgumentException">Regular expression parsing error, path error</exception>
        /// <exception cref="FileNotFoundException">A file cannot be found.</exception>
        /// <exception cref="DirectoryNotFoundException">A file cannot be found.</exception>
        protected override void ProcessRecord()
        {
            if (_doneProcessing)
                return;

            List<string> expandedPaths = null;
            if (Path != null)
            {
                expandedPaths = ResolveFilePaths(Path, _isLiteralPath);
                if (expandedPaths == null)
                    return;
            }
            else
            {
                FileInfo fileInfo = _inputObject.BaseObject as FileInfo;
                if (fileInfo != null)
                {
                    expandedPaths = new List<string>();
                    expandedPaths.Add(fileInfo.FullName);
                }
            }

            if (expandedPaths != null)
            {
                foreach (string filename in expandedPaths)
                {
                    bool foundMatch = ProcessFile(filename);
                    if (_quiet && foundMatch)
                        return;
                }

                // No results in any files.
                if (_quiet)
                {
                    if (_list)
                        WriteObject(null);
                    else
                        WriteObject(false);
                }
            }
            else
            {
                // Set the line number in the matched object to be the record number
                _inputRecordNumber++;

                bool matched;
                MatchInfo result;
                MatchInfo matchInfo = null;
                var line = _inputObject.BaseObject as string;
                if (line != null)
                {
                    matched = doMatch(line, out result);
                }
                else
                {
                    matchInfo = _inputObject.BaseObject as MatchInfo;
                    object objectToCheck = matchInfo ?? (object)_inputObject;
                    matched = doMatch(objectToCheck, out result, out line);
                }

                if (matched)
                {
                    // Don't re-write the line number if it was already set...
                    if (matchInfo == null)
                    {
                        result.LineNumber = _inputRecordNumber;
                    }
                    // doMatch will have already set the pattern and line text...
                    _globalContextTracker.TrackMatch(result);
                }
                else
                {
                    _globalContextTracker.TrackLine(line);
                }

                // Emit any queued up objects...
                if (FlushTrackerQueue(_globalContextTracker))
                {
                    // If we're in quiet mode, go ahead and stop processing
                    // now.
                    if (_quiet)
                        _doneProcessing = true;
                }
            }
        }

        /// <summary>
        /// Process a file which was either specified on the
        /// command line or passed in as a FileInfo object.
        /// </summary>
        /// <param name="filename">The file to process.</param>
        /// <returns>True if a match was found; otherwise false.</returns>
        private bool ProcessFile(string filename)
        {
            ContextTracker contextTracker = new ContextTracker(_preContext, _postContext);

            bool foundMatch = false;

            // Read the file one line at a time...
            try
            {
                // see if the file is one the include exclude list...
                if (!meetsIncludeExcludeCriteria(filename))
                    return false;

                using (FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    using (StreamReader sr = new StreamReader(fs, _textEncoding))
                    {
                        String line;
                        int lineNo = 0;

                        // Read and display lines from the file until the end of 
                        // the file is reached.
                        while ((line = sr.ReadLine()) != null)
                        {
                            lineNo++;

                            MatchInfo result;

                            if (doMatch(line, out result))
                            {
                                result.Path = filename;
                                result.LineNumber = lineNo;
                                contextTracker.TrackMatch(result);
                            }
                            else
                            {
                                contextTracker.TrackLine(line);
                            }

                            // Flush queue of matches to emit.
                            if (contextTracker.EmitQueue.Count > 0)
                            {
                                foundMatch = true;

                                // If -list or -quiet was specified, we only want to emit the first match
                                // for each file so record the object to emit and stop processing
                                // this file. It's done this way so the file is closed before emitting
                                // the result so the downstream cmdlet can actually manipulate the file
                                // that was found.

                                if (_quiet || _list)
                                {
                                    break;
                                }
                                else
                                {
                                    FlushTrackerQueue(contextTracker);
                                }
                            }
                        }
                    }
                }

                // Check for any remaining matches. This could be caused
                // by breaking out of the loop early for quiet or list
                // mode, or by reaching EOF before we collected all
                // our postcontext.
                contextTracker.TrackEOF();
                if (FlushTrackerQueue(contextTracker))
                    foundMatch = true;
            }
            catch (System.NotSupportedException nse)
            {
                WriteError(BuildErrorRecord(MatchStringStrings.FileReadError, filename, nse.Message, "ProcessingFile", nse));
            }
            catch (System.IO.IOException ioe)
            {
                WriteError(BuildErrorRecord(MatchStringStrings.FileReadError, filename, ioe.Message, "ProcessingFile", ioe));
            }
            catch (System.Security.SecurityException se)
            {
                WriteError(BuildErrorRecord(MatchStringStrings.FileReadError, filename, se.Message, "ProcessingFile", se));
            }
            catch (System.UnauthorizedAccessException uae)
            {
                WriteError(BuildErrorRecord(MatchStringStrings.FileReadError, filename, uae.Message, "ProcessingFile", uae));
            }

            return foundMatch;
        }

        /// <summary>
        /// Emit any objects which have been queued up, and clear
        /// the queue.
        /// </summary>
        /// <param name="contextTracker">The context tracker to operate on.</param>
        /// <returns>Whether or not any objects were emitted.</returns>
        private bool FlushTrackerQueue(ContextTracker contextTracker)
        {
            // Do we even have any matches to emit?
            if (contextTracker.EmitQueue.Count < 1)
                return false;

            // If -quiet is specified but not -list return true on first match
            if (_quiet && !_list)
            {
                WriteObject(true);
            }
            else if (_list)
            {
                WriteObject(contextTracker.EmitQueue[0]);
            }
            else
            {
                foreach (MatchInfo match in contextTracker.EmitQueue)
                {
                    WriteObject(match);
                }
            }

            contextTracker.EmitQueue.Clear();
            return true;
        }

        /// <summary>
        /// Complete processing. Emits any objects which have been queued up
        /// due to -context tracking.
        /// </summary>
        protected override void EndProcessing()
        {
            // Check for a leftover match that was still tracking context.
            _globalContextTracker.TrackEOF();
            if (!_doneProcessing)
                FlushTrackerQueue(_globalContextTracker);
        }

        private bool doMatch(string operandString, out MatchInfo matchResult)
        {
            return doMatchWorker(operandString, null, out matchResult);
        }

        private bool doMatch(object operand, out MatchInfo matchResult, out string operandString)
        {
            MatchInfo matchInfo = operand as MatchInfo;
            if (matchInfo != null)
            {
                // We're operating in filter mode. Match
                // against the provided MatchInfo's line.
                // If the user has specified context tracking,
                // inform them that it is not allowed in filter
                // mode and disable it. Also, reset the global
                // context tracker used for processing pipeline
                // objects to use the new settings.
                operandString = matchInfo.Line;

                if (_preContext > 0 || _postContext > 0)
                {
                    _preContext = 0;
                    _postContext = 0;
                    _globalContextTracker = new ContextTracker(_preContext, _postContext);
                    WarnFilterContext();
                }
            }
            else
            {
                operandString = (string)LanguagePrimitives.ConvertTo(operand, typeof(string), CultureInfo.InvariantCulture);
            }

            return doMatchWorker(operandString, matchInfo, out matchResult);
        }

        /// <summary>
        /// Check the operand and see if it matches, if this.quiet is not set, then
        /// return a partially populated MatchInfo object with Line, Pattern, IgnoreCase
        /// set.
        /// </summary>
        /// <param name="matchInfo"></param>
        /// <param name="matchResult">the match info object - this will be
        /// null if this.quiet is set. </param>
        /// <param name="operandString">the result of converting operand to
        /// a string.</param>
        /// <returns>true if the input object matched</returns>
        private bool doMatchWorker(string operandString, MatchInfo matchInfo, out MatchInfo matchResult)
        {
            bool gotMatch = false;
            Match[] matches = null;
            int patternIndex = 0;
            matchResult = null;

            if (!_simpleMatch)
            {
                while (patternIndex < Pattern.Length)
                {
                    Regex r = _regexPattern[patternIndex];

                    // Only honor allMatches if notMatch is not set,
                    // since it's a fairly expensive operation and
                    // notMatch takes precedent over allMatch.
                    if (_allMatches && !_notMatch)
                    {
                        MatchCollection mc = r.Matches(operandString);
                        if (mc.Count > 0)
                        {
                            matches = new Match[mc.Count];
                            ((ICollection)mc).CopyTo(matches, 0);
                            gotMatch = true;
                        }
                    }
                    else
                    {
                        Match match = r.Match(operandString);
                        gotMatch = match.Success;

                        if (match.Success)
                        {
                            matches = new Match[] { match };
                        }
                    }

                    if (gotMatch)
                        break;

                    patternIndex++;
                }
            }
            else
            {
                StringComparison compareOption = _caseSensitive ?
                    StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase;
                while (patternIndex < Pattern.Length)
                {
                    string pat = Pattern[patternIndex];

                    if (operandString.IndexOf(pat, compareOption) >= 0)
                    {
                        gotMatch = true;
                        break;
                    }

                    patternIndex++;
                }
            }

            if (_notMatch)
            {
                gotMatch = !gotMatch;
                // If notMatch was specified with multiple
                // patterns, then *none* of the patterns
                // matched and any pattern could be picked
                // to report in MatchInfo. However, that also
                // means that patternIndex will have been
                // incremented past the end of the pattern array.
                // So reset it to select the first pattern. 
                patternIndex = 0;
            }

            if (gotMatch)
            {
                // if we were passed a MatchInfo object as the operand,
                // we're operating in filter mode.
                if (matchInfo != null)
                {
                    // If the original MatchInfo was tracking context,
                    // we need to copy it and disable display context,
                    // since we can't guarantee it will be displayed
                    // correctly when filtered.
                    if (matchInfo.Context != null)
                    {
                        matchResult = matchInfo.Clone();
                        matchResult.Context.DisplayPreContext = new string[] { };
                        matchResult.Context.DisplayPostContext = new string[] { };
                    }
                    else
                    {
                        // Otherwise, just pass the object as is.
                        matchResult = matchInfo;
                    }

                    return true;
                }

                // otherwise construct and populate a new MatchInfo object
                matchResult = new MatchInfo();
                matchResult.IgnoreCase = !_caseSensitive;
                matchResult.Line = operandString;
                matchResult.Pattern = Pattern[patternIndex];

                if (_preContext > 0 || _postContext > 0)
                {
                    matchResult.Context = new MatchInfoContext();
                }

                // Matches should be an empty list, rather than null,
                // in the cases of notMatch and simpleMatch.
                matchResult.Matches = matches ?? new Match[] { };

                return true;
            }
            return false;
        } // end doMatch

        /// Get a list or resolved file paths.
        private List<string> ResolveFilePaths(string[] filePaths, bool isLiteralPath)
        {
            ProviderInfo provider;
            List<string> allPaths = new List<string>();

            foreach (string path in filePaths)
            {
                Collection<string> resolvedPaths;
                if (isLiteralPath)
                {
                    resolvedPaths = new Collection<string>();
                    PSDriveInfo drive;
                    string resolvedPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(path, out provider, out drive);
                    resolvedPaths.Add(resolvedPath);
                }
                else
                {
                    resolvedPaths = GetResolvedProviderPathFromPSPath(path, out provider);
                }

                if (!provider.NameEquals(((PSCmdlet)this).Context.ProviderNames.FileSystem))
                {
                    // "The current provider ({0}) cannot open a file"
                    WriteError(BuildErrorRecord(MatchStringStrings.FileOpenError, provider.FullName, "ProcessingFile", null));
                    continue;
                }
                allPaths.AddRange(resolvedPaths);
            }

            return allPaths;
        }

        private static ErrorRecord BuildErrorRecord(string messageId, string argument, string errorId, Exception innerException)
        {
            return BuildErrorRecord(messageId, new object[] { argument }, errorId, innerException);
        }

        private static ErrorRecord BuildErrorRecord(string messageId, string arg0, string arg1, string errorId, Exception innerException)
        {
            return BuildErrorRecord(messageId, new object[] { arg0, arg1 }, errorId, innerException);
        }

        private static ErrorRecord BuildErrorRecord(string messageId, object[] arguments, string errorId, Exception innerException)
        {
            string fmtedMsg = StringUtil.Format(messageId, arguments);
            ArgumentException e = new ArgumentException(fmtedMsg, innerException);
            return new ErrorRecord(e, errorId, ErrorCategory.InvalidArgument, null);
        }

        private void WarnFilterContext()
        {
            string msg = MatchStringStrings.FilterContextWarning;
            WriteWarning(msg);
        }

        /// <summary>
        /// Magic class that works around the limitations on ToString() for FileInfo.
        /// </summary>
        private class FileinfoToStringAttribute : ArgumentTransformationAttribute
        {
            public override object Transform(EngineIntrinsics engineIntrinsics, object inputData)
            {
                object result = inputData;

                PSObject mso = result as PSObject;
                if (mso != null)
                    result = mso.BaseObject;

                IList argList = result as IList;
                FileInfo fileInfo;

                // Handle an array of elements...
                if (argList != null)
                {
                    object[] resultList = new object[argList.Count];

                    for (int i = 0; i < argList.Count; i++)
                    {
                        object element = argList[i];

                        mso = element as PSObject;
                        if (mso != null)
                            element = mso.BaseObject;

                        fileInfo = element as FileInfo;

                        if (fileInfo != null)
                            resultList[i] = fileInfo.FullName;
                        else resultList[i] = element;
                    }
                    return resultList;
                }

                // Handle the singleton case...
                fileInfo = result as FileInfo;
                if (fileInfo != null)
                    return fileInfo.FullName;

                return inputData;
            }
        }

        /// <summary>
        /// Check whether the supplied name meets the include/exclude criteria.
        /// That is - it's on the include list if there is one and not on
        /// the exclude list if there was one of those.
        /// </summary>
        /// <param name="filename"></param>
        /// <returns>True if the filename is acceptable.</returns>
        private bool meetsIncludeExcludeCriteria(string filename)
        {
            bool ok = false;

            // see if the file is on the include list...
            if (this.include != null)
            {
                foreach (WildcardPattern patternItem in this.include)
                {
                    if (patternItem.IsMatch(filename))
                    {
                        ok = true;
                        break;
                    }
                }
            }
            else
            {
                ok = true;
            }

            if (!ok)
                return false;

            // now see if it's on the exclude list...
            if (this.exclude != null)
            {
                foreach (WildcardPattern patternItem in this.exclude)
                {
                    if (patternItem.IsMatch(filename))
                    {
                        ok = false;
                        break;
                    }
                }
            }

            return ok;
        }
    } // end class SelectStringCommand
}

