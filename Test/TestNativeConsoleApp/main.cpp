#include "stdafx.h"
#include <map>
#include <string>
#include <vector>
#include <functional>
#include <set>
#include <list>
#include <forward_list>
#include <memory>
#include <Windows.h>
#include <unordered_map>
#include <strsafe.h>

using namespace std;


int g_SomeInts[ 5 ] = { 0, 1, 2, 3, 4 };
int g_SomeInts2[ 5 ][ 4 ] =
{
    {  0,  1,  2,  3 },
    { 10, 11, 12, 13 },
    { 20, 21, 22, 23 },
    { 30, 31, 32, 33 },
    { 40, 41, 42, 43 },
};

auto g_pSomeInts2 = &g_SomeInts2;
auto g_ppSomeInts2 = &g_pSomeInts2;

int (*g_pSomeIntsNull)[5][4] = nullptr;
int (**g_ppSomeIntsNull)[5][4] = &g_pSomeIntsNull;


LPSTR g_narrowString = nullptr;
LPWSTR g_wideString = nullptr;


LPSTR g_strAtAmbiguousAddress = nullptr;

typedef struct _ODD_THING
{
    union
    {
        UINT64 u64;
        bool b;
    } u1;
    int b1 : 1;
    int b2 : 2;
    int b3 : 4;
    int b4 : 5;
                   // <-- bits intentionally left out
    bool cppBool;
} ODD_THING, *PODD_THING;

ODD_THING g_ot[ 3 ] =
{
    { 0xffffffffffffffff,
      -1,
      -1,
      -1,
      true },
    { 0,
      0,
      0,
      0,
      false },
    { 0x0000002300000000,
      0,
      3,
      2,
      true },
};

ODD_THING* g_pOts = g_ot;


const int g_numImageElements = 0x100;
int* g_pImageData = new int[ g_numImageElements ];

PLONG* g_pIntsIncludingNulls = new PLONG[ 10 ];


typedef struct _SIMPLE_THING
{
    int I;
} SIMPLE_THING, *PSIMPLE_THING;


SIMPLE_THING*** g_pppSimpleThings = nullptr;

int g_pppSimpleThings_count = 3;
int g_ppSimpleThings_count = 4;
int g_pSimpleThings_count = 5;

// Case-insensitive less.
template< class T >
struct ci_less : binary_function< T, T, bool >
{
    bool operator()( T const& x, T const& y ) const;
};

template<>
struct ci_less< const wchar_t* >
{
    bool operator()( const wchar_t* const& x, const wchar_t* const& y ) const
    {
        return _wcsicmp( x, y ) < 0;
    }
};

/* I'm not sure why this doesn't work.
template<>
struct ci_less< wstring > : ci_less< const wchar_t* >
{
    bool operator()( wstring const& x, wstring const& y ) const
    {
        return (*this)( x.c_str(), y.c_str() );
    }
};
*/

template<>
struct ci_less< wstring >
{
    bool operator()( wstring const& x, wstring const& y ) const
    {
        return _wcsicmp( x.c_str(), y.c_str() ) < 0;
    }
};

typedef function<int ( vector<wstring>& )> Routine;
// I was going to use an unordered_map (which is a hashtable), but there didn't seem
// to be anything pre-made I could easily use as a case-insensitive hash function.
//typedef unordered_map< wstring, Routine, hash<wstring> > RoutineMap;
typedef map< wstring, Routine, ci_less< wstring > > RoutineMap;


#pragma optimize( "ts", on )
struct TinyType
{
    short S;
    BYTE B1;
    BYTE B2;
};

struct TinyType2
{
    short S;
};

/*
TinyType __fastcall _Foo( TinyType t )
{
    t.B2 = t.B2 + 1;
    return t;
}
*/
TinyType2 __fastcall _Foo( TinyType2 t )
{
    return t;
}

int _CallFoo( vector< wstring >& args )
{
 // TinyType t;
 // t.S = 5;
 // t.B1 = 6;
 // t.B2 = 7;
    TinyType2 t;
    t.S = 5;
    return _Foo( t ).S;
}

__declspec( dllexport ) // prevents it from being optimized away on FRE builds
int FFE0();

int _CallFFE0( vector< wstring >& args )
{
    return FFE0();
}

CRITICAL_SECTION g_cs;

int _LockCritSec( vector< wstring >& args )
{
	wprintf( L"Outside CRITICAL_SECTION.\n" );

	EnterCriticalSection( &g_cs );

	wprintf( L"Inside CRITICAL_SECTION.\n" );

	LeaveCriticalSection( &g_cs );

	return 0;
}
#pragma optimize( "", off ) // go back to default



