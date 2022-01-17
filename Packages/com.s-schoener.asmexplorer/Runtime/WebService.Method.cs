using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
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
            var asm = m_Explorer.FindAssembly(assemblyName);
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

            var method = Serialization.DecodeMethod(type, encodedMethod);
            var ctor = Serialization.DecodeCtor(type, encodedMethod);
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
                return;
            }
        }

        private void LayoutMethodHeader(HtmlWriter writer, Assembly asm, Type type, MethodBase method)
        {
            using (writer.Tag("small"))
            {
                DomainLink(writer);
                writer.Write(" | ");
                AssemblyLink(writer, asm);
                writer.Write(" | ");
                NamespaceLink(writer, asm.FindNamespace(type.Namespace), type.Namespace ?? "<root>");
                writer.Write(" | ");
                TypeLink(writer, type);
                writer.Break();
                writer.Break();
            }

            var attr = method.GetCustomAttributes(false);
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
            byte[] ilBytes;
            List<MonoIlOffset> monoIlOffset;
            if (context.DebugEnabled) {
                ilBytes = method.GetMethodBody().GetILAsByteArray();
                monoIlOffset = MonoDebug.GetIlOffsets(jitInfo).ToList();
            } else {
                ilBytes = new byte[0];
                monoIlOffset = new List<MonoIlOffset>();
            }
            int ilOffsetWrittenAlready = 0;
            var sourceLocation = MonoDebug.GetSourceLocation(jitInfo, monoIlOffset.Select(i => i.IlOffset));
            var files = new Dictionary<string, string[]>();
            foreach (var loc in sourceLocation) {
                if (string.IsNullOrEmpty(loc.File) || !File.Exists(loc.File))
                    continue;
                if (!files.ContainsKey(loc.File)) {
                    var lines = File.ReadAllLines(loc.File);
                    files.Add(loc.File, lines);
                }
            }
            int monoIlOffsetIndex = 0;
            using (writer.ContainerFluid(true))
            {
                writer.Write("Address: ");
                writer.Write(jitInfo.CodeStart.ToString("X16"));
                writer.Break();
                writer.Write("Code Size in Bytes: ");
                writer.Write(jitInfo.CodeSize.ToString());
                writer.Break();
                writer.Write("Optimization level: ");
#if UNITY_EDITOR
                if (WebService.OptimizationLevel == UnityEditor.Compilation.CodeOptimization.Debug)
                    writer.Write("Debug");
                else
                    writer.Write("Release");
#else
#if DEBUG
                writer.Write("enabled");
#else
                writer.Write("disabled");
#endif
#endif
                writer.Break();
                writer.Write("Debug info: ");
                writer.Write(context.DebugEnabled ? "enabled" : "disabled");
                writer.Break();
                writer.Write("Note: in builds with Optimization Level set to Release, line mapping may be inaccurate");
                writer.Break();
            }

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

                            if (first)
                            {
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

                            ulong address = Address(inst);
                            using (writer.Tag("span", "id", "X" + address.ToString("X16")))
                            {
                                uint offset = (uint) (address - (ulong)jitInfo.CodeStart);
                                if (monoIlOffsetIndex < monoIlOffset.Count && offset == monoIlOffset[monoIlOffsetIndex].NativeOffset) {
                                    using (writer.ContainerFluid(true)) {
                                        var sourceLoc = sourceLocation[monoIlOffsetIndex];
                                        if (!string.IsNullOrEmpty(sourceLoc.File) && files.TryGetValue(sourceLoc.File, out var lines)) {
                                            if (sourceLoc.Row <= lines.Length && sourceLoc.Column < lines[sourceLoc.Row - 1].Length) {
                                                writer.Write($"L{sourceLoc.Row}:");
                                                writer.Write(lines[sourceLoc.Row - 1]);
                                                writer.Write("\n");
                                            }
                                        }
                                        int ilEndOffset = ilBytes.Length;
                                        if (monoIlOffsetIndex + 1 < monoIlOffset.Count)
                                            ilEndOffset = (int)monoIlOffset[monoIlOffsetIndex + 1].IlOffset;
                                        int currentIlOffset = (int)monoIlOffset[monoIlOffsetIndex].IlOffset;
                                        if (currentIlOffset < ilOffsetWrittenAlready)
                                            currentIlOffset = ilOffsetWrittenAlready;

                                        using (writer.Tag("small", "class", "text-muted")) {
                                            while (currentIlOffset < ilEndOffset) {
                                                writer.Write(FormatOpcode(method, ilBytes, currentIlOffset, out int ilLength, out var arg));
                                                if (arg != null)
                                                    writer.Write("  ");
                                                if (arg is Type) {
                                                    TypeLink(writer, arg as Type);
                                                } else if (arg is MethodBase) {
                                                    MethodLink(writer, arg as MethodBase);
                                                } else if (arg is FieldInfo) {
                                                    var field = arg as FieldInfo;
                                                    WriteFieldModifiers(writer, field);
                                                    writer.Write(" ");
                                                    WriteFieldType(writer, field);
                                                    writer.Write(" ");
                                                    writer.Write(field.Name);
                                                }
                                                writer.Write("\n");
                                                currentIlOffset += ilLength;
                                            }
                                        }
                                        if (currentIlOffset > ilOffsetWrittenAlready)
                                            ilOffsetWrittenAlready = currentIlOffset;
                                        monoIlOffsetIndex++;
                                    }
                                }

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
                                        if (IsLocalStore(inst) && IsR11(op1) && context.R11 != null && context.R11.Type == ud_type.UD_OP_IMM)
                                        {
                                            if (context.BreakpointTrampolineOffset == 0)
                                            {
                                                context.BreakpointTrampolineOffset = op0.Value;
                                                StartNote(writer, ref context);
                                                writer.Write("write breakpoint trampoline");
                                            }
                                            else if (context.SinglestepTrampolineOffset == 0)
                                            {
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
                                    if (op1.Type == ud_type.UD_OP_IMM)
                                    {
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
                var target = Mono.GetJitInfoAnyDomain((IntPtr)callTarget, out _);
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

        private void InspectMethod(HtmlWriter writer, Assembly assembly, Type type, MethodBase method)
        {
            LayoutMethodHeader(writer, assembly, type, method);
            using (writer.Tag("code"))
            using (writer.Tag("h5"))
            {
                WriteMethodPrefix(writer, method);
                writer.Write(" ");
                WriteMethodReturnType(writer, method);
                writer.Write(" ");
                WriteMethodDeclaration(writer, method);
            }

            using (writer.ContainerFluid())
                WriteDissassembly(writer, method);
        }

        private void InspectCtor(HtmlWriter writer, Assembly assembly, Type type, ConstructorInfo ctor)
        {
            LayoutMethodHeader(writer, assembly, type, ctor);
            using (writer.Tag("code"))
            using (writer.Tag("h5"))
            {
                WriteCtorPrefix(writer, ctor);
                writer.Write(" ");
                WriteCtorDeclaration(writer, ctor);
            }

            using (writer.ContainerFluid())
                WriteDissassembly(writer, ctor);
        }

        private void WriteCtorPrefix(HtmlWriter writer, MethodBase c)
        {
            writer.Write(c.GetAccessModifier().Pretty());
        }

        private void WriteCtorDeclaration(HtmlWriter writer, ConstructorInfo c, bool noLink=false)
        {
            if (noLink)
            {
                writer.Write(c.DeclaringType.Name);
                writer.Write(".");
                writer.Write(c.Name);
            }
            else
            {
                TypeLink(writer, c.DeclaringType, c.DeclaringType.Name);
                writer.Write(".");
                MethodLink(writer, c, c.Name);
            }

            if (c.IsGenericMethodDefinition)
            {
                WriteGenericArguments(writer, c.GetGenericArguments(), TypeExt.NameMode.Short, noLink);
            }

            writer.Write("(");
            var ps = c.GetParameters();
            for (int i = 0; i < ps.Length; i++)
            {
                if (i > 0)
                {
                    writer.Write(", ");
                }

                WriteParameter(writer, ps[i], noLink);
            }

            writer.Write(")");

            if (c.IsGenericMethodDefinition)
            {
                var args = c.GetGenericArguments();
                for (int i = 0; i < args.Length; i++)
                    WriteGenericConstraints(writer, args[i], noLink);
            }
        }

        private void WriteMethodReturnType(HtmlWriter writer, MethodBase mb)
        {
            var m = mb as MethodInfo;
            if (m == null) return;
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

        private void WriteMethodDeclaration(HtmlWriter writer, MethodBase m, bool noLink = false)
        {
            if (noLink)
                writer.Write(m.Name);
            else
                MethodLink(writer, m, m.Name);

            if (m.IsGenericMethodDefinition)
            {
                WriteGenericArguments(writer, m.GetGenericArguments(), TypeExt.NameMode.Short, noLink);
            }

            writer.Write("(");
            var ps = m.GetParameters();
            for (int i = 0; i < ps.Length; i++)
            {
                if (i > 0)
                {
                    writer.Write(", ");
                }

                WriteParameter(writer, ps[i], noLink);
            }

            writer.Write(")");

            if (m.IsGenericMethodDefinition)
            {
                var args = m.GetGenericArguments();
                for (int i = 0; i < args.Length; i++)
                    WriteGenericConstraints(writer, args[i], noLink);
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

        public static string FormatOpcode(MethodBase method, byte[] data, int offset, out int length, out object arg)
        {
            var b = data[offset];
            length = 1;
            arg = null;
            if (b <= 0xE0) {
                switch (b) {
                    case 0x00: return "nop";
                    case 0x01: return "break";
                    case 0x02: return "ldarg.0";
                    case 0x03: return "ldarg.1";
                    case 0x04: return "ldarg.2";
                    case 0x05: return "ldarg.3";
                    case 0x06: return "ldloc.0";
                    case 0x07: return "ldloc.1";
                    case 0x08: return "ldloc.2";
                    case 0x09: return "ldloc.3";
                    case 0x0A: return "stloc.0";
                    case 0x0B: return "stloc.1";
                    case 0x0C: return "stloc.2";
                    case 0x0D: return "stloc.3";
                    case 0x0E: length += 1; return "ldarg.s " + data[offset + 1];
                    case 0x0F: length += 1; return "ldarga.s " + data[offset + 1];
                    case 0x10: length += 1; return "starg.s " + data[offset + 1];
                    case 0x11: length += 1; return "ldloc.s " + data[offset + 1];
                    case 0x12: length += 1; return "ldloca.s " + data[offset + 1];
                    case 0x13: length += 1; return "stloc.s " + data[offset + 1];
                    case 0x14: return "ldnull";
                    case 0x15: return "ldc.i4.m1";
                    case 0x16: return "ldc.i4.0";
                    case 0x17: return "ldc.i4.1";
                    case 0x18: return "ldc.i4.2";
                    case 0x19: return "ldc.i4.3";
                    case 0x1A: return "ldc.i4.4";
                    case 0x1B: return "ldc.i4.5";
                    case 0x1C: return "ldc.i4.6";
                    case 0x1D: return "ldc.i4.7";
                    case 0x1E: return "ldc.i4.8";
                    case 0x1F: length += 1; return "ldc.i4.s " + (sbyte)data[offset + 1];
                    case 0x20: length += 4; return "ldc.i4 " + BitConverter.ToInt32(data, offset + 1);
                    case 0x21: length += 8; return "ldc.i8 " + BitConverter.ToInt64(data, offset + 1);
                    case 0x22: length += 4; return "ldc.r4 " + BitConverter.ToSingle(data, offset + 1);
                    case 0x23: length += 8; return "ldc.r8 " + BitConverter.ToDouble(data, offset + 1);
                    case 0x25: return "dup";
                    case 0x26: return "pop";
                    case 0x27: length += 4; arg = method.Module.ResolveMethod(BitConverter.ToInt32(data, offset + 1)); return "jmp";
                    case 0x28: length += 4; arg = method.Module.ResolveMethod(BitConverter.ToInt32(data, offset + 1)); return "call";
                    case 0x29: length += 4;  arg = method.Module.ResolveMethod(BitConverter.ToInt32(data, offset + 1)); return "calli";
                    case 0x2A: return "ret";
                    case 0x2B: length += 1; return "br.s " + (sbyte)data[offset + 1];
                    case 0x2C: length += 1; return "brfalse.s " + (sbyte)data[offset + 1];
                    case 0x2D: length += 1; return "brtrue.s " + (sbyte)data[offset + 1];
                    case 0x2E: length += 1; return "beq.s " + (sbyte)data[offset + 1];
                    case 0x2F: length += 1; return "bge.s " + (sbyte)data[offset + 1];
                    case 0x30: length += 1; return "bgt.s " + (sbyte)data[offset + 1];
                    case 0x31: length += 1; return "ble.s " + (sbyte)data[offset + 1];
                    case 0x32: length += 1; return "blt.s " + (sbyte)data[offset + 1];
                    case 0x33: length += 1; return "bne.un.s " + (sbyte)data[offset + 1];
                    case 0x34: length += 1; return "bge.un.s " + (sbyte)data[offset + 1];
                    case 0x35: length += 1; return "bgt.un.s " + (sbyte)data[offset + 1];
                    case 0x36: length += 1; return "ble.un.s " + (sbyte)data[offset + 1];
                    case 0x37: length += 1; return "blt.un.s " + (sbyte)data[offset + 1];
                    case 0x38: length += 4; return "br " + BitConverter.ToInt32(data, offset + 1);
                    case 0x39: length += 4; return "brfalse " + BitConverter.ToInt32(data, offset + 1);
                    case 0x3A: length += 4; return "brtrue " + BitConverter.ToInt32(data, offset + 1);
                    case 0x3B: length += 4; return "beq " + BitConverter.ToInt32(data, offset + 1);
                    case 0x3C: length += 4; return "bge " + BitConverter.ToInt32(data, offset + 1);
                    case 0x3D: length += 4; return "bgt " + BitConverter.ToInt32(data, offset + 1);
                    case 0x3E: length += 4; return "ble " + BitConverter.ToInt32(data, offset + 1);
                    case 0x3F: length += 4; return "blt " + BitConverter.ToInt32(data, offset + 1);
                    case 0x40: length += 4; return "bne.un " + BitConverter.ToInt32(data, offset + 1);
                    case 0x41: length += 4; return "bge.un " + BitConverter.ToInt32(data, offset + 1);
                    case 0x42: length += 4; return "bgt.un " + BitConverter.ToInt32(data, offset + 1);
                    case 0x43: length += 4; return "ble.un " + BitConverter.ToInt32(data, offset + 1);
                    case 0x44: length += 4; return "blt.un " + BitConverter.ToInt32(data, offset + 1);
                    case 0x45: {
                        length += 4;
                        length += (int) BitConverter.ToUInt32(data, offset + 1) * 4;
                        return "switch";
                    }
                    case 0x46: return "ldind.i1";
                    case 0x47: return "ldind.u1";
                    case 0x48: return "ldind.i2";
                    case 0x49: return "ldind.u2";
                    case 0x4A: return "ldind.i4";
                    case 0x4B: return "ldind.u4";
                    case 0x4C: return "ldind.i8";
                    case 0x4D: return "ldind.i";
                    case 0x4E: return "ldind.r4";
                    case 0x4F: return "ldind.r8";
                    case 0x50: return "ldind.ref";
                    case 0x51: return "stind.ref";
                    case 0x52: return "stind.i1";
                    case 0x53: return "stind.i2";
                    case 0x54: return "stind.i4";
                    case 0x55: return "stind.i8";
                    case 0x56: return "stind.r4";
                    case 0x57: return "stind.r8";
                    case 0x58: return "add";
                    case 0x59: return "sub";
                    case 0x5A: return "mul";
                    case 0x5B: return "div";
                    case 0x5C: return "div.un";
                    case 0x5D: return "rem";
                    case 0x5E: return "rem.un";
                    case 0x5F: return "and";
                    case 0x60: return "or";
                    case 0x61: return "xor";
                    case 0x62: return "shl";
                    case 0x63: return "shr";
                    case 0x64: return "shr.un";
                    case 0x65: return "neg";
                    case 0x66: return "not";
                    case 0x67: return "conv.i1";
                    case 0x68: return "conv.i2";
                    case 0x69: return "conv.i4";
                    case 0x6A: return "conv.i8";
                    case 0x6B: return "conv.r4";
                    case 0x6C: return "conv.r8";
                    case 0x6D: return "conv.u4";
                    case 0x6E: return "conv.u8";
                    case 0x6F: length += 4; arg = method.Module.ResolveMethod(BitConverter.ToInt32(data, offset + 1)); return "callvirt";
                    case 0x70: length += 4; arg = method.Module.ResolveType(BitConverter.ToInt32(data, offset + 1)); return "cpobj";
                    case 0x71: length += 4; arg = method.Module.ResolveType(BitConverter.ToInt32(data, offset + 1)); return "ldobj";
                    case 0x72: length += 4; return "ldstr " + '"' + method.Module.ResolveString(BitConverter.ToInt32(data, offset + 1)) + '"';
                    case 0x73: length += 4; arg = method.Module.ResolveMethod(BitConverter.ToInt32(data, offset + 1)); return "newobj";
                    case 0x74: length += 4; arg = method.Module.ResolveType(BitConverter.ToInt32(data, offset + 1)); return "castclass";
                    case 0x75: length += 4; arg = method.Module.ResolveType(BitConverter.ToInt32(data, offset + 1)); return "isinst";
                    case 0x76: return "conv.r.un";
                    case 0x79: length += 4; arg = method.Module.ResolveType(BitConverter.ToInt32(data, offset + 1)); return "unbox";
                    case 0x7A: return "throw";
                    case 0x7B: length += 4; arg = method.Module.ResolveField(BitConverter.ToInt32(data, offset + 1)); return "ldfld";
                    case 0x7C: length += 4; arg = method.Module.ResolveField(BitConverter.ToInt32(data, offset + 1)); return "ldflda";
                    case 0x7D: length += 4; arg = method.Module.ResolveField(BitConverter.ToInt32(data, offset + 1)); return "stfld";
                    case 0x7E: length += 4; arg = method.Module.ResolveField(BitConverter.ToInt32(data, offset + 1)); return "ldsfld";
                    case 0x7F: length += 4; arg = method.Module.ResolveField(BitConverter.ToInt32(data, offset + 1)); return "ldsflda";
                    case 0x80: length += 4; arg = method.Module.ResolveField(BitConverter.ToInt32(data, offset + 1)); return "stsfld";
                    case 0x81: length += 4; arg = method.Module.ResolveType(BitConverter.ToInt32(data, offset + 1)); return "stobj";
                    case 0x82: return "conv.ovf.i1.un";
                    case 0x83: return "conv.ovf.i2.un";
                    case 0x84: return "conv.ovf.i4.un";
                    case 0x85: return "conv.ovf.i8.un";
                    case 0x86: return "conv.ovf.u1.un";
                    case 0x87: return "conv.ovf.u2.un";
                    case 0x88: return "conv.ovf.u4.un";
                    case 0x89: return "conv.ovf.u8.un";
                    case 0x8A: return "conv.ovf.i.un";
                    case 0x8B: return "conv.ovf.u.un";
                    case 0x8C: length += 4; arg = method.Module.ResolveType(BitConverter.ToInt32(data, offset + 1)); return "box";
                    case 0x8D: length += 4; arg = method.Module.ResolveType(BitConverter.ToInt32(data, offset + 1)); return "newarr";
                    case 0x8E: return "ldlen";
                    case 0x8F: length += 4; arg = method.Module.ResolveType(BitConverter.ToInt32(data, offset + 1)); return "ldelema";
                    case 0x90: return "ldelem.i1";
                    case 0x91: return "ldelem.u1";
                    case 0x92: return "ldelem.i2";
                    case 0x93: return "ldelem.u2";
                    case 0x94: return "ldelem.i4";
                    case 0x95: return "ldelem.u4";
                    case 0x96: return "ldelem.i8";
                    case 0x97: return "ldelem.i";
                    case 0x98: return "ldelem.r4";
                    case 0x99: return "ldelem.r8";
                    case 0x9A: return "ldelem.ref";
                    case 0x9B: return "stelem.i";
                    case 0x9C: return "stelem.i1";
                    case 0x9D: return "stelem.i2";
                    case 0x9E: return "stelem.i4";
                    case 0x9F: return "stelem.i8";
                    case 0xA0: return "stelem.r4";
                    case 0xA1: return "stelem.r8";
                    case 0xA2: return "stelem.ref";
                    case 0xA3: length += 4; arg = method.Module.ResolveType(BitConverter.ToInt32(data, offset + 1)); return "ldelem";
                    case 0xA4: length += 4; arg = method.Module.ResolveType(BitConverter.ToInt32(data, offset + 1)); return "stelem";
                    case 0xA5: length += 4; arg = method.Module.ResolveType(BitConverter.ToInt32(data, offset + 1)); return "unbox.any";
                    case 0xB3: return "conv.ovf.i1";
                    case 0xB4: return "conv.ovf.u1";
                    case 0xB5: return "conv.ovf.i2";
                    case 0xB6: return "conv.ovf.u2";
                    case 0xB7: return "conv.ovf.i4";
                    case 0xB8: return "conv.ovf.u4";
                    case 0xB9: return "conv.ovf.i8";
                    case 0xBA: return "conv.ovf.u8";
                    case 0xC2: length += 4; arg = method.Module.ResolveType(BitConverter.ToInt32(data, offset + 1)); return "refanyval";
                    case 0xC3: return "ckfinite";
                    case 0xC6: length += 4; arg = method.Module.ResolveType(BitConverter.ToInt32(data, offset + 1)); return "mkrefany";
                    case 0xD0: length += 4; return "ldtoken " + BitConverter.ToInt32(data, offset + 1);
                    case 0xD1: return "conv.u2";
                    case 0xD2: return "conv.u1";
                    case 0xD3: return "conv.i";
                    case 0xD4: return "conv.ovf.i";
                    case 0xD5: return "conv.ovf.u";
                    case 0xD6: return "add.ovf";
                    case 0xD7: return "add.ovf.un";
                    case 0xD8: return "mul.ovf";
                    case 0xD9: return "mul.ovf.un";
                    case 0xDA: return "sub.ovf";
                    case 0xDB: return "sub.ovf.un";
                    case 0xDC: return "endfinally";
                    case 0xDD: length += 4; return "leave " + BitConverter.ToInt32(data, offset + 1);;
                    case 0xDE: length += 1; return "leave.s" + (sbyte)data[offset + 1];
                    case 0xDF: return "stind.i";
                    case 0xE0: return "conv.u";
                }
            }
            if (b == 0xFE) {
                b = data[offset + 1];
                length = 2;
                switch (b) {
                    case 0x00: return "arglist";
                    case 0x01: return "ceq";
                    case 0x02: return "cgt";
                    case 0x03: return "cgt.un";
                    case 0x04: return "clt";
                    case 0x05: return "clt.un";
                    case 0x06: length += 4; arg = method.Module.ResolveMethod(BitConverter.ToInt32(data, offset + 2)); return "ldftn";
                    case 0x07: length += 4; arg = method.Module.ResolveMethod(BitConverter.ToInt32(data, offset + 2)); return "ldvirtftn";
                    case 0x09: length += 2; return "ldarg " + BitConverter.ToUInt16(data, offset + 1);
                    case 0x0A: length += 2; return "ldarga " + BitConverter.ToUInt16(data, offset + 1);
                    case 0x0B: length += 2; return "starg " + BitConverter.ToUInt16(data, offset + 1);
                    case 0x0C: length += 2; return "ldloc " + BitConverter.ToUInt16(data, offset + 1);
                    case 0x0D: length += 2; return "ldloca " + BitConverter.ToUInt16(data, offset + 1);
                    case 0x0E: length += 2; return "stloc " + BitConverter.ToUInt16(data, offset + 1);
                    case 0x0F: return "localloc";
                    case 0x11: return "endfilter";
                    case 0x12: {
                        length += 1;
                        var nextInst = FormatOpcode(method, data, offset + 3, out var nextLength, out arg);
                        length += nextLength;
                        string alignment = data[offset + 2].ToString();
                        return "unaligned." + alignment + "." + nextInst;
                    }
                    case 0x13: {
                        var nextInst = FormatOpcode(method, data, offset + 2, out var nextLength, out arg);
                        length += nextLength;
                        return "volatile." + nextInst;
                    }
                    case 0x14: {
                        var nextInst = FormatOpcode(method, data, offset + 2, out var nextLength, out arg);
                        length += nextLength;
                        return "tail." + nextInst;
                    }
                    case 0x15: length += 4; arg = method.Module.ResolveType(BitConverter.ToInt32(data, offset + 2)); return "initobj";
                    case 0x16: {
                        var constrain = method.Module.ResolveType(BitConverter.ToInt32(data, offset + 2));
                        length += 4;
                        var nextInst = FormatOpcode(method, data, offset + 6, out var nextLength, out arg);
                        length += nextLength;
                        return "constrained. " + constrain + " " + nextInst;
                    }
                    case 0x17: return "cpblk";
                    case 0x18: return "initblk";
                    case 0x19: {
                        length += 1;
                        var nextInst = FormatOpcode(method, data, offset + 3, out var nextLength, out arg);
                        length += nextLength;
                        byte flags = data[offset + 2];
                        string flagName = "";
                        bool needSeparator = false;
                        if ((flags & 0x1) != 0) {
                            flagName += "typecheck";
                            needSeparator = true;
                        }
                        if ((flags & 0x2) != 0) {
                            if (needSeparator)
                                flagName += "|";
                            flagName += "rangecheck";
                        }
                        if ((flags & 04) != 0) {
                            if (needSeparator)
                                flagName += "|";
                            flagName += "nullcheck";
                        }
                        return "no." + flagName + "." + nextInst;
                    }
                    case 0x1A: return "rethrow";
                    case 0x1C: length += 4; arg = method.Module.ResolveType(BitConverter.ToInt32(data, offset + 2)); return "sizeof";
                    case 0x1D: return "refanytype";
                    case 0x1E: {
                        var nextInst = FormatOpcode(method, data, offset + 2, out var nextLength, out arg);
                        length += nextLength;
                        return "readonly." + nextInst;
                    }
                }
            }
            throw new Exception("Unknown opcode " + b.ToString("X"));
        }
    }
}
