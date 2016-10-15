/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Apache License, Version 2.0. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the  Apache License, Version 2.0, please send an email to 
 * dlr@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Apache License, Version 2.0.
 *
 * You must not remove this notice, or any other, from this software.
 *
 *
 * ***************************************************************************/

#if !CLR2
using System.Linq.Expressions;
#else
using Microsoft.Scripting.Ast;
#endif

#if CORECLR
// Use stub for SpecialNameAttribute
using Microsoft.PowerShell.CoreClr.Stubs;
#endif
using System.Runtime.CompilerServices;
using System.Threading;

using System.Diagnostics;
using System.Collections.Generic;

namespace System.Management.Automation.Interpreter
{
    /// <summary>
    /// A simple forth-style stack machine for executing Expression trees
    /// without the need to compile to IL and then invoke the JIT.  This trades
    /// off much faster compilation time for a slower execution performance.
    /// For code that is only run a small number of times this can be a 
    /// sweet spot.
    /// 
    /// The core loop in the interpreter is the RunInstructions method.
    /// </summary>
    internal sealed class Interpreter
    {
        internal static readonly object NoValue = new object();
        internal const int RethrowOnReturn = Int32.MaxValue;

        // zero: sync compilation
        // negative: default
        internal readonly int _compilationThreshold;

        internal readonly object[] _objects;
        internal readonly RuntimeLabel[] _labels;

        internal readonly string _name;
        internal readonly DebugInfo[] _debugInfos;

        internal Interpreter(string name, LocalVariables locals, HybridReferenceDictionary<LabelTarget, BranchLabel> labelMapping,
            InstructionArray instructions, DebugInfo[] debugInfos, int compilationThreshold)
        {
            _name = name;
            LocalCount = locals.LocalCount;
            ClosureVariables = locals.ClosureVariables;

            Instructions = instructions;
            _objects = instructions.Objects;
            _labels = instructions.Labels;
            LabelMapping = labelMapping;

            _debugInfos = debugInfos;
            _compilationThreshold = compilationThreshold;
        }

        internal int ClosureSize
        {
            get
            {
                if (ClosureVariables == null)
                {
                    return 0;
                }
                return ClosureVariables.Count;
            }
        }

        internal int LocalCount { get; }

        internal bool CompileSynchronously
        {
            get { return _compilationThreshold <= 1; }
        }

        internal InstructionArray Instructions { get; }

        internal Dictionary<ParameterExpression, LocalVariable> ClosureVariables { get; }

        internal HybridReferenceDictionary<LabelTarget, BranchLabel> LabelMapping { get; }

        /// <summary>
        /// Runs instructions within the given frame.
        /// </summary>
        /// <remarks>
        /// Interpreted stack frames are linked via Parent reference so that each CLR frame of this method corresponds 
        /// to an interpreted stack frame in the chain. It is therefore possible to combine CLR stack traces with 
        /// interpreted stack traces by aligning interpreted frames to the frames of this method.
        /// Each group of subsequent frames of Run method corresponds to a single interpreted frame.
        /// </remarks>
        [SpecialName, MethodImpl(MethodImplOptions.NoInlining)]
        public void Run(InterpretedFrame frame)
        {
            var instructions = Instructions.Instructions;
            int index = frame.InstructionIndex;
            while (index < instructions.Length)
            {
                index += instructions[index].Run(frame);
                frame.InstructionIndex = index;
            }
        }

#if !CORECLR // Thread.Abort and ThreadAbortException are not in CoreCLR.
        /// <summary>
        /// To get to the current AbortReason object on Thread.CurrentThread 
        /// we need to use ExceptionState property of any ThreadAbortException instance.
        /// </summary>
        [ThreadStatic]
        internal static ThreadAbortException AnyAbortException = null;

        /// <summary>
        /// If the target that 'Goto' jumps to is inside the current catch block or the subsequent finally block,
        /// we delay the call to 'Abort' method, because we want to finish the catch/finally blocks
        /// </summary>
        internal static void AbortThreadIfRequested(InterpretedFrame frame, int targetLabelIndex)
        {
            var abortHandler = frame.CurrentAbortHandler;
            var targetInstrIndex = frame.Interpreter._labels[targetLabelIndex].Index;
            if (abortHandler != null &&
                !abortHandler.IsInsideCatchBlock(targetInstrIndex) &&
                !abortHandler.IsInsideFinallyBlock(targetInstrIndex))
            {
                frame.CurrentAbortHandler = null;

                var currentThread = Thread.CurrentThread;
                if ((currentThread.ThreadState & System.Threading.ThreadState.AbortRequested) != 0)
                {
                    Debug.Assert(AnyAbortException != null);

                    // The current abort reason needs to be preserved.
#if SILVERLIGHT
                    currentThread.Abort();
#else
                    currentThread.Abort(AnyAbortException.ExceptionState);
#endif
                }
            }
        }
#else
        /// <summary>
        /// A thread cannot be aborted in CoreCLR, as Thread.Abort() is not available in CoreCLR (neither is ThreadAbortException).
        /// So this method doesn't need to do anything when running with CoreCLR
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void AbortThreadIfRequested(InterpretedFrame frame, int targetLabelIndex) { }
#endif

        internal int ReturnAndRethrowLabelIndex
        {
            get
            {
                // the last label is "return and rethrow" label:
                Debug.Assert(_labels[_labels.Length - 1].Index == RethrowOnReturn);
                return _labels.Length - 1;
            }
        }
    }
}
