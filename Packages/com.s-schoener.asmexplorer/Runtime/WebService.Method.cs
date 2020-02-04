using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SharpDisasm;
using SharpDisasm.Udis86;

namespace AsmExplorer
{
    public partial class WebService
    {
        const int k_NoteColumn = 77;

        void StartNote(HtmlWriter writer, ref MethodContext context)
        {
            if (!context.HasLineNote)
            {
                for (int i = context.LineLength; i < k_NoteColumn; i++)
                    writer.Write(" ");
                context.HasLineNote = true;
            }
            writer.Write("; ");
        }

        private void InspectMethod(HtmlWriter writer, string assemblyName, string typeName, string encodedMethod)
        {
            var asm = _explorer.FindAssembly(assemblyName);
            if (asm == null)
            {
                writer.Write("Unknown assembly name " + assemblyName);
                return;
            }
            var type = asm.FindType(typeName);
            if (type == null)
            {
                writer.Write("Unknown type name " + typeName + " in " + asm.FullName);
                return;
            }
            var method = DecodeMethod(type, encodedMethod);
            var ctor = DecodeCtor(type, encodedMethod);
            if (method != null)
            {
                InspectMethod(writer, asm, type, method);
            }
            else if (ctor != null)
            {
                InspectCtor(writer, asm, type, ctor);
            }
            else
            {
                writer.Write("Unknown method name " + encodedMethod + " on type " + type.FullName + " in " + asm.FullName);
                var methods = string.Join("<br/>", type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Select(x => x.ToString()));
                writer.Write("Have to following methods: " + methods);
                return;
            }
        }

        private void LayoutMethodHeader(HtmlWriter writer, Assembly asm, Type type, MethodBase method)
        {
            using (writer.Tag("small"))
            {
                AssemblyLink(writer, asm);
                writer.Write(" | ");
                NamespaceLink(writer, asm.FindNamespace(type.Namespace), type.Namespace ?? "<root>");
                writer.Write(" | ");
                TypeLink(writer, type);
            }
            var attr = method.GetCustomAttributes(true);
            if (attr.Length > 0)
            {
                writer.Break();
                WriteAttributes(writer, attr);
            }
        }

        private IEnumerable<Instruction> GetInstructions(MonoJitInfo jit)
        {
            Disassembler.Translator.IncludeBinary = true;
            Disassembler.Translator.IncludeAddress = true;
            ArchitectureMode mode = ArchitectureMode.x86_64;
            if (IntPtr.Size == 4)
                mode = ArchitectureMode.x86_32;
            var disasm = new Disassembler(jit.CodeStart, jit.CodeSize, mode, (ulong)jit.CodeStart, true);
            return disasm.Disassemble();
        }

        struct MethodContext
        {
            public Operand R11;

            public Instruction LastInstruction;
            public bool DebugEnabled;
            public bool HasBasePointer;

            public long BreakpointTrampolineOffset;
            public long SinglestepTrampolineOffset;

            public int LineLength;
            public bool HasLineNote;
        }

