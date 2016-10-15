/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management.Automation;
using Microsoft.PowerShell.Commands.Internal.Format;
using System.Management.Automation.Internal;
using System.Diagnostics.CodeAnalysis;

#if CORECLR
// Use stubs for SystemException
using Microsoft.PowerShell.CoreClr.Stubs;
#endif

namespace Microsoft.PowerShell.Commands
{
    /// <summary>
    /// helper class to do wildcard matching on MshExpressions
    /// </summary>
    internal sealed class MshExpressionFilter
    {
        /// <summary>
        /// construct the class, using an array of patterns
        /// </summary>
        /// <param name="wildcardPatternsStrings">array of pattern strings to use</param>
        internal MshExpressionFilter(string[] wildcardPatternsStrings)
        {
            if (wildcardPatternsStrings == null)
            {
                throw new ArgumentNullException("wildcardPatternsStrings");
            }

            _wildcardPatterns = new WildcardPattern[wildcardPatternsStrings.Length];
            for (int k = 0; k < wildcardPatternsStrings.Length; k++)
            {
                _wildcardPatterns[k] = WildcardPattern.Get(wildcardPatternsStrings[k], WildcardOptions.IgnoreCase);
            }
        }

        /// <summary>
        /// try to match the expression against the array of wildcard patterns.
        /// the first match shortcircuits the search
        /// </summary>
        /// <param name="expression">MshExpression to test against</param>
        /// <returns>true if there is a match, else false</returns>
        internal bool IsMatch(MshExpression expression)
        {
            for (int k = 0; k < _wildcardPatterns.Length; k++)
            {
                if (_wildcardPatterns[k].IsMatch(expression.ToString()))
                    return true;
            }
            return false;
        }

        private WildcardPattern[] _wildcardPatterns;
    }

    internal class SelectObjectExpressionParameterDefinition : CommandParameterDefinition
    {
        protected override void SetEntries()
        {
            this.hashEntries.Add(new ExpressionEntryDefinition());
            this.hashEntries.Add(new NameEntryDefinition());
        }
    }

    /// <summary>
    /// 
    /// </summary>
    [Cmdlet("Select", "Object", DefaultParameterSetName = "DefaultParameter",
        HelpUri = "http://go.microsoft.com/fwlink/?LinkID=113387", RemotingCapability = RemotingCapability.None)]
    public sealed class SelectObjectCommand : PSCmdlet
    {
        #region Command Line Switches

        /// <summary>
        /// 
        /// </summary>
        /// <value></value>
        [Parameter(ValueFromPipeline = true)]
        public PSObject InputObject { set; get; } = AutomationNull.Value;


        /// <summary>
        /// 
        /// </summary>
        /// <value></value>
        [Parameter(Position = 0, ParameterSetName = "DefaultParameter")]
        [Parameter(Position = 0, ParameterSetName = "SkipLastParameter")]
        public object[] Property { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <value></value>
        [Parameter(ParameterSetName = "DefaultParameter")]
        [Parameter(ParameterSetName = "SkipLastParameter")]
        public string[] ExcludeProperty { get; set; } = null;

        /// <summary>
        /// 
        /// </summary>
        /// <value></value>
        [Parameter(ParameterSetName = "DefaultParameter")]
        [Parameter(ParameterSetName = "SkipLastParameter")]
        public string ExpandProperty { get; set; } = null;

        /// <summary>
        /// 
        /// </summary>
        /// <value></value>
        [Parameter]
        public SwitchParameter Unique
        {
            get { return _unique; }
            set { _unique = value; }
        }
        private bool _unique;

        /// <summary>
        /// 
        /// </summary>
        /// <value></value>
        [Parameter(ParameterSetName = "DefaultParameter")]
        // NTRAID#Windows Out Of Band Releases-927878-2006/03/02
        // Allow zero
        [ValidateRange(0, int.MaxValue)]
        public int Last
        {
            get { return _last; }
            set { _last = value; _firstOrLastSpecified = true; }
        }
        private int _last = 0;

