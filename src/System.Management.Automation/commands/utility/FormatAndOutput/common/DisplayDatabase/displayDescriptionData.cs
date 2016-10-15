/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

// this file contains the data structures for the in memory database
// containing display and formatting information

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.PowerShell.Commands;
using Microsoft.PowerShell.Commands.Internal.Format;

namespace Microsoft.PowerShell.Commands.Internal.Format
{
    internal enum EnumerableExpansion
    {
        /// <summary>
        /// process core only, ignore IEumerable
        /// </summary>
        CoreOnly,

        /// <summary>
        /// process IEnumerable, ignore core
        /// </summary>
        EnumOnly,

        /// <summary>
        /// process both core and IEnumerable, core first
        /// </summary>
        Both,
    }

    #region Type Info Database

    internal sealed partial class TypeInfoDataBase
    {
        // define the sections corresponding the XML file
        internal DefaultSettingsSection defaultSettingsSection = new DefaultSettingsSection();
        internal TypeGroupsSection typeGroupSection = new TypeGroupsSection();
        internal ViewDefinitionsSection viewDefinitionsSection = new ViewDefinitionsSection();
        internal FormatControlDefinitionHolder formatControlDefinitionHolder = new FormatControlDefinitionHolder();


        /// <summary>
        /// cache for resource strings in format.ps1xml
        /// </summary>
        internal DisplayResourceManagerCache displayResourceManagerCache = new DisplayResourceManagerCache();
    }

    internal sealed class DatabaseLoadingInfo
    {
        internal string fileDirectory = null;
        internal string filePath = null;
        internal bool isFullyTrusted = false;
        internal bool isProductCode = false;
        internal string xPath = null;
        internal DateTime loadTime = DateTime.Now;
    }
    #endregion


    #region Default Settings

#if _LATER
    internal class SettableOnceValue<T>
    {
        SettableOnceValue (T defaultValue)
        {
            this._default = defaultValue;
        }

        internal void f(T x)
        {
            Nullable<T> y = x;
            this._value = y;
            //this._value = (Nullable<T>)x;
        }

        internal T Value
        {
        /*
            set
            {
                if (_value == null)
                {
                    this._value = value;
                }
            }
        */
            get
            {
                if (_value != null)
                    return this._value.Value;
                return _default;
            }
        }
        
        private Nullable<T> _value;
        private T _default;
    }
#endif

    internal sealed class DefaultSettingsSection
    {
        internal bool MultilineTables
        {
            set
            {
                if (!_multilineTables.HasValue)
                {
                    _multilineTables = value;
                }
            }
            get
            {
                if (_multilineTables.HasValue)
                    return _multilineTables.Value;
                return false;
            }
        }
        private bool? _multilineTables;


        internal FormatErrorPolicy formatErrorPolicy = new FormatErrorPolicy();
        internal ShapeSelectionDirectives shapeSelectionDirectives = new ShapeSelectionDirectives();
        internal List<EnumerableExpansionDirective> enumerableExpansionDirectiveList = new List<EnumerableExpansionDirective>();
    }

    internal sealed class FormatErrorPolicy
    {
        /// <summary>
        /// if true, display error messages
        /// </summary>
        internal bool ShowErrorsAsMessages
        {
            set
            {
                if (!_showErrorsAsMessages.HasValue)
                {
                    _showErrorsAsMessages = value;
                }
            }
            get
            {
                if (_showErrorsAsMessages.HasValue)
                    return _showErrorsAsMessages.Value;
                return false;
            }
        }
        private bool? _showErrorsAsMessages;

        /// <summary>
        /// if true, display an error string in the formatted display
        /// (e.g. cell in a table)
        /// </summary>
        internal bool ShowErrorsInFormattedOutput
        {
            set
            {
                if (!_showErrorsInFormattedOutput.HasValue)
                {
                    _showErrorsInFormattedOutput = value;
                }
            }
            get
            {
                if (_showErrorsInFormattedOutput.HasValue)
                    return _showErrorsInFormattedOutput.Value;
                return false;
            }
        }
        private bool? _showErrorsInFormattedOutput;

