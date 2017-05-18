﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;

using AtlusScriptLib.Common.Utilities;

namespace AtlusScriptLib.Disassemblers
{
    public class FlowScriptBinaryDisassembler : IDisposable
    {
        private bool mDisposed;
        private string mHeaderString = "This file was generated by AtlusScriptLib";
        private FlowScriptBinary mScript;
        private IDisassemblerTextOutput mOutput;     
        private int mInstructionIndex;

        public string HeaderString
        {
            get { return mHeaderString; }
            set { mHeaderString = value; }
        }

        private FlowScriptBinaryInstruction CurrentInstruction
        {
            get
            {
                if (mScript == null || mScript.TextSection == null || mScript.TextSection.Count == 0)
                    throw new InvalidDataException("Invalid state");

                return mScript.TextSection[mInstructionIndex];
            }
        }

        private FlowScriptBinaryInstruction? NextInstruction
        {
            get
            {
                if (mScript == null || mScript.TextSection == null || mScript.TextSection.Count == 0)
                    return null;

                if ((mInstructionIndex + 1) < (mScript.TextSection.Count - 1))
                    return mScript.TextSection[mInstructionIndex + 1];
                else
                    return null;
            }
        }

        public FlowScriptBinaryDisassembler(StringBuilder stringBuilder)
        {
            mOutput = new StringBuilderDisassemblerTextOutput(stringBuilder);
        }

        public FlowScriptBinaryDisassembler(TextWriter writer)
        {
            mOutput = new TextWriterDisassemblerTextOutput(writer);
        }

        public FlowScriptBinaryDisassembler(string outpath)
        {
            mOutput = new TextWriterDisassemblerTextOutput(new StreamWriter(outpath));
        }

        public FlowScriptBinaryDisassembler(Stream stream)
        {
            mOutput = new TextWriterDisassemblerTextOutput(new StreamWriter(stream));
        }

        public void Disassemble(FlowScriptBinary script)
        {
            mScript = script ?? throw new ArgumentNullException(nameof(script));
            mInstructionIndex = 0;

            PutDisassembly();
        }

        private void PutDisassembly()
        {
            PutHeader();
            PutTextDisassembly();
            PutMessageScriptDisassembly();
        }

        private void PutHeader()
        {
            mOutput.PutCommentLine(mHeaderString);
            mOutput.PutNewline();
        }

        private void PutTextDisassembly()
        {
            mOutput.PutLine(".text");

            while (mInstructionIndex < mScript.TextSection.Count)
            {
                // Check if there is a possible jump label at the current index
                foreach (var jump in mScript.JumpLabelSection.Where(x => x.InstructionIndex == mInstructionIndex))
                {
                    mOutput.PutLine($"{jump.Name}:");
                }

                PutInstructionDisassembly();

                if (OpcodeUsesExtendedOperand(CurrentInstruction.Opcode))
                {
                    mInstructionIndex += 2;
                }
                else
                {
                    mInstructionIndex += 1;
                }
            }

            mOutput.PutNewline();
        }

        private bool OpcodeUsesExtendedOperand(FlowScriptOpcode opcode)
        {
            if (opcode == FlowScriptOpcode.PUSHI || opcode == FlowScriptOpcode.PUSHF)
                return true;

            return false;
        }