        private object _disassemblerLock = new object();
        private void WriteDissassembly(HtmlWriter writer, MethodBase method)
        {
            if (method.IsAbstract || method.ContainsGenericParameters)
            {
                writer.Write("Cannot display disassembly for generic or abstract methods.");
                return;
            }
            var context = new MethodContext()
            {
                DebugEnabled = MonoDebug.IsEnabled
            };

            var jitInfo = Mono.GetJitInfo(method);
            writer.Write("Address: ");
            writer.Write(jitInfo.CodeStart.ToString("X16"));
            writer.Break();
            writer.Write("Code Size in Bytes: ");
            writer.Write(jitInfo.CodeSize.ToString());
            writer.Break();
            writer.Write("Debug mode: ");
            writer.Write(context.DebugEnabled ? "enabled" : "disabled");
            writer.Break();
            if (jitInfo.CodeSize <= 0)
            {
                return;
            }

            using (writer.Tag("pre"))
            {
                using (writer.Tag("code"))
                {
                    // some special help for calls using R11 and nops
                    int nops = 0;
                    bool first = true;
                    lock (_disassemblerLock)
                    {
                        foreach (var inst in GetInstructions(jitInfo))
                        {
                            context.HasLineNote = false;

                            if (first) {
                                context.HasBasePointer =
                                    inst.Mnemonic == ud_mnemonic_code.UD_Ipush &&
                                    inst.Operands[0].Type == ud_type.UD_OP_REG &&
                                    inst.Operands[0].Base == ud_type.UD_R_RBP;
                                first = false;
                            }



                            // abbreviate excessive nopping
                            if (inst.Mnemonic == ud_mnemonic_code.UD_Inop)
                            {
                                nops++;
                                context.LastInstruction = inst;
                                continue;
                            }
                            if (nops > 0)
                            {
                                if (nops == 1)
                                {
                                    writer.Write(context.LastInstruction.ToString());
                                }
                                else
                                {
                                    var str = context.LastInstruction.ToString();
                                    writer.Write(str);
                                    context.LineLength = str.Length;
                                    StartNote(writer, ref context);

                                    writer.Write("repeated nops (");
                                    writer.Write(nops.ToString());
                                    writer.Write(" bytes)");
                                }
                                nops = 0;
                                context.HasLineNote = false;
                                writer.Write("\n");
                            }

                            using (writer.Tag("span").With("id", "X" + Address(inst).ToString("X16")))
                            {
                                var str = inst.ToString();
                                context.LineLength = str.Length;
                                writer.Write(str);

                                if (inst.Mnemonic == ud_mnemonic_code.UD_Imov)
                                {
                                    var op0 = inst.Operands[0];
                                    var op1 = inst.Operands[1];
                                    // call targets on x64 are frequently placed in R11, so let's ensure that we catch that.
                                    if (IsR11(op0))
                                        context.R11 = op1;
                                    if (context.DebugEnabled)
                                    {
                                        if (IsLocalStore(inst) && IsR11(op1) && context.R11 != null && context.R11.Type == ud_type.UD_OP_IMM) {
                                            if (context.BreakpointTrampolineOffset == 0) {
                                                context.BreakpointTrampolineOffset = op0.Value;
                                                StartNote(writer, ref context);
                                                writer.Write("write breakpoint trampoline");
                                            }
                                            else if (context.SinglestepTrampolineOffset == 0) {
                                                context.SinglestepTrampolineOffset = op0.Value;
                                                StartNote(writer, ref context);
                                                writer.Write("write singlestep trampoline");
                                            }
                                        }
                                        else if (IsReadSinglestepTrampoline(inst))
                                        {
                                            StartNote(writer, ref context);
                                            writer.Write("read singlestep trampoline");
                                        }
                                        else if (IsReadBreakpointTrampoline(inst))
                                        {
                                            StartNote(writer, ref context);
                                            writer.Write("read breakpoint trampoline");
                                        }
                                    }
                                }
                                else if (inst.Mnemonic == ud_mnemonic_code.UD_Iadd)
                                {
                                    var op1 = inst.Operands[1];
                                    if (op1.Type == ud_type.UD_OP_IMM) {
                                        StartNote(writer, ref context);
                                        writer.Write(op1.Value.ToString());
                                    }
                                }
                                else if (inst.Mnemonic == ud_mnemonic_code.UD_Icall)
                                {
                                    WriteCallInstruction(writer, inst, ref context);
                                    context.R11 = null;
                                }
                                else if (IsJump(inst.Mnemonic))
                                {
                                    WriteJumpInstruction(writer, inst, ref context);
                                }
                            }
                            writer.Write("\n");

                            context.LastInstruction = inst;
                        }
                    }
                }
            }

            bool IsR11(Operand op) => op.Type == ud_type.UD_OP_REG && op.Base == ud_type.UD_R_R11;
            bool IsReadSinglestepTrampoline(Instruction inst) => IsLocalLoadOffset(inst, context.SinglestepTrampolineOffset);
            bool IsReadBreakpointTrampoline(Instruction inst) => IsLocalLoadOffset(inst, context.BreakpointTrampolineOffset);
            ud_type StackFrameRegister() => context.HasBasePointer ? ud_type.UD_R_RBP : ud_type.UD_R_RSP;
            bool IsLocalInteraction(Instruction inst, int operand)
            {
                if (inst.Mnemonic != ud_mnemonic_code.UD_Imov) return false;
                var op = inst.Operands[operand];
                return op.Type == ud_type.UD_OP_MEM && op.Base == StackFrameRegister();
            }
            bool IsLocalStore(Instruction inst) => IsLocalInteraction(inst, 0);
            bool IsLocalLoad(Instruction inst) => IsLocalInteraction(inst, 1);
            bool IsLocalLoadOffset(Instruction inst, long offset) => IsLocalLoad(inst) && inst.Operands[1].Value == offset;
        }

