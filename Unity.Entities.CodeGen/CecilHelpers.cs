using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.Entities.CodeGeneratedJobForEach;
#if !UNITY_DOTSRUNTIME
using UnityEngine.Scripting;
#endif
using MethodAttributes = Mono.Cecil.MethodAttributes;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace Unity.Entities.CodeGen
{
    static class CecilHelpers
    {
        public static Instruction MakeInstruction(OpCode opcode, object operand)
        {
            if (operand is Instruction[] instructions)
                return Instruction.Create(opcode, instructions);
            switch (operand)
            {
                case null:
                    return Instruction.Create(opcode);
                case FieldReference o:
                    return Instruction.Create(opcode, o);
                case MethodReference o:
                    return Instruction.Create(opcode, o);
                case VariableDefinition o:
                    return Instruction.Create(opcode, o);
                case ParameterDefinition o:
                    return Instruction.Create(opcode, o);
                case GenericInstanceType o:
                    return Instruction.Create(opcode, o);
                case TypeReference o:
                    return Instruction.Create(opcode, o);
                case Mono.Cecil.CallSite o:
                    return Instruction.Create(opcode, o);
                case int o:
                    return Instruction.Create(opcode, o);
                case float o:
                    return Instruction.Create(opcode, o);
                case double o:
                    return Instruction.Create(opcode, o);
                case sbyte o:
                    return Instruction.Create(opcode, o);
                case byte o:
                    return Instruction.Create(opcode, o);
                case long o:
                    return Instruction.Create(opcode, o);
                case uint o:
                    return Instruction.Create(opcode, o);
                case string o:
                    return Instruction.Create(opcode, o);
                case Instruction o:
                    return Instruction.Create(opcode, o);
                default:
                    throw new NotSupportedException("Unknown operand: " + operand.GetType());
            }
        }

        public static SequencePoint FindBestSequencePointFor(MethodDefinition method, Instruction instruction)
        {
            var sequencePoints = method.DebugInformation?.GetSequencePointMapping().Values.OrderBy(s => s.Offset).ToList();
            if (sequencePoints == null || !sequencePoints.Any())
                return null;

            for (int i = 0; i != sequencePoints.Count-1; i++)
            {
                if (sequencePoints[i].Offset < instruction.Offset &&
                    sequencePoints[i + 1].Offset > instruction.Offset)
                    return sequencePoints[i];
            }

            return sequencePoints.FirstOrDefault();
        }

        public class DelegateProducingSequence
        {
            public bool CapturesLocals;
            public MethodDefinition MethodLambdaWasEmittedAs;
            public MethodDefinition OriginalLambdaContainingMethod;
            public Instruction[] Instructions;

            public void RewriteToProduceSingleNullValue()
            {
                if (CapturesLocals)
                    throw new ArgumentException($"Cannot {nameof(RewriteToProduceSingleNullValue)} when {nameof(CapturesLocals)} is true");

                foreach (var i in Instructions)
                    i.MakeNOP();
                Instructions.Last().OpCode = OpCodes.Ldnull;
            }
        }

        public class DelegateProducingPattern
        {
            public Func<Instruction, DelegateProducingPatternInstructionMatchResult>[] InstructionMatchers;
            public bool CapturesLocal;
            public bool CapturesField;

            public enum MatchSide
            {
                Start,
                End
            }

            public DelegateProducingSequence Match(MethodDefinition containingMethod, Instruction i, MatchSide side)
            {
                DelegateProducingSequence MakeResult(Instruction[] instructions)
                {
                    return new DelegateProducingSequence()
                    {
                        Instructions = instructions, CapturesLocals = CapturesLocal,
                        OriginalLambdaContainingMethod = containingMethod,
                        MethodLambdaWasEmittedAs = FindLambdaMethod(instructions)
                    };
                }

                switch (side)
                {
                    case MatchSide.Start:
                    {
                        return IsStartOfSequence(i, InstructionMatchers, out var instructions) ? MakeResult(instructions.ToArray()) : null;
                    }
                    case MatchSide.End:
                    {
                        return IsEndOfSequence(i, InstructionMatchers, out var instructions) ? MakeResult(instructions.ToArray()) : null;
                    }
                    default:
                        throw new ArgumentOutOfRangeException(nameof(side), side, null);
                }
            }

            private MethodDefinition FindLambdaMethod(Instruction[] instructions)
            {
                var instruction = instructions.FirstOrDefault(i => i.OpCode == OpCodes.Ldftn);
                if (instruction == null)
                    throw new ArgumentException("Instruction array did not have ldftn opcode. Instruction array way: "+instructions.Select(i=>i.ToString()).SeparateBy(Environment.NewLine));
                return ((MethodReference) instruction.Operand).Resolve();
            }
        }

        internal enum DelegateProducingPatternInstructionMatchResult
        {
            NoMatch,
            MatchAndContinue,
            MatchAndRepeatThisInstruction,
            TryNextPatternInstruction
        }

        private static DelegateProducingPattern[] s_DelegateProducingPatterns;
        private static DelegateProducingPattern[] GetDelegateProducingPatterns()
        {
            if (s_DelegateProducingPatterns != null)
                return s_DelegateProducingPatterns;

            //roslyn will has a variety of ways it will emit IL code when it converts a lambda expression to a delegate object. This variety depends on several factors:
            //
            //which things the lambda expression captures.  Possible things to capture:
            //- locals
            //- parameters.  when captured, these will be copied into a local, and then the local is captured. going forward, I'll pretend parameter capture doesn't exist, and refer only to locals.
            //- instance fields
            //
            // Example of local capture:
            // https://sharplab.io/#v2:C4LglgNgPgAgTARgLACgYGYAE9MGFMDeqmJ2WMALJgLIAUAlIcaS2AHbCYC2AngDIB7AMYBDCJgC8mdAG5mLEgFEAHgFMhAV2CraDSQD48AtgGcBEVQDoA6gCcw22r0GiI9enJQsAvvOxUVdS0dGAQ4TBFGAl8UbyA==
            // Emitted IL: the expression turns into a method on the displayclass.  debug/release only differs through some NOP's sprinkled in between.
            //
            // Example of field capture:
            // https://sharplab.io/#v2:D4AQTAjAsAUCDMACciDCiDetE8QSwDsAXRAWwE8AxPAUwBsATRAXkXgG5tcFkAWRALIAKAJSYuuXAFEAHjQDGAVyI0holgD40AewIBnbXRoA6AOoAnPCqEVq9BiJGcYkgL4S+iWQuWqQEMEQAQzEMdxhXIA=
            // Emitted IL: lambda expression gets emitted as an instancemethod on the same type.    (we do not support this kind of capture, and give a compile error about it).  debug/release only differs through NOPs
            //
            // Example of local _and_ field capture
            // https://sharplab.io/#v2:D4AQTAjAsAUCDMACciDCiDetE8QSwDsAXRAWwE8AxPAUwBsATRAXkXgG5tcFkAWRALIAKAJSYuuXIRIUAMgHsAxgEM6LRAFZOMSZICiADxqKArkRpDRLAHxp5BAM7y6NAHQB1AE55zQitXomAGoycgUVOhERbUkAXwk+RENjMwsQCDBEZTEMeJhYoA==
            // Emitted IL: emitted like local capture, and the instance of the method's declaring type, gets stored in the displayclass. (we do not support this kind of capture, and give a compile error about it).  debug/release only differs through NOPs
            //
            // Because creating a new delegate is a heap allocation, roslyn tries to be smart, and in situations where it can cache the delegate, it will. The first example of this is the surprisingly simple looking:
            //
            // Example of not capturing anything
            // https://sharplab.io/#v2:CYLg1APgAgTAjAWAFBQMwAJboMLoN7LrqFGYZQAs6AsgBQCU+JppAogB4CmAxgK4AunWg3QBeAHw4A9gDsAzlIA2nAHQB1AE4BLQbQr16AbmboAvicroOPAUKhwY6AIaM85pKaA=
            // Emitted IL: method gets emitted as an instance method on a compiler generated type. the type is a singleton, and the only instance is stored in a static field on the same type.
            // The generated delegate object is cached on a static field in the same type. debug/release only differs through NOP.
            // Sidenote: Roslyn used to emit this simply as a static method, which is a lot simpler, but CoreCLR is faster at invoking instance delegates than static ones, so roslyn changed to this method.
            //
            //
            // Another dimension of "roslyn will choose a different codegen strategy" is wether or not the lambda expression is used more than once. If it's possible that it might be used more than once (like in a forloop)
            // and it wasn't possible to permanently cache the delegate, it will attempt to cache the delegate for this method invocation, so we get 1 allocation, instead of the number of loop iterations.
            //
            // Example of local capture inside a loop
            // https://sharplab.io/#v2:D4AQTAjAsAUCDMACciDCiDetE9wSwDsAXRAWwE8AzPAUwBsATAbm1wWQBZEBZACgEpMrXPmJlydAPYBjAIZ1EAXkTwWMESKzqNOSpIBOiXoRJ5FABiaI8AQkWrrAakf9hO3AFEAHjWkBXIhpeASUAPjRJAgBnSToaADoAdX08QN4KKTk6fn41d0QAXzci7RwQLm9fAKCQCDBEWUEMEoKgA==
            // Emitted IL: like regular local capture, but it will cache the delegate in a field in the displayclass.
            // Sidenote: using a forloop does cause the caching codegen to kick in, but using a while(true) loop does not.
            //
            // the extra attempt at caching only happens for "also captures locals" lambdas. lambdas that only capture fields are never cached.
            //
            // About local functions: While local functions can make the codegen look more complicated, in essence they don't affect the big picture. a local function in a lambda expression has access to every variable the lambda expression has access to
            // this means that "what things the lambda expression captures" gets augmented by whatever its local functions capture, and for the rest normal rules are being followed.
            //
            // Now that we have written an sherlock holmes essay on how roslyn emits code today, what do we do with it. The only scenarios we support is "captures only locals", and "captures nothing".
            // When nothing is captured, we can just NOP out the entire IL sequence that was responsible for making the delegate, as we only need to know what the target method was that our lambda expression ended up at.
            // When locals are captured, we have to do work:
            //   - in all of these scenarios, the lambda expression will be emitted as an instance method on the displayclass.
            //   - in some of these scenarios the delegate will be cached on the displayclass, in other cases not.
            //
            // the second case is just a "check if we stored this already, if yes use that, if not, create it, then store" wrapper around the first case.
            // the first case (and thus also the second case) uses a "load displayclass on the stack, ldftn our executemethod, newobj our delegate type" sequence. We need to replace that with
            // create our own jobstruct, populate its fields from the displayclass. _not_ create the delegate, not try to cache the delegate, and then schedule/run our jobstruct.

            DelegateProducingPatternInstructionMatchResult CheckMatchAndContinue(bool isMatch) =>
                isMatch ? DelegateProducingPatternInstructionMatchResult.MatchAndContinue : DelegateProducingPatternInstructionMatchResult.NoMatch;

            var notCapturingPattern = new DelegateProducingPattern()
            {
                InstructionMatchers = new Func<Instruction, DelegateProducingPatternInstructionMatchResult>[]
                {
                    i => CheckMatchAndContinue(i.OpCode == OpCodes.Ldsfld),
                    i => CheckMatchAndContinue(i.OpCode == OpCodes.Dup),
                    i => CheckMatchAndContinue(i.IsBranch()),
                    i => CheckMatchAndContinue(i.OpCode == OpCodes.Pop),
                    i => CheckMatchAndContinue(i.OpCode == OpCodes.Ldsfld),
                    i => CheckMatchAndContinue(i.OpCode == OpCodes.Ldftn),
                    i => CheckMatchAndContinue(i.OpCode == OpCodes.Newobj),
                    i => CheckMatchAndContinue(i.OpCode == OpCodes.Dup),
                    i => CheckMatchAndContinue(i.OpCode == OpCodes.Stsfld),
                },
                CapturesLocal = false
            };

            var capturingLocal_InvokedOnce = new DelegateProducingPattern()
            {
                InstructionMatchers = new Func<Instruction, DelegateProducingPatternInstructionMatchResult>[]
                {
                    i => CheckMatchAndContinue(i.IsLoadLocal(out _) || i.IsLoadLocalAddress(out _)),
                    i => CheckMatchAndContinue(i.OpCode == OpCodes.Ldftn),
                    i => CheckMatchAndContinue(i.OpCode == OpCodes.Newobj),
                },
                CapturesLocal = true
            };

            var capturingLocal_ExpectedMoreThanOnce = new DelegateProducingPattern()
            {
                InstructionMatchers = new Func<Instruction, DelegateProducingPatternInstructionMatchResult>[]
                {
                    i => CheckMatchAndContinue(i.IsLoadLocal(out _)),
                    i => CheckMatchAndContinue(i.OpCode == OpCodes.Ldfld),
                    i => CheckMatchAndContinue(i.OpCode == OpCodes.Dup),
                    i => CheckMatchAndContinue(i.IsBranch()),
                    i => CheckMatchAndContinue(i.OpCode == OpCodes.Pop),
                    i => CheckMatchAndContinue(i.IsLoadLocal(out _)),
                    i => CheckMatchAndContinue(i.IsLoadLocal(out _)),
                    i => CheckMatchAndContinue(i.OpCode == OpCodes.Ldftn),
                    i => CheckMatchAndContinue(i.OpCode == OpCodes.Newobj),
                    i => CheckMatchAndContinue(i.OpCode == OpCodes.Dup),
                    i => CheckMatchAndContinue(i.IsStoreLocal(out _)),
                    i => CheckMatchAndContinue(i.OpCode == OpCodes.Stfld),
                    i => CheckMatchAndContinue(i.IsLoadLocal(out _)),
                },
                CapturesLocal = true,
            };

            var capturingOnlyFieldPattern = new DelegateProducingPattern()
            {
                InstructionMatchers = new Func<Instruction, DelegateProducingPatternInstructionMatchResult>[]
                {
                    i => CheckMatchAndContinue((i.OpCode == OpCodes.Ldarg_0) || (i.OpCode == OpCodes.Ldarg && ((ParameterDefinition)i.Operand).Index == -1)),
                    i => CheckMatchAndContinue(i.OpCode == OpCodes.Ldftn),
                    i => CheckMatchAndContinue(i.OpCode == OpCodes.Newobj),
                },
                CapturesLocal = false,
                CapturesField = true
            };

            var capturingLocal_MultipleCapturingFromDifferentScopes = new DelegateProducingPattern()
            {
                InstructionMatchers = new Func<Instruction, DelegateProducingPatternInstructionMatchResult>[]
                {
                    i => CheckMatchAndContinue(i.IsLoadLocal(out _) || i.IsLoadLocalAddress(out _)),
                    // Special case were we might have a number if ldfld of DisplayClasses inserted if Roslyn decides to store our lambda method on a nested DisplayClass
                    // https://sharplab.io/#v2:EYLgxg9gTgpgtADwGwBYA0AXEBLANmgExAGoAfAAQCYBGAWAChyBmAAipYGEWBvBl/lgFEAdhmxiYAZyGjx2KSwC8LYTADuMsRMkAKAJQBuPgOP9mbFCwCy+nqYH8AbgEMoLMM4AO1JSyZH6BwdeQKCwlzcPT0pff3sw/hEteUkAOgAxaEFnMAALHR1YADNrLwAVPBgWMVwYNBZsYRYAGQgPXDKIAHVoXAIWXAw1PSUAPjtQhISIgbbnXB9lKOoAqamZ3DncGKWvSlW1/gBfPVSAJQBXYX0A+ISkuSkMrJz8wpgSq3LK6sr6xpaW06PSgfQGQxGinGIUO4Vcs3ai3cXhWdzCJ3OVxuaJYR3sePoBIY5kkGCgFzAGFKngqtR4BJJZIpVNa7WBvX63CJjFYpPJlJYACkIMAGDCBOZyJZLtcRlyGNzJTEHtoxfZzAQYLUAObODBVKUsAAiWpguv170+3zpAFtrXUGk1WfN2aD+oNhgczKxhcAWJkoNk8joTTq9VVNbg9PZxWFyAB2FTqIUi7GTXEKoA
                    i =>
                    {
                        if (i.IsLoadFieldOrLoadFieldAddress() && ((FieldReference) i.Operand).FieldType.IsDisplayClassCandidate())
                            return DelegateProducingPatternInstructionMatchResult.MatchAndRepeatThisInstruction;
                        return DelegateProducingPatternInstructionMatchResult.TryNextPatternInstruction;
                    },
                    i => CheckMatchAndContinue(i.OpCode == OpCodes.Ldftn),
                    i => CheckMatchAndContinue(i.OpCode == OpCodes.Newobj)
                },
                CapturesLocal = true
            };


            s_DelegateProducingPatterns = new[] {notCapturingPattern, capturingLocal_InvokedOnce, capturingLocal_ExpectedMoreThanOnce, capturingOnlyFieldPattern,
                                                 capturingLocal_MultipleCapturingFromDifferentScopes};
            return s_DelegateProducingPatterns;
        }

        internal static bool IsStartOfSequence(Instruction instruction, Func<Instruction, DelegateProducingPatternInstructionMatchResult>[] pattern,
            out List<Instruction> instructions)
        {
            Instruction cursor = instruction;
            instructions = null;

            var results = new List<Instruction>(50);
            int patternIndex = 0;
            bool matchFound = false;
            while(!matchFound)
            {
                if (cursor == null)
                    return false;

                bool moveToNext = true;
                if (cursor.OpCode != OpCodes.Nop)
                {
                    switch (pattern[patternIndex].Invoke(cursor))
                    {
                        case DelegateProducingPatternInstructionMatchResult.NoMatch: return false;
                        case DelegateProducingPatternInstructionMatchResult.MatchAndContinue: patternIndex++; break;
                        case DelegateProducingPatternInstructionMatchResult.MatchAndRepeatThisInstruction: break;
                        case DelegateProducingPatternInstructionMatchResult.TryNextPatternInstruction: patternIndex++; moveToNext = false; break;
                    }

                    if (patternIndex == pattern.Length)
                        matchFound = true;
                }

                if (moveToNext)
                {
                    results.Add(cursor);
                    cursor = cursor.Next;
                }
            }

            instructions = results;
            return true;
        }

        internal static bool IsEndOfSequence(Instruction instruction, Func<Instruction, DelegateProducingPatternInstructionMatchResult>[] pattern,out List<Instruction> instructions)
        {
            Instruction cursor = instruction;
            instructions = null;

            var results = new List<Instruction>(50);
            int patternIndex = pattern.Length-1;
            bool matchFound = false;
            while(!matchFound)
            {
                if (cursor == null)
                    return false;

                bool moveToPrevious = true;
                if (cursor.OpCode != OpCodes.Nop)
                {
                    switch (pattern[patternIndex].Invoke(cursor))
                    {
                        case DelegateProducingPatternInstructionMatchResult.NoMatch: return false;
                        case DelegateProducingPatternInstructionMatchResult.MatchAndContinue: patternIndex--; break;
                        case DelegateProducingPatternInstructionMatchResult.MatchAndRepeatThisInstruction: break;
                        case DelegateProducingPatternInstructionMatchResult.TryNextPatternInstruction: patternIndex--; moveToPrevious = false; break;
                    }

                    if (patternIndex == -1)
                        matchFound = true; //match!
                }

                if (moveToPrevious)
                {
                    results.Add(cursor);
                    cursor = cursor.Previous;
                }
            }

            results.Reverse();
            instructions = results;
            return true;
        }

        public static DelegateProducingSequence MatchesDelegateProducingPattern(MethodDefinition containingMethod, Instruction instruction, DelegateProducingPattern.MatchSide matchSide)
        {
            return GetDelegateProducingPatterns().Select(pattern => pattern.Match(containingMethod, instruction, matchSide)).FirstOrDefault(result => result != null);
        }

        public static IEnumerable<MethodDefinition> FindUsedInstanceMethodsOnSameType(MethodDefinition method, HashSet<string> foundSoFar = null)
        {
            foundSoFar = foundSoFar ?? new HashSet<string>();

            var usedInThisMethod = method.Body.Instructions.Where(i => i.IsInvocation(out _)).Select(i => i.Operand).OfType<MethodReference>().Where(
                mr => mr.DeclaringType.TypeReferenceEquals(method.DeclaringType) && mr.HasThis);

            foreach (var usedMethod in usedInThisMethod)
            {
                if (foundSoFar.Contains(usedMethod.FullName))
                    continue;
                foundSoFar.Add(usedMethod.FullName);

                var usedMethodResolved = usedMethod.Resolve();
                yield return usedMethodResolved;

                foreach (var used in FindUsedInstanceMethodsOnSameType(usedMethodResolved, foundSoFar))
                    yield return used;
            }
        }

        public static void PatchDisplayClassToBeAStruct(TypeDefinition displayClass)
        {
            displayClass.BaseType = displayClass.Module.ImportReference(typeof(ValueType));
            displayClass.IsClass = false;

            //we have to kill the body of the default constructor, as it invokes the base class constructor, which makes no sense for a valuetype
            var constructorDefinition = displayClass.Methods.Single(m => m.IsConstructor);
            constructorDefinition.Body = new MethodBody(constructorDefinition);
            constructorDefinition.Body.GetILProcessor().Emit(OpCodes.Ret);
        }

        public static void CloneMethodForDiagnosingProblems(MethodDefinition methodToAnalyze)
        {
            var cloneName = methodToAnalyze.Name + "_Unmodified";
            if (methodToAnalyze.DeclaringType.Methods.Any(m => m.Name == cloneName))
                return;

            var clonedMethod = new MethodDefinition(cloneName, methodToAnalyze.Attributes, methodToAnalyze.ReturnType);
            foreach (var parameter in methodToAnalyze.Parameters)
                clonedMethod.Parameters.Add(parameter);
            foreach (var v in methodToAnalyze.Body.Variables)
                clonedMethod.Body.Variables.Add(new VariableDefinition(v.VariableType));
            var p = clonedMethod.Body.GetILProcessor();
            var oldToNew = new Dictionary<Instruction, Instruction>();
            foreach (var i in methodToAnalyze.Body.Instructions)
            {
                var newInstruction = CecilHelpers.MakeInstruction(i.OpCode, i.Operand);
                oldToNew.Add(i, newInstruction);
                p.Append(newInstruction);
            }

            foreach (var i in oldToNew.Values)
            {
                if (i.Operand is Instruction operand)
                {
                    if (oldToNew.TryGetValue(operand, out var replacement))
                        i.Operand = replacement;
                }
            }

            methodToAnalyze.DeclaringType.Methods.Add(clonedMethod);
        }

        public static Instruction FindInstructionThatPushedArg(MethodDefinition containingMethod, int argNumber,
            Instruction callInstructionsWhoseArgumentsWeWantToFind, bool breakWhenBranchDetected = false)
        {
            containingMethod.Body.EnsurePreviousAndNextAreSet();

            var cursor = callInstructionsWhoseArgumentsWeWantToFind.Previous;

            int stackSlotWhoseWriteWeAreLookingFor = argNumber;
            int stackSlotWhereNextPushWouldBeWrittenTo = InstructionExtensions.GetPopDelta(callInstructionsWhoseArgumentsWeWantToFind);

            var seenInstructions = new HashSet<Instruction>() {callInstructionsWhoseArgumentsWeWantToFind, cursor};

            while (cursor != null)
            {
                var pushAmount = cursor.GetPushDelta();
                var popAmount = cursor.GetPopDelta();

                var result = CecilHelpers.MatchesDelegateProducingPattern(containingMethod, cursor, CecilHelpers.DelegateProducingPattern.MatchSide.End);
                if (result != null)
                {
                    //so we are crawling backwards through instructions.  if we find a "this is roslyn caching a delegate" sequence,
                    //we're going to pretend it is a single instruction, that pushes the delegate on the stack, and pops nothing.
                    cursor = result.Instructions.First();
                    pushAmount = 1;
                    popAmount = 0;
                }
                else if (cursor.IsBranch())
                {
                    if (breakWhenBranchDetected)
                        return null;
                    var target = (Instruction) cursor.Operand;
                    if (!seenInstructions.Contains(target))
                    {
                        if (IsUnsupportedBranch(cursor))
                            UserError.DC0010(containingMethod, cursor).Throw();
                    }
                }

                for (int i = 0; i != pushAmount; i++)
                {
                    stackSlotWhereNextPushWouldBeWrittenTo--;
                    if (stackSlotWhereNextPushWouldBeWrittenTo == stackSlotWhoseWriteWeAreLookingFor)
                        return cursor;
                }

                for (int i = 0; i != popAmount; i++)
                {
                    stackSlotWhereNextPushWouldBeWrittenTo++;
                }

                cursor = cursor.Previous;
                seenInstructions.Add(cursor);
            }

            return null;
        }

        public static bool IsUnsupportedBranch(Instruction cursor)
        {
            if (cursor.OpCode.FlowControl == FlowControl.Next)
                return false;

            if (cursor.OpCode.FlowControl == FlowControl.Call)
                return false;

            return true;
        }

        public static MethodDefinition AddMethodImplementingInterfaceMethod(ModuleDefinition module, TypeDefinition type, MethodInfo interfaceMethod)
        {
            var interfaceMethodReference = module.ImportReference(interfaceMethod);
            var newMethod = new MethodDefinition(interfaceMethodReference.Name,
                MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.Final | MethodAttributes.Public |
                MethodAttributes.HideBySig, interfaceMethodReference.ReturnType);

            int index = 0;
            foreach (var pd in interfaceMethodReference.Parameters)
            {
                var pdName = pd.Name;
                if (pdName.Length == 0)
                    pdName = interfaceMethod.GetParameters()[index].Name;
                newMethod.Parameters.Add(new ParameterDefinition(pdName, pd.Attributes, module.ImportReference(pd.ParameterType)));
                index++;
            }

            type.Methods.Add(newMethod);
            return newMethod;
        }

#if !UNITY_DOTSRUNTIME
        /// <summary>
        /// Adds the [Preserve] attribute to the MethodDefinition instance
        /// </summary>
        /// <param name="methodDef"></param>
        public static void MarkAsPreserve(MethodDefinition methodDef, ModuleDefinition moduleDef)
        {
            var preserveAttributeCtor = moduleDef.ImportReference(typeof(PreserveAttribute).GetConstructor(Type.EmptyTypes));
            methodDef.CustomAttributes.Add(new CustomAttribute(preserveAttributeCtor));
        }

        /// <summary>
        /// Adds the [Preserve] attribute to the TypeDefinition instance
        /// </summary>
        /// <param name="typeDef"></param>
        public static void MarkAsPreserve(TypeDefinition typeDef, ModuleDefinition moduleDef)
        {
            var preserveAttributeCtor = moduleDef.ImportReference(typeof(PreserveAttribute).GetConstructor(Type.EmptyTypes));
            typeDef.CustomAttributes.Add(new CustomAttribute(preserveAttributeCtor));
        }

#endif
    }
}
