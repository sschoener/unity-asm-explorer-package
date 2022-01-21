using System;
using System.Collections.Generic;
using System.Linq;
using SharpDisasm;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;

namespace AsmExplorer.Profiler
{
    static class ProfilerDisassembler
    {
        public static List<string> FormatInstructions(ulong originalOffset, NativeArray<byte> blobData, int offset, int length, NativeSlice<SampleData> samples)
        {
            var instructions = GetInstructions(originalOffset, blobData, offset, length);
            var output = new List<string>();
            using (var samplesAtAddress = new NativeList<SampleAtAddress>(Allocator.TempJob))
            {
                new SortByAddressesJob
                {
                    Samples = samples,
                    SamplesAtAddress = samplesAtAddress
                }.Run();

                int totalSamples = 0;
                for (int i = 0; i < samplesAtAddress.Length; i++)
                    totalSamples += samplesAtAddress[i].NumSamples;
                output.Add($"{totalSamples} samples in total");

                int maxSampleLength = totalSamples.ToString().Length;
                int maxLength = maxSampleLength + " - xxx.x%".Length;

                int sample = 0;
                int instr = 0;
                var whiteSpace = new string(' ', maxLength);
                for (;instr < instructions.Count; instr++)
                {
                    while (sample < samplesAtAddress.Length && samplesAtAddress[sample].Address < instructions[instr].Offset)
                        sample++;
                    if (sample >= samplesAtAddress.Length)
                        break;
                    string prefix = whiteSpace;
                    if (samplesAtAddress[sample].Address == instructions[instr].Offset)
                    {
                        int numSamples = samplesAtAddress[sample].NumSamples;
                        float p = numSamples / (float) totalSamples;
                        var percentage = (p * 100).ToString("0.0").PadLeft(5) + '%';
                        prefix = numSamples.ToString().PadRight(maxSampleLength) + " - " + percentage;
                    }
                    output.Add(prefix + " | " + instructions[instr]);
                }

                for (;instr < instructions.Count; instr++)
                    output.Add(whiteSpace + " | " + instructions[instr]);
            }
            return output;
        }

        struct SampleAtAddress
        {
            public ulong Address;
            public int NumSamples;
        }

        [BurstCompile]
        struct SortByAddressesJob : IJob
        {
            public NativeSlice<SampleData> Samples;
            public NativeList<SampleAtAddress> SamplesAtAddress;
            public void Execute()
            {
                Samples.Sort(new ByAddressSorter());

                if (Samples.Length == 0)
                    return;
                var lastAddress = Samples[0].Address;
                int count = 1;
                for (int i = 1; i < Samples.Length; i++)
                {
                    if (Samples[i].Address == lastAddress)
                        count++;
                    else
                    {
                        SamplesAtAddress.Add(new SampleAtAddress
                        {
                            Address = (ulong) lastAddress,
                            NumSamples = count
                        });
                        lastAddress = Samples[i].Address;
                        count = 1;
                    }
                }

                SamplesAtAddress.Add(new SampleAtAddress
                {
                    Address = (ulong) lastAddress,
                    NumSamples = count
                });
            }

            struct ByAddressSorter : IComparer<SampleData>
            {
                public int Compare(SampleData x, SampleData y) => x.Address.CompareTo(y.Address);
            }
        }

        public static List<Instruction> GetInstructions(ulong originalOffset, NativeArray<byte> blobData, int offset, int length)
        {
            Debug.Assert(offset >= 0);
            Debug.Assert(length >= 0);
            Debug.Assert(blobData.Length >= offset + length);
            Disassembler.Translator.IncludeBinary = true;
            Disassembler.Translator.IncludeAddress = true;
            ArchitectureMode mode = ArchitectureMode.x86_64;
            if (IntPtr.Size == 4)
                mode = ArchitectureMode.x86_32;
            unsafe
            {
                var codeStart = (IntPtr) ((byte*) blobData.GetUnsafePtr() + offset);
                var disasm = new Disassembler(codeStart, length, mode, originalOffset, true, Vendor.Intel);
                return disasm.Disassemble().ToList();
            }
        }
    }
}