        private static ulong Address(Instruction inst)
        {
            return inst.PC - (ulong)inst.Bytes.Length;
        }

        private static bool IsJump(ud_mnemonic_code mnemonic)
        {
            switch (mnemonic)
            {
                case ud_mnemonic_code.UD_Ija:
                case ud_mnemonic_code.UD_Ijae:
                case ud_mnemonic_code.UD_Ijb:
                case ud_mnemonic_code.UD_Ijbe:
                case ud_mnemonic_code.UD_Ijcxz:
                case ud_mnemonic_code.UD_Ijecxz:
                case ud_mnemonic_code.UD_Ijg:
                case ud_mnemonic_code.UD_Ijge:
                case ud_mnemonic_code.UD_Ijl:
                case ud_mnemonic_code.UD_Ijle:
                case ud_mnemonic_code.UD_Ijmp:
                case ud_mnemonic_code.UD_Ijno:
                case ud_mnemonic_code.UD_Ijnp:
                case ud_mnemonic_code.UD_Ijns:
                case ud_mnemonic_code.UD_Ijnz:
                case ud_mnemonic_code.UD_Ijo:
                case ud_mnemonic_code.UD_Ijp:
                case ud_mnemonic_code.UD_Ijrcxz:
                case ud_mnemonic_code.UD_Ijs:
                case ud_mnemonic_code.UD_Ijz:
                    return true;
                default:
                    return false;
            }
        }

        private ulong GetBranchTarget(Instruction inst, ulong r11)
        {
            var op0 = inst.Operands[0];
            ulong callTarget = 0;
            if (op0.Type == ud_type.UD_OP_REG && op0.Base == ud_type.UD_R_R11)
            {
                callTarget = r11;
            }
            else if (op0.Type == ud_type.UD_OP_IMM)
            {
                if (op0.Size == 8)
                {
                    callTarget = op0.LvalByte; // doesn't happen
                }
                else if (op0.Size == 16)
                {
                    callTarget = op0.LvalUWord; // doesn't happen
                }
                else if (op0.Size == 32)
                {
                    callTarget = inst.PC + op0.LvalUDWord;
                }
                else if (op0.Size == 64)
                {
                    callTarget = op0.LvalUQWord;
                }
            }
            else if (op0.Type == ud_type.UD_OP_JIMM)
            {
                long offset = (long)inst.PC;
                if (op0.Size == 8)
                {
                    callTarget = (ulong)(offset + op0.LvalSByte);
                }
                else if (op0.Size == 16)
                {
                    callTarget = (ulong)(offset + op0.LvalSWord);
                }
                else if (op0.Size == 32)
                {
                    callTarget = (ulong)(offset + op0.LvalSDWord);
                }
                else if (op0.Size == 64)
                {
                    callTarget = (ulong)(offset + op0.LvalSQWord);
                }
            }
            return callTarget;
        }

        private void WriteJumpInstruction(HtmlWriter writer, Instruction inst, ref MethodContext context)
        {
            ulong callTarget = GetBranchTarget(inst, context.R11?.LvalUQWord ?? 0);
            if (callTarget == 0)
                return;
            StartNote(writer, ref context);
            writer.AHref("go to target", "#X" + callTarget.ToString("X16"));
        }

        private void WriteCallInstruction(HtmlWriter writer, Instruction inst, ref MethodContext context)
        {
            // we should be able to find the call targets for R11/relative immediate
            ulong callTarget = GetBranchTarget(inst, context.R11?.LvalUQWord ?? 0);
            StartNote(writer, ref context);
            if (callTarget != 0)
            {
                var target = Mono.GetJitInfo((IntPtr)callTarget);
                if (target.Method != null)
                {
                    if (target.Method.IsConstructor)
                    {
                        WriteCtorPrefix(writer, target.Method);
                        writer.Write(" ");
                        WriteCtorDeclaration(writer, target.Method as ConstructorInfo);
                    }
                    else if (target.Method != null)
                    {
                        WriteMethodPrefix(writer, target.Method);
                        writer.Write(" ");
                        WriteMethodReturnType(writer, target.Method as MethodInfo);
                        writer.Write(" ");
                        if (target.Method.DeclaringType != null)
                        {
                            TypeLink(writer, target.Method.DeclaringType);
                            writer.Write(".");
                        }
                        WriteMethodDeclaration(writer, target.Method as MethodInfo);
                    }
                }
                else
                {
                    writer.Write("unknown target @ " + callTarget.ToString("X16"));
                }
            }
            else if (context.DebugEnabled)
            {
                var r11 = context.R11;
                ud_type stackFrameRegister = context.HasBasePointer ? ud_type.UD_R_RBP : ud_type.UD_R_RSP;
                if (r11 != null && r11.Base == stackFrameRegister && r11.Type == ud_type.UD_OP_MEM && r11.Value == context.SinglestepTrampolineOffset)
                {
                    writer.Write("check for singlestep");
                    return;
                }
                writer.Write("unknown target; native, virtual, or unpatched JIT trampoline");
            }
            else
            {
                writer.Write("unknown target; native, virtual, or unpatched JIT trampoline");
            }
        }

