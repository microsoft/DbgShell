using System;

namespace MS.Dbg
{
    /// <summary>
    ///    Represents a line of disassembly.
    /// </summary>
    public class DbgDisassembly : ISupportColor
    {
        public readonly ulong Address;
        public readonly byte[] CodeBytes;
        public readonly string Instruction;
        public readonly string Arguments;
        public readonly ColorString BlockId;

        private ColorString m_colorString;

        internal DbgDisassembly( ulong address,
                                 byte[] codeBytes,
                                 string instruction,
                                 string arguments,
                                 ColorString blockId,
                                 ColorString colorString )
        {
            if( 0 == address )
                throw new ArgumentOutOfRangeException( "address", address, "There shouldn't be any code at address 0." );

            if( String.IsNullOrEmpty( instruction ) )
                throw new ArgumentException( "You must supply an instruction.", "instruction" );

            if( String.IsNullOrEmpty( arguments ) )
                arguments = null; // standardize on null

            if( String.IsNullOrEmpty( blockId ) )
                blockId = null;

            if( null == colorString )
                throw new ArgumentNullException( "colorString" );

            Address = address;
            CodeBytes = codeBytes;
            Instruction = instruction;
            Arguments = arguments;
            BlockId = blockId;
            m_colorString = colorString;
        } // constructor


        public ColorString ToColorString()
        {
            return m_colorString;
        } // end ToColorString()


        public override string ToString()
        {
            return m_colorString.ToString( false );
        } // end ToString()
    } // end class DbgDisassembly
}