enum TypeTag
{
    Type1Tag = 0,
    Type2Tag,
    Type3Tag
};

class Base
{
private:
    TypeTag m_typeTag;
    wstring m_name;

    static const int s_constStaticInt = 0xccc;

protected:
    Base( TypeTag typeTag, PCWSTR name )
        : m_typeTag( typeTag ),
          m_name( name )
    {
    }

public:
    TypeTag get_TypeTag() const
    {
        return m_typeTag;
    }

    wstring get_Name() const
    {
        return m_name;
    }

    static int s_staticInt;
};

int Base::s_staticInt = 0x999;

class Type1 : public Base
{
private:
    map< int, wstring > m_map;

public:
    Type1( PCWSTR name ) : Base( Type1Tag, name )
    {
        m_map[ 0 ] = L"zero";
        m_map[ 1 ] = L"one";
        m_map[ 2 ] = L"two";
    }
};


class Type2 : public Base
{
private:
    vector< wstring > m_vector;

public:
    Type2( PCWSTR name ) : Base( Type2Tag, name )
    {
        m_vector.push_back( L"zero" );
        m_vector.push_back( L"one" );
        m_vector.push_back( L"two" );
    }
};


class Type3 : public Base
{
private:
    int m_i;

public:
    Type3( PCWSTR name ) : Base( Type3Tag, name )
    {
        m_i = 42;
    }
};


Base* g_polymorphicThings[ 3 ] = { nullptr, nullptr, nullptr };


class AbstractBase1
{
protected:
    AbstractBase1() { }
    virtual ~AbstractBase1() { }

public:
    virtual void AB1_M1() = 0;
}; // end class AbstractBase1

class VirtualBase1
{
protected:
    VirtualBase1() { }
    virtual ~VirtualBase1() { }

public:
    virtual void VB1_M1()
    {
        wprintf( __FUNCTIONW__ L"\r\n" );
    }
}; // end class VirtualBase1


class MultiDerived1 : public AbstractBase1, public VirtualBase1
{
private:
    int m_i;

public:
    MultiDerived1() : m_i( 42 )
    {
    }

    ~MultiDerived1() { }

    virtual void VB1_M1()
    {
        // TODO: I forget, how do you call the base implementation?
        wprintf( __FUNCTIONW__ L"\r\n" );
    }

    virtual void AB1_M1()
    {
        wprintf( __FUNCTIONW__ L"\r\n" );
    }
}; // end class MultiDerived1

class DerivedFromMultiDerived1 : public MultiDerived1
{
private:
    ULONG m_myMember;

public:
    DerivedFromMultiDerived1() : m_myMember( 0x123 )
    {
    }

    ~DerivedFromMultiDerived1() { }

    virtual void VB1_M1()
    {
        // TODO: I forget, how do you call the base implementation?
        wprintf( __FUNCTIONW__ L"\r\n" );
    }

    virtual void AB1_M1()
    {
        wprintf( __FUNCTIONW__ L"\r\n" );
    }
};

class Derived1 : public VirtualBase1
{
private:
    int m_derivedMember;

public:
    Derived1() : m_derivedMember( 42 )
    {
    }
};


class Derived2 : public VirtualBase1
{
private:
    int m_derivedMember;

public:
    Derived2() : m_derivedMember( 42 )
    {
    }

    void VB1_M1()
    {
        wprintf( __FUNCTIONW__ L"\r\n" );
    }
};


struct Uniony
{
    union {
        int a;
        int b;
        int c;
    };
    union {
        short d;
        short e;
        short f;
    };
    union {
        bool g;
        bool h;
        bool i;
        bool j;
        bool k;
        bool L;
    };
    union {
        union {
            int m;
            int n;
            int o;
        };
        union {
            short p;
            short q;
            short r;
        };
        union {
            bool s;
            bool t;
            bool u;
            bool v;
            bool w;
            bool x;
            bool y;
        };
        int z;
    };
};


struct Uniony2
{
    union {
        struct {
            int a;
            int b;
            int c;
        };
        struct {
            int niu1;
            union {
                short d;
                short e;
                short f;
            };
            union {
                bool g;
                bool h;
                bool i;
                bool j;
                bool k;
                bool L;
            };
        }; // end struct
        struct {
            union {
                int m;
                int n;
                int o;
            };
            union {
                short p;
                short q;
                short r;
            };
            int niu2;
            union {
                bool s;
                bool t;
                bool u;
                bool v;
                bool w;
                bool x;
                bool y;
            };
            int z;
        };
    };
};