        /// <summary>
        /// string to display in the formatted display (e.g. cell in a table)
        /// when the evaluation of an MshExpression fails
        /// </summary>
        internal string errorStringInFormattedOutput = "#ERR";

        /// <summary>
        /// string to display in the formatted display (e.g. cell in a table)
        /// when a format operation on a value fails
        /// </summary>
        internal string formatErrorStringInFormattedOutput = "#FMTERR";
    }


    internal sealed class ShapeSelectionDirectives
    {
        internal int PropertyCountForTable
        {
            set
            {
                if (!_propertyCountForTable.HasValue)
                {
                    _propertyCountForTable = value;
                }
            }
            get
            {
                if (_propertyCountForTable.HasValue)
                    return _propertyCountForTable.Value;
                return 4;
            }
        }
        private int? _propertyCountForTable;

        internal List<FormatShapeSelectionOnType> formatShapeSelectionOnTypeList = new List<FormatShapeSelectionOnType>();
    }

    internal enum FormatShape { Table, List, Wide, Complex, Undefined }

    internal abstract class FormatShapeSelectionBase
    {
        internal FormatShape formatShape = FormatShape.Undefined;
    }

    internal sealed class FormatShapeSelectionOnType : FormatShapeSelectionBase
    {
        internal AppliesTo appliesTo;
    }

    internal sealed class EnumerableExpansionDirective
    {
        internal EnumerableExpansion enumerableExpansion = EnumerableExpansion.EnumOnly;
        internal AppliesTo appliesTo;
    }

    #endregion


    #region Type Groups Definitions

    internal sealed class TypeGroupsSection
    {
        internal List<TypeGroupDefinition> typeGroupDefinitionList = new List<TypeGroupDefinition>();
    }

    internal sealed class TypeGroupDefinition
    {
        internal string name;
        internal List<TypeReference> typeReferenceList = new List<TypeReference>();
    }

    internal abstract class TypeOrGroupReference
    {
        internal string name;

        /// <summary>
        /// optional expression for conditional binding
        /// </summary>
        internal ExpressionToken conditionToken = null;
    }

    internal sealed class TypeReference : TypeOrGroupReference
    {
    }

    internal sealed class TypeGroupReference : TypeOrGroupReference
    {
    }


    #endregion


    #region Elementary Tokens

    internal abstract class FormatToken
    {
    }

    internal sealed class TextToken : FormatToken
    {
        internal string text;
        internal StringResourceReference resource;
    }

    internal sealed class NewLineToken : FormatToken
    {
        internal int count = 1;
    }

    internal sealed class FrameToken : FormatToken
    {
        /// <summary>
        /// item associated with this frame definition
        /// </summary>
        internal ComplexControlItemDefinition itemDefinition = new ComplexControlItemDefinition();

        /// <summary>
        /// frame info associated with this frame definition
        /// </summary>
        internal FrameInfoDefinition frameInfoDefinition = new FrameInfoDefinition();
    }

    internal sealed class FrameInfoDefinition
    {
        /// <summary>
        /// left indentation for a frame is relative to the parent frame.
        /// it must be a value >=0
        /// </summary>
        internal int leftIndentation = 0;

        /// <summary>
        /// right indentation for a frame is relative to the parent frame.
        /// it must be a value >=0
        /// </summary>
        internal int rightIndentation = 0;

        /// <summary>
        /// it can have the following values:
        /// 0 : ignore
        /// greater than 0 : it represents the indentation for the first line (i.e. "first line indent").
        ///                  The first line will be indented by the indicated number of characters.
        /// less than 0    : it represents the hanging of the first line WRT the following ones 
        ///                  (i.e. "first line hanging").
        /// </summary>
        internal int firstLine = 0;
    }

    internal sealed class ExpressionToken
    {
        internal ExpressionToken() { }

        internal ExpressionToken(string expressionValue, bool isScriptBlock)
        {
            this.expressionValue = expressionValue;
            this.isScriptBlock = isScriptBlock;
        }

        internal bool isScriptBlock;
        internal string expressionValue;
    }

    internal abstract class PropertyTokenBase : FormatToken
    {
        /// <summary>
        /// optional expression for conditional binding
        /// </summary>
        internal ExpressionToken conditionToken = null;

