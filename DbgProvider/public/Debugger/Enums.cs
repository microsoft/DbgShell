using System;
using Microsoft.Diagnostics.Runtime.Interop;

namespace MS.Dbg
{
    public enum ResumeType
    {
        Go = 0,
        GoHandled,
        GoNotHandled,
        StepBranch,
        StepInto,
        StepOver,
        ReverseGo,
        ReverseStepBranch,
        ReverseStepInto,
        ReverseStepOver,

        // These are "virtual"--they don't actually match up to a DEBUG_STATUS value.
        StepOut,
        StepToCall,
        Passive, // used when we don't want to actually change execution status; we just need to WaitForEvent.
        GoConditional, // completely fake
    } // end enum ResumeType


    [Flags]
    public enum DbgAssemblyOptions : uint
    {
        Default           = DEBUG_ASMOPT.DEFAULT,             //0x00000000,
        Verbose           = DEBUG_ASMOPT.VERBOSE,             //0x00000001,
        NoCodeBytes       = DEBUG_ASMOPT.NO_CODE_BYTES,       //0x00000002,
        IgnoreOutputWidth = DEBUG_ASMOPT.IGNORE_OUTPUT_WIDTH, //0x00000004,
        SourceLineNumber  = DEBUG_ASMOPT.SOURCE_LINE_NUMBER,  //0x00000008,
    } // end enum DbgAssemblyOptions


    [Flags]
    public enum GlobalSymbolCategory
    {
        Data     = 0x01,
        Function = 0x02,
        NoType   = 0x04,
        All = Data | Function | NoType
    }
}