__declspec( dllexport ) // prevents it from being optimized away on FRE builds
MultiDerived1 g_md1;
__declspec( dllexport ) // prevents it from being optimized away on FRE builds
AbstractBase1* g_ab1 = &g_md1;

__declspec( dllexport ) // prevents it from being optimized away on FRE builds
DerivedFromMultiDerived1 g_dfmd1;

__declspec( dllexport ) // prevents it from being optimized away on FRE builds
Derived1 g_d1;
__declspec( dllexport ) // prevents it from being optimized away on FRE builds
VirtualBase1* g_vb1 = &g_d1;

__declspec( dllexport ) // prevents it from being optimized away on FRE builds
Derived2 g_d2;
__declspec( dllexport ) // prevents it from being optimized away on FRE builds
VirtualBase1* g_vb1_2 = &g_d2;

__declspec(dllexport) // prevents it from being optimized away on FRE builds
Uniony g_uniony = { 0 };

__declspec(dllexport) // prevents it from being optimized away on FRE builds
Uniony2 g_uniony2 = { 0 };


int g_anInt = 42;
int* g_pAnInt = &g_anInt;
int** g_ppAnInt = &g_pAnInt;
int*** g_pppAnInt = &g_ppAnInt;

class IndirectThing
{
public:
    IndirectThing** PpIT;

    IndirectThing() : PpIT( nullptr ) { }
    explicit IndirectThing( IndirectThing** ppIT ) : PpIT( ppIT ) { }
    ~IndirectThing() { }
};

IndirectThing g_innerIT;
IndirectThing g_middleIT;
IndirectThing g_outerIT;
IndirectThing* g_pInnerIT = &g_innerIT;
IndirectThing* g_pMiddleIT = &g_middleIT;


class HasVariants
{
private:
    UINT m_uint;
public:
    VARIANT v1;
    VARIANT v2;
    VARIANT v3;
    VARIANT v4;
    VARIANT v5;

    HasVariants()
    {
        VariantInit( &v1 );
        VariantInit( &v2 );
        VariantInit( &v3 );
        VariantInit( &v4 );

        v1.vt = VT_I1;
        v1.bVal = 21;

        v2.vt = VT_UI4;
        v2.uintVal = 21;

        v3.vt = VT_UI4 | VT_BYREF;
        m_uint = 0x123;
        v3.puintVal = &m_uint;

        v4.vt = VT_BSTR;
        v4.bstrVal = SysAllocString( L"This is my variant string." );

        // v5 left empty
    } // end constructor
}; // end class HasVariants

HasVariants g_hasVariants;



class NestingThing1
{
public:
	NestingThing1() : m_blah(0x42) { }

	UINT m_blah;
};

class NestingThing2
{
public:
	NestingThing1 m_n1;
};

class NestingThing3
{
public:
	NestingThing2 m_n2;
};

class NestingThing4
{
public:
	NestingThing3 m_n3;
};

NestingThing4 g_n4 = {};



class DtdNestingThing1Base
{
public:
	DtdNestingThing1Base() : m_myInt(0x99) { }

	UINT m_myInt;

	virtual void SayGreeting()
	{
		printf( "hello" );
	}
};

class DtdNestingThing1Derived : public DtdNestingThing1Base
{
public:
	DtdNestingThing1Derived() { }

	virtual void SayGreeting() override
	{
		DtdNestingThing1Base::SayGreeting();
		printf( ", eh" );
	}
};


class DtdNestingThing2Base
{
private:
	DtdNestingThing1Base* m_p1;

public:
	DtdNestingThing2Base( DtdNestingThing1Base* p1 ) : m_p1( p1 ) { }

	virtual void SayGreeting()
	{
		printf( "g'day" );
	}
};

class DtdNestingThing2Derived : public DtdNestingThing2Base
{
public:
	DtdNestingThing2Derived() : DtdNestingThing2Base( new DtdNestingThing1Derived() ) { }

	virtual void SayGreeting() override
	{
		DtdNestingThing2Base::SayGreeting();
		printf( ", mate" );
	}
};


class DtdNestingThing3Base
{
private:
	DtdNestingThing2Base* m_p2;

public:
	DtdNestingThing3Base( DtdNestingThing2Base* p2 ) : m_p2( p2 ) { }

	virtual void SayGreeting()
	{
		printf( "hello" );
	}
};

class DtdNestingThing3Derived : public DtdNestingThing3Base
{
public:
	DtdNestingThing3Derived() : DtdNestingThing3Base( new DtdNestingThing2Derived() ) { }