        private void PutInstructionDisassembly()
        {
            switch (CurrentInstruction.Opcode)
            {
                // extended int operand
                case FlowScriptOpcode.PUSHI:
                    mOutput.PutLine(DisassembleInstructionWithIntOperand(CurrentInstruction, NextInstruction.Value));
                    break;

                // extended float operand
                case FlowScriptOpcode.PUSHF:
                    mOutput.PutLine(DisassembleInstructionWithFloatOperand(CurrentInstruction, NextInstruction.Value));
                    break;

                // short operand
                case FlowScriptOpcode.PUSHIX:
                case FlowScriptOpcode.PUSHIF:
                case FlowScriptOpcode.POPIX:
                case FlowScriptOpcode.POPFX:
                case FlowScriptOpcode.RUN:
                case FlowScriptOpcode.PUSHIS:
                case FlowScriptOpcode.PUSHLIX:
                case FlowScriptOpcode.PUSHLFX:
                case FlowScriptOpcode.POPLIX:
                case FlowScriptOpcode.POPLFX:
                    mOutput.PutLine(DisassembleInstructionWithShortOperand(CurrentInstruction));
                    break;

                // string opcodes
                case FlowScriptOpcode.PUSHSTR:
                    mOutput.PutLine(DisassembleInstructionWithStringReferenceOperand(CurrentInstruction, mScript.StringSection));
                    break;

                // branch procedure opcodes
                case FlowScriptOpcode.PROC:
                case FlowScriptOpcode.CALL:
                    mOutput.PutLine(DisassembleInstructionWithLabelReferenceOperand(CurrentInstruction, mScript.ProcedureLabelSection));
                    break;

                // branch jump opcodes
                case FlowScriptOpcode.JUMP:           
                case FlowScriptOpcode.GOTO:
                case FlowScriptOpcode.IF:
                    mOutput.PutLine(DisassembleInstructionWithLabelReferenceOperand(CurrentInstruction, mScript.JumpLabelSection));
                    break;

                // branch communicate opcode
                case FlowScriptOpcode.COMM:
                    mOutput.PutLine(DisassembleInstructionWithCommReferenceOperand(CurrentInstruction));
                    break;

                // No operands
                case FlowScriptOpcode.PUSHREG:          
                case FlowScriptOpcode.ADD:
                case FlowScriptOpcode.SUB:               
                case FlowScriptOpcode.MUL:
                case FlowScriptOpcode.DIV:
                case FlowScriptOpcode.MINUS:
                case FlowScriptOpcode.NOT:
                case FlowScriptOpcode.OR:
                case FlowScriptOpcode.AND:
                case FlowScriptOpcode.EQ:
                case FlowScriptOpcode.NEQ:
                case FlowScriptOpcode.S:
                case FlowScriptOpcode.L:
                case FlowScriptOpcode.SE:
                case FlowScriptOpcode.LE:
                    mOutput.PutLine(DisassembleInstructionWithNoOperand(CurrentInstruction));
                    break;

                case FlowScriptOpcode.END:
                    mOutput.PutLine(DisassembleInstructionWithNoOperand(CurrentInstruction));
                    if (NextInstruction.HasValue)
                    {
                        if (NextInstruction.Value.Opcode != FlowScriptOpcode.END)
                            mOutput.PutNewline();
                    }
                    break;

                default:
                    DebugUtils.FatalException($"Unknown opcode {CurrentInstruction.Opcode}");
                    break;
            }
        }

        private void PutMessageScriptDisassembly()
        {
            mOutput.PutLine(".msgdata raw");
            for (int i = 0; i < mScript.MessageScriptSection.Count; i++)
            {
                mOutput.Put(mScript.MessageScriptSection[i].ToString("X2"));
            }
        }

        public static string DisassembleInstructionWithNoOperand(FlowScriptBinaryInstruction instruction)
        {
            if (instruction.OperandShort != 0)
            {
                DebugUtils.TraceError($"{instruction.Opcode} should not have any operands");
            }

            return $"{instruction.Opcode}";
        }

        public static string DisassembleInstructionWithIntOperand(FlowScriptBinaryInstruction instruction, FlowScriptBinaryInstruction operand)
        {
            return $"{instruction.Opcode} {operand.OperandInt}";
        }

        public static string DisassembleInstructionWithFloatOperand(FlowScriptBinaryInstruction instruction, FlowScriptBinaryInstruction operand)
        {
            return $"{instruction.Opcode} {operand.OperandFloat.ToString("0.00#####", CultureInfo.InvariantCulture)}f";
        }

        public static string DisassembleInstructionWithShortOperand(FlowScriptBinaryInstruction instruction)
        {
            return $"{instruction.Opcode} {instruction.OperandShort}";
        }

        public static string DisassembleInstructionWithStringReferenceOperand(FlowScriptBinaryInstruction instruction, IList<byte> stringTable)
        {
            string value = string.Empty;
            for (int i = instruction.OperandShort; i < stringTable.Count; i++)
            {
                if (stringTable[i] == 0)
                    break;

                value += (char)stringTable[i];
            }

            return $"{instruction.Opcode} \"{value}\"";
        }

        public static string DisassembleInstructionWithLabelReferenceOperand(FlowScriptBinaryInstruction instruction, IList<FlowScriptBinaryLabel> labels)
        {
            if (instruction.OperandShort >= labels.Count)
            {
                DebugUtils.FatalException($"No label for label reference id {instruction.OperandShort} present in {nameof(labels)}");
            }

            return $"{instruction.Opcode} {labels[instruction.OperandShort].Name}";
        }

        public static string DisassembleInstructionWithCommReferenceOperand(FlowScriptBinaryInstruction instruction)
        {
            return $"{instruction.Opcode} {instruction.OperandShort}";
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (mDisposed)
                return;

            mOutput.Dispose();
            mDisposed = true;
        }
    }
}
