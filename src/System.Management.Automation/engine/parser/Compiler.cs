/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Management.Automation.Internal;
using System.Management.Automation.Interpreter;
using System.Management.Automation.Runspaces;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.PowerShell.Commands;

namespace System.Management.Automation.Language
{
    using KeyValuePair = Tuple<ExpressionAst, StatementAst>;
    using IfClause = Tuple<PipelineBaseAst, StatementBlockAst>;

    internal static class CachedReflectionInfo
    {
        // ReSharper disable InconsistentNaming
        internal const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        internal const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.NonPublic;
        internal const BindingFlags staticPublicFlags = BindingFlags.Static | BindingFlags.Public;
        internal const BindingFlags instancePublicFlags = BindingFlags.Instance | BindingFlags.Public;

        internal static readonly ConstructorInfo ObjectList_ctor =
            typeof(List<object>).GetConstructor(PSTypeExtensions.EmptyTypes);
        internal static readonly MethodInfo ObjectList_ToArray =
            typeof(List<object>).GetMethod(nameof(List<object>.ToArray), PSTypeExtensions.EmptyTypes);

        internal static readonly MethodInfo ArrayOps_GetMDArrayValue =
            typeof(ArrayOps).GetMethod(nameof(ArrayOps.GetMDArrayValue), staticFlags);
        internal static readonly MethodInfo ArrayOps_GetMDArrayValueOrSlice =
            typeof(ArrayOps).GetMethod(nameof(ArrayOps.GetMDArrayValueOrSlice), staticFlags);
        internal static readonly MethodInfo ArrayOps_GetNonIndexable =
            typeof(ArrayOps).GetMethod(nameof(ArrayOps.GetNonIndexable), staticFlags);
        internal static readonly MethodInfo ArrayOps_IndexStringMessage =
            typeof(ArrayOps).GetMethod(nameof(ArrayOps.IndexStringMessage), staticFlags);
        internal static readonly MethodInfo ArrayOps_Multiply =
            typeof(ArrayOps).GetMethod(nameof(ArrayOps.Multiply), staticFlags);
        internal static readonly MethodInfo ArrayOps_SetMDArrayValue =
            typeof(ArrayOps).GetMethod(nameof(ArrayOps.SetMDArrayValue), staticFlags);
        internal static readonly MethodInfo ArrayOps_SlicingIndex =
            typeof(ArrayOps).GetMethod(nameof(ArrayOps.SlicingIndex), staticFlags);

        internal static readonly ConstructorInfo BreakException_ctor =
            typeof(BreakException).GetConstructor(instanceFlags, null, CallingConventions.Standard,
                                                  new Type[] { typeof(string) }, null);

        internal static readonly MethodInfo CharOps_CompareIeq =
            typeof(CharOps).GetMethod(nameof(CharOps.CompareIeq), staticFlags);
        internal static readonly MethodInfo CharOps_CompareIne =
            typeof(CharOps).GetMethod(nameof(CharOps.CompareIne), staticFlags);
        internal static readonly MethodInfo CharOps_CompareStringIeq =
            typeof(CharOps).GetMethod(nameof(CharOps.CompareStringIeq), staticFlags);
        internal static readonly MethodInfo CharOps_CompareStringIne =
            typeof(CharOps).GetMethod(nameof(CharOps.CompareStringIne), staticFlags);

        internal static readonly MethodInfo CommandParameterInternal_CreateArgument =
            typeof(CommandParameterInternal).GetMethod(nameof(CommandParameterInternal.CreateArgument), staticFlags);
        internal static readonly MethodInfo CommandParameterInternal_CreateParameter =
            typeof(CommandParameterInternal).GetMethod(nameof(CommandParameterInternal.CreateParameter), staticFlags);
        internal static readonly MethodInfo CommandParameterInternal_CreateParameterWithArgument =
            typeof(CommandParameterInternal).GetMethod(nameof(CommandParameterInternal.CreateParameterWithArgument), staticFlags);

        internal static readonly MethodInfo CommandProcessorBase_CheckForSevereException =
            typeof(CommandProcessorBase).GetMethod(nameof(CommandProcessorBase.CheckForSevereException), staticFlags);

        internal static readonly MethodInfo CommandRedirection_UnbindForExpression =
            typeof(CommandRedirection).GetMethod(nameof(CommandRedirection.UnbindForExpression), instanceFlags);

        internal static readonly ConstructorInfo ContinueException_ctor =
            typeof(ContinueException).GetConstructor(instanceFlags, null, CallingConventions.Standard,
                                                     new Type[] { typeof(string) }, null);

        internal static readonly MethodInfo Convert_ChangeType =
            typeof(Convert).GetMethod(nameof(Convert.ChangeType), new Type[] { typeof(object), typeof(Type) });

        internal static readonly MethodInfo Debugger_EnterScriptFunction =
            typeof(ScriptDebugger).GetMethod(nameof(ScriptDebugger.EnterScriptFunction), instanceFlags);
        internal static readonly MethodInfo Debugger_ExitScriptFunction =
            typeof(ScriptDebugger).GetMethod(nameof(ScriptDebugger.ExitScriptFunction), instanceFlags);
        internal static readonly MethodInfo Debugger_OnSequencePointHit =
            typeof(ScriptDebugger).GetMethod(nameof(ScriptDebugger.OnSequencePointHit), instanceFlags);

        internal static readonly MethodInfo EnumerableOps_AddEnumerable =
            typeof(EnumerableOps).GetMethod(nameof(EnumerableOps.AddEnumerable), staticFlags);
        internal static readonly MethodInfo EnumerableOps_AddObject =
            typeof(EnumerableOps).GetMethod(nameof(EnumerableOps.AddObject), staticFlags);
        internal static readonly MethodInfo EnumerableOps_Compare =
            typeof(EnumerableOps).GetMethod(nameof(EnumerableOps.Compare), staticFlags);
        internal static readonly MethodInfo EnumerableOps_Current =
            typeof(EnumerableOps).GetMethod(nameof(EnumerableOps.Current), staticFlags);
        internal static readonly MethodInfo EnumerableOps_GetEnumerator =
            typeof(EnumerableOps).GetMethod(nameof(EnumerableOps.GetEnumerator), staticFlags);
        internal static readonly MethodInfo EnumerableOps_GetCOMEnumerator =
            typeof(EnumerableOps).GetMethod(nameof(EnumerableOps.GetCOMEnumerator), staticFlags);
        internal static readonly MethodInfo EnumerableOps_GetGenericEnumerator =
            typeof(EnumerableOps).GetMethod(nameof(EnumerableOps.GetGenericEnumerator), staticFlags);
        internal static readonly MethodInfo EnumerableOps_GetSlice =
            typeof(EnumerableOps).GetMethod(nameof(EnumerableOps.GetSlice), staticFlags);
        internal static readonly MethodInfo EnumerableOps_MethodInvoker =
            typeof(EnumerableOps).GetMethod(nameof(EnumerableOps.MethodInvoker), staticFlags);
        internal static readonly MethodInfo EnumerableOps_Where =
            typeof(EnumerableOps).GetMethod(nameof(EnumerableOps.Where), staticFlags);
        internal static readonly MethodInfo EnumerableOps_ForEach =
            typeof(EnumerableOps).GetMethod(nameof(EnumerableOps.ForEach), staticFlags);
        internal static readonly MethodInfo EnumerableOps_MoveNext =
            typeof(EnumerableOps).GetMethod(nameof(EnumerableOps.MoveNext), staticFlags);
        internal static readonly MethodInfo EnumerableOps_Multiply =
            typeof(EnumerableOps).GetMethod(nameof(EnumerableOps.Multiply), staticFlags);
        internal static readonly MethodInfo EnumerableOps_PropertyGetter =
            typeof(EnumerableOps).GetMethod(nameof(EnumerableOps.PropertyGetter), staticFlags);
        internal static readonly MethodInfo EnumerableOps_SlicingIndex =
            typeof(EnumerableOps).GetMethod(nameof(EnumerableOps.SlicingIndex), staticFlags);
        internal static readonly MethodInfo EnumerableOps_ToArray =
            typeof(EnumerableOps).GetMethod(nameof(EnumerableOps.ToArray), staticFlags);
        internal static readonly MethodInfo EnumerableOps_WriteEnumerableToPipe =
            typeof(EnumerableOps).GetMethod(nameof(EnumerableOps.WriteEnumerableToPipe), staticFlags);

        internal static readonly ConstructorInfo ErrorRecord__ctor =
            typeof(ErrorRecord).GetConstructor(instanceFlags | BindingFlags.Public, null, CallingConventions.Standard,
                                               new Type[] { typeof(ErrorRecord), typeof(RuntimeException) }, null);

        internal static readonly PropertyInfo Exception_Message =
            typeof(Exception).GetProperty(nameof(Exception.Message));

        internal static readonly MethodInfo ExceptionHandlingOps_CheckActionPreference =
            typeof(ExceptionHandlingOps).GetMethod(nameof(ExceptionHandlingOps.CheckActionPreference), staticFlags);
        internal static readonly MethodInfo ExceptionHandlingOps_ConvertToArgumentConversionException =
            typeof(ExceptionHandlingOps).GetMethod(nameof(ExceptionHandlingOps.ConvertToArgumentConversionException), staticFlags);
        internal static readonly MethodInfo ExceptionHandlingOps_ConvertToException =
            typeof(ExceptionHandlingOps).GetMethod(nameof(ExceptionHandlingOps.ConvertToException), staticFlags);
        internal static readonly MethodInfo ExceptionHandlingOps_ConvertToMethodInvocationException =
            typeof(ExceptionHandlingOps).GetMethod(nameof(ExceptionHandlingOps.ConvertToMethodInvocationException), staticFlags);
        internal static readonly MethodInfo ExceptionHandlingOps_ConvertToRuntimeException =
            typeof(ExceptionHandlingOps).GetMethod(nameof(ExceptionHandlingOps.ConvertToRuntimeException), staticFlags);
        internal static readonly MethodInfo ExceptionHandlingOps_FindMatchingHandler =
            typeof(ExceptionHandlingOps).GetMethod(nameof(ExceptionHandlingOps.FindMatchingHandler), staticFlags);
        internal static readonly MethodInfo ExceptionHandlingOps_RestoreStoppingPipeline =
            typeof(ExceptionHandlingOps).GetMethod(nameof(ExceptionHandlingOps.RestoreStoppingPipeline), staticFlags);
        internal static readonly MethodInfo ExceptionHandlingOps_SuspendStoppingPipeline =
            typeof(ExceptionHandlingOps).GetMethod(nameof(ExceptionHandlingOps.SuspendStoppingPipeline), staticFlags);

        internal static readonly PropertyInfo ExecutionContext_CurrentExceptionBeingHandled =
            typeof(ExecutionContext).GetProperty(nameof(ExecutionContext.CurrentExceptionBeingHandled), instanceFlags);
        internal static readonly FieldInfo ExecutionContext_Debugger =
            typeof(ExecutionContext).GetField(nameof(ExecutionContext._debugger), instanceFlags);
        internal static readonly FieldInfo ExecutionContext_DebuggingMode =
            typeof(ExecutionContext).GetField(nameof(ExecutionContext._debuggingMode), instanceFlags);
        internal static readonly PropertyInfo ExecutionContext_ExceptionHandlerInEnclosingStatementBlock =
            typeof(ExecutionContext).GetProperty(nameof(ExecutionContext.PropagateExceptionsToEnclosingStatementBlock), instanceFlags);
        internal static readonly MethodInfo ExecutionContext_IsStrictVersion =
            typeof(ExecutionContext).GetMethod(nameof(ExecutionContext.IsStrictVersion), staticFlags);
        internal static readonly PropertyInfo ExecutionContext_QuestionMarkVariableValue =
            typeof(ExecutionContext).GetProperty(nameof(ExecutionContext.QuestionMarkVariableValue), instanceFlags);
        internal static readonly PropertyInfo ExecutionContext_LanguageMode =
            typeof(ExecutionContext).GetProperty(nameof(ExecutionContext.LanguageMode), instanceFlags);
        internal static readonly PropertyInfo ExecutionContext_EngineIntrinsics =
            typeof(ExecutionContext).GetProperty(nameof(ExecutionContext.EngineIntrinsics), instanceFlags);
        internal static readonly PropertyInfo ExecutionContext_ShellFunctionErrorOutputPipe =
            typeof(ExecutionContext).GetProperty(nameof(ExecutionContext.ShellFunctionErrorOutputPipe), instanceFlags);
        internal static readonly PropertyInfo ExecutionContext_TypeTable =
            typeof(ExecutionContext).GetProperty(nameof(ExecutionContext.TypeTable), instanceFlags);

        internal static readonly PropertyInfo ExecutionContext_ExpressionWarningOutputPipe =
            typeof(ExecutionContext).GetProperty(nameof(ExecutionContext.ExpressionWarningOutputPipe), instanceFlags);
        internal static readonly PropertyInfo ExecutionContext_ExpressionVerboseOutputPipe =
            typeof(ExecutionContext).GetProperty(nameof(ExecutionContext.ExpressionVerboseOutputPipe), instanceFlags);
        internal static readonly PropertyInfo ExecutionContext_ExpressionDebugOutputPipe =
            typeof(ExecutionContext).GetProperty(nameof(ExecutionContext.ExpressionDebugOutputPipe), instanceFlags);
        internal static readonly PropertyInfo ExecutionContext_ExpressionInformationOutputPipe =
            typeof(ExecutionContext).GetProperty(nameof(ExecutionContext.ExpressionInformationOutputPipe), instanceFlags);

        internal static readonly MethodInfo FileRedirection_BindForExpression =
            typeof(FileRedirection).GetMethod(nameof(FileRedirection.BindForExpression), instanceFlags);
        internal static readonly ConstructorInfo FileRedirection_ctor =
            typeof(FileRedirection).GetConstructor(instanceFlags, null, CallingConventions.Standard,
                                                   new Type[] { typeof(RedirectionStream), typeof(bool), typeof(string) }, null);
        internal static readonly MethodInfo FileRedirection_Dispose =
            typeof(FileRedirection).GetMethod(nameof(FileRedirection.Dispose));

        internal static readonly FieldInfo FunctionContext__currentSequencePointIndex =
            typeof(FunctionContext).GetField(nameof(FunctionContext._currentSequencePointIndex), instanceFlags);
        internal static readonly FieldInfo FunctionContext__executionContext =
            typeof(FunctionContext).GetField(nameof(FunctionContext._executionContext), instanceFlags);
        internal static readonly FieldInfo FunctionContext__functionName =
            typeof(FunctionContext).GetField(nameof(FunctionContext._functionName), instanceFlags);
        internal static readonly FieldInfo FunctionContext__localsTuple =
            typeof(FunctionContext).GetField(nameof(FunctionContext._localsTuple), instanceFlags);
        internal static readonly FieldInfo FunctionContext__outputPipe =
            typeof(FunctionContext).GetField(nameof(FunctionContext._outputPipe), instanceFlags);
        internal static readonly FieldInfo FunctionContext__traps =
            typeof(FunctionContext).GetField(nameof(FunctionContext._traps), instanceFlags);
        internal static readonly MethodInfo FunctionContext_PopTrapHandlers =
            typeof(FunctionContext).GetMethod(nameof(FunctionContext.PopTrapHandlers), instanceFlags);
        internal static readonly MethodInfo FunctionContext_PushTrapHandlers =
            typeof(FunctionContext).GetMethod(nameof(FunctionContext.PushTrapHandlers), instanceFlags);

        internal static readonly MethodInfo FunctionOps_DefineFunction =
            typeof(FunctionOps).GetMethod(nameof(FunctionOps.DefineFunction), staticFlags);
        internal static readonly MethodInfo FunctionOps_DefineWorkflows =
            typeof(FunctionOps).GetMethod(nameof(FunctionOps.DefineWorkflows), staticFlags);

        internal static readonly ConstructorInfo Hashtable_ctor =
            typeof(Hashtable).GetConstructor(BindingFlags.Instance | BindingFlags.Public, null,
                                             CallingConventions.Standard, new Type[] { typeof(int), typeof(IEqualityComparer) }, null);

        internal static readonly MethodInfo HashtableOps_Add =
            typeof(HashtableOps).GetMethod(nameof(HashtableOps.Add), staticFlags);
        internal static readonly MethodInfo HashtableOps_AddKeyValuePair =
            typeof(HashtableOps).GetMethod(nameof(HashtableOps.AddKeyValuePair), staticFlags);

        internal static readonly PropertyInfo ICollection_Count =
            typeof(ICollection).GetProperty(nameof(ICollection.Count));

        internal static readonly MethodInfo IComparable_CompareTo =
            typeof(IComparable).GetMethod(nameof(IComparable.CompareTo));

        internal static readonly MethodInfo IDictionary_set_Item =
            typeof(IDictionary).GetMethod("set_Item");

        internal static readonly MethodInfo IDisposable_Dispose =
            typeof(IDisposable).GetMethod(nameof(IDisposable.Dispose));

        internal static readonly MethodInfo IEnumerable_GetEnumerator =
            typeof(IEnumerable).GetMethod(nameof(IEnumerable.GetEnumerator));

        internal static readonly PropertyInfo IEnumerator_Current =
            typeof(IEnumerator).GetProperty(nameof(IEnumerator.Current));
        internal static readonly MethodInfo IEnumerator_MoveNext =
            typeof(IEnumerator).GetMethod(nameof(IEnumerator.MoveNext));

        internal static readonly MethodInfo IList_get_Item =
            typeof(IList).GetMethod("get_Item");

        internal static readonly MethodInfo InterpreterError_NewInterpreterException =
            typeof(InterpreterError).GetMethod(nameof(InterpreterError.NewInterpreterException), staticFlags);
        internal static readonly MethodInfo InterpreterError_NewInterpreterExceptionWithInnerException =
            typeof(InterpreterError).GetMethod(nameof(InterpreterError.NewInterpreterExceptionWithInnerException), staticFlags);

        internal static readonly MethodInfo IntOps_Range =
            typeof(IntOps).GetMethod(nameof(IntOps.Range), staticFlags);

        internal static readonly MethodInfo LanguagePrimitives_GetInvalidCastMessages =
            typeof(LanguagePrimitives).GetMethod(nameof(LanguagePrimitives.GetInvalidCastMessages), staticFlags);
        internal static readonly MethodInfo LanguagePrimitives_ThrowInvalidCastException =
            typeof(LanguagePrimitives).GetMethod(nameof(LanguagePrimitives.ThrowInvalidCastException), staticFlags);

        internal static readonly MethodInfo LocalPipeline_GetExecutionContextFromTLS =
            typeof(LocalPipeline).GetMethod(nameof(LocalPipeline.GetExecutionContextFromTLS), staticFlags);

        internal static readonly MethodInfo LoopFlowException_MatchLabel =
            typeof(LoopFlowException).GetMethod(nameof(LoopFlowException.MatchLabel), instanceFlags);

        internal static readonly MethodInfo MergingRedirection_BindForExpression =
            typeof(MergingRedirection).GetMethod(nameof(MergingRedirection.BindForExpression), instanceFlags);

        internal static readonly ConstructorInfo MethodException_ctor =
            typeof(MethodException).GetConstructor(instanceFlags, null,
                                                   new Type[] { typeof(string), typeof(Exception), typeof(string), typeof(object[]) }, null);

        internal static readonly MethodInfo MutableTuple_IsValueSet =
            typeof(MutableTuple).GetMethod(nameof(MutableTuple.IsValueSet), instanceFlags);

        internal static readonly MethodInfo Object_Equals =
            typeof(object).GetMethod(nameof(object.Equals), new Type[] { typeof(object) });

        internal static readonly ConstructorInfo OrderedDictionary_ctor =
            typeof(OrderedDictionary).GetConstructor(BindingFlags.Instance | BindingFlags.Public, null,
                                                     CallingConventions.Standard, new Type[] { typeof(int), typeof(IEqualityComparer) }, null);

        internal static readonly MethodInfo Parser_ScanNumber =
            typeof(Parser).GetMethod(nameof(Parser.ScanNumber), staticFlags);

        internal static readonly MethodInfo ParserOps_ContainsOperatorCompiled =
            typeof(ParserOps).GetMethod(nameof(ParserOps.ContainsOperatorCompiled), staticFlags);
        internal static readonly MethodInfo ParserOps_ImplicitOp =
            typeof(ParserOps).GetMethod(nameof(ParserOps.ImplicitOp), staticFlags);
        internal static readonly MethodInfo ParserOps_JoinOperator =
            typeof(ParserOps).GetMethod(nameof(ParserOps.JoinOperator), staticFlags);
        internal static readonly MethodInfo ParserOps_LikeOperator =
            typeof(ParserOps).GetMethod(nameof(ParserOps.LikeOperator), staticFlags);
        internal static readonly MethodInfo ParserOps_MatchOperator =
            typeof(ParserOps).GetMethod(nameof(ParserOps.MatchOperator), staticFlags);
        internal static readonly MethodInfo ParserOps_ReplaceOperator =
            typeof(ParserOps).GetMethod(nameof(ParserOps.ReplaceOperator), staticFlags);
        internal static readonly MethodInfo ParserOps_SplitOperator =
            typeof(ParserOps).GetMethod(nameof(ParserOps.SplitOperator), staticFlags);
        internal static readonly MethodInfo ParserOps_UnaryJoinOperator =
            typeof(ParserOps).GetMethod(nameof(ParserOps.UnaryJoinOperator), staticFlags);
        internal static readonly MethodInfo ParserOps_UnarySplitOperator =
            typeof(ParserOps).GetMethod(nameof(ParserOps.UnarySplitOperator), staticFlags);

        internal static readonly ConstructorInfo Pipe_ctor =
            typeof(Pipe).GetConstructor(instanceFlags, null, CallingConventions.Standard, new Type[] { typeof(List<object>) }, null);
        internal static readonly MethodInfo Pipe_Add =
            typeof(Pipe).GetMethod(nameof(Pipe.Add), instanceFlags);
        internal static readonly MethodInfo Pipe_SetVariableListForTemporaryPipe =
            typeof(Pipe).GetMethod(nameof(Pipe.SetVariableListForTemporaryPipe), instanceFlags);

        internal static readonly MethodInfo PipelineOps_CheckAutomationNullInCommandArgument =
            typeof(PipelineOps).GetMethod(nameof(PipelineOps.CheckAutomationNullInCommandArgument), staticFlags);
        internal static readonly MethodInfo PipelineOps_CheckAutomationNullInCommandArgumentArray =
            typeof(PipelineOps).GetMethod(nameof(PipelineOps.CheckAutomationNullInCommandArgumentArray), staticFlags);
        internal static readonly MethodInfo PipelineOps_CheckForInterrupts =
            typeof(PipelineOps).GetMethod(nameof(PipelineOps.CheckForInterrupts), staticFlags);
        internal static readonly MethodInfo PipelineOps_GetExitException =
            typeof(PipelineOps).GetMethod(nameof(PipelineOps.GetExitException), staticFlags);
        internal static readonly MethodInfo PipelineOps_FlushPipe =
            typeof(PipelineOps).GetMethod(nameof(PipelineOps.FlushPipe), staticFlags);
        internal static readonly MethodInfo PipelineOps_InvokePipeline =
            typeof(PipelineOps).GetMethod(nameof(PipelineOps.InvokePipeline), staticFlags);
        internal static readonly MethodInfo PipelineOps_Nop =
            typeof(PipelineOps).GetMethod(nameof(PipelineOps.Nop), staticFlags);
        internal static readonly MethodInfo PipelineOps_PipelineResult =
            typeof(PipelineOps).GetMethod(nameof(PipelineOps.PipelineResult), staticFlags);
        internal static readonly MethodInfo PipelineOps_ClearPipe =
            typeof(PipelineOps).GetMethod(nameof(PipelineOps.ClearPipe), staticFlags);

        internal static readonly MethodInfo PSGetDynamicMemberBinder_GetIDictionaryMember =
            typeof(PSGetDynamicMemberBinder).GetMethod(nameof(PSGetDynamicMemberBinder.GetIDictionaryMember), staticFlags);

        internal static readonly MethodInfo PSGetMemberBinder_CloneMemberInfo =
            typeof(PSGetMemberBinder).GetMethod(nameof(PSGetMemberBinder.CloneMemberInfo), staticFlags);
        internal static readonly MethodInfo PSGetMemberBinder_GetAdaptedValue =
            typeof(PSGetMemberBinder).GetMethod(nameof(PSGetMemberBinder.GetAdaptedValue), staticFlags);
        internal static readonly MethodInfo PSGetMemberBinder_GetTypeTableFromTLS =
            typeof(PSGetMemberBinder).GetMethod(nameof(PSGetMemberBinder.GetTypeTableFromTLS), staticFlags);
        internal static readonly MethodInfo PSGetMemberBinder_IsTypeNameSame =
            typeof(PSGetMemberBinder).GetMethod(nameof(PSGetMemberBinder.IsTypeNameSame), staticFlags);
        internal static readonly MethodInfo PSGetMemberBinder_TryGetGenericDictionaryValue =
            typeof(PSGetMemberBinder).GetMethod(nameof(PSGetMemberBinder.TryGetGenericDictionaryValue), staticFlags);
        internal static readonly MethodInfo PSGetMemberBinder_TryGetInstanceMember =
            typeof(PSGetMemberBinder).GetMethod(nameof(PSGetMemberBinder.TryGetInstanceMember), staticFlags);
        internal static readonly MethodInfo PSGetMemberBinder_TryGetIDictionaryValue =
            typeof(PSGetMemberBinder).GetMethod(nameof(PSGetMemberBinder.TryGetIDictionaryValue), staticFlags);

        internal static readonly MethodInfo PSInvokeMemberBinder_InvokeAdaptedMember =
            typeof(PSInvokeMemberBinder).GetMethod(nameof(PSInvokeMemberBinder.InvokeAdaptedMember), staticFlags);
        internal static readonly MethodInfo PSInvokeMemberBinder_InvokeAdaptedSetMember =
            typeof(PSInvokeMemberBinder).GetMethod(nameof(PSInvokeMemberBinder.InvokeAdaptedSetMember), staticFlags);
        internal static readonly MethodInfo PSInvokeMemberBinder_IsHeterogeneousArray =
            typeof(PSInvokeMemberBinder).GetMethod(nameof(PSInvokeMemberBinder.IsHeterogeneousArray), staticFlags);
        internal static readonly MethodInfo PSInvokeMemberBinder_IsHomogenousArray =
            typeof(PSInvokeMemberBinder).GetMethod(nameof(PSInvokeMemberBinder.IsHomogenousArray), staticFlags);
        internal static readonly MethodInfo PSInvokeMemberBinder_TryGetInstanceMethod =
            typeof(PSInvokeMemberBinder).GetMethod(nameof(PSInvokeMemberBinder.TryGetInstanceMethod), staticFlags);

        internal static readonly MethodInfo PSMethodInfo_Invoke =
            typeof(PSMethodInfo).GetMethod(nameof(PSMethodInfo.Invoke));

        internal static readonly PropertyInfo PSNoteProperty_Value =
            typeof(PSNoteProperty).GetProperty(nameof(PSNoteProperty.Value));

        internal static readonly MethodInfo PSObject_Base =
            typeof(PSObject).GetMethod(nameof(PSObject.Base), staticFlags);
        internal static readonly PropertyInfo PSObject_BaseObject =
            typeof(PSObject).GetProperty(nameof(PSObject.BaseObject));
        internal static readonly FieldInfo PSObject_isDeserialized =
            typeof(PSObject).GetField(nameof(PSObject.isDeserialized), instanceFlags);
        internal static readonly MethodInfo PSObject_ToStringParser =
            typeof(PSObject).GetMethod(nameof(PSObject.ToStringParser), staticFlags);

        internal static readonly PropertyInfo PSReference_Value =
            typeof(PSReference).GetProperty(nameof(PSReference.Value));

        internal static readonly MethodInfo PSScriptMethod_InvokeScript =
            typeof(PSScriptMethod).GetMethod(nameof(PSScriptMethod.InvokeScript), staticFlags);

        internal static readonly MethodInfo PSScriptProperty_InvokeGetter =
            typeof(PSScriptProperty).GetMethod(nameof(PSScriptProperty.InvokeGetter), instanceFlags);
        internal static readonly MethodInfo PSScriptProperty_InvokeSetter =
            typeof(PSScriptProperty).GetMethod(nameof(PSScriptProperty.InvokeSetter), instanceFlags);

        internal static readonly MethodInfo PSSetMemberBinder_SetAdaptedValue =
            typeof(PSSetMemberBinder).GetMethod(nameof(PSSetMemberBinder.SetAdaptedValue), staticFlags);

        internal static readonly MethodInfo PSVariableAssignmentBinder_CopyInstanceMembersOfValueType =
            typeof(PSVariableAssignmentBinder).GetMethod(nameof(PSVariableAssignmentBinder.CopyInstanceMembersOfValueType), staticFlags);
        internal static readonly FieldInfo PSVariableAssignmentBinder__mutableValueWithInstanceMemberVersion =
            typeof(PSVariableAssignmentBinder).GetField(nameof(PSVariableAssignmentBinder.s_mutableValueWithInstanceMemberVersion), staticFlags);

        internal static readonly MethodInfo PSCreateInstanceBinder_IsTargetTypeNonPublic =
            typeof(PSCreateInstanceBinder).GetMethod(nameof(PSCreateInstanceBinder.IsTargetTypeNonPublic), staticFlags);
        internal static readonly MethodInfo PSCreateInstanceBinder_GetTargetTypeName =
            typeof(PSCreateInstanceBinder).GetMethod(nameof(PSCreateInstanceBinder.GetTargetTypeName), staticFlags);

        internal static readonly ConstructorInfo RangeEnumerator_ctor =
            typeof(RangeEnumerator).GetConstructor(new Type[] { typeof(int), typeof(int) });

        internal static readonly MethodInfo ReservedNameMembers_GeneratePSAdaptedMemberSet =
            typeof(ReservedNameMembers).GetMethod(nameof(ReservedNameMembers.GeneratePSAdaptedMemberSet), staticFlags);
        internal static readonly MethodInfo ReservedNameMembers_GeneratePSBaseMemberSet =
            typeof(ReservedNameMembers).GetMethod(nameof(ReservedNameMembers.GeneratePSBaseMemberSet), staticFlags);
        internal static readonly MethodInfo ReservedNameMembers_GeneratePSExtendedMemberSet =
            typeof(ReservedNameMembers).GetMethod(nameof(ReservedNameMembers.GeneratePSExtendedMemberSet), staticFlags);
        internal static readonly MethodInfo ReservedNameMembers_GeneratePSObjectMemberSet =
            typeof(ReservedNameMembers).GetMethod(nameof(ReservedNameMembers.GeneratePSObjectMemberSet), staticFlags);
        internal static readonly MethodInfo ReservedNameMembers_PSTypeNames =
            typeof(ReservedNameMembers).GetMethod(nameof(ReservedNameMembers.PSTypeNames));

        internal static readonly MethodInfo RestrictedLanguageChecker_CheckDataStatementLanguageModeAtRuntime =
            typeof(RestrictedLanguageChecker).GetMethod(nameof(RestrictedLanguageChecker.CheckDataStatementLanguageModeAtRuntime), staticFlags);
        internal static readonly MethodInfo RestrictedLanguageChecker_CheckDataStatementAstAtRuntime =
            typeof(RestrictedLanguageChecker).GetMethod(nameof(RestrictedLanguageChecker.CheckDataStatementAstAtRuntime), staticFlags);

        internal static readonly MethodInfo RestrictedLanguageChecker_EnsureUtilityModuleLoaded =
            typeof(RestrictedLanguageChecker).GetMethod(nameof(RestrictedLanguageChecker.EnsureUtilityModuleLoaded), staticFlags);



        internal static readonly ConstructorInfo ReturnException_ctor =
            typeof(ReturnException).GetConstructor(instanceFlags, null, CallingConventions.Standard, new Type[] { typeof(object) }, null);

        internal static readonly PropertyInfo RuntimeException_ErrorRecord =
            typeof(RuntimeException).GetProperty(nameof(RuntimeException.ErrorRecord));

        internal static readonly MethodInfo ScriptBlock_DoInvokeReturnAsIs =
            typeof(ScriptBlock).GetMethod(nameof(ScriptBlock.DoInvokeReturnAsIs), instanceFlags);
        internal static readonly MethodInfo ScriptBlock_InvokeAsDelegateHelper =
            typeof(ScriptBlock).GetMethod(nameof(ScriptBlock.InvokeAsDelegateHelper), instanceFlags);
        internal static readonly MethodInfo ScriptBlock_InvokeAsMemberFunction =
            typeof(ScriptBlock).GetMethod(nameof(ScriptBlock.InvokeAsMemberFunction), instanceFlags);

        internal static readonly MethodInfo ScriptBlockExpressionWrapper_GetScriptBlock =
            typeof(ScriptBlockExpressionWrapper).GetMethod(nameof(ScriptBlockExpressionWrapper.GetScriptBlock), instanceFlags);

        internal static readonly ConstructorInfo SetValueException_ctor =
            typeof(SetValueException).GetConstructor(instanceFlags, null,
                                                     new[] { typeof(string), typeof(Exception), typeof(string), typeof(object[]) }, null);