	virtual void SayGreeting() override
	{
		printf( "hola" );
	}
};


class DtdNestingThing4Base
{
private:
	DtdNestingThing3Base* m_p3;

public:
	DtdNestingThing4Base( DtdNestingThing3Base* p3 ) : m_p3( p3 ) { }

	virtual void SayGreeting()
	{
		printf( "hello" );
	}
};

class DtdNestingThing4Derived : public DtdNestingThing4Base
{
public:
	DtdNestingThing4Derived() : DtdNestingThing4Base( new DtdNestingThing3Derived() ) { }

	virtual void SayGreeting() override
	{
		printf( "hola" );
	}
};


DtdNestingThing4Base* g_pDtdNestingThing4 = new DtdNestingThing4Derived();



int _WaitEvent( vector< wstring >& args )
{
    if( 0 == args.size() )
    {
        wprintf( L"Error: %s: what event should I wait for?\n", __FUNCTIONW__ );
        return -1;
    }
    else if( args.size() > 1 )
    {
        wprintf( L"Error: %s: Too many arguments.\n", __FUNCTIONW__ );
        return -1;
    }

    HANDLE hEvent = (HANDLE) _wtoi64( args[ 0 ].c_str() );
    if( nullptr == hEvent )
    {
        wprintf( L"Error: %s: Invalid event value.\n", __FUNCTIONW__ );
        return -1;
    }

    wprintf( L"Waiting for event: %p\n", hEvent );

    DWORD waitResult;
    while( (waitResult = WaitForSingleObject( hEvent, 3000 )) == WAIT_TIMEOUT )
    {
        wprintf( L"Still waiting..." );
    }

    wprintf( L"Wait result: %#x\n", waitResult );

    return waitResult;
} // end _WaitEvent()


int _SetEvent( vector< wstring >& args )
{
    if( 0 == args.size() )
    {
        wprintf( L"Error: %s: what event should I set?\n", __FUNCTIONW__ );
        return -1;
    }
    else if( args.size() > 1 )
    {
        wprintf( L"Error: %s: Too many arguments.\n", __FUNCTIONW__ );
        return -1;
    }

    HANDLE hEvent = (HANDLE) _wtoi64( args[ 0 ].c_str() );
    if( nullptr == hEvent )
    {
        wprintf( L"Error: %s: Invalid event value.\n", __FUNCTIONW__ );
        return -1;
    }

    wprintf( L"Setting event: %p\n", hEvent );

    DWORD rc = 0;
    if( !SetEvent( hEvent ) )
        rc = GetLastError();

    wprintf( L"SetEvent result: %i\n", rc );

    return rc;
} // end _SetEvent()


int _Sleep( vector< wstring >& args )
{
    if( 0 == args.size() )
    {
        wprintf( L"Error: %s: How long should I sleep?\n", __FUNCTIONW__ );
        return -1;
    }
    else if( args.size() > 1 )
    {
        wprintf( L"Error: %s: Too many arguments.\n", __FUNCTIONW__ );
        return -1;
    }

    int millis = _wtoi( args[ 0 ].c_str() );

    if( (0 == millis) && errno )
    {
        wprintf( L"Error: %s: Bad argument (%i).\n", __FUNCTIONW__, errno );
        return errno;
    }

    wprintf( L"Sleeping for %i milliseconds.\n", millis );

    Sleep( millis );

    wprintf( L"Done sleeping.\n" );

    return 0;
} // end _Sleep()


void _TwoThreadGuTestWorkerInner( DWORD sleepMillis )
{
    DWORD dwTid = GetCurrentThreadId();
    wprintf( L"In _TwoThreadGuTestWorkerInner, on thread %u. Will sleep for %u millis.\n",
             dwTid,
             sleepMillis );

    if( sleepMillis > 1000 )
    {
        wprintf( L"This must be the slow thread...\n" );
        Sleep( 100 ); // Make sure the other thread has a chance to get off the ground.
        __debugbreak();
    }

    Sleep( sleepMillis );

    wprintf( L"_TwoThreadGuTestWorkerInner (thread %u returning).\n", dwTid );
}


DWORD WINAPI _TwoThreadGuTestWorker( LPVOID param )
{
#pragma warning( push )
#pragma warning( disable : 4311 ) // yes, we know we are truncating
#pragma warning( disable : 4302 ) // yes, we know we are truncating
    DWORD sleepMillis = reinterpret_cast<DWORD>(param);
#pragma warning( pop )
    DWORD dwTid = GetCurrentThreadId();

    _TwoThreadGuTestWorkerInner( sleepMillis );

    wprintf( L"_TwoThreadGuTestWorker thread %u exiting.\n", dwTid );
    return 0;
}