        internal ExpressionToken expression = new ExpressionToken();
        internal bool enumerateCollection = false;
    }

    internal sealed class CompoundPropertyToken : PropertyTokenBase
    {
        /// <summary>
        /// an inline control or a reference to a control definition
        /// </summary>
        internal ControlBase control = null;
    }

    internal sealed class FieldPropertyToken : PropertyTokenBase
    {
        internal FieldFormattingDirective fieldFormattingDirective = new FieldFormattingDirective();
    }

    internal sealed class FieldFormattingDirective
    {
        internal string formatString = null; // optional
    }

    #endregion Elementary Tokens


    #region Control Definitions: common data

    /// <summary>
    /// root class for all the control types
    /// </summary>
    internal abstract class ControlBase
    {
        internal static string GetControlShapeName(ControlBase control)
        {
            if (control is TableControlBody)
            {
                return FormatShape.Table.ToString();
            }
            if (control is ListControlBody)
            {
                return FormatShape.List.ToString();
            }
            if (control is WideControlBody)
            {
                return FormatShape.Wide.ToString();
            }
            if (control is ComplexControlBody)
            {
                return FormatShape.Complex.ToString();
            }
            return "";
        }

        /// <summary>
        /// Returns a Shallow Copy of the current object.
        /// </summary>
        /// <returns></returns>
        internal virtual ControlBase Copy()
        {
            System.Management.Automation.Diagnostics.Assert(false,
                "This should never be called directly on the base. Let the derived class implement this method.");
            return this;
        }
    }

    /// <summary>
    /// reference to a control
    /// </summary>
    internal sealed class ControlReference : ControlBase
    {
        /// <summary>
        /// name of the control we refer to, it cannot be null
        /// </summary>
        internal string name = null;

        /// <summary>
        /// type of the control we refer to, it cannot be null
        /// </summary>
        internal Type controlType = null;
    }

    /// <summary>
    /// base class for all control definitions
    /// NOTE: this is an extensibility point, if a new control
    /// needs to be created, it has to be derived from this class
    /// </summary>
    internal abstract class ControlBody : ControlBase
    {
        /// <summary>
        /// RULE: valid only for table and wide only
        /// </summary>
        internal bool? autosize = null;
    }

    /// <summary>
    /// class to hold a definition of a control
    /// </summary>
    internal sealed class ControlDefinition
    {
        /// <summary>
        /// name of the control we define, it cannot be null
        /// </summary>
        internal string name = null;

        /// <summary>
        /// body of the control we define, it cannot be null
        /// </summary>
        internal ControlBody controlBody = null;
    }

    #endregion


    #region View Definitions: common data
    internal sealed class ViewDefinitionsSection
    {
        internal List<ViewDefinition> viewDefinitionList = new List<ViewDefinition>();
    }

    internal sealed partial class AppliesTo
    {
        //it can contain either a type or type group reference 
        internal List<TypeOrGroupReference> referenceList = new List<TypeOrGroupReference>();
    }

    internal sealed class GroupBy
    {
        internal StartGroup startGroup = new StartGroup();
        // NOTE: extension point for describing:
        // * end group statistics
        // * end group footer
        // This can be done with defining a new Type called EndGroup with fields
        // such as stat and footer.

    }


    internal sealed class StartGroup
    {
        /// <summary>
        /// expression to be used to select the grouping
        /// </summary>
        internal ExpressionToken expression = null;

        /// <summary>
        /// an inline control or a reference to a control definition
        /// </summary>
        internal ControlBase control = null;

        /// <summary>
        /// alternative (and simplified) representation for the control
        /// RULE: if the control object is null, use this one
        /// </summary>
        internal TextToken labelTextToken = null;
    }

    /// <summary>
    /// container for control definitions
    /// </summary>
    internal sealed class FormatControlDefinitionHolder
    {
        /// <summary>
        /// list of control definitions
        /// </summary>
        internal List<ControlDefinition> controlDefinitionList = new List<ControlDefinition>();
    }

