using System;

namespace MS.Dbg
{
    // TODO: IEquatable?
    public abstract class DbgFunction : DebuggerObject, ISupportColor
    {
        public readonly DbgEngContext Context;


        protected DbgFunction( DbgEngDebugger debugger,
                               DbgEngContext context,
                               ulong address,
                               string name )
            : base( debugger )
        {
            if( String.IsNullOrEmpty( name ) )
                throw new ArgumentNullException( "You must specify a name.", "name" );

            if( null == context )
                throw new ArgumentNullException( "context" );

            // We don't actually want thread/frame info in the context, I think. We just
            // want enough to identify what address space it refers to.
            Context = context.AsProcessContext();
            Address = address;
            Name = name;
        } // end constructor()


        public readonly string Name;

        public readonly ulong Address;


        public string ModuleQualifiedName
        {
            get
            {
                if( null == Module )
                    // I used to return "<unknown>!" + Name, but it made it "blend in" too
                    // much in stack traces--I want module-less frames to stick out more.
                    return Name;
                else
                    return Module.Name + "!" + Name;
            }
        }

        // Size would be nice, but DesktopMethod does not have it. Maybe it could be
        // added? Or maybe I can get it from the -3 entry in the ILOffsetMap
        //public readonly uint Size;

        // Should I require this to be passed into the constructor, too?
        // Actually, maybe not all functions should have a module--what about jscript
        // frames?
        public abstract DbgModuleInfo Module { get; }


        public abstract ColorString ToColorString();

    } // end class DbgFunction
}