int _TwoThreadGuTest( vector< wstring >& args )
{
    DWORD otherFastThreadId = 0;
    // We'll have two threads that are both in the same function, a "fast" thread and a
    // "slow" thread (the "fast" thread will return from the function sooner). We'll
    // arrange for the debugger to break in on the "slow" thread, so that we can execute
    // "gu", and then make sure we don't stop when the "fast" thread returns.

    // We'll spawn a separate thread to be the "fast" thread.
    HANDLE hOtherFastThread = CreateThread( nullptr,
                                            0,
                                            _TwoThreadGuTestWorker,
                                            reinterpret_cast< LPVOID >( 1000 ),
                                            0,
                                            &otherFastThreadId );
    wprintf( L"\"Fast\" thread id is: %u\n", otherFastThreadId );
    wprintf( L"Current (\"slow\") thread id is: %u\n", GetCurrentThreadId() );

    _TwoThreadGuTestWorker( reinterpret_cast<LPVOID>( 2000 ) );
    return 0;
} // end _TwoThreadGuTest()


vector< int >       g_intVector;
vector< wstring >   g_wsVector;
vector< string >    g_sVector;
vector< bool >      g_bVector;
vector< bool >      g_bVectorEmpty;
map< int, wstring > g_intStringMap;
map< wstring, wstring > g_stringStringMap;
multimap< wstring, wstring > g_stringStringMultimap;

unordered_map< int, wstring > g_hm_00;
unordered_map< int, wstring > g_hm_01;
unordered_map< int, wstring > g_hm_02;
unordered_map< int, wstring > g_hm_03;
unordered_map< int, wstring > g_hm_04;
unordered_map< int, wstring > g_hm_05;
unordered_map< int, wstring > g_hm_06;
unordered_map< int, wstring > g_hm_07;
unordered_map< int, wstring > g_hm_08;
unordered_map< int, wstring > g_hm_09;
unordered_map< int, wstring > g_hm_10;
unordered_map< int, wstring > g_hm_11;
unordered_map< int, wstring > g_hm_12;

unordered_map< int, wstring >* g_hash_maps[] = {
    &g_hm_00,
    &g_hm_01,
    &g_hm_02,
    &g_hm_03,
    &g_hm_04,
    &g_hm_05,
    &g_hm_06,
    &g_hm_07,
    &g_hm_08,
    &g_hm_09,
    &g_hm_10,
    &g_hm_11,
    &g_hm_12
};


set< int >                              g_intSet0;
set< LPCWSTR, ci_less< LPCWSTR > >      g_wsSet1;
set< LPCWSTR, ci_less< LPCWSTR > >      g_wsSet2;
set< LPCWSTR, ci_less< LPCWSTR > >      g_wsSet3;
set< wstring, ci_less< wstring > >      g_wsSet4;
set< int >                              g_intSet50;
multiset< int >                         g_intMultiset10;

list< int > g_intList;
list< int >::iterator g_intListIter;
list< int >::iterator g_intListIterEnd;

list< int > g_emptyIntList;

forward_list< int > g_intForwardList;
forward_list< int > g_emptyIntForwardList;

unique_ptr< wchar_t > g_uniquePtr( new wchar_t[ 10 ] );

// Good for testing boundary conditions of the "small string optimization".
wstring g_wstrings[] = {
    L"",
    L"a",
    L"aa",
    L"aaa",
    L"aaaa",
    L"aaaaa",
    L"aaaaaa",
    L"aaaaaaa",
    L"aaaaaaaa",
    L"aaaaaaaaa",
    L"aaaaaaaaaa",
    L"aaaaaaaaaaa",
    L"aaaaaaaaaaaa",
    L"aaaaaaaaaaaaa",
    L"aaaaaaaaaaaaaa",
    L"aaaaaaaaaaaaaaa",
    L"aaaaaaaaaaaaaaaa",
    L"aaaaaaaaaaaaaaaaa",
    L"aaaaaaaaaaaaaaaaaa",
    L"aaaaaaaaaaaaaaaaaaa",
    L"aaaaaaaaaaaaaaaaaaaa",
    L"aaaaaaaaaaaaaaaaaaaaa",
};

