using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SharpDisasm;
using SharpDisasm.Udis86;

namespace AsmExplorer {
    public partial class WebService {
        private void InspectMethod(HtmlWriter writer, string assemblyName, string typeName, string methodName) {
            var asm = _explorer.FindAssembly(assemblyName);
            if (asm == null) {
                writer.Write("Unknown assembly name " + assemblyName);
                return;
            }
            var type = asm.FindType(typeName);
            if (type == null) {
                writer.Write("Unknown type name " + typeName + " in " + asm.FullName);
                return;
            }
            var method = FindMethod(type, methodName);
            var ctor = FindCtor(type, methodName);
            if (method != null) {
                InspectMethod(writer, asm, type, method);
            } else if (ctor != null) {
                InspectCtor(writer, asm, type, ctor);
            } else {
                writer.Write("Unknown method name " + methodName + " on type " + type.FullName + " in " + asm.FullName);
                return;
            }
        }

        private MethodInfo FindMethod(Type type, string methodName) {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var methods = type.GetMethods(flags);
            for (int i = 0; i < methods.Length; i++) {
                if (methods[i].ToString() == methodName)
                    return methods[i];
            }
            return null;
        }

        private ConstructorInfo FindCtor(Type type, string ctorName) {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var ctors = type.GetConstructors(flags);
            for (int i = 0; i < ctors.Length; i++) {
                if (ctors[i].ToString() == ctorName)
                    return ctors[i];
            }
            return null;
        }

        private void LayoutMethodHeader(HtmlWriter writer, Assembly asm, Type type, MethodBase method) {
            using(writer.Tag("small")) {
                AssemblyLink(writer, asm);
                writer.Write(" | ");
                NamespaceLink(writer, asm.FindNamespace(type.Namespace), type.Namespace ?? "<root>");
                writer.Write(" | ");
                TypeLink(writer, type);
            }
            var attr = method.GetCustomAttributes(true);
            if (attr.Length > 0) {
                writer.Break();
                WriteAttributes(writer, attr);
            }
        }

        private IEnumerable<SharpDisasm.Instruction> GetInstructions(MonoJitInfo jit) {
            Disassembler.Translator.IncludeBinary = true;
            Disassembler.Translator.IncludeAddress = true;
            ArchitectureMode mode = ArchitectureMode.x86_64;
            if (IntPtr.Size == 4)
                mode = ArchitectureMode.x86_32;
            var disasm = new Disassembler(jit.CodeStart, jit.CodeSize, mode, (ulong)jit.CodeStart, true);
            return disasm.Disassemble();
        }

        private object _disassemblerLock = new object();
        private void WriteDissassembly(HtmlWriter writer, MethodBase method) {
            if (method.IsAbstract || method.ContainsGenericParameters) {
                writer.Write("Cannot display disassembly for generic or abstract methods.");
                return;
            }
            var jitInfo = Mono.GetJitInfo(method);
            writer.Write("Address: ");
            writer.Write(jitInfo.CodeStart.ToString("X16"));
            writer.Break();
            writer.Write("Code Size in Bytes: ");
            writer.Write(jitInfo.CodeSize.ToString());
            writer.Break();
            if (jitInfo.CodeSize <= 0) {
                return;
            }
            using(writer.Tag("pre")) {
                using (writer.Tag("code")) {
                    // some special help for calls using R11 and nops
                    int nops = 0;
                    Instruction lastInstruction = null;
                    ulong r11Register = 0;
                    lock (_disassemblerLock) {
                        foreach (var inst in GetInstructions(jitInfo)) {
                            // abbreviate excessive nopping
                            if (inst.Mnemonic == ud_mnemonic_code.UD_Inop) {
                                nops++;
                                lastInstruction = inst;
                                continue;
                            }
                            if (nops > 0) {
                                if (nops == 1) {
                                    writer.Write(lastInstruction.ToString());
                                } else {
                                    writer.Write("nop (");
                                    writer.Write(nops.ToString());
                                    writer.Write(" bytes)");
                                }
                                nops = 0;
                                writer.Write("\n");
                            }
                            lastInstruction = inst;
                            using(writer.Tag("span").With("id", "X" + Address(inst).ToString("X16"))) {
                                writer.Write(inst.ToString());

                                if (inst.Mnemonic == ud_mnemonic_code.UD_Imov) {
                                    var op0 = inst.Operands[0];
                                    // call targets on x64 are frequently placed in R11, so let's ensure that we catch that.
                                    if (op0.Type == ud_type.UD_OP_REG && op0.Base == ud_type.UD_R_R11) {
                                        r11Register = inst.Operands[1].LvalUQWord;
                                    }
                                } else if (inst.Mnemonic == ud_mnemonic_code.UD_Icall) {
                                    WriteCallInstruction(writer, inst, r11Register);
                                    r11Register = 0;
                                } else if (IsJump(inst.Mnemonic)) {
                                    WriteJumpInstruction(writer, inst, r11Register);
                                }
                            }
                            writer.Write("\n");
                        }
                    }
                }
            }
        }

