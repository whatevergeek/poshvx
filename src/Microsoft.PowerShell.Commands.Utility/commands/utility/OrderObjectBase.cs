/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Collections.Generic;
using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Internal;
using System.Globalization;
using Microsoft.PowerShell.Commands.Internal.Format;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.PowerShell.Commands
{
    /// <summary>
    /// definitions for hash table keys
    /// </summary>
    internal static class SortObjectParameterDefinitionKeys
    {
        internal const string AscendingEntryKey = "ascending";
        internal const string DescendingEntryKey = "descending";
    }

    /// <summary>
    /// </summary>
    internal class SortObjectExpressionParameterDefinition : CommandParameterDefinition
    {
        protected override void SetEntries()
        {
            this.hashEntries.Add(new ExpressionEntryDefinition(false));
            this.hashEntries.Add(new BooleanEntryDefinition(SortObjectParameterDefinitionKeys.AscendingEntryKey));
            this.hashEntries.Add(new BooleanEntryDefinition(SortObjectParameterDefinitionKeys.DescendingEntryKey));
        }
    }

    /// <summary>
    /// </summary>
    internal class GroupObjectExpressionParameterDefinition : CommandParameterDefinition
    {
        protected override void SetEntries()
        {
            this.hashEntries.Add(new ExpressionEntryDefinition(true));
        }
    }

    /// <summary>
    /// Base Cmdlet for cmdlets which deal with raw objects
    /// </summary>
    public class ObjectCmdletBase : PSCmdlet
    {
        #region Parameters
        /// <summary>
        /// 
        /// </summary>
        /// <value></value>
        [Parameter]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("GoldMan", "#pw17903:UseOfLCID", Justification = "The CultureNumber is only used if the property has been set with a hex string starting with 0x")]
        public string Culture
        {
            get { return _cultureInfo != null ? _cultureInfo.ToString() : null; }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    _cultureInfo = null;
                    return;
                }

#if !CORECLR //TODO: CORECLR 'new  CultureInfo(Int32)' not available yet.
                int cultureNumber;
                string trimmedValue = value.Trim();
                if (trimmedValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    if ((trimmedValue.Length > 2) &&
                        int.TryParse(trimmedValue.Substring(2), NumberStyles.AllowHexSpecifier,
                                  CultureInfo.CurrentCulture, out cultureNumber))
                    {
                        _cultureInfo = new CultureInfo(cultureNumber);
                        return;
                    }
                }
                else if (int.TryParse(trimmedValue, NumberStyles.AllowThousands,
                                 CultureInfo.CurrentCulture, out cultureNumber))
                {
                    _cultureInfo = new CultureInfo(cultureNumber);
                    return;
                }
#endif
                _cultureInfo = new CultureInfo(value);
            }
        }
        internal CultureInfo _cultureInfo = null;

        /// <summary>
        /// 
        /// </summary>
        /// <value></value>
        [Parameter]
        public SwitchParameter CaseSensitive
        {
            get { return _caseSensitive; }
            set { _caseSensitive = value; }
        }
        private bool _caseSensitive;
        #endregion Parameters
    }

    /// <summary>
    /// Base Cmdlet for object cmdlets that deal with Grouping, Sorting and Comparison.
    /// </summary>
    public abstract class ObjectBase : ObjectCmdletBase
    {
        #region Parameters

        /// <summary>
        /// 
        /// </summary>
        [Parameter(ValueFromPipeline = true)]
        public PSObject InputObject { set; get; } = AutomationNull.Value;

        /// <summary>
        /// Gets or Sets the Properties that would be used for Grouping, Sorting and Comparison.
        /// </summary>
        [Parameter(Position = 0)]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public object[] Property { get; set; }

        #endregion Parameters
    }

    /// <summary>
    /// Base Cmdlet for object cmdlets that deal with Ordering and Comparison.
    /// </summary>
    public class OrderObjectBase : ObjectBase
    {
        #region Internal Properties

        /// <summary>
        /// Specifies sorting order. 
        /// </summary>
        internal SwitchParameter DescendingOrder
        {
            get { return !_ascending; }
            set { _ascending = !value; }
        }
        private bool _ascending = true;

        internal List<PSObject> InputObjects { get; } = new List<PSObject>();

        /// <summary>
        /// CultureInfo converted from the Culture Cmdlet parameter
        /// </summary>
        internal CultureInfo ConvertedCulture
        {
            get
            {
                return _cultureInfo;
            }
        }

        #endregion Internal Properties

        /// <summary>
        /// 
        /// Simply accumulates the incoming objects
        /// 
        /// </summary>
        protected override void ProcessRecord()
        {
            if (InputObject != null && InputObject != AutomationNull.Value)
            {
                InputObjects.Add(InputObject);
            }
        }
    }

    internal sealed class OrderByProperty
    {
        #region Internal properties

        /// <summary>
        /// a logical matrix where each row is an input object and its property values specified by Properties
        /// </summary>
        internal List<OrderByPropertyEntry> OrderMatrix { get; } = null;

        internal OrderByPropertyComparer Comparer { get; } = null;

        internal List<MshParameter> MshParameterList
        {
            get
            {
                return _mshParameterList;
            }
        }
        #endregion Internal properties

        #region Utils
        // These are made static for Measure-Object's GroupBy parameter that measure the outputs of Group-Object
        // However, Measure-Object differs from Group-Object and Sort-Object considerably that it should not
        // be built on the same base class, i.e., this class. Moreover, Measure-Object's Property parameter is
        // a string array and allows wildcard.
        // Yes, the Cmdlet is needed. It's used to get the TerminatingErrorContext, WriteError and WriteDebug.

        #region process MshExpression and MshParameter

        private static void ProcessExpressionParameter(
            List<PSObject> inputObjects,
            PSCmdlet cmdlet,
            object[] expr,
            out List<MshParameter> mshParameterList)
        {
            mshParameterList = null;
            TerminatingErrorContext invocationContext = new TerminatingErrorContext(cmdlet);
            // compare-object and group-object use the same definition here
            ParameterProcessor processor = cmdlet is SortObjectCommand ?
                new ParameterProcessor(new SortObjectExpressionParameterDefinition()) :
                new ParameterProcessor(new GroupObjectExpressionParameterDefinition());

            if (expr == null && inputObjects != null && inputObjects.Count > 0)
            {
                expr = GetDefaultKeyPropertySet(inputObjects[0]);
            }
            if (expr != null)
            {
                List<MshParameter> unexpandedParameterList = processor.ProcessParameters(expr, invocationContext);
                mshParameterList = ExpandExpressions(inputObjects, unexpandedParameterList);
            }
            // NOTE: if no parameters are passed, we will look at the default keys of the first
            // incoming object
        }

        internal void ProcessExpressionParameter(
            PSCmdlet cmdlet,
            object[] expr)
        {
            TerminatingErrorContext invocationContext = new TerminatingErrorContext(cmdlet);
            // compare-object and group-object use the same definition here
            ParameterProcessor processor = cmdlet is SortObjectCommand ?
                new ParameterProcessor(new SortObjectExpressionParameterDefinition()) :
                new ParameterProcessor(new GroupObjectExpressionParameterDefinition());

            if (expr != null)
            {
                if (_unexpandedParameterList == null)
                {
                    _unexpandedParameterList = processor.ProcessParameters(expr, invocationContext);

                    foreach (MshParameter unexpandedParameter in _unexpandedParameterList)
                    {
                        MshExpression mshExpression = (MshExpression)unexpandedParameter.GetEntry(FormatParameterDefinitionKeys.ExpressionEntryKey);
                        if (!mshExpression.HasWildCardCharacters) // this special cases 1) script blocks and 2) wildcard-less strings
                        {
                            _mshParameterList.Add(unexpandedParameter);
                        }
                        else
                        {
                            if (_unExpandedParametersWithWildCardPattern == null)
                            {
                                _unExpandedParametersWithWildCardPattern = new List<MshParameter>();
                            }

                            _unExpandedParametersWithWildCardPattern.Add(unexpandedParameter);
                        }
                    }
                }
            }
        }

        // Expand a list of (possibly wildcarded) expressions into resolved expressions that
        // match property names on the incoming objects.
        private static List<MshParameter> ExpandExpressions(List<PSObject> inputObjects, List<MshParameter> unexpandedParameterList)
        {
            List<MshParameter> expandedParameterList = new List<MshParameter>();

            if (unexpandedParameterList != null)
            {
                foreach (MshParameter unexpandedParameter in unexpandedParameterList)
                {
                    MshExpression ex = (MshExpression)unexpandedParameter.GetEntry(FormatParameterDefinitionKeys.ExpressionEntryKey);
                    if (!ex.HasWildCardCharacters) // this special cases 1) script blocks and 2) wildcard-less strings
                    {
                        expandedParameterList.Add(unexpandedParameter);
                    }
                    else
                    {
                        SortedDictionary<string, MshExpression> expandedPropertyNames = new SortedDictionary<string, MshExpression>(StringComparer.OrdinalIgnoreCase);
                        if (inputObjects != null)
                        {
                            foreach (object inputObject in inputObjects)
                            {
                                if (inputObject == null)
                                {
                                    continue;
                                }

                                foreach (MshExpression resolvedName in ex.ResolveNames(PSObject.AsPSObject(inputObject)))
                                {
                                    expandedPropertyNames[resolvedName.ToString()] = resolvedName;
                                }
                            }
                        }

                        foreach (MshExpression expandedExpression in expandedPropertyNames.Values)
                        {
                            MshParameter expandedParameter = new MshParameter();
                            expandedParameter.hash = (Hashtable)unexpandedParameter.hash.Clone();
                            expandedParameter.hash[FormatParameterDefinitionKeys.ExpressionEntryKey] = expandedExpression;

                            expandedParameterList.Add(expandedParameter);
                        }
                    }
                }
            }

            return expandedParameterList;
        }

        // Expand a list of (possibly wildcarded) expressions into resolved expressions that
        // match property names on the incoming objects.
        private static void ExpandExpressions(PSObject inputObject, List<MshParameter> UnexpandedParametersWithWildCardPattern, List<MshParameter> expandedParameterList)
        {
            if (UnexpandedParametersWithWildCardPattern != null)
            {
                foreach (MshParameter unexpandedParameter in UnexpandedParametersWithWildCardPattern)
                {
                    MshExpression ex = (MshExpression)unexpandedParameter.GetEntry(FormatParameterDefinitionKeys.ExpressionEntryKey);

                    SortedDictionary<string, MshExpression> expandedPropertyNames = new SortedDictionary<string, MshExpression>(StringComparer.OrdinalIgnoreCase);
                    if (inputObject == null)
                    {
                        continue;
                    }

                    foreach (MshExpression resolvedName in ex.ResolveNames(PSObject.AsPSObject(inputObject)))
                    {
                        expandedPropertyNames[resolvedName.ToString()] = resolvedName;
                    }

                    foreach (MshExpression expandedExpression in expandedPropertyNames.Values)
                    {
                        MshParameter expandedParameter = new MshParameter();
                        expandedParameter.hash = (Hashtable)unexpandedParameter.hash.Clone();
                        expandedParameter.hash[FormatParameterDefinitionKeys.ExpressionEntryKey] = expandedExpression;

                        expandedParameterList.Add(expandedParameter);
                    }
                }
            }
        }

        internal static string[] GetDefaultKeyPropertySet(PSObject mshObj)
        {
            PSMemberSet standardNames = mshObj.PSStandardMembers;
            if (standardNames == null)
            {
                return null;
            }
            PSPropertySet defaultKeys = standardNames.Members["DefaultKeyPropertySet"] as PSPropertySet;

            if (defaultKeys == null)
            {
                return null;
            }
            string[] props = new string[defaultKeys.ReferencedPropertyNames.Count];
            defaultKeys.ReferencedPropertyNames.CopyTo(props, 0);
            return props;
        }

        #endregion process MshExpression and MshParameter

        internal static List<OrderByPropertyEntry> CreateOrderMatrix(
            PSCmdlet cmdlet,
            List<PSObject> inputObjects,
            List<MshParameter> mshParameterList
            )
        {
            List<OrderByPropertyEntry> orderMatrixToCreate = new List<OrderByPropertyEntry>();
            foreach (PSObject so in inputObjects)
            {
                if (so == null || so == AutomationNull.Value)
                    continue;
                List<ErrorRecord> evaluationErrors = new List<ErrorRecord>();
                List<string> propertyNotFoundMsgs = new List<string>();
                OrderByPropertyEntry result =
                    OrderByPropertyEntryEvaluationHelper.ProcessObject(so, mshParameterList, evaluationErrors, propertyNotFoundMsgs);
                foreach (ErrorRecord err in evaluationErrors)
                {
                    cmdlet.WriteError(err);
                }
                foreach (string debugMsg in propertyNotFoundMsgs)
                {
                    cmdlet.WriteDebug(debugMsg);
                }
                orderMatrixToCreate.Add(result);
            }

            return orderMatrixToCreate;
        }

        private static bool isOrderEntryKeyDefined(object orderEntryKey)
        {
            return orderEntryKey != null && orderEntryKey != AutomationNull.Value;
        }

        private static OrderByPropertyComparer CreateComparer(
            List<OrderByPropertyEntry> orderMatrix,
            List<MshParameter> mshParameterList,
            bool ascending,
            CultureInfo cultureInfo,
            bool caseSensitive)
        {
            if (orderMatrix == null || orderMatrix.Count == 0)
            {
                return null;
            }
            Nullable<bool>[] ascendingOverrides = null;
            if (mshParameterList != null && mshParameterList.Count != 0)
            {
                ascendingOverrides = new Nullable<bool>[mshParameterList.Count];
                for (int k = 0; k < ascendingOverrides.Length; k++)
                {
                    object ascendingVal = mshParameterList[k].GetEntry(
                        SortObjectParameterDefinitionKeys.AscendingEntryKey);
                    object descendingVal = mshParameterList[k].GetEntry(
                        SortObjectParameterDefinitionKeys.DescendingEntryKey);
                    bool isAscendingDefined = isOrderEntryKeyDefined(ascendingVal);
                    bool isDescendingDefined = isOrderEntryKeyDefined(descendingVal);
                    if (!isAscendingDefined && !isDescendingDefined)
                    {
                        // if neither ascending nor descending is defined
                        ascendingOverrides[k] = null;
                    }
                    else if (isAscendingDefined && isDescendingDefined &&
                        (bool)ascendingVal == (bool)descendingVal)
                    {
                        // if both ascending and descending defined but their values conflict
                        // they are ignored.
                        ascendingOverrides[k] = null;
                    }
                    else if (isAscendingDefined)
                    {
                        ascendingOverrides[k] = (bool)ascendingVal;
                    }
                    else
                    {
                        ascendingOverrides[k] = !(bool)descendingVal;
                    }
                }
            }
            OrderByPropertyComparer comparer =
                OrderByPropertyComparer.CreateComparer(orderMatrix, ascending,
                ascendingOverrides, cultureInfo, caseSensitive);

            return comparer;
        }

        internal OrderByProperty(
            PSCmdlet cmdlet,
            List<PSObject> inputObjects,
            object[] expr,
            bool ascending,
            CultureInfo cultureInfo,
            bool caseSensitive
            )
        {
            Diagnostics.Assert(cmdlet != null, "cmdlet must be an instance");

            ProcessExpressionParameter(inputObjects, cmdlet, expr, out _mshParameterList);
            OrderMatrix = CreateOrderMatrix(cmdlet, inputObjects, _mshParameterList);
            Comparer = CreateComparer(OrderMatrix, _mshParameterList, ascending, cultureInfo, caseSensitive);
        }

        /// <summary>
        /// OrderByProperty constructor.
        /// </summary>
        internal OrderByProperty()
        {
            _mshParameterList = new List<MshParameter>();
            OrderMatrix = new List<OrderByPropertyEntry>();
        }

        /// <summary>
        /// Utility function used to create OrderByPropertyEntry for the supplied input object.
        /// </summary>
        /// <param name="cmdlet">PSCmdlet</param>
        /// <param name="inputObject">Input Object.</param>
        /// <param name="isCaseSensitive">Indicates if the Property value comparisons need to be case sensitive or not.</param>
        /// <param name="cultureInfo">Culture Info that needs to be used for comparison.</param>
        /// <returns>OrderByPropertyEntry for the supplied InputObject.</returns>
        internal OrderByPropertyEntry CreateOrderByPropertyEntry(
            PSCmdlet cmdlet,
            PSObject inputObject,
            bool isCaseSensitive,
            CultureInfo cultureInfo)
        {
            Diagnostics.Assert(cmdlet != null, "cmdlet must be an instance");

            if (_unExpandedParametersWithWildCardPattern != null)
            {
                ExpandExpressions(inputObject, _unExpandedParametersWithWildCardPattern, _mshParameterList);
            }

            List<ErrorRecord> evaluationErrors = new List<ErrorRecord>();
            List<string> propertyNotFoundMsgs = new List<string>();
            OrderByPropertyEntry result =
                OrderByPropertyEntryEvaluationHelper.ProcessObject(inputObject, _mshParameterList, evaluationErrors, propertyNotFoundMsgs, isCaseSensitive, cultureInfo);
            foreach (ErrorRecord err in evaluationErrors)
            {
                cmdlet.WriteError(err);
            }
            foreach (string debugMsg in propertyNotFoundMsgs)
            {
                cmdlet.WriteDebug(debugMsg);
            }

            return result;
        }

        #endregion Utils

        // list of processed parameters obtained from the Expression array
        private List<MshParameter> _mshParameterList = null;

        // list of unprocessed parameters obtained from the Expression array.
        private List<MshParameter> _unexpandedParameterList = null;

        // list of unprocessed parameters with wild card patterns.
        private List<MshParameter> _unExpandedParametersWithWildCardPattern = null;
    }

    internal static class OrderByPropertyEntryEvaluationHelper
    {
        internal static OrderByPropertyEntry ProcessObject(PSObject inputObject, List<MshParameter> mshParameterList,
            List<ErrorRecord> errors, List<string> propertyNotFoundMsgs, bool isCaseSensitive = false, CultureInfo cultureInfo = null)
        {
            Diagnostics.Assert(errors != null, "errors cannot be null!");
            Diagnostics.Assert(propertyNotFoundMsgs != null, "propertyNotFoundMsgs cannot be null!");
            OrderByPropertyEntry entry = new OrderByPropertyEntry();
            entry.inputObject = inputObject;

            if (mshParameterList == null || mshParameterList.Count == 0)
            {
                // we do not have a property to evaluate, we sort on $_
                entry.orderValues.Add(new ObjectCommandPropertyValue(inputObject, isCaseSensitive, cultureInfo));
                return entry;
            }

            // we need to compute the properties
            foreach (MshParameter p in mshParameterList)
            {
                string propertyNotFoundMsg = null;
                EvaluateSortingExpression(p, inputObject, entry.orderValues, errors, out propertyNotFoundMsg);
                if (!string.IsNullOrEmpty(propertyNotFoundMsg))
                {
                    propertyNotFoundMsgs.Add(propertyNotFoundMsg);
                }
            }
            return entry;
        }

        private static void EvaluateSortingExpression(
            MshParameter p,
            PSObject inputObject,
            List<ObjectCommandPropertyValue> orderValues,
            List<ErrorRecord> errors, out string propertyNotFoundMsg)
        {
            // NOTE: we assume globbing was not allowed in input
            MshExpression ex = p.GetEntry(FormatParameterDefinitionKeys.ExpressionEntryKey) as MshExpression;

            // get the values, but do not expand aliases
            List<MshExpressionResult> expressionResults = ex.GetValues(inputObject, false, true);

            if (expressionResults.Count == 0)
            {
                // we did not get any result out of the expression:
                // we enter a null as a place holder
                orderValues.Add(ObjectCommandPropertyValue.NonExistingProperty);
                propertyNotFoundMsg = StringUtil.Format(SortObjectStrings.PropertyNotFound, ex.ToString());
                return;
            }
            propertyNotFoundMsg = null;
            // we obtained some results, enter them into the list
            foreach (MshExpressionResult r in expressionResults)
            {
                if (r.Exception == null)
                {
                    orderValues.Add(new ObjectCommandPropertyValue(r.Result));
                }
                else
                {
                    ErrorRecord errorRecord = new ErrorRecord(
                        r.Exception,
                        "ExpressionEvaluation",
                        ErrorCategory.InvalidResult,
                        inputObject);
                    errors.Add(errorRecord);
                    orderValues.Add(ObjectCommandPropertyValue.ExistingNullProperty);
                }
            }
        }
    }

    /// <summary>
    /// This is the row of the OrderMatrix
    /// </summary>
    internal sealed class OrderByPropertyEntry
    {
        internal PSObject inputObject = null;
        internal List<ObjectCommandPropertyValue> orderValues = new List<ObjectCommandPropertyValue>();
    }

    internal class OrderByPropertyComparer : IComparer<OrderByPropertyEntry>
    {
        internal OrderByPropertyComparer(bool[] ascending, CultureInfo cultureInfo, bool caseSensitive)
        {
            _propertyComparers = new ObjectCommandComparer[ascending.Length];
            for (int k = 0; k < ascending.Length; k++)
            {
                _propertyComparers[k] = new ObjectCommandComparer(ascending[k], cultureInfo, caseSensitive);
            }
        }

        public int Compare(OrderByPropertyEntry firstEntry, OrderByPropertyEntry secondEntry)
        {
            // we have to take into consideration that some vectors
            // might be shorter than others
            int order = 0;
            for (int k = 0; k < _propertyComparers.Length; k++)
            {
                ObjectCommandPropertyValue firstValue = (k < firstEntry.orderValues.Count) ?
                    firstEntry.orderValues[k] : ObjectCommandPropertyValue.NonExistingProperty;
                ObjectCommandPropertyValue secondValue = (k < secondEntry.orderValues.Count) ?
                    secondEntry.orderValues[k] : ObjectCommandPropertyValue.NonExistingProperty;
                order = _propertyComparers[k].Compare(firstValue, secondValue);

                if (order != 0)
                    return order;
            }
            return order;
        }

        internal static OrderByPropertyComparer CreateComparer(List<OrderByPropertyEntry> orderMatrix, bool ascendingFlag, Nullable<bool>[] ascendingOverrides, CultureInfo cultureInfo, bool caseSensitive)
        {
            if (orderMatrix.Count == 0)
                return null;

            // create a comparer able to handle a vector of N entries,
            // where N is the max number of entries
            int maxEntries = 0;
            foreach (OrderByPropertyEntry entry in orderMatrix)
            {
                if (entry.orderValues.Count > maxEntries)
                    maxEntries = entry.orderValues.Count;
            }
            if (maxEntries == 0)
                return null;

            bool[] ascending = new bool[maxEntries];
            for (int k = 0; k < maxEntries; k++)
            {
                if (ascendingOverrides != null && ascendingOverrides[k].HasValue)
                {
                    ascending[k] = ascendingOverrides[k].Value;
                }
                else
                {
                    ascending[k] = ascendingFlag;
                }
            }

            // NOTE: the size of the boolean array will determine the max width of the 
            // vectors to check
            return new OrderByPropertyComparer(ascending, cultureInfo, caseSensitive);
        }

        private ObjectCommandComparer[] _propertyComparers = null;
    }
}