        /// <summary>
        /// 
        /// </summary>
        /// <value></value>
        [Parameter(ParameterSetName = "DefaultParameter")]
        // NTRAID#Windows Out Of Band Releases-927878-2006/03/02
        // Allow zero
        [ValidateRange(0, int.MaxValue)]
        public int First
        {
            get { return _first; }
            set { _first = value; _firstOrLastSpecified = true; }
        }
        private int _first = 0;
        private bool _firstOrLastSpecified;


        /// <summary>
        /// Skips the specified number of items from top when used with First,from end when used with Last
        /// </summary>
        /// <value></value>
        [Parameter(ParameterSetName = "DefaultParameter")]
        [ValidateRange(0, int.MaxValue)]
        public int Skip { get; set; } = 0;

        /// <summary>
        /// Skip the specified number of items from end.
        /// </summary>
        [Parameter(ParameterSetName = "SkipLastParameter")]
        [ValidateRange(0, int.MaxValue)]
        public int SkipLast { get; set; } = 0;

        /// <summary>
        /// With this switch present, the cmdlet won't "short-circuit" 
        /// (i.e. won't stop upstream cmdlets after it knows that no further objects will be emitted downstream)
        /// </summary>
        [Parameter(ParameterSetName = "DefaultParameter")]
        [Parameter(ParameterSetName = "IndexParameter")]
        public SwitchParameter Wait { get; set; }

        /// <summary>
        /// Used to display the object at specified index
        /// </summary>
        /// <value></value>
        [Parameter(ParameterSetName = "IndexParameter")]
        [ValidateRangeAttribute(0, int.MaxValue)]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public int[] Index
        {
            get
            {
                return _index;
            }
            set
            {
                _index = value;
                _indexSpecified = true;
                Array.Sort(_index);
            }
        }
        private int[] _index;
        private bool _indexSpecified;

        #endregion

        private SelectObjectQueue _selectObjectQueue;

        private class SelectObjectQueue : Queue<PSObject>
        {
            internal SelectObjectQueue(int first, int last, int skip, int skipLast, bool firstOrLastSpecified)
            {
                _first = first;
                _last = last;
                _skip = skip;
                _skipLast = skipLast;
                _firstOrLastSpecified = firstOrLastSpecified;
            }

            public bool AllRequestedObjectsProcessed
            {
                get
                {
                    return _firstOrLastSpecified && _last == 0 && _first != 0 && _streamedObjectCount >= _first;
                }
            }

            public new void Enqueue(PSObject obj)
            {
                if (_last > 0 && this.Count >= (_last + _skip) && _first == 0)
                {
                    base.Dequeue();
                }
                else if (_last > 0 && this.Count >= _last && _first != 0)
                {
                    base.Dequeue();
                }
                base.Enqueue(obj);
            }

            public PSObject StreamingDequeue()
            {
                //if skip parameter is not mentioned or there are no more objects to skip
                if (_skip == 0)
                {
                    if (_skipLast > 0)
                    {
                        // We are going to skip some items from end, but it's okay to process
                        // the early input objects once we have more items in queue than the
                        // specified 'skipLast' value.
                        if (this.Count > _skipLast)
                        {
                            return Dequeue();
                        }
                    }
                    else
                    {
                        if (_streamedObjectCount < _first || !_firstOrLastSpecified)
                        {
                            Diagnostics.Assert(this.Count > 0, "Streaming an empty queue");
                            _streamedObjectCount++;
                            return Dequeue();
                        }

                        if (_last == 0)
                        {
                            Dequeue();
                        }
                    }
                }
                else
                {
                    //if last parameter is not mentioned,remove the objects and decrement the skip
                    if (_last == 0)
                    {
                        Dequeue();
                        _skip--;
                    }
                    else if (_first != 0)
                    {
                        _skip--;
                        Dequeue();
                    }
                }

                return null;
            }

            private int _streamedObjectCount;
            private int _first,_last,_skip,_skipLast;
            private bool _firstOrLastSpecified;
        }

