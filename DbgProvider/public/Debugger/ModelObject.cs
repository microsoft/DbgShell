using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Diagnostics.Runtime.Interop;
using DbgEngWrapper;

namespace MS.Dbg
{
    // INTd12d3285 prevented us from deriving from PSObject (wherein such objects, piped
    // to Get-Member, cause a hang). Does that still apply?
    public abstract class ModelObject
    {
        internal PSObject WrappingPSObject { get; private set; }

        private string m_name;
        private IntPtr m_ptrMO;
        private IntPtr m_ptrKeyStore;
        private readonly ulong m_execStatusCookie;

        // TODO: is it useful to track parent?

        public string Name => m_name;

        public ModelObjectKind Kind { get; private set; }

        /// <summary>
        ///     The value of the debugger's ExecStatusCookie at the time the ModelObject
        ///     object was created.
        /// </summary>
        public ulong DbgGetExecStatusCookie() { return m_execStatusCookie; }

        // TODO: pull this from somewhere else?
        protected static void _CheckHr( int hr )
        {
            if( 0 != hr )
                throw new DbgEngException( hr );
        }

        protected ModelObject( string name,
                               IntPtr ptrMO,
                               IntPtr ptrKeyStore,
                               ModelObjectKind kind )
        {
            m_name = name;
            // No: we AddRef as soon as it enters our world, which may be a while before
            // we hit this constructor (because we store the ptrs and lazily create
            // objects).
            // WModelObject.AddRef( ptrMO );
            m_ptrMO = ptrMO;
            Kind = kind;
            m_execStatusCookie = 0; // TODO: get the debugger

            // TODO: addref and store ptrKeyStore

            WrappingPSObject = new PSObject( this );
        } // end constructor


        public static ModelObject CreateModelObject( string name,
                                                     IntPtr ptrMO,
                                                     IntPtr ptrKeyStore )
        {
            return DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
            {
                _CheckHr( WModelObject.GetKind( ptrMO, out ModelObjectKind kind ) );

                switch( kind )
                {
                    case ModelObjectKind.ObjectSynthetic:
                        return new SyntheticModelObject( name, ptrMO, ptrKeyStore );

                    default:
                        // not implemented
                        return null;
                }
            } );
        }
    } // end class ModelObject

    public class SyntheticModelObject : ModelObject
    {
        internal SyntheticModelObject( string name, IntPtr ptrMO, IntPtr ptrKeyStore )
            : base( name, ptrMO, ptrKeyStore, ModelObjectKind.ObjectSynthetic )
        {
            _CheckHr( WModelObject.EnumerateKeyValues( ptrMO, out var keyEnumerable  ) );

            foreach( var subThing in keyEnumerable  )
            {
                _CheckHr( WModelObject.GetKind( subThing.Item2, out ModelObjectKind kind ) );
                if( kind == ModelObjectKind.ObjectSynthetic )
                {
                    WModelObject.AddRef( subThing.Item2 );

                    WrappingPSObject.Properties.Add(
                        new PSLazyPropertyInfo( subThing.Item1,
                                                () =>
                                                {
                                                    return _LazyProduceSyntheticObject( subThing.Item1,
                                                                                        subThing.Item2,
                                                                                        subThing.Item3 );
                                                },
                                                "MS.Dbg.SyntheticModelObject" ) );
                }
                else if( kind == ModelObjectKind.ObjectMethod )
                {
                    Func< object > del = () =>
                    {
                        return "oh hai";
                    };

                    var pmi = new PSDbgMethodInfo( subThing.Item1,
                                                   "MS.Dbg.ModelObject", // what should this be??
                                                   del );

                    WrappingPSObject.Methods.Add( pmi );
                }
                else if( kind == ModelObjectKind.ObjectIntrinsic )
                {
                    // TODO: how to clean /this/ addref up???
                    WModelObject.AddRef( subThing.Item2 );

                    WrappingPSObject.Properties.Add(
                        new PSLazyPropertyInfo( subThing.Item1,
                                                () =>
                                                {
                                                    return _LazyProduceIntrinsicObject( subThing.Item1,
                                                                                        subThing.Item2,
                                                                                        subThing.Item3 );
                                                },
                                                "MS.Dbg.SyntheticModelObject" ) );
                }
            } // end foreach( subThing )
        } // end constructor


        private Dictionary< IntPtr, object > m_children = new Dictionary< IntPtr, object >();

        private SyntheticModelObject _LazyProduceSyntheticObject( string name,
                                                                  IntPtr ptrMO,
                                                                  IntPtr ptrKeyStore )
        {
            object mo = null;
            if( !m_children.TryGetValue( ptrMO, out mo ) )
            {
                DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread(
                    () => mo = new SyntheticModelObject( name, ptrMO, ptrKeyStore )
                );
                m_children.Add( ptrMO, mo );
            }
            return (SyntheticModelObject) mo;
        }

        private object _LazyProduceIntrinsicObject( string name,
                                                    IntPtr ptrMO,
                                                    IntPtr ptrKeyStore )
        {
            object mo = null;
            if( !m_children.TryGetValue( ptrMO, out mo ) )
            {
                DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread(
                    () => _CheckHr( WModelObject.GetIntrinsicValue( ptrMO, out mo ) )
                );
                m_children.Add( ptrMO, mo );
            }
            return mo;
        }
    } // end class SyntheticModelObject



    internal class PSModelObjectMethodInfo : PSMethodInfo
    {
        // Even if /we/ supported overloads, the debugger data model doesn't.

        private Delegate m_delegate;
        private string m_valTypeName;

        public PSModelObjectMethodInfo( string methodName, string valTypeName, Delegate methodDelegate )
        {
            if( String.IsNullOrEmpty( methodName ) )
                throw new ArgumentException( "You must supply a method name.", "methodName" );

            if( String.IsNullOrEmpty( valTypeName ) )
                throw new ArgumentException( "You must supply a value type name.", "valTypeName" );

            if( null == methodDelegate )
                throw new ArgumentNullException( "methodDelegate" );

            m_delegate = methodDelegate;
            m_valTypeName = valTypeName;
            SetMemberName( methodName );
        } // end constructor


        public override object Invoke( params object[] arguments )
        {
            return m_delegate.DynamicInvoke( arguments );
        }

        public override Collection< string > OverloadDefinitions
        {
            get { return new Collection< string >() { Util.Sprintf( "{0} {1}()", TypeNameOfValue, Name ) }; }
        }

        public override PSMemberInfo Copy()
        {
            return new PSDbgMethodInfo( Name, m_valTypeName, m_delegate );
        }

        public override PSMemberTypes MemberType
        {
            get { return PSMemberTypes.CodeMethod; }
        }

        public override string TypeNameOfValue
        {
            get { return m_valTypeName; }
        }
    } // end class PSDbgMethodInfo
}