string g_strings[] = {
    "",
    "a",
    "aa",
    "aaa",
    "aaaa",
    "aaaaa",
    "aaaaaa",
    "aaaaaaa",
    "aaaaaaaa",
    "aaaaaaaaa",
    "aaaaaaaaaa",
    "aaaaaaaaaaa",
    "aaaaaaaaaaaa",
    "aaaaaaaaaaaaa",
    "aaaaaaaaaaaaaa",
    "aaaaaaaaaaaaaaa",
    "aaaaaaaaaaaaaaaa",
    "aaaaaaaaaaaaaaaaa",
    "aaaaaaaaaaaaaaaaaa",
    "aaaaaaaaaaaaaaaaaaa",
    "aaaaaaaaaaaaaaaaaaaa",
    "aaaaaaaaaaaaaaaaaaaaa",
};


// Just need something
string g_oneString = "hello there";

// A variable with a name that looks like a number.
__declspec( dllexport ) // prevents it from being optimized away on FRE builds
LONG abcd = 0xabcd;

int _SomeOtherFunc()
{
    return 42;
}

__declspec( dllexport ) // prevents it from being optimized away on FRE builds
__forceinline int _PleaseInlineMe()
{
    wprintf( L"Inside _PleaseInlineMe\n" );
    return _SomeOtherFunc();
}

// A function with a name that looks like a number.
__declspec( dllexport ) // prevents it from being optimized away on FRE builds
int FFE0()
{
    return _PleaseInlineMe();
}

SYSTEM_INFO g_SysInfo = { 0 };

DWORD GetSystemPageSize()
{
    if( 0 == g_SysInfo.dwPageSize )
    {
        GetSystemInfo( &g_SysInfo );
        printf( "page size: %#x\n", g_SysInfo.dwPageSize );

        // Just need to do "something" with an STL string object, to prevent
        // the compiler/linker from optimizing too much of the structure away,
        // so that the test that verifies we can convert STL strings will work.
        g_oneString += " okay, now there is more stuff here";
    }

    return g_SysInfo.dwPageSize;
}


template<typename T>
void _AllocatePageBufferFollowedByNoAccessPage( T& result )
{
    DWORD pageSize = GetSystemPageSize();

    // Reserve two pages of PAGE_NOACCESS address space.
    auto buf = VirtualAlloc( 0, 2 * pageSize, MEM_COMMIT, PAGE_NOACCESS );
    if( !buf )
    {
        RaiseFailFastException( nullptr, nullptr, 0 );
    }

    // Commit the first page and make it read/write.
    auto commit = VirtualAlloc( buf, pageSize, MEM_COMMIT, PAGE_READWRITE );
    if( commit != buf )
    {
        RaiseFailFastException( nullptr, nullptr, 0 );
    }

    result = static_cast<T>( buf);
}


LPSTR AllocatePageAtAmbiguousAddress()
{
    LPVOID buf = nullptr;

    while( !buf )
    {
        // We need four digits in the range 0-9, which we'll then shift over, into the
        // form 0x0NNNN000.
        ULONGLONG digit1 = rand() % 9;
        ULONGLONG digit2 = rand() % 9;
        ULONGLONG digit3 = rand() % 9;
        ULONGLONG digit4 = rand() % 9;

        ULONGLONG candidateAddr = (digit1 << 0x18) |
                                  (digit3 << 0x14) |
                                  (digit2 << 0x10) |
                                  (digit1 << 0xc);

        if( !candidateAddr )
        {
            continue; // don't pass "0"; it means "give me whatever address is convenient"
        }

        printf( "candidateAddr: %#I64x\n", candidateAddr );

        buf = VirtualAlloc( reinterpret_cast<LPVOID>( candidateAddr ),
                                                      GetSystemPageSize(),
                                                      MEM_RESERVE | MEM_COMMIT,
                                                      PAGE_READWRITE );
        if( !buf )
        {
            printf( "GLE: %#x\n", GetLastError() );
        }
    }

    return static_cast<LPSTR>( buf );
}