        /// <summary>
        /// list of processed parameters obtained from the Expression array
        /// </summary>
        private List<MshParameter> _propertyMshParameterList;

        /// <summary>
        /// singleton list of process parameters obtained from ExpandProperty
        /// </summary>
        private List<MshParameter> _expandMshParameterList;



        private MshExpressionFilter _exclusionFilter;

        private class UniquePSObjectHelper
        {
            internal UniquePSObjectHelper(PSObject o, int notePropertyCount)
            {
                WrittenObject = o;
                NotePropertyCount = notePropertyCount;
            }
            internal readonly PSObject WrittenObject;
            internal int NotePropertyCount { get; }
        }

        private List<UniquePSObjectHelper> _uniques = null;

        private void ProcessExpressionParameter()
        {
            TerminatingErrorContext invocationContext = new TerminatingErrorContext(this);
            ParameterProcessor processor =
                new ParameterProcessor(new SelectObjectExpressionParameterDefinition());
            if ((Property != null) && (Property.Length != 0))
            {
                _propertyMshParameterList = processor.ProcessParameters(Property, invocationContext);
            }
            else
            {
                _propertyMshParameterList = new List<MshParameter>();
            }

            if (!string.IsNullOrEmpty(ExpandProperty))
            {
                _expandMshParameterList = processor.ProcessParameters(new string[] { ExpandProperty }, invocationContext);
            }

            if (ExcludeProperty != null)
            {
                _exclusionFilter = new MshExpressionFilter(ExcludeProperty);
            }
        }

        private void ProcessObject(PSObject inputObject)
        {
            if ((Property == null || Property.Length == 0) && string.IsNullOrEmpty(ExpandProperty))
            {
                FilteredWriteObject(inputObject, new List<PSNoteProperty>());
                return;
            }


            //If property parameter is mentioned
            List<PSNoteProperty> matchedProperties = new List<PSNoteProperty>();
            foreach (MshParameter p in _propertyMshParameterList)
            {
                ProcessParameter(p, inputObject, matchedProperties);
            }

            if (string.IsNullOrEmpty(ExpandProperty))
            {
                PSObject result = new PSObject();
                if (matchedProperties.Count != 0)
                {
                    HashSet<string> propertNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (PSNoteProperty noteProperty in matchedProperties)
                    {
                        try
                        {
                            if (!propertNames.Contains(noteProperty.Name))
                            {
                                propertNames.Add(noteProperty.Name);
                                result.Properties.Add(noteProperty);
                            }
                            else
                            {
                                WriteAlreadyExistingPropertyError(noteProperty.Name, inputObject,
                                    "AlreadyExistingUserSpecifiedPropertyNoExpand");
                            }
                        }
                        catch (ExtendedTypeSystemException)
                        {
                            WriteAlreadyExistingPropertyError(noteProperty.Name, inputObject,
                                "AlreadyExistingUserSpecifiedPropertyNoExpand");
                        }
                    }
                }
                FilteredWriteObject(result, matchedProperties);
            }
            else
            {
                ProcessExpandParameter(_expandMshParameterList[0], inputObject, matchedProperties);
            }
        }



