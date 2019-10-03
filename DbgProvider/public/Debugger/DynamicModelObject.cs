using System;
using System.Linq;
using System.Collections.Generic;
using System.Dynamic;
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
    public abstract class DynamicModelObject : DynamicObject
    {
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

        protected DynamicModelObject( string name,
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
        } // end constructor


        public static DynamicModelObject CreateModelObject( string name,
                                                     IntPtr ptrMO,
                                                     IntPtr ptrKeyStore )
        {
            return DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread( () =>
            {
                _CheckHr( WModelObject.GetKind( ptrMO, out ModelObjectKind kind ) );

                switch( kind )
                {
                    case ModelObjectKind.ObjectSynthetic:
                        return new SyntheticDynamicModelObject( name, ptrMO, ptrKeyStore );

                    default:
                        // not implemented
                        return null;
                }
            } );
        }


        public override IEnumerable< string > GetDynamicMemberNames()
        {
            _CheckHr( WModelObject.EnumerateKeyValues( m_ptrMO, out var keyEnumerable  ) );

            foreach( var subThing in keyEnumerable  )
            {
                if( !String.IsNullOrEmpty( subThing.Item1 ) )
                {
                    yield return subThing.Item1;
                }
            } // end foreach( subThing )
        }

        public override bool TryGetMember( GetMemberBinder binder, out object result )
        {
            IntPtr pVal;
            IntPtr pKeyStore;
            int hr = WModelObject.GetKeyValue( m_ptrMO, binder.Name, out pVal, out pKeyStore );

            if( 0 == hr )
            {
                result = DynamicModelObject.CreateModelObject( binder.Name, pVal, pKeyStore );
                return true;
            }

            result = null;

            // Extended error information may be returned when hr is a failure code, so
            // free everything no matter what.
            WModelObject.Release( pVal );
            WModelObject.Release( pKeyStore );

            if( (hr != DebuggerObject.E_BOUNDS) && (hr != DebuggerObject.E_NOT_SET) )
            {
                _CheckHr( hr );
            }

            return false;
        }
    } // end class DynamicModelObject

    public class SyntheticDynamicModelObject : DynamicModelObject
    {
        internal SyntheticDynamicModelObject( string name, IntPtr ptrMO, IntPtr ptrKeyStore )
            : base( name, ptrMO, ptrKeyStore, ModelObjectKind.ObjectSynthetic )
        {
            _CheckHr( WModelObject.EnumerateKeyValues( ptrMO, out var keyEnumerable  ) );

            foreach( var subThing in keyEnumerable  )
            {
                _CheckHr( WModelObject.GetKind( subThing.Item2, out ModelObjectKind kind ) );
                if( kind == ModelObjectKind.ObjectSynthetic )
                {
                    WModelObject.AddRef( subThing.Item2 );

                 // WrappingPSObject.Properties.Add(
                 //     new PSLazyPropertyInfo( subThing.Item1,
                 //                             () =>
                 //                             {
                 //                                 return _LazyProduceSyntheticObject( subThing.Item1,
                 //                                                                     subThing.Item2,
                 //                                                                     subThing.Item3 );
                 //                             },
                 //                             "MS.Dbg.SyntheticDynamicModelObject" ) );
                }
                else if( kind == ModelObjectKind.ObjectMethod )
                {
                 // Func< object > del = () =>
                 // {
                 //     return "oh hai";
                 // };

                 // var pmi = new PSDbgMethodInfo( subThing.Item1,
                 //                                "MS.Dbg.ModelObject", // what should this be??
                 //                                del );

                 // WrappingPSObject.Methods.Add( pmi );
                }
                else if( kind == ModelObjectKind.ObjectIntrinsic )
                {
                    // TODO: how to clean /this/ addref up???
                    WModelObject.AddRef( subThing.Item2 );

                //  WrappingPSObject.Properties.Add(
                //      new PSLazyPropertyInfo( subThing.Item1,
                //                              () =>
                //                              {
                //                                  return _LazyProduceIntrinsicObject( subThing.Item1,
                //                                                                      subThing.Item2,
                //                                                                      subThing.Item3 );
                //                              },
                //                              "MS.Dbg.SyntheticDynamicModelObject" ) );
                }
            } // end foreach( subThing )
        } // end constructor


        private Dictionary< IntPtr, object > m_children = new Dictionary< IntPtr, object >();

        private SyntheticDynamicModelObject _LazyProduceSyntheticObject( string name,
                                                                  IntPtr ptrMO,
                                                                  IntPtr ptrKeyStore )
        {
            object mo = null;
            if( !m_children.TryGetValue( ptrMO, out mo ) )
            {
                DbgEngDebugger._GlobalDebugger.ExecuteOnDbgEngThread(
                    () => mo = new SyntheticDynamicModelObject( name, ptrMO, ptrKeyStore )
                );
                m_children.Add( ptrMO, mo );
            }
            return (SyntheticDynamicModelObject) mo;
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
    } // end class SyntheticDynamicModelObject
}