        internal static readonly ConstructorInfo GetValueException_ctor =
            typeof(GetValueException).GetConstructor(instanceFlags, null,
                                                     new[] { typeof(string), typeof(Exception), typeof(string), typeof(object[]) }, null);
        internal static readonly ConstructorInfo StreamReader_ctor =
            typeof(StreamReader).GetConstructor(new Type[] { typeof(string) });
        internal static readonly MethodInfo StreamReader_ReadLine =
            typeof(StreamReader).GetMethod(nameof(StreamReader.ReadLine));

        internal static readonly ConstructorInfo String_ctor_char_int =
            typeof(String).GetConstructor(new Type[] { typeof(char), typeof(int) });
        internal static readonly MethodInfo String_Concat_String =
            typeof(String).GetMethod(nameof(String.Concat), staticPublicFlags, null,
                                     CallingConventions.Standard, new Type[] { typeof(string), typeof(string) }, null);
        internal static readonly MethodInfo String_Equals =
            typeof(String).GetMethod(nameof(String.Equals), staticPublicFlags, null,
                                     CallingConventions.Standard,
                                     new Type[] { typeof(string), typeof(string), typeof(StringComparison) }, null);
        internal static readonly MethodInfo String_get_Chars =
            typeof(string).GetMethod("get_Chars");
        internal static readonly PropertyInfo String_Length =
            typeof(string).GetProperty(nameof(String.Length));

        internal static readonly MethodInfo StringOps_Compare =
            typeof(StringOps).GetMethod(nameof(StringOps.Compare), staticFlags);
        internal static readonly MethodInfo StringOps_Equals =
            typeof(StringOps).GetMethod(nameof(StringOps.Equals), staticFlags);

        internal static readonly MethodInfo StringOps_FormatOperator =
            typeof(StringOps).GetMethod(nameof(StringOps.FormatOperator), staticFlags);
        internal static readonly MethodInfo StringOps_Multiply =
            typeof(StringOps).GetMethod(nameof(StringOps.Multiply), staticFlags);

        internal static readonly MethodInfo SwitchOps_ConditionSatisfiedRegex =
            typeof(SwitchOps).GetMethod(nameof(SwitchOps.ConditionSatisfiedRegex), staticFlags);
        internal static readonly MethodInfo SwitchOps_ConditionSatisfiedWildcard =
            typeof(SwitchOps).GetMethod(nameof(SwitchOps.ConditionSatisfiedWildcard), staticFlags);
        internal static readonly MethodInfo SwitchOps_ResolveFilePath =
            typeof(SwitchOps).GetMethod(nameof(SwitchOps.ResolveFilePath), staticFlags);

        internal static readonly MethodInfo TypeOps_AsOperator =
            typeof(TypeOps).GetMethod(nameof(TypeOps.AsOperator), staticFlags);
        internal static readonly MethodInfo TypeOps_AddPowerShellTypesToTheScope =
            typeof(TypeOps).GetMethod(nameof(TypeOps.AddPowerShellTypesToTheScope), staticFlags);
        internal static readonly MethodInfo TypeOps_InitPowerShellTypesAtRuntime =
            typeof(TypeOps).GetMethod(nameof(TypeOps.InitPowerShellTypesAtRuntime), staticFlags);
        internal static readonly MethodInfo TypeOps_SetCurrentTypeResolutionState =
            typeof(TypeOps).GetMethod(nameof(TypeOps.SetCurrentTypeResolutionState), staticFlags);
        internal static readonly MethodInfo TypeOps_SetAssemblyDefiningPSTypes =
            typeof(TypeOps).GetMethod(nameof(TypeOps.SetAssemblyDefiningPSTypes), staticFlags);
        internal static readonly MethodInfo TypeOps_IsInstance =
            typeof(TypeOps).GetMethod(nameof(TypeOps.IsInstance), staticFlags);
        internal static readonly MethodInfo TypeOps_ResolveTypeName =
            typeof(TypeOps).GetMethod(nameof(TypeOps.ResolveTypeName), staticFlags);

        internal static readonly MethodInfo VariableOps_GetUsingValue =
            typeof(VariableOps).GetMethod(nameof(VariableOps.GetUsingValue), staticFlags);
        internal static readonly MethodInfo VariableOps_GetVariableAsRef =
            typeof(VariableOps).GetMethod(nameof(VariableOps.GetVariableAsRef), staticFlags);
        internal static readonly MethodInfo VariableOps_GetVariableValue =
            typeof(VariableOps).GetMethod(nameof(VariableOps.GetVariableValue), staticFlags);
        internal static readonly MethodInfo VariableOps_GetAutomaticVariableValue =
            typeof(VariableOps).GetMethod(nameof(VariableOps.GetAutomaticVariableValue), staticFlags);
        internal static readonly MethodInfo VariableOps_SetVariableValue =
            typeof(VariableOps).GetMethod(nameof(VariableOps.SetVariableValue), staticFlags);

        internal static readonly MethodInfo ClassOps_ValidateSetProperty =
            typeof(ClassOps).GetMethod(nameof(ClassOps.ValidateSetProperty), staticPublicFlags);
        internal static readonly MethodInfo ClassOps_CallBaseCtor =
            typeof(ClassOps).GetMethod(nameof(ClassOps.CallBaseCtor), staticPublicFlags);
        internal static readonly MethodInfo ClassOps_CallMethodNonVirtually =
            typeof(ClassOps).GetMethod(nameof(ClassOps.CallMethodNonVirtually), staticPublicFlags);
        internal static readonly MethodInfo ClassOps_CallVoidMethodNonVirtually =
            typeof(ClassOps).GetMethod(nameof(ClassOps.CallVoidMethodNonVirtually), staticPublicFlags);

        internal static readonly MethodInfo ArgumentTransformationAttribute_Transform =
            typeof(ArgumentTransformationAttribute).GetMethod(nameof(ArgumentTransformationAttribute.Transform), instancePublicFlags);
        // ReSharper restore InconsistentNaming
    }

    internal static class ExpressionCache
    {
        internal static readonly Expression NullConstant = Expression.Constant(null);
        internal static readonly Expression NullExecutionContext = Expression.Constant(null, typeof(ExecutionContext));
        internal static readonly Expression NullPSObject = Expression.Constant(null, typeof(PSObject));
        internal static readonly Expression NullEnumerator = Expression.Constant(null, typeof(IEnumerator));
        internal static readonly Expression NullExtent = Expression.Constant(null, typeof(IScriptExtent));
        internal static readonly Expression NullTypeTable = Expression.Constant(null, typeof(TypeTable));
        internal static readonly Expression NullFormatProvider = Expression.Constant(null, typeof(IFormatProvider));
        internal static readonly Expression NullObjectArray = Expression.Constant(null, typeof(object[]));
        internal static readonly Expression AutomationNullConstant = Expression.Constant(AutomationNull.Value, typeof(object));
        internal static readonly Expression NullCommandRedirections = Expression.Constant(null, typeof(CommandRedirection[][]));
        internal static readonly Expression NullTypeArray = Expression.Constant(null, typeof(Type[]));
        internal static readonly Expression NullType = Expression.Constant(null, typeof(Type));
        internal static readonly Expression NullDelegateArray = Expression.Constant(null, typeof(Action<FunctionContext>[]));
        internal static readonly Expression NullPipe = Expression.Constant(new Pipe { NullPipe = true });
        internal static readonly Expression ConstEmptyString = Expression.Constant("");
        internal static readonly Expression CompareOptionsIgnoreCase = Expression.Constant(CompareOptions.IgnoreCase);
        internal static readonly Expression CompareOptionsNone = Expression.Constant(CompareOptions.None);
        internal static readonly Expression Ordinal = Expression.Constant(StringComparison.Ordinal);
        internal static readonly Expression InvariantCulture = Expression.Constant(CultureInfo.InvariantCulture);
        internal static readonly Expression CurrentCultureIgnoreCaseComparer = Expression.Constant(StringComparer.CurrentCultureIgnoreCase, typeof(StringComparer));
        internal static readonly Expression CatchAllType = Expression.Constant(typeof(ExceptionHandlingOps.CatchAll), typeof(Type));
        internal static readonly Expression Empty = Expression.Empty();
        internal static Expression GetExecutionContextFromTLS =
            Expression.Call(CachedReflectionInfo.LocalPipeline_GetExecutionContextFromTLS);

        internal static readonly Expression BoxedTrue = Expression.Field(null,
            typeof(Boxed).GetField("True", BindingFlags.Static | BindingFlags.NonPublic));
        internal static readonly Expression BoxedFalse = Expression.Field(null,
            typeof(Boxed).GetField("False", BindingFlags.Static | BindingFlags.NonPublic));

        private static readonly Expression[] s_intConstants = new Expression[102];

        internal static Expression Constant(int i)
        {
            // We cache -1..100, anything else don't bother caching
            if (i < -1 || i > 100)
            {
                return Expression.Constant(i);
            }

            Expression result = s_intConstants[i + 1];
            if (result == null)
            {
                result = Expression.Constant(i);
                s_intConstants[i + 1] = result;
            }
            return result;
        }

        internal static readonly Expression TrueConstant = Expression.Constant(true);
        internal static readonly Expression FalseConstant = Expression.Constant(false);

        internal static Expression Constant(bool b)
        {
            return b ? TrueConstant : FalseConstant;
        }
    }

    internal static class ExpressionExtensions
    {
        internal static Expression Convert(this Expression expr, Type type)
        {
            if (expr.Type == type)
            {
                return expr;
            }

            if (expr.Type == typeof(void))
            {
                // If we're converting from void, use $null instead to figure the conversion.
                expr = ExpressionCache.NullConstant;
            }

            var conversion = LanguagePrimitives.GetConversionRank(expr.Type, type);
            if (conversion == ConversionRank.Assignable)
            {
                return Expression.Convert(expr, type);
            }

            if (type.GetTypeInfo().ContainsGenericParameters)
            {
                return Expression.Call(
                    CachedReflectionInfo.LanguagePrimitives_ThrowInvalidCastException,
                    expr.Cast(typeof(object)),
                    Expression.Constant(type, typeof(Type)));
            }

            return DynamicExpression.Dynamic(PSConvertBinder.Get(type), type, expr);
        }