        private void InspectMethod(HtmlWriter writer, Assembly assembly, Type type, MethodInfo method)
        {
            LayoutMethodHeader(writer, assembly, type, method);
            using (writer.Tag("h2"))
            {
                WriteMethodPrefix(writer, method);
                writer.Write(" ");
                WriteMethodReturnType(writer, method);
                writer.Write(" ");
                WriteMethodDeclaration(writer, method);
            }
            WriteDissassembly(writer, method);
        }

        private void InspectCtor(HtmlWriter writer, Assembly assembly, Type type, ConstructorInfo ctor)
        {
            LayoutMethodHeader(writer, assembly, type, ctor);
            using (writer.Tag("h2"))
            {
                WriteCtorPrefix(writer, ctor);
                writer.Write(" ");
                WriteCtorDeclaration(writer, ctor);
            }
            WriteDissassembly(writer, ctor);
        }

        private void WriteCtorPrefix(HtmlWriter writer, MethodBase c)
        {
            writer.Write(c.GetAccessModifier().Pretty());
        }

        private void WriteCtorDeclaration(HtmlWriter writer, ConstructorInfo c)
        {
            TypeLink(writer, c.DeclaringType, c.DeclaringType.Name);
            writer.Write(" ");
            FunctionLink(writer, c, c.Name);

            if (c.IsGenericMethodDefinition)
            {
                WriteGenericArguments(writer, c.GetGenericArguments());
            }

            writer.Write(" (");
            var ps = c.GetParameters();
            for (int i = 0; i < ps.Length; i++)
            {
                if (i > 0)
                {
                    writer.Write(", ");
                }
                WriteParameter(writer, ps[i]);
            }
            writer.Write(" )");

            if (c.IsGenericMethodDefinition)
            {
                var args = c.GetGenericArguments();
                for (int i = 0; i < args.Length; i++)
                    WriteGenericConstraints(writer, args[i]);
            }
        }

        private void WriteMethodReturnType(HtmlWriter writer, MethodInfo m)
        {
            if (m.ReturnType.IsByRef)
            {
                writer.Write("ref ");
                WriteTypeName(writer, m.ReturnType.GetElementType());
            }
            else
            {
                WriteTypeName(writer, m.ReturnType);
            }
        }

        private void WriteMethodDeclaration(HtmlWriter writer, MethodInfo m)
        {
            FunctionLink(writer, m, m.Name);

            if (m.IsGenericMethodDefinition)
            {
                WriteGenericArguments(writer, m.GetGenericArguments());
            }

            writer.Write(" ( ");
            var ps = m.GetParameters();
            for (int i = 0; i < ps.Length; i++)
            {
                if (i > 0)
                {
                    writer.Write(", ");
                }
                WriteParameter(writer, ps[i]);
            }
            writer.Write(" )");

            if (m.IsGenericMethodDefinition)
            {
                var args = m.GetGenericArguments();
                for (int i = 0; i < args.Length; i++)
                    WriteGenericConstraints(writer, args[i]);
            }
        }

        private void WriteMethodPrefix(HtmlWriter writer, MethodBase method)
        {
            writer.Write(method.GetAccessModifier().Pretty());
            if ((method.GetMethodImplementationFlags() & MethodImplAttributes.InternalCall) != 0)
                writer.Write(" extern");
            else if (method.IsAbstract)
            {
                writer.Write(" abstract");
            }
            else if (method.IsFinal)
            {
                writer.Write(" sealed");
            }
            else if (method.IsVirtual)
            {
                writer.Write(" virtual");
            }
            else if (method.IsStatic)
            {
                writer.Write(" static");
            }
        }
    }
}