        private void ProcessParameter(MshParameter p, PSObject inputObject, List<PSNoteProperty> result)
        {
            string name = p.GetEntry(NameEntryDefinition.NameEntryKey) as string;

            MshExpression ex = p.GetEntry(FormatParameterDefinitionKeys.ExpressionEntryKey) as MshExpression;
            List<MshExpressionResult> expressionResults = new List<MshExpressionResult>();
            foreach (MshExpression resolvedName in ex.ResolveNames(inputObject))
            {
                if (_exclusionFilter == null || !_exclusionFilter.IsMatch(resolvedName))
                {
                    List<MshExpressionResult> tempExprResults = resolvedName.GetValues(inputObject);
                    if (tempExprResults == null) continue;
                    foreach (MshExpressionResult mshExpRes in tempExprResults)
                    {
                        expressionResults.Add(mshExpRes);
                    }
                }
            }

            if (expressionResults.Count == 0)
            {
                //Commented out for bug 1107600
                //if (!ex.HasWildCardCharacters)
                //{
                //    ErrorRecord errorRecord = new ErrorRecord(
                //        tracer.NewArgumentException("Property", ResourcesBaseName, "PropertyNotFound", ex.ToString()),
                //        "PropertyNotFound",
                //         ErrorCategory.InvalidArgument,
                //        inputObject);
                //    WriteError(errorRecord);
                //}
                expressionResults.Add(new MshExpressionResult(null, ex, null));
            }

            // if we have an expansion, renaming is not acceptable
            else if (!string.IsNullOrEmpty(name) && expressionResults.Count > 1)
            {
                string errorMsg = SelectObjectStrings.RenamingMultipleResults;
                ErrorRecord errorRecord = new ErrorRecord(
                    new InvalidOperationException(errorMsg),
                    "RenamingMultipleResults",
                    ErrorCategory.InvalidOperation,
                    inputObject);
                WriteError(errorRecord);
                return;
            }

            foreach (MshExpressionResult r in expressionResults)
            {
                // filter the exclusions, if any
                if (_exclusionFilter != null && _exclusionFilter.IsMatch(r.ResolvedExpression))
                    continue;

                PSNoteProperty mshProp;
                if (string.IsNullOrEmpty(name))
                {
                    string resolvedExpressionName = r.ResolvedExpression.ToString();
                    if (string.IsNullOrEmpty(resolvedExpressionName))
                    {
                        PSArgumentException mshArgE = PSTraceSource.NewArgumentException(
                                                        "Property",
                                                        SelectObjectStrings.EmptyScriptBlockAndNoName);
                        ThrowTerminatingError(
                            new ErrorRecord(
                            mshArgE,
                            "EmptyScriptBlockAndNoName",
                            ErrorCategory.InvalidArgument, null));
                    }
                    mshProp = new PSNoteProperty(resolvedExpressionName, r.Result);
                }
                else
                {
                    mshProp = new PSNoteProperty(name, r.Result);
                }
                result.Add(mshProp);
            }
        }
        private void ProcessExpandParameter(MshParameter p, PSObject inputObject,
            List<PSNoteProperty> matchedProperties)
        {
            MshExpression ex = p.GetEntry(FormatParameterDefinitionKeys.ExpressionEntryKey) as MshExpression;
            List<MshExpressionResult> expressionResults = ex.GetValues(inputObject);


            if (expressionResults.Count == 0)
            {
                ErrorRecord errorRecord = new ErrorRecord(
                    PSTraceSource.NewArgumentException("ExpandProperty", SelectObjectStrings.PropertyNotFound, ExpandProperty),
                    "ExpandPropertyNotFound",
                     ErrorCategory.InvalidArgument,
                    inputObject);
                throw new SelectObjectException(errorRecord);
            }
            if (expressionResults.Count > 1)
            {
                ErrorRecord errorRecord = new ErrorRecord(
                    PSTraceSource.NewArgumentException("ExpandProperty", SelectObjectStrings.MutlipleExpandProperties, ExpandProperty),
                    "MutlipleExpandProperties",
                    ErrorCategory.InvalidArgument,
                    inputObject);
                throw new SelectObjectException(errorRecord);
            }

            MshExpressionResult r = expressionResults[0];
            if (r.Exception == null)
            {
                // ignore the property value if it's null
                if (r.Result == null) { return; }

                System.Collections.IEnumerable results = LanguagePrimitives.GetEnumerable(r.Result);
                if (results == null)
                {
                    // add NoteProperties if there is any
                    // If r.Result is a base object, we don't want to associate the NoteProperty
                    // directly with it. We want the NoteProperty to be associated only with this
                    // particular PSObject, so that when the user uses the base object else where,
                    // its members remain the same as before the Select-Object command run.
                    PSObject expandedObject = PSObject.AsPSObject(r.Result, true);
                    AddNoteProperties(expandedObject, inputObject, matchedProperties);

                    FilteredWriteObject(expandedObject, matchedProperties);
                    return;
                }

                foreach (object expandedValue in results)
                {
                    // ignore the element if it's null
                    if (expandedValue == null) { continue; }

                    // add NoteProperties if there is any
                    // If expandedValue is a base object, we don't want to associate the NoteProperty
                    // directly with it. We want the NoteProperty to be associated only with this
                    // particular PSObject, so that when the user uses the base object else where,
                    // its members remain the same as before the Select-Object command run.
                    PSObject expandedObject = PSObject.AsPSObject(expandedValue, true);
                    AddNoteProperties(expandedObject, inputObject, matchedProperties);

                    FilteredWriteObject(expandedObject, matchedProperties);
                }
            }
            else
            {
                ErrorRecord errorRecord = new ErrorRecord(
                    r.Exception,
                    "PropertyEvaluationExpand",
                    ErrorCategory.InvalidResult,
                    inputObject);
                throw new SelectObjectException(errorRecord);
            }
        }