void _InitGlobals()
{
	InitializeCriticalSection( &g_cs );

    for( int i = 0; i < g_numImageElements; i++ )
    {
        g_pImageData[ i ] = i;
    }

    g_pIntsIncludingNulls[ 0 ] = &abcd;
    g_pIntsIncludingNulls[ 1 ] = &abcd;
    g_pIntsIncludingNulls[ 2 ] = &abcd;
    g_pIntsIncludingNulls[ 3 ] = &abcd;
    g_pIntsIncludingNulls[ 4 ] = &abcd;
    g_pIntsIncludingNulls[ 5 ] = nullptr;
    g_pIntsIncludingNulls[ 6 ] = &abcd;
    g_pIntsIncludingNulls[ 7 ] = &abcd;
    g_pIntsIncludingNulls[ 8 ] = &abcd;
    g_pIntsIncludingNulls[ 9 ] = &abcd;


    _AllocatePageBufferFollowedByNoAccessPage( g_narrowString );

    g_narrowString[ 0 ] = 0;
    LPCSTR filler = "this is a narrow string. ";
    LPSTR cursor = g_narrowString;
    size_t len = GetSystemPageSize();
    HRESULT hr = S_OK;

    // Fill the buffer with some text.
    while( hr == S_OK )
    {
        hr = StringCchCatExA( cursor,
                              len,
                              filler,
                              &cursor,
                              &len,
                              0 ); // flags
    }

    _AllocatePageBufferFollowedByNoAccessPage( g_wideString );

    g_wideString[ 0 ] = 0;
    LPCWSTR wideFiller = L"this is a WIDE string. ";
    LPWSTR wideCursor = g_wideString;
    len = GetSystemPageSize() / sizeof( wchar_t );
    hr = S_OK;

    // Fill the buffer with some text.
    while( hr == S_OK )
    {
        hr = StringCchCatExW( wideCursor,
                              len,
                              wideFiller,
                              &wideCursor,
                              &len,
                              0 ); // flags
    }
    hr = S_OK;


    g_strAtAmbiguousAddress = AllocatePageAtAmbiguousAddress();

    hr = StringCchCopyA( g_strAtAmbiguousAddress,
                         GetSystemPageSize(),
                         "This string is at an ambiguous address." );
    if( hr )
    {
        RaiseFailFastException( nullptr, nullptr, 0 );
    }


    /*
SIMPLE_THING*** g_pppSimpleThings = nullptr;

int g_pppSimpleThings_count = 3;
int g_ppSimpleThings_count = 4;
int g_pSimpleThings_count = 5;
*/
    g_pppSimpleThings = new SIMPLE_THING**[ g_pSimpleThings_count ];
    for( int i = 0; i < g_pSimpleThings_count; i++ )
    {
        g_pppSimpleThings[ i ] = new SIMPLE_THING*[ g_ppSimpleThings_count ];
        for( int j = 0; j < g_ppSimpleThings_count; j++ )
        {
            g_pppSimpleThings[ i ][ j ] = new SIMPLE_THING[ g_pSimpleThings_count ];
            for( int k = 0; k < g_pSimpleThings_count; k++ )
            {
                g_pppSimpleThings[ i ][ j ][ k ].I = (i * 100) + (j * 10) + k;
            }
        }
    }

    g_intVector.push_back( 0 );
    g_intVector.push_back( 1 );
    g_intVector.push_back( 2 );
    g_intVector.push_back( 3 );
    g_intVector.reserve( 10 );

    g_wsVector.push_back( L"zero" );
    g_wsVector.push_back( L"one" );
    g_wsVector.push_back( L"two" );
    g_wsVector.push_back( L"three" );
    g_wsVector.reserve( 10 );

    g_sVector.push_back( "zero" );
    g_sVector.push_back( "one" );
    g_sVector.push_back( "two" );
    g_sVector.push_back( "three" );
    g_sVector.reserve( 10 );

    g_bVector.push_back( true );
    g_bVector.push_back( false );
    g_bVector.push_back( true );

    g_intStringMap[ 0 ] = L"zero";
    g_intStringMap[ 1 ] = L"one";
    g_intStringMap[ 2 ] = L"two";
    g_intStringMap[ 3 ] = L"three";

    g_stringStringMap[ L"zero" ] = L"nothing";
    g_stringStringMap[ L"one" ] = L"something";
    g_stringStringMap[ L"two" ] = L"a couple";
    g_stringStringMap[ L"three" ] = L"several";

    g_stringStringMultimap.insert( pair< wstring, wstring >( L"zero", L"nothing" ) );
    g_stringStringMultimap.insert( pair< wstring, wstring >( L"one", L"something" ) );
    g_stringStringMultimap.insert( pair< wstring, wstring >( L"two", L"a couple" ) );
    g_stringStringMultimap.insert( pair< wstring, wstring >( L"three", L"several" ) );
    g_stringStringMultimap.insert( pair< wstring, wstring >( L"zero", L"nothing again" ) );
    g_stringStringMultimap.insert( pair< wstring, wstring >( L"one", L"something again" ) );
    g_stringStringMultimap.insert( pair< wstring, wstring >( L"two", L"a couple again" ) );
    g_stringStringMultimap.insert( pair< wstring, wstring >( L"three", L"several again" ) );


    for( int i = 0; i < _countof( g_hash_maps ); i++ )
    {
        auto hm = g_hash_maps[ i ];
        for( int j = 0; j < i; j++ )
        {
            (*hm)[ j ] = wstring( j, 'z' );
        }
    }


    g_polymorphicThings[ 0 ] = new Type1( L"Polymorphic thing 1" );
    g_polymorphicThings[ 1 ] = new Type2( L"Polymorphic thing 2" );
    g_polymorphicThings[ 2 ] = new Type3( L"Polymorphic thing 3" );

    g_wsSet1.insert( L"zero" );

    g_wsSet2.insert( L"zero" );
    g_wsSet2.insert( L"one" );

    g_wsSet3.insert( L"zero" );
    g_wsSet3.insert( L"one" );
    g_wsSet3.insert( L"two" );

    g_wsSet4.insert( L"zero" );
    g_wsSet4.insert( L"one" );
    g_wsSet4.insert( L"two" );
    g_wsSet4.insert( L"three" );

    for( int i = 0; i < 50; i++ )
    {
        g_intSet50.insert( i );
    }

    for( int i = 0; i < 10; i++ )
    {
        g_intMultiset10.insert( i / 2 );
    }

    for( int i = 0; i < 10; i++ )
    {
        g_intList.push_back( i );
    }
    // TODO: Figure these out and come up with value converters for them.
    g_intListIter = g_intList.begin();
    g_intListIterEnd = g_intList.end();

    for( int i = 0; i < 10; i++ )
    {
        g_intForwardList.push_front( i );
    }

    wcscpy_s( g_uniquePtr.get(), 10, L"abcdefghi" );

    g_outerIT.PpIT = &g_pMiddleIT;
    g_middleIT.PpIT = &g_pInnerIT;
} // end _InitGlobals()