        internal static Expression Cast(this Expression expr, Type type)
        {
            if (expr.Type == type)
            {
                return expr;
            }

            if ((expr.Type.IsFloating() || expr.Type == typeof(Decimal)) && type.GetTypeInfo().IsPrimitive)
            {
                // Convert correctly handles most "primitive" conversions for PowerShell,
                // but it does not correctly handle floating point.

                expr = Expression.Call(CachedReflectionInfo.Convert_ChangeType,
                                       Expression.Convert(expr, typeof(object)),
                                       Expression.Constant(type, typeof(Type)));
            }
            return Expression.Convert(expr, type);
        }

#if ENABLE_BINDER_DEBUG_LOGGING && !CORECLR
        internal static string ToDebugString(this Expression expr)
        {
            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                DebugViewWriter.WriteTo(expr, writer);
                return writer.ToString();
            }
        }
#endif
    }

    internal class FunctionContext
    {
        internal ScriptBlock _scriptBlock;
        internal string _file;
        internal bool _debuggerHidden;
        internal bool _debuggerStepThrough;
        internal IScriptExtent[] _sequencePoints;
        internal ExecutionContext _executionContext;
        internal Pipe _outputPipe;
        internal BitArray _breakPoints;
        internal List<LineBreakpoint> _boundBreakpoints;
        internal int _currentSequencePointIndex;
        internal MutableTuple _localsTuple;
        internal List<Tuple<Type[], Action<FunctionContext>[], Type[]>> _traps = new List<Tuple<Type[], Action<FunctionContext>[], Type[]>>();
        internal string _functionName;

        internal IScriptExtent CurrentPosition
        {
            get { return _sequencePoints != null ? _sequencePoints[_currentSequencePointIndex] : PositionUtilities.EmptyExtent; }
        }

        internal void PushTrapHandlers(Type[] type, Action<FunctionContext>[] handler, Type[] tupleType)
        {
            _traps.Add(Tuple.Create(type, handler, tupleType));
        }

        internal void PopTrapHandlers()
        {
            _traps.RemoveAt(_traps.Count - 1);
        }
    }

    internal class Compiler : ICustomAstVisitor2
    {
        internal static readonly ParameterExpression _executionContextParameter;
        internal static readonly ParameterExpression _functionContext;
        internal static readonly ParameterExpression _returnPipe;
        private static readonly Expression s_setDollarQuestionToTrue;
        private static readonly Expression s_callCheckForInterrupts;
        private static readonly Expression s_getCurrentPipe;
        private static readonly Expression s_currentExceptionBeingHandled;
        private static readonly CatchBlock s_catchFlowControl;

        internal static readonly CatchBlock[] _stmtCatchHandlers;
        internal static readonly Type DottedLocalsTupleType = MutableTuple.MakeTupleType(SpecialVariables.AutomaticVariableTypes);
        internal static Type DottedScriptCmdletLocalsTupleType =
            MutableTuple.MakeTupleType(SpecialVariables.AutomaticVariableTypes.Concat(SpecialVariables.PreferenceVariableTypes).ToArray());
        internal static readonly Dictionary<string, int> DottedLocalsNameIndexMap =
            new Dictionary<string, int>(SpecialVariables.AutomaticVariableTypes.Length, StringComparer.OrdinalIgnoreCase);
        internal static readonly Dictionary<string, int> DottedScriptCmdletLocalsNameIndexMap =
            new Dictionary<string, int>(SpecialVariables.AutomaticVariableTypes.Length + SpecialVariables.PreferenceVariableTypes.Length,
                                        StringComparer.OrdinalIgnoreCase);

        static Compiler()
        {
            _functionContext = Expression.Parameter(typeof(FunctionContext), "funcContext");
            _executionContextParameter = Expression.Variable(typeof(ExecutionContext), "context");

            s_setDollarQuestionToTrue = Expression.Assign(
                Expression.Property(_executionContextParameter, CachedReflectionInfo.ExecutionContext_QuestionMarkVariableValue),
                ExpressionCache.TrueConstant);

            s_callCheckForInterrupts = Expression.Call(CachedReflectionInfo.PipelineOps_CheckForInterrupts,
                                                      _executionContextParameter);

            s_getCurrentPipe = Expression.Field(_functionContext, CachedReflectionInfo.FunctionContext__outputPipe);
            _returnPipe = Expression.Variable(s_getCurrentPipe.Type, "returnPipe");

            var exception = Expression.Variable(typeof(Exception), "exception");

            s_catchFlowControl = Expression.Catch(typeof(FlowControlException), Expression.Rethrow());
            var catchAll = Expression.Catch(
                exception,
                Expression.Block(
                    Expression.Call(
                        CachedReflectionInfo.ExceptionHandlingOps_CheckActionPreference,
                            Compiler._functionContext, exception)));
            _stmtCatchHandlers = new CatchBlock[] { s_catchFlowControl, catchAll };

            s_currentExceptionBeingHandled = Expression.Property(
                _executionContextParameter, CachedReflectionInfo.ExecutionContext_CurrentExceptionBeingHandled);

            int i;
            for (i = 0; i < SpecialVariables.AutomaticVariables.Length; ++i)
            {
                DottedLocalsNameIndexMap.Add(SpecialVariables.AutomaticVariables[i], i);
                DottedScriptCmdletLocalsNameIndexMap.Add(SpecialVariables.AutomaticVariables[i], i);
            }
            for (i = 0; i < SpecialVariables.PreferenceVariables.Length; ++i)
            {
                DottedScriptCmdletLocalsNameIndexMap.Add(SpecialVariables.PreferenceVariables[i],
                                                         i + (int)AutomaticVariable.NumberOfAutomaticVariables);
            }

            s_builtinAttributeGenerator.Add(typeof(CmdletBindingAttribute), NewCmdletBindingAttribute);
            s_builtinAttributeGenerator.Add(typeof(ParameterAttribute), NewParameterAttribute);
            s_builtinAttributeGenerator.Add(typeof(OutputTypeAttribute), NewOutputTypeAttribute);
            s_builtinAttributeGenerator.Add(typeof(AliasAttribute), NewAliasAttribute);
            s_builtinAttributeGenerator.Add(typeof(ValidateSetAttribute), NewValidateSetAttribute);
            s_builtinAttributeGenerator.Add(typeof(DebuggerHiddenAttribute), NewDebuggerHiddenAttribute);
            s_builtinAttributeGenerator.Add(typeof(ValidateNotNullAttribute), NewValidateNotNullAttribute);
            s_builtinAttributeGenerator.Add(typeof(ValidateNotNullOrEmptyAttribute), NewValidateNotNullOrEmptyAttribute);
        }

        private Compiler(List<IScriptExtent> sequencePoints)
        {
            _sequencePoints = sequencePoints;
        }

        internal Compiler()
        {
            _sequencePoints = new List<IScriptExtent>();
        }

        internal bool CompilingConstantExpression { get; set; }
        internal bool Optimize { get; private set; }
        internal Type LocalVariablesTupleType { get; private set; }
        internal ParameterExpression LocalVariablesParameter { get; private set; }
        private SymbolDocumentInfo _debugSymbolDocument;
        internal TypeDefinitionAst _memberFunctionType;
        private bool _compilingTrap;
        private bool _compilingSingleExpression;
        private bool _compilingScriptCmdlet;
        private string _currentFunctionName;
        private int _switchTupleIndex = VariableAnalysis.Unanalyzed;
        private int _foreachTupleIndex = VariableAnalysis.Unanalyzed;
        private readonly List<IScriptExtent> _sequencePoints;
        private int _stmtCount;

        internal bool CompilingMemberFunction { get; set; }
        internal SpecialMemberFunctionType SpecialMemberFunctionType { get; set; }
        internal Type MemberFunctionReturnType
        {
            get
            {
                Diagnostics.Assert(CompilingMemberFunction, "Return not only set for member functions");
                return _memberFunctionReturnType;
            }
            set { _memberFunctionReturnType = value; }
        }

        private Type _memberFunctionReturnType;

        #region Helpers for AST Compile methods

        internal Expression Compile(Ast ast)
        {
            return (Expression)ast.Accept(this);
        }

        internal Expression CompileExpressionOperand(ExpressionAst exprAst)
        {
            var result = Compile(exprAst);
            if (result.Type == typeof(void))
            {
                result = Expression.Block(result, ExpressionCache.NullConstant);
            }
            return result;
        }

        private IEnumerable<Expression> CompileInvocationArguments(IEnumerable<ExpressionAst> arguments)
        {
            return arguments == null ? Utils.EmptyArray<Expression>() : arguments.Select(CompileExpressionOperand);
        }

        internal Expression ReduceAssignment(ISupportsAssignment left, TokenKind tokenKind, Expression right)
        {
            IAssignableValue av = left.GetAssignableValue();
            ExpressionType et = ExpressionType.Extension;
            switch (tokenKind)
            {
                case TokenKind.Equals: return av.SetValue(this, right);
                case TokenKind.PlusEquals: et = ExpressionType.Add; break;
                case TokenKind.MinusEquals: et = ExpressionType.Subtract; break;
                case TokenKind.MultiplyEquals: et = ExpressionType.Multiply; break;
                case TokenKind.DivideEquals: et = ExpressionType.Divide; break;
                case TokenKind.RemainderEquals: et = ExpressionType.Modulo; break;
            }

            var exprs = new List<Expression>();
            var temps = new List<ParameterExpression>();
            var getExpr = av.GetValue(this, exprs, temps);
            exprs.Add(av.SetValue(this, DynamicExpression.Dynamic(PSBinaryOperationBinder.Get(et), typeof(object), getExpr, right)));
            return Expression.Block(temps, exprs);
        }

        internal Expression GetLocal(int tupleIndex)
        {
            Expression result = LocalVariablesParameter;
            foreach (var property in MutableTuple.GetAccessPath(LocalVariablesTupleType, tupleIndex))
            {
                result = Expression.Property(result, property);
            }
            return result;
        }

        internal static Expression CallGetVariable(Expression variablePath, VariableExpressionAst varAst)
        {
            return Expression.Call(CachedReflectionInfo.VariableOps_GetVariableValue, variablePath,
                                   _executionContextParameter,
                                   Expression.Constant(varAst).Cast(typeof(VariableExpressionAst)));
        }

        internal static Expression CallSetVariable(Expression variablePath, Expression rhs, Expression attributes = null)
        {
            return Expression.Call(CachedReflectionInfo.VariableOps_SetVariableValue,
                                   variablePath, rhs.Cast(typeof(object)), _executionContextParameter,
                                   attributes ?? ExpressionCache.NullConstant.Cast(typeof(AttributeAst[])));
        }

        internal Expression GetAutomaticVariable(VariableExpressionAst varAst)
        {
            // Generate, in psuedo code:
            //
            //     return (localsTuple.IsValueSet(tupleIndex)
            //          ? localsTuple.ItemXXX
            //          : VariableOps.GetAutomaticVariable(tupleIndex, executionContextParameter);
            //
            // We could be smarter about the generated code.  For example:
            //
            //     * $PSCmdlet - always set if the script uses cmdletbinding.
            //     * $_ - always set in process and end block, otherwise need dynamic checks.
            //     * $this - can never know if it's set, always need above psuedo code.
            //     * $input - also can never know - it's always set from a command process, but not necessarily set from ScriptBlock.Invoke.
            //
            // These optimizations are not yet performed.

            int tupleIndex = varAst.TupleIndex;
            var expr = GetLocal(tupleIndex);
            var callGetAutomaticVariable = Expression.Call(CachedReflectionInfo.VariableOps_GetAutomaticVariableValue,
                                                           ExpressionCache.Constant(tupleIndex),
                                                           _executionContextParameter,
                                                           Expression.Constant(varAst)).Convert(expr.Type);
            if (!Optimize)
                return callGetAutomaticVariable;

            return Expression.Condition(
                Expression.Call(LocalVariablesParameter,
                                CachedReflectionInfo.MutableTuple_IsValueSet,
                                ExpressionCache.Constant(tupleIndex)),
                expr,
                callGetAutomaticVariable);
        }

        internal static Expression CallStringEquals(Expression left, Expression right, bool ignoreCase)
        {
            return Expression.Call(CachedReflectionInfo.StringOps_Equals, left, right, ExpressionCache.InvariantCulture,
                                   ignoreCase
                                       ? ExpressionCache.CompareOptionsIgnoreCase
                                       : ExpressionCache.CompareOptionsNone);
        }

        internal static Expression IsStrictMode(int version, Expression executionContext = null)
        {
            if (executionContext == null)
            {
                executionContext = ExpressionCache.NullExecutionContext;
            }

            return Expression.Call(CachedReflectionInfo.ExecutionContext_IsStrictVersion,
                                   executionContext,
                                   ExpressionCache.Constant(version));
        }

        internal Expression UpdatePosition(Ast ast)
        {
            _sequencePoints.Add(ast.Extent);

            // If we just added the first sequence point, then we don't want to check for breakpoints - we'll do that
            // in EnterScriptFunction.
            // Except for while/do loops, in this case we want to check breakpoints on the first sequence point since it
            // will be executed multiple times.
            return ((_sequencePoints.Count == 1) && !_generatingWhileOrDoLoop)
                       ? ExpressionCache.Empty
                       : new UpdatePositionExpr(ast.Extent, _sequencePoints.Count - 1, _debugSymbolDocument, !_compilingSingleExpression);
        }

        private int _tempCounter;
        internal ParameterExpression NewTemp(Type type, string name)
        {
            return Expression.Variable(type, string.Format(CultureInfo.InvariantCulture, "{0}{1}", name, _tempCounter++));
        }

        internal static Type GetTypeConstraintForMethodResolution(ExpressionAst expr)
        {
            while (expr is ParenExpressionAst)
            {
                expr = ((ParenExpressionAst)expr).Pipeline.GetPureExpression();
            }

            ConvertExpressionAst firstConvert = null;
            while (expr is AttributedExpressionAst)
            {
                if (expr is ConvertExpressionAst && !((ConvertExpressionAst)expr).IsRef())
                {
                    firstConvert = (ConvertExpressionAst)expr;
                    break;
                }
                expr = ((AttributedExpressionAst)expr).Child;
            }

            return firstConvert == null ? null : firstConvert.Type.TypeName.GetReflectionType();
        }

        internal static PSMethodInvocationConstraints CombineTypeConstraintForMethodResolution(Type targetType, Type argType)
        {
            if (targetType == null && argType == null)
                return null;

            return new PSMethodInvocationConstraints(targetType, new[] { argType });
        }

        internal static PSMethodInvocationConstraints CombineTypeConstraintForMethodResolution(Type targetType, Type[] argTypes)
        {
            if (targetType == null && (argTypes == null || argTypes.Length == 0))
                return null;

            return new PSMethodInvocationConstraints(targetType, argTypes);
        }

        internal static Expression ConvertValue(TypeConstraintAst typeConstraint, Expression expr)
        {
            var typeName = typeConstraint.TypeName;
            var toType = typeName.GetReflectionType();
            if (toType != null)
            {
                if (toType == typeof(void))
                {
                    return Expression.Block(typeof(void), expr);
                }

                return expr.Convert(toType);
            }

            // typeName can't be resolved at compile time, so defer resolution until runtime.
            return DynamicExpression.Dynamic(PSDynamicConvertBinder.Get(), typeof(object),
                                             Expression.Call(CachedReflectionInfo.TypeOps_ResolveTypeName,
                                                             Expression.Constant(typeName),
                                                             Expression.Constant(typeName.Extent)),
                                             expr);
        }

        internal static Expression ConvertValue(Expression expr, List<AttributeBaseAst> conversions)
        {
            for (int index = 0; index < conversions.Count; index++)
            {
                var typeConstraint = conversions[index] as TypeConstraintAst;
                if (typeConstraint != null)
                {
                    expr = ConvertValue(typeConstraint, expr);
                }
            }

            return expr;
        }

        #endregion Helpers for AST Compile methods

        #region Parameter Metadata

        internal static RuntimeDefinedParameterDictionary GetParameterMetaData(ReadOnlyCollection<ParameterAst> parameters, bool automaticPositions, ref bool usesCmdletBinding)
        {
            var md = new RuntimeDefinedParameterDictionary();
            var listMd = new List<RuntimeDefinedParameter>();
            var customParameterSet = false;
            for (int index = 0; index < parameters.Count; index++)
            {
                var param = parameters[index];
                var rdp = GetRuntimeDefinedParameter(param, ref customParameterSet, ref usesCmdletBinding);

                listMd.Add(rdp);
                md.Add(param.Name.VariablePath.UserPath, rdp);
            }

            int pos = 0;
            if (automaticPositions && !customParameterSet)
            {
                for (int index = 0; index < listMd.Count; index++)
                {
                    var rdp = listMd[index];
                    var paramAttribute = (ParameterAttribute)rdp.Attributes.First(attr => attr is ParameterAttribute);
                    if (!(rdp.ParameterType == typeof(SwitchParameter)))
                    {
                        paramAttribute.Position = pos++;
                    }
                }
            }

            md.Data = listMd.ToArray();
            return md;
        }

        private static readonly Dictionary<CallInfo, Delegate> s_attributeGeneratorCache = new Dictionary<CallInfo, Delegate>();
        private static readonly Dictionary<Type, Func<AttributeAst, Attribute>> s_builtinAttributeGenerator = new Dictionary<Type, Func<AttributeAst, Attribute>>(10);

        private static Delegate GetAttributeGenerator(CallInfo callInfo)
        {
            Delegate result;
            lock (s_attributeGeneratorCache)
            {
                if (!s_attributeGeneratorCache.TryGetValue(callInfo, out result))
                {
                    var binder = PSAttributeGenerator.Get(callInfo);

                    var parameters = new ParameterExpression[callInfo.ArgumentCount + 1];
                    for (int i = 0; i < parameters.Length; ++i)
                    {
                        parameters[i] = Expression.Variable(typeof(object));
                    }

                    result = Expression.Lambda(DynamicExpression.Dynamic(binder, typeof(object), parameters), parameters).Compile();
                    s_attributeGeneratorCache.Add(callInfo, result);
                }
            }
            return result;
        }

        private static readonly CallSite<Func<CallSite, object, int>> s_attrArgToIntConverter =
            CallSite<Func<CallSite, object, int>>.Create(PSConvertBinder.Get(typeof(int)));
        internal static readonly CallSite<Func<CallSite, object, string>> _attrArgToStringConverter =
            CallSite<Func<CallSite, object, string>>.Create(PSConvertBinder.Get(typeof(string)));
        private static readonly CallSite<Func<CallSite, object, string[]>> s_attrArgToStringArrayConverter =
            CallSite<Func<CallSite, object, string[]>>.Create(PSConvertBinder.Get(typeof(string[])));
        private static readonly CallSite<Func<CallSite, object, bool>> s_attrArgToBoolConverter =
            CallSite<Func<CallSite, object, bool>>.Create(PSConvertBinder.Get(typeof(bool)));
        private static readonly CallSite<Func<CallSite, object, ConfirmImpact>> s_attrArgToConfirmImpactConverter =
            CallSite<Func<CallSite, object, ConfirmImpact>>.Create(PSConvertBinder.Get(typeof(ConfirmImpact)));
        private static readonly CallSite<Func<CallSite, object, RemotingCapability>> s_attrArgToRemotingCapabilityConverter =
            CallSite<Func<CallSite, object, RemotingCapability>>.Create(PSConvertBinder.Get(typeof(RemotingCapability)));

        private static void CheckNoPositionalArgs(AttributeAst ast)
        {
            var positionalArgCount = ast.PositionalArguments.Count;
            if (positionalArgCount > 0)
            {
                throw InterpreterError.NewInterpreterException(null, typeof(MethodException), ast.Extent,
                    "MethodCountCouldNotFindBest", ExtendedTypeSystem.MethodArgumentCountException, ".ctor",
                    positionalArgCount);
            }
        }

        private static void CheckNoNamedArgs(AttributeAst ast)
        {
            if (ast.NamedArguments.Count > 0)
            {
                var namedArg = ast.NamedArguments[0];
                var argumentName = namedArg.ArgumentName;
                throw InterpreterError.NewInterpreterException(namedArg, typeof(RuntimeException), namedArg.Extent,
                    "PropertyNotFoundForType", ParserStrings.PropertyNotFoundForType, argumentName,
                    typeof(CmdletBindingAttribute));
            }
        }

        private static Attribute NewCmdletBindingAttribute(AttributeAst ast)
        {
            CheckNoPositionalArgs(ast);

            var cvv = new ConstantValueVisitor { AttributeArgument = true };

            var result = new CmdletBindingAttribute();

            foreach (var namedArg in ast.NamedArguments)
            {
                var argValue = namedArg.Argument.Accept(cvv);
                var argumentName = namedArg.ArgumentName;
                if (argumentName.Equals("DefaultParameterSetName", StringComparison.OrdinalIgnoreCase))
                {
                    result.DefaultParameterSetName = _attrArgToStringConverter.Target(_attrArgToStringConverter, argValue);
                }
                else if (argumentName.Equals("HelpUri", StringComparison.OrdinalIgnoreCase))
                {
                    result.HelpUri = _attrArgToStringConverter.Target(_attrArgToStringConverter, argValue);
                }
                else if (argumentName.Equals("SupportsShouldProcess", StringComparison.OrdinalIgnoreCase))
                {
                    result.SupportsShouldProcess = s_attrArgToBoolConverter.Target(s_attrArgToBoolConverter, argValue);
                }
                else if (argumentName.Equals("PositionalBinding", StringComparison.OrdinalIgnoreCase))
                {
                    result.PositionalBinding = s_attrArgToBoolConverter.Target(s_attrArgToBoolConverter, argValue);
                }
                else if (argumentName.Equals("ConfirmImpact", StringComparison.OrdinalIgnoreCase))
                {
                    result.ConfirmImpact = s_attrArgToConfirmImpactConverter.Target(s_attrArgToConfirmImpactConverter, argValue);
                }
                else if (argumentName.Equals("SupportsTransactions", StringComparison.OrdinalIgnoreCase))
                {
                    result.SupportsTransactions = s_attrArgToBoolConverter.Target(s_attrArgToBoolConverter, argValue);
                }
                else if (argumentName.Equals("SupportsPaging", StringComparison.OrdinalIgnoreCase))
                {
                    result.SupportsPaging = s_attrArgToBoolConverter.Target(s_attrArgToBoolConverter, argValue);
                }
                else if (argumentName.Equals("RemotingCapability", StringComparison.OrdinalIgnoreCase))
                {
                    result.RemotingCapability = s_attrArgToRemotingCapabilityConverter.Target(s_attrArgToRemotingCapabilityConverter, argValue);
                }
                else
                {
                    throw InterpreterError.NewInterpreterException(namedArg, typeof(RuntimeException), namedArg.Extent,
                        "PropertyNotFoundForType", ParserStrings.PropertyNotFoundForType, argumentName,
                        typeof(CmdletBindingAttribute));
                }
            }

            return result;
        }

        private static Attribute NewParameterAttribute(AttributeAst ast)
        {
            CheckNoPositionalArgs(ast);

            var cvv = new ConstantValueVisitor { AttributeArgument = true };

            var result = new ParameterAttribute();

            foreach (var namedArg in ast.NamedArguments)
            {
                var argValue = namedArg.Argument.Accept(cvv);
                var argumentName = namedArg.ArgumentName;

                if (argumentName.Equals("Position", StringComparison.OrdinalIgnoreCase))
                {
                    result.Position = s_attrArgToIntConverter.Target(s_attrArgToIntConverter, argValue);
                }
                else if (argumentName.Equals("ParameterSetName", StringComparison.OrdinalIgnoreCase))
                {
                    result.ParameterSetName = _attrArgToStringConverter.Target(_attrArgToStringConverter, argValue);
                }
                else if (argumentName.Equals("Mandatory", StringComparison.OrdinalIgnoreCase))
                {
                    result.Mandatory = s_attrArgToBoolConverter.Target(s_attrArgToBoolConverter, argValue);
                }
                else if (argumentName.Equals("ValueFromPipeline", StringComparison.OrdinalIgnoreCase))
                {
                    result.ValueFromPipeline = s_attrArgToBoolConverter.Target(s_attrArgToBoolConverter, argValue);
                }
                else if (argumentName.Equals("ValueFromPipelineByPropertyName", StringComparison.OrdinalIgnoreCase))
                {
                    result.ValueFromPipelineByPropertyName = s_attrArgToBoolConverter.Target(s_attrArgToBoolConverter, argValue);
                }
                else if (argumentName.Equals("ValueFromRemainingArguments", StringComparison.OrdinalIgnoreCase))
                {
                    result.ValueFromRemainingArguments = s_attrArgToBoolConverter.Target(s_attrArgToBoolConverter, argValue);
                }
                else if (argumentName.Equals("HelpMessage", StringComparison.OrdinalIgnoreCase))
                {
                    result.HelpMessage = _attrArgToStringConverter.Target(_attrArgToStringConverter, argValue);
                }
                else if (argumentName.Equals("HelpMessageBaseName", StringComparison.OrdinalIgnoreCase))
                {
                    result.HelpMessageBaseName = _attrArgToStringConverter.Target(_attrArgToStringConverter, argValue);
                }
                else if (argumentName.Equals("HelpMessageResourceId", StringComparison.OrdinalIgnoreCase))
                {
                    result.HelpMessageResourceId = _attrArgToStringConverter.Target(_attrArgToStringConverter, argValue);
                }
                else if (argumentName.Equals("DontShow", StringComparison.OrdinalIgnoreCase))
                {
                    result.DontShow = s_attrArgToBoolConverter.Target(s_attrArgToBoolConverter, argValue);
                }
                else
                {
                    throw InterpreterError.NewInterpreterException(namedArg, typeof(RuntimeException), namedArg.Extent,
                        "PropertyNotFoundForType", ParserStrings.PropertyNotFoundForType, argumentName,
                        typeof(CmdletBindingAttribute));
                }
            }

            return result;
        }

        private static Attribute NewOutputTypeAttribute(AttributeAst ast)
        {
            var cvv = new ConstantValueVisitor { AttributeArgument = true };

            OutputTypeAttribute result;
            if (ast.PositionalArguments.Count == 0)
            {
                result = new OutputTypeAttribute(Utils.EmptyArray<string>());
            }
            else if (ast.PositionalArguments.Count == 1)
            {
                var typeArg = ast.PositionalArguments[0] as TypeExpressionAst;
                if (typeArg != null)
                {
                    var type = TypeOps.ResolveTypeName(typeArg.TypeName, typeArg.Extent);
                    result = new OutputTypeAttribute(type);
                }
                else
                {
                    var argValue = ast.PositionalArguments[0].Accept(cvv);
                    result = new OutputTypeAttribute(_attrArgToStringConverter.Target(_attrArgToStringConverter, argValue));
                }
            }
            else
            {
                var args = new object[ast.PositionalArguments.Count];
                for (int i = 0; i < ast.PositionalArguments.Count; i++)
                {
                    var positionalArgument = ast.PositionalArguments[i];
                    var typeArg = positionalArgument as TypeExpressionAst;
                    args[i] = typeArg != null
                        ? TypeOps.ResolveTypeName(typeArg.TypeName, typeArg.Extent)
                        : positionalArgument.Accept(cvv);
                }

                if (args[0] is Type)
                {
                    result = new OutputTypeAttribute(LanguagePrimitives.ConvertTo<Type[]>(args));
                }
                else
                {
                    result = new OutputTypeAttribute(LanguagePrimitives.ConvertTo<string[]>(args));
                }
            }

            foreach (var namedArg in ast.NamedArguments)
            {
                var argValue = namedArg.Argument.Accept(cvv);
                var argumentName = namedArg.ArgumentName;

                if (argumentName.Equals("ParameterSetName", StringComparison.OrdinalIgnoreCase))
                {
                    result.ParameterSetName = s_attrArgToStringArrayConverter.Target(s_attrArgToStringArrayConverter, argValue);
                }
                else if (argumentName.Equals("ProviderCmdlet", StringComparison.OrdinalIgnoreCase))
                {
                    result.ProviderCmdlet = _attrArgToStringConverter.Target(_attrArgToStringConverter, argValue);
                }
                else
                {
                    throw InterpreterError.NewInterpreterException(namedArg, typeof(RuntimeException), namedArg.Extent,
                        "PropertyNotFoundForType", ParserStrings.PropertyNotFoundForType, argumentName,
                        typeof(CmdletBindingAttribute));
                }
            }

            return result;
        }

        private static Attribute NewAliasAttribute(AttributeAst ast)
        {
            CheckNoNamedArgs(ast);

            var cvv = new ConstantValueVisitor { AttributeArgument = true };
            var args = new string[ast.PositionalArguments.Count];
            for (int i = 0; i < ast.PositionalArguments.Count; i++)
            {
                args[i] = _attrArgToStringConverter.Target(_attrArgToStringConverter,
                    ast.PositionalArguments[i].Accept(cvv));
            }
            return new AliasAttribute(args);
        }

        private static Attribute NewValidateSetAttribute(AttributeAst ast)
        {
            var cvv = new ConstantValueVisitor { AttributeArgument = true };
            var args = new string[ast.PositionalArguments.Count];
            for (int i = 0; i < ast.PositionalArguments.Count; i++)
            {
                args[i] = _attrArgToStringConverter.Target(_attrArgToStringConverter,
                    ast.PositionalArguments[i].Accept(cvv));
            }

            var result = new ValidateSetAttribute(args);
            foreach (var namedArg in ast.NamedArguments)
            {
                var argValue = namedArg.Argument.Accept(cvv);
                var argumentName = namedArg.ArgumentName;
                if (argumentName.Equals("IgnoreCase", StringComparison.OrdinalIgnoreCase))
                {
                    result.IgnoreCase = s_attrArgToBoolConverter.Target(s_attrArgToBoolConverter, argValue);
                }
                else
                {
                    throw InterpreterError.NewInterpreterException(namedArg, typeof(RuntimeException), namedArg.Extent,
                        "PropertyNotFoundForType", ParserStrings.PropertyNotFoundForType, argumentName,
                        typeof(CmdletBindingAttribute));
                }
            }

            return result;
        }

        private static Attribute NewDebuggerHiddenAttribute(AttributeAst ast)
        {
            CheckNoPositionalArgs(ast);
            CheckNoNamedArgs(ast);
            return new DebuggerHiddenAttribute();
        }

        private static Attribute NewValidateNotNullOrEmptyAttribute(AttributeAst ast)
        {
            CheckNoPositionalArgs(ast);
            CheckNoNamedArgs(ast);
            return new ValidateNotNullOrEmptyAttribute();
        }

        private static Attribute NewValidateNotNullAttribute(AttributeAst ast)
        {
            CheckNoPositionalArgs(ast);
            CheckNoNamedArgs(ast);
            return new ValidateNotNullAttribute();
        }

        internal static Attribute GetAttribute(AttributeAst attributeAst)
        {
            var attributeType = attributeAst.TypeName.GetReflectionAttributeType();
            if (attributeType == null)
            {
                throw InterpreterError.NewInterpreterException(attributeAst, typeof(RuntimeException), attributeAst.Extent,
                    "CustomAttributeTypeNotFound", ParserStrings.CustomAttributeTypeNotFound, attributeAst.TypeName.FullName);
            }

            Func<AttributeAst, Attribute> generator;
            if (s_builtinAttributeGenerator.TryGetValue(attributeType, out generator))
            {
                return generator(attributeAst);
            }

            var positionalArgCount = attributeAst.PositionalArguments.Count;
            var argumentNames = attributeAst.NamedArguments.Select(name => name.ArgumentName).ToArray();
            var totalArgCount = positionalArgCount + argumentNames.Length;
            var callInfo = new CallInfo(totalArgCount, argumentNames);

            var delegateArgs = new object[totalArgCount + 1];
            delegateArgs[0] = attributeType;

            var cvv = new ConstantValueVisitor { AttributeArgument = true };
            int i = 1;
            for (int index = 0; index < attributeAst.PositionalArguments.Count; index++)
            {
                var posArg = attributeAst.PositionalArguments[index];
                delegateArgs[i++] = posArg.Accept(cvv);
            }
            for (int index = 0; index < attributeAst.NamedArguments.Count; index++)
            {
                var namedArg = attributeAst.NamedArguments[index];
                delegateArgs[i++] = namedArg.Argument.Accept(cvv);
            }

            try
            {
                return (Attribute)GetAttributeGenerator(callInfo).DynamicInvoke(delegateArgs);
            }
            catch (TargetInvocationException tie)
            {
                // Unwrap the wrapped exception
                var innerException = tie.InnerException;
                var rte = innerException as RuntimeException;
                if (rte == null)
                {
                    rte = InterpreterError.NewInterpreterExceptionWithInnerException(null, typeof(RuntimeException), attributeAst.Extent,
                        "ExceptionConstructingAttribute", ExtendedTypeSystem.ExceptionConstructingAttribute, innerException,
                        innerException.Message, attributeAst.TypeName.FullName);
                }
                InterpreterError.UpdateExceptionErrorRecordPosition(rte, attributeAst.Extent);
                throw rte;
            }
        }

        internal static Attribute GetAttribute(TypeConstraintAst typeConstraintAst)
        {
            Type type = null;
            var ihct = typeConstraintAst.TypeName as ISupportsTypeCaching;
            if (ihct != null)
            {
                type = ihct.CachedType;
            }
            if (type == null)
            {
                type = TypeOps.ResolveTypeName(typeConstraintAst.TypeName, typeConstraintAst.Extent);
                if (ihct != null)
                {
                    ihct.CachedType = type;
                }
            }
            return new ArgumentTypeConverterAttribute(type);
        }

        private static RuntimeDefinedParameter GetRuntimeDefinedParameter(ParameterAst parameterAst, ref bool customParameterSet, ref bool usesCmdletBinding)
        {
            List<Attribute> attributes = new List<Attribute>();
            bool hasParameterAttribute = false;
            for (int index = 0; index < parameterAst.Attributes.Count; index++)
            {
                var attributeAst = parameterAst.Attributes[index];
                var attribute = attributeAst.GetAttribute();
                attributes.Add(attribute);

                var parameterAttribute = attribute as ParameterAttribute;
                if (parameterAttribute != null)
                {
                    hasParameterAttribute = true;
                    usesCmdletBinding = true;
                    if (parameterAttribute.Position != int.MinValue ||
                        !parameterAttribute.ParameterSetName.Equals(ParameterAttribute.AllParameterSets,
                                                                    StringComparison.OrdinalIgnoreCase))
                    {
                        customParameterSet = true;
                    }
                }
            }

            attributes.Reverse();
            if (!hasParameterAttribute)
            {
                attributes.Insert(0, new ParameterAttribute());
            }

            var result = new RuntimeDefinedParameter(parameterAst.Name.VariablePath.UserPath, parameterAst.StaticType,
                                                     new Collection<Attribute>(attributes.ToArray()));

            if (parameterAst.DefaultValue != null)
            {
                object constantValue;
                if (IsConstantValueVisitor.IsConstant(parameterAst.DefaultValue, out constantValue))
                {
                    result.Value = constantValue;
                }
                else
                {
                    // Expression isn't constant, create a wrapper that holds the ast, and if necessary,
                    // will cache a delegate to evaluate the default value.
                    result.Value = new DefaultValueExpressionWrapper { Expression = parameterAst.DefaultValue };
                }
            }
            else
            {
                object defaultValue;
                if (TryGetDefaultParameterValue(parameterAst.StaticType, out defaultValue) && defaultValue != null)
                {
                    // Skip setting the value when defaultValue is null because if we do call the setter,
                    // we'll try converting null to the parameter, which we might not want, e.g. if the parameter is [ref].
                    result.Value = defaultValue;
                }
            }
            return result;
        }

        internal static bool TryGetDefaultParameterValue(Type type, out object value)
        {
            if (type == typeof(string))
            {
                value = string.Empty;
                return true;
            }

            if (type.GetTypeInfo().IsClass)
            {
                value = null;
                return true;
            }

            if (type == typeof(bool))
            {
                value = Boxed.False;
                return true;
            }

            if (type == typeof(SwitchParameter))
            {
                value = new SwitchParameter(false);
                return true;
            }

            if (LanguagePrimitives.IsNumeric(LanguagePrimitives.GetTypeCode(type)) && !type.GetTypeInfo().IsEnum)
            {
                value = 0;
                return true;
            }

            value = null;
            return false;
        }

        internal class DefaultValueExpressionWrapper
        {
            internal ExpressionAst Expression { get; set; }
            private Func<FunctionContext, object> _delegate;
            private IScriptExtent[] _sequencePoints;
            private Type _localsTupleType;

            internal object GetValue(ExecutionContext context, SessionStateInternal sessionStateInternal, IDictionary usingValues = null)
            {
                lock (this)
                {
                    // Code written as part of a default value in a parameter is considered trusted
                    return Compiler.GetExpressionValue(this.Expression, true, context, sessionStateInternal, usingValues, ref _delegate,
                                                       ref _sequencePoints, ref _localsTupleType);
                }
            }
        }

        #endregion Parameter Metadata

        // This is the main entry point for turning an AST into compiled code.
        internal void Compile(CompiledScriptBlockData scriptBlock, bool optimize)
        {
            var body = scriptBlock.Ast;
            Diagnostics.Assert(body is ScriptBlockAst || body is FunctionDefinitionAst || body is FunctionMemberAst || body is CompilerGeneratedMemberFunctionAst,
                               "Caller to verify ast is correct type.");
            var ast = (Ast)body;
            Optimize = optimize;
            _compilingScriptCmdlet = scriptBlock.UsesCmdletBinding;

            var fileName = ast.Extent.File;
            if (fileName != null)
            {
                _debugSymbolDocument = Expression.SymbolDocument(fileName);
            }

            var details = VariableAnalysis.Analyze(body, !optimize, _compilingScriptCmdlet);
            LocalVariablesTupleType = details.Item1;
            var nameToIndexMap = details.Item2;

            if (!nameToIndexMap.TryGetValue(SpecialVariables.@switch, out _switchTupleIndex))
            {
                _switchTupleIndex = VariableAnalysis.ForceDynamic;
            }
            if (!nameToIndexMap.TryGetValue(SpecialVariables.@foreach, out _foreachTupleIndex))
            {
                _foreachTupleIndex = VariableAnalysis.ForceDynamic;
            }

            LocalVariablesParameter = Expression.Variable(LocalVariablesTupleType, "locals");

            var functionMemberAst = ast as FunctionMemberAst;
            if (functionMemberAst != null)
            {
                CompilingMemberFunction = true;
                MemberFunctionReturnType = functionMemberAst.GetReturnType();
                _memberFunctionType = (TypeDefinitionAst)functionMemberAst.Parent;
                SpecialMemberFunctionType = SpecialMemberFunctionType.None;
                if (functionMemberAst.Name.Equals(_memberFunctionType.Name, StringComparison.OrdinalIgnoreCase))
                {
                    // TODO: default argument support
                    var parameters = ((IParameterMetadataProvider)functionMemberAst.Body).Parameters;
                    if (parameters == null || parameters.Count == 0)
                    {
                        SpecialMemberFunctionType = functionMemberAst.IsStatic
                            ? SpecialMemberFunctionType.StaticConstructor
                            : SpecialMemberFunctionType.DefaultConstructor;
                    }
                }
            }
            else
            {
                var generatedMemberFunctionAst = ast as CompilerGeneratedMemberFunctionAst;
                if (generatedMemberFunctionAst != null)
                {
                    CompilingMemberFunction = true;
                    SpecialMemberFunctionType = generatedMemberFunctionAst.Type;
                    MemberFunctionReturnType = typeof(void);
                    _memberFunctionType = generatedMemberFunctionAst.DefiningType;
                }
            }

            body.Body.Accept(this);

            if (_sequencePoints.Count == 0)
            {
                // Uncommon, but possible if a script is empty, or if it only defines functions.
                // In this case, add the entire body as a sequence point.  Debugging won't stop
                // on this sequence point, but it makes it safe to access the CurrentPosition
                // property in FunctionContext (which can happen if there are exceptions
                // defining the functions.)
                _sequencePoints.Add(ast.Extent);
            }

            var compileInterpretChoice = (_stmtCount > 300) ? CompileInterpretChoice.NeverCompile : CompileInterpretChoice.CompileOnDemand;

            if (optimize)
            {
                scriptBlock.DynamicParamBlock = CompileTree(_dynamicParamBlockLambda, compileInterpretChoice);
                scriptBlock.BeginBlock = CompileTree(_beginBlockLambda, compileInterpretChoice);
                scriptBlock.ProcessBlock = CompileTree(_processBlockLambda, compileInterpretChoice);
                scriptBlock.EndBlock = CompileTree(_endBlockLambda, compileInterpretChoice);
                scriptBlock.LocalsMutableTupleType = LocalVariablesTupleType;
                scriptBlock.LocalsMutableTupleCreator = MutableTuple.TupleCreator(LocalVariablesTupleType);
                scriptBlock.NameToIndexMap = nameToIndexMap;
            }
            else
            {
                scriptBlock.UnoptimizedDynamicParamBlock = CompileTree(_dynamicParamBlockLambda, compileInterpretChoice);
                scriptBlock.UnoptimizedBeginBlock = CompileTree(_beginBlockLambda, compileInterpretChoice);
                scriptBlock.UnoptimizedProcessBlock = CompileTree(_processBlockLambda, compileInterpretChoice);
                scriptBlock.UnoptimizedEndBlock = CompileTree(_endBlockLambda, compileInterpretChoice);
                scriptBlock.UnoptimizedLocalsMutableTupleType = LocalVariablesTupleType;
                scriptBlock.UnoptimizedLocalsMutableTupleCreator = MutableTuple.TupleCreator(LocalVariablesTupleType);
            }

            // The sequence points are identical optimized or not.  Regardless, we want to ensure
            // that the list is unique no matter when the property is accessed, so make sure it is set just once.
            if (scriptBlock.SequencePoints == null)
            {
                scriptBlock.SequencePoints = _sequencePoints.ToArray();
            }
        }

        private Action<FunctionContext> CompileTree(Expression<Action<FunctionContext>> lambda, CompileInterpretChoice compileInterpretChoice)
        {
            if (lambda == null)
                return null;

            if (compileInterpretChoice == CompileInterpretChoice.AlwaysCompile)
            {
                return lambda.Compile();
            }

            // threshold is # of times the script must run before we decide to compile
            // NeverCompile sets the threshold to int.MaxValue, so theoretically we might compile
            // at some point, but it's very unlikely.
            int threshold = (compileInterpretChoice == CompileInterpretChoice.NeverCompile) ? int.MaxValue : -1;
            var deleg = new LightCompiler(threshold).CompileTop(lambda).CreateDelegate();
            return (Action<FunctionContext>)deleg;
        }

        internal static object GetExpressionValue(ExpressionAst expressionAst, bool isTrustedInput, ExecutionContext context, IDictionary usingValues = null)
        {
            return GetExpressionValue(expressionAst, isTrustedInput, context, null, usingValues);
        }

        internal static object GetExpressionValue(ExpressionAst expressionAst, bool isTrustedInput, ExecutionContext context, SessionStateInternal sessionStateInternal, IDictionary usingValues = null)
        {
            Func<FunctionContext, object> lambda = null;
            IScriptExtent[] sequencePoints = null;
            Type localsTupleType = null;
            return GetExpressionValue(expressionAst, isTrustedInput, context, sessionStateInternal, usingValues, ref lambda, ref sequencePoints, ref localsTupleType);
        }

        private static object GetExpressionValue(ExpressionAst expressionAst,
                                                 bool isTrustedInput,
                                                 ExecutionContext context,
                                                 SessionStateInternal sessionStateInternal,
                                                 IDictionary usingValues,
                                                 ref Func<FunctionContext, object> lambda,
                                                 ref IScriptExtent[] sequencePoints,
                                                 ref Type localsTupleType)
        {
            object constantValue;
            if (IsConstantValueVisitor.IsConstant(expressionAst, out constantValue))
            {
                return constantValue;
            }

            // If this isn't trusted input, then just return.
            if (!isTrustedInput)
            {
                return null;
            }

            // Can't be exposed to untrusted input - exposing private variable names / etc. could be
            // information disclosure.
            var variableAst = expressionAst as VariableExpressionAst;
            if (variableAst != null)
            {
                // We can avoid creating a lambda for the common case of a simple variable expression.
                return VariableOps.GetVariableValue(variableAst.VariablePath, context, variableAst);
            }

            // Can't be exposed to untrusted input - invoking arbitrary code could result in remote code
            // execution.
            if (lambda == null)
            {
                lambda = (new Compiler()).CompileSingleExpression(expressionAst, out sequencePoints, out localsTupleType);
            }

            SessionStateInternal oldSessionState = context.EngineSessionState;
            try
            {
                if (sessionStateInternal != null && context.EngineSessionState != sessionStateInternal)
                {
                    // If we're running a function from a module, we need to evaluate the initializers in the
                    // module context, not the callers context...
                    context.EngineSessionState = sessionStateInternal;
                }

                var resultList = new List<object>();
                var pipe = new Pipe(resultList);
                try
                {
                    var functionContext = new FunctionContext
                    {
                        _sequencePoints = sequencePoints,
                        _executionContext = context,
                        _file = expressionAst.Extent.File,
                        _outputPipe = pipe,
                        _localsTuple = MutableTuple.MakeTuple(localsTupleType, DottedLocalsNameIndexMap)
                    };
                    if (usingValues != null)
                    {
                        var boundParameters = new PSBoundParametersDictionary { ImplicitUsingParameters = usingValues };
                        functionContext._localsTuple.SetAutomaticVariable(AutomaticVariable.PSBoundParameters, boundParameters, context);
                    }
                    var result = lambda(functionContext);
                    if (result == AutomationNull.Value)
                    {
                        return resultList.Count == 0 ? null : PipelineOps.PipelineResult(resultList);
                    }
                    return result;
                }
                catch (TargetInvocationException tie)
                {
                    throw tie.InnerException;
                }
            }
            catch (TerminateException)
            {
                // the debugger is terminating the execution; bubble up the exception
                throw;
            }
            catch (FlowControlException)
            {
                // ignore break, continue and return exceptions
                return null;
            }
            finally
            {
                context.EngineSessionState = oldSessionState;
            }
        }

        private Func<FunctionContext, object> CompileSingleExpression(ExpressionAst expressionAst, out IScriptExtent[] sequencePoints, out Type localsTupleType)
        {
            Optimize = false;
            _compilingSingleExpression = true;

            var details = VariableAnalysis.AnalyzeExpression(expressionAst);
            LocalVariablesTupleType = localsTupleType = details.Item1;
            LocalVariablesParameter = Expression.Variable(LocalVariablesTupleType, "locals");
            _returnTarget = Expression.Label(typeof(object), "returnTarget");
            _loopTargets.Clear();

            var exprs = new List<Expression>();
            var temps = new List<ParameterExpression> { _executionContextParameter, LocalVariablesParameter };
            GenerateFunctionProlog(exprs, temps, null);
            _sequencePoints.Add(expressionAst.Extent);
            exprs.Add(new UpdatePositionExpr(expressionAst.Extent, _sequencePoints.Count - 1, _debugSymbolDocument, checkBreakpoints: true));
            var result = Compile(expressionAst).Cast(typeof(object));
            exprs.Add(Expression.Label(_returnTarget, result));
            var body = Expression.Block(new[] { _executionContextParameter, LocalVariablesParameter }, exprs);
            var parameters = new[] { _functionContext };
            sequencePoints = _sequencePoints.ToArray();
            return Expression.Lambda<Func<FunctionContext, object>>(body, parameters).Compile();
        }

        private class LoopGotoTargets
        {
            internal LoopGotoTargets(string label, LabelTarget breakLabel, LabelTarget continueLabel)
            {
                this.Label = label;
                this.BreakLabel = breakLabel;
                this.ContinueLabel = continueLabel;
            }
            internal string Label { get; private set; }
            internal LabelTarget ContinueLabel { get; private set; }
            internal LabelTarget BreakLabel { get; private set; }
        }

        private LabelTarget _returnTarget;
        private Expression<Action<FunctionContext>> _dynamicParamBlockLambda;
        private Expression<Action<FunctionContext>> _beginBlockLambda;
        private Expression<Action<FunctionContext>> _processBlockLambda;
        private Expression<Action<FunctionContext>> _endBlockLambda;

        private readonly List<LoopGotoTargets> _loopTargets = new List<LoopGotoTargets>();
        private bool _generatingWhileOrDoLoop;

        private enum CaptureAstContext
        {
            Condition,
            Enumerable,
            AssignmentWithResultPreservation,
            AssignmentWithoutResultPreservation
        }

        private delegate void MergeRedirectExprs(List<Expression> exprs, List<Expression> finallyExprs);

        private Expression CaptureAstResults(
            Ast ast,
            CaptureAstContext context,
            MergeRedirectExprs generateRedirectExprs = null)
        {
            Expression result;

            // We'll generate code like:
            //  try {
            //    oldPipe = funcContext.OutputPipe;
            //    resultList = new List<object>();
            //    resultListPipe = new Pipe(resultList);
            //    funcContext.OutputPipe = resultListPipe;
            //    <optionally add merge redirection expressions>
            //    expression...
            //  } finally {
            //    FlushPipe(oldPipe, resultList);
            //    funcContext.OutputPipe = oldPipe;
            //  }

            var temps = new List<ParameterExpression>();
            var exprs = new List<Expression>();
            var catches = new List<CatchBlock>();
            var finallyExprs = new List<Expression>();

            var oldPipe = NewTemp(typeof(Pipe), "oldPipe");
            var resultList = NewTemp(typeof(List<object>), "resultList");
            temps.Add(resultList);
            temps.Add(oldPipe);
            exprs.Add(Expression.Assign(oldPipe, s_getCurrentPipe));
            exprs.Add(Expression.Assign(resultList, Expression.New(CachedReflectionInfo.ObjectList_ctor)));
            exprs.Add(Expression.Assign(s_getCurrentPipe, Expression.New(CachedReflectionInfo.Pipe_ctor, resultList)));
            exprs.Add(Expression.Call(oldPipe, CachedReflectionInfo.Pipe_SetVariableListForTemporaryPipe, s_getCurrentPipe));
            if (generateRedirectExprs != null)
            {
                // Add merge redirection expressions if delegate is provided.
                generateRedirectExprs(exprs, finallyExprs);
            }
            exprs.Add(Compile(ast));

            switch (context)
            {
                case CaptureAstContext.AssignmentWithResultPreservation:
                case CaptureAstContext.AssignmentWithoutResultPreservation:
                    result = Expression.Call(CachedReflectionInfo.PipelineOps_PipelineResult, resultList);

                    // Clear the temporary pipe in case of exception, if we are not required to preserve the results
                    if (context == CaptureAstContext.AssignmentWithoutResultPreservation)
                    {
                        var catchExprs = new List<Expression>
                                         {
                                             Expression.Call(CachedReflectionInfo.PipelineOps_ClearPipe, resultList),
                                             Expression.Rethrow(),
                                             Expression.Constant(null, typeof (object))
                                         };

                        catches.Add(Expression.Catch(typeof(RuntimeException), Expression.Block(typeof(object), catchExprs)));
                    }

                    // PipelineResult might get skipped in some circumstances due to a FlowControlException thrown out, in which case
                    // we write to the oldPipe. This can happen in cases like:
                    //     $(1;2;return 3)
                    finallyExprs.Add(Expression.Call(CachedReflectionInfo.PipelineOps_FlushPipe, oldPipe, resultList));
                    break;
                case CaptureAstContext.Condition:
                    result = DynamicExpression.Dynamic(PSPipelineResultToBoolBinder.Get(), typeof(bool), resultList);
                    break;
                case CaptureAstContext.Enumerable:
                    result = resultList;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("context");
            }

            finallyExprs.Add(Expression.Assign(s_getCurrentPipe, oldPipe));
            exprs.Add(result);

            if (catches.Count > 0)
            {
                return Expression.Block(
                    temps.ToArray(),
                    Expression.TryCatchFinally(Expression.Block(exprs),
                                               Expression.Block(finallyExprs),
                                               catches.ToArray()));
            }

            return Expression.Block(
                    temps.ToArray(),
                    Expression.TryFinally(Expression.Block(exprs),
                                          Expression.Block(finallyExprs)));
        }

        private Expression CaptureStatementResultsHelper(
            StatementAst stmt,
            CaptureAstContext context,
            MergeRedirectExprs generateRedirectExprs)
        {
            var commandExpressionAst = stmt as CommandExpressionAst;
            if (commandExpressionAst != null)
            {
                if (commandExpressionAst.Redirections.Count > 0)
                {
                    return GetRedirectedExpression(commandExpressionAst, captureForInput: true);
                }
                return Compile(commandExpressionAst.Expression);
            }

            var assignmentStatementAst = stmt as AssignmentStatementAst;
            if (assignmentStatementAst != null)
            {
                var expr = Compile(assignmentStatementAst);
                if (stmt.Parent is StatementBlockAst)
                {
                    expr = Expression.Block(expr, ExpressionCache.Empty);
                }
                return expr;
            }

            var pipelineAst = stmt as PipelineAst;
            if (pipelineAst != null)
            {
                var expr = pipelineAst.GetPureExpression();
                if (expr != null) { return Compile(expr); }
            }

            return CaptureAstResults(stmt, context, generateRedirectExprs);
        }

        private Expression CaptureStatementResults(
            StatementAst stmt,
            CaptureAstContext context,
            MergeRedirectExprs generateRedirectExprs = null)
        {
            var result = CaptureStatementResultsHelper(stmt, context, generateRedirectExprs);

            // If we're generating code for a condition and the condition contains some command invocation,
            // we want to be sure that $? is set to true even if the condition fails, e.g.:
            //     if (get-command foo -ea SilentlyContinue) { foo }
            //     $?  # never $false here
            // Many conditions don't invoke commands though, and in trivial empty loops, setting $? = $true
            // does have a measurable impact, so only set $? = $true if the condition might change $? to $false.
            // We do this after evaluating the condition so that you could do something like:
            //    if ((dir file1,file2 -ea SilentlyContinue) -and $?) { <# files both exist, otherwise $? would be $false if 0 or 1 files existed #> }
            if (context == CaptureAstContext.Condition && AstSearcher.FindFirst(stmt, ast => ast is CommandAst, searchNestedScriptBlocks: false) != null)
            {
                var tmp = NewTemp(result.Type, "condTmp");
                result = Expression.Block(new[] { tmp },
                    Expression.Assign(tmp, result),
                    s_setDollarQuestionToTrue,
                    tmp);
            }
            return result;
        }

        internal Expression CallAddPipe(Expression expr, Expression pipe)
        {
            if (!PSEnumerableBinder.IsStaticTypePossiblyEnumerable(expr.Type))
            {
                return Expression.Call(pipe, CachedReflectionInfo.Pipe_Add, expr.Cast(typeof(object)));
            }

            return DynamicExpression.Dynamic(PSPipeWriterBinder.Get(), typeof(void), expr, pipe, _executionContextParameter);
        }

        public object VisitErrorStatement(ErrorStatementAst errorStatementAst)
        {
            return null;
        }

        public object VisitErrorExpression(ErrorExpressionAst errorExpressionAst)
        {
            return ExpressionCache.Constant(1);
        }

        #region Script Blocks

        public object VisitScriptBlock(ScriptBlockAst scriptBlockAst)
        {
            var funcDefn = scriptBlockAst.Parent as FunctionDefinitionAst;
            var funcName = (funcDefn != null) ? funcDefn.Name : "<ScriptBlock>";

            var rootForDefiningTypesAndUsings = scriptBlockAst.Find(ast => ast is TypeDefinitionAst || ast is UsingStatementAst, true) != null
                ? scriptBlockAst
                : null;

            if (scriptBlockAst.DynamicParamBlock != null)
            {
                _dynamicParamBlockLambda = CompileNamedBlock(scriptBlockAst.DynamicParamBlock, funcName + "<DynamicParam>", rootForDefiningTypesAndUsings);
                rootForDefiningTypesAndUsings = null;
            }

            // Skip param block - nothing to generate, defaults get generated when generating parameter metadata.

            if (scriptBlockAst.BeginBlock != null)
            {
                _beginBlockLambda = CompileNamedBlock(scriptBlockAst.BeginBlock, funcName + "<Begin>", rootForDefiningTypesAndUsings);
                rootForDefiningTypesAndUsings = null;
            }
            if (scriptBlockAst.ProcessBlock != null)
            {
                var processFuncName = funcName;
                if (!scriptBlockAst.ProcessBlock.Unnamed)
                {
                    processFuncName = funcName + "<Process>";
                }
                _processBlockLambda = CompileNamedBlock(scriptBlockAst.ProcessBlock, processFuncName, rootForDefiningTypesAndUsings);
                rootForDefiningTypesAndUsings = null;
            }
            if (scriptBlockAst.EndBlock != null)
            {
                if (!scriptBlockAst.EndBlock.Unnamed)
                {
                    funcName = funcName + "<End>";
                }
                _endBlockLambda = CompileNamedBlock(scriptBlockAst.EndBlock, funcName, rootForDefiningTypesAndUsings);
            }

            return null;
        }

        private Expression<Action<FunctionContext>> CompileNamedBlock(NamedBlockAst namedBlockAst, string funcName, ScriptBlockAst rootForDefiningTypes)
        {
            IScriptExtent entryExtent = null;
            IScriptExtent exitExtent = null;
            if (namedBlockAst.Unnamed)
            {
                // Get extent from the function or scriptblock expression parent, if any.
                var scriptBlock = (ScriptBlockAst)namedBlockAst.Parent;
                if (scriptBlock.Parent != null && scriptBlock.Extent is InternalScriptExtent)
                {
                    // We must have curlies at the start/end.
                    var scriptExtent = (InternalScriptExtent)scriptBlock.Extent;
                    entryExtent = new InternalScriptExtent(scriptExtent.PositionHelper, scriptExtent.StartOffset, scriptExtent.StartOffset + 1);
                    exitExtent = new InternalScriptExtent(scriptExtent.PositionHelper, scriptExtent.EndOffset - 1, scriptExtent.EndOffset);
                }
            }
            else
            {
                entryExtent = namedBlockAst.OpenCurlyExtent;
                exitExtent = namedBlockAst.CloseCurlyExtent;
            }

            return CompileSingleLambda(namedBlockAst.Statements, namedBlockAst.Traps, funcName, entryExtent, exitExtent, rootForDefiningTypes);
        }

        private Tuple<Action<FunctionContext>, Type> CompileTrap(TrapStatementAst trap)
        {
            var compiler = new Compiler(_sequencePoints) { _compilingTrap = true };
            string funcName = _currentFunctionName + "<trap>";
            if (trap.TrapType != null)
            {
                funcName += "<" + trap.TrapType.TypeName.Name + ">";
            }

            // We generate code as though we're dotting the trap, but in reality we don't dot it,
            // a new scope is always created.  The code gen for dotting means we can avoid passing
            // around the array of local variable names.  We assume traps don't need to perform well,
            // and that they rarely if ever actually create any local variables.  We still do the
            // variable analysis though because we must mark automatic variables like $_.
            var analysis = VariableAnalysis.AnalyzeTrap(trap);

            compiler.LocalVariablesTupleType = analysis.Item1;
            compiler.LocalVariablesParameter = Expression.Variable(compiler.LocalVariablesTupleType, "locals");

            var lambda = compiler.CompileSingleLambda(trap.Body.Statements, trap.Body.Traps, funcName, null, null, null);
            return Tuple.Create(lambda.Compile(), compiler.LocalVariablesTupleType);
        }

        private Expression<Action<FunctionContext>> CompileSingleLambda(ReadOnlyCollection<StatementAst> statements,
                                             ReadOnlyCollection<TrapStatementAst> traps,
                                             string funcName,
                                             IScriptExtent entryExtent,
                                             IScriptExtent exitExtent,
                                             ScriptBlockAst rootForDefiningTypesAndUsings)
        {
            _currentFunctionName = funcName;

            _loopTargets.Clear();

            _returnTarget = Expression.Label("returnTarget");
            var exprs = new List<Expression>();
            var temps = new List<ParameterExpression>();

            GenerateFunctionProlog(exprs, temps, entryExtent);

            if (rootForDefiningTypesAndUsings != null)
            {
                GenerateTypesAndUsings(rootForDefiningTypesAndUsings, exprs);
            }

            var actualBodyExprs = new List<Expression>();

            if (CompilingMemberFunction)
            {
                temps.Add(_returnPipe);
            }

            CompileStatementListWithTraps(statements, traps, actualBodyExprs, temps);

            exprs.AddRange(actualBodyExprs);

            // We always add the return label even if it's unused - that way it doesn't matter what the last
            // expression is in the body - the full body will always have void type.
            exprs.Add(Expression.Label(_returnTarget));

            GenerateFunctionEpilog(exprs, exitExtent);

            temps.Add(LocalVariablesParameter);
            Expression body = Expression.Block(temps, exprs);

            // A return from a normal block is just that - a simple return (no exception).
            // A return from a trap turns into an exception because the trap is compiled into a different lambda, yet
            // the return from the trap must return from the function containing the trap.  So we wrap the full
            // body of regular begin/process/end blocks with a try/catch so a return from the trap returns
            // to the right place.  We can avoid also avoid generating the catch if we know there aren't any traps.
            if (!_compilingTrap &&
                ((traps != null && traps.Count > 0)
                || statements.Any(stmt => AstSearcher.Contains(stmt, ast => ast is TrapStatementAst, searchNestedScriptBlocks: false))))
            {
                body = Expression.Block(
                    new[] { _executionContextParameter },
                    Expression.TryCatchFinally(
                        body,
                        Expression.Call(
                            Expression.Field(_executionContextParameter, CachedReflectionInfo.ExecutionContext_Debugger),
                            CachedReflectionInfo.Debugger_ExitScriptFunction),
                        Expression.Catch(typeof(ReturnException), ExpressionCache.Empty)));
            }
            else
            {
                // Either no traps, or we're compiling a trap - either way don't catch the ReturnException.
                body = Expression.Block(
                    new[] { _executionContextParameter },
                    Expression.TryFinally(
                        body,
                        Expression.Call(
                            Expression.Field(_executionContextParameter, CachedReflectionInfo.ExecutionContext_Debugger),
                            CachedReflectionInfo.Debugger_ExitScriptFunction)));
            }

            return Expression.Lambda<Action<FunctionContext>>(body, funcName, new[] { _functionContext });
        }

        private void GenerateTypesAndUsings(ScriptBlockAst rootForDefiningTypesAndUsings, List<Expression> exprs)
        {
            // We don't postpone load assemblies, import modules from 'using' to the moment, when enclosed scriptblock is executed.
            // We do loading, when root of the script is compiled.
            // This allow us to avoid creating 10 different classes in this situation:
            // 1..10 | % { class C {} }
            // But it's possible that we are loading something from the codepaths that we never execute.

            // If Parent of rootForDefiningTypesAndUsings is not null, then we already defined all types, when Visit a parent ScriptBlock
            if (rootForDefiningTypesAndUsings.Parent == null)
            {
                if (rootForDefiningTypesAndUsings.UsingStatements.Any())
                {
                    bool allUsingsAreNamespaces = rootForDefiningTypesAndUsings.UsingStatements.All(us => us.UsingStatementKind == UsingStatementKind.Namespace);
                    GenerateLoadUsings(rootForDefiningTypesAndUsings.UsingStatements, allUsingsAreNamespaces, exprs);
                }

                TypeDefinitionAst[] typeAsts =
                    rootForDefiningTypesAndUsings.FindAll(ast => ast is TypeDefinitionAst, true)
                        .Cast<TypeDefinitionAst>()
                        .ToArray();

                if (typeAsts.Length > 0)
                {
                    var assembly = DefinePowerShellTypes(rootForDefiningTypesAndUsings, typeAsts);
                    exprs.Add(Expression.Call(CachedReflectionInfo.TypeOps_SetAssemblyDefiningPSTypes,
                        _functionContext, Expression.Constant(assembly)));

                    exprs.Add(Expression.Call(CachedReflectionInfo.TypeOps_InitPowerShellTypesAtRuntime,
                        Expression.Constant(typeAsts)));
                }
            }

            Dictionary<string, TypeDefinitionAst> typesToAddToScope =
                rootForDefiningTypesAndUsings.FindAll(ast => ast is TypeDefinitionAst, false)
                    .Cast<TypeDefinitionAst>()
                    .ToDictionary(type => type.Name);
            if (typesToAddToScope.Count > 0)
            {
                exprs.Add(Expression.Call(CachedReflectionInfo.TypeOps_AddPowerShellTypesToTheScope,
                    Expression.Constant(typesToAddToScope), _executionContextParameter));
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="usingStatements"></param>
        /// <param name="allUsingsAreNamespaces">This flag allow us some optimizations, if usings don't have assemblies and modules.</param>
        /// <param name="exprs"></param>
        internal static void GenerateLoadUsings(IEnumerable<UsingStatementAst> usingStatements, bool allUsingsAreNamespaces, List<Expression> exprs)
        {
            TypeResolutionState trs;
            Dictionary<string, TypeDefinitionAst> typesToAdd = null;
            if (allUsingsAreNamespaces)
            {
                trs = new TypeResolutionState(TypeOps.GetNamespacesForTypeResolutionState(usingStatements), null);
            }
            else
            {
                Assembly[] assemblies;
                typesToAdd = LoadUsingsImpl(usingStatements, out assemblies);
                trs = new TypeResolutionState(
                    TypeOps.GetNamespacesForTypeResolutionState(usingStatements),
                    assemblies);
            }
            exprs.Add(Expression.Call(CachedReflectionInfo.TypeOps_SetCurrentTypeResolutionState,
                    Expression.Constant(trs), _executionContextParameter));
            if (typesToAdd != null && typesToAdd.Count > 0)
            {
                exprs.Add(Expression.Call(CachedReflectionInfo.TypeOps_AddPowerShellTypesToTheScope,
                    Expression.Constant(typesToAdd), _executionContextParameter));
            }
        }

        /// <summary>
        /// Bake types and creates a dynamic assembly. 
        /// This method should be called only for rootAsts (i.e. script file root ScriptBlockAst).
        /// </summary>
        /// <param name="rootForDefiningTypes"></param>
        /// <param name="typeAsts">non-empty array of TypeDefinitionAst</param>
        /// <returns>Assembly with defined types</returns>
        internal static Assembly DefinePowerShellTypes(Ast rootForDefiningTypes, TypeDefinitionAst[] typeAsts)
        {
            // TODO(sevoroby): this Diagnostic is conceptually right. 
            // BUT It triggers, when we define type in an InitialSessionState and use it later in two different PowerShell instances.
            // Diagnostics.Assert(typeAsts[0].Type == null, "We must not call DefinePowerShellTypes twice for the same TypeDefinitionAsts");

            if (typeAsts[0].Type != null)
            {
                // We treat Type as a mutable buffer field and wipe it here to start definitions from scratch.

                // I didn't find any real scenario when it can cause problems, except multi-threaded environment, which is rear and out-of-scope for now.
                // Potentially, we can fix it with Ast.Copy() and rewiring ITypeName references for the whole tree.
                foreach (var typeDefinitionAst in typeAsts)
                {
                    typeDefinitionAst.Type = null;
                }
            }

            // This is a short term solution - all error messages produced by creating the types should happen
            // at parse time, not runtime.
            var parser = new Parser();
            var assembly = TypeDefiner.DefineTypes(parser, rootForDefiningTypes, typeAsts);
            if (parser.ErrorList.Count > 0)
            {
                // wipe types, if there are any errors.
                foreach (var typeDefinitionAst in typeAsts)
                {
                    typeDefinitionAst.Type = null;
                }

                throw new ParseException(parser.ErrorList.ToArray());
            }

            return assembly;
        }

        private static Dictionary<string, TypeDefinitionAst> LoadUsingsImpl(IEnumerable<UsingStatementAst> usingAsts, out Assembly[] assemblies)
        {
            var asms = new List<Assembly>();
            var types = new Dictionary<string, TypeDefinitionAst>(StringComparer.OrdinalIgnoreCase);

            foreach (var usingStmt in usingAsts)
            {
                switch (usingStmt.UsingStatementKind)
                {
                    case UsingStatementKind.Assembly:
                        asms.Add(LoadAssembly(usingStmt.Name.Value, usingStmt.Extent.File));
                        break;
                    case UsingStatementKind.Command:
                        break;
                    case UsingStatementKind.Module:
                        var module = LoadModule(usingStmt.ModuleInfo);
                        if (module != null)
                        {
                            if (module.ImplementingAssembly != null)
                            {
                                asms.Add(module.ImplementingAssembly);
                            }

                            var exportedTypes = module.GetExportedTypeDefinitions();
                            PopulateRuntimeTypes(usingStmt.ModuleInfo.GetExportedTypeDefinitions(), exportedTypes);
                            foreach (var nameTypePair in exportedTypes)
                            {
                                types[SymbolResolver.GetModuleQualifiedName(module.Name, nameTypePair.Key)] = nameTypePair.Value;
                            }
                        }
                        break;
                    case UsingStatementKind.Namespace:
                        break;
                    case UsingStatementKind.Type:
                        break;
                    default:
                        Diagnostics.Assert(false, "Unknown enum value " + usingStmt.UsingStatementKind + " for UsingStatementKind");
                        break;
                }
            }

            assemblies = asms.ToArray();
            return types;
        }

        private static void PopulateRuntimeTypes(ReadOnlyDictionary<string, TypeDefinitionAst> parseTimeTypes, ReadOnlyDictionary<string, TypeDefinitionAst> runtimeTypes)
        {
            foreach (KeyValuePair<string, TypeDefinitionAst> parseTypePair in parseTimeTypes)
            {
                // We only need to populate types, if ASTs are not reused. Otherwise it's already populated.
                if (parseTypePair.Value.Type == null)
                {
                    TypeDefinitionAst typeDefinitionAst;
                    if (!runtimeTypes.TryGetValue(parseTypePair.Key, out typeDefinitionAst))
                    {
                        throw InterpreterError.NewInterpreterException(parseTypePair.Value, typeof(RuntimeException),
                            parseTypePair.Value.Extent,
                            "TypeNotFound", ParserStrings.TypeNotFound, parseTypePair.Value.Name);
                    }

                    parseTypePair.Value.Type = typeDefinitionAst.Type;
                }
            }
        }

        private static Assembly LoadAssembly(string assemblyName, string scriptFileName)
        {
            var assemblyFileName = assemblyName;
            Assembly assembly = null;
            try
            {
                if (!string.IsNullOrEmpty(scriptFileName) && !Path.IsPathRooted(assemblyFileName))
                {
                    assemblyFileName = Path.GetDirectoryName(scriptFileName) + "\\" + assemblyFileName;
                }

#if !CORECLR
                if (!File.Exists(assemblyFileName))
                {
                    Microsoft.CodeAnalysis.GlobalAssemblyCache.ResolvePartialName(assemblyName, out assemblyFileName);
                }
#endif
                if (File.Exists(assemblyFileName))
                {
                    assembly = ClrFacade.LoadFrom(assemblyFileName);
                }
            }
            catch
            {
            }

            if (assembly == null)
            {
                throw InterpreterError.NewInterpreterException(assemblyName, typeof(RuntimeException), null,
                                                               "ErrorLoadingAssembly",
                                                               ParserStrings.ErrorLoadingAssembly,
                                                               assemblyName);
            }
            return assembly;
        }

        /// <summary>
        /// Take module info of module that can be already loaded or not and loads it.
        /// </summary>
        /// <param name="originalModuleInfo"></param>
        /// <returns>module info of the same module, but loaded</returns>
        private static PSModuleInfo LoadModule(PSModuleInfo originalModuleInfo)
        {
            // originalModuleInfo is created during parse time and may not contain [System.Type] types exported from the module.
            // At this moment, we need to actually load the module and create types.
            // We want to load **exactly the same module** as we used at parse time.
            // And avoid the module resolution logic that can lead to another module (if something changed on the file system).
            // So, we load the module by the file path, not by module specification (ModuleName, RequiredVersion).
            var modulePath = originalModuleInfo.Path;
            var commandInfo = new CmdletInfo("Import-Module", typeof(ImportModuleCommand));
            var ps = PowerShell.Create(RunspaceMode.CurrentRunspace)
                .AddCommand(commandInfo)
                .AddParameter("Name", modulePath)
                .AddParameter("PassThru");
            var moduleInfo = ps.Invoke<PSModuleInfo>();
            if (ps.HadErrors)
            {
                var errorRecord = ps.Streams.Error[0];
                throw InterpreterError.NewInterpreterException(modulePath, typeof(RuntimeException), null,
                    errorRecord.FullyQualifiedErrorId, errorRecord.ToString());
            }

            if (moduleInfo.Count == 1)
            {
                return moduleInfo[0];
            }
            else
            {
                Diagnostics.Assert(false, "We should load exactly one module from the provided original module");
                return null;
            }
        }

        private void GenerateFunctionProlog(List<Expression> exprs, List<ParameterExpression> temps, IScriptExtent entryExtent)
        {
            exprs.Add(Expression.Assign(_executionContextParameter,
                                        Expression.Field(_functionContext, CachedReflectionInfo.FunctionContext__executionContext)));
            exprs.Add(Expression.Assign(LocalVariablesParameter,
                                        Expression.Field(_functionContext, CachedReflectionInfo.FunctionContext__localsTuple).Cast(this.LocalVariablesTupleType)));

            // Compiling a single expression (a default argument, or an locally evaluated argument in a ScriptBlock=>PowerShell conversion)
            // does not support debugging, so skip calling EnterScriptFunction.
            if (!_compilingSingleExpression)
            {
                exprs.Add(Expression.Assign(
                    Expression.Field(_functionContext, CachedReflectionInfo.FunctionContext__functionName),
                    Expression.Constant(_currentFunctionName)));

                if (entryExtent != null)
                {
                    _sequencePoints.Add(entryExtent);
                    exprs.Add(new UpdatePositionExpr(entryExtent, _sequencePoints.Count - 1, _debugSymbolDocument, checkBreakpoints: false));
                }

                exprs.Add(
                    Expression.Call(
                        Expression.Field(_executionContextParameter, CachedReflectionInfo.ExecutionContext_Debugger),
                        CachedReflectionInfo.Debugger_EnterScriptFunction,
                        _functionContext));
            }

            if (CompilingMemberFunction)
            {
                // Member functions don't write to the pipeline, they return values.
                // Set the default pipe to the null pipe, but remember the pipe parameter
                // so when we do compile the return statement, we can write to it's pipe.
                exprs.Add(Expression.Assign(_returnPipe, s_getCurrentPipe));
                exprs.Add(Expression.Assign(s_getCurrentPipe, ExpressionCache.NullPipe));

                Diagnostics.Assert(_memberFunctionType.Type != null, "Member function type should not be null");
                var ourThis = NewTemp(_memberFunctionType.Type, "this");
                temps.Add(ourThis);
                exprs.Add(
                    Expression.Assign(ourThis,
                        GetLocal(Array.IndexOf(SpecialVariables.AutomaticVariables, SpecialVariables.This)).Cast(_memberFunctionType.Type)));

                switch (SpecialMemberFunctionType)
                {
                    case SpecialMemberFunctionType.DefaultConstructor:
                        exprs.Add(InitializeMemberProperties(ourThis));
                        break;
                    case SpecialMemberFunctionType.StaticConstructor:
                        exprs.Add(InitializeMemberProperties(ourThis: null));
                        break;
                }
            }
        }

        private Expression InitializeMemberProperties(Expression ourThis)
        {
            var exprs = new List<Expression>();
            bool wantStatic = ourThis == null;
            var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | (wantStatic ? BindingFlags.Static : BindingFlags.Instance);
            foreach (var propertyMember in _memberFunctionType.Members.OfType<PropertyMemberAst>())
            {
                if (propertyMember.InitialValue == null || propertyMember.IsStatic != wantStatic)
                {
                    continue;
                }

                var extent = propertyMember.InitialValue.Extent;
                _sequencePoints.Add(extent);
                exprs.Add(new UpdatePositionExpr(extent, _sequencePoints.Count - 1, _debugSymbolDocument, checkBreakpoints: false));
                var property = _memberFunctionType.Type.GetProperty(propertyMember.Name, bindingFlags);
                exprs.Add(
                    Expression.Assign(
                        Expression.Property(ourThis, property),
                        Compile(propertyMember.InitialValue).Convert(property.PropertyType)));
            }

            exprs.Add(ExpressionCache.Empty);
            return Expression.TryCatch(Expression.Block(exprs), _stmtCatchHandlers);
        }

        private void GenerateFunctionEpilog(List<Expression> exprs, IScriptExtent exitExtent)
        {
            if (exitExtent != null)
            {
                exprs.Add(UpdatePosition(new SequencePointAst(exitExtent)));
            }
        }

        public object VisitTypeConstraint(TypeConstraintAst typeConstraintAst)
        {
            Diagnostics.Assert(false, "Nothing to generate for a type constraint");
            return null;
        }

        public object VisitAttribute(AttributeAst attributeAst)
        {
            Diagnostics.Assert(false, "Nothing to generate for an attribute");
            return null;
        }

        public object VisitNamedAttributeArgument(NamedAttributeArgumentAst namedAttributeArgumentAst)
        {
            Diagnostics.Assert(false, "Nothing to generate for a named attribute argument");
            return null;
        }

        public object VisitParameter(ParameterAst parameterAst)
        {
            Diagnostics.Assert(false, "Nothing to generate for a parameter");
            return null;
        }

        public object VisitParamBlock(ParamBlockAst paramBlockAst)
        {
            Diagnostics.Assert(false, "Nothing to generate for a parameter block");
            return null;
        }

        public object VisitNamedBlock(NamedBlockAst namedBlockAst)
        {
            Diagnostics.Assert(false, "NamedBlockAst is handled specially, not via the visitor.");
            return null;
        }

        public object VisitStatementBlock(StatementBlockAst statementBlockAst)
        {
            var exprs = new List<Expression>();
            var temps = new List<ParameterExpression>();
            CompileStatementListWithTraps(statementBlockAst.Statements, statementBlockAst.Traps, exprs, temps);
            if (exprs.Count == 0)
            {
                exprs.Add(ExpressionCache.Empty);
            }
            return Expression.Block(typeof(void), temps, exprs);
        }

        private int _trapNestingCount;
        private void CompileStatementListWithTraps(ReadOnlyCollection<StatementAst> statements,
                                                   ReadOnlyCollection<TrapStatementAst> traps,
                                                   List<Expression> exprs,
                                                   List<ParameterExpression> temps)
        {
            if (statements.Count == 0)
            {
                exprs.Add(ExpressionCache.Empty);
                return;
            }

            var originalExprs = exprs;

            Expression handlerInScope;
            ParameterExpression oldActiveHandler;
            ParameterExpression trapHandlersPushed;
            if (traps != null)
            {
                // If the statement block has any traps, we'll generate code like:
                //     try {
                //         oldActiveHandler = executionContext.PropagateExceptionsToEnclosingStatementBlock;
                //         executionContext.PropagateExceptionsToEnclosingStatementBlock = true;
                //         functionContext.PushTrapHandlers(
                //             new Type[] { trapTypes },
                //             new Action<FunctionContext>[] { trapDelegates },
                //             new Type[] { trapLocalTupleTypes });
                //         trapHandlersPushed = true
                //
                //         statements
                //     } finally {
                //         executionContext.PropagateExceptionsToEnclosingStatementBlock = oldActiveHandler;
                //         if (trapHandlersPushed) {
                //             functionContext.PopTrapHandlers()
                //         }
                //     }
                //
                // We use a runtime check on popping because in some rare cases, PushTrapHandlers might not
                // get called (e.g. if a trap handler specifies a type that doesn't exist, like trap [baddtype]{}).
                // We don't want to pop if we didn't push.

                exprs = new List<Expression>();

                handlerInScope = Expression.Property(_executionContextParameter,
                                                     CachedReflectionInfo.ExecutionContext_ExceptionHandlerInEnclosingStatementBlock);
                oldActiveHandler = NewTemp(typeof(bool), "oldActiveHandler");

                exprs.Add(Expression.Assign(oldActiveHandler, handlerInScope));
                exprs.Add(Expression.Assign(handlerInScope, ExpressionCache.Constant(true)));

                var trapTypes = new List<Expression>();
                var trapDelegates = new List<Action<FunctionContext>>();
                var trapTupleType = new List<Type>();
                for (int index = 0; index < traps.Count; index++)
                {
                    var trap = traps[index];
                    trapTypes.Add(trap.TrapType != null
                                      ? CompileTypeName(trap.TrapType.TypeName, trap.TrapType.Extent)
                                      : ExpressionCache.CatchAllType);
                    var tuple = CompileTrap(trap);
                    trapDelegates.Add(tuple.Item1);
                    trapTupleType.Add(tuple.Item2);
                }

                exprs.Add(Expression.Call(_functionContext, CachedReflectionInfo.FunctionContext_PushTrapHandlers,
                                          Expression.NewArrayInit(typeof(Type), trapTypes),
                                          Expression.Constant(trapDelegates.ToArray()),
                                          Expression.Constant(trapTupleType.ToArray())));
                trapHandlersPushed = NewTemp(typeof(bool), "trapHandlersPushed");
                exprs.Add(Expression.Assign(trapHandlersPushed, ExpressionCache.Constant(true)));
                _trapNestingCount += 1;
            }
            else
            {
                oldActiveHandler = null;
                handlerInScope = null;
                trapHandlersPushed = null;

                if (_trapNestingCount > 0)
                {
                    // If this statement block has no traps, but a parent block does, we need to make sure that process the
                    // trap at the correct level in case we continue.  For example:
                    //     trap { continue }
                    //     if (1) {
                    //         throw 1
                    //         "Shouldn't continue here"
                    //     }
                    //     "Should continue here"
                    // In this example, the trap just continues, but we want to continue after the 'if' statement, not after
                    // the 'throw' statement.
                    // We push null onto the active trap handlers to let ExceptionHandlingOps.CheckActionPreference know it
                    // shouldn't process traps (but should still query the user if appropriate), and just rethrow so we can
                    // unwind to the block with the trap.

                    exprs = new List<Expression>();

                    exprs.Add(Expression.Call(_functionContext, CachedReflectionInfo.FunctionContext_PushTrapHandlers,
                                              ExpressionCache.NullTypeArray, ExpressionCache.NullDelegateArray, ExpressionCache.NullTypeArray));
                    trapHandlersPushed = NewTemp(typeof(bool), "trapHandlersPushed");
                    exprs.Add(Expression.Assign(trapHandlersPushed, ExpressionCache.Constant(true)));
                }
            }

            _stmtCount += statements.Count;

            // If there is a single statement, we just wrap it in try/catch (to handle traps and/or prompting based on
            // $ErrorActionPreference.
            //
            // If there are multiple statements, we could wrap each statement in try/catch, but it's more efficient
            // to have a single try/catch around the entire block.
            //
            // The interpreter handles a large number or try/catches fine, but the JIT fails miserably because it uses
            // an exponential algorithm.
            //
            // Because a trap or user prompting can have us stop the erroneous statement, but continue to the next
            // statement, we need to generate code like this:
            //
            //     dispatchIndex = 0;
            // DispatchNextStatementTarget:
            //     try {
            //         switch (dispatchIndex) {
            //         case 0: goto L0;
            //         case 1: goto L1;
            //         case 2: goto L2;
            //         }
            // L0:     dispatchIndex = 1;
            //         statement1;
            // L1:     dispatchIndex = 2;
            //         statement2;
            //     } catch (FlowControlException) {
            //         throw;
            //     } catch (Exception e) {
            //         ExceptionHandlingOps.CheckActionPreference(functionContext, e);
            //         goto DispatchNextStatementTarget;
            //     }
            // L2:
            //
            // This approach makes JIT possible (but still slow) for large functions and it speeds up the light
            // compile (interpreter compile) by about 80%.

            if (statements.Count == 1)
            {
                var exprList = new List<Expression>(3);
                CompileTrappableExpression(exprList, statements[0]);
                exprList.Add(ExpressionCache.Empty);
                var expr = Expression.TryCatch(Expression.Block(exprList), _stmtCatchHandlers);
                exprs.Add(expr);
            }
            else
            {
                var switchCases = new SwitchCase[statements.Count + 1];
                var dispatchTargets = new LabelTarget[statements.Count + 1];
                for (int i = 0; i <= statements.Count; i++)
                {
                    dispatchTargets[i] = Expression.Label();
                    switchCases[i] = Expression.SwitchCase(Expression.Goto(dispatchTargets[i]),
                                                           ExpressionCache.Constant(i));
                }

                var dispatchIndex = Expression.Variable(typeof(int), "stmt");
                temps.Add(dispatchIndex);
                exprs.Add(Expression.Assign(dispatchIndex, ExpressionCache.Constant(0)));

                var dispatchNextStatementTarget = Expression.Label();
                exprs.Add(Expression.Label(dispatchNextStatementTarget));

                var tryBodyExprs = new List<Expression>();
                tryBodyExprs.Add(Expression.Switch(dispatchIndex, switchCases));

                for (int i = 0; i < statements.Count; i++)
                {
                    tryBodyExprs.Add(Expression.Label(dispatchTargets[i]));
                    tryBodyExprs.Add(Expression.Assign(dispatchIndex, ExpressionCache.Constant(i + 1)));
                    CompileTrappableExpression(tryBodyExprs, statements[i]);
                }

                tryBodyExprs.Add(ExpressionCache.Empty);

                var exception = Expression.Variable(typeof(Exception), "exception");
                var callCheckActionPreference = Expression.Call(
                    CachedReflectionInfo.ExceptionHandlingOps_CheckActionPreference,
                    Compiler._functionContext, exception);
                var catchAll = Expression.Catch(
                    exception,
                    Expression.Block(callCheckActionPreference,
                    Expression.Goto(dispatchNextStatementTarget)));

                var expr = Expression.TryCatch(Expression.Block(tryBodyExprs),
                                               new CatchBlock[] { s_catchFlowControl, catchAll });
                exprs.Add(expr);
                exprs.Add(Expression.Label(dispatchTargets[statements.Count]));
            }

            if (_trapNestingCount > 0)
            {
                var parameterExprs = new List<ParameterExpression>();
                var finallyBlockExprs = new List<Expression>();
                if (oldActiveHandler != null)
                {
                    parameterExprs.Add(oldActiveHandler);
                    finallyBlockExprs.Add(Expression.Assign(handlerInScope, oldActiveHandler));
                }

                parameterExprs.Add(trapHandlersPushed);
                finallyBlockExprs.Add(
                    Expression.IfThen(trapHandlersPushed, Expression.Call(_functionContext, CachedReflectionInfo.FunctionContext_PopTrapHandlers)));

                originalExprs.Add(
                    Expression.Block(parameterExprs, Expression.TryFinally(Expression.Block(exprs), Expression.Block(finallyBlockExprs))));
            }

            if (traps != null)
            {
                _trapNestingCount -= 1;
            }
        }

        private void CompileTrappableExpression(List<Expression> exprList, StatementAst stmt)
        {
            var expr = Compile(stmt);
            exprList.Add(expr);

            var pipeAst = stmt as PipelineAst;
            if (pipeAst != null)
            {
                if (pipeAst.PipelineElements.Count == 1 && pipeAst.PipelineElements[0] is CommandExpressionAst)
                {
                    // A single expression - must set $? after the expression.
                    exprList.Add(s_setDollarQuestionToTrue);
                }
            }
            else
            {
                var assignmentStatementAst = stmt as AssignmentStatementAst;
                if (assignmentStatementAst != null)
                {
                    Ast right = null;
                    var assignAst = assignmentStatementAst;
                    while (assignAst != null)
                    {
                        right = assignAst.Right;
                        assignAst = right as AssignmentStatementAst;
                    }

                    pipeAst = right as PipelineAst;
                    if (right is CommandExpressionAst ||
                        (pipeAst != null && pipeAst.PipelineElements.Count == 1 &&
                         pipeAst.PipelineElements[0] is CommandExpressionAst))
                    {
                        // If the RHS of the assign was an expression, 
                        exprList.Add(s_setDollarQuestionToTrue);
                    }
                }
            }
        }

        #endregion Script Blocks

        #region Statements

        public object VisitTypeDefinition(TypeDefinitionAst typeDefinitionAst)
        {
            return ExpressionCache.Empty;
        }

        public object VisitPropertyMember(PropertyMemberAst propertyMemberAst)
        {
            return null;
        }

        public object VisitFunctionMember(FunctionMemberAst functionMemberAst)
        {
            return null;
        }

        public object VisitBaseCtorInvokeMemberExpression(BaseCtorInvokeMemberExpressionAst baseCtorInvokeMemberExpressionAst)
        {
            var target = CompileExpressionOperand(baseCtorInvokeMemberExpressionAst.Expression);
            var args = CompileInvocationArguments(baseCtorInvokeMemberExpressionAst.Arguments);
            var baseCtorCallConstraints = GetInvokeMemberConstraints(baseCtorInvokeMemberExpressionAst);
            return InvokeBaseCtorMethod(baseCtorCallConstraints, target, args);
        }

        public object VisitUsingStatement(UsingStatementAst usingStatementAst)
        {
            return ExpressionCache.Empty;
        }

        public object VisitConfigurationDefinition(ConfigurationDefinitionAst configurationAst)
        {
            return this.VisitPipeline(configurationAst.GenerateSetItemPipelineAst());
        }

        public object VisitDynamicKeywordStatement(DynamicKeywordStatementAst dynamicKeywordAst)
        {
            if (dynamicKeywordAst.Keyword.MetaStatement)
            {
                return Expression.Empty();
            }

            return this.VisitPipeline(dynamicKeywordAst.GenerateCommandCallPipelineAst());
        }

        private bool _generatedCallToDefineWorkflows;
        public object VisitFunctionDefinition(FunctionDefinitionAst functionDefinitionAst)
        {
            if (functionDefinitionAst.IsWorkflow)
            {
                if (_generatedCallToDefineWorkflows)
                    return ExpressionCache.Empty;

                var topAst = functionDefinitionAst.Parent;
                while (!(topAst is ScriptBlockAst))
                {
                    topAst = topAst.Parent;
                }

                _generatedCallToDefineWorkflows = true;
                return Expression.Call(CachedReflectionInfo.FunctionOps_DefineWorkflows,
                                       _executionContextParameter,
                                       Expression.Constant(topAst, typeof(ScriptBlockAst)));
            }

            return Expression.Call(CachedReflectionInfo.FunctionOps_DefineFunction,
                                   _executionContextParameter,
                                   Expression.Constant(functionDefinitionAst),
                                   Expression.Constant(new ScriptBlockExpressionWrapper(functionDefinitionAst)));
        }

        public object VisitIfStatement(IfStatementAst ifStmtAst)
        {
            int clauseCount = ifStmtAst.Clauses.Count;

            var exprs = new Tuple<BlockExpression, Expression>[clauseCount];
            for (int i = 0; i < clauseCount; ++i)
            {
                IfClause ifClause = ifStmtAst.Clauses[i];
                var cond = Expression.Block(
                    UpdatePosition(ifClause.Item1),
                    CaptureStatementResults(ifClause.Item1, CaptureAstContext.Condition).Convert(typeof(bool)));
                var body = Compile(ifClause.Item2);
                exprs[i] = Tuple.Create(cond, body);
            }

            Expression elseExpr = null;
            if (ifStmtAst.ElseClause != null)
            {
                elseExpr = Compile(ifStmtAst.ElseClause);
            }

            Expression result = null;
            for (int i = clauseCount - 1; i >= 0; --i)
            {
                var cond = exprs[i].Item1;
                var body = exprs[i].Item2;
                if (elseExpr != null)
                {
                    result = elseExpr = Expression.IfThenElse(cond, body, elseExpr);
                }
                else
                {
                    result = elseExpr = Expression.IfThen(cond, body);
                }
            }

            return result;
        }

        public object VisitTrap(TrapStatementAst trapStatementAst)
        {
            Diagnostics.Assert(false, "Traps are not visited directly.");
            return null;
        }

        public object VisitAssignmentStatement(AssignmentStatementAst assignmentStatementAst)
        {
            return CompileAssignment(assignmentStatementAst);
        }

        private Expression CompileAssignment(
            AssignmentStatementAst assignmentStatementAst,
            MergeRedirectExprs generateRedirectExprs = null)
        {
            var arrayLHS = assignmentStatementAst.Left as ArrayLiteralAst;
            var parenExpressionAst = assignmentStatementAst.Left as ParenExpressionAst;
            if (parenExpressionAst != null)
            {
                arrayLHS = parenExpressionAst.Pipeline.GetPureExpression() as ArrayLiteralAst;
            }

            // If assigning to an array, then we prefer an enumerable result because we use an IList
            // to generate the assignments, no sense in converting to object[], or worse, returning a
            // single object.
            // We should not preserve the partial output if exception is thrown when evaluating right-hand-side expression.
            Expression rightExpr = CaptureStatementResults(assignmentStatementAst.Right,
                arrayLHS != null ? CaptureAstContext.Enumerable : CaptureAstContext.AssignmentWithoutResultPreservation, generateRedirectExprs);

            if (arrayLHS != null)
            {
                rightExpr = DynamicExpression.Dynamic(PSArrayAssignmentRHSBinder.Get(arrayLHS.Elements.Count), typeof(IList), rightExpr);
            }

            var exprs = new List<Expression>
                            {
                            // Set current position in case of errors.
                            UpdatePosition(assignmentStatementAst),
                            ReduceAssignment((ISupportsAssignment)assignmentStatementAst.Left,
                                             assignmentStatementAst.Operator,
                                             rightExpr)
                        };

            return Expression.Block(exprs);
        }

        public object VisitPipeline(PipelineAst pipelineAst)
        {
            var temps = new List<ParameterExpression>();
            var exprs = new List<Expression>();

            if (!(pipelineAst.Parent is AssignmentStatementAst || pipelineAst.Parent is ParenExpressionAst))
            {
                // If the parent is an assignment, we've already added a sequence point, don't add another.
                exprs.Add(UpdatePosition(pipelineAst));
            }

            var pipeElements = pipelineAst.PipelineElements;
            var firstCommandExpr = (pipeElements[0] as CommandExpressionAst);
            if (firstCommandExpr != null && pipeElements.Count == 1)
            {
                if (firstCommandExpr.Redirections.Count > 0)
                {
                    exprs.Add(GetRedirectedExpression(firstCommandExpr, captureForInput: false));
                }
                else
                {
                    exprs.Add(Compile(firstCommandExpr));
                }
            }
            else
            {
                Expression input;
                int i, commandsInPipe;

                if (firstCommandExpr != null)
                {
                    if (firstCommandExpr.Redirections.Count > 0)
                    {
                        input = GetRedirectedExpression(firstCommandExpr, captureForInput: true);
                    }
                    else
                    {
                        input = GetRangeEnumerator(firstCommandExpr.Expression) ??
                                Compile(firstCommandExpr.Expression);
                    }
                    i = 1;
                    commandsInPipe = pipeElements.Count - 1;
                }
                else
                {
                    // Compiled code normally never sees AutomationNull.  We use that value
                    // here so that we can tell the difference b/w $null and no input when
                    // starting the pipeline, in other words, PipelineOps.InvokePipe will
                    // not pass this value to the pipe.

                    input = ExpressionCache.AutomationNullConstant;
                    i = 0;
                    commandsInPipe = pipeElements.Count;
                }
                Expression[] pipelineExprs = new Expression[commandsInPipe];
                CommandBaseAst[] pipeElementAsts = new CommandBaseAst[commandsInPipe];
                var commandRedirections = new object[commandsInPipe];

                for (int j = 0; i < pipeElements.Count; ++i, ++j)
                {
                    var pipeElement = pipeElements[i];
                    pipelineExprs[j] = Compile(pipeElement);

                    commandRedirections[j] = GetCommandRedirections(pipeElement);
                    pipeElementAsts[j] = pipeElement;
                }

                // The redirections are passed as a CommandRedirection[][] - one dimension for each command in the pipe,
                // one dimension because each command may have multiple redirections.  Here we create the array for
                // each command in the pipe, either a compile time constant or created at runtime if necessary.
                Expression redirectionExpr;
                if (commandRedirections.Any(r => r is Expression))
                {
                    // If any command redirections are non-constant, commandRedirections will have a Linq.Expression in it,
                    // in which case we must create the array at runtime
                    redirectionExpr =
                        Expression.NewArrayInit(typeof(CommandRedirection[]),
                                                commandRedirections.Select(r => (r as Expression) ?? Expression.Constant(r, typeof(CommandRedirection[]))));
                }
                else if (commandRedirections.Any(r => r != null))
                {
                    // There were redirections, but all were compile time constant, so build the array at compile time.
                    redirectionExpr =
                        Expression.Constant(commandRedirections.Map(r => r as CommandRedirection[]));
                }
                else
                {
                    // No redirections.
                    redirectionExpr = ExpressionCache.NullCommandRedirections;
                }

                if (firstCommandExpr != null)
                {
                    var inputTemp = Expression.Variable(input.Type);
                    temps.Add(inputTemp);
                    exprs.Add(Expression.Assign(inputTemp, input));
                    input = inputTemp;
                }

                Expression invokePipe = Expression.Call(
                    CachedReflectionInfo.PipelineOps_InvokePipeline,
                    input.Cast(typeof(object)),
                    firstCommandExpr != null ? ExpressionCache.FalseConstant : ExpressionCache.TrueConstant,
                    Expression.NewArrayInit(typeof(CommandParameterInternal[]), pipelineExprs),
                    Expression.Constant(pipeElementAsts),
                    redirectionExpr,
                    _functionContext);

                exprs.Add(invokePipe);
            }

            return Expression.Block(temps, exprs);
        }

        private object GetCommandRedirections(CommandBaseAst command)
        {
            int count = command.Redirections.Count;
            if (count == 0)
            {
                return null;
            }

            // Most redirections will be instances of CommandRedirection, but non-constant filenames
            // will generated a Linq.Expression, so we store objects.
            object[] compiledRedirections = new object[count];
            for (int i = 0; i < count; ++i)
            {
                compiledRedirections[i] = command.Redirections[i].Accept(this);
            }

            // If there were any non-constant expressions, we must generate the array at runtime.
            if (compiledRedirections.Any(r => r is Expression))
            {
                return Expression.NewArrayInit(typeof(CommandRedirection),
                                               compiledRedirections.Select(r => (r as Expression) ?? Expression.Constant(r)));
            }

            // Otherwise, we can use a compile time constant array.
            return compiledRedirections.Map(r => (CommandRedirection)r);
        }

        // A redirected expression requires extra work because there is no CommandProcessor or PipelineProcessor
        // created to pass the redirections to, so we must change the redirections in the ExecutionContext
        // directly.
        private Expression GetRedirectedExpression(CommandExpressionAst commandExpr, bool captureForInput)
        {
            // Generate code like:
            //
            //   try {
            //       oldPipe = funcContext._outputPipe;
            //       funcContext._outputPipe = outputFileRedirection.BindForExpression(executionContext);
            //       tmp = nonOutputFileRedirection.BindForExpression(executionContext);
            //       tmp1 = mergingRedirection.BindForExpression(executionContext, funcContext._outputPipe);
            //       funcContext._outputPipe.Add(expression...);
            //   } finally {
            //       nonOutputFileRedirection.UnbindForExpression(tmp);
            //       mergingRedirection.UnbindForExpression(tmp1);
            //       nonOutputFileRedirection.Dispose();
            //       outputFileRedirection.Dispose();
            //       funcContext._outputPipe = oldPipe;
            //   }
            //
            // In the above psuedo-code, any of {outputFileRedirection, nonOutputFileRedirection, mergingRedirection} may 
            // not exist, but the order is preserved, so that file redirections go before merging redirections (so that
            // funcContext._outputPipe has the correct value when setting up merging.)

            var exprs = new List<Expression>();
            var temps = new List<ParameterExpression>();
            var finallyExprs = new List<Expression>();

            // For the output stream, we change funcContext._outputPipe so all output goes to the file.
            // Currently output can only be redirected to a file stream.
            bool outputRedirected =
                commandExpr.Redirections.Any(r => r is FileRedirectionAst &&
                                                  (r.FromStream == RedirectionStream.Output || r.FromStream == RedirectionStream.All));

            ParameterExpression resultList = null;
            ParameterExpression oldPipe = null;
            var subExpr = commandExpr.Expression as SubExpressionAst;
            if (subExpr != null && captureForInput)
            {
                oldPipe = NewTemp(typeof(Pipe), "oldPipe");
                resultList = NewTemp(typeof(List<object>), "resultList");
                temps.Add(resultList);
                temps.Add(oldPipe);
                exprs.Add(Expression.Assign(oldPipe, s_getCurrentPipe));
                exprs.Add(Expression.Assign(resultList, Expression.New(CachedReflectionInfo.ObjectList_ctor)));
                exprs.Add(Expression.Assign(s_getCurrentPipe, Expression.New(CachedReflectionInfo.Pipe_ctor, resultList)));
                exprs.Add(Expression.Call(oldPipe, CachedReflectionInfo.Pipe_SetVariableListForTemporaryPipe, s_getCurrentPipe));
            }

            // We must generate the code for output redirection to a file before any merging redirections
            // because merging redirections will use funcContext._outputPipe as the value to merge to, so defer merging
            // redirections until file redirections are done.
            foreach (var fileRedirectionAst in commandExpr.Redirections.OfType<FileRedirectionAst>())
            {
                // This will simply return a Linq.Expression representing the redirection.
                var compiledRedirection = VisitFileRedirection(fileRedirectionAst);

                // For non-output streams (error, warning, etc.) we must save the old stream so it can be restored.
                // The savedPipe variable is used only for setting funcContext._outputPipe for redirecting Output to file.
                // The savedPipes variable is used for restoring non-output streams (error, warning, etc.).
                var savedPipes = NewTemp(typeof(Pipe[]), "savedPipes");
                temps.Add(savedPipes);

                var redirectionExpr = NewTemp(typeof(FileRedirection), "fileRedirection");
                temps.Add(redirectionExpr);
                exprs.Add(Expression.Assign(redirectionExpr, (Expression)compiledRedirection));
                /*
                                if (fileRedirectionAst.FromStream != RedirectionStream.Output && !(redirectionExpr is ConstantExpression))
                                {
                                    // We'll be reusing redirectionExpr, it's not constant, so save it in a temp.
                                    var temp = Expression.Variable(redirectionExpr.Type);
                                    temps.Add(temp);
                                    exprs.Add(Expression.Assign(temp, redirectionExpr));
                                    redirectionExpr = temp;
                                }
                */
                exprs.Add(Expression.Assign(
                    savedPipes,
                    Expression.Call(redirectionExpr, CachedReflectionInfo.FileRedirection_BindForExpression, _functionContext)));
                finallyExprs.Add(Expression.Call(redirectionExpr.Cast(typeof(CommandRedirection)),
                                                    CachedReflectionInfo.CommandRedirection_UnbindForExpression,
                                                    _functionContext,
                                                    savedPipes));
                // In either case, we must dispose of the redirection or file handles won't get released.
                finallyExprs.Add(Expression.Call(redirectionExpr,
                                                 CachedReflectionInfo.FileRedirection_Dispose));
            }

            Expression result = null;
            var parenExpr = commandExpr.Expression as ParenExpressionAst;
            if (parenExpr != null)
            {
                // Special processing for paren expressions that capture output.
                // Insert any merge redirect expressions during paren expression compilation.
                var assignmentStatementAst = parenExpr.Pipeline as AssignmentStatementAst;
                if (assignmentStatementAst != null)
                {
                    result = CompileAssignment(
                        assignmentStatementAst,
                        (mergeExprs, mergeFinallyExprs) => AddMergeRedirectionExpressions(commandExpr.Redirections, temps, mergeExprs, mergeFinallyExprs));
                }
                else
                {
                    bool shouldPreserveResultInCaseofException = parenExpr.ShouldPreserveOutputInCaseOfException();
                    result = CaptureAstResults(
                        parenExpr.Pipeline,
                        shouldPreserveResultInCaseofException
                                ? CaptureAstContext.AssignmentWithResultPreservation
                                : CaptureAstContext.AssignmentWithoutResultPreservation,
                        (mergeExprs, mergeFinallyExprs) => AddMergeRedirectionExpressions(commandExpr.Redirections, temps, mergeExprs, mergeFinallyExprs));
                }
            }
            else if (subExpr != null)
            {
                // Include any redirection merging.
                AddMergeRedirectionExpressions(commandExpr.Redirections, temps, exprs, finallyExprs);

                exprs.Add(Compile(subExpr.SubExpression));
                if (resultList != null)
                {
                    // If there is no resultList, we wrote our results of the subexpression directly to the pipe
                    // instead of being collected to be written here.
                    result = Expression.Call(CachedReflectionInfo.PipelineOps_PipelineResult, resultList);
                }
            }
            else
            {
                // Include any redirection merging.
                AddMergeRedirectionExpressions(commandExpr.Redirections, temps, exprs, finallyExprs);

                result = Compile(commandExpr.Expression);
            }

            if (result != null)
            {
                if (!outputRedirected && captureForInput)
                {
                    // If we are capturing the input (for code like:  $foo.Bar() 2>&1 | downstream), then we must
                    // capture the expression results, unless output was redirected to a file because in that case,
                    // the output can go straight to a file.
                    exprs.Add(result);
                }
                else
                {
                    exprs.Add(CallAddPipe(result, s_getCurrentPipe));
                    // Make sure the result of the expression we return is AutomationNull.Value.
                    exprs.Add(ExpressionCache.AutomationNullConstant);
                }
            }

            if (oldPipe != null)
            {
                // If a temporary pipe was created at the beginning, we should restore the original pipe in the 
                // very end of the finally block. Otherwise, _getCurrentPipe may be messed up by the following 
                // file redirection unbind operation.
                // For example:
                //    function foo
                //    {
                //       [cmdletbinding()]
                //       param()
                //       $(gcm NoExist) > test.txt | % { $_ }  ## file redirect with new temp pipe
                //       "hello"
                //    }
                // before this change, running 'foo' will not write out 'hello'.
                finallyExprs.Add(Expression.Assign(s_getCurrentPipe, oldPipe));
            }

            if (finallyExprs.Count != 0)
            {
                return Expression.Block(temps.ToArray(), Expression.TryFinally(Expression.Block(exprs), Expression.Block(finallyExprs)));
            }
            return Expression.Block(temps.ToArray(), exprs);
        }

        private void AddMergeRedirectionExpressions(
            ReadOnlyCollection<RedirectionAst> redirections,
            List<ParameterExpression> temps,
            List<Expression> exprs,
            List<Expression> finallyExprs)
        {
            foreach (var mergingRedirectionAst in redirections.OfType<MergingRedirectionAst>())
            {
                var savedPipes = NewTemp(typeof(Pipe[]), "savedPipes");
                temps.Add(savedPipes);
                var redirectionExpr = Expression.Constant(VisitMergingRedirection(mergingRedirectionAst));
                exprs.Add(
                    Expression.Assign(savedPipes,
                                      Expression.Call(redirectionExpr,
                                                      CachedReflectionInfo.MergingRedirection_BindForExpression,
                                                      _executionContextParameter,
                                                      _functionContext)));
                // Undo merging redirections first (so file redirections get undone after).
                finallyExprs.Insert(0, Expression.Call(redirectionExpr.Cast(typeof(CommandRedirection)),
                                                       CachedReflectionInfo.CommandRedirection_UnbindForExpression,
                                                       _functionContext,
                                                       savedPipes));
            }
        }

        public object VisitMergingRedirection(MergingRedirectionAst mergingRedirectionAst)
        {
            // Most Visit* methods return a Linq.Expression, this method being an exception.  VisitPipeline
            // may be able to pass a compile time array of redirections if all of the redirections are
            // constant.  Merging redirections never vary at runtime, so there is no sense in deferring
            // the creation of the merging object until runtime.

            return new MergingRedirection(mergingRedirectionAst.FromStream, mergingRedirectionAst.ToStream);
        }

        public object VisitFileRedirection(FileRedirectionAst fileRedirectionAst)
        {
            Expression fileNameExpr;
            var strConst = fileRedirectionAst.Location as StringConstantExpressionAst;
            if (strConst != null)
            {
                // When the filename is a constant, we still must generate a new FileRedirection
                // at runtime because we can't keep a cached object in the closure because the object
                // is disposed after executing the command.
                fileNameExpr = Compile(strConst);
            }
            else
            {
                // The filename is not constant, so we must generate code to evaluate the filename at runtime.
                fileNameExpr = DynamicExpression.Dynamic(PSToStringBinder.Get(), typeof(string),
                                                         CompileExpressionOperand(fileRedirectionAst.Location), _executionContextParameter);
            }

            return Expression.New(CachedReflectionInfo.FileRedirection_ctor,
                                  Expression.Constant(fileRedirectionAst.FromStream),
                                  ExpressionCache.Constant(fileRedirectionAst.Append),
                                  fileNameExpr);
        }

        public object VisitCommand(CommandAst commandAst)
        {
            var commandElements = commandAst.CommandElements;
            Expression[] elementExprs = new Expression[commandElements.Count];

            for (int i = 0; i < commandElements.Count; ++i)
            {
                var element = commandElements[i];
                if (element is CommandParameterAst)
                {
                    elementExprs[i] = Compile(element);
                }
                else
                {
                    var splatTest = element;
                    bool splatted = false;

                    UsingExpressionAst usingExpression = element as UsingExpressionAst;
                    if (usingExpression != null)
                    {
                        splatTest = usingExpression.SubExpression;
                    }

                    VariableExpressionAst variableExpression = splatTest as VariableExpressionAst;
                    if (variableExpression != null)
                    {
                        splatted = variableExpression.Splatted;
                    }

                    bool arrayIsSingleArgumentForNativeCommand = ArgumentIsNotReallyArrayIfCommandIsNative(element);
                    elementExprs[i] =
                        Expression.Call(CachedReflectionInfo.CommandParameterInternal_CreateArgument,
                                        Expression.Constant(element.Extent),
                                        Expression.Convert(GetCommandArgumentExpression(element), typeof(object)),
                                        ExpressionCache.Constant(splatted),
                                        ExpressionCache.Constant(arrayIsSingleArgumentForNativeCommand));
                }
            }

            Expression result = Expression.NewArrayInit(typeof(CommandParameterInternal), elementExprs);

            if (commandElements.Count == 2 && commandElements[1] is ParenExpressionAst)
            {
                // If they used method invocation syntax, we want a strict mode error.  We can't
                // check that at compile time as it might change at runtime, so we'll add a runtime check.

                var args = ((ParenExpressionAst)commandElements[1]).Pipeline.GetPureExpression();
                if (args is ArrayLiteralAst)
                {
                    if (commandElements[0].Extent.EndColumnNumber == commandElements[1].Extent.StartColumnNumber
                        && commandElements[0].Extent.EndLineNumber == commandElements[1].Extent.StartLineNumber)
                    {
                        // no whitespace b/w command name and paren, add a strict mode check
                        result = Expression.Block(
                            Expression.IfThen(
                                Compiler.IsStrictMode(2, _executionContextParameter),
                                Compiler.ThrowRuntimeError("StrictModeFunctionCallWithParens",
                                                           ParserStrings.StrictModeFunctionCallWithParens)),
                            result);
                    }
                }
            }

            return result;
        }

        private Expression GetCommandArgumentExpression(CommandElementAst element)
        {
            var constElement = element as ConstantExpressionAst;
            if (constElement != null && LanguagePrimitives.IsNumeric(LanguagePrimitives.GetTypeCode(constElement.StaticType)))
            {
                var commandArgumentText = constElement.Extent.Text;
                if (!commandArgumentText.Equals(constElement.Value.ToString(), StringComparison.Ordinal))
                {
                    // If the ToString on the constant would differ from what the user specified, then wrap the
                    // value so we can recover the actual argument text.
                    return Expression.Constant(ParserOps.WrappedNumber(constElement.Value, commandArgumentText));
                }
            }

            var result = Compile(element);
            if (result.Type == typeof(object[]))
            {
                result = Expression.Call(CachedReflectionInfo.PipelineOps_CheckAutomationNullInCommandArgumentArray, result);
            }
            else if (constElement == null && result.Type == typeof(object))
            {
                result = Expression.Call(CachedReflectionInfo.PipelineOps_CheckAutomationNullInCommandArgument, result);
            }

            return result;
        }

        public object VisitCommandExpression(CommandExpressionAst commandExpressionAst)
        {
            var child = commandExpressionAst.Expression;
            var expr = Compile(child);
            var unary = child as UnaryExpressionAst;
            if ((unary != null && unary.TokenKind.HasTrait(TokenFlags.PrefixOrPostfixOperator)) || expr.Type == typeof(void))
            {
                return expr;
            }
            return CallAddPipe(expr, s_getCurrentPipe);
        }

        private bool ArgumentIsNotReallyArrayIfCommandIsNative(Ast arg)
        {
            var arrayLiteralAst = arg as ArrayLiteralAst;
            if (arrayLiteralAst == null)
            {
                return false;
            }

            Diagnostics.Assert(arrayLiteralAst.Elements.Count > 1, "Single dimension array arguments are surrounded with parens if the value is an argument");
            var previousElement = arrayLiteralAst.Elements[0];
            for (int index = 1; index < arrayLiteralAst.Elements.Count; index++)
            {
                var element = arrayLiteralAst.Elements[index];
                // EndOffset is 1 past the end which puts it on the comma, so if +1 is not the next element,
                // there is whitespace between elements
                if (previousElement.Extent.EndOffset + 1 != element.Extent.StartOffset)
                {
                    return false;
                }
                previousElement = element;
            }

            return true;
        }

        public object VisitCommandParameter(CommandParameterAst commandParameterAst)
        {
            var arg = commandParameterAst.Argument;
            var errorPos = commandParameterAst.ErrorPosition;
            if (arg != null)
            {
                bool spaceAfterParameter = (errorPos.EndLineNumber != arg.Extent.StartLineNumber ||
                                            errorPos.EndColumnNumber != arg.Extent.StartColumnNumber);
                bool arrayIsSingleArgumentForNativeCommand = ArgumentIsNotReallyArrayIfCommandIsNative(arg);
                return Expression.Call(CachedReflectionInfo.CommandParameterInternal_CreateParameterWithArgument,
                                       Expression.Constant(errorPos),
                                       Expression.Constant(commandParameterAst.ParameterName),
                                       Expression.Constant(errorPos.Text),
                                       Expression.Constant(arg.Extent),
                                       Expression.Convert(GetCommandArgumentExpression(arg), typeof(object)),
                                       ExpressionCache.Constant(spaceAfterParameter),
                                       ExpressionCache.Constant(arrayIsSingleArgumentForNativeCommand));
            }

            return Expression.Call(CachedReflectionInfo.CommandParameterInternal_CreateParameter,
                                   Expression.Constant(errorPos),
                                   Expression.Constant(commandParameterAst.ParameterName),
                                   Expression.Constant(errorPos.Text));
        }

        internal static Expression ThrowRuntimeError(string errorID, string resourceString, params Expression[] exceptionArgs)
        {
            return ThrowRuntimeError(errorID, resourceString, typeof(object), exceptionArgs);
        }

        internal static Expression ThrowRuntimeError(string errorID, string resourceString, Type throwResultType, params Expression[] exceptionArgs)
        {
            return ThrowRuntimeError(typeof(RuntimeException), errorID, resourceString, throwResultType, exceptionArgs);
        }

        internal static Expression ThrowRuntimeError(Type exceptionType, string errorID, string resourceString, Type throwResultType, params Expression[] exceptionArgs)
        {
            var exceptionArgArray = exceptionArgs != null
                                        ? Expression.NewArrayInit(typeof(object), exceptionArgs.Select(e => e.Cast(typeof(object))))
                                        : ExpressionCache.NullConstant;
            Expression[] argExprs = new Expression[]
            {
                ExpressionCache.NullConstant,                        // targetObject
                Expression.Constant(exceptionType, typeof(Type)),    // exceptionType
                ExpressionCache.NullExtent,                          // errorPosition
                Expression.Constant(errorID),                        // ErrorID
                Expression.Constant(resourceString),                 // error message
                exceptionArgArray                                    // args to use when formatting error message
            };

            return Expression.Throw(
                Expression.Call(CachedReflectionInfo.InterpreterError_NewInterpreterException, argExprs),
                throwResultType);
        }

        internal static Expression ThrowRuntimeErrorWithInnerException(string errorID,
                                                                       string resourceString,
                                                                       Expression innerException,
                                                                       params Expression[] exceptionArgs)
        {
            return ThrowRuntimeErrorWithInnerException(errorID, Expression.Constant(resourceString), innerException, typeof(object), exceptionArgs);
        }

        internal static Expression ThrowRuntimeErrorWithInnerException(string errorID,
                                                                       Expression resourceString,
                                                                       Expression innerException,
                                                                       Type throwResultType,
                                                                       params Expression[] exceptionArgs)
        {
            var exceptionArgArray = exceptionArgs != null
                                        ? Expression.NewArrayInit(typeof(object), exceptionArgs)
                                        : ExpressionCache.NullConstant;
            Expression[] argExprs = new Expression[]
            {
                ExpressionCache.NullConstant,                                   // targetObject
                Expression.Constant(typeof(RuntimeException), typeof(Type)),    // exceptionType
                ExpressionCache.NullExtent,                                     // errorPosition
                Expression.Constant(errorID),                                   // ErrorID
                resourceString,                                                 // error message
                innerException,                                                 // innerException
                exceptionArgArray                                               // args to use when formatting error message
            };

            return Expression.Throw(
                Expression.Call(CachedReflectionInfo.InterpreterError_NewInterpreterExceptionWithInnerException, argExprs),
                throwResultType);
        }

        internal static Expression CreateThrow(Type resultType, Type exception, Type[] exceptionArgTypes, params object[] exceptionArgs)
        {
            Diagnostics.Assert(exceptionArgTypes.Length == exceptionArgs.Length, "types count must match args count for constructor call.");

            Expression[] argExprs = new Expression[exceptionArgs.Length];
            for (int i = 0; i < exceptionArgs.Length; i++)
            {
                object o = exceptionArgs[i];
                Expression e = Expression.Constant(o, exceptionArgTypes[i]);
                argExprs[i] = e;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            ConstructorInfo constructor = exception.GetConstructor(flags, null, CallingConventions.Any, exceptionArgTypes, null);
            if (constructor == null)
            {
                throw new PSArgumentException("Type doesn't have constructor with a given signature");
            }

            return (Expression.Throw(Expression.New(constructor, argExprs), resultType));
        }

        internal static Expression CreateThrow(Type resultType, Type exception, params object[] exceptionArgs)
        {
            Type[] argTypes = PSTypeExtensions.EmptyTypes;
            if (exceptionArgs != null)
            {
                argTypes = new Type[exceptionArgs.Length];
                for (int i = 0; i < exceptionArgs.Length; i++)
                {
                    Diagnostics.Assert(exceptionArgs[i] != null, "Can't deduce argument type from null");
                    argTypes[i] = exceptionArgs[i].GetType();
                }
            }

            return CreateThrow(resultType, exception, argTypes, exceptionArgs);
        }

        public object VisitSwitchStatement(SwitchStatementAst switchStatementAst)
        {
            var avs = new AutomaticVarSaver(this, SpecialVariables.UnderbarVarPath, (int)AutomaticVariable.Underbar);
            var temps = new List<ParameterExpression>();
            ParameterExpression skipDefault = null;
            if (switchStatementAst.Default != null)
            {
                skipDefault = NewTemp(typeof(bool), "skipDefault");
                temps.Add(skipDefault);
            }

            var switchBodyGenerator = GetSwitchBodyGenerator(switchStatementAst, avs, skipDefault);

            if ((switchStatementAst.Flags & SwitchFlags.File) != 0)
            {
                // Generate:
                //
                //    string path = SwitchOps.ResolveFilePath(cond.Extent, cond, context);
                //    StreamReader sr = null;
                //    try
                //    {
                //        sr = new StreamReader(path);
                //        string line;
                //        while ((line = sr.ReadLine()) != null)
                //        {
                //            $_ = line
                //            test clauses
                //        }
                //    }
                //    catch (FlowControlException) { throw; }
                //    catch (Exception exception)
                //    {
                //        CommandProcessorBase.CheckForSevereException(exception);
                //        // "The file could not be read:" + fne.Message
                //        throw InterpreterError.NewInterpreterExceptionWithInnerException(path, typeof(RuntimeException),
                //            cond.Extent, "FileReadError", ParserStrings.FileReadError, exception, exception.Message);
                //    }
                //    finally
                //    {
                //        if (sr != null) sr.Dispose();
                //    }

                var exprs = new List<Expression>();

                var path = NewTemp(typeof(string), "path");
                temps.Add(path);

                exprs.Add(UpdatePosition(switchStatementAst.Condition));

                // We should not preserve the partial output if exception is thrown when evaluating the condition.
                var cond = DynamicExpression.Dynamic(PSToStringBinder.Get(), typeof(string),
                                                     CaptureStatementResults(switchStatementAst.Condition, CaptureAstContext.AssignmentWithoutResultPreservation),
                                                     _executionContextParameter);
                exprs.Add(
                    Expression.Assign(
                        path,
                        Expression.Call(CachedReflectionInfo.SwitchOps_ResolveFilePath,
                                        Expression.Constant(switchStatementAst.Condition.Extent),
                                        cond,
                                        _executionContextParameter)));

                var tryBodyExprs = new List<Expression>();

                var streamReader = NewTemp(typeof(StreamReader), "streamReader");
                var line = NewTemp(typeof(string), "line");
                temps.Add(streamReader);
                temps.Add(line);

                tryBodyExprs.Add(
                    Expression.Assign(streamReader,
                                      Expression.New(CachedReflectionInfo.StreamReader_ctor, path)));
                var loopTest =
                    Expression.NotEqual(
                        Expression.Assign(line,
                                          Expression.Call(streamReader, CachedReflectionInfo.StreamReader_ReadLine)).Cast(typeof(object)),
                        ExpressionCache.NullConstant);

                tryBodyExprs.Add(avs.SaveAutomaticVar());
                tryBodyExprs.Add(GenerateWhileLoop(switchStatementAst.Label,
                                            () => loopTest,
                                            (loopBody, breakTarget, continueTarget) => switchBodyGenerator(loopBody, line)));
                var tryBlock = Expression.Block(tryBodyExprs);
                var finallyBlock = Expression.Block(
                    Expression.IfThen(
                        Expression.NotEqual(streamReader, ExpressionCache.NullConstant),
                        Expression.Call(streamReader.Cast(typeof(IDisposable)), CachedReflectionInfo.IDisposable_Dispose)),
                    avs.RestoreAutomaticVar());
                var exception = NewTemp(typeof(Exception), "exception");
                var catchAllBlock = Expression.Block(
                    tryBlock.Type,
                    Expression.Call(CachedReflectionInfo.CommandProcessorBase_CheckForSevereException, exception),
                    ThrowRuntimeErrorWithInnerException("FileReadError",
                                                        ParserStrings.FileReadError,
                                                        exception,
                                                        Expression.Property(exception, CachedReflectionInfo.Exception_Message)));

                exprs.Add(Expression.TryCatchFinally(
                    tryBlock, finallyBlock,
                    new[]
                    {
                        Expression.Catch(typeof(FlowControlException), Expression.Rethrow(tryBlock.Type)),
                        Expression.Catch(exception, catchAllBlock)
                    }));

                return Expression.Block(temps.Concat(avs.GetTemps()), exprs);
            }

            // We convert:
            //     switch ($enumerable) {}
            // Into:
            //     $switch = GetEnumerator $enumerable
            //     if ($switch == $null)
            //     {
            //         $switch  = (new object[] { $enumerable }).GetEnumerator()
            //     }
            // REVIEW: should we consider this form of switch a loop for the purposes of deciding
            // to compile or not?  I have a feeling the loop form is uncommon and compiling isn't worth it.

            var tryStmt = Expression.TryFinally(
                Expression.Block(
                    avs.SaveAutomaticVar(),
                    GenerateIteratorStatement(SpecialVariables.switchVarPath, () => UpdatePosition(switchStatementAst.Condition), _switchTupleIndex,
                                              switchStatementAst, switchBodyGenerator)
                    ),
                avs.RestoreAutomaticVar());

            return Expression.Block(temps.Concat(avs.GetTemps()), tryStmt);
        }

        private Action<List<Expression>, Expression> GetSwitchBodyGenerator(SwitchStatementAst switchStatementAst, AutomaticVarSaver avs, ParameterExpression skipDefault)
        {
            return (exprs, newValue) =>
                   {
                       var clauseEvalBinder = PSSwitchClauseEvalBinder.Get(switchStatementAst.Flags);
                       exprs.Add(avs.SetNewValue(newValue));

                       if (skipDefault != null)
                       {
                           exprs.Add(Expression.Assign(skipDefault, ExpressionCache.Constant(false)));
                       }

                       IsConstantValueVisitor iscvv = new IsConstantValueVisitor();
                       ConstantValueVisitor cvv = new ConstantValueVisitor();

                       int clauseCount = switchStatementAst.Clauses.Count;
                       for (int i = 0; i < clauseCount; i++)
                       {
                           var clause = switchStatementAst.Clauses[i];

                           Expression test;
                           object constValue = ((bool)clause.Item1.Accept(iscvv)) ? clause.Item1.Accept(cvv) : null;
                           if (constValue is ScriptBlock)
                           {
                               var call = Expression.Call(Expression.Constant(constValue),
                                                           CachedReflectionInfo.ScriptBlock_DoInvokeReturnAsIs,
                               /*useLocalScope=*/         ExpressionCache.Constant(true),
                               /*errorHandlingBehavior=*/ Expression.Constant(ScriptBlock.ErrorHandlingBehavior.WriteToExternalErrorPipe),
                               /*dollarUnder=*/           GetLocal((int)AutomaticVariable.Underbar).Convert(typeof(object)),
                               /*input=*/                 ExpressionCache.AutomationNullConstant,
                               /*scriptThis=*/            ExpressionCache.AutomationNullConstant,
                               /*args=*/                  ExpressionCache.NullObjectArray);
                               test = DynamicExpression.Dynamic(PSConvertBinder.Get(typeof(bool)), typeof(bool), call);
                           }
                           else if (constValue != null)
                           {
                               SwitchFlags flags = switchStatementAst.Flags;
                               Expression conditionExpr = constValue is Regex || constValue is WildcardPattern
                                                    ? (Expression)Expression.Constant(constValue)
                                                    : DynamicExpression.Dynamic(PSToStringBinder.Get(), typeof(string),
                                                                                (constValue is Type)
                                                                                    ? Expression.Constant(constValue, typeof(Type))
                                                                                    : Expression.Constant(constValue),
                                                                                _executionContextParameter);
                               Expression currentAsString =
                                   DynamicExpression.Dynamic(PSToStringBinder.Get(), typeof(string), GetLocal((int)AutomaticVariable.Underbar),
                                                             _executionContextParameter);
                               if ((flags & SwitchFlags.Regex) != 0 || constValue is Regex)
                               {
                                   test = Expression.Call(CachedReflectionInfo.SwitchOps_ConditionSatisfiedRegex,
                                       /*caseSensitive=*/ ExpressionCache.Constant((flags & SwitchFlags.CaseSensitive) != 0),
                                       /*condition=*/     conditionExpr,
                                       /*errorPosition=*/ Expression.Constant(clause.Item1.Extent),
                                       /*str=*/           currentAsString,
                                       /*context=*/       _executionContextParameter);
                               }
                               else if ((flags & SwitchFlags.Wildcard) != 0 || constValue is WildcardPattern)
                               {
                                   // It would be a little better to just build the wildcard at compile time, but
                                   // the runtime method must exist when variable cases are involved.
                                   test = Expression.Call(CachedReflectionInfo.SwitchOps_ConditionSatisfiedWildcard,
                                       /*caseSensitive=*/ ExpressionCache.Constant((flags & SwitchFlags.CaseSensitive) != 0),
                                       /*condition=*/     conditionExpr,
                                       /*str=*/           currentAsString,
                                       /*context=*/       _executionContextParameter);
                               }
                               else
                               {
                                   test = CallStringEquals(conditionExpr, currentAsString,
                                                           ((flags & SwitchFlags.CaseSensitive) == 0));
                               }
                           }
                           else
                           {
                               var cond = Compile(clause.Item1);
                               test = DynamicExpression.Dynamic(clauseEvalBinder, typeof(bool),
                                                                cond, GetLocal((int)AutomaticVariable.Underbar), _executionContextParameter);
                           }

                           exprs.Add(UpdatePosition(clause.Item1));
                           if (skipDefault != null)
                           {
                               exprs.Add(Expression.IfThen(
                                   test,
                                   Expression.Block(Compile(clause.Item2),
                                                    Expression.Assign(skipDefault, ExpressionCache.Constant(true)))));
                           }
                           else
                           {
                               exprs.Add(Expression.IfThen(test, Compile(clause.Item2)));
                           }
                       }

                       if (skipDefault != null)
                       {
                           exprs.Add(Expression.IfThen(Expression.Not(skipDefault), Compile(switchStatementAst.Default)));
                       }
                   };
        }

        public object VisitDataStatement(DataStatementAst dataStatementAst)
        {
            // We'll generate the following:
            //
            // try
            // {
            //     oldLanguageMode = context.LanguageMode;
            //     CheckLanguageMode (if necessary, only if SupportedCommands specified)
            //     CheckAllowedCommands (if necessary, it may have been done at parse time if possible)
            //     context.LanguageMode = PSLanguageMode.RestrictedLanguage;
            //     either:
            //         dataStatementAst.Variable = execute body
            //     or if the variable name was not specified:
            //         execute body, writing results to the current output pipe
            // }
            // finally
            // {
            //     context.LanguageMode = oldLanguageMode;
            // }

            var oldLanguageMode = NewTemp(typeof(PSLanguageMode), "oldLanguageMode");
            var languageModePropertyExpr = Expression.Property(_executionContextParameter, CachedReflectionInfo.ExecutionContext_LanguageMode);

            var exprs = new List<Expression>
                {
                    // Save old language mode before doing anything else so if there is an exception, we don't
                    // "restore" the language mode to an uninitialized value (which happens to be "full" mode.
                    Expression.Assign(oldLanguageMode, languageModePropertyExpr),

                    UpdatePosition(dataStatementAst),

                    // Auto-import utility module if needed, so that ConvertFrom-StringData isn't set to RestrictedLanguage
                    // by the data section.
                    Expression.Call(CachedReflectionInfo.RestrictedLanguageChecker_EnsureUtilityModuleLoaded,
                                    _executionContextParameter)
                };

            if (dataStatementAst.CommandsAllowed.Count > 0)
            {
                // If CommandsAllowed was specified, we need to check the language mode - the data section runs
                // in restricted language mode and we don't want to allow disallowed commands to run if we were in
                // constrained language mode.
                exprs.Add(
                    Expression.Call(
                        CachedReflectionInfo.RestrictedLanguageChecker_CheckDataStatementLanguageModeAtRuntime,
                        Expression.Constant(dataStatementAst), _executionContextParameter));
            }

            if (dataStatementAst.HasNonConstantAllowedCommand)
            {
                // We couldn't check the commands allowed at parse time, so we must do so at runtime
                exprs.Add(
                    Expression.Call(CachedReflectionInfo.RestrictedLanguageChecker_CheckDataStatementAstAtRuntime,
                                    Expression.Constant(dataStatementAst),
                                    Expression.NewArrayInit(typeof(string),
                                                            dataStatementAst.CommandsAllowed.Select(
                                                                elem => Compile(elem).Convert(typeof(string))))));
            }

            exprs.Add(Expression.Assign(languageModePropertyExpr, Expression.Constant(PSLanguageMode.RestrictedLanguage)));

            // If we'll be assigning directly, we need to capture the value, otherwise just write to the current output pipe.
            // We should not preserve the partial output if exception is thrown when evaluating the body expr.
            var dataExpr = dataStatementAst.Variable != null
                               ? CaptureAstResults(dataStatementAst.Body, CaptureAstContext.AssignmentWithoutResultPreservation).Cast(typeof(object))
                               : Compile(dataStatementAst.Body);
            exprs.Add(dataExpr);

            var block = Expression.Block(
                new[] { oldLanguageMode },
                Expression.TryFinally(
                    Expression.Block(exprs),
                    Expression.Assign(languageModePropertyExpr, oldLanguageMode)));

            if (dataStatementAst.Variable != null)
            {
                return dataStatementAst.TupleIndex < 0
                    ? CallSetVariable(Expression.Constant(new VariablePath("local:" + dataStatementAst.Variable)), block)
                    : Expression.Assign(GetLocal(dataStatementAst.TupleIndex), block);
            }

            return block;
        }

        private static CatchBlock[] GenerateLoopBreakContinueCatchBlocks(string label, LabelTarget breakLabel, LabelTarget continueLabel)
        {
            var breakExceptionVar = Expression.Parameter(typeof(BreakException));
            var continueExceptionVar = Expression.Parameter(typeof(ContinueException));
            return new[]
            {
                // catch (BreakException ev) { if (ev.MatchLabel(loopStatement.Label) { goto breakTarget; } throw; }
                Expression.Catch(breakExceptionVar,
                    Expression.IfThenElse(
                        Expression.Call(breakExceptionVar,
                                        CachedReflectionInfo.LoopFlowException_MatchLabel,
                                        Expression.Constant(label ?? "", typeof(string))),
                        Expression.Break(breakLabel),
                        Expression.Rethrow())),

                // catch (ContinueException ev) { if (ev.MatchLabel(loopStatement.Label) { goto continueTarget; } throw; }
                Expression.Catch(continueExceptionVar,
                    Expression.IfThenElse(
                        Expression.Call(continueExceptionVar,
                                        CachedReflectionInfo.LoopFlowException_MatchLabel,
                                        Expression.Constant(label ?? "", typeof(string))),
                        Expression.Continue(continueLabel),
                        Expression.Rethrow()))
            };
        }

        private Expression GenerateWhileLoop(string loopLabel,
                                             Func<Expression> generateCondition,
                                             Action<List<Expression>, LabelTarget, LabelTarget> generateLoopBody,
                                             PipelineBaseAst continueAst = null)
        {
            // If continueAst is not null, generate:
            //    LoopTop:
            //    if (condition)
            //    {
            //        try {
            //            loop body
            //            // break -> goto BreakTarget
            //            // continue -> goto ContinueTarget
            //        } catch (BreakException be) {
            //            if (be.MatchLabel(loopLabel)) goto BreakTarget;
            //            throw;
            //        } catch (ContinueException ce) {
            //            if (ce.MatchLabel(loopLabel)) goto ContinueTarget;
            //            throw;
            //        }
            //    ContinueTarget:
            //            continueAction
            //            goto LoopTop:
            //    }
            //    :BreakTarget
            //
            // If continueAst is null, generate:
            //    ContinueTarget:
            //    if (condition)
            //    {
            //        try {
            //            loop body
            //            // break -> goto BreakTarget
            //            // continue -> goto ContinueTarget
            //            goto ContinueTarget
            //        } catch (BreakException be) {
            //            if (be.MatchLabel(loopLabel)) goto BreakTarget;
            //            throw;
            //        } catch (ContinueException ce) {
            //            if (ce.MatchLabel(loopLabel)) goto ContinueTarget;
            //            throw;
            //        }
            //    }
            //    :BreakTarget

            int preStmtCount = _stmtCount;

            var exprs = new List<Expression>();
            var continueLabel = Expression.Label(!string.IsNullOrEmpty(loopLabel) ? loopLabel + "Continue" : "continue");
            var breakLabel = Expression.Label(!string.IsNullOrEmpty(loopLabel) ? loopLabel + "Break" : "break");
            var enterLoop = new EnterLoopExpression();

            var loopTop = (continueAst != null)
                              ? Expression.Label(!string.IsNullOrEmpty(loopLabel) ? loopLabel + "LoopTop" : "looptop")
                              : continueLabel;

            exprs.Add(Expression.Label(loopTop));
            exprs.Add(enterLoop);

            var loopBodyExprs = new List<Expression>();
            loopBodyExprs.Add(s_callCheckForInterrupts);

            _loopTargets.Add(new LoopGotoTargets(loopLabel ?? "", breakLabel, continueLabel));
            _generatingWhileOrDoLoop = true;
            generateLoopBody(loopBodyExprs, breakLabel, continueLabel);
            _generatingWhileOrDoLoop = false;
            if (continueAst == null)
            {
                loopBodyExprs.Add(Expression.Goto(loopTop));
            }
            _loopTargets.RemoveAt(_loopTargets.Count - 1);

            Expression loopBody =
                Expression.TryCatch(Expression.Block(loopBodyExprs),
                                    GenerateLoopBreakContinueCatchBlocks(loopLabel, breakLabel, continueLabel));
            if (continueAst != null)
            {
                var x = new List<Expression>();
                x.Add(loopBody);
                x.Add(Expression.Label(continueLabel));
                if (continueAst.GetPureExpression() != null)
                {
                    // Assignments generate the code to update the position automatically,
                    // but pre/post increments don't, so we add it here explicitly.
                    x.Add(UpdatePosition(continueAst));
                }
                // We should not preserve the partial output if exception is thrown when evaluating the continueAst.
                x.Add(CaptureStatementResults(continueAst, CaptureAstContext.AssignmentWithoutResultPreservation));
                x.Add(Expression.Goto(loopTop));
                loopBody = Expression.Block(x);
            }

            if (generateCondition != null)
            {
                exprs.Add(Expression.IfThen(generateCondition().Convert(typeof(bool)), loopBody));
            }
            else
            {
                exprs.Add(loopBody);
            }
            exprs.Add(Expression.Label(breakLabel));
            enterLoop.LoopStatementCount = _stmtCount - preStmtCount;
            return (enterLoop.Loop = new PowerShellLoopExpression(exprs));
        }

        private Expression GenerateDoLoop(LoopStatementAst loopStatement)
        {
            // Generate code like:
            //    :RepeatTarget
            //    try {
            //       loop body
            //       // break -> goto BreakTarget
            //       // continue -> goto TestBlock
            //    } catch (BreakException be) {
            //        if (be.MatchLabel(loopLabel))
            //             goto BreakTarget;
            //        throw;
            //    } catch (ContinueException ce) {
            //        if (ce.MatchLabel(loopLabel))
            //            goto ContinueTarget;
            //        throw;
            //    }
            //    :ContinueTarget
            //    if (condition)
            //    {
            //        goto RepeatTarget
            //    }
            //    :BreakTarget

            int preStmtCount = _stmtCount;

            string loopLabel = loopStatement.Label;
            var exprs = new List<Expression>();
            var repeatLabel = Expression.Label(!string.IsNullOrEmpty(loopLabel) ? loopLabel : null);
            var continueLabel = Expression.Label(!string.IsNullOrEmpty(loopLabel) ? loopLabel + "Continue" : "continue");
            var breakLabel = Expression.Label(!string.IsNullOrEmpty(loopLabel) ? loopLabel + "Break" : "break");
            var enterLoopExpression = new EnterLoopExpression();

            exprs.Add(Expression.Label(repeatLabel));
            exprs.Add(enterLoopExpression);

            _loopTargets.Add(new LoopGotoTargets(loopLabel ?? "", breakLabel, continueLabel));
            _generatingWhileOrDoLoop = true;
            var loopBodyExprs = new List<Expression>
                                    {
                                    s_callCheckForInterrupts,
                                    Compile(loopStatement.Body),
                                    ExpressionCache.Empty
                                };
            _generatingWhileOrDoLoop = false;
            _loopTargets.RemoveAt(_loopTargets.Count - 1);

            exprs.Add(Expression.TryCatch(Expression.Block(loopBodyExprs),
                                          GenerateLoopBreakContinueCatchBlocks(loopLabel, breakLabel, continueLabel)));
            exprs.Add(Expression.Label(continueLabel));
            var test = CaptureStatementResults(loopStatement.Condition, CaptureAstContext.Condition).Convert(typeof(bool));
            if (loopStatement is DoUntilStatementAst)
            {
                test = Expression.Not(test);
            }
            exprs.Add(Expression.IfThen(test, Expression.Goto(repeatLabel)));
            exprs.Add(Expression.Label(breakLabel));

            enterLoopExpression.LoopStatementCount = _stmtCount - preStmtCount;
            return enterLoopExpression.Loop = new PowerShellLoopExpression(exprs);
        }

        private Expression GenerateIteratorStatement(VariablePath iteratorVariablePath,
                                                     Func<Expression> generateMoveNextUpdatePosition,
                                                     int iteratorTupleIndex,
                                                     LabeledStatementAst stmt,
                                                     Action<List<Expression>, Expression> generateBody)
        {
            // We convert:
            //     foreach ($x in $enumerable) {}
            // Into:
            //     try
            //     {
            //         $oldforeach = $foreach
            //         $enumerable = condition
            //         $foreach = GetEnumerator $enumerable
            //         if ($foreach == $null && $enumerable != $null)
            //         {
            //             $foreach  = (new object[] { $enumerable }).GetEnumerator()
            //         }
            //         if ($foreach != $null)
            //         {
            //             while ($foreach.MoveNext())
            //             {
            //                 $x = $foreach.Current
            //             }
            //         }
            //    }
            //    finally
            //    {
            //        $foreach = $oldforeach
            //    }
            // The translation for switch is similar.

            var temps = new List<ParameterExpression>();
            var exprs = new List<Expression>();
            var avs = new AutomaticVarSaver(this, iteratorVariablePath, iteratorTupleIndex);
            bool generatingForeach = stmt is ForEachStatementAst;

            exprs.Add(avs.SaveAutomaticVar());

            // $enumerable = condition
            // $foreach/$switch = GetEnumerator $enumerable
            var enumerable = NewTemp(typeof(object), "enumerable");
            temps.Add(enumerable);
            if (generatingForeach)
            {
                exprs.Add(UpdatePosition(stmt.Condition));
            }
            exprs.Add(
                Expression.Assign(enumerable,
                                  GetRangeEnumerator(stmt.Condition.GetPureExpression())
                                    ?? CaptureStatementResults(stmt.Condition, CaptureAstContext.Enumerable).Convert(typeof(object))));

            var iteratorTemp = NewTemp(typeof(IEnumerator), iteratorVariablePath.UnqualifiedPath);
            temps.Add(iteratorTemp);
            exprs.Add(Expression.Assign(iteratorTemp,
                                        DynamicExpression.Dynamic(PSEnumerableBinder.Get(), typeof(IEnumerator), enumerable)));

            // In a foreach, generate:
            //     if ($foreach == $null && $enumerable != $null)
            //     {
            //         $foreach = (new object[] { $enumerable }).GetEnumerator()
            //     }
            // In a switch, generate:
            //     if ($switch == $null)
            //     {
            //         $switch = (new object[] { $enumerable }).GetEnumerator()
            //     }

            var testNeedScalarToEnumerable =
                generatingForeach
                    ? Expression.AndAlso(
                        Expression.Equal(iteratorTemp, ExpressionCache.NullConstant),
                        Expression.NotEqual(enumerable, ExpressionCache.NullConstant))
                    : Expression.Equal(iteratorTemp, ExpressionCache.NullConstant);
            var scalarToEnumerable =
                Expression.Assign(iteratorTemp,
                    Expression.Call(Expression.NewArrayInit(typeof(object),
                                        Expression.Convert(enumerable, typeof(object))),
                                    CachedReflectionInfo.IEnumerable_GetEnumerator));
            exprs.Add(Expression.IfThen(testNeedScalarToEnumerable, scalarToEnumerable));
            exprs.Add(avs.SetNewValue(iteratorTemp));

            var moveNext = Expression.Block(
                    generateMoveNextUpdatePosition(),
                    Expression.Call(iteratorTemp, CachedReflectionInfo.IEnumerator_MoveNext));

            var loop = GenerateWhileLoop(stmt.Label,
                                         () => moveNext,
                                         (loopBody, breakTarget, continueTarget) => generateBody(loopBody, Expression.Property(iteratorTemp, CachedReflectionInfo.IEnumerator_Current)));

            // With a foreach, the enumerator may never get assigned, in which case we skip the loop entirely.
            // Generate that test.
            // With a switch, the switch body is never skipped, so skip generating that test, and skip creating
            // target block.
            if (generatingForeach)
            {
                exprs.Add(Expression.IfThen(Expression.NotEqual(iteratorTemp, ExpressionCache.NullConstant), loop));
            }
            else
            {
                exprs.Add(loop);
            }

            return Expression.Block(
                temps.Concat(avs.GetTemps()),
                Expression.TryFinally(Expression.Block(exprs), avs.RestoreAutomaticVar()));
        }

        public object VisitForEachStatement(ForEachStatementAst forEachStatementAst)
        {
            // We convert:
            //     foreach ($x in $enumerable) {}
            // Into:
            //     $foreach = GetEnumerator $enumerable
            //     if ($foreach == $null && $enumerable != $null)
            //     {
            //         $foreach  = (new object[] { $enumerable }).GetEnumerator()
            //     }
            //     if ($foreach != $null)
            //     {
            //         while ($foreach.MoveNext())
            //         {
            //             $x = $foreach.Current
            //         }
            //     }

            Action<List<Expression>, Expression> loopBodyGenerator =
                (exprs, newValue) =>
                {
                    exprs.Add(ReduceAssignment(forEachStatementAst.Variable, TokenKind.Equals, newValue));
                    exprs.Add(Compile(forEachStatementAst.Body));
                };

            return GenerateIteratorStatement(SpecialVariables.foreachVarPath,
                                             () => UpdatePosition(forEachStatementAst.Variable), _foreachTupleIndex,
                                             forEachStatementAst, loopBodyGenerator);
        }

        private Expression GetRangeEnumerator(ExpressionAst condExpr)
        {
            Expression result = null;
            if (condExpr != null)
            {
                var binaryExpr = condExpr as BinaryExpressionAst;
                if (binaryExpr != null && binaryExpr.Operator == TokenKind.DotDot)
                {
                    Expression lhs = Compile(binaryExpr.Left);
                    Expression rhs = Compile(binaryExpr.Right);

                    result = Expression.New(CachedReflectionInfo.RangeEnumerator_ctor,
                                            lhs.Convert(typeof(int)),
                                            rhs.Convert(typeof(int)));
                }
            }
            return result;
        }

        public object VisitDoWhileStatement(DoWhileStatementAst doWhileStatementAst)
        {
            return GenerateDoLoop(doWhileStatementAst);
        }

        public object VisitDoUntilStatement(DoUntilStatementAst doUntilStatementAst)
        {
            return GenerateDoLoop(doUntilStatementAst);
        }

        public object VisitForStatement(ForStatementAst forStatementAst)
        {
            // We should not preserve the partial output if exception is thrown when evaluating the initializer.
            var init = (forStatementAst.Initializer != null)
                           ? CaptureStatementResults(forStatementAst.Initializer, CaptureAstContext.AssignmentWithoutResultPreservation)
                           : null;

            var generateCondition = forStatementAst.Condition != null
                ? () => Expression.Block(UpdatePosition(forStatementAst.Condition),
                                         CaptureStatementResults(forStatementAst.Condition, CaptureAstContext.Condition))
                : (Func<Expression>)null;

            var loop = GenerateWhileLoop(forStatementAst.Label, generateCondition,
                                         (loopBody, breakTarget, continueTarget) => loopBody.Add(Compile(forStatementAst.Body)),
                                         forStatementAst.Iterator);

            if (init != null)
            {
                return Expression.Block(init, loop);
            }
            return loop;
        }

        public object VisitWhileStatement(WhileStatementAst whileStatementAst)
        {
            return GenerateWhileLoop(whileStatementAst.Label,
                                     () => Expression.Block(UpdatePosition(whileStatementAst.Condition),
                                                            CaptureStatementResults(whileStatementAst.Condition, CaptureAstContext.Condition)),
                                     (loopBody, breakTarget, continueTarget) => loopBody.Add(Compile(whileStatementAst.Body)));
        }

        public object VisitCatchClause(CatchClauseAst catchClauseAst)
        {
            Diagnostics.Assert(false, "the catch body is visited directly from VisitTryStatement.");
            return null;
        }

        // This helper class is used to help save and restore the value of an automatic variable that is set
        // as part of executing some statement, then restored to it's previous value after the statement completes.
        //
        // The basic idea is, if the automatic var has a value in the current frame, save it and restore it.
        // If the automatic var has no value in the current frame, then we set the variable's value to $null
        // after leaving the stmt.
        //
        // The psuedo-code:
        //
        //    try {
        //        oldValue = (localSet.Get(automaticVar)) ? locals.ItemNNN : null;
        //        locals.ItemNNN = newValue
        //        localsSet.Set(automaticVar, true);
        //        any code
        //    } finally {
        //        locals.ItemNNN = oldValue;
        //    }
        //
        // This is a little convoluted because an automatic variable isn't necessarily set.
        private class AutomaticVarSaver
        {
            private readonly Compiler _compiler;
            private readonly int _automaticVar;
            private readonly VariablePath _autoVarPath;
            private ParameterExpression _oldValue;

            internal AutomaticVarSaver(Compiler compiler, VariablePath autoVarPath, int automaticVar)
            {
                _compiler = compiler;
                _autoVarPath = autoVarPath;
                _automaticVar = automaticVar;
            }

            internal IEnumerable<ParameterExpression> GetTemps()
            {
                Diagnostics.Assert(_oldValue != null, "caller to only call GetTemps after calling SaveAutomaticVar");
                yield return _oldValue;
            }

            internal Expression SaveAutomaticVar()
            {
                var getValueExpr = _automaticVar < 0
                                       ? Compiler.CallGetVariable(Expression.Constant(_autoVarPath), null)
                                       : _compiler.GetLocal(_automaticVar);
                _oldValue = _compiler.NewTemp(getValueExpr.Type, "old_" + _autoVarPath.UnqualifiedPath);
                return Expression.Assign(_oldValue, getValueExpr);
            }

            internal Expression SetNewValue(Expression newValue)
            {
                if (_automaticVar < 0)
                {
                    return Compiler.CallSetVariable(Expression.Constant(_autoVarPath), newValue);
                }

                return Expression.Assign(_compiler.GetLocal(_automaticVar), newValue);
            }

            internal Expression RestoreAutomaticVar()
            {
                if (_automaticVar < 0)
                {
                    return Compiler.CallSetVariable(Expression.Constant(_autoVarPath), _oldValue);
                }

                return Expression.Assign(_compiler.GetLocal(_automaticVar), _oldValue);
            }
        }

        public object VisitTryStatement(TryStatementAst tryStatementAst)
        {
            var temps = new List<ParameterExpression>();
            var tryBlockExprs = new List<Expression>();
            var finallyBlockExprs = new List<Expression>();

            // We must set $ExecutionContext.PropagateExceptionsToEnclosingStatementBlock = $true so we don't prompt
            // if an exception is raised, and we must restore the previous value when leaving because we can't
            // know if we're dynamically executing code guarded by a try/catch.
            var oldActiveHandler = NewTemp(typeof(bool), "oldActiveHandler");
            temps.Add(oldActiveHandler);
            var handlerInScope = Expression.Property(_executionContextParameter,
                                     CachedReflectionInfo.ExecutionContext_ExceptionHandlerInEnclosingStatementBlock);
            tryBlockExprs.Add(Expression.Assign(oldActiveHandler, handlerInScope));
            tryBlockExprs.Add(Expression.Assign(handlerInScope, ExpressionCache.Constant(true)));
            finallyBlockExprs.Add(Expression.Assign(handlerInScope, oldActiveHandler));

            CompileStatementListWithTraps(tryStatementAst.Body.Statements, tryStatementAst.Body.Traps, tryBlockExprs, temps);

            var catches = new List<CatchBlock>();

            if (tryStatementAst.CatchClauses.Count == 1 && tryStatementAst.CatchClauses[0].IsCatchAll)
            {
                // Generate:
                //    catch (RuntimeException rte)
                //    {
                //        oldrte = context.CurrentExceptionBeingHandled
                //        context.CurrentExceptionBeingHandled = rte
                //        oldDollarUnder = $_
                //        $_ = new ErrorRecord(rte.ErrorRecord, rte)
                //        try {
                //            user catch code
                //        } finally {
                //            $_ = oldDollarUnder
                //            context.CurrentExceptionBeingHandled = oldrte
                //        }
                AutomaticVarSaver avs = new AutomaticVarSaver(this, SpecialVariables.UnderbarVarPath,
                                                              (int)AutomaticVariable.Underbar);
                var rte = NewTemp(typeof(RuntimeException), "rte");
                var oldrte = NewTemp(typeof(RuntimeException), "oldrte");
                var errorRecord = Expression.New(CachedReflectionInfo.ErrorRecord__ctor,
                                                 Expression.Property(rte, CachedReflectionInfo.RuntimeException_ErrorRecord),
                                                 rte);
                var catchExprs = new List<Expression>
                                     {
                                     Expression.Assign(oldrte, s_currentExceptionBeingHandled),
                                     Expression.Assign(s_currentExceptionBeingHandled, rte),
                                     avs.SaveAutomaticVar(),
                                     avs.SetNewValue(errorRecord)
                                 };
                StatementBlockAst statementBlock = tryStatementAst.CatchClauses[0].Body;
                CompileStatementListWithTraps(statementBlock.Statements, statementBlock.Traps, catchExprs, temps);

                var tf = Expression.TryFinally(
                    Expression.Block(typeof(void), catchExprs),
                    Expression.Block(typeof(void),
                                     avs.RestoreAutomaticVar(),
                                     Expression.Assign(s_currentExceptionBeingHandled, oldrte)));

                catches.Add(Expression.Catch(typeof(PipelineStoppedException), Expression.Rethrow()));
                catches.Add(Expression.Catch(rte, Expression.Block(avs.GetTemps().Append(oldrte).ToArray(), tf)));
            }
            else if (tryStatementAst.CatchClauses.Count > 0)
            {
                // We can't generate a try/catch in quite the way one might expect for a few reasons:
                //     * At compile time, we may not have loaded the types that are being caught
                //     * We wrap exceptions in a RuntimeException so they can carry a position.
                //
                // Instead, we generate something like:
                //     try {}
                //     catch (RuntimeException re) {
                //         try
                //         {
                //             oldexception = context.CurrentExceptionBeingHandled
                //             context.CurrentExceptionBeingHandled = re
                //             old_ = $_
                //             $_ = re.ErrorRecord
                //             switch (ExceptionHandlingOps.FindMatchingHandler(re, types))
                //             {
                //             case 0:
                //                  /* first handler */
                //                  break;
                //             case 1:
                //             case 2:
                //                  /* second handler (we do allow a single handler for multiple types) */
                //                  break;
                //             default:
                //                  /* no matching handler, but could be a trap or user might want prompting */
                //                  /* will rethrow the exception if that's what we need to do */
                //                  ExceptionHandlingOps.CheckActionPreference(functionContext, exception);
                //             }
                //         } finally {
                //             $_ = old_
                //             context.CurrentExceptionBeingHandled = oldexception
                //         }
                //     }

                // Try to get the types at compile time.  We could end up with nulls in this array, we'll handle
                // that with runtime code.
                int countTypes = 0;
                for (int index = 0; index < tryStatementAst.CatchClauses.Count; index++)
                {
                    var c = tryStatementAst.CatchClauses[index];
                    // If CatchTypes.Count is empty, we still want to count the catch all handler.
                    countTypes += Math.Max(c.CatchTypes.Count, 1);
                }

                var catchTypes = new Type[countTypes];
                Expression catchTypesExpr = Expression.Constant(catchTypes);
                var dynamicCatchTypes = new List<Expression>();
                var cases = new List<SwitchCase>();
                int handlerTypeIndex = 0;
                int i = 0;

                var exception = Expression.Parameter(typeof(RuntimeException));
                for (int index = 0; index < tryStatementAst.CatchClauses.Count; index++)
                {
                    var c = tryStatementAst.CatchClauses[index];
                    if (c.IsCatchAll)
                    {
                        catchTypes[i] = typeof(ExceptionHandlingOps.CatchAll);
                    }
                    else
                    {
                        for (int index1 = 0; index1 < c.CatchTypes.Count; index1++)
                        {
                            var ct = c.CatchTypes[index1];
                            catchTypes[i] = ct.TypeName.GetReflectionType();
                            if (catchTypes[i] == null)
                            {
                                // Type needs to be resolved at runtime, so we'll use code like:
                                //
                                // if (catchTypes[i] == null) catchTypes[i] = ResolveTypeName(ct.TypeName)
                                //
                                // We use a constant array, resolve just once (unless it fails) to prevent re-resolving
                                // each time it executes.

                                var indexExpr = Expression.ArrayAccess(catchTypesExpr, ExpressionCache.Constant(i));
                                dynamicCatchTypes.Add(
                                    Expression.IfThen(
                                        Expression.Equal(indexExpr, ExpressionCache.NullType),
                                        Expression.Assign(
                                            indexExpr,
                                            Expression.Call(CachedReflectionInfo.TypeOps_ResolveTypeName,
                                                            Expression.Constant(ct.TypeName),
                                                            Expression.Constant(ct.Extent)))));
                            }
                            i += 1;
                        }
                    }

                    // Wrap the body in a void block so all cases have the same type.
                    var catchBody = Expression.Block(typeof(void), Compile(c.Body));

                    if (c.IsCatchAll)
                    {
                        cases.Add(Expression.SwitchCase(catchBody,
                                                        ExpressionCache.Constant(handlerTypeIndex)));
                        handlerTypeIndex += 1;
                    }
                    else
                    {
                        cases.Add(Expression.SwitchCase(catchBody,
                                                        Enumerable.Range(handlerTypeIndex,
                                                                         handlerTypeIndex + c.CatchTypes.Count).Select(
                                                                             ExpressionCache.Constant)));
                        handlerTypeIndex += c.CatchTypes.Count;
                    }
                }

                if (dynamicCatchTypes.Count > 0)
                {
                    // This might be worth a strict-mode check - if there was a typo, the typo isn't discovered until
                    // the first time an exception is raised, which is rather unfortunate.

                    catchTypesExpr = Expression.Block(dynamicCatchTypes.Append(catchTypesExpr));
                }

                AutomaticVarSaver avs = new AutomaticVarSaver(this, SpecialVariables.UnderbarVarPath,
                                                              (int)AutomaticVariable.Underbar);
                var swCond = Expression.Call(CachedReflectionInfo.ExceptionHandlingOps_FindMatchingHandler,
                                             LocalVariablesParameter, exception, catchTypesExpr, _executionContextParameter);
                var oldexception = NewTemp(typeof(RuntimeException), "oldrte");

                var tf = Expression.TryFinally(
                    Expression.Block(
                        typeof(void),
                        Expression.Assign(oldexception, s_currentExceptionBeingHandled),
                        Expression.Assign(s_currentExceptionBeingHandled, exception),
                        avs.SaveAutomaticVar(),
                        // $_ is set in the call to ExceptionHandlingOps.FindMatchingHandler
                        Expression.Switch(
                            swCond,
                            Expression.Call(
                                CachedReflectionInfo.ExceptionHandlingOps_CheckActionPreference,
                                _functionContext, exception),
                            cases.ToArray())),
                    Expression.Block(
                        avs.RestoreAutomaticVar(),
                        Expression.Assign(s_currentExceptionBeingHandled, oldexception)));

                catches.Add(Expression.Catch(typeof(PipelineStoppedException), Expression.Rethrow()));
                catches.Add(Expression.Catch(exception, Expression.Block(avs.GetTemps().Append(oldexception).ToArray(), tf)));
            }

            if (tryStatementAst.Finally != null)
            {
                // Generate:
                //     oldIsStopping = ExceptionHandlingOps.SuspendStoppingPipeline(executionContext);
                //     try {
                //         user finally statements
                //     } finally {
                //         ExceptionHandlingOps.RestoreStoppingPipeline(executionContext, oldIsStopping);
                //     }

                var oldIsStopping = NewTemp(typeof(bool), "oldIsStopping");
                temps.Add(oldIsStopping);
                finallyBlockExprs.Add(
                    Expression.Assign(oldIsStopping,
                                      Expression.Call(CachedReflectionInfo.ExceptionHandlingOps_SuspendStoppingPipeline,
                                                      _executionContextParameter)));
                var nestedFinallyExprs = new List<Expression>();
                CompileStatementListWithTraps(tryStatementAst.Finally.Statements,
                                              tryStatementAst.Finally.Traps, nestedFinallyExprs, temps);
                if (nestedFinallyExprs.Count == 0)
                {
                    nestedFinallyExprs.Add(ExpressionCache.Empty);
                }

                finallyBlockExprs.Add(Expression.Block(
                    Expression.TryFinally(
                        Expression.Block(nestedFinallyExprs),
                        Expression.Call(CachedReflectionInfo.ExceptionHandlingOps_RestoreStoppingPipeline,
                                        _executionContextParameter,
                                        oldIsStopping))));
            }

            // Our result must have void type, so make sure it does.
            if (tryBlockExprs[tryBlockExprs.Count - 1].Type != typeof(void))
            {
                tryBlockExprs.Add(ExpressionCache.Empty);
            }

            if (catches.Count > 0)
            {
                return Expression.Block(
                    temps.ToArray(),
                    Expression.TryCatchFinally(
                        Expression.Block(tryBlockExprs),
                        Expression.Block(finallyBlockExprs),
                        catches.ToArray()));
            }

            return Expression.Block(
                temps.ToArray(),
                Expression.TryFinally(
                    Expression.Block(tryBlockExprs),
                    Expression.Block(finallyBlockExprs)));
        }

        private Expression GenerateBreakOrContinue(Ast ast,
                                                   ExpressionAst label,
                                                   Func<LoopGotoTargets, LabelTarget> fieldSelector,
                                                   Func<LabelTarget, Expression> exprGenerator,
                                                   ConstructorInfo nonLocalExceptionCtor)
        {
            LabelTarget labelTarget = null;
            Expression labelExpr = null;
            if (label != null)
            {
                labelExpr = Compile(label);
                if (_loopTargets.Count > 0)
                {
                    var labelStrAst = label as StringConstantExpressionAst;
                    if (labelStrAst != null)
                    {
                        labelTarget = (from t in _loopTargets
                                       where t.Label.Equals(labelStrAst.Value, StringComparison.OrdinalIgnoreCase)
                                       select fieldSelector(t)).LastOrDefault();
                    }
                }
            }
            else if (_loopTargets.Count > 0)
            {
                labelTarget = fieldSelector(_loopTargets[_loopTargets.Count - 1]);
            }

            Expression result;
            if (labelTarget != null)
            {
                result = exprGenerator(labelTarget);
            }
            else
            {
                labelExpr = labelExpr ?? ExpressionCache.ConstEmptyString;
                result = Expression.Throw(Expression.New(nonLocalExceptionCtor, labelExpr.Convert(typeof(string))));
            }

            return Expression.Block(UpdatePosition(ast), result);
        }

        public object VisitBreakStatement(BreakStatementAst breakStatementAst)
        {
            return GenerateBreakOrContinue(breakStatementAst, breakStatementAst.Label, lgt => lgt.BreakLabel, Expression.Break,
                                           CachedReflectionInfo.BreakException_ctor);
        }

        public object VisitContinueStatement(ContinueStatementAst continueStatementAst)
        {
            return GenerateBreakOrContinue(continueStatementAst, continueStatementAst.Label, lgt => lgt.ContinueLabel, Expression.Continue,
                                           CachedReflectionInfo.ContinueException_ctor);
        }

        public object VisitReturnStatement(ReturnStatementAst returnStatementAst)
        {
            // If we're returning from a trap, we must raise an exception because the trap is a distinct method, but we want
            // to return from the function containing the trap, not just the trap itself.
            Expression returnExpr;
            if (_compilingTrap)
            {
                returnExpr = Expression.Throw(Expression.New(CachedReflectionInfo.ReturnException_ctor,
                                                             ExpressionCache.AutomationNullConstant));
            }
            else
            {
                returnExpr = Expression.Return(_returnTarget,
                                               _returnTarget.Type == typeof(object)
                                                   ? ExpressionCache.AutomationNullConstant
                                                   : ExpressionCache.Empty);
            }

            if (returnStatementAst.Pipeline != null)
            {
                var pipe = returnStatementAst.Pipeline;
                var assignmentStatementAst = pipe as AssignmentStatementAst;

                Expression returnValue;
                if (CompilingMemberFunction)
                {
                    // We used a null pipe for the function body, but for the return statement,
                    // we need to write to the pipe passed to our dynamic method so InvokeAsMemberFunction
                    // can get the return value to return it.
                    returnValue = CaptureStatementResults(returnStatementAst.Pipeline, CaptureAstContext.AssignmentWithoutResultPreservation);
                    if (MemberFunctionReturnType != typeof(void))
                    {
                        // Write directly to the pipe - don't use the dynamic site (CallAddPipe) as that could enumerate.
                        returnValue = Expression.Call(_returnPipe, CachedReflectionInfo.Pipe_Add,
                            returnValue.Convert(MemberFunctionReturnType).Cast(typeof(object)));
                    }

                    return Expression.Block(UpdatePosition(returnStatementAst.Pipeline),
                                            Expression.Assign(s_getCurrentPipe, _returnPipe),
                                            returnValue,
                                            returnExpr);
                }

                returnValue = assignmentStatementAst != null
                    ? CallAddPipe(CompileAssignment(assignmentStatementAst), s_getCurrentPipe)
                    : Compile(pipe);

                return Expression.Block(returnValue, returnExpr);
            }

            return returnExpr;
        }

        public object VisitExitStatement(ExitStatementAst exitStatementAst)
        {
            // We should not preserve the partial output if exception is thrown when evaluating exitStmt.pipeline.
            Expression exitCode = exitStatementAst.Pipeline != null
                                      ? CaptureStatementResults(exitStatementAst.Pipeline, CaptureAstContext.AssignmentWithoutResultPreservation)
                                      : ExpressionCache.Constant(0);

            return Expression.Block(
                UpdatePosition(exitStatementAst),
                Expression.Throw(Expression.Call(CachedReflectionInfo.PipelineOps_GetExitException,
                                                 exitCode.Convert(typeof(object))),
                                 typeof(void)));
        }

        public object VisitThrowStatement(ThrowStatementAst throwStatementAst)
        {
            // We should not preserve the partial output if exception is thrown when evaluating throwStmt.pipeline.
            Expression throwExpr = throwStatementAst.IsRethrow
                                       ? s_currentExceptionBeingHandled
                                       : (throwStatementAst.Pipeline == null)
                                             ? ExpressionCache.NullConstant
                                             : CaptureStatementResults(throwStatementAst.Pipeline,
                                                                       CaptureAstContext.AssignmentWithoutResultPreservation);

            return Expression.Block(
                UpdatePosition(throwStatementAst),
                Expression.Throw(Expression.Call(CachedReflectionInfo.ExceptionHandlingOps_ConvertToException,
                                                 throwExpr.Convert(typeof(object)),
                                                 Expression.Constant(throwStatementAst.Extent))));
        }

        #endregion Statements

        #region Expressions

        public Expression GenerateCallContains(Expression lhs, Expression rhs, bool ignoreCase)
        {
            return Expression.Call(
                CachedReflectionInfo.ParserOps_ContainsOperatorCompiled,
                _executionContextParameter,
                Expression.Constant(CallSite<Func<CallSite, object, IEnumerator>>.Create(PSEnumerableBinder.Get())),
                Expression.Constant(CallSite<Func<CallSite, object, object, object>>.Create(
                    PSBinaryOperationBinder.Get(ExpressionType.Equal, ignoreCase, scalarCompare: true))),
                lhs.Cast(typeof(object)),
                rhs.Cast(typeof(object)));
        }

        public object VisitBinaryExpression(BinaryExpressionAst binaryExpressionAst)
        {
            object constantValue;
            if (!CompilingConstantExpression && IsConstantValueVisitor.IsConstant(binaryExpressionAst, out constantValue))
            {
                return Expression.Constant(constantValue);
            }

            DynamicMetaObjectBinder binder;
            var lhs = CompileExpressionOperand(binaryExpressionAst.Left);
            var rhs = CompileExpressionOperand(binaryExpressionAst.Right);

            switch (binaryExpressionAst.Operator)
            {
                case TokenKind.And:
                    return Expression.AndAlso(lhs.Convert(typeof(bool)), rhs.Convert(typeof(bool)));
                case TokenKind.Or:
                    return Expression.OrElse(lhs.Convert(typeof(bool)), rhs.Convert(typeof(bool)));
                case TokenKind.Is:
                case TokenKind.IsNot:
                    if (rhs is ConstantExpression && rhs.Type == typeof(Type))
                    {
                        var isType = (Type)((ConstantExpression)rhs).Value;
                        if (!(isType == typeof(PSCustomObject)) && !(isType == typeof(PSObject)))
                        {
                            lhs = lhs.Type.GetTypeInfo().IsValueType ? lhs : Expression.Call(CachedReflectionInfo.PSObject_Base, lhs);
                            if (binaryExpressionAst.Operator == TokenKind.Is)
                                return Expression.TypeIs(lhs, isType);
                            return Expression.Not(Expression.TypeIs(lhs, isType));
                        }
                    }

                    Expression result = Expression.Call(CachedReflectionInfo.TypeOps_IsInstance, lhs.Cast(typeof(object)), rhs.Cast(typeof(object)));
                    if (binaryExpressionAst.Operator == TokenKind.IsNot)
                    {
                        result = Expression.Not(result);
                    }
                    return result;

                case TokenKind.As:
                    return Expression.Call(CachedReflectionInfo.TypeOps_AsOperator, lhs.Cast(typeof(object)), rhs.Convert(typeof(Type)));

                case TokenKind.DotDot:
                    return Expression.Call(CachedReflectionInfo.IntOps_Range,
                                           lhs.Convert(typeof(int)),
                                           rhs.Convert(typeof(int)));
                case TokenKind.Multiply:
                    if (lhs.Type == typeof(double) && rhs.Type == typeof(double))
                    {
                        return Expression.Multiply(lhs, rhs);
                    }
                    binder = PSBinaryOperationBinder.Get(ExpressionType.Multiply);
                    return DynamicExpression.Dynamic(binder, typeof(object), lhs, rhs);
                case TokenKind.Divide:
                    if (lhs.Type == typeof(double) && rhs.Type == typeof(double))
                    {
                        return Expression.Divide(lhs, rhs);
                    }
                    binder = PSBinaryOperationBinder.Get(ExpressionType.Divide);
                    return DynamicExpression.Dynamic(binder, typeof(object), lhs, rhs);
                case TokenKind.Rem:
                    binder = PSBinaryOperationBinder.Get(ExpressionType.Modulo);
                    return DynamicExpression.Dynamic(binder, typeof(object), lhs, rhs);
                case TokenKind.Plus:
                    if (lhs.Type == typeof(double) && rhs.Type == typeof(double))
                    {
                        return Expression.Add(lhs, rhs);
                    }
                    binder = PSBinaryOperationBinder.Get(ExpressionType.Add);
                    return DynamicExpression.Dynamic(binder, typeof(object), lhs, rhs);
                case TokenKind.Minus:
                    if (lhs.Type == typeof(double) && rhs.Type == typeof(double))
                    {
                        return Expression.Subtract(lhs, rhs);
                    }
                    binder = PSBinaryOperationBinder.Get(ExpressionType.Subtract);
                    return DynamicExpression.Dynamic(binder, typeof(object), lhs, rhs);
                case TokenKind.Format:
                    if (lhs.Type != typeof(string))
                    {
                        lhs = DynamicExpression.Dynamic(PSToStringBinder.Get(), typeof(string), lhs, _executionContextParameter);
                    }
                    return Expression.Call(CachedReflectionInfo.StringOps_FormatOperator, lhs, rhs.Cast(typeof(object)));
                case TokenKind.Xor:
                    return Expression.NotEqual(lhs.Convert(typeof(bool)), rhs.Convert(typeof(bool)));
                case TokenKind.Shl:
                    binder = PSBinaryOperationBinder.Get(ExpressionType.LeftShift);
                    return DynamicExpression.Dynamic(binder, typeof(object), lhs, rhs);
                case TokenKind.Shr:
                    binder = PSBinaryOperationBinder.Get(ExpressionType.RightShift);
                    return DynamicExpression.Dynamic(binder, typeof(object), lhs, rhs);
                case TokenKind.Band:
                    binder = PSBinaryOperationBinder.Get(ExpressionType.And);
                    return DynamicExpression.Dynamic(binder, typeof(object), lhs, rhs);
                case TokenKind.Bor:
                    binder = PSBinaryOperationBinder.Get(ExpressionType.Or);
                    return DynamicExpression.Dynamic(binder, typeof(object), lhs, rhs);
                case TokenKind.Bxor:
                    binder = PSBinaryOperationBinder.Get(ExpressionType.ExclusiveOr);
                    return DynamicExpression.Dynamic(binder, typeof(object), lhs, rhs);
                case TokenKind.Join:
                    // TODO: replace this with faster code
                    return Expression.Call(
                        CachedReflectionInfo.ParserOps_JoinOperator,
                        _executionContextParameter, Expression.Constant(binaryExpressionAst.ErrorPosition), lhs.Cast(typeof(object)), rhs.Cast(typeof(object)));
                case TokenKind.Ieq:
                    binder = PSBinaryOperationBinder.Get(ExpressionType.Equal);
                    return DynamicExpression.Dynamic(binder, typeof(object), lhs, rhs);
                case TokenKind.Ine:
                    binder = PSBinaryOperationBinder.Get(ExpressionType.NotEqual);
                    return DynamicExpression.Dynamic(binder, typeof(object), lhs, rhs);
                case TokenKind.Ige:
                    binder = PSBinaryOperationBinder.Get(ExpressionType.GreaterThanOrEqual);
                    return DynamicExpression.Dynamic(binder, typeof(object), lhs, rhs);
                case TokenKind.Igt:
                    binder = PSBinaryOperationBinder.Get(ExpressionType.GreaterThan);
                    return DynamicExpression.Dynamic(binder, typeof(object), lhs, rhs);
                case TokenKind.Ilt:
                    binder = PSBinaryOperationBinder.Get(ExpressionType.LessThan);
                    return DynamicExpression.Dynamic(binder, typeof(object), lhs, rhs);
                case TokenKind.Ile:
                    binder = PSBinaryOperationBinder.Get(ExpressionType.LessThanOrEqual);
                    return DynamicExpression.Dynamic(binder, typeof(object), lhs, rhs);
                case TokenKind.Ilike:
                    // TODO: replace this with faster code
                    return Expression.Call(
                        CachedReflectionInfo.ParserOps_LikeOperator,
                        _executionContextParameter, Expression.Constant(binaryExpressionAst.ErrorPosition),
                        lhs.Cast(typeof(object)),
                        GetLikeRHSOperand(WildcardOptions.IgnoreCase, rhs).Cast(typeof(object)),
                        Expression.Constant(binaryExpressionAst.Operator));
                case TokenKind.Inotlike:
                    // TODO: replace this with faster code
                    return Expression.Call(
                        CachedReflectionInfo.ParserOps_LikeOperator,
                        _executionContextParameter, Expression.Constant(binaryExpressionAst.ErrorPosition),
                        lhs.Cast(typeof(object)),
                        GetLikeRHSOperand(WildcardOptions.IgnoreCase, rhs).Cast(typeof(object)),
                        Expression.Constant(binaryExpressionAst.Operator));
                case TokenKind.Imatch:
                    // TODO: replace this with faster code
                    return Expression.Call(
                        CachedReflectionInfo.ParserOps_MatchOperator,
                        _executionContextParameter, Expression.Constant(binaryExpressionAst.ErrorPosition), lhs.Cast(typeof(object)), rhs.Cast(typeof(object)),
                        ExpressionCache.Constant(false), ExpressionCache.Constant(true));
                case TokenKind.Inotmatch:
                    // TODO: replace this with faster code
                    return Expression.Call(
                        CachedReflectionInfo.ParserOps_MatchOperator,
                        _executionContextParameter, Expression.Constant(binaryExpressionAst.ErrorPosition), lhs.Cast(typeof(object)), rhs.Cast(typeof(object)),
                        ExpressionCache.Constant(true), ExpressionCache.Constant(true));
                case TokenKind.Ireplace:
                    // TODO: replace this with faster code
                    return Expression.Call(
                        CachedReflectionInfo.ParserOps_ReplaceOperator,
                        _executionContextParameter, Expression.Constant(binaryExpressionAst.ErrorPosition), lhs.Cast(typeof(object)), rhs.Cast(typeof(object)),
                        ExpressionCache.Constant(true));
                case TokenKind.Icontains:
                    return GenerateCallContains(lhs, rhs, true);
                case TokenKind.Inotcontains:
                    return Expression.Not(GenerateCallContains(lhs, rhs, true));
                case TokenKind.Iin:
                    return GenerateCallContains(rhs, lhs, true);
                case TokenKind.Inotin:
                    return Expression.Not(GenerateCallContains(rhs, lhs, true));
                case TokenKind.Isplit:
                    // TODO: replace this with faster code
                    return Expression.Call(
                        CachedReflectionInfo.ParserOps_SplitOperator,
                        _executionContextParameter, Expression.Constant(binaryExpressionAst.ErrorPosition), lhs.Cast(typeof(object)), rhs.Cast(typeof(object)),
                        ExpressionCache.Constant(true));
                case TokenKind.Ceq:
                    binder = PSBinaryOperationBinder.Get(ExpressionType.Equal, false);
                    return DynamicExpression.Dynamic(binder, typeof(object), lhs, rhs);
                case TokenKind.Cne:
                    binder = PSBinaryOperationBinder.Get(ExpressionType.NotEqual, false);
                    return DynamicExpression.Dynamic(binder, typeof(object), lhs, rhs);
                case TokenKind.Cge:
                    binder = PSBinaryOperationBinder.Get(ExpressionType.GreaterThanOrEqual, false);
                    return DynamicExpression.Dynamic(binder, typeof(object), lhs, rhs);
                case TokenKind.Cgt:
                    binder = PSBinaryOperationBinder.Get(ExpressionType.GreaterThan, false);
                    return DynamicExpression.Dynamic(binder, typeof(object), lhs, rhs);
                case TokenKind.Clt:
                    binder = PSBinaryOperationBinder.Get(ExpressionType.LessThan, false);
                    return DynamicExpression.Dynamic(binder, typeof(object), lhs, rhs);
                case TokenKind.Cle:
                    binder = PSBinaryOperationBinder.Get(ExpressionType.LessThanOrEqual, false);
                    return DynamicExpression.Dynamic(binder, typeof(object), lhs, rhs);
                case TokenKind.Clike:
                    // TODO: replace this with faster code
                    return Expression.Call(
                        CachedReflectionInfo.ParserOps_LikeOperator,
                        _executionContextParameter, Expression.Constant(binaryExpressionAst.ErrorPosition),
                        lhs.Cast(typeof(object)),
                        GetLikeRHSOperand(WildcardOptions.None, rhs).Cast(typeof(object)),
                        Expression.Constant(binaryExpressionAst.Operator));
                case TokenKind.Cnotlike:
                    // TODO: replace this with faster code
                    return Expression.Call(
                        CachedReflectionInfo.ParserOps_LikeOperator,
                        _executionContextParameter, Expression.Constant(binaryExpressionAst.ErrorPosition),
                        lhs.Cast(typeof(object)),
                        GetLikeRHSOperand(WildcardOptions.None, rhs).Cast(typeof(object)),
                        Expression.Constant(binaryExpressionAst.Operator));
                case TokenKind.Cmatch:
                    // TODO: replace this with faster code
                    return Expression.Call(
                        CachedReflectionInfo.ParserOps_MatchOperator,
                        _executionContextParameter, Expression.Constant(binaryExpressionAst.ErrorPosition), lhs.Cast(typeof(object)), rhs.Cast(typeof(object)),
                        ExpressionCache.Constant(false), ExpressionCache.Constant(false));
                case TokenKind.Cnotmatch:
                    // TODO: replace this with faster code
                    return Expression.Call(
                        CachedReflectionInfo.ParserOps_MatchOperator,
                        _executionContextParameter, Expression.Constant(binaryExpressionAst.ErrorPosition), lhs.Cast(typeof(object)), rhs.Cast(typeof(object)),
                        ExpressionCache.Constant(true), ExpressionCache.Constant(false));
                case TokenKind.Creplace:
                    // TODO: replace this with faster code
                    return Expression.Call(
                        CachedReflectionInfo.ParserOps_ReplaceOperator,
                        _executionContextParameter, Expression.Constant(binaryExpressionAst.ErrorPosition), lhs.Cast(typeof(object)), rhs.Cast(typeof(object)),
                        ExpressionCache.Constant(false));
                case TokenKind.Ccontains:
                    return GenerateCallContains(lhs, rhs, false);
                case TokenKind.Cnotcontains:
                    return Expression.Not(GenerateCallContains(lhs, rhs, false));
                case TokenKind.Cin:
                    return GenerateCallContains(rhs, lhs, false);
                case TokenKind.Cnotin:
                    return Expression.Not(GenerateCallContains(rhs, lhs, false));
                case TokenKind.Csplit:
                    // TODO: replace this with faster code
                    return Expression.Call(
                        CachedReflectionInfo.ParserOps_SplitOperator,
                        _executionContextParameter, Expression.Constant(binaryExpressionAst.ErrorPosition), lhs.Cast(typeof(object)), rhs.Cast(typeof(object)),
                        ExpressionCache.Constant(false));
            }

            throw new InvalidOperationException("Unknown token in binary operator.");
        }

        private static Expression GetLikeRHSOperand(WildcardOptions options, Expression expr)
        {
            var constExpr = expr as ConstantExpression;
            if (constExpr == null)
                return expr;

            var val = constExpr.Value as string;
            if (val == null)
                return expr;

            return Expression.Constant(WildcardPattern.Get(val, options));
        }

        public object VisitUnaryExpression(UnaryExpressionAst unaryExpressionAst)
        {
            object constantValue;
            if (!CompilingConstantExpression && IsConstantValueVisitor.IsConstant(unaryExpressionAst, out constantValue))
            {
                return Expression.Constant(constantValue);
            }

            ExpressionAst child = unaryExpressionAst.Child;
            switch (unaryExpressionAst.TokenKind)
            {
                case TokenKind.Exclaim:
                case TokenKind.Not:
                    return DynamicExpression.Dynamic(PSUnaryOperationBinder.Get(ExpressionType.Not), typeof(object), CompileExpressionOperand(child));
                case TokenKind.Minus:
                    return DynamicExpression.Dynamic(PSBinaryOperationBinder.Get(ExpressionType.Subtract),
                                                     typeof(object), ExpressionCache.Constant(0), CompileExpressionOperand(child));
                case TokenKind.Plus:
                    return DynamicExpression.Dynamic(PSBinaryOperationBinder.Get(ExpressionType.Add),
                                                     typeof(object), ExpressionCache.Constant(0), CompileExpressionOperand(child));
                case TokenKind.Bnot:
                    return DynamicExpression.Dynamic(PSUnaryOperationBinder.Get(ExpressionType.OnesComplement),
                                                     typeof(object), CompileExpressionOperand(child));
                case TokenKind.PlusPlus:
                    return CompileIncrementOrDecrement(child, 1, true);
                case TokenKind.MinusMinus:
                    return CompileIncrementOrDecrement(child, -1, true);
                case TokenKind.PostfixPlusPlus:
                    return CompileIncrementOrDecrement(child, 1, false);
                case TokenKind.PostfixMinusMinus:
                    return CompileIncrementOrDecrement(child, -1, false);
                case TokenKind.Join:
                    // TODO: replace this with faster code
                    return Expression.Call(CachedReflectionInfo.ParserOps_UnaryJoinOperator,
                                           _executionContextParameter,
                                           Expression.Constant(unaryExpressionAst.Extent),
                                           (CompileExpressionOperand(child)).Cast(typeof(object)));
                case TokenKind.Isplit:
                case TokenKind.Csplit:
                    // TODO: replace this with faster code
                    return Expression.Call(CachedReflectionInfo.ParserOps_UnarySplitOperator,
                                           _executionContextParameter,
                                           Expression.Constant(unaryExpressionAst.Extent),
                                           (CompileExpressionOperand(child)).Cast(typeof(object)));
            }

            throw new InvalidOperationException("Unknown token in unary operator.");
        }

        private Expression CompileIncrementOrDecrement(ExpressionAst exprAst, int valueToAdd, bool prefix)
        {
            var av = ((ISupportsAssignment)exprAst).GetAssignableValue();
            List<ParameterExpression> temps = new List<ParameterExpression>();
            List<Expression> exprs = new List<Expression>();
            ParameterExpression tmp;
            Expression beforeVal = av.GetValue(this, exprs, temps);
            if (prefix)
            {
                var newValue = DynamicExpression.Dynamic(PSUnaryOperationBinder.Get(valueToAdd == 1 ? ExpressionType.Increment : ExpressionType.Decrement),
                                                         typeof(object), beforeVal);
                tmp = Expression.Parameter(newValue.Type);
                exprs.Add(Expression.Assign(tmp, newValue));
                exprs.Add(av.SetValue(this, tmp));
                exprs.Add(tmp);
            }
            else
            {
                tmp = Expression.Parameter(beforeVal.Type);
                exprs.Add(Expression.Assign(tmp, beforeVal));
                var newValue = DynamicExpression.Dynamic(PSUnaryOperationBinder.Get(valueToAdd == 1 ? ExpressionType.Increment : ExpressionType.Decrement),
                                                         typeof(object), tmp);
                exprs.Add(av.SetValue(this, newValue));
                if (tmp.Type.GetTypeInfo().IsValueType)
                {
                    // This is the result of the expression - it might be unused, but we don't bother knowing if it is used or not.
                    exprs.Add(tmp);
                }
                else
                {
                    // For backwards compatibility, return 0 when the pre-incremented value was null.  We do the check after
                    // the increment because this value isn't always used, so it might be removed as dead code, and we can
                    // thus avoid adding an extra if test in some common cases (e.g. a for loop frequently uses ++ as the iteration
                    // expression.
                    exprs.Add(Expression.Condition(Expression.Equal(tmp, ExpressionCache.NullConstant),
                                                   ExpressionCache.Constant(0).Cast(typeof(object)), tmp));
                }
            }
            temps.Add(tmp);
            return Expression.Block(temps, exprs);
        }

        public object VisitConvertExpression(ConvertExpressionAst convertExpressionAst)
        {
            object constantValue;
            if (!CompilingConstantExpression && IsConstantValueVisitor.IsConstant(convertExpressionAst, out constantValue))
            {
                return Expression.Constant(constantValue);
            }

            var typeName = convertExpressionAst.Type.TypeName;
            var hashTableAst = convertExpressionAst.Child as HashtableAst;
            Expression childExpr = null;
            if (hashTableAst != null)
            {
                var temp = NewTemp(typeof(OrderedDictionary), "orderedDictionary");
                if (typeName.FullName.Equals(LanguagePrimitives.OrderedAttribute, StringComparison.OrdinalIgnoreCase))
                {
                    return Expression.Block(typeof(OrderedDictionary),
                        new[] { temp },
                        BuildHashtable(hashTableAst.KeyValuePairs, temp, ordered: true));
                }
                if (typeName.FullName.Equals("PSCustomObject", StringComparison.OrdinalIgnoreCase))
                {
                    // pure laziness here - we should construct the PSObject directly.  Instead, we're relying on the conversion
                    // to create the PSObject from an OrderedDictionary.
                    childExpr = Expression.Block(typeof(OrderedDictionary),
                        new[] { temp },
                        BuildHashtable(hashTableAst.KeyValuePairs, temp, ordered: true));
                }
            }

            if (convertExpressionAst.IsRef())
            {
                var varExpr = convertExpressionAst.Child as VariableExpressionAst;
                if (varExpr != null && varExpr.VariablePath.IsVariable && !varExpr.IsConstantVariable())
                {
                    // We'll wrap the variable in a PSReference, but not the constant variables ($true, $false, $null) because those
                    // can't be changed.
                    IEnumerable<PropertyInfo> unused1;
                    bool unused2;
                    var varType = varExpr.GetVariableType(this, out unused1, out unused2);
                    return Expression.Call(CachedReflectionInfo.VariableOps_GetVariableAsRef,
                                           Expression.Constant(varExpr.VariablePath), _executionContextParameter,
                                           varType != null && varType != typeof(object)
                                               ? Expression.Constant(varType, typeof(Type))
                                               : ExpressionCache.NullType);
                }
            }

            if (childExpr == null)
            {
                childExpr = Compile(convertExpressionAst.Child);
            }

            if (typeName.FullName.Equals("PSCustomObject", StringComparison.OrdinalIgnoreCase))
            {
                // We can't use the normal PSConvertBinder because it is strongly typed, and we
                // play some funny games with the PSCustomObject type (externally, it's just an
                // alias for PSObject, internally we do other stuff with it.)
                return DynamicExpression.Dynamic(PSCustomObjectConverter.Get(), typeof(object), childExpr);
            }

            return ConvertValue(convertExpressionAst.Type, childExpr);
        }

        public object VisitConstantExpression(ConstantExpressionAst constantExpressionAst)
        {
            return Expression.Constant(constantExpressionAst.Value);
        }

        public object VisitStringConstantExpression(StringConstantExpressionAst stringConstantExpressionAst)
        {
            return Expression.Constant(stringConstantExpressionAst.Value);
        }

        public object VisitSubExpression(SubExpressionAst subExpressionAst)
        {
            if (subExpressionAst.SubExpression.Statements.Count == 0)
            {
                // This seems wrong, but is compatible with V2.
                return ExpressionCache.NullConstant;
            }

            // SubExpression and ParenExpression are two special cases for handling the partial output while exception
            // is thrown. For example, the output of $(1; throw 2) should be 1 and the error record with message '2';
            // but the output of $(1; throw 2).Length should just be the error record with message '2'. 
            bool shouldPreserveResultInCaseofException = subExpressionAst.ShouldPreserveOutputInCaseOfException();
            return CaptureAstResults(subExpressionAst.SubExpression,
                                     shouldPreserveResultInCaseofException
                                        ? CaptureAstContext.AssignmentWithResultPreservation
                                        : CaptureAstContext.AssignmentWithoutResultPreservation);
        }

        public object VisitUsingExpression(UsingExpressionAst usingExpression)
        {
            string usingExprKey = PsUtils.GetUsingExpressionKey(usingExpression);
            return Expression.Call(CachedReflectionInfo.VariableOps_GetUsingValue, LocalVariablesParameter,
                                   Expression.Constant(usingExprKey),
                                   ExpressionCache.Constant(usingExpression.RuntimeUsingIndex),
                                   _executionContextParameter);
        }

        public object VisitVariableExpression(VariableExpressionAst variableExpressionAst)
        {
            var varPath = variableExpressionAst.VariablePath;
            if (varPath.IsVariable)
            {
                // Generate constants for variables that really are constant.
                if (varPath.UnqualifiedPath.Equals(SpecialVariables.Null, StringComparison.OrdinalIgnoreCase))
                {
                    return ExpressionCache.NullConstant;
                }
                if (varPath.UnqualifiedPath.Equals(SpecialVariables.True, StringComparison.OrdinalIgnoreCase))
                {
                    return ExpressionCache.Constant(true);
                }
                if (varPath.UnqualifiedPath.Equals(SpecialVariables.False, StringComparison.OrdinalIgnoreCase))
                {
                    return ExpressionCache.Constant(false);
                }
            }

            int tupleIndex = variableExpressionAst.TupleIndex;
            if (variableExpressionAst.Automatic)
            {
                if (variableExpressionAst.VariablePath.UnqualifiedPath.Equals(SpecialVariables.Question, StringComparison.OrdinalIgnoreCase))
                {
                    if (Optimize)
                    {
                        return Expression.Property(_executionContextParameter,
                                                   CachedReflectionInfo.ExecutionContext_QuestionMarkVariableValue);
                    }

                    // Unoptimized - need to check for breakpoints, so just get the variable, etc.
                    return CallGetVariable(Expression.Constant(variableExpressionAst.VariablePath), variableExpressionAst);
                }

                return GetAutomaticVariable(variableExpressionAst);
            }

            if (tupleIndex < 0)
            {
                return CallGetVariable(Expression.Constant(variableExpressionAst.VariablePath), variableExpressionAst);
            }
            return GetLocal(tupleIndex);
        }

        internal Expression CompileTypeName(ITypeName typeName, IScriptExtent errorPos)
        {
            Type type;
            try
            {
                // If creating the type throws an exception, just defer that error until runtime.
                type = typeName.GetReflectionType();
            }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                type = null;
            }

            if (type != null)
            {
                return Expression.Constant(type, typeof(Type));
            }

            return Expression.Call(CachedReflectionInfo.TypeOps_ResolveTypeName,
                                   Expression.Constant(typeName),
                                   Expression.Constant(errorPos));
        }

        public object VisitTypeExpression(TypeExpressionAst typeExpressionAst)
        {
            return CompileTypeName(typeExpressionAst.TypeName, typeExpressionAst.Extent);
        }

        public object VisitMemberExpression(MemberExpressionAst memberExpressionAst)
        {
            // Getting a static member is much simpler because we can ignore instance
            // and type table members and we only have one adapter - .Net.  So we'll
            // avoid a dynamic expression if possible.
            if (memberExpressionAst.Static && (memberExpressionAst.Expression is TypeExpressionAst))
            {
                var type = ((TypeExpressionAst)memberExpressionAst.Expression).TypeName.GetReflectionType();
                if (type != null && !type.GetTypeInfo().IsGenericTypeDefinition)
                {
                    var member = memberExpressionAst.Member as StringConstantExpressionAst;
                    if (member != null)
                    {
                        // We skip Methods because the adapter wraps them in a PSMethod and it's not a common scenario.
                        var memberInfo = type.GetMember(member.Value, MemberTypes.Field | MemberTypes.Property,
                            BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Static |
                            BindingFlags.FlattenHierarchy);
                        if (memberInfo.Length == 1)
                        {
                            var propertyInfo = memberInfo[0] as PropertyInfo;
                            if (propertyInfo != null)
                            {
                                if (propertyInfo.CanRead)
                                {
                                    return Expression.Property(null, propertyInfo);
                                }
                                // else let the dynamic site generate the error - this is rare anyway
                            }
                            else
                            {
                                return Expression.Field(null, (FieldInfo)memberInfo[0]);
                            }
                        }
                    }
                }
            }

            var target = CompileExpressionOperand(memberExpressionAst.Expression);
            var memberNameAst = memberExpressionAst.Member as StringConstantExpressionAst;
            if (memberNameAst != null)
            {
                string name = memberNameAst.Value;
                return DynamicExpression.Dynamic(PSGetMemberBinder.Get(name, _memberFunctionType, memberExpressionAst.Static), typeof(object), target);
            }

            var memberNameExpr = Compile(memberExpressionAst.Member);
            return DynamicExpression.Dynamic(PSGetDynamicMemberBinder.Get(_memberFunctionType, memberExpressionAst.Static), typeof(object), target, memberNameExpr);
        }

        internal static PSMethodInvocationConstraints GetInvokeMemberConstraints(InvokeMemberExpressionAst invokeMemberExpressionAst)
        {
            var arguments = invokeMemberExpressionAst.Arguments;
            var targetTypeConstraint = GetTypeConstraintForMethodResolution(invokeMemberExpressionAst.Expression);
            return CombineTypeConstraintForMethodResolution(targetTypeConstraint,
                arguments != null ? arguments.Select(Compiler.GetTypeConstraintForMethodResolution).ToArray() : null);
        }

        internal static PSMethodInvocationConstraints GetInvokeMemberConstraints(BaseCtorInvokeMemberExpressionAst invokeMemberExpressionAst)
        {
            Type targetTypeConstraint = null;
            var arguments = invokeMemberExpressionAst.Arguments;
            TypeDefinitionAst typeDefinitionAst = Ast.GetAncestorTypeDefinitionAst(invokeMemberExpressionAst);
            if (typeDefinitionAst != null)
            {
                targetTypeConstraint = (typeDefinitionAst as TypeDefinitionAst).Type.GetTypeInfo().BaseType;
            }
            else
            {
                Diagnostics.Assert(false, "BaseCtorInvokeMemberExpressionAst must be used only inside TypeDefinitionAst");
            }

            return CombineTypeConstraintForMethodResolution(targetTypeConstraint,
                arguments != null ? arguments.Select(Compiler.GetTypeConstraintForMethodResolution).ToArray() : null);
        }

        internal Expression InvokeMember(string name,
                                         PSMethodInvocationConstraints constraints,
                                         Expression target,
                                         IEnumerable<Expression> args,
                                         bool @static,
                                         bool propertySet)
        {
            var callInfo = new CallInfo(args.Count());
            var classScope = _memberFunctionType != null ? _memberFunctionType.Type : null;
            var binder = name.Equals("new", StringComparison.OrdinalIgnoreCase) && @static
                ? (CallSiteBinder)PSCreateInstanceBinder.Get(callInfo, constraints, publicTypeOnly: true)
                : PSInvokeMemberBinder.Get(name, callInfo, @static, propertySet, constraints, classScope);
            return DynamicExpression.Dynamic(binder, typeof(object), args.Prepend(target));
        }

        private Expression InvokeBaseCtorMethod(PSMethodInvocationConstraints constraints, Expression target, IEnumerable<Expression> args)
        {
            var callInfo = new CallInfo(args.Count());
            var binder = PSInvokeBaseCtorBinder.Get(callInfo, constraints);
            return DynamicExpression.Dynamic(binder, typeof(object), args.Prepend(target));
        }

        internal Expression InvokeDynamicMember(Expression memberNameExpr,
                                                       PSMethodInvocationConstraints constraints,
                                                       Expression target,
                                                       IEnumerable<Expression> args,
                                                       bool @static,
                                                       bool propertySet)
        {
            var binder = PSInvokeDynamicMemberBinder.Get(new CallInfo(args.Count()), _memberFunctionType, @static, propertySet, constraints);
            return DynamicExpression.Dynamic(binder, typeof(object), args.Prepend(memberNameExpr).Prepend(target));
        }

        public object VisitInvokeMemberExpression(InvokeMemberExpressionAst invokeMemberExpressionAst)
        {
            var constraints = GetInvokeMemberConstraints(invokeMemberExpressionAst);

            var target = CompileExpressionOperand(invokeMemberExpressionAst.Expression);
            var args = CompileInvocationArguments(invokeMemberExpressionAst.Arguments);

            var memberNameAst = invokeMemberExpressionAst.Member as StringConstantExpressionAst;
            if (memberNameAst != null)
            {
                return InvokeMember(memberNameAst.Value, constraints, target,
                                    args, invokeMemberExpressionAst.Static, false);
            }

            var memberNameExpr = Compile(invokeMemberExpressionAst.Member);
            return InvokeDynamicMember(memberNameExpr, constraints, target,
                                       args, invokeMemberExpressionAst.Static, false);
        }

        public object VisitArrayExpression(ArrayExpressionAst arrayExpressionAst)
        {
            Expression values = null;
            var subExpr = arrayExpressionAst.SubExpression;
            if (subExpr.Traps == null)
            {
                if (subExpr.Statements.Count == 1)
                {
                    var pipelineBase = subExpr.Statements[0] as PipelineBaseAst;
                    if (pipelineBase != null)
                    {
                        var exprAst = pipelineBase.GetPureExpression();
                        if (exprAst != null)
                        {
                            values = Compile(exprAst);
                        }
                    }
                }
                else if (subExpr.Statements.Count == 0)
                {
                    // A dynamic site can't take void - but a void value is just an empty array.
                    return Expression.NewArrayInit(typeof(object));
                }
            }
            values = values ?? CaptureAstResults(subExpr, CaptureAstContext.Enumerable);

            if (values.Type.IsArray)
            {
                // If the result is already an array, don't wrap the array.
                return values;
            }
            if (values.Type == typeof(List<object>))
            {
                return Expression.Call(values, CachedReflectionInfo.ObjectList_ToArray);
            }
            if (values.Type.GetTypeInfo().IsPrimitive || values.Type == typeof(string))
            {
                // Slight optimization - no need for a dynamic site.  We could special case other
                // types as well, but it's probably not worth it.
                return Expression.NewArrayInit(typeof(object), values.Cast(typeof(object)));
            }
            if (values.Type == typeof(void))
            {
                // A dynamic site can't take void - but a void value is just an empty array.
                return Expression.NewArrayInit(typeof(object));
            }

            return DynamicExpression.Dynamic(PSToObjectArrayBinder.Get(), typeof(object[]), values);
        }

        public object VisitArrayLiteral(ArrayLiteralAst arrayLiteralAst)
        {
            return Expression.NewArrayInit(typeof(object),
                                           arrayLiteralAst.Elements.Select(elem => Compile(elem).Cast(typeof(object))));
        }

        private IEnumerable<Expression> BuildHashtable(ReadOnlyCollection<KeyValuePair> keyValuePairs, ParameterExpression temp, bool ordered)
        {
            yield return Expression.Assign(temp,
                Expression.New(ordered ? CachedReflectionInfo.OrderedDictionary_ctor : CachedReflectionInfo.Hashtable_ctor,
                                ExpressionCache.Constant(keyValuePairs.Count),
                                ExpressionCache.CurrentCultureIgnoreCaseComparer.Cast(typeof(IEqualityComparer))));
            for (int index = 0; index < keyValuePairs.Count; index++)
            {
                var keyValuePair = keyValuePairs[index];
                Expression key = Expression.Convert(Compile(keyValuePair.Item1), typeof(object));
                // We should not preserve the partial output if exception is thrown when evaluating the value.
                Expression value =
                    Expression.Convert(
                        CaptureStatementResults(keyValuePair.Item2,
                                                CaptureAstContext.AssignmentWithoutResultPreservation), typeof(object));
                Expression errorExtent = Expression.Constant(keyValuePair.Item1.Extent);
                yield return
                    Expression.Call(CachedReflectionInfo.HashtableOps_AddKeyValuePair,
                                    temp, key, value, errorExtent);
            }
            yield return temp;
        }

        public object VisitHashtable(HashtableAst hashtableAst)
        {
            var temp = NewTemp(typeof(Hashtable), "hashtable");
            return Expression.Block(typeof(Hashtable),
                new[] { temp },
                BuildHashtable(hashtableAst.KeyValuePairs, temp, ordered: false));
        }

        public object VisitScriptBlockExpression(ScriptBlockExpressionAst scriptBlockExpressionAst)
        {
            // The script block is not visited until it is executed, executing a script block expression node simply
            // builds a new script block without analyzing it.
            //
            // A wrapper is returned so we can cache the script block and return clones that are pre-compiled if
            // the expression is evaluated more than once (e.g. in a loop, or if the containing function/script
            // is called again.)
            return Expression.Call(
                Expression.Constant(new ScriptBlockExpressionWrapper(scriptBlockExpressionAst.ScriptBlock)),
                CachedReflectionInfo.ScriptBlockExpressionWrapper_GetScriptBlock,
                _executionContextParameter,
                ExpressionCache.Constant(false)
                );
        }

        public object VisitParenExpression(ParenExpressionAst parenExpressionAst)
        {
            var pipe = parenExpressionAst.Pipeline;
            var assignmentStatementAst = pipe as AssignmentStatementAst;

            if (assignmentStatementAst != null)
            {
                return CompileAssignment(assignmentStatementAst);
            }

            // SubExpression and ParenExpression are two special cases for handling the partial output while exception
            // is thrown. For example, function bar { 1; throw 2 }, the output of (bar) should be 1 and the error record with message '2';
            // but the output of (bar).Length should just be the error record with message '2'. 
            bool shouldPreserveOutputInCaseOfException = parenExpressionAst.ShouldPreserveOutputInCaseOfException();
            return CaptureStatementResults(pipe,
                                           shouldPreserveOutputInCaseOfException
                                               ? CaptureAstContext.AssignmentWithResultPreservation
                                               : CaptureAstContext.AssignmentWithoutResultPreservation);
        }

        public object VisitExpandableStringExpression(ExpandableStringExpressionAst expandableStringExpressionAst)
        {
            var left = Expression.Constant(expandableStringExpressionAst.FormatExpression);
            var nestedAsts = expandableStringExpressionAst.NestedExpressions;
            var toStringBinder = PSToStringBinder.Get();
            var right = Expression.NewArrayInit(typeof(string),
                nestedAsts.Select(
                    e => DynamicExpression.Dynamic(toStringBinder, typeof(string), Compile(e), _executionContextParameter)));
            return Expression.Call(CachedReflectionInfo.StringOps_FormatOperator,
                                   left, right);
        }

        public object VisitIndexExpression(IndexExpressionAst indexExpressionAst)
        {
            var targetExpr = CompileExpressionOperand(indexExpressionAst.Target);

            var index = indexExpressionAst.Index;
            var arrayLiteral = (index as ArrayLiteralAst);
            var constraints = CombineTypeConstraintForMethodResolution(GetTypeConstraintForMethodResolution(indexExpressionAst.Target),
                                                                       GetTypeConstraintForMethodResolution(index));

            // An array literal is either:
            //    $x[1,2]
            // or
            //    $x[,1]
            // In the former case, the user is requesting an array slice.  In the latter case, they index expression is likely
            // an array (dynamically determined) and they don't want an array slice, they want to use the array as the index
            // expression.
            if (arrayLiteral != null && arrayLiteral.Elements.Count > 1)
            {
                return DynamicExpression.Dynamic(PSGetIndexBinder.Get(arrayLiteral.Elements.Count, constraints), typeof(object),
                    arrayLiteral.Elements.Select(CompileExpressionOperand).Prepend(targetExpr));
            }

            return DynamicExpression.Dynamic(PSGetIndexBinder.Get(1, constraints), typeof(object), targetExpr, CompileExpressionOperand(index));
        }

        public object VisitAttributedExpression(AttributedExpressionAst attributedExpressionAst)
        {
            return attributedExpressionAst.Child.Accept(this);
        }

        public object VisitBlockStatement(BlockStatementAst blockStatementAst)
        {
            return null;
        }

        #endregion Expressions
    }

    internal class MemberAssignableValue : IAssignableValue
    {
        internal MemberExpressionAst MemberExpression { get; set; }

        private Expression CachedTarget { get; set; }
        private Expression CachedPropertyExpr { get; set; }

        private Expression GetTargetExpr(Compiler compiler)
        {
            return compiler.Compile(MemberExpression.Expression);
        }

        private Expression GetPropertyExpr(Compiler compiler)
        {
            return compiler.Compile(MemberExpression.Member);
        }

        public Expression GetValue(Compiler compiler, List<Expression> exprs, List<ParameterExpression> temps)
        {
            var target = GetTargetExpr(compiler);
            var targetTemp = Expression.Parameter(target.Type);
            temps.Add(targetTemp);
            CachedTarget = targetTemp;
            exprs.Add(Expression.Assign(targetTemp, target));
            var memberNameAst = MemberExpression.Member as StringConstantExpressionAst;
            if (memberNameAst != null)
            {
                string name = memberNameAst.Value;
                return DynamicExpression.Dynamic(PSGetMemberBinder.Get(name, compiler._memberFunctionType, MemberExpression.Static), typeof(object), targetTemp);
            }

            var propertyExpr = GetPropertyExpr(compiler);
            var propertyNameTemp = Expression.Parameter(propertyExpr.Type);
            temps.Add(propertyNameTemp);
            exprs.Add(Expression.Assign(propertyNameTemp, compiler.Compile(MemberExpression.Member)));
            CachedPropertyExpr = propertyNameTemp;
            return DynamicExpression.Dynamic(PSGetDynamicMemberBinder.Get(compiler._memberFunctionType, MemberExpression.Static), typeof(object), targetTemp, propertyNameTemp);
        }

        public Expression SetValue(Compiler compiler, Expression rhs)
        {
            var memberNameAst = MemberExpression.Member as StringConstantExpressionAst;
            if (memberNameAst != null)
            {
                string name = memberNameAst.Value;
                return DynamicExpression.Dynamic(PSSetMemberBinder.Get(name, compiler._memberFunctionType, MemberExpression.Static),
                                                 typeof(object), CachedTarget ?? GetTargetExpr(compiler), rhs);
            }

            return DynamicExpression.Dynamic(PSSetDynamicMemberBinder.Get(compiler._memberFunctionType, MemberExpression.Static), typeof(object),
                                             CachedTarget ?? GetTargetExpr(compiler),
                                             CachedPropertyExpr ?? GetPropertyExpr(compiler), rhs);
        }
    }

    internal class InvokeMemberAssignableValue : IAssignableValue
    {
        internal InvokeMemberExpressionAst InvokeMemberExpressionAst { get; set; }

        private ParameterExpression _targetExprTemp;
        private ParameterExpression _memberNameExprTemp;
        private IEnumerable<ParameterExpression> _argExprTemps;

        private Expression GetTargetExpr(Compiler compiler)
        {
            return _targetExprTemp ?? compiler.Compile(InvokeMemberExpressionAst.Expression);
        }

        private Expression GetMemberNameExpr(Compiler compiler)
        {
            return _memberNameExprTemp ?? compiler.Compile(InvokeMemberExpressionAst.Member);
        }

        private IEnumerable<Expression> GetArgumentExprs(Compiler compiler)
        {
            if (_argExprTemps != null)
            {
                return _argExprTemps;
            }

            return InvokeMemberExpressionAst.Arguments == null
               ? Utils.EmptyArray<Expression>()
               : (InvokeMemberExpressionAst.Arguments.Select(compiler.Compile)).ToArray();
        }

        public Expression GetValue(Compiler compiler, List<Expression> exprs, List<ParameterExpression> temps)
        {
            var constraints = Compiler.GetInvokeMemberConstraints(InvokeMemberExpressionAst);

            var targetExpr = GetTargetExpr(compiler);
            _targetExprTemp = Expression.Variable(targetExpr.Type);
            exprs.Add(Expression.Assign(_targetExprTemp, targetExpr));
            int exprsIndex = exprs.Count;

            var args = GetArgumentExprs(compiler);
            _argExprTemps = args.Select(arg => Expression.Variable(arg.Type)).ToArray();
            exprs.AddRange(args.Zip(_argExprTemps, (arg, temp) => Expression.Assign(temp, arg)));

            temps.Add(_targetExprTemp);
            int tempsIndex = temps.Count;
            temps.AddRange(_argExprTemps);

            var memberNameAst = InvokeMemberExpressionAst.Member as StringConstantExpressionAst;
            if (memberNameAst != null)
            {
                return compiler.InvokeMember(memberNameAst.Value, constraints, _targetExprTemp, _argExprTemps, @static: false, propertySet: false);
            }

            var memberNameExpr = GetMemberNameExpr(compiler);
            _memberNameExprTemp = Expression.Variable(memberNameExpr.Type);

            exprs.Insert(exprsIndex, Expression.Assign(_memberNameExprTemp, memberNameExpr));
            temps.Insert(tempsIndex, _memberNameExprTemp);

            return compiler.InvokeDynamicMember(_memberNameExprTemp, constraints, _targetExprTemp, _argExprTemps, @static: false, propertySet: false);
        }

        public Expression SetValue(Compiler compiler, Expression rhs)
        {
            var constraints = Compiler.GetInvokeMemberConstraints(InvokeMemberExpressionAst);

            var memberNameAst = InvokeMemberExpressionAst.Member as StringConstantExpressionAst;
            var target = GetTargetExpr(compiler);
            var args = GetArgumentExprs(compiler);
            if (memberNameAst != null)
            {
                return compiler.InvokeMember(memberNameAst.Value, constraints, target, args.Append(rhs), @static: false, propertySet: true);
            }

            var memberNameExpr = GetMemberNameExpr(compiler);
            return compiler.InvokeDynamicMember(memberNameExpr, constraints, target, args.Append(rhs), @static: false, propertySet: true);
        }
    }

    internal class IndexAssignableValue : IAssignableValue
    {
        internal IndexExpressionAst IndexExpressionAst { get; set; }

        private ParameterExpression _targetExprTemp;
        private ParameterExpression _indexExprTemp;

        private PSMethodInvocationConstraints GetInvocationConstraints()
        {
            return Compiler.CombineTypeConstraintForMethodResolution(Compiler.GetTypeConstraintForMethodResolution(IndexExpressionAst.Target),
                                                                     Compiler.GetTypeConstraintForMethodResolution(IndexExpressionAst.Index));
        }

        private Expression GetTargetExpr(Compiler compiler)
        {
            return _targetExprTemp ?? compiler.Compile(IndexExpressionAst.Target);
        }

        private Expression GetIndexExpr(Compiler compiler)
        {
            return _indexExprTemp ?? compiler.Compile(IndexExpressionAst.Index);
        }

        public Expression GetValue(Compiler compiler, List<Expression> exprs, List<ParameterExpression> temps)
        {
            var targetExpr = compiler.Compile(IndexExpressionAst.Target);
            _targetExprTemp = Expression.Variable(targetExpr.Type);
            temps.Add(_targetExprTemp);
            exprs.Add(Expression.Assign(_targetExprTemp, targetExpr));

            var index = IndexExpressionAst.Index;
            var arrayLiteral = (index as ArrayLiteralAst);
            var constraints = GetInvocationConstraints();
            Expression result;
            if (arrayLiteral != null)
            {
                // If assignment to slices were allowed, we'd need to save the elements in temps
                // like we do when doing normal assignment (below).  But it's not allowed, so it
                // doesn't matter.
                result = DynamicExpression.Dynamic(PSGetIndexBinder.Get(arrayLiteral.Elements.Count, constraints),
                                                   typeof(object),
                                                   arrayLiteral.Elements.Select(compiler.Compile).Prepend(_targetExprTemp));
            }
            else
            {
                var indexExpr = compiler.Compile(index);
                _indexExprTemp = Expression.Variable(indexExpr.Type);
                temps.Add(_indexExprTemp);
                exprs.Add(Expression.Assign(_indexExprTemp, indexExpr));
                result = DynamicExpression.Dynamic(PSGetIndexBinder.Get(1, constraints), typeof(object), _targetExprTemp,
                                                   _indexExprTemp);
            }
            return result;
        }

        public Expression SetValue(Compiler compiler, Expression rhs)
        {
            // Save the rhs for multi-assign, i.e. $x = $y[0] = 2
            // This seems wrong though - the value $y[0] should be assigned to $x, not 2.
            // It's not often they would be different values, but if a conversion is involved, they could
            // be.  At any rate, V2 did it this way, so we do as well.
            var temp = Expression.Variable(rhs.Type);

            var index = IndexExpressionAst.Index;
            var arrayLiteral = (index as ArrayLiteralAst);
            var constraints = GetInvocationConstraints();
            var targetExpr = GetTargetExpr(compiler);
            Expression setExpr;
            if (arrayLiteral != null)
            {
                setExpr = DynamicExpression.Dynamic(PSSetIndexBinder.Get(arrayLiteral.Elements.Count, constraints),
                                                    typeof(object),
                                                    arrayLiteral.Elements.Select(compiler.Compile).Prepend(targetExpr).Append(temp));
            }
            else
            {
                setExpr = DynamicExpression.Dynamic(PSSetIndexBinder.Get(1, constraints), typeof(object), targetExpr,
                                                    GetIndexExpr(compiler), temp);
            }
            return Expression.Block(new[] { temp }, Expression.Assign(temp, rhs), setExpr, temp);
        }
    }

    internal class ArrayAssignableValue : IAssignableValue
    {
        internal ArrayLiteralAst ArrayLiteral { get; set; }

        public Expression GetValue(Compiler compiler, List<Expression> exprs, List<ParameterExpression> temps)
        {
            Diagnostics.Assert(false, "Array assignment does not support read/modify/write operators");
            return null;
        }

        public Expression SetValue(Compiler compiler, Expression rhs)
        {
            Diagnostics.Assert(rhs.Type == typeof(IList), "Code generation must ensure we have an IList to assign from.");
            var rhsTemp = Expression.Variable(rhs.Type);

            int count = ArrayLiteral.Elements.Count;
            var exprs = new List<Expression>();
            exprs.Add(Expression.Assign(rhsTemp, rhs));
            for (int i = 0; i < count; ++i)
            {
                Expression indexedRHS = Expression.Call(rhsTemp, CachedReflectionInfo.IList_get_Item, ExpressionCache.Constant(i));
                var lhsElement = ArrayLiteral.Elements[i];
                var nestedArrayLHS = lhsElement as ArrayLiteralAst;
                var parenExpressionAst = lhsElement as ParenExpressionAst;
                if (parenExpressionAst != null)
                {
                    nestedArrayLHS = parenExpressionAst.Pipeline.GetPureExpression() as ArrayLiteralAst;
                }

                if (nestedArrayLHS != null)
                {
                    // Need to turn the rhs into an IList
                    indexedRHS = DynamicExpression.Dynamic(PSArrayAssignmentRHSBinder.Get(nestedArrayLHS.Elements.Count), typeof(IList),
                                                           indexedRHS);
                }

                exprs.Add(compiler.ReduceAssignment((ISupportsAssignment)lhsElement,
                                                    TokenKind.Equals, indexedRHS));
            }
            // Add the temp as the last expression for chained assignment, i.e. $x = $y,$z = 1,2
            exprs.Add(rhsTemp);
            return Expression.Block(new[] { rhsTemp }, exprs);
        }
    }

    internal class PowerShellLoopExpression : Expression, Interpreter.IInstructionProvider
    {
        public override bool CanReduce { get { return true; } }
        public override Type Type { get { return typeof(void); } }
        public override ExpressionType NodeType { get { return ExpressionType.Extension; } }

        private readonly IEnumerable<Expression> _exprs;

        internal PowerShellLoopExpression(IEnumerable<Expression> exprs)
        {
            _exprs = exprs;
        }

        public override Expression Reduce()
        {
            return Expression.Block(_exprs);
        }

        public void AddInstructions(LightCompiler compiler)
        {
            EnterLoopInstruction enterLoop = null;

            compiler.PushLabelBlock(LabelScopeKind.Statement);

            // emit loop body:
            foreach (var expr in _exprs)
            {
                compiler.CompileAsVoid(expr);
                var enterLoopExpression = expr as EnterLoopExpression;
                if (enterLoopExpression != null)
                {
                    enterLoop = enterLoopExpression.EnterLoopInstruction;
                }
            }

            compiler.PopLabelBlock(LabelScopeKind.Statement);

            // If enterLoop is null, we will never JIT compile the loop.
            if (enterLoop != null)
            {
                enterLoop.FinishLoop(compiler.Instructions.Count);
            }
        }
    }

    internal class EnterLoopExpression : Expression, Interpreter.IInstructionProvider
    {
        public override bool CanReduce { get { return true; } }
        public override Type Type { get { return typeof(void); } }
        public override ExpressionType NodeType { get { return ExpressionType.Extension; } }

        public override Expression Reduce()
        {
            return ExpressionCache.Empty;
        }

        internal new PowerShellLoopExpression Loop { get; set; }
        internal EnterLoopInstruction EnterLoopInstruction { get; private set; }
        internal int LoopStatementCount { get; set; }

        public void AddInstructions(LightCompiler compiler)
        {
            // The EnterLoopInstruction is the instruction the interpreter uses to track the number of iterations a loop
            // has executed and the instruction that kicks of the background compilation.
            // If the statement count is too high, JIT takes too much time/resources to compile, so
            // we just skip it.  By not emitting a EnterLoopInstruction, we'll never attempt to JIT the loop body.
            if (LoopStatementCount < 300)
            {
                // Start compilation after 16 iterations of the loop.
                EnterLoopInstruction = new EnterLoopInstruction(Loop, compiler.Locals, 16, compiler.Instructions.Count);
                compiler.Instructions.Emit(EnterLoopInstruction);
            }
        }
    }

    internal class UpdatePositionExpr : Expression, Interpreter.IInstructionProvider
    {
        public override bool CanReduce { get { return true; } }
        public override Type Type { get { return typeof(void); } }
        public override ExpressionType NodeType { get { return ExpressionType.Extension; } }

        private readonly IScriptExtent _extent;
        private readonly SymbolDocumentInfo _debugSymbolDocument;
        private readonly int _sequencePoint;
        private readonly bool _checkBreakpoints;

        public UpdatePositionExpr(IScriptExtent extent, int sequencePoint, SymbolDocumentInfo debugSymbolDocument, bool checkBreakpoints)
        {
            _extent = extent;
            _checkBreakpoints = checkBreakpoints;
            _debugSymbolDocument = debugSymbolDocument;
            _sequencePoint = sequencePoint;
        }

        public override Expression Reduce()
        {
            var exprs = new List<Expression>();
            if (_debugSymbolDocument != null)
            {
                exprs.Add(Expression.DebugInfo(_debugSymbolDocument, _extent.StartLineNumber, _extent.StartColumnNumber, _extent.EndLineNumber, _extent.EndColumnNumber));
            }

            exprs.Add(
                Expression.Assign(
                    Expression.Field(Compiler._functionContext, CachedReflectionInfo.FunctionContext__currentSequencePointIndex),
                    ExpressionCache.Constant(_sequencePoint)));

            if (_checkBreakpoints)
            {
                exprs.Add(
                    Expression.IfThen(
                        Expression.GreaterThan(
                            Expression.Field(Compiler._executionContextParameter, CachedReflectionInfo.ExecutionContext_DebuggingMode),
                            ExpressionCache.Constant(0)),
                        Expression.Call(
                            Expression.Field(Compiler._executionContextParameter, CachedReflectionInfo.ExecutionContext_Debugger),
                            CachedReflectionInfo.Debugger_OnSequencePointHit,
                            Compiler._functionContext)));
            }
            exprs.Add(ExpressionCache.Empty);

            return Expression.Block(exprs);
        }

        public void AddInstructions(LightCompiler compiler)
        {
            compiler.Instructions.Emit(UpdatePositionInstruction.Create(_sequencePoint, _checkBreakpoints));
        }
    }
}