        private void AddNoteProperties(PSObject expandedObject, PSObject inputObject, IEnumerable<PSNoteProperty> matchedProperties)
        {
            foreach (PSNoteProperty noteProperty in matchedProperties)
            {
                try
                {
                    if (expandedObject.Properties[noteProperty.Name] != null)
                    {
                        WriteAlreadyExistingPropertyError(noteProperty.Name, inputObject, "AlreadyExistingUserSpecifiedPropertyExpand");
                    }
                    else
                    {
                        expandedObject.Properties.Add(noteProperty);
                    }
                }
                catch (ExtendedTypeSystemException)
                {
                    WriteAlreadyExistingPropertyError(noteProperty.Name, inputObject, "AlreadyExistingUserSpecifiedPropertyExpand");
                }
            }
        }

        private void WriteAlreadyExistingPropertyError(string name, object inputObject, string errorId)
        {
            ErrorRecord errorRecord = new ErrorRecord(
                PSTraceSource.NewArgumentException("Property", SelectObjectStrings.AlreadyExistingProperty, name),
                errorId,
                ErrorCategory.InvalidOperation,
                inputObject);
            WriteError(errorRecord);
        }
        private void FilteredWriteObject(PSObject obj, List<PSNoteProperty> addedNoteProperties)
        {
            Diagnostics.Assert(obj != null, "This command should never write null");

            if (!_unique)
            {
                if (obj != AutomationNull.Value)
                {
                    SetPSCustomObject(obj);
                    WriteObject(obj);
                }
                return;
            }
            //if only unique is mentioned
            else if ((_unique))
            {
                bool isObjUnique = true;
                foreach (UniquePSObjectHelper uniqueObj in _uniques)
                {
                    ObjectCommandComparer comparer = new ObjectCommandComparer(true, CultureInfo.CurrentCulture, true);
                    if ((comparer.Compare(obj.BaseObject, uniqueObj.WrittenObject.BaseObject) == 0) &&
                        (uniqueObj.NotePropertyCount == addedNoteProperties.Count))
                    {
                        bool found = true;
                        foreach (PSNoteProperty note in addedNoteProperties)
                        {
                            PSMemberInfo prop = uniqueObj.WrittenObject.Properties[note.Name];
                            if (prop == null || comparer.Compare(prop.Value, note.Value) != 0)
                            {
                                found = false;
                                break;
                            }
                        }
                        if (found)
                        {
                            isObjUnique = false;
                            break;
                        }
                    }
                    else
                    {
                        continue;
                    }
                }
                if (isObjUnique)
                {
                    SetPSCustomObject(obj);
                    _uniques.Add(new UniquePSObjectHelper(obj, addedNoteProperties.Count));
                }
            }
        }

        private void SetPSCustomObject(PSObject psObj)
        {
            if (psObj.ImmediateBaseObject is PSCustomObject)
                psObj.TypeNames.Insert(0, "Selected." + InputObject.BaseObject.GetType().ToString());
        }