void _Takes_a_ref( int& refInt )
{
    refInt = 42;
    //__debugbreak();
}


template< typename T >
class Foo
{
public:
    void Blah( T refT )
    {
        wprintf( L"Address of refT: %p\n", &refT );
    }
};


enum class SomeEnum : unsigned int
{
	None = 0,
	FirstBit  = 0x01,
	SecondBit = 0x02,
	ThirdBit  = 0x04,
	FourthBit = 0x08,
	FifthBit  = 0x10,
};


int wmain( int numArgs, wchar_t* args[] )
{
	SomeEnum se = (SomeEnum) ((unsigned int) SomeEnum::FifthBit | (unsigned int) SomeEnum::SecondBit);

	wprintf( L"Hi. This is the native test app. se: %#x\n\n", (unsigned int) se );
    int rc = 0;

    _InitGlobals();
 // wprintf( L"Please attach a debugger.\n" );
 // for( int i = 30; i > 0; i-- )
 // {
 //     if( IsDebuggerPresent() )
 //         break;
 //     Sleep( 1000 );
 // }
 // DebugBreak();

    int blah = 1;
    _Takes_a_ref( blah );

    Foo< int& > f;
    f.Blah( blah );

    if( numArgs < 2 ) // args[ 0 ] should be the name of the EXE
    {
        Base::s_staticInt = -1; // will prevent it from being optimized or const-ized, I hope

        wprintf( L"What do you want to do?\n" );
        // TODO: print out choices, loop, handle "quit"
        return -1;
    }

    RoutineMap rm;

    rm[ L"nothing" ]   = [] ( vector<wstring>& args ) { wprintf( L"(nothing)\n" ); return 0; };
    rm[ L"setEvent" ]  = _SetEvent;
    rm[ L"waitEvent" ] = _WaitEvent;
    rm[ L"sleep" ]     = _Sleep;
    rm[ L"twoThreadGuTest" ] = _TwoThreadGuTest;
    rm[ L"callFoo" ]   = _CallFoo;
    rm[ L"callFFE0" ]  = _CallFFE0;
    rm[ L"lockCs" ]    = _LockCritSec;

    vector< wstring > routineArgs;
    Routine routine;
    for( int i = 1; i < numArgs; i++ ) // skip 0 because it's the name of the program
    {
        wstring routineName = args[ i ];

        while( ++i < numArgs )
        {
            if( 0 == _wcsicmp( args[ i ], L";" ) )
                break;

            routineArgs.push_back( args[ i ] );
        }

        auto iter = rm.find( routineName );
        if( iter == rm.end() )
        {
            wprintf( L"Error: did not understand instruction: %s\n", routineName.c_str() );
        }
        else
        {
            rc = iter->second( routineArgs );
        }
        routineArgs.clear();
    } // end for( each arg )

    return rc;
} // end wmain()