    /// <summary>
    /// definition of a view 
    /// </summary>
    internal sealed class ViewDefinition
    {
        internal DatabaseLoadingInfo loadingInfo;

        /// <summary>
        /// the name of this view. Must not be null
        /// </summary>
        internal string name;

        /// <summary>
        /// applicability of the view. Mandatory
        /// </summary>
        internal AppliesTo appliesTo = new AppliesTo();

        /// <summary>
        /// optional grouping directive
        /// </summary>
        internal GroupBy groupBy;

        /// <summary>
        /// container for optional local formatting directives
        /// </summary>
        internal FormatControlDefinitionHolder formatControlDefinitionHolder = new FormatControlDefinitionHolder();

        /// <summary>
        /// main control for the view (e.g. reference to a control or a control body
        /// </summary>
        internal ControlBase mainControl;

        /// <summary>
        /// RULE: only valid for list and complex
        /// </summary>
        internal bool outOfBand;

        /// <summary>
        /// Set if the view is for help output, used so we can prune the view from Get-FormatData
        /// because those views are too complicated and big for remoting.
        /// </summary>
        internal bool isHelpFormatter;

        internal Guid InstanceId { get; private set; }

        internal ViewDefinition()
        {
            InstanceId = Guid.NewGuid();
        }
    }

    /// <summary>
    /// base class for all the "shape"-Directive classes
    /// </summary>
    internal abstract class FormatDirective
    {
    }

    #endregion

    #region Localized Resources

    internal sealed class StringResourceReference
    {
        internal DatabaseLoadingInfo loadingInfo = null;
        internal string assemblyName = null;
        internal string baseName = null;
        internal string resourceId = null;
    }

    #endregion
}

namespace System.Management.Automation
{
    /// <summary>
    /// Specifies additional type definitions for an object
    /// </summary>
    public sealed class ExtendedTypeDefinition
    {
        /// <summary>
        /// A format definition may apply to multiple types.  This api returns
        /// the first typename that this format definition applies to, but there
        /// may be other types that apply. <see cref="TypeNames"/> should be
        /// used instead.
        /// </summary>
        public string TypeName
        {
            get { return TypeNames[0]; }
        }

        /// <summary>
        /// The list of type names this set of format definitions applies to.
        /// </summary>
        public List<string> TypeNames { get; internal set; }

        /// <summary>
        /// The formatting view definition for
        /// the specified type
        /// </summary>
        public List<FormatViewDefinition> FormatViewDefinition { get; internal set; }

        /// <summary>
        /// Overloaded to string method for
        /// better display
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return TypeName;
        }

        /// <summary>
        /// Constructor for the ExtendedTypeDefinition
        /// </summary>
        /// <param name="typeName"></param>
        /// <param name="viewDefinitions"></param>
        public ExtendedTypeDefinition(string typeName, IEnumerable<FormatViewDefinition> viewDefinitions) : this()
        {
            if (String.IsNullOrEmpty(typeName))
                throw PSTraceSource.NewArgumentNullException("typeName");
            if (viewDefinitions == null)
                throw PSTraceSource.NewArgumentNullException("viewDefinitions");

            TypeNames.Add(typeName);
            foreach (FormatViewDefinition definition in viewDefinitions)
            {
                FormatViewDefinition.Add(definition);
            }
        }

        /// <summary>
        /// Initiate an instance of ExtendedTypeDefinition with the type name
        /// </summary>
        /// <param name="typeName"></param>
        public ExtendedTypeDefinition(string typeName) : this()
        {
            if (String.IsNullOrEmpty(typeName))
                throw PSTraceSource.NewArgumentNullException("typeName");

            TypeNames.Add(typeName);
        }