        private void ProcessObjectAndHandleErrors(PSObject pso)
        {
            Diagnostics.Assert(pso != null, "Caller should verify pso != null");

            try
            {
                ProcessObject(pso);
            }
            catch (SelectObjectException e)
            {
                WriteError(e.ErrorRecord);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        protected override void BeginProcessing()
        {
            ProcessExpressionParameter();

            if (_unique)
            {
                _uniques = new List<UniquePSObjectHelper>();
            }

            _selectObjectQueue = new SelectObjectQueue(_first, _last, Skip, SkipLast, _firstOrLastSpecified);
        }

        private int _indexOfCurrentObject = 0;
        private int _indexCount = 0;
        /// <summary>
        /// 
        /// </summary>
        protected override void ProcessRecord()
        {
            if (InputObject != AutomationNull.Value && InputObject != null)
            {
                if (!_indexSpecified)
                {
                    _selectObjectQueue.Enqueue(InputObject);
                    PSObject streamingInputObject = _selectObjectQueue.StreamingDequeue();
                    if (streamingInputObject != null)
                    {
                        ProcessObjectAndHandleErrors(streamingInputObject);
                    }
                    if (_selectObjectQueue.AllRequestedObjectsProcessed && !this.Wait)
                    {
                        this.EndProcessing();
                        throw new StopUpstreamCommandsException(this);
                    }
                }
                else
                {
                    if (_indexOfCurrentObject < _index.Length)
                    {
                        int currentlyRequestedIndex = _index[_indexOfCurrentObject];
                        if (_indexCount == currentlyRequestedIndex)
                        {
                            ProcessObjectAndHandleErrors(InputObject);
                            while ((_indexOfCurrentObject < _index.Length) && (_index[_indexOfCurrentObject] == currentlyRequestedIndex))
                            {
                                _indexOfCurrentObject++;
                            }
                        }
                    }

                    if (!this.Wait && _indexOfCurrentObject >= _index.Length)
                    {
                        this.EndProcessing();
                        throw new StopUpstreamCommandsException(this);
                    }

                    _indexCount++;
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        protected override void EndProcessing()
        {
            // We can skip this part for 'IndexParameter' and 'SkipLastParameter' sets because:
            //   1. 'IndexParameter' set doesn't use selectObjectQueue.
            //   2. 'SkipLastParameter' set should have processed all valid input in the ProcessRecord.
            if (ParameterSetName == "DefaultParameter")
            {
                if (_first != 0)
                {
                    while ((_selectObjectQueue.Count > 0))
                    {
                        ProcessObjectAndHandleErrors(_selectObjectQueue.Dequeue());
                    }
                }
                else
                {
                    while ((_selectObjectQueue.Count > 0))
                    {
                        int lenQueue = _selectObjectQueue.Count;
                        if (lenQueue > Skip)
                        {
                            ProcessObjectAndHandleErrors(_selectObjectQueue.Dequeue());
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }

            if (_uniques != null)
            {
                foreach (UniquePSObjectHelper obj in _uniques)
                {
                    if (obj.WrittenObject == null || obj.WrittenObject == AutomationNull.Value)
                    {
                        continue;
                    }

                    WriteObject(obj.WrittenObject);
                }
            }
        }
    }

    /// <summary>
    /// Used only internally for select-object
    /// </summary>
    [SuppressMessage("Microsoft.Usage", "CA2237:MarkISerializableTypesWithSerializable", Justification = "This exception is internal and never thrown by any public API")]
    [SuppressMessage("Microsoft.Design", "CA1032:ImplementStandardExceptionConstructors", Justification = "This exception is internal and never thrown by any public API")]
    [SuppressMessage("Microsoft.Design", "CA1064:ExceptionsShouldBePublic", Justification = "This exception is internal and never thrown by any public API")]
    internal class SelectObjectException : SystemException
    {
        internal ErrorRecord ErrorRecord { get; }

        internal SelectObjectException(ErrorRecord errorRecord)
        {
            ErrorRecord = errorRecord;
        }
    }
}