        private static ulong Address(Instruction inst) {
            return inst.PC - (ulong)inst.Bytes.Length;
        }

        private static bool IsJump(ud_mnemonic_code mnemonic) {
            switch(mnemonic) {
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

        private ulong GetBranchTarget(Instruction inst, ulong r11) {
            var op0 = inst.Operands[0];
            ulong callTarget = 0;
            if (op0.Type == ud_type.UD_OP_REG && op0.Base == ud_type.UD_R_R11) {
                callTarget = r11;
            } else if (op0.Type == ud_type.UD_OP_IMM) {
                if (op0.Size == 8) {
                    callTarget = op0.LvalByte; // doesn't happen
                } else if (op0.Size == 16) {
                    callTarget = op0.LvalUWord; // doesn't happen
                } else if (op0.Size == 32) {
                    callTarget = inst.PC + op0.LvalUDWord;
                } else if (op0.Size == 64) {
                    callTarget = op0.LvalUQWord;
                }
            } else if (op0.Type == ud_type.UD_OP_JIMM) {
                long offset = (long) inst.PC;
                if (op0.Size == 8) {
                    callTarget = (ulong) (offset + op0.LvalSByte);
                } else if (op0.Size == 16) {
                    callTarget = (ulong) (offset + op0.LvalSWord);
                } else if (op0.Size == 32) {
                    callTarget = (ulong) (offset + op0.LvalSDWord);
                } else if (op0.Size == 64) {
                    callTarget = (ulong) (offset + op0.LvalSQWord);
                }
            }
            return callTarget;
        }

        private void WriteJumpInstruction(HtmlWriter writer, Instruction inst, ulong r11) {
            ulong callTarget = GetBranchTarget(inst, r11);
            if (callTarget == 0)
                return;
            writer.Write(" ; ");
            writer.AHref("go to target", "#X" + callTarget.ToString("X16"));
        }

        private void WriteCallInstruction(HtmlWriter writer, Instruction inst, ulong r11) {
            // we should be able to find the call targets for R11/relative immediate
            ulong callTarget = GetBranchTarget(inst, r11);
            writer.Write(" ; ");
            if (callTarget != 0) {
                var target = Mono.GetJitInfo((IntPtr)callTarget);
                if (target.Method != null) {
                    if (target.Method.IsConstructor) {
                        WriteCtorPrefix(writer, target.Method);
                        writer.Write(" ");
                        WriteCtorDeclaration(writer, target.Method as ConstructorInfo);
                    } else if (target.Method != null) {
                        WriteMethodPrefix(writer, target.Method);
                        writer.Write(" ");
                        WriteMethodReturnType(writer, target.Method as MethodInfo);
                        writer.Write(" ");
                        if (target.Method.DeclaringType != null) {
                            TypeLink(writer, target.Method.DeclaringType);
                            writer.Write(".");
                        }
                        WriteMethodDeclaration(writer, target.Method as MethodInfo);
                    }
                } else {
                    writer.Write("unknown target @ " + callTarget.ToString("X16"));
                }
            } else {
                writer.Write("unsupported call, probably virtual");
            }
        }

        private void InspectMethod(HtmlWriter writer, Assembly assembly, Type type, MethodInfo method) {
            LayoutMethodHeader(writer, assembly, type, method);
            using (writer.Tag("h2")) {
                WriteMethodPrefix(writer, method);
                writer.Write(" ");
                WriteMethodReturnType(writer, method);
                writer.Write(" ");
                WriteMethodDeclaration(writer, method);
            }
            WriteDissassembly(writer, method);
        }

        private void InspectCtor(HtmlWriter writer, Assembly assembly, Type type, ConstructorInfo ctor) {
            LayoutMethodHeader(writer, assembly, type, ctor);
            using (writer.Tag("h2")) {
                WriteCtorPrefix(writer, ctor);
                writer.Write(" ");
                WriteCtorDeclaration(writer, ctor);
            }
            WriteDissassembly(writer, ctor);
        }

        private void WriteCtorPrefix(HtmlWriter writer, MethodBase c) {
            writer.Write(c.GetAccessModifier().Pretty());
        }

        private void WriteCtorDeclaration(HtmlWriter writer, ConstructorInfo c) {
            TypeLink(writer, c.DeclaringType, c.DeclaringType.Name);
            writer.Write(" ");
            FunctionLink(writer, c, c.Name);

            if (c.IsGenericMethodDefinition) {
                WriteGenericArguments(writer, c.GetGenericArguments());
            }

            writer.Write(" (");
            var ps = c.GetParameters();
            for (int i = 0; i < ps.Length; i++) {
                if (i > 0) {
                    writer.Write(", ");
                }
                WriteParameter(writer, ps[i]);
            }
            writer.Write(" )");

            if (c.IsGenericMethodDefinition) {
                var args = c.GetGenericArguments();
                for (int i = 0; i < args.Length; i++)
                    WriteGenericConstraints(writer, args[i]);
            }
        }

        private void WriteMethodReturnType(HtmlWriter writer, MethodInfo m) {
            if (m.ReturnType.IsByRef) {
                writer.Write("ref ");
                WriteTypeName(writer, m.ReturnType.GetElementType());
            } else {
                WriteTypeName(writer, m.ReturnType);
            }
        }

        private void WriteMethodDeclaration(HtmlWriter writer, MethodInfo m) {
            FunctionLink(writer, m, m.Name);

            if (m.IsGenericMethodDefinition) {
                WriteGenericArguments(writer, m.GetGenericArguments());
            }

            writer.Write(" ( ");
            var ps = m.GetParameters();
            for (int i = 0; i < ps.Length; i++) {
                if (i > 0) {
                    writer.Write(", ");
                }
                WriteParameter(writer, ps[i]);
            }
            writer.Write(" )");

            if (m.IsGenericMethodDefinition) {
                var args = m.GetGenericArguments();
                for (int i = 0; i < args.Length; i++)
                    WriteGenericConstraints(writer, args[i]);
            }
        }

        private void WriteMethodPrefix(HtmlWriter writer, MethodBase method) {
            writer.Write(method.GetAccessModifier().Pretty());
            if ((method.GetMethodImplementationFlags() & MethodImplAttributes.InternalCall) != 0)
                writer.Write(" extern");
            else if (method.IsAbstract) {
                writer.Write(" abstract");
            } else if (method.IsFinal) {
                writer.Write(" sealed");
            } else if (method.IsVirtual) {
                writer.Write(" virtual");
            } else if (method.IsStatic) {
                writer.Write(" static");
            }
        }
    }
}