        internal ExtendedTypeDefinition()
        {
            FormatViewDefinition = new List<FormatViewDefinition>();
            TypeNames = new List<string>();
        }
    }

    /// <summary>
    /// Defines a formatting view for a particular type
    /// </summary>
    [DebuggerDisplay("{Name}")]
    public sealed class FormatViewDefinition
    {
        /// <summary>Name of the formatting view as defined in the formatting file</summary>
        public string Name { get; private set; }

        /// <summary>The control defined by this formatting view can be one of table, list, wide, or custom</summary>
        public PSControl Control { get; private set; }

        /// <summary>instance id of the original view this will be used to distinguish two views with the same name and control types</summary>
        internal Guid InstanceId { get; set; }

        internal FormatViewDefinition(string name, PSControl control, Guid instanceid)
        {
            Name = name;
            Control = control;
            InstanceId = instanceid;
        }

        /// <summary/>
        public FormatViewDefinition(string name, PSControl control)
        {
            if (String.IsNullOrEmpty(name))
                throw PSTraceSource.NewArgumentNullException("name");
            if (control == null)
                throw PSTraceSource.NewArgumentNullException("control");

            Name = name;
            Control = control;
        }
    }

    /// <summary>
    /// Defines a control for the formatting types defined by PowerShell
    /// </summary>
    public abstract class PSControl
    {
        /// <summary>
        /// Each control can group items and specify a header for the group.
        /// You can group by same property value, or result of evaluating a script block.
        /// </summary>
        public PSControlGroupBy GroupBy { get; set; }

        /// <summary>
        /// When the "shape" of formatting has been determined by previous objects,
        /// sometimes you want objects of different types to continue using that shape
        /// (table, list, or whatever) even if they specify their own views, and sometimes
        /// you want your view to take over. When OutOfBand is true, the view will apply
        /// regardless of previous objects that may have selected the shape.
        /// </summary>
        public bool OutOfBand { get; set; }

        internal abstract void WriteToXml(FormatXmlWriter writer);

        internal virtual bool SafeForExport()
        {
            return GroupBy == null || GroupBy.IsSafeForExport();
        }

        internal virtual bool CompatibleWithOldPowerShell()
        {
            // This is too strict, the GroupBy would just be ignored by the remote
            // PowerShell, but that's still wrong.
            // OutOfBand is also ignored by old PowerShell, but it's of less importance.

            return GroupBy == null;
        }
    }

    /// <summary>
    /// Allows specifying a header for groups of related objects being formatted, can
    /// be specified on any type of PSControl.
    /// </summary>
    public sealed class PSControlGroupBy
    {
        /// <summary>
        /// Specifies the property or expression (script block) that controls grouping.
        /// </summary>
        public DisplayEntry Expression { get; set; }

        /// <summary>
        /// Optional - used to specify a label for the header of a group
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// Optional - used to format the header of a group
        /// </summary>
        public CustomControl CustomControl { get; set; }

        internal bool IsSafeForExport()
        {
            return (Expression == null || Expression.SafeForExport()) &&
                   (CustomControl == null || CustomControl.SafeForExport());
        }

        internal static PSControlGroupBy Get(GroupBy groupBy)
        {
            if (groupBy != null)
            {
                // TODO - groupBy.startGroup.control
                var expressionToken = groupBy.startGroup.expression;
                return new PSControlGroupBy
                {
                    Expression = new DisplayEntry(expressionToken),
                    Label = (groupBy.startGroup.labelTextToken != null) ? groupBy.startGroup.labelTextToken.text : null
                };
            }

            return null;
        }
    }

    /// <summary>
    /// One entry in a format display unit, script block or property name.
    /// </summary>
    public sealed class DisplayEntry
    {
        /// <summary>Returns the type of this value</summary>
        public DisplayEntryValueType ValueType { get; internal set; }

        /// <summary>Returns the value as a string</summary>
        public string Value { get; internal set; }

        internal DisplayEntry() { }

        /// <summary>Public constructor for DisplayEntry</summary>
        public DisplayEntry(string value, DisplayEntryValueType type)
        {
            if (String.IsNullOrEmpty(value))
                if (value == null || type == DisplayEntryValueType.Property)
                    throw PSTraceSource.NewArgumentNullException("value");

            Value = value;
            ValueType = type;
        }

        /// <summary/>
        public override string ToString()
        {
            return (ValueType == DisplayEntryValueType.Property ? "property: " : "script: ") + Value;
        }

        internal DisplayEntry(ExpressionToken expression)
        {
            Value = expression.expressionValue;
            ValueType = expression.isScriptBlock ? DisplayEntryValueType.ScriptBlock : DisplayEntryValueType.Property;

            if (String.IsNullOrEmpty(Value))
                if (Value == null || ValueType == DisplayEntryValueType.Property)
                    throw PSTraceSource.NewArgumentNullException("value");
        }

        internal bool SafeForExport()
        {
            return ValueType != DisplayEntryValueType.ScriptBlock;
        }
    }

    /// <summary>
    /// Each control (table, list, wide, or custom) may have multiple entries. If there are multiple
    /// entries, there must be a default entry with no condition, all other entries must have EntrySelectedBy
    /// specified. This is useful when you need a single view for grouping or otherwise just selecting the
    /// shape of formatting, but need distinct formatting rules for each instance.  For example, when
    /// listing files, you may want to group based on the parent path, but select different entries
    /// depending on if the item is a file or directory.
    /// </summary>
    public sealed class EntrySelectedBy
    {
        /// <summary>
        /// An optional list of typenames that apply to the entry.
        /// </summary>
        public List<string> TypeNames { get; set; }

        /// <summary>
        /// An optional condition that applies to the entry.
        /// </summary>
        public List<DisplayEntry> SelectionCondition { get; set; }

        internal static EntrySelectedBy Get(IEnumerable<string> entrySelectedByType, IEnumerable<DisplayEntry> entrySelectedByCondition)
        {
            EntrySelectedBy result = null;
            if (entrySelectedByType != null || entrySelectedByCondition != null)
            {
                result = new EntrySelectedBy();
                bool isEmpty = true;
                if (entrySelectedByType != null)
                {
                    result.TypeNames = new List<string>(entrySelectedByType);
                    if (result.TypeNames.Count > 0)
                        isEmpty = false;
                }
                if (entrySelectedByCondition != null)
                {
                    result.SelectionCondition = new List<DisplayEntry>(entrySelectedByCondition);
                    if (result.SelectionCondition.Count > 0)
                        isEmpty = false;
                }

                if (isEmpty)
                    return null;
            }

            return result;
        }

        internal static EntrySelectedBy Get(List<TypeOrGroupReference> references)
        {
            EntrySelectedBy result = null;
            if (references != null && references.Count > 0)
            {
                result = new EntrySelectedBy();
                foreach (TypeOrGroupReference tr in references)
                {
                    if (tr.conditionToken != null)
                    {
                        if (result.SelectionCondition == null) result.SelectionCondition = new List<DisplayEntry>();

                        result.SelectionCondition.Add(new DisplayEntry(tr.conditionToken));
                        continue;
                    }

                    if (tr is TypeGroupReference)
                        continue;

                    if (result.TypeNames == null) result.TypeNames = new List<string>();

                    result.TypeNames.Add(tr.name);
                }
            }

            return result;
        }

        internal bool SafeForExport()
        {
            if (SelectionCondition == null)
                return true;

            foreach (var cond in SelectionCondition)
            {
                if (!cond.SafeForExport())
                    return false;
            }

            return true;
        }

        internal bool CompatibleWithOldPowerShell()
        {
            // Old versions of PowerShell know nothing about selection conditions.
            return SelectionCondition == null || SelectionCondition.Count == 0;
        }
    }

    /// <summary>
    /// Specifies possible alignment enumerations for display cells
    /// </summary>
    public enum Alignment
    {
        /// <summary>
        /// not defined
        /// </summary>
        Undefined = 0,

        /// <summary>
        /// left of the cell, contents will trail with a ... if exceeded - ex "Display..."
        /// </summary>
        Left = 1,

        /// <summary>
        /// center of the cell
        /// </summary>
        Center = 2,

        /// <summary>
        /// right of the cell, contents will lead with a ... if exceeded - ex "...456"
        /// </summary>
        Right = 3,
    }

    /// <summary>
    /// Specifies the type of entry value
    /// </summary>
    public enum DisplayEntryValueType
    {
        /// <summary>
        /// The value is a property. Look for a property with the specified name
        /// </summary>
        Property = 0,

        /// <summary>
        /// The value is a scriptblock. Evaluate the script block and fill the entry with the result
        /// </summary>
        ScriptBlock = 1,
    }